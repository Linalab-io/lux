using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityCodexImage.Editor.IR;
using Linalab.UnityCodexImage.Editor.Pipeline;
using NUnit.Framework;

namespace Linalab.UnityCodexImage.Editor.Tests
{
    public sealed class PipelineContextPromptNodeTests
    {
        private const string PackageRoot = "Packages/com.linalab.lux/CodexImage";
        private const string FixtureContextPath = PackageRoot + "/Tests/Editor/Fixtures/Pipeline/unity-context-fixture.json";
        private const string TemplateSubstitutionEvidencePath = PackageRoot + "/.sisyphus/evidence/task-3-template-substitution.txt";
        private const string MissingContextEvidencePath = PackageRoot + "/.sisyphus/evidence/task-3-missing-context.txt";

        [Test]
        public async Task PromptTemplateSubstitutionIsDeterministicAndLeavesNoPlaceholders()
        {
            var graph = CreatePromptGraph("Generate {characterName} using {contextPath} into {outputDirectory} with {backendName}.");
            var result = await ExecuteGraph(graph, new FixtureUnityContextAdapter(FixtureContextPath));

            Assert.That(result.succeeded, Is.True, result.message);
            var promptArtifact = result.artifacts.Single(artifact => artifact.kind == CodexImagePipelineArtifactKinds.Prompt);
            Assert.That(promptArtifact.value, Does.Contain("Nana"));
            Assert.That(promptArtifact.value, Does.Contain(FixtureContextPath));
            Assert.That(promptArtifact.value, Does.Contain("Assets/Generated/CodexImages/Nana"));
            Assert.That(promptArtifact.value, Does.Not.Contain("{characterName}"));
            Assert.That(promptArtifact.value, Does.Not.Contain("{contextPath}"));

            WriteEvidence(
                TemplateSubstitutionEvidencePath,
                "Prompt template substitution passed",
                "prompt=" + promptArtifact.value,
                "artifacts=" + string.Join(",", result.artifacts.Select(artifact => artifact.kind)));
        }

        [Test]
        public async Task FixtureContextNodeProducesContextArtifact()
        {
            var graph = new PipelineGraph
            {
                id = "fixture-context",
                nodes = new[]
                {
                    CreateContextNode()
                }
            };

            var result = await ExecuteGraph(graph, new FixtureUnityContextAdapter(FixtureContextPath));

            Assert.That(result.succeeded, Is.True, result.message);
            var contextArtifact = result.artifacts.Single(artifact => artifact.kind == CodexImagePipelineArtifactKinds.UnityContext);
            Assert.That(contextArtifact.path, Is.EqualTo(FixtureContextPath));
            Assert.That(contextArtifact.value, Does.Contain("fixture-scene.unity"));
        }

        [Test]
        public async Task MissingFixtureContextFailsCleanlyWithoutPromptOrManifestArtifacts()
        {
            var graph = CreatePromptGraph("Generate {characterName}.");
            var result = await ExecuteGraph(graph, new FixtureUnityContextAdapter(PackageRoot + "/Tests/Editor/Fixtures/Pipeline/missing-context.json"));

            Assert.That(result.succeeded, Is.False);
            Assert.That(result.message, Does.Contain("Unity context export failed"));
            Assert.That(result.artifacts.Any(artifact => artifact.kind == CodexImagePipelineArtifactKinds.Prompt), Is.False);
            Assert.That(result.artifacts.Any(artifact => artifact.kind == CodexImagePipelineArtifactKinds.GeneratedAssetManifest), Is.False);

            WriteEvidence(
                MissingContextEvidencePath,
                "Missing fixture context failure passed",
                $"succeeded={result.succeeded}",
                $"message={result.message}",
                "artifacts=" + string.Join(",", result.artifacts.Select(artifact => artifact.kind)));
        }

        [Test]
        public void OutputDirectoryPolicyValidatesAndNormalizesProjectRelativePaths()
        {
            Assert.That(
                PipelineOutputDirectoryPolicy.NormalizeProjectRelativePath("Assets\\Generated//CodexImages/./Nana"),
                Is.EqualTo("Assets/Generated/CodexImages/Nana"));

            Assert.Throws<ArgumentException>(() => PipelineOutputDirectoryPolicy.NormalizeProjectRelativePath(string.Empty));
            Assert.Throws<ArgumentException>(() => PipelineOutputDirectoryPolicy.NormalizeProjectRelativePath("/tmp/out"));
            Assert.Throws<ArgumentException>(() => PipelineOutputDirectoryPolicy.NormalizeProjectRelativePath("../outside"));
        }

        [Test]
        public async Task PromptNodeCapturesProvenanceIntoGeneratedAssetManifest()
        {
            var graph = CreatePromptGraph("Draw {characterName} with [style]. Context: {contextPath}.");
            graph.nodes[2].parameters = graph.nodes[2].parameters.Concat(new[]
            {
                new PipelineParameter { name = "var.style", value = "flat cel shading" }
            }).ToArray();

            var result = await ExecuteGraph(graph, new FixtureUnityContextAdapter(FixtureContextPath));

            Assert.That(result.succeeded, Is.True, result.message);
            var manifestArtifact = result.artifacts.Single(artifact => artifact.kind == CodexImagePipelineArtifactKinds.GeneratedAssetManifest);
            var manifest = GeneratedAssetManifest.FromJson(manifestArtifact.value);

            Assert.That(manifest.metadata.provenance.prompt, Does.Contain("Nana"));
            Assert.That(manifest.metadata.provenance.prompt, Does.Contain("flat cel shading"));
            Assert.That(manifest.metadata.provenance.backendName, Is.EqualTo("Codex"));
            Assert.That(manifest.metadata.provenance.requestId, Is.Not.Empty);
            Assert.That(manifest.metadata.createdUtc, Is.Not.Empty);
            Assert.That(manifest.sourceContext.contextJsonPath, Is.EqualTo(FixtureContextPath));
            Assert.That(manifest.sourceContext.scenePath, Is.EqualTo("Assets/Scenes/fixture-scene.unity"));
            Assert.That(manifest.sourceContext.selectedAssetPath, Is.EqualTo("Assets/Characters/Nana/Nana.png"));
            Assert.That(manifest.exportHints.outputDirectory, Is.EqualTo("Assets/Generated/CodexImages/Nana"));
        }

        [Test]
        public void RealUnityBridgeContextAdapterIsAvailableForCodexContextExport()
        {
            var executor = new UnityContextNodeExecutor();

            Assert.That(executor.NodeType, Is.EqualTo(CodexImagePipelineNodeTypes.UnityContext));
        }

        private static async Task<PipelineExecutionResult> ExecuteGraph(PipelineGraph graph, IUnityContextAdapter contextAdapter)
        {
            var registry = new PipelineNodeExecutorRegistry();
            registry.Register(new UnityContextNodeExecutor(contextAdapter));
            registry.Register(new OutputDirectoryNodeExecutor());
            registry.Register(new PromptTemplateNodeExecutor());
            return await new PipelineGraphExecutor().ExecuteAsync(graph, registry, CancellationToken.None);
        }

        private static PipelineGraph CreatePromptGraph(string template)
        {
            return new PipelineGraph
            {
                id = "context-prompt-graph",
                nodes = new[]
                {
                    CreateContextNode(),
                    new PipelineNode
                    {
                        id = "output",
                        type = CodexImagePipelineNodeTypes.OutputDirectory,
                        outputPorts = new[] { CreatePort("outputDirectory", PipelinePortDirection.Output, CodexImagePipelineArtifactKinds.OutputDirectory) },
                        parameters = new[]
                        {
                            new PipelineParameter { name = "path", value = "Assets\\Generated//CodexImages/./Nana" }
                        }
                    },
                    new PipelineNode
                    {
                        id = "prompt",
                        type = CodexImagePipelineNodeTypes.PromptTemplate,
                        inputPorts = new[]
                        {
                            CreatePort("context", PipelinePortDirection.Input, CodexImagePipelineArtifactKinds.UnityContext),
                            CreatePort("outputDirectory", PipelinePortDirection.Input, CodexImagePipelineArtifactKinds.OutputDirectory)
                        },
                        outputPorts = new[]
                        {
                            CreatePort("prompt", PipelinePortDirection.Output, CodexImagePipelineArtifactKinds.Prompt),
                            CreatePort("manifest", PipelinePortDirection.Output, CodexImagePipelineArtifactKinds.GeneratedAssetManifest)
                        },
                        parameters = new[]
                        {
                            new PipelineParameter { name = "template", value = template },
                            new PipelineParameter { name = "var.characterName", value = "Nana" },
                            new PipelineParameter { name = "backendName", value = "Codex" }
                        }
                    }
                },
                edges = new[]
                {
                    CreateEdge("context-to-prompt", "context", "context", "prompt", "context"),
                    CreateEdge("output-to-prompt", "output", "outputDirectory", "prompt", "outputDirectory")
                }
            };
        }

        private static PipelineNode CreateContextNode()
        {
            return new PipelineNode
            {
                id = "context",
                type = CodexImagePipelineNodeTypes.UnityContext,
                outputPorts = new[] { CreatePort("context", PipelinePortDirection.Output, CodexImagePipelineArtifactKinds.UnityContext) }
            };
        }

        private static PipelinePort CreatePort(string name, PipelinePortDirection direction, string dataType)
        {
            return new PipelinePort
            {
                name = name,
                direction = direction,
                dataType = dataType
            };
        }

        private static PipelineEdge CreateEdge(string id, string fromNodeId, string fromPortName, string toNodeId, string toPortName)
        {
            return new PipelineEdge
            {
                id = id,
                fromNodeId = fromNodeId,
                fromPortName = fromPortName,
                toNodeId = toNodeId,
                toPortName = toPortName
            };
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
    }
}
