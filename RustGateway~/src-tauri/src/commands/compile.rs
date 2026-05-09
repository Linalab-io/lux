use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use tauri::State;
use tokio::sync::Mutex;

use crate::commands::{run_lux, CommandOutput};
use crate::AppState;

#[derive(Debug, Clone, Serialize, Default)]
pub struct CompileStatus {
    pub running: bool,
    pub last_result: Option<CommandOutput>,
}

#[derive(Debug, Default)]
pub struct CompileState(pub Mutex<CompileStatus>);

#[derive(Debug, Deserialize)]
pub struct CompileRequest {
    pub project_path: Option<PathBuf>,
}

#[tauri::command]
pub async fn run_compile(
    request: CompileRequest,
    state: State<'_, AppState>,
) -> Result<CommandOutput, String> {
    {
        let mut status = state.compile.0.lock().await;
        status.running = true;
    }

    let mut args = vec!["compile".to_string()];
    if let Some(project_path) = request.project_path {
        args.push("--project-path".to_string());
        args.push(project_path.display().to_string());
    }

    let result = run_lux(&args).await;
    let mut status = state.compile.0.lock().await;
    status.running = false;
    if let Ok(output) = &result {
        status.last_result = Some(output.clone());
    }
    result
}

#[tauri::command]
pub async fn get_compile_status(state: State<'_, AppState>) -> Result<CompileStatus, String> {
    Ok(state.compile.0.lock().await.clone())
}
