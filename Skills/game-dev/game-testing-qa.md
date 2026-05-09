---
name: game-testing-qa
description: "게임 테스팅 및 QA 스킬 — 플레이테스트(알파/베타/접근성), 자동화 테스트(Unit/Integration/E2E), 회귀 테스트 전략, 버그 추적 시스템 통합(Jira/Linear/GitHub Issues), LDP B-category 윤리 검증(과도한 유입 유도/조작적 UI), C-category 종료 검증(서비스 중단 시 데이터 보존). When to Use, Prerequisites, Procedure, Pitfalls, Verification checklist 포함."
version: 1.0.0
author: Linalab Game Dev
license: MIT
tags: [game-dev, ldp]
related_skills: [lina-decision-protocol]
metadata:
  hermes:
    tags: [game-dev, qa, testing, playtesting, automation, regression, bug-tracking, ldp-ethics, accessibility, ci-cd]
    related_skills: [lina-decision-protocol, game-tech-architecture, game-mechanics-design, game-live-ops-playbook]
---

# 게임 테스팅 & QA (Game Testing & QA)

> **핵심 문장:**
> "테스트는 개발의 마지막 단계가 아니라 매일의 호흡이다. 좋은 QA는 버그를 찾는 것이 아니라, 플레이어가 겪을 불만을 미리 발견하고 — 특히 그 불만이 설계적으로 조작된 것인지 — 기계적으로 증명하는 과정이다."

이 스킬은 게임 개발 **전체 라이프사이클**에 걸친 테스팅 및 QA 워크플로우를 다룹니다. 단위 테스트부터 플레이테스트까지, 자동화 파이프라인부터 버그 추적 통합까지, 그리고 **LDP 윤리 기준**(조작적 UI 패턴, 과도한 유입 유도)을 QA 프로세스에 내장합니다.

## When to Use

**Load this skill when:**
- 게임 프로젝트의 QA 전략/테스트 계획을 수립할 때
- Alpha / Beta / Public Test 일정을 설계할 때
- CI/CD 파이프라인에 자동화 테스트를 통합할 때
- 버그 추적 시스템(Jira/Linear/GitHub)을 게임 개발에 맞게 구성할 때
- 출시 전 회귀 테스트(regression test) 우선순위를 정할 때
- 접근성(accessibility) 테스트가 필요할 때
- Live Ops 중 핫픽스/긴급 패치의 QA 범위를 결정할 때

**Don't use for:**
- 컨셉 단계 윤리 리뷰 → `game-ethics-review`
- 기술 스택 선택 → `game-tech-architecture`
- 메커닉스 밸런스 튜닝 → `game-mechanics-design`

## Prerequisites

1. **GDD v0.5 이상** — 핵심 메커니즘, 진행 구조, 승리 조건이 정의되어야 함
2. **빌드 파이프라인 동작 중** — CI에서 게임 빌드가 성공적으로 생성 가능해야 함
3. **버그 추적 도구 선정됨** — Jira, Linear, GitHub Issues, 또는 Taiga 중 하나
4. **타겟 플랫폼 목록** — 테스트 대상 디바이스/OS/해상도 범위 확인
5. **LDP 스킬 로드됨** — `lina-decision-protocol` §4(Ethics) + §5(Termination) 이해 필요

## Procedure

### Step 1: 테스트 매트릭스 설계 (Test Matrix Design)

게임의 **기능 영역 × 테스트 유형 × 플랫폼**으로 3차원 매트릭스를 구축합니다:

| 기능 영역 | Unit | Integration | E2E | Playtest | Accessibility |
|-----------|------|-------------|-----|----------|---------------|
| Core Loop (전투/이동/상호작용) | ✓ | ✓ | ✓ | ✓ | ✓ |
| UI/UX (메뉴/인벤토리/설정) | △ | ✓ | ✓ | ✓ | ✓ |
| 네트워크 (멀티/매칭/동기화) | ✓ | ✓ | ✓ | ✓ | — |
| 경제 시스템 (재화/상점/결제) | ✓ | ✓ | ✓ | ✓ | ✓ |
| 저장/로드 (클라우드/로컬) | ✓ | ✓ | ✓ | △ | ✓ |
| 라이브옵스 (이벤트/일일퀘스트/시즌) | △ | ✓ | ✓ | ✓ | ✓ |

**기호:** ✓=필수, △=권장, —=해당 없음

**출력:** Test Matrix 문서 — 각 셀에 우선순위(P0~P3)와 예상 소요 시간 할당

### Step 2: 자동화 테스트 계층 구축 (Automation Pyramid)

게임 테스트 자동화는 **피라미드 구조**로 구성합니다:

```
        ┌──────────────┐
        │  E2E Tests   │  ← 10% : 희소하게, 가장 느림
        │  (Playwright │     크리티컬 유저 경로만
        │   / GameCI)  │
        ├──────────────┤
        │ Integration  │  ← 30% : 시스템 간 상호작용
        │  Tests       │     서버-클라이언트, API, DB
        ├──────────────┤
        │  Unit Tests  │  ← 60% : 빠르고, 많고, 안정적
        │  (xUnit/Jest │     메커니즘 로직, 수식, 상태머신
        │   / NUnit)   │
        └──────────────┘
```

#### 2.1 Unit Test 작성 가이드

| 테스트 대상 | 도구 예시 | 커버리지 목표 | 주의사항 |
|------------|---------|--------------|---------|
| 메커니즘 수식 (데미지/경험치/확률) | NUnit / Jest / pytest | ≥90% | 경계값(edge case) 집중 |
| 상태 머신 (Idle→Run→Attack→Death) | StateMachineTester | 100% | 모든 전이(transition) 커버 |
| AI 행동 트리 (BT) 노드 | Mock-based | ≥80% | 무한 루프 타임아웃 필수 |
| 인벤토리/경제 로직 | Property-based | ≥85% | 음수/오버플로우 방어 |

#### 2.2 Integration Test 범위

- **클라이언트-서버 통신**: RPC 직렬화/역직렬화, 패킷 손실 시나리오
- **데이터 지속성**: 저장/로드 무결성, 클라우드 동기화 충돌 해결
- **결제 모듈**: IAP(In-App Purchase) sandbox 환경에서의 성공/실패/취소
- ** third-party SDK**: Analytics, AdMob, Social Login 연동

### Step 3: 플레이테스트 계획 (Playtest Planning)

#### 3.1 플레이테스트 단계별 목표

| 단계 | 참여자 | 목표 | 지표 | 산출물 |
|------|--------|------|------|--------|
| **Alpha (내부)** | 개발팀 5-10명 | 치명적 버그 제거 | Crash-free 시간 > 2시간 | Bug report 50건+ |
| **Closed Beta** | 친구/가족 20-50명 | 핵심 루프 재미 검증 | D1 Retention ≥40%, NPS | 설문 + 녹화 리플레이 |
| **Open Beta** | 일반 유저 500+명 | 부하 + 규모 테스트 | CCU 목표 달성률, Error rate < 1% | 서버 메트릭 + 유저 피드백 |
| **Soft Launch** | 1-2개국 제한 출시 | 문화권/디바이스 검증 | ROI 기반 KPI | A/B 테스트 결과 |

#### 3.2 플레이테스트 세션 설계

각 세션은 다음 구조를 따릅니다:

```
1. Pre-briefing (10분)  — 목적, 주시할 포인트, 금지사항 설명
2. Think-aloud 플레이 (30-60분) — 소리내며 생각하게 하고 녹화
3. Post-interview (15분) — 구체적 질문 (왜 그렇게 했는가?)
4. SRQ(Subjective Rating Questionnaire) (5분) — fun/frustration/confusion 1-7척도
5. 데브리핑 (15분) — 관찰팀 공유 + 액션 아이템 도출
```

### Step 4: 회귀 테스트 전략 (Regression Testing Strategy)

모든 빌드마다 **전체 테스트를 돌리는 것은 비현실적**입니다. 스마트한 우선순위 전략이 필요합니다:

#### 4.1 위험 기반 우선순위 (Risk-Based Prioritization)

| 위험 등급 | 기준 | 테스트 빈도 | 예시 |
|-----------|------|-----------|------|
| **P0 - Critical** | 크래시/데이터 손실/결제 오류 | **매 빌드** | 앱 시작, 저장/로드, IAP |
| **P1 - High** | 핵심 루프 차단 | 매 빌드 or daily | 전투, 레벨 완료, 보상 수령 |
| **P2 - Medium** | 기능 장애 but 우회 가능 | weekly | 설정 변경, 사운드 옵션 |
| **P3 - Low** | 미관/Cosmetic | release 전만 | 폰트 렌더링, 애니메이션 트윈 |

#### 4.2 Smoke Test Suite (빌드 게이트)

모든 CI 빌드가 통과하기 위한 **최소 15분 smoke test**:

```yaml
smoke_test_suite:
  - name: "앱 cold start"
    expected: "메인 화면 5초 이내 도달"
    timeout_sec: 30
  - name: "튜토리얼 완료"
    expected: "처음 플레이어가 튜토리얼을 끝까지 완료"
    timeout_sec: 300
  - name: "Core Loop 1사이클"
    expected: "전투 → 보상 → 성장 → 다음 전투 가능"
    timeout_sec: 120
  - name: "저장 → 재시작 → 로드"
    expected: "진행 상태 복원, 데이터 무결"
    timeout_sec: 60
  - name: "설정 → 언어 변경"
    expected: "UI 반영, 데이터 영향 없음"
    timeout_sec: 30
```

### Step 5: 버그 추적 시스템 통합 (Bug Tracking Integration)

#### 5.1 버그 라이프사이클

```
New → Triage → Confirmed → In Progress → Fix Verified → Closed
                ↕ Rejected          ↕ Won't Fix      ↕ Reopened
```

#### 5.2 버그 보고서 템플릿

| 필드 | 설명 | 예시 |
|------|------|------|
| **Title** | [영역] 짧은 요약 | `[Combat] 보스 광역 공격이 벽을 뚫고 hit 판정` |
| **Reproduction Steps** | 1-2-3-... 형태 | 1. 보스전 시작 2. 좌측 벽에 붙기 3. 광역 스킬 사용 |
| **Expected vs Actual** | 기대 vs 실제 | Expected: 벽 뒤에는 무피 / Actual: 3000 데미지 입음 |
| **Severity** | S1~S4 | S1(Crash/Data loss) / S2(Major feature broken) / S3(Minor) / S4(Cosmetic) |
| **Priority** | P0~P3 | 비즈니스 영향 기반 (Step 4.1 참조) |
| **Environment** | 디바이스/OS/빌드/해상도 | iPhone 14 Pro / iOS 17.4 / Build 0.5.12-a / 2796×1290 |
| **Attachment** | 스크린샷/로그/녹화 | crash log .txt + screen recording .mp4 |
| **LDP Flag** | 윤리/종료 관련 여부 | ☑ Ethics-relevant (조작적 UI) / ☐ Normal |

#### 5.3 버그 분류 자동화 (CI 통합)

버그 리포트가 생성될 때 **자동 태깅** 규칙:

```python
# 의사 코드 — CI hook 예시
def auto_tag_bug(report):
    tags = []
    if "crash" in report.title.lower() or report.log_contains("EXC_BAD_ACCESS"):
        tags.append("crash")
        tags.append("P0")
    if "payment" in report.area or "iap" in report.area:
        tags.append("financial")
        tags.append("P0")  # 결제 관련 = 최우선
    if is_manipulative_pattern(report):  # Step 6 참조
        tags.append("ldp-ethics-review-required")
    return tags
```

### Step 6: LDP B-category 윤리 검증 (Ethics Check in QA)

QA 단계에서 **조작적 디자인 패턴(manipulative patterns)**을 체계적으로 검출합니다.

#### 6.1 Dark Pattern 검출 체크리스트

| B-category 질문 | QA 검출 방법 | 판정 | 조치 |
|-----------------|-------------|------|------|
| **과도한 유입 유도 (Excessive Engagement)** | 세션 시간 분석: 평균 90분 초과 시 경고 로그 | ☐ Yes ☐ No ☐ Partial | Yes → 디자인 리뷰 요청 |
| **FOMO(놓치면 손해) 강박** | 한정/카운트다운 UI가 3곳 이상이면 플래그 | ☐ Yes ☐ No ☐ Partial | Yes → `game-ethics-review` 에스컬레이션 |
| **결제 장벽 낮추기 (Friction Reduction)** | 구매까지의 tap 수가 3번 미만이면 플래그 | ☐ Yes ☐ No ☐ Partial | Yes → 의도적 확인 단계 추가 권고 |
| **잃어버린 근성(Loss Aversion) 악용** | "지금 안 사면 영구 손해" 식 카피 검출 | ☐ Yes ☐ No ☐ Partial | Yes → 카피 수정 + LDP 문서화 |
| **Social Pressure (사회적 압박)** | "친구들이 이미 구매했습니다" 싯다운 검출 | ☐ Yes ☐ No ☐ Partial | Yes → 프라이버시/윤리 리뷰 |
| **Random Reward 조작 의심** | 확률형 아이템의 RNG seed 검증 가능 여부 | ☐ Yes ☐ No ☐ Partial | No(검증 불가) → 투명성 공개 요구 |

#### 6.2 윤리 플래그 처리 프로세스

```
QA Tester가 Dark Pattern 발견
    ↓
Bug Report에 [ldp-ethics-review-required] 태그 부착
    ↓
Triage 단계에서 game-ethics-review 스킬 에스컬레이션
    ↓
Design Lead + Ethics Reviewer 공동 판정
    ↓
Fix / Accept with Disclosure / Reject(출시 보류)
```

### Step 7: LDP C-category 종료 검증 (Termination Check in QA)

서비스 종료 시나리오를 QA 단계에서 **미리 검증**합니다.

| C-category 질문 | QA 검증 방법 | 판정 | 조치 |
|-----------------|-------------|------|------|
| **서비스 종료 시 플레이어 데이터 내보내기 가능?** | Account → Export Data 기능 테스트 | ☐ Pass ☐ Fail ☐ N/A | Fail → 기능 추가 (출시 전 필수) |
| **오프라인 플레이 경로 존재?** | 서버 차단 후 싱글플레이 동작 확인 | ☐ Pass ☐ Fail ☐ N/A | Fail → 오프라인 모드 설계 검토 |
| **구매한 콘텐츠의 소유권 명확?** | EULA/TOS에 종료 시 환불/보상 조항 확인 | ☐ Pass ☐ Fail ☐ Partial | Partial → 법무 리뷰 |
| **커뮤니티 마이그레이션 경로?** | Discord/포럼 데이터 export 가능 여부 | ☐ Pass ☐ Fail ☐ N/A | Fail → 커뮤니티 플랫폼 선택 시 고려 |

### Step 8: 접근성 테스트 (Accessibility Testing)

LDP의 **포괄적 설계(Inclusive Design)** 원칙을 QA에 통합합니다:

| 접근성 영역 | 테스트 항목 | 도구 | 통과 기준 |
|------------|-----------|------|----------|
| **시각** | 색대비 (WCAG AA 4.5:1) | Contrast Checker | 모든 UI 요소 통과 |
| **시각** | 스크린 리더 호환 | VoiceOver/TalkBack | 모든 인터랙티브 요소 label 존재 |
| **청각** | 자막/시각적 피드백 | 수동 체크 | 모든 오디오 cue에 시각 대안 |
| **운동** | 터치 타겟 크기 (44×44pt min) | Layout Inspector | 모든 버튼 최소 크기 충족 |
| **인지** | UI 복잡도 (동시 정보량) | Cognitive Walkthrough | 1화면 7±2 요소 이내 |
| **색각** | 색만으로 정보 전달 여부 | Grayscale 변환 | 색 제거 후에도 정보 전달 가능 |

## Pitfalls

1. **"플레이테스트 = 버그 찾기" 오류 (Playtest Scope Creep)** — 플레이테스트의 1차 목적은 **"재미인가?"**입니다. 버그 찾기는 자동화 테스트의 몫입니다. 두 목적을 섞으면 플레이어가 "테스터 모드"로 전환되어 자연스러운 플레이 데이터를 잃습니다. **목적을 명확히 분리하세요.**

2. **자동화 과신 (Automation Overconfidence)** — 게임은 렌더링/물리/입력이 얽힌 복잡계입니다. UI 자동화는 깨지기 쉽고(셀렉터 변경, 프레임 타이밍), 게임플레이 E2E는 false negative가 많습니다. **자동화 커버리지 70%를 목표로 하고, 나머지 30%는 사람이 테스트하는 것이 현실적입니다.**

3. **회귀 테스트 무게 불균형 (Regression Bloat)** — 매주 테스트 케이스가 20%씩 늘어나면 3개월 뒤엔 실행 불가능해집니다. **매 sprint마다 테스트 케이스를 audit해서 P3 이하를 폐기(deprecate)하거나 자동화로 대체하세요.** "옛날에 한 번 걸렸던 버그"를 평생 테스트할 필요는 없습니다.

4. **버그 Severity와 Priority 혼동 (Severity-Priority Conflation)** — Severity는 **기술적 영향**(Crash=S1), Priority는 **비즈니스 영향**(출시 차단=P0)입니다. S1 버그라도 출시 후 핫픽스로 해결 가능하면 P2일 수 있습니다. **두 축을 독립적으로 평가하세요.**

5. **윤리 검증을 "기능"으로 취급 (Ethics as Feature)** — Dark Pattern 검출을 일반 버그와 같은 우선순위 큐에 넣으면 언제나 밀려납니다. **LDP ethics 관련 건은 별도 에스컬레이션 경로(Step 6.2)를 가져야 합니다.** 일반 triage에서 해결할 수 있는 문제가 아닙니다.

6. **접근성을 "나중에" 추가 (Accessibility Afterthought)** — 출시 2주 전에 WCAG를 맞추려는 것은 불가능에 가깝습니다. **Step 1 매트릭스 설계 단계에서 접근성을 P1으로 포함하세요.** 접근성은 기능이 아니라 품질 기준입니다.

## Verification Checklist

- [ ] **Test Matrix** 작성 완료 — 기능 영역 × 테스트 유형 × 플랫폼, P0~P3 우선순위 할당
- [ ] **Automation Pyramid** 구축 — Unit 60% / Integration 30% / E2E 10% 비율, CI에 통합
- [ ] **Smoke Test Suite** 작성 — 15분 이내 완료 가능, 모든 CI 빌드 게이트 적용
- [ ] **Playtest Plan** 수립 — Alpha/Beta/Open Beta/Soft Launch 4단계 일정 + 목표
- [ ] **Bug Tracker** 구성 — 템플릿, 라이프사이클, 자동 태깅 규칙 적용
- [ ] **B-category Ethics Check** 6개 Dark Pattern 항목 전부 판정 완료
- [ ] **C-category Termination Check** 4개 종료 시나리오 항목 전부 판정 완료
- [ ] **Accessibility Audit** 6개 영역(시각/청각/운동/인지/색각) 테스트 완료
- [ ] **Regression Strategy** 문서화 — Risk-based 우선순위 + 주기적 케이스 audit 계획
- [ ] **CI Pipeline**에서 테스트 결과 자동 보고 — 팀 채널(Slack/Discord)로 알람 통합
