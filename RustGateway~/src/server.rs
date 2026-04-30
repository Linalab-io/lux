use std::{collections::VecDeque, sync::Arc};

use axum::{
    extract::{
        ws::{Message, WebSocket, WebSocketUpgrade},
        Query, State,
    },
    http::{HeaderMap, StatusCode, Uri},
    response::{IntoResponse, Response},
    routing::get,
    Json, Router,
};
use futures_util::{SinkExt, StreamExt};
use serde::{Deserialize, Serialize};
use tokio::sync::{broadcast, Mutex};
use tower_http::trace::TraceLayer;
use uuid::Uuid;

use crate::protocol::{EventEnvelope, PROTOCOL_VERSION};

#[derive(Clone, Debug)]
pub struct GatewayConfig {
    pub token: String,
    pub history_capacity: usize,
}

#[derive(Clone)]
pub struct GatewayState {
    config: Arc<GatewayConfig>,
    events: broadcast::Sender<EventEnvelope>,
    history: Arc<Mutex<VecDeque<EventEnvelope>>>,
}

#[derive(Debug, Deserialize)]
struct SocketQuery {
    role: Option<String>,
    client_id: Option<String>,
}

#[derive(Debug, Serialize)]
struct HealthResponse {
    ok: bool,
    protocol_version: u32,
    websocket_path: &'static str,
    history_capacity: usize,
}

impl GatewayState {
    pub fn new(config: GatewayConfig) -> Self {
        let (events, _) = broadcast::channel(config.history_capacity.max(1));
        Self {
            config: Arc::new(config),
            events,
            history: Arc::new(Mutex::new(VecDeque::new())),
        }
    }

    pub fn accepts_token(&self, supplied: Option<&str>) -> bool {
        supplied
            .filter(|value| !value.is_empty())
            .is_some_and(|value| value == self.config.token)
    }

    async fn record_event(&self, event: EventEnvelope) {
        let mut history = self.history.lock().await;
        while history.len() >= self.config.history_capacity.max(1) {
            history.pop_front();
        }
        history.push_back(event);
    }

    async fn history_snapshot(&self) -> Vec<EventEnvelope> {
        self.history.lock().await.iter().cloned().collect()
    }
}

pub fn router(state: GatewayState) -> Router {
    Router::new()
        .route("/health", get(health))
        .route("/schema", get(schema))
        .route("/events", get(events_socket))
        .layer(TraceLayer::new_for_http())
        .with_state(state)
}

async fn health(State(state): State<GatewayState>) -> Json<HealthResponse> {
    Json(HealthResponse {
        ok: true,
        protocol_version: PROTOCOL_VERSION,
        websocket_path: "/events",
        history_capacity: state.config.history_capacity,
    })
}

async fn schema() -> Json<EventEnvelope> {
    Json(EventEnvelope::schema_example())
}

async fn events_socket(
    State(state): State<GatewayState>,
    Query(query): Query<SocketQuery>,
    headers: HeaderMap,
    ws: WebSocketUpgrade,
) -> Response {
    let token = headers
        .get("x-lux-token")
        .and_then(|value| value.to_str().ok());

    if !state.accepts_token(token) {
        return (
            StatusCode::UNAUTHORIZED,
            "invalid or missing Lux gateway token",
        )
            .into_response();
    }

    if !accepts_origin(&headers) {
        return (
            StatusCode::FORBIDDEN,
            "forbidden Lux gateway WebSocket origin",
        )
            .into_response();
    }

    let role = query.role.unwrap_or_else(|| "subscriber".to_string());
    let client_id = query
        .client_id
        .unwrap_or_else(|| Uuid::new_v4().to_string());
    ws.on_upgrade(move |socket| handle_socket(state, socket, role, client_id))
}

async fn handle_socket(state: GatewayState, socket: WebSocket, role: String, client_id: String) {
    let (mut sender, mut receiver) = socket.split();
    let mut events = state.events.subscribe();

    for event in state.history_snapshot().await {
        if send_event(&mut sender, &event).await.is_err() {
            return;
        }
    }

    let connected = EventEnvelope {
        schema_version: PROTOCOL_VERSION,
        event_id: Uuid::new_v4().to_string(),
        category: crate::protocol::EventCategory::Tool,
        source: "lux".to_string(),
        session_id: client_id.clone(),
        captured_at_utc: chrono_like_now(),
        payload: serde_json::json!({
            "kind": "client-connected",
            "role": role,
            "clientId": client_id,
        }),
    };
    publish_event(&state, connected).await;

    loop {
        tokio::select! {
            received = events.recv() => {
                match received {
                    Ok(event) => {
                        if send_event(&mut sender, &event).await.is_err() {
                            return;
                        }
                    }
                    Err(broadcast::error::RecvError::Lagged(skipped)) => {
                        tracing::warn!(%skipped, "Lux gateway subscriber lagged behind");
                    }
                    Err(broadcast::error::RecvError::Closed) => return,
                }
            }
            message = receiver.next() => {
                match message {
                    Some(Ok(Message::Text(text))) => {
                        if text.len() > 64 * 1024 {
                            tracing::warn!("Lux gateway ignored oversized event envelope");
                            continue;
                        }

                        match serde_json::from_str::<EventEnvelope>(&text) {
                            Ok(event) => publish_event(&state, event.normalize()).await,
                            Err(error) => tracing::warn!(%error, "Lux gateway ignored malformed event envelope"),
                        }
                    },
                    Some(Ok(Message::Close(_))) | None => return,
                    Some(Ok(_)) => {}
                    Some(Err(error)) => {
                        tracing::warn!(%error, "Lux gateway WebSocket error");
                        return;
                    }
                }
            }
        }
    }
}

fn accepts_origin(headers: &HeaderMap) -> bool {
    let Some(origin) = headers.get("origin").and_then(|value| value.to_str().ok()) else {
        return true;
    };

    if origin == "null" {
        return true;
    }

    let Ok(uri) = origin.parse::<Uri>() else {
        return false;
    };

    matches!(uri.scheme_str(), Some("http") | Some("https"))
        && matches!(
            uri.host(),
            Some("localhost") | Some("127.0.0.1") | Some("::1")
        )
}

async fn publish_event(state: &GatewayState, event: EventEnvelope) {
    state.record_event(event.clone()).await;
    let _ = state.events.send(event);
}

async fn send_event(
    sender: &mut futures_util::stream::SplitSink<WebSocket, Message>,
    event: &EventEnvelope,
) -> Result<(), axum::Error> {
    sender
        .send(Message::Text(
            serde_json::to_string(event).unwrap_or_default(),
        ))
        .await
}

fn chrono_like_now() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};

    let seconds = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_secs())
        .unwrap_or_default();
    format!("unix:{seconds}")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn token_validation_requires_exact_match() {
        let state = GatewayState::new(GatewayConfig {
            token: "secret".to_string(),
            history_capacity: 8,
        });

        assert!(state.accepts_token(Some("secret")));
        assert!(!state.accepts_token(Some("SECRET")));
        assert!(!state.accepts_token(Some("")));
        assert!(!state.accepts_token(None));
    }

    #[test]
    fn origin_validation_allows_localhost_and_rejects_remote_origins() {
        let mut headers = HeaderMap::new();
        assert!(accepts_origin(&headers));

        headers.insert("origin", "http://127.0.0.1:3000".parse().unwrap());
        assert!(accepts_origin(&headers));

        headers.insert("origin", "http://localhost:3000".parse().unwrap());
        assert!(accepts_origin(&headers));

        headers.insert("origin", "https://evil.example".parse().unwrap());
        assert!(!accepts_origin(&headers));

        headers.insert("origin", "http://localhost.evil.example".parse().unwrap());
        assert!(!accepts_origin(&headers));

        headers.insert("origin", "http://127.0.0.1.evil.example".parse().unwrap());
        assert!(!accepts_origin(&headers));
    }

    #[tokio::test]
    async fn history_respects_capacity() {
        let state = GatewayState::new(GatewayConfig {
            token: "secret".to_string(),
            history_capacity: 2,
        });

        for index in 0..3 {
            state
                .record_event(EventEnvelope {
                    schema_version: PROTOCOL_VERSION,
                    event_id: format!("event-{index}"),
                    category: crate::protocol::EventCategory::Log,
                    source: "test".to_string(),
                    session_id: "test-session".to_string(),
                    captured_at_utc: "test-time".to_string(),
                    payload: serde_json::json!({ "index": index }),
                })
                .await;
        }

        let history = state.history_snapshot().await;
        assert_eq!(history.len(), 2);
        assert_eq!(history[0].event_id, "event-1");
        assert_eq!(history[1].event_id, "event-2");
    }
}
