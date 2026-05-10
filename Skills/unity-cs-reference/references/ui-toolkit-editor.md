# UI Toolkit Editor Ecosystem

Use this file when LUX needs Editor UI frameworks around GraphView, SceneView, presets, style sheet editing, QuickSearch, hierarchy data, shortcuts, or scene templates.

## Primary Paths

| Path | Use for |
|---|---|
| `Modules/GraphViewEditor/` | node graph UI, ports, edges, manipulators, blackboard patterns |
| `Modules/SceneView/` | Scene view overlays, tools, camera/view interaction |
| `Modules/PresetsEditor/` | preset assets, preset UI, default preset behavior |
| `Modules/StyleSheetsEditor/` | USS authoring and style sheet editor internals |
| `Modules/QuickSearch/Editor/` | search providers, indexing, result actions |
| `Modules/HierarchyCore/` | hierarchy data structures and view behavior |
| `Modules/ShortcutManager/` | shortcut registration and conflict handling |
| `Modules/SceneTemplateEditor/` | scene template creation and instantiation workflows |
| `Modules/IMGUI/` | IMGUI integration and legacy controls |
| `Modules/InputForUI/` | UI input routing internals |

## Lookup Matrix

| Need | Search for | Confirm |
|---|---|---|
| Node graph editor | `GraphView`, `Node`, `Port`, `Edge` | manipulators, selection, serialization expectations |
| Scene interaction | `SceneView`, `EditorTool`, `Overlay` | repaint and tool lifecycle |
| Search integration | `SearchProvider`, `SearchItem`, `SearchService` | provider registration and action handling |
| Shortcut support | `Shortcut`, `ShortcutManager` | context, binding, conflict behavior |
| Preset workflow | `Preset`, `PresetSelector` | default preset application rules |
| USS editing | `StyleSheet`, `StyleSheetsEditor` | selector/property editing behavior |
| Hierarchy UI | `Hierarchy`, `TreeView` | data source and selection flow |
| Scene templates | `SceneTemplate` | dependency and instantiation flow |

## GraphView Checklist

- Use GraphView as a reference for node-editor behavior, but consider API stability before building new LUX dependencies.
- Keep graph serialization in LUX-owned ScriptableObject data.
- Avoid copying Unity graph manipulators; implement minimal LUX-specific interactions.
- Verify zoom, selection, and edge connection event order before adding AI automation.

## SceneView Checklist

- Use SceneView APIs only for Editor-facing visualization or object manipulation.
- Register and unregister callbacks with clear lifecycle ownership.
- Avoid SceneView dependencies in headless or batch compile paths.
- Repaint only when state changes or command output requires it.

## Search and Shortcut Checklist

- Register search providers with stable IDs and narrow scopes.
- Keep shortcut IDs namespaced to LUX.
- Check conflict and context behavior in UnityCsReference before assigning defaults.
- Do not trigger package/project mutations from passive search providers.

## LUX Fit

- Use GraphView patterns for CodexImage visual editor decisions.
- Use QuickSearch and ShortcutManager patterns for command discovery.
- Use SceneView and HierarchyCore patterns for object/hierarchy automation.
- Use StyleSheetsEditor paths to debug USS behavior for LUX UI Toolkit panels.
