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
    }
}
