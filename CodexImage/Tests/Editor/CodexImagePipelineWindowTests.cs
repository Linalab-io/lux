using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Linalab.UnityCodexImage.Editor.UI;
using Linalab.UnityCodexImage.Editor.Pipeline;

namespace Linalab.UnityCodexImage.Editor.Tests
{
    public class CodexImagePipelineWindowTests
    {
        private CodexImagePipelineWindow window;

        [SetUp]
        public void Setup()
        {
            window = EditorWindow.GetWindow<CodexImagePipelineWindow>("Codex Image Pipeline", false);
        }

        [TearDown]
        public void Teardown()
        {
            if (window != null)
            {
                window.Close();
            }
        }

        [Test]
        public void Window_CanBeOpened()
        {
            Assert.IsNotNull(window);
            Assert.AreEqual("Codex Image Pipeline", window.titleContent.text);
        }

        [UnityTest]
        public IEnumerator Window_RunStubPipeline_CompletesSuccessfully()
        {
            var method = typeof(CodexImagePipelineWindow).GetMethod("RunStubPipeline", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method, "RunStubPipeline method not found.");

            method.Invoke(window, null);

            var isRunningField = typeof(CodexImagePipelineWindow).GetField("isRunning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            int timeout = 100;
            while ((bool)isRunningField.GetValue(window) && timeout > 0)
            {
                yield return null;
                timeout--;
            }

            Assert.IsFalse((bool)isRunningField.GetValue(window), "Pipeline did not finish in time.");

            var lastResultField = typeof(CodexImagePipelineWindow).GetField("lastResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var lastResult = lastResultField.GetValue(window) as Linalab.UnityCodexImage.Editor.Pipeline.PipelineExecutionResult;

            Assert.IsNotNull(lastResult);
            Assert.IsTrue(lastResult.succeeded);
            Assert.AreEqual(6, lastResult.executedNodeIds.Length);

            var manifestArtifact = lastResult.artifacts?.FirstOrDefault(a => a.kind == CodexImagePipelineArtifactKinds.GeneratedAssetManifest);
            Assert.IsNotNull(manifestArtifact, "Manifest artifact should be generated.");
            Assert.IsFalse(string.IsNullOrEmpty(manifestArtifact.path), "Manifest path should not be empty.");

            var stubArtifacts = lastResult.artifacts?.Where(a => a.kind == "stub").ToList();
            Assert.IsNotNull(stubArtifacts);
            Assert.Greater(stubArtifacts.Count, 0, "Preview entries (stub artifacts) should be generated.");
        }
    }
}
