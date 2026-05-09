use serde::Serialize;
use std::path::PathBuf;
use tokio::process::Command;

pub mod compile;
pub mod config;
pub mod project;
pub mod server;
pub mod skills;
pub mod tests;

#[derive(Debug, Clone, Serialize)]
pub struct CommandOutput {
    pub success: bool,
    pub code: Option<i32>,
    pub stdout: String,
    pub stderr: String,
}

pub fn workspace_root() -> Result<PathBuf, String> {
    std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .map(PathBuf::from)
        .ok_or_else(|| "failed to resolve Lux workspace root".to_string())
}

pub async fn run_lux(args: &[String]) -> Result<CommandOutput, String> {
    let root = workspace_root()?;
    let manifest = root.join("Cargo.toml");
    let output = Command::new("cargo")
        .arg("run")
        .arg("--manifest-path")
        .arg(manifest)
        .arg("--")
        .args(args)
        .current_dir(root)
        .output()
        .await
        .map_err(|err| format!("failed to run lux CLI: {err}"))?;

    Ok(CommandOutput {
        success: output.status.success(),
        code: output.status.code(),
        stdout: String::from_utf8_lossy(&output.stdout).to_string(),
        stderr: String::from_utf8_lossy(&output.stderr).to_string(),
    })
}
