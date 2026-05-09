use serde::Deserialize;

use crate::commands::{run_lux, CommandOutput};

#[derive(Debug, Deserialize)]
pub struct SkillInstallRequest {
    pub name: String,
    pub source: String,
    pub project: Option<bool>,
    pub adapt: Option<bool>,
}

#[tauri::command]
pub async fn list_skills() -> Result<CommandOutput, String> {
    run_lux(&[
        "skill".to_string(),
        "list".to_string(),
        "--json".to_string(),
    ])
    .await
}

#[tauri::command]
pub async fn get_skill_info(name: String) -> Result<CommandOutput, String> {
    run_lux(&[
        "skill".to_string(),
        "info".to_string(),
        name,
        "--json".to_string(),
    ])
    .await
}

#[tauri::command]
pub async fn install_skill(request: SkillInstallRequest) -> Result<CommandOutput, String> {
    let mut args = vec![
        "skill".to_string(),
        "install".to_string(),
        request.name,
        "--source".to_string(),
        request.source,
        "--json".to_string(),
    ];
    if request.project.unwrap_or(false) {
        args.push("--project".to_string());
    }
    if request.adapt.unwrap_or(false) {
        args.push("--adapt".to_string());
    }
    run_lux(&args).await
}
