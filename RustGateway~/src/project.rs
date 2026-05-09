use anyhow::{Context, Result};
use std::{
    fs,
    path::{Path, PathBuf},
};

pub struct ProjectInfo {
    pub root: PathBuf,
    pub editor_version: String,
    pub project_name: String,
}

pub fn detect_from_cwd() -> Result<Option<ProjectInfo>> {
    let mut current = std::env::current_dir().context("failed to read current directory")?;
    loop {
        if let Some(info) = detect_from_path(&current)? {
            return Ok(Some(info));
        }
        if !current.pop() {
            return Ok(None);
        }
    }
}

pub fn detect_from_path(path: &Path) -> Result<Option<ProjectInfo>> {
    let version_path = path.join("ProjectSettings").join("ProjectVersion.txt");
    if !version_path.is_file() {
        return Ok(None);
    }

    let editor_version = read_editor_version(&version_path)?;
    let project_name = read_project_name(path)?.unwrap_or_else(|| {
        path.file_name()
            .map(|name| name.to_string_lossy().to_string())
            .unwrap_or_else(|| path.display().to_string())
    });

    Ok(Some(ProjectInfo {
        root: path.to_path_buf(),
        editor_version,
        project_name,
    }))
}

fn read_editor_version(version_path: &Path) -> Result<String> {
    let text = fs::read_to_string(version_path)
        .with_context(|| format!("failed to read {}", version_path.display()))?;
    text.lines()
        .find_map(|line| line.strip_prefix("m_EditorVersion:"))
        .map(str::trim)
        .filter(|version| !version.is_empty())
        .map(ToOwned::to_owned)
        .with_context(|| format!("{} did not contain m_EditorVersion", version_path.display()))
}

fn read_project_name(root: &Path) -> Result<Option<String>> {
    let settings_path = root.join("ProjectSettings").join("ProjectSettings.asset");
    if !settings_path.is_file() {
        return Ok(None);
    }

    let text = fs::read_to_string(&settings_path)
        .with_context(|| format!("failed to read {}", settings_path.display()))?;
    Ok(text
        .lines()
        .find_map(|line| line.trim().strip_prefix("productName:"))
        .map(str::trim)
        .filter(|name| !name.is_empty())
        .map(ToOwned::to_owned))
}
