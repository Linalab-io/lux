use lux::{project, unity_hub};
use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize)]
pub struct ProjectDetails {
    pub root: PathBuf,
    pub editor_version: String,
    pub project_name: String,
    pub unity_hub_path: Option<PathBuf>,
    pub unity_install_path: Option<PathBuf>,
    pub matching_editor: Option<PathBuf>,
}

#[derive(Debug, Deserialize)]
pub struct ProjectRequest {
    pub path: Option<PathBuf>,
}

#[tauri::command]
pub async fn detect_project(request: ProjectRequest) -> Result<Option<ProjectDetails>, String> {
    let info = match request.path {
        Some(path) => project::detect_from_path(&path),
        None => project::detect_from_cwd(),
    }
    .map_err(|err| err.to_string())?;

    info.map(enrich_project).transpose()
}

#[tauri::command]
pub async fn get_project_info(path: PathBuf) -> Result<ProjectDetails, String> {
    let info = project::detect_from_path(&path)
        .map_err(|err| err.to_string())?
        .ok_or_else(|| format!("{} is not a Unity project", path.display()))?;
    enrich_project(info)
}

fn enrich_project(info: project::ProjectInfo) -> Result<ProjectDetails, String> {
    let hub = unity_hub::discover_hub().map_err(|err| err.to_string())?;
    let (unity_hub_path, unity_install_path, matching_editor) = if let Some(hub) = hub {
        let matching_editor = unity_hub::list_installed_editors(&hub)
            .map_err(|err| err.to_string())?
            .into_iter()
            .find(|editor| editor.version == info.editor_version)
            .map(|editor| editor.executable);
        (Some(hub.hub_path), Some(hub.install_path), matching_editor)
    } else {
        (None, None, None)
    };

    Ok(ProjectDetails {
        root: info.root,
        editor_version: info.editor_version,
        project_name: info.project_name,
        unity_hub_path,
        unity_install_path,
        matching_editor,
    })
}
