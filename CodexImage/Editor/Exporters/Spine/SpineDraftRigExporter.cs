using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Linalab.UnityCodexImage.Editor.IR;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor.Exporters.Spine
{
    public sealed class SpineDraftRigExportOptions
    {
        public string SkeletonName = "codex_draft_rig";
        public string OutputDirectory;
    }

    public sealed class SpineDraftRigExportResult
    {
        public string OutputDirectory;
        public string SkeletonJsonPath;
        public string AtlasPath;
        public string[] PartImagePaths = Array.Empty<string>();
        public string[] RegionNames = Array.Empty<string>();
        public SpineDraftRigValidationResult Validation;
    }

    public sealed class SpineDraftRigExporter
    {
        public const string SpineVersion = "4.2.00";
        private const string DraftMarker = "Draft Rig / Auto Rig Attempt";

        public SpineDraftRigExportResult Export(GeneratedAssetManifest manifest, SpineDraftRigExportOptions options)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                throw new ArgumentException("Spine draft rig output directory is required.", nameof(options));
            }

            var outputDirectory = NormalizePath(options.OutputDirectory);
            Directory.CreateDirectory(outputDirectory);

            var parts = ResolveParts(manifest);
            if (parts.Length == 0)
            {
                throw new InvalidOperationException("Spine draft rig export requires at least one part image from the generated asset manifest.");
            }

            CopyPartImages(parts, outputDirectory);

            var skeletonName = SanitizeName(options.SkeletonName, "codex_draft_rig");
            var atlasPath = NormalizePath(Path.Combine(outputDirectory, skeletonName + ".atlas.txt"));
            var jsonPath = NormalizePath(Path.Combine(outputDirectory, skeletonName + ".json.txt"));
            File.WriteAllText(atlasPath, BuildAtlas(parts), Encoding.UTF8);
            File.WriteAllText(jsonPath, BuildSkeletonJson(skeletonName, parts), Encoding.UTF8);

            var validation = SpineDraftRigValidator.Validate(jsonPath, atlasPath, parts.Select(part => part.outputPath).ToArray());
            return new SpineDraftRigExportResult
            {
                OutputDirectory = outputDirectory,
                SkeletonJsonPath = jsonPath,
                AtlasPath = atlasPath,
                PartImagePaths = parts.Select(part => part.outputPath).ToArray(),
                RegionNames = parts.Select(part => part.regionName).ToArray(),
                Validation = validation
            };
        }

        private static DraftPart[] ResolveParts(GeneratedAssetManifest manifest)
        {
            var imagesById = (manifest.images ?? Array.Empty<GeneratedAssetImage>())
                .Where(image => image != null && !string.IsNullOrWhiteSpace(image.id))
                .GroupBy(image => image.id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var sprites = manifest.sprites ?? Array.Empty<GeneratedAssetSprite>();
            var partSprites = sprites.Where(sprite => sprite != null && !string.IsNullOrWhiteSpace(sprite.imageId) && imagesById.ContainsKey(sprite.imageId)).ToArray();

            if (partSprites.Length > 0)
            {
                return partSprites.Select((sprite, index) => ToPart(sprite, imagesById[sprite.imageId], index)).ToArray();
            }

            return (manifest.images ?? Array.Empty<GeneratedAssetImage>())
                .Where(image => image != null && string.Equals(image.role, "part", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(image.path))
                .Select((image, index) => ToPart(null, image, index))
                .ToArray();
        }

        private static DraftPart ToPart(GeneratedAssetSprite sprite, GeneratedAssetImage image, int index)
        {
            var displayName = sprite == null || string.IsNullOrWhiteSpace(sprite.name) ? Path.GetFileNameWithoutExtension(image.path) : sprite.name;
            var regionName = UniqueRegionName(SanitizeName(displayName, "part_" + (index + 1).ToString(CultureInfo.InvariantCulture)), index);
            var width = Math.Max(1, Mathf.RoundToInt(sprite?.rect?.width ?? image.width));
            var height = Math.Max(1, Mathf.RoundToInt(sprite?.rect?.height ?? image.height));
            if ((width <= 1 || height <= 1) && File.Exists(image.path))
            {
                var size = ReadTextureSize(image.path);
                width = size.x;
                height = size.y;
            }

            return new DraftPart
            {
                sourcePath = NormalizePath(image.path),
                regionName = regionName,
                boneName = BoneFor(regionName),
                drawOrder = index,
                width = width,
                height = height
            };
        }

        private static void CopyPartImages(DraftPart[] parts, string outputDirectory)
        {
            var regionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part.sourcePath) || !File.Exists(part.sourcePath))
                {
                    throw new FileNotFoundException("Spine draft rig attachment image is missing.", part.sourcePath);
                }

                var baseName = part.regionName;
                if (regionCounts.TryGetValue(baseName, out var count))
                {
                    count++;
                    regionCounts[baseName] = count;
                    part.regionName = baseName + "_" + count.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    regionCounts[baseName] = 1;
                }

                part.outputPath = NormalizePath(Path.Combine(outputDirectory, part.regionName + ".png"));
                File.Copy(part.sourcePath, part.outputPath, true);
                var size = ReadTextureSize(part.outputPath);
                part.width = size.x;
                part.height = size.y;
            }
        }

        private static string BuildAtlas(DraftPart[] parts)
        {
            var builder = new StringBuilder();
            foreach (var part in parts)
            {
                builder.AppendLine(part.regionName + ".png");
                builder.AppendLine("size: " + part.width.ToString(CultureInfo.InvariantCulture) + "," + part.height.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("format: RGBA8888");
                builder.AppendLine("filter: Linear,Linear");
                builder.AppendLine("repeat: none");
                builder.AppendLine(part.regionName);
                builder.AppendLine("  rotate: false");
                builder.AppendLine("  xy: 0, 0");
                builder.AppendLine("  size: " + part.width.ToString(CultureInfo.InvariantCulture) + ", " + part.height.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("  orig: " + part.width.ToString(CultureInfo.InvariantCulture) + ", " + part.height.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("  offset: 0, 0");
                builder.AppendLine("  index: -1");
            }

            return builder.ToString();
        }

        private static string BuildSkeletonJson(string skeletonName, DraftPart[] parts)
        {
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"skeleton\": {");
            builder.AppendLine("    \"spine\": \"" + SpineVersion + "\",");
            builder.AppendLine("    \"version\": \"" + DraftMarker + "\",");
            builder.AppendLine("    \"hash\": \"auto-draft\",");
            builder.AppendLine("    \"name\": \"" + EscapeJson(skeletonName) + "\",");
            builder.AppendLine("    \"images\": \"./\"");
            builder.AppendLine("  },");
            builder.AppendLine("  \"bones\": [");
            AppendBone(builder, "root", null, 0f, 0f, 0f, false);
            AppendBone(builder, "torso", "root", 0f, 80f, 120f, true);
            AppendBone(builder, "head", "torso", 0f, 110f, 45f, true);
            AppendBone(builder, "arm_l", "torso", -45f, 70f, 90f, true);
            AppendBone(builder, "arm_r", "torso", 45f, 70f, 90f, false);
            builder.AppendLine("  ],");
            builder.AppendLine("  \"slots\": [");
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                builder.Append("    { \"name\": \"").Append(EscapeJson(part.regionName)).Append("\", \"bone\": \"").Append(part.boneName).Append("\", \"color\": \"FFFFFFFF\", \"attachment\": \"").Append(EscapeJson(part.regionName)).Append("\", \"blendMode\": \"normal\" }");
                builder.AppendLine(index == parts.Length - 1 ? string.Empty : ",");
            }

            builder.AppendLine("  ],");
            builder.AppendLine("  \"skins\": [");
            builder.AppendLine("    {");
            builder.AppendLine("      \"name\": \"default\",");
            builder.AppendLine("      \"attachments\": {");
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                builder.AppendLine("        \"" + EscapeJson(part.regionName) + "\": {");
                builder.Append("          \"").Append(EscapeJson(part.regionName)).Append("\": { \"type\": \"region\", \"path\": \"").Append(EscapeJson(part.regionName)).Append("\", \"x\": 0, \"y\": 0, \"width\": ").Append(part.width.ToString(CultureInfo.InvariantCulture)).Append(", \"height\": ").Append(part.height.ToString(CultureInfo.InvariantCulture)).Append(" }").AppendLine();
                builder.Append("        }").AppendLine(index == parts.Length - 1 ? string.Empty : ",");
            }

            builder.AppendLine("      }");
            builder.AppendLine("    }");
            builder.AppendLine("  ],");
            builder.AppendLine("  \"animations\": { \"draft_default_pose\": {} },");
            builder.AppendLine("  \"physics\": []");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendBone(StringBuilder builder, string name, string parent, float x, float y, float length, bool trailingComma)
        {
            builder.Append("    { \"name\": \"").Append(name).Append("\"");
            if (!string.IsNullOrEmpty(parent))
            {
                builder.Append(", \"parent\": \"").Append(parent).Append("\"");
            }

            builder.Append(", \"x\": ").Append(x.ToString(CultureInfo.InvariantCulture));
            builder.Append(", \"y\": ").Append(y.ToString(CultureInfo.InvariantCulture));
            builder.Append(", \"length\": ").Append(length.ToString(CultureInfo.InvariantCulture));
            builder.Append(" }").AppendLine(trailingComma ? "," : string.Empty);
        }

        private static string BoneFor(string regionName)
        {
            var name = regionName.ToLowerInvariant();
            if (name.Contains("head") || name.Contains("face") || name.Contains("hair"))
            {
                return "head";
            }

            if (name.Contains("left") || name.EndsWith("_l", StringComparison.Ordinal))
            {
                return "arm_l";
            }

            if (name.Contains("right") || name.EndsWith("_r", StringComparison.Ordinal) || name.Contains("arm"))
            {
                return "arm_r";
            }

            return "torso";
        }

        private static Vector2Int ReadTextureSize(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                throw new InvalidOperationException("PNG could not be decoded: " + path);
            }

            return new Vector2Int(Math.Max(1, texture.width), Math.Max(1, texture.height));
        }

        private static string UniqueRegionName(string baseName, int index)
        {
            return string.IsNullOrWhiteSpace(baseName) ? "part_" + (index + 1).ToString(CultureInfo.InvariantCulture) : baseName;
        }

        private static string SanitizeName(string value, string fallback)
        {
            var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
            var builder = new StringBuilder(source.Length);
            var previousSeparator = false;
            foreach (var character in source)
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
            return string.IsNullOrEmpty(result) ? fallback : result;
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');
        }

        private sealed class DraftPart
        {
            public string sourcePath;
            public string outputPath;
            public string regionName;
            public string boneName;
            public int drawOrder;
            public int width;
            public int height;
        }
    }
}
