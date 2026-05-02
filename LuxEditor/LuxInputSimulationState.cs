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
