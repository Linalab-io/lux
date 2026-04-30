using System;

namespace Linalab.UnityCodexImage.Editor
{
    public static class CodexImageOptions
    {
        public const string DefaultSize = "1024x1024";
        public const string DefaultQuality = "auto";

        public static readonly string[] Sizes =
        {
            "1024x1024",
            "1024x1536",
            "1536x1024",
            "auto"
        };

        public static readonly string[] Qualities =
        {
            "low",
            "medium",
            "high",
            "auto"
        };

        public static bool IsValidSize(string value)
        {
            return Contains(Sizes, value);
        }

        public static bool IsValidQuality(string value)
        {
            return Contains(Qualities, value);
        }

        private static bool Contains(string[] values, string candidate)
        {
            foreach (var value in values)
            {
                if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
