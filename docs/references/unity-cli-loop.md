# Reference: unity-cli-loop

**Source**: [https://github.com/hatayama/unity-cli-loop](https://github.com/hatayama/unity-cli-loop)

## Relationship
LUX's `AiBridgeEditor` (TCP server, protocol handler, dynamic code execution, input recording/replay) was derived from `unity-cli-loop` (formerly `uLoopMCP`). LUX aims to maintain feature parity with `unity-cli-loop` while extending it into a broader automation gateway.

## Feature Parity Table

LUX must provide equivalent functionality for all 16 core `uloop` skills.

| uloop skill | LUX equivalent | Status |
|---|---|---|
| launch | `lux unity launch` | Implemented |
| compile | `lux compile` | Implemented |
| get-logs | `lux unity get-logs` | Implemented |
| run-tests | `lux run-tests` | Implemented |
| clear-console | `lux unity clear-console` | Implemented |
| focus-window | `lux unity focus-window` | Implemented |
| get-hierarchy | `lux unity get-hierarchy` | Implemented |
| find-game-objects | `lux unity find-game-objects` | Implemented |
| screenshot | `lux unity screenshot` | Implemented |
| simulate-mouse-ui | `lux unity simulate-mouse-ui` | Implemented |
| simulate-mouse-input | `lux unity simulate-mouse-input` | Implemented |
| simulate-keyboard | `lux unity simulate-keyboard` | Implemented |
| record-input | `lux unity record-input` | Implemented |
| replay-input | `lux unity replay-input` | Implemented |
| control-play-mode | `lux unity control-play-mode` | Implemented |
| execute-dynamic-code | `lux unity execute-dynamic-code` | Implemented |

## Architecture Patterns

### 3-Tier Fallback
When executing commands, LUX follows the `uloop` pattern of falling back to more generic execution methods if a specific command is not available:
1. **Dedicated Command**: Use the specific protocol command if it exists.
2. **Execute Dynamic Code**: If no dedicated command exists, attempt to execute the logic via dynamic C# injection.
3. **Batch Mode**: Fallback to Unity batch mode execution for heavy operations like compilation or testing if the Editor is not responsive.

### CLI over TCP
The primary communication channel between the Rust CLI and the Unity Editor is a TCP socket managed by `UnityAiBridgeTcpServer`. This allows for low-latency, bidirectional communication without the overhead of HTTP for simple command execution.

## Unity Freeze Prevention
Key rules inherited from `uloop` to prevent Unity from hanging during AI-driven operations:
- **Avoid Heavy Main Thread Work**: Long-running operations should be offloaded or broken into chunks.
- **EditMode Test Safety**: When running tests in EditMode, ensure they don't trigger infinite loops or modal dialogs that block the main thread.
- **Timeout Handling**: Always implement timeouts on the CLI side when waiting for a TCP response from Unity.

## Design Philosophy
**Minimal set of tools principle**: Provide a small, powerful set of primitive tools (like `execute-dynamic-code`) that can be composed to perform complex tasks, rather than creating a specialized command for every possible Unity operation.
