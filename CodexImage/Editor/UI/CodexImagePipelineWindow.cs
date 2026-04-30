using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityCodexImage.Editor.Pipeline;
using UnityEditor;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.UI
{
    public sealed class CodexImagePipelineWindow : EditorWindow
    {
        private PipelineGraphExecutor executor;
        private PipelineNodeExecutorRegistry registry;
        private CancellationTokenSource cancellationTokenSource;
        private PipelineExecutionResult lastResult;
        private readonly List<PipelineProgressEvent> progressEvents = new List<PipelineProgressEvent>();
        private Vector2 scrollPosition;
        private bool isRunning;

        private PipelineGraph currentGraph;
        private Dictionary<string, Rect> nodeRects = new Dictionary<string, Rect>();
        private Vector2 graphScrollPosition;
        private string selectedNodeId;
        private const float NodeWidth = 160f;
        private const float NodeHeight = 60f;

        [MenuItem("Tools/Linalab/Codex Image Pipeline")]
        private static void Open()
        {
            GetWindow<CodexImagePipelineWindow>("Codex Image Pipeline");
        }

        private void OnEnable()
        {
            executor = new PipelineGraphExecutor();
            registry = new PipelineNodeExecutorRegistry();
            
            var executionOrder = new List<string>();
            registry.Register(new DeterministicStubNodeExecutor(CodexImagePipelineNodeTypes.UnityContext, executionOrder));
            registry.Register(new DeterministicStubNodeExecutor(CodexImagePipelineNodeTypes.OutputDirectory, executionOrder));
            registry.Register(new DeterministicStubNodeExecutor(CodexImagePipelineNodeTypes.PromptTemplate, executionOrder, (node, ctx, ct) =>
            {
                ctx.AddArtifact(new PipelineArtifact
                {
                    id = $"artifact-{node.id}-manifest",
                    nodeId = node.id,
                    portName = "manifest",
                    name = "Generated Asset Manifest",
                    kind = CodexImagePipelineArtifactKinds.GeneratedAssetManifest,
                    value = "{}",
                    path = "Assets/StubOutput/manifest.json"
                });
                return Task.CompletedTask;
            }));
            registry.Register(new DeterministicStubNodeExecutor(CodexImagePipelineNodeTypes.CodexGeneration, executionOrder));
            registry.Register(new DeterministicStubNodeExecutor(CodexImagePipelineNodeTypes.Segmentation, executionOrder));
            registry.Register(new DeterministicStubNodeExecutor(CodexImagePipelineNodeTypes.MaskPostProcessing, executionOrder));

            currentGraph = CreateStubGraph();
            InitializeNodePositions();
        }

        private void OnDisable()
        {
            CancelPipeline();
        }

        private void InitializeNodePositions()
        {
            nodeRects.Clear();
            if (currentGraph == null || currentGraph.nodes == null) return;

            nodeRects["node-context"] = new Rect(50, 50, NodeWidth, NodeHeight);
            nodeRects["node-output"] = new Rect(50, 150, NodeWidth, NodeHeight);
            nodeRects["node-template"] = new Rect(280, 100, NodeWidth, NodeHeight);
            nodeRects["node-generation"] = new Rect(510, 100, NodeWidth, NodeHeight);
            nodeRects["node-segmentation"] = new Rect(740, 100, NodeWidth, NodeHeight);
            nodeRects["node-post"] = new Rect(970, 100, NodeWidth, NodeHeight);

            float x = 50f;
            float y = 250f;
            foreach (var node in currentGraph.nodes)
            {
                if (!nodeRects.ContainsKey(node.id))
                {
                    nodeRects[node.id] = new Rect(x, y, NodeWidth, NodeHeight);
                    x += NodeWidth + 50f;
                    if (x > 1000f) { x = 50f; y += NodeHeight + 50f; }
                }
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();

            DrawGraphCanvas();
            DrawInspectorPanel();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            using (new EditorGUI.DisabledScope(isRunning))
            {
                if (GUILayout.Button("Run Stub Pipeline", EditorStyles.toolbarButton))
                {
                    RunStubPipeline();
                }
            }

            using (new EditorGUI.DisabledScope(!isRunning))
            {
                if (GUILayout.Button("Cancel", EditorStyles.toolbarButton))
                {
                    CancelPipeline();
                }
            }

            GUILayout.FlexibleSpace();

            if (isRunning || progressEvents.Count > 0)
            {
                var lastEvent = progressEvents.LastOrDefault();
                var progress = lastEvent != null && lastEvent.totalNodes > 0 
                    ? (float)lastEvent.completedNodes / lastEvent.totalNodes 
                    : 0f;
                
                var statusText = lastEvent != null ? lastEvent.message : "Starting...";
                Rect rect = GUILayoutUtility.GetRect(200, 18);
                rect.y += 2;
                rect.height -= 4;
                EditorGUI.ProgressBar(rect, progress, statusText);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawGraphCanvas()
        {
            Rect canvasRect = GUILayoutUtility.GetRect(0, 10000, 0, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.Box(canvasRect, GUIContent.none, EditorStyles.helpBox);

            Event e = Event.current;
            if (e.type == EventType.MouseDrag && e.button == 2 && canvasRect.Contains(e.mousePosition))
            {
                graphScrollPosition += e.delta;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && canvasRect.Contains(e.mousePosition) && !IsMouseOverNode(e.mousePosition - canvasRect.position))
            {
                selectedNodeId = null;
                GUI.FocusControl(null);
                Repaint();
            }

            GUI.BeginClip(canvasRect);

            if (currentGraph != null)
            {
                DrawEdges();
                DrawNodes();
            }

            GUI.EndClip();
        }

        private bool IsMouseOverNode(Vector2 localMousePosition)
        {
            if (currentGraph == null || currentGraph.nodes == null)
            {
                return false;
            }

            foreach (var node in currentGraph.nodes)
            {
                if (!nodeRects.TryGetValue(node.id, out var baseRect))
                {
                    continue;
                }

                var rect = new Rect(baseRect.position + graphScrollPosition, baseRect.size);
                if (rect.Contains(localMousePosition))
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawEdges()
        {
            if (currentGraph.edges == null || currentGraph.nodes == null) return;

            foreach (var edge in currentGraph.edges)
            {
                if (!nodeRects.TryGetValue(edge.fromNodeId, out Rect fromRect) ||
                    !nodeRects.TryGetValue(edge.toNodeId, out Rect toRect))
                    continue;

                var fromNode = currentGraph.nodes.FirstOrDefault(n => n.id == edge.fromNodeId);
                var toNode = currentGraph.nodes.FirstOrDefault(n => n.id == edge.toNodeId);

                int fromPortIndex = Array.FindIndex(fromNode?.outputPorts ?? Array.Empty<PipelinePort>(), p => p.name == edge.fromPortName);
                int toPortIndex = Array.FindIndex(toNode?.inputPorts ?? Array.Empty<PipelinePort>(), p => p.name == edge.toPortName);

                Vector2 startPos = GetPortPosition(fromRect, fromPortIndex, true, fromNode?.outputPorts?.Length ?? 1);
                Vector2 endPos = GetPortPosition(toRect, toPortIndex, false, toNode?.inputPorts?.Length ?? 1);

                startPos += graphScrollPosition;
                endPos += graphScrollPosition;

                Handles.DrawBezier(
                    startPos,
                    endPos,
                    startPos + Vector2.right * 50f,
                    endPos - Vector2.right * 50f,
                    Color.white,
                    null,
                    2f
                );
            }
        }

        private Vector2 GetPortPosition(Rect nodeRect, int portIndex, bool isOutput, int totalPorts)
        {
            if (portIndex < 0) portIndex = 0;
            if (totalPorts <= 0) totalPorts = 1;

            float yOffset = nodeRect.height * ((portIndex + 1f) / (totalPorts + 1f));
            float x = isOutput ? nodeRect.xMax : nodeRect.xMin;
            return new Vector2(x, nodeRect.y + yOffset);
        }

        private void DrawNodes()
        {
            if (currentGraph.nodes == null) return;

            Event e = Event.current;

            foreach (var node in currentGraph.nodes)
            {
                if (!nodeRects.TryGetValue(node.id, out Rect baseRect)) continue;

                Rect rect = new Rect(baseRect.position + graphScrollPosition, baseRect.size);

                if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    selectedNodeId = node.id;
                    GUI.FocusControl(null);
                    e.Use();
                    Repaint();
                }

                Color statusColor = new Color(0.25f, 0.25f, 0.25f);
                if (isRunning)
                {
                    var lastEvent = progressEvents.LastOrDefault(evt => evt.nodeId == node.id);
                    if (lastEvent != null)
                    {
                        if (lastEvent.stage == PipelineProgressStage.NodeStarted) statusColor = new Color(0.8f, 0.8f, 0.2f);
                        else if (lastEvent.stage == PipelineProgressStage.NodeCompleted) statusColor = new Color(0.2f, 0.6f, 0.2f);
                        else if (lastEvent.stage == PipelineProgressStage.GraphFailed) statusColor = new Color(0.8f, 0.2f, 0.2f);
                    }
                }
                else if (lastResult != null)
                {
                    if (lastResult.executedNodeIds.Contains(node.id))
                    {
                        statusColor = lastResult.succeeded ? new Color(0.2f, 0.6f, 0.2f) : new Color(0.8f, 0.2f, 0.2f);
                    }
                }

                bool isSelected = selectedNodeId == node.id;
                if (isSelected)
                {
                    EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), new Color(0.3f, 0.6f, 1f));
                }

                EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
                Rect headerRect = new Rect(rect.x, rect.y, rect.width, 20);
                EditorGUI.DrawRect(headerRect, statusColor);

                GUI.Label(new Rect(rect.x + 5, rect.y + 2, rect.width - 10, 18), node.id, EditorStyles.boldLabel);
                GUI.Label(new Rect(rect.x + 5, rect.y + 24, rect.width - 10, 18), node.type, EditorStyles.whiteMiniLabel);

                DrawPorts(rect, node.inputPorts, false);
                DrawPorts(rect, node.outputPorts, true);
            }
        }

        private void DrawPorts(Rect nodeRect, PipelinePort[] ports, bool isOutput)
        {
            if (ports == null) return;
            for (int i = 0; i < ports.Length; i++)
            {
                Vector2 pos = GetPortPosition(nodeRect, i, isOutput, ports.Length);
                Rect portRect = new Rect(pos.x - 4, pos.y - 4, 8, 8);
                EditorGUI.DrawRect(portRect, Color.cyan);
            }
        }

        private void DrawInspectorPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(300));
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (!string.IsNullOrEmpty(selectedNodeId) && currentGraph != null)
            {
                var node = currentGraph.nodes.FirstOrDefault(n => n.id == selectedNodeId);
                if (node != null)
                {
                    EditorGUILayout.LabelField("Node Details", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox($"ID: {node.id}\nType: {node.type}", MessageType.None);

                    if (node.parameters != null && node.parameters.Length > 0)
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
                        foreach (var p in node.parameters)
                        {
                            EditorGUILayout.LabelField(p.name, p.value);
                        }
                    }

                    if (lastResult != null && lastResult.artifacts != null)
                    {
                        var nodeArtifacts = lastResult.artifacts.Where(a => a.nodeId == node.id).ToList();
                        if (nodeArtifacts.Count > 0)
                        {
                            EditorGUILayout.Space();
                            EditorGUILayout.LabelField("Generated Artifacts", EditorStyles.boldLabel);
                            foreach (var a in nodeArtifacts)
                            {
                                EditorGUILayout.LabelField($"- {a.name} ({a.kind})");
                                if (!string.IsNullOrEmpty(a.path))
                                    EditorGUILayout.SelectableLabel(a.path, EditorStyles.textField, GUILayout.Height(20));
                                if (!string.IsNullOrEmpty(a.value))
                                    EditorGUILayout.SelectableLabel(a.value, EditorStyles.textField, GUILayout.Height(40));
                            }
                        }
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("No node selected.", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pipeline Result", EditorStyles.boldLabel);
            if (lastResult != null)
            {
                EditorGUILayout.HelpBox(lastResult.message, lastResult.succeeded ? MessageType.Info : MessageType.Error);

                var manifestArtifact = lastResult.artifacts?.FirstOrDefault(a => a.kind == CodexImagePipelineArtifactKinds.GeneratedAssetManifest);
                if (manifestArtifact != null)
                {
                    EditorGUILayout.LabelField("Manifest Path", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(manifestArtifact.path, EditorStyles.textField, GUILayout.Height(20));
                }
            }
            else
            {
                EditorGUILayout.LabelField("Not run yet.");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Logs", EditorStyles.boldLabel);
            foreach (var evt in progressEvents)
            {
                EditorGUILayout.LabelField($"[{evt.stage}] {evt.nodeId ?? "Graph"}: {evt.message}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private async void RunStubPipeline()
        {
            if (isRunning) return;

            isRunning = true;
            progressEvents.Clear();
            lastResult = null;
            cancellationTokenSource = new CancellationTokenSource();

            currentGraph = CreateStubGraph();
            InitializeNodePositions();

            var progress = new Progress<PipelineProgressEvent>(evt => 
            {
                progressEvents.Add(evt);
                Repaint();
            });

            try
            {
                lastResult = await executor.ExecuteAsync(currentGraph, registry, cancellationTokenSource.Token, progress);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Pipeline execution failed: {ex}");
            }
            finally
            {
                isRunning = false;
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                Repaint();
            }
        }

        private void CancelPipeline()
        {
            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource.Cancel();
            }
        }

        private PipelineGraph CreateStubGraph()
        {
            return new PipelineGraph
            {
                id = "stub-pipeline",
                displayName = "Stub Pipeline",
                nodes = new[]
                {
                    new PipelineNode { id = "node-context", type = CodexImagePipelineNodeTypes.UnityContext, parameters = new[] { new PipelineParameter { name = "value", value = "Stub Context" } }, outputPorts = new[] { new PipelinePort { name = "out", direction = PipelinePortDirection.Output } } },
                    new PipelineNode { id = "node-output", type = CodexImagePipelineNodeTypes.OutputDirectory, parameters = new[] { new PipelineParameter { name = "value", value = "Assets/StubOutput" } }, outputPorts = new[] { new PipelinePort { name = "out", direction = PipelinePortDirection.Output } } },
                    new PipelineNode { id = "node-template", type = CodexImagePipelineNodeTypes.PromptTemplate, parameters = new[] { new PipelineParameter { name = "value", value = "Stub Template" } }, inputPorts = new[] { new PipelinePort { name = "in", direction = PipelinePortDirection.Input } }, outputPorts = new[] { new PipelinePort { name = "out", direction = PipelinePortDirection.Output } } },
                    new PipelineNode { id = "node-generation", type = CodexImagePipelineNodeTypes.CodexGeneration, parameters = new[] { new PipelineParameter { name = "value", value = "Stub Image" } }, inputPorts = new[] { new PipelinePort { name = "in", direction = PipelinePortDirection.Input } }, outputPorts = new[] { new PipelinePort { name = "out", direction = PipelinePortDirection.Output } } },
                    new PipelineNode { id = "node-segmentation", type = CodexImagePipelineNodeTypes.Segmentation, parameters = new[] { new PipelineParameter { name = "value", value = "Stub Segments" } }, inputPorts = new[] { new PipelinePort { name = "in", direction = PipelinePortDirection.Input } }, outputPorts = new[] { new PipelinePort { name = "out", direction = PipelinePortDirection.Output } } },
                    new PipelineNode { id = "node-post", type = CodexImagePipelineNodeTypes.MaskPostProcessing, parameters = new[] { new PipelineParameter { name = "value", value = "Stub Masks" } }, inputPorts = new[] { new PipelinePort { name = "in", direction = PipelinePortDirection.Input } } }
                },
                edges = new[]
                {
                    new PipelineEdge { id = "edge-1", fromNodeId = "node-context", fromPortName = "out", toNodeId = "node-template", toPortName = "in" },
                    new PipelineEdge { id = "edge-2", fromNodeId = "node-output", fromPortName = "out", toNodeId = "node-template", toPortName = "in" },
                    new PipelineEdge { id = "edge-3", fromNodeId = "node-template", fromPortName = "out", toNodeId = "node-generation", toPortName = "in" },
                    new PipelineEdge { id = "edge-4", fromNodeId = "node-generation", fromPortName = "out", toNodeId = "node-segmentation", toPortName = "in" },
                    new PipelineEdge { id = "edge-5", fromNodeId = "node-segmentation", fromPortName = "out", toNodeId = "node-post", toPortName = "in" }
                }
            };
        }
    }
}
