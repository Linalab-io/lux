using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Linalab.UnityCodexImage.Editor
{
    internal static class CodexPromptBuilder
    {
        private static readonly Regex BracePlaceholderPattern = new Regex("\\{(?<name>[A-Za-z0-9_.-]+)\\}", RegexOptions.Compiled);
        private static readonly Regex BracketPlaceholderPattern = new Regex("\\[(?<name>[A-Za-z0-9_.-]+)\\]", RegexOptions.Compiled);

        public static string Build(CodexImageGenerationRequest request, string outputDirectory, string contextPath)
        {
            var filePrefix = "codex-image-" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            return string.Join("\n", new[]
            {
                "Generate image assets for this Unity project using image_gen.",
                "Use the Unity context JSON at: " + contextPath,
                "Prompt: " + request.prompt,
                "Size: " + request.size,
                "Quality: " + request.quality,
                "Count: " + request.count,
                "Save every generated PNG under: " + outputDirectory,
                "Use this file prefix: " + filePrefix,
                "For one image, save " + Path.Combine(outputDirectory, filePrefix + ".png"),
                "For multiple images, append -1, -2, and so on before .png.",
                "Return only the saved file paths and any concise generation notes."
            });
        }

        public static string BuildTemplate(string template, IReadOnlyDictionary<string, string> bindings)
        {
            if (template == null)
            {
                throw new System.ArgumentNullException(nameof(template));
            }

            bindings ??= new Dictionary<string, string>();
            var substituted = ReplacePlaceholders(BracePlaceholderPattern, template, bindings);
            return ReplacePlaceholders(BracketPlaceholderPattern, substituted, bindings);
        }

        public static bool HasUnresolvedPlaceholders(string prompt)
        {
            return !string.IsNullOrEmpty(prompt)
                && (BracePlaceholderPattern.IsMatch(prompt) || BracketPlaceholderPattern.IsMatch(prompt));
        }

        private static string ReplacePlaceholders(Regex pattern, string template, IReadOnlyDictionary<string, string> bindings)
        {
            return pattern.Replace(template, match =>
            {
                var name = match.Groups["name"].Value;
                return bindings.TryGetValue(name, out var value) ? value ?? string.Empty : match.Value;
            });
        }
    }
}
