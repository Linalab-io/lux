using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Linalab.UnityCodexImage.Editor.IR;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Exporters.Unity2D
{
    public sealed class Unity2DAnimationExporter
    {
        private const string AnimationPackageName = "com.unity.2d.animation";
        private const string PsdImporterPackageName = "com.unity.2d.psdimporter";
        private const string LayeredPngFileName = "unity2d-layered.png";
        private const string HandoffManifestFileName = "unity2d-handoff.json";
        private const string DraftPrefabFileName = "unity2d-spriteskin-draft.prefab";

        private readonly IUnity2DPackageAvailability packageAvailability;
        private readonly IUnity2DDraftPrefabWriter draftPrefabWriter;

        public Unity2DAnimationExporter()
            : this(new UnityPackageManagerAvailability(), new UnityPrefabDraftWriter())
        {
        }

        public Unity2DAnimationExporter(IUnity2DPackageAvailability packageAvailability, IUnity2DDraftPrefabWriter draftPrefabWriter)
        {
            this.packageAvailability = packageAvailability ?? throw new ArgumentNullException(nameof(packageAvailability));
            this.draftPrefabWriter = draftPrefabWriter ?? throw new ArgumentNullException(nameof(draftPrefabWriter));
        }

        public Unity2DExportResult Export(GeneratedAssetManifest manifest, string outputDirectory)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
            }

            Directory.CreateDirectory(outputDirectory);
            var normalizedOutputDirectory = NormalizePath(outputDirectory);
            var availability = packageAvailability.GetAvailability();
            var layeredPngPath = NormalizePath(Path.Combine(normalizedOutputDirectory, LayeredPngFileName));
            WriteLayeredPng(manifest, layeredPngPath);

            if (!availability.HasUnity2DAnimation || !availability.HasPsdImporter)
            {
                var missingPackages = availability.MissingPackageNames;
                var result = Unity2DExportResult.Skipped(
                    layeredPngPath,
                    NormalizePath(Path.Combine(normalizedOutputDirectory, HandoffManifestFileName)),
                    missingPackages,
                    "Unity 2D Animation and PSD Importer are optional; wrote layered PNG and JSON handoff instead of a SpriteSkin draft.");
                RecordExport(manifest, result);
                WriteHandoffManifest(manifest, result.HandoffManifestPath);
                return result;
            }

            var requestedBoneNames = ResolveBoneNames(manifest);
            var draftPrefabPath = NormalizePath(Path.Combine(normalizedOutputDirectory, DraftPrefabFileName));
            var prefabResult = draftPrefabWriter.WriteDraftPrefab(manifest, draftPrefabPath, requestedBoneNames);
            var status = prefabResult.Created ? Unity2DExportStatus.DraftPrefab : Unity2DExportStatus.Partial;
            var draftResult = new Unity2DExportResult(
                status,
                layeredPngPath,
                NormalizePath(Path.Combine(normalizedOutputDirectory, HandoffManifestFileName)),
                prefabResult.PrefabPath,
                prefabResult.BoneNames,
                Array.Empty<string>(),
                prefabResult.Message);
            RecordExport(manifest, draftResult);
            WriteHandoffManifest(manifest, draftResult.HandoffManifestPath);
            return draftResult;
        }

        private static void RecordExport(GeneratedAssetManifest manifest, Unity2DExportResult result)
        {
            var exports = manifest.unity2DExports ?? Array.Empty<GeneratedAssetUnity2DExport>();
            manifest.unity2DExports = exports.Concat(new[]
            {
                new GeneratedAssetUnity2DExport
                {
                    status = result.Status.ToSerializedValue(),
                    layeredPngPath = result.LayeredPngPath,
                    handoffManifestPath = result.HandoffManifestPath,
                    draftPrefabPath = result.DraftPrefabPath,
                    boneNames = result.BoneNames,
                    missingPackages = result.MissingPackages,
                    message = result.Message
                }
            }).ToArray();

            if (result.Status == Unity2DExportStatus.Skipped)
            {
                manifest.warnings = (manifest.warnings ?? Array.Empty<GeneratedAssetWarning>()).Concat(new[]
                {
                    new GeneratedAssetWarning
                    {
                        id = "warning-unity2d-optional-packages",
                        sourceId = "unity2d-export",
                        code = "unity2d-animation-packages-missing",
                        message = result.Message
                    }
                }).ToArray();
            }
        }

        private static string[] ResolveBoneNames(GeneratedAssetManifest manifest)
        {
            var names = (manifest.rigBones ?? Array.Empty<GeneratedAssetRigBone>())
                .Where(bone => !string.IsNullOrWhiteSpace(bone.name))
                .Select(bone => bone.name.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return names.Length == 0 ? new[] { "root" } : names;
        }

        private static void WriteLayeredPng(GeneratedAssetManifest manifest, string outputPath)
        {
            var imagesById = (manifest.images ?? Array.Empty<GeneratedAssetImage>())
                .Where(image => !string.IsNullOrWhiteSpace(image.id))
                .GroupBy(image => image.id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var layers = (manifest.layers ?? Array.Empty<GeneratedAssetLayer>())
                .OrderBy(layer => layer.order)
                .ToArray();
            var sourceImage = (manifest.images ?? Array.Empty<GeneratedAssetImage>())
                .FirstOrDefault(image => string.Equals(image.role, "source", StringComparison.Ordinal) && File.Exists(image.path));

            Texture2D canvas;
            if (sourceImage != null)
            {
                canvas = new Texture2D(sourceImage.width, sourceImage.height, TextureFormat.RGBA32, false);
            }
            else
            {
                var width = Math.Max(1, (int)Math.Ceiling(layers.Select(layer => layer.rect.x + layer.rect.width).DefaultIfEmpty(1f).Max()));
                var height = Math.Max(1, (int)Math.Ceiling(layers.Select(layer => layer.rect.y + layer.rect.height).DefaultIfEmpty(1f).Max()));
                canvas = new Texture2D(width, height, TextureFormat.RGBA32, false);
            }

            Fill(canvas, new Color32(0, 0, 0, 0));
            foreach (var layer in layers.Where(layer => layer.visible))
            {
                if (!imagesById.TryGetValue(layer.imageId ?? string.Empty, out var image) || string.IsNullOrWhiteSpace(image.path) || !File.Exists(image.path))
                {
                    continue;
                }

                var texture = LoadTexture(image.path);
                Blend(canvas, texture, Mathf.RoundToInt(layer.rect.x), Mathf.RoundToInt(layer.rect.y), Mathf.Clamp01(layer.opacity));
            }

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(outputPath, canvas.EncodeToPNG());
        }

        private static void WriteHandoffManifest(GeneratedAssetManifest manifest, string outputPath)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, manifest.ToJson());
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

        private static void Fill(Texture2D texture, Color32 color)
        {
            var pixels = new Color32[texture.width * texture.height];
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = color;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
        }

        private static void Blend(Texture2D target, Texture2D source, int offsetX, int offsetY, float opacity)
        {
            var targetPixels = target.GetPixels32();
            var sourcePixels = source.GetPixels32();
            for (var y = 0; y < source.height; y++)
            {
                var targetY = offsetY + y;
                if (targetY < 0 || targetY >= target.height)
                {
                    continue;
                }

                for (var x = 0; x < source.width; x++)
                {
                    var targetX = offsetX + x;
                    if (targetX < 0 || targetX >= target.width)
                    {
                        continue;
                    }

                    var sourcePixel = sourcePixels[y * source.width + x];
                    var alpha = Mathf.Clamp01(sourcePixel.a / 255f * opacity);
                    if (alpha <= 0f)
                    {
                        continue;
                    }

                    var targetIndex = targetY * target.width + targetX;
                    var targetPixel = targetPixels[targetIndex];
                    var inverseAlpha = 1f - alpha;
                    targetPixels[targetIndex] = new Color32(
                        (byte)Mathf.RoundToInt(sourcePixel.r * alpha + targetPixel.r * inverseAlpha),
                        (byte)Mathf.RoundToInt(sourcePixel.g * alpha + targetPixel.g * inverseAlpha),
                        (byte)Mathf.RoundToInt(sourcePixel.b * alpha + targetPixel.b * inverseAlpha),
                        (byte)Mathf.RoundToInt((alpha + targetPixel.a / 255f * inverseAlpha) * 255f));
                }
            }

            target.SetPixels32(targetPixels);
            target.Apply();
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private sealed class UnityPackageManagerAvailability : IUnity2DPackageAvailability
        {
            public Unity2DPackageAvailability GetAvailability()
            {
                return new Unity2DPackageAvailability(
                    UnityEditor.PackageManager.PackageInfo.FindForPackageName(AnimationPackageName) != null,
                    UnityEditor.PackageManager.PackageInfo.FindForPackageName(PsdImporterPackageName) != null);
            }
        }

        private sealed class UnityPrefabDraftWriter : IUnity2DDraftPrefabWriter
        {
            public Unity2DDraftPrefabResult WriteDraftPrefab(GeneratedAssetManifest manifest, string prefabPath, string[] boneNames)
            {
                var root = new GameObject("Unity2DAnimationDraft");
                try
                {
                    var bonesRoot = new GameObject("Bones");
                    bonesRoot.transform.SetParent(root.transform, false);
                    foreach (var boneName in boneNames)
                    {
                        var bone = new GameObject(boneName);
                        bone.transform.SetParent(bonesRoot.transform, false);
                    }

                    var spriteSkinAdded = TryAddSpriteSkin(root);

                    var directory = Path.GetDirectoryName(prefabPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    return new Unity2DDraftPrefabResult(
                        saved != null,
                        NormalizePath(prefabPath),
                        boneNames,
                        saved != null
                            ? "Draft SpriteSkin prefab handoff was created with requested bone transforms; SpriteSkin component added=" + spriteSkinAdded + "."
                            : "Draft SpriteSkin prefab handoff could not be saved.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            private static bool TryAddSpriteSkin(GameObject root)
            {
                var spriteSkinType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("UnityEngine.U2D.Animation.SpriteSkin"))
                    .FirstOrDefault(type => type != null);
                if (spriteSkinType == null)
                {
                    return false;
                }

                root.AddComponent(spriteSkinType);
                return true;
            }
        }
    }

    public interface IUnity2DPackageAvailability
    {
        Unity2DPackageAvailability GetAvailability();
    }

    public interface IUnity2DDraftPrefabWriter
    {
        Unity2DDraftPrefabResult WriteDraftPrefab(GeneratedAssetManifest manifest, string prefabPath, string[] boneNames);
    }

    public readonly struct Unity2DPackageAvailability
    {
        public Unity2DPackageAvailability(bool hasUnity2DAnimation, bool hasPsdImporter)
        {
            HasUnity2DAnimation = hasUnity2DAnimation;
            HasPsdImporter = hasPsdImporter;
        }

        public bool HasUnity2DAnimation { get; }
        public bool HasPsdImporter { get; }

        public string[] MissingPackageNames
        {
            get
            {
                var missing = new List<string>();
                if (!HasUnity2DAnimation)
                {
                    missing.Add("com.unity.2d.animation");
                }

                if (!HasPsdImporter)
                {
                    missing.Add("com.unity.2d.psdimporter");
                }

                return missing.ToArray();
            }
        }
    }

    public readonly struct Unity2DDraftPrefabResult
    {
        public Unity2DDraftPrefabResult(bool created, string prefabPath, string[] boneNames, string message)
        {
            Created = created;
            PrefabPath = prefabPath;
            BoneNames = boneNames ?? Array.Empty<string>();
            Message = message ?? string.Empty;
        }

        public bool Created { get; }
        public string PrefabPath { get; }
        public string[] BoneNames { get; }
        public string Message { get; }
    }

    public sealed class Unity2DExportResult
    {
        public Unity2DExportResult(
            Unity2DExportStatus status,
            string layeredPngPath,
            string handoffManifestPath,
            string draftPrefabPath,
            string[] boneNames,
            string[] missingPackages,
            string message)
        {
            Status = status;
            LayeredPngPath = layeredPngPath;
            HandoffManifestPath = handoffManifestPath;
            DraftPrefabPath = draftPrefabPath;
            BoneNames = boneNames ?? Array.Empty<string>();
            MissingPackages = missingPackages ?? Array.Empty<string>();
            Message = message ?? string.Empty;
        }

        public Unity2DExportStatus Status { get; }
        public string LayeredPngPath { get; }
        public string HandoffManifestPath { get; }
        public string DraftPrefabPath { get; }
        public string[] BoneNames { get; }
        public string[] MissingPackages { get; }
        public string Message { get; }

        public static Unity2DExportResult Skipped(string layeredPngPath, string handoffManifestPath, string[] missingPackages, string message)
        {
            return new Unity2DExportResult(
                Unity2DExportStatus.Skipped,
                layeredPngPath,
                handoffManifestPath,
                string.Empty,
                Array.Empty<string>(),
                missingPackages,
                message);
        }
    }

    public enum Unity2DExportStatus
    {
        Skipped,
        DraftPrefab,
        Partial
    }

    internal static class Unity2DExportStatusExtensions
    {
        public static string ToSerializedValue(this Unity2DExportStatus status)
        {
            switch (status)
            {
                case Unity2DExportStatus.DraftPrefab:
                    return "draft-prefab";
                case Unity2DExportStatus.Partial:
                    return "partial";
                default:
                    return "skipped";
            }
        }
    }
}
