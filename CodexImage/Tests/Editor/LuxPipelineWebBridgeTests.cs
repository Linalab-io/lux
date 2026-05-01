using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityCodexImage.Editor.Pipeline;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Linalab.UnityCodexImage.Editor.Tests
{
    public sealed class LuxPipelineWebBridgeTests
    {
        [Test]
        public async Task bridge_deserializes_valid_graph_from_event_payload()
        {
            var completed = new List<PipelineExecutionResult>();
            var bridge = new LuxPipelineWebBridge(
                () => new FakeWebSocketClient(),
                () => CreateStubRegistry());
            bridge.OnExecutionComplete += completed.Add;

            var result = await bridge.HandleEventJsonAsync(CreateExecuteGraphEnvelope(CreateSingleNodeGraph()), CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.succeeded, Is.True, result.message);
            Assert.That(result.executedNodeIds, Is.EqualTo(new[] { "stub-node" }));
            Assert.That(completed, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task bridge_rejects_invalid_graph_and_reports_error()
        {
            var errors = new List<string>();
            var bridge = new LuxPipelineWebBridge(
                () => new FakeWebSocketClient(),
                () => CreateStubRegistry());
            bridge.OnError += errors.Add;
            LogAssert.Expect(LogType.Error, "Pipeline graph schemaVersion is required.");

            var result = await bridge.HandleEventJsonAsync(CreateExecuteGraphEnvelope(new PipelineGraph { id = "invalid", schemaVersion = string.Empty }), CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.succeeded, Is.False);
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("schemaVersion"));
        }

        [Test]
        public void bridge_progress_reporting_formats_events_correctly()
        {
            var progressEvent = new PipelineProgressEvent
            {
                stage = PipelineProgressStage.NodeStarted,
                nodeId = "prompt",
                message = "Pipeline node 'prompt' started.",
                completedNodes = 2,
                totalNodes = 6
            };

            var json = LuxPipelineWebBridge.CreateProgressEnvelopeJson("session-1", "graph-1", progressEvent);

            Assert.That(json, Does.Contain("\"schema_version\":1"));
            Assert.That(json, Does.Contain("\"category\":\"tool\""));
            Assert.That(json, Does.Contain("\"source\":\"lux-pipeline-bridge\""));
            Assert.That(json, Does.Contain("\"session_id\":\"session-1\""));
            Assert.That(json, Does.Contain("\"kind\":\"pipeline-progress\""));
            Assert.That(json, Does.Contain("\"graphId\":\"graph-1\""));
            Assert.That(json, Does.Contain("\"stage\":\"NodeStarted\""));
            Assert.That(json, Does.Contain("\"nodeId\":\"prompt\""));
            Assert.That(json, Does.Contain("\"completedNodes\":2"));
            Assert.That(json, Does.Contain("\"totalNodes\":6"));
        }

        private static string CreateExecuteGraphEnvelope(PipelineGraph graph)
        {
            return "{"
                + "\"schema_version\":1,"
                + "\"event_id\":\"execute-1\","
                + "\"category\":\"tool\","
                + "\"source\":\"test\","
                + "\"session_id\":\"session-1\","
                + "\"captured_at_utc\":\"2026-05-01T00:00:00.0000000Z\","
                + "\"payload\":{"
                + "\"kind\":\"execute-graph\","
                + "\"graphId\":\"graph-1\","
                + "\"graph\":" + graph.ToJson(false)
                + "}"
                + "}";
        }

        private static PipelineGraph CreateSingleNodeGraph()
        {
            return new PipelineGraph
            {
                id = "graph-1",
                nodes = new[]
                {
                    new PipelineNode
                    {
                        id = "stub-node",
                        type = "stub",
                        outputPorts = new[]
                        {
                            new PipelinePort
                            {
                                name = "out",
                                direction = PipelinePortDirection.Output,
                                dataType = "stub"
                            }
                        }
                    }
                }
            };
        }

        private static PipelineNodeExecutorRegistry CreateStubRegistry()
        {
            var registry = new PipelineNodeExecutorRegistry();
            registry.Register(new DeterministicStubNodeExecutor("stub", new List<string>()));
            return registry;
        }

        private sealed class FakeWebSocketClient : ILuxPipelineWebSocketClient
        {
            public bool IsConnected => false;

            public Task ConnectAsync(Uri uri, string token, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task<string> ReceiveTextAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<string>(null);
            }

            public Task SendTextAsync(string message, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }
    }
}
