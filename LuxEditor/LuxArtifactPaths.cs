using System;
using System.Collections.Generic;
using System.IO;

namespace Linalab.Lux.Editor
{
    public static class LuxArtifactPaths
    {
        const string PersistentOutputRelativePath = ".lux/outputs";
        const string ScreenshotOutputRelativePath = "Temp/Lux/Screenshots";

        public static string GetPersistentOutputPath(string feature)
        {
            string projectRoot = LuxBridgeSettings.GetProjectRoot();
            string featureSegment = ValidateFeatureSegment(feature);
            string outputPath = EnsureProjectLocalPath(projectRoot, Path.Combine(PersistentOutputRelativePath, featureSegment));
            Directory.CreateDirectory(outputPath);
            return outputPath;
        }

        public static string GetScreenshotOutputPath()
        {
            string projectRoot = LuxBridgeSettings.GetProjectRoot();
            string screenshotPath = EnsureProjectLocalPath(projectRoot, ScreenshotOutputRelativePath);
            Directory.CreateDirectory(screenshotPath);
            return screenshotPath;
        }

        public static Dictionary<string, object> WriteArtifactMetadata(string filePath, string feature, string requestId)
        {
            string projectRoot = LuxBridgeSettings.GetProjectRoot();
            string normalizedFilePath = EnsureProjectLocalFilePath(projectRoot, filePath);

            if (!File.Exists(normalizedFilePath))
            {
                throw new FileNotFoundException($"Artifact file does not exist: {normalizedFilePath}", normalizedFilePath);
            }

            string featureSegment = ValidateFeatureSegment(feature);
            string requestSegment = ValidateMetadataToken(requestId, nameof(requestId));

            return new Dictionary<string, object>
            {
                ["filePath"] = normalizedFilePath,
                ["fileSizeBytes"] = new FileInfo(normalizedFilePath).Length,
                ["feature"] = featureSegment,
                ["requestId"] = requestSegment,
            };
        }

        static string ValidateFeatureSegment(string feature)
        {
            string trimmed = (feature ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                throw new ArgumentException("Feature name is required.", nameof(feature));
            }

            if (ContainsForbiddenSegment(trimmed) || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException($"Invalid feature name: {trimmed}", nameof(feature));
            }

            if (trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new ArgumentException($"Feature name must be a single path segment: {trimmed}", nameof(feature));
            }

            return trimmed;
        }

        static string ValidateMetadataToken(string value, string parameterName)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                throw new ArgumentException($"{parameterName} is required.", parameterName);
            }

            if (ContainsForbiddenSegment(trimmed))
            {
                throw new ArgumentException($"{parameterName} must not reference .uloop paths.", parameterName);
            }

            return trimmed;
        }

        static string EnsureProjectLocalPath(string projectRoot, string relativePath)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string normalizedTargetPath = Path.GetFullPath(Path.Combine(normalizedProjectRoot, relativePath));

            ValidateProjectLocalPath(normalizedProjectRoot, normalizedTargetPath);
            return normalizedTargetPath;
        }

        static string EnsureProjectLocalFilePath(string projectRoot, string filePath)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string normalizedTargetPath = Path.GetFullPath(Path.IsPathRooted(filePath)
                ? filePath
                : Path.Combine(normalizedProjectRoot, filePath));

            ValidateProjectLocalPath(normalizedProjectRoot, normalizedTargetPath);
            return normalizedTargetPath;
        }

        static void ValidateProjectLocalPath(string normalizedProjectRoot, string normalizedTargetPath)
        {
            if (ContainsForbiddenSegment(normalizedTargetPath))
            {
                throw new ArgumentException($"Output paths must not use .uloop: {normalizedTargetPath}");
            }

            string projectRootWithSeparator = normalizedProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!normalizedTargetPath.StartsWith(projectRootWithSeparator, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedTargetPath, normalizedProjectRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Path escapes the project root: {normalizedTargetPath}");
            }
        }

        static bool ContainsForbiddenSegment(string value)
        {
            return value.IndexOf(".uloop", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
