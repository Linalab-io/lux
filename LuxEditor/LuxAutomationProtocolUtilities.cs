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
        static UnityAiBridgeProtocolResponse CreateAutomationResponse(string requestId, LuxAutomationResult result)
        {
            return UnityAiBridgeProtocol.CreateOkResponse(
                requestId,
                new UnityAiBridgeProtocolResponsePayload
                {
                    luxAutomationResult = new UnityAiBridgeLuxAutomationResultPayload
                    {
                        allowed = result.Allowed,
                        success = result.Success,
                        exitCode = result.ExitCode,
                        output = result.Output,
                        error = result.Error,
                        message = result.Message
                    }
                });
        }

        static string GetWorkingDirectory(string requestedWorkingDirectory)
        {
            return string.IsNullOrWhiteSpace(requestedWorkingDirectory) ? GetProjectRoot() : requestedWorkingDirectory;
        }

        static string GetActor(string requestedActor)
        {
            return string.IsNullOrWhiteSpace(requestedActor) ? LuxAutomationGateway.DefaultActor : requestedActor;
        }

        static string GetProjectRoot()
        {
            var assetsDirectory = new DirectoryInfo(Application.dataPath);
            return assetsDirectory.Parent == null ? Directory.GetCurrentDirectory() : assetsDirectory.Parent.FullName;
        }
    }
}
