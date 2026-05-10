# Serialization

Use this file when LUX needs Unity serialization, JSONSerialize, SerializedObject, SerializedProperty, ScriptableObject, inspector state, or settings persistence patterns.

## Primary Paths

| Path | Use for |
|---|---|
| `Modules/JSONSerialize/Public/` | Unity JSON serialization API surface |
| `Editor/Mono/SerializedObject*.cs` | Editor serialized object/property bindings and behavior |
| `Runtime/Export/Scripting/` | ScriptableObject and Object API surface |
| `Editor/Mono/Inspector/` | inspector serialized editing patterns |
| `Editor/Mono/ScriptableSingleton.cs` | persisted Editor singleton settings pattern |
| `Modules/UIElements/Bindings/` | SerializedObject binding to UI Toolkit |

## Lookup Matrix

| Need | Search for | Confirm |
|---|---|---|
| Edit serialized fields | `SerializedObject`, `SerializedProperty` | update/apply order and iterator behavior |
| Bind UI Toolkit to object | `Bind`, `bindingPath`, `SerializedObjectBinding` | refresh timing and property path format |
| Persist Editor settings | `ScriptableSingleton`, `EditorPrefs`, `SettingsProvider` | storage location and save call |
| JSON round-trip | `JsonUtility`, `EditorJsonUtility` | supported field types and object handling |
| ScriptableObject asset | `ScriptableObject.CreateInstance`, `CreateAsset` | lifecycle and asset persistence |
| Inspector arrays | `arraySize`, `GetArrayElementAtIndex` | managed reference and reorder behavior |

## SerializedObject Checklist

- Call `Update` before reading when data may have changed externally.
- Call `ApplyModifiedProperties` after writes that should persist.
- Use serialized field names for paths.
- Handle multi-object editing only when LUX explicitly supports it.
- Avoid mixing direct field writes and serialized property writes in one flow.

## ScriptableObject Checklist

- Use ScriptableObject assets for persistent project-level LUX settings.
- Use ScriptableSingleton for Editor-only global state when Unity patterns fit.
- Mark dirty and save through Unity-supported APIs after changes.
- Avoid serializing secrets into tracked assets.

## JSON Checklist

- Use Unity JSON APIs for Unity object-shaped data when compatible.
- Use Rust/TypeScript JSON serializers for gateway and dashboard protocol data.
- Confirm Unity JSON limitations before relying on dictionaries, polymorphism, or private fields.
- Keep AI event logs as explicit JSONL contracts, not Unity serialization side effects.

## LUX Fit

- Use for Workbench state, CodexImage graph assets, Editor settings, inspector integrations, and UI Toolkit bindings.
- Keep generated data schemas original to LUX.
