# Scenario: Addon Management

## Trigger
Load this document when modifying the logic or UI for installing, removing, or updating LUX addons.

## Context
LUX uses an addon architecture to keep the core package lightweight. Addons are optional features (like WebRTC or Git integration) that can be managed via the `lux addon` CLI or the `LuxAddonManagerWindow` in Unity.

## Checklist

### 1. Manifest Verification
- Ensure `addon.json` contains `name`, `version`, `description`, and `dependencies`.
- Verify that dependencies are checked before installation.

### 2. Directory Structure
- Addons must be placed in the `Addons/` directory.
- Each addon should have its own folder with a unique name.

### 3. UI Consistency
- Follow the patterns in `references/gpm-unity.md`.
- Ensure the `LuxAddonManagerWindow` correctly reflects the state returned by `lux addon list`.
- Provide visual feedback (progress bars or status text) during install/remove operations.

### 4. CLI Integration
- Verify that the Rust CLI commands (`install`, `remove`, `list`) handle file operations atomically.
- Ensure the CLI returns structured JSON for the Unity UI to consume.

## Cross-references
- `references/gpm-unity.md`: For UI and discovery patterns.
- `AGENTS.md`: For general codebase structure.
