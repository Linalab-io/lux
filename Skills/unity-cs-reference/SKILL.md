---
name: unity-cs-reference
description: "Lazy-load when Unity Editor automation or extension work needs UnityCsReference lookup: internal UnityEditor APIs, Editor scripting patterns, UI Toolkit/UIElements internals, AssetDatabase behavior, BuildPipeline internals, PackageManager code, serialization, or engine C# source behavior. Use as read-only reference only; do not load for ordinary C# edits that only need public docs."
---

# unity-cs-reference

Use UnityCsReference to understand Unity Editor and UnityEngine C# API surface, call patterns, and internal behavior before implementing LUX Editor automation.

## Repository Info

| Item | Value |
|---|---|
| Repository | https://github.com/Unity-Technologies/UnityCsReference |
| Version | Unity 6000.2.0b4 |
| Update status | Not currently updated beyond 6.2.0b4 |
| Solution | `Projects/CSharp/UnityReferenceSource.sln` |
| License | Unity Reference Only License |
| License URL | https://unity3d.com/legal/licenses/Unity_Reference_Only_License |

## License Constraints

- Use for understanding behavior and API surface only. Do NOT duplicate code.
- Do not copy-paste UnityCsReference source into this project.
- Do not redistribute modified UnityCsReference files or extracted implementations.
- Extract only facts needed for implementation: type names, member signatures, parameter names, call order, event flow, and observable behavior.
- Reimplement LUX code independently with original structure, names, and comments.

## Open References

| Open when you need to... | Read |
|---|---|
| orient in the repo, choose a path, verify version/license, or navigate the solution | `references/repo-overview.md` |
| inspect `Editor/` APIs, EditorWindow, EditorGUI, EditorUtility, ScriptBuild, PlayerSettings, or Mono bindings | `references/editor-apis.md` |
| inspect UI Toolkit core internals: VisualElement, UXML, USS, panels, debugger, bindings, event dispatch | `references/uidelements.md` |
| understand AssetDatabase, AssetImporter, import refresh, build asset collection, or incremental asset/build pipeline | `references/asset-pipeline.md` |
| understand BuildPipeline, BuildTarget, PlayerSettings, build reports, and pre/post build hooks | `references/build-system.md` |
| understand runtime scripting, Mono/managed bindings, scripting defines, domain reload, or compilation pipeline | `references/scripting-engine.md` |
| inspect PackageManager Editor UI, PackageInfo, package resolution, or manifest workflow | `references/package-manager.md` |
| understand JSONSerialize, SerializedObject, SerializedProperty, ScriptableObject, or inspector serialization patterns | `references/serialization.md` |
| inspect Editor UI frameworks around GraphView, SceneView, PresetsEditor, StyleSheetsEditor, QuickSearch, or HierarchyCore | `references/ui-toolkit-editor.md` |

## Key Directories for LUX

| LUX use case | UnityCsReference path |
|---|---|
| Editor automation windows and utilities | `Editor/Mono/`, `Editor/Mono/GUI/`, `Editor/Mono/Inspector/` |
| UI Toolkit panels and LUX Workbench UI | `Modules/UIElements/`, `Modules/StyleSheetsEditor/` |
| Asset discovery, import, and refresh operations | `Modules/AssetDatabase/Editor/`, `Editor/Mono/AssetDatabase/` |
| Build and compile automation | `Modules/BuildPipeline/Editor/`, `Editor/Mono/BuildPipeline/`, `Editor/IncrementalBuildPipeline/` |
| Package install/remove/update behavior | `Modules/PackageManager/Editor/Managed/` |
| Scene view, hierarchy, and object selection tools | `Modules/SceneView/`, `Modules/HierarchyCore/`, `Editor/Mono/SceneView/` |
| Dynamic code, domain reload, and compilation behavior | `Runtime/Scripting/`, `Editor/Mono/Scripting/`, `Editor/Mono/Compilation/` |
| Serialized settings and inspector state | `Modules/JSONSerialize/Public/`, `Editor/Mono/SerializedObject.bindings.cs`, `Runtime/Export/Scripting/` |
| Search, presets, shortcuts, and editor workflows | `Modules/QuickSearch/Editor/`, `Modules/PresetsEditor/`, `Modules/ShortcutManager/` |

## Lookup Workflow

1. Identify the Unity feature name from the LUX task.
2. Open the matching reference file above and choose the repo path.
3. Search UnityCsReference for exact symbols before broad text search.
4. Record signatures and behavior, not source implementation.
5. Implement original LUX code using public APIs when available; use internal APIs only when LUX already accepts that risk.
6. Verify C# changes with LSP diagnostics and Unity/LUX compile commands when applicable.

## LUX Coding Constraints

- Keep C# namespace `UnityEditor` and assembly `Linalab.LuxEditor` for LUX Editor scripts.
- Prefix new C# classes with `Lux`.
- Use partial classes for large Editor files.
- Use NUnit `[Test]` under `*Tests/Editor/` for Editor tests.
- Do not add `.meta` files manually.
