using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityCodexImage.Editor.Backends.Segmentation;
using Linalab.UnityCodexImage.Editor.IR;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Pipeline
{
    public sealed class MaskPostProcessingNodeExecutor : IPipelineNodeExecutor
    {
        private const float DefaultThreshold = 0.5f;
        private const int DefaultMinimumAreaPixels = 32;

        public string NodeType => CodexImagePipelineNodeTypes.MaskPostProcessing;

        public Task<PipelineNodeExecutionResult> ExecuteAsync(PipelineNode node, PipelineExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var segmentationArtifact = LatestArtifact(context, CodexImagePipelineArtifactKinds.SegmentationResponse);
                if (segmentationArtifact == null || string.IsNullOrWhiteSpace(segmentationArtifact.value))
                {
                    return Task.FromResult(PipelineNodeExecutionResult.Failed("Mask post-processing node requires a segmentation response artifact."));
                }

                var segmentation = SegmentationResponse.FromJson(segmentationArtifact.value);
                if (segmentation == null || segmentation.masks == null)
                {
                    return Task.FromResult(PipelineNodeExecutionResult.Failed("Segmentation response JSON did not contain masks."));
                }

                var sourceImagePath = ResolveSourceImagePath(node, context);
                if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
                {
                    return Task.FromResult(PipelineNodeExecutionResult.Failed("Mask post-processing node requires an existing source image path."));
                }

                var outputDirectory = ResolveOutputDirectory(node, context);
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    return Task.FromResult(PipelineNodeExecutionResult.Failed("Mask post-processing node requires an output directory."));
                }

                var sourceTexture = LoadTexture(sourceImagePath);
                var threshold = ParseFloat(node.GetParameterValue("threshold"), DefaultThreshold);
                var minimumAreaPixels = ParseInt(node.GetParameterValue("minimumAreaPixels"), DefaultMinimumAreaPixels);
                var manifest = ResolveManifest(context);
                var extraction = ExtractParts(segmentation, sourceTexture, outputDirectory, threshold, minimumAreaPixels, cancellationToken);
                ApplyExtraction(manifest, sourceImagePath, sourceTexture, extraction, threshold);

                return Task.FromResult(PipelineNodeExecutionResult.Success(new PipelineArtifact
                {
                    id = $"artifact-{node.id}-parts-manifest",
                    nodeId = node.id,
                    portName = OutputPortName(node, "manifest", "manifest"),
                    name = "Extracted Parts Manifest",
                    kind = CodexImagePipelineArtifactKinds.GeneratedAssetManifest,
                    value = manifest.ToJson(),
                    path = outputDirectory
                }));
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is InvalidOperationException || exception is UnauthorizedAccessException)
            {
                return Task.FromResult(PipelineNodeExecutionResult.Failed("Mask post-processing failed: " + exception.Message));
            }
        }

        private static PartExtractionResult ExtractParts(
            SegmentationResponse segmentation,
            Texture2D sourceTexture,
            string outputDirectory,
            float threshold,
            int minimumAreaPixels,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            var acceptedParts = new List<ExtractedPart>();
            var warnings = new List<GeneratedAssetWarning>();
            var seenHashes = new HashSet<string>(StringComparer.Ordinal);
            var safeNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var masks = segmentation.masks ?? Array.Empty<SegmentationMask>();

            for (var index = 0; index < masks.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mask = masks[index];
                if (mask == null || string.IsNullOrWhiteSpace(mask.maskPngPath) || !File.Exists(mask.maskPngPath))
                {
                    warnings.Add(CreateWarning(index, mask?.id, "missing-mask", "Mask PNG was missing and was skipped."));
                    continue;
                }

                var maskTexture = LoadTexture(mask.maskPngPath);
                var alpha = BuildAlpha(maskTexture, threshold, out var bounds, out var areaPixels, out var hash);
                if (areaPixels < Math.Max(1, minimumAreaPixels))
                {
                    warnings.Add(CreateWarning(index, mask.id, "minimum-area", $"Mask '{mask.label}' area {areaPixels} is below minimum {minimumAreaPixels}."));
                    continue;
                }

                if (!seenHashes.Add(hash))
                {
                    warnings.Add(CreateWarning(index, mask.id, "duplicate-mask", $"Mask '{mask.label}' duplicates an earlier accepted mask."));
                    continue;
                }

                var safeName = UniqueName(SanitizeName(mask.label), safeNameCounts);
                var partPath = NormalizePath(Path.Combine(outputDirectory, safeName + ".png"));
                WritePartPng(sourceTexture, alpha, bounds, partPath);
                acceptedParts.Add(new ExtractedPart
                {
                    sourceMask = mask,
                    safeName = safeName,
                    outputPath = partPath,
                    order = acceptedParts.Count,
                    bounds = bounds,
                    areaPixels = areaPixels
                });
            }

            return new PartExtractionResult
            {
                parts = acceptedParts.ToArray(),
                warnings = warnings.ToArray()
            };
        }

        private static void ApplyExtraction(GeneratedAssetManifest manifest, string sourceImagePath, Texture2D sourceTexture, PartExtractionResult extraction, float threshold)
        {
            var existingLayers = manifest.layers ?? Array.Empty<GeneratedAssetLayer>();
            var existingMasks = manifest.masks ?? Array.Empty<GeneratedAssetMask>();
            var existingPolygons = manifest.polygons ?? Array.Empty<GeneratedAssetPolygon>();
            var existingSprites = manifest.sprites ?? Array.Empty<GeneratedAssetSprite>();
            EnsureSourceImage(manifest, sourceImagePath, sourceTexture);
            var existingImages = manifest.images ?? Array.Empty<GeneratedAssetImage>();

            manifest.images = existingImages.Concat(extraction.parts.Select(part => new GeneratedAssetImage
            {
                id = "part-image-" + part.safeName,
                role = "part",
                path = part.outputPath,
                width = part.bounds.width,
                height = part.bounds.height,
                alphaMode = "transparent"
            })).ToArray();
            manifest.layers = existingLayers.Concat(extraction.parts.Select(part => new GeneratedAssetLayer
            {
                id = "layer-" + part.safeName,
                name = part.safeName,
                imageId = "part-image-" + part.safeName,
                order = part.order,
                rect = ToRect(part.bounds),
                maskIds = new[] { "mask-" + part.safeName },
                polygonIds = new[] { "polygon-" + part.safeName }
            })).ToArray();
            manifest.masks = existingMasks.Concat(extraction.parts.Select(part => new GeneratedAssetMask
            {
                id = "mask-" + part.safeName,
                layerId = "layer-" + part.safeName,
                imagePath = part.sourceMask.maskPngPath,
                kind = "binary-alpha",
                threshold = threshold,
                bounds = ToRect(part.bounds),
                polygonId = "polygon-" + part.safeName
            })).ToArray();
            manifest.polygons = existingPolygons.Concat(extraction.parts.Select(part => new GeneratedAssetPolygon
            {
                id = "polygon-" + part.safeName,
                maskId = "mask-" + part.safeName,
                label = part.safeName,
                confidence = part.sourceMask.confidence,
                points = ToPolygon(part.sourceMask, part.bounds)
            })).ToArray();
            manifest.sprites = existingSprites.Concat(extraction.parts.Select(part => new GeneratedAssetSprite
            {
                id = "sprite-" + part.safeName,
                name = part.safeName,
                imageId = "part-image-" + part.safeName,
                layerId = "layer-" + part.safeName,
                rect = new GeneratedAssetRect { x = 0f, y = 0f, width = part.bounds.width, height = part.bounds.height },
                pivot = PivotFor(part.bounds, sourceTexture.width, sourceTexture.height)
            })).ToArray();
            manifest.warnings = (manifest.warnings ?? Array.Empty<GeneratedAssetWarning>()).Concat(extraction.warnings).ToArray();

            if (string.IsNullOrWhiteSpace(manifest.exportHints.outputDirectory) && extraction.parts.Length > 0)
            {
                manifest.exportHints.outputDirectory = NormalizePath(Path.GetDirectoryName(extraction.parts[0].outputPath));
            }

            if (string.IsNullOrWhiteSpace(manifest.sourceContext.selectedAssetPath))
            {
                manifest.sourceContext.selectedAssetPath = sourceImagePath;
            }
        }

        private static string EnsureSourceImage(GeneratedAssetManifest manifest, string sourceImagePath, Texture2D sourceTexture)
        {
            var images = manifest.images ?? Array.Empty<GeneratedAssetImage>();
            var existing = images.FirstOrDefault(image => string.Equals(image.path, sourceImagePath, StringComparison.Ordinal));
            if (existing != null)
            {
                return existing.id;
            }

            var sourceImage = new GeneratedAssetImage
            {
                id = "image-source",
                role = "source",
                path = sourceImagePath,
                width = sourceTexture.width,
                height = sourceTexture.height,
                alphaMode = "source"
            };
            manifest.images = images.Concat(new[] { sourceImage }).ToArray();
            return sourceImage.id;
        }

        private static Texture2D LoadTexture(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                throw new InvalidOperationException("PNG could not be decoded: " + path);
            }

            return texture;
        }

        private static float[] BuildAlpha(Texture2D maskTexture, float threshold, out PixelBounds bounds, out int areaPixels, out string hash)
        {
            var pixels = maskTexture.GetPixels32();
            var alpha = new float[pixels.Length];
            var minX = maskTexture.width;
            var minY = maskTexture.height;
            var maxX = -1;
            var maxY = -1;
            areaPixels = 0;
            using (var sha = SHA256.Create())
            {
                var bytes = new byte[pixels.Length];
                for (var y = 0; y < maskTexture.height; y++)
                {
                    for (var x = 0; x < maskTexture.width; x++)
                    {
                        var pixelIndex = y * maskTexture.width + x;
                        var pixel = pixels[pixelIndex];
                        var coverage = Math.Max(pixel.a / 255f, Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) / 255f);
                        if (coverage < threshold)
                        {
                            continue;
                        }

                        alpha[pixelIndex] = 1f;
                        bytes[pixelIndex] = 1;
                        areaPixels++;
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }

                hash = Convert.ToBase64String(sha.ComputeHash(bytes));
            }

            bounds = areaPixels == 0
                ? new PixelBounds()
                : new PixelBounds { x = minX, y = minY, width = maxX - minX + 1, height = maxY - minY + 1 };
            return alpha;
        }

        private static void WritePartPng(Texture2D sourceTexture, float[] alpha, PixelBounds bounds, string path)
        {
            var output = new Texture2D(bounds.width, bounds.height, TextureFormat.RGBA32, false);
            var sourcePixels = sourceTexture.GetPixels32();
            var outputPixels = new Color32[bounds.width * bounds.height];
            for (var y = 0; y < bounds.height; y++)
            {
                for (var x = 0; x < bounds.width; x++)
                {
                    var sourceX = bounds.x + x;
                    var sourceY = bounds.y + y;
                    var outputIndex = y * bounds.width + x;
                    if (sourceX < 0 || sourceY < 0 || sourceX >= sourceTexture.width || sourceY >= sourceTexture.height)
                    {
                        outputPixels[outputIndex] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    var sourceIndex = sourceY * sourceTexture.width + sourceX;
                    var color = sourcePixels[sourceIndex];
                    color.a = (byte)Mathf.RoundToInt(color.a * alpha[sourceIndex]);
                    outputPixels[outputIndex] = color;
                }
            }

            output.SetPixels32(outputPixels);
            output.Apply();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, output.EncodeToPNG());
        }

        private static GeneratedAssetVector2[] ToPolygon(SegmentationMask mask, PixelBounds bounds)
        {
            var points = mask.polygonPoints ?? Array.Empty<SegmentationPoint>();
            if (points.Length > 0)
            {
                return points.Select(point => new GeneratedAssetVector2 { x = point.x, y = point.y }).ToArray();
            }

            return new[]
            {
                new GeneratedAssetVector2 { x = bounds.x, y = bounds.y },
                new GeneratedAssetVector2 { x = bounds.x + bounds.width, y = bounds.y },
                new GeneratedAssetVector2 { x = bounds.x + bounds.width, y = bounds.y + bounds.height },
                new GeneratedAssetVector2 { x = bounds.x, y = bounds.y + bounds.height }
            };
        }

        private static GeneratedAssetVector2 PivotFor(PixelBounds bounds, int width, int height)
        {
            return new GeneratedAssetVector2
            {
                x = width <= 0 ? 0.5f : Mathf.Clamp01((bounds.x + (bounds.width * 0.5f)) / width),
                y = height <= 0 ? 0.5f : Mathf.Clamp01((bounds.y + (bounds.height * 0.5f)) / height)
            };
        }

        private static GeneratedAssetRect ToRect(PixelBounds bounds)
        {
            return new GeneratedAssetRect { x = bounds.x, y = bounds.y, width = bounds.width, height = bounds.height };
        }

        private static GeneratedAssetWarning CreateWarning(int index, string sourceId, string code, string message)
        {
            return new GeneratedAssetWarning
            {
                id = "mask-warning-" + (index + 1).ToString(CultureInfo.InvariantCulture),
                sourceId = sourceId,
                code = code,
                message = message
            };
        }

        private static GeneratedAssetManifest ResolveManifest(PipelineExecutionContext context)
        {
            var artifact = LatestArtifact(context, CodexImagePipelineArtifactKinds.GeneratedAssetManifest);
            return artifact == null || string.IsNullOrWhiteSpace(artifact.value)
                ? GeneratedAssetManifest.CreateDefault()
                : GeneratedAssetManifest.FromJson(artifact.value);
        }

        private static string ResolveSourceImagePath(PipelineNode node, PipelineExecutionContext context)
        {
            var explicitPath = node.GetParameterValue("sourceImagePath");
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return NormalizePath(explicitPath);
            }

            var manifest = ResolveManifest(context);
            var generatedImage = (manifest.images ?? Array.Empty<GeneratedAssetImage>()).FirstOrDefault(image => string.Equals(image.role, "generated", StringComparison.Ordinal));
            return NormalizePath(generatedImage?.path ?? manifest.sourceContext.selectedAssetPath);
        }

        private static string ResolveOutputDirectory(PipelineNode node, PipelineExecutionContext context)
        {
            var explicitPath = node.GetParameterValue("outputDirectory");
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return NormalizePath(explicitPath);
            }

            var outputArtifact = LatestArtifact(context, CodexImagePipelineArtifactKinds.OutputDirectory);
            if (outputArtifact != null && !string.IsNullOrWhiteSpace(outputArtifact.value))
            {
                return NormalizePath(outputArtifact.value.TrimEnd('/') + "/parts");
            }

            var manifest = ResolveManifest(context);
            return string.IsNullOrWhiteSpace(manifest.exportHints.outputDirectory)
                ? string.Empty
                : NormalizePath(manifest.exportHints.outputDirectory.TrimEnd('/') + "/parts");
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

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? Mathf.Clamp01(parsed) : fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : fallback;
        }

        private static string UniqueName(string baseName, IDictionary<string, int> counts)
        {
            if (!counts.TryGetValue(baseName, out var count))
            {
                counts[baseName] = 1;
                return baseName;
            }

            counts[baseName] = count + 1;
            return baseName + "_" + (count + 1).ToString(CultureInfo.InvariantCulture);
        }

        private static string SanitizeName(string label)
        {
            var normalized = string.IsNullOrWhiteSpace(label) ? "part" : label.Trim().ToLowerInvariant();
            var builder = new StringBuilder(normalized.Length);
            var previousSeparator = false;
            foreach (var character in normalized)
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                {
                    builder.Append(character);
                    previousSeparator = false;
                    continue;
                }

                if (!previousSeparator)
                {
                    builder.Append('_');
                    previousSeparator = true;
                }
            }

            var result = builder.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "part" : result;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');
        }

        private struct PixelBounds
        {
            public int x;
            public int y;
            public int width;
            public int height;
        }

        private sealed class ExtractedPart
        {
            public SegmentationMask sourceMask;
            public string safeName;
            public string outputPath;
            public int order;
            public PixelBounds bounds;
            public int areaPixels;
        }

        private sealed class PartExtractionResult
        {
            public ExtractedPart[] parts = Array.Empty<ExtractedPart>();
            public GeneratedAssetWarning[] warnings = Array.Empty<GeneratedAssetWarning>();
        }
    }
}
