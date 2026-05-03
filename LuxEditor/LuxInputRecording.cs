using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Linalab.UnityAiBridge.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Linalab.Lux.Editor
{
    public static class LuxInputRecording
    {
        const string MediaType = "application/vnd.linalab.lux.input-recording+json";
        static readonly object SessionLock = new object();
        static readonly List<LuxInputFrame> RecordingFrames = new List<LuxInputFrame>();
        static LuxInputSessionKind activeSession;
        static double recordStartedAt;
        static int recordFrameIndex;
        static string recordRequestId;
        static string recordOutputPath;
        static LuxInputDeviceStates lastRecordedStates;
        static LuxInputRecordingFile replayFile;
        static int replayFrameCursor;
        static string replayFilePath;

        public static UnityAiBridgeInputRecordPayload Record(string action, string requestId, string outputPath)
        {
            action = NormalizeAction(action);
            if (!EditorApplication.isPlaying)
            {
                throw new LuxInputPlayModeRequiredException("Input recording requires PlayMode.");
            }

            switch (action)
            {
                case "start":
                    return StartRecording(requestId, outputPath);
                case "stop":
                    return StopRecording();
                default:
                    throw new ArgumentException($"Unsupported input record action: {action}");
            }
        }

        public static UnityAiBridgeInputReplayPayload Replay(string action, string filePath)
        {
            action = NormalizeAction(action);
            if (!EditorApplication.isPlaying)
            {
                throw new LuxInputPlayModeRequiredException("Input replay requires PlayMode.");
            }

            switch (action)
            {
                case "start":
                    return StartReplay(filePath);
                case "stop":
                    return StopReplay();
                case "status":
                    return GetReplayStatus(action);
                default:
                    throw new ArgumentException($"Unsupported input replay action: {action}");
            }
        }

        static UnityAiBridgeInputRecordPayload StartRecording(string requestId, string outputPath)
        {
            lock (SessionLock)
            {
                EnsureNoActiveSession();
                activeSession = LuxInputSessionKind.Recording;
                recordStartedAt = EditorApplication.timeSinceStartup;
                recordFrameIndex = 0;
                recordRequestId = CreateSafeRequestId(requestId);
                recordOutputPath = outputPath ?? string.Empty;
                RecordingFrames.Clear();
                lastRecordedStates = CaptureStates();
                EditorApplication.update += CaptureRecordingFrame;
                return new UnityAiBridgeInputRecordPayload
                {
                    action = "start",
                    active = true,
                    frameCount = 0,
                    filePath = string.Empty,
                    fileSizeBytes = 0L,
                    mediaType = MediaType,
                    message = "Input recording started."
                };
            }
        }

        static UnityAiBridgeInputRecordPayload StopRecording()
        {
            LuxInputFrame[] frames;
            string requestId;
            string requestedOutputPath;
            lock (SessionLock)
            {
                if (activeSession != LuxInputSessionKind.Recording)
                {
                    return new UnityAiBridgeInputRecordPayload
                    {
                        action = "stop",
                        active = false,
                        frameCount = 0,
                        filePath = string.Empty,
                        fileSizeBytes = 0L,
                        mediaType = MediaType,
                        message = "No input recording session was active."
                    };
                }

                EditorApplication.update -= CaptureRecordingFrame;
                activeSession = LuxInputSessionKind.None;
                frames = RecordingFrames.ToArray();
                requestId = recordRequestId;
                requestedOutputPath = recordOutputPath;
                RecordingFrames.Clear();
                recordRequestId = string.Empty;
                recordOutputPath = string.Empty;
            }

            string outputPath = ResolveRecordingOutputPath(requestedOutputPath, requestId);
            var recording = new LuxInputRecordingFile
            {
                schemaVersion = 1,
                protocol = "lux.input-recording.v1",
                createdAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion ?? string.Empty,
                frameCount = frames.Length,
                frames = frames
            };
            File.WriteAllText(outputPath, JsonUtility.ToJson(recording, true));
            var metadata = LuxArtifactPaths.WriteArtifactMetadata(outputPath, "input-recordings", requestId);
            return new UnityAiBridgeInputRecordPayload
            {
                action = "stop",
                active = false,
                frameCount = frames.Length,
                filePath = metadata["filePath"] as string ?? outputPath,
                fileSizeBytes = metadata.TryGetValue("fileSizeBytes", out var sizeValue) ? Convert.ToInt64(sizeValue) : 0L,
                mediaType = MediaType,
                message = "Input recording stopped."
            };
        }

        static string ResolveRecordingOutputPath(string requestedOutputPath, string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestedOutputPath))
            {
                string outputDirectory = LuxArtifactPaths.GetPersistentOutputPath("input-recordings");
                return Path.Combine(outputDirectory, requestId + ".json");
            }

            string projectRoot = Path.GetFullPath(LuxBridgeSettings.GetProjectRoot());
            string normalizedPath = Path.GetFullPath(Path.IsPathRooted(requestedOutputPath)
                ? requestedOutputPath
                : Path.Combine(projectRoot, requestedOutputPath));
            string projectRootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!normalizedPath.StartsWith(projectRootWithSeparator, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedPath, projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Recording output path escapes the project root: {normalizedPath}");
            }
            if (normalizedPath.IndexOf(".uloop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new ArgumentException($"Recording output path must not use .uloop: {normalizedPath}");
            }

            var directory = Path.GetDirectoryName(normalizedPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return normalizedPath;
        }

        static UnityAiBridgeInputReplayPayload StartReplay(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Replay input file is required.");
            }

            lock (SessionLock)
            {
                EnsureNoActiveSession();
                replayFilePath = NormalizeProjectFilePath(filePath);
                replayFile = JsonUtility.FromJson<LuxInputRecordingFile>(File.ReadAllText(replayFilePath));
                if (replayFile == null || replayFile.frames == null)
                {
                    throw new ArgumentException($"Input recording file is invalid: {replayFilePath}");
                }

                replayFrameCursor = 0;
                activeSession = LuxInputSessionKind.Replay;
                EditorApplication.update += ReplayNextFrame;
                return GetReplayStatus("start");
            }
        }

        static UnityAiBridgeInputReplayPayload StopReplay()
        {
            lock (SessionLock)
            {
                if (activeSession == LuxInputSessionKind.Replay)
                {
                    EditorApplication.update -= ReplayNextFrame;
                    activeSession = LuxInputSessionKind.None;
                }

                return GetReplayStatus("stop");
            }
        }

        static UnityAiBridgeInputReplayPayload GetReplayStatus(string action)
        {
            var totalFrames = replayFile?.frames == null ? 0 : replayFile.frames.Length;
            return new UnityAiBridgeInputReplayPayload
            {
                action = action,
                active = activeSession == LuxInputSessionKind.Replay,
                filePath = replayFilePath ?? string.Empty,
                frameCount = totalFrames,
                replayedFrameCount = Math.Min(replayFrameCursor, totalFrames),
                completed = activeSession != LuxInputSessionKind.Replay && totalFrames > 0 && replayFrameCursor >= totalFrames,
                message = activeSession == LuxInputSessionKind.Replay ? "Input replay is running." : "Input replay is not running."
            };
        }

        static void CaptureRecordingFrame()
        {
            lock (SessionLock)
            {
                if (activeSession != LuxInputSessionKind.Recording)
                {
                    return;
                }

                var currentStates = CaptureStates();
                var frame = new LuxInputFrame
                {
                    frameIndex = recordFrameIndex,
                    elapsedSeconds = EditorApplication.timeSinceStartup - recordStartedAt,
                    keyboard = currentStates.KeyboardChanged(lastRecordedStates) ? currentStates.keyboard : null,
                    mouse = currentStates.MouseChanged(lastRecordedStates) ? currentStates.mouse : null,
                    gamepad = currentStates.GamepadChanged(lastRecordedStates) ? currentStates.gamepad : null
                };
                recordFrameIndex++;
                lastRecordedStates = currentStates;
                if (frame.keyboard != null || frame.mouse != null || frame.gamepad != null)
                {
                    RecordingFrames.Add(frame);
                }
            }
        }

        static void ReplayNextFrame()
        {
            lock (SessionLock)
            {
                if (activeSession != LuxInputSessionKind.Replay || replayFile?.frames == null)
                {
                    return;
                }

                if (replayFrameCursor >= replayFile.frames.Length)
                {
                    EditorApplication.update -= ReplayNextFrame;
                    activeSession = LuxInputSessionKind.None;
                    return;
                }

                ApplyFrame(replayFile.frames[replayFrameCursor]);
                replayFrameCursor++;
            }
        }

        static void ApplyFrame(LuxInputFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            if (frame.keyboard != null && Keyboard.current != null)
            {
                var state = new KeyboardState();
                SetKeyboardKey(ref state, Key.Space, frame.keyboard.space);
                SetKeyboardKey(ref state, Key.Enter, frame.keyboard.enter);
                SetKeyboardKey(ref state, Key.Escape, frame.keyboard.escape);
                SetKeyboardKey(ref state, Key.LeftShift, frame.keyboard.leftShift);
                SetKeyboardKey(ref state, Key.RightShift, frame.keyboard.rightShift);
                SetKeyboardKey(ref state, Key.LeftCtrl, frame.keyboard.leftCtrl);
                SetKeyboardKey(ref state, Key.RightCtrl, frame.keyboard.rightCtrl);
                SetKeyboardKey(ref state, Key.W, frame.keyboard.w);
                SetKeyboardKey(ref state, Key.A, frame.keyboard.a);
                SetKeyboardKey(ref state, Key.S, frame.keyboard.s);
                SetKeyboardKey(ref state, Key.D, frame.keyboard.d);
                SetKeyboardKey(ref state, Key.Q, frame.keyboard.q);
                SetKeyboardKey(ref state, Key.E, frame.keyboard.e);
                SetKeyboardKey(ref state, Key.R, frame.keyboard.r);
                SetKeyboardKey(ref state, Key.F, frame.keyboard.f);
                SetKeyboardKey(ref state, Key.Digit1, frame.keyboard.alpha1);
                SetKeyboardKey(ref state, Key.Digit2, frame.keyboard.alpha2);
                SetKeyboardKey(ref state, Key.Digit3, frame.keyboard.alpha3);
                InputSystem.QueueStateEvent(Keyboard.current, state);
            }

            if (frame.mouse != null && Mouse.current != null)
            {
                var state = new MouseState
                {
                    position = new Vector2(frame.mouse.x, frame.mouse.y),
                    delta = new Vector2(frame.mouse.deltaX, frame.mouse.deltaY),
                    scroll = new Vector2(frame.mouse.scrollX, frame.mouse.scrollY)
                };
                state.WithButton(MouseButton.Left, frame.mouse.leftButton);
                state.WithButton(MouseButton.Right, frame.mouse.rightButton);
                state.WithButton(MouseButton.Middle, frame.mouse.middleButton);
                InputSystem.QueueStateEvent(Mouse.current, state);
            }

            if (frame.gamepad != null && Gamepad.current != null)
            {
                var state = new GamepadState
                {
                    leftStick = new Vector2(frame.gamepad.leftStickX, frame.gamepad.leftStickY),
                    rightStick = new Vector2(frame.gamepad.rightStickX, frame.gamepad.rightStickY),
                    leftTrigger = frame.gamepad.leftTrigger,
                    rightTrigger = frame.gamepad.rightTrigger
                };
                SetGamepadButton(ref state, GamepadButton.South, frame.gamepad.southButton);
                SetGamepadButton(ref state, GamepadButton.East, frame.gamepad.eastButton);
                SetGamepadButton(ref state, GamepadButton.West, frame.gamepad.westButton);
                SetGamepadButton(ref state, GamepadButton.North, frame.gamepad.northButton);
                SetGamepadButton(ref state, GamepadButton.LeftShoulder, frame.gamepad.leftShoulder);
                SetGamepadButton(ref state, GamepadButton.RightShoulder, frame.gamepad.rightShoulder);
                SetGamepadButton(ref state, GamepadButton.Start, frame.gamepad.startButton);
                SetGamepadButton(ref state, GamepadButton.Select, frame.gamepad.selectButton);
                InputSystem.QueueStateEvent(Gamepad.current, state);
            }

            InputSystem.Update();
        }

        static LuxInputDeviceStates CaptureStates()
        {
            return new LuxInputDeviceStates
            {
                keyboard = CaptureKeyboard(),
                mouse = CaptureMouse(),
                gamepad = CaptureGamepad()
            };
        }

        static LuxKeyboardState CaptureKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return null;
            }

            return new LuxKeyboardState
            {
                space = keyboard.spaceKey.isPressed,
                enter = keyboard.enterKey.isPressed,
                escape = keyboard.escapeKey.isPressed,
                leftShift = keyboard.leftShiftKey.isPressed,
                rightShift = keyboard.rightShiftKey.isPressed,
                leftCtrl = keyboard.leftCtrlKey.isPressed,
                rightCtrl = keyboard.rightCtrlKey.isPressed,
                w = keyboard.wKey.isPressed,
                a = keyboard.aKey.isPressed,
                s = keyboard.sKey.isPressed,
                d = keyboard.dKey.isPressed,
                q = keyboard.qKey.isPressed,
                e = keyboard.eKey.isPressed,
                r = keyboard.rKey.isPressed,
                f = keyboard.fKey.isPressed,
                alpha1 = keyboard.digit1Key.isPressed,
                alpha2 = keyboard.digit2Key.isPressed,
                alpha3 = keyboard.digit3Key.isPressed
            };
        }

        static LuxMouseState CaptureMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return null;
            }

            var position = mouse.position.ReadValue();
            var delta = mouse.delta.ReadValue();
            var scroll = mouse.scroll.ReadValue();
            return new LuxMouseState
            {
                x = position.x,
                y = position.y,
                deltaX = delta.x,
                deltaY = delta.y,
                scrollX = scroll.x,
                scrollY = scroll.y,
                leftButton = mouse.leftButton.isPressed,
                rightButton = mouse.rightButton.isPressed,
                middleButton = mouse.middleButton.isPressed
            };
        }

        static LuxGamepadState CaptureGamepad()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return null;
            }

            var leftStick = gamepad.leftStick.ReadValue();
            var rightStick = gamepad.rightStick.ReadValue();
            return new LuxGamepadState
            {
                leftStickX = leftStick.x,
                leftStickY = leftStick.y,
                rightStickX = rightStick.x,
                rightStickY = rightStick.y,
                leftTrigger = gamepad.leftTrigger.ReadValue(),
                rightTrigger = gamepad.rightTrigger.ReadValue(),
                southButton = gamepad.buttonSouth.isPressed,
                eastButton = gamepad.buttonEast.isPressed,
                westButton = gamepad.buttonWest.isPressed,
                northButton = gamepad.buttonNorth.isPressed,
                leftShoulder = gamepad.leftShoulder.isPressed,
                rightShoulder = gamepad.rightShoulder.isPressed,
                startButton = gamepad.startButton.isPressed,
                selectButton = gamepad.selectButton.isPressed
            };
        }

        static void EnsureNoActiveSession()
        {
            if (activeSession != LuxInputSessionKind.None)
            {
                throw new LuxInputSingleFlightException("Only one Lux input recording or replay session can run at a time.");
            }
        }

        static string NormalizeAction(string action)
        {
            return string.IsNullOrWhiteSpace(action) ? string.Empty : action.Trim().ToLowerInvariant();
        }

        static string CreateSafeRequestId(string requestId)
        {
            var raw = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId.Trim();
            return Regex.Replace(raw, "[^A-Za-z0-9_.-]", "_");
        }

        static string NormalizeProjectFilePath(string filePath)
        {
            var projectRoot = LuxBridgeSettings.GetProjectRoot();
            var normalizedProjectRoot = Path.GetFullPath(projectRoot);
            var normalizedPath = Path.GetFullPath(Path.IsPathRooted(filePath)
                ? filePath
                : Path.Combine(normalizedProjectRoot, filePath));
            var projectRootWithSeparator = normalizedProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!normalizedPath.StartsWith(projectRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Replay input file must be inside the project root: {normalizedPath}");
            }
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException($"Replay input file was not found: {normalizedPath}", normalizedPath);
            }
            return normalizedPath;
        }

        static void SetKeyboardKey(ref KeyboardState state, Key key, bool pressed)
        {
            if (pressed)
            {
                state.Press(key);
            }
        }

        static void SetGamepadButton(ref GamepadState state, GamepadButton button, bool pressed)
        {
            if (pressed)
            {
                state = state.WithButton(button);
            }
        }
    }

    enum LuxInputSessionKind
    {
        None,
        Recording,
        Replay
    }

    public sealed class LuxInputSingleFlightException : InvalidOperationException
    {
        public LuxInputSingleFlightException(string message) : base(message) { }
    }

    public sealed class LuxInputPlayModeRequiredException : InvalidOperationException
    {
        public LuxInputPlayModeRequiredException(string message) : base(message) { }
    }

    struct LuxInputDeviceStates
    {
        public LuxKeyboardState keyboard;
        public LuxMouseState mouse;
        public LuxGamepadState gamepad;

        public bool KeyboardChanged(LuxInputDeviceStates previous) => !LuxInputStateComparer.Equals(keyboard, previous.keyboard);
        public bool MouseChanged(LuxInputDeviceStates previous) => !LuxInputStateComparer.Equals(mouse, previous.mouse);
        public bool GamepadChanged(LuxInputDeviceStates previous) => !LuxInputStateComparer.Equals(gamepad, previous.gamepad);
    }

    static class LuxInputStateComparer
    {
        public static bool Equals(LuxKeyboardState a, LuxKeyboardState b) => JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        public static bool Equals(LuxMouseState a, LuxMouseState b) => JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        public static bool Equals(LuxGamepadState a, LuxGamepadState b) => JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
    }

    [Serializable]
    public sealed class LuxInputRecordingFile
    {
        public int schemaVersion;
        public string protocol;
        public string createdAtUtc;
        public string unityVersion;
        public int frameCount;
        public LuxInputFrame[] frames;
    }

    [Serializable]
    public sealed class LuxInputFrame
    {
        public int frameIndex;
        public double elapsedSeconds;
        public LuxKeyboardState keyboard;
        public LuxMouseState mouse;
        public LuxGamepadState gamepad;
    }

    [Serializable]
    public sealed class LuxKeyboardState
    {
        public bool space;
        public bool enter;
        public bool escape;
        public bool leftShift;
        public bool rightShift;
        public bool leftCtrl;
        public bool rightCtrl;
        public bool w;
        public bool a;
        public bool s;
        public bool d;
        public bool q;
        public bool e;
        public bool r;
        public bool f;
        public bool alpha1;
        public bool alpha2;
        public bool alpha3;
    }

    [Serializable]
    public sealed class LuxMouseState
    {
        public float x;
        public float y;
        public float deltaX;
        public float deltaY;
        public float scrollX;
        public float scrollY;
        public bool leftButton;
        public bool rightButton;
        public bool middleButton;
    }

    [Serializable]
    public sealed class LuxGamepadState
    {
        public float leftStickX;
        public float leftStickY;
        public float rightStickX;
        public float rightStickY;
        public float leftTrigger;
        public float rightTrigger;
        public bool southButton;
        public bool eastButton;
        public bool westButton;
        public bool northButton;
        public bool leftShoulder;
        public bool rightShoulder;
        public bool startButton;
        public bool selectButton;
    }
}
