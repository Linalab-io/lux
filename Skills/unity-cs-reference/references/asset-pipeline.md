# Asset Pipeline

Use this file when LUX needs AssetDatabase, AssetImporter, import refresh, GUID/path handling, dependency tracking, or incremental build pipeline behavior.

## Primary Paths

| Path | Use for |
|---|---|
| `Modules/AssetDatabase/Editor/` | AssetDatabase API surface, import pipeline, GUID/path behavior |
| `Editor/Mono/AssetDatabase/` | Editor-side asset database helpers and bindings where present |
| `Editor/Mono/AssetPostprocessor.cs` | import callbacks and postprocessor lifecycle |
| `Editor/IncrementalBuildPipeline/` | artifact graph, build cache, dependency tracking |
| `Modules/BuildPipeline/Editor/` | build-time asset collection and pipeline integration |
| `Runtime/Export/Resources/` | Resources API surface when runtime asset lookup matters |

## Lookup Matrix

| Need | Search for | Confirm |
|---|---|---|
| Convert GUID/path | `AssetPathToGUID`, `GUIDToAssetPath` | empty string behavior and path normalization |
| Load assets in Editor | `LoadAssetAtPath`, `FindAssets` | type filter and folder scope behavior |
| Refresh imports | `Refresh`, `ImportAsset`, `StartAssetEditing` | batching, sync behavior, refresh options |
| Inspect importers | `AssetImporter`, `GetAtPath` | importer type and dirty/apply behavior |
| React to import | `AssetPostprocessor` | callback order and asset path arrays |
| Save generated assets | `CreateAsset`, `AddObjectToAsset`, `SaveAssets` | main object and sub-asset rules |
| Track dependencies | `GetDependencies`, `GetAssetDependencyHash` | recursive behavior and hash stability |
| Incremental build artifacts | `Artifact`, `BuildCache`, `BuildPipeline` | cache invalidation behavior |

## AssetDatabase Checklist

- Normalize project-relative paths before calling AssetDatabase APIs.
- Use `StartAssetEditing` / `StopAssetEditing` only around bounded batches with guaranteed cleanup.
- Prefer GUIDs for persistent references and paths for immediate operations.
- Avoid synchronous refresh inside frequent UI callbacks.
- Verify whether an API returns null, empty string, or throws for missing assets.

## Importer Checklist

- Get the importer from the asset path and cast only after type checking.
- Apply importer changes through Unity-supported save/reimport flow.
- Avoid editing importer settings during import callbacks unless Unity patterns allow it.
- Record asset path and GUID in LUX logs for reproducibility.

## Incremental Pipeline Checklist

- Use UnityCsReference to understand invalidation and artifact names, not to copy graph code.
- Keep LUX build/cache adapters narrow and version-aware.
- Prefer public `BuildPipeline` and `AssetDatabase` APIs unless a LUX feature explicitly requires deeper inspection.

## LUX Fit

- Use for CodexImage output import, generated asset registration, screenshot asset handling, package skill asset discovery, and project context scans.
- Do not create `.meta` files manually; Unity generates them.
