mod commands;

use commands::server::ServerState;
use commands::{compile::CompileState, tests::TestState};
use tauri::{
    menu::{Menu, MenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    Manager, WindowEvent,
};

pub struct AppState {
    pub compile: CompileState,
    pub tests: TestState,
    pub server: ServerState,
}

impl Default for AppState {
    fn default() -> Self {
        Self {
            compile: CompileState::default(),
            tests: TestState::default(),
            server: ServerState::default(),
        }
    }
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_dialog::init())
        .manage(AppState::default())
        .invoke_handler(tauri::generate_handler![
            commands::compile::run_compile,
            commands::compile::get_compile_status,
            commands::tests::run_tests,
            commands::tests::get_test_results,
            commands::project::detect_project,
            commands::project::get_project_info,
            commands::skills::list_skills,
            commands::skills::get_skill_info,
            commands::skills::install_skill,
            commands::config::get_config,
            commands::config::set_config,
            commands::config::get_config_path,
            commands::server::start_server,
            commands::server::stop_server,
            commands::server::get_server_status
        ])
        .setup(|app| {
            let show_hide = MenuItem::with_id(app, "show_hide", "Show/Hide", true, None::<&str>)?;
            let quit = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&show_hide, &quit])?;

            TrayIconBuilder::new()
                .menu(&menu)
                .show_menu_on_left_click(false)
                .on_menu_event(|app, event| match event.id().as_ref() {
                    "show_hide" => toggle_main_window(app),
                    "quit" => app.exit(0),
                    _ => {}
                })
                .on_tray_icon_event(|tray, event| {
                    if let TrayIconEvent::Click {
                        button: MouseButton::Left,
                        button_state: MouseButtonState::Up,
                        ..
                    } = event
                    {
                        toggle_main_window(tray.app_handle());
                    }
                })
                .build(app)?;

            Ok(())
        })
        .on_window_event(|window, event| {
            if matches!(event, WindowEvent::CloseRequested { .. }) {
                let _ = window.hide();
            }
        })
        .run(tauri::generate_context!())
        .expect("failed to run Lux desktop shell");
}

fn toggle_main_window(app: &tauri::AppHandle) {
    if let Some(window) = app.get_webview_window("main") {
        let visible = window.is_visible().unwrap_or(false);
        if visible {
            let _ = window.hide();
        } else {
            let _ = window.show();
            let _ = window.set_focus();
        }
    }
}
