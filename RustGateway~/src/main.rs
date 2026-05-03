mod mcp_server;
mod protocol;
mod server;

use std::{
    collections::BTreeMap,
    fs,
    io::{BufRead, BufReader, ErrorKind, Read, Write},
    net::{IpAddr, Ipv4Addr, SocketAddr},
    path::{Path, PathBuf},
    process::{Command as ProcessCommand, Stdio},
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

use anyhow::{bail, Context};
use clap::{CommandFactory, Parser, Subcommand, ValueEnum};
use clap_complete::{generate, shells::Shell};
use protocol::EventEnvelope;
use serde_json::{json, Value};

#[derive(Parser, Debug)]
#[command(name = "lux")]
#[command(version)]
#[command(about = "Lux CLI — Unity batch mode automation for Neon Glitch")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand, Debug)]
enum Command {
    Serve(ServeArgs),
    Unity(UnityArgs),
    Skill(SkillArgs),
    Addon(AddonArgs),
    Compile(CompileArgs),
    Bridge(BridgeArgs),
    Mcp(McpArgs),
    RunTests(RunTestsArgs),
    Schema,
    /// Generate shell completion scripts
    Completion {
        /// Shell type to generate completions for
        #[arg(long, value_enum)]
        shell: Option<Shell>,
    },
}

#[derive(Parser, Debug)]
struct SkillArgs {
    #[command(subcommand)]
    action: SkillAction,
}

#[derive(Subcommand, Debug)]
enum SkillAction {
    List(SkillListArgs),
    Info(SkillInfoArgs),
    Install(SkillInstallArgs),
    Remove(SkillRemoveArgs),
    Update(SkillUpdateArgs),
}

#[derive(Parser, Debug)]
struct SkillListArgs {
    #[arg(long, default_value_t = false)]
    json: bool,
}

#[derive(Parser, Debug)]
struct SkillInfoArgs {
    name: String,
    #[arg(long, default_value_t = false)]
    json: bool,
}

#[derive(Parser, Debug)]
struct SkillInstallArgs {
    /// Skill name (e.g. my-skill)
    name: String,
    /// Source URL or path to install from
    #[arg(short, long)]
    source: String,
    /// Install to project scope (.lux/skills/) instead of global
    #[arg(short, long)]
    project: bool,
}

#[derive(Parser, Debug)]
struct SkillRemoveArgs {
    /// Skill name to remove
    name: String,
    /// Remove from project scope
    #[arg(short, long)]
    project: bool,
    /// Remove from global scope
    #[arg(short, long)]
    global: bool,
}

#[derive(Parser, Debug)]
struct SkillUpdateArgs {
    /// Skill name to update
    name: String,
}

#[derive(Parser, Debug)]
struct AddonArgs {
    #[command(subcommand)]
    action: AddonAction,
}

#[derive(Subcommand, Debug)]
enum AddonAction {
    List(AddonListArgs),
    Info(AddonInfoArgs),
    Install(AddonInstallArgs),
    Remove(AddonRemoveArgs),
}

#[derive(Parser, Debug)]
struct AddonListArgs {
    #[arg(long, default_value_t = false)]
    json: bool,
    #[arg(long, default_value_t = false)]
    available: bool,
}

#[derive(Parser, Debug)]
struct AddonInfoArgs {
    name: String,
    #[arg(long, default_value_t = false)]
    json: bool,
}

#[derive(Parser, Debug)]
struct AddonInstallArgs {
    /// Addon name to install
    name: String,
}

#[derive(Parser, Debug)]
struct AddonRemoveArgs {
    /// Addon name to remove
    name: String,
    /// Remove even when installed addons depend on it
    #[arg(long, default_value_t = false)]
    force: bool,
}

#[derive(Parser, Debug)]
struct UnityArgs {
    #[command(subcommand)]
    command: UnityCommand,
}

#[derive(Subcommand, Debug)]
enum UnityCommand {
    Status(UnityStatusArgs),
    Context(UnityContextArgs),
    BackendStatus(UnityBackendStatusArgs),
    BackendListCommands(UnityBackendListCommandsArgs),
    GetLogs(UnityGetLogsArgs),
    ClearConsole(UnityClearConsoleArgs),
    FocusWindow(UnityFocusWindowArgs),
    Launch(UnityLaunchArgs),
    SceneSmoke(UnitySceneSmokeArgs),
    CreateObjects(UnityCreateObjectsArgs),
    FindGameObjects(UnityFindGameObjectsArgs),
    GetHierarchy(UnityGetHierarchyArgs),
    ControlPlayMode(UnityControlPlayModeArgs),
    Screenshot(UnityScreenshotArgs),
    SimulateMouseUi(UnitySimulateMouseUiArgs),
    SimulateKeyboard(UnitySimulateKeyboardArgs),
    SimulateMouseInput(UnitySimulateMouseInputArgs),
    RecordInput(UnityRecordInputArgs),
    ReplayInput(UnityReplayInputArgs),
    ExecuteDynamicCode(UnityExecuteDynamicCodeArgs),
}

#[derive(Parser, Debug)]
struct UnityStatusArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
}

#[derive(Parser, Debug)]
struct UnityContextArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, default_value_t = false)]
    refresh: bool,
}

#[derive(Parser, Debug)]
struct UnityBackendStatusArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
}

#[derive(Parser, Debug)]
struct UnityBackendListCommandsArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
}

#[derive(Parser, Debug)]
struct UnityGetLogsArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long)]
    log_type: Option<String>,
    #[arg(long, default_value_t = 100)]
    max_count: i64,
    #[arg(long)]
    search_text: Option<String>,
    #[arg(long, default_value_t = false)]
    include_stack_trace: bool,
    #[arg(long, default_value_t = false)]
    use_regex: bool,
    #[arg(long, default_value_t = false)]
    search_in_stack_trace: bool,
}

#[derive(Parser, Debug)]
struct UnityClearConsoleArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, default_value_t = false)]
    add_confirmation_message: bool,
}

#[derive(Parser, Debug)]
struct UnityFocusWindowArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
}

#[derive(Parser, Debug)]
struct UnityLaunchArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, default_value_t = false)]
    no_wait: bool,
    #[arg(short = 'r', long, default_value_t = false)]
    restart: bool,
    #[arg(long)]
    platform: Option<String>,
    #[arg(long, default_value_t = 3)]
    max_depth: i64,
    #[arg(short = 'a', long, default_value_t = false)]
    add_unity_hub: bool,
    #[arg(short = 'f', long, default_value_t = false)]
    favorite: bool,
}

#[derive(Parser, Debug)]
struct UnitySceneSmokeArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, default_value = "Assets/_Main/Scenes/GamePlay.unity")]
    scene_path: String,
    #[arg(long, default_value_t = 10)]
    object_count: u32,
    #[arg(long, default_value_t = false)]
    batch: bool,
}

#[derive(Parser, Debug)]
struct UnityCreateObjectsArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, default_value = "Assets/_Main/Scenes/GamePlay.unity")]
    scene_path: String,
    #[arg(long, default_value_t = 10)]
    object_count: u32,
}

#[derive(Parser, Debug)]
struct UnityFindGameObjectsArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, default_value = "query")]
    search_mode: String,
    #[arg(long)]
    search_text: Option<String>,
    #[arg(long)]
    name: Option<String>,
    #[arg(long)]
    regex: Option<String>,
    #[arg(long)]
    path: Option<String>,
    #[arg(long)]
    component: Option<String>,
    #[arg(long)]
    tag: Option<String>,
    #[arg(long)]
    layer: Option<String>,
    #[arg(long, default_value = "any")]
    active_state: String,
    #[arg(long, default_value_t = 50)]
    inline_limit: i64,
    #[arg(long, default_value_t = false)]
    include_inherited_properties: bool,
}

#[derive(Parser, Debug)]
struct UnityGetHierarchyArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, default_value_t = false)]
    all: bool,
    #[arg(long)]
    root_path: Option<String>,
    #[arg(long, default_value_t = false)]
    use_selection: bool,
    #[arg(long)]
    max_depth: Option<i64>,
    #[arg(long, default_value_t = false)]
    include_components: bool,
    #[arg(long, default_value_t = false)]
    include_inactive: bool,
    #[arg(long, default_value_t = false)]
    include_paths: bool,
    #[arg(long)]
    use_components_lut: Option<String>,
}

#[derive(Parser, Debug)]
struct UnityControlPlayModeArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, value_enum)]
    action: PlayModeAction,
    #[arg(long, default_value_t = false)]
    wait: bool,
}

#[derive(Parser, Debug)]
struct UnityScreenshotArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, default_value = "rendering")]
    capture_mode: String,
    #[arg(long, default_value_t = false)]
    annotate_elements: bool,
    #[arg(long, default_value_t = false)]
    elements_only: bool,
    #[arg(long)]
    window_name: Option<String>,
    #[arg(long)]
    resolution_scale: Option<f64>,
    #[arg(long)]
    match_mode: Option<String>,
    #[arg(long)]
    output_directory: Option<PathBuf>,
}

#[derive(Parser, Debug)]
struct UnitySimulateKeyboardArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, value_enum)]
    action: KeyboardInputAction,
    #[arg(long)]
    key: String,
    #[arg(long, default_value_t = 50)]
    duration_ms: i64,
}

#[derive(Parser, Debug)]
struct UnitySimulateMouseUiArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, value_enum)]
    action: MouseUiAction,
    #[arg(long)]
    x: f64,
    #[arg(long)]
    y: f64,
    #[arg(long, default_value_t = 500)]
    duration_ms: i64,
    #[arg(long)]
    from_x: Option<f64>,
    #[arg(long)]
    from_y: Option<f64>,
    #[arg(long)]
    drag_speed: Option<f64>,
    #[arg(long)]
    button: Option<String>,
    #[arg(long, default_value_t = false)]
    bypass_raycast: bool,
    #[arg(long)]
    target_path: Option<String>,
    #[arg(long)]
    drop_target_path: Option<String>,
}

#[derive(Parser, Debug)]
struct UnitySimulateMouseInputArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, value_enum)]
    action: MouseInputAction,
    #[arg(long, default_value = "left")]
    button: String,
    #[arg(long, default_value_t = 0.0)]
    delta_x: f64,
    #[arg(long, default_value_t = 0.0)]
    delta_y: f64,
    #[arg(long, default_value_t = 0.0)]
    scroll_x: f64,
    #[arg(long, default_value_t = 0.0)]
    scroll_y: f64,
    #[arg(long, default_value_t = 50)]
    duration_ms: i64,
    #[arg(long, default_value_t = 5)]
    steps: i64,
}

#[derive(Parser, Debug)]
struct UnityRecordInputArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, value_enum)]
    action: RecordInputAction,
    #[arg(long)]
    output_path: Option<PathBuf>,
    #[arg(long)]
    keys: Option<String>,
    #[arg(long)]
    delay_seconds: Option<i64>,
    #[arg(long, default_value_t = false)]
    show_overlay: bool,
}

#[derive(Parser, Debug)]
struct UnityReplayInputArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, value_enum)]
    action: ReplayInputAction,
    #[arg(long)]
    file: Option<PathBuf>,
    #[arg(long, default_value_t = false)]
    show_overlay: bool,
    #[arg(long, default_value_t = false)]
    r#loop: bool,
}

#[derive(Parser, Debug)]
struct UnityExecuteDynamicCodeArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long)]
    code: Option<String>,
    #[arg(long)]
    file: Option<PathBuf>,
    #[arg(long)]
    parameters: Option<String>,
    #[arg(long, default_value_t = false)]
    compile_only: bool,
    #[arg(long, default_value_t = false)]
    yield_to_foreground_requests: bool,
}

#[derive(Clone, Copy, Debug, ValueEnum)]
enum PlayModeAction {
    Play,
    Stop,
    Pause,
    Resume,
    Status,
}

#[derive(Clone, Copy, Debug, ValueEnum)]
enum KeyboardInputAction {
    Press,
    KeyDown,
    KeyUp,
}

#[derive(Clone, Copy, Debug, ValueEnum)]
enum MouseUiAction {
    Click,
    LongPress,
    DragStart,
    DragMove,
    DragEnd,
}

impl MouseUiAction {
    fn as_str(self) -> &'static str {
        match self {
            MouseUiAction::Click => "click",
            MouseUiAction::LongPress => "long-press",
            MouseUiAction::DragStart => "drag-start",
            MouseUiAction::DragMove => "drag-move",
            MouseUiAction::DragEnd => "drag-end",
        }
    }
}

impl KeyboardInputAction {
    fn as_str(self) -> &'static str {
        match self {
            KeyboardInputAction::Press => "press",
            KeyboardInputAction::KeyDown => "key-down",
            KeyboardInputAction::KeyUp => "key-up",
        }
    }
}

#[derive(Clone, Copy, Debug, ValueEnum)]
enum MouseInputAction {
    Click,
    LongPress,
    MoveDelta,
    SmoothDelta,
    Scroll,
}

impl MouseInputAction {
    fn as_str(self) -> &'static str {
        match self {
            MouseInputAction::Click => "click",
            MouseInputAction::LongPress => "long-press",
            MouseInputAction::MoveDelta => "move-delta",
            MouseInputAction::SmoothDelta => "smooth-delta",
            MouseInputAction::Scroll => "scroll",
        }
    }
}

#[derive(Clone, Copy, Debug, ValueEnum)]
enum RecordInputAction {
    Start,
    Stop,
}

impl RecordInputAction {
    fn as_str(self) -> &'static str {
        match self {
            RecordInputAction::Start => "start",
            RecordInputAction::Stop => "stop",
        }
    }
}

#[derive(Clone, Copy, Debug, ValueEnum)]
enum ReplayInputAction {
    Start,
    Stop,
    Status,
}

impl ReplayInputAction {
    fn as_str(self) -> &'static str {
        match self {
            ReplayInputAction::Start => "start",
            ReplayInputAction::Stop => "stop",
            ReplayInputAction::Status => "status",
        }
    }
}

impl PlayModeAction {
    fn as_str(self) -> &'static str {
        match self {
            PlayModeAction::Play => "play",
            PlayModeAction::Stop => "stop",
            PlayModeAction::Pause => "pause",
            PlayModeAction::Resume => "resume",
            PlayModeAction::Status => "status",
        }
    }
}

#[derive(Parser, Debug)]
struct ServeArgs {
    #[arg(long, env = "LUX_GATEWAY_HOST", default_value_t = IpAddr::V4(Ipv4Addr::LOCALHOST))]
    host: IpAddr,
    #[arg(long, env = "LUX_GATEWAY_PORT", default_value_t = 17340)]
    port: u16,
    #[arg(long, env = "LUX_GATEWAY_TOKEN")]
    token: String,
    #[arg(long, env = "LUX_GATEWAY_HISTORY", default_value_t = 256)]
    history_capacity: usize,
    /// Minutes without HTTP or WebSocket activity before graceful shutdown (0 disables)
    #[arg(long, env = "LUX_GATEWAY_IDLE_TIMEOUT", default_value_t = 30)]
    idle_timeout: u64,
}

#[derive(Parser, Debug)]
struct CompileArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
}

#[derive(Parser, Debug)]
struct BridgeArgs {
    #[command(subcommand)]
    action: BridgeAction,
}

#[derive(Subcommand, Debug)]
enum BridgeAction {
    Watch(BridgeWatchArgs),
}

#[derive(Parser, Debug)]
struct BridgeWatchArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
}

#[derive(Parser, Debug)]
pub(crate) struct McpArgs {
    #[arg(long)]
    pub(crate) project_path: Option<PathBuf>,
}

#[derive(Parser, Debug)]
struct RunTestsArgs {
    #[arg(long)]
    project_path: Option<PathBuf>,
    #[arg(long, default_value = "EditMode")]
    test_platform: String,
    #[arg(long)]
    test_results: Option<PathBuf>,
    #[arg(long)]
    log_file: Option<PathBuf>,
}

#[derive(Debug, serde::Deserialize)]
struct LuxBridgeSettings {
    schema_version: u32,
    protocol: String,
    package_name: String,
    package_version: String,
    project_root: String,
    rust_gateway_path: String,
    #[serde(default)]
    unity_server_port: Option<u16>,
    generated_at_utc: String,
}

#[derive(Debug, serde::Deserialize)]
pub(crate) struct UnityBridgeDiscovery {
    pub(crate) host: String,
    pub(crate) port: u16,
    pub(crate) token: String,
}

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(tracing_subscriber::EnvFilter::from_default_env())
        .init();

    match Cli::parse().command {
        Command::Serve(args) => serve(args).await,
        Command::Unity(args) => run_lux_unity_command(args),
        Command::Skill(args) => run_skill_command(args),
        Command::Addon(args) => run_addon_command(args),
        Command::Compile(args) => run_batch_compile(args),
        Command::Bridge(args) => run_bridge_command(args),
        Command::Mcp(args) => mcp_server::run_mcp_server(args),
        Command::RunTests(args) => run_batch_tests(args),
        Command::Schema => {
            println!(
                "{}",
                serde_json::to_string_pretty(&EventEnvelope::schema_example())?
            );
            Ok(())
        }
        Command::Completion { shell } => {
            let shell = shell.unwrap_or_else(|| {
                if std::env::var_os("SHELL")
                    .map(|s| s.to_string_lossy().contains("zsh"))
                    .unwrap_or(false)
                {
                    Shell::Zsh
                } else if std::env::var_os("PSModulePath").is_some() {
                    Shell::PowerShell
                } else {
                    Shell::Bash
                }
            });
            let mut cmd = Cli::command();
            let name = cmd.get_name().to_string();
            generate(shell, &mut cmd, name, &mut std::io::stdout());
            Ok(())
        }
    }
}

// ---------------------------------------------------------------------------
// lux skill
// ---------------------------------------------------------------------------

#[derive(Debug, serde::Deserialize, serde::Serialize)]
struct SkillManifest {
    name: String,
    version: String,
    description: String,
    #[serde(rename = "displayName")]
    display_name: Option<String>,
    #[serde(rename = "luxVersion")]
    lux_version: Option<String>,
    author: Option<SkillAuthor>,
    keywords: Option<Vec<String>>,
    #[serde(rename = "type")]
    skill_type: String,
    source: Option<String>,
    dependencies: Option<Value>,
}

#[derive(Debug, serde::Deserialize, serde::Serialize)]
struct SkillAuthor {
    name: String,
    email: Option<String>,
    url: Option<String>,
}

#[derive(Debug, serde::Serialize)]
struct SkillEntry {
    manifest: SkillManifest,
    directory_path: PathBuf,
    scope: String,
}

#[derive(Debug, serde::Serialize)]
struct SkillInfo<'a> {
    manifest: &'a SkillManifest,
    directory_path: &'a Path,
    references: Vec<String>,
    skill_md_preview: Vec<String>,
}

fn run_skill_command(args: SkillArgs) -> anyhow::Result<()> {
    match args.action {
        SkillAction::List(list_args) => print_skill_list(list_args),
        SkillAction::Info(info_args) => print_skill_info(info_args),
        SkillAction::Install(install_args) => install_skill(install_args),
        SkillAction::Remove(remove_args) => remove_skill(remove_args),
        SkillAction::Update(update_args) => update_skill(update_args),
    }
}

fn print_skill_list(args: SkillListArgs) -> anyhow::Result<()> {
    let entries = discover_skills()?;

    if args.json {
        println!("{}", serde_json::to_string_pretty(&entries)?);
        return Ok(());
    }

    if entries.is_empty() {
        println!("No skills found");
        return Ok(());
    }

    println!("{:20} {:10} {:8} DESCRIPTION", "NAME", "VERSION", "TYPE");
    for entry in entries {
        println!(
            "{:20} {:10} {:8} {}",
            entry.manifest.name, entry.manifest.version, entry.scope, entry.manifest.description
        );
    }

    Ok(())
}

fn print_skill_info(args: SkillInfoArgs) -> anyhow::Result<()> {
    let entries = discover_skills()?;
    let Some(entry) = entries
        .iter()
        .find(|entry| entry.manifest.name == args.name)
    else {
        eprintln!("Error: skill '{}' not found", args.name);
        std::process::exit(1);
    };

    let references = read_skill_references(&entry.directory_path);
    let preview = read_skill_md_preview(&entry.directory_path);

    if args.json {
        let info = SkillInfo {
            manifest: &entry.manifest,
            directory_path: &entry.directory_path,
            references,
            skill_md_preview: preview,
        };
        println!("{}", serde_json::to_string_pretty(&info)?);
        return Ok(());
    }

    println!("Name:         {}", entry.manifest.name);
    println!(
        "Display Name: {}",
        entry.manifest.display_name.as_deref().unwrap_or("N/A")
    );
    println!("Version:      {}", entry.manifest.version);
    println!("Description:  {}", entry.manifest.description);
    println!("Type:         {}", entry.manifest.skill_type);
    println!(
        "Author:       {}",
        entry
            .manifest
            .author
            .as_ref()
            .map(|author| author.name.as_str())
            .unwrap_or("N/A")
    );
    println!(
        "Keywords:     {}",
        entry
            .manifest
            .keywords
            .as_ref()
            .filter(|keywords| !keywords.is_empty())
            .map(|keywords| keywords.join(", "))
            .unwrap_or_else(|| "N/A".to_string())
    );
    println!(
        "Lux Version:  {}",
        entry.manifest.lux_version.as_deref().unwrap_or("N/A")
    );
    println!("Location:     {}", entry.directory_path.display());
    println!();
    println!("References:");
    if references.is_empty() {
        println!("  N/A");
    } else {
        for reference in references {
            println!("  - {}", reference);
        }
    }
    println!();
    println!("SKILL.md preview:");
    if preview.is_empty() {
        println!("  N/A");
    } else {
        for line in preview {
            println!("  {}", line);
        }
    }

    Ok(())
}

fn install_skill(args: SkillInstallArgs) -> anyhow::Result<()> {
    let target_root = if args.project {
        project_skills_dir().context("failed to determine project skills directory")?
    } else {
        global_skills_dir().context("failed to determine global skills directory")?
    };
    let target_dir = target_root.join(&args.name);

    if target_dir.exists() {
        eprintln!(
            "Error: skill '{}' already exists at {}",
            args.name,
            target_dir.display()
        );
        std::process::exit(1);
    }

    fs::create_dir_all(&target_dir)
        .with_context(|| format!("failed to create skill directory {}", target_dir.display()))?;

    let result = install_skill_from_source(&args.source, &target_dir);
    if let Err(error) = result {
        let _ = fs::remove_dir_all(&target_dir);
        return Err(error);
    }

    println!(
        "Installed skill '{}' to {}",
        args.name,
        target_dir.display()
    );
    Ok(())
}

fn remove_skill(args: SkillRemoveArgs) -> anyhow::Result<()> {
    if args.project && args.global {
        eprintln!("Error: choose either --project or --global, not both");
        std::process::exit(1);
    }

    if discover_skills()?
        .iter()
        .any(|entry| entry.scope == "core" && entry.manifest.name == args.name)
    {
        eprintln!("Error: refusing to remove core skill '{}'", args.name);
        std::process::exit(1);
    }

    let target_dir = if args.project {
        project_skills_dir()
            .context("failed to determine project skills directory")?
            .join(&args.name)
    } else if args.global {
        global_skills_dir()
            .context("failed to determine global skills directory")?
            .join(&args.name)
    } else {
        let project_dir = project_skills_dir()
            .context("failed to determine project skills directory")?
            .join(&args.name);
        if project_dir.exists() {
            project_dir
        } else {
            global_skills_dir()
                .context("failed to determine global skills directory")?
                .join(&args.name)
        }
    };

    if !target_dir.exists() {
        eprintln!("Error: skill '{}' not found", args.name);
        std::process::exit(1);
    }

    fs::remove_dir_all(&target_dir)
        .with_context(|| format!("failed to remove skill directory {}", target_dir.display()))?;
    println!(
        "Removed skill '{}' from {}",
        args.name,
        target_dir.display()
    );
    Ok(())
}

fn update_skill(args: SkillUpdateArgs) -> anyhow::Result<()> {
    let entries = discover_skills()?;
    let Some(entry) = find_skill_for_update(&entries, &args.name) else {
        eprintln!("Error: skill '{}' not found", args.name);
        std::process::exit(1);
    };

    let Some(source) = entry.manifest.source.as_deref() else {
        eprintln!("Error: Skill has no source URL configured");
        std::process::exit(1);
    };

    install_skill_from_source(source, &entry.directory_path)?;
    println!(
        "Updated skill '{}' at {}",
        args.name,
        entry.directory_path.display()
    );
    Ok(())
}

fn find_skill_for_update<'a>(entries: &'a [SkillEntry], name: &str) -> Option<&'a SkillEntry> {
    entries
        .iter()
        .find(|entry| entry.manifest.name == name && entry.scope == "project")
        .or_else(|| {
            entries
                .iter()
                .find(|entry| entry.manifest.name == name && entry.scope == "global")
        })
        .or_else(|| entries.iter().find(|entry| entry.manifest.name == name))
}

fn install_skill_from_source(source: &str, target_dir: &Path) -> anyhow::Result<()> {
    if is_url_source(source) {
        eprintln!("Note: URL-based skill install/update is a placeholder");
        download_skill_file(source, "manifest.json", target_dir, true)?;
        download_skill_file(source, "SKILL.md", target_dir, false)?;
        return Ok(());
    }

    let source_dir = Path::new(source);
    if !source_dir.is_dir() {
        bail!("source is not a directory: {}", source_dir.display());
    }

    copy_required_skill_file(source_dir, target_dir, "manifest.json")?;
    copy_required_skill_file(source_dir, target_dir, "SKILL.md")?;

    let references_dir = source_dir.join("references");
    if references_dir.is_dir() {
        let target_references_dir = target_dir.join("references");
        if target_references_dir.exists() {
            fs::remove_dir_all(&target_references_dir).with_context(|| {
                format!(
                    "failed to replace references directory {}",
                    target_references_dir.display()
                )
            })?;
        }
        copy_dir_recursive(&references_dir, &target_references_dir)?;
    }

    Ok(())
}

fn copy_required_skill_file(
    source_dir: &Path,
    target_dir: &Path,
    file_name: &str,
) -> anyhow::Result<()> {
    let source_path = source_dir.join(file_name);
    let target_path = target_dir.join(file_name);
    fs::copy(&source_path, &target_path).with_context(|| {
        format!(
            "failed to copy {} to {}",
            source_path.display(),
            target_path.display()
        )
    })?;
    Ok(())
}

fn copy_dir_recursive(source_dir: &Path, target_dir: &Path) -> anyhow::Result<()> {
    fs::create_dir_all(target_dir)
        .with_context(|| format!("failed to create directory {}", target_dir.display()))?;

    for entry in fs::read_dir(source_dir)
        .with_context(|| format!("failed to read directory {}", source_dir.display()))?
    {
        let entry = entry?;
        let source_path = entry.path();
        let target_path = target_dir.join(entry.file_name());
        if source_path.is_dir() {
            copy_dir_recursive(&source_path, &target_path)?;
        } else {
            fs::copy(&source_path, &target_path).with_context(|| {
                format!(
                    "failed to copy {} to {}",
                    source_path.display(),
                    target_path.display()
                )
            })?;
        }
    }

    Ok(())
}

fn is_url_source(source: &str) -> bool {
    source.starts_with("http://") || source.starts_with("https://")
}

fn download_skill_file(
    source: &str,
    file_name: &str,
    target_dir: &Path,
    required: bool,
) -> anyhow::Result<()> {
    let url = format!("{}/{}", source.trim_end_matches('/'), file_name);
    let target_path = target_dir.join(file_name);
    let output = ProcessCommand::new("curl")
        .args([
            "--fail",
            "--silent",
            "--show-error",
            "--location",
            "--output",
        ])
        .arg(&target_path)
        .arg(&url)
        .output()
        .with_context(|| format!("failed to start curl for {url}"))?;

    if output.status.success() {
        return Ok(());
    }

    let _ = fs::remove_file(&target_path);
    if required {
        bail!(
            "failed to download {url}: {}",
            String::from_utf8_lossy(&output.stderr).trim()
        );
    }

    eprintln!("Warning: failed to download optional {file_name} from {url}");
    Ok(())
}

fn discover_skills() -> anyhow::Result<Vec<SkillEntry>> {
    let mut entries = Vec::new();

    scan_skill_scope(&core_skills_dir(), "core", &mut entries)?;
    if let Some(skills_dir) = project_skills_dir() {
        scan_skill_scope(&skills_dir, "project", &mut entries)?;
    }
    if let Some(skills_dir) = global_skills_dir() {
        scan_skill_scope(&skills_dir, "global", &mut entries)?;
    }

    entries.sort_by(|left, right| {
        left.manifest
            .name
            .cmp(&right.manifest.name)
            .then_with(|| left.scope.cmp(&right.scope))
    });
    Ok(entries)
}

fn scan_skill_scope(
    skills_dir: &Path,
    scope: &str,
    entries: &mut Vec<SkillEntry>,
) -> anyhow::Result<()> {
    let read_dir = match fs::read_dir(&skills_dir) {
        Ok(read_dir) => read_dir,
        Err(error) if error.kind() == ErrorKind::NotFound => return Ok(()),
        Err(error) => {
            return Err(error).with_context(|| {
                format!("failed to read skills directory {}", skills_dir.display())
            })
        }
    };

    for dir_entry in read_dir {
        let dir_entry = match dir_entry {
            Ok(dir_entry) => dir_entry,
            Err(error) => {
                eprintln!("Warning: failed to read skill directory entry: {error}");
                continue;
            }
        };
        let directory_path = dir_entry.path();
        if !directory_path.is_dir() {
            continue;
        }

        let manifest_path = directory_path.join("manifest.json");
        let manifest_json = match fs::read_to_string(&manifest_path) {
            Ok(manifest_json) => manifest_json,
            Err(error) if error.kind() == ErrorKind::NotFound => {
                eprintln!(
                    "Warning: missing manifest.json for skill directory {}",
                    directory_path.display()
                );
                continue;
            }
            Err(error) => {
                eprintln!(
                    "Warning: failed to read {}: {error}",
                    manifest_path.display()
                );
                continue;
            }
        };

        let manifest = match serde_json::from_str::<SkillManifest>(&manifest_json) {
            Ok(manifest) => manifest,
            Err(error) => {
                eprintln!(
                    "Warning: failed to parse {}: {error}",
                    manifest_path.display()
                );
                continue;
            }
        };

        entries.push(SkillEntry {
            manifest,
            directory_path,
            scope: scope.to_string(),
        });
    }

    Ok(())
}

fn core_skills_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../Skills")
}

fn project_skills_dir() -> Option<PathBuf> {
    std::env::current_dir()
        .ok()
        .map(|d| d.join(".lux").join("skills"))
}

fn global_skills_dir() -> Option<PathBuf> {
    let home = if cfg!(windows) {
        std::env::var("USERPROFILE").ok()
    } else {
        std::env::var("HOME").ok()
    };
    home.map(|h| PathBuf::from(h).join(".lux").join("skills"))
}

fn read_skill_references(directory_path: &Path) -> Vec<String> {
    let references_dir = directory_path.join("references");
    let read_dir = match fs::read_dir(&references_dir) {
        Ok(read_dir) => read_dir,
        Err(_) => return Vec::new(),
    };

    let mut references = Vec::new();
    for dir_entry in read_dir.flatten() {
        let path = dir_entry.path();
        if path.extension().and_then(|extension| extension.to_str()) != Some("md") {
            continue;
        }
        if let Some(file_name) = path.file_name().and_then(|file_name| file_name.to_str()) {
            references.push(file_name.to_string());
        }
    }
    references.sort();
    references
}

fn read_skill_md_preview(directory_path: &Path) -> Vec<String> {
    let skill_md_path = directory_path.join("SKILL.md");
    let content = match fs::read_to_string(&skill_md_path) {
        Ok(content) => content,
        Err(_) => return Vec::new(),
    };

    content.lines().take(10).map(str::to_string).collect()
}

// ---------------------------------------------------------------------------
// lux addon
// ---------------------------------------------------------------------------

#[allow(dead_code)]
mod lux_addon_legacy {
    use super::*;

    const DEFAULT_INSTALLED_ADDONS: [&str; 5] = [
        "webrtc",
        "codex-image",
        "pipeline-editor",
        "unity-git",
        "multi-ai",
    ];

    #[derive(Debug, serde::Deserialize, serde::Serialize)]
    struct AddonManifest {
        name: String,
        #[serde(rename = "displayName")]
        display_name: Option<String>,
        version: String,
        description: String,
        category: Option<String>,
        #[serde(rename = "defineSymbols", default)]
        define_symbols: Vec<String>,
        #[serde(rename = "requiredPackages", default)]
        required_packages: BTreeMap<String, String>,
        #[serde(rename = "addonDependencies", default)]
        addon_dependencies: BTreeMap<String, String>,
        #[serde(default)]
        assemblies: Vec<String>,
        keywords: Option<Vec<String>>,
    }

    #[derive(Debug, serde::Serialize)]
    struct AddonEntry {
        manifest: AddonManifest,
        directory_path: PathBuf,
        scope: String,
        status: String,
    }

    #[derive(Debug, serde::Deserialize, serde::Serialize)]
    struct AddonState {
        version: u32,
        #[serde(rename = "installedAddons")]
        installed_addons: Vec<String>,
        #[serde(rename = "lastUpdated")]
        last_updated: String,
    }

    fn run_addon_command(args: AddonArgs) -> anyhow::Result<()> {
        match args.action {
            AddonAction::List(list_args) => print_addon_list(list_args),
            AddonAction::Info(info_args) => print_addon_info(info_args),
            AddonAction::Install(install_args) => install_addon(install_args),
            AddonAction::Remove(remove_args) => remove_addon(remove_args),
        }
    }

    fn print_addon_list(args: AddonListArgs) -> anyhow::Result<()> {
        let mut entries = discover_addons()?;
        if args.available {
            entries.retain(|entry| entry.status == "available");
        }

        if args.json {
            println!("{}", serde_json::to_string_pretty(&entries)?);
            return Ok(());
        }

        if entries.is_empty() {
            println!("No addons found");
            return Ok(());
        }

        println!("{:20} {:10} {:10} DESCRIPTION", "NAME", "VERSION", "STATUS");
        for entry in entries {
            println!(
                "{:20} {:10} {:10} {}",
                entry.manifest.name,
                entry.manifest.version,
                entry.status,
                entry.manifest.description
            );
        }

        Ok(())
    }

    fn print_addon_info(args: AddonInfoArgs) -> anyhow::Result<()> {
        let entries = discover_addons()?;
        let Some(entry) = entries
            .iter()
            .find(|entry| entry.manifest.name == args.name)
        else {
            eprintln!("Error: addon '{}' not found", args.name);
            std::process::exit(1);
        };

        if args.json {
            println!("{}", serde_json::to_string_pretty(&entry.manifest)?);
            return Ok(());
        }

        println!("Name:               {}", entry.manifest.name);
        println!(
            "Display Name:       {}",
            entry.manifest.display_name.as_deref().unwrap_or("N/A")
        );
        println!("Version:            {}", entry.manifest.version);
        println!("Description:        {}", entry.manifest.description);
        println!(
            "Category:           {}",
            entry.manifest.category.as_deref().unwrap_or("N/A")
        );
        println!(
            "Define Symbols:     {}",
            format_string_list(&entry.manifest.define_symbols)
        );
        println!(
            "Required Packages:  {}",
            format_string_map(&entry.manifest.required_packages)
        );
        println!(
            "Addon Dependencies: {}",
            format_string_map(&entry.manifest.addon_dependencies)
        );
        println!(
            "Assemblies:         {}",
            format_string_list(&entry.manifest.assemblies)
        );
        println!("Status:             {}", entry.status);
        println!("Location:           {}", entry.directory_path.display());

        Ok(())
    }

    fn install_addon(args: AddonInstallArgs) -> anyhow::Result<()> {
        let entries = discover_addons()?;
        let Some(entry) = entries
            .iter()
            .find(|entry| entry.manifest.name == args.name)
        else {
            eprintln!("Error: addon '{}' not found", args.name);
            std::process::exit(1);
        };

        let project_root = addon_project_root()?;
        let mut state = read_addon_state(&project_root)?;

        if state
            .installed_addons
            .iter()
            .any(|installed| installed == &args.name)
        {
            println!("Addon '{}' is already installed", args.name);
            return Ok(());
        }

        let missing_dependencies: Vec<String> = entry
            .manifest
            .addon_dependencies
            .keys()
            .filter(|dependency| {
                !state
                    .installed_addons
                    .iter()
                    .any(|name| name == *dependency)
            })
            .cloned()
            .collect();
        if !missing_dependencies.is_empty() {
            eprintln!(
                "Error: addon '{}' requires installed addons: {}",
                args.name,
                missing_dependencies.join(", ")
            );
            std::process::exit(1);
        }

        warn_missing_unity_packages(&project_root, &entry.manifest.required_packages);

        state.installed_addons.push(args.name.clone());
        state.installed_addons.sort();
        state.installed_addons.dedup();
        state.last_updated = current_utc_timestamp_string();
        write_addon_state(&project_root, &state)?;

        println!("Installed addon '{}'", args.name);
        println!(
        "Next steps: return to Unity and allow scripts to recompile so addon define symbols take effect."
    );
        Ok(())
    }

    fn remove_addon(args: AddonRemoveArgs) -> anyhow::Result<()> {
        let project_root = addon_project_root()?;
        let mut state = read_addon_state(&project_root)?;

        if !state
            .installed_addons
            .iter()
            .any(|installed| installed == &args.name)
        {
            eprintln!("Error: addon '{}' is not installed", args.name);
            std::process::exit(1);
        }

        let entries = discover_addons()?;
        let dependents: Vec<String> = entries
            .iter()
            .filter(|entry| {
                entry.manifest.addon_dependencies.contains_key(&args.name)
                    && state
                        .installed_addons
                        .iter()
                        .any(|installed| installed == &entry.manifest.name)
            })
            .map(|entry| entry.manifest.name.clone())
            .collect();

        if !args.force && !dependents.is_empty() {
            eprintln!(
                "Error: addon '{}' is required by installed addons: {}",
                args.name,
                dependents.join(", ")
            );
            eprintln!("Use --force to remove it anyway.");
            std::process::exit(1);
        }

        state.installed_addons.retain(|name| name != &args.name);
        state.last_updated = current_utc_timestamp_string();
        write_addon_state(&project_root, &state)?;

        println!("Removed addon '{}'", args.name);
        println!(
        "Next steps: return to Unity and allow scripts to recompile so addon define symbols are refreshed."
    );
        Ok(())
    }

    fn discover_addons() -> anyhow::Result<Vec<AddonEntry>> {
        let project_root = addon_project_root().ok();
        let state = project_root
            .as_ref()
            .and_then(|root| read_addon_state(root).ok())
            .unwrap_or_else(default_addon_state);
        let mut entries = Vec::new();

        scan_addon_scope(&bundled_addons_dir(), "bundled", &state, &mut entries)?;
        if let Some(root) = project_root.as_ref() {
            scan_addon_scope(
                &root.join(".lux").join("addons"),
                "project",
                &state,
                &mut entries,
            )?;
        }
        if let Some(addons_dir) = global_addons_dir() {
            scan_addon_scope(&addons_dir, "global", &state, &mut entries)?;
        }

        entries.sort_by(|left, right| {
            left.manifest
                .name
                .cmp(&right.manifest.name)
                .then_with(|| left.scope.cmp(&right.scope))
        });
        Ok(entries)
    }

    fn scan_addon_scope(
        addons_dir: &Path,
        scope: &str,
        state: &AddonState,
        entries: &mut Vec<AddonEntry>,
    ) -> anyhow::Result<()> {
        let read_dir = match fs::read_dir(addons_dir) {
            Ok(read_dir) => read_dir,
            Err(error) if error.kind() == ErrorKind::NotFound => return Ok(()),
            Err(error) => {
                return Err(error).with_context(|| {
                    format!("failed to read addons directory {}", addons_dir.display())
                })
            }
        };

        for dir_entry in read_dir {
            let dir_entry = match dir_entry {
                Ok(dir_entry) => dir_entry,
                Err(error) => {
                    eprintln!("Warning: failed to read addon directory entry: {error}");
                    continue;
                }
            };
            let directory_path = dir_entry.path();
            if !directory_path.is_dir() {
                continue;
            }

            let manifest_path = directory_path.join("addon.json");
            let manifest_json = match fs::read_to_string(&manifest_path) {
                Ok(manifest_json) => manifest_json,
                Err(error) if error.kind() == ErrorKind::NotFound => continue,
                Err(error) => {
                    eprintln!(
                        "Warning: failed to read {}: {error}",
                        manifest_path.display()
                    );
                    continue;
                }
            };

            let manifest = match serde_json::from_str::<AddonManifest>(&manifest_json) {
                Ok(manifest) => manifest,
                Err(error) => {
                    eprintln!(
                        "Warning: failed to parse {}: {error}",
                        manifest_path.display()
                    );
                    continue;
                }
            };
            let status = if state
                .installed_addons
                .iter()
                .any(|installed| installed == &manifest.name)
            {
                "installed"
            } else {
                "available"
            };

            entries.push(AddonEntry {
                manifest,
                directory_path,
                scope: scope.to_string(),
                status: status.to_string(),
            });
        }

        Ok(())
    }

    fn bundled_addons_dir() -> PathBuf {
        Path::new(env!("CARGO_MANIFEST_DIR")).join("../Addons")
    }

    fn global_addons_dir() -> Option<PathBuf> {
        let home = if cfg!(windows) {
            std::env::var("USERPROFILE").ok()
        } else {
            std::env::var("HOME").ok()
        };
        home.map(|h| PathBuf::from(h).join(".lux").join("addons"))
    }

    fn addon_project_root() -> anyhow::Result<PathBuf> {
        let current_dir = std::env::current_dir()?;
        find_unity_project_root(current_dir.clone())
            .or_else(|| current_dir.parent().map(Path::to_path_buf))
            .context(
                "Unity project not found. Run lux addon from a Unity project or package checkout.",
            )
    }

    fn addon_state_path(project_root: &Path) -> PathBuf {
        project_root
            .join("Library")
            .join("Lux")
            .join("addon-state.json")
    }

    fn default_addon_state() -> AddonState {
        AddonState {
            version: 1,
            installed_addons: DEFAULT_INSTALLED_ADDONS
                .iter()
                .map(|name| (*name).to_string())
                .collect(),
            last_updated: "2026-05-03T00:00:00Z".to_string(),
        }
    }

    fn read_addon_state(project_root: &Path) -> anyhow::Result<AddonState> {
        let state_path = addon_state_path(project_root);
        let state_json = match fs::read_to_string(&state_path) {
            Ok(state_json) => state_json,
            Err(error) if error.kind() == ErrorKind::NotFound => return Ok(default_addon_state()),
            Err(error) => {
                return Err(error)
                    .with_context(|| format!("failed to read {}", state_path.display()))
            }
        };

        serde_json::from_str(&state_json)
            .with_context(|| format!("failed to parse {}", state_path.display()))
    }

    fn write_addon_state(project_root: &Path, state: &AddonState) -> anyhow::Result<()> {
        let state_path = addon_state_path(project_root);
        if let Some(parent) = state_path.parent() {
            fs::create_dir_all(parent)
                .with_context(|| format!("failed to create directory {}", parent.display()))?;
        }
        fs::write(&state_path, serde_json::to_string_pretty(state)?)
            .with_context(|| format!("failed to write addon state file {}", state_path.display()))
    }

    fn warn_missing_unity_packages(
        project_root: &Path,
        required_packages: &BTreeMap<String, String>,
    ) {
        if required_packages.is_empty() {
            return;
        }

        let manifest_path = project_root.join("Packages").join("manifest.json");
        let manifest_json = match fs::read_to_string(&manifest_path) {
            Ok(manifest_json) => manifest_json,
            Err(_) => {
                eprintln!(
                    "Warning: could not read {}; required Unity packages were not validated",
                    manifest_path.display()
                );
                return;
            }
        };
        let manifest: Value = match serde_json::from_str(&manifest_json) {
            Ok(manifest) => manifest,
            Err(error) => {
                eprintln!(
                    "Warning: failed to parse {}: {error}",
                    manifest_path.display()
                );
                return;
            }
        };
        let dependencies = manifest.get("dependencies").and_then(Value::as_object);

        for (package_name, required_version) in required_packages {
            if dependencies
                .and_then(|dependencies| dependencies.get(package_name))
                .is_none()
            {
                eprintln!(
                "Warning: required Unity package '{}' ({}) is not listed in Packages/manifest.json",
                package_name, required_version
            );
            }
        }
    }

    fn format_string_list(values: &[String]) -> String {
        if values.is_empty() {
            "N/A".to_string()
        } else {
            values.join(", ")
        }
    }

    fn format_string_map(values: &BTreeMap<String, String>) -> String {
        if values.is_empty() {
            "N/A".to_string()
        } else {
            values
                .iter()
                .map(|(key, value)| format!("{key}: {value}"))
                .collect::<Vec<_>>()
                .join(", ")
        }
    }

    fn current_utc_timestamp_string() -> String {
        let seconds = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_secs() as i64;
        format_unix_timestamp_utc(seconds)
    }

    fn format_unix_timestamp_utc(seconds: i64) -> String {
        let days = seconds.div_euclid(86_400);
        let seconds_of_day = seconds.rem_euclid(86_400);
        let (year, month, day) = civil_from_days(days);
        let hour = seconds_of_day / 3_600;
        let minute = (seconds_of_day % 3_600) / 60;
        let second = seconds_of_day % 60;
        format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}Z")
    }

    fn civil_from_days(days_since_epoch: i64) -> (i64, i64, i64) {
        let days = days_since_epoch + 719_468;
        let era = if days >= 0 { days } else { days - 146_096 } / 146_097;
        let day_of_era = days - era * 146_097;
        let year_of_era =
            (day_of_era - day_of_era / 1_460 + day_of_era / 36_524 - day_of_era / 146_096) / 365;
        let mut year = year_of_era + era * 400;
        let day_of_year = day_of_era - (365 * year_of_era + year_of_era / 4 - year_of_era / 100);
        let month_prime = (5 * day_of_year + 2) / 153;
        let day = day_of_year - (153 * month_prime + 2) / 5 + 1;
        let month = month_prime + if month_prime < 10 { 3 } else { -9 };
        year += if month <= 2 { 1 } else { 0 };
        (year, month, day)
    }
}

// ---------------------------------------------------------------------------
// lux addon
// ---------------------------------------------------------------------------

#[derive(Debug, serde::Deserialize, serde::Serialize)]
struct AddonManifest {
    name: String,
    #[serde(rename = "displayName")]
    display_name: Option<String>,
    version: String,
    description: String,
    category: Option<String>,
    #[serde(rename = "defineSymbols", default)]
    define_symbols: Vec<String>,
    #[serde(rename = "requiredPackages", default)]
    required_packages: BTreeMap<String, String>,
    #[serde(rename = "addonDependencies", default)]
    addon_dependencies: BTreeMap<String, String>,
    #[serde(default)]
    assemblies: Vec<String>,
    #[serde(default)]
    keywords: Vec<String>,
}

#[derive(Debug, serde::Serialize)]
struct AddonEntry {
    manifest: AddonManifest,
    directory_path: PathBuf,
    scope: String,
    status: String,
}

#[derive(Debug, serde::Deserialize, serde::Serialize)]
struct AddonState {
    version: u32,
    #[serde(rename = "installedAddons")]
    installed_addons: Vec<String>,
    #[serde(rename = "lastUpdated")]
    last_updated: String,
}

fn run_addon_command(args: AddonArgs) -> anyhow::Result<()> {
    match args.action {
        AddonAction::List(list_args) => print_addon_list(list_args),
        AddonAction::Info(info_args) => print_addon_info(info_args),
        AddonAction::Install(install_args) => install_addon(install_args),
        AddonAction::Remove(remove_args) => remove_addon(remove_args),
    }
}

fn print_addon_list(args: AddonListArgs) -> anyhow::Result<()> {
    let entries = discover_addons()?;
    let entries: Vec<_> = entries
        .into_iter()
        .filter(|entry| !args.available || entry.status == "available")
        .collect();

    if args.json {
        println!("{}", serde_json::to_string_pretty(&entries)?);
        return Ok(());
    }

    if entries.is_empty() {
        println!("No addons found");
        return Ok(());
    }

    println!("{:20} {:10} {:10} DESCRIPTION", "NAME", "VERSION", "STATUS");
    for entry in entries {
        println!(
            "{:20} {:10} {:10} {}",
            entry.manifest.name, entry.manifest.version, entry.status, entry.manifest.description
        );
    }

    Ok(())
}

fn print_addon_info(args: AddonInfoArgs) -> anyhow::Result<()> {
    let entries = discover_addons()?;
    let Some(entry) = entries
        .iter()
        .find(|entry| entry.manifest.name == args.name)
    else {
        eprintln!("Error: addon '{}' not found", args.name);
        std::process::exit(1);
    };

    if args.json {
        println!("{}", serde_json::to_string_pretty(&entry.manifest)?);
        return Ok(());
    }

    println!("Name:               {}", entry.manifest.name);
    println!(
        "Display Name:       {}",
        entry.manifest.display_name.as_deref().unwrap_or("N/A")
    );
    println!("Version:            {}", entry.manifest.version);
    println!("Description:        {}", entry.manifest.description);
    println!(
        "Category:           {}",
        entry.manifest.category.as_deref().unwrap_or("N/A")
    );
    println!(
        "Define Symbols:     {}",
        format_string_list(&entry.manifest.define_symbols)
    );
    println!(
        "Required Packages:  {}",
        format_string_map(&entry.manifest.required_packages)
    );
    println!(
        "Addon Dependencies: {}",
        format_string_map(&entry.manifest.addon_dependencies)
    );
    println!(
        "Assemblies:         {}",
        format_string_list(&entry.manifest.assemblies)
    );
    println!("Status:             {}", entry.status);
    println!("Location:           {}", entry.directory_path.display());

    Ok(())
}

fn install_addon(args: AddonInstallArgs) -> anyhow::Result<()> {
    let entries = discover_addons()?;
    let Some(entry) = entries
        .iter()
        .find(|entry| entry.manifest.name == args.name)
    else {
        eprintln!("Error: addon '{}' not found", args.name);
        std::process::exit(1);
    };

    let project_root = project_root_dir()?;
    let mut state = read_addon_state(&project_root)?;
    if state.installed_addons.iter().any(|name| name == &args.name) {
        println!("Addon '{}' is already installed", args.name);
        return Ok(());
    }

    let missing_dependencies: Vec<_> = entry
        .manifest
        .addon_dependencies
        .keys()
        .filter(|name| {
            !state
                .installed_addons
                .iter()
                .any(|installed| installed == *name)
        })
        .cloned()
        .collect();
    if !missing_dependencies.is_empty() {
        eprintln!(
            "Error: addon '{}' requires installed addon(s): {}",
            args.name,
            missing_dependencies.join(", ")
        );
        std::process::exit(1);
    }

    warn_missing_unity_packages(&project_root, &entry.manifest.required_packages)?;

    state.installed_addons.push(args.name.clone());
    state.installed_addons.sort();
    state.installed_addons.dedup();
    write_addon_state(&project_root, &state)?;

    println!("Installed addon '{}'", args.name);
    println!("Next steps: return to Unity and allow scripts to recompile.");
    Ok(())
}

fn remove_addon(args: AddonRemoveArgs) -> anyhow::Result<()> {
    let entries = discover_addons()?;
    let project_root = project_root_dir()?;
    let mut state = read_addon_state(&project_root)?;
    if !state.installed_addons.iter().any(|name| name == &args.name) {
        println!("Addon '{}' is not installed", args.name);
        return Ok(());
    }

    let dependents: Vec<_> = entries
        .iter()
        .filter(|entry| {
            state
                .installed_addons
                .iter()
                .any(|name| name == &entry.manifest.name)
        })
        .filter(|entry| entry.manifest.addon_dependencies.contains_key(&args.name))
        .map(|entry| entry.manifest.name.clone())
        .collect();
    if !args.force && !dependents.is_empty() {
        eprintln!(
            "Error: addon '{}' is required by installed addon(s): {}",
            args.name,
            dependents.join(", ")
        );
        eprintln!("Use --force to remove it anyway.");
        std::process::exit(1);
    }

    state.installed_addons.retain(|name| name != &args.name);
    write_addon_state(&project_root, &state)?;

    println!("Removed addon '{}'", args.name);
    println!("Next steps: return to Unity and allow scripts to recompile.");
    Ok(())
}

fn discover_addons() -> anyhow::Result<Vec<AddonEntry>> {
    let state = read_addon_state(&project_root_dir()?)?;
    let mut entries = Vec::new();

    scan_addon_scope(&package_addons_dir(), "bundled", &state, &mut entries)?;
    if let Some(addons_dir) = project_addons_dir() {
        scan_addon_scope(&addons_dir, "project", &state, &mut entries)?;
    }
    if let Some(addons_dir) = global_addons_dir() {
        scan_addon_scope(&addons_dir, "global", &state, &mut entries)?;
    }

    entries.sort_by(|left, right| left.manifest.name.cmp(&right.manifest.name));
    Ok(entries)
}

fn scan_addon_scope(
    addons_dir: &Path,
    scope: &str,
    state: &AddonState,
    entries: &mut Vec<AddonEntry>,
) -> anyhow::Result<()> {
    let read_dir = match fs::read_dir(addons_dir) {
        Ok(read_dir) => read_dir,
        Err(error) if error.kind() == ErrorKind::NotFound => return Ok(()),
        Err(error) => {
            return Err(error).with_context(|| {
                format!("failed to read addons directory {}", addons_dir.display())
            })
        }
    };

    for dir_entry in read_dir {
        let dir_entry = match dir_entry {
            Ok(dir_entry) => dir_entry,
            Err(error) => {
                eprintln!("Warning: failed to read addon directory entry: {error}");
                continue;
            }
        };
        let directory_path = dir_entry.path();
        if !directory_path.is_dir() {
            continue;
        }

        let manifest_path = directory_path.join("addon.json");
        let manifest_json = match fs::read_to_string(&manifest_path) {
            Ok(manifest_json) => manifest_json,
            Err(error) if error.kind() == ErrorKind::NotFound => continue,
            Err(error) => {
                eprintln!(
                    "Warning: failed to read {}: {error}",
                    manifest_path.display()
                );
                continue;
            }
        };

        let manifest = match serde_json::from_str::<AddonManifest>(&manifest_json) {
            Ok(manifest) => manifest,
            Err(error) => {
                eprintln!(
                    "Warning: failed to parse {}: {error}",
                    manifest_path.display()
                );
                continue;
            }
        };

        if entries
            .iter()
            .any(|entry| entry.manifest.name == manifest.name)
        {
            continue;
        }

        let status = if state
            .installed_addons
            .iter()
            .any(|name| name == &manifest.name)
        {
            "installed"
        } else {
            "available"
        };

        entries.push(AddonEntry {
            manifest,
            directory_path,
            scope: scope.to_string(),
            status: status.to_string(),
        });
    }

    Ok(())
}

fn read_addon_state(project_root: &Path) -> anyhow::Result<AddonState> {
    let state_path = addon_state_path(project_root);
    let state_json = match fs::read_to_string(&state_path) {
        Ok(state_json) => state_json,
        Err(error) if error.kind() == ErrorKind::NotFound => return Ok(default_addon_state()),
        Err(error) => {
            return Err(error).with_context(|| format!("failed to read {}", state_path.display()))
        }
    };
    serde_json::from_str(&state_json)
        .with_context(|| format!("failed to parse {}", state_path.display()))
}

fn write_addon_state(project_root: &Path, state: &AddonState) -> anyhow::Result<()> {
    let state_path = addon_state_path(project_root);
    if let Some(parent) = state_path.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("failed to create {}", parent.display()))?;
    }

    let updated_state = AddonState {
        version: state.version,
        installed_addons: state.installed_addons.clone(),
        last_updated: current_timestamp_utc(),
    };
    fs::write(&state_path, serde_json::to_string_pretty(&updated_state)?)
        .with_context(|| format!("failed to write {}", state_path.display()))?;
    Ok(())
}

fn default_addon_state() -> AddonState {
    AddonState {
        version: 1,
        installed_addons: vec![
            "webrtc".to_string(),
            "codex-image".to_string(),
            "pipeline-editor".to_string(),
            "unity-git".to_string(),
            "multi-ai".to_string(),
        ],
        last_updated: "2026-05-03T00:00:00Z".to_string(),
    }
}

fn warn_missing_unity_packages(
    project_root: &Path,
    required_packages: &BTreeMap<String, String>,
) -> anyhow::Result<()> {
    if required_packages.is_empty() {
        return Ok(());
    }

    let manifest_path = project_root.join("Packages").join("manifest.json");
    let manifest_json = match fs::read_to_string(&manifest_path) {
        Ok(manifest_json) => manifest_json,
        Err(error) if error.kind() == ErrorKind::NotFound => {
            for package_name in required_packages.keys() {
                eprintln!("Warning: required Unity package '{package_name}' could not be verified");
            }
            return Ok(());
        }
        Err(error) => {
            return Err(error)
                .with_context(|| format!("failed to read {}", manifest_path.display()))
        }
    };
    let manifest: Value = serde_json::from_str(&manifest_json)
        .with_context(|| format!("failed to parse {}", manifest_path.display()))?;
    let dependencies = manifest.get("dependencies").and_then(Value::as_object);
    for package_name in required_packages.keys() {
        if dependencies
            .and_then(|dependencies| dependencies.get(package_name))
            .is_none()
        {
            eprintln!("Warning: required Unity package '{package_name}' is not listed in Packages/manifest.json");
        }
    }
    Ok(())
}

fn package_addons_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../Addons")
}

fn project_addons_dir() -> Option<PathBuf> {
    std::env::current_dir()
        .ok()
        .map(|d| d.join(".lux").join("addons"))
}

fn global_addons_dir() -> Option<PathBuf> {
    let home = if cfg!(windows) {
        std::env::var("USERPROFILE").ok()
    } else {
        std::env::var("HOME").ok()
    };
    home.map(|h| PathBuf::from(h).join(".lux").join("addons"))
}

fn project_root_dir() -> anyhow::Result<PathBuf> {
    std::env::current_dir().context("failed to determine current directory")
}

fn addon_state_path(project_root: &Path) -> PathBuf {
    project_root
        .join("Library")
        .join("Lux")
        .join("addon-state.json")
}

fn format_string_list(values: &[String]) -> String {
    if values.is_empty() {
        "N/A".to_string()
    } else {
        values.join(", ")
    }
}

fn format_string_map(values: &BTreeMap<String, String>) -> String {
    if values.is_empty() {
        return "N/A".to_string();
    }

    values
        .iter()
        .map(|(key, value)| format!("{key}@{value}"))
        .collect::<Vec<_>>()
        .join(", ")
}

fn current_timestamp_utc() -> String {
    let seconds = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_secs() as i64)
        .unwrap_or(0);
    format_unix_timestamp_utc(seconds)
}

fn format_unix_timestamp_utc(seconds: i64) -> String {
    let days = seconds.div_euclid(86_400);
    let seconds_of_day = seconds.rem_euclid(86_400);
    let (year, month, day) = civil_from_days(days);
    let hour = seconds_of_day / 3_600;
    let minute = (seconds_of_day % 3_600) / 60;
    let second = seconds_of_day % 60;
    format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}Z")
}

fn civil_from_days(days: i64) -> (i64, u32, u32) {
    let z = days + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1_460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = mp + if mp < 10 { 3 } else { -9 };
    let year = y + if month <= 2 { 1 } else { 0 };
    (year, month as u32, day as u32)
}

// ---------------------------------------------------------------------------
// lux unity status
// ---------------------------------------------------------------------------

fn run_lux_unity_command(args: UnityArgs) -> anyhow::Result<()> {
    match args.command {
        UnityCommand::Status(status_args) => print_lux_unity_status(status_args),
        UnityCommand::Context(context_args) => print_lux_unity_context(context_args),
        UnityCommand::BackendStatus(backend_status_args) => {
            print_lux_backend_status(backend_status_args)
        }
        UnityCommand::BackendListCommands(backend_list_commands_args) => {
            print_lux_backend_command_list(backend_list_commands_args)
        }
        UnityCommand::GetLogs(get_logs_args) => print_lux_backend_console_logs(get_logs_args),
        UnityCommand::ClearConsole(clear_console_args) => {
            clear_lux_backend_clear_console(clear_console_args)
        }
        UnityCommand::FocusWindow(focus_window_args) => {
            print_lux_backend_focus_window(focus_window_args)
        }
        UnityCommand::Launch(launch_args) => run_lux_unity_launch(launch_args),
        UnityCommand::SceneSmoke(scene_smoke_args) => run_lux_scene_smoke(scene_smoke_args),
        UnityCommand::CreateObjects(create_objects_args) => {
            run_lux_create_objects(create_objects_args)
        }
        UnityCommand::FindGameObjects(find_game_objects_args) => {
            print_lux_backend_find_game_objects(find_game_objects_args)
        }
        UnityCommand::GetHierarchy(get_hierarchy_args) => {
            print_lux_backend_get_hierarchy(get_hierarchy_args)
        }
        UnityCommand::ControlPlayMode(control_play_mode_args) => {
            print_lux_backend_control_play_mode(control_play_mode_args)
        }
        UnityCommand::Screenshot(screenshot_args) => print_lux_backend_screenshot(screenshot_args),
        UnityCommand::SimulateMouseUi(simulate_mouse_ui_args) => {
            print_lux_backend_simulate_mouse_ui(simulate_mouse_ui_args)
        }
        UnityCommand::SimulateKeyboard(simulate_keyboard_args) => {
            print_lux_backend_simulate_keyboard(simulate_keyboard_args)
        }
        UnityCommand::SimulateMouseInput(simulate_mouse_input_args) => {
            print_lux_backend_simulate_mouse_input(simulate_mouse_input_args)
        }
        UnityCommand::RecordInput(record_input_args) => {
            print_lux_backend_record_input(record_input_args)
        }
        UnityCommand::ReplayInput(replay_input_args) => {
            print_lux_backend_replay_input(replay_input_args)
        }
        UnityCommand::ExecuteDynamicCode(execute_dynamic_code_args) => {
            print_lux_backend_execute_dynamic_code(execute_dynamic_code_args)
        }
    }
}

fn run_lux_create_objects(args: UnityCreateObjectsArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    run_lux_backend_object_command(
        &project_root,
        "create_lux_scene_objects",
        &args.scene_path,
        args.object_count,
        Duration::from_secs(10),
    )
}

fn run_lux_unity_launch(args: UnityLaunchArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root_with_max_depth(&args.project_path, args.max_depth)?;
    let launch_target = resolve_unity_launch_target(&project_root)?;
    let launch_params = json!({
        "restart": args.restart,
        "platform": args.platform,
        "maxDepth": args.max_depth,
        "addUnityHub": args.add_unity_hub,
        "favorite": args.favorite,
        "noWait": args.no_wait,
        "projectPath": project_root.to_string_lossy().to_string(),
    });

    eprintln!(
        "Lux launch: launching Unity editor for {}",
        project_root.display()
    );

    ProcessCommand::new(&launch_target.executable)
        .args(&launch_target.prefix_args)
        .arg("-projectPath")
        .arg(&project_root)
        .env(
            "LUX_UNITY_LAUNCH_PARAMS",
            serde_json::to_string(&launch_params)?,
        )
        .stdin(Stdio::null())
        .stdout(Stdio::inherit())
        .stderr(Stdio::inherit())
        .spawn()
        .with_context(|| {
            format!(
                "failed to launch Unity at {}",
                launch_target.executable.display()
            )
        })?;

    if args.no_wait {
        return Ok(());
    }

    wait_for_unity_bridge_ready(&project_root, Duration::from_secs(60))
}

fn print_lux_backend_find_game_objects(args: UnityFindGameObjectsArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let discovery = read_unity_bridge_discovery(&project_root)?;
    let search_text = args.search_text.clone().or_else(|| args.name.clone());
    let request = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": "find_lux_game_objects",
        "token": discovery.token,
        "params": {
            "findSearchMode": args.search_mode.clone(),
            "findSearchText": search_text,
            "findInlineLimit": args.inline_limit,
            "searchMode": args.search_mode.clone(),
            "name": args.name.clone(),
            "regex": args.regex.clone(),
            "path": args.path.clone(),
            "component": args.component.clone(),
            "tag": args.tag.clone(),
            "layer": args.layer.clone(),
            "activeState": args.active_state.clone(),
            "inlineLimit": args.inline_limit,
            "includeInheritedProperties": args.include_inherited_properties,
        }
    });
    let response_line = send_unity_tcp_line(
        &discovery,
        &format!("{}\n", serde_json::to_string(&request)?),
    )?;
    let response_json: Value =
        serde_json::from_str(&response_line).context("Unity TCP response was not valid JSON")?;
    if response_json.get("ok").and_then(Value::as_bool) != Some(true) {
        bail!(
            "Unity backend rejected find_lux_game_objects: {}",
            response_json
        );
    }
    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("findGameObjectsResult"))
        .context("Unity TCP response did not include payload.findGameObjectsResult")?;
    println!("{}", serde_json::to_string_pretty(payload)?);
    Ok(())
}

fn print_lux_backend_get_hierarchy(args: UnityGetHierarchyArgs) -> anyhow::Result<()> {
    let filter_count =
        (args.all as u8) + (args.root_path.is_some() as u8) + (args.use_selection as u8);
    if filter_count > 1 {
        bail!("Specify only one hierarchy filter: --all, --root-path, or --use-selection");
    }

    let project_root = resolve_project_root(&args.project_path)?;
    let discovery = read_unity_bridge_discovery(&project_root)?;
    let request = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": "get_lux_hierarchy",
        "token": discovery.token,
        "params": {
            "hierarchyAll": args.all || filter_count == 0,
            "hierarchyRootPath": args.root_path,
            "hierarchyUseSelection": args.use_selection,
            "maxDepth": args.max_depth,
            "includeComponents": args.include_components,
            "includeInactive": args.include_inactive,
            "includePaths": args.include_paths,
            "useComponentsLut": args.use_components_lut,
        }
    });
    let response_line = send_unity_tcp_line(
        &discovery,
        &format!("{}\n", serde_json::to_string(&request)?),
    )?;
    let response_json: Value =
        serde_json::from_str(&response_line).context("Unity TCP response was not valid JSON")?;
    if response_json.get("ok").and_then(Value::as_bool) != Some(true) {
        bail!(
            "Unity backend rejected get_lux_hierarchy: {}",
            response_json
        );
    }

    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("getHierarchyResult"))
        .context("Unity TCP response did not include payload.getHierarchyResult")?;
    let file_path = payload
        .get("filePath")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include payload.getHierarchyResult.filePath")?;
    let file_size_bytes = payload
        .get("fileSizeBytes")
        .and_then(Value::as_i64)
        .context("Unity TCP response did not include payload.getHierarchyResult.fileSizeBytes")?;
    let root_count = payload
        .get("rootCount")
        .and_then(Value::as_i64)
        .context("Unity TCP response did not include payload.getHierarchyResult.rootCount")?;
    let node_count = payload
        .get("nodeCount")
        .and_then(Value::as_i64)
        .context("Unity TCP response did not include payload.getHierarchyResult.nodeCount")?;
    let active_scene = payload
        .get("activeScene")
        .cloned()
        .context("Unity TCP response did not include payload.getHierarchyResult.activeScene")?;
    let filters = payload
        .get("filters")
        .cloned()
        .context("Unity TCP response did not include payload.getHierarchyResult.filters")?;

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "filePath": file_path,
            "fileSizeBytes": file_size_bytes,
            "rootCount": root_count,
            "nodeCount": node_count,
            "activeScene": active_scene,
            "filters": filters,
        }))?
    );
    Ok(())
}

fn print_lux_backend_screenshot(args: UnityScreenshotArgs) -> anyhow::Result<()> {
    if args.elements_only && !args.annotate_elements {
        bail!("--elements-only requires --annotate-elements");
    }

    let project_root = resolve_project_root(&args.project_path)?;
    let discovery = read_unity_bridge_discovery(&project_root)?;
    let request = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": "capture_lux_screenshot",
        "token": discovery.token,
        "params": {
            "screenshotCaptureMode": args.capture_mode,
            "screenshotAnnotateElements": args.annotate_elements,
            "screenshotElementsOnly": args.elements_only,
            "windowName": args.window_name,
            "resolutionScale": args.resolution_scale,
            "matchMode": args.match_mode,
            "outputDirectory": args.output_directory.as_ref().map(|path| path.to_string_lossy().to_string()),
            "actor": "lux-cli"
        }
    });
    let response_line = send_unity_tcp_line_with_timeout(
        &discovery,
        &format!("{}\n", serde_json::to_string(&request)?),
        Duration::from_secs(15),
    )?;
    let response_json: Value =
        serde_json::from_str(&response_line).context("Unity TCP response was not valid JSON")?;
    if response_json.get("ok").and_then(Value::as_bool) != Some(true) {
        bail!(
            "Unity backend rejected capture_lux_screenshot: {}",
            response_json
        );
    }

    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("screenshotResult"))
        .context("Unity TCP response did not include payload.screenshotResult")?;
    let file_path = payload
        .get("filePath")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include payload.screenshotResult.filePath")?;
    let file_size_bytes = payload
        .get("fileSizeBytes")
        .and_then(Value::as_i64)
        .context("Unity TCP response did not include payload.screenshotResult.fileSizeBytes")?;
    let media_type = payload
        .get("mediaType")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include payload.screenshotResult.mediaType")?;
    let capture_mode = payload
        .get("captureMode")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include payload.screenshotResult.captureMode")?;
    let annotation_count = payload
        .get("annotationCount")
        .and_then(Value::as_i64)
        .context("Unity TCP response did not include payload.screenshotResult.annotationCount")?;
    let annotated_elements = payload
        .get("annotatedElements")
        .cloned()
        .unwrap_or_else(|| json!([]));
    let annotated = payload
        .get("annotated")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let elements_only = payload
        .get("elementsOnly")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let screenshot_saved = payload
        .get("screenshotSaved")
        .and_then(Value::as_bool)
        .unwrap_or(false);

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "filePath": file_path,
            "fileSizeBytes": file_size_bytes,
            "mediaType": media_type,
            "captureMode": capture_mode,
            "annotated": annotated,
            "elementsOnly": elements_only,
            "screenshotSaved": screenshot_saved,
            "annotationCount": annotation_count,
            "annotatedElements": annotated_elements,
        }))?
    );
    Ok(())
}

fn print_lux_backend_simulate_keyboard(args: UnitySimulateKeyboardArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let response_json = send_lux_input_simulation_request(
        &project_root,
        "simulate_lux_keyboard",
        json!({
            "inputAction": args.action.as_str(),
            "inputKey": args.key,
            "inputDurationMs": args.duration_ms,
            "actor": "lux-cli"
        }),
    )?;
    print_lux_input_simulation_result(&response_json)
}

fn print_lux_backend_simulate_mouse_input(args: UnitySimulateMouseInputArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let response_json = send_lux_input_simulation_request(
        &project_root,
        "simulate_lux_mouse_input",
        json!({
            "inputAction": args.action.as_str(),
            "inputButton": args.button,
            "inputDeltaX": args.delta_x,
            "inputDeltaY": args.delta_y,
            "inputScrollX": args.scroll_x,
            "inputScrollY": args.scroll_y,
            "inputDurationMs": args.duration_ms,
            "inputSteps": args.steps,
            "actor": "lux-cli"
        }),
    )?;
    print_lux_input_simulation_result(&response_json)
}

fn print_lux_backend_record_input(args: UnityRecordInputArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let response_json = send_lux_input_simulation_request(
        &project_root,
        "record_lux_input",
        json!({
            "inputAction": args.action.as_str(),
            "outputPath": args.output_path.as_ref().map(|path| path.to_string_lossy().to_string()),
            "keys": args.keys,
            "delaySeconds": args.delay_seconds,
            "showOverlay": args.show_overlay,
            "actor": "lux-cli"
        }),
    )?;
    print_lux_input_record_result(&response_json)
}

fn print_lux_backend_replay_input(args: UnityReplayInputArgs) -> anyhow::Result<()> {
    if matches!(args.action, ReplayInputAction::Start) && args.file.is_none() {
        bail!("lux unity replay-input --action start requires --file <path>");
    }

    let project_root = resolve_project_root(&args.project_path)?;
    let response_json = send_lux_input_simulation_request(
        &project_root,
        "replay_lux_input",
        json!({
            "inputAction": args.action.as_str(),
            "inputFilePath": args.file.as_ref().map(|path| path.to_string_lossy().to_string()),
            "showOverlay": args.show_overlay,
            "loop": args.r#loop,
            "actor": "lux-cli"
        }),
    )?;
    print_lux_input_replay_result(&response_json)
}

fn print_lux_backend_simulate_mouse_ui(args: UnitySimulateMouseUiArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let response_json = send_lux_input_simulation_request(
        &project_root,
        "simulate_lux_mouse_ui",
        json!({
            "mouseUiAction": args.action.as_str(),
            "mouseUiX": args.x,
            "mouseUiY": args.y,
            "mouseUiDurationMs": args.duration_ms,
            "fromX": args.from_x,
            "fromY": args.from_y,
            "dragSpeed": args.drag_speed,
            "button": args.button,
            "bypassRaycast": args.bypass_raycast,
            "targetPath": args.target_path,
            "dropTargetPath": args.drop_target_path,
            "actor": "lux-cli"
        }),
    )?;
    print_lux_mouse_ui_result(&response_json)
}

fn send_lux_input_simulation_request(
    project_root: &Path,
    command: &str,
    params: Value,
) -> anyhow::Result<Value> {
    let discovery = read_unity_bridge_discovery(project_root)?;
    let request = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": command,
        "token": discovery.token,
        "params": params
    });
    let response_line = send_unity_tcp_line_with_timeout(
        &discovery,
        &format!("{}\n", serde_json::to_string(&request)?),
        Duration::from_secs(10),
    )?;
    let response_json: Value =
        serde_json::from_str(&response_line).context("Unity TCP response was not valid JSON")?;
    if response_json.get("ok").and_then(Value::as_bool) != Some(true) {
        bail!("Unity backend rejected {command}: {}", response_json);
    }

    Ok(response_json)
}

fn print_lux_mouse_ui_result(response_json: &Value) -> anyhow::Result<()> {
    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("mouseUiResult"))
        .context("Unity TCP response did not include payload.mouseUiResult")?;
    let schema_version = response_json
        .get("schemaVersion")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include schemaVersion")?;
    let captured_at_utc = response_json
        .get("capturedAtUtc")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include capturedAtUtc")?;

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "schemaVersion": schema_version,
            "capturedAtUtc": captured_at_utc,
            "action": payload.get("action").cloned().unwrap_or(Value::Null),
            "x": payload.get("x").cloned().unwrap_or(Value::Null),
            "y": payload.get("y").cloned().unwrap_or(Value::Null),
            "success": payload.get("success").cloned().unwrap_or(Value::Null),
            "targetName": payload.get("targetName").cloned().unwrap_or(Value::Null),
            "targetPath": payload.get("targetPath").cloned().unwrap_or(Value::Null),
            "raycastCount": payload.get("raycastCount").cloned().unwrap_or(Value::Null),
            "dragActive": payload.get("dragActive").cloned().unwrap_or(Value::Null),
        }))?
    );
    Ok(())
}

fn print_lux_input_simulation_result(response_json: &Value) -> anyhow::Result<()> {
    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("inputSimulationResult"))
        .context("Unity TCP response did not include payload.inputSimulationResult")?;
    let schema_version = response_json
        .get("schemaVersion")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include schemaVersion")?;
    let captured_at_utc = response_json
        .get("capturedAtUtc")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include capturedAtUtc")?;

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "schemaVersion": schema_version,
            "capturedAtUtc": captured_at_utc,
            "device": payload.get("device").cloned().unwrap_or(Value::Null),
            "action": payload.get("action").cloned().unwrap_or(Value::Null),
            "key": payload.get("key").cloned().unwrap_or(Value::Null),
            "button": payload.get("button").cloned().unwrap_or(Value::Null),
            "deltaX": payload.get("deltaX").cloned().unwrap_or(Value::Null),
            "deltaY": payload.get("deltaY").cloned().unwrap_or(Value::Null),
            "scrollX": payload.get("scrollX").cloned().unwrap_or(Value::Null),
            "scrollY": payload.get("scrollY").cloned().unwrap_or(Value::Null),
            "heldKeys": payload.get("heldKeys").cloned().unwrap_or_else(|| json!([])),
            "heldButtons": payload.get("heldButtons").cloned().unwrap_or_else(|| json!([])),
            "queuedActions": payload.get("queuedActions").cloned().unwrap_or(Value::Null),
        }))?
    );
    Ok(())
}

fn print_lux_input_record_result(response_json: &Value) -> anyhow::Result<()> {
    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("inputRecordResult"))
        .context("Unity TCP response did not include payload.inputRecordResult")?;
    let schema_version = response_json
        .get("schemaVersion")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include schemaVersion")?;
    let captured_at_utc = response_json
        .get("capturedAtUtc")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include capturedAtUtc")?;

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "schemaVersion": schema_version,
            "capturedAtUtc": captured_at_utc,
            "action": payload.get("action").cloned().unwrap_or(Value::Null),
            "active": payload.get("active").cloned().unwrap_or(Value::Null),
            "frameCount": payload.get("frameCount").cloned().unwrap_or(Value::Null),
            "filePath": payload.get("filePath").cloned().unwrap_or(Value::Null),
            "fileSizeBytes": payload.get("fileSizeBytes").cloned().unwrap_or(Value::Null),
            "mediaType": payload.get("mediaType").cloned().unwrap_or(Value::Null),
            "message": payload.get("message").cloned().unwrap_or(Value::Null),
        }))?
    );
    Ok(())
}

fn print_lux_input_replay_result(response_json: &Value) -> anyhow::Result<()> {
    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("inputReplayResult"))
        .context("Unity TCP response did not include payload.inputReplayResult")?;
    let schema_version = response_json
        .get("schemaVersion")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include schemaVersion")?;
    let captured_at_utc = response_json
        .get("capturedAtUtc")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include capturedAtUtc")?;

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "schemaVersion": schema_version,
            "capturedAtUtc": captured_at_utc,
            "action": payload.get("action").cloned().unwrap_or(Value::Null),
            "active": payload.get("active").cloned().unwrap_or(Value::Null),
            "filePath": payload.get("filePath").cloned().unwrap_or(Value::Null),
            "frameCount": payload.get("frameCount").cloned().unwrap_or(Value::Null),
            "replayedFrameCount": payload.get("replayedFrameCount").cloned().unwrap_or(Value::Null),
            "completed": payload.get("completed").cloned().unwrap_or(Value::Null),
            "message": payload.get("message").cloned().unwrap_or(Value::Null),
        }))?
    );
    Ok(())
}

fn print_lux_backend_execute_dynamic_code(args: UnityExecuteDynamicCodeArgs) -> anyhow::Result<()> {
    let code = resolve_dynamic_code_source(&args)?;
    let project_root = resolve_project_root(&args.project_path)?;
    let discovery = read_unity_bridge_discovery(&project_root)?;
    let request = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": "execute_lux_dynamic_code",
        "token": discovery.token,
        "params": {
            "dynamicCode": code,
            "parameters": args.parameters,
            "compileOnly": args.compile_only,
            "yieldToForegroundRequests": args.yield_to_foreground_requests,
            "actor": "lux-cli"
        }
    });
    let response_line = send_unity_tcp_line_with_timeout(
        &discovery,
        &format!("{}\n", serde_json::to_string(&request)?),
        Duration::from_secs(30),
    )?;
    let response_json: Value =
        serde_json::from_str(&response_line).context("Unity TCP response was not valid JSON")?;
    if response_json.get("ok").and_then(Value::as_bool) != Some(true) {
        bail!(
            "Unity backend rejected execute_lux_dynamic_code: {}",
            response_json
        );
    }

    print_lux_dynamic_code_result(&response_json)
}

fn resolve_dynamic_code_source(args: &UnityExecuteDynamicCodeArgs) -> anyhow::Result<String> {
    match (&args.code, &args.file) {
        (Some(_), Some(_)) => bail!("Specify only one dynamic code source: --code or --file"),
        (Some(code), None) => Ok(code.clone()),
        (None, Some(path)) => fs::read_to_string(path)
            .with_context(|| format!("failed to read dynamic code file at {}", path.display())),
        (None, None) => {
            bail!("lux unity execute-dynamic-code requires --code <string> or --file <path>")
        }
    }
}

fn print_lux_dynamic_code_result(response_json: &Value) -> anyhow::Result<()> {
    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("dynamicCodeResult"))
        .context("Unity TCP response did not include payload.dynamicCodeResult")?;
    let schema_version = response_json
        .get("schemaVersion")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include schemaVersion")?;
    let captured_at_utc = response_json
        .get("capturedAtUtc")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include capturedAtUtc")?;

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "schemaVersion": schema_version,
            "capturedAtUtc": captured_at_utc,
            "success": payload.get("success").cloned().unwrap_or(Value::Null),
            "action": payload.get("action").cloned().unwrap_or(Value::Null),
            "result": payload.get("result").cloned().unwrap_or(Value::Null),
            "resultType": payload.get("resultType").cloned().unwrap_or(Value::Null),
            "message": payload.get("message").cloned().unwrap_or(Value::Null),
            "diagnostics": payload.get("diagnostics").cloned().unwrap_or_else(|| json!([])),
            "logs": payload.get("logs").cloned().unwrap_or_else(|| json!([])),
            "elapsedTimeMs": payload.get("elapsedTimeMs").cloned().unwrap_or(Value::Null),
        }))?
    );
    Ok(())
}

fn print_lux_backend_control_play_mode(args: UnityControlPlayModeArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let requested_action = args.action.as_str();
    let initial_response = fetch_lux_backend_play_mode_state(&project_root, requested_action)?;
    let mut state = extract_lux_play_mode_state(&initial_response, requested_action)?;

    if args.wait && requested_action != "status" {
        let deadline = Instant::now() + Duration::from_secs(15);
        let mut last_wait_error: Option<String> = None;
        while !play_mode_state_matches(&state, requested_action) {
            if Instant::now() >= deadline {
                if let Some(error) = last_wait_error {
                    bail!(
                        "timed out waiting for PlayMode action {requested_action}; last state: {}; last transient error: {error}",
                        serde_json::to_string(&state)?
                    );
                }

                bail!(
                    "timed out waiting for PlayMode action {requested_action}; last state: {}",
                    serde_json::to_string(&state)?
                );
            }

            std::thread::sleep(Duration::from_millis(250));
            match fetch_lux_backend_play_mode_state(&project_root, "status") {
                Ok(poll_response) => {
                    state = extract_lux_play_mode_state(&poll_response, requested_action)?;
                    last_wait_error = None;
                }
                Err(error)
                    if is_transient_play_mode_wait_error(&error) && Instant::now() < deadline =>
                {
                    last_wait_error = Some(error.to_string());
                }
                Err(error) => return Err(error),
            }
        }
    }

    println!("{}", serde_json::to_string_pretty(&state)?);
    Ok(())
}

fn fetch_lux_backend_play_mode_state(project_root: &Path, action: &str) -> anyhow::Result<Value> {
    let discovery = read_unity_bridge_discovery(project_root)?;
    let request = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": "control_lux_play_mode",
        "token": discovery.token,
        "params": {
            "playModeAction": action,
            "actor": "lux-cli"
        }
    });
    let response_line = send_unity_tcp_line(
        &discovery,
        &format!("{}\n", serde_json::to_string(&request)?),
    )?;
    let response_json: Value =
        serde_json::from_str(&response_line).context("Unity TCP response was not valid JSON")?;
    if response_json.get("ok").and_then(Value::as_bool) != Some(true) {
        bail!(
            "Unity backend rejected control_lux_play_mode: {}",
            response_json
        );
    }

    Ok(response_json)
}

fn extract_lux_play_mode_state(
    response_json: &Value,
    requested_action: &str,
) -> anyhow::Result<Value> {
    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("playModeState"))
        .context("Unity TCP response did not include payload.playModeState")?;
    let is_playing = payload
        .get("isPlaying")
        .and_then(Value::as_bool)
        .context("Unity TCP response did not include payload.playModeState.isPlaying")?;
    let is_paused = payload
        .get("isPaused")
        .and_then(Value::as_bool)
        .context("Unity TCP response did not include payload.playModeState.isPaused")?;
    let transition_requested = payload
        .get("transitionRequested")
        .and_then(Value::as_bool)
        .context("Unity TCP response did not include payload.playModeState.transitionRequested")?;
    let schema_version = response_json
        .get("schemaVersion")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include schemaVersion")?;
    let captured_at_utc = response_json
        .get("capturedAtUtc")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include capturedAtUtc")?;

    Ok(json!({
        "schemaVersion": schema_version,
        "capturedAtUtc": captured_at_utc,
        "action": requested_action,
        "isPlaying": is_playing,
        "isPaused": is_paused,
        "transitionRequested": transition_requested,
    }))
}

fn play_mode_state_matches(state: &Value, action: &str) -> bool {
    let is_playing = state
        .get("isPlaying")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let is_paused = state
        .get("isPaused")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let transition_requested = state
        .get("transitionRequested")
        .and_then(Value::as_bool)
        .unwrap_or(false);

    match action {
        "play" => is_playing && !transition_requested,
        "stop" => !is_playing && !transition_requested,
        "pause" => is_playing && is_paused,
        "resume" => is_playing && !is_paused,
        "status" => true,
        _ => false,
    }
}

fn is_transient_play_mode_wait_error(error: &anyhow::Error) -> bool {
    if error.chain().any(|cause| {
        cause
            .downcast_ref::<std::io::Error>()
            .map(is_transient_play_mode_wait_io_error)
            .unwrap_or(false)
    }) {
        return true;
    }

    let message = error.to_string().to_ascii_lowercase();
    message.contains("unity tcp connection closed before sending a response")
        || message.contains("unity tcp connection closed while writing request")
}

fn is_transient_play_mode_wait_io_error(error: &std::io::Error) -> bool {
    matches!(
        error.kind(),
        ErrorKind::ConnectionRefused
            | ErrorKind::ConnectionReset
            | ErrorKind::BrokenPipe
            | ErrorKind::ConnectionAborted
            | ErrorKind::UnexpectedEof
            | ErrorKind::NotConnected
            | ErrorKind::WouldBlock
            | ErrorKind::Interrupted
            | ErrorKind::TimedOut
    )
}

fn print_lux_backend_status(args: UnityBackendStatusArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let discovery_path = project_root.join("Library/UnityAiBridge/server.json");
    let discovery = match read_unity_bridge_discovery(&project_root) {
        Ok(discovery) => discovery,
        Err(error) => {
            println!(
                "{}",
                serde_json::to_string_pretty(&json!({
                    "ok": false,
                    "running": false,
                    "discovery_path": discovery_path,
                    "message": error.to_string(),
                }))?
            );
            return Ok(());
        }
    };

    let request = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": "get_backend_status",
        "token": discovery.token,
        "params": {}
    });
    let status_result = send_unity_tcp_line(
        &discovery,
        &format!("{}\n", serde_json::to_string(&request)?),
    );
    match status_result {
        Ok(response_line) => {
            let response_json: Value = serde_json::from_str(&response_line)
                .unwrap_or_else(|_| json!({ "raw": response_line }));
            if response_json.get("ok").and_then(Value::as_bool) == Some(true) {
                if let Some(backend_status) = response_json
                    .get("payload")
                    .and_then(|payload| payload.get("backendStatus"))
                {
                    println!("{}", serde_json::to_string_pretty(backend_status)?);
                    return Ok(());
                }
            }
            println!(
                "{}",
                serde_json::to_string_pretty(&json!({
                    "ok": true,
                    "running": true,
                    "host": discovery.host,
                    "port": discovery.port,
                    "discovery_path": discovery_path,
                    "ping": response_json,
                }))?
            );
        }
        Err(error) => {
            println!(
                "{}",
                serde_json::to_string_pretty(&json!({
                    "ok": false,
                    "running": false,
                    "host": discovery.host,
                    "port": discovery.port,
                    "discovery_path": discovery_path,
                    "message": error.to_string(),
                }))?
            );
        }
    }

    Ok(())
}

fn print_lux_backend_command_list(args: UnityBackendListCommandsArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let response_json = fetch_lux_backend_protocol_info(&project_root)?;
    let protocol_info = response_json
        .get("payload")
        .and_then(|payload| payload.get("protocolInfo"))
        .context("Unity TCP response did not include payload.protocolInfo")?;

    let schema_version = response_json
        .get("schemaVersion")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include schemaVersion")?;
    let backend_version = protocol_info
        .get("backendVersion")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include payload.protocolInfo.backendVersion")?;
    let commands = protocol_info
        .get("commands")
        .and_then(Value::as_array)
        .context("Unity TCP response did not include payload.protocolInfo.commands")?
        .iter()
        .map(|command| {
            command
                .as_str()
                .map(str::to_owned)
                .context("Unity TCP response included a non-string command name")
        })
        .collect::<anyhow::Result<Vec<_>>>()?;
    let captured_at_utc = response_json
        .get("capturedAtUtc")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include capturedAtUtc")?;

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "schemaVersion": schema_version,
            "backendVersion": backend_version,
            "commands": commands,
            "capturedAtUtc": captured_at_utc,
        }))?
    );

    Ok(())
}

fn print_lux_backend_console_logs(args: UnityGetLogsArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let response_json = fetch_lux_backend_command_response_with_params(
        &project_root,
        "get_lux_console_logs",
        json!({
            "logType": args.log_type,
            "maxCount": args.max_count,
            "searchText": args.search_text,
            "includeStackTrace": args.include_stack_trace,
            "useRegex": args.use_regex,
            "searchInStackTrace": args.search_in_stack_trace,
        }),
    )?;
    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("consoleLogs"))
        .context("Unity TCP response did not include payload.consoleLogs")?;

    let schema_version = response_json
        .get("schemaVersion")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include schemaVersion")?;
    let captured_at_utc = response_json
        .get("capturedAtUtc")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include capturedAtUtc")?;
    let total_count = payload
        .get("totalCount")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include payload.consoleLogs.totalCount")?;
    let displayed_count = payload
        .get("displayedCount")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include payload.consoleLogs.displayedCount")?;
    let console_logs = payload
        .get("consoleLogs")
        .and_then(Value::as_array)
        .context("Unity TCP response did not include payload.consoleLogs.consoleLogs")?;

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "schemaVersion": schema_version,
            "capturedAtUtc": captured_at_utc,
            "totalCount": total_count,
            "displayedCount": displayed_count,
            "consoleLogs": console_logs,
        }))?
    );

    Ok(())
}

fn clear_lux_backend_clear_console(args: UnityClearConsoleArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let response_json = fetch_lux_backend_command_response_with_params(
        &project_root,
        "clear_lux_console",
        json!({
            "addConfirmationMessage": args.add_confirmation_message,
        }),
    )?;
    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("consoleClearResult"))
        .context("Unity TCP response did not include payload.consoleClearResult")?;

    let schema_version = response_json
        .get("schemaVersion")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include schemaVersion")?;
    let captured_at_utc = response_json
        .get("capturedAtUtc")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include capturedAtUtc")?;
    let before_count = payload
        .get("beforeCount")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include payload.consoleClearResult.beforeCount")?;
    let after_count = payload
        .get("afterCount")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include payload.consoleClearResult.afterCount")?;

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "schemaVersion": schema_version,
            "capturedAtUtc": captured_at_utc,
            "beforeCount": before_count,
            "afterCount": after_count,
        }))?
    );

    Ok(())
}

fn print_lux_backend_focus_window(args: UnityFocusWindowArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let response_json = fetch_lux_backend_command_response(&project_root, "focus_lux_window")?;
    let payload = response_json
        .get("payload")
        .and_then(|payload| payload.get("focusWindowResult"))
        .context("Unity TCP response did not include payload.focusWindowResult")?;

    let schema_version = response_json
        .get("schemaVersion")
        .and_then(Value::as_u64)
        .context("Unity TCP response did not include schemaVersion")?;
    let captured_at_utc = response_json
        .get("capturedAtUtc")
        .and_then(Value::as_str)
        .context("Unity TCP response did not include capturedAtUtc")?;
    let focused = payload
        .get("focused")
        .and_then(Value::as_bool)
        .context("Unity TCP response did not include payload.focusWindowResult.focused")?;

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "schemaVersion": schema_version,
            "capturedAtUtc": captured_at_utc,
            "focused": focused,
        }))?
    );

    Ok(())
}

fn fetch_lux_backend_protocol_info(project_root: &Path) -> anyhow::Result<Value> {
    fetch_lux_backend_command_response(project_root, "get_protocol_info")
}

fn fetch_lux_backend_command_response(project_root: &Path, command: &str) -> anyhow::Result<Value> {
    fetch_lux_backend_command_response_with_params(project_root, command, json!({}))
}

fn fetch_lux_backend_command_response_with_params(
    project_root: &Path,
    command: &str,
    params: Value,
) -> anyhow::Result<Value> {
    let discovery = read_unity_bridge_discovery(project_root)?;
    let request = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": command,
        "token": discovery.token,
        "params": params
    });
    let response_line = send_unity_tcp_line(
        &discovery,
        &format!("{}\n", serde_json::to_string(&request)?),
    )?;
    let response_json: Value =
        serde_json::from_str(&response_line).context("Unity TCP response was not valid JSON")?;

    if response_json.get("ok").and_then(Value::as_bool) != Some(true) {
        bail!("Unity TCP rejected {command}: {}", response_json);
    }

    Ok(response_json)
}

fn run_lux_scene_smoke(args: UnitySceneSmokeArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    if !args.batch {
        return run_lux_scene_smoke_live(&project_root, &args)
            .with_context(|| "Lux backend live scene-smoke failed. Start the Lux/Unity AI Bridge backend in the open Unity Editor, or pass --batch only when no Unity instance has the project open.");
    }

    let launch_target = resolve_unity_launch_target(&project_root)?;
    let results_dir = project_root.join("TestResults");
    fs::create_dir_all(&results_dir)
        .with_context(|| format!("failed to create {}", results_dir.display()))?;
    let log_path = results_dir.join("LuxSceneSmoke.log");
    let result_path = results_dir.join("LuxSceneSmokeResult.json");
    if result_path.exists() {
        fs::remove_file(&result_path)
            .with_context(|| format!("failed to remove stale {}", result_path.display()))?;
    }

    eprintln!(
        "Lux scene-smoke: launching Unity batch mode for {}",
        project_root.display()
    );

    let status = ProcessCommand::new(&launch_target.executable)
        .args(&launch_target.prefix_args)
        .arg("-batchmode")
        .arg("-nographics")
        .arg("-projectPath")
        .arg(&project_root)
        .arg("-executeMethod")
        .arg("Linalab.Lux.Editor.LuxSceneSmoke.Run")
        .arg("-logFile")
        .arg(&log_path)
        .env("LUX_SCENE_SMOKE_SCENE_PATH", &args.scene_path)
        .env(
            "LUX_SCENE_SMOKE_OBJECT_COUNT",
            args.object_count.to_string(),
        )
        .stdin(Stdio::null())
        .stdout(Stdio::inherit())
        .stderr(Stdio::inherit())
        .status()
        .with_context(|| {
            format!(
                "failed to launch Unity at {}",
                launch_target.executable.display()
            )
        })?;

    if result_path.exists() {
        let result_text = fs::read_to_string(&result_path)
            .with_context(|| format!("failed to read {}", result_path.display()))?;
        println!("{}", result_text.trim());
    } else {
        println!(
            "{{ \"ok\": {}, \"message\": \"Unity exited without writing LuxSceneSmokeResult.json\", \"log\": \"{}\" }}",
            status.success(),
            log_path.display()
        );
    }

    if !status.success() {
        bail!("Lux scene-smoke failed. See log: {}", log_path.display());
    }

    Ok(())
}

fn run_lux_scene_smoke_live(project_root: &Path, args: &UnitySceneSmokeArgs) -> anyhow::Result<()> {
    let result_path = project_root.join("TestResults/LuxSceneSmokeResult.json");
    if result_path.exists() {
        fs::remove_file(&result_path)
            .with_context(|| format!("failed to remove stale {}", result_path.display()))?;
    }

    let discovery = read_unity_bridge_discovery(project_root)?;
    let request = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": "run_lux_scene_smoke",
        "token": discovery.token,
        "params": {
            "scenePath": args.scene_path,
            "sceneSmokeObjectCount": args.object_count,
            "actor": "lux-cli"
        }
    });
    let response = send_unity_tcp_line_with_timeout(
        &discovery,
        &format!("{}\n", serde_json::to_string(&request)?),
        Duration::from_secs(45),
    )?;
    let response_json: Value =
        serde_json::from_str(&response).context("Unity TCP response was not valid JSON")?;
    if response_json.get("ok").and_then(Value::as_bool) != Some(true) {
        bail!("Unity TCP rejected scene-smoke: {}", response_json);
    }

    let deadline = Instant::now() + Duration::from_secs(45);
    loop {
        if result_path.exists() {
            let result_text = fs::read_to_string(&result_path)
                .with_context(|| format!("failed to read {}", result_path.display()))?;
            println!("{}", result_text.trim());
            let result_json: Value = serde_json::from_str(&result_text)
                .context("LuxSceneSmokeResult.json was not valid JSON")?;
            if result_json.get("ok").and_then(Value::as_bool) == Some(true) {
                return Ok(());
            }
            bail!("Lux live scene-smoke failed: {}", result_text.trim());
        }

        if Instant::now() >= deadline {
            bail!("timed out waiting for {}", result_path.display());
        }
        std::thread::sleep(Duration::from_millis(250));
    }
}

fn run_lux_backend_object_command(
    project_root: &Path,
    command: &str,
    scene_path: &str,
    object_count: u32,
    timeout: Duration,
) -> anyhow::Result<()> {
    let result_path = project_root.join("TestResults/LuxSceneSmokeResult.json");
    if result_path.exists() {
        fs::remove_file(&result_path)
            .with_context(|| format!("failed to remove stale {}", result_path.display()))?;
    }

    let discovery = read_unity_bridge_discovery(project_root)?;
    let request = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": command,
        "token": discovery.token,
        "params": {
            "scenePath": scene_path,
            "createObjectsScenePath": scene_path,
            "createObjectsCount": object_count,
            "sceneSmokeObjectCount": object_count,
            "actor": "lux-cli"
        }
    });
    let response = send_unity_tcp_line_with_timeout(
        &discovery,
        &format!("{}\n", serde_json::to_string(&request)?),
        Duration::from_secs(30),
    )?;
    let response_json: Value =
        serde_json::from_str(&response).context("Unity TCP response was not valid JSON")?;
    if response_json.get("ok").and_then(Value::as_bool) != Some(true) {
        bail!("Unity TCP rejected {command}: {}", response_json);
    }

    let deadline = Instant::now() + timeout;
    loop {
        if result_path.exists() {
            let result_text = fs::read_to_string(&result_path)
                .with_context(|| format!("failed to read {}", result_path.display()))?;
            println!("{}", result_text.trim());
            let result_json: Value = serde_json::from_str(&result_text)
                .context("LuxSceneSmokeResult.json was not valid JSON")?;
            if result_json.get("ok").and_then(Value::as_bool) == Some(true) {
                return Ok(());
            }
            bail!(
                "Lux backend command {command} failed: {}",
                result_text.trim()
            );
        }

        if Instant::now() >= deadline {
            bail!("timed out waiting for {}", result_path.display());
        }
        std::thread::sleep(Duration::from_millis(100));
    }
}

pub(crate) fn read_unity_bridge_discovery(
    project_root: &Path,
) -> anyhow::Result<UnityBridgeDiscovery> {
    let discovery_path = project_root.join("Library/UnityAiBridge/server.json");
    let text = fs::read_to_string(&discovery_path).with_context(|| {
        format!(
            "Unity AI Bridge discovery file not found at {}",
            discovery_path.display()
        )
    })?;
    serde_json::from_str(&text).with_context(|| {
        format!(
            "failed to parse Unity AI Bridge discovery file at {}",
            discovery_path.display()
        )
    })
}

pub(crate) fn send_unity_tcp_line(
    discovery: &UnityBridgeDiscovery,
    request_line: &str,
) -> anyhow::Result<String> {
    send_unity_tcp_line_with_timeout(discovery, request_line, Duration::from_secs(10))
}

fn wait_for_unity_bridge_ready(project_root: &Path, timeout: Duration) -> anyhow::Result<()> {
    let deadline = Instant::now() + timeout;
    let discovery_path = project_root.join("Library/UnityAiBridge/server.json");
    let mut last_error: Option<String> = None;

    loop {
        if Instant::now() >= deadline {
            let message = last_error
                .map(|error| format!(": {error}"))
                .unwrap_or_default();
            bail!(
                "timed out waiting for Unity bridge readiness at {}{}",
                discovery_path.display(),
                message
            );
        }

        match read_unity_bridge_discovery(project_root) {
            Ok(discovery) => {
                let ping = json!({
                    "schemaVersion": 1,
                    "requestId": uuid::Uuid::new_v4().to_string(),
                    "command": "ping",
                    "token": discovery.token,
                    "params": {}
                });
                match send_unity_tcp_line_with_timeout(
                    &discovery,
                    &format!("{}\n", serde_json::to_string(&ping)?),
                    Duration::from_secs(1),
                ) {
                    Ok(response_line) => {
                        let response_json: Value = serde_json::from_str(&response_line)
                            .context("Unity TCP response was not valid JSON")?;
                        if response_json.get("ok").and_then(Value::as_bool) == Some(true)
                            && response_json
                                .get("payload")
                                .and_then(|payload| payload.get("ping"))
                                .and_then(|ping| ping.get("status"))
                                .and_then(Value::as_str)
                                == Some("ok")
                        {
                            return Ok(());
                        }
                        last_error =
                            Some(format!("Unity TCP ping was not ready: {}", response_json));
                    }
                    Err(error) => {
                        last_error = Some(error.to_string());
                    }
                }
            }
            Err(error) => {
                last_error = Some(error.to_string());
            }
        }

        std::thread::sleep(Duration::from_millis(250));
    }
}

pub(crate) fn send_unity_tcp_line_with_timeout(
    discovery: &UnityBridgeDiscovery,
    request_line: &str,
    timeout: Duration,
) -> anyhow::Result<String> {
    let deadline = Instant::now() + timeout;
    let mut stream = connect_unity_tcp_with_retry(discovery, deadline)?;
    stream.set_read_timeout(Some(Duration::from_millis(250)))?;
    stream.set_write_timeout(Some(Duration::from_millis(250)))?;
    write_unity_tcp_with_retry(&mut stream, request_line.as_bytes(), deadline)?;

    let mut buffer = Vec::new();
    let mut chunk = [0_u8; 1024];
    loop {
        let size = match stream.read(&mut chunk) {
            Ok(size) => size,
            Err(error) if is_transient_socket_error(&error) && Instant::now() < deadline => {
                std::thread::sleep(Duration::from_millis(25));
                continue;
            }
            Err(error) => return Err(error).context("Unity TCP response read failed"),
        };
        if size == 0 {
            break;
        }
        buffer.extend_from_slice(&chunk[..size]);
        if let Some(index) = buffer.iter().position(|byte| *byte == b'\n') {
            return String::from_utf8(buffer[..index].to_vec())
                .context("Unity TCP response was not UTF-8");
        }

        if Instant::now() >= deadline {
            bail!("timed out waiting for Unity TCP response");
        }
    }

    bail!("Unity TCP connection closed before sending a response")
}

fn run_bridge_command(args: BridgeArgs) -> anyhow::Result<()> {
    match args.action {
        BridgeAction::Watch(watch_args) => watch_unity_bridge_events(watch_args),
    }
}

fn watch_unity_bridge_events(args: BridgeWatchArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let discovery = read_unity_bridge_discovery(&project_root)?;
    let deadline = Instant::now() + Duration::from_secs(10);
    let mut stream = connect_unity_tcp_with_retry(&discovery, deadline)?;
    stream.set_read_timeout(Some(Duration::from_millis(250)))?;
    stream.set_write_timeout(Some(Duration::from_millis(250)))?;

    let subscribe = json!({
        "schemaVersion": 1,
        "requestId": uuid::Uuid::new_v4().to_string(),
        "command": "subscribe_events",
        "token": discovery.token,
        "params": {
            "eventTypes": "compile_started,compile_result"
        }
    });
    let subscribe_line = format!("{}\n", serde_json::to_string(&subscribe)?);
    write_unity_tcp_with_retry(&mut stream, subscribe_line.as_bytes(), deadline)?;

    let mut reader = BufReader::new(stream);
    let mut line = String::new();
    loop {
        line.clear();
        match reader.read_line(&mut line) {
            Ok(0) => return Ok(()),
            Ok(_) => {
                let trimmed = line.trim_end();
                if trimmed.is_empty() {
                    continue;
                }

                let value: Value = serde_json::from_str(trimmed)
                    .context("Unity AI Bridge watch received invalid JSON")?;
                if value.get("type").and_then(Value::as_str) == Some("event") {
                    println!("{}", serde_json::to_string(&value)?);
                } else if value.get("ok").and_then(Value::as_bool) == Some(false) {
                    bail!("Unity AI Bridge event subscription failed: {}", value);
                }
            }
            Err(error) if is_transient_socket_error(&error) => continue,
            Err(error) => return Err(error).context("Unity AI Bridge watch read failed"),
        }
    }
}

pub(crate) fn connect_unity_tcp_with_retry(
    discovery: &UnityBridgeDiscovery,
    deadline: Instant,
) -> anyhow::Result<std::net::TcpStream> {
    loop {
        match std::net::TcpStream::connect((discovery.host.as_str(), discovery.port)) {
            Ok(stream) => return Ok(stream),
            Err(error) if is_transient_socket_error(&error) && Instant::now() < deadline => {
                std::thread::sleep(Duration::from_millis(50));
            }
            Err(error) => {
                return Err(error).with_context(|| {
                    format!(
                        "failed to connect to Unity AI Bridge at {}:{}",
                        discovery.host, discovery.port
                    )
                });
            }
        }
    }
}

pub(crate) fn write_unity_tcp_with_retry(
    stream: &mut std::net::TcpStream,
    mut bytes: &[u8],
    deadline: Instant,
) -> anyhow::Result<()> {
    while !bytes.is_empty() {
        match stream.write(bytes) {
            Ok(0) => bail!("Unity TCP connection closed while writing request"),
            Ok(size) => bytes = &bytes[size..],
            Err(error) if is_transient_socket_error(&error) && Instant::now() < deadline => {
                std::thread::sleep(Duration::from_millis(25));
            }
            Err(error) => return Err(error).context("Unity TCP request write failed"),
        }
    }

    stream.flush().context("Unity TCP request flush failed")
}

fn is_transient_socket_error(error: &std::io::Error) -> bool {
    matches!(
        error.kind(),
        ErrorKind::WouldBlock | ErrorKind::Interrupted | ErrorKind::TimedOut
    )
}

fn print_lux_unity_context(args: UnityContextArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    if args.refresh {
        refresh_lux_unity_context(&project_root)?;
    }

    let context_path = project_root.join("UserSettings/LuxUnityContext.json");
    let context_text = fs::read_to_string(&context_path).with_context(|| {
        format!(
            "failed to read Lux Unity context at {}. Open Unity or run `lux unity context --refresh`.",
            context_path.display()
        )
    })?;
    let context_json: Value = serde_json::from_str(&context_text).with_context(|| {
        format!(
            "failed to parse Lux Unity context at {}",
            context_path.display()
        )
    })?;

    println!("{}", serde_json::to_string_pretty(&context_json)?);
    Ok(())
}

fn refresh_lux_unity_context(project_root: &Path) -> anyhow::Result<()> {
    let launch_target = resolve_unity_launch_target(project_root)?;
    let results_dir = project_root.join("TestResults");
    fs::create_dir_all(&results_dir)
        .with_context(|| format!("failed to create {}", results_dir.display()))?;
    let log_path = results_dir.join("LuxUnityContextRefresh.log");

    eprintln!(
        "Lux unity context: refreshing via Unity batch mode for {}",
        project_root.display()
    );

    let status = ProcessCommand::new(&launch_target.executable)
        .args(&launch_target.prefix_args)
        .arg("-batchmode")
        .arg("-quit")
        .arg("-nographics")
        .arg("-projectPath")
        .arg(project_root)
        .arg("-executeMethod")
        .arg("Linalab.Lux.Editor.LuxUnityContext.Refresh")
        .arg("-logFile")
        .arg(&log_path)
        .stdin(Stdio::null())
        .stdout(Stdio::inherit())
        .stderr(Stdio::inherit())
        .status()
        .with_context(|| {
            format!(
                "failed to launch Unity at {}",
                launch_target.executable.display()
            )
        })?;

    if !status.success() {
        bail!(
            "Lux Unity context refresh failed. See log: {}",
            log_path.display()
        );
    }

    Ok(())
}

fn print_lux_unity_status(args: UnityStatusArgs) -> anyhow::Result<()> {
    let project_root = match args.project_path {
        Some(path) => path,
        None => find_unity_project_root(std::env::current_dir()?)
            .context("Unity project not found. Use --project-path.")?,
    };
    let settings_path = project_root.join("UserSettings/LuxBridgeSettings.json");
    let settings_text = fs::read_to_string(&settings_path).with_context(|| {
        format!(
            "failed to read Lux bridge settings at {}. Open Unity and run Tools > Linalab > Lux > Unity Bridge > Write Lux Bridge Settings.",
            settings_path.display()
        )
    })?;
    let settings: LuxBridgeSettings = serde_json::from_str(&settings_text).with_context(|| {
        format!(
            "failed to parse Lux bridge settings at {}",
            settings_path.display()
        )
    })?;

    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "ok": true,
            "schema_version": settings.schema_version,
            "protocol": settings.protocol,
            "package_name": settings.package_name,
            "package_version": settings.package_version,
            "project_root": settings.project_root,
            "rust_gateway_path": settings.rust_gateway_path,
            "unity_server_port": settings.unity_server_port,
            "generated_at_utc": settings.generated_at_utc,
            "settings_path": settings_path,
        }))?
    );
    Ok(())
}

// ---------------------------------------------------------------------------
// lux compile — Unity batch mode via -executeMethod
// ---------------------------------------------------------------------------

fn run_batch_compile(args: CompileArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;

    if let Ok(discovery) = read_unity_bridge_discovery(&project_root) {
        // Try dedicated compile command first
        let request = json!({
            "schemaVersion": 1,
            "requestId": uuid::Uuid::new_v4().to_string(),
            "command": "compile_lux_project",
            "token": discovery.token,
            "params": {}
        });
        match send_unity_tcp_line(
            &discovery,
            &format!("{}\n", serde_json::to_string(&request)?),
        ) {
            Ok(response) => {
                let response_json: Value = serde_json::from_str(&response)
                    .context("compile TCP response was not valid JSON")?;
                // If command is registered, use its result
                if response_json.get("errorCode").and_then(Value::as_str) != Some("unknown_command")
                {
                    let compile_ok = response_json.get("ok").and_then(Value::as_bool) == Some(true);
                    if let Some(payload) = response_json
                        .get("payload")
                        .and_then(|payload| payload.get("compileResult"))
                    {
                        println!("{}", serde_json::to_string_pretty(payload)?);
                        if payload.get("ok").and_then(Value::as_bool) != Some(true) {
                            std::process::exit(1);
                        }
                    } else {
                        println!("{}", serde_json::to_string_pretty(&response_json)?);
                    }
                    if !compile_ok {
                        std::process::exit(1);
                    }
                    return Ok(());
                }
                // compile_lux_project not registered — fall through to dynamic code
                eprintln!(
                    "compile_lux_project not registered, trying execute-dynamic-code fallback..."
                );
            }
            Err(error) => {
                eprintln!("Live Unity Editor compile failed to connect, falling back to batch mode: {error}");
            }
        }

        // Fallback: use execute-dynamic-code to compile in live Editor
        let compile_code = "UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate); var ok = !UnityEditor.EditorUtility.scriptCompilationFailed; return new { ok, error_count = 0, message = ok ? \"Compilation succeeded.\" : \"Script compilation failed. Check Unity console for errors.\", timestamp_utc = System.DateTime.UtcNow.ToString(\"O\") };";
        let dynamic_request = json!({
            "schemaVersion": 1,
            "requestId": uuid::Uuid::new_v4().to_string(),
            "command": "execute_lux_dynamic_code",
            "token": discovery.token,
            "params": { "dynamicCode": compile_code }
        });
        match send_unity_tcp_line(
            &discovery,
            &format!("{}\n", serde_json::to_string(&dynamic_request)?),
        ) {
            Ok(response) => {
                let response_json: Value = serde_json::from_str(&response)
                    .context("dynamic code compile response was not valid JSON")?;
                let compile_ok = response_json.get("ok").and_then(Value::as_bool) == Some(true);
                if let Some(payload) = response_json
                    .get("payload")
                    .and_then(|p| p.get("dynamicCodeResult"))
                {
                    let _success = payload
                        .get("success")
                        .and_then(Value::as_bool)
                        .unwrap_or(false);
                    let result_str = payload.get("result").and_then(Value::as_str).unwrap_or("");
                    let ok = result_str.contains("ok = True") || result_str.contains("ok=True");
                    println!(
                        "{{\"ok\": {}, \"message\": \"{}\", \"source\": \"dynamic-code\"}}",
                        ok,
                        if ok {
                            "Compilation succeeded."
                        } else {
                            "Script compilation failed. Check Unity console for errors."
                        }
                    );
                    if !ok {
                        std::process::exit(1);
                    }
                } else {
                    println!("{}", serde_json::to_string_pretty(&response_json)?);
                }
                if !compile_ok {
                    std::process::exit(1);
                }
                return Ok(());
            }
            Err(error) => {
                eprintln!(
                    "Dynamic code compile also failed: {error}, falling back to batch mode..."
                );
            }
        }
    } else {
        eprintln!("No live Unity Editor detected, falling back to batch mode...");
    }

    let launch_target = resolve_unity_launch_target(&project_root)?;

    eprintln!(
        "Lux compile: launching Unity in batch mode for {}",
        project_root.display()
    );

    let results_dir = project_root.join("TestResults");
    fs::create_dir_all(&results_dir)
        .with_context(|| format!("failed to create {}", results_dir.display()))?;
    let log_path = results_dir.join("CompileLog.log");
    let compile_result_path = results_dir.join("CompileResult.json");
    if compile_result_path.exists() {
        fs::remove_file(&compile_result_path)
            .with_context(|| format!("failed to remove stale {}", compile_result_path.display()))?;
    }

    let status = ProcessCommand::new(&launch_target.executable)
        .args(&launch_target.prefix_args)
        .arg("-batchmode")
        .arg("-quit")
        .arg("-nographics")
        .arg("-projectPath")
        .arg(&project_root)
        .arg("-executeMethod")
        .arg("Linalab.Lux.Editor.LuxBatchCompile.Compile")
        .arg("-logFile")
        .arg(&log_path)
        .stdin(Stdio::null())
        .stdout(Stdio::inherit())
        .stderr(Stdio::inherit())
        .status()
        .with_context(|| {
            format!(
                "failed to launch Unity at {}",
                launch_target.executable.display()
            )
        })?;

    if compile_result_path.exists() {
        let result_text = fs::read_to_string(&compile_result_path)
            .with_context(|| format!("failed to read {}", compile_result_path.display()))?;
        println!("{}", result_text.trim());
    } else {
        println!(
            "{{ \"ok\": false, \"message\": \"Unity exited without writing CompileResult.json\", \"unity_exit_success\": {} }}",
            status.success()
        );
    }

    if !status.success() {
        eprintln!("Lux compile failed. See log: {}", log_path.display());
        std::process::exit(1);
    }
    Ok(())
}

// ---------------------------------------------------------------------------
// lux run-tests — Unity batch mode via -runTests
// ---------------------------------------------------------------------------

fn run_batch_tests(args: RunTestsArgs) -> anyhow::Result<()> {
    let project_root = resolve_project_root(&args.project_path)?;
    let platform = args.test_platform.clone();

    if let Ok(discovery) = read_unity_bridge_discovery(&project_root) {
        let mut params = serde_json::Map::new();
        params.insert("testPlatform".to_string(), Value::String(platform.clone()));
        if let Some(test_results) = &args.test_results {
            params.insert(
                "testResults".to_string(),
                Value::String(test_results.display().to_string()),
            );
        }
        let request = json!({
            "schemaVersion": 1,
            "requestId": uuid::Uuid::new_v4().to_string(),
            "command": "run_lux_tests",
            "token": discovery.token,
            "params": Value::Object(params)
        });
        match send_unity_tcp_line(
            &discovery,
            &format!("{}\n", serde_json::to_string(&request)?),
        ) {
            Ok(response) => {
                let response_json: Value = serde_json::from_str(&response)
                    .context("run-tests TCP response was not valid JSON")?;
                let error_code = response_json.get("errorCode").and_then(Value::as_str);
                if matches!(error_code, Some("unknown_command" | "registry_not_ready")) {
                    eprintln!(
                        "run_lux_tests not available in live Unity Editor, falling back to batch mode..."
                    );
                } else {
                    if let Some(payload) = response_json
                        .get("payload")
                        .and_then(|payload| payload.get("testRunResult"))
                    {
                        println!("{}", serde_json::to_string_pretty(payload)?);
                    } else {
                        println!("{}", serde_json::to_string_pretty(&response_json)?);
                    }
                    if response_json.get("ok").and_then(Value::as_bool) != Some(true) {
                        std::process::exit(1);
                    }
                    return Ok(());
                }
            }
            Err(error) => {
                eprintln!("Live Unity Editor test run failed to connect, falling back to batch mode: {error}");
            }
        }
    } else {
        eprintln!("No live Unity Editor detected, falling back to batch mode...");
    }

    let launch_target = resolve_unity_launch_target(&project_root)?;

    let results_dir = project_root.join("TestResults");
    fs::create_dir_all(&results_dir)
        .with_context(|| format!("failed to create {}", results_dir.display()))?;

    let platform_label = match platform.as_str() {
        "EditMode" => "EditMode",
        "PlayMode" => "PlayMode",
        other => other,
    };
    let test_results = match &args.test_results {
        Some(p) => p.clone(),
        None => results_dir.join(format!("{}Results.xml", platform_label)),
    };
    let log_file = match &args.log_file {
        Some(p) => p.clone(),
        None => results_dir.join(format!("{}Log.log", platform_label)),
    };

    eprintln!(
        "Lux run-tests: launching Unity in batch mode for {} ({})",
        project_root.display(),
        platform_label
    );

    let status = ProcessCommand::new(&launch_target.executable)
        .args(&launch_target.prefix_args)
        .arg("-runTests")
        .arg("-batchmode")
        .arg("-nographics")
        .arg("-projectPath")
        .arg(&project_root)
        .arg("-testPlatform")
        .arg(&platform)
        .arg("-testResults")
        .arg(&test_results)
        .arg("-logFile")
        .arg(&log_file)
        .stdin(Stdio::null())
        .stdout(Stdio::inherit())
        .stderr(Stdio::inherit())
        .status()
        .with_context(|| {
            format!(
                "failed to launch Unity at {}",
                launch_target.executable.display()
            )
        })?;

    println!(
        "{{ \"ok\": {}, \"test_platform\": \"{}\", \"results\": \"{}\", \"log\": \"{}\" }}",
        status.success(),
        platform_label,
        test_results.display(),
        log_file.display()
    );

    if !status.success() {
        eprintln!("Lux run-tests failed. See log: {}", log_file.display());
        std::process::exit(1);
    }
    Ok(())
}

// ---------------------------------------------------------------------------
// Shared helpers
// ---------------------------------------------------------------------------

pub(crate) fn resolve_project_root(project_path: &Option<PathBuf>) -> anyhow::Result<PathBuf> {
    resolve_project_root_with_max_depth(project_path, 0)
}

fn resolve_project_root_with_max_depth(
    project_path: &Option<PathBuf>,
    max_depth: i64,
) -> anyhow::Result<PathBuf> {
    match project_path {
        Some(path) => Ok(path.clone()),
        None => {
            let current_dir = std::env::current_dir()?;
            find_unity_project_root(current_dir.clone())
                .or_else(|| find_unity_project_root_within_depth(&current_dir, max_depth.max(0)))
                .context("Unity project not found. Use --project-path.")
        }
    }
}

fn find_unity_project_root(mut current: PathBuf) -> Option<PathBuf> {
    loop {
        if is_unity_project(&current) {
            return Some(current);
        }
        if !current.pop() {
            return None;
        }
    }
}

fn find_unity_project_root_within_depth(root: &Path, max_depth: i64) -> Option<PathBuf> {
    if is_unity_project(root) {
        return Some(root.to_path_buf());
    }

    if max_depth == 0 {
        return None;
    }

    let entries = fs::read_dir(root).ok()?;
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_dir() {
            if let Some(project_root) = find_unity_project_root_within_depth(&path, max_depth - 1) {
                return Some(project_root);
            }
        }
    }

    None
}

fn is_unity_project(path: &Path) -> bool {
    path.join("Assets").is_dir() && path.join("ProjectSettings").is_dir()
}

struct UnityLaunchTarget {
    executable: PathBuf,
    prefix_args: Vec<String>,
}

fn resolve_unity_launch_target(project_root: &Path) -> anyhow::Result<UnityLaunchTarget> {
    if let Some(editor) = std::env::var_os("LUX_UNITY_EDITOR") {
        return Ok(UnityLaunchTarget {
            executable: PathBuf::from(editor),
            prefix_args: Vec::new(),
        });
    }

    let version = read_unity_editor_version(project_root)?;

    #[cfg(target_os = "macos")]
    {
        let hub_editor = PathBuf::from(format!(
            "/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"
        ));
        if hub_editor.is_file() {
            return Ok(UnityLaunchTarget {
                executable: hub_editor,
                prefix_args: Vec::new(),
            });
        }
    }

    #[cfg(target_os = "windows")]
    {
        let hub_editor = PathBuf::from(format!(
            "C:\\Program Files\\Unity\\Hub\\Editor\\{version}\\Editor\\Unity.exe"
        ));
        if hub_editor.is_file() {
            return Ok(UnityLaunchTarget {
                executable: hub_editor,
                prefix_args: Vec::new(),
            });
        }
    }

    #[cfg(target_os = "linux")]
    {
        let hub_editor = PathBuf::from(format!("/opt/Unity/Hub/Editor/{version}/Editor/Unity"));
        if hub_editor.is_file() {
            return Ok(UnityLaunchTarget {
                executable: hub_editor,
                prefix_args: Vec::new(),
            });
        }

        if let Some(home) = std::env::var_os("HOME") {
            let home_editor =
                PathBuf::from(home).join(format!("Unity/Hub/Editor/{version}/Editor/Unity"));
            if home_editor.is_file() {
                return Ok(UnityLaunchTarget {
                    executable: home_editor,
                    prefix_args: Vec::new(),
                });
            }
        }
    }

    bail!(
        "Unity Editor {version} not found in standard Hub locations. \
         Set LUX_UNITY_EDITOR to the Unity executable path."
    )
}

fn read_unity_editor_version(project_root: &Path) -> anyhow::Result<String> {
    let version_path = project_root.join("ProjectSettings/ProjectVersion.txt");
    let text = fs::read_to_string(&version_path)
        .with_context(|| format!("failed to read {}", version_path.display()))?;
    text.lines()
        .find_map(|line| line.strip_prefix("m_EditorVersion:"))
        .map(str::trim)
        .filter(|version| !version.is_empty())
        .map(ToOwned::to_owned)
        .context("ProjectSettings/ProjectVersion.txt did not contain m_EditorVersion")
}

// ---------------------------------------------------------------------------
// lux serve — WebSocket gateway
// ---------------------------------------------------------------------------

async fn serve(args: ServeArgs) -> anyhow::Result<()> {
    let addr = SocketAddr::new(args.host, args.port);
    let state = server::GatewayState::new(server::GatewayConfig {
        token: args.token,
        history_capacity: args.history_capacity,
    });
    let idle_timeout = args.idle_timeout.checked_mul(60).map(Duration::from_secs);
    let app = server::router(state.clone());
    let listener = tokio::net::TcpListener::bind(addr)
        .await
        .with_context(|| format!("failed to bind Lux gateway at {addr}"))?;

    tracing::info!(%addr, "Lux gateway listening");
    axum::serve(listener, app)
        .with_graceful_shutdown(shutdown_signal(state, idle_timeout))
        .await
        .context("Lux gateway stopped with an error")
}

async fn shutdown_signal(state: server::GatewayState, idle_timeout: Option<Duration>) {
    let ctrl_c = async {
        if let Err(error) = tokio::signal::ctrl_c().await {
            tracing::warn!(%error, "failed to listen for shutdown signal");
        }
    };

    if let Some(timeout) = idle_timeout.filter(|duration| !duration.is_zero()) {
        tokio::select! {
            _ = ctrl_c => {}
            _ = state.wait_for_idle_timeout(timeout) => {
                eprintln!(
                    "Lux gateway graceful shutdown: idle timeout reached after {} minutes without activity",
                    timeout.as_secs() / 60
                );
            }
        }
    } else {
        ctrl_c.await;
    }
}
