---
name: game-ethics-review
description: "LDP SS6-SS8 (Ethics) — 게임 윤리 리뷰: 수익화 윤리(gacha, FOMO, 손실 회피, 사회적 압력), 플레이어 심리 조작, 다크 패턴 탐지"
version: 1.0.0
tags: [game-dev, ldp]
related_skills: [lina-decision-protocol]
---

# 게임 윤리 리뷰 (Game Ethics Review)

**LDP SS6-SS8 | Monetization Ethics · Dark Pattern Detection · Player Psychology**

---

## When to Use (사용 시점)

- **SS6**: 수익화 모델 설계 완료 후 — gacha, battle pass, season pass, loot box 등 도입 전
- **SS7**: UI/UX 디자인 완료 후 — 다크 패턴(dark patterns) 감사 필요시
- **SS8**: 라이브 오픈 전 최종 윤리 검수 단계
- 기존 게임의 수익화 정책 변경 또는 새로운 결제 시스템 추가시
- 규제 기관(GRAC, ESRB, PEGI) 심의 전 사전 자체 점검

## Prerequisites (선행 조건)

| 항목 | 설명 |
|------|------|
| Game Design Document (GDD) | 수익화 모델이 명시된 GDD v1.0 이상 |
| Monetization Spec | 가격 책정, 확률 표, VIP 등급 혜택 상세 |
| UI/UX Wireframes | 상점, 결제 플로우, 팝업/알림 화면 |
| Target Audience Profile | 연령대, 지역, 결제 능력 프로필 |
| Regulatory Requirements | 대상 국가별 법률(확률 공개 의무, 미성년자 보호 등) |

## Procedure (절차)

### Phase 1: 수익화 윤리 평가 (Monetization Ethics Assessment)

1. **Gacha/RNG 시스템 분석**
   - [ ] 드롭 확률(pity system 포함)이 공개되었는가?
   - [ ] 최대 투자금액으로 목표 아이템 획득 가능 여부 계산
   - [ ] Pity count가 합리적인가? (일반: 50~150 draws, hard pity: 300 draws 내외)
   - [ ] 중복 아이템 처리 방식이 공정한가?

2. **FOMO(Fear Of Missing Out) 요소 식별**
   - [ ] 한정 타이머(limited timer) 사용 빈도와 강도 체크
   - [ ] "지금만", "마감 임박" 같은 문구 목록화
   - [ ] 일일/주간 리셋 스케줄이 과도한가?
   - [ ] Seasonal content 만료 후 재등장 가능성 확인

3. **손실 회피(Loss Aversion) 메커니즘 감사**
   - [ ] 구매하지 않을 경우 "잃게 되는 것"을 강조하는 카피 확인
   - [ ] 이미 투자한 금액(sunk cost)을 부각하는 UI 요소 식별
   - [ ] 환불 불가 정책이 적절히 고지되는가?
   - [ ] Virtual currency 소진 유도 메커니즘(잔돈 남기기) 평가

4. **사회적 압력(Social Pressure) 요소 분석**
   - [ ] 길드/클랜 기여도에 따른 결제 유도 여부
   - [ ] 랭킹/리더보드에서의 사회적 낙인(stigma) 우려
   - [ ] 친구 초대 보상이 과도한가?
   - [ ] 공유/선물 기능이 peer pressure를 조장하는가?

### Phase 2: 다크 패턴 탐지 (Dark Pattern Detection)

5. **Deceptive Design Patterns 점검표** (Brignull Classification 기반)
   - [ ] **Bait and Switch**: 실제 제품과 다른 광고/프리뷰
   - [ ] **Confirmshaming**: 거부 버튼에 부정적 문구("아니요, 저는 혜택을 놓치겠습니다")
   - [ ] **Disguised Ads**: 콘텐츠처럼 보이는 광고
   - [ ] **Forced Continuity**: 해절 어렵게 만든 구독/결제
   - [ ] **Friend Spam**: 친구 초대 강제
   - [ ] **Hidden Costs**: 결제 직전 추가 비용 노출
   - [ ] **Misdirection**: 주요 행동(CTA)을 눈에 띄지 않게 처리
   - [ ] **Privacy Zuckering**: 기본 설정으로 개인 정보 과다 수집
   - [ ] **Price Comparison Prevention**: 가격 비교 어렵게 만듦
   - [ ] **Roach Motel**: 진입은 쉽지만 탈출이 어려운 구조
   - [ ] **Sneak into Basket**: 동의하지 않은 항목 장바구니 추가
   - [ ] **Trick Questions**: 기본 선택이 불리한 체크박스/토글

6. **UI Flow 분석**
   - 결제 경로에서 단계별 스크린샷 촬영 및 분석
   - 각 화면의 CTA(Call-to-Action) 배치와 색상 심리학 평가
   - 취소/환불 경로의 접근성(accessibility) 확인

### Phase 3: 플레이어 심리 영향 평가 (Player Psychology Impact)

7. **Cognitive Load Assessment**
   - [ ] 정보 과부하(information overload)으로 인한 충동 구매 유발 여부
   - [ ] 복잡한 통화兑换率(exchange rate)으로 가격 인식 왜곡 여부
   - [ ] Decision fatigue 유발 요소 식별

8. **Vulnerability Protection Check**
   - [ ] 미성년자 보호 장치(결제 한도, 부모 동의) 작동 여부
   - [ ] 도박 중독 위험군을 위한 self-exclusion 기능
   - [ ] 과도한 playtime/과도한 지출 알림(alert) 시스템

9. **윤리 점수 산출 (Ethics Scorecard)**

   | 카테고리 | 가중치 | 점수 (1-5) | 가중 점수 |
   |----------|--------|-----------|-----------|
   | 투명성 (Transparency) | 25% | ___ | ___ |
   | 공정성 (Fairness) | 25% | ___ | ___ |
   | 책임감 (Responsibility) | 25% | ___ | ___ |
   | 존엄성 (Respect) | 25% | ___ | ___ |
   | **총계** | 100% | | **___ / 5.0** |

   - **4.0+**: ✅ 통과 — 라이브 진행 가능
   - **3.0~3.9**: ⚠️ 조건부 통과 — 수정 후 재심
   - **3.0 미만**: ❌ 불합격 — 근본적 재설계 필요

## Pitfalls (주의사항)

1. **규제 변화 무시**: 한국 게임법, EU DSA(Digital Services Act), 중국 loot box 규제 등은 자주 변경됨. 심의 직전 최신 법률 확인 필수.

2. **"경쟁사도 하니까" fallacy**: 경쟁사가 다크 패턴을 사용한다고 해서 정당화되지 않음. 장기적으로 브랜드 신뢰도와 규제 리스크 증가.

3. **디자이너 vs 윤리 담당 갈등**: UX 최적화(conversion rate)와 윤리는 trade-off 관계. 사전에 합의된 ethics threshold를 문서화하여 갈등 방지.

4. **미성년자 타겟팅 누락**: 전연령 게임인 경우 특히 주의. GRAC 등급 심의 기준보다 엄격한 내부 기준 적용 권장.

5. **확률 조작 의혹**: 서버측 RNG 로그 audit trail 없이 "운영팀이 확률을 조작했다"는 음모론 대응 불가. 반드시 third-party audit 가능한 구조 설계.

## Verification Checklist (검증 체크리스트)

- [ ] 모든 RNG 기반 결제의 확률 표가 in-game에 공개됨
- [ ] 다크 패턴 점검표에서 0개의 Critical 항목, 2개 이하의 Moderate 항목
- [ ] Ethics Scorecard 3.5 이상 획득
- [ ] 미성년자 보호 장치가 대상 연령대에 적합하게 구현됨
- [ ] 환불/취소 경로가 3단계 이내로 접근 가능
- [ ] 관련 법률(대상 국가 전체) 준수 확인서 서명
- [ ] 윤리 리뷰 결과가 GDD에 반영되어 버전업됨
- [ ] 향후 6개월간 분기별 재심(re-audit) 일정 확정
