using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Text.RegularExpressions;
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
    public readonly struct LuxAutomationResult
    {
        public LuxAutomationResult(bool allowed, bool success, int exitCode, string output, string error, string message)
        {
            Allowed = allowed;
            Success = success;
            ExitCode = exitCode;
            Output = output ?? string.Empty;
            Error = error ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Allowed { get; }
        public bool Success { get; }
        public int ExitCode { get; }
        public string Output { get; }
        public string Error { get; }
        public string Message { get; }
    }

    public sealed class LuxAutomationGateway
    {
        public const string DefaultActor = "ai";

        readonly LuxAutomationPolicy _policy;
        readonly LuxAutomationAuditLog _auditLog;

        public LuxAutomationGateway(LuxAutomationPolicy policy = null, LuxAutomationAuditLog auditLog = null)
        {
            _policy = policy ?? new LuxAutomationPolicy();
            _auditLog = auditLog ?? new LuxAutomationAuditLog();
        }

        public LuxAutomationPolicy Policy => _policy;
        public LuxAutomationAuditLog AuditLog => _auditLog;

        public LuxAutomationResult ExecuteShellCommand(string command, string workingDirectory, string actor = DefaultActor, bool approvalGranted = false)
        {
            return ExecuteProcess("shell", GetShellExecutable(), GetShellArguments(command ?? string.Empty), command, workingDirectory, actor, approvalGranted);
        }

        static string GetShellExecutable()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return "cmd.exe";
            }

            var zsh = "/bin/zsh";
            if (File.Exists(zsh)) return zsh;

            var bash = "/bin/bash";
            if (File.Exists(bash)) return bash;

            return "/bin/sh";
        }

        static string[] GetShellArguments(string command)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return new[] { "/c", command };
            }

            return new[] { "-lc", command };
        }
        public LuxAutomationResult ExecuteGitCommand(string arguments, string workingDirectory, string actor = DefaultActor, bool approvalGranted = false)
        {
            var command = string.IsNullOrWhiteSpace(arguments) ? "git" : $"git {arguments}";
            return ExecuteProcess("git", "git", SplitArguments(arguments), command, workingDirectory, actor, approvalGranted);
        }

        LuxAutomationResult ExecuteProcess(
            string commandKind,
            string executable,
            string[] arguments,
            string commandForPolicy,
            string workingDirectory,
            string actor,
            bool approvalGranted)
        {
            var decision = _policy.Evaluate(commandForPolicy);
            if (decision.Kind == LuxAutomationDecisionKind.Block)
            {
                return Deny(commandKind, commandForPolicy, workingDirectory, actor, decision.Reason);
            }

            if (decision.Kind == LuxAutomationDecisionKind.RequireApproval && !approvalGranted)
            {
                return Deny(commandKind, commandForPolicy, workingDirectory, actor, decision.Reason);
            }

            var normalizedWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Directory.GetCurrentDirectory()
                : workingDirectory;

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = normalizedWorkingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                foreach (var argument in arguments ?? Array.Empty<string>())
                {
                    process.StartInfo.ArgumentList.Add(argument ?? string.Empty);
                }

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                var success = process.ExitCode == 0;
                var message = success ? "Command completed." : "Command failed.";
                _auditLog.Record(actor, commandKind, commandForPolicy, normalizedWorkingDirectory, true, success, message);
                return new LuxAutomationResult(true, success, process.ExitCode, output, error, message);
            }
            catch (Exception exception)
            {
                _auditLog.Record(actor, commandKind, commandForPolicy, normalizedWorkingDirectory, true, false, exception.Message);
                return new LuxAutomationResult(true, false, -1, string.Empty, string.Empty, exception.Message);
            }
        }

        LuxAutomationResult Deny(string commandKind, string command, string targetContext, string actor, string reason)
        {
            _auditLog.Record(actor, commandKind, command, targetContext, false, false, reason);
            return new LuxAutomationResult(false, false, -1, string.Empty, string.Empty, reason);
        }

        static string[] SplitArguments(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return Array.Empty<string>();
            }

            return arguments.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    public static class LuxAiBridgeProtocolRegistration
    {
        public const string CommandGetLuxContext = "get_lux_context";
        public const string CommandExecuteLuxShell = "execute_lux_shell";
        public const string CommandExecuteLuxGit = "execute_lux_git";
        public const string CommandRunLuxSceneSmoke = "run_lux_scene_smoke";
        public const string CommandCreateLuxSceneObjects = "create_lux_scene_objects";
        public const string CommandFocusLuxWindow = "focus_lux_window";
        public const string CommandGetLuxConsoleLogs = "get_lux_console_logs";
        public const string CommandClearLuxConsole = "clear_lux_console";
        public const string CommandFindLuxGameObjects = "find_lux_game_objects";
        public const string CommandGetLuxHierarchy = "get_lux_hierarchy";
        public const string CommandControlLuxPlayMode = "control_lux_play_mode";
        public const string CommandCaptureLuxScreenshot = "capture_lux_screenshot";
        public const string CommandSimulateLuxMouseUi = "simulate_lux_mouse_ui";
        public const string CommandSimulateLuxKeyboard = "simulate_lux_keyboard";
        public const string CommandSimulateLuxMouseInput = "simulate_lux_mouse_input";
        public const string CommandRecordLuxInput = "record_lux_input";
        public const string CommandReplayLuxInput = "replay_lux_input";
        public const string CommandExecuteLuxDynamicCode = "execute_lux_dynamic_code";

        static readonly LuxAutomationGateway AutomationGateway = new LuxAutomationGateway();
        static PointerEventData ActiveMouseUiDragEvent;
        static GameObject ActiveMouseUiDragTarget;
        static bool ActiveMouseUiDragStarted;

        [InitializeOnLoadMethod]
        public static void RegisterCommands()
        {
            RebuildCommandRegistry("InitializeOnLoad");
        }

        [MenuItem("Tools/Linalab/Lux/AI Bridge/Rebuild Command Registry")]
        public static void RebuildCommandRegistryMenu()
        {
            RebuildCommandRegistry("menu rebuild");
        }

        public static void RebuildCommandRegistry(string reason)
        {
            RegisterOrReplace(CommandGetLuxContext, CreateContextResponse);
            RegisterOrReplace(CommandExecuteLuxShell, ExecuteShellCommand);
            RegisterOrReplace(CommandExecuteLuxGit, ExecuteGitCommand);
            RegisterOrReplace(CommandRunLuxSceneSmoke, RunSceneSmoke);
            RegisterOrReplace(CommandCreateLuxSceneObjects, CreateSceneObjects);
            RegisterOrReplace(CommandFocusLuxWindow, FocusWindow);
            RegisterOrReplace(CommandGetLuxConsoleLogs, GetConsoleLogs);
            RegisterOrReplace(CommandClearLuxConsole, ClearConsole);
            RegisterOrReplace(CommandFindLuxGameObjects, FindGameObjects);
            RegisterOrReplace(CommandGetLuxHierarchy, GetHierarchy);
            RegisterOrReplace(CommandControlLuxPlayMode, ControlPlayMode);
            RegisterOrReplace(CommandCaptureLuxScreenshot, CaptureScreenshot);
            RegisterOrReplace(CommandSimulateLuxMouseUi, SimulateMouseUi);
            RegisterOrReplace(CommandSimulateLuxKeyboard, SimulateKeyboard);
            RegisterOrReplace(CommandSimulateLuxMouseInput, SimulateMouseInput);
            RegisterOrReplace(CommandRecordLuxInput, RecordInput);
            RegisterOrReplace(CommandReplayLuxInput, ReplayInput);
            RegisterOrReplace(CommandExecuteLuxDynamicCode, ExecuteDynamicCode);
            UnityAiBridgeProtocol.MarkRegistryReady(reason);
            UnityAiBridgeProtocol.LogRegisteredCommands(reason);
        }

        static void RegisterOrReplace(string command, Func<UnityAiBridgeProtocolRequest, UnityAiBridgeProtocolResponse> handler)
        {
            UnityAiBridgeProtocol.UnregisterCommand(command);
            UnityAiBridgeProtocol.RegisterCommand(command, handler);
            UnityEngine.Debug.Log($"Lux Unity AI Bridge registered command: {command}");
        }

        static UnityAiBridgeProtocolResponse CreateContextResponse(UnityAiBridgeProtocolRequest request)
        {
            return UnityAiBridgeProtocol.CreateOkResponse(
                request.requestId,
                new UnityAiBridgeProtocolResponsePayload
                {
                    luxContext = new UnityAiBridgeLuxContextPayload
                    {
                        packageName = "com.linalab.lux",
                        protocolSurface = "ai-bridge-tcp",
                        projectPath = GetProjectRoot(),
                        unityVersion = Application.unityVersion ?? string.Empty,
                        platform = Application.platform.ToString(),
                        remotePhase = LuxRemoteGatewayPlan.Phase,
                        videoTransport = LuxRemoteGatewayPlan.VideoTransport,
                        signalingTransport = LuxRemoteGatewayPlan.SignalingTransport,
                        controlTransport = LuxRemoteGatewayPlan.ControlTransport,
                        permissionModel = LuxRemoteGatewayPlan.PermissionModel,
                        includesIosClientImplementation = LuxRemoteGatewayPlan.IncludesIosClientImplementation,
                        automationBlockedTokens = AutomationGateway.Policy.BlockedTokens.ToArray(),
                        automationApprovalTokens = AutomationGateway.Policy.ApprovalTokens.ToArray(),
                        auditEntryCount = AutomationGateway.AuditLog.Entries.Count
                    }
                });
        }

        static UnityAiBridgeProtocolResponse ExecuteShellCommand(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var result = AutomationGateway.ExecuteShellCommand(
                parameters.commandText,
                GetWorkingDirectory(parameters.workingDirectory),
                GetActor(parameters.actor),
                parameters.approvalGranted);

            return CreateAutomationResponse(request.requestId, result);
        }

        static UnityAiBridgeProtocolResponse ExecuteGitCommand(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var arguments = string.IsNullOrWhiteSpace(parameters.gitArguments) ? parameters.commandText : parameters.gitArguments;
            var result = AutomationGateway.ExecuteGitCommand(
                arguments,
                GetWorkingDirectory(parameters.workingDirectory),
                GetActor(parameters.actor),
                parameters.approvalGranted);

            return CreateAutomationResponse(request.requestId, result);
        }

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

        static UnityAiBridgeProtocolResponse FindGameObjects(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var searchMode = string.IsNullOrWhiteSpace(parameters.findGameObjectsSearchMode)
                ? "query"
                : parameters.findGameObjectsSearchMode.Trim().ToLowerInvariant();
            var inlineLimit = parameters.findGameObjectsInlineLimit <= 0 ? 50 : parameters.findGameObjectsInlineLimit;
            var activeState = string.IsNullOrWhiteSpace(parameters.findGameObjectsActiveState)
                ? "any"
                : parameters.findGameObjectsActiveState.Trim().ToLowerInvariant();

            try
            {
                var roots = CollectRootGameObjects(searchMode);
                var matches = new List<GameObject>();
                Regex nameRegex = null;
                if (!string.IsNullOrEmpty(parameters.findGameObjectsRegex))
                {
                    nameRegex = new Regex(parameters.findGameObjectsRegex);
                }

                foreach (var root in roots)
                {
                    if (root == null)
                    {
                        continue;
                    }

                    if (searchMode == "selected")
                    {
                        if (MatchesFilters(root, parameters, nameRegex, activeState))
                        {
                            matches.Add(root);
                        }
                        continue;
                    }

                    CollectMatches(root.transform, parameters, nameRegex, activeState, matches);
                }

                var totalMatchCount = matches.Count;
                var returnedCount = Math.Min(totalMatchCount, inlineLimit);
                var truncated = totalMatchCount > inlineLimit;
                var entries = new UnityAiBridgeFindGameObjectsEntry[returnedCount];
                for (int i = 0; i < returnedCount; i++)
                {
                    entries[i] = BuildEntry(matches[i]);
                }

                string outputFilePath = string.Empty;
                long fileSizeBytes = 0L;
                if (truncated)
                {
                    var allEntries = new UnityAiBridgeFindGameObjectsEntry[totalMatchCount];
                    for (int i = 0; i < totalMatchCount; i++)
                    {
                        allEntries[i] = BuildEntry(matches[i]);
                    }
                    outputFilePath = WriteFindGameObjectsOutput(request.requestId, allEntries, out fileSizeBytes);
                }

                var payload = new UnityAiBridgeFindGameObjectsPayload
                {
                    searchMode = searchMode,
                    totalMatchCount = totalMatchCount,
                    returnedCount = returnedCount,
                    truncated = truncated,
                    outputFilePath = outputFilePath,
                    fileSizeBytes = fileSizeBytes,
                    gameObjects = entries
                };

                return UnityAiBridgeProtocol.CreateOkResponse(
                    request.requestId,
                    new UnityAiBridgeProtocolResponsePayload { findGameObjectsResult = payload });
            }
            catch (Exception exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    exception.Message);
            }
        }
        static UnityAiBridgeProtocolResponse GetHierarchy(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var filters = CreateHierarchyFilters(parameters);

            try
            {
                var activeScene = EditorSceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    return UnityAiBridgeProtocol.CreateErrorResponse(
                        request.requestId,
                        UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                        "No active loaded scene is available for hierarchy export.");
                }

                var roots = CollectHierarchyRoots(activeScene, filters);

                int nodeCount = 0;
                var nodeList = new List<UnityAiBridgeHierarchyNode>();
                foreach (var root in roots)
                {
                    if (root == null)
                    {
                        continue;
                    }

                    nodeList.Add(BuildHierarchyNode(root, ref nodeCount));
                }

                var safeRequestId = string.IsNullOrWhiteSpace(request.requestId)
                    ? Guid.NewGuid().ToString("N")
                    : request.requestId;
                var outputDirectory = LuxArtifactPaths.GetPersistentOutputPath("hierarchy");
                var outputPath = Path.Combine(outputDirectory, safeRequestId + ".json");

                var activeScenePayload = new UnityAiBridgeHierarchyActiveScene
                {
                    name = activeScene.name ?? string.Empty,
                    path = activeScene.path ?? string.Empty
                };
                var export = new UnityAiBridgeHierarchyExport
                {
                    schemaVersion = UnityAiBridgeProtocol.SchemaVersion,
                    requestId = safeRequestId,
                    rootCount = nodeList.Count,
                    nodeCount = nodeCount,
                    activeScene = activeScenePayload,
                    filters = filters,
                    roots = nodeList.ToArray()
                };

                try
                {
                    File.WriteAllText(outputPath, JsonUtility.ToJson(export, true));
                }
                catch (Exception exception)
                {
                    return UnityAiBridgeProtocol.CreateErrorResponse(
                        request.requestId,
                        UnityAiBridgeProtocol.ErrorCodeArtifactWriteFailed,
                        $"Failed to write hierarchy export: {exception.Message}");
                }

                var metadata = LuxArtifactPaths.WriteArtifactMetadata(outputPath, "hierarchy", safeRequestId);

                var payload = new UnityAiBridgeGetHierarchyPayload
                {
                    filePath = metadata["filePath"] as string ?? outputPath,
                    fileSizeBytes = metadata.TryGetValue("fileSizeBytes", out var fileSizeValue)
                        ? Convert.ToInt64(fileSizeValue)
                        : 0L,
                    rootCount = nodeList.Count,
                    nodeCount = nodeCount,
                    activeScene = activeScenePayload,
                    filters = filters
                };

                return UnityAiBridgeProtocol.CreateOkResponse(
                    request.requestId,
                    new UnityAiBridgeProtocolResponsePayload { getHierarchyResult = payload });
            }
            catch (Exception exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    exception.Message);
            }
        }

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
                    filePath = CaptureRenderingScreenshot(safeRequestId);
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

        static UnityAiBridgeProtocolResponse RecordInput(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            try
            {
                var payload = LuxInputRecording.Record(parameters.inputAction, request.requestId);
                return UnityAiBridgeProtocol.CreateOkResponse(
                    request.requestId,
                    new UnityAiBridgeProtocolResponsePayload { inputRecordResult = payload });
            }
            catch (LuxInputPlayModeRequiredException exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(request.requestId, UnityAiBridgeProtocol.ErrorCodePlayModeRequired, exception.Message);
            }
            catch (LuxInputSingleFlightException exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(request.requestId, UnityAiBridgeProtocol.ErrorCodeSingleFlightBusy, exception.Message);
            }
            catch (IOException exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(request.requestId, UnityAiBridgeProtocol.ErrorCodeArtifactWriteFailed, exception.Message);
            }
            catch (Exception exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(request.requestId, UnityAiBridgeProtocol.ErrorCodeInvalidParams, exception.Message);
            }
        }

        static UnityAiBridgeProtocolResponse ReplayInput(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            try
            {
                var payload = LuxInputRecording.Replay(parameters.inputAction, parameters.inputFilePath);
                return UnityAiBridgeProtocol.CreateOkResponse(
                    request.requestId,
                    new UnityAiBridgeProtocolResponsePayload { inputReplayResult = payload });
            }
            catch (LuxInputPlayModeRequiredException exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(request.requestId, UnityAiBridgeProtocol.ErrorCodePlayModeRequired, exception.Message);
            }
            catch (LuxInputSingleFlightException exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(request.requestId, UnityAiBridgeProtocol.ErrorCodeSingleFlightBusy, exception.Message);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(request.requestId, UnityAiBridgeProtocol.ErrorCodeInvalidParams, exception.Message);
            }
            catch (Exception exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(request.requestId, UnityAiBridgeProtocol.ErrorCodeInvalidParams, exception.Message);
            }
        }

        static UnityAiBridgeProtocolResponse ExecuteDynamicCode(UnityAiBridgeProtocolRequest request)
        {
            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            if (string.IsNullOrWhiteSpace(parameters.dynamicCode))
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    "Dynamic code is required.");
            }

            try
            {
                var result = LuxDynamicCodeExecution.Execute(parameters.dynamicCode);
                return UnityAiBridgeProtocol.CreateOkResponse(
                    request.requestId,
                    new UnityAiBridgeProtocolResponsePayload { dynamicCodeResult = result });
            }
            catch (LuxDynamicCodePolicyViolationException exception)
            {
                var blockedToken = string.IsNullOrEmpty(exception.BlockedToken) ? "unknown" : exception.BlockedToken;
                var message = exception.Message.IndexOf(blockedToken, StringComparison.Ordinal) >= 0
                    ? exception.Message
                    : $"{exception.Message} Blocked token: {blockedToken}";
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodePolicyViolation,
                    message);
            }
            catch (LuxDynamicCodeSingleFlightException exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeSingleFlightBusy,
                    exception.Message);
            }
            catch (Exception exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    exception.Message);
            }
        }

        static UnityAiBridgeProtocolResponse SimulateKeyboard(UnityAiBridgeProtocolRequest request)
        {
            if (!EditorApplication.isPlaying)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodePlayModeRequired,
                    "Keyboard input simulation requires PlayMode.");
            }

            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var action = NormalizeInputAction(parameters.inputAction, "press");
            var keyName = parameters.inputKey ?? string.Empty;
            if (!TryParseKey(keyName, out var key))
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    $"Unsupported keyboard key: {keyName}");
            }

            try
            {
                var result = LuxInputSimulationState.SimulateKeyboard(action, key, parameters.inputDurationMs);
                return UnityAiBridgeProtocol.CreateOkResponse(
                    request.requestId,
                    new UnityAiBridgeProtocolResponsePayload { inputSimulationResult = result });
            }
            catch (Exception exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    exception.Message);
            }
        }

        static UnityAiBridgeProtocolResponse SimulateMouseInput(UnityAiBridgeProtocolRequest request)
        {
            if (!EditorApplication.isPlaying)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodePlayModeRequired,
                    "Mouse input simulation requires PlayMode.");
            }

            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var action = NormalizeInputAction(parameters.inputAction, "click");
            try
            {
                var result = LuxInputSimulationState.SimulateMouse(
                    action,
                    parameters.inputButton,
                    parameters.inputDeltaX,
                    parameters.inputDeltaY,
                    parameters.inputScrollX,
                    parameters.inputScrollY,
                    parameters.inputDurationMs,
                    parameters.inputSteps);
                return UnityAiBridgeProtocol.CreateOkResponse(
                    request.requestId,
                    new UnityAiBridgeProtocolResponsePayload { inputSimulationResult = result });
            }
            catch (Exception exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    exception.Message);
            }
        }

        static UnityAiBridgeProtocolResponse SimulateMouseUi(UnityAiBridgeProtocolRequest request)
        {
            if (!EditorApplication.isPlaying)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodePlayModeRequired,
                    "UI mouse simulation requires PlayMode.");
            }

            var parameters = request.@params ?? new UnityAiBridgeProtocolRequestParameters();
            var action = NormalizeInputAction(parameters.mouseUiAction, "click");
            var position = new Vector2(parameters.mouseUiX, parameters.mouseUiY);
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    "No active UnityEngine.EventSystems.EventSystem is available.");
            }

            if (!HasInputSystemUiRaycaster())
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    "No active InputSystemUIRaycaster is available for UI raycasting.");
            }

            var raycastResults = RaycastUi(eventSystem, position, out var pointerEventData);
            var target = raycastResults.Count == 0 ? null : raycastResults[0].gameObject;
            if ((action == "click" || action == "long-press" || action == "drag-start") && target == null)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    $"UI raycast did not hit an EventSystem target at ({position.x}, {position.y}).");
            }

            try
            {
                ExecuteMouseUiAction(action, pointerEventData, target, raycastResults, parameters.mouseUiDurationMs);
            }
            catch (Exception exception)
            {
                return UnityAiBridgeProtocol.CreateErrorResponse(
                    request.requestId,
                    UnityAiBridgeProtocol.ErrorCodeInvalidParams,
                    exception.Message);
            }

            var payload = new UnityAiBridgeMouseUiPayload
            {
                action = action,
                x = position.x,
                y = position.y,
                success = true,
                targetName = target == null ? string.Empty : target.name,
                targetPath = target == null ? string.Empty : BuildHierarchyPath(target),
                raycastCount = raycastResults.Count,
                dragActive = ActiveMouseUiDragEvent != null
            };

            return UnityAiBridgeProtocol.CreateOkResponse(
                request.requestId,
                new UnityAiBridgeProtocolResponsePayload { mouseUiResult = payload });
        }

        static void ExecuteMouseUiAction(string action, PointerEventData pointerEventData, GameObject target, List<RaycastResult> raycastResults, int durationMs)
        {
            switch (action)
            {
                case "click":
                    ExecutePointerClick(pointerEventData, target);
                    return;

                case "long-press":
                    ExecutePointerDown(pointerEventData, target);
                    Thread.Sleep(Math.Max(durationMs, 500));
                    ExecutePointerUp(pointerEventData, target);
                    return;

                case "drag-start":
                    ActiveMouseUiDragEvent = pointerEventData;
                    ActiveMouseUiDragTarget = target;
                    ActiveMouseUiDragStarted = false;
                    ExecutePointerDown(ActiveMouseUiDragEvent, ActiveMouseUiDragTarget);
                    ExecuteEvents.Execute(ActiveMouseUiDragTarget, ActiveMouseUiDragEvent, ExecuteEvents.initializePotentialDrag);
                    return;

                case "drag-move":
                    EnsureActiveMouseUiDrag(action);
                    UpdateActiveMouseUiDrag(pointerEventData.position, raycastResults);
                    if (!ActiveMouseUiDragStarted)
                    {
                        ExecuteEvents.Execute(ActiveMouseUiDragTarget, ActiveMouseUiDragEvent, ExecuteEvents.beginDragHandler);
                        ActiveMouseUiDragStarted = true;
                    }
                    ExecuteEvents.Execute(ActiveMouseUiDragTarget, ActiveMouseUiDragEvent, ExecuteEvents.dragHandler);
                    return;

                case "drag-end":
                    EnsureActiveMouseUiDrag(action);
                    UpdateActiveMouseUiDrag(pointerEventData.position, raycastResults);
                    if (target != null)
                    {
                        ExecuteEvents.Execute(target, ActiveMouseUiDragEvent, ExecuteEvents.dropHandler);
                    }
                    ExecutePointerUp(ActiveMouseUiDragEvent, ActiveMouseUiDragTarget);
                    ExecuteEvents.Execute(ActiveMouseUiDragTarget, ActiveMouseUiDragEvent, ExecuteEvents.endDragHandler);
                    ActiveMouseUiDragEvent = null;
                    ActiveMouseUiDragTarget = null;
                    ActiveMouseUiDragStarted = false;
                    return;

                default:
                    throw new InvalidOperationException($"Unsupported mouse UI action: {action}");
            }
        }

        static void EnsureActiveMouseUiDrag(string action)
        {
            if (ActiveMouseUiDragEvent == null || ActiveMouseUiDragTarget == null)
            {
                throw new InvalidOperationException($"{action} requires an active drag-start.");
            }
        }

        static void UpdateActiveMouseUiDrag(Vector2 position, List<RaycastResult> raycastResults)
        {
            ActiveMouseUiDragEvent.position = position;
            ActiveMouseUiDragEvent.pointerCurrentRaycast = raycastResults.Count == 0 ? new RaycastResult() : raycastResults[0];
        }

        static List<RaycastResult> RaycastUi(EventSystem eventSystem, Vector2 position, out PointerEventData pointerEventData)
        {
            pointerEventData = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                position = position,
                pressPosition = position
            };

            var raycastResults = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerEventData, raycastResults);
            if (raycastResults.Count > 0)
            {
                pointerEventData.pointerCurrentRaycast = raycastResults[0];
            }

            return raycastResults;
        }

        static void ExecutePointerClick(PointerEventData eventData, GameObject target)
        {
            ExecutePointerDown(eventData, target);
            ExecutePointerUp(eventData, target);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerClickHandler);
        }

        static void ExecutePointerDown(PointerEventData eventData, GameObject target)
        {
            eventData.pointerPressRaycast = eventData.pointerCurrentRaycast;
            eventData.pointerPress = target;
            eventData.rawPointerPress = target;
            eventData.eligibleForClick = true;
            eventData.dragging = false;
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerDownHandler);
        }

        static void ExecutePointerUp(PointerEventData eventData, GameObject target)
        {
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerUpHandler);
            eventData.eligibleForClick = false;
            eventData.pointerPress = null;
            eventData.rawPointerPress = null;
        }

        static bool HasInputSystemUiRaycaster()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                return false;
            }

            foreach (var root in activeScene.GetRootGameObjects())
            {
                if (root == null)
                {
                    continue;
                }

                var components = root.GetComponentsInChildren<Component>(true);
                foreach (var component in components)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    var componentType = component.GetType();
                    if ((componentType.Name == "InputSystemUIRaycaster" ||
                         componentType.FullName == "UnityEngine.InputSystem.UI.InputSystemUIRaycaster") &&
                        component is Behaviour behaviour &&
                        behaviour.isActiveAndEnabled)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static string NormalizeInputAction(string action, string defaultAction)
        {
            return string.IsNullOrWhiteSpace(action) ? defaultAction : action.Trim().ToLowerInvariant();
        }

        static bool TryParseKey(string keyName, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrWhiteSpace(keyName))
            {
                return false;
            }

            var normalized = keyName.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
            return Enum.TryParse(normalized, true, out key) && key != Key.None;
        }

        static string CaptureRenderingScreenshot(string safeRequestId)
        {
            var outputDirectory = LuxArtifactPaths.GetScreenshotOutputPath();
            var outputPath = Path.Combine(outputDirectory, safeRequestId + ".png");
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            EditorApplication.ExecuteMenuItem("Window/Game/Game View");
            ScreenCapture.CaptureScreenshot(outputPath);

            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0L)
                {
                    return outputPath;
                }

                Thread.Sleep(50);
            }

            throw new IOException($"ScreenCapture.CaptureScreenshot did not write {outputPath}");
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


        static List<GameObject> CollectRootGameObjects(string searchMode)
        {
            var roots = new List<GameObject>();
            if (searchMode == "selected")
            {
                var selected = Selection.gameObjects;
                if (selected != null)
                {
                    roots.AddRange(selected);
                }
                return roots;
            }

            var activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                return roots;
            }

            roots.AddRange(activeScene.GetRootGameObjects());
            return roots;
        }

        static void CollectMatches(Transform current, UnityAiBridgeProtocolRequestParameters parameters, Regex nameRegex, string activeState, List<GameObject> matches)
        {
            if (current == null)
            {
                return;
            }

            if (MatchesFilters(current.gameObject, parameters, nameRegex, activeState))
            {
                matches.Add(current.gameObject);
            }

            for (int i = 0; i < current.childCount; i++)
            {
                CollectMatches(current.GetChild(i), parameters, nameRegex, activeState, matches);
            }
        }

        static bool MatchesFilters(GameObject candidate, UnityAiBridgeProtocolRequestParameters parameters, Regex nameRegex, string activeState)
        {
            if (candidate == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(parameters.findGameObjectsName) &&
                !candidate.name.Equals(parameters.findGameObjectsName, StringComparison.Ordinal))
            {
                return false;
            }

            if (nameRegex != null && !nameRegex.IsMatch(candidate.name))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(parameters.findGameObjectsPath))
            {
                var path = BuildHierarchyPath(candidate);
                if (path.IndexOf(parameters.findGameObjectsPath, StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(parameters.findGameObjectsTag) &&
                !candidate.CompareTag(parameters.findGameObjectsTag))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(parameters.findGameObjectsLayer))
            {
                var layerName = LayerMask.LayerToName(candidate.layer) ?? string.Empty;
                if (!layerName.Equals(parameters.findGameObjectsLayer, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(parameters.findGameObjectsComponent))
            {
                bool hasComponent = false;
                var components = candidate.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component == null)
                    {
                        continue;
                    }
                    var typeName = component.GetType().Name;
                    var fullTypeName = component.GetType().FullName ?? string.Empty;
                    if (typeName.Equals(parameters.findGameObjectsComponent, StringComparison.Ordinal) ||
                        fullTypeName.Equals(parameters.findGameObjectsComponent, StringComparison.Ordinal))
                    {
                        hasComponent = true;
                        break;
                    }
                }
                if (!hasComponent)
                {
                    return false;
                }
            }

            if (activeState == "active" && !candidate.activeInHierarchy)
            {
                return false;
            }
            if (activeState == "inactive" && candidate.activeInHierarchy)
            {
                return false;
            }

            return true;
        }

        static UnityAiBridgeFindGameObjectsEntry BuildEntry(GameObject gameObject)
        {
            var components = gameObject.GetComponents<Component>();
            var typeNames = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                typeNames[i] = components[i] == null ? string.Empty : components[i].GetType().FullName ?? string.Empty;
            }

            return new UnityAiBridgeFindGameObjectsEntry
            {
                name = gameObject.name,
                hierarchyPath = BuildHierarchyPath(gameObject),
                sceneName = gameObject.scene.name ?? string.Empty,
                scenePath = gameObject.scene.path ?? string.Empty,
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                tag = gameObject.tag,
                layer = gameObject.layer,
                layerName = LayerMask.LayerToName(gameObject.layer) ?? string.Empty,
                instanceId = gameObject.GetInstanceID(),
                componentTypeNames = typeNames
            };
        }

        static string BuildHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }
            var stack = new Stack<string>();
            var current = gameObject.transform;
            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", stack.ToArray());
        }

        static string WriteFindGameObjectsOutput(string requestId, UnityAiBridgeFindGameObjectsEntry[] entries, out long fileSizeBytes)
        {
            var projectRoot = GetProjectRoot();
            var outputDirectory = Path.Combine(projectRoot, ".lux", "outputs", "find-game-objects");
            Directory.CreateDirectory(outputDirectory);
            var safeRequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
            var outputPath = Path.Combine(outputDirectory, safeRequestId + ".json");
            var json = JsonUtility.ToJson(new UnityAiBridgeFindGameObjectsPayload
            {
                searchMode = string.Empty,
                totalMatchCount = entries.Length,
                returnedCount = entries.Length,
                truncated = false,
                outputFilePath = string.Empty,
                fileSizeBytes = 0L,
                gameObjects = entries
            }, true);
            File.WriteAllText(outputPath, json);
            fileSizeBytes = new FileInfo(outputPath).Length;
            return outputPath;
        }

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

        static UnityAiBridgeHierarchyFilters CreateHierarchyFilters(UnityAiBridgeProtocolRequestParameters parameters)
        {
            var useSelection = parameters.hierarchyUseSelection;
            var rootPath = string.IsNullOrWhiteSpace(parameters.hierarchyRootPath)
                ? string.Empty
                : parameters.hierarchyRootPath.Trim();
            var all = parameters.hierarchyAll || (!useSelection && string.IsNullOrEmpty(rootPath));
            var enabledFilterCount = (all ? 1 : 0) + (useSelection ? 1 : 0) + (string.IsNullOrEmpty(rootPath) ? 0 : 1);
            if (enabledFilterCount != 1)
            {
                throw new InvalidOperationException("Specify exactly one hierarchy filter: --all, --root-path, or --use-selection.");
            }

            return new UnityAiBridgeHierarchyFilters
            {
                all = all,
                rootPath = rootPath,
                useSelection = useSelection
            };
        }

        static List<Transform> CollectHierarchyRoots(Scene activeScene, UnityAiBridgeHierarchyFilters filters)
        {
            var sceneRoots = activeScene.GetRootGameObjects();
            if (filters.useSelection)
            {
                return CollectSelectedHierarchyRoots();
            }

            if (!string.IsNullOrEmpty(filters.rootPath))
            {
                foreach (var sceneRoot in sceneRoots)
                {
                    var match = FindHierarchyRootByPath(sceneRoot.transform, filters.rootPath);
                    if (match != null)
                    {
                        return new List<Transform> { match };
                    }
                }

                throw new InvalidOperationException($"Hierarchy root path was not found in the active scene: {filters.rootPath}");
            }

            var roots = new List<Transform>(sceneRoots.Length);
            foreach (var sceneRoot in sceneRoots)
            {
                if (sceneRoot != null)
                {
                    roots.Add(sceneRoot.transform);
                }
            }

            return roots;
        }

        static List<Transform> CollectSelectedHierarchyRoots()
        {
            var selectedTransforms = Selection.transforms;
            var results = new List<Transform>();
            if (selectedTransforms == null || selectedTransforms.Length == 0)
            {
                return results;
            }

            var selectedSet = new HashSet<Transform>(selectedTransforms);
            foreach (var selectedTransform in selectedTransforms)
            {
                if (selectedTransform == null)
                {
                    continue;
                }

                if (selectedTransform.parent != null && selectedSet.Contains(selectedTransform.parent))
                {
                    continue;
                }

                results.Add(selectedTransform);
            }

            return results;
        }

        static Transform FindHierarchyRootByPath(Transform current, string targetPath)
        {
            if (current == null)
            {
                return null;
            }

            if (string.Equals(BuildHierarchyPath(current.gameObject), targetPath, StringComparison.Ordinal))
            {
                return current;
            }

            for (int i = 0; i < current.childCount; i++)
            {
                var match = FindHierarchyRootByPath(current.GetChild(i), targetPath);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        static UnityAiBridgeHierarchyNode BuildHierarchyNode(Transform t, ref int nodeCount)
        {
            nodeCount++;
            var go = t.gameObject;
            var components = go.GetComponents<Component>();
            var typeNames = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                typeNames[i] = components[i] == null ? string.Empty : components[i].GetType().FullName ?? string.Empty;
            }

            var childNodes = new UnityAiBridgeHierarchyNode[t.childCount];
            for (int i = 0; i < t.childCount; i++)
            {
                childNodes[i] = BuildHierarchyNode(t.GetChild(i), ref nodeCount);
            }

            return new UnityAiBridgeHierarchyNode
            {
                name = go.name,
                path = BuildHierarchyPath(go),
                active = go.activeInHierarchy,
                tag = go.tag,
                layer = LayerMask.LayerToName(go.layer) ?? go.layer.ToString(),
                componentTypeNames = typeNames,
                children = childNodes
            };
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

    static class LuxInputSimulationState
    {
        static readonly HashSet<Key> HeldKeys = new HashSet<Key>();
        static readonly HashSet<InputMouseButton> HeldMouseButtons = new HashSet<InputMouseButton>();
        static readonly List<TimedInputAction> TimedActions = new List<TimedInputAction>();
        static Vector2 MousePosition;

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            EditorApplication.update -= ProcessTimedActions;
            EditorApplication.update += ProcessTimedActions;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        public static UnityAiBridgeInputSimulationPayload SimulateKeyboard(string action, Key key, int durationMs)
        {
            EnsureKeyboardAvailable();
            switch (action)
            {
                case "press":
                    SetKey(key, true);
                    Schedule(durationMs <= 0 ? 50 : durationMs, () => SetKey(key, false));
                    break;
                case "key-down":
                case "keydown":
                    SetKey(key, true);
                    break;
                case "key-up":
                case "keyup":
                    SetKey(key, false);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported keyboard action: {action}");
            }

            return CreatePayload("keyboard", action, key.ToString(), string.Empty, 0, 0, 0, 0, TimedActions.Count);
        }

        public static UnityAiBridgeInputSimulationPayload SimulateMouse(
            string action,
            string buttonName,
            float deltaX,
            float deltaY,
            float scrollX,
            float scrollY,
            int durationMs,
            int steps)
        {
            EnsureMouseAvailable();
            switch (action)
            {
                case "click":
                    var clickButton = ParseMouseButton(buttonName);
                    SetMouseButton(clickButton, true);
                    Schedule(durationMs <= 0 ? 50 : durationMs, () => SetMouseButton(clickButton, false));
                    return CreatePayload("mouse", action, string.Empty, clickButton.ToString(), 0, 0, 0, 0, TimedActions.Count);

                case "long-press":
                case "longpress":
                    var longPressButton = ParseMouseButton(buttonName);
                    SetMouseButton(longPressButton, true);
                    Schedule(durationMs <= 0 ? 500 : durationMs, () => SetMouseButton(longPressButton, false));
                    return CreatePayload("mouse", action, string.Empty, longPressButton.ToString(), 0, 0, 0, 0, TimedActions.Count);

                case "move-delta":
                case "movedelta":
                    QueueMouseState(new Vector2(deltaX, deltaY), Vector2.zero);
                    return CreatePayload("mouse", action, string.Empty, string.Empty, deltaX, deltaY, 0, 0, TimedActions.Count);

                case "smooth-delta":
                case "smoothdelta":
                    var stepCount = Math.Max(1, steps <= 0 ? 5 : steps);
                    var stepDelta = new Vector2(deltaX / stepCount, deltaY / stepCount);
                    for (int i = 0; i < stepCount; i++)
                    {
                        Schedule(i * 16, () => QueueMouseState(stepDelta, Vector2.zero));
                    }
                    return CreatePayload("mouse", action, string.Empty, string.Empty, deltaX, deltaY, 0, 0, TimedActions.Count);

                case "scroll":
                    QueueMouseState(Vector2.zero, new Vector2(scrollX, scrollY));
                    return CreatePayload("mouse", action, string.Empty, string.Empty, 0, 0, scrollX, scrollY, TimedActions.Count);

                default:
                    throw new InvalidOperationException($"Unsupported mouse action: {action}");
            }
        }

        static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                ReleaseAll();
            }
        }

        static void ProcessTimedActions()
        {
            if (TimedActions.Count == 0)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            for (int i = TimedActions.Count - 1; i >= 0; i--)
            {
                if (TimedActions[i].ExecuteAt > now)
                {
                    continue;
                }

                var action = TimedActions[i].Action;
                TimedActions.RemoveAt(i);
                action?.Invoke();
            }
        }

        static void Schedule(int delayMs, Action action)
        {
            TimedActions.Add(new TimedInputAction
            {
                ExecuteAt = EditorApplication.timeSinceStartup + Math.Max(0, delayMs) / 1000.0,
                Action = action
            });
        }

        static void SetKey(Key key, bool isPressed)
        {
            if (isPressed)
            {
                HeldKeys.Add(key);
            }
            else
            {
                HeldKeys.Remove(key);
            }

            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(HeldKeys.ToArray()));
            InputSystem.Update();
        }

        static void SetMouseButton(InputMouseButton button, bool isPressed)
        {
            if (isPressed)
            {
                HeldMouseButtons.Add(button);
            }
            else
            {
                HeldMouseButtons.Remove(button);
            }

            QueueMouseState(Vector2.zero, Vector2.zero);
        }

        static void QueueMouseState(Vector2 delta, Vector2 scroll)
        {
            var mouse = Mouse.current;
            MousePosition = mouse == null ? MousePosition : mouse.position.ReadValue();
            MousePosition += delta;
            var state = new MouseState
            {
                position = MousePosition,
                delta = delta,
                scroll = scroll
            };
            foreach (var button in HeldMouseButtons)
            {
                state = state.WithButton(button, true);
            }

            InputSystem.QueueStateEvent(Mouse.current, state);
            InputSystem.Update();
        }

        static InputMouseButton ParseMouseButton(string buttonName)
        {
            var normalized = string.IsNullOrWhiteSpace(buttonName) ? "left" : buttonName.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "left":
                case "left-button":
                    return InputMouseButton.Left;
                case "right":
                case "right-button":
                    return InputMouseButton.Right;
                case "middle":
                case "middle-button":
                    return InputMouseButton.Middle;
                case "back":
                case "back-button":
                    return InputMouseButton.Back;
                case "forward":
                case "forward-button":
                    return InputMouseButton.Forward;
                default:
                    throw new InvalidOperationException($"Unsupported mouse button: {buttonName}");
            }
        }

        static void EnsureKeyboardAvailable()
        {
            if (Keyboard.current == null)
            {
                throw new InvalidOperationException("Input System Keyboard.current is unavailable.");
            }
        }

        static void EnsureMouseAvailable()
        {
            if (Mouse.current == null)
            {
                throw new InvalidOperationException("Input System Mouse.current is unavailable.");
            }
        }

        static void ReleaseAll()
        {
            TimedActions.Clear();
            if (Keyboard.current != null && HeldKeys.Count > 0)
            {
                HeldKeys.Clear();
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                InputSystem.Update();
            }
            else
            {
                HeldKeys.Clear();
            }

            if (Mouse.current != null && HeldMouseButtons.Count > 0)
            {
                HeldMouseButtons.Clear();
                QueueMouseState(Vector2.zero, Vector2.zero);
            }
            else
            {
                HeldMouseButtons.Clear();
            }
        }

        static UnityAiBridgeInputSimulationPayload CreatePayload(
            string device,
            string action,
            string key,
            string button,
            float deltaX,
            float deltaY,
            float scrollX,
            float scrollY,
            int queuedActions)
        {
            return new UnityAiBridgeInputSimulationPayload
            {
                device = device,
                action = action,
                key = key ?? string.Empty,
                button = button ?? string.Empty,
                deltaX = deltaX,
                deltaY = deltaY,
                scrollX = scrollX,
                scrollY = scrollY,
                heldKeys = HeldKeys.Select(k => k.ToString()).OrderBy(k => k, StringComparer.Ordinal).ToArray(),
                heldButtons = HeldMouseButtons.Select(b => b.ToString()).OrderBy(b => b, StringComparer.Ordinal).ToArray(),
                queuedActions = queuedActions
            };
        }

        struct TimedInputAction
        {
            public double ExecuteAt;
            public Action Action;
        }
    }
}
