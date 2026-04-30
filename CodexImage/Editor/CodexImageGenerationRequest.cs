using System;

namespace Linalab.UnityCodexImage.Editor
{
    [Serializable]
    public sealed class CodexImageGenerationRequest
    {
        public string prompt;
        public string size = CodexImageOptions.DefaultSize;
        public string quality = CodexImageOptions.DefaultQuality;
        public int count = 1;
        public string outputDirectory = "Assets/Generated/CodexImages";
    }
}
