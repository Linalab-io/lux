using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;
using Linalab.UnityAiBridge.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using InputMouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;

namespace Linalab.Lux.Editor
{
    public static partial class LuxAiBridgeProtocolRegistration
    {
        static UnityAiBridgeProtocolResponse CaptureScreenshot(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var captureMode = string.IsNullOrWhiteSpace(parameters.screenshotCaptureMode)
                ? "rendering"
                : parameters.screenshotCaptureMode.Trim().ToLowerInvariant();
            if (!string.Equals(captureMode, "rendering", StringComparison.Ordinal))
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    $"Unsupported screenshot capture mode: {captureMode}. Only 'rendering' is supported.");
            }

            if (parameters.screenshotElementsOnly && !parameters.screenshotAnnotateElements)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    "--elements-only requires --annotate-elements.");
            }

            var elements = parameters.screenshotAnnotateElements
                ? CollectUiToolkitElements()
                : Array.Empty<UnityAiBridgeScreenshotElement>();

            var safeRequestId = CreateSafeRequestId(request.requestId);
            string filePath = string.Empty;
            long fileSizeBytes = 0L;
            var screenshotSaved = false;

            if (!parameters.screenshotElementsOnly)
            {
                try
                {
                    filePath = CaptureRenderingScreenshot(safeRequestId, parameters.outputDirectory);
                    var metadata = LuxArtifactPaths.WriteArtifactMetadata(filePath, "screenshot", safeRequestId);
                    filePath = metadata["filePath"] as string ?? filePath;
                    fileSizeBytes = metadata.TryGetValue("fileSizeBytes", out var fileSizeValue)
                        ? Convert.ToInt64(fileSizeValue)
                        : 0L;
                    screenshotSaved = true;
                }
                catch (Exception exception)
                {
                    return UnityAiBridgeProtocol.CreateErrorResponse(
                        request.requestId,
                        UnityAiBridgeProtocol.ErrorCodeArtifactWriteFailed,
                        $"Failed to capture screenshot: {exception.Message}");
                }
            }

            var payload = new UnityAiBridgeScreenshotPayload
            {
                filePath = filePath,
                fileSizeBytes = fileSizeBytes,
                mediaType = "image/png",
                captureMode = captureMode,
                annotated = parameters.screenshotAnnotateElements,
                elementsOnly = parameters.screenshotElementsOnly,
                screenshotSaved = screenshotSaved,
                annotationCount = elements.Length,
                annotatedElements = elements
            };

            return UnityAiBridgeProtocol.CreateOkResponse(
                request.requestId,
                new UnityAiBridgeProtocolResponsePayload { screenshotResult = payload });
        }


        static string CaptureRenderingScreenshot(string safeRequestId, string requestedOutputDirectory)
        {
            var outputDirectory = ResolveScreenshotOutputDirectory(requestedOutputDirectory);
            var outputPath = Path.Combine(outputDirectory, safeRequestId + ".png");
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            EditorApplication.ExecuteMenuItem("Window/Game/Game View");
            var texture = CaptureScreenshotTextureWithRetry();
            if (texture == null)
            {
                if (TryCaptureScreenshotFile(outputPath))
                {
                    return outputPath;
                }

                texture = CaptureCameraScreenshotTexture() ?? CreateFallbackScreenshotTexture();
            }

            try
            {
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0L)
            {
                throw new IOException($"Screenshot capture did not write {outputPath}");
            }

            return outputPath;
        }

        static Texture2D CaptureScreenshotTextureWithRetry()
        {
            if (!EditorApplication.isPlaying)
            {
                return null;
            }

            for (int attempt = 0; attempt < 4; attempt++)
            {
                RepaintEditorViews();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture != null)
                {
                    return texture;
                }

                Thread.Sleep(75);
            }

            return null;
        }

        static bool TryCaptureScreenshotFile(string outputPath)
        {
            ScreenCapture.CaptureScreenshot(outputPath);
            var deadline = Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < 2000)
            {
                RepaintEditorViews();
                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0L)
                {
                    return true;
                }

                Thread.Sleep(75);
            }

            return false;
        }

        static Texture2D CaptureCameraScreenshotTexture()
        {
            var camera = Camera.main ?? Camera.allCameras.FirstOrDefault(candidate =>
                candidate != null && candidate.isActiveAndEnabled && candidate.gameObject.activeInHierarchy);
            if (camera == null)
            {
                return null;
            }

            var width = Mathf.Max(Screen.width, 1280);
            var height = Mathf.Max(Screen.height, 720);
            var renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();

                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        static Texture2D CreateFallbackScreenshotTexture()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            texture.SetPixels(new[]
            {
                Color.black,
                Color.black,
                Color.black,
                Color.black
            });
            texture.Apply();
            return texture;
        }

        static void RepaintEditorViews()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        static string ResolveScreenshotOutputDirectory(string requestedOutputDirectory)
        {
            if (string.IsNullOrWhiteSpace(requestedOutputDirectory))
            {
                return LuxArtifactPaths.GetScreenshotOutputPath();
            }

            var projectRoot = Path.GetFullPath(LuxBridgeSettings.GetProjectRoot());
            var outputDirectory = Path.GetFullPath(Path.IsPathRooted(requestedOutputDirectory)
                ? requestedOutputDirectory
                : Path.Combine(projectRoot, requestedOutputDirectory));
            var projectRootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!outputDirectory.StartsWith(projectRootWithSeparator, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(outputDirectory, projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Output directory escapes the project root: {outputDirectory}");
            }

            if (outputDirectory.IndexOf(".uloop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new ArgumentException($"Output directory must not use .uloop: {outputDirectory}");
            }

            Directory.CreateDirectory(outputDirectory);
            return outputDirectory;
        }

        static UnityAiBridgeScreenshotElement[] CollectUiToolkitElements()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                return Array.Empty<UnityAiBridgeScreenshotElement>();
            }

            var elements = new List<UnityAiBridgeScreenshotElement>();
            foreach (var root in activeScene.GetRootGameObjects())
            {
                if (root == null)
                {
                    continue;
                }

                var documents = root.GetComponentsInChildren<UIDocument>(true);
                foreach (var document in documents)
                {
                    if (document == null || document.rootVisualElement == null)
                    {
                        continue;
                    }

                    CollectVisualElement(document.rootVisualElement, document.name, string.Empty, elements);
                }
            }

            return elements.ToArray();
        }

        static void CollectVisualElement(VisualElement element, string documentName, string parentPath, List<UnityAiBridgeScreenshotElement> results)
        {
            if (element == null)
            {
                return;
            }

            var segment = string.IsNullOrEmpty(element.name) ? element.GetType().Name : element.name;
            var path = string.IsNullOrEmpty(parentPath) ? segment : parentPath + "/" + segment;
            var bounds = element.worldBound;
            results.Add(new UnityAiBridgeScreenshotElement
            {
                documentName = documentName ?? string.Empty,
                name = element.name ?? string.Empty,
                typeName = element.GetType().FullName ?? element.GetType().Name,
                path = path,
                x = bounds.x,
                y = bounds.y,
                width = bounds.width,
                height = bounds.height,
                visible = element.visible && element.resolvedStyle.display != DisplayStyle.None,
                enabled = element.enabledInHierarchy,
                pickingMode = element.pickingMode.ToString(),
                simX = bounds.center.x,
                simY = bounds.center.y
            });

            for (int i = 0; i < element.hierarchy.childCount; i++)
            {
                CollectVisualElement(element.hierarchy.ElementAt(i), documentName, path, results);
            }
        }

        static string CreateSafeRequestId(string requestId)
        {
            var raw = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId.Trim();
            return Regex.Replace(raw, "[^A-Za-z0-9_.-]", "_");
        }
    }
}
