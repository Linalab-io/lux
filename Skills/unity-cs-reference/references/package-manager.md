# Package Manager

Use this file when LUX needs Unity Package Manager behavior, PackageInfo metadata, package resolution, manifest interactions, or Package Manager UI patterns.

## Primary Paths

| Path | Use for |
|---|---|
| `Modules/PackageManager/Editor/Managed/` | Package Manager Editor API, UI, client, package metadata |
| `Modules/PackageManager/Editor/Managed/UI/` | Package Manager window and UI Toolkit patterns |
| `Modules/PackageManager/Editor/Managed/Services/` | package services, registry, resolution behavior |
| `Modules/PackageManager/Editor/Managed/PackageInfo.cs` | package metadata shape where present |
| `Editor/Mono/PackageManager/` | Editor package manager bindings where present |

## Lookup Matrix

| Need | Search for | Confirm |
|---|---|---|
| Inspect installed package | `PackageInfo`, `FindForAssetPath`, `FindForAssembly` | null behavior and path rules |
| Add/remove package | `Client.Add`, `Client.Remove`, `Request` | async polling and error shape |
| List packages | `Client.List` | offline/cache flags and embedded packages |
| Resolve manifest data | `manifest`, `PackageManager` | dependency source and version fields |
| Package Manager UI pattern | `PackageManagerWindow`, `PackageDetails` | UI Toolkit usage and service boundaries |
| Registry behavior | `Registry`, `ScopedRegistry` | source and authentication assumptions |

## Request Checklist

- Treat package operations as asynchronous request objects.
- Poll or subscribe according to Unity-supported patterns.
- Check request status and error before reading results.
- Avoid blocking Editor UI while waiting for package operations.
- Record package name, version, source, and request error in LUX logs.

## PackageInfo Checklist

- Use package metadata to identify whether LUX runs as embedded, local, registry, or git package.
- Resolve asset paths before calling `FindForAssetPath`.
- Avoid assuming package folder is under `Packages/` only; local packages may resolve elsewhere.
- Confirm assembly-to-package mapping for generated diagnostics.

## LUX Fit

- Use for Lux skill installation context, package self-discovery, dependency diagnostics, and Package Manager-integrated UI.
- Do not duplicate Package Manager UI implementation; build LUX-specific surfaces.
