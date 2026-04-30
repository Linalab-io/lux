using System;
using System.IO;
using System.Linq;
using Linalab.UnityCodexImage.Editor.Exporters;
using Linalab.UnityCodexImage.Editor.IR;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Tests
{
    public sealed class GeneratedAssetExportersTests
    {
        private const string PackageRoot = "Packages/com.linalab.lux/CodexImage";
        private const string SpritesheetEvidencePath = PackageRoot + "/.sisyphus/evidence/task-7-spritesheet.txt";
        private const string EmptyFramesEvidencePath = PackageRoot + "/.sisyphus/evidence/task-7-empty-frames.txt";

        [Test]
        public void ExportsFourFrameSequenceSpritesheetAndAnimationClip()
        {
            var root = CreateEvidenceDirectory("spritesheet");
            var manifest = CreateFourFrameManifest(root);
            var frameSequence = GeneratedAssetFrameSequenceExporter.Export(manifest, new GeneratedAssetFrameSequenceExportOptions
            {
                outputDirectory = root + "/frames",
                fileNamePrefix = "walk",
                overwriteExisting = false,
                importAsSprites = true
            });

            Assert.That(frameSequence.succeeded, Is.True, frameSequence.message);
            Assert.That(frameSequence.framePaths, Has.Length.EqualTo(4));
            Assert.That(frameSequence.framePaths.All(File.Exists), Is.True);
            Assert.That(frameSequence.importedSprites, Has.Length.EqualTo(4));

            var spritesheet = GeneratedAssetSpritesheetExporter.Export(manifest, new GeneratedAssetSpritesheetExportOptions
            {
                outputPath = root + "/walk-sheet.png",
                spriteNamePrefix = "walk",
                columns = 2,
                overwriteExisting = false,
                importAsMultipleSprites = true
            });

            Assert.That(spritesheet.succeeded, Is.True, spritesheet.message);
            Assert.That(File.Exists(spritesheet.spritesheetPath), Is.True);
            Assert.That(spritesheet.spriteNames, Is.EqualTo(new[] { "walk_000", "walk_001", "walk_002", "walk_003" }));
            Assert.That(spritesheet.importedSprites, Has.Length.EqualTo(4));
            Assert.That(spritesheet.importedSprites.All(sprite => sprite != null), Is.True);

            var sheetTexture = LoadTexture(spritesheet.spritesheetPath);
            Assert.That(sheetTexture.width, Is.EqualTo(8));
            Assert.That(sheetTexture.height, Is.EqualTo(8));
            Assert.That(sheetTexture.GetPixel(0, 4), Is.EqualTo(new Color(1f, 0f, 0f, 1f)));
            Assert.That(sheetTexture.GetPixel(4, 4), Is.EqualTo(new Color(0f, 1f, 0f, 1f)));
            Assert.That(sheetTexture.GetPixel(0, 0), Is.EqualTo(new Color(0f, 0f, 1f, 1f)));
            Assert.That(sheetTexture.GetPixel(4, 0), Is.EqualTo(new Color(1f, 1f, 0f, 1f)));

            var clipResult = GeneratedAssetAnimationClipExporter.Export(manifest, spritesheet.importedSprites, new GeneratedAssetAnimationClipExportOptions
            {
                outputPath = root + "/walk.anim",
                clipName = "walk",
                overwriteExisting = false
            });

            Assert.That(clipResult.succeeded, Is.True, clipResult.message);
            Assert.That(File.Exists(clipResult.clipPath), Is.True);
            Assert.That(clipResult.clip.frameRate, Is.EqualTo(8f));
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clipResult.clip).Single();
            Assert.That(binding.type, Is.EqualTo(typeof(SpriteRenderer)));
            Assert.That(binding.propertyName, Is.EqualTo("m_Sprite"));
            var curve = AnimationUtility.GetObjectReferenceCurve(clipResult.clip, binding);
            Assert.That(curve.Select(keyframe => keyframe.time), Is.EqualTo(new[] { 0f, 0.125f, 0.25f, 0.375f }).Within(0.0001f));
            Assert.That(curve.Select(keyframe => keyframe.value.name), Is.EqualTo(spritesheet.spriteNames));

            WriteEvidence(
                SpritesheetEvidencePath,
                "Task 7 spritesheet and AnimationClip export passed",
                "frameSequence=" + string.Join(",", frameSequence.framePaths),
                "spritesheet=" + spritesheet.spritesheetPath,
                "sheetSize=" + sheetTexture.width + "x" + sheetTexture.height,
                "sprites=" + string.Join(",", spritesheet.spriteNames),
                "clip=" + clipResult.clipPath,
                "curveTimes=" + string.Join(",", curve.Select(keyframe => CultureInvariant(keyframe.time))));
        }

        [Test]
        public void EmptyFrameListFailsWithoutWritingSpritesheet()
        {
            var root = CreateEvidenceDirectory("empty-frames");
            var outputPath = root + "/empty-sheet.png";

            var result = GeneratedAssetSpritesheetExporter.Export(GeneratedAssetManifest.CreateDefault(), new GeneratedAssetSpritesheetExportOptions
            {
                outputPath = outputPath,
                overwriteExisting = false
            });

            Assert.That(result.succeeded, Is.False);
            Assert.That(result.message, Does.Contain("frames"));
            Assert.That(File.Exists(outputPath), Is.False);

            WriteEvidence(
                EmptyFramesEvidencePath,
                "Task 7 empty frame export failure passed",
                "succeeded=" + result.succeeded,
                "message=" + result.message,
                "outputExists=" + File.Exists(outputPath));
        }

        private static GeneratedAssetManifest CreateFourFrameManifest(string root)
        {
            var colors = new[]
            {
                new Color32(255, 0, 0, 255),
                new Color32(0, 255, 0, 255),
                new Color32(0, 0, 255, 255),
                new Color32(255, 255, 0, 255)
            };
            var images = new GeneratedAssetImage[4];
            var sprites = new GeneratedAssetSprite[4];
            var frames = new GeneratedAssetFrame[4];
            var keyframes = new GeneratedAssetKeyframe[4];
            for (var index = 0; index < 4; index++)
            {
                var path = root + "/source-frame-" + index + ".png";
                WriteSolidPng(path, 4, 4, colors[index]);
                images[index] = new GeneratedAssetImage
                {
                    id = "image-" + index,
                    role = "frame",
                    path = path,
                    width = 4,
                    height = 4,
                    pixelsPerUnit = 16f,
                    alphaMode = "transparent"
                };
                sprites[index] = new GeneratedAssetSprite
                {
                    id = "sprite-" + index,
                    name = "walk_" + index,
                    imageId = images[index].id,
                    rect = new GeneratedAssetRect { x = 0f, y = 0f, width = 4f, height = 4f },
                    pivot = new GeneratedAssetVector2 { x = 0.5f, y = 0.5f }
                };
                frames[index] = new GeneratedAssetFrame
                {
                    id = "frame-" + index,
                    name = "walk_" + index,
                    imageId = images[index].id,
                    spriteId = sprites[index].id,
                    index = index,
                    durationSeconds = 0.125f
                };
                keyframes[index] = new GeneratedAssetKeyframe
                {
                    frameId = frames[index].id,
                    timeSeconds = index * 0.125f
                };
            }

            var manifest = GeneratedAssetManifest.CreateDefault();
            manifest.images = images;
            manifest.sprites = sprites;
            manifest.frames = frames;
            manifest.animations = new[]
            {
                new GeneratedAssetAnimationClip
                {
                    id = "animation-walk",
                    name = "walk",
                    frameRate = 8f,
                    loop = true,
                    tracks = new[]
                    {
                        new GeneratedAssetAnimationTrack
                        {
                            id = "track-sprite",
                            property = "sprite",
                            keyframes = keyframes
                        }
                    }
                }
            };
            return manifest;
        }

        private static void WriteSolidPng(string path, int width, int height, Color32 color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(Enumerable.Repeat(color, width * height).ToArray());
            texture.Apply();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

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
            var path = PackageRoot + "/.sisyphus/evidence/task-7-fixtures/" + name + "-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(path);
            return path;
        }

        private static string CultureInvariant(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
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
