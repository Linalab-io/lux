# Scenario: New CLI Command

## Trigger
Load this document when adding a new `lux` CLI command in the Rust gateway.

## Context
The `lux` CLI is the primary entry point for automation and AI tool integration. It is implemented in Rust within the `RustGateway~/` directory.

## Checklist

### 1. Argument Parsing
- Add the new command to the `Cli` enum in `RustGateway~/src/main.rs` using `clap` attributes.
- Follow the existing command grouping (e.g., `unity`, `skill`, `addon`).

### 2. Execution Mode
- Determine if the command should run in **TCP Mode** (communicating with a running Editor) or **Batch Mode** (launching Unity in the background).
- Use the `send_unity_command` helper for TCP communication.

### 3. Error Handling
- Use `anyhow::Result` for error propagation.
- Provide clear, actionable error messages to the user via `eprintln!`.

### 4. Smoke Testing
- Every new CLI command **must** have a corresponding smoke test in `RustGateway~/tests/gateway_cli_smoke.rs`.
- Verify both success and failure cases (e.g., command fails when Editor is not running).

### 5. Feature Parity
- Check `references/unity-cli-loop.md` to see if this command fulfills a parity requirement.

## Cross-references
- `references/unity-cli-loop.md`: For feature parity check.
- `AGENTS.md`: For Rust coding conventions.
