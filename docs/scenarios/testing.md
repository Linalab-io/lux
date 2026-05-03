# Scenario: Testing

## Trigger
Load this document when writing or modifying tests (Rust smoke tests or C# unit tests).

## Context
LUX maintains a high bar for stability through a combination of C# unit tests and Rust-based CLI smoke tests.

## Checklist

### 1. Rust Smoke Tests
- Located in `RustGateway~/tests/gateway_cli_smoke.rs`.
- Use the `tokio::test` attribute.
- Ensure tests are independent and clean up any temporary files.
- **Naming**: Use descriptive names like `test_unity_screenshot_success`.

### 2. C# Unit Tests
- Located in `*Tests/Editor/` directories.
- Use NUnit `[Test]` and `[UnityTest]` attributes.
- Follow the `Lux` prefix convention for test classes.

### 3. Unity Freeze Prevention
- Refer to `references/unity-cli-loop.md` for rules on preventing Unity hangs during tests.
- Avoid `Thread.Sleep` in the main thread; use `yield return null` in `UnityTest` instead.

### 4. Verification
- Run `cargo test` in `RustGateway~` before committing.
- Check LSP diagnostics for C# test files.

## Cross-references
- `references/unity-cli-loop.md`: For freeze prevention rules.
- `AGENTS.md`: For verification commands.
