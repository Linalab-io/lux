# Build System

Use this file when LUX needs Unity build automation, BuildPipeline behavior, PlayerSettings, BuildTarget handling, build reports, or build callbacks.

## Primary Paths

| Path | Use for |
|---|---|
| `Modules/BuildPipeline/Editor/` | BuildPipeline module implementation and build APIs |
| `Editor/Mono/BuildPipeline/` | Editor build orchestration and platform build helpers |
| `Editor/IncrementalBuildPipeline/` | incremental build graph and artifact invalidation |
| `Editor/Mono/PlayerSettings*.cs` | project/player build settings surface |
| `Editor/Mono/BuildTarget*.cs` | target groups, named build targets, platform mapping |
| `Editor/Mono/BuildPlayerWindow*` | build window settings and default option flow |

## Lookup Matrix

| Need | Search for | Confirm |
|---|---|---|
| Execute a build | `BuildPipeline.BuildPlayer` | overloads, options, report return type |
| Read build result | `BuildReport`, `BuildSummary` | result enum and error counts |
| Choose target | `BuildTarget`, `BuildTargetGroup`, `NamedBuildTarget` | group mapping and active target behavior |
| Change build settings | `EditorBuildSettings`, `EditorBuildSettingsScene` | enabled scenes and path validity |
| Configure player | `PlayerSettings` | per-platform overload and named target support |
| Pre-build hook | `IPreprocessBuildWithReport` | callback order and report data |
| Post-build hook | `IPostprocessBuildWithReport` | result handling and output path |
| Build window parity | `BuildPlayerWindow` | default options and scene list |

## Build Automation Checklist

- Resolve scenes from `EditorBuildSettings.scenes` unless the user explicitly supplies a scene list.
- Validate output path and target before invoking build.
- Capture `BuildReport.summary` fields for LUX JSON output.
- Keep build hooks deterministic and side-effect scoped.
- Restore changed settings if LUX temporarily changes build target or defines.

## PlayerSettings Checklist

- Confirm whether the API uses `BuildTargetGroup` or `NamedBuildTarget` in Unity 6000.2.
- Prefer explicit target/group parameters over active-target assumptions.
- Treat scripting define changes as domain-reload-sensitive.
- Log changed settings before and after automated operations.

## Callback Checklist

- Use preprocess callbacks for validation and generated asset preparation.
- Use postprocess callbacks for reporting and external artifact registration.
- Avoid UI prompts inside batch or CLI-triggered builds.
- Keep LUX callbacks namespaced and class-prefixed with `Lux`.

## LUX Fit

- Use for `lux compile`, future build commands, project context reporting, and CI-like Editor automation.
- Verify Rust gateway build endpoints separately from Unity build behavior.
