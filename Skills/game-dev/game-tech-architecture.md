---
name: game-tech-architecture
description: "게임 기술 아키텍처 스킬 — 엔진 선택(Unity/Unreal/Godot/Custom), 서버 아키텍처, 확장성 패턴, 네트워킹 모델, 클라이언트-서버 구조, LDP 준수 기술적 고려사항. When to Use, Prerequisites, Procedure, Pitfalls, Verification checklist 포함."
version: 1.0.0
author: Linalab
license: MIT
tags: [game-dev, ldp]
related_skills: [lina-decision-protocol]
metadata:
  hermes:
    tags: [game-dev, ldp, tech-architecture, engine-selection, server-architecture, scalability, networking, game-backend]
    related_skills: [lina-decision-protocol]
---

# 게임 기술 아키텍처 (Game Tech Architecture)

> **핵심 문장:**
> "기술은 게임 디자인을 뒷받침하는 도구다. 엔진 선택은 '무엇이 멋있는가'가 아니라 '우리 게임에 무엇이 필요한가'로 결정한다."

이 스킬은 게임 개발의 **엔진 선택, 서버 아키텍처, 확장성 패턴, 네트워킹 모델**을 다룹니다. 특히 프로젝트의 규모, 팀 역량, 타겟 플랫폼, 그리고 **LDP 윤리 기준**(개인정보 보호, 투명한 데이터 처리)을 기술적 의사결정에 반영합니다.

## When to Use

**Load this skill when:**
- 게임 엔진을 선택/검토할 때 (Unity vs Unreal vs Godot vs Custom)
- 서버 아키텍처를 설계할 때 (클라이언트-서버, P2P, Hybrid)
- 확장성 전략을 수립할 때 (오토스케일링, 샤딩, 리전 분산)
- 네트워킹 모델을 결정할 때 (Real-time, Turn-based, Async)
- 백엔드 인프라(BaaS vs Self-hosted)를 평가할 때
- 기술 스택 마이그레이션을 고려할 때

**Don't use for:**
- 메커니즘/밸런스 설계 (`game-mechanics-design` 참조)
- 수익화 전략 (`game-monetization-strategy` 참조)
- UI/UX 디자인 (`game-aesthetics-art-pipeline` 참조)

## Prerequisites

1. **게임의 GDD(Game Design Document) 초안**이 있어야 함 (최소 장르, 규모, 타겟 플랫폼 정의)
2. **팀 기술 역량**: 사용 가능한 언어(C#/C++/GDScript/Rust), 경험 있는 엔진
3. **예산 & 타임라인**: 초기 예산, 목표 출시일, 운영 비용 상한선
4. **타겟 동시 접속자(CCU) 예상**: DAU/MAU 추정치
5. **LDP 데이터 처리 가이드라인** 이해 (개인정보 수집 최소화 원칙)

## Procedure

### 1단계: 엔진 선택 (Engine Selection)

#### 1.1 주요 엔진 비교 매트릭스

| 평가 항목 | Unity | Unreal Engine | Godot | Custom |
|-----------|-------|--------------|-------|--------|
| **언어** | C# (IL2CPP) | C++ | GDScript/C#/C++ | 자유 |
| **학습 곡선** | 낮음 | 중~높음 | 낮음 | 매우 높음 |
| **2D 지원** | ★★★★★ | ★★★☆☆ | ★★★★★ | ★★★★☆ |
| **3D/고사양** | ★★★★☆ | ★★★★★ | ★★★☆☆ | ★★★★★ |
| **모바일 최적화** | ★★★★★ | ★★★☆☆ | ★★★★☆ | ★★★★☆ |
| **콘솔 지원** | 전 플랫폼 | 전 플랫폼 | 제한적 | 직접 개발 필요 |
| **소스 코드 접근** | 유료(Pro) | 완전 공개 | 완전 공개(MIT) | 100% |
| **로열티** | 없음(구매형) | 5%(100만$ 이상) | 없음(MIT) | 없음 |
| **에셋 스토어** | 거대함 | 큼 | 성장 중 | 없음 |
| **커뮤니티** | 가장 큼 | 큼 | 활발히 성장 | 없음 |
| **적합 팀 규모** | 1~500+ | 10~500+ | 1~50 | 5~50(전문가) |

#### 1.2 의사결정 트리 (Decision Tree)

```
시작
├── 2D 게임인가?
│   ├── 예 → Godot 또는 Unity (Pixel Art/Hand-drawn → Godot 우선)
│   └── 아니오 → 3D로 진행
│       ├── 하이엔드 그래픽 필요? (Photorealism/VFX Heavy)
│       │   ├── 예 → Unreal Engine
│       │   └── 아니오 (Stylized/Low-poly/Mobile)
│       │       ├── 모바일 메인?
│       │       │   ├── 예 → Unity
│       │       │   └── 아니오 → Unreal 또는 Unity
│       └── 온라인 멀티플레이어?
│           ├── 대규모 MMO (CCU 1000+) → Unity + 커스텀 백엔드
│           ├── 소규멀티 (CCU < 100) → Unity Mirror/Netcode for GameObjects
│           └── 싱글/Async → 어떤 엔진이든 무방
```

#### 1.3 Custom Engine을 선택해야 하는 경우

**Custom Engine이 정당화되는 조건 (모두 충족 시)**:
- [ ] 팀에 엔진/그래픽스 전문가가 2명 이상 있음
- [ ] 기존 엔진으로 해결할 수 없는 **기술적 차별점**이 명확함
- [ ] 12개월 이상의 개발 기간 허용됨
- [ ] Long-term 유지보수 역량 확보됨
- [ ] 엔진 개발 자체가 프로젝트 목표의 일부가 아님 (엔진 만들기 ≠ 게임 만들기)

### 2단계: 네트워킹 모델 선택 (Networking Model)

#### 2.1 네트워킹 아키텍처 유형

| 모델 | 특징 | 적합 장르 | Latency 요구 | 복잡도 |
|------|------|----------|-------------|--------|
| **Client-Authoritative** | 서버가 모든 상태 관리 | MMO, RPG, 경쟁 게임 | 100~300ms | 높음 |
| **Client-Predicted** | 클라이언트가 예측 + 서버 보정 | FPS, RTS, 액션 | <100ms | 매우 높음 |
| **P2P (Peer-to-Peer)** | 호스트 클라이언트가 권한 보유 | 소규멀티(2~8인), 캐주얼 | 50~200ms | 낮음 |
| **Relay Server** | P2P + 서버 중계(NAT traversal) | 콘솔 멀티, Switch | 50~150ms | 중간 |
| **Turn-based / Async** | 서버 저장 + 폴링 | 퍼즐, 보드, 4X | 1s~1h | 낮음 |
| **State Synchronization** | 프레임 단위 상태 동기화 | 파이팅, 스포츠 | <16ms(60fps) | 매우 높음 |

#### 2.2 네트워킹 토폴로지 선택 가이드

```
CCU(동시접속) 기준:
├── CCU < 100:    P2P 또는 Relay (비용 최소화)
├── CCU 100~1000: Single Server (수직 스케일)
├── CCU 1K~10K:   Cluster (Load Balancer + Game Servers)
├── CCU 10K~100K: Sharded (월드 분할/채널)
└── CCU 100K+:    Distributed (Region-based + Edge Computing)
```

#### 2.3 핵심 네트워킹 기술 고려사항

**Latency 숨기기 기법 (Latency Hiding Techniques)**:
1. **Client-Side Prediction**: 클라이언트가 즉시 반영, 서버 결과로 보정 (FPS 필수)
2. **Interpolation**: 다른 플레이어 위치를 부드럽게 보간
3. **Lag Compensation**: 서버가 과거 시점으로 되돌려 hit 검사 (경쟁 게임 필수)
4. **Entity Interpolation Buffer**: 100~200ms 버퍼로 jitter 흡수

**보안 고려사항**:
```
⚠️ 절대 클라이언트를 신Trust하지 말 것 (Never Trust the Client):
- 위치/속도/체력은 서버에서 Authoritative하게 관리
- 클라이언트는 "의도(Intent)"만 전송, 서버가 "결과(Result)"를 결정
- Speed Hack / Wall Hack / Aim Bot 방지를 위한 Server-side Validation
- Anti-Cheat: Easy Anti-Cheat, BattlEye, 또는 자체 heuristic
```

### 3단계: 서버 아키텍처 설계 (Server Architecture)

#### 3.1 참조 아키텍처 (Reference Architecture)

```
┌─────────────────────────────────────────────────────┐
│                    Client Layer                      │
│         (Mobile / PC / Console / Web)                │
└──────────────────────┬──────────────────────────────┘
                       │ HTTPS / WebSocket / UDP
                       ▼
┌─────────────────────────────────────────────────────┐
│                  Edge / CDN Layer                    │
│     CloudFlare / AWS CloudFront / 자체 Edge          │
│     (DDoS Protection, SSL Termination, Caching)      │
└──────────────────────┬──────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────┐
│               API Gateway / Load Balancer            │
│        (AWS ALB / NGINX / Kong / Envoy)              │
│     Rate Limiting, Auth, Routing, Logging            │
└──────┬───────────┬───────────┬───────────────────────┘
      │           │           │
      ▼           ▼           ▼
┌──────────┐ ┌──────────┐ ┌──────────────────────┐
│  Game    │ │  Social  │ │    Backend Services   │
│  Server  │ │  Service │ │  (Auth, Matchmaking,  │
│  (Room-  │ │ (Chat,   │ │   Leaderboard,        │
│   based) │ │  Guild,  │ │   Inventory, Payment) │
│          │ │  Friend) │ │                       │
└────┬─────┘ └────┬─────┘ └──────────┬─────────────┘
     │            │                 │
     ▼            ▼                 ▼
┌─────────────────────────────────────────────────────┐
│                   Data Layer                        │
│   PostgreSQL (관계형) + Redis (캐시/세션)             │
│   + MongoDB (로그/이벤트) + S3 (에셋/저장)           │
└─────────────────────────────────────────────────────┘
```

#### 3.2 BaaS(Backend-as-a-Service) vs Self-Hosted

| 항목 | BaaS (PlayFab, Firebase, Supabase) | Self-Hosted |
|------|-----------------------------------|-------------|
| **초기 개발 속도** | 빠름 (주~개월) | 느림 (개월~년) |
| **운영 복잡도** | 낮음 | 높음 (DevOps 팀 필요) |
| **비용 구조** | DAU 비례 (초기 저렴, 규모 커지면 비쌈) | 인프라 고정비 + 변동비 |
| **커스터마이징** | 제한적 | 무제한 |
| **데이터 소유권** | 제한적 / 이식 어려움 | 100% |
| **LDP 준수 용이성** | 제공업체 의존 ( auditor 필요) | 직접 통제 가능 |
| **추천** | 프로토타입 / 소규모 / 빠른 검증 | 서비스형 / 대규모 / 장기 운영 |

**BaaS 선택 가이드**:
- **Firebase**: 실시간 DB, Auth, Push — 모바일/캐주얼, 빠른 MVP
- **PlayFab (Azure)**: LiveOps, Economy, Matchmaking — 서비스형 게임
- **Supabase**: PostgreSQL 기반, 오픈소스 — 중규모, 데이터 통제 중요
- **AWS GameLift**: 전용 게임 서버 호스팅 — 대규모 멀티플레이어

### 4단계: 확장성 패턴 (Scalability Patterns)

#### 4.1 수직 vs 수평 확장

| | Vertical Scale-Out | Horizontal Scale-Out |
|--|-------------------|---------------------|
| **방법** | 더 강한 서버 (CPU/RAM up) | 더 많은 서버 (인스턴스 추가) |
| **장점** | 간단, 데이터 일관성 용이 | 이론적 무한 확장, 비용 효율 |
| **단계** | 한계存在 (하드웨어 상한) | 복잡도 급증, 분산 트랜잭션 |
| **적합** | 초기 ~ CCU 1K | CCU 1K+ |

#### 4.2 확장성 패턴 카탈로그

**패턴 1: Game Server Autoscaling (게임 서버 오토스케일링)**
```yaml
# Conceptual config
autoscaling:
  metric: "room_count_per_server"  # 서버 당 방 수
  target: 20                       # 목표: 서버 당 20방
  min_instances: 2
  max_instances: 100
  scale_up_threshold: 25           # 25방 초과 시 추가
  scale_down_threshold: 10         # 10방 미만 시 축소
  cooldown: 300s                   # 5분 쿨다운
```

**패턴 2: Sharding (샤딩/월드 분할)**
```
World Shard 1 (Asia-East):  Players A-M, CCU ~30K
World Shard 2 (Asia-West):  Players N-Z, CCU ~25K
World Shard 3 (NA-East):    All NA players, CCU ~20K

⚠️ 샤드 간 이동 제한 or 비용 발생 → 게임 디자인 영향
```

**패턴 3: Interest Management (관심 영역 관리)**
- **AOI (Area of Interest)**: 플레이어 주변 Nm 내 엔티티만 동기화
- **Spatial Partitioning**: Quadtree / Grid / BVH로 공간 분할
- **LOD (Level of Detail)**: 거리에 따라 동기화 주기/정밀도 조절
- **효과**: 대역폭 50~80% 절감 가능

**패턴 4: Event-Driven Architecture (이벤트 기반 아키텍처)**
```
Game Event → Message Queue (Kafka/RabbitMQ) → Consumer Services
                                                    ├── Analytics Service
                                                    ├── Achievement Service
                                                    ├── Anti-Cheat Service
                                                    └── Notification Service
```
- **장점**: 결합도 감소, 독립적 확장, 내고장성
- **단점**: 복잡도 증가, 디버깅 어려움, eventual consistency

**패턴 5: Caching Strategy (캐싱 전략)**
```
Cache Layers:
L1: In-memory (Game Server process) — 핫 데이터 (현재 방 정보)
L2: Redis Cluster — 웜 데이터 (플레이어 인벤토리, 친구 목록)
L3: CDN — 콜드 데이터 (에셋, 정적 리소스)

Invalidation: Write-Through (쓰기 시 캐시 갱신) or TTL-based
```

### 5단계: 데이터 아키텍처 & LDP 준수 (Data Architecture & LDP Compliance)

#### 5.1 데이터 수집 원칙 (LDP §3 Data Minimization)

```
✅ 수집 OK:
- 게임플레이 필수 데이터 (위치, 체력, 인벤토리)
- 계정 보안 필수 데이터 (로그인 이메일, 해시된 비번)
- 법적 의무 데이터 (结稅 정보, 연령 확인)

❌ 수집 NO (또는 Opt-in 필수):
- 위치 정보 (GPS) — 게임플레이 외 목적 금지
- 연락처 — 명시적 동의 없이 금지
- 생체정보 — 원칙적 금지
- 행동 패턴 심층 분석 — 익명화/집계화 후에만

📊 LDP 데이터 수명 주기:
수집 → 최소화 → 익명화 → 목적 달성 후 삭제 (Retention Policy 필수)
```

#### 5.2 데이터베이스 선택

| 용도 | 추천 DB | 이유 |
|------|---------|------|
| 플레이어 계정/인벤토리 | PostgreSQL | ACID, Relational, JSON 지원 |
| 실-time 랭킹/리더보드 | Redis (Sorted Set) | O(log N), In-memory |
| 게임 로그/이벤트 | ClickHouse / TimescaleDB | 시계열, 대용량 INSERT |
| 플레이어 설정/프로필 | MongoDB / Firestore | Schema-less, 문서형 |
| 에셋/저장 파일 | S3-compatible (MinIO/R2) | 객체 저장, CDN 연동 |

#### 5.3 보안 아키텍처

```
Security Layers:
┌──────────────────────────────────────┐
│  L7: Application Security            │
│      - Input Validation              │
│      - SQL Injection Prevention      │
│      - Rate Limiting (per user/IP)   │
│      - CSRF / XSS Protection         │
├──────────────────────────────────────┤
│  L6: Authentication & Authorization  │
│      - OAuth 2.0 / OIDC              │
│      - JWT with short expiry         │
│      - Refresh Token Rotation        │
│      - MFA Support                   │
├──────────────────────────────────────┤
│  L5: Transport Security              │
│      - TLS 1.3 only                  │
│      - Certificate Pinning (mobile)  │
│      - HSTS                          │
├──────────────────────────────────────┤
│  L4: Network Security                │
│      - DDoS Protection (CloudFlare)  │
│      - WAF (Web Application Firewall)│
│      - IP Whitelisting (admin)       │
└──────────────────────────────────────┘
```

### 6단계: DevOps & 배포 전략 (DevOps & Deployment)

#### 6.1 CI/CD 파이프라인

```
Push Code → Unit Test → Build (Server + Client)
    → Integration Test → Staging Deploy → Smoke Test
        → Production Deploy (Blue-Green or Canary)
            → Monitor (Error Rate, Latency, CCU)
                → Auto-Rollback if degraded
```

#### 6.2 배포 전략

| 전략 | 특징 | 롤백 용이성 | 위험도 |
|------|------|-----------|--------|
| **Blue-Green** | 두 환경 교체 | 즉시 | 낮음 |
| **Canary** | 점진적 트래픽 (1%→10%→100%) | 쉬움 | 중간 |
| **Rolling** | 인스턴스 순차 교체 | 어려움 | 중간 |
| **Feature Flag** | 기능 on/off 토글 | 즉시 | 가장 낮음 |

#### 6.3 모니터링 대시보드 필수 메트릭

```
핵심 메트릭 (Core Metrics):
├── Health: Error Rate, Uptime, P99 Latency
├── Capacity: CPU, Memory, Disk, Network I/O per instance
├── Game-specific: CCU, Rooms Active, Match Queue Length, Avg Session Time
├── Business: DAU, Retention D1/D7/D30, ARPU
└── LDP Compliance: Data Access Logs, Deletion Request Fulfillment Time
```

## Common Pitfalls

1. **엔진 FOMO (Fear Of Missing Out)**: "Unreal이 최신이라서 Unreal로 해야지" → 프로젝트 요구사항 무시. → **해결**: 위 Decision Tree를 따르고, 각 결정에 Justification 문서화.

2. **Early Optimization (조기 최적화)**: MMO 아키텍처로 인디 게임 시작. → **해결**: YAGNI 원칙. 현재 필요한 것만 구축, 6개월 내 확장 가능한 포인트만 예약.

3. **Client Trust (클라이언트 신뢰)**: 클라이언트가 데미지/위치/결과를 계산. → **해결**: "Never Trust the Client"를 철칙으로. 모든 중요 상태는 서버 Authoritative.

4. **Database as Queue (DB를 메시지 큐로 사용)**: polling으로 DB 테이블 감시. → **해결**: Redis Pub/Sub 또는 Kafka 같은 proper message queue 사용.

5. **Monolithic Game Server (거대한 monolith)**: 모든 로직이 하나의 프로세스. → **해결**: Domain-driven으로 서비스 분리 (Game Logic / Social / Matching / Analytics).

6. **No Rollback Plan (롤백 계획 부재)**: 배포 후 문제 발생 시 대응 불가. → **해결**: Blue-Green 배포 + 자동 롤백 트리거(Error Rate > 5% 시).

7. **LDP 사후대응**: 출시 후 개인정보 이슈 발견. → **해결**: 기획 단계부터 Data Minimization, Privacy by Design 적용.

## Verification Checklist

이 스킬을 적용한 후 즉시 실행:

- [ ] **엔진 선택(Step 1)**: Decision Tree를 따른 Justification 문서가 있는가?
- [ ] **네트워킹(Step 2)**: 모델 선택의 이유와 Latency 목표치가 정의되었는가?
- [ ] **서버 아키텍처(Step 3)**: Reference Diagram이 있고 BaaS vs Self-Hosted 결정이 있는가?
- [ ] **확장성(Step 4)**: CCU 목표에 맞는 Scaling Pattern이 선택되었는가?
- [ ] **데이터/LDP(Step 5)**: DB 선택, 보안 레이어, Data Minimization 정책이 있는가?
- [ ] **DevOps(Step 6)**: CI/CD 파이프라인, 배포 전략, 모니터링 메트릭이 정의되었는가?
- [ ] **Cost Estimation**: 월 예상 운영 비용(CCU 기반)이 산출되었는가?
- [ ] **Verdict 산출**: PASS/REVIEW/REJECT 판정과 점수(0~100)를 기록했는가?

---
*버전: 1.0.0 | 마지막 업데이트: 2026-05-09 | Linalab Game Dev Skill Set*
