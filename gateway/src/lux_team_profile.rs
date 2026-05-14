//! Team profile schema — defines default team composition for lux run.
//! Stored at .lux/team-profile.json as canonical config.
//! Team-mode runtime state is derived from this, never canonical.

use std::fs;
use std::path::Path;

use anyhow::{bail, Context, Result};
use clap::ValueEnum;
use serde::{Deserialize, Serialize};

pub const TEAM_PROFILE_SCHEMA_VERSION: u32 = 1;

/// Canonical team profile — the ONLY place team composition config lives.
/// Team-mode registry stores a capability snapshot derived from this file.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TeamProfile {
    pub version: u32,
    pub default_team_size: u8,
    pub role_mappings: Vec<RoleMapping>,
    pub skill_bindings: Vec<SkillBinding>,
    pub verification_tiers: Vec<DomainVerificationTier>,
}

impl Default for TeamProfile {
    fn default() -> Self {
        Self {
            version: TEAM_PROFILE_SCHEMA_VERSION,
            default_team_size: 3,
            role_mappings: vec![
                RoleMapping {
                    role: "lead".to_string(),
                    category: "unspecified-high".to_string(),
                    skills: vec!["coordination".to_string(), "review".to_string()],
                },
                RoleMapping {
                    role: "implementer".to_string(),
                    category: "deep".to_string(),
                    skills: vec!["unity-cs".to_string(), "game-dev".to_string()],
                },
                RoleMapping {
                    role: "reviewer".to_string(),
                    category: "ultrabrain".to_string(),
                    skills: vec!["code-review".to_string(), "invariant-check".to_string()],
                },
                RoleMapping {
                    role: "verifier".to_string(),
                    category: "deep".to_string(),
                    skills: vec!["unity-test".to_string(), "bridge".to_string()],
                },
                RoleMapping {
                    role: "integrator".to_string(),
                    category: "deep".to_string(),
                    skills: vec!["unity-scene".to_string(), "asset-pipeline".to_string()],
                },
                RoleMapping {
                    role: "security".to_string(),
                    category: "oracle".to_string(),
                    skills: vec!["security-audit".to_string(), "core-invariants".to_string()],
                },
                RoleMapping {
                    role: "release".to_string(),
                    category: "unspecified-high".to_string(),
                    skills: vec![
                        "release-checklist".to_string(),
                        "regression-suite".to_string(),
                    ],
                },
            ],
            skill_bindings: vec![
                SkillBinding {
                    domain: "gameplay".to_string(),
                    skills: vec!["game-dev".to_string()],
                },
                SkillBinding {
                    domain: "ui".to_string(),
                    skills: vec!["frontend-ui-ux".to_string()],
                },
                SkillBinding {
                    domain: "architecture".to_string(),
                    skills: vec!["architecture-decision".to_string()],
                },
                SkillBinding {
                    domain: "testing".to_string(),
                    skills: vec!["test-setup".to_string(), "test-helpers".to_string()],
                },
                SkillBinding {
                    domain: "unity".to_string(),
                    skills: vec!["lux-unity".to_string(), "unity-cs-reference".to_string()],
                },
            ],
            verification_tiers: vec![
                DomainVerificationTier {
                    domain: "*".to_string(),
                    tier: VerificationTier::T1Always,
                },
                DomainVerificationTier {
                    domain: "gameplay".to_string(),
                    tier: VerificationTier::T2Bridge,
                },
                DomainVerificationTier {
                    domain: "scene".to_string(),
                    tier: VerificationTier::T2Bridge,
                },
                DomainVerificationTier {
                    domain: "integration".to_string(),
                    tier: VerificationTier::T3Gate,
                },
                DomainVerificationTier {
                    domain: "ship".to_string(),
                    tier: VerificationTier::T3Gate,
                },
            ],
        }
    }
}

/// Maps a team role to an agent category + required skills.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RoleMapping {
    pub role: String,
    pub category: String,
    pub skills: Vec<String>,
}

/// Binds a domain to recommended skills for agents working in that domain.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SkillBinding {
    pub domain: String,
    pub skills: Vec<String>,
}

/// Verification tier per domain — controls how aggressively each domain is verified.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DomainVerificationTier {
    pub domain: String,
    pub tier: VerificationTier,
}

/// Verification tier per domain — controls how aggressively each domain is verified.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub enum VerificationTier {
    /// Static analysis + compile check — always runs, cheap
    T1Always,
    /// Unit tests + Unity bridge checks — if bridge connected
    T2Bridge,
    /// T2 bridge checks plus Unity batchmode compile and scene smoke gates.
    T3Gate,
}

impl std::str::FromStr for VerificationTier {
    type Err = anyhow::Error;

    fn from_str(s: &str) -> Result<Self> {
        match s {
            "T1Always" => Ok(Self::T1Always),
            "T2Bridge" => Ok(Self::T2Bridge),
            "T3Gate" => Ok(Self::T3Gate),
            other => Err(anyhow::anyhow!("unknown VerificationTier: {}", other)),
        }
    }
}

/// Named team size presets — used by --team flag.
#[derive(Debug, Clone, Serialize, Deserialize, ValueEnum)]
pub enum TeamSizePreset {
    /// 2 agents: lead + worker sharing reviewer duty
    Small,
    /// 4 agents: lead + implementer + verifier + integrator
    Medium,
    /// 7 agents: all roles including security + release
    Full,
}

impl std::fmt::Display for TeamSizePreset {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(match self {
            Self::Small => "small",
            Self::Medium => "medium",
            Self::Full => "full",
        })
    }
}

impl TeamSizePreset {
    /// Returns the number of agents and which roles to include.
    pub fn agent_count(&self) -> u8 {
        match self {
            Self::Small => 2,
            Self::Medium => 4,
            Self::Full => 7,
        }
    }

    /// Returns role names for this preset (used to filter role_mappings).
    pub fn role_names(&self) -> &'static [&'static str] {
        match self {
            Self::Small => &["lead", "implementer"],
            Self::Medium => &["lead", "implementer", "verifier", "integrator"],
            Self::Full => &[
                "lead",
                "implementer",
                "verifier",
                "integrator",
                "security",
                "release",
            ],
        }
    }
}

impl TeamProfile {
    /// Load team profile from .lux/team-profile.json.
    pub fn load(lux_dir: &Path) -> Result<Self> {
        let path = lux_dir.join("team-profile.json");
        if !path.exists() {
            bail!(
                "team-profile.json not found at {}. Run 'lux init' with --team-profile to generate it.",
                path.display()
            );
        }
        let content = fs::read_to_string(&path)
            .with_context(|| format!("failed to read team-profile {}", path.display()))?;
        let profile: TeamProfile = serde_json::from_str(&content)
            .with_context(|| format!("failed to parse team-profile {}", path.display()))?;
        if profile.version > TEAM_PROFILE_SCHEMA_VERSION {
            bail!(
                "team-profile schema version {} is newer than supported {}",
                profile.version,
                TEAM_PROFILE_SCHEMA_VERSION
            );
        }
        Ok(profile)
    }

    /// Save team profile to .lux/team-profile.json (atomic write via lux_io).
    pub fn save(&self, lux_dir: &Path) -> Result<()> {
        let path = lux_dir.join("team-profile.json");
        crate::lux_io::atomic_write_json(&path, self)?;
        Ok(())
    }

    /// Get role mappings for a given team size preset.
    pub fn roles_for_preset(&self, preset: &TeamSizePreset) -> Vec<&RoleMapping> {
        let names = preset.role_names();
        self.role_mappings
            .iter()
            .filter(|r| names.contains(&r.role.as_str()))
            .collect()
    }

    /// Get verification tier for a specific domain.
    pub fn verification_tier_for_domain(&self, domain: &str) -> VerificationTier {
        self.verification_tiers
            .iter()
            .find(|t| t.domain == domain)
            .or_else(|| self.verification_tiers.iter().find(|t| t.domain == "*"))
            .map(|t| t.tier.clone())
            .unwrap_or(VerificationTier::T1Always)
    }
}

/// Generate default team-profile.json content for a game-dev project.
pub fn default_game_dev_profile() -> TeamProfile {
    TeamProfile::default()
}
