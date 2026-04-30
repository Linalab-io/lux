using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Linalab.UnityAiBridge.Editor;
using UnityEditor;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor
{
    public static class CodexImageGenerator
    {
        public static CodexImageGenerationResult Generate(CodexImageGenerationRequest request)
        {
            ValidateRequest(request);

            var absoluteOutputDirectory = ToAbsolutePath(request.outputDirectory);
            Directory.CreateDirectory(absoluteOutputDirectory);

            var contextResult = Linalab.UnityAiBridge.Editor.UnityAiBridge.ExportDefaultContext(AiToolKind.Codex);
            var prompt = CodexPromptBuilder.Build(request, absoluteOutputDirectory, contextResult.OutputPath);
            var result = RunCodex(prompt);

            if (result.Succeeded)
            {
                AssetDatabase.Refresh();
            }

            return new CodexImageGenerationResult(result.ExitCode, result.Output, result.Error, contextResult.OutputPath);
        }

        private static void ValidateRequest(CodexImageGenerationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.prompt))
            {
                throw new ArgumentException("Prompt is required.", nameof(request));
            }

            if (!CodexImageOptions.IsValidSize(request.size))
            {
                throw new ArgumentException($"Unsupported size: {request.size}", nameof(request));
            }

            if (!CodexImageOptions.IsValidQuality(request.quality))
            {
                throw new ArgumentException($"Unsupported quality: {request.quality}", nameof(request));
            }

            if (request.count < 1 || request.count > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Count must be between 1 and 10.");
            }
        }

        private static string ToAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Output directory is required.", nameof(path));
            }

            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
        }

        private static CodexProcessResult RunCodex(string prompt)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "codex",
                Arguments = $"exec {Quote(prompt)} -s workspace-write --skip-git-repo-check",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var output = new StringBuilder();
            var error = new StringBuilder();
            process.OutputDataReceived += (_, args) => AppendLine(output, args.Data);
            process.ErrorDataReceived += (_, args) => AppendLine(error, args.Data);

            try
            {
                process.Start();
            }
            catch (Exception exception)
            {
                return new CodexProcessResult(127, string.Empty, exception.Message);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return new CodexProcessResult(process.ExitCode, output.ToString(), error.ToString());
        }

        private static void AppendLine(StringBuilder builder, string value)
        {
            if (value != null)
            {
                builder.AppendLine(value);
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private readonly struct CodexProcessResult
        {
            public CodexProcessResult(int exitCode, string output, string error)
            {
                ExitCode = exitCode;
                Output = output;
                Error = error;
            }

            public int ExitCode { get; }
            public string Output { get; }
            public string Error { get; }
            public bool Succeeded => ExitCode == 0;
        }
    }
}
