---
name: game-concept-validation
description: "게임 컨셉 검증용 스킬 — LDP SS1-SS3(Acknowledge) + Schell's Elemental Tetrad(기술/메커닉스/스토리/미학) 기반으로 게임 아이디어의 타당성과 독창성을 평가한다. 소크라테스식 문답 → 4요소 테트라드 분석 → LDP B-category 윤리 선검증 → PASS/REVIEW/REJECT 판정."
version: 1.0.0
author: Linalab Game Dev
license: MIT
tags: [game-dev, ldp]
related_skills: [lina-decision-protocol]
metadata:
  hermes:
    tags: [game-dev, concept-validation, elemental-tetrad, ldp-ss1-ss3, game-design, ideation]
    related_skills: [lina-decision-protocol, game-feasibility-analysis, game-ethics-review]
---

# 게임 컨셉 검증 (Game Concept Validation)

> **핵심 문장:**
> \"좋은 게임 컨셉은 장면을 상상하게 만들고, 훌륭한 컨셉은 그 장면이 4가지 요소(기술·메커닉스·스토리·미학)에서 모두 설득력을 갖추며, 뛰어난 컨셉은 플레이어의 취약성을 착취하지 않는다는 것까지 증명해야 한다.\"

**LDP SS1-SS3 (Acknowledge)** 단계와 **Schell's Elemental Tetrad**를 결합한 게임 컨셉 검증 프레임워크입니다.

## When to Use

**Load this skill when:**
- 새로운 게임 아이디어/컨셉을 처음 구상했을 때
- GDD(Game Design Document) v0.1 작성 전 컨셉 유효성 점검이 필요할 때
- 팀 브레인스토밍 후 후보 컨셉들을 비교·선정해야 할 때
- 피칭 준비 전 컨셉의 논리적 완성도를 검증할 때
- 기존 컨셉의 방향 전환(pivot)이 필요하다고 판단될 때

**Don't use for:**
- 이미 출시된 게임의 운영/라이브옵스 최적화 → `game-live-ops-playbook`
- 순수 기술 스택 선택 논의 → `game-tech-architecture`
- 수익모델 세부 설계 → `game-monetization-strategy`

## Prerequisites

1. **컨셉 문서 1페이지 이상** — 제목, 장르, 타겟 유저, 핵심 경험(Core Experience), 차별점이 포함된 초안
2. **LDP 스킬 로드됨** — `lina-decision-protocol`의 §0-§5 프로세스 이해 필요
3. **경쟁작 조사 자료** — 최소 3개의 직접/간접 경쟁작에 대한 개략적 분석
4. **타겟 유저 페르소나** — 2-3개의 대표 유저 프로파일

## Procedure

### Step 1: 소크라테스식 컨셉 인터뷰 (LDP §0 + SS1)

컨셉 제안자에게 Paul & Elder의 6가지 질문 유형을 적용합니다:

| 질문 유형 | 게임 컨셉 특화 질문 | 목적 |
|-----------|---------------------|------|
| **명확화(Clarification)** | \"이 게임에서 플레이어가 느껴야 할 핵심 감정은 정확히 무엇인가?\" | Core Experience 명확화 |
| **가정 검토(Probing Assumptions)** | \"우리가 당연하게 여기는 '재미'의 정의가 타겟 유저와 일치하는가?\" | 편향 식별 |
| **증명 요구(Probing Evidence)** | \"이 컨셉이 시장에서 통할 근거는 무엇인가? 참고작/데이터는?\" | 타당성 확보 |
| **관점 전환(Questioning Viewpoints)** | \"이 장르를 싫어하는 플레이어는 이 컨셉을 어떻게 평가할까?\" | 맹점 발견 |
| **함의 탐색(Examining Implications)** | \"이 컨셉이 성공하면 우리 팀/브랜드에 어떤 영향을 주는가?\" | 전략적 함의 |
| **질문 자체(Questioning Q)** | \"우리가 진짜 검증해야 하는 가정 하나는 무엇인가?\" | 핵심 리스크 좁히기 |

**출력:** 컨셉 가정 로그(Assumption Log) — 확인된 가정 5-10개와 각각의 신뢰도(High/Medium/Low)

### Step 2: Elemental Tetrad 분석 (SS2)

Schell의 4요소 테트라드로 컨셉을 해체합니다:

```
┌─────────────────────────────────────┐
│         ELEMENTAL TETRAD            │
├──────────┬──────────────────────────┤
│ Mechanics│ • Core Loop은 무엇인가?   │
│ (메커닉스) │ • 규칙은 명확한가?       │
│          │ • 플레이어 선택의 의미는?  │
├──────────┼──────────────────────────┤
│ Story    │ • 서사는 메커닥스와 연결되는가?
│ (스토리)  │ • 플레이어가 주인공인가?   │
│          │ • 세계관은 일관된가?       │
├──────────┼──────────────────────────┤
│ Aesthetics│ • 시각/청각적 감성은 무엇? │
│ (미학)    │ • 타겟 유저 취향과 맞는가?  │
│          │ • 브랜드 아이덴티티와 일치? │
├──────────┼──────────────────────────┤
│ Technology│ • 어떤 플랫폼/엔진이 적합? │
│ (기술)    │ • 기술적 제약이 창의성을   │
│          │   제한하는가? 오히려 가능성 │
│          │   을 여는가?              │
└──────────┴───────────────────────────┘
```

**각 요소별 평가 기준 (1-5점):**
- **5점**: 요소가 다른 3요소와 강력하게 시너지를 내며 컨셉의 핵심 가치를 뒷받침함
- **3점**: 요소가 무난하지만 시너지가 약하거나 파괴적이지 않음
- **1점**: 요소가 다른 요소와 충돌하거나 컨셉을 약화시킴

**⚠️ Tetrad Balance Rule:** 4요소 중 어느 한 요소라도 2점 이하면 **REVIEW** 필수. 두 개 이상이 2점 이하면 **REJECT** 권고.

### Step 3: 컨셉 인정 및 강점 식별 (SS3)

LDP SS1 원칙 — **무작정 까지 않음**. 컨셉의 잘 만든 부분을 먼저 인정합니다:

- [ ] **독창성 (Novelty)** — 기존 작품과의 차별점이 명확한가?
- [ ] **감정선 (Emotional Arc)** — 플레이어가 느낄 여정이 그려지는가?
- [ ] **연구 기반 (Research Basis)** — 시장/유저 조사가 반영되었는가?
- [ ] **실행 가능성 (Feasibility Signal)** — 팀 역량 범위 내에서 실현 가능한 신호가 있는가?

**출력:** Strength Map — 인정된 강점 3-5개와 각각의 증거

### Step 4: LDP B-category 윤리 선검증 (Ethics Pre-check)

본격적인 윤리 리뷰(`game-ethics-review`) 전, 컨셉 단계에서 미리 체크합니다:

| B-category 질문 | 컨셉 단계 체크포인트 | 판정 |
|-----------------|---------------------|------|
| 이 게임은 플레이어의 취약한 감정을 수익화하는가? | 컨셉의 핵심 감정이 공포/불안/FOMO/소속감 욕구 중哪一个? | ☐ Yes ☐ No ☐ Partial |
| 결제하지 않으면 불이익/죄책감을 느끼게 되는가? | Free-to-play vs Premium 모델의 초기 방향 | ☐ Yes ☐ No ☐ Partial |
| 의도치 않게 사용자를 붙잡는 감정적 덫이 있는가? | Core Loop에 habit-forming 패턴 포함 여부 | ☐ Yes ☐ No ☐ Partial |

**B-category에서 단 하나라도 명확한 'No'가 없으면 → `game-ethics-review` 스킬로 넘깁니다.**

### Step 5: LDP C-category 종료 선검증 (Termination Pre-check)

| C-category 질문 | 컨셉 단계 체크포인트 | 판정 |
|-----------------|---------------------|------|
| 서비스 종료 시 플레이어는 무엇을 잃는가? | 진행형 서비스 vs 단판작 여부 | ☐ Yes ☐ No ☐ Partial |
| 플레이어 데이터를 내보낼 수 있는가? | 클라우드 저장/계정 시스템 계획 | ☐ Yes ☐ No ☐ Partial |

### Step 6: Verdict 산출

```
PASS:  Tetrad 평균 3.5+ + B-category 'no' 0건 + C-category 'no' 0건
       + Stronghold Map에 3개 이상의 확실한 강점

REVIEW: Tetrad 요소 중 2점以下 1개 있음
        또는 B/C category에 'partial' 존재
        → 구체적 개선 방향과 함께 수정 권고

REJECT: Tetrad 요소 중 2개 이상이 2점以下
        또는 B-category에 명확한 'no'
        또는 컨셉 자체의 핵심 가정이 파기됨
```

## Pitfalls

1. **Tetrad 편중 오류 (Tetrad Imbalance Fallacy)** — Mechanics만 강조하고 Story/Aesthetics를 후순위로 밀어내면 '기계적으로 완벽하지만 영혼 없는 게임'이 됩니다. 4요소를 동등한 무게로 평가하세요.
2. **"모든 사람을 위한 게임" 함정 (Everyone Trap)** — 타겝을 넓게 잡으면 Tetrad의 모든 요소가 흐려집니다. **구체적인 타겟 한 명**을 상상하고 그 사람에게 4요소가 모두 설득력 있는지 검증하세요.
3. **컨셉 = 기능 목록 오류 (Feature List Confusion)** — 컨셉 검증은 "무엇을 넣을까"가 아니라 "무슨 경험을 줄까"입니다. 기능 목록이 나오기 전에 Core Experience 문장 하나로 컨셉을 요약할 수 없으면 아직 컨셉이 아닙니다.
4. **경쟁작 분석의 확인 편향 (Confirmation Bias)** — 우리 컨셉과 비슷한 성공작만 참고하고 실패작은 무시하기 쉽습니다. **같은 컨셉으로 실패한 작품 2개**를 반드시 분석에 포함하세요.
5. **윤리 검증을 "나중에" 미루기 (Ethics Deferral)** — 컨셉 단계에서 윤리 리스크를 간과하면 나중에 수익모델 설계 단계에서 "이미 컨셉이 정해졌으니 어쩔 수 없"는 상황에 빠집니다. **Step 4는 선택이 아닙니다.**

## Verification Checklist

- [ ] **Assumption Log** 작성 완료 — 가정 5-10개와 신뢰도 등급
- [ ] **Elemental Tetrad 점수 매트릭스** 완성 — 4요소 각 1-5점 + 근거
- [ ] **Strength Map** 작성 — 인정된 강점 3-5개
- [ ] **B-category Ethics Pre-check** 3문항 전부 판정 완료
- [ ] **C-category Termination Pre-check** 2문항 전부 판정 완료
- [ ] **Verdict** 산출 (PASS/REVIEW/REJECT) + 근거 요약
- [ ] REVIEW일 경우 → `game-feasibility-analysis` 또는 `game-ethics-review`로 연결 계획 수립
- [ ] REJECT일 경우 → 기각 사유 문서화 + 학습 포인트 추출 (LDP §12)
