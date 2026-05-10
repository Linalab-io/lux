# LUX

**LUX** = **L**inalab **U**nity **X** — 로컬 퍼스트 유니티 에디터 AI 자동화 툴킷.

LUX는 AI 코딩 도구(Claude Code, OpenAI Codex, OpenCode)와 Unity Editor를 연결하는
**로컬 전용** 패키지입니다. 원격 스트리밍 없이, 에디터 내에서 직접 동작합니다.

> **"Local-first"** — 모든 기능은 `localhost`에서 작동합니다. WebRTC/원격 접속은 지원하지 않습니다.

## 핵심 기능

### 🤖 AI 코딩 도구 연동
- **멀티 AI 터미널** — Claude Code, OpenAI Codex, OpenCode를 웹 대시보드에서 전환하며 사용
- **스킬 디스패치** — compile, test, screenshot, logs, playmode 등을 어떤 AI 도구에서든 일관되게 호출
- **도구 실행 API** — `/api/tools/execute` + WebSocket 이벤트 브로드캐스팅
- **AI 이벤트 로깅** — 구조화된 JSONL 이벤트 로그 (22+ redaction 패턴)

### 🎨 CodexImage 파이프라인
- **노드 기반 이미지 생성** — 6가지 노드 타입 (UnityContext, PromptTemplate, CodexGeneration, Segmentation, MaskPostProcessing, OutputDirectory)
- **다중 익스포터** — Unity 2D Animation, Spine draft rig, sprite sheet
- **비주얼 에디터** — ReactFlow 노드 그래프 에디터 (Phase D 개발 중)

### 🔧 Unity Editor 통합
- **Lux Workbench** — LUX 메인 컨트롤 윈도우
- **AI Bridge TCP 서버** — 외부 터미널/클라이언트 프로토콜 핸들러
- **Unity Git** — status, staging, history, branches, submodules
- **자동화 가드레일** — 명령어 블랙리스트, 감사 로그, 승인 상태
- **서버 상태 인디케이터** — 게이트웨이 서버 상태 + heartbeat keep-alive

### 🖥️ Rust 게이트웨이 & CLI
- **웹 서버** — Axum HTTP/WebSocket 게이트웨이 (React SPA + REST API)
- **CLI 명령어** — serve, unity, skill, ai-log, compile, session, mcp, config, screenshot
- **스킬 시스템** — 번들 코어 스킬 + 설치 가능한 외부 스킬 (`lux skill install/remove/update`)
- **서버 라이프사이클** — idle timeout + graceful shutdown; Unity heartbeat로 유지

### 📊 로컬 웹 대시보드 (Phase C)
- **Compile Panel** — 배치 컴파일 실행 및 결과 확인
- **Test Panel** — EditMode/PlayMode 테스트 실행 및 결과 보기
- **Log Panel** — AI 이벤트 로그 조회 (필터링 지원)
- **Skill Panel** — 설치된 스킬 관리
- **Project Panel** — Unity 프로젝트 상태 및 컨텍스트
- **Dashboard Overview** — 통합 상태 카드 그리드

## 로드맵

| Phase | 이름 | 상태 | 설명 |
|-------|------|------|------|
| **A** | Core Editor Adapter | ✅ 완료 | LuxEditor, AiBridge, UnityGit, CodexImage, Rust CLI |
| **B** | AI Event System | ✅ 완료 | 이벤트 로깅, JSONL, Redaction, Sessions API, Static Serving |
| **C** | Local Web Dashboard | 🔄 진행 중 | React SPA 로컬 대시보드 (compile/test/log/skill) |
| **D** | CodexImage Visual Editor | ⏳ 계획 | ReactFlow 노드 기반 파이프라인 비주얼 에디터 |
| **E** | Multi-AI Skill Dispatch | ⏳ 계획 | Claude/Codex/OpenCode 통합 스킬 디스패치 |

### Out of Scope
- ❌ WebRTC / RTC Terminal / 원격 비디오 스트리밍
- ❌ 브라우저에서 Unity Editor 원격 제어
- ❌ iOS companion app / PWA
- ❌ Windows/Linux 에디터 지원 (macOS-first)

## 빠른 시작

### 사전 요구사항
- **Unity 6000.0+** (Unity 6.x)
- **Rust toolchain** (`rustup` + `cargo`)
- **Node.js 18+** (웹 UI 개발 시)
- **macOS** (Linux/Windows 경로는 준비됐으나 미테스트)

### 설치

```bash
# 1. LUX 패키지를 Unity 프로젝트의 Packages/에 추가
# (git dependency 또는 local path)

# 2. Rust CLI 빌드 & 설치
cd Packages/com.linalab.lux/RustGateway~
cargo build --release
cargo run -- install    # 전역 설치

# 3. 게이트웨이 시작
lux serve --token my-secret-token

# 4. Unity Editor에서 Window > Linalab > Lux Workbench 열기
```

### CLI 참조

```bash
# 서버
lux serve --token <TOKEN> [--host 127.0.0.1] [--port 17340] [--idle-timeout 30]

# Unity 제어
lux unity status                    # 에디터 연결 상태
lux unity context                   # 공유 컨텍스트 파일 읽기
lux unity get-logs                  # 콘솔 로그
lux unity screenshot                # 에디터 스크린샷
lux unity launch                    # Unity 에디터 실행
lux unity control-play-mode         # Play/Pause/Stop
lux unity execute-dynamic-code      # C# 코드 실행
lux unity get-hierarchy             # Hierarchy 메타데이터

# 스킬 관리
lux skill list                      # 설치된 스킬 목록
lux skill list --json               # JSON 출력
lux skill info <name>               # 스킬 상세 정보
lux skill install <name> --source <path|url>
lux skill remove <name>
lux skill update <name>

# AI 이벤트 로그
lux ai-log                          # 이벤트 로그 조회
lux ai-log --json                   # 머신 리더블 출력

# 빌드 & 테스트
lux compile [--project-path <path>] # 배치 컴파일
lux run-tests [--project-path <path>]  # 테스트 실행
lux run-tests --playmode-platform   # PlayMode 테스트

# 세션 관리
lux session list                    # 활성 세션 목록
lux session create --name "work"    # 새 세션 생성

# 설정
lux config                          # 현재 설정 확인
```

## API Reference

### Health & Lifecycle
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/health` | Server readiness |
| GET | `/api/health` | Uptime & status |
| POST | `/api/heartbeat` | Reset idle timer |

### Sessions
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET/POST | `/api/sessions` | List/create sessions |
| GET | `/api/sessions/:id` | Get session detail |
| PUT | `/api/sessions/:id` | Update session |
| DELETE | `/api/sessions/:id` | Delete session |

### Graphs (CodexImage Pipeline)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET/POST | `/api/graphs` | List/create graphs |
| GET/PUT/DELETE | `/api/graphs/:id` | Graph CRUD |
| POST | `/api/graphs/:id/execute` | Execute graph |

### Tools (Multi-AI)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/tools` | Available tools |
| GET/POST/DELETE | `/api/tools/sessions` | Tool sessions |
| POST | `/api/tools/execute` | Execute command/skill |
| GET | `/api/tools/executions/:id` | Execution status |

### AI Events
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/ai-log` | Query event log |
| WS | `/events` | Real-time event stream |

### Config & Schema
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/schema` | Example event envelope |
| GET | `/api/node-types` | Node type registry |
| GET | `/ui/*` | Static SPA files |

## Unity Editor 메뉴

### Window
| Menu Path | Description |
|-----------|-------------|
| `Window > Linalab > Lux Workbench` | LUX 메인 컨트롤 윈도우 |
| `Window > Linalab > Lux > Unity Git` | Git status, staging, diff |
| `Window > Linalab > Lux > Git History Graph` | 시각화 Git 히스토리 |

### Tools (주요)
| Menu Path | Description |
|-----------|-------------|
| `Tools > Linalab > Lux > Server Status` | 게이트웨이 상태 |
| `Tools > Linalab > Lux > AI Bridge > Export Unity Context` | AI 툴용 컨텍스트 export |
| `Tools > Linalab > Lux > Rust CLI > Install or Update Global Tool` | lux CLI 전역 설치 |
| `Tools > Linalab > Lux > Toggle Auto-Compile Watch` | 컴파일 와처 토글 |
| `Tools > Linalab > Lux > Batch > Compile (Dry Run)` | 배치 컴파일 (dry-run) |
| `Tools > Linalab > Codex Image` | CodexImage 윈도우 |
| `Tools > Linalab > Codex Image Pipeline` | 비주얼 파이프라인 에디터 |

## 프로젝트 구조

```
com.linalab.lux/
├── LuxEditor/              # C# 에디터 어댑터 (LuxAutomationGateway 등)
│   ├── LuxAutomationGateway.cs       # 자동화 코디네이터 (partial ~10파일)
│   ├── LuxWorkbenchWindow.cs         # 메인 워크벤치
│   ├── LuxAIToolDispatcher.cs        # AI 툴 실행 브리지
│   ├── LuxServerStatusIndicator.cs   # 서버 상태 UI
│   └── LuxRuntimeEvent*.cs           # 런타임 이벤트 채널
├── AiBridgeEditor/         # TCP 서버 + 프로토콜 (~1,900줄)
├── UnityGitEditor/         # Git 통합 (status/staging/history/branches)
├── CodexImage/             # 이미지 생성 파이프라인
│   ├── Editor/Pipeline/    # 파이프라인 엔진 + 웹 브리지
│   ├── Editor/Backends/    # Codex CLI / Segmentation 백엔드
│   └── Editor/Exporters/   # Unity 2D, Spine, sprite sheet
├── RustGateway~/           # Rust HTTP/WS 게이트웨이 + CLI
│   ├── src/main.rs         # CLI 엔트리 (serve/unity/skill/ai-log/...)
│   ├── src/server.rs       # Axum 서버 (health/schema/events/sessions/graphs)
│   ├── src/ai_log.rs       # AI 이벤트 로깅 (EventSource enum, JSONL, redaction)
│   ├── src/skill_adapter/  # 스킬 어댑터 (5모듈: adaptation/compatibility/metadata/slimming)
│   ├── src/protocol.rs     # 이벤트 envelope 스키마
│   ├── tests/              # 10개 smoke test 파일 (286/289 pass)
│   └── ui-src/             # React 19 + TypeScript SPA (local dashboard)
├── McpHelper~/             # Node.js MCP 헬퍼
├── Skills/lux-unity/       # 코어 AI 스킬 (manifest + SKILL.md + references)
├── *Tests/                 # C# (NUnit) + Rust (cargo test) 테스트 스위트
└── seeds/                  # Seed specification 파일
```

## 테스트 커버리지

| Suite | Tests | Status |
|-------|-------|--------|
| Rust unit tests (server.rs) | 134 | ✅ All pass |
| Rust event_schema | 24 | ✅ All pass |
| Rust cli_api_contract | 40 | ✅ All pass |
| Rust event_schema_smoke | 24 | ✅ All pass |
| Rust gateway_cli_smoke | 52/55 | ⚠️ 3 pre-existing |
| Rust sessions_api_smoke | 8 | ✅ New (P7) |
| Rust static_serving_smoke | 4 | ✅ New (P7) |
| Rust redact_patterns_smoke | 22 | ✅ New (P7) |
| C# test files | ~27 | Across *Tests/Editor/ |
| **Total** | **~335** | **~98.9% pass** |

## 기술 스택

| 영역 | 기술 |
|------|------|
| Gateway | Rust, Axum 0.7, tokio 1, serde, tower-http 0.6 |
| CLI | Rust, clap 4.5, anyhow |
| Web UI | React 19, TypeScript strict, ReactFlow, Vite, Zustand |
| Editor C# | Unity 6000.0+, UIToolkit, NUnit, Newtonsoft.Json |
| Desktop (optional) | Tauri v2 (Rust + system tray) |
| Protocol | JSON envelopes over HTTP/WebSocket/TCP |

## 컨벤션

### Rust (`RustGateway~/`)
- Axum 0.7, tokio 1, clap 4.5, anyhow, serde
- `anyhow` for logic errors, `eprintln` for user output
- **No TODO/FIXME/HACK comments**
- New endpoints → always add matching tests

### TypeScript (`ui-src/`)
- React 19 functional components + hooks, strict mode
- No mock/fallback data in API hooks
- State: useState, useRef, useCallback, useEffect

### C# (Editor)
- Namespace: `UnityEditor`, Assembly: `Linalab.LuxEditor`
- Class prefix: `Lux`, large files use partial classes
- Tests: NUnit `[Test]` in `*Tests/Editor/`

## Acknowledgments

LUX was heavily inspired by [**unity-cli-loop**](https://github.com/hatayama/unity-cli-loop) by [hatayama](https://github.com/hatayama) (formerly uLoopMCP, 346⭐).

The AI Bridge module (`AiBridgeEditor/`), including the TCP server, protocol handler,
dynamic code execution, input recording/replay, and skill/reference structure,
was derived from unity-cli-loop.

## License

[MIT License](LICENSE)
