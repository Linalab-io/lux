using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Pipeline
{
    [Serializable]
    public sealed class PipelineGraph
    {
        public const string CurrentSchemaVersion = "0.1";

        public string schemaVersion = CurrentSchemaVersion;
        public string id;
        public string displayName;
        public PipelineNode[] nodes = Array.Empty<PipelineNode>();
        public PipelineEdge[] edges = Array.Empty<PipelineEdge>();

        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public static PipelineGraph FromJson(string json)
        {
            return JsonUtility.FromJson<PipelineGraph>(json);
        }

        public static PipelineGraph CreateDefault()
        {
            return new PipelineGraph();
        }

        public static PipelineValidationResult ValidateJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return PipelineValidationResult.Invalid(PipelineValidationStatus.InvalidJson, "Pipeline graph JSON is empty.");
            }

            if (!JsonContainsField(json, "schemaVersion"))
            {
                return PipelineValidationResult.Invalid(PipelineValidationStatus.InvalidGraph, "Pipeline graph schemaVersion is required.");
            }

            PipelineGraph graph;
            try
            {
                graph = FromJson(json);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return PipelineValidationResult.Invalid(PipelineValidationStatus.InvalidJson, exception.Message);
            }

            return Validate(graph);
        }

        private static bool JsonContainsField(string json, string fieldName)
        {
            return json.IndexOf('"' + fieldName + '"', StringComparison.Ordinal) >= 0;
        }

        public static PipelineValidationResult Validate(PipelineGraph graph)
        {
            var errors = new List<string>();

            if (graph == null)
            {
                return PipelineValidationResult.Invalid(PipelineValidationStatus.InvalidGraph, "Pipeline graph JSON did not produce a graph.");
            }

            if (string.IsNullOrWhiteSpace(graph.schemaVersion))
            {
                errors.Add("Pipeline graph schemaVersion is required.");
            }
            else if (!string.Equals(graph.schemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            {
                errors.Add($"Pipeline graph schemaVersion '{graph.schemaVersion}' is not supported.");
            }

            var nodes = graph.nodes ?? Array.Empty<PipelineNode>();
            var edges = graph.edges ?? Array.Empty<PipelineEdge>();
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            var nodeById = new Dictionary<string, PipelineNode>(StringComparer.Ordinal);

            foreach (var node in nodes)
            {
                if (node == null)
                {
                    errors.Add("Pipeline graph contains a null node.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.id))
                {
                    errors.Add("Pipeline node id is required.");
                    continue;
                }

                if (!nodeIds.Add(node.id))
                {
                    errors.Add($"Duplicate pipeline node id '{node.id}'.");
                    continue;
                }

                nodeById[node.id] = node;
                AddDuplicatePortErrors(node, node.inputPorts, PipelinePortDirection.Input, errors);
                AddDuplicatePortErrors(node, node.outputPorts, PipelinePortDirection.Output, errors);
                AddDuplicateParameterErrors(node, errors);
            }

            var edgeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edge in edges)
            {
                if (edge == null)
                {
                    errors.Add("Pipeline graph contains a null edge.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(edge.id))
                {
                    errors.Add("Pipeline edge id is required.");
                }
                else if (!edgeIds.Add(edge.id))
                {
                    errors.Add($"Duplicate pipeline edge id '{edge.id}'.");
                }

                ValidateEdgeEndpoint(edge.fromNodeId, edge.fromPortName, PipelinePortDirection.Output, nodeById, edge.id, errors);
                ValidateEdgeEndpoint(edge.toNodeId, edge.toPortName, PipelinePortDirection.Input, nodeById, edge.id, errors);
            }

            if (errors.Count == 0 && HasCycle(nodes, edges))
            {
                errors.Add("Pipeline graph contains a cycle.");
            }

            return errors.Count == 0
                ? PipelineValidationResult.Valid()
                : PipelineValidationResult.Invalid(PipelineValidationStatus.InvalidGraph, errors.ToArray());
        }

        public static PipelineNode[] TopologicalSort(PipelineGraph graph)
        {
            var validation = Validate(graph);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.Message);
            }

            return SortValidated(graph.nodes ?? Array.Empty<PipelineNode>(), graph.edges ?? Array.Empty<PipelineEdge>()).ToArray();
        }

        private static void AddDuplicatePortErrors(PipelineNode node, PipelinePort[] ports, PipelinePortDirection direction, ICollection<string> errors)
        {
            var portNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var port in ports ?? Array.Empty<PipelinePort>())
            {
                if (port == null)
                {
                    errors.Add($"Pipeline node '{node.id}' contains a null {direction} port.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(port.name))
                {
                    errors.Add($"Pipeline node '{node.id}' has a {direction} port without a name.");
                    continue;
                }

                if (!portNames.Add(port.name))
                {
                    errors.Add($"Pipeline node '{node.id}' has duplicate {direction} port '{port.name}'.");
                }
            }
        }

        private static void AddDuplicateParameterErrors(PipelineNode node, ICollection<string> errors)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in node.parameters ?? Array.Empty<PipelineParameter>())
            {
                if (parameter == null)
                {
                    errors.Add($"Pipeline node '{node.id}' contains a null parameter.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(parameter.name))
                {
                    errors.Add($"Pipeline node '{node.id}' has a parameter without a name.");
                    continue;
                }

                if (!names.Add(parameter.name))
                {
                    errors.Add($"Pipeline node '{node.id}' has duplicate parameter '{parameter.name}'.");
                }
            }
        }

        private static void ValidateEdgeEndpoint(
            string nodeId,
            string portName,
            PipelinePortDirection direction,
            IReadOnlyDictionary<string, PipelineNode> nodeById,
            string edgeId,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                errors.Add($"Pipeline edge '{edgeId}' has a missing {direction} node id.");
                return;
            }

            if (!nodeById.TryGetValue(nodeId, out var node))
            {
                errors.Add($"Pipeline edge '{edgeId}' references missing node '{nodeId}'.");
                return;
            }

            if (string.IsNullOrWhiteSpace(portName))
            {
                errors.Add($"Pipeline edge '{edgeId}' has a missing {direction} port name.");
                return;
            }

            var ports = direction == PipelinePortDirection.Input ? node.inputPorts : node.outputPorts;
            if ((ports ?? Array.Empty<PipelinePort>()).All(port => port == null || !string.Equals(port.name, portName, StringComparison.Ordinal)))
            {
                errors.Add($"Pipeline edge '{edgeId}' references missing {direction} port '{portName}' on node '{nodeId}'.");
            }
        }

        private static bool HasCycle(PipelineNode[] nodes, PipelineEdge[] edges)
        {
            return SortValidated(nodes, edges).Count != nodes.Length;
        }

        private static List<PipelineNode> SortValidated(PipelineNode[] nodes, PipelineEdge[] edges)
        {
            var indegreeByNodeId = nodes.ToDictionary(node => node.id, _ => 0, StringComparer.Ordinal);
            var outgoingByNodeId = nodes.ToDictionary(node => node.id, _ => new List<string>(), StringComparer.Ordinal);

            foreach (var edge in edges)
            {
                outgoingByNodeId[edge.fromNodeId].Add(edge.toNodeId);
                indegreeByNodeId[edge.toNodeId]++;
            }

            var queue = new Queue<PipelineNode>(nodes.Where(node => indegreeByNodeId[node.id] == 0));
            var sorted = new List<PipelineNode>(nodes.Length);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                sorted.Add(node);

                foreach (var dependentNodeId in outgoingByNodeId[node.id])
                {
                    indegreeByNodeId[dependentNodeId]--;
                    if (indegreeByNodeId[dependentNodeId] == 0)
                    {
                        queue.Enqueue(nodes.First(candidate => string.Equals(candidate.id, dependentNodeId, StringComparison.Ordinal)));
                    }
                }
            }

            return sorted;
        }
    }

    [Serializable]
    public sealed class PipelineNode
    {
        public string id;
        public string type;
        public string displayName;
        public PipelinePort[] inputPorts = Array.Empty<PipelinePort>();
        public PipelinePort[] outputPorts = Array.Empty<PipelinePort>();
        public PipelineParameter[] parameters = Array.Empty<PipelineParameter>();

        public string GetParameterValue(string name, string fallback = null)
        {
            foreach (var parameter in parameters ?? Array.Empty<PipelineParameter>())
            {
                if (string.Equals(parameter.name, name, StringComparison.Ordinal))
                {
                    return parameter.value;
                }
            }

            return fallback;
        }
    }

    [Serializable]
    public sealed class PipelinePort
    {
        public string name;
        public PipelinePortDirection direction;
        public string dataType;
    }

    public enum PipelinePortDirection
    {
        Input,
        Output
    }

    [Serializable]
    public sealed class PipelineEdge
    {
        public string id;
        public string fromNodeId;
        public string fromPortName;
        public string toNodeId;
        public string toPortName;
    }

    [Serializable]
    public sealed class PipelineParameter
    {
        public string name;
        public string value;
    }

    [Serializable]
    public sealed class PipelineArtifact
    {
        public string id;
        public string nodeId;
        public string portName;
        public string name;
        public string kind;
        public string value;
        public string path;
    }

    [Serializable]
    public sealed class PipelineProgressEvent
    {
        public string nodeId;
        public PipelineProgressStage stage;
        public string message;
        public int completedNodes;
        public int totalNodes;
    }

    public enum PipelineProgressStage
    {
        GraphStarted,
        NodeStarted,
        NodeCompleted,
        GraphCompleted,
        GraphCancelled,
        GraphFailed
    }

    public enum PipelineValidationStatus
    {
        Valid,
        InvalidJson,
        InvalidGraph
    }

    public readonly struct PipelineValidationResult
    {
        public PipelineValidationResult(PipelineValidationStatus status, string[] errors)
        {
            Status = status;
            Errors = errors ?? Array.Empty<string>();
        }

        public PipelineValidationStatus Status { get; }
        public string[] Errors { get; }
        public bool IsValid => Status == PipelineValidationStatus.Valid;
        public string Message => IsValid ? "Pipeline graph is valid." : string.Join(" ", Errors);

        public static PipelineValidationResult Valid()
        {
            return new PipelineValidationResult(PipelineValidationStatus.Valid, Array.Empty<string>());
        }

        public static PipelineValidationResult Invalid(PipelineValidationStatus status, string error)
        {
            return Invalid(status, new[] { error });
        }

        public static PipelineValidationResult Invalid(PipelineValidationStatus status, string[] errors)
        {
            return new PipelineValidationResult(status, errors);
        }
    }

    public sealed class PipelineExecutionContext
    {
        private readonly List<PipelineArtifact> artifacts = new List<PipelineArtifact>();
        private readonly List<string> completedNodeIds = new List<string>();

        public PipelineExecutionContext(PipelineGraph graph)
        {
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public PipelineGraph Graph { get; }
        public IReadOnlyList<PipelineArtifact> Artifacts => artifacts;
        public IReadOnlyList<string> CompletedNodeIds => completedNodeIds;

        public void AddArtifact(PipelineArtifact artifact)
        {
            if (artifact == null)
            {
                return;
            }

            artifacts.Add(artifact);
        }

        public void MarkNodeCompleted(string nodeId)
        {
            completedNodeIds.Add(nodeId);
        }

        public PipelineArtifact[] GetArtifactsForNode(string nodeId)
        {
            return artifacts.Where(artifact => string.Equals(artifact.nodeId, nodeId, StringComparison.Ordinal)).ToArray();
        }
    }

    public sealed class PipelineNodeExecutionResult
    {
        public bool succeeded = true;
        public string message;
        public PipelineArtifact[] artifacts = Array.Empty<PipelineArtifact>();

        public static PipelineNodeExecutionResult Success(params PipelineArtifact[] artifacts)
        {
            return new PipelineNodeExecutionResult
            {
                succeeded = true,
                artifacts = artifacts ?? Array.Empty<PipelineArtifact>()
            };
        }

        public static PipelineNodeExecutionResult Failed(string message)
        {
            return new PipelineNodeExecutionResult
            {
                succeeded = false,
                message = message
            };
        }
    }

    public sealed class PipelineExecutionResult
    {
        public bool succeeded;
        public bool cancelled;
        public PipelineValidationResult validation;
        public string message;
        public string[] executedNodeIds = Array.Empty<string>();
        public PipelineArtifact[] artifacts = Array.Empty<PipelineArtifact>();
        public PipelineProgressEvent[] progressEvents = Array.Empty<PipelineProgressEvent>();
    }

    public interface IPipelineNodeExecutor
    {
        string NodeType { get; }
        Task<PipelineNodeExecutionResult> ExecuteAsync(PipelineNode node, PipelineExecutionContext context, CancellationToken cancellationToken);
    }

    public sealed class PipelineNodeExecutorRegistry
    {
        private readonly Dictionary<string, IPipelineNodeExecutor> executors = new Dictionary<string, IPipelineNodeExecutor>(StringComparer.Ordinal);

        public void Register(IPipelineNodeExecutor executor)
        {
            if (executor == null)
            {
                throw new ArgumentNullException(nameof(executor));
            }

            if (string.IsNullOrWhiteSpace(executor.NodeType))
            {
                throw new ArgumentException("Node executor type is required.", nameof(executor));
            }

            executors[executor.NodeType] = executor;
        }

        public bool TryGet(string nodeType, out IPipelineNodeExecutor executor)
        {
            return executors.TryGetValue(nodeType ?? string.Empty, out executor);
        }
    }

    public sealed class PipelineGraphExecutor
    {
        public async Task<PipelineExecutionResult> ExecuteAsync(
            PipelineGraph graph,
            PipelineNodeExecutorRegistry registry,
            CancellationToken cancellationToken,
            IProgress<PipelineProgressEvent> progress = null)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var progressEvents = new List<PipelineProgressEvent>();
            var validation = PipelineGraph.Validate(graph);
            if (!validation.IsValid)
            {
                Report(progress, progressEvents, null, PipelineProgressStage.GraphFailed, validation.Message, 0, 0);
                return new PipelineExecutionResult
                {
                    succeeded = false,
                    validation = validation,
                    message = validation.Message,
                    progressEvents = progressEvents.ToArray()
                };
            }

            var sortedNodes = PipelineGraph.TopologicalSort(graph);
            var context = new PipelineExecutionContext(graph);
            Report(progress, progressEvents, null, PipelineProgressStage.GraphStarted, "Pipeline graph execution started.", 0, sortedNodes.Length);

            try
            {
                foreach (var node in sortedNodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!registry.TryGet(node.type, out var executor))
                    {
                        var message = $"No executor registered for pipeline node type '{node.type}'.";
                        Report(progress, progressEvents, node.id, PipelineProgressStage.GraphFailed, message, context.CompletedNodeIds.Count, sortedNodes.Length);
                        return ToResult(false, false, validation, message, context, progressEvents);
                    }

                    Report(progress, progressEvents, node.id, PipelineProgressStage.NodeStarted, $"Pipeline node '{node.id}' started.", context.CompletedNodeIds.Count, sortedNodes.Length);
                    var nodeResult = await executor.ExecuteAsync(node, context, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (nodeResult == null || !nodeResult.succeeded)
                    {
                        var message = nodeResult?.message ?? $"Pipeline node '{node.id}' failed without a message.";
                        Report(progress, progressEvents, node.id, PipelineProgressStage.GraphFailed, message, context.CompletedNodeIds.Count, sortedNodes.Length);
                        return ToResult(false, false, validation, message, context, progressEvents);
                    }

                    foreach (var artifact in nodeResult.artifacts ?? Array.Empty<PipelineArtifact>())
                    {
                        if (string.IsNullOrWhiteSpace(artifact.nodeId))
                        {
                            artifact.nodeId = node.id;
                        }

                        context.AddArtifact(artifact);
                    }

                    context.MarkNodeCompleted(node.id);
                    Report(progress, progressEvents, node.id, PipelineProgressStage.NodeCompleted, $"Pipeline node '{node.id}' completed.", context.CompletedNodeIds.Count, sortedNodes.Length);
                }
            }
            catch (OperationCanceledException)
            {
                Report(progress, progressEvents, null, PipelineProgressStage.GraphCancelled, "Pipeline graph execution cancelled.", context.CompletedNodeIds.Count, sortedNodes.Length);
                return ToResult(false, true, validation, "Pipeline graph execution cancelled.", context, progressEvents);
            }

            Report(progress, progressEvents, null, PipelineProgressStage.GraphCompleted, "Pipeline graph execution completed.", context.CompletedNodeIds.Count, sortedNodes.Length);
            return ToResult(true, false, validation, "Pipeline graph execution completed.", context, progressEvents);
        }

        private static PipelineExecutionResult ToResult(
            bool succeeded,
            bool cancelled,
            PipelineValidationResult validation,
            string message,
            PipelineExecutionContext context,
            List<PipelineProgressEvent> progressEvents)
        {
            return new PipelineExecutionResult
            {
                succeeded = succeeded,
                cancelled = cancelled,
                validation = validation,
                message = message,
                executedNodeIds = context.CompletedNodeIds.ToArray(),
                artifacts = context.Artifacts.ToArray(),
                progressEvents = progressEvents.ToArray()
            };
        }

        private static void Report(
            IProgress<PipelineProgressEvent> progress,
            ICollection<PipelineProgressEvent> progressEvents,
            string nodeId,
            PipelineProgressStage stage,
            string message,
            int completedNodes,
            int totalNodes)
        {
            var progressEvent = new PipelineProgressEvent
            {
                nodeId = nodeId,
                stage = stage,
                message = message,
                completedNodes = completedNodes,
                totalNodes = totalNodes
            };
            progressEvents.Add(progressEvent);
            progress?.Report(progressEvent);
        }
    }

    public sealed class DeterministicStubNodeExecutor : IPipelineNodeExecutor
    {
        private readonly IList<string> executionOrder;
        private readonly Func<PipelineNode, PipelineExecutionContext, CancellationToken, Task> beforeCompleteAsync;

        public DeterministicStubNodeExecutor(
            string nodeType,
            IList<string> executionOrder,
            Func<PipelineNode, PipelineExecutionContext, CancellationToken, Task> beforeCompleteAsync = null)
        {
            NodeType = nodeType;
            this.executionOrder = executionOrder;
            this.beforeCompleteAsync = beforeCompleteAsync;
        }

        public string NodeType { get; }

        public async Task<PipelineNodeExecutionResult> ExecuteAsync(PipelineNode node, PipelineExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            executionOrder?.Add(node.id);
            if (beforeCompleteAsync != null)
            {
                await beforeCompleteAsync(node, context, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return PipelineNodeExecutionResult.Success(new PipelineArtifact
            {
                id = $"artifact-{node.id}",
                nodeId = node.id,
                portName = FirstOutputPortName(node),
                name = $"Artifact {node.id}",
                kind = "stub",
                value = node.GetParameterValue("value", node.id)
            });
        }

        private static string FirstOutputPortName(PipelineNode node)
        {
            var outputs = node.outputPorts ?? Array.Empty<PipelinePort>();
            return outputs.Length == 0 ? string.Empty : outputs[0].name;
        }
    }
}
