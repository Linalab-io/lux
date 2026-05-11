use std::collections::HashMap;
use std::fs;
use std::path::{Path, PathBuf};

use anyhow::{bail, Context, Result};
use chrono::Utc;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use uuid::Uuid;

pub const SUPPORTED_SPEC_MAJOR_VERSION: &str = "1";

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct SpecProject {
    pub version: String,
    pub project_id: String,
    pub project_name: String,
    pub created_at: String,
    pub updated_at: String,
    pub source: String,
    pub status: SpecStatus,
    pub domains: SpecDomains,
    #[serde(default)]
    pub unity: Option<UnitySpec>,
    #[serde(default)]
    pub targets: Option<TargetsSpec>,
    #[serde(default)]
    pub packages: Option<PackagesSpec>,
    #[serde(default)]
    pub testing: Option<TestingSpec>,
    #[serde(default)]
    pub glossary: Option<GlossarySpec>,
    pub schell_evaluation: SchellEvaluation,
    pub overall_ambiguity: f64,
}

impl Default for SpecProject {
    fn default() -> Self {
        let now = Utc::now().to_rfc3339();
        Self {
            version: "1.0.0".to_string(),
            project_id: String::new(),
            project_name: String::new(),
            created_at: now.clone(),
            updated_at: now,
            source: "lux-init".to_string(),
            status: SpecStatus::Draft,
            domains: SpecDomains::default(),
            unity: None,
            targets: None,
            packages: None,
            testing: None,
            glossary: None,
            schell_evaluation: SchellEvaluation::default(),
            overall_ambiguity: 1.0,
        }
    }
}

impl SpecProject {
    pub fn validate(&self) -> Result<(), String> {
        validate_supported_version(&self.version)?;
        validate_score("overall_ambiguity", self.overall_ambiguity)?;
        self.domains.validate()?;
        if let Some(unity) = &self.unity {
            unity.validate()?;
        }
        if let Some(targets) = &self.targets {
            targets.validate()?;
        }
        if let Some(packages) = &self.packages {
            packages.validate()?;
        }
        if let Some(testing) = &self.testing {
            testing.validate()?;
        }
        if let Some(glossary) = &self.glossary {
            glossary.validate()?;
        }
        self.schell_evaluation.validate()?;
        Ok(())
    }
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum SpecStatus {
    Draft,
    Active,
    Deprecated,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct SpecDomains {
    pub design: Option<DomainSpec>,
    pub architecture: Option<DomainSpec>,
    pub art_style: Option<DomainSpec>,
    pub audio: Option<DomainSpec>,
    pub narrative: Option<DomainSpec>,
    pub levels: Option<DomainSpec>,
    pub ui_ux: Option<DomainSpec>,
    pub custom: HashMap<String, DomainSpec>,
}

impl Default for SpecDomains {
    fn default() -> Self {
        Self {
            design: None,
            architecture: None,
            art_style: None,
            audio: None,
            narrative: None,
            levels: None,
            ui_ux: None,
            custom: HashMap::new(),
        }
    }
}

impl SpecDomains {
    pub fn validate(&self) -> Result<(), String> {
        let built_in = [
            self.design.as_ref(),
            self.architecture.as_ref(),
            self.art_style.as_ref(),
            self.audio.as_ref(),
            self.narrative.as_ref(),
            self.levels.as_ref(),
            self.ui_ux.as_ref(),
        ];

        for domain in built_in.into_iter().flatten() {
            domain.validate()?;
        }

        for (name, domain) in &self.custom {
            if name.trim().is_empty() {
                return Err("custom domain name cannot be empty".to_string());
            }
            domain.validate()?;
        }

        Ok(())
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct DomainSpec {
    pub name: String,
    pub content_path: String,
    pub fields: HashMap<String, Value>,
    pub ambiguity_score: f64,
    pub last_evaluated: Option<String>,
    pub defined: bool,
}

impl DomainSpec {
    pub fn new(
        name: impl Into<String>,
        content_path: impl Into<String>,
        ambiguity_score: f64,
    ) -> Self {
        Self {
            name: name.into(),
            content_path: content_path.into(),
            fields: HashMap::new(),
            ambiguity_score: clamp_score(ambiguity_score),
            last_evaluated: None,
            defined: false,
        }
    }

    pub fn validate(&self) -> Result<(), String> {
        if self.name.trim().is_empty() {
            return Err("domain name cannot be empty".to_string());
        }
        if self.content_path.trim().is_empty() {
            return Err(format!(
                "domain '{}' content_path cannot be empty",
                self.name
            ));
        }
        validate_score(
            &format!("domain '{}' ambiguity_score", self.name),
            self.ambiguity_score,
        )
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct UnitySpec {
    pub required_version: Option<String>,
    pub detected_version: Option<String>,
    pub render_pipeline: Option<String>,
    pub scripting_backend: Option<String>,
}

impl Default for UnitySpec {
    fn default() -> Self {
        Self {
            required_version: None,
            detected_version: None,
            render_pipeline: None,
            scripting_backend: None,
        }
    }
}

impl UnitySpec {
    pub fn validate(&self) -> Result<(), String> {
        if let Some(value) = &self.render_pipeline {
            match value.as_str() {
                "urp" | "hdrp" | "built-in" => {}
                _ => return Err("render_pipeline must be one of: urp, hdrp, built-in".to_string()),
            }
        }
        if let Some(value) = &self.scripting_backend {
            match value.as_str() {
                "il2cpp" | "mono" => {}
                _ => return Err("scripting_backend must be one of: il2cpp, mono".to_string()),
            }
        }
        Ok(())
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct TargetsSpec {
    #[serde(default)]
    pub platforms: Vec<String>,
    #[serde(default)]
    pub min_sdk: HashMap<String, String>,
    pub test_platform: Option<String>,
}

impl Default for TargetsSpec {
    fn default() -> Self {
        Self {
            platforms: Vec::new(),
            min_sdk: HashMap::new(),
            test_platform: None,
        }
    }
}

impl TargetsSpec {
    pub fn validate(&self) -> Result<(), String> {
        for platform in &self.platforms {
            if platform.trim().is_empty() {
                return Err("targets.platforms cannot contain empty values".to_string());
            }
        }
        for (platform, sdk) in &self.min_sdk {
            if platform.trim().is_empty() {
                return Err("targets.min_sdk keys cannot be empty".to_string());
            }
            if sdk.trim().is_empty() {
                return Err(format!("targets.min_sdk['{platform}'] cannot be empty"));
            }
        }
        if let Some(platform) = &self.test_platform {
            if platform.trim().is_empty() {
                return Err("test_platform cannot be empty".to_string());
            }
        }
        Ok(())
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct PackageEntry {
    pub name: String,
    pub reason: Option<String>,
    pub version: Option<String>,
}

impl Default for PackageEntry {
    fn default() -> Self {
        Self {
            name: String::new(),
            reason: None,
            version: None,
        }
    }
}

impl PackageEntry {
    pub fn validate(&self) -> Result<(), String> {
        if self.name.trim().is_empty() {
            return Err("package name cannot be empty".to_string());
        }
        Ok(())
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct PackagesSpec {
    #[serde(default)]
    pub required: Vec<PackageEntry>,
    #[serde(default)]
    pub forbidden: Vec<PackageEntry>,
    #[serde(default)]
    pub detected: Vec<PackageEntry>,
}

impl Default for PackagesSpec {
    fn default() -> Self {
        Self {
            required: Vec::new(),
            forbidden: Vec::new(),
            detected: Vec::new(),
        }
    }
}

impl PackagesSpec {
    pub fn validate(&self) -> Result<(), String> {
        for package in &self.required {
            package.validate()?;
        }
        for package in &self.forbidden {
            package.validate()?;
        }
        for package in &self.detected {
            package.validate()?;
        }
        Ok(())
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct TestingSpec {
    pub framework: Option<String>,
    pub strategy: Option<String>,
    #[serde(default)]
    pub coverage: bool,
}

impl Default for TestingSpec {
    fn default() -> Self {
        Self {
            framework: None,
            strategy: None,
            coverage: false,
        }
    }
}

impl TestingSpec {
    pub fn validate(&self) -> Result<(), String> {
        if let Some(framework) = &self.framework {
            if framework.trim().is_empty() {
                return Err("testing.framework cannot be empty".to_string());
            }
        }
        if let Some(strategy) = &self.strategy {
            if strategy.trim().is_empty() {
                return Err("testing.strategy cannot be empty".to_string());
            }
        }
        Ok(())
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct GlossarySpec {
    #[serde(default = "default_glossary_path")]
    pub path: String,
    pub last_updated: Option<String>,
    #[serde(default)]
    pub term_count: u32,
}

impl Default for GlossarySpec {
    fn default() -> Self {
        Self {
            path: default_glossary_path(),
            last_updated: None,
            term_count: 0,
        }
    }
}

impl GlossarySpec {
    pub fn validate(&self) -> Result<(), String> {
        if self.path.trim().is_empty() {
            return Err("glossary.path cannot be empty".to_string());
        }
        Ok(())
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct SchellEvaluation {
    pub phase1_experience: PhaseResult,
    pub phase2_tetrad: TetradResult,
    pub phase3_core_loop: PhaseResult,
    pub phase4_motivation: PhaseResult,
    pub phase5_assessment: AssessmentResult,
}

impl Default for SchellEvaluation {
    fn default() -> Self {
        Self {
            phase1_experience: PhaseResult::missing("Experience Lens"),
            phase2_tetrad: TetradResult::default(),
            phase3_core_loop: PhaseResult::missing("Core Loop Stress Test"),
            phase4_motivation: PhaseResult::missing("Player Motivation"),
            phase5_assessment: AssessmentResult::missing(),
        }
    }
}

impl SchellEvaluation {
    pub fn validate(&self) -> Result<(), String> {
        self.phase1_experience.validate()?;
        self.phase2_tetrad.validate()?;
        self.phase3_core_loop.validate()?;
        self.phase4_motivation.validate()?;
        self.phase5_assessment.validate()?;
        Ok(())
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct TetradResult {
    pub mechanics: PillarRating,
    pub story: PillarRating,
    pub aesthetics: PillarRating,
    pub technology: PillarRating,
    pub harmony_score: f64,
}

impl Default for TetradResult {
    fn default() -> Self {
        Self {
            mechanics: PillarRating::missing(),
            story: PillarRating::missing(),
            aesthetics: PillarRating::missing(),
            technology: PillarRating::missing(),
            harmony_score: 0.0,
        }
    }
}

impl TetradResult {
    pub fn validate(&self) -> Result<(), String> {
        self.mechanics.validate("mechanics")?;
        self.story.validate("story")?;
        self.aesthetics.validate("aesthetics")?;
        self.technology.validate("technology")?;
        validate_score("harmony_score", self.harmony_score)
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct PillarRating {
    pub status: PillarStatus,
    pub description: Option<String>,
    pub score: f64,
}

impl PillarRating {
    pub fn missing() -> Self {
        Self {
            status: PillarStatus::Missing,
            description: None,
            score: 0.0,
        }
    }

    pub fn validate(&self, name: &str) -> Result<(), String> {
        validate_score(&format!("{name} score"), self.score)
    }
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum PillarStatus {
    Strong,
    NeedsWork,
    Missing,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct PhaseResult {
    pub name: String,
    pub status: PillarStatus,
    pub summary: Option<String>,
    pub score: f64,
    pub questions: Vec<String>,
}

impl PhaseResult {
    pub fn missing(name: impl Into<String>) -> Self {
        Self {
            name: name.into(),
            status: PillarStatus::Missing,
            summary: None,
            score: 0.0,
            questions: Vec::new(),
        }
    }

    pub fn validate(&self) -> Result<(), String> {
        if self.name.trim().is_empty() {
            return Err("phase name cannot be empty".to_string());
        }
        validate_score(&format!("phase '{}' score", self.name), self.score)
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct AssessmentResult {
    pub status: PillarStatus,
    pub viability_score: f64,
    pub strengths: Vec<String>,
    pub risks: Vec<String>,
    pub recommendations: Vec<String>,
    pub summary: Option<String>,
}

impl AssessmentResult {
    pub fn missing() -> Self {
        Self {
            status: PillarStatus::Missing,
            viability_score: 0.0,
            strengths: Vec::new(),
            risks: Vec::new(),
            recommendations: Vec::new(),
            summary: None,
        }
    }

    pub fn validate(&self) -> Result<(), String> {
        validate_score("viability_score", self.viability_score)
    }
}

fn validate_supported_version(version: &str) -> Result<(), String> {
    let mut parts = version.split('.');
    let major = parts.next().unwrap_or_default();
    let minor = parts.next();
    let patch = parts.next();

    if parts.next().is_some()
        || major != SUPPORTED_SPEC_MAJOR_VERSION
        || minor.and_then(|part| part.parse::<u64>().ok()).is_none()
        || patch.and_then(|part| part.parse::<u64>().ok()).is_none()
    {
        return Err(format!("unsupported spec version: {version}"));
    }

    Ok(())
}

fn default_glossary_path() -> String {
    "glossary.md".to_string()
}

fn validate_score(name: &str, value: f64) -> Result<(), String> {
    if !value.is_finite() || !(0.0..=1.0).contains(&value) {
        return Err(format!("{name} must be between 0.0 and 1.0"));
    }
    Ok(())
}

fn clamp_score(value: f64) -> f64 {
    if value.is_nan() {
        return 0.0;
    }
    value.clamp(0.0, 1.0)
}

pub fn lux_init(project_path: &Path) -> Result<PathBuf> {
    let lux_path = project_path.join(".lux");
    let spec_path = lux_path.join("spec.json");
    if spec_path.exists() {
        bail!(".lux already exists; use lux_load instead");
    }

    let domains_path = lux_path.join("domains");
    fs::create_dir_all(&domains_path)
        .with_context(|| format!("failed to create {}", domains_path.display()))?;

    for directory in ["tickets", "logs", "backups", "sessions", "builds"] {
        let path = lux_path.join(directory);
        fs::create_dir_all(&path)
            .with_context(|| format!("failed to create {}", path.display()))?;
    }

    let now = Utc::now().to_rfc3339();
    let mut spec: SpecProject = serde_json::from_str(&get_default_spec_json()?)
        .context("failed to parse default spec template")?;
    spec.project_id = Uuid::new_v4().to_string();
    spec.project_name = project_path
        .file_name()
        .and_then(|name| name.to_str())
        .unwrap_or_default()
        .to_string();
    spec.created_at = now.clone();
    spec.updated_at = now;

    let spec_json = serde_json::to_string_pretty(&spec).context("failed to serialize spec")?;
    fs::write(&spec_path, spec_json)
        .with_context(|| format!("failed to write {}", spec_path.display()))?;

    for (domain, template) in domain_templates() {
        let path = domains_path.join(format!("{domain}.md"));
        if !path.exists() {
            fs::write(&path, template)
                .with_context(|| format!("failed to write {}", path.display()))?;
        }
    }

    let glossary_path = lux_path.join("glossary.md");
    if !glossary_path.exists() {
        fs::write(&glossary_path, include_str!("templates/glossary.md"))
            .with_context(|| format!("failed to write {}", glossary_path.display()))?;
    }

    Ok(lux_path)
}

pub fn lux_load_or_init(project_path: &Path) -> Result<SpecProject> {
    if !project_path.join(".lux/spec.json").is_file() {
        lux_init(project_path)?;
    }
    lux_load(project_path)
}

pub fn lux_load(project_path: &Path) -> Result<SpecProject> {
    let spec_path = project_path.join(".lux/spec.json");
    let content = fs::read_to_string(&spec_path)
        .with_context(|| format!("failed to read {}", spec_path.display()))?;
    serde_json::from_str(&content)
        .with_context(|| format!("failed to parse {}", spec_path.display()))
}

pub fn lux_save(project_path: &Path, spec: &SpecProject) -> Result<()> {
    let lux_path = project_path.join(".lux");
    let spec_path = lux_path.join("spec.json");
    let backups_path = lux_path.join("backups");
    fs::create_dir_all(&backups_path)
        .with_context(|| format!("failed to create {}", backups_path.display()))?;

    if spec_path.exists() {
        let timestamp = Utc::now().format("%Y%m%d%H%M%S%f");
        let backup_path = backups_path.join(format!("spec-{timestamp}.json"));
        fs::copy(&spec_path, &backup_path).with_context(|| {
            format!(
                "failed to back up {} to {}",
                spec_path.display(),
                backup_path.display()
            )
        })?;
    }

    let mut updated = spec.clone();
    updated.updated_at = Utc::now().to_rfc3339();
    let spec_json = serde_json::to_string_pretty(&updated).context("failed to serialize spec")?;
    fs::write(&spec_path, spec_json)
        .with_context(|| format!("failed to write {}", spec_path.display()))
}

pub fn lux_load_domain(project_path: &Path, domain: &str) -> Result<String> {
    let path = project_path
        .join(".lux/domains")
        .join(format!("{domain}.md"));
    fs::read_to_string(&path).with_context(|| format!("failed to read {}", path.display()))
}

pub fn lux_save_domain(project_path: &Path, domain: &str, content: &str) -> Result<()> {
    let domains_path = project_path.join(".lux/domains");
    fs::create_dir_all(&domains_path)
        .with_context(|| format!("failed to create {}", domains_path.display()))?;
    let path = domains_path.join(format!("{domain}.md"));
    fs::write(&path, content).with_context(|| format!("failed to write {}", path.display()))
}

pub fn lux_update_domain_field(
    project_path: &Path,
    domain: &str,
    key: &str,
    value: Value,
) -> Result<SpecProject> {
    let mut spec = lux_load(project_path)?;
    let normalized = domain.replace('-', "_");
    let content_path = format!("{}.md", domain.replace('_', "-"));

    let domain_spec = match normalized.as_str() {
        "design" => spec
            .domains
            .design
            .get_or_insert_with(|| DomainSpec::new("design", "design.md", 1.0)),
        "architecture" => spec
            .domains
            .architecture
            .get_or_insert_with(|| DomainSpec::new("architecture", "architecture.md", 1.0)),
        "art_style" => spec
            .domains
            .art_style
            .get_or_insert_with(|| DomainSpec::new("art_style", "art-style.md", 1.0)),
        "audio" => spec
            .domains
            .audio
            .get_or_insert_with(|| DomainSpec::new("audio", "audio.md", 1.0)),
        "narrative" => spec
            .domains
            .narrative
            .get_or_insert_with(|| DomainSpec::new("narrative", "narrative.md", 1.0)),
        "levels" => spec
            .domains
            .levels
            .get_or_insert_with(|| DomainSpec::new("levels", "levels.md", 1.0)),
        "ui_ux" => spec
            .domains
            .ui_ux
            .get_or_insert_with(|| DomainSpec::new("ui_ux", "ui-ux.md", 1.0)),
        _ => spec
            .domains
            .custom
            .entry(domain.to_string())
            .or_insert_with(|| DomainSpec::new(domain, content_path, 1.0)),
    };

    domain_spec.fields.insert(key.to_string(), value);
    domain_spec.defined = true;
    lux_save(project_path, &spec)?;
    lux_load(project_path)
}

pub fn get_default_spec_json() -> Result<String> {
    Ok(include_str!("templates/spec.json").to_string())
}

pub fn render_markdown_template(
    template_name: &str,
    vars: &HashMap<String, String>,
) -> Result<String> {
    let mut rendered = match template_name {
        "design" | "design.md" => include_str!("templates/design.md").to_string(),
        "architecture" | "architecture.md" => include_str!("templates/architecture.md").to_string(),
        "art-style" | "art_style" | "art-style.md" | "art_style.md" => {
            include_str!("templates/art-style.md").to_string()
        }
        "audio" | "audio.md" => include_str!("templates/audio.md").to_string(),
        "narrative" | "narrative.md" => include_str!("templates/narrative.md").to_string(),
        "levels" | "levels.md" => include_str!("templates/levels.md").to_string(),
        "ui-ux" | "ui_ux" | "ui-ux.md" | "ui_ux.md" => {
            include_str!("templates/ui-ux.md").to_string()
        }
        "packages" | "packages.md" => include_str!("templates/packages.md").to_string(),
        "testing" | "testing.md" => include_str!("templates/testing.md").to_string(),
        _ => bail!("unknown markdown template: {template_name}"),
    };

    for (key, value) in vars {
        rendered = rendered.replace(&format!("{{{{{key}}}}}"), value);
    }

    Ok(rendered)
}

fn domain_templates() -> [(&'static str, &'static str); 9] {
    [
        ("design", include_str!("templates/design.md")),
        ("architecture", include_str!("templates/architecture.md")),
        ("art-style", include_str!("templates/art-style.md")),
        ("audio", include_str!("templates/audio.md")),
        ("narrative", include_str!("templates/narrative.md")),
        ("levels", include_str!("templates/levels.md")),
        ("ui-ux", include_str!("templates/ui-ux.md")),
        ("packages", include_str!("templates/packages.md")),
        ("testing", include_str!("templates/testing.md")),
    ]
}
