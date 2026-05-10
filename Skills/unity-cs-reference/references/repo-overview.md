# UnityCsReference Repo Overview

Use this file to choose the first UnityCsReference path before opening narrower references.

## Source Facts

| Item | Value |
|---|---|
| Repository | https://github.com/Unity-Technologies/UnityCsReference |
| Version | Unity 6000.2.0b4 |
| Update status | Not currently updated beyond 6.2.0b4 |
| Solution | `Projects/CSharp/UnityReferenceSource.sln` |
| License | Unity Reference Only License |
| License URL | https://unity3d.com/legal/licenses/Unity_Reference_Only_License |

## License Checklist

- Use for understanding behavior and API surface only. Do NOT duplicate code.
- Do not copy source code, comments, or file structure into LUX.
- Quote only tiny identifiers or signatures needed to identify APIs.
- Summarize behavior in your own words before implementation.
- Prefer Unity public APIs and LUX-native abstractions.

## Top-Level Map

| Path | Use for |
|---|---|
| `Editor/` | UnityEditor internal APIs, Editor Mono bindings, inspectors, windows, GUI, ScriptBuild |
| `Editor/Mono/` | Editor-side managed bindings and classic Editor scripting implementation |
| `Editor/IncrementalBuildPipeline/` | Incremental build graph and artifact/build dependency internals |
| `Runtime/` | UnityEngine runtime APIs, bindings, exported scripting surface |
| `Runtime/Scripting/` | Scripting engine integration and managed runtime behavior |
| `Runtime/Export/` | Exported runtime APIs and script binding declarations |
| `Runtime/Profiler/ScriptBindings/` | Profiler runtime bindings |
| `Runtime/Transform/ScriptBindings/` | Transform runtime bindings |
| `Modules/` | Feature modules; start here for most LUX work |
| `Tools/` | Build and repository support tools |
| `External/` | Third-party code; avoid unless diagnosing dependency behavior |
| `Projects/CSharp/` | Solution and project files for IDE navigation |

## Module Triage for LUX

| Need | Start at |
|---|---|
| Asset import, refresh, GUID/path lookup | `Modules/AssetDatabase/Editor/` |
| Build settings, build targets, build execution | `Modules/BuildPipeline/Editor/` |
| UI Toolkit controls and event behavior | `Modules/UIElements/` |
| UI Toolkit style editing | `Modules/StyleSheetsEditor/` |
| Package manager UI or package metadata | `Modules/PackageManager/Editor/Managed/` |
| Graph-style node editors | `Modules/GraphViewEditor/` |
| Scene view overlays and tools | `Modules/SceneView/` |
| IMGUI behavior | `Modules/IMGUI/` |
| Keyboard shortcuts | `Modules/ShortcutManager/` |
| Quick search providers | `Modules/QuickSearch/Editor/` |
| Hierarchy data and views | `Modules/HierarchyCore/` |
| Scene templates | `Modules/SceneTemplateEditor/` |
| Presets | `Modules/PresetsEditor/` |
| Localization | `Modules/Localization/` |
| Accessibility | `Modules/Accessibility/` |
| JSON serialization | `Modules/JSONSerialize/Public/` |
| AI module hooks | `Modules/AI/`, `Modules/AIEditor/` |

## Navigation Workflow

1. Search exact Unity type or method name first.
2. If no result, search module name plus public API name.
3. Prefer `.bindings.cs` files for API signatures and native linkage.
4. Prefer Editor module files for usage patterns and event order.
5. Trace call sites two levels up before mirroring a pattern.
6. Record the Unity version when behavior is version-sensitive.
7. Reimplement behavior in LUX terms; do not port Unity implementation code.

## Search Targets

| Query shape | Use when |
|---|---|
| `class TypeName` | Find primary type definition |
| `partial class TypeName` | Find split Editor/runtime definitions |
| `extern` plus method name | Find native-bound API signatures |
| `RequiredByNativeCode` | Find native lifecycle entry points |
| `UsedByNativeCode` | Find APIs called from engine code |
| `MenuItem` | Find Editor command patterns |
| `InitializeOnLoad` | Find Editor startup behavior |
| `ScriptableSingleton` | Find persisted Editor settings patterns |

## Verification Notes

- Treat internal APIs as unstable; wrap LUX usage behind narrow adapters.
- Check official public documentation when a public API exists.
- Validate LUX C# with LSP diagnostics and Lux compile flow when code changes.
