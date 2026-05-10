# Editor APIs

Use this file when implementing Unity Editor automation, EditorWindow UI, Editor GUI, utilities, build settings, or Editor-only managed bindings.

## Primary Paths

| Path | Use for |
|---|---|
| `Editor/Mono/` | Core UnityEditor managed implementation and bindings |
| `Editor/Mono/GUI/` | IMGUI controls, EditorGUI, EditorGUILayout, GUI utility behavior |
| `Editor/Mono/EditorWindow.cs` | EditorWindow lifecycle and docking/window behavior |
| `Editor/Mono/EditorUtility.cs` | Utility dialogs, dirty flags, object helpers |
| `Editor/Mono/EditorApplication.cs` | update loop, delay calls, play mode and compilation events |
| `Editor/Mono/PlayerSettings*.cs` | PlayerSettings API surface and platform-specific settings |
| `Editor/Mono/BuildPipeline/` | Editor build orchestration and report patterns |
| `Editor/Mono/Scripting/` | compilation, script reload, define symbols, scripting utilities |
| `Editor/Mono/Inspector/` | inspector and custom editor patterns |
| `Editor/Mono/ProjectBrowser/` | project window and asset selection behavior |

## Lookup Matrix

| LUX task | Search for | Confirm |
|---|---|---|
| Create an EditorWindow | `EditorWindow`, `CreateGUI`, `OnEnable`, `Show` | lifecycle, UI Toolkit vs IMGUI entry point |
| Run delayed Editor work | `EditorApplication.delayCall`, `update` | event timing and cleanup |
| React to play mode | `playModeStateChanged` | state enum and transition order |
| Mark assets/settings changed | `SetDirty`, `SaveAssets` | object type and persistence rules |
| Read or change PlayerSettings | `PlayerSettings` | platform overloads and define handling |
| Show dialogs or progress | `DisplayDialog`, `Progress` | blocking behavior and cancel paths |
| Use selection or hierarchy | `Selection`, `ActiveEditorTracker` | object scope and event notifications |
| Add menu commands | `MenuItem` | validation method and priority |

## EditorWindow Checklist

- Prefer UI Toolkit `CreateGUI` for new LUX windows.
- Use `OnEnable` for state setup and event subscription.
- Use `OnDisable` for unsubscribing and releasing callbacks.
- Avoid long work on GUI callbacks; route to async command or delayed work.
- Persist Editor window state through Unity-supported Editor preferences or ScriptableSingleton patterns.

## Editor Automation Checklist

- Verify whether API is public, internal, or native-bound before depending on it.
- Prefer stable public UnityEditor APIs for LUX commands.
- Guard version-sensitive behavior behind one adapter method.
- Re-check call order in UnityCsReference before using Editor events.
- Do not copy Unity implementation; write original LUX flow around observed behavior.

## Common Anchors

| Anchor | Why inspect it |
|---|---|
| `InitializeOnLoad` | Editor domain reload initialization |
| `InitializeOnLoadMethod` | static startup hooks without class constructors |
| `DidReloadScripts` | post-compilation actions |
| `EditorApplication.CallbackFunction` | update and delay callback shape |
| `ScriptableSingleton<T>` | persisted Editor singleton settings |
| `SettingsProvider` | Project Settings / Preferences integration |
| `CustomEditor` | Inspector extension patterns |
| `AssetPostprocessor` | asset pipeline event hooks |

## LUX Fit

- Use these APIs for Lux Workbench, AI Bridge controls, compile/test command integration, and Editor status surfaces.
- Keep new C# classes `Lux*`, in namespace `UnityEditor`, in assembly `Linalab.LuxEditor`.
- Add NUnit Editor tests only for LUX behavior, not Unity internals.
