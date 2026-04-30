using System;
using System.IO;
using System.Linq;
using Linalab.UnityCodexImage.Editor.Exporters.Unity2D;
using Linalab.UnityCodexImage.Editor.IR;
using NUnit.Framework;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Tests
{
    public sealed class Unity2DAnimationExporterTests
    {
        private const string PackageRoot = "Packages/com.linalab.lux/CodexImage";
        private const string AbsentEvidencePath = PackageRoot + "/.sisyphus/evidence/task-8-unity2d-absent.txt";
        private const string DraftPrefabEvidencePath = PackageRoot + "/.sisyphus/evidence/task-8-unity2d-draft-prefab.txt";

        [Test]
        public void WritesLayeredPngAndJsonHandoffWhenOptionalPackagesAreAbsent()
        {
            var root = CreateEvidenceDirectory("absent");
            var layerPath = root + "/head.png";
            WriteTexture(layerPath, 4, 4, new Color32(80, 120, 200, 255));
            var manifest = CreateManifest(layerPath);
            var writer = new RecordingDraftPrefabWriter(true);
            var exporter = new Unity2DAnimationExporter(new FixedAvailability(false, false), writer);

            var result = exporter.Export(manifest, root + "/unity2d");

            Assert.That(result.Status, Is.EqualTo(Unity2DExportStatus.Skipped));
            Assert.That(result.MissingPackages, Is.EquivalentTo(new[] { "com.unity.2d.animation", "com.unity.2d.psdimporter" }));
            Assert.That(File.Exists(result.LayeredPngPath), Is.True);
            Assert.That(File.Exists(result.HandoffManifestPath), Is.True);
            Assert.That(writer.WasCalled, Is.False);
            Assert.That(manifest.unity2DExports.Single().status, Is.EqualTo("skipped"));
            Assert.That(manifest.unity2DExports.Single().layeredPngPath, Is.EqualTo(result.LayeredPngPath));
            Assert.That(manifest.warnings.Single().code, Is.EqualTo("unity2d-animation-packages-missing"));

            var handoff = GeneratedAssetManifest.FromJson(File.ReadAllText(result.HandoffManifestPath));
            Assert.That(handoff.unity2DExports.Single().status, Is.EqualTo("skipped"));
            Assert.That(handoff.unity2DExports.Single().missingPackages, Is.EquivalentTo(result.MissingPackages));

            WriteEvidence(
                AbsentEvidencePath,
                "Unity2D optional package absence handoff passed",
                "status=" + result.Status,
                "layeredPngPath=" + result.LayeredPngPath,
                "handoffManifestPath=" + result.HandoffManifestPath,
                "missingPackages=" + string.Join(",", result.MissingPackages),
                "warning=" + manifest.warnings.Single().code);
        }

        [Test]
        public void CreatesDraftPrefabHandoffThroughPackageAvailableWriter()
        {
            var root = CreateEvidenceDirectory("draft-prefab");
            var layerPath = root + "/torso.png";
            WriteTexture(layerPath, 4, 4, new Color32(200, 120, 80, 255));
            var manifest = CreateManifest(layerPath);
            var writer = new RecordingDraftPrefabWriter(true);
            var exporter = new Unity2DAnimationExporter(new FixedAvailability(true, true), writer);

            var result = exporter.Export(manifest, root + "/unity2d");

            Assert.That(result.Status, Is.EqualTo(Unity2DExportStatus.DraftPrefab));
            Assert.That(writer.WasCalled, Is.True);
            Assert.That(writer.CapturedPrefabPath, Does.EndWith("unity2d-spriteskin-draft.prefab"));
            Assert.That(writer.CapturedBoneNames, Is.EqualTo(new[] { "root", "torso", "head", "arm_l" }));
            Assert.That(result.BoneNames, Is.EqualTo(new[] { "root", "torso", "head", "arm_l" }));
            Assert.That(manifest.unity2DExports.Single().status, Is.EqualTo("draft-prefab"));
            Assert.That(manifest.unity2DExports.Single().draftPrefabPath, Is.EqualTo(writer.CapturedPrefabPath));
            Assert.That(File.Exists(result.HandoffManifestPath), Is.True);

            WriteEvidence(
                DraftPrefabEvidencePath,
                "Unity2D draft prefab handoff passed",
                "status=" + result.Status,
                "draftPrefabPath=" + result.DraftPrefabPath,
                "boneNames=" + string.Join(",", result.BoneNames),
                "handoffManifestPath=" + result.HandoffManifestPath);
        }

        private static GeneratedAssetManifest CreateManifest(string layerPath)
        {
            return new GeneratedAssetManifest
            {
                images = new[]
                {
                    new GeneratedAssetImage
                    {
                        id = "part-image-head",
                        role = "part",
                        path = layerPath,
                        width = 4,
                        height = 4,
                        alphaMode = "transparent"
                    }
                },
                layers = new[]
                {
                    new GeneratedAssetLayer
                    {
                        id = "layer-head",
                        name = "head",
                        imageId = "part-image-head",
                        order = 0,
                        rect = new GeneratedAssetRect { x = 0f, y = 0f, width = 4f, height = 4f }
                    }
                },
                rigBones = new[]
                {
                    new GeneratedAssetRigBone { id = "bone-root", name = "root" },
                    new GeneratedAssetRigBone { id = "bone-torso", name = "torso", parentId = "bone-root" },
                    new GeneratedAssetRigBone { id = "bone-head", name = "head", parentId = "bone-torso" },
                    new GeneratedAssetRigBone { id = "bone-arm-l", name = "arm_l", parentId = "bone-torso" }
                },
                exportHints = new GeneratedAssetExportHints { generateUnity2DAnimationDraft = true }
            };
        }

        private static void WriteTexture(string path, int width, int height, Color32 color)
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
            var path = PackageRoot + "/.sisyphus/evidence/task-8-fixtures/" + name + "-" + Guid.NewGuid().ToString("N");
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

        private sealed class FixedAvailability : IUnity2DPackageAvailability
        {
            private readonly bool hasUnity2DAnimation;
            private readonly bool hasPsdImporter;

            public FixedAvailability(bool hasUnity2DAnimation, bool hasPsdImporter)
            {
                this.hasUnity2DAnimation = hasUnity2DAnimation;
                this.hasPsdImporter = hasPsdImporter;
            }

            public Unity2DPackageAvailability GetAvailability()
            {
                return new Unity2DPackageAvailability(hasUnity2DAnimation, hasPsdImporter);
            }
        }

        private sealed class RecordingDraftPrefabWriter : IUnity2DDraftPrefabWriter
        {
            private readonly bool created;

            public RecordingDraftPrefabWriter(bool created)
            {
                this.created = created;
            }

            public bool WasCalled { get; private set; }
            public string CapturedPrefabPath { get; private set; }
            public string[] CapturedBoneNames { get; private set; }

            public Unity2DDraftPrefabResult WriteDraftPrefab(GeneratedAssetManifest manifest, string prefabPath, string[] boneNames)
            {
                WasCalled = true;
                CapturedPrefabPath = prefabPath;
                CapturedBoneNames = boneNames;
                return new Unity2DDraftPrefabResult(created, prefabPath, boneNames, "test draft prefab writer");
            }
        }
    }
}
