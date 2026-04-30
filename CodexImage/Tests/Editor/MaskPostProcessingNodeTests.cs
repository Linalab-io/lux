using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linalab.UnityCodexImage.Editor.Backends.Segmentation;
using Linalab.UnityCodexImage.Editor.IR;
using Linalab.UnityCodexImage.Editor.Pipeline;
using NUnit.Framework;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Tests
{
    public sealed class MaskPostProcessingNodeTests
    {
        private const string PackageRoot = "Packages/com.linalab.lux/CodexImage";
        private const string MaskToPartEvidencePath = PackageRoot + "/.sisyphus/evidence/task-6-mask-to-part.txt";
        private const string TinyMaskEvidencePath = PackageRoot + "/.sisyphus/evidence/task-6-tiny-mask-filtered.txt";

        [Test]
        public async Task ExtractsTransparentPartPngAndManifestMetadataFromMask()
        {
            var root = CreateEvidenceDirectory("mask-to-part");
            var sourcePath = root + "/source.png";
            var maskPath = root + "/mask-head.png";
            var outputDirectory = root + "/parts";
            WriteSourcePng(sourcePath, 8, 8);
            WriteMaskPng(maskPath, 8, 8, 2, 1, 3, 4);
            var response = CreateResponse(maskPath, "Head Layer", 2, 1, 3, 4);
            var executor = new MaskPostProcessingNodeExecutor();
            var context = CreateContext(response);

            var result = await executor.ExecuteAsync(CreateNode(sourcePath, outputDirectory, 4), context, CancellationToken.None);

            Assert.That(result.succeeded, Is.True, result.message);
            var manifest = GeneratedAssetManifest.FromJson(result.artifacts.Single().value);
            Assert.That(manifest.images.Count(image => image.role == "part"), Is.EqualTo(1));
            Assert.That(manifest.layers.Select(layer => layer.name), Does.Contain("head_layer"));
            Assert.That(manifest.layers.Single().order, Is.EqualTo(0));
            Assert.That(manifest.masks.Single().bounds.x, Is.EqualTo(2f));
            Assert.That(manifest.masks.Single().bounds.y, Is.EqualTo(1f));
            Assert.That(manifest.masks.Single().bounds.width, Is.EqualTo(3f));
            Assert.That(manifest.masks.Single().bounds.height, Is.EqualTo(4f));
            Assert.That(manifest.polygons.Single().points, Has.Length.EqualTo(4));
            Assert.That(manifest.sprites.Single().pivot.x, Is.EqualTo(0.4375f).Within(0.0001f));
            Assert.That(manifest.sprites.Single().pivot.y, Is.EqualTo(0.375f).Within(0.0001f));
            Assert.That(manifest.warnings, Is.Empty);

            var partPath = manifest.images.Single(image => image.role == "part").path;
            Assert.That(File.Exists(partPath), Is.True);
            var partTexture = LoadTexture(partPath);
            Assert.That(partTexture.width, Is.EqualTo(3));
            Assert.That(partTexture.height, Is.EqualTo(4));
            Assert.That(partTexture.GetPixels32().All(pixel => pixel.a > 0), Is.True);

            WriteEvidence(
                MaskToPartEvidencePath,
                "Mask post-processing extraction passed",
                "partPath=" + partPath,
                "layers=" + string.Join(",", manifest.layers.Select(layer => layer.name)),
                "bounds=" + RectLine(manifest.masks.Single().bounds),
                "pivot=" + manifest.sprites.Single().pivot.x + "," + manifest.sprites.Single().pivot.y,
                "warnings=" + manifest.warnings.Length);
        }

        [Test]
        public async Task FiltersTinyMaskAndRecordsWarningWithoutPartLayer()
        {
            var root = CreateEvidenceDirectory("tiny-filter");
            var sourcePath = root + "/source.png";
            var maskPath = root + "/mask-speck.png";
            var outputDirectory = root + "/parts";
            WriteSourcePng(sourcePath, 8, 8);
            WriteMaskPng(maskPath, 8, 8, 4, 4, 1, 1);
            var response = CreateResponse(maskPath, "Tiny Speck", 4, 4, 1, 1);
            var executor = new MaskPostProcessingNodeExecutor();
            var context = CreateContext(response);

            var result = await executor.ExecuteAsync(CreateNode(sourcePath, outputDirectory, 4), context, CancellationToken.None);

            Assert.That(result.succeeded, Is.True, result.message);
            var manifest = GeneratedAssetManifest.FromJson(result.artifacts.Single().value);
            Assert.That(manifest.images.Any(image => image.role == "part"), Is.False);
            Assert.That(manifest.layers, Is.Empty);
            Assert.That(manifest.masks, Is.Empty);
            Assert.That(manifest.polygons, Is.Empty);
            Assert.That(manifest.warnings.Single().code, Is.EqualTo("minimum-area"));
            Assert.That(Directory.Exists(outputDirectory) ? Directory.GetFiles(outputDirectory, "*.png") : Array.Empty<string>(), Is.Empty);

            WriteEvidence(
                TinyMaskEvidencePath,
                "Tiny mask filtering passed",
                "minimumAreaPixels=4",
                "warnings=" + string.Join(",", manifest.warnings.Select(warning => warning.code)),
                "parts=" + manifest.images.Count(image => image.role == "part"));
        }

        private static PipelineExecutionContext CreateContext(SegmentationResponse response)
        {
            var context = new PipelineExecutionContext(new PipelineGraph { id = "mask-post-processing-test" });
            context.AddArtifact(new PipelineArtifact
            {
                id = "segmentation-response",
                nodeId = "segment",
                portName = "segmentation",
                kind = CodexImagePipelineArtifactKinds.SegmentationResponse,
                value = response.ToJson()
            });
            context.AddArtifact(new PipelineArtifact
            {
                id = "manifest",
                nodeId = "prompt",
                portName = "manifest",
                kind = CodexImagePipelineArtifactKinds.GeneratedAssetManifest,
                value = GeneratedAssetManifest.CreateDefault().ToJson()
            });
            return context;
        }

        private static PipelineNode CreateNode(string sourcePath, string outputDirectory, int minimumAreaPixels)
        {
            return new PipelineNode
            {
                id = "post-process",
                type = CodexImagePipelineNodeTypes.MaskPostProcessing,
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
                    new PipelineParameter { name = "sourceImagePath", value = sourcePath },
                    new PipelineParameter { name = "outputDirectory", value = outputDirectory },
                    new PipelineParameter { name = "minimumAreaPixels", value = minimumAreaPixels.ToString() }
                }
            };
        }

        private static SegmentationResponse CreateResponse(string maskPath, string label, int x, int y, int width, int height)
        {
            return new SegmentationResponse
            {
                requestId = "task-6-test",
                rasterWidth = 8,
                rasterHeight = 8,
                sourceBackend = new SegmentationBackendMetadata { backendId = "fixture", backendKind = "test" },
                masks = new[]
                {
                    new SegmentationMask
                    {
                        id = "mask-" + label.ToLowerInvariant().Replace(' ', '-'),
                        maskPngPath = maskPath,
                        label = label,
                        confidence = 0.9f,
                        rasterWidth = 8,
                        rasterHeight = 8,
                        bbox = new SegmentationRect { x = x, y = y, width = width, height = height },
                        polygonPoints = new[]
                        {
                            new SegmentationPoint { x = x, y = y },
                            new SegmentationPoint { x = x + width, y = y },
                            new SegmentationPoint { x = x + width, y = y + height },
                            new SegmentationPoint { x = x, y = y + height }
                        },
                        sourceBackend = new SegmentationBackendMetadata { backendId = "fixture", backendKind = "test" }
                    }
                }
            };
        }

        private static void WriteSourcePng(string path, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    pixels[y * width + x] = new Color32((byte)(20 + x), (byte)(40 + y), 120, 255);
                }
            }

            WriteTexture(path, texture, pixels);
        }

        private static void WriteMaskPng(string path, int width, int height, int x, int y, int maskWidth, int maskHeight)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var row = y; row < y + maskHeight; row++)
            {
                for (var column = x; column < x + maskWidth; column++)
                {
                    pixels[row * width + column] = new Color32(255, 255, 255, 255);
                }
            }

            WriteTexture(path, texture, pixels);
        }

        private static void WriteTexture(string path, Texture2D texture, Color32[] pixels)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }

        private static Texture2D LoadTexture(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True);
            return texture;
        }

        private static string CreateEvidenceDirectory(string name)
        {
            var path = PackageRoot + "/.sisyphus/evidence/task-6-fixtures/" + name + "-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(path);
            return path;
        }

        private static string RectLine(GeneratedAssetRect rect)
        {
            return rect.x + "," + rect.y + "," + rect.width + "," + rect.height;
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
