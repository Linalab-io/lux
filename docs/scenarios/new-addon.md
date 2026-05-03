# Scenario: New Addon

## Trigger
Load this document when creating a new addon package for LUX.

## Context
Addons are modular extensions to LUX. They reside in the `Addons/` directory and are discovered dynamically by the system.

## Checklist

### 1. Manifest Creation
- Create an `addon.json` in the root of your addon folder.
- Include `name`, `version`, `description`, and any `dependencies`.

### 2. Assembly Definition (asmdef)
- Use a unique namespace for your addon.
- Add `defineConstraints` to your `.asmdef` file if the addon depends on external packages (e.g., `com.unity.webrtc`).
- Use `#if` preprocessor guards in your code to handle missing dependencies gracefully.

### 3. Directory Structure
- Keep all addon-specific assets, scripts, and resources within the addon's folder.
- Follow the standard Unity folder structure (`Editor/`, `Runtime/`, `Tests/`).

### 4. Discovery
- Verify that the addon appears in `lux addon list` and the `LuxAddonManagerWindow`.

## Cross-references
- `references/gpm-unity.md`: For service package patterns.
- `scenarios/addon-management.md`: For management logic.
