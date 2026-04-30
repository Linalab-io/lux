using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Linalab.UnityGit.Editor
{
    public static class GitCommandRunner
    {
        static readonly SemaphoreSlim AsyncCommandGate = new SemaphoreSlim(1, 1);

        public static GitCommandResult Execute(string workingDirectory, params string[] arguments)
        {
            return ExecuteCore(workingDirectory, arguments);
        }

        public static GitCommandResult ExecuteSerialized(string workingDirectory, params string[] arguments)
        {
            AsyncCommandGate.Wait();
            try
            {
                return ExecuteCore(workingDirectory, arguments);
            }
            finally
            {
                AsyncCommandGate.Release();
            }
        }

        public static async Task<GitCommandResult> ExecuteSerializedAsync(string workingDirectory, params string[] arguments)
        {
            await AsyncCommandGate.WaitAsync().ConfigureAwait(false);

            try
            {
                return await Task.Run(() => ExecuteCore(workingDirectory, arguments)).ConfigureAwait(false);
            }
            finally
            {
                AsyncCommandGate.Release();
            }
        }

        static GitCommandResult ExecuteCore(string workingDirectory, string[] arguments)
        {
            var normalizedWorkingDirectory = workingDirectory ?? string.Empty;

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = normalizedWorkingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                AddArguments(process.StartInfo, arguments);

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                return new GitCommandResult(
                    process.ExitCode == 0,
                    output,
                    error,
                    process.ExitCode,
                    normalizedWorkingDirectory,
                    process.ExitCode == 0 ? string.Empty : BuildFailureMessage(error));
            }
            catch (Exception exception)
            {
                return new GitCommandResult(false, string.Empty, string.Empty, -1, normalizedWorkingDirectory, exception.Message);
            }
        }

        static void AddArguments(ProcessStartInfo startInfo, string[] arguments)
        {
            if (arguments == null)
            {
                return;
            }

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument ?? string.Empty);
            }
        }

        static string BuildFailureMessage(string standardError)
        {
            return string.IsNullOrWhiteSpace(standardError) ? "git command failed." : standardError.Trim();
        }
    }

    public readonly struct GitCommandResult
    {
        public GitCommandResult(bool success, string standardOutput, string standardError, int exitCode, string workingDirectory, string errorMessage)
        {
            Success = success;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
            ExitCode = exitCode;
            WorkingDirectory = workingDirectory ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool Success { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public int ExitCode { get; }
        public string WorkingDirectory { get; }
        public string ErrorMessage { get; }

        public string Output => StandardOutput;
    }
}
