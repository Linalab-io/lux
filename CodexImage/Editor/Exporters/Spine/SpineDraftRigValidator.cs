using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Linalab.UnityCodexImage.Editor.Exporters.Spine
{
    public enum SpineDraftRigValidationStatus
    {
        Valid,
        Invalid
    }

    public readonly struct SpineDraftRigValidationResult
    {
        public SpineDraftRigValidationResult(SpineDraftRigValidationStatus status, string[] errors)
        {
            Status = status;
            Errors = errors ?? Array.Empty<string>();
        }

        public SpineDraftRigValidationStatus Status { get; }
        public string[] Errors { get; }
        public bool IsValid => Status == SpineDraftRigValidationStatus.Valid;
        public string Message => IsValid ? "Spine draft rig export is valid." : string.Join(" ", Errors);

        public static SpineDraftRigValidationResult Valid()
        {
            return new SpineDraftRigValidationResult(SpineDraftRigValidationStatus.Valid, Array.Empty<string>());
        }

        public static SpineDraftRigValidationResult Invalid(IEnumerable<string> errors)
        {
            return new SpineDraftRigValidationResult(SpineDraftRigValidationStatus.Invalid, (errors ?? Array.Empty<string>()).ToArray());
        }
    }

    public static class SpineDraftRigValidator
    {
        public static SpineDraftRigValidationResult Validate(string skeletonJsonPath, string atlasPath, string[] imagePaths)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(skeletonJsonPath) || !File.Exists(skeletonJsonPath))
            {
                errors.Add("Spine skeleton .json.txt file is missing.");
            }

            if (string.IsNullOrWhiteSpace(atlasPath) || !File.Exists(atlasPath))
            {
                errors.Add("Spine .atlas.txt file is missing.");
            }

            foreach (var imagePath in imagePaths ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    errors.Add("Spine attachment image is missing: " + imagePath);
                }
            }

            if (errors.Count > 0)
            {
                return SpineDraftRigValidationResult.Invalid(errors);
            }

            var json = File.ReadAllText(skeletonJsonPath);
            var atlas = File.ReadAllText(atlasPath);
            ValidateSkeletonJson(json, errors);
            ValidateAtlasAndImages(json, atlas, imagePaths ?? Array.Empty<string>(), errors);
            return errors.Count == 0 ? SpineDraftRigValidationResult.Valid() : SpineDraftRigValidationResult.Invalid(errors);
        }

        private static void ValidateSkeletonJson(string json, ICollection<string> errors)
        {
            if (!Regex.IsMatch(json, "\\\"spine\\\"\\s*:\\s*\\\"4\\.2(?:\\.[0-9]+)?\\\""))
            {
                errors.Add("Spine skeleton.spine must be a 4.2-compatible version.");
            }

            var boneNames = MatchValues(json, "\\\"name\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"")
                .Where(name => IsDraftBoneName(name))
                .ToHashSet(StringComparer.Ordinal);
            if (boneNames.Count == 0)
            {
                errors.Add("Spine skeleton must contain at least one bone.");
            }

            foreach (var requiredBone in new[] { "root", "torso", "head", "arm_l", "arm_r" })
            {
                if (!boneNames.Contains(requiredBone))
                {
                    errors.Add("Spine draft rig is missing required bone: " + requiredBone);
                }
            }

            foreach (var slotBone in MatchValues(json, "\\\"bone\\\"\\s*:\\s*\\\"([^\\\"]+)\\\""))
            {
                if (!boneNames.Contains(slotBone))
                {
                    errors.Add("Spine slot references missing bone: " + slotBone);
                }
            }

            if (!json.Contains("\"skins\"", StringComparison.Ordinal) || !json.Contains("\"attachments\"", StringComparison.Ordinal))
            {
                errors.Add("Spine skeleton must contain skins with attachments.");
            }
        }

        private static void ValidateAtlasAndImages(string json, string atlas, string[] imagePaths, ICollection<string> errors)
        {
            var attachmentRegions = MatchValues(json, "\\\"path\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"")
                .Where(region => !string.Equals(region, "./", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);
            if (attachmentRegions.Count == 0)
            {
                errors.Add("Spine skeleton must contain attachment region paths.");
            }

            var atlasRegions = ParseAtlasRegions(atlas).ToHashSet(StringComparer.Ordinal);
            foreach (var region in attachmentRegions)
            {
                if (!atlasRegions.Contains(region))
                {
                    errors.Add("Spine attachment region is missing from atlas: " + region);
                }
            }

            var imageRegions = imagePaths.Select(path => Path.GetFileNameWithoutExtension(path)).ToHashSet(StringComparer.Ordinal);
            foreach (var region in atlasRegions)
            {
                if (!imageRegions.Contains(region))
                {
                    errors.Add("Spine atlas region does not have a matching PNG image: " + region);
                }
            }

            foreach (var region in imageRegions)
            {
                if (!atlasRegions.Contains(region))
                {
                    errors.Add("Spine PNG image does not have a matching atlas region: " + region);
                }
            }
        }

        private static IEnumerable<string> ParseAtlasRegions(string atlas)
        {
            var lines = atlas.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToArray();
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || line.Contains(":", StringComparison.Ordinal))
                {
                    continue;
                }

                if (index + 1 < lines.Length && lines[index + 1].StartsWith("rotate:", StringComparison.Ordinal))
                {
                    yield return line;
                }
            }
        }

        private static IEnumerable<string> MatchValues(string text, string pattern)
        {
            return Regex.Matches(text ?? string.Empty, pattern).Cast<Match>().Select(match => match.Groups[1].Value);
        }

        private static bool IsDraftBoneName(string name)
        {
            return string.Equals(name, "root", StringComparison.Ordinal)
                || string.Equals(name, "torso", StringComparison.Ordinal)
                || string.Equals(name, "head", StringComparison.Ordinal)
                || string.Equals(name, "arm_l", StringComparison.Ordinal)
                || string.Equals(name, "arm_r", StringComparison.Ordinal);
        }
    }
}
