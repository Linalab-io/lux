using System;
using UnityEditor;
using UnityEngine;

namespace Linalab.Lux.Editor
{
    /// <summary>
    /// Batch mode compile entry point for `lux compile`.
    /// Invoked via Unity `-batchmode -executeMethod Linalab.Lux.Editor.LuxBatchCompile.Compile`.
    /// </summary>
    public static class LuxBatchCompile
    {
        const string ResultsPath = "TestResults/CompileResult.json";

        [MenuItem("Tools/Linalab/Lux/Batch/Compile (Dry Run)")]
        public static void Compile()
        {
            bool success = false;
            int errorCount = 0;
            string message = string.Empty;

            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                success = !EditorUtility.scriptCompilationFailed;
                if (!success)
                {
                    message = "Script compilation failed. Check Unity console for errors.";
                }
                else
                {
                    message = "Compilation succeeded.";
                }
            }
            catch (Exception e)
            {
                success = false;
                message = $"Compilation threw an exception: {e.Message}";
            }

            WriteResult(success, errorCount, message);

            if (!success)
            {
                EditorApplication.Exit(1);
            }
        }

        static void WriteResult(bool success, int errorCount, string message)
        {
            string projectRoot = LuxBridgeSettings.GetProjectRoot();
            string resultsDir = System.IO.Path.Combine(projectRoot, "TestResults");
            System.IO.Directory.CreateDirectory(resultsDir);

            string resultsPath = System.IO.Path.Combine(projectRoot, ResultsPath);
            string json = "{\n"
                + $"  \"ok\": {success.ToString().ToLowerInvariant()},\n"
                + $"  \"error_count\": {errorCount},\n"
                + $"  \"message\": \"{Escape(message)}\",\n"
                + $"  \"timestamp_utc\": \"{DateTime.UtcNow:O}\"\n"
                + "}\n";

            System.IO.File.WriteAllText(resultsPath, json);
            Debug.Log($"Lux batch compile result written to {resultsPath}: {(success ? "OK" : "FAILED")}");
        }

        static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }
    }
}
