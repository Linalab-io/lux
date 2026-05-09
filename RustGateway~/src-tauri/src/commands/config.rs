use lux::config::{self, LuxConfig};
use serde::Deserialize;
use std::path::PathBuf;

#[derive(Debug, Deserialize)]
pub struct SetConfigRequest {
    pub key: String,
    pub value: String,
}

#[tauri::command]
pub async fn get_config() -> Result<LuxConfig, String> {
    config::load().map_err(|err| err.to_string())
}

#[tauri::command]
pub async fn set_config(request: SetConfigRequest) -> Result<LuxConfig, String> {
    let mut lux_config = config::load().map_err(|err| err.to_string())?;
    set_value(&mut lux_config, &request.key, &request.value)?;
    config::save(&lux_config).map_err(|err| err.to_string())?;
    Ok(lux_config)
}

#[tauri::command]
pub async fn get_config_path() -> Result<PathBuf, String> {
    Ok(config::config_path())
}

fn set_value(config: &mut LuxConfig, key: &str, value: &str) -> Result<(), String> {
    match key {
        "unity.hub_path" => config.unity.hub_path = Some(PathBuf::from(value)),
        "unity.editor_path" => config.unity.editor_path = Some(PathBuf::from(value)),
        "unity.custom_install_path" => {
            config.unity.custom_install_path = Some(PathBuf::from(value));
        }
        "server.host" => config.server.host = value.to_string(),
        "server.port" => {
            config.server.port = value
                .parse()
                .map_err(|_| "server.port must be a u16".to_string())?;
        }
        "server.idle_timeout_secs" => {
            config.server.idle_timeout_secs = value
                .parse()
                .map_err(|_| "server.idle_timeout_secs must be a u64".to_string())?;
        }
        "server.token" => config.server.token = Some(value.to_string()),
        "general.project_root" => config.general.project_root = Some(PathBuf::from(value)),
        "general.log_level" => config.general.log_level = value.to_string(),
        _ => return Err(format!("unknown config key: {key}")),
    }
    Ok(())
}
