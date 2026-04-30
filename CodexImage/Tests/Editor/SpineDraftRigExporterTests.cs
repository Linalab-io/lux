using System;
using System.IO;
using System.Linq;
using Linalab.UnityCodexImage.Editor.Exporters.Spine;
using Linalab.UnityCodexImage.Editor.IR;
using NUnit.Framework;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Tests
{
    public sealed class SpineDraftRigExporterTests
    {
        private const string PackageRoot = "Packages/com.linalab.lux/CodexImage";
        private const string JsonValidationEvidencePath = PackageRoot + "/.sisyphus/evidence/task-9-spine-json-validation.txt";
        private const string MissingAttachmentEvidencePath = PackageRoot + "/.sisyphus/evidence/task-9-spine-missing-attachment.txt";
        private const string InvalidSlotReferenceEvidencePath = PackageRoot + "/.sisyphus/evidence/task-11-spine-invalid-slot-reference.txt";

        [Test]
        public void ExportsSpine42DraftSkeletonAtlasAndPartPngs()
        {
            var root = CreateEvidenceDirectory("json-validation");
            var manifest = CreateHumanoidManifest(root);
            var outputDirectory = root + "/spine";

            var result = new SpineDraftRigExporter().Export(manifest, new SpineDraftRigExportOptions
            {
                SkeletonName = "Task 9 Draft Rig",
                OutputDirectory = outputDirectory
            });

            Assert.That(result.Validation.IsValid, Is.True, result.Validation.Message);
            Assert.That(File.Exists(result.SkeletonJsonPath), Is.True);
            Assert.That(File.Exists(result.AtlasPath), Is.True);
            Assert.That(result.PartImagePaths.All(File.Exists), Is.True);

            var skeletonJson = File.ReadAllText(result.SkeletonJsonPath);
            Assert.That(skeletonJson, Does.Contain("\"spine\": \"4.2.00\""));
            Assert.That(skeletonJson, Does.Contain("Draft Rig / Auto Rig Attempt"));
            foreach (var boneName in new[] { "root", "torso", "head", "arm_l", "arm_r" })
            {
                Assert.That(skeletonJson, Does.Contain("\"name\": \"" + boneName + "\""));
            }

            var atlas = File.ReadAllText(result.AtlasPath);
            foreach (var regionName in result.RegionNames)
            {
                Assert.That(skeletonJson, Does.Contain("\"path\": \"" + regionName + "\""));
                Assert.That(atlas, Does.Contain(regionName + ".png"));
                Assert.That(atlas, Does.Contain(Environment.NewLine + regionName + Environment.NewLine));
            }

            WriteEvidence(
                JsonValidationEvidencePath,
                "Spine 4.2 draft JSON validation passed",
                "skeletonJsonPath=" + result.SkeletonJsonPath,
                "atlasPath=" + result.AtlasPath,
                "spineVersion=" + SpineDraftRigExporter.SpineVersion,
                "regions=" + string.Join(",", result.RegionNames),
                "validationStatus=" + result.Validation.Status);
        }

        [Test]
        public void ValidatorFailsWhenAttachmentImageIsMissing()
        {
            var root = CreateEvidenceDirectory("missing-attachment");
            var manifest = CreateHumanoidManifest(root);
            var result = new SpineDraftRigExporter().Export(manifest, new SpineDraftRigExportOptions
            {
                SkeletonName = "Task 9 Missing Attachment",
                OutputDirectory = root + "/spine"
            });
            File.Delete(result.PartImagePaths[0]);

            var validation = SpineDraftRigValidator.Validate(result.SkeletonJsonPath, result.AtlasPath, result.PartImagePaths);

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Errors.Any(error => error.Contains("attachment image is missing", StringComparison.Ordinal)), Is.True, validation.Message);

            WriteEvidence(
                MissingAttachmentEvidencePath,
                "Spine draft missing attachment validation failed as expected",
                "deletedImage=" + result.PartImagePaths[0],
                "validationStatus=" + validation.Status,
                "errors=" + string.Join(" | ", validation.Errors));
        }

        [Test]
        public void ValidatorFailsWhenSlotReferencesMissingBone()
        {
            var root = CreateEvidenceDirectory("invalid-slot-reference");
            var manifest = CreateHumanoidManifest(root);
            var result = new SpineDraftRigExporter().Export(manifest, new SpineDraftRigExportOptions
            {
                SkeletonName = "Task 11 Invalid Slot",
                OutputDirectory = root + "/spine"
            });
            var skeletonJson = File.ReadAllText(result.SkeletonJsonPath).Replace("\"bone\": \"torso\"", "\"bone\": \"missing_bone\"");
            File.WriteAllText(result.SkeletonJsonPath, skeletonJson);

            var validation = SpineDraftRigValidator.Validate(result.SkeletonJsonPath, result.AtlasPath, result.PartImagePaths);

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Errors.Any(error => error.Contains("Spine slot references missing bone: missing_bone", StringComparison.Ordinal)), Is.True, validation.Message);

            WriteEvidence(
                InvalidSlotReferenceEvidencePath,
                "Task 11 Spine invalid slot reference validation failed as expected",
                "skeletonJsonPath=" + result.SkeletonJsonPath,
                "validationStatus=" + validation.Status,
                "errors=" + string.Join(" | ", validation.Errors));
        }

        private static GeneratedAssetManifest CreateHumanoidManifest(string root)
        {
            var torsoPath = root + "/source_parts/torso.png";
            var headPath = root + "/source_parts/head.png";
            var leftArmPath = root + "/source_parts/left_arm.png";
            var rightArmPath = root + "/source_parts/right_arm.png";
            WritePng(torsoPath, 32, 48, new Color32(120, 40, 40, 255));
            WritePng(headPath, 24, 24, new Color32(220, 180, 140, 255));
            WritePng(leftArmPath, 12, 36, new Color32(80, 160, 220, 255));
            WritePng(rightArmPath, 12, 36, new Color32(80, 220, 160, 255));

            return new GeneratedAssetManifest
            {
                metadata = new GeneratedAssetMetadata
                {
                    assetId = "task-9-humanoid",
                    displayName = "Task 9 Humanoid Draft Rig Fixture"
                },
                images = new[]
                {
                    CreateImage("image-torso", "part", torsoPath, 32, 48),
                    CreateImage("image-head", "part", headPath, 24, 24),
                    CreateImage("image-left-arm", "part", leftArmPath, 12, 36),
                    CreateImage("image-right-arm", "part", rightArmPath, 12, 36)
                },
                sprites = new[]
                {
                    CreateSprite("sprite-torso", "Torso", "image-torso", 32, 48),
                    CreateSprite("sprite-head", "Head", "image-head", 24, 24),
                    CreateSprite("sprite-left-arm", "Left Arm", "image-left-arm", 12, 36),
                    CreateSprite("sprite-right-arm", "Right Arm", "image-right-arm", 12, 36)
                },
                exportHints = new GeneratedAssetExportHints { generateSpineDraft = true }
            };
        }

        private static GeneratedAssetImage CreateImage(string id, string role, string path, int width, int height)
        {
            return new GeneratedAssetImage
            {
                id = id,
                role = role,
                path = path,
                width = width,
                height = height,
                alphaMode = "transparent"
            };
        }

        private static GeneratedAssetSprite CreateSprite(string id, string name, string imageId, int width, int height)
        {
            return new GeneratedAssetSprite
            {
                id = id,
                name = name,
                imageId = imageId,
                rect = new GeneratedAssetRect { x = 0f, y = 0f, width = width, height = height },
                pivot = new GeneratedAssetVector2 { x = 0.5f, y = 0.5f }
            };
        }

        private static void WritePng(string path, int width, int height, Color32 color)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = Enumerable.Repeat(color, width * height).ToArray();
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }

        private static string CreateEvidenceDirectory(string name)
        {
            var path = PackageRoot + "/.sisyphus/evidence/task-9-fixtures/" + name + "-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(path);
            return path;
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
