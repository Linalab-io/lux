using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityCodexImage.Editor.Pipeline;
using NUnit.Framework;

namespace Linalab.UnityCodexImage.Editor.Tests
{
    public sealed class PipelineGraphRuntimeTests
    {
        private const string PackageRoot = "Packages/com.linalab.lux/CodexImage";
        private const string DagOrderEvidencePath = PackageRoot + "/.sisyphus/evidence/task-2-dag-order.txt";
        private const string CycleRejectedEvidencePath = PackageRoot + "/.sisyphus/evidence/task-2-cycle-rejected.txt";

        [Test]
        public void RoundTripsGraphJsonPreservingIdsParametersAndPorts()
        {
            var graph = CreateLinearGraph();

            var roundTripped = PipelineGraph.FromJson(graph.ToJson());

            Assert.That(roundTripped.schemaVersion, Is.EqualTo(PipelineGraph.CurrentSchemaVersion));
            Assert.That(roundTripped.nodes.Select(node => node.id), Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(roundTripped.edges.Select(edge => edge.id), Is.EqualTo(new[] { "edge-A-B", "edge-B-C" }));
            Assert.That(roundTripped.nodes[0].parameters[0].name, Is.EqualTo("value"));
            Assert.That(roundTripped.nodes[0].parameters[0].value, Is.EqualTo("A-value"));
            Assert.That(roundTripped.nodes[1].inputPorts[0].name, Is.EqualTo("in"));
            Assert.That(roundTripped.nodes[1].outputPorts[0].name, Is.EqualTo("out"));
            Assert.That(PipelineGraph.Validate(roundTripped).IsValid, Is.True);
        }

        [Test]
        public async Task ExecutesDagInTopologicalOrder()
        {
            var order = new List<string>();
            var registry = CreateStubRegistry(order);
            var result = await new PipelineGraphExecutor().ExecuteAsync(CreateLinearGraph(), registry, CancellationToken.None);

            Assert.That(result.succeeded, Is.True, result.message);
            Assert.That(result.cancelled, Is.False);
            Assert.That(order, Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(result.executedNodeIds, Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(result.artifacts.Select(artifact => artifact.id), Is.EqualTo(new[] { "artifact-A", "artifact-B", "artifact-C" }));

            WriteEvidence(
                DagOrderEvidencePath,
                "Pipeline DAG execution order passed",
                "order=" + string.Join(",", order),
                "artifacts=" + string.Join(",", result.artifacts.Select(artifact => artifact.id)),
                "progress=" + string.Join(",", result.progressEvents.Select(progressEvent => progressEvent.stage)));
        }

        [Test]
        public void RejectsCycleBeforeExecution()
        {
            var graph = CreateCycleGraph();
            var validation = PipelineGraph.Validate(graph);

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Message, Does.Contain("cycle").IgnoreCase);

            WriteEvidence(
                CycleRejectedEvidencePath,
                "Pipeline cycle rejection passed",
                $"status={validation.Status}",
                $"message={validation.Message}");
        }

        [Test]
        public async Task CancellationPreservesCompletedArtifactsAndStopsRemainingNodes()
        {
            var order = new List<string>();
            var cancellation = new CancellationTokenSource();
            var progress = new InlineProgress<PipelineProgressEvent>(progressEvent =>
            {
                if (progressEvent.stage == PipelineProgressStage.NodeCompleted && progressEvent.nodeId == "A")
                {
                    cancellation.Cancel();
                }
            });
            var result = await new PipelineGraphExecutor().ExecuteAsync(
                CreateLinearGraph(),
                CreateStubRegistry(order),
                cancellation.Token,
                progress);

            Assert.That(result.succeeded, Is.False);
            Assert.That(result.cancelled, Is.True);
            Assert.That(order, Is.EqualTo(new[] { "A" }));
            Assert.That(result.executedNodeIds, Is.EqualTo(new[] { "A" }));
            Assert.That(result.artifacts.Select(artifact => artifact.id), Is.EqualTo(new[] { "artifact-A" }));
        }

        [Test]
        public void MissingPortsAndDuplicateIdsReturnValidationErrors()
        {
            var graph = CreateLinearGraph();
            graph.nodes[2].id = "A";
            graph.edges[0].fromPortName = "missing";

            var validation = PipelineGraph.Validate(graph);

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Message, Does.Contain("Duplicate pipeline node id 'A'"));
            Assert.That(validation.Message, Does.Contain("missing Output port 'missing'"));
        }

        [Test]
        public void MissingGraphSchemaVersionJsonReturnsValidationError()
        {
            var json = "{ \"id\": \"missing-schema\", \"nodes\": [], \"edges\": [] }";
            var validation = PipelineGraph.ValidateJson(json);

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Message, Does.Contain("schemaVersion"));
        }

        private static PipelineGraph CreateLinearGraph()
        {
            return new PipelineGraph
            {
                id = "linear-graph",
                displayName = "Linear Graph",
                nodes = new[]
                {
                    CreateNode("A", Array.Empty<PipelinePort>(), new[] { CreatePort("out", PipelinePortDirection.Output) }),
                    CreateNode("B", new[] { CreatePort("in", PipelinePortDirection.Input) }, new[] { CreatePort("out", PipelinePortDirection.Output) }),
                    CreateNode("C", new[] { CreatePort("in", PipelinePortDirection.Input) }, Array.Empty<PipelinePort>())
                },
                edges = new[]
                {
                    CreateEdge("edge-A-B", "A", "out", "B", "in"),
                    CreateEdge("edge-B-C", "B", "out", "C", "in")
                }
            };
        }

        private static PipelineGraph CreateCycleGraph()
        {
            var graph = CreateLinearGraph();
            graph.edges = graph.edges.Concat(new[]
            {
                CreateEdge("edge-C-A", "C", "out", "A", "in")
            }).ToArray();
            graph.nodes[0].inputPorts = new[] { CreatePort("in", PipelinePortDirection.Input) };
            graph.nodes[2].outputPorts = new[] { CreatePort("out", PipelinePortDirection.Output) };
            return graph;
        }

        private static PipelineNode CreateNode(string id, PipelinePort[] inputPorts, PipelinePort[] outputPorts)
        {
            return new PipelineNode
            {
                id = id,
                type = "stub",
                displayName = "Node " + id,
                inputPorts = inputPorts,
                outputPorts = outputPorts,
                parameters = new[]
                {
                    new PipelineParameter
                    {
                        name = "value",
                        value = id + "-value"
                    }
                }
            };
        }

        private static PipelinePort CreatePort(string name, PipelinePortDirection direction)
        {
            return new PipelinePort
            {
                name = name,
                direction = direction,
                dataType = "stub"
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

        private static PipelineNodeExecutorRegistry CreateStubRegistry(IList<string> order)
        {
            var registry = new PipelineNodeExecutorRegistry();
            registry.Register(new DeterministicStubNodeExecutor("stub", order));
            return registry;
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

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> onProgress;

            public InlineProgress(Action<T> onProgress)
            {
                this.onProgress = onProgress;
            }

            public void Report(T value)
            {
                onProgress(value);
            }
        }
    }
}
