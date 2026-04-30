namespace Linalab.UnityCodexImage.Editor
{
    public readonly struct CodexImageGenerationResult
    {
        public CodexImageGenerationResult(int exitCode, string output, string error, string contextPath)
        {
            ExitCode = exitCode;
            Output = output;
            Error = error;
            ContextPath = contextPath;
        }

        public int ExitCode { get; }
        public string Output { get; }
        public string Error { get; }
        public string ContextPath { get; }
        public bool Succeeded => ExitCode == 0;
    }
}
