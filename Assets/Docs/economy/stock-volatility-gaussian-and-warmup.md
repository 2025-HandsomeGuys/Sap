# 주식 변동성 — 가우시안 워크 전환 + 시작 예열 (2026-08-15)

> **⚠ 2026-08-25 재튜닝 — 변동폭 축소(이 문서의 원래 방향과 반대):** "80% 상승도 보인다, 기본
> ±10~20%·뉴스 포함 ±30~50%로 줄여줘." 조정한 상수(모두 `StockPriceEngine`):
> - `NoiseSigmaFactor` 0.72 → **0.58** (기본 평균 틱변동 ~13%)
> - **`AmbientMaxMove` = 0.20 신설** — 기본(뉴스X) 틱변동의 절대 상한. 예전엔 고변동주가
>   `dailySigma×2.5`로 **±80%까지** 튀었다(= 사용자가 본 "80% 상승"의 정체).
> - `NewsTickCap` 0.7 → **0.35**, `NewsEffectMultiplier` 4.5 → **3.0** (뉴스 성분 ~30~35%)
> - **`MaxTickChange` = 0.40 신설** — hype·희귀점프·칼날까지 합친 **한 틱 총변동 하드캡**(요청: 최대 ±40%).
>   hype(상한 우회)와 점프도 이제 한 틱 ±50%를 못 넘는다(hype의 '여러 틱 누적'은 유지).
> - 희귀점프 범위 +100%/−60% → **+45%/−40%** (하드캡에 안 잘리도록).
>
> 측정(신규): 기본 틱 최대 20%(저변동주 평균 ~6%·고변동주 ~18%), 뉴스 틱 평균 ~38%·최대 50%.
> 아래 본문의 "평균 20~30%·뉴스 50~100%" 수치는 **옛 방향**이므로 지금과 다르다.

## 배경 / 요구
"평균 일변동이 낮게 느껴진다. 기본 흔들림을 평균 20~30%, 뉴스 터진 날은 50~100%까지.
원가에서 멀어지면 천천히 자연스럽게 복귀하는 구조는 유지. 그리고 새 게임을 시작해
바로 주식창을 열어도 1틱짜리 밋밋한 그래프가 아니라 20틱쯤 진행된 살아있는 차트가 보이게."

## 변경 전 상태
`StockPriceEngine.ProcessTick`의 평상시 노이즈가 **균등난수**였다.
```
maxChangePercent = clamp(volatility, 0.20, 0.45)
baseNoise = Random(-1,1) * maxChangePercent * 0.75   // NoiseFraction
```
- 기대 |일변동| = 0.375 × cap. 종목 volatility 평균 0.29 → **평균 ≈ ±11%**.
- 문제 3가지:
  1. **균등분포 = 떨림.** +5%와 +25%가 같은 확률 → 추세감 없이 매일 널뜀.
  2. **상한 벽.** 평균을 상한 근처로 올리면 매일 clamp에 붙어 인위적.
  3. **뉴스가 상한 안에 갇힘.** 뉴스 효과도 `maxChangePercent`에 clamp돼 크게 못 튐.

## 변경 후 — 가우시안 로그워크 + 뉴스 별도 상한

### 1) 평상시 노이즈 = 정규분포(Box-Muller)
```
dailySigma = max(0.01, Volatility × NoiseSigmaFactor)   // NoiseSigmaFactor = 1.1
baseNoise  = NextGaussian() × dailySigma
기대 |일변동| ≈ 0.8 × dailySigma   (E|N(0,σ)| = σ√(2/π) ≈ 0.7979σ)
```
종목 volatility 평균 0.29 → 평균 일변동 **≈ 25%** (저변동주 ~12%, 고변동주 ~42%).
"대부분 중간, 가끔 큼"의 종 모양 → 자연스러움.

### 2) 평상시 상한은 '벽'이 아니라 '안전 레일'
```
ambientCap = dailySigma × AmbientCapSigma   // AmbientCapSigma = 2.5
ambient    = clamp(noise + sentiment + momentum + reversion, ±ambientCap)
```
평균(≈0.8σ)보다 훨씬 위(2.5σ ≈ 상위 1%)라 평소엔 clamp가 거의 안 문다 → 벽 느낌 없음.

### 3) 뉴스는 평상시 상한과 별개의 더 큰 자기 상한
```
news = clamp(newsEffect, ±NewsTickCap)      // NewsTickCap = 0.7
totalChange = ambient + news + hypeEffect + rareJump
```
뉴스 날 = 평상시 ±25% + 뉴스 ±최대 70% → **자연스럽게 50~100% 스윙**.
급등주(HypeBypass) / 희귀점프 우회 경로는 그대로.

### 4) 평균회귀 데드밴드 ±15% → ±35%
노이즈가 커졌으므로 데드밴드를 넓혀야 평상시 흔들림에 회귀가 매 틱 발작(whipsaw)하지 않는다.
평소엔 자유롭게 떠다니고, **뉴스급 >35% 이탈만** 램프업 회귀로 되돌린다.
`ReversionBase/RampPerTick/Max/DownsideBoost`는 그대로 → "멀수록 세게, 가까우면 스르륵" 복귀 유지.
(회귀 램프 자체 설계는 `stock-mean-reversion-ramp.md`)

## 시작 예열 (WarmUp)
`StockPriceEngine.WarmUp(ticks)` 신설. `StockGameManager.InitializeSystem`에서
**세이브가 없는 새 게임일 때만**, 리스너에게 알리기 전(`OnSystemInitialized` 앞)에서
`WarmUp(20)` 호출.
- 뉴스 없이(`s_emptyNews`), 중립 심리(0.5)로 `ProcessTick`을 20회 → PriceHistory가 21포인트.
- `CurrentTick`은 0 유지 → 게임적으로는 방금 시작(뉴스 fresh). 요구사항 그대로: "뉴스는 없지만 주식만 20틱 진행".
- **예열 중 희귀 점프는 끈다**(`_suppressJumps`). 플레이어가 아무것도 안 했는데 시작 보드에
  잭팟 종목이 뜨는 걸 방지 → '평범한 시작 시세'.
- 세이브 로드 시엔 `_pendingSaveData != null`로 스킵(어차피 RestoreCompanyPrices가 히스토리를 덮음).

## 튜닝 다이얼
- 평균 일변동 조절: `NoiseSigmaFactor`(현 1.1). 1.0→평균 ~23%, 1.2→~28%.
- 평상시 극단 허용: `AmbientCapSigma`(현 2.5). 낮추면 큰 날이 줄고 벽에 더 자주 붙음.
- 뉴스 최대 폭: `NewsTickCap`(현 0.7).
- 회귀가 붙기 시작하는 이탈폭: `ReversionDeadbandPct`(현 0.35).
- 예열 길이: `StockGameManager.StockWarmupTicks`(현 20).

## ⚠ 동작 변화 (회귀 의심 시 여기부터)
- 일변동 평균이 ±11% → ±25%로 커짐. 밸런스(주식 수입 EV)가 함께 커진다 —
  코인과의 역할 분리(`stock-vs-coin-role-and-tuning.md`) 재점검 필요할 수 있음.
- 뉴스 효과가 이제 상한 밖(±0.7)이라 뉴스 날 변동이 눈에 띄게 커진다.
- 새 게임 첫 화면의 주가가 BasePrice가 아니라 ±수십% 흩어진 값에서 시작한다(의도).
