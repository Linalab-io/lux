using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityCodexImage.Editor.IR;
using Linalab.UnityCodexImage.Editor.Pipeline;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Backends.Codex
{
    public enum CodexPreflightStatus
    {
        Installed,
        CodexUnavailable
    }

    public readonly struct CodexPreflightResult
    {
        public CodexPreflightResult(CodexPreflightStatus status, string message, string versionOutput)
        {
            Status = status;
            Message = message ?? string.Empty;
            VersionOutput = versionOutput ?? string.Empty;
        }

        public CodexPreflightStatus Status { get; }
        public string Message { get; }
        public string VersionOutput { get; }
        public bool IsInstalled => Status == CodexPreflightStatus.Installed;

        public static CodexPreflightResult Installed(string versionOutput)
        {
            return new CodexPreflightResult(CodexPreflightStatus.Installed, "Codex CLI is available.", versionOutput);
        }

        public static CodexPreflightResult Unavailable(string message)
        {
            return new CodexPreflightResult(CodexPreflightStatus.CodexUnavailable, message, string.Empty);
        }
    }

    public sealed class CodexGenerationRequest
    {
        public string prompt;
        public string outputDirectory;
        public string workingDirectory;
        public TimeSpan timeout = TimeSpan.FromMinutes(10);
        public bool allowLocalUserOverride;
    }

    public sealed class CodexGenerationResult
    {
        public bool succeeded;
        public bool cancelled;
        public bool timedOut;
        public CodexPreflightStatus preflightStatus = CodexPreflightStatus.Installed;
        public int exitCode;
        public string message;
        public string stdout;
        public string stderr;
        public string outputDirectory;
        public string[] generatedPngPaths = Array.Empty<string>();
    }

    public sealed class CodexProcessStartInfo
    {
        public string fileName;
        public string arguments;
        public string workingDirectory;
        public TimeSpan timeout;
    }

    public sealed class CodexProcessResult
    {
        public bool started = true;
        public bool cancelled;
        public bool timedOut;
        public int exitCode;
        public string stdout = string.Empty;
        public string stderr = string.Empty;
        public string startError = string.Empty;
    }

    public interface ICodexProcessRunner
    {
        Task<CodexProcessResult> RunAsync(CodexProcessStartInfo startInfo, CancellationToken cancellationToken);
    }

    public interface ICodexImageBackend
    {
        Task<CodexPreflightResult> PreflightAsync(CancellationToken cancellationToken);
        Task<CodexGenerationResult> GenerateAsync(CodexGenerationRequest request, CancellationToken cancellationToken);
    }

    public sealed class CodexCliBackend : ICodexImageBackend
    {
        private static readonly Regex PngPathPattern = new Regex("(?<path>(?:[A-Za-z]:)?[^\\s\\\"'<>|]+\\.png)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly ICodexProcessRunner processRunner;

        public CodexCliBackend(ICodexProcessRunner processRunner = null)
        {
            this.processRunner = processRunner ?? new SystemDiagnosticsCodexProcessRunner();
        }

        public async Task<CodexPreflightResult> PreflightAsync(CancellationToken cancellationToken)
        {
            var result = await processRunner.RunAsync(new CodexProcessStartInfo
            {
                fileName = "codex",
                arguments = "--version",
                workingDirectory = Directory.GetCurrentDirectory(),
                timeout = TimeSpan.FromSeconds(10)
            }, cancellationToken);

            if (!result.started)
            {
                return CodexPreflightResult.Unavailable("Codex CLI was not found on PATH. Install Codex CLI and ensure the 'codex' executable is available to Unity.");
            }

            if (result.cancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return CodexPreflightResult.Unavailable("Codex CLI preflight was cancelled.");
            }

            if (result.timedOut)
            {
                return CodexPreflightResult.Unavailable("Codex CLI preflight timed out.");
            }

            if (result.exitCode != 0)
            {
                var details = FirstNonEmpty(result.stderr, result.stdout, $"exit code {result.exitCode}");
                return CodexPreflightResult.Unavailable("Codex CLI preflight failed: " + details.Trim());
            }

            return CodexPreflightResult.Installed(result.stdout.Trim());
        }

        public async Task<CodexGenerationResult> GenerateAsync(CodexGenerationRequest request, CancellationToken cancellationToken)
        {
            ValidateGenerationRequest(request);
            var workingDirectory = string.IsNullOrWhiteSpace(request.workingDirectory) ? Directory.GetCurrentDirectory() : request.workingDirectory;
            var outputDirectory = CodexOutputDirectoryValidator.Normalize(request.outputDirectory, workingDirectory, request.allowLocalUserOverride);
            Directory.CreateDirectory(outputDirectory.absolutePath);

            var preflight = await PreflightAsync(cancellationToken);
            if (!preflight.IsInstalled)
            {
                return new CodexGenerationResult
                {
                    succeeded = false,
                    preflightStatus = preflight.Status,
                    message = preflight.Message,
                    outputDirectory = outputDirectory.projectOrLocalPath
                };
            }

            var process = await processRunner.RunAsync(new CodexProcessStartInfo
            {
                fileName = "codex",
                arguments = $"exec {Quote(request.prompt)} -s workspace-write --skip-git-repo-check",
                workingDirectory = workingDirectory,
                timeout = request.timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : request.timeout
            }, cancellationToken);

            var generatedPaths = process.exitCode == 0 && !process.cancelled && !process.timedOut
                ? ResolveGeneratedPngPaths(process.stdout, outputDirectory.absolutePath, workingDirectory)
                : Array.Empty<string>();

            return new CodexGenerationResult
            {
                succeeded = process.exitCode == 0 && !process.cancelled && !process.timedOut && generatedPaths.Length > 0,
                cancelled = process.cancelled,
                timedOut = process.timedOut,
                preflightStatus = process.started ? CodexPreflightStatus.Installed : CodexPreflightStatus.CodexUnavailable,
                exitCode = process.exitCode,
                message = BuildGenerationMessage(process, generatedPaths.Length),
                stdout = process.stdout,
                stderr = process.stderr,
                outputDirectory = outputDirectory.projectOrLocalPath,
                generatedPngPaths = generatedPaths.Select(path => ToProjectRelativeOrNormalized(path, workingDirectory)).ToArray()
            };
        }

        private static void ValidateGenerationRequest(CodexGenerationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.prompt))
            {
                throw new ArgumentException("Prompt is required.", nameof(request));
            }
        }

        private static string[] ResolveGeneratedPngPaths(string stdout, string outputDirectory, string workingDirectory)
        {
            var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in PngPathPattern.Matches(stdout ?? string.Empty))
            {
                var rawPath = match.Groups["path"].Value.Trim().Trim('`', '*', '-', ' ');
                var absolutePath = Path.IsPathRooted(rawPath) ? rawPath : Path.GetFullPath(Path.Combine(workingDirectory, rawPath));
                if (IsUnderDirectory(absolutePath, outputDirectory) && IsPngFile(absolutePath))
                {
                    paths.Add(Path.GetFullPath(absolutePath));
                }
            }

            if (paths.Count == 0 && Directory.Exists(outputDirectory))
            {
                foreach (var path in Directory.GetFiles(outputDirectory, "*.png", SearchOption.TopDirectoryOnly))
                {
                    if (IsPngFile(path))
                    {
                        paths.Add(Path.GetFullPath(path));
                    }
                }
            }

            return paths.ToArray();
        }

        private static bool IsPngFile(string path)
        {
            if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var signature = new byte[8];
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length < signature.Length || stream.Read(signature, 0, signature.Length) != signature.Length)
                {
                    return false;
                }
            }

            return signature[0] == 0x89 && signature[1] == 0x50 && signature[2] == 0x4E && signature[3] == 0x47
                && signature[4] == 0x0D && signature[5] == 0x0A && signature[6] == 0x1A && signature[7] == 0x0A;
        }

        private static string BuildGenerationMessage(CodexProcessResult process, int generatedPathCount)
        {
            if (!process.started)
            {
                return "Codex CLI was not found on PATH. Install Codex CLI and ensure the 'codex' executable is available to Unity.";
            }

            if (process.cancelled)
            {
                return "Codex image generation was cancelled.";
            }

            if (process.timedOut)
            {
                return "Codex image generation timed out.";
            }

            if (process.exitCode != 0)
            {
                return "Codex image generation failed: " + FirstNonEmpty(process.stderr, process.stdout, $"exit code {process.exitCode}").Trim();
            }

            return generatedPathCount == 0
                ? "Codex completed but no generated PNG files were found in the output directory."
                : $"Codex generated {generatedPathCount} PNG file(s).";
        }

        private static bool IsUnderDirectory(string path, string directory)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToProjectRelativeOrNormalized(string path, string workingDirectory)
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(root.Length).Replace('\\', '/');
            }

            return fullPath.Replace('\\', '/');
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }

    public static class CodexOutputDirectoryValidator
    {
        public static (string projectOrLocalPath, string absolutePath) Normalize(string path, string projectRoot, bool allowLocalUserOverride)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Output directory is required.", nameof(path));
            }

            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                projectRoot = Directory.GetCurrentDirectory();
            }

            projectRoot = Path.GetFullPath(projectRoot);

            if (allowLocalUserOverride && Path.IsPathRooted(path))
            {
                return (path.Replace('\\', '/'), Path.GetFullPath(path));
            }

            var normalized = PipelineOutputDirectoryPolicy.NormalizeProjectRelativePath(path, allowLocalUserOverride);
            var absolutePath = Path.GetFullPath(Path.Combine(projectRoot, normalized));
            return (normalized, absolutePath);
        }
    }

    public sealed class SystemDiagnosticsCodexProcessRunner : ICodexProcessRunner
    {
        public async Task<CodexProcessResult> RunAsync(CodexProcessStartInfo startInfo, CancellationToken cancellationToken)
        {
            if (startInfo == null)
            {
                throw new ArgumentNullException(nameof(startInfo));
            }

            using (var process = new Process())
            using (var timeout = CreateTimeout(startInfo.timeout))
            using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                var exited = new TaskCompletionSource<int>();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = startInfo.fileName,
                    Arguments = startInfo.arguments,
                    WorkingDirectory = string.IsNullOrWhiteSpace(startInfo.workingDirectory) ? Directory.GetCurrentDirectory() : startInfo.workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process.EnableRaisingEvents = true;
                process.Exited += (_, __) => exited.TrySetResult(process.ExitCode);

                try
                {
                    process.Start();
                }
                catch (Exception exception) when (exception is Win32Exception || exception is FileNotFoundException || exception is IOException)
                {
                    return new CodexProcessResult
                    {
                        started = false,
                        exitCode = 127,
                        startError = exception.Message,
                        stderr = exception.Message
                    };
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                using (linkedCancellation.Token.Register(() => exited.TrySetCanceled()))
                {
                    try
                    {
                        var exitCode = await exited.Task;
                        var output = await outputTask;
                        var error = await errorTask;
                        return new CodexProcessResult
                        {
                            exitCode = exitCode,
                            stdout = output,
                            stderr = error
                        };
                    }
                    catch (TaskCanceledException)
                    {
                        TryKill(process);
                        var output = await ReadCompletedOrEmpty(outputTask);
                        var error = await ReadCompletedOrEmpty(errorTask);
                        return new CodexProcessResult
                        {
                            cancelled = cancellationToken.IsCancellationRequested,
                            timedOut = !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested,
                            exitCode = -1,
                            stdout = output,
                            stderr = error
                        };
                    }
                }
            }
        }

        private static CancellationTokenSource CreateTimeout(TimeSpan timeout)
        {
            return timeout <= TimeSpan.Zero ? new CancellationTokenSource() : new CancellationTokenSource(timeout);
        }

        private static async Task<string> ReadCompletedOrEmpty(Task<string> task)
        {
            try
            {
                return await task;
            }
            catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException || exception is InvalidOperationException)
            {
                return string.Empty;
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    public sealed class CodexGenerationNodeExecutor : IPipelineNodeExecutor
    {
        private readonly ICodexImageBackend backend;

        public CodexGenerationNodeExecutor(ICodexImageBackend backend = null)
        {
            this.backend = backend ?? new CodexCliBackend();
        }

        public string NodeType => CodexImagePipelineNodeTypes.CodexGeneration;

        public async Task<PipelineNodeExecutionResult> ExecuteAsync(PipelineNode node, PipelineExecutionContext context, CancellationToken cancellationToken)
        {
            var promptArtifact = LatestArtifact(context, CodexImagePipelineArtifactKinds.Prompt);
            var outputArtifact = LatestArtifact(context, CodexImagePipelineArtifactKinds.OutputDirectory);
            if (promptArtifact == null || string.IsNullOrWhiteSpace(promptArtifact.value))
            {
                return PipelineNodeExecutionResult.Failed("Codex generation node requires a prompt artifact.");
            }

            if (outputArtifact == null || string.IsNullOrWhiteSpace(outputArtifact.value))
            {
                return PipelineNodeExecutionResult.Failed("Codex generation node requires an output directory artifact.");
            }

            var result = await backend.GenerateAsync(new CodexGenerationRequest
            {
                prompt = promptArtifact.value,
                outputDirectory = outputArtifact.value,
                timeout = ParseTimeout(node.GetParameterValue("timeoutSeconds")),
                allowLocalUserOverride = IsTrue(node.GetParameterValue("allowLocalUserOverride"))
            }, cancellationToken);

            if (!result.succeeded)
            {
                if (result.cancelled)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return PipelineNodeExecutionResult.Failed(result.message);
            }

            return PipelineNodeExecutionResult.Success(
                new PipelineArtifact
                {
                    id = $"artifact-{node.id}-generated-manifest",
                    nodeId = node.id,
                    portName = OutputPortName(node, "manifest", "manifest"),
                    name = "Generated Asset Manifest",
                    kind = CodexImagePipelineArtifactKinds.GeneratedAssetManifest,
                    value = BuildGeneratedManifest(context, result).ToJson()
                },
                new PipelineArtifact
                {
                    id = $"artifact-{node.id}-stdout",
                    nodeId = node.id,
                    portName = OutputPortName(node, "stdout", "stdout"),
                    name = "Codex stdout",
                    kind = CodexImagePipelineArtifactKinds.CodexStdout,
                    value = result.stdout
                },
                new PipelineArtifact
                {
                    id = $"artifact-{node.id}-stderr",
                    nodeId = node.id,
                    portName = OutputPortName(node, "stderr", "stderr"),
                    name = "Codex stderr",
                    kind = CodexImagePipelineArtifactKinds.CodexStderr,
                    value = result.stderr
                });
        }

        private static GeneratedAssetManifest BuildGeneratedManifest(PipelineExecutionContext context, CodexGenerationResult result)
        {
            var existingManifestArtifact = LatestArtifact(context, CodexImagePipelineArtifactKinds.GeneratedAssetManifest);
            var manifest = existingManifestArtifact == null || string.IsNullOrWhiteSpace(existingManifestArtifact.value)
                ? GeneratedAssetManifest.CreateDefault()
                : GeneratedAssetManifest.FromJson(existingManifestArtifact.value);

            manifest.images = result.generatedPngPaths.Select((path, index) => new GeneratedAssetImage
            {
                id = $"image-{index + 1}",
                role = "generated",
                path = path
            }).ToArray();
            manifest.exportHints.outputDirectory = result.outputDirectory;
            return manifest;
        }

        private static TimeSpan ParseTimeout(string value)
        {
            return int.TryParse(value, out var seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromMinutes(10);
        }

        private static bool IsTrue(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static PipelineArtifact LatestArtifact(PipelineExecutionContext context, string kind)
        {
            return context.Artifacts.LastOrDefault(artifact => string.Equals(artifact.kind, kind, StringComparison.Ordinal));
        }

        private static string OutputPortName(PipelineNode node, string desiredName, string fallback)
        {
            var outputs = node.outputPorts ?? Array.Empty<PipelinePort>();
            return outputs.FirstOrDefault(port => string.Equals(port.name, desiredName, StringComparison.Ordinal))?.name
                ?? (outputs.Length == 0 ? fallback : outputs[0].name);
        }
    }
}
