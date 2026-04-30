using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityAiBridge.Editor;
using Linalab.UnityCodexImage.Editor.IR;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Pipeline
{
    public static class CodexImagePipelineNodeTypes
    {
        public const string UnityContext = "unity-context";
        public const string OutputDirectory = "output-directory";
        public const string PromptTemplate = "prompt-template";
        public const string CodexGeneration = "codex-generation";
        public const string Segmentation = "segmentation";
        public const string MaskPostProcessing = "mask-post-processing";
    }

    public static class CodexImagePipelineArtifactKinds
    {
        public const string UnityContext = "unity-context";
        public const string OutputDirectory = "output-directory";
        public const string Prompt = "prompt";
        public const string GeneratedAssetManifest = "generated-asset-manifest";
        public const string CodexStdout = "codex-stdout";
        public const string CodexStderr = "codex-stderr";
        public const string SegmentationResponse = "segmentation-response";
    }

    public readonly struct UnityContextExport
    {
        public UnityContextExport(string outputPath, string json)
        {
            OutputPath = outputPath;
            Json = json;
        }

        public string OutputPath { get; }
        public string Json { get; }
    }

    public interface IUnityContextAdapter
    {
        UnityContextExport ExportContext();
    }

    public sealed class UnityAiBridgeContextAdapter : IUnityContextAdapter
    {
        public UnityContextExport ExportContext()
        {
            var result = global::Linalab.UnityAiBridge.Editor.UnityAiBridge.ExportDefaultContext(AiToolKind.Codex);
            return new UnityContextExport(result.OutputPath, result.Json);
        }
    }

    public sealed class FixtureUnityContextAdapter : IUnityContextAdapter
    {
        private readonly string fixturePath;

        public FixtureUnityContextAdapter(string fixturePath)
        {
            this.fixturePath = fixturePath;
        }

        public UnityContextExport ExportContext()
        {
            if (string.IsNullOrWhiteSpace(fixturePath))
            {
                throw new InvalidOperationException("Fixture context path is required.");
            }

            if (!File.Exists(fixturePath))
            {
                throw new FileNotFoundException("Fixture context JSON was not found.", fixturePath);
            }

            return new UnityContextExport(NormalizeSlashes(fixturePath), File.ReadAllText(fixturePath));
        }

        private static string NormalizeSlashes(string path)
        {
            return path.Replace('\\', '/');
        }
    }

    public static class PipelineOutputDirectoryPolicy
    {
        public static string NormalizeProjectRelativePath(string path, bool allowLocalUserOverride = false)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Output directory is required.", nameof(path));
            }

            var normalized = path.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(normalized))
            {
                if (!allowLocalUserOverride)
                {
                    throw new ArgumentException("Output directory must be project-relative.", nameof(path));
                }

                return normalized;
            }

            var parts = new List<string>();
            foreach (var part in normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    if (parts.Count == 0)
                    {
                        throw new ArgumentException("Output directory cannot escape the project root.", nameof(path));
                    }

                    parts.RemoveAt(parts.Count - 1);
                    continue;
                }

                parts.Add(part);
            }

            if (parts.Count == 0)
            {
                throw new ArgumentException("Output directory is required.", nameof(path));
            }

            normalized = string.Join("/", parts);
            if (!allowLocalUserOverride && !normalized.StartsWith("Assets/", StringComparison.Ordinal) && !string.Equals(normalized, "Assets", StringComparison.Ordinal))
            {
                throw new ArgumentException("Output directory must be under Assets/ for pipeline generation.", nameof(path));
            }

            return normalized;
        }
    }

    public sealed class UnityContextNodeExecutor : IPipelineNodeExecutor
    {
        private readonly IUnityContextAdapter adapter;

        public UnityContextNodeExecutor(IUnityContextAdapter adapter = null)
        {
            this.adapter = adapter ?? new UnityAiBridgeContextAdapter();
        }

        public string NodeType => CodexImagePipelineNodeTypes.UnityContext;

        public Task<PipelineNodeExecutionResult> ExecuteAsync(PipelineNode node, PipelineExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var export = adapter.ExportContext();
                if (string.IsNullOrWhiteSpace(export.OutputPath))
                {
                    return Task.FromResult(PipelineNodeExecutionResult.Failed("Unity context export did not return an output path."));
                }

                return Task.FromResult(PipelineNodeExecutionResult.Success(new PipelineArtifact
                {
                    id = $"artifact-{node.id}-context",
                    nodeId = node.id,
                    portName = FirstOutputPortName(node, "context"),
                    name = "Unity Context",
                    kind = CodexImagePipelineArtifactKinds.UnityContext,
                    value = export.Json,
                    path = export.OutputPath
                }));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                return Task.FromResult(PipelineNodeExecutionResult.Failed($"Unity context export failed: {exception.Message}"));
            }
        }

        private static string FirstOutputPortName(PipelineNode node, string fallback)
        {
            var outputs = node.outputPorts ?? Array.Empty<PipelinePort>();
            return outputs.Length == 0 ? fallback : outputs[0].name;
        }
    }

    public sealed class OutputDirectoryNodeExecutor : IPipelineNodeExecutor
    {
        public string NodeType => CodexImagePipelineNodeTypes.OutputDirectory;

        public Task<PipelineNodeExecutionResult> ExecuteAsync(PipelineNode node, PipelineExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var outputDirectory = PipelineOutputDirectoryPolicy.NormalizeProjectRelativePath(
                    node.GetParameterValue("path", node.GetParameterValue("outputDirectory")),
                    string.Equals(node.GetParameterValue("allowLocalUserOverride"), "true", StringComparison.OrdinalIgnoreCase));

                return Task.FromResult(PipelineNodeExecutionResult.Success(new PipelineArtifact
                {
                    id = $"artifact-{node.id}-output-directory",
                    nodeId = node.id,
                    portName = FirstOutputPortName(node, "outputDirectory"),
                    name = "Output Directory",
                    kind = CodexImagePipelineArtifactKinds.OutputDirectory,
                    value = outputDirectory,
                    path = outputDirectory
                }));
            }
            catch (ArgumentException exception)
            {
                return Task.FromResult(PipelineNodeExecutionResult.Failed(exception.Message));
            }
        }

        private static string FirstOutputPortName(PipelineNode node, string fallback)
        {
            var outputs = node.outputPorts ?? Array.Empty<PipelinePort>();
            return outputs.Length == 0 ? fallback : outputs[0].name;
        }
    }

    public sealed class PromptTemplateNodeExecutor : IPipelineNodeExecutor
    {
        public string NodeType => CodexImagePipelineNodeTypes.PromptTemplate;

        public Task<PipelineNodeExecutionResult> ExecuteAsync(PipelineNode node, PipelineExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var templateResult = ResolveTemplate(node);
            if (!templateResult.succeeded)
            {
                return Task.FromResult(PipelineNodeExecutionResult.Failed(templateResult.message));
            }

            var contextArtifact = LatestArtifact(context, CodexImagePipelineArtifactKinds.UnityContext);
            if (contextArtifact == null || string.IsNullOrWhiteSpace(contextArtifact.path))
            {
                return Task.FromResult(PipelineNodeExecutionResult.Failed("Prompt template node requires a Unity context artifact."));
            }

            var outputArtifact = LatestArtifact(context, CodexImagePipelineArtifactKinds.OutputDirectory);
            if (outputArtifact == null || string.IsNullOrWhiteSpace(outputArtifact.value))
            {
                return Task.FromResult(PipelineNodeExecutionResult.Failed("Prompt template node requires an output directory artifact."));
            }

            var bindings = BuildBindings(node, contextArtifact, outputArtifact);
            var prompt = CodexPromptBuilder.BuildTemplate(templateResult.template, bindings);
            if (CodexPromptBuilder.HasUnresolvedPlaceholders(prompt))
            {
                return Task.FromResult(PipelineNodeExecutionResult.Failed("Prompt contains unresolved placeholders after template substitution."));
            }

            var backendName = node.GetParameterValue("backendName", "Codex");
            var manifest = BuildManifest(prompt, contextArtifact, outputArtifact.value, backendName, templateResult.templatePath);
            var promptPortName = OutputPortName(node, "prompt", "prompt");

            return Task.FromResult(PipelineNodeExecutionResult.Success(
                new PipelineArtifact
                {
                    id = $"artifact-{node.id}-prompt",
                    nodeId = node.id,
                    portName = promptPortName,
                    name = "Prompt",
                    kind = CodexImagePipelineArtifactKinds.Prompt,
                    value = prompt
                },
                new PipelineArtifact
                {
                    id = $"artifact-{node.id}-manifest",
                    nodeId = node.id,
                    portName = OutputPortName(node, "manifest", "manifest"),
                    name = "Generated Asset Manifest",
                    kind = CodexImagePipelineArtifactKinds.GeneratedAssetManifest,
                    value = manifest.ToJson()
                }));
        }

        private static (bool succeeded, string template, string templatePath, string message) ResolveTemplate(PipelineNode node)
        {
            var template = node.GetParameterValue("template");
            if (!string.IsNullOrEmpty(template))
            {
                return (true, template, string.Empty, string.Empty);
            }

            var templatePath = node.GetParameterValue("templatePath");
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return (false, string.Empty, string.Empty, "Prompt template node requires a template or templatePath parameter.");
            }

            if (!File.Exists(templatePath))
            {
                return (false, string.Empty, templatePath, $"Prompt template file was not found: {templatePath}");
            }

            return (true, File.ReadAllText(templatePath), templatePath.Replace('\\', '/'), string.Empty);
        }

        private static Dictionary<string, string> BuildBindings(PipelineNode node, PipelineArtifact contextArtifact, PipelineArtifact outputArtifact)
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contextPath"] = contextArtifact.path,
                ["outputDirectory"] = outputArtifact.value,
                ["backendName"] = node.GetParameterValue("backendName", "Codex")
            };

            foreach (var parameter in node.parameters ?? Array.Empty<PipelineParameter>())
            {
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.name))
                {
                    continue;
                }

                if (parameter.name.StartsWith("var.", StringComparison.Ordinal))
                {
                    bindings[parameter.name.Substring(4)] = parameter.value ?? string.Empty;
                }
            }

            return bindings;
        }

        private static GeneratedAssetManifest BuildManifest(
            string prompt,
            PipelineArtifact contextArtifact,
            string outputDirectory,
            string backendName,
            string templatePath)
        {
            var manifest = GeneratedAssetManifest.CreateDefault();
            manifest.metadata.createdUtc = DateTime.UtcNow.ToString("O");
            manifest.metadata.generatorName = "Linalab.UnityCodexImage";
            manifest.metadata.provenance.prompt = prompt;
            manifest.metadata.provenance.backendName = backendName;
            manifest.metadata.provenance.requestId = Guid.NewGuid().ToString("N");
            manifest.sourceContext.contextJsonPath = contextArtifact.path;
            manifest.sourceContext.promptTemplatePath = templatePath;
            ApplyContextHints(manifest.sourceContext, contextArtifact.value);
            manifest.exportHints.outputDirectory = outputDirectory;
            return manifest;
        }

        private static void ApplyContextHints(GeneratedAssetSourceContext sourceContext, string contextJson)
        {
            if (string.IsNullOrWhiteSpace(contextJson))
            {
                return;
            }

            try
            {
                var summary = JsonUtility.FromJson<UnityContextSummary>(contextJson);
                if (summary == null)
                {
                    return;
                }

                sourceContext.unityProjectPath = summary.unityProjectPath;
                sourceContext.unityVersion = summary.unityVersion;
                sourceContext.scenePath = summary.scenePath;
                sourceContext.selectedAssetPath = summary.selectedAssetPath;
            }
            catch (ArgumentException)
            {
            }
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

        [Serializable]
        private sealed class UnityContextSummary
        {
            public string unityProjectPath;
            public string unityVersion;
            public string scenePath;
            public string selectedAssetPath;
        }
    }
}
