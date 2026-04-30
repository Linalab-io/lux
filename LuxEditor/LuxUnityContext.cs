using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Linalab.Lux.Editor
{
    /// <summary>
    /// Tool-agnostic Unity context snapshot contract for any coding agent attached to this project.
    /// Writes UserSettings/LuxUnityContext.json so external tools can read Unity state without a private bridge.
    /// </summary>
    [InitializeOnLoad]
    public static class LuxUnityContext
    {
        public const string ContextRelativePath = "UserSettings/LuxUnityContext.json";
        public const string RefreshResultRelativePath = "TestResults/LuxUnityContextResult.json";

        const double SnapshotIntervalSeconds = 2.0d;
        const int RecentLogLimit = 50;

        static readonly Queue<LogRecord> RecentLogs = new Queue<LogRecord>(RecentLogLimit);
        static double _nextSnapshotTime;
        static bool _dirty = true;

        static LuxUnityContext()
        {
            Application.logMessageReceived += OnLogMessageReceived;
            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += MarkDirty;
            EditorApplication.playModeStateChanged += _ => MarkDirty();
            EditorSceneManagerShim.SceneChanged += MarkDirty;
            AssemblyReloadEvents.beforeAssemblyReload += WriteSnapshotQuietly;
            EditorApplication.quitting += WriteSnapshotQuietly;
        }

        [MenuItem("Tools/Linalab/Lux/Unity Context/Write Snapshot")]
        public static void WriteSnapshotFromMenu()
        {
            string path = WriteSnapshot();
            Debug.Log($"Lux Unity context snapshot written: {path}");
        }

        /// <summary>
        /// Batch mode entry point for `lux unity context --refresh`.
        /// Invoked via Unity `-executeMethod Linalab.Lux.Editor.LuxUnityContext.Refresh`.
        /// </summary>
        [MenuItem("Tools/Linalab/Lux/Unity Context/Refresh Now")]
        public static void Refresh()
        {
            bool success = false;
            string message;
            string contextPath = string.Empty;

            try
            {
                contextPath = WriteSnapshot();
                success = true;
                message = "Lux Unity context refreshed.";
            }
            catch (Exception exception)
            {
                message = exception.Message;
                Debug.LogWarning($"Lux Unity context refresh failed: {exception.Message}");
            }

            WriteRefreshResult(success, contextPath, message);
            if (!success)
            {
                EditorApplication.Exit(1);
            }
        }

        public static string WriteSnapshot()
        {
            string projectRoot = LuxBridgeSettings.GetProjectRoot();
            string userSettings = Path.Combine(projectRoot, "UserSettings");
            Directory.CreateDirectory(userSettings);

            string contextPath = Path.Combine(projectRoot, ContextRelativePath);
            File.WriteAllText(contextPath, BuildSnapshotJson(projectRoot));
            _dirty = false;
            _nextSnapshotTime = EditorApplication.timeSinceStartup + SnapshotIntervalSeconds;
            return contextPath;
        }

        public static LogRecord[] GetRecentLogsSnapshot()
        {
            return RecentLogs.ToArray();
        }

        public static ConsoleCounts GetConsoleCountsSnapshot()
        {
            return ReadConsoleCounts();
        }

        public static bool ClearConsole()
        {
            try
            {
                Type logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor");
                MethodInfo clearMethod = logEntriesType == null
                    ? null
                    : logEntriesType.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (clearMethod == null)
                {
                    return false;
                }

                clearMethod.Invoke(null, null);
                RecentLogs.Clear();
                MarkDirty();
                return true;
            }
            catch
            {
                return false;
            }
        }

        static void OnEditorUpdate()
        {
            if (!_dirty && EditorApplication.timeSinceStartup < _nextSnapshotTime)
            {
                return;
            }

            WriteSnapshotQuietly();
        }

        static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (RecentLogs.Count >= RecentLogLimit)
            {
                RecentLogs.Dequeue();
            }

            RecentLogs.Enqueue(new LogRecord
            {
                Type = type.ToString(),
                Message = condition ?? string.Empty,
                StackTrace = stackTrace ?? string.Empty,
                TimestampUtc = DateTime.UtcNow.ToString("o")
            });
            MarkDirty();
        }

        static void MarkDirty()
        {
            _dirty = true;
        }

        static void WriteSnapshotQuietly()
        {
            try
            {
                WriteSnapshot();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Lux Unity context snapshot failed: {exception.Message}");
            }
        }

        static string BuildSnapshotJson(string projectRoot)
        {
            var scene = SceneManager.GetActiveScene();
            var selected = Selection.activeObject;
            string selectedPath = selected == null ? string.Empty : AssetDatabase.GetAssetPath(selected);
            string selectedType = selected == null ? string.Empty : selected.GetType().FullName;
            string selectedName = selected == null ? string.Empty : selected.name;
            string activeGameObjectPath = Selection.activeGameObject == null
                ? string.Empty
                : GetGameObjectPath(Selection.activeGameObject);

            var consoleCounts = ReadConsoleCounts();

            var builder = new StringBuilder(4096);
            builder.Append('{');
            AppendProperty(builder, "schema_version", "1", false, false);
            AppendProperty(builder, "protocol", "lux.unity.context.v1", true, true);
            AppendProperty(builder, "generated_at_utc", DateTime.UtcNow.ToString("o"), true, true);
            AppendProperty(builder, "project_root", projectRoot, true, true);
            AppendProperty(builder, "unity_version", Application.unityVersion ?? string.Empty, true, true);
            AppendProperty(builder, "is_playing", EditorApplication.isPlaying ? "true" : "false", true, false);
            AppendProperty(builder, "is_paused", EditorApplication.isPaused ? "true" : "false", true, false);
            AppendProperty(builder, "is_compiling", EditorApplication.isCompiling ? "true" : "false", true, false);
            AppendProperty(builder, "active_scene_name", scene.name ?? string.Empty, true, true);
            AppendProperty(builder, "active_scene_path", scene.path ?? string.Empty, true, true);
            AppendProperty(builder, "selected_object_name", selectedName, true, true);
            AppendProperty(builder, "selected_object_type", selectedType, true, true);
            AppendProperty(builder, "selected_asset_path", selectedPath, true, true);
            AppendProperty(builder, "selected_game_object_path", activeGameObjectPath, true, true);
            builder.Append(",\"console\":{");
            AppendProperty(builder, "errors", consoleCounts.Errors.ToString(), false, false);
            AppendProperty(builder, "warnings", consoleCounts.Warnings.ToString(), true, false);
            AppendProperty(builder, "logs", consoleCounts.Logs.ToString(), true, false);
            builder.Append(",\"recent\":[");
            int index = 0;
            foreach (var log in RecentLogs)
            {
                if (index++ > 0)
                {
                    builder.Append(',');
                }

                builder.Append('{');
                AppendProperty(builder, "type", log.Type, false, true);
                AppendProperty(builder, "message", log.Message, true, true);
                AppendProperty(builder, "stack_trace", log.StackTrace, true, true);
                AppendProperty(builder, "timestamp_utc", log.TimestampUtc, true, true);
                builder.Append('}');
            }
            builder.Append("]}}");
            return builder.ToString();
        }

        static void WriteRefreshResult(bool success, string contextPath, string message)
        {
            string projectRoot = LuxBridgeSettings.GetProjectRoot();
            string resultPath = Path.Combine(projectRoot, RefreshResultRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath) ?? projectRoot);

            var builder = new StringBuilder(512);
            builder.Append('{');
            AppendProperty(builder, "ok", success ? "true" : "false", false, false);
            AppendProperty(builder, "context_path", contextPath, true, true);
            AppendProperty(builder, "message", message, true, true);
            AppendProperty(builder, "timestamp_utc", DateTime.UtcNow.ToString("o"), true, true);
            builder.Append("}\n");
            File.WriteAllText(resultPath, builder.ToString());
        }

        static ConsoleCounts ReadConsoleCounts()
        {
            try
            {
                Type logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor");
                MethodInfo getCounts = logEntriesType == null
                    ? null
                    : logEntriesType.GetMethod("GetCountsByType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (getCounts == null)
                {
                    return default;
                }

                object[] args = { 0, 0, 0 };
                getCounts.Invoke(null, args);
                return new ConsoleCounts
                {
                    Errors = Convert.ToInt32(args[0]),
                    Warnings = Convert.ToInt32(args[1]),
                    Logs = Convert.ToInt32(args[2])
                };
            }
            catch
            {
                return default;
            }
        }

        static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            Transform current = gameObject.transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        static void AppendProperty(StringBuilder builder, string name, string value, bool comma, bool quoteValue)
        {
            if (comma)
            {
                builder.Append(',');
            }

            builder.Append('"');
            builder.Append(Escape(name));
            builder.Append("\":");
            if (quoteValue)
            {
                builder.Append('"');
                builder.Append(Escape(value));
                builder.Append('"');
            }
            else
            {
                builder.Append(value);
            }
        }

        static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        public struct LogRecord
        {
            public string Type;
            public string Message;
            public string StackTrace;
            public string TimestampUtc;
        }

        public struct ConsoleCounts
        {
            public int Errors;
            public int Warnings;
            public int Logs;
        }

        static class EditorSceneManagerShim
        {
            public static event Action SceneChanged
            {
                add { UnityEditor.SceneManagement.EditorSceneManager.activeSceneChangedInEditMode += (_, _) => value(); }
                remove { }
            }
        }
    }
}
