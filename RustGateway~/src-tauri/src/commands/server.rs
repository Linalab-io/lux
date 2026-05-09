use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use tauri::State;
use tokio::{process::Child, sync::Mutex};

use crate::commands::workspace_root;
use crate::AppState;

#[derive(Debug, Default)]
pub struct ServerState(pub Mutex<Option<Child>>);

#[derive(Debug, Clone, Serialize)]
pub struct ServerStatus {
    pub running: bool,
    pub pid: Option<u32>,
}

#[derive(Debug, Deserialize)]
pub struct StartServerRequest {
    pub host: Option<String>,
    pub port: Option<u16>,
    pub token: Option<String>,
    pub project_path: Option<PathBuf>,
}

#[tauri::command]
pub async fn start_server(
    request: StartServerRequest,
    state: State<'_, AppState>,
) -> Result<ServerStatus, String> {
    let mut server = state.server.0.lock().await;
    if let Some(child) = server.as_ref() {
        return Ok(ServerStatus {
            running: true,
            pid: child.id(),
        });
    }

    let root = workspace_root()?;
    let mut command = tokio::process::Command::new("cargo");
    command
        .arg("run")
        .arg("--manifest-path")
        .arg(root.join("Cargo.toml"))
        .arg("--")
        .arg("serve")
        .current_dir(&root);

    if let Some(host) = request.host {
        command.arg("--host").arg(host);
    }
    if let Some(port) = request.port {
        command.arg("--port").arg(port.to_string());
    }
    if let Some(token) = request.token {
        command.arg("--token").arg(token);
    }
    if let Some(project_path) = request.project_path {
        command.arg("--project-path").arg(project_path);
    }

    let child = command
        .spawn()
        .map_err(|err| format!("failed to start Lux server: {err}"))?;
    let pid = child.id();
    *server = Some(child);

    Ok(ServerStatus { running: true, pid })
}

#[tauri::command]
pub async fn stop_server(state: State<'_, AppState>) -> Result<ServerStatus, String> {
    let mut server = state.server.0.lock().await;
    if let Some(mut child) = server.take() {
        let _ = child.kill().await;
    }
    Ok(ServerStatus {
        running: false,
        pid: None,
    })
}

#[tauri::command]
pub async fn get_server_status(state: State<'_, AppState>) -> Result<ServerStatus, String> {
    let server = state.server.0.lock().await;
    Ok(ServerStatus {
        running: server.is_some(),
        pid: server.as_ref().and_then(Child::id),
    })
}
