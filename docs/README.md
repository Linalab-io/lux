# LUX Documentation

This directory contains situational reference documentation for the LUX Unity package. These documents are designed to be loaded by AI agents based on their current task to ensure consistency with the project's architecture, patterns, and external references.

## Situational Loading Guide

When you are performing a specific task, load the corresponding reference and scenario documents to get the necessary context and guidelines.

| Agent is about to... | Load this reference |
|---|---|
| Modify AI Bridge protocol or commands | `references/unity-cli-loop.md` + `scenarios/protocol-changes.md` |
| Add/modify addon management UI or logic | `references/gpm-unity.md` + `scenarios/addon-management.md` |
| Add a new CLI command in Rust | `references/unity-cli-loop.md` + `scenarios/new-cli-command.md` |
| Create a new addon package | `references/gpm-unity.md` + `scenarios/new-addon.md` |
| Write or modify tests | `references/unity-cli-loop.md` + `scenarios/testing.md` |

## Directory Structure

- `references/`: Deep dives into external projects and patterns that LUX follows.
- `scenarios/`: Actionable checklists and guidelines for specific development tasks.
