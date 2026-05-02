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
        static UnityAiBridgeProtocolResponse GetConsoleLogs(UnityAiBridgeProtocolRequest request)
        {
            var consoleCounts = LuxUnityContext.GetConsoleCountsSnapshot();
            var recentLogs = LuxUnityContext.GetRecentLogsSnapshot();

            return UnityAiBridgeProtocol.CreateOkResponse(
                request.requestId,
                new UnityAiBridgeProtocolResponsePayload
                {
                    consoleLogs = new UnityAiBridgeConsoleLogsPayload
                    {
                        totalCount = consoleCounts.Errors + consoleCounts.Warnings + consoleCounts.Logs,
                        displayedCount = recentLogs.Length,
                        consoleLogs = recentLogs.Select(log => new UnityAiBridgeConsoleLogEntry
                        {
                            level = log.Type,
                            message = log.Message,
                            stackTrace = log.StackTrace,
                            timestampUtc = log.TimestampUtc
                        }).ToArray()
                    }
                });
        }

        static UnityAiBridgeProtocolResponse ClearConsole(UnityAiBridgeProtocolRequest request)
        {
            var beforeCounts = LuxUnityContext.GetConsoleCountsSnapshot();
            if (!LuxUnityContext.ClearConsole())
            {
                throw new InvalidOperationException("Lux console clear API is unavailable.");
            }

            var afterCounts = LuxUnityContext.GetConsoleCountsSnapshot();
            return UnityAiBridgeProtocol.CreateOkResponse(
                request.requestId,
                new UnityAiBridgeProtocolResponsePayload
                {
                    consoleClearResult = new UnityAiBridgeConsoleClearPayload
                    {
                        beforeCount = beforeCounts.Errors + beforeCounts.Warnings + beforeCounts.Logs,
                        afterCount = afterCounts.Errors + afterCounts.Warnings + afterCounts.Logs
                    }
                });
        }

        static UnityAiBridgeProtocolResponse FocusWindow(UnityAiBridgeProtocolRequest request)
        {
            var focused = EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
            if (!focused)
            {
                focused = EditorApplication.ExecuteMenuItem("Window/General/Game");
            }

            if (!focused)
            {
                EditorUtility.FocusProjectWindow();
                focused = true;
            }

            return UnityAiBridgeProtocol.CreateOkResponse(
                request.requestId,
                new UnityAiBridgeProtocolResponsePayload
                {
                    focusWindowResult = new UnityAiBridgeFocusWindowPayload
                    {
                        focused = focused
                    }
                });
        }
    }
}
