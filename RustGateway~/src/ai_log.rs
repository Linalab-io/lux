use std::{
    fs::{self, File},
    io::{BufRead, BufReader, Write},
    path::{Path, PathBuf},
};

use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};

const LOG_RELATIVE_PATH: [&str; 2] = [".lux", "ai-action-log.jsonl"];
const LEGACY_LOG_RELATIVE_PATH: [&str; 2] = ["UserSettings", "LuxAiActionLog.jsonl"];

#[derive(Clone, Debug, Default, Eq, PartialEq)]
pub struct AiLogFilter {
    pub limit: Option<usize>,
    pub actor: Option<String>,
    pub category: Option<String>,
    pub source: Option<String>,
    pub action: Option<String>,
    pub event_type: Option<String>,
}

#[derive(Clone, Debug, Deserialize, PartialEq, Serialize)]
pub struct AiLogEntry {
    pub line_number: usize,
    pub timestamp: String,
    pub value: Value,
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AiLogCompactResult {
    pub path: PathBuf,
    pub valid_before: usize,
    pub valid_after: usize,
    pub invalid_dropped: usize,
    pub lines_dropped: usize,
}

pub fn resolve_log_path(project_root: impl AsRef<Path>) -> PathBuf {
    relative_path(project_root.as_ref(), &LOG_RELATIVE_PATH)
}

pub fn ensure_log_path(project_root: impl AsRef<Path>) -> Result<PathBuf> {
    let project_root = project_root.as_ref();
    let log_path = resolve_log_path(project_root);
    migrate_legacy_log_if_needed(project_root, &log_path)?;
    if let Some(parent) = log_path.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("failed to create AI log directory {}", parent.display()))?;
    }
    Ok(log_path)
}

fn migrate_legacy_log_if_needed(project_root: &Path, log_path: &Path) -> Result<()> {
    if log_path.exists() {
        return Ok(());
    }

    let legacy_path = relative_path(project_root, &LEGACY_LOG_RELATIVE_PATH);
    if !legacy_path.exists() {
        return Ok(());
    }

    if let Some(parent) = log_path.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("failed to create AI log directory {}", parent.display()))?;
    }

    fs::rename(&legacy_path, log_path).with_context(|| {
        format!(
            "failed to migrate AI log from {} to {}",
            legacy_path.display(),
            log_path.display()
        )
    })
}

fn relative_path(project_root: &Path, segments: &[&str]) -> PathBuf {
    segments
        .iter()
        .fold(project_root.to_path_buf(), |path, segment| {
            path.join(segment)
        })
}

pub fn parse_jsonl_values(reader: impl BufRead) -> Vec<Value> {
    reader
        .lines()
        .map_while(Result::ok)
        .filter_map(|line| parse_jsonl_line(&line))
        .collect()
}

pub fn read_log_entries(path: impl AsRef<Path>, filter: &AiLogFilter) -> Result<Vec<AiLogEntry>> {
    let file = File::open(path.as_ref())
        .with_context(|| format!("failed to open AI log {}", path.as_ref().display()))?;
    Ok(filter_entries(parse_entries(BufReader::new(file)), filter))
}

pub fn filter_entries(entries: Vec<AiLogEntry>, filter: &AiLogFilter) -> Vec<AiLogEntry> {
    let mut filtered: Vec<_> = entries
        .into_iter()
        .filter(|entry| matches_filter(&entry.value, filter))
        .collect();

    if let Some(limit) = filter.limit {
        if filtered.len() > limit {
            filtered = filtered.split_off(filtered.len() - limit);
        }
    }

    filtered
}

pub fn build_continuation_context(entries: &[AiLogEntry], limit: Option<usize>) -> Value {
    let mut ordered = entries.to_vec();
    ordered.sort_by(|left, right| {
        left.timestamp
            .cmp(&right.timestamp)
            .then(left.line_number.cmp(&right.line_number))
    });

    if let Some(limit) = limit {
        if ordered.len() > limit {
            ordered = ordered.split_off(ordered.len() - limit);
        }
    }

    let items: Vec<Value> = ordered
        .iter()
        .map(|entry| {
            json!({
                "timestampUtc": entry.timestamp,
                "actor": string_field(&entry.value, "actor"),
                "category": string_field(&entry.value, "category"),
                "source": string_field(&entry.value, "source"),
                "action": string_field(&entry.value, "action"),
                "eventType": event_type(&entry.value),
                "summary": compact_summary(&entry.value),
            })
        })
        .collect();

    json!({
        "count": items.len(),
        "entries": items,
    })
}

pub fn compact_log_file(path: impl AsRef<Path>, max_lines: usize) -> Result<AiLogCompactResult> {
    let path = path.as_ref();
    let file =
        File::open(path).with_context(|| format!("failed to open AI log {}", path.display()))?;
    let mut valid_lines = Vec::new();
    let mut invalid_dropped = 0usize;

    for line in BufReader::new(file).lines() {
        let line = line.with_context(|| format!("failed to read AI log {}", path.display()))?;
        if parse_jsonl_line(&line).is_some() {
            valid_lines.push(line.trim().to_string());
        } else if !line.trim().is_empty() {
            invalid_dropped += 1;
        }
    }

    let valid_before = valid_lines.len();
    let keep_from = valid_before.saturating_sub(max_lines);
    let kept_lines = &valid_lines[keep_from..];

    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)
            .with_context(|| format!("failed to create AI log directory {}", parent.display()))?;
    }

    let temp_path = path.with_extension("jsonl.tmp");
    let mut output = File::create(&temp_path)
        .with_context(|| format!("failed to write temporary AI log {}", temp_path.display()))?;
    for line in kept_lines {
        writeln!(output, "{line}")
            .with_context(|| format!("failed to write temporary AI log {}", temp_path.display()))?;
    }
    output
        .sync_all()
        .with_context(|| format!("failed to sync temporary AI log {}", temp_path.display()))?;
    drop(output);
    fs::rename(&temp_path, path).with_context(|| {
        format!(
            "failed to atomically replace AI log {} with {}",
            path.display(),
            temp_path.display()
        )
    })?;

    Ok(AiLogCompactResult {
        path: path.to_path_buf(),
        valid_before,
        valid_after: kept_lines.len(),
        invalid_dropped,
        lines_dropped: keep_from + invalid_dropped,
    })
}

pub fn redact_secrets(text: &str) -> String {
    text.split_whitespace()
        .map(|part| {
            if part.eq_ignore_ascii_case("bearer") || part.to_ascii_lowercase().contains("token=") {
                "[REDACTED]"
            } else {
                part
            }
        })
        .collect::<Vec<_>>()
        .join(" ")
}

fn parse_entries(reader: impl BufRead) -> Vec<AiLogEntry> {
    reader
        .lines()
        .enumerate()
        .filter_map(|(index, line)| {
            let value = parse_jsonl_line(&line.ok()?)?;
            Some(AiLogEntry {
                line_number: index + 1,
                timestamp: timestamp(&value).unwrap_or_default(),
                value,
            })
        })
        .collect()
}

fn parse_jsonl_line(line: &str) -> Option<Value> {
    let trimmed = line.trim();
    if trimmed.is_empty() {
        return None;
    }

    serde_json::from_str::<Value>(trimmed).ok()
}

fn matches_filter(value: &Value, filter: &AiLogFilter) -> bool {
    matches_optional(value, "actor", filter.actor.as_deref())
        && matches_optional(value, "category", filter.category.as_deref())
        && matches_optional(value, "source", filter.source.as_deref())
        && matches_optional(value, "action", filter.action.as_deref())
        && filter.event_type.as_deref().map_or(true, |expected| {
            event_type(value).as_deref() == Some(expected)
        })
}

fn matches_optional(value: &Value, field: &str, expected: Option<&str>) -> bool {
    expected.map_or(true, |expected| {
        string_field(value, field).as_deref() == Some(expected)
    })
}

fn timestamp(value: &Value) -> Option<String> {
    string_field(value, "timestampUtc").or_else(|| string_field(value, "captured_at_utc"))
}

fn event_type(value: &Value) -> Option<String> {
    string_field(value, "eventType").or_else(|| string_field(value, "event_type"))
}

fn string_field(value: &Value, field: &str) -> Option<String> {
    value.get(field)?.as_str().map(ToOwned::to_owned)
}

fn compact_summary(value: &Value) -> Option<String> {
    ["summary", "message", "description"]
        .iter()
        .find_map(|field| string_field(value, field))
        .map(|text| redact_secrets(&text))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Cursor;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn ai_log_resolves_project_local_path() {
        let path = resolve_log_path(Path::new("/project"));
        assert_eq!(path, PathBuf::from("/project/.lux/ai-action-log.jsonl"));
    }

    #[test]
    fn ai_log_migrates_legacy_log_to_lux_directory() {
        let project_root = temp_project_root("ai-log-migration");
        let legacy_dir = project_root.join("UserSettings");
        fs::create_dir_all(&legacy_dir).unwrap();
        let legacy_path = legacy_dir.join("LuxAiActionLog.jsonl");
        fs::write(&legacy_path, "{\"actor\":\"legacy\"}\n").unwrap();

        let log_path = ensure_log_path(&project_root).unwrap();

        assert_eq!(log_path, project_root.join(".lux/ai-action-log.jsonl"));
        assert_eq!(
            fs::read_to_string(&log_path).unwrap(),
            "{\"actor\":\"legacy\"}\n"
        );
        assert!(!legacy_path.exists());
        fs::remove_dir_all(project_root).unwrap();
    }

    #[test]
    fn ai_log_jsonl_parsing_ignores_invalid_and_blank_lines() {
        let input =
            Cursor::new("\n{\"actor\":\"codex\"}\nnot-json\n  \n{\"actor\":\"opencode\"}\n");
        let values = parse_jsonl_values(input);

        assert_eq!(values.len(), 2);
        assert_eq!(values[0]["actor"], "codex");
        assert_eq!(values[1]["actor"], "opencode");
    }

    #[test]
    fn ai_log_filter_applies_actor_category_event_type_and_tail_limit() {
        let entries = parse_entries(Cursor::new(
            "{\"timestampUtc\":\"2026-05-04T00:00:00Z\",\"actor\":\"codex\",\"category\":\"tool\",\"eventType\":\"start\"}\n\
             {\"timestampUtc\":\"2026-05-04T00:00:01Z\",\"actor\":\"codex\",\"category\":\"ai-action-log\",\"eventType\":\"append\"}\n\
             {\"timestampUtc\":\"2026-05-04T00:00:02Z\",\"actor\":\"opencode\",\"category\":\"ai-action-log\",\"eventType\":\"append\"}\n\
             {\"timestampUtc\":\"2026-05-04T00:00:03Z\",\"actor\":\"codex\",\"category\":\"ai-action-log\",\"eventType\":\"append\"}\n",
        ));

        let filter = AiLogFilter {
            limit: Some(1),
            actor: Some("codex".to_string()),
            category: Some("ai-action-log".to_string()),
            event_type: Some("append".to_string()),
            ..AiLogFilter::default()
        };

        let filtered = filter_entries(entries, &filter);
        assert_eq!(filtered.len(), 1);
        assert_eq!(filtered[0].timestamp, "2026-05-04T00:00:03Z");
    }

    #[test]
    fn ai_log_continuation_context_orders_by_timestamp() {
        let entries = parse_entries(Cursor::new(
            "{\"timestampUtc\":\"2026-05-04T00:00:02Z\",\"actor\":\"codex\",\"summary\":\"second\"}\n\
             {\"captured_at_utc\":\"2026-05-04T00:00:01Z\",\"actor\":\"opencode\",\"message\":\"first\"}\n",
        ));

        let context = build_continuation_context(&entries, None);
        let items = context["entries"].as_array().unwrap();
        assert_eq!(items[0]["timestampUtc"], "2026-05-04T00:00:01Z");
        assert_eq!(items[0]["summary"], "first");
        assert_eq!(items[1]["summary"], "second");
    }

    #[test]
    fn ai_log_compact_preserves_valid_jsonl_and_drops_excess_invalid_lines() {
        let path = std::env::temp_dir().join(format!(
            "lux-ai-log-test-{}.jsonl",
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        fs::write(
            &path,
            "{\"n\":1}\ninvalid\n\n{\"n\":2}\n{\"n\":3}\n{\"n\":4}\n",
        )
        .unwrap();

        let result = compact_log_file(&path, 2).unwrap();
        let compacted = fs::read_to_string(&path).unwrap();

        assert_eq!(result.valid_before, 4);
        assert_eq!(result.valid_after, 2);
        assert_eq!(result.invalid_dropped, 1);
        assert_eq!(result.lines_dropped, 3);
        assert_eq!(compacted, "{\"n\":3}\n{\"n\":4}\n");

        let _ = fs::remove_file(path);
    }

    #[test]
    fn ai_log_redacts_bearer_and_token_values_in_summaries() {
        assert_eq!(
            redact_secrets("Authorization: Bearer abc token=secret keep"),
            "Authorization: [REDACTED] abc [REDACTED] keep"
        );
    }

    fn temp_project_root(prefix: &str) -> PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let root = std::env::temp_dir().join(format!("lux-{prefix}-{nanos}"));
        fs::create_dir_all(&root).unwrap();
        root
    }
}
