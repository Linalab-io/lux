using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Linalab.Lux.Editor
{
    public readonly struct LuxRustCliStatus
    {
        public LuxRustCliStatus(bool cargoAvailable, bool cliInstalled, string cliVersion, string cargoPath, string cliPath)
        {
            CargoAvailable = cargoAvailable;
            CliInstalled = cliInstalled;
            CliVersion = cliVersion ?? string.Empty;
            CargoPath = cargoPath ?? string.Empty;
            CliPath = cliPath ?? string.Empty;
        }

        public bool CargoAvailable { get; }
        public bool CliInstalled { get; }
        public string CliVersion { get; }
        public string CargoPath { get; }
        public string CliPath { get; }
    }

    public readonly struct LuxRustCliInstallResult
    {
        public LuxRustCliInstallResult(bool success, int exitCode, string output, string error, string message)
        {
            Success = success;
            ExitCode = exitCode;
            Output = output ?? string.Empty;
            Error = error ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public int ExitCode { get; }
        public string Output { get; }
        public string Error { get; }
        public string Message { get; }
    }

    public static class LuxRustCliInstaller
    {
        const string ExecutableName = "lux";
        const string CargoExecutableName = "cargo";
        const string RustGatewayDirectoryName = "RustGateway~";
        const string CargoInstallTargetDirectoryName = "lux-rust-cli-target";
        const int ProcessTimeoutMilliseconds = 120000;

        [MenuItem("Tools/Linalab/Lux/Rust CLI/Install or Update Global Tool")]
        public static async void InstallOrUpdateFromMenu()
        {
            var result = await InstallOrUpdateAsync();
            EditorUtility.DisplayDialog(
                result.Success ? "Lux Rust CLI Installed" : "Lux Rust CLI Install Failed",
                result.Message,
                "OK");
        }

        [MenuItem("Tools/Linalab/Lux/Rust CLI/Copy Terminal Install Command")]
        public static void CopyInstallCommand()
        {
            string command = BuildTerminalInstallCommand();
            EditorGUIUtility.systemCopyBuffer = command;
            UnityEngine.Debug.Log($"Copied Lux Rust CLI install command: {command}");
        }

        public static LuxRustCliStatus GetStatus()
        {
            string cargoPath = FindExecutable(CargoExecutableName);
            string cliPath = FindExecutable(ExecutableName);
            string cliVersion = string.IsNullOrEmpty(cliPath)
                ? string.Empty
                : ReadVersion(cliPath);

            return new LuxRustCliStatus(
                !string.IsNullOrEmpty(cargoPath),
                !string.IsNullOrEmpty(cliVersion),
                NormalizeVersion(cliVersion),
                cargoPath,
                cliPath);
        }

        public static async Task<LuxRustCliInstallResult> InstallOrUpdateAsync()
        {
            string cargoPath = FindExecutable(CargoExecutableName);
            if (string.IsNullOrEmpty(cargoPath))
            {
                return new LuxRustCliInstallResult(
                    false,
                    -1,
                    string.Empty,
                    string.Empty,
                    "cargo was not found. Install Rust from https://rustup.rs, then reopen Unity or retry from a terminal.");
            }

            string cratePath = GetRustGatewayPath();
            if (!Directory.Exists(cratePath))
            {
                return new LuxRustCliInstallResult(
                    false,
                    -1,
                    string.Empty,
                    string.Empty,
                    $"RustGateway~ was not found at {cratePath}.");
            }

            string targetDirectory = Path.Combine(Path.GetTempPath(), CargoInstallTargetDirectoryName);
            var result = await RunProcessAsync(
                cargoPath,
                new[] { "install", "--path", cratePath, "--force", "--locked", "--target-dir", targetDirectory },
                cratePath,
                ProcessTimeoutMilliseconds);

            if (!result.Success)
            {
                string manualCommand = BuildTerminalInstallCommand();
                return new LuxRustCliInstallResult(
                    false,
                    result.ExitCode,
                    result.Output,
                    result.Error,
                    $"Failed to install or update the Lux Rust CLI.\n\n{result.Error}\n\nTry manually:\n{manualCommand}");
            }

            var status = GetStatus();
            string versionText = string.IsNullOrEmpty(status.CliVersion) ? "installed" : $"v{status.CliVersion}";
            return new LuxRustCliInstallResult(
                true,
                result.ExitCode,
                result.Output,
                result.Error,
                $"Lux Rust CLI {versionText} is installed globally.\n\nCommand: {ExecutableName}");
        }

        public static string BuildTerminalInstallCommand()
        {
            return $"cargo install --path {QuoteForShell(GetRustGatewayPath())} --force --locked";
        }

        public static string GetRustGatewayPath()
        {
            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LuxRustCliInstaller).Assembly);
            string packageRoot = packageInfo == null ? Application.dataPath : packageInfo.resolvedPath;
            return Path.Combine(packageRoot, RustGatewayDirectoryName);
        }

        static string ReadVersion(string executablePath)
        {
            var result = RunProcess(executablePath, new[] { "--version" }, Directory.GetCurrentDirectory(), 5000);
            return result.Success ? result.Output.Trim() : string.Empty;
        }

        static string NormalizeVersion(string output)
        {
            string prefix = ExecutableName + " ";
            return output.StartsWith(prefix, StringComparison.Ordinal)
                ? output.Substring(prefix.Length).Trim()
                : output.Trim();
        }

        static string FindExecutable(string executableName)
        {
            string fromPath = FindExecutableOnPath(executableName);
            if (!string.IsNullOrEmpty(fromPath))
            {
                return fromPath;
            }

            string cargoHomePath = FindCargoHomeExecutable(executableName);
            return string.IsNullOrEmpty(cargoHomePath) ? null : cargoHomePath;
        }

        static string FindExecutableOnPath(string executableName)
        {
            bool windows = Application.platform == RuntimePlatform.WindowsEditor;
            string finder = windows ? "where" : "/usr/bin/env";
            string[] args = windows ? new[] { executableName } : new[] { "which", executableName };
            var result = RunProcess(finder, args, Directory.GetCurrentDirectory(), 5000);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            {
                return null;
            }

            foreach (string line in result.Output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && File.Exists(trimmed))
                {
                    return trimmed;
                }
            }

            return null;
        }

        static string FindCargoHomeExecutable(string executableName)
        {
            string cargoHome = Environment.GetEnvironmentVariable("CARGO_HOME");
            if (string.IsNullOrEmpty(cargoHome))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home))
                {
                    return null;
                }

                cargoHome = Path.Combine(home, ".cargo");
            }

            string fileName = Application.platform == RuntimePlatform.WindowsEditor
                ? executableName + ".exe"
                : executableName;
            string candidate = Path.Combine(cargoHome, "bin", fileName);
            return File.Exists(candidate) ? candidate : null;
        }

        static async Task<LuxRustCliInstallResult> RunProcessAsync(string executable, string[] args, string workingDirectory, int timeoutMilliseconds)
        {
            return await Task.Run(() => RunProcess(executable, args, workingDirectory, timeoutMilliseconds));
        }

        static LuxRustCliInstallResult RunProcess(string executable, string[] args, string workingDirectory, int timeoutMilliseconds)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                AddCargoBinToPath(process.StartInfo);
                foreach (string arg in args ?? Array.Empty<string>())
                {
                    process.StartInfo.ArgumentList.Add(arg ?? string.Empty);
                }

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                bool exited = process.WaitForExit(timeoutMilliseconds);
                if (!exited)
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    return new LuxRustCliInstallResult(false, -1, output, error, "Process timed out.");
                }

                bool success = process.ExitCode == 0;
                return new LuxRustCliInstallResult(success, process.ExitCode, output, error, success ? "Process completed." : "Process failed.");
            }
            catch (Exception exception)
            {
                return new LuxRustCliInstallResult(false, -1, string.Empty, string.Empty, exception.Message);
            }
        }

        static void AddCargoBinToPath(ProcessStartInfo startInfo)
        {
            string cargoBin = Path.GetDirectoryName(FindCargoHomeExecutable(CargoExecutableName));
            if (string.IsNullOrEmpty(cargoBin))
            {
                return;
            }

            string separator = Application.platform == RuntimePlatform.WindowsEditor ? ";" : ":";
            string path = startInfo.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (!path.Contains(cargoBin))
            {
                startInfo.EnvironmentVariables["PATH"] = cargoBin + separator + path;
            }
        }

        static string QuoteForShell(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }
}
