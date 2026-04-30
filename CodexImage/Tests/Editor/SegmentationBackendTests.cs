using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityCodexImage.Editor.Backends.Segmentation;
using Linalab.UnityCodexImage.Editor.IR;
using Linalab.UnityCodexImage.Editor.Pipeline;
using NUnit.Framework;

namespace Linalab.UnityCodexImage.Editor.Tests
{
    public sealed class SegmentationBackendTests
    {
        private const string PackageRoot = "Packages/com.linalab.lux/CodexImage";
        private const string FakeEvidencePath = PackageRoot + "/.sisyphus/evidence/task-5-segmentation-fake.txt";
        private const string FailureEvidencePath = PackageRoot + "/.sisyphus/evidence/task-5-segmentation-failure.txt";

        [Test]
        public async Task FakeBackendReturnsRequiredMaskAndPartMetadata()
        {
            var backend = new FakeSegmentationBackend();
            var request = SegmentationRequest.Create("Assets/Fixtures/character.png", "Assets/Generated/CodexImages/Nana");

            var result = await backend.SegmentAsync(request, CancellationToken.None);

            Assert.That(result.Succeeded, Is.True, result.Message);
            AssertRequiredResponseFields(result.Response);
            Assert.That(result.Response.masks.Select(mask => mask.label), Is.EqualTo(new[] { "head", "torso", "arm_l" }));
            Assert.That(result.Response.masks.All(mask => mask.maskPngPath.EndsWith(".png", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Response.masks.All(mask => mask.polygonPoints.Length >= 4), Is.True);

            WriteEvidence(
                FakeEvidencePath,
                "Fake segmentation backend passed",
                "Real SAM/SAM2 is not required and is not bundled.",
                "labels=" + string.Join(",", result.Response.masks.Select(mask => mask.label)),
                "maskPaths=" + string.Join(",", result.Response.masks.Select(mask => mask.maskPngPath)),
                "backend=" + result.Response.sourceBackend.backendId);
        }

        [Test]
        public async Task SegmentationNodeReturnsArtifactForFakeBackend()
        {
            var graph = CreateSegmentationGraph("fake-segmentation-node");
            var registry = new PipelineNodeExecutorRegistry();
            registry.Register(new SegmentationNodeExecutor(new FakeSegmentationBackend()));

            var result = await new PipelineGraphExecutor().ExecuteAsync(graph, registry, CancellationToken.None);

            Assert.That(result.succeeded, Is.True, result.message);
            var artifact = result.artifacts.Single(candidate => candidate.kind == CodexImagePipelineArtifactKinds.SegmentationResponse);
            var response = SegmentationResponse.FromJson(artifact.value);
            AssertRequiredResponseFields(response);
            Assert.That(response.masks.Select(mask => mask.label), Does.Contain("head"));
        }

        [Test]
        public async Task SegmentationNodeUsesGeneratedImagePathFromManifestValueWhenSourcePathIsMissing()
        {
            const string generatedPath = "Assets/Generated/CodexImages/Nana/generated-character.png";
            var manifest = GeneratedAssetManifest.CreateDefault();
            manifest.images = new[]
            {
                new GeneratedAssetImage { id = "image-reference", role = "reference", path = "Assets/References/sketch.png" },
                new GeneratedAssetImage { id = "image-generated", role = "generated", path = generatedPath }
            };

            var graph = CreateSegmentationGraphWithoutSourceImagePath(manifest.ToJson());
            var backend = new CapturingSegmentationBackend();
            var registry = new PipelineNodeExecutorRegistry();
            registry.Register(new ManifestArtifactNodeExecutor(manifest.ToJson()));
            registry.Register(new SegmentationNodeExecutor(backend));

            var result = await new PipelineGraphExecutor().ExecuteAsync(graph, registry, CancellationToken.None);

            Assert.That(result.succeeded, Is.True, result.message);
            Assert.That(backend.LastSourceImagePath, Is.EqualTo(generatedPath));
            Assert.That(result.artifacts.Any(artifact => artifact.kind == CodexImagePipelineArtifactKinds.SegmentationResponse), Is.True);
        }

        [Test]
        public async Task SegmentationNodeTreatsMalformedManifestValueAsMissingSourcePath()
        {
            var graph = CreateSegmentationGraphWithoutSourceImagePath("{not-valid-json");
            var registry = new PipelineNodeExecutorRegistry();
            registry.Register(new ManifestArtifactNodeExecutor("{not-valid-json"));
            registry.Register(new SegmentationNodeExecutor(new FakeSegmentationBackend()));

            var result = await new PipelineGraphExecutor().ExecuteAsync(graph, registry, CancellationToken.None);

            Assert.That(result.succeeded, Is.False);
            Assert.That(result.cancelled, Is.False);
            Assert.That(result.message, Does.Contain("InvalidRequest"));
            Assert.That(result.message, Does.Contain("source image path"));
            Assert.That(result.artifacts.Any(artifact => artifact.kind == CodexImagePipelineArtifactKinds.SegmentationResponse), Is.False);
        }

        [Test]
        public async Task BadRemoteEndpointReturnsStructuredConnectionErrorWithoutGraphCrash()
        {
            var graph = CreateSegmentationGraph("remote-failure-node", includePriorNode: true);
            var order = new List<string>();
            var registry = new PipelineNodeExecutorRegistry();
            registry.Register(new DeterministicStubNodeExecutor("prior-artifact", order));
            registry.Register(new SegmentationNodeExecutor(new RemoteSegmentationBackend(SegmentationBackendSettings.Remote("http://127.0.0.1:1/masks", 1000))));

            var result = await new PipelineGraphExecutor().ExecuteAsync(graph, registry, CancellationToken.None);

            Assert.That(result.succeeded, Is.False);
            Assert.That(result.cancelled, Is.False);
            Assert.That(result.message, Does.Contain("ConnectionFailed"));
            Assert.That(result.message, Does.Contain("Remote segmentation connection failed"));
            Assert.That(result.artifacts.Any(artifact => artifact.kind == CodexImagePipelineArtifactKinds.SegmentationResponse), Is.False);
            Assert.That(result.executedNodeIds, Is.EqualTo(new[] { "prior" }));
            Assert.That(result.artifacts.Select(artifact => artifact.id), Does.Contain("artifact-prior"));

            WriteEvidence(
                FailureEvidencePath,
                "Remote segmentation endpoint failure passed",
                "Real SAM/SAM2 is not required and is not bundled.",
                "endpoint=http://127.0.0.1:1/masks",
                $"succeeded={result.succeeded}",
                "message=" + result.message,
                "preservedArtifacts=" + string.Join(",", result.artifacts.Select(artifact => artifact.id)),
                "artifacts=" + string.Join(",", result.artifacts.Select(artifact => artifact.kind)));
        }

        [Test]
        public void EditorPrefsSettingsDefaultToFakeWithoutProjectAssets()
        {
            var settings = new EditorPrefsSegmentationBackendSettings();

            Assert.That(
                settings.Mode == SegmentationBackendMode.Fake || settings.Mode == SegmentationBackendMode.Remote,
                Is.True);
            Assert.That(settings.TimeoutMilliseconds, Is.GreaterThanOrEqualTo(100));
        }

        private static PipelineGraph CreateSegmentationGraph(string id, bool includePriorNode = false)
        {
            var segmentationNode = new PipelineNode
            {
                id = "segment",
                type = CodexImagePipelineNodeTypes.Segmentation,
                inputPorts = includePriorNode
                    ? new[]
                    {
                        new PipelinePort
                        {
                            name = "input",
                            direction = PipelinePortDirection.Input,
                            dataType = "stub"
                        }
                    }
                    : Array.Empty<PipelinePort>(),
                outputPorts = new[]
                {
                    new PipelinePort
                    {
                        name = "segmentation",
                        direction = PipelinePortDirection.Output,
                        dataType = CodexImagePipelineArtifactKinds.SegmentationResponse
                    }
                },
                parameters = new[]
                {
                    new PipelineParameter { name = "sourceImagePath", value = "Assets/Fixtures/character.png" },
                    new PipelineParameter { name = "outputDirectory", value = "Assets/Generated/CodexImages/Nana" }
                }
            };

            if (!includePriorNode)
            {
                return new PipelineGraph
                {
                    id = id,
                    nodes = new[] { segmentationNode }
                };
            }

            return new PipelineGraph
            {
                id = id,
                nodes = new[]
                {
                    new PipelineNode
                    {
                        id = "prior",
                        type = "prior-artifact",
                        outputPorts = new[]
                        {
                            new PipelinePort
                            {
                                name = "out",
                                direction = PipelinePortDirection.Output,
                                dataType = "stub"
                            }
                        }
                    },
                    segmentationNode
                },
                edges = new[]
                {
                    new PipelineEdge
                    {
                        id = "prior-to-segment",
                        fromNodeId = "prior",
                        fromPortName = "out",
                        toNodeId = "segment",
                        toPortName = "input"
                    }
                }
            };
        }

        private static PipelineGraph CreateSegmentationGraphWithoutSourceImagePath(string manifestJson)
        {
            return new PipelineGraph
            {
                id = "manifest-value-segmentation-source",
                nodes = new[]
                {
                    new PipelineNode
                    {
                        id = "generation",
                        type = "manifest-artifact",
                        outputPorts = new[]
                        {
                            new PipelinePort
                            {
                                name = "manifest",
                                direction = PipelinePortDirection.Output,
                                dataType = CodexImagePipelineArtifactKinds.GeneratedAssetManifest
                            }
                        },
                        parameters = new[]
                        {
                            new PipelineParameter { name = "manifestJson", value = manifestJson }
                        }
                    },
                    new PipelineNode
                    {
                        id = "segment",
                        type = CodexImagePipelineNodeTypes.Segmentation,
                        inputPorts = new[]
                        {
                            new PipelinePort
                            {
                                name = "manifest",
                                direction = PipelinePortDirection.Input,
                                dataType = CodexImagePipelineArtifactKinds.GeneratedAssetManifest
                            }
                        },
                        outputPorts = new[]
                        {
                            new PipelinePort
                            {
                                name = "segmentation",
                                direction = PipelinePortDirection.Output,
                                dataType = CodexImagePipelineArtifactKinds.SegmentationResponse
                            }
                        },
                        parameters = new[]
                        {
                            new PipelineParameter { name = "outputDirectory", value = "Assets/Generated/CodexImages/Nana" }
                        }
                    }
                },
                edges = new[]
                {
                    new PipelineEdge
                    {
                        id = "generation-to-segment",
                        fromNodeId = "generation",
                        fromPortName = "manifest",
                        toNodeId = "segment",
                        toPortName = "manifest"
                    }
                }
            };
        }

        private static void AssertRequiredResponseFields(SegmentationResponse response)
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response.requestId, Is.Not.Empty);
            Assert.That(response.rasterWidth, Is.GreaterThan(0));
            Assert.That(response.rasterHeight, Is.GreaterThan(0));
            Assert.That(response.sourceBackend.backendId, Is.Not.Empty);
            Assert.That(response.sourceBackend.backendKind, Is.Not.Empty);
            Assert.That(response.masks, Is.Not.Empty);

            foreach (var mask in response.masks)
            {
                Assert.That(mask.maskPngPath, Is.Not.Empty);
                Assert.That(mask.label, Is.Not.Empty);
                Assert.That(mask.confidence, Is.InRange(0f, 1f));
                Assert.That(mask.bbox.width, Is.GreaterThan(0));
                Assert.That(mask.bbox.height, Is.GreaterThan(0));
                Assert.That(mask.rasterWidth, Is.EqualTo(response.rasterWidth));
                Assert.That(mask.rasterHeight, Is.EqualTo(response.rasterHeight));
                Assert.That(mask.polygonPoints, Is.Not.Empty);
                Assert.That(mask.sourceBackend.backendId, Is.EqualTo(response.sourceBackend.backendId));
            }
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

        private sealed class CapturingSegmentationBackend : ISegmentationBackend
        {
            private readonly FakeSegmentationBackend fakeBackend = new FakeSegmentationBackend();

            public string BackendId => fakeBackend.BackendId;
            public string LastSourceImagePath { get; private set; }

            public Task<SegmentationBackendResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken)
            {
                LastSourceImagePath = request?.sourceImagePath;
                return fakeBackend.SegmentAsync(request, cancellationToken);
            }
        }

        private sealed class ManifestArtifactNodeExecutor : IPipelineNodeExecutor
        {
            private readonly string manifestJson;

            public ManifestArtifactNodeExecutor(string manifestJson)
            {
                this.manifestJson = manifestJson;
            }

            public string NodeType => "manifest-artifact";

            public Task<PipelineNodeExecutionResult> ExecuteAsync(PipelineNode node, PipelineExecutionContext context, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(PipelineNodeExecutionResult.Success(new PipelineArtifact
                {
                    id = "artifact-generated-manifest",
                    nodeId = node.id,
                    portName = "manifest",
                    name = "Generated Asset Manifest",
                    kind = CodexImagePipelineArtifactKinds.GeneratedAssetManifest,
                    value = manifestJson
                }));
            }
        }
    }
}
