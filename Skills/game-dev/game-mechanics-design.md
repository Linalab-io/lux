---
name: game-mechanics-design
description: "게임 메커니즘 설계 스킬 — Core Loop 설계, Progression System, Balance Framework, MDA Framework (Mechanics/Dynamics/Aesthetics), 게임 밸런스 수학적 모델링, LDP 윤리 기준을 적용한 메커니즘 기획. When to Use, Prerequisites, Procedure, Pitfalls, Verification checklist 포함."
version: 1.0.0
author: Linalab
license: MIT
tags: [game-dev, ldp]
related_skills: [lina-decision-protocol]
metadata:
  hermes:
    tags: [game-dev, ldp, mechanics, core-loop, progression, balance, mda-framework, game-design]
    related_skills: [lina-decision-protocol]
---

# 게임 메커니즘 설계 (Game Mechanics Design)

> **핵심 문장:**
> "좋은 메커니즘은 플레이어의 의도된 행동을 자연스럽게 유도한다. 복잡함은 깊이가 아니라 장애물이다."

이 스킬은 게임의 **핵심 루프(Core Loop) 설계, 진행 시스템(Progression System), 밸런스 프레임워크, 그리고 MDA 프레임워크**를 다룹니다. 특히 LDP(Lina's Decision Protocol)의 B-카테고리(윤리 질문)를 메커니즘 설계에 적용하여, **몰입적이면서도 착취적이지 않은** 게임플레이 시스템을 구축합니다.

## When to Use

**Load this skill when:**
- 게임의 Core Loop(핵심 순환)를 정의/검증할 때
- Progression System(레벨, 스킬, 아이템 등 진행 체계)을 설계할 때
- 게임 밸런스(수치, 난이도, 경제)를 설계/조정할 때
- MDA Framework로 메커니즘을 분석/설계할 때
- 신규 메커니즘이 유저 행동(결제, 플레이시간, 중독성)에 미치는 영향을 평가할 때
- PvP/PvE 밸런스를 검토할 때

**Don't use for:**
- 순수 기술적 구현 (엔진/서버 → `game-tech-architecture` 참조)
- 수익화 전략 (`game-monetization-strategy` 참조)
- 내러티브/스토리 (`game-narrative-design` 참조)

## Prerequisites

1. **게임의 핵심 컨셉과 타겟 오디언스**가 정의되어 있어야 함 (`game-concept-validation` 참조)
2. **LDP 기본 프로토콜** 이해 (`lina-decision-protocol` 선행 로드 권장)
3. **기본 게임 디자인 이론**: MDA Framework, Core Loop, Player Psychology 기초 지식
4. **타겟 플레이어 프로파일**: Bartle 유형 분류(Achiever/Explorer/Socializer/Killer), 연령대, 플레이 패턴

## Procedure

### 1단계: Core Loop 설계 (Core Loop Design)

Core Loop는 **플레이어가 게임에서 반복하는 가장 기본적인 행동 순환**입니다.

1. **Loop 식별**: "플레이어가 게임에 들어와서 무엇을 반복하는가?"
   ```
   예시 (RPG): 탐험 → 전투 → 보상 획득 → 성장 → 더 강한 적 도전
   예시 (Match-3): 보드 관찰 → 매치 실행 → 점수 획득 → 목표 달성 확인
   예시 (배틀로얄): 낙하 → 수집 → 생존 → 축소 → 최종전
   ```

2. **Loop 구성 요소 검증 (4요소)**:
   - [ ] **Action (행동)**: 플레이어가 능동적으로 수행하는 것
   - [ ] **Feedback (피드백)**: 행동의 즉각적인 결과 (시각/청각/수치)
   - [ ] **Reward (보상)**: 행동으로 얻는 가치 (내재적/외재적)
   - [ ] **Motivation (동기)**: 다음 Action을 수행하게 하는 원동력

3. **Loop 깊이(Depth) 계층화**:
   ```
   Primary Loop (초기 루프): 30초~3분 내 완료 (예: 한 번의 전투)
   Secondary Loop (중간 루프): 10~30분 (예: 던전 클리어)
   Tertiary Loop (장기 루프): 세션~주 단위 (예: 캐릭터 성장)
   Meta Loop (메타 루프): 월~년 단위 (예: 순위/시즌)
   ```

4. **Loop 간 연결성 확인**: 상위 Loop가 하위 Loop의 결과를 의미있게 소비하는가?

5. **LDP §0 검증**: "이 Loop가 플레이어에게 '그만둘 수 없게' 만드는가, 아니면 '하고 싶게' 만드는가?"

### 2단계: MDA Framework 분석 및 설계 (MDA Framework)

MDA는 **Mechanics(메커니즘) → Dynamics(다이나믹스) → Aesthetics(미학)**의 3계층 분석 프레임워크입니다.

| 계층 | 정의 | 설계자 관점 | 플레이어 경험 |
|------|------|------------|--------------|
| **Mechanics** | 규칙, 알고리즘, 데이터 | "무엇을 만들었는가?" | 구체적으로 인식하지 못함 |
| **Dynamics** | Mechanics의 런타임 상호작용 | "어떻게 작동하는가?" | "게임이 이렇게 반응하네" |
| **Aesthetics** | 플레이어의 정서적 반응 | "어떤 감정을 주는가?" | "이 게임은 재밌어/긴장돼" |

**설계 방향 (Bottom-Up)**:
```
1. Aesthetics부터 역설계: "플레이어에게 어떤 감정을 주고 싶은가?"
   └── Fantasy, Challenge, Fellowship, Discovery, Expression, Narrative, Abandonment (8가지)

2. Dynamics 정의: "어떤 상호작용이 그 감정을 만들어내는가?"
   └── Time Pressure, Resource Management, Social Interaction, Information Asymmetry...

3. Mechanics 구현: "어떤 규칙과 시스템이 그 다이나믹스를 생성하는가?"
   └── Concrete rules, numbers, algorithms
```

**Aesthetics 8가지 카테고리 (Hunicke et al.)**:

| Aesthetic | 감정적 경험 | 대표 장르 |
|-----------|-----------|----------|
| **Sensation** | 게임세계 감각 경험 | 액션, 레이싱 |
| **Fantasy** | 상상 속 존재 되기 | RPG, MMORPG |
| **Narrative** | 드라마적 아크 | 어드벤처, 비주얼노벨 |
| **Challenge** | 장애물 극복 | 퍼즐, roguelike |
| **Fellowship** | 사회적 교류 | MMO, 협작 |
| **Discovery** | 새로운 것 발견 | 오픈월드, 메타바이스 |
| **Expression** | 자기 표현 | 샌드박스, 크리에이티브 |
| **Abandonment** | 몰입적 흐름(Flow) | 아케이드, 리듬 |

### 3단계: Progression System 설계 (진행 시스템)

Progression System은 **플레이어의 성장과 발전을 체계화한 시스템**입니다.

#### 3.1 진행 유형 선택

| 유형 | 특징 | 적합 장르 | LDP 고려사항 |
|------|------|----------|-------------|
| **Power Progression** | 수치적 성장 (레벨, 스탯) | RPG, MMORPG | Power Creep 관리 필수 |
| **Meta Progression** | 세션 간 영구적 성장 | Roguelike, Idle | FOMO 유발 가능성 |
| **Unlock Progression** | 콘텐츠 잠금 해제 | 모바일, 서비스형 | Pay-to-Win 위험 |
| **Skill Progression** | 플레이어 숙련도 | 대전, 스포츠 | 가장 윤리적 형태 |
| **Social Progression** | 사회적 지위/명성 | 소셜, 길드 | Peer Pressure 조작 위험 |

#### 3.2 곡선 설계 (Curve Design)

**경험치/성장 곡선 유형**:
```
Linear:        y = x          (지루함, 차별화 없음)
Quadratic:     y = x²         (초반 빠름, 후반 벽)
Exponential:   y = eˣ         (폭주, 밸런스 붕괴)
Logarithmic:   y = log(x)     (초반 벽, 후반 포화 — 권장)
S-Curve:       Logistic 함수   (초반 빠름 → 중반 선형 → 후반 포화 — 가장 자연스러움)
```

**권장 S-Curve 파라미터**:
```python
# Pseudo-code for S-Curve XP requirement
def xp_required(level):
    base = 100
    k = 0.05      # growth rate (높을수록 급격)
    midpoint = 50 # inflection point
    max_xp = 100000
    return int(max_xp / (1 + exp(-k * (level - midpoint))))
```

#### 3.3 보상 스케줄링 (Reward Scheduling)

| 스케줄 유형 | 특징 | 사용처 | LDP 위험도 |
|-----------|------|--------|-----------|
| **Fixed Ratio** | N번 행동마다 보상 | 퀘스트, 일일 과제 | 낮음 |
| **Fixed Interval** | 일정 시간마다 보상 | 일일 보상, 출석 체크 | 중간 |
| **Variable Ratio** | 확률적 보상 (가장 중독성 강함) | Gacha, 드롭, 랜덤 박스 | **높음** |
| **Variable Interval** | 불규칙한 시간 간격 보상 | 이벤트, 스폰 | 중간 |

**LDP 준수 원칙**: Variable Ratio는 **반드시 상한선(Cap) + 확률 공개(Pity System)**를 동반해야 함.

### 4단계: Balance Framework (밸런스 프레임워크)

#### 4.1 수치 밸런스 (Numerical Balance)

**기본 공식 (Damage Calculation Example)**:
```
Final Damage = Base Damage × (ATK / DEF) × Critical Multiplier
             × Element Modifier × Skill Modifier × Random Variance(±5%)
```

**밸런스 삼각형 (Balance Triangle)**:
```
      Attack
       / \
      /   \
Defense ← → Utility
```
- 어떤 옵션도 세 요소 모두에서 최고일 수 없음
- Trade-off이 명확해야 함

#### 4.2 Transitive vs Intransitive 밸런스

| 유형 | 정의 | 예시 | 장점 | 단점 |
|------|------|------|------|------|
| **Transitive** | A > B > C (순위 명확) | 무기 티어, 레벨 | 직관적, 명확 | Power Crep 발생 |
| **Intransitive** | 가위바위보 (순환) | RTS 유닛, Pokémon 타입 | 깊이 있음, 메타 변화 | 밸런스 난이도 높음 |

**권장**: 핵심 시스템은 Intransitive, 진행 시스템은 Transitive 혼합

#### 4.3 Rock-Paper-Scissors (RPS) 매트릭스 설계

```
          공격형    수비형    기술형    지원형
공격형    50%      승(70%)   패(30%)   무승부(50%)
수비형    패(30%)  50%       승(70%)   무승부(50%)
기술형    승(70%)  패(30%)   50%       무승부(50%)
지원형    무승부   무승부    무승부    50%
```

**밸런스 허용 오차**: 승率 45%~55% = 균형, 40%~60% = 용인, 그 외 = 수정 필요

#### 4.4 경제 밸런스 (Economy Balance)

**Faucet & Drain 모델**:
```
[ Faucets (유입) ]           [ Drains (유출) ]
- 몬스터 처치 보상            - 아이템 구매
- 퀘스트 완료 보상            - 수리비/강화비
- 일일/주간 보상              - 경매 수수료
- 초기 자본                   - 소모품 사용
-                           - 거래세

⚠️ 유입 > 유출 → 인플레이션 (화폐 가치 하락)
⚠️ 유출 > 유입 → 디플레이션 (신규 유저 진입 장벽)
✅ 목표: 유입 ≈ 유출 ±10%, 성장률은 플레이어 숙련도에 비례
```

### 5단계: LDP 윤리 검증 — 메커니즘 착취성 평가 (B-Category)

**B-Category 질문을 메커니즘에 적용:**

| LDP B-질문 | 메커니즘 적용 버전 | 체크 |
|-----------|-------------------|------|
| 메커니즘이 중독성 행동 패턴을 유도하는가? | Variable Ratio 스케줄링, Daily Login Bonus, Stamina System이 불안감/강박을 만드는가? | |
| 진행이 인위적으로 지연되어 결제를 유도하는가? | Energy/Stamina, Timed Wall, Grinding Wall이 "돈 내거나 기다려"를 강요하는가? | |
| 손실 회피(Loss Aversion)이 과도하게 활용되는가? | Battle Pass 잃어버림, Ranking Drop, Limited Offer FOMO? | |
| 사회적 압력(Social Pressure)이 결제를 강요하는가? | Guild Contribution, Leaderboard, Friend Gifting? | |

**윤리적 메커니즘 설계 원칙:**

1. **투명한 확률 (Transparent Probability)**: 모든 Random Mechanics의 확률 공개
2. **상한선 (Hard Cap)**: 무한 지출 방지 (월/일 상한)
3. **Pity System**: Variable Ratio에 반드시 보정 메커니즘 (예: 100번 시도 시 100% 확률)
4. **Graceful Exit**: 게임 종료가 자연스럽고 페널티가 없어야 함
5. **Play-to-Play, Not Pay-to-Play**: 기본 루프가 유료 없이 완결되어야 함

### 6단계: 프로토타이핑 & 반복 (Prototype & Iterate)

1. **Paper Prototype**: 종이/카드로 Core Loop 시뮬레이션 (1일 이내)
2. **Digital Prototype (Graybox)**: 최소 기능으로 루프만 구현 (1주 이내)
3. **Playtest (Internal)**: 팀원 5~10명, 녹화 + 설문
4. **Playtest (External)**: 타겟 오디언스 10~20명
5. **분석 지표**:
   - **Fun Score** (1~10): "다시 하고 싶은가?"
   - **Flow State 지속시간**: 몰입 상태가 얼마나 유지되는가?
   - **Friction Point**: 어디서 이탈/불편함이 발생하는가?
   - **Unexpected Emergence**: 예상치 못한 플레이 패턴 (버그 or 기회?)

6. **LDP Verdict 산출**:
   - **PASS**: 모든 B-Category 통과 + Core Loop가 재미/Fun을 입증
   - **REVIEW**: 부분적 우려 → 수정 후 재검토
   - **REJECT**: 구조적 착취성 발견 → 근본적 재설계

## Common Pitfalls

1. **Core Loop 부재 (Missing Core Loop)**: "우리 게임은 할 게임 너무 많아" → 루프가 흐릿함. → **해결**: "플레이어가 5분 동안 반복하는 한 가지 행동"을 먼저 정의.

2. **Complexity as Depth (복잡함 ≠ 깊이)**: 메커니즘이 너무 많아 학습 곡선이 가파름. → **해결**: "최소 학습으로 최대 표현" 원칙. 메커니즘 수는 3~7개 권장 (Miller's Law).

3. **Power Creep (파워 크리프)**: 신규 콘텐츠가 기존 콘텐츠를 무력화. → **해결**: Horizontal Progression(다양성)과 Vertical Progression(성장)의 비율을 6:4로 설정.

4. **Variable Ratio 남용 (Gacha Trap)**: 도박성 메커니즘을 핵심 루프에 배치. → **해결**: Variable Ratio는 Optional Content에만 배치. Core Loop는 Fixed Ratio 기반.

5. **One-True-Build (최적화 폭주)**: 하나의 빌드/전략만 유효하여 다양성 상실. → **해결**: Intransitive 밸런스 + 정기적 메타 시프트(Meta Shift).

6. **Pacing Collapse (페이싱 붕괴)**: 초반 너무 빠르거나 너무 느림. → **해결**: Engagement Curve 설계. 3분 내 첫 Dopamine Hit, 10분 내 첫 Small Win, 30분 내 Meaningful Progress.

7. **Artificial Scarcity (인위적 희소성)**: "24시간 한정" 같은 마케팅을 게임플레이 메커니즘에 주입. → **해결**: 희소성은 진정한 제약(Resource/Dev capacity)에서만 발생해야 함.

## Verification Checklist

이 스킬을 적용한 후 즉시 실행:

- [ ] **Core Loop(Step 1)**: Action→Feedback→Reward→Motivation 4요소가 모두 정의되었는가?
- [ ] **Loop Depth**: Primary/Meta Loop까지 4계층이 연결되었는가?
- [ ] **MDA(Step 2)**: Target Aesthetics → Dynamics → Mechanics가 Top-Down으로 정의되었는가?
- [ ] **Progression(Step 3)**: 진행 유형, 곡선, 보상 스케줄링이 문서화되었는가?
- [ ] **Balance(Step 4)**: RPS 매트릭스, Faucet&Drain, 수치 공식이 있는가?
- [ ] **LDP B-Category(Step 5)**: 4가지 메커니즘 윤리 질문에 답변이 있는가?
- [ ] **Prototype(Step 6)**: Paper Prototype 또는 Graybox Playtest가 수행되었는가?
- [ ] **Verdict 산출**: PASS/REVIEW/REJECT 판정과 점수(0~100)를 기록했는가?

---
*버전: 1.0.0 | 마지막 업데이트: 2026-05-09 | Linalab Game Dev Skill Set*
