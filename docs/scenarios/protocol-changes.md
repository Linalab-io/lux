# Scenario: Protocol Changes

## Trigger
Load this document when modifying the AI Bridge protocol, adding/removing commands, or changing the TCP message format.

## Context
The AI Bridge is the core communication layer between external tools and the Unity Editor. It uses a custom TCP protocol handled by `UnityAiBridgeTcpServer` and `UnityAiBridgeProtocol`.

## Checklist

### 1. Command Registration
- New commands must be registered in `LuxAiBridgeProtocolRegistration`.
- Ensure the command name is unique and follows the existing naming convention.

### 2. Constants and Schema
- Update protocol constants if the message envelope or header format changes.
- Maintain backward compatibility where possible.

### 3. 3-Tier Fallback
- If adding a new feature, consider if it should be a dedicated command or if it can be handled by `execute-dynamic-code`.
- Refer to `references/unity-cli-loop.md` for the fallback philosophy.

### 4. Registry Rebuild
- After adding a command, trigger `Tools > Linalab > Lux > AI Bridge > Rebuild Command Registry` to ensure the Editor recognizes the change.

### 5. Rust CLI Update
- Ensure the `lux unity` commands in `RustGateway~/src/main.rs` are updated to match the new protocol capabilities.

## Cross-references
- `references/unity-cli-loop.md`: For architecture patterns and feature parity.
