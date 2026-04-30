using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityCodexImage.Editor.IR;
using Linalab.UnityCodexImage.Editor.Pipeline;
using UnityEditor;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Backends.Segmentation
{
    public interface ISegmentationBackend
    {
        string BackendId { get; }
        Task<SegmentationBackendResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken);
    }

    public interface ISegmentationBackendSettings
    {
        SegmentationBackendMode Mode { get; }
        string EndpointUrl { get; }
        int TimeoutMilliseconds { get; }
    }

    public enum SegmentationBackendMode
    {
        Fake,
        Remote
    }

    [Serializable]
    public sealed class SegmentationRequest
    {
        public string sourceImagePath;
        public string outputDirectory;
        public string requestId;
        public string[] requestedLabels = Array.Empty<string>();

        public static SegmentationRequest Create(string sourceImagePath, string outputDirectory, params string[] requestedLabels)
        {
            return new SegmentationRequest
            {
                sourceImagePath = Normalize(sourceImagePath),
                outputDirectory = Normalize(outputDirectory),
                requestId = Guid.NewGuid().ToString("N"),
                requestedLabels = requestedLabels ?? Array.Empty<string>()
            };
        }

        private static string Normalize(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');
        }
    }

    [Serializable]
    public sealed class SegmentationResponse
    {
        public string requestId;
        public int rasterWidth;
        public int rasterHeight;
        public SegmentationMask[] masks = Array.Empty<SegmentationMask>();
        public SegmentationBackendMetadata sourceBackend = new SegmentationBackendMetadata();

        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public static SegmentationResponse FromJson(string json)
        {
            return JsonUtility.FromJson<SegmentationResponse>(json);
        }
    }

    [Serializable]
    public sealed class SegmentationMask
    {
        public string id;
        public string maskPngPath;
        public string label;
        public float confidence;
        public SegmentationRect bbox = new SegmentationRect();
        public int rasterWidth;
        public int rasterHeight;
        public SegmentationPoint[] polygonPoints = Array.Empty<SegmentationPoint>();
        public SegmentationBackendMetadata sourceBackend = new SegmentationBackendMetadata();
    }

    [Serializable]
    public sealed class SegmentationBackendMetadata
    {
        public string backendId;
        public string backendKind;
        public string endpointUrl;
        public string modelName;
        public string version;
    }

    [Serializable]
    public sealed class SegmentationRect
    {
        public int x;
        public int y;
        public int width;
        public int height;
    }

    [Serializable]
    public sealed class SegmentationPoint
    {
        public float x;
        public float y;
    }

    public enum SegmentationBackendErrorKind
    {
        None,
        InvalidRequest,
        ConnectionFailed,
        Timeout,
        RemoteError,
        Cancelled
    }

    public readonly struct SegmentationBackendResult
    {
        public SegmentationBackendResult(bool succeeded, SegmentationResponse response, SegmentationBackendErrorKind errorKind, string message)
        {
            Succeeded = succeeded;
            Response = response;
            ErrorKind = errorKind;
            Message = message;
        }

        public bool Succeeded { get; }
        public SegmentationResponse Response { get; }
        public SegmentationBackendErrorKind ErrorKind { get; }
        public string Message { get; }

        public static SegmentationBackendResult Success(SegmentationResponse response)
        {
            return new SegmentationBackendResult(true, response, SegmentationBackendErrorKind.None, "Segmentation completed.");
        }

        public static SegmentationBackendResult Failed(SegmentationBackendErrorKind errorKind, string message)
        {
            return new SegmentationBackendResult(false, null, errorKind, message);
        }
    }

    public sealed class SegmentationBackendSettings : ISegmentationBackendSettings
    {
        public SegmentationBackendSettings(SegmentationBackendMode mode, string endpointUrl = null, int timeoutMilliseconds = 5000)
        {
            Mode = mode;
            EndpointUrl = endpointUrl ?? string.Empty;
            TimeoutMilliseconds = timeoutMilliseconds <= 0 ? 5000 : timeoutMilliseconds;
        }

        public SegmentationBackendMode Mode { get; }
        public string EndpointUrl { get; }
        public int TimeoutMilliseconds { get; }

        public static SegmentationBackendSettings Fake()
        {
            return new SegmentationBackendSettings(SegmentationBackendMode.Fake);
        }

        public static SegmentationBackendSettings Remote(string endpointUrl, int timeoutMilliseconds = 5000)
        {
            return new SegmentationBackendSettings(SegmentationBackendMode.Remote, endpointUrl, timeoutMilliseconds);
        }
    }

    public sealed class EditorPrefsSegmentationBackendSettings : ISegmentationBackendSettings
    {
        public const string ModeKey = "Linalab.UnityCodexImage.Segmentation.Mode";
        public const string EndpointUrlKey = "Linalab.UnityCodexImage.Segmentation.EndpointUrl";
        public const string TimeoutMillisecondsKey = "Linalab.UnityCodexImage.Segmentation.TimeoutMilliseconds";

        public SegmentationBackendMode Mode
        {
            get
            {
                var value = EditorPrefs.GetString(ModeKey, SegmentationBackendMode.Fake.ToString());
                return Enum.TryParse(value, out SegmentationBackendMode mode) ? mode : SegmentationBackendMode.Fake;
            }
        }

        public string EndpointUrl => EditorPrefs.GetString(EndpointUrlKey, string.Empty);
        public int TimeoutMilliseconds => Math.Max(100, EditorPrefs.GetInt(TimeoutMillisecondsKey, 5000));
    }

    public static class SegmentationBackendFactory
    {
        public static ISegmentationBackend Create(ISegmentationBackendSettings settings = null, HttpMessageHandler httpMessageHandler = null)
        {
            var resolvedSettings = settings ?? new EditorPrefsSegmentationBackendSettings();
            if (resolvedSettings.Mode == SegmentationBackendMode.Remote)
            {
                return new RemoteSegmentationBackend(resolvedSettings, httpMessageHandler);
            }

            return new FakeSegmentationBackend();
        }
    }

    public sealed class FakeSegmentationBackend : ISegmentationBackend
    {
        private static readonly string[] DefaultLabels = { "head", "torso", "arm_l" };

        public string BackendId => "fake-segmentation";

        public Task<SegmentationBackendResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = ValidateRequest(request);
            if (!string.IsNullOrEmpty(validation))
            {
                return Task.FromResult(SegmentationBackendResult.Failed(SegmentationBackendErrorKind.InvalidRequest, validation));
            }

            var labels = (request.requestedLabels == null || request.requestedLabels.Length == 0)
                ? DefaultLabels
                : request.requestedLabels;
            var metadata = new SegmentationBackendMetadata
            {
                backendId = BackendId,
                backendKind = "fake",
                modelName = "fixture-labels",
                version = "0.1"
            };
            var response = new SegmentationResponse
            {
                requestId = string.IsNullOrWhiteSpace(request.requestId) ? Guid.NewGuid().ToString("N") : request.requestId,
                rasterWidth = 512,
                rasterHeight = 512,
                sourceBackend = metadata,
                masks = labels.Select((label, index) => CreateMask(request.outputDirectory, label, index, metadata)).ToArray()
            };

            return Task.FromResult(SegmentationBackendResult.Success(response));
        }

        private static SegmentationMask CreateMask(string outputDirectory, string label, int index, SegmentationBackendMetadata metadata)
        {
            var x = 96 + (index * 48);
            var y = 64 + (index * 72);
            var width = label == "torso" ? 180 : 96;
            var height = label == "torso" ? 220 : 120;
            var normalizedOutput = string.IsNullOrWhiteSpace(outputDirectory) ? "Assets/Generated/CodexImages/Segmentation" : outputDirectory.Trim().Replace('\\', '/');

            return new SegmentationMask
            {
                id = "mask-" + Sanitize(label),
                maskPngPath = normalizedOutput.TrimEnd('/') + "/masks/" + Sanitize(label) + ".png",
                label = label,
                confidence = 0.95f - (index * 0.04f),
                rasterWidth = 512,
                rasterHeight = 512,
                bbox = new SegmentationRect { x = x, y = y, width = width, height = height },
                polygonPoints = RectanglePolygon(x, y, width, height),
                sourceBackend = metadata
            };
        }

        private static SegmentationPoint[] RectanglePolygon(int x, int y, int width, int height)
        {
            return new[]
            {
                new SegmentationPoint { x = x, y = y },
                new SegmentationPoint { x = x + width, y = y },
                new SegmentationPoint { x = x + width, y = y + height },
                new SegmentationPoint { x = x, y = y + height }
            };
        }

        private static string Sanitize(string label)
        {
            return string.IsNullOrWhiteSpace(label) ? "part" : label.Trim().Replace(' ', '_').ToLowerInvariant();
        }

        private static string ValidateRequest(SegmentationRequest request)
        {
            if (request == null)
            {
                return "Segmentation request is required.";
            }

            if (string.IsNullOrWhiteSpace(request.sourceImagePath))
            {
                return "Segmentation request requires a source image path.";
            }

            if (string.IsNullOrWhiteSpace(request.outputDirectory))
            {
                return "Segmentation request requires an output directory.";
            }

            return string.Empty;
        }
    }

    public sealed class RemoteSegmentationBackend : ISegmentationBackend
    {
        private readonly ISegmentationBackendSettings settings;
        private readonly HttpMessageHandler httpMessageHandler;

        public RemoteSegmentationBackend(ISegmentationBackendSettings settings, HttpMessageHandler httpMessageHandler = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.httpMessageHandler = httpMessageHandler;
        }

        public string BackendId => "remote-segmentation";

        public async Task<SegmentationBackendResult> SegmentAsync(SegmentationRequest request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.sourceImagePath) || string.IsNullOrWhiteSpace(request.outputDirectory))
            {
                return SegmentationBackendResult.Failed(SegmentationBackendErrorKind.InvalidRequest, "Remote segmentation requires source image and output directory paths.");
            }

            if (!Uri.TryCreate(settings.EndpointUrl, UriKind.Absolute, out var endpoint))
            {
                return SegmentationBackendResult.Failed(SegmentationBackendErrorKind.InvalidRequest, "Remote segmentation endpoint URL is invalid or missing.");
            }

            try
            {
                using (var client = httpMessageHandler == null ? new HttpClient() : new HttpClient(httpMessageHandler, false))
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    client.Timeout = TimeSpan.FromMilliseconds(settings.TimeoutMilliseconds <= 0 ? 5000 : settings.TimeoutMilliseconds);
                    var json = JsonUtility.ToJson(request);
                    using (var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"))
                    using (var response = await client.PostAsync(endpoint, content, timeout.Token))
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            return SegmentationBackendResult.Failed(SegmentationBackendErrorKind.RemoteError, $"Remote segmentation endpoint returned {(int)response.StatusCode}: {body}");
                        }

                        var segmentationResponse = SegmentationResponse.FromJson(body);
                        if (segmentationResponse == null || segmentationResponse.masks == null)
                        {
                            return SegmentationBackendResult.Failed(SegmentationBackendErrorKind.RemoteError, "Remote segmentation response did not contain masks.");
                        }

                        return SegmentationBackendResult.Success(segmentationResponse);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return SegmentationBackendResult.Failed(SegmentationBackendErrorKind.Cancelled, "Remote segmentation was cancelled.");
            }
            catch (TaskCanceledException exception)
            {
                return SegmentationBackendResult.Failed(SegmentationBackendErrorKind.Timeout, "Remote segmentation timed out: " + exception.Message);
            }
            catch (HttpRequestException exception)
            {
                return SegmentationBackendResult.Failed(SegmentationBackendErrorKind.ConnectionFailed, "Remote segmentation connection failed: " + exception.Message);
            }
            catch (IOException exception)
            {
                return SegmentationBackendResult.Failed(SegmentationBackendErrorKind.ConnectionFailed, "Remote segmentation IO failed: " + exception.Message);
            }
            catch (ArgumentException exception)
            {
                return SegmentationBackendResult.Failed(SegmentationBackendErrorKind.RemoteError, "Remote segmentation response was invalid: " + exception.Message);
            }
        }
    }

    public sealed class SegmentationNodeExecutor : IPipelineNodeExecutor
    {
        private readonly ISegmentationBackend backend;

        public SegmentationNodeExecutor(ISegmentationBackend backend = null)
        {
            this.backend = backend ?? SegmentationBackendFactory.Create(SegmentationBackendSettings.Fake());
        }

        public string NodeType => CodexImagePipelineNodeTypes.Segmentation;

        public async Task<PipelineNodeExecutionResult> ExecuteAsync(PipelineNode node, PipelineExecutionContext context, CancellationToken cancellationToken)
        {
            var request = CreateRequest(node, context);
            var result = await backend.SegmentAsync(request, cancellationToken);
            if (!result.Succeeded)
            {
                return PipelineNodeExecutionResult.Failed($"Segmentation backend '{backend.BackendId}' failed ({result.ErrorKind}): {result.Message}");
            }

            if (!HasRequiredFields(result.Response))
            {
                return PipelineNodeExecutionResult.Failed($"Segmentation backend '{backend.BackendId}' returned an incomplete response.");
            }

            return PipelineNodeExecutionResult.Success(new PipelineArtifact
            {
                id = $"artifact-{node.id}-segmentation",
                nodeId = node.id,
                portName = FirstOutputPortName(node, "segmentation"),
                name = "Segmentation Response",
                kind = CodexImagePipelineArtifactKinds.SegmentationResponse,
                value = result.Response.ToJson(),
                path = request.outputDirectory
            });
        }

        private static SegmentationRequest CreateRequest(PipelineNode node, PipelineExecutionContext context)
        {
            var sourceImagePath = node.GetParameterValue("sourceImagePath");
            if (string.IsNullOrWhiteSpace(sourceImagePath))
            {
                var manifestArtifact = LatestArtifact(context, CodexImagePipelineArtifactKinds.GeneratedAssetManifest);
                sourceImagePath = FirstNonEmpty(
                    LatestArtifact(context, "generated-image")?.path,
                    manifestArtifact?.path,
                    ResolveSourceImagePathFromManifestValue(manifestArtifact?.value));
            }

            var outputDirectory = node.GetParameterValue("outputDirectory");
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                var outputArtifact = LatestArtifact(context, CodexImagePipelineArtifactKinds.OutputDirectory);
                outputDirectory = outputArtifact?.value ?? outputArtifact?.path;
            }

            var labelsValue = node.GetParameterValue("labels", string.Empty);
            var labels = labelsValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(label => label.Trim())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToArray();
            return SegmentationRequest.Create(sourceImagePath, outputDirectory, labels);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return (values ?? Array.Empty<string>()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static string ResolveSourceImagePathFromManifestValue(string manifestJson)
        {
            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                return string.Empty;
            }

            try
            {
                var manifest = GeneratedAssetManifest.FromJson(manifestJson);
                var images = manifest?.images ?? Array.Empty<GeneratedAssetImage>();
                var generatedImage = images.FirstOrDefault(image =>
                    image != null
                    && string.Equals(image.role, "generated", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(image.path));

                return (generatedImage ?? images.FirstOrDefault(image => image != null && !string.IsNullOrWhiteSpace(image.path)))?.path
                    ?? string.Empty;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return string.Empty;
            }
        }

        private static bool HasRequiredFields(SegmentationResponse response)
        {
            if (response == null || response.rasterWidth <= 0 || response.rasterHeight <= 0 || response.sourceBackend == null || string.IsNullOrWhiteSpace(response.sourceBackend.backendId))
            {
                return false;
            }

            return (response.masks ?? Array.Empty<SegmentationMask>()).All(mask =>
                mask != null
                && !string.IsNullOrWhiteSpace(mask.maskPngPath)
                && !string.IsNullOrWhiteSpace(mask.label)
                && mask.confidence >= 0f
                && mask.bbox != null
                && mask.bbox.width > 0
                && mask.bbox.height > 0
                && mask.rasterWidth > 0
                && mask.rasterHeight > 0
                && mask.polygonPoints != null
                && mask.polygonPoints.Length > 0
                && mask.sourceBackend != null
                && !string.IsNullOrWhiteSpace(mask.sourceBackend.backendId));
        }

        private static PipelineArtifact LatestArtifact(PipelineExecutionContext context, string kind)
        {
            return context.Artifacts.LastOrDefault(artifact => string.Equals(artifact.kind, kind, StringComparison.Ordinal));
        }

        private static string FirstOutputPortName(PipelineNode node, string fallback)
        {
            var outputs = node.outputPorts ?? Array.Empty<PipelinePort>();
            return outputs.Length == 0 ? fallback : outputs[0].name;
        }
    }
}
