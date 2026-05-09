use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use tauri::State;
use tokio::sync::Mutex;

use crate::commands::{run_lux, CommandOutput};
use crate::AppState;

#[derive(Debug, Clone, Serialize, Default)]
pub struct TestResults {
    pub running: bool,
    pub last_result: Option<CommandOutput>,
}

#[derive(Debug, Default)]
pub struct TestState(pub Mutex<TestResults>);

#[derive(Debug, Deserialize)]
pub struct TestRequest {
    pub project_path: Option<PathBuf>,
    pub test_platform: Option<String>,
    pub test_results: Option<PathBuf>,
    pub log_file: Option<PathBuf>,
}

#[tauri::command]
pub async fn run_tests(
    request: TestRequest,
    state: State<'_, AppState>,
) -> Result<CommandOutput, String> {
    {
        let mut results = state.tests.0.lock().await;
        results.running = true;
    }

    let mut args = vec!["run-tests".to_string()];
    if let Some(project_path) = request.project_path {
        args.push("--project-path".to_string());
        args.push(project_path.display().to_string());
    }
    if let Some(test_platform) = request.test_platform {
        args.push("--test-platform".to_string());
        args.push(test_platform);
    }
    if let Some(test_results) = request.test_results {
        args.push("--test-results".to_string());
        args.push(test_results.display().to_string());
    }
    if let Some(log_file) = request.log_file {
        args.push("--log-file".to_string());
        args.push(log_file.display().to_string());
    }

    let result = run_lux(&args).await;
    let mut results = state.tests.0.lock().await;
    results.running = false;
    if let Ok(output) = &result {
        results.last_result = Some(output.clone());
    }
    result
}

#[tauri::command]
pub async fn get_test_results(state: State<'_, AppState>) -> Result<TestResults, String> {
    Ok(state.tests.0.lock().await.clone())
}
