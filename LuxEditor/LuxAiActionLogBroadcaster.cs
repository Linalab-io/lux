using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Linalab.UnityAiBridge.Editor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEditor
{
    [InitializeOnLoad]
    public static class LuxAiActionLogBroadcaster
    {
        const double AttributionTtlSeconds = 2.0;
        const double SelectionDebounceSeconds = 0.25;
        const double ConsoleSummaryDebounceSeconds = 0.5;
        const int BroadcastBatchSize = 16;
        const int MaxQueuedBroadcasts = 256;
        const int MaxMetadataValueLength = 512;

        static readonly object Sync = new object();
        static readonly Queue<LuxAiActionLogEntry> PendingBroadcasts = new Queue<LuxAiActionLogEntry>();
        static readonly Dictionary<LogType, int> PendingConsoleCounts = new Dictionary<LogType, int>();
        static readonly LuxAiActionLog SharedLog = new LuxAiActionLog();

        static Func<double> timeProvider = () => EditorApplication.timeSinceStartup;
        static Action<string, object> broadcastSink = UnityAiBridgeTcpServer.BroadcastEvent;
        static LuxAiActionLog log = SharedLog;
        static LuxAiActionAttribution activeAttribution;
        static LuxAiActionAttribution propagatedAttribution;
        static double propagatedUntil;
        static double nextSelectionRecordAt = -1.0;
        static double nextConsoleSummaryAt = -1.0;
        static bool selectionDirty;

        static LuxAiActionLogBroadcaster()
        {
            EditorApplication.update += Pump;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            Application.logMessageReceived += OnLogMessageReceived;
            AssemblyReloadEvents.beforeAssemblyReload += Flush;
            EditorApplication.quitting += Flush;
        }

        public static IDisposable PushAttribution(string actor, string source, string correlationId = null)
        {
            var previous = activeAttribution;
            activeAttribution = CreateAttribution(actor, source, correlationId);
            propagatedAttribution = activeAttribution;
            propagatedUntil = Now() + AttributionTtlSeconds;
            return new AttributionScope(previous);
        }

        public static LuxAiActionLogEntry Record(
            string category,
            string action,
            string target,
            string message,
            string severity = "info",
            bool success = true,
            IReadOnlyDictionary<string, string> metadata = null)
        {
            var attribution = ResolveAttribution();
            var entry = log.Record(
                attribution.Source,
                attribution.Actor,
                category,
                action,
                target,
                message,
                severity,
                success,
                WithCorrelation(metadata, attribution.CorrelationId));
            EnqueueBroadcast(entry);
            return entry;
        }

        public static LuxAiActionLogEntry RecordDynamicCodeExecution(string code, bool success, string resultSummary, int diagnosticCount)
        {
            string safeCode = code ?? string.Empty;
            return Record(
                "dynamic-code",
                "execute_result",
                "UnityEditor",
                string.IsNullOrWhiteSpace(resultSummary) ? "Dynamic code execution completed." : resultSummary,
                success ? "info" : "error",
                success,
                new Dictionary<string, string>
                {
                    ["codeLength"] = safeCode.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["codeHash"] = Sha256(safeCode),
                    ["diagnosticCount"] = diagnosticCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
        }

        public static LuxAiActionLogEntry RecordAutomationCommandResult(string commandKind, string actor, string correlationId, bool allowed, bool success, int exitCode, string message)
        {
            using (PushAttribution(actor, "automation-command", correlationId))
            {
                return Record(
                    "automation",
                    "command_result",
                    commandKind,
                    message,
                    success ? "info" : "warning",
                    success,
                    new Dictionary<string, string>
                    {
                        ["allowed"] = allowed ? "true" : "false",
                        ["exitCode"] = exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
            }
        }

        public static LuxAiActionLogEntry RecordAIToolDispatch(string kind, string toolType, string skillName, string executionId, bool success, string message)
        {
            using (PushAttribution(string.IsNullOrWhiteSpace(toolType) ? "ai-tool" : toolType, "ai-tool-dispatcher", executionId))
            {
                return Record(
                    "ai-tool",
                    string.IsNullOrWhiteSpace(kind) ? "dispatch_result" : kind,
                    string.IsNullOrWhiteSpace(skillName) ? toolType : skillName,
                    message,
                    success ? "info" : "error",
                    success,
                    new Dictionary<string, string> { ["executionId"] = executionId ?? string.Empty });
            }
        }

        public static LuxAiActionLogEntry RecordWebRTCRemoteInput(string inputType, string sessionId)
        {
            using (PushAttribution("remote", "webrtc-remote", sessionId))
            {
                return Record(
                    "remote-input",
                    "receive",
                    string.IsNullOrWhiteSpace(inputType) ? "unknown" : inputType,
                    "WebRTC remote input event received.",
                    metadata: new Dictionary<string, string> { ["sessionId"] = sessionId ?? string.Empty });
            }
        }

        public static LuxAiActionLogEntry RecordSceneClosedUnsafeExplicit(string scenePath)
        {
            return Record(
                "scene",
                "close_explicit",
                scenePath,
                "Scene closed explicit record method used because Unity sceneClosed lacks a before-close path on older editor versions.");
        }

        public static void Flush()
        {
            log.Flush();
            PumpBroadcasts();
        }

        public static void ConfigureForTests(LuxAiActionLog testLog, Action<string, object> testBroadcastSink, Func<double> testTimeProvider)
        {
            log = testLog ?? SharedLog;
            broadcastSink = testBroadcastSink ?? UnityAiBridgeTcpServer.BroadcastEvent;
            timeProvider = testTimeProvider ?? (() => EditorApplication.timeSinceStartup);
            activeAttribution = default;
            propagatedAttribution = default;
            propagatedUntil = 0.0;
            lock (Sync)
            {
                PendingBroadcasts.Clear();
                PendingConsoleCounts.Clear();
            }
        }

        public static int PumpForTests()
        {
            return PumpBroadcasts();
        }

        static void Pump()
        {
            double now = Now();
            if (selectionDirty && now >= nextSelectionRecordAt)
            {
                selectionDirty = false;
                RecordSelectionChanged();
            }

            if (nextConsoleSummaryAt > 0.0 && now >= nextConsoleSummaryAt)
            {
                RecordConsoleSummary();
            }

            PumpBroadcasts();
        }

        static void OnSelectionChanged()
        {
            selectionDirty = true;
            nextSelectionRecordAt = Now() + SelectionDebounceSeconds;
        }

        static void RecordSelectionChanged()
        {
            Record(
                "selection",
                "changed",
                Selection.activeObject == null ? string.Empty : Selection.activeObject.name,
                "Unity selection changed.",
                metadata: new Dictionary<string, string>
                {
                    ["objectCount"] = Selection.objects == null ? "0" : Selection.objects.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["assetGuidCount"] = Selection.assetGUIDs == null ? "0" : Selection.assetGUIDs.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            Record("playmode", "state_changed", state.ToString(), "Unity playmode state changed.");
        }

        static void OnHierarchyChanged()
        {
            Record("hierarchy", "changed", "Hierarchy", "Unity hierarchy changed.");
        }

        static void OnProjectChanged()
        {
            Record("project", "changed", "Project", "Unity project assets changed.");
        }

        static void OnUndoRedoPerformed()
        {
            Record("undo-redo", "performed", "Undo", "Unity undo or redo performed.");
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            Record("scene", "opened", scene.path, "Unity scene opened.", metadata: new Dictionary<string, string> { ["mode"] = mode.ToString() });
        }

        static void OnSceneSaved(Scene scene)
        {
            Record("scene", "saved", scene.path, "Unity scene saved.");
        }

        static void OnSceneClosing(Scene scene, bool removingScene)
        {
            Record("scene", "closing", scene.path, "Unity scene closing.", metadata: new Dictionary<string, string> { ["removingScene"] = removingScene ? "true" : "false" });
        }

        static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            lock (Sync)
            {
                PendingConsoleCounts.TryGetValue(type, out int count);
                PendingConsoleCounts[type] = count + 1;
                nextConsoleSummaryAt = Now() + ConsoleSummaryDebounceSeconds;
            }
        }

        static void RecordConsoleSummary()
        {
            Dictionary<LogType, int> counts;
            lock (Sync)
            {
                if (PendingConsoleCounts.Count == 0)
                {
                    nextConsoleSummaryAt = -1.0;
                    return;
                }

                counts = new Dictionary<LogType, int>(PendingConsoleCounts);
                PendingConsoleCounts.Clear();
                nextConsoleSummaryAt = -1.0;
            }

            var metadata = new Dictionary<string, string>();
            int total = 0;
            foreach (var pair in counts)
            {
                metadata[pair.Key.ToString()] = pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                total += pair.Value;
            }

            Record("console", "summary", "Console", "Unity console messages summarized.", total > 0 && counts.ContainsKey(LogType.Error) ? "error" : "info", !counts.ContainsKey(LogType.Error), metadata);
        }

        static LuxAiActionAttribution ResolveAttribution()
        {
            if (!string.IsNullOrWhiteSpace(activeAttribution.Actor))
            {
                return activeAttribution;
            }

            if (Now() <= propagatedUntil && !string.IsNullOrWhiteSpace(propagatedAttribution.Actor))
            {
                return propagatedAttribution;
            }

            return new LuxAiActionAttribution("user", "unity-editor", string.Empty);
        }

        static LuxAiActionAttribution CreateAttribution(string actor, string source, string correlationId)
        {
            return new LuxAiActionAttribution(
                string.IsNullOrWhiteSpace(actor) ? "user" : actor,
                string.IsNullOrWhiteSpace(source) ? "unity-editor" : source,
                correlationId ?? string.Empty);
        }

        static IReadOnlyDictionary<string, string> WithCorrelation(IReadOnlyDictionary<string, string> metadata, string correlationId)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (metadata != null)
            {
                foreach (var pair in metadata)
                {
                    result[pair.Key ?? string.Empty] = Truncate(pair.Value);
                }
            }

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                result["correlationId"] = correlationId;
            }

            return result;
        }

        static void EnqueueBroadcast(LuxAiActionLogEntry entry)
        {
            lock (Sync)
            {
                while (PendingBroadcasts.Count >= MaxQueuedBroadcasts)
                {
                    PendingBroadcasts.Dequeue();
                }

                PendingBroadcasts.Enqueue(entry.Clone());
            }
        }

        static int PumpBroadcasts()
        {
            int sent = 0;
            while (sent < BroadcastBatchSize)
            {
                LuxAiActionLogEntry entry;
                lock (Sync)
                {
                    if (PendingBroadcasts.Count == 0)
                    {
                        return sent;
                    }

                    entry = PendingBroadcasts.Dequeue();
                }

                broadcastSink?.Invoke("ai_action_log", entry);
                sent++;
            }

            return sent;
        }

        static string Truncate(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= MaxMetadataValueLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, MaxMetadataValueLength) + "[TRUNCATED]";
        }

        static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (byte item in bytes)
                {
                    builder.Append(item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        static double Now()
        {
            return timeProvider == null ? EditorApplication.timeSinceStartup : timeProvider();
        }

        readonly struct LuxAiActionAttribution
        {
            public LuxAiActionAttribution(string actor, string source, string correlationId)
            {
                Actor = actor ?? string.Empty;
                Source = source ?? string.Empty;
                CorrelationId = correlationId ?? string.Empty;
            }

            public string Actor { get; }
            public string Source { get; }
            public string CorrelationId { get; }
        }

        sealed class AttributionScope : IDisposable
        {
            readonly LuxAiActionAttribution previous;
            bool disposed;

            public AttributionScope(LuxAiActionAttribution previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                activeAttribution = previous;
                disposed = true;
            }
        }
    }
}
