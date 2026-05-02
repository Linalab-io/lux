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
        static UnityAiBridgeProtocolResponse RunSceneSmoke(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var objectCount = parameters.sceneSmokeObjectCount <= 0 ? 10 : parameters.sceneSmokeObjectCount;
            var scenePath = string.IsNullOrWhiteSpace(parameters.scenePath)
                ? "Assets/_Main/Scenes/GamePlay.unity"
                : parameters.scenePath;

            try
            {
                var message = LuxSceneSmoke.RunLive(objectCount, scenePath);
                var result = new LuxAutomationResult(true, true, 0, message, string.Empty, "Lux scene smoke started.");
                return CreateAutomationResponse(request.requestId, result);
            }
            catch (Exception exception)
            {
                var result = new LuxAutomationResult(true, false, -1, string.Empty, exception.Message, "Lux scene smoke failed to start.");
                return CreateAutomationResponse(request.requestId, result);
            }
        }

        static UnityAiBridgeProtocolResponse CreateSceneObjects(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var objectCount = parameters.sceneSmokeObjectCount <= 0 ? 10 : parameters.sceneSmokeObjectCount;
            var scenePath = string.IsNullOrWhiteSpace(parameters.scenePath)
                ? "Assets/_Main/Scenes/GamePlay.unity"
                : parameters.scenePath;

            try
            {
                var message = LuxSceneSmoke.CreateObjectsLive(objectCount, scenePath);
                var result = new LuxAutomationResult(true, true, 0, message, string.Empty, "Lux scene objects created.");
                return CreateAutomationResponse(request.requestId, result);
            }
            catch (Exception exception)
            {
                var result = new LuxAutomationResult(true, false, -1, string.Empty, exception.Message, "Lux scene object creation failed.");
                return CreateAutomationResponse(request.requestId, result);
            }
        }
    }
}
