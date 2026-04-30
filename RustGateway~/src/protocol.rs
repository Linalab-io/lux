use serde::{Deserialize, Serialize};
use serde_json::{json, Value};

pub const PROTOCOL_VERSION: u32 = 1;

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(rename_all = "kebab-case")]
pub enum EventCategory {
    Playmode,
    Scene,
    Log,
    Tool,
    Input,
    Screenshot,
    Hierarchy,
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct EventEnvelope {
    pub schema_version: u32,
    pub event_id: String,
    pub category: EventCategory,
    pub source: String,
    pub session_id: String,
    pub captured_at_utc: String,
    pub payload: Value,
}

impl EventEnvelope {
    pub fn schema_example() -> Self {
        Self {
            schema_version: PROTOCOL_VERSION,
            event_id: "example-event".to_string(),
            category: EventCategory::Tool,
            source: "unity-editor".to_string(),
            session_id: "example-session".to_string(),
            captured_at_utc: "2026-04-30T00:00:00.0000000Z".to_string(),
            payload: json!({
                "kind": "example",
                "message": "Lux gateway event envelope prototype"
            }),
        }
    }

    pub fn normalize(mut self) -> Self {
        if self.schema_version == 0 {
            self.schema_version = PROTOCOL_VERSION;
        }

        self
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn schema_example_has_phase_one_categories() {
        let json = serde_json::to_value(EventEnvelope::schema_example()).unwrap();
        assert_eq!(json["schema_version"], PROTOCOL_VERSION);
        assert_eq!(json["category"], "tool");
    }

    #[test]
    fn all_categories_serialize_as_protocol_names() {
        let names = [
            EventCategory::Playmode,
            EventCategory::Scene,
            EventCategory::Log,
            EventCategory::Tool,
            EventCategory::Input,
            EventCategory::Screenshot,
            EventCategory::Hierarchy,
        ]
        .map(|category| serde_json::to_value(category).unwrap());

        assert_eq!(
            names,
            [
                json!("playmode"),
                json!("scene"),
                json!("log"),
                json!("tool"),
                json!("input"),
                json!("screenshot"),
                json!("hierarchy"),
            ]
        );
    }
}
