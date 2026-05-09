---
name: lux-integration
description: "Lux 통합 어댑터 템플릿 — LDP(Lina Decision Protocol) 검증 엔진과 외부 Lux 시스템 간의 인터페이스 계약(Interface Contract)을 정의한다. Lux 소스 코드 접근 없이 추상 구조로 설계된 Adapter 패턴 기반 통합 가이드. 데이터 흐름, API 계약, 에러 핸들링, 검증 파이프라인 연동 방식을 포함한다."
version: 1.0.0
author: Linalab Game Dev
license: MIT
tags: [game-dev, ldp]
related_skills: [lina-decision-protocol]
metadata:
  hermes:
    tags: [game-dev, lux-integration, ldp, adapter-pattern, interface-contract, verification-engine, integration]
    related_skills: [lina-decision-protocol, game-ethics-review, game-analytics-dashboard]
---

# Lux 통합 어댑터 (Lux Integration Adapter)

> **핵심 문장:**
> \"좋은 인터페이스는 구현체가 무엇인지 알 필요 없이 '무엇을 할 수 있는지'만 말해주며, 훌륭한 인터페이스는 그 계약이 깨졌을 때 양쪽 모두 누구 잘못인지 명확하게 알 수 있게 만든다.\"

**LDP 검증 엔진과 Lux 시스템 간의 추상 인터페이스 계약을 정의하는 Adapter 패턴 기반 통합 템플릿입니다. Lux 소스 코드에 직접 의존하지 않고 계약(Contract) 수준에서 통합을 가능하게 합니다.**

## When to Use

**Load this skill when:**
- LDP 검증 엔진과 Lux 시스템 간의 통합 어댑터를 개발할 때
- Lux와 LDP 간의 데이터 교환 포맷/API 계약을 정의해야 할 때
- Lux 이벤트/데이터를 LDP 검증 파이프라인에 공급하려 할 때
- LDP 검증 결과를 Lux 쪽으로 다시 전달하는 리턴 채널을 설계할 때
- Lux 버전 변경/업데이트 시 호환성을 검증해야 할 때

**Don't use for:**
- Lux 내부 구현 상세 분석/수정 → Lux 팀 문서 참조
- LDP 프로토콜 자체의 수정 → `lina-decision-protocol`
- 게임 클라이언트 SDK 통합 → 해당 플랫폼별 integration guide

## Prerequisites

1. **LDP 스킬 로드됨** — LDP §0-§12 전체 프로세스와 B/C category 검증 로직 이해
2. **Lux 시스템의 공개 API/Event 스펙** — Lux가 외부에 노출하는 인터페이스 문서 (내부 소스 불필요)
3. **통합 목표 명확화** — 어떤 LDP 검증 단계(SS1-SS6)와 Lux의 어떤 기능을 연결할 것인지
4. **기본적인 Adapter/Interface 패턴 이해** — 추상화 계약 vs 구현체 분리 원칙

## Procedure

### Step 1: 통합 목적 및 범위 정의 (Integration Scope)

#### 1.1 통합 목적 매트릭스

| 통합 방향 | 목적 | 예시 |
|----------|------|------|
| **Lux → LDP** | Lux 데이터를 LDP 검증 입력으로 공급 | Lux 유저 행동 데이터 → LDP B-category 분석 |
| **LDP → Lux** | LDP 검증 결과를 Lux 액션으로 전달 | LDP REJECT 판정 → Lux 자동 브레이크/경고 |
| **Bidirectional** | 양방향 실시간 피드백 루프 | Lux A/B Test 결과 ↔ LDP 윤리 재검증 |

#### 1.2 Scope 경계 명확화

```
┌─────────────────────────────────────────────────────┐
│              INTEGRATION BOUNDARY                   │
│                                                     │
│  ┌──────────┐      ┌──────────────┐      ┌──────┐ │
│  │   Lux    │ ───→ │   Lux        │ ───→ │  LDP │ │
│  │  System  │      │   Adapter    │      │ Engine│ │
│  │          │ ←── │   (本 스킬)   │ ←── │      │ │
│  └──────────┘      └──────────────┘      └──────┘ │
│       ↑                  ↑                    ↑    │
│   Lux Internal     Contract Layer         LDP Core │
│   (접근 불필요)     (우리가 정의)        (접근 불필요)│
│                                                     │
│  ⚠️ Adapter는 양쪽 내부를 모른다.                    │
│     오직 Interface Contract만 안다.                 │
└─────────────────────────────────────────────────────┘
```

### Step 2: Interface Contract 정의 (IDL — Interface Definition Language)

#### 2.1 Lux → LDP 입력 계약 (Input Contract)

```typescript
/**
 * LuxIntegrationTypes.ts
 * Lux → LDP Adapter용 인터페이스 계약 정의
 * Lux 내부 구현에 독립적인 추상 타입
 */

// ============================================================
// SECTION 1: CORE DATA TYPES (Lux가 제공하는 데이터 형식)
// ============================================================

/** Lux 유저 식별자 — PII 포함 금지 */
interface LuxUserId {
  /** Lux 내부의 익명화/해싱된 유저 ID */
  anonymousId: string;
  /** 선택적: 외부 매핑용 비-PII 식별자 (예: device_fingerprint_hash) */
  externalRef?: string;
}

/** Lux 세션 메타데이터 */
interface LuxSessionMeta {
  sessionId: string;
  startTime: ISO8601;
  endTime?: ISO8601;
  platform: "ios" | "android" | "web" | "pc" | "console" | "other";
  appVersion: string;
  sdkVersion: string;
  locale: string;           // ISO 639-1 (예: "ko", "en")
  region: string;            // ISO 3166-1 alpha-2
}

/** Lux 행동 이벤트 (추상화된 공통 형식) */
interface LuxBehaviorEvent {
  eventId: string;
  timestamp: ISO8601;
  userId: LuxUserId;
  sessionMeta: LuxSessionMeta;

  /** 이벤트 카테고리 — LDP 카테고리와 매핑 가능해야 함 */
  category:
    | "engagement"       // 참여 (플레이, 도전, 성취)
    | "monetization"     // 과금 (구매, 조회, 결제 시도)
    | "social"           // 소셜 (친구, 길드, 채팅)
    | "retention"        // 리텐션 관련 (복귀, 잔류)
    | "onboarding"       // 온보딩 (튜토리얼, FTE)
    | "error"            // 오류 (크래시, 버그)
    | "feedback"         // 피드백 (평점, 리뷰, CS)
    | "custom";          // 기타 (Lux 고유 이벤트)

  /** 이벤트 이름 */
  eventName: string;

  /** 이벤트 속성 — 구조화된 키-값 */
  properties: Record<string, string | number | boolean | null>;

  /** 값 정보 (과금 등 민감 데이터용 별도 필드) */
  valueInfo?: {
    /** 금액은 소수점 2자리 문자열 (예: "4900.00") */
    amount?: string;
    currency?: string;     // ISO 4217 (예: "KRW", "USD")
    /** ⚠️ 금액을 0으로 마스킹할 수 있는 플래그 (LDP C-category 준수) */
    masked?: boolean;
  };
}

/** Lux 집계 데이터 (배치/주기적 보고용) */
interface LuxAggregatedMetrics {
  reportPeriod: {
    start: ISO8601;
    end: ISO8601;
    granularity: "hourly" | "daily" | "weekly" | "monthly";
  };

  /** 핵심 게임 KPI — game-analytics-dashboard 스킬과 정렭 */
  kpis: {
    dau: number;
    mau: number;
    arpu?: string;          // 평균 매출 (문자열로 정밀도 보장)
    arppu?: string;
    retention: {
      d1: number;           // 0.0 ~ 1.0
      d7: number;
      d30: number;
      d60?: number;
    };
    churnRate?: number;     // 0.0 ~ 1.0
  };

  /** Segment별 분할 데이터 */
  segments?: Array<{
    segmentName: string;
    segmentCriteria: string; // 세그먼트 정의 ( human-readable)
    kpis: LuxAggregatedMetrics["kpis"];
  }>;
}

// ============================================================
// SECTION 2: LDP VERIFICATION REQUEST (LDP 검증 요청)
// ============================================================

/** LDP 검증 요청 — Adapter가 LDP Engine에 보내는 메시지 */
interface LdpVerificationRequest {
  requestId: string;          // UUID v4
  requestedAt: ISO8601;

  /** 검증 대상 데이터 */
  source: {
    type: "event" | "aggregated" | "mixed";
    events?: LuxBehaviorEvent[];
    metrics?: LuxAggregatedMetrics;
  };

  /** 검증 설정 */
  config: {
    /** 적용할 LDP Section (다중 선택 가능) */
    sections: Array<"SS1" | "SS2" | "SS3" | "SS4" | "SS5" | "SS6">;

    /** B-category (윤리) 검증 깊이 */
    ethicsDepth: "quick" | "standard" | "deep";

    /** C-category (종료/데이터) 검증 포함 여부 */
    includeTerminationCheck: boolean;

    /** 출력 상세 수준 */
    verbosity: "summary" | "detailed" | "full";

    /** 커스텀 임계값 (선택적 재정의) */
    thresholds?: {
      retentionD1Min?: number;    // 기준: 0.35
      retentionD7Min?: number;    // 기준: 0.12
      arpuMaxRatio?: number;      // ARPPU/ARPU 최대 배수
      churnRateMax?: number;      // 기준: 0.25
    };
  };

  /** 요청자 컨텍스트 (audit trail용) */
  requesterContext: {
    sourceSystem: "lux-adapter";  // 고정
    triggeredBy: "scheduled" | "manual" | "event-driven" | "api-call";
    correlationId?: string;       // Lux 측 요청 ID (추적용)
  };
}
```

#### 2.2 LDP → Lux 출력 계약 (Output Contract)

```typescript
// ============================================================
// SECTION 3: LDP VERIFICATION RESPONSE (LDP 검증 응답)
// ============================================================

/** LDP 검증 결과 — LDP Engine이 Adapter에게 반환 */
interface LdpVerificationResponse {
  requestId: string;
  respondedAt: ISO8601;
  processingTimeMs: number;

  /** 종합 판정 */
  verdict: "PASS" | "REVIEW" | "REJECT" | "ERROR" | "DEFERRED";

  /** 신뢰도 점수 (0.0 ~ 1.0) — 데이터 부족/불확실성 반영 */
  confidence: number;

  /** Section별 상세 결과 */
  sectionResults: Array<{
    section: string;              // "SS1" | "SS2" | ...
    status: "pass" | "review" | "reject" | "skipped" | "error";
    score?: number;               // 0 ~ 100
    findings: LdpFinding[];
  }>;

  /** B-category (윤리) 전용 결과 */
  ethicsResult?: {
    overallStatus: "clear" | "warning" | "violation" | "inconclusive";
    flags: Array<{
      code: string;               // 예: "B-001", "B-007"
      severity: "info" | "low" | "medium" | "high" | "critical";
      category:
        | "manipulative_design"   // 조작적 설계
        | "exploitative_monetization" // 착취적 수익화
        | "coercive_communication"    // 강압적 커뮤니케이션
        | "psychological_harm"        // 심리적 해악
        | "unfair_advantage"          // 불공정 우위
        | "other";
      description: string;        // human-readable 설명 (한글+영어)
      evidence: string;           // 어떤 데이터/지표에서 발견되었는가
      suggestion: string;         // 권장 개선안
      ldpReference: string;       // LDP 원문 섹션 참조 (예: "LDP §7-B3")
    }>;
  };

  /** C-category (종료/데이터) 전용 결과 */
  terminationResult?: {
    overallStatus: "ready" | "partial" | "not_ready" | "na";
    checks: Array<{
      name: string;               // 예: "data_export", "account_deletion"
      status: "passed" | "failed" | "pending" | "not_applicable";
      details?: string;
    }>;
  };

  /** 실행 가능한 권장사항 (Lux 측에서 바로 사용 가능한 형식) */
  actionItems: Array<{
    priority: "P0" | "P1" | "P2" | "P3";
    category: "immediate_action" | "design_change" | "policy_update" | "monitoring" | "investigation";
    title: string;
    description: string;
    /** Lux 측에서 처리했음을 보고할 수 있는 tracking ID */
    trackingId: string;
    dueDate?: ISO8601;
  }>;
}

/** LDP Finding — 개별 검증 발견 사항 */
interface LdpFinding {
  id: string;
  type: "strength" | "weakness" | "risk" | "opportunity" | "neutral";
  title: string;
  detail: string;
  evidence?: string;             // 관련 데이터 조각
  severity?: "info" | "low" | "medium" | "high" | "critical";
}
```

### Step 3: Adapter 아키텍처 설계 (Adapter Architecture)

#### 3.1 Adapter 구성 요소

```
┌──────────────────────────────────────────────────────────┐
│                  LUX ADAPTER ARCHITECTURE                │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │              INGESTION LAYER                     │   │
│  │  ┌─────────────┐  ┌─────────────┐               │   │
│  │  │ Event       │  │ Batch/      │               │   │
│  │  │ Ingestor    │  │ Poller      │               │   │
│  │  │ (real-time) │  │ (scheduled) │               │   │
│  │  └──────┬──────┘  └──────┬──────┘               │   │
│  │         └────────┬────────┘                      │   │
│  │                  ▼                               │   │
│  │  ┌──────────────────────────────┐                │   │
│  │  │ NORMALIZER                   │                │   │
│  │  │ Lux raw format →             │                │   │
│  │  │ LuxBehaviorEvent (Contract)  │                │   │
│  │  └──────────────┬───────────────┘                │   │
│  ├──────────────────┼───────────────────────────────┤   │
│  │          VALIDATION LAYER                        │   │
│  │  ┌──────────────────────────────┐                │   │
│  │  │ Schema Validator            │                │   │
│  │  │ • Required fields check     │                │   │
│  │  │ • Type/range validation     │                │   │
│  │  │ • PII detection & mask      │                │   │
│  │  │ • Data freshness check      │                │   │
│  │  └──────────────┬───────────────┘                │   │
│  ├──────────────────┼───────────────────────────────┤   │
│  │          DISPATCH LAYER                          │   │
│  │  ┌──────────────────────────────┐                │   │
│  │  │ LDP Client                   │                │   │
│  │  │ • Build VerificationRequest  │                │   │
│  │  │ • Call LDP Engine (HTTP/RPC) │                │   │
│  │  │ • Retry / Timeout / Circuit  │                │   │
│  │  │   Breaker                    │                │   │
│  │  └──────────────┬───────────────┘                │   │
│  ├──────────────────┼───────────────────────────────┤   │
│  │          RESPONSE LAYER                         │   │
│  │  ┌──────────┐  ┌──────────────┐  ┌───────────┐  │   │
│  │  │ Response │  │ Alert        │  │ Feedback  │  │   │
│  │  │ Formatter│  │ Generator    │  │ Loop      │  │   │
│  │  │ (Lux fmt)│  │ (threshold)  │  │ (→ Lux)   │  │   │
│  │  └──────────┘  └──────────────┘  └───────────┘  │   │
│  ├──────────────────────────────────────────────────┤   │
│  │          OBSERVABILITY LAYER                     │   │
│  │  • Metrics (request latency, error rate)         │   │
│  │  • Logs (structured JSON)                        │   │
│  │  • Traces (request correlation)                  │   │
│  └──────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
```

#### 3.2 핵심 인터페이스 (Adapter Public API)

```typescript
// ============================================================
// SECTION 4: ADAPTER PUBLIC INTERFACE (구현체가 지켜야 할 계약)
// ============================================================

/** LuxLdpAdapter — Lux와 LDP 간의 공식 어댑터 인터페이스 */
interface ILuxLdpAdapter {
  // ---------- Ingestion ----------

  /** 실시간 이벤트 수신 (Lux → Adapter) */
  ingestEvent(rawEvent: unknown): Promise<IngestionResult>;

  /** 배치/집계 데이터 수신 (Lux → Adapter) */
  ingestAggregatedMetrics(
    rawMetrics: unknown
  ): Promise<IngestionResult>;

  // ---------- Verification ----------

  /** LDP 검증 요청 (동기/비동기) */
  requestVerification(
    params: VerificationParams
  ): Promise<LdpVerificationResponse>;

  /** 검증 상태 조회 (비동기 요청용) */
  getVerificationStatus(
    requestId: string
  ): Promise<VerificationStatus>;

  // ---------- Feedback Loop ----------

  /** LDP 검증 결과를 Lux 측으로 전달 (Adapter → Lux) */
  pushResultToLux(
    response: LdpVerificationResponse,
    targetEndpoint: string
  ): Promise<PushResult>;

  // ---------- Health & Admin ----------

  /** Adapter 건강 체크 */
  healthCheck(): Promise<AdapterHealth>;

  /** Lux ↔ LDP 연결 상태 확인 */
  connectivityCheck(): Promise<ConnectivityStatus>;
}

/** 지원 타입들 */
type IngestionResult =
  | { success: true; eventId: string; normalized: LuxBehaviorEvent }
  | { success: false; error: IngestionError; retryable: boolean };

type VerificationParams = {
  eventType?: "realtime" | "batch" | "custom";
  timeRange?: { start: ISO8601; end: ISO8601 };
  segmentFilter?: string[];
  config: LdpVerificationRequest["config"];
};

type VerificationStatus =
  | { state: "pending"; queuedAt: ISO8601 }
  | { state: "processing"; startedAt: ISO8601; estimatedCompletion?: ISO8601 }
  | { state: "completed"; response: LdpVerificationResponse }
  | { state: "failed"; error: string; retriable: boolean };

type PushResult =
  | { success: true; deliveredAt: ISO8601; luxReceiptId?: string }
  | { success: false; error: string; retryable: boolean };

type AdapterHealth = {
  status: "healthy" | "degraded" | "unhealthy";
  components: {
    ingestion: "up" | "down" | "degraded";
    ldpConnection: "up" | "down" | "degraded";
    luxOutbound: "up" | "down" | "degraded";
  };
  metrics: {
    eventsProcessedLastHour: number;
    avgProcessingTimeMs: number;
    errorRate: number;           // 0.0 ~ 1.0
    queueDepth: number;
  };
};

type ConnectivityStatus = {
  ldpEngine: { reachable: boolean; latencyMs: number };
  luxSystem: { reachable: boolean; latencyMs: number };
  lastSuccessfulPing: ISO8601;
};
```

### Step 4: 에러 핸들링 및 회복 전략 (Error Handling)

#### 4.1 에러 분류 및 대응

| 에러 유형 | 원인 | 대응 | Retry? |
|----------|------|------|--------|
| **Schema Validation Error** | Lux 데이터가 Contract과 불일치 | 거부 + 로깅 + 알람 | No (발신측 수정 필요) |
| **PII Detection Error** | PII 포함 감지 | 마스킹 후 재처리 or 거부 | Case-by-case |
| **LDP Engine Unreachable** | LDP 서버 다운/네트워크 | Queue에 저장 + Retry | Yes (Exponential Backoff) |
| **LDP Processing Error** | LDP 내부 오류 | 결과에 ERROR 표기 + 알람 | No (LDP 팀 에스컬레이션) |
| **Timeout** | 처리 시간 초과 | DEFERRED 반환 + 백그라운드 재시도 | Yes |
| **Lux Outbound Failure** | Lux 수신 실패 | Queue + Retry + Dead Letter Queue | Yes (유한 횟수) |
| **Rate Limit** | LDP/Lux 호출량 제한 | 429 처리 + Backoff | Yes |

#### 4.2 Circuit Breaker 패턴

```
Circuit Breaker States:

  CLOSED ──[연속 N회 실패]──→ OPEN
    │                              │
    │ [정상 요청]                   │ [모든 요청 즉시 fail-fast]
    ▼                              │
  성공 처리                         │
    │                              │
    └──────[Half-Open 타이머 만료]──┘
              │
              ▼
          HALF-OPEN ──[M회 성공]──→ CLOSED
              │
              └──[1회 실패]──→ OPEN (타이머 리셋)

권장 설정:
  • failureThreshold: 5
  • halfOpenMaxCalls: 3
  • openDuration: 30s (initial), 60s, 120s (exponential)
```

### Step 5: LDP 검증 파이프라인 연동 (Pipeline Integration)

#### 5.1 검증 워크플로우

```
┌─────────────────────────────────────────────────────────┐
│              VERIFICATION PIPELINE                       │
│                                                          │
│  1. TRIGGER                                              │
│     ├── Scheduled (매일 09:00 KST)                       │
│     ├── Event-driven (Lux 이벤트 임계값 도달)             │
│     ├── Manual (PM/Operator 요청)                        │
│     └── API Call (외부 시스템 트리거)                     │
│          │                                               │
│          ▼                                               │
│  2. COLLECT                                              │
│     ├── Lux Event Store에서 데이터 수집                   │
│     ├── Aggregation (필요 시)                            │
│     └── PII Masking                                     │
│          │                                               │
│          ▼                                               │
│  3. BUILD REQUEST                                        │
│     ├── LdpVerificationRequest 조립                      │
│     ├── Config 적용 (Section, Depth, Thresholds)         │
│     └── Request ID 생성 (UUID)                           │
│          │                                               │
│          ▼                                               │
│  4. EXECUTE (LDP Engine)                                 │
│     ├── SS1-SS3: Acknowledge & Analyze                   │
│     ├── SS4-SS5: Numbers & Budget                        │
│     ├── SS6: Synthesize Verdict                         │
│     ├── B-category: Ethics Check                         │
│     └── C-category: Termination Check                    │
│          │                                               │
│          ▼                                               │
│  5. PROCESS RESPONSE                                    │
│     ├── Verdict 해석                                     │
│     ├── Action Items 생성                                │
│     ├── Alert 발생 (Critical flag 시)                    │
│     └── Lux 측으로 Result Push                          │
│          │                                               │
│          ▼                                               │
│  6. FEEDBACK LOOP                                       │
│     ├── Lux에서 Action Item 처리 완료 보고               │
│     ├── 재검증 스케줄링 (필요 시)                        │
│     └── Audit Log 기록                                   │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

#### 5.2 Auto-trigger 규칙 예시

```yaml
# lux-adapter-triggers.yaml
triggers:
  - name: daily_health_check
    schedule: "0 9 * * *"          # 매일 09:00 KST
    config:
      sections: [SS1, SS2, SS3, SS4, SS5, SS6]
      ethicsDepth: standard
      includeTerminationCheck: true
      verbosity: detailed

  - name: churn_spike_alert
    condition:
      metric: churnRate
      operator: ">"
      threshold: 0.25
      window: 7d
    config:
      sections: [SS3, SS6]
      ethicsDepth: deep
      includeTerminationCheck: false
      verbosity: full

  - name: post_release_check
    trigger: event_driven
    eventType: "lux.version.deployed"
    cooldown: 24h                  # 배포 후 24시간 내 1회만
    config:
      sections: [SS1, SS2, SS3]
      ethicsDepth: quick
      includeTerminationCheck: false
      verbosity: summary

  - name: ethics_flag_auto_review
    condition:
      flag: B-category
      severity: ">= high"
    config:
      sections: [SS3, SS6]
      ethicsDepth: deep
      includeTerminationCheck: true
      verbosity: full
    escalation:
      notify: ["pm", "ethics-reviewer"]
      sla: "4h"                    # 4시간 내 인간 검토 필요
```

### Step 6: 테스트 및 검증 전략 (Testing Strategy)

#### 6.1 테스트 계층

| 테스트 유형 | 목적 | 방법 | 빈도 |
|-----------|------|------|------|
| **Contract Test** | Interface Contract 준수 검증 | Schema validation, Type checking | PR마다 |
| **Integration Test** | Mock Lux/Mock LDP로 E2E 흐름 검증 | ContractKit, Pact | PR마다 |
| **Resilience Test** | 장애 시나리오 검증 | Chaos engineering (Latency, Error injection) | 분기마다 |
| **Performance Test** | 처리량/지연시간 검증 | Load test (목표 QPS × 3) | 릴리즈 전 |
| **PII Safety Test** | PII 유출 방지 검증 | PII 샘플 데이터 주입 + 마스킹 확인 | PR마다 |

#### 6.2 Contract Test 예시 (Pact-style)

```typescript
// Contract Test: Lux → Adapter 수신 계약
describe("Lux → Adapter: Ingestion Contract", () => {
  it("should accept valid LuxBehaviorEvent and normalize it", async () => {
    const validEvent = { /* contract-compliant event */ };
    const result = await adapter.ingestEvent(validEvent);
    expect(result.success).toBe(true);
    expect(result.normalized).toMatchSchema(LuxBehaviorEventSchema);
  });

  it("should reject event with PII in userId", async () => {
    const piiEvent = { userId: { anonymousId: "email@example.com" } };
    const result = await adapter.ingestEvent(piiEvent);
    expect(result.success).toBe(false);
    expect(result.error.code).toBe("PII_DETECTED");
  });

  it("should handle malformed event gracefully", async () => {
    const garbage = { not: "a valid event" };
    const result = await adapter.ingestEvent(garbage);
    expect(result.success).toBe(false);
    expect(result.retryable).toBe(false); // 발신측 수정 필요
  });
});
```

### Step 7: 보안 및 감사 (Security & Audit)

#### 7.1 보안 요구사항

| 영역 | 요구사항 | 구현 가이드 |
|------|---------|-----------|
| **전송 암호화** | Lux ↔ Adapter ↔ LDP 간 TLS 1.3+ | mTLS 권장 |
| **인증** | Mutual authentication | API Key + Certificate |
| **인가** | Role-based access | read-only / verify / admin 역할 분리 |
| **로그 보안** | PII 미포함, tamper-proof | Structured log + SIEM 전송 |
| **Audit Trail** | 모든 검증 요청/응답 기록 | Write-once log (append-only) |

#### 7.2 Audit Log 스키마

```typescript
interface LuxAdapterAuditLog {
  logId: string;              // UUID
  timestamp: ISO8601;
  actor: "system" | "user:{userId}";
  action: "ingest" | "verify" | "push_result" | "config_change";
  requestId?: string;
  inputHash: string;          // SHA-256 of input (무결성)
  outputSummary: string;      // verdict only (전체 데이터 미포함);
  ipAddress?: string;         // masking: /24 prefix only
  userAgent?: string;
  durationMs: number;
}
```

## Pitfalls

1. **Concrete-to-Concrete coupling (구현체 결합)** — Lux 내부 타입을 그대로 Adapter 내부에 가져오면 Lux 버전업마다 Adapter가 깨집니다. **반드시 Contract 타입(Step 2)을 중간에 두세요.**
2. **Silent data loss (조용한 데이터 손실)** — Normalizer에서 필드를 무시하거나 마스킹하면서 로그를 남기지 않으면 데이터가 사라진 줄도 모릅니다. **모든 drop/mask에 반드시 log를 남기세요.**
3. **LDP를 black box로 취급** — LDP Engine의 응답만 받고 내부 로직을 이해하지 않으면 잘못된 검증 결과를 그대로 Lux에 전달합니다. **최소한 LDP Section별로 어떤 검증을 하는지 이해하세요.**
4. **Retry storm (재시도 폭주)** — LDP 장애 시 큐가 차면서 무한 재시도하면 양쪽 시스템 모두 악화됩니다. **Circuit Breaker(Step 4.2)는 선택이 아닙니다.**
5. **Verdict의 맹신** — LDP가 "PASS"라고 해도 Lux의 비즈니스 문맥에서 PASS가 아닐 수 있습니다. **Verdict는 참고 자료이지 최종 결정이 아닙니다.** 항상 human-in-the-loop를 유지하세요.
6. **PII boundary blur (PII 경계 흐림)** — "익명화했다"고 생각하지만 여러 이벤트를 조합하면 re-identification 가능한 경우가 많습니다. **Data Minimization 원칙(Step 3.1)을 엄격히 적용하고 정기적으로 Re-identification risk audit를 하세요.**

## Verification Checklist

- [ ] **Integration Scope** 정의 완료 — 통합 방향(단방향/양방향) + 목적 명확
- [ ] **Input Contract (IDL)** 작성 완료 — LuxUserId, LuxBehaviorEvent, LuxAggregatedMetrics, LdpVerificationRequest
- [ ] **Output Contract (IDL)** 작성 완료 — LdpVerificationResponse, EthicsResult, TerminationResult, ActionItem
- [ ] **Adapter Public Interface** 정의 완료 — ILuxLdpAdapter의 6개 메서드 + 모든 지원 타입
- [ ] **Adapter Architecture** — 5-layer 구조(Ingestion/Validation/Dispatch/Response/Observability) 설계 완료
- [ ] **Error Handling Matrix** — 7가지 에러 유형별 대응 + Retry 정책
- [ ] **Circuit Breaker** 구현 — CLOSED/HALF-OPEN/OPEN 상태 머신 + 권장 파라미터
- [ ] **Verification Pipeline** — 6단계 워크플로우 + Auto-trigger 규칙 4종 이상
- [ ] **Test Strategy** — 5가지 테스트 계층 + Contract Test 예시
- [ ] **Security & Audit** — 5가지 보안 요구사항 + Audit Log 스키마
- [ ] **Version Compatibility Plan** — Lux API 버전 변경 시 Adapter 대응 절차
