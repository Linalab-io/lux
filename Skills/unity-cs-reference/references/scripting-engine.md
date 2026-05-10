# Scripting Engine

Use this file when LUX needs script compilation behavior, domain reload timing, Mono/managed bindings, scripting defines, or runtime scripting API surface.

## Primary Paths

| Path | Use for |
|---|---|
| `Runtime/Scripting/` | scripting engine integration and runtime managed behavior |
| `Runtime/Export/Scripting/` | exported UnityEngine scripting API declarations |
| `Editor/Mono/Scripting/` | Editor scripting utilities, domain reload, define handling |
| `Editor/Mono/Compilation/` | compilation pipeline and assembly metadata |
| `Editor/Mono/AssemblyHelper.cs` | assembly loading and metadata helpers where present |
| `Runtime/Export/` | native-bound runtime APIs and attributes |

## Lookup Matrix

| Need | Search for | Confirm |
|---|---|---|
| Compile scripts | `CompilationPipeline` | event names, assembly result shape |
| Track assembly reload | `AssemblyReloadEvents`, `DidReloadScripts` | before/after order |
| Set scripting defines | `PlayerSettings`, `SetScriptingDefineSymbols` | target type and reload effects |
| Inspect managed bindings | `NativeHeader`, `NativeMethod`, `FreeFunction` | public signature and native linkage |
| Runtime initialization | `RuntimeInitializeOnLoadMethod` | load type and play mode timing |
| Editor initialization | `InitializeOnLoad`, `InitializeOnLoadMethod` | domain reload behavior |
| Scriptable object lifecycle | `ScriptableObject` | create/destroy/save behavior |
| Attribute behavior | `RequiredByNativeCode`, `UsedByNativeCode` | engine-called entry points |

## Compilation Checklist

- Use `CompilationPipeline` events to observe compile lifecycle, not to force unsupported states.
- Expect domain reload after compile unless project settings disable or alter it.
- Store transient LUX state outside reload-sensitive static fields when needed.
- Capture compiler messages through Unity-supported APIs or Lux compile output.

## Defines Checklist

- Resolve target as `NamedBuildTarget` or `BuildTargetGroup` based on API signature.
- Compare existing defines before writing.
- Batch define changes with explicit user intent because they can trigger recompilation.
- Restore previous defines after temporary automation if LUX changed them.

## Managed Binding Checklist

- Treat `.bindings.cs` as signature and attribute reference.
- Do not infer native implementation details beyond exposed behavior.
- Prefer public API wrappers even if internal binding names are visible.
- Note version-specific attributes when behavior differs across Unity versions.

## LUX Fit

- Use for dynamic code execution, compile panel behavior, AI Bridge domain reload resilience, and generated Editor script workflows.
- Keep dynamically generated or executed code constrained by LUX guardrails.
