using UnityEditor;
using UnityEngine;

namespace Linalab.UnityCodexImage.Editor
{
    public sealed class CodexImageWindow : EditorWindow
    {
        private readonly CodexImageGenerationRequest request = new CodexImageGenerationRequest();
        private string lastOutput;
        private string lastError;

        [MenuItem("Tools/Linalab/Codex Image")]
        private static void Open()
        {
            GetWindow<CodexImageWindow>("Codex Image");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Codex Image", EditorStyles.boldLabel);
            request.prompt = EditorGUILayout.TextField("Prompt", request.prompt);
            request.size = DrawPopup("Size", request.size, CodexImageOptions.Sizes);
            request.quality = DrawPopup("Quality", request.quality, CodexImageOptions.Qualities);
            request.count = EditorGUILayout.IntSlider("Count", request.count, 1, 10);
            request.outputDirectory = EditorGUILayout.TextField("Output Directory", request.outputDirectory);

            if (GUILayout.Button("Generate"))
            {
                Generate();
            }

            DrawResult("Output", lastOutput);
            DrawResult("Error", lastError);
        }

        private static string DrawPopup(string label, string current, string[] options)
        {
            var index = System.Array.IndexOf(options, current);
            if (index < 0)
            {
                index = 0;
            }

            return options[EditorGUILayout.Popup(label, index, options)];
        }

        private void Generate()
        {
            try
            {
                var result = CodexImageGenerator.Generate(request);
                lastOutput = $"ExitCode: {result.ExitCode}\nContext: {result.ContextPath}\n{result.Output}";
                lastError = result.Error;
            }
            catch (System.Exception exception)
            {
                lastOutput = string.Empty;
                lastError = exception.Message;
            }
        }

        private static void DrawResult(string label, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(value, MessageType.None);
        }
    }
}
