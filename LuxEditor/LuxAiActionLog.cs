using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Linalab.Lux.Editor;
using UnityEngine;

namespace UnityEditor
{
    [Serializable]
    public sealed class LuxAiActionLogEntry
    {
        public int schemaVersion;
        public string protocol;
        public string id;
        public string timestampUtc;
        public string source;
        public string actor;
        public string category;
        public string action;
        public string target;
        public string message;
        public string severity;
        public bool success;
        public Dictionary<string, string> metadata;

        public LuxAiActionLogEntry()
        {
            schemaVersion = LuxAiActionLog.SchemaVersion;
            protocol = LuxAiActionLog.Protocol;
            id = Guid.NewGuid().ToString("N");
            timestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            source = string.Empty;
            actor = string.Empty;
            category = string.Empty;
            action = string.Empty;
            target = string.Empty;
            message = string.Empty;
            severity = "info";
            success = true;
            metadata = new Dictionary<string, string>();
        }

        public LuxAiActionLogEntry Clone()
        {
            return new LuxAiActionLogEntry
            {
                schemaVersion = schemaVersion,
                protocol = protocol,
                id = id,
                timestampUtc = timestampUtc,
                source = source,
                actor = actor,
                category = category,
                action = action,
                target = target,
                message = message,
                severity = severity,
                success = success,
                metadata = metadata == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(metadata)
            };
        }
    }

    public sealed class LuxAiActionLogContext
    {
        public LuxAiActionLogContext(
            IReadOnlyList<LuxAiActionLogEntry> entries,
            IReadOnlyDictionary<string, int> categoryCounts,
            IReadOnlyDictionary<string, int> actorCounts)
        {
            Entries = entries;
            CategoryCounts = categoryCounts;
            ActorCounts = actorCounts;
        }

        public IReadOnlyList<LuxAiActionLogEntry> Entries { get; }
        public IReadOnlyDictionary<string, int> CategoryCounts { get; }
        public IReadOnlyDictionary<string, int> ActorCounts { get; }
    }

    public readonly struct LuxAiActionLogCompactResult
    {
        public LuxAiActionLogCompactResult(string path, int originalLineCount, int validLineCount, int writtenLineCount)
        {
            Path = path ?? string.Empty;
            OriginalLineCount = originalLineCount;
            ValidLineCount = validLineCount;
            WrittenLineCount = writtenLineCount;
        }

        public string Path { get; }
        public int OriginalLineCount { get; }
        public int ValidLineCount { get; }
        public int WrittenLineCount { get; }
        public bool Compacted => WrittenLineCount != OriginalLineCount || ValidLineCount != OriginalLineCount;
    }

    public sealed class LuxAiActionLog : IDisposable
    {
        public const int SchemaVersion = 1;
        public const string Protocol = "lux.ai.action_log.v1";

        const int DefaultRecentCapacity = 200;
        const int DefaultMaxTextLength = 4096;
        const string LuxDirectoryName = ".lux";
        const string LogFileName = "ai-action-log.jsonl";
        const string LegacyUserSettingsDirectoryName = "UserSettings";
        const string LegacyLogFileName = "LuxAiActionLog.jsonl";
        const string Redacted = "[REDACTED]";
        const string Truncated = "[TRUNCATED]";

        static readonly Regex PrivateKeyBlockPattern = new Regex(
            "-----BEGIN [A-Z ]*PRIVATE KEY-----[\\s\\S]*?-----END [A-Z ]*PRIVATE KEY-----",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex AuthorizationPattern = new Regex(
            "(?i)(authorization\\s*[:=]\\s*)(bearer\\s+)?[^\\s,;\\\"'}]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex BearerPattern = new Regex(
            "(?i)\\bbearer\\s+[^\\s,;\\\"'}]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex SecretTokenPattern = new Regex(
            "(?i)secret-token",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex SensitiveAssignmentPattern = new Regex(
            "(?i)\\b(token|api[_-]?key|password|passwd|pwd|secret)\\b\\s*[:=]\\s*[^\\s,;\\\"'}]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex SensitiveKeyPattern = new Regex(
            "(?i)(token|authorization|api[_-]?key|password|passwd|pwd|secret|private[_-]?key)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        readonly object _sync = new object();
        readonly Queue<LuxAiActionLogEntry> _recent;
        readonly ConcurrentQueue<string> _pendingLines;
        readonly AutoResetEvent _writeSignal;
        readonly string _logPath;
        readonly int _recentCapacity;
        readonly int _maxTextLength;
        readonly Thread _writerThread;
        bool _disposed;

        public LuxAiActionLog(int recentCapacity = DefaultRecentCapacity, string logPath = null, int maxTextLength = DefaultMaxTextLength)
        {
            _recentCapacity = Math.Max(1, recentCapacity);
            _maxTextLength = Math.Max(128, maxTextLength);
            _logPath = string.IsNullOrWhiteSpace(logPath) ? GetLogPath() : Path.GetFullPath(logPath);
            if (string.IsNullOrWhiteSpace(logPath))
            {
                MigrateLegacyLogIfNeeded(_logPath);
            }
            _recent = new Queue<LuxAiActionLogEntry>(_recentCapacity);
            _pendingLines = new ConcurrentQueue<string>();
            _writeSignal = new AutoResetEvent(false);
            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "LuxAiActionLogWriter"
            };
            _writerThread.Start();
        }

        public static string GetLogPath()
        {
            return Path.GetFullPath(Path.Combine(LuxBridgeSettings.GetProjectRoot(), LuxDirectoryName, LogFileName));
        }

        static void MigrateLegacyLogIfNeeded(string logPath)
        {
            if (File.Exists(logPath))
            {
                return;
            }

            string legacyPath = Path.Combine(
                LuxBridgeSettings.GetProjectRoot(),
                LegacyUserSettingsDirectoryName,
                LegacyLogFileName);
            if (!File.Exists(legacyPath))
            {
                return;
            }

            string directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Move(legacyPath, logPath);
        }

        public LuxAiActionLogEntry Record(
            string source,
            string actor,
            string category,
            string action,
            string target,
            string message,
            string severity = "info",
            bool success = true,
            IReadOnlyDictionary<string, string> metadata = null)
        {
            ThrowIfDisposed();

            var entry = new LuxAiActionLogEntry
            {
                schemaVersion = SchemaVersion,
                protocol = Protocol,
                id = Guid.NewGuid().ToString("N"),
                timestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                source = RedactText(source),
                actor = RedactText(actor),
                category = RedactText(category),
                action = RedactText(action),
                target = RedactText(target),
                message = RedactText(message),
                severity = NormalizeSeverity(severity),
                success = success,
                metadata = RedactMetadata(metadata)
            };

            lock (_sync)
            {
                while (_recent.Count >= _recentCapacity)
                {
                    _recent.Dequeue();
                }

                _recent.Enqueue(entry.Clone());
            }

            _pendingLines.Enqueue(ToJson(entry));
            _writeSignal.Set();
            return entry.Clone();
        }

        public IReadOnlyList<LuxAiActionLogEntry> Recent(int maxEntries = 0)
        {
            lock (_sync)
            {
                var entries = _recent.Select(entry => entry.Clone()).ToList();
                if (maxEntries > 0 && entries.Count > maxEntries)
                {
                    entries = entries.Skip(entries.Count - maxEntries).ToList();
                }

                return entries;
            }
        }

        public LuxAiActionLogContext Context(int maxEntries = 0)
        {
            var entries = Recent(maxEntries)
                .OrderBy(entry => ParseTimestamp(entry.timestampUtc))
                .ThenBy(entry => entry.id, StringComparer.Ordinal)
                .ToList();
            var categoryCounts = entries
                .GroupBy(entry => string.IsNullOrWhiteSpace(entry.category) ? "uncategorized" : entry.category)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var actorCounts = entries
                .GroupBy(entry => string.IsNullOrWhiteSpace(entry.actor) ? "unknown" : entry.actor)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            return new LuxAiActionLogContext(entries, categoryCounts, actorCounts);
        }

        public LuxAiActionLogCompactResult Compact(int maxLines = 1000)
        {
            ThrowIfDisposed();
            Flush();

            int normalizedMaxLines = Math.Max(1, maxLines);
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath));

            lock (_sync)
            {
                string[] originalLines = File.Exists(_logPath) ? File.ReadAllLines(_logPath) : Array.Empty<string>();
                var validLines = originalLines
                    .Where(IsValidJsonLine)
                    .ToList();
                var keptLines = validLines
                    .Skip(Math.Max(0, validLines.Count - normalizedMaxLines))
                    .ToArray();

                string tempPath = _logPath + ".tmp";
                File.WriteAllLines(tempPath, keptLines, Encoding.UTF8);
                if (File.Exists(_logPath))
                {
                    File.Delete(_logPath);
                }

                File.Move(tempPath, _logPath);
                return new LuxAiActionLogCompactResult(_logPath, originalLines.Length, validLines.Count, keptLines.Length);
            }
        }

        public void Flush()
        {
            ThrowIfDisposed();
            DrainPendingLines();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            DrainPendingLines();
            _disposed = true;
            _writeSignal.Set();
            _writerThread.Join(500);
            _writeSignal.Dispose();
        }

        void WriterLoop()
        {
            while (!_disposed)
            {
                _writeSignal.WaitOne(250);
                DrainPendingLines();
            }
        }

        void DrainPendingLines()
        {
            if (_pendingLines.IsEmpty)
            {
                return;
            }

            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
                using (var writer = new StreamWriter(_logPath, true, Encoding.UTF8))
                {
                    while (_pendingLines.TryDequeue(out string line))
                    {
                        writer.WriteLine(line);
                    }
                }
            }
        }

        Dictionary<string, string> RedactMetadata(IReadOnlyDictionary<string, string> metadata)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (metadata == null)
            {
                return result;
            }

            foreach (var pair in metadata)
            {
                string key = RedactText(pair.Key);
                result[key] = SensitiveKeyPattern.IsMatch(pair.Key ?? string.Empty) ? Redacted : RedactText(pair.Value);
            }

            return result;
        }

        string RedactText(string value)
        {
            string redacted = value ?? string.Empty;
            redacted = PrivateKeyBlockPattern.Replace(redacted, Redacted);
            redacted = AuthorizationPattern.Replace(redacted, match => match.Groups[1].Value + Redacted);
            redacted = BearerPattern.Replace(redacted, Redacted);
            redacted = SecretTokenPattern.Replace(redacted, Redacted);
            redacted = SensitiveAssignmentPattern.Replace(redacted, match => match.Groups[1].Value + "=" + Redacted);

            if (redacted.Length <= _maxTextLength)
            {
                return redacted;
            }

            return redacted.Substring(0, _maxTextLength) + Truncated;
        }

        static string NormalizeSeverity(string severity)
        {
            string normalized = (severity ?? string.Empty).Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(normalized) ? "info" : normalized;
        }

        static DateTime ParseTimestamp(string timestampUtc)
        {
            return DateTime.TryParse(timestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime parsed)
                ? parsed.ToUniversalTime()
                : DateTime.MinValue;
        }

        static bool IsValidJsonLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string trimmed = line.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                var parsed = JsonUtility.FromJson<LuxAiActionLogEntrySurrogate>(trimmed);
                return parsed != null
                    && parsed.schemaVersion > 0
                    && !string.IsNullOrEmpty(parsed.protocol)
                    && !string.IsNullOrEmpty(parsed.id)
                    && !string.IsNullOrEmpty(parsed.timestampUtc);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        static string ToJson(LuxAiActionLogEntry entry)
        {
            var builder = new StringBuilder(512);
            builder.Append('{');
            AppendJsonProperty(builder, "schemaVersion", entry.schemaVersion, true);
            AppendJsonProperty(builder, "protocol", entry.protocol, false);
            AppendJsonProperty(builder, "id", entry.id, false);
            AppendJsonProperty(builder, "timestampUtc", entry.timestampUtc, false);
            AppendJsonProperty(builder, "source", entry.source, false);
            AppendJsonProperty(builder, "actor", entry.actor, false);
            AppendJsonProperty(builder, "category", entry.category, false);
            AppendJsonProperty(builder, "action", entry.action, false);
            AppendJsonProperty(builder, "target", entry.target, false);
            AppendJsonProperty(builder, "message", entry.message, false);
            AppendJsonProperty(builder, "severity", entry.severity, false);
            AppendJsonProperty(builder, "success", entry.success, false);
            builder.Append(",\"metadata\":");
            AppendMetadata(builder, entry.metadata);
            builder.Append('}');
            return builder.ToString();
        }

        static void AppendJsonProperty(StringBuilder builder, string name, string value, bool first)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(name).Append("\":\"").Append(EscapeJson(value)).Append('"');
        }

        static void AppendJsonProperty(StringBuilder builder, string name, int value, bool first)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
        }

        static void AppendJsonProperty(StringBuilder builder, string name, bool value, bool first)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(name).Append("\":").Append(value ? "true" : "false");
        }

        static void AppendMetadata(StringBuilder builder, IReadOnlyDictionary<string, string> metadata)
        {
            builder.Append('{');
            bool first = true;
            if (metadata != null)
            {
                foreach (var pair in metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    builder.Append('"').Append(EscapeJson(pair.Key)).Append("\":\"").Append(EscapeJson(pair.Value)).Append('"');
                    first = false;
                }
            }

            builder.Append('}');
        }

        static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 16);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(character))
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LuxAiActionLog));
            }
        }

        [Serializable]
        sealed class LuxAiActionLogEntrySurrogate
        {
            public int schemaVersion;
            public string protocol;
            public string id;
            public string timestampUtc;
        }
    }
}
