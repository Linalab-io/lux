# Lux Codex Image

Lux Codex Image is an internal Lux capability that transforms Unity project
context and prompts into structured 2D assets. It provides a data-first node
pipeline for generating images, sprite animations, and rigged character drafts
directly within the Unity Editor.

## Overview

This capability bridges generative AI and Unity-native workflows. By leveraging
the Codex CLI and pluggable segmentation backends, it automates the creation of
layered sprites and draft rigs, significantly reducing the manual effort
required for 2D asset prototyping.

## Architecture

The package is built on a **data-first node pipeline architecture**. This design separates the graph definition from the execution logic, allowing for robust, asynchronous workflows.

1.  **Intermediate Representation (IR)**: A serializable schema (version 0.1) for nodes, ports, and artifacts (images, layers, masks, polygons, bones, etc.).
2.  **Graph Runtime**: An asynchronous execution engine that handles topological sorting, dependency resolution, and artifact flow.
3.  **Pluggable Executors**: Specialized handlers for external services like Codex CLI and SAM-like segmentation backends.

## Requirements

- **Unity**: 6000.0 or newer.
- **Dependencies**: Lux provides the migrated Unity AI Bridge assembly and
  hosts this capability internally.
- **Codex CLI**: Must be installed and authenticated via `codex login`. Ensure the `codex` command is available in your system's PATH.

## Usage

This capability provides two main entry points under the `Tools` menu:

### Basic Generation
**Menu Path**: `Tools > Linalab > Codex Image`
A simple window for quick image generation. It injects Unity project context and invokes the Codex CLI to generate images based on your prompt.

### Node Pipeline
**Menu Path**: `Tools > Linalab > Codex Image Pipeline`
The full node-based workflow. It allows you to define complex pipelines involving prompt templates, segmentation, and multiple export targets. The window provides progress tracking, cancellation, and preview thumbnails for generated artifacts.

## Backends

### Codex CLI
The primary image generation backend. It runs asynchronously to avoid blocking the Unity Editor. It captures generated file paths and integrates them into the pipeline's manifest.

### Segmentation
Supports a SAM-like (Segment Anything Model) contract for isolating character parts.
- **Local/Remote**: Can be configured to use a local Python/GPU service or a remote endpoint.
- **Configuration**: Endpoint URLs and local settings should be configured in user-local settings (e.g., `EditorPrefs`) and are **not** stored in tracked package files.

## Exporters

The pipeline can export to several formats:
- **Static Image**: Standard PNG output.
- **Frame Sequence**: Individual PNG files for each animation frame.
- **Spritesheet**: A single PNG with Unity-native sprite slicing metadata.
- **AnimationClip**: Generates Unity `.anim` files that animate `SpriteRenderer` references.
- **Spine 4.2**: Generates `.json.txt` skeleton and `.atlas.txt` files compatible with Spine 4.2.
- **Unity 2D**: Generates a draft prefab with a `SpriteSkin` hierarchy.

## Optional Packages

The following packages are detected at runtime to enable enhanced export features like **Unity 2D Animation**:
- `com.unity.2d.animation`
- `com.unity.2d.psdimporter`

If these packages are missing, the Unity 2D exporter will fall back to a **layered PNG + JSON handoff** workflow.

## Limitations

- **Draft Rig / Auto Rig Attempt**: All Spine and Unity 2D outputs are intended as a **Draft Rig / Auto Rig Attempt**. They provide a baseline hierarchy and attachments but require manual refinement (weights, constraints, animation polish) for production use.
- **No PSB Binary**: Binary PSB file generation is not implemented in this version. Layered assets are handled via PNGs and JSON metadata.
- **Unity Batchmode**: Execution in batchmode may be blocked if another Unity instance already has the project open.

## Troubleshooting

- **Codex CLI**: If generation fails, verify `codex` is in your system PATH and you are logged in via `codex login`.
- **Unity Batchmode**: If the pipeline fails to start in batchmode, ensure no other Unity instance is locking the project folder.
- **Optional Packages**: If Unity 2D export is missing features, ensure `com.unity.2d.animation` is installed via Package Manager.
- **Segmentation**: If segmentation fails, check your local/remote endpoint connectivity in your local settings.

## Security and Secrets

This package follows a strict **no-secret policy** regarding **secrets**:
- **No API Keys**: Never store API keys, tokens, or passwords in tracked assets or package files.
- **Local Config**: Use local configuration paths for sensitive endpoint URLs.
- **No Placeholders**: This documentation and the package source contain no fake API-key or token placeholders.


## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
