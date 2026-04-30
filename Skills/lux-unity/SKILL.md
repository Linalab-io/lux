---
name: lux-unity
description: "Lazy-load only when Unity context, Console logs, Lux compile, or Lux test execution is needed in this repo. Uses the repo-local Lux Rust CLI; do not load for ordinary non-Unity edits."
---

# lux-unity

Lazy-load only when Unity context, Console logs, Lux compile, or Lux test execution is needed. Uses the repo-local Lux Rust CLI.

## Command Location
The `lux` binary must be available on PATH or invoked via `cargo run --manifest-path <cargo-path> --`.
Resolve the binary location based on how Lux was installed in this project.

## Usage Guidelines
- Fetch Unity context before making Unity-facing changes: `lux unity context`
- Compile after C# edits: `lux compile`
- Run tests: `lux run-tests --test-platform EditMode|PlayMode`
- Use `--refresh` with `context` sparingly (invokes batch mode).
- Live Editor operations require the Lux AI Bridge backend (Tools > Linalab > Lux > AI Bridge).

## Command References
Detailed command flags and examples are available in the following reference docs:
- [Backend Status & Discovery](./references/backend-status.md) (ping, list-commands)
- [Compile & Tests](./references/compile-tests.md) (compile, run-tests)
- [Console Logs](./references/logs.md) (get-logs, clear-console)
- [Object & Hierarchy](./references/object-hierarchy.md) (find-game-objects, get-hierarchy)
- [Screenshots](./references/screenshots.md) (screenshot, annotations)
- [PlayMode Input](./references/playmode-input.md) (mouse, keyboard, UI Toolkit)
- [Dynamic Code](./references/dynamic-code.md) (C# snippet execution)
- [Record & Replay](./references/record-replay.md) (Input System recording)
- [Launch & Lifecycle](./references/launch.md) (launch, play-mode, context, status)

## Constraints
- **JSON Output**: All commands return JSON; treat as source of truth.
- **PlayMode**: Input and recording commands require PlayMode.
