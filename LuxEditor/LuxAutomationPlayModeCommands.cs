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
        static UnityAiBridgeProtocolResponse ControlPlayMode(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var action = string.IsNullOrWhiteSpace(parameters.playModeAction)
                ? "status"
                : parameters.playModeAction.Trim().ToLowerInvariant();
            var transitionRequested = false;

            switch (action)
            {
                case "play":
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        EditorApplication.EnterPlaymode();
                        transitionRequested = true;
                    }
                    break;

                case "stop":
                    if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        LuxSceneSmoke.ReleaseSyntheticInputState();
                        EditorApplication.ExitPlaymode();
                        transitionRequested = true;
                    }
                    break;

                case "pause":
                    if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                    {
                        EditorApplication.isPaused = true;
                    }
                    break;

                case "resume":
                    if (EditorApplication.isPlaying && EditorApplication.isPaused)
                    {
                        EditorApplication.isPaused = false;
                    }
                    break;

                case "status":
                    break;

                default:
                    return UnityAiBridgeProtocol.CreateErrorResponse(
                        request.requestId,
                        UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                        $"Unsupported play mode action: {action}");
            }

            return UnityAiBridgeProtocol.CreateOkResponse(
                request.requestId,
                new UnityAiBridgeProtocolResponsePayload
                {
                    playModeState = new UnityAiBridgePlayModeStatePayload
                    {
                        action = action,
                        isPlaying = EditorApplication.isPlaying,
                        isPaused = EditorApplication.isPaused,
                        transitionRequested = transitionRequested || (!EditorApplication.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode)
                    }
                });
        }
    }
}
