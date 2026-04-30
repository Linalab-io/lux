using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityCodexImage.Editor.Backends.Codex;
using NUnit.Framework;

namespace Linalab.UnityCodexImage.Editor.Tests
{
    public sealed class CodexCliBackendTests
    {
        private const string PackageRoot = "Packages/com.linalab.lux/CodexImage";
        private const string MissingCliEvidencePath = PackageRoot + "/.sisyphus/evidence/task-4-codex-missing.txt";
        private const string CancelEvidencePath = PackageRoot + "/.sisyphus/evidence/task-4-codex-cancel.txt";

        [Test]
        public async Task MissingCodexCliReturnsUnavailableWithoutThrowing()
        {
            var runner = new FakeCodexProcessRunner(new[]
            {
                new CodexProcessResult
                {
                    started = false,
                    exitCode = 127,
                    stderr = "codex: command not found"
                }
            });
            var backend = new CodexCliBackend(runner);

            var preflight = await backend.PreflightAsync(CancellationToken.None);

            Assert.That(preflight.Status, Is.EqualTo(CodexPreflightStatus.CodexUnavailable));
            Assert.That(preflight.Message, Does.Contain("PATH"));
            Assert.That(runner.StartInfos.Single().arguments, Is.EqualTo("--version"));

            WriteEvidence(
                MissingCliEvidencePath,
                "Missing Codex CLI preflight passed",
                $"status={preflight.Status}",
                $"message={preflight.Message}",
                "commands=" + string.Join(",", runner.StartInfos.Select(info => info.fileName + " " + info.arguments)));
        }

        [Test]
        public async Task GenerationCancellationReturnsStructuredCancelledResult()
        {
            var runner = new FakeCodexProcessRunner(new[]
            {
                new CodexProcessResult
                {
                    exitCode = 0,
                    stdout = "codex 1.0"
                },
                new CodexProcessResult
                {
                    cancelled = true,
                    exitCode = -1,
                    stdout = "partial stdout",
                    stderr = "partial stderr"
                }
            });
            var backend = new CodexCliBackend(runner);
            var outputPath = CreatePackageTempPath("cancel-output");

            var result = await backend.GenerateAsync(new CodexGenerationRequest
            {
                prompt = "Generate a sprite.",
                outputDirectory = outputPath.projectRelative,
                workingDirectory = outputPath.projectRoot,
                timeout = TimeSpan.FromSeconds(30)
            }, CancellationToken.None);

            Assert.That(result.succeeded, Is.False);
            Assert.That(result.cancelled, Is.True);
            Assert.That(result.message, Does.Contain("cancelled"));
            Assert.That(result.stdout, Does.Contain("partial stdout"));
            Assert.That(result.stderr, Does.Contain("partial stderr"));
            Assert.That(runner.StartInfos[1].arguments, Does.Contain("exec"));

            WriteEvidence(
                CancelEvidencePath,
                "Codex cancellation handling passed",
                $"succeeded={result.succeeded}",
                $"cancelled={result.cancelled}",
                $"exitCode={result.exitCode}",
                $"stdout={result.stdout}",
                $"stderr={result.stderr}",
                "commands=" + string.Join(",", runner.StartInfos.Select(info => info.fileName + " " + info.arguments)));
        }

        [Test]
        public async Task SuccessfulGenerationCapturesStdoutStderrAndVerifiesPngFiles()
        {
            var outputPath = CreatePackageTempPath("success-output");
            Directory.CreateDirectory(outputPath.absolute);
            var pngPath = Path.Combine(outputPath.absolute, "generated.png");
            File.WriteAllBytes(pngPath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 });
            var runner = new FakeCodexProcessRunner(new[]
            {
                new CodexProcessResult { exitCode = 0, stdout = "codex 1.0" },
                new CodexProcessResult
                {
                    exitCode = 0,
                    stdout = "saved " + outputPath.projectRelative + "/generated.png",
                    stderr = "warning text"
                }
            });
            var backend = new CodexCliBackend(runner);

            var result = await backend.GenerateAsync(new CodexGenerationRequest
            {
                prompt = "Generate a sprite.",
                outputDirectory = outputPath.projectRelative,
                workingDirectory = outputPath.projectRoot
            }, CancellationToken.None);

            Assert.That(result.succeeded, Is.True, result.message);
            Assert.That(result.stdout, Does.Contain("saved"));
            Assert.That(result.stderr, Is.EqualTo("warning text"));
            Assert.That(result.generatedPngPaths, Is.EqualTo(new[] { outputPath.projectRelative + "/generated.png" }));
        }

        [Test]
        public void OutputDirectoryValidationRequiresAssetsUnlessLocalOverrideIsExplicit()
        {
            var projectRoot = Directory.GetCurrentDirectory();

            Assert.Throws<ArgumentException>(() => CodexOutputDirectoryValidator.Normalize("Generated/Codex", projectRoot, false));
            Assert.DoesNotThrow(() => CodexOutputDirectoryValidator.Normalize("Generated/Codex", projectRoot, true));
        }

        private static (string projectRoot, string projectRelative, string absolute) CreatePackageTempPath(string name)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var projectRelative = "Assets/Generated/CodexBackendTests/" + name + "-" + Guid.NewGuid().ToString("N");
            return (projectRoot, projectRelative, Path.GetFullPath(Path.Combine(projectRoot, projectRelative)));
        }

        private static void WriteEvidence(string path, params string[] lines)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(path, lines.Concat(new[] { $"utc={DateTime.UtcNow:O}" }));
        }

        private sealed class FakeCodexProcessRunner : ICodexProcessRunner
        {
            private readonly Queue<CodexProcessResult> results;

            public FakeCodexProcessRunner(IEnumerable<CodexProcessResult> results)
            {
                this.results = new Queue<CodexProcessResult>(results);
            }

            public List<CodexProcessStartInfo> StartInfos { get; } = new List<CodexProcessStartInfo>();

            public Task<CodexProcessResult> RunAsync(CodexProcessStartInfo startInfo, CancellationToken cancellationToken)
            {
                StartInfos.Add(startInfo);
                return Task.FromResult(results.Dequeue());
            }
        }
    }
}
