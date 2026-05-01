using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityCodexImage.Editor.Backends.Codex;
using Linalab.UnityCodexImage.Editor.Backends.Segmentation;
using UnityEditor;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Pipeline
{
    public sealed class LuxPipelineWebBridge : IDisposable
    {
        private const string Source = "lux-pipeline-bridge";
        private readonly Func<ILuxPipelineWebSocketClient> socketFactory;
        private readonly Func<PipelineNodeExecutorRegistry> registryFactory;
        private readonly PipelineGraphExecutor graphExecutor;
        private ILuxPipelineWebSocketClient socket;
        private CancellationTokenSource cancellation;
        private Task receiveLoop;
        private string sessionId = "unity-editor";

        public LuxPipelineWebBridge()
            : this(() => new ClientWebSocketTransport(), CreateDefaultRegistry, new PipelineGraphExecutor())
        {
        }

        public LuxPipelineWebBridge(
            Func<ILuxPipelineWebSocketClient> socketFactory,
            Func<PipelineNodeExecutorRegistry> registryFactory = null,
            PipelineGraphExecutor graphExecutor = null)
        {
            this.socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            this.registryFactory = registryFactory ?? CreateDefaultRegistry;
            this.graphExecutor = graphExecutor ?? new PipelineGraphExecutor();
        }

        public event Action<PipelineProgressEvent> OnProgress;
        public event Action<PipelineExecutionResult> OnExecutionComplete;
        public event Action<string> OnError;

        public void Connect(string gatewayUrl, string token)
        {
            if (string.IsNullOrWhiteSpace(gatewayUrl))
            {
                Warn("Lux pipeline web bridge gateway URL is not configured.");
                return;
            }

            Disconnect();
            cancellation = new CancellationTokenSource();
            socket = socketFactory();
            receiveLoop = ConnectAndReceiveAsync(gatewayUrl, token, cancellation.Token);
        }

        public void Disconnect()
        {
            if (cancellation != null)
            {
                cancellation.Cancel();
                cancellation.Dispose();
                cancellation = null;
            }

            if (socket != null)
            {
                socket.Dispose();
                socket = null;
            }

            receiveLoop = null;
        }

        public void Dispose()
        {
            Disconnect();
        }

        public async Task<PipelineExecutionResult> HandleEventJsonAsync(string eventJson, CancellationToken cancellationToken)
        {
            if (!TryDeserializeExecutionRequest(eventJson, out var request, out var error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    await ReportErrorAsync(string.Empty, string.Empty, error, cancellationToken);
                }

                return null;
            }

            sessionId = string.IsNullOrWhiteSpace(request.sessionId) ? sessionId : request.sessionId;
            return await ExecuteRequestAsync(request, cancellationToken);
        }

        public static bool TryDeserializeExecutionRequest(string eventJson, out LuxPipelineExecutionRequest request, out string error)
        {
            request = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(eventJson))
            {
                return false;
            }

            if (!string.Equals(ExtractString(eventJson, "category"), "tool", StringComparison.Ordinal))
            {
                return false;
            }

            var payloadJson = ExtractJsonValue(eventJson, "payload");
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return false;
            }

            if (!string.Equals(ExtractString(payloadJson, "kind"), "execute-graph", StringComparison.Ordinal))
            {
                return false;
            }

            var graphJson = ExtractJsonValue(payloadJson, "graph");
            if (string.IsNullOrWhiteSpace(graphJson))
            {
                error = "execute-graph payload did not include graph JSON.";
                return false;
            }

            request = new LuxPipelineExecutionRequest
            {
                graphId = ExtractString(payloadJson, "graphId"),
                graphJson = graphJson,
                sessionId = ExtractString(eventJson, "session_id")
            };
            return true;
        }

        public static string CreateProgressEnvelopeJson(string sessionId, string graphId, PipelineProgressEvent progressEvent)
        {
            return CreateEnvelopeJson(sessionId, "pipeline-progress", graphId, progressEvent, null, null);
        }

        public static string CreateErrorEnvelopeJson(string sessionId, string graphId, string message)
        {
            return CreateEnvelopeJson(sessionId, "pipeline-error", graphId, null, message, false);
        }

        private async Task ConnectAndReceiveAsync(string gatewayUrl, string token, CancellationToken cancellationToken)
        {
            try
            {
                var eventsUri = BuildEventsUri(gatewayUrl);
                await socket.ConnectAsync(eventsUri, token, cancellationToken);
                while (!cancellationToken.IsCancellationRequested && socket.IsConnected)
                {
                    var message = await socket.ReceiveTextAsync(cancellationToken);
                    if (message == null)
                    {
                        break;
                    }

                    await HandleEventJsonAsync(message, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception) when (exception is WebSocketException || exception is IOException || exception is InvalidOperationException || exception is UriFormatException || exception is ObjectDisposedException)
            {
                Warn("Lux pipeline web bridge could not connect to gateway: " + exception.Message);
                OnError?.Invoke(exception.Message);
            }
        }

        private async Task<PipelineExecutionResult> ExecuteRequestAsync(LuxPipelineExecutionRequest request, CancellationToken cancellationToken)
        {
            PipelineGraph graph;
            try
            {
                graph = PipelineGraph.FromJson(request.graphJson);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                await ReportErrorAsync(request.sessionId, request.graphId, "Pipeline graph JSON was invalid: " + exception.Message, cancellationToken);
                return null;
            }

            var validation = PipelineGraph.Validate(graph);
            if (!validation.IsValid)
            {
                await ReportErrorAsync(request.sessionId, request.graphId, validation.Message, cancellationToken);
                return new PipelineExecutionResult
                {
                    succeeded = false,
                    validation = validation,
                    message = validation.Message
                };
            }

            try
            {
                var progress = new Progress<PipelineProgressEvent>(progressEvent =>
                {
                    OnProgress?.Invoke(progressEvent);
                    _ = SendProgressAsync(request.sessionId, request.graphId, progressEvent, CancellationToken.None);
                });
                var result = await graphExecutor.ExecuteAsync(graph, registryFactory(), cancellationToken, progress);
                OnExecutionComplete?.Invoke(result);
                if (!result.succeeded && !result.cancelled)
                {
                    await ReportErrorAsync(request.sessionId, request.graphId, result.message, cancellationToken);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                var progressEvent = new PipelineProgressEvent
                {
                    stage = PipelineProgressStage.GraphCancelled,
                    message = "Pipeline graph execution cancelled."
                };
                OnProgress?.Invoke(progressEvent);
                await SendProgressAsync(request.sessionId, request.graphId, progressEvent, CancellationToken.None);
                return new PipelineExecutionResult { succeeded = false, cancelled = true, validation = validation, message = progressEvent.message };
            }
            catch (Exception exception)
            {
                await ReportErrorAsync(request.sessionId, request.graphId, "Pipeline graph execution failed: " + exception.Message, cancellationToken);
                return new PipelineExecutionResult { succeeded = false, validation = validation, message = exception.Message };
            }
        }

        private async Task SendProgressAsync(string eventSessionId, string graphId, PipelineProgressEvent progressEvent, CancellationToken cancellationToken)
        {
            if (socket == null || !socket.IsConnected)
            {
                return;
            }

            await socket.SendTextAsync(CreateProgressEnvelopeJson(ResolveSessionId(eventSessionId), graphId, progressEvent), cancellationToken);
        }

        private async Task ReportErrorAsync(string eventSessionId, string graphId, string message, CancellationToken cancellationToken)
        {
            OnError?.Invoke(message);
            Debug.LogError(message);
            if (socket != null && socket.IsConnected)
            {
                await socket.SendTextAsync(CreateErrorEnvelopeJson(ResolveSessionId(eventSessionId), graphId, message), cancellationToken);
            }
        }

        private string ResolveSessionId(string eventSessionId)
        {
            return string.IsNullOrWhiteSpace(eventSessionId) ? sessionId : eventSessionId;
        }

        private static PipelineNodeExecutorRegistry CreateDefaultRegistry()
        {
            var registry = new PipelineNodeExecutorRegistry();
            registry.Register(new UnityContextNodeExecutor(new UnityAiBridgeContextAdapter()));
            registry.Register(new OutputDirectoryNodeExecutor());
            registry.Register(new PromptTemplateNodeExecutor());
            registry.Register(new CodexGenerationNodeExecutor());
            registry.Register(new SegmentationNodeExecutor());
            registry.Register(new MaskPostProcessingNodeExecutor());
            return registry;
        }

        private static Uri BuildEventsUri(string gatewayUrl)
        {
            var builder = new UriBuilder(gatewayUrl);
            if (string.IsNullOrEmpty(builder.Path) || builder.Path == "/")
            {
                builder.Path = "/events";
            }

            if (string.IsNullOrEmpty(builder.Query))
            {
                builder.Query = "role=unity&client_id=lux-pipeline-bridge";
            }

            return builder.Uri;
        }

        private static string CreateEnvelopeJson(string envelopeSessionId, string kind, string graphId, PipelineProgressEvent progressEvent, string message, bool? succeeded)
        {
            var payload = new LuxPipelineBridgePayload
            {
                kind = kind,
                graphId = graphId ?? string.Empty,
                stage = progressEvent == null ? string.Empty : progressEvent.stage.ToString(),
                nodeId = progressEvent == null ? string.Empty : progressEvent.nodeId,
                message = progressEvent == null ? message : progressEvent.message,
                completedNodes = progressEvent == null ? 0 : progressEvent.completedNodes,
                totalNodes = progressEvent == null ? 0 : progressEvent.totalNodes,
                succeeded = succeeded.HasValue && succeeded.Value
            };

            var envelope = new LuxPipelineBridgeEnvelope
            {
                schema_version = 1,
                event_id = Guid.NewGuid().ToString("N"),
                category = "tool",
                source = Source,
                session_id = string.IsNullOrWhiteSpace(envelopeSessionId) ? "unity-editor" : envelopeSessionId,
                captured_at_utc = DateTime.UtcNow.ToString("O"),
                payload = payload
            };
            return JsonUtility.ToJson(envelope, false);
        }

        private static string ExtractString(string json, string fieldName)
        {
            var value = ExtractJsonValue(json, fieldName);
            return string.IsNullOrEmpty(value) || value.Length < 2 || value[0] != '"' ? string.Empty : UnescapeJsonString(value.Substring(1, value.Length - 2));
        }

        private static string ExtractJsonValue(string json, string fieldName)
        {
            var key = "\"" + fieldName + "\"";
            var keyIndex = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return string.Empty;
            }

            var colon = json.IndexOf(':', keyIndex + key.Length);
            if (colon < 0)
            {
                return string.Empty;
            }

            var start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
            {
                start++;
            }

            if (start >= json.Length)
            {
                return string.Empty;
            }

            if (json[start] == '"')
            {
                return ExtractQuoted(json, start);
            }

            if (json[start] == '{' || json[start] == '[')
            {
                return ExtractBalanced(json, start);
            }

            var end = start;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']')
            {
                end++;
            }

            return json.Substring(start, end - start).Trim();
        }

        private static string ExtractQuoted(string json, int start)
        {
            var escaped = false;
            for (var index = start + 1; index < json.Length; index++)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (json[index] == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (json[index] == '"')
                {
                    return json.Substring(start, index - start + 1);
                }
            }

            return string.Empty;
        }

        private static string ExtractBalanced(string json, int start)
        {
            var open = json[start];
            var close = open == '{' ? '}' : ']';
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var index = start; index < json.Length; index++)
            {
                var character = json[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                }
                else if (character == open)
                {
                    depth++;
                }
                else if (character == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return json.Substring(start, index - start + 1);
                    }
                }
            }

            return string.Empty;
        }

        private static string UnescapeJsonString(string value)
        {
            return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static void Warn(string message)
        {
            Debug.LogWarning(message);
        }

        [Serializable]
        private sealed class LuxPipelineBridgeEnvelope
        {
            public int schema_version;
            public string event_id;
            public string category;
            public string source;
            public string session_id;
            public string captured_at_utc;
            public LuxPipelineBridgePayload payload;
        }

        [Serializable]
        private sealed class LuxPipelineBridgePayload
        {
            public string kind;
            public string graphId;
            public string stage;
            public string nodeId;
            public string message;
            public int completedNodes;
            public int totalNodes;
            public bool succeeded;
        }
    }

    public sealed class LuxPipelineExecutionRequest
    {
        public string graphId;
        public string graphJson;
        public string sessionId;
    }

    public interface ILuxPipelineWebSocketClient : IDisposable
    {
        bool IsConnected { get; }
        Task ConnectAsync(Uri uri, string token, CancellationToken cancellationToken);
        Task<string> ReceiveTextAsync(CancellationToken cancellationToken);
        Task SendTextAsync(string message, CancellationToken cancellationToken);
    }

    public sealed class ClientWebSocketTransport : ILuxPipelineWebSocketClient
    {
        private readonly ClientWebSocket webSocket = new ClientWebSocket();

        public bool IsConnected => webSocket.State == WebSocketState.Open;

        public async Task ConnectAsync(Uri uri, string token, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(token))
            {
                webSocket.Options.SetRequestHeader("x-lux-token", token);
            }

            await webSocket.ConnectAsync(uri, cancellationToken);
        }

        public async Task<string> ReceiveTextAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using (var stream = new MemoryStream())
            {
                while (true)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return null;
                    }

                    stream.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        return Encoding.UTF8.GetString(stream.ToArray());
                    }
                }
            }
        }

        public Task SendTextAsync(string message, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(message ?? string.Empty);
            return webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }

        public void Dispose()
        {
            webSocket.Dispose();
        }
    }

    public static class LuxPipelineBridgeIntegration
    {
        private static LuxPipelineWebBridge bridge;

        [MenuItem("Tools/Linalab/Lux/Pipeline Web Bridge/Connect")]
        public static void ConnectBridge()
        {
            var gatewayUrl = Environment.GetEnvironmentVariable("LUX_GATEWAY_URL");
            var token = Environment.GetEnvironmentVariable("LUX_GATEWAY_TOKEN");
            if (string.IsNullOrWhiteSpace(gatewayUrl))
            {
                Debug.LogWarning("Set LUX_GATEWAY_URL before connecting the Lux pipeline web bridge.");
                return;
            }

            bridge = bridge ?? new LuxPipelineWebBridge();
            bridge.Connect(gatewayUrl, token);
            Debug.Log("Lux pipeline web bridge connecting to configured gateway.");
        }

        [MenuItem("Tools/Linalab/Lux/Pipeline Web Bridge/Disconnect")]
        public static void DisconnectBridge()
        {
            bridge?.Disconnect();
            bridge = null;
            Debug.Log("Lux pipeline web bridge disconnected.");
        }
    }
}
