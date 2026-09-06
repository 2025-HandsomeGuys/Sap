# NEON 마켓 — 주식·코인 통합 단말기 씬 설계

> 거래소 단말기(모니터) 컨셉의 **다크 레트로** UI 씬. 상단 토글로 **마켓 안의 두 계열**을 전환한다:
> **주식 계열**(기존 시뮬레이션 리테마) ↔ **코인 계열**(신규 도박 미니게임).
> 레퍼런스 디자인: `NEON 마켓 / TRADING TERMINAL` (모니터 베젤).

이 문서는 **확정용 설계 문서**다. 명세 확정 → C# 코드 → 에디터 씬 제작 순서로 진행한다.
`.unity` 씬·프리팹 배치는 사람이 에디터에서 수행(§11), Claude는 C# 스크립트와 이 문서를 담당한다.

---

## 0. 한눈에 보기

| 항목 | 내용 |
|------|------|
| 씬 이름 | `MarketScene` (Assets/Scenes/Demo/MarketScene.unity) |
| 진입 | 정착지의 **거래소 단말기** 오브젝트 상호작용 → MarketScene **Additive 로드** |
| 구조 | **마켓(상위)** 안에 **주식 계열 / 코인 계열** 두 세부 계열. 상단 세그먼트 토글로 전환 |
| 주식 계열 | **기존 `Stock.*` 시스템 재사용** + 다크 레트로 리테마 (탭: 시세/뉴스/포트폴리오) |
| 코인 계열 | **신규** 도박 미니게임. 명명된 코인 10종+ (위험도 차등). **하루 1코인 잠금 + 하루 N판 제한**, 단판 라운드 반복 |
| 코인 규칙 | **승률(부호) 50% 고정**(전 코인). 손익은 **비대칭 페이아웃**(winCap<lossCap)+**하우스 엣지**+**양방향 베팅세**+**연승 감쇠**+**레버리지**로 **EV 항상 마이너스**(수입원 방지). ±100% 극단(상장폐지/떡상). **일일 출금 한도**(첫 베팅액×배수)·**소프트 초과청산**(빚 없음) |
| 동적 로스터 | 상장폐지(−100%) 시 그 **슬롯에 새 코인이 새 이름으로 교체 상장** (위험 티어·가격대 유지, 영속) — §7.7 |
| 화폐 | 기존 `PlayerStat`(Gold) 그대로 (`SpendGold`/`AddGold`/`OnGoldChanged`) |
| 날짜 | 기존 `DayCycleManager.CurrentDay`로 하루 1코인 잠금 처리 |
| 세이브 | 신규 `CoinSaveData`를 기존 `SaveManager` 파이프라인에 연동 (StockSaveData와 동일 패턴) |
| 로컬라이제이션 | **주식=이미 KR/EN/CN 정상 적용.** 코인=`CoinLoc`+`Coin_Localization.csv`+`LanguageManager` 한 줄로 신규 연동 (§10.1) |
| 신규 코드 | `Assets/Scripts/Coin/**`, `Assets/Scripts/Market/**` |
| 수정 코드 | `Stock.UI` 색상 리테마 + `StockUIController` 씬 모드 대응 + `LanguageManager`(코인 CSV 로드) |

---

## 1. 목표와 범위

### 목표
1. 주식과 코인을 한 화면(마켓)에서 **두 세부 계열**로 다루는 **단일 전용 씬**.
2. 레퍼런스의 **다크 레트로 단말기** 룩을 두 계열에 일관 적용.
3. 코인을 **빠르게 끝나는 사이드 도박 미니게임**으로 신규 구현 — 자유 베팅, 코인별 위험도(변동성) 차등, ±100% 극단 이벤트, 선택 시 실시간 그래프 반응, 재미 요소.

### 범위에 포함
- 다크 레트로 디자인 스펙(팔레트·타이포·공통 컴포넌트)
- 씬 GameObject 계층 + 컴포넌트/스크립트 매핑
- 코인 미니게임 규칙·수학·시뮬레이션 엔진·재미 요소
- 신규/수정 스크립트 명세, 세이브·골드·날짜 연동
- 에디터 제작 절차, 구현 체크리스트, 밸런스 노트

### 범위에서 제외
- 주식 **시뮬레이션 로직** 변경 (가격 엔진/뉴스/심리는 그대로 — 색·레이아웃만 리테마)
- `.unity` 씬·프리팹 실제 배치 (사람이 §11 절차로 수행)

---

## 2. 씬 진입·이탈 흐름

```
[정착지 씬]  거래소 단말기 (MarketTerminalInteractable)
     │  상호작용(E)
     ▼
SceneManager.LoadSceneAsync("MarketScene", Additive)
     │  - 플레이어 씬 유지 → PlayerStat / StockGameManager / DayCycleManager 참조 유지
     │  - 플레이어 입력 일시정지
     ▼
[MarketScene]  MarketSceneController.Enter()
     │  기본 계열 = 주식. 토글로 주식↔코인 전환
     ▼
닫기/ESC → Exit() → SceneManager.UnloadSceneAsync("MarketScene")
     ▼
[정착지 씬]  입력 복귀
```

설계 근거:
- `StockGameManager`·`DayCycleManager`는 `DontDestroyOnLoad` 싱글톤 → 씬 전환과 무관하게 살아있다. MarketScene은 **UI와 코인 매니저만** 들면 된다.
- `PlayerStat`은 `FindFirstObjectByType<PlayerStat>`로 해석(기존 패턴). **Additive 로드**여야 플레이어 씬의 `PlayerStat`을 찾으므로 단독 로드보다 **Additive 오버레이 권장**.

> 레퍼런스의 모니터 베젤/받침은 "거래소 단말기 화면" 컨셉. 기존 `Assets/Scenes/Demo/MonitorScene.unity`를 출발점으로 삼거나 MarketScene을 신규로 만든다.

---

## 3. 다크 레트로 디자인 스펙

### 3.1 팔레트 (레퍼런스에서 추출)

| 토큰 | Hex | 용도 |
|------|-----|------|
| `screenBg` | `#0D1117` | 화면(모니터 내부) 배경 |
| `panelBg` | `#151B26` | 패널 배경 |
| `panelBgAlt` | `#11161F` | 패널 내부 섹션(호가·뉴스·베팅 카드) |
| `panelBorder` | `#243043` | 패널 테두리(은은) |
| `rowSelected` | `#1B2A3D` | 선택된 리스트 행/카드 배경 |
| `accentCyan` | `#38BDF8` | 활성 탭·선택 좌측 액센트 바 |
| `accentTeal` | `#2DD4BF` | 보조 강조(차트 글로우) |
| `brandA`/`brandB` | `#5B6CF0`/`#8B5CF6` | 로고 그라데이션(파랑→보라) |
| `gold` | `#F5C542` | 골드 표시·활성 알약 |
| `up` (상승) | `#3FB950` | ▲ 상승·매수·UP |
| `down` (하락) | `#F85149` | ▼ 하락·매도·DOWN |
| `textPrimary` | `#E6EDF3` | 본문 |
| `textMuted` | `#8B98A9` | 보조 라벨 |
| `textDim` | `#5A6678` | 흐린 보조(단위·플레이스홀더) |
| `badgeRed` | `#F85149` | 뉴스 카운트 뱃지 |
| `chipBg` | `#1E2733` | 태그 칩 배경 |

**시장 심리 그라데이션 바**(주식): 좌→우 `#F85149`(공포)→`#F0883E`→`#F5C542`→`#3FB950`(탐욕) + 흰 마커.

### 3.2 타이포그래피
- **숫자 강조(가격·손익·잔액)**: 픽셀/레트로 비트맵 TMP 폰트(예: Galmuri11/NeoDunggeunmo → TMP Font Asset). `₩46,100`의 블록감. 한글·라틴·숫자 글리프 포함.
- **한글 본문/라벨**: 깔끔한 볼드 산세리프(기본 TMP 한글 폰트).
- 위계: 종목/코인명 Bold 32~40, 대형 가격 픽셀 48~64, 라벨 18~22, 보조 16. 영문 소제목은 대문자+넓은 자간.

### 3.3 공통 컴포넌트(프리팹화 권장)
- **PanelFrame**: 라운드 9-slice, `panelBg`+`panelBorder`, 선택적 시안 글로우.
- **SegmentedToggle**(주식/코인): 활성=`gold` 채움+어두운 텍스트, 비활성=투명+`textMuted`.
- **PixelButton**: `Assets/DEVNIK 2D/2D UI PIXEL BUTTONS` 활용. 상태별 색조.
- **TagChip**: `chipBg`+라운드+`textMuted`.
- **RiskGauge**: 코인 카드용 위험도 게이지(낮음=시안/초록 → 높음=주황/빨강 막대).
- **ScanlineOverlay**(CRT 연출, 선택): 주사선 텍스처 알파 5~8% + 비네트, `Raycast Target=Off`.
- **MonitorBezel**(선택): 받침 + `● NEONTECH` 라벨. 순수 장식.

모든 색은 **`MarketTheme`(§8.1)** 중앙 팔레트에서 가져온다. 프리팹 하드코딩 금지.

---

## 4. 씬 GameObject 계층

```
MarketScene
├─ EventSystem
├─ MarketSceneController            ← 최상위 오케스트레이터 (§8.2)
├─ Canvas (Screen Space-Overlay, CanvasScaler 1920x1080 / Match 0.5)
│   ├─ MonitorBezel (장식, 선택)
│   ├─ ScreenRoot (Image: screenBg)
│   │   ├─ TopBar
│   │   │   ├─ Brand ("N" 로고 + "NEON 마켓 / TRADING TERMINAL")
│   │   │   ├─ ModeToggle (SegmentedToggle: [주식][코인])   ← §5
│   │   │   └─ GoldDisplay (코인 아이콘 + "{0:N0} G")        ← PlayerStat.Gold 구독
│   │   │
│   │   ├─ StockModeRoot          ← 주식 계열 (기존 UI 이식 + 리테마, §6)
│   │   │   ├─ StockUIController (기존)
│   │   │   ├─ TabBar ([시세][뉴스 ●n][포트폴리오]) + 시장심리 바
│   │   │   ├─ StocksTab (StockListUI 좌 + CompanyDetailUI 우)
│   │   │   ├─ NewsTab (NewsTabUI)
│   │   │   └─ PortfolioTab (PortfolioUI)
│   │   │
│   │   └─ CoinModeRoot           ← 코인 계열 (신규, §7)
│   │       ├─ CoinModeUI (§8.5)
│   │       ├─ CoinSelectView (CoinSelectUI)               ← 코인 라인업(좌측 목록)
│   │       │   └─ CoinCard(프리팹) × 10+ (CoinCardUI: 영문명/위험게이지/변동폭)
│   │       └─ BettingView (CoinBettingUI)                 ← 베팅 테이블
│   │           ├─ CoinChartUI (실시간 캔들/라인 차트, §7.5)
│   │           ├─ NewsTicker (가짜 코인 뉴스 흐름, 연출)   ← 재미요소
│   │           ├─ StakePanel (보유 골드 % 프리셋 버튼 10/25/50/100 + 스테이크 표시)
│   │           ├─ UpButton (UP ▲ up색) / DownButton (DOWN ▼ down색)
│   │           ├─ PayoutPreview (예상 손익 범위 미리보기)
│   │           ├─ StreakHUD (연승 콤보·세션 손익·기록)     ← 재미요소
│   │           └─ ResultBanner (CoinResultPopupUI: 승/패/극단 연출)
│   │
│   └─ ScanlineOverlay (장식, 선택, Raycast Off)
└─ CoinGameManager                  ← 코인 매니저 (§8.3) (씬 스코프)
```

`StockModeRoot`/`CoinModeRoot`는 `SetActive` 토글. `MarketSceneController`가 단일 진입점에서 제어.

---

## 5. 모드 전환 / 상단바

### SegmentedToggle (주식 / 코인)
- 활성=앰버(`gold`)+어두운 텍스트, 비활성=`textMuted`. 클릭 → `MarketSceneController.SetMode`.
  - Stock: `StockModeRoot` 활성 + `StockUIController.Open()`.
  - Coin: `CoinModeRoot` 활성 + `CoinModeUI.Open()`(코인 선택 화면부터; 단, 오늘 이미 코인 잠금 시 바로 베팅 화면).
- 마지막 계열 기억(선택). 기본 진입 = 주식.

### GoldDisplay
- `PlayerStat.OnGoldChanged` 구독 → `"{0:N0} G"`. 주식 거래와 코인 베팅이 **같은 골드**를 공유, 즉시 반영.

### 시장 심리 바 (주식 전용)
- 기존 `sentimentFill`/`sentimentText`를 그라데이션 바+마커로 리테마(§9.1).

---

## 6. 모드 A — 주식 계열 (기존 시스템 리테마)

**로직 불변.** 기존 `Stock.*`를 그대로 쓰고 색/레이아웃만 다크 레트로로 맞춘다.

### 6.1 기존 → 레퍼런스 매핑

| 레퍼런스 요소 | 기존 컴포넌트 | 비고 |
|---------------|---------------|------|
| 좌측 "전체 종목" + 선택 좌측 액센트 바 | `StockListUI`+`StockListItemUI` | 선택색/등락색 리테마 |
| 우측 상세(대형 가격·등락·태그칩) | `CompanyDetailUI` | 색/폰트 리테마 |
| 추세선(초록 라인+영역) | `MiniChartUI` | 라인색·영역 페이드 |
| 호가(빨강 톤) | 신규 소형 위젯/기존 영역 | 단순 표시면 §9에 추가 |
| 최신 뉴스 카드([피라시] 뱃지) | `CompanyDetailUI`+`NewsItemUI` | 뱃지·카드 리테마 |
| 탭(시세/뉴스 ●3/포트폴리오) | `StockUIController` 탭 버튼 | 활성 시안 언더라인+뉴스 뱃지 |
| 시장 심리 그라데이션 바 | `StockUIController.sentimentFill` | fillAmount→그라데이션+마커 |

### 6.2 리테마 변경점(코드) → §9. 색상 기본값만 §3.1 팔레트로 교체.

### 6.3 종목 라인업 & 해금 티어 (30종목 레벨디자인)

`Companies.csv`의 `tier` 컬럼(1~4)이 **해금 단계**를 정의한다(현재 95종목: 티어별 18/37/27/13).
`CompanyData.Tier`로 로드되고, `listingOrder`는 티어순 → 가격순.

**게이팅 구현됨 (2026-08-05)** — `Stock.Systems.StockUnlockGate`:

| 채광 레벨(`StatType.MiningLevel`) | 대응 땅 | 보이는 종목 티어 |
|---|---|---|
| 0 (시작) | 땅(Dirt) | 1 |
| 1 (심층 탐사 면허 I) | 얼음땅 | 1~2 |
| 2 (심층 탐사 면허 II) | 용암땅 | 1~3 |
| 3 (미구현 — 3번째 면허 노드 없음) | 우주 | 1~4 |

- 매핑은 **종목 티어 = 채광 레벨 + 1**. 채광 레벨은 지형 파기 게이트(`PlayerStat.CanDig` ↔ `tileData.tier`)와
  **같은 값**이라, "그 땅을 팔 수 있게 된 시점 = 그 티어 종목이 상장되는 시점"으로 묶인다.
  땅 단계 업그레이드(`UpgradeEffectType.MiningLevel`) 외에는 이 값이 오르지 않는다.
- 시뮬레이션은 **잠긴 종목도 계속 돌린다**(`CompanyManager.GetAllCompanies`) — 해금 순간 죽은 차트가 아니라
  그동안 움직여온 차트로 등장한다. UI는 `GetUnlockedCompanies()`만 쓴다(`StockListUI`, `StockFilterPanel`).
  필터 태그 목록도 해금분에서만 모아 잠긴 종목이 태그로 새어나가지 않게 한다.
- ⚠ 티어 4(13종목)는 채광 레벨 3이 필요하지만 업그레이드 트리에 3번째 `MiningLevel` 노드가 아직 없다
  (`UpgradeTreeGenerator`는 면허 I·II까지, `tree.tiers`도 0~2). 4번째 땅(우주)을 열 때 노드를 추가해야 상장된다.

| 티어 | 컨셉 | 종목 수 | basePrice 대역 | 시총 등급 | 대표 종목 |
|------|------|--------|----------------|-----------|-----------|
| 1 | 시작 개방 — 생활 밀착 소형주 | 7 | ₩9,500~45,000 | C~B | 데일리마트, 딥록 마이닝, 미래푸드, 선웰 오일, 에코에너지, 에어로링크, 메디젠 |
| 2 | 첫 해금 — 성장 중형주 | 8 | ₩46,000~96,000 | B~A | 픽셀웍스, 타이타늄, 프로스트라인, 스틸코어, 사이버넷, 바이오퓨어, 비타핀테크, 볼트셀 |
| 3 | 중후반 — 대형주 | 8 | ₩120,000~210,000 | A~S | 넥스칩, 럭스오라, 이지스, 스타스페이스, 골드브릿지, 헬리오스, 제노바이오, 퀀텀코어 |
| 4 | 엔드게임 — 대장주 | 7 | ₩195,000~600,000 | S | 딥AI, 코어드릴 헤비, 가이아, 오비탈X, 하이퍼넷, 이터나, 아틀라스 |

설계 규칙:
- **고티어일수록 고가 + 개성 분화**: 티어 4는 "잭팟형"(오비탈X vol 0.45)과 "초안정 블루칩"(아틀라스 vol 0.20 / rel 0.95)이 공존 — 자산 규모가 커진 플레이어에게 스타일 선택지를 준다.
- **분류는 태그로 통일**(구 `sector` 컬럼 폐지 — 2026-08-05): 종목 분류·필터·리스트 표시가 모두 태그 하나로 돌아간다. 태그 어휘는 49종 → **33종**으로 통합(chip→semiconductor, healthcare→health, metal→mining, bank/insurance→finance 등), 종목당 태그는 최대 3개. `tags[0]`이 구 섹터 자리의 **대표 태그**다.
- **태그 밸런스**: 전 티어에 걸쳐 태그가 분산되어 어느 해금 단계에서도 뉴스 체인 3~5개가 유효하다.
- **materials(원자재·소재) 계열**: 채굴 게임 정체성과 연결(딥록→스틸코어→코어드릴 헤비 성장 라인). 태그 `materials|mining`.
- **뉴스 커버리지 보장**: 모든 종목의 태그가 최소 1개 이상의 뉴스 템플릿 태그와 매칭됨(검증 완료). 체인 10종 + 독립 뉴스 27종.

---

## 7. 모드 B — 코인 도박 미니게임 (신규)

### 7.1 핵심 컨셉
- 코인 = **"단계"가 아니라 짧은 영문(가공) 이름의 코인들** (GRAVEL·BOOM·ABYSS 등). 각 코인은 곧 **위험도(변동성) 차등**. 최소 **10종**.
- **가격대 차등 + 소지금 연동 진행**: 코인마다 가격(`basePrice`)과 베팅 한도(`minBet/maxBet`)가 천차만별(₩40 ~ ₩2.6억). 플레이어 돈이 불어날수록 **더 비싸고 더 출렁이는 상위 코인이 자연 해금**된다 → "점점 비싸지고 더 재밌어지는" 진행 (§7.4).
- **하루 1코인(=1슬롯) 잠금**: 그날 처음 고른 코인의 **슬롯이 오늘로 잠긴다**. 그날은 그 슬롯에서만 베팅(상장폐지로 코인이 갈리면 새 코인으로 이어서), **다음 날** 다시 선택. (`DayCycleManager.CurrentDay`)
- **하루 N판 제한**: 하루 베팅 횟수 상한(`dailyRoundLimit`, 기본 5). 소진하면 그날은 종료, 다음 날 리셋. 빠른 일일 루틴·마틴게일/파산 방지.
- **빠른 사이드 미니게임**: 라운드가 짧고(빠른 reveal) 즉시 승패. 잠깐 들러 몇 판 굴리고 나오는 일일 루틴.
- **승률(부호) 50% 고정**: 모든 코인에서 다음 캔들의 방향은 정확히 50/50. 이건 게임 정체성이라 안 건드린다.
- **비대칭 페이아웃 = EV 마이너스**: 스테이크는 "마진(노출)". 적중은 마진의 일부까지만(`winCap`), 빗나감은 마진 이상까지(`lossCap`) → 같은 50%라도 **이겨도 적게, 져도 많이**. 여기에 **하우스 엣지**(`houseEdge`)·**양방향 베팅세**(`fee`)·**연승 보상 감쇠**(`streakPayoutDecay`)가 더해져 **EV는 항상 마이너스**(수입원이 못 된다). 위험은 분산 + 하우스 엣지 둘 다에서.
- **레버리지(배율) = 위험-보상 다이얼**: 배율↑ → 적중 상한(`effWinCap`)과 빗나감 상한(`effLossCap`)이 함께 커진다 → 큰 승리 + 마진 초과청산. EV는 고배율일수록 더 나쁘다.
- **일일 출금 한도**: 코인 이익은 오늘 `첫 베팅액 × withdrawLimitMultiplier`(기본 5)까지만 — 골드 상한 = `시작 골드 + 첫 베팅액 × 5`. 베팅·손실은 무제한이라 **전재산 올인(=전액 청산) 드라마는 보존**하면서 복리 폭주만 막는다.
- **±100% 극단 이벤트**: 고위험 코인일수록 갑자기 **상장폐지(−100%)** 또는 **떡상(+100%)**.
- **동적 로스터 (상장폐지 → 교체)**: 코인이 상장폐지(−100%)되면 **영구히 사라지고, 그 자리(슬롯)에 새 코인이 새 이름으로 상장**된다. 슬롯의 위험 티어·가격대는 유지되고 **이름·정체성만 갈린다** → 라인업이 계속 살아 움직인다 (§7.7).

### 7.2 게임 플로우

```
[코인 계열 진입]
   ├─ 오늘 코인 미선택 → CoinSelectView (코인 라인업 10종+)
   │     · 카드: 코인명/티커/위험 게이지/일반 변동폭/극단 확률/베팅 한도
   │     · 클릭 → "오늘의 코인" 잠금 → BettingView
   └─ 오늘 코인 이미 잠김 → 바로 BettingView (그 코인)
              ▼
[BettingView]  (한 라운드 = 한 번의 베팅)
   1) Idle  : 코인 추세선(과거 캔들) 표시. 잔액·예상 손익범위 미리보기. 뉴스 티커 흐름.
   2) Bet   : 스테이크 = 보유 골드의 % 프리셋 버튼(10/25/50/100)으로 선택([minBet, min(maxBet, 보유골드)] 클램프) → UP(▲)/DOWN(▼)
   3) Reveal: 다음 캔들이 실시간으로 그려짐(빠른 애니메이션, 확정 직전 떨림 = 서스펜스).
              극단 이벤트면 슬로우모션 + 화면 흔들림 + 전용 배너.
   4) Resolve: 새 캔들 방향 == 선택?  손익 = ±round(스테이크 × |변동폭|)
              · 승: AddGold(+손익)        · 패: SpendGold(−손익)  (손실 ≤ 스테이크)
              · 연승 콤보/세션 손익/기록 갱신, 승/패 배너
   5) 상장폐지 처리: 이번 캔들이 극단 −100%(상장폐지)였다면 → "💀 상장폐지" 연출 후 **그 슬롯에 새 코인 상장**("🌱 신규 상장: {새이름}"). 같은 슬롯이라 위험 티어·가격대는 유지, 이름·차트만 새로. 오늘의 잠금은 **슬롯**을 따라가므로 새 코인으로 계속 플레이 (§7.7).
   6) 다음 라운드: 남은 판(오늘 N판 중)이 있으면 (살아있는 코인은 이어서, 교체된 코인은 새로) 1)로.
        · 오늘 N판 소진 → 베팅 잠금("내일 다시"). · "코인 정보/나가기" → CoinModeUI로. (슬롯 변경은 다음 날에만)
```

### 7.3 베팅·정산 수학 (비대칭 페이아웃 + 레버리지)

> **부호(승패)는 전 코인 50% 고정**이되, **손익의 '크기'를 비대칭**으로 만들어 EV를 항상 마이너스로 둔다(수입원 방지). 위험은 ① 비대칭 캡(`winCap`<`lossCap`) ② 하우스 엣지(`houseEdge`) ③ 양방향 베팅세(`fee`) ④ 연승 보상 감쇠(`streakPayoutDecay`) ⑤ 레버리지에서 나온다. 스테이크 = 마진(노출). 코인 절대가는 차트 연출용.

```
입력: coin(슬롯 티어), dir ∈ {Up, Down}, stake(=마진), leverage, streak
검증: minBet ≤ stake ≤ (coin.unlimitedMax ? Gold : min(coin.maxBet, Gold))

변동 생성(엔진 §7.5):
   effExtreme = clamp01(extremeChance + streak × streakGreed)    // 연승 욕심: 극단(절반은 상폐) 확률↑
   extreme    = Random.value < effExtreme
   mag        = extreme ? 1.0 : Random.Range(minSwing, maxSwing) // |변동폭|
   sign       = (Random.value < 0.5) ? +1 : -1                   // 50/50 → 승률 50% 고정
   win        = (sign == dirSign(dir))                           // dir Up=+1 / Down=-1 → 정확히 50%

레버리지 스케일(t = maxLev≤1 ? 0 : clamp01((lev−1)/(maxLev−1))):
   move       = mag × leverage
   effWinCap  = Lerp(winCap × 0.5, winCap, t)    // 고배율 = 적중 상한↑(보상↑) — 안 지배당함
   effLossCap = Lerp(1.0, lossCap, t)            // 고배율 = 빗나감 상한↑(마진 초과청산)

엔진 정산(CoinPriceEngine.Resolve):
   적중:  edge  = clamp01(houseEdge + (extreme ? extremeExtraEdge : 0) + streak × streakPayoutDecay)
          delta = +round(stake × min(move, effWinCap) × (1 − edge))
   빗나감: delta = −round(stake × min(move, effLossCap))
          liquidated = (move ≥ 1)               // 변동이 마진을 가득 채움 이상 = 청산

매니저 후처리(CoinGameManager.TryBet §8.3):
   양방향 베팅세:   delta −= round(stake × fee)                         // 승패 무관 레이크(money sink)
   일일 출금 한도:   delta > 0 이면 delta = min(delta, (DayStartGold + DayFirstStake×withdrawLimitMultiplier) − Gold)
   소프트 초과청산:  delta < −Gold 이면 delta = −Gold                    // 지갑 바닥(음수 골드/빚 없음 = L1)
   적용:           delta ≥ 0 ? AddGold(delta) : SpendGold(−delta)
```

**워크드 예시** (MAGMA · winCap 0.85 / lossCap 1.20 / houseEdge 13% / fee 2% · **최대 레버리지**, 1,000G 베팅):
- 적중(일반): `+round(1000 × 0.85 × 0.87) − 20` ≈ **+720G** (마진의 ~72%만)
- 빗나감(청산): `−round(1000 × 1.20) − 20` = **−1,220G** (마진 초과 = 청산, 단 지갑까지만)
- → 한 판 EV ≈ `0.5×720 − 0.5×1220 = −250G` (**−25%**). 저배율이면 캡이 안 걸려 ≈ −fee(약 −2%).

**기대값(EV)**: 부호는 50/50이지만 `effLossCap > effWinCap×(1−edge)` + fee 라 **EV는 어떤 배율에서도 마이너스**(하우스 유리). 고배율일수록 캡이 물려 더 가혹 → "신나게 지르는 방식이 곧 가장 빨리 녹는 방식". 위험은 **분산 + 하우스 엣지** 둘 다에서 나온다.

### 7.4 코인 라인업 (기본값 10종 — 데이터로 조절)

위험도 = 변동폭(`minSwing~maxSwing`) + 극단 확률(`extremeChance`) + **하우스 노브**(`houseEdge`·`winCap`·`lossCap`·`streakPayoutDecay`·`maxLeverage`)로 차등. **승률(부호)은 전부 50%** — 차등은 손익 '크기'와 EV에서.
주식(틱당 ~20~45% 캡, 하루 1틱)보다 **체감 변동을 크게**: 코인은 라운드마다 즉시 큰 폭으로 출렁인다.

**코인 이름은 짧은 영문(가공) 한 단어.** 실제 코인(BTC·DOGE 등)을 쓰지 않고, 지하 채굴 세계관 + 밈을 섞은 창작 영문명으로 짧게 짓는다. 이름은 **모든 언어에서 영문 그대로**(번역 안 함, 티커와 동급의 고정 문자열).

| # | 코인명(영문) | 컨셉 | basePrice(₩) | 일반 \|d\| | 극단 ±100% | minBet | maxBet |
|---|------------|------|-------------:|-----------|-----------|-------:|-------:|
| 1 | GRAVEL | 흔하디흔함, 거의 정지 | 40 | 1~4% | 0% | 10 | 50,000 |
| 2 | MOLE | 땅 파다 나온 밈 | 350 | 3~9% | 0% | 100 | 200,000 |
| 3 | LUCKY | "제발 올라라" 밈 | 1,800 | 5~13% | 0.5% | 500 | 800,000 |
| 4 | VEIN | 광부들의 코인 | 9,000 | 7~18% | 1% | 2,000 | 3,000,000 |
| 5 | QUARTZ | 반짝이는 변동성 | 45,000 | 10~24% | 2% | 10,000 | 10,000,000 |
| 6 | GOBLIN | 변덕쟁이 | 220,000 | 13~32% | 3.5% | 50,000 | 30,000,000 |
| 7 | MAGMA | 뜨겁고 과열 | 1,100,000 | 16~42% | 6% | 200,000 | 80,000,000 |
| 8 | BOOM | 펑펑 터짐 | 6,000,000 | 20~55% | 10% | 1,000,000 | 200,000,000 |
| 9 | JACKPOT | 한 방 노림 | 38,000,000 | 26~70% | 15% | 5,000,000 | 500,000,000 |
| 10 | ABYSS | 바닥 없는 룰렛 | 260,000,000 | 32~95% | 24% | 20,000,000 | (무제한) |

> 별도 티커 컬럼은 두지 않는다(영문명이 곧 티커 역할). 차트 헤더엔 이 영문명을 그대로 노출.

**위험 노브 (DefaultSlots 실제값 — 고티어일수록 가혹)**

| # | 코인 | houseEdge | winCap | lossCap | streakPayoutDecay | maxLeverage |
|---|------|----------:|-------:|--------:|------------------:|------------:|
| 1 | GRAVEL | 2% | 0.97 | 1.05 | 0.03 | 100 |
| 2 | MOLE | 3% | 0.95 | 1.07 | 0.04 | 30 |
| 3 | LUCKY | 5% | 0.93 | 1.10 | 0.05 | 20 |
| 4 | VEIN | 6% | 0.91 | 1.12 | 0.06 | 14 |
| 5 | QUARTZ | 8% | 0.89 | 1.15 | 0.07 | 10 |
| 6 | GOBLIN | 10% | 0.87 | 1.17 | 0.08 | 8 |
| 7 | MAGMA | 13% | 0.85 | 1.20 | 0.09 | 6 |
| 8 | BOOM | 15% | 0.83 | 1.22 | 0.10 | 5 |
| 9 | JACKPOT | 18% | 0.80 | 1.25 | 0.11 | 4 |
| 10 | ABYSS | 22% | 0.78 | 1.30 | 0.12 | 3 |

- `winCap`(적중 effMove 상한, <1) / `lossCap`(빗나감 상한, >1) / `streakPayoutDecay`(연승 1당 적중 보상 추가 차감) / `extremeExtraEdge`(극단 떡상 추가 차감). 전부 `CoinData`.
- `maxLeverage`는 저변동 코인일수록 크다(≈1/minSwing) — 저변동을 고배율로 키워 캡에 닿게.
- 전역 노브(`CoinTableSO`): `fee`(양방향 베팅세, 기본 2%) · `withdrawLimitMultiplier`(일일 출금 한도 배수, 기본 5) · `dailyRoundLimit`(하루 판수, 기본 5).
- **EV는 전 티어 마이너스** — 고티어일수록 더 깊다. 고티어는 큰 변동(분산)과 큰 EV 손실을 동시에 준다.

**가격대 차등 + 소지금 연동 진행 (핵심 설계)**
- **`basePrice` = 코인의 정체성/위상 (시각용)**. 차트에 표시되는 호가 단위. 손익 계산엔 직접 쓰지 않는다(손익 = 스테이크 × 변동% — §7.3). 코인마다 ₩40 ~ ₩2.6억으로 **가격대가 확연히 다르다**.
- **`minBet`/`maxBet` = 실제 진행 게이트**. 최소 베팅액을 못 내면 그 코인은 **선택 불가**. 별도 해금 플래그 없이 **소지금이 곧 해금**:
  - 초반(수천 G) → GRAVEL·MOLE만 가능.
  - 중반(수백만 G) → VEIN·QUARTZ·GOBLIN.
  - 후반(수억 G) → MAGMA·BOOM·JACKPOT·ABYSS.
- 즉 **돈이 많아질수록 더 비싼·더 출렁이는·더 큰 판의 코인**이 자연스럽게 열린다 = "점점 더 비싸지고 더 재미있어지는" 진행을 한 번에 충족.
- 위로 갈수록 변동폭↑·극단↑·판돈↑ = **위험·보상 최대**. 아래(안전)는 잔잔해 "재미없지만 안전".
- 극단 이벤트 +100%/−100%는 기본 **대칭(50/50)** → 승률 50% 유지. (비대칭은 승률을 깨므로 §13 참고.)
- `maxBet` 상한으로 단판 분산을 제한. ABYSS만 무제한(하이리스크 의도) — 과하면 상한 지정.

> **코인명(영문)은 번역하지 않는 고정 문자열**(`CoinData.name`). 로컬라이제이션은 **플레이버 설명**(`COIN_FLAVOR_*`)과 **UI 문자열**(`ui_coin_*`)만 KR/EN/CN으로 관리한다(§10.1).

### 7.5 가격 시뮬레이션 엔진 (`CoinPriceEngine`)

차트가 **그럴듯**하게 보이면서 승률 50%·비례 손익을 **정확히** 만든다.

**(a) 진입 시 추세선(장식)**: 코인 선택 시 과거 캔들 `history`를 랜덤워크로 N개(예 24) 생성. 결과와 무관, "읽을 거리"용.
```
price0 = coin.basePrice
for i in 1..N: price_i = clampMin(price_{i-1} × (1 + Random(-1,1) × midSwing × 0.6), floor)
```
**(b) 라운드 정산**: §7.3 그대로(레버리지 스케일·비대칭 캡·연승 감쇠 포함). 새 가격 `newPrice = clampMin(currentPrice × (1+sign×mag), floor)`을 history에 추가(코스메틱).
- **극단 −100%(상장폐지)**: `sign<0`인 극단이면 `delisted=true` 플래그. 정산(승패)은 정상 처리하고, 이후 `CoinGameManager`가 슬롯 코인을 교체(§7.7). (극단 +100%는 떡상 — 코인 생존, 가격만 급등.)
- 절대 가격은 **손익에 영향 없음**(손익은 마진×캡×엣지 — §7.3). 따라서 상장폐지/교체/리셋 자유.
- 양방향 fee·일일 출금 한도·소프트 초과청산은 **엔진이 아니라 매니저(`TryBet`)** 가 후처리한다(엔진 `delta`는 fee·한도·지갑클램프 미반영 기본값).
- 순수 C# (MonoBehaviour 아님) → 단위 테스트 용이.

### 7.6 재미 요소 (사이드 미니게임 양념)

| 요소 | 내용 | 비고 |
|------|------|------|
| **상장폐지 → 새 코인 상장 연출** | −100% 시 화면 흔들림 + 슬로우모션 + "💀 상장폐지" → 곧바로 "🌱 신규 상장: {새이름}" 등장 연출 | 헤드라인 재미. §7.7 |
| **떡상 대박 연출** | +100% 시 "🚀 떡상!" 배너 + 글로우 + SFX | SoundManager 연동 |
| **연승 콤보** | 연속 정답 시 콤보 카운터·화염 게이지 상승, 연출 강화 | 보상 보너스 **없음** — 오히려 `streakPayoutDecay`(보상↓)+`streakGreed`(상폐↑) 페널티(§13) |
| **올인 버튼** | 스테이크 전액 + 확인 + 드라마틱 reveal | ABYSS 올인 = 인생 한 방 |
| **레버리지 다이얼** | 배율↑ = 큰 승리 + 마진 초과청산. 슬라이더 옆 "변동 X%↑ 청산" 표시 | 진짜 위험-보상 선택. UI 완료 |
| **일일 출금 한도** | "출금 한도까지 +₩X / 한도 ₩Y", 도달 시 "🎉 오늘 출금 한도 달성!" | 복리 폭주 차단 + 멈출 타이밍 신호. UI 완료 |
| **청산 연출** | 손실이 마진 이상이면 미리보기에 "⚠ 청산" + Crash SFX + 화면 흔들림 | 고배율의 긴장감. `liquidated` 플래그 |
| **세션 손익 / 기록** | 오늘 누적 손익, 최고 단판 수익, 최장 연승, **상장폐지 목격 횟수**. 신기록 갱신 연출 | `CoinSaveData`에 영속 |
| **코인 개성** | 슬롯별 영문명·아이콘·플레이버·위험 게이지. 교체될 때마다 새 이름으로 갱신 | `CoinData`(티어) + 로스터(이름) |
| **가짜 뉴스 티커** | 하단에 밈 뉴스 흐름("○○ 창립자 잠적설…", 상장폐지 직전 불길한 헤드라인) | 연출용(결과 영향 없음). 다음 변동 힌트로 쓰려면 §13 |
| **리빌 서스펜스** | 캔들 확정 직전 떨림 + 카운트다운 틱 사운드 | 짧게(빠른 템포 유지) |
| **데일리 코인 결정** | "오늘은 어떤 슬롯에 걸까" — 하루 1슬롯 잠금이 곧 선택의 재미 | 핵심 루프 |

### 7.7 상장폐지 & 코인 교체 (동적 로스터)

**핵심 아이디어**: 라인업은 고정된 코인 10개가 아니라 **고정된 10개의 "슬롯(위험 티어)"**이고, 각 슬롯에 **코인 인스턴스**(이름·차트)가 들어앉아 있다. 코인이 상장폐지되면 그 코인은 영구히 사라지고, **같은 슬롯에 새 코인이 새 이름으로 상장**된다.

```
슬롯(고정, CoinData)        : 위험 티어 = basePrice 밴드 · minSwing/maxSwing · extremeChance · minBet/maxBet
   └─ 코인 인스턴스(가변, 로스터): name(영문) · currentBasePrice · 차트 history · alive
```

**무엇이 유지되고 무엇이 바뀌나**
- **유지(슬롯 = 티어)**: 위험도, 변동폭, 극단 확률, 베팅 한도, 가격대(밴드) → 라인업의 진행/게이팅 구조가 안 깨진다(§7.4).
- **교체(코인 인스턴스)**: 영문 이름, 표시 가격(밴드 내 랜덤 재설정), 차트 히스토리 → "완전히 다른 코인"으로 보인다.

**교체 트리거 & 흐름**
1. 베팅 중인 코인의 라운드에서 **극단 −100%(상장폐지)** 발생 → `CoinRoundResult.delisted = true`.
2. 정산(승패)은 정상 처리. (DOWN 베팅이었으면 +stake 대박, UP이었으면 −stake.)
3. `CoinGameManager`가 해당 슬롯의 코인을 교체:
   - 새 이름 = **그 슬롯의 순환 목록(`CoinData.rotationNames`)에서 다음 이름** — `RotationNameAt(delistCount + 1)`. 슬롯마다 3개를 A→B→C→A로 돌려 쓴다(아래 표). 순환 목록이 빈 슬롯만 `CoinTableSO.replacementNames` 전역 풀에서 **현재 사용 중이 아닌** 이름 1개를 랜덤 추출(소진 시 접미 번호, 예 `RUBBLE-2`).
   - 새 `currentBasePrice` = `slot.basePrice × Random(0.6, 1.8)` (밴드 내 재설정, 코스메틱).
   - 엔진 `StartCoin(slot, newName, newBasePrice)` → 차트 추세선 새로 생성.
   - `delistCount++`, 통계(상장폐지 목격) 증가, `OnCoinDelisted(slotId, oldName, newName)` 발화.
4. **오늘의 잠금은 슬롯을 따라간다**(코인 인스턴스가 아니라). 따라서 상장폐지돼도 그날은 새 코인으로 남은 판을 계속 플레이.

**교체 범위**
- 상장폐지는 **실제 베팅한 라운드에서만** 발생(엔진이 그 코인의 캔들을 생성하므로). 플레이하지 않는 다른 슬롯은 그대로 둔다(백그라운드 자동 상장폐지 없음 — 단순·예측 가능).
- 극단 **+100%(떡상)**는 교체 아님 — 코인 생존, 가격만 급등.
- 효과적 상장폐지 확률/라운드 ≈ `extremeChance × 0.5`(극단 중 하락 절반). GRAVEL(0%)은 절대 안 죽고, ABYSS(24%)는 라운드당 ~12%로 자주 갈린다 = 의도된 "룰렛".

**스테이지별 코인 순환(`CoinData.rotationNames`)** — 슬롯마다 3개를 고정 순서로 돌려 쓴다. `[0]`이 그 스테이지의 첫 코인(`defaultName`)이고, 상장폐지될 때마다 다음 칸으로, 3번째가 죽으면 다시 `[0]`으로 돌아온다.

| 슬롯 | 티어 | ① 기본 | ② 1차 교체 | ③ 2차 교체 |
|---|---|---|---|---|
| 1 | 1 | GRAVEL | PEBBLE | GRIT |
| 2 | 1 | MOLE | WORM | GOPHER |
| 3 | 2 | LUCKY | CLOVER | WISH |
| 4 | 2 | VEIN | NUGGET | SEAM |
| 5 | 3 | QUARTZ | GEODE | PRISM |
| 6 | 3 | GOBLIN | GREMLIN | IMP |
| 7 | 4 | MAGMA | EMBER | CINDER |
| 8 | 4 | BOOM | BLAST | FUSE |
| 9 | 5 | JACKPOT | BONANZA | MOONSHOT |
| 10 | 5 | ABYSS | VOID | OBLIVION |

이름은 **번역하지 않고** 영문 고정이며, 코인 30종 각각의 **플레이버 문구**는 `Coin_Localization.csv`에 `COIN_FLAVOR_{이름}` 키로 KR/EN/CN을 넣는다. UI(`CoinCardUI`/`CoinPreviewUI`)는 `CoinData.FlavorKeyFor(state.currentName)`으로 **현재 상장된 코인의** 플레이버를 읽고, 키가 없으면 슬롯 기본 코인 플레이버로 폴백한다.

**예비 이름 풀(`replacementNames`)** — 순환 목록이 없는 슬롯에만 쓰이는 폴백(지하·밈 테마):
`RUBBLE, NUGGET, GEODE, SHALE, EMBER, CINDER, RELIC, FOSSIL, SLUDGE, GUSHER, PEBBLE, OBSIDIAN, BASALT, COMET, BLAZE, DRIFT, SPARK, HOARD, TROVE, RUIN, GHOST, ZOMBIE, PHOENIX, VOID`

**영속**: 각 슬롯의 현재 이름·표시가·`delistCount`를 `CoinSaveData.roster`에 저장 → 갈려나간 라인업이 세이브/로드에 유지(§10). 신규 게임 시작 시 슬롯은 `CoinData.defaultName`으로 초기화.

---

## 8. 신규 스크립트 명세

디렉토리/네임스페이스(기존 `Stock.*`와 대칭):
```
Assets/Scripts/Coin/  { Core | Systems | Data | UI }   → namespace Coin.*
Assets/Scripts/Market/                                  → namespace Market  (두 계열 공용)
```

### 8.1 `Market/MarketTheme.cs`
- 다크 레트로 팔레트 중앙 정의(§3.1 토큰 전부 `static readonly Color`) + `SentimentColor(float t)`(공포→탐욕 보간).
- (선택) `MarketThemeSO : ScriptableObject`로 인스펙터 조절. 폴백 static.

### 8.2 `Market/MarketSceneController.cs`
- 씬 최상위. 계열 전환·진입/이탈·상단바 골드·ESC 총괄.
- 필드: `GameObject stockModeRoot, coinModeRoot`; `StockUIController stockUI`; `CoinModeUI coinUI`; `SegmentedToggle modeToggle`; `TMP_Text goldText`; `Button closeButton`.
- 메서드: `Enter()`, `Exit()`(Additive Unload), `SetMode(MarketMode)`, `RefreshGold()`.
- 구독: `PlayerStat.OnGoldChanged += RefreshGold`. `enum MarketMode { Stock, Coin }`.
- **키 입력**: ESC=Exit, **Tab=주식↔코인 전환**(입력필드 포커스 중엔 무시). 마켓 열림 동안 게임 쪽 Tab(인벤토리)/J(퀘스트)는 `UIStateManager`가 `UIState.Market` 상태로 차단.

### 8.3 `Coin/Core/CoinGameManager.cs`
- 코인 계열 두뇌. **슬롯 로스터** 보유·관리, **하루 1슬롯 잠금**, 베팅 검증·골드 연동·정산, **상장폐지 교체**, 세션/통계/세이브.
- 형태: 씬 스코프 MonoBehaviour(`Instance`). **DontDestroyOnLoad 아님** — 영속 데이터는 SaveData로.
- 필드: `CoinTableSO coinTable`; `CoinPriceEngine _engine`; `PlayerStat _playerStat`(지연 해석); `List<CoinSlotState> _roster`(슬롯별 현재 코인); 세션 상태(연승, 세션 손익).
- 로스터:
  - 초기화: 각 슬롯 = `CoinData`(티어) + `CoinSlotState{ currentName=defaultName, currentBasePrice=basePrice, delistCount=0 }`.
  - `IReadOnlyList<CoinSlotState> Roster` ; `CoinData SlotTier(int slotId)` ; `CoinSlotState Slot(int slotId)`.
  - `void DelistAndReplace(int slotId)` → 새 이름(`PickReplacementName()`)·새 표시가(`basePrice×Random(0.6,1.8)`)·`delistCount++`·엔진 재시작·`OnCoinDelisted(slotId, old, new)`.
  - `string PickReplacementName()` → `replacementNames` 중 현재 미사용 1개(소진 시 접미 번호).
- 데일리 잠금 + 라운드 제한 + 일일 출금 한도:
  - `int LockedSlotId`(오늘의 슬롯, 미선택 −1), `int LockedDay`, `int RoundsToday`, **`long DayStartGold`**(오늘 첫 베팅 시점 골드 — 한도 시작점, 영속), **`long DayFirstStake`**(오늘 첫 베팅액 — 한도 = 첫 베팅액×배수, 영속).
  - `int DailyRoundLimit`(`coinTable.dailyRoundLimit`, 기본 5), `int RoundsLeft` = `DailyRoundLimit − RoundsToday`.
  - **`int WithdrawLimitMultiplier`**(`coinTable.withdrawLimitMultiplier`, 기본 5), **`long WithdrawLimitGold`** = `DayStartGold + DayFirstStake × WithdrawLimitMultiplier`, **`int WithdrawLimitHeadroom()`** = 오늘 더 딸 수 있는 골드(미잠금 시 `int.MaxValue`, 한도 도달 시 0).
  - `bool CanSelectSlotToday` = `Today != LockedDay || LockedSlotId < 0`. `bool CanBetToday` = `ActiveSlotId≥0 && RoundsToday<DailyRoundLimit && (CanSelectSlotToday || LockedSlotId==ActiveSlotId)`.
  - **잠금은 첫 베팅에서**: `BeginSlot(slotId)`는 미확정 진입(엔진 추세선만), `TryBet` 첫 호출에서 `CanSelectSlotToday`면 잠금(`LockedDay`=오늘, `RoundsToday`=0, **`DayStartGold`=PlayerGold·`DayFirstStake`=첫 베팅액 기록**).
  - `DayCycleManager.OnDateTimeChanged` 구독 → 날짜 바뀌면 `RoundsToday=0`·선택 가능 상태로(로스터는 유지). `DayStartGold`·`DayFirstStake`는 다음 첫 베팅에서 재기록.
- 레버리지: `int CurrentLeverage`(기본 1), `int MaxLeverage`(현재 슬롯 `maxLeverage`), `void SetLeverage(int)`(슬롯 최대치로 클램프).
- 베팅 API:
  - `bool TryBet(BetDirection dir, int stake, out CoinRoundResult result)` → 검증·잠금·`_engine.Resolve(dir, stake, CurrentStreak, CurrentLeverage)`, 이어 **후처리**: ① 양방향 fee 레이크 ② **일일 출금 한도 클램프**(양수 delta를 `WithdrawLimitHeadroom()`까지) ③ **소프트 초과청산 클램프**(`delta < −Gold`면 `−Gold`) → `ApplyDelta`·`RoundsToday++`·통계·`OnRoundResolved`; **`delisted`면 `DelistAndReplace`**. 한도 소진/검증 실패 시 false.
  - `(int min,int max) StakeRange()`(unlimitedMax면 Gold, 아니면 `min(maxBet,Gold)`) ; `int ClampStake(int)` ; `int StakeFromPercent(int pct)` ; `(int worst,int best) PreviewPnL(int stake)` — 레버리지 스케일 캡·엣지·연승감쇠·fee 반영, best는 `WithdrawLimitHeadroom()`까지·worst는 지갑까지 클램프.
- 이벤트: `Action<int> OnDailySlotLocked`; `Action<CoinRoundResult> OnRoundResolved`; `Action<int,string,string> OnCoinDelisted`(slotId, oldName, newName); `Action OnSessionChanged`.
- 세이브: `CoinSaveData CaptureSaveData()` / `ApplySaveData(CoinSaveData)` (StockGameManager 패턴) — 로스터 포함.

### 8.4 `Coin/Systems/CoinPriceEngine.cs`
- §7.5. 추세선 생성 + 라운드 정산(승률 50% + 비례 변동 + 극단 이벤트 + 상장폐지 신호).
- 상태: `List<float> _history`; `CoinData _slot`(티어 파라미터); `float _basePrice`(현재 인스턴스 표시가); `const int MaxHistory = 60`.
- API: `void StartCoin(CoinData slot, float basePrice)`(교체 시 새 basePrice로 재시작); `IReadOnlyList<float> History`; `float CurrentPrice`; `CoinRoundResult Resolve(BetDirection dir, int stake, int streak = 0, int leverage = 1)`(부호 50/50, 레버리지 스케일 `effWinCap`/`effLossCap`, 연승 욕심(`streakGreed`)·연승 감쇠(`streakPayoutDecay`) 반영, 극단 −100%면 `delisted=true`, `move≥1`이면 `liquidated=true`). **fee·일일 출금 한도·지갑클램프는 미반영**(매니저가 후처리).
- 순수 C#.

### 8.5 `Coin/UI/CoinModeUI.cs`
- 코인 계열 컨테이너. CoinSelectView ↔ BettingView 전환.
- `Open()`: `CoinGameManager.CanSelectSlotToday`면 선택 화면, 아니면 잠긴 슬롯의 코인으로 베팅 화면.
- 메서드: `ShowSelect()`, `EnterSlot(int slotId)`(SelectDailySlot 호출), `BackToSelect()`(오늘 잠김 시 안내).

### 8.6 `Coin/UI/CoinSelectUI.cs` + `CoinCardUI.cs`
- `CoinSelectUI`: 슬롯 라인업(10칸+) 카드 목록 빌드 — **로스터의 현재 코인**을 표시. 잠금/오늘의 슬롯 강조, `minBet > Gold`면 비활성(자연 해금). 클릭 → `CoinModeUI.EnterSlot`.
- `CoinCardUI`: 단일 슬롯 카드. **현재 코인 영문명**(로스터)/아이콘/**위험 게이지(RiskGauge)**/변동폭/극단 확률/베팅 한도/플레이버/(선택)상장폐지 횟수.

### 8.7 `Coin/UI/CoinBettingUI.cs`
- 베팅 테이블. 차트·스테이크·레버리지·방향·정산·재미요소 묶음.
- 필드: `CoinChartUI chart`; **`Button[] stakeButtons` + `int[] percentValues`(기본 10/25/50/100) + `TMP_Text stakeAmountText`**("₩X (Y%)"); **`Button[] leverageButtons` + `int[] leverageValues`(기본 1/2/5/10/25) + `TMP_Text leverageText`**("배율 xN · 변동 M%↑ 청산 (최대 xMax)"); `Button up, down, back`; `TMP_Text pnlPreviewText`(적중/빗나감 — 손실≥마진이면 "⚠ 청산" 태그), `roundsLeftText`, **`dailyCapText`**(출금 한도까지 +₩X / 한도 / 도달); `StreakHUD streakHud`; `CoinEventNewsUI eventNews`; `CoinResultPopupUI resultPopup`. **TMP_InputField/Slider/드롭다운 없음(프리셋 버튼 방식).**
- 스테이크: 프리셋 버튼 클릭 → `CoinGameManager.StakeFromPercent(pct)`(최소 미달 % 버튼은 자동 숨김). 베팅·골드 변동 후 같은 %로 재산출(항상 *현재* 골드 기준). 현재 선택된 % 버튼은 비활성화로 표시.
- 레버리지: 프리셋 버튼 클릭 → `SetLeverage`(슬롯 최대치 이하 버튼만 노출). 선택 시 `PreviewPnL`·청산% 갱신. 현재 선택된 배율 버튼은 비활성화로 표시.
- 동작: 선택 → `PreviewPnL`·`WithdrawLimitHeadroom`(한도) 갱신. UP/DOWN → `TryBet` → '띠' 카운트 → **브레이킹(급등/급락·청산)이면 잠깐 정지(NewsHold) 후 영향 뉴스를 먼저 띄우고**(`CoinEventNewsUI.Show(bullish, chart.TriggerReveal)`, 스킵 없음) 뉴스가 그래프 공개를 트리거 → Reveal(그래프 이동) → 결과 배너·연승·세션·남은판·한도 갱신.
- 입력 잠금: Reveal 중 또는 `RoundsLeft==0`이면 베팅·프리셋 버튼 비활성.

### 8.8 `Coin/UI/CoinChartUI.cs`
- `MaskableGraphic` 기반 애니메이션 차트(`MiniChartUI` 메시 패턴 차용). 그리드·글로우·영역 페이드.
- API: `void SetHistory(IReadOnlyList<float>)`; `void PlayReveal(float oldPrice,float newPrice,bool win,bool extreme,float swing,bool awaitNews)`; `void TriggerReveal()`(NewsHold→Reveal); `event Action OnAnticipatePeak`; `event Action OnRevealComplete`; `event Action OnZoomOutComplete`.
- 상태: Idle→ZoomIn→Anticipate(**가속 드럼롤** — 빌드업: '띠' 간격 `tickIntervalStart`(0.45초)→`tickIntervalEnd`(0.11초)로 점점 짧아지고 피치 1.0→1.35 상승, `tickCount`(7번) → **버스트**: `burstTickCount`(6번)×`burstTickInterval`(0.05초) 기관총 연타+피치 1.35→1.6 → `preRevealSilence`(0.26초) 무음의 정점)→(awaitNews면 NewsHold: 잠깐 정지, 뉴스가 TriggerReveal)→Reveal(스프링)→Hold→ZoomOut→Idle. 결과 확정 후 평소 그래프 원상복귀까지 총 `returnToIdleDuration`(기본 1초). 연출 스킵 없음. x축 스크롤(maxPoints).
- **도파민 juice(전부 코드 합성 — 씬 오브젝트 불필요)**: '띠'마다 줌 펀치(`_zoomPunch` 순간 과줌)+머리점 확대 펀치(`_dotPunch`), 버스트 스윙은 랜덤 과대 진폭(1.4~2.3×)+백색 스트로브, 공개 시작 릴리즈 플래시(백), 착지 순간 결과색(Up/Down) 전면 플래시 — 전면 플래시는 OnPopulateMesh에서 라인 뒤 풀렉트 쿼드로 그림. `event OnAnticipateTick(bool up, float progress)`로 BettingUI의 **시세 숫자 슬롯 롤링**(틱마다 ±0.6%→±7% 널뜀, up/down 색 동기, 착지 시 실제 결과값·결과색 확정) 동기화.

### 8.9 `Coin/UI/CoinResultPopupUI.cs`
- 승/패/극단 배너 플래시(획득/손실·배율·"상장폐지/떡상"). up/down 색 + 짧은 페이드. (인라인이면 BettingUI 흡수 가능.)

### 8.10 `Coin/Data/*`
- `CoinEnums.cs`: `enum BetDirection { Up, Down }`, `enum CoinRoundPhase { Idle, Revealing, Resolved }`.
- `CoinData.cs` `[Serializable]` — **슬롯(위험 티어) 정의. 고정, SO에 보관**:
  ```
  int id;                  // 슬롯 id (1~10) — 안정적. 코인이 갈려도 변하지 않음
  string defaultName;      // 슬롯 최초 코인 영문명 (GRAVEL …). 모든 언어 동일 — 번역 안 함
  string flavorKey;        // 슬롯(티어) 플레이버 키 (COIN_FLAVOR_GRAVEL …) → CoinLoc.L (§10.1)
  int riskTier;            // 1~5, 위험 게이지
  float basePrice;         // 표시가 기준(밴드 중심, 시각용). 손익엔 직접 안 씀 (§7.4)
  float minSwing, maxSwing;// 일반 |변동폭| 범위 (예 0.20~0.55)
  float extremeChance;     // ±100% 발생 확률 (0~1). 중 절반이 −100%(상장폐지)
  // 하우스 노브 — 승률 50% 유지, 손익 크기/EV로 위험 부여 (고티어↑)
  float houseEdge;         // 적중 보상 차감 비율(EV 하락)
  float extremeExtraEdge;  // 극단(떡상) 보상 추가 차감
  float streakGreed;       // 연승 1당 극단확률 가산(욕심 페널티 → 상폐 확률↑)
  int   maxLeverage;       // 이 티어 최대 배율(레버리지 스케일 기준)
  float winCap;            // 적중 effMove 상한(<1) — 레버리지 0.5×winCap→winCap 스케일
  float lossCap;           // 빗나감 effMove 상한(>1) — 레버리지 1.0→lossCap 스케일(초과청산)
  float streakPayoutDecay; // 연승 1당 적중 보상 추가 차감(복리 런어웨이 차단)
  int minBet, maxBet; bool unlimitedMax;   // minBet = 소지금 기반 자연 해금 게이트. unlimitedMax = ABYSS만(전재산 올인)
  ```
- `CoinSlotState.cs` `[Serializable]` — **런타임/세이브. 슬롯에 현재 앉은 코인 인스턴스**:
  ```
  int slotId;              // 대응 CoinData.id
  string currentName;      // 현재 코인 영문명 (교체 시 갱신; 초기 = defaultName)
  float currentBasePrice;  // 현재 표시가 (교체 시 basePrice×Random(0.6,1.8))
  int delistCount;         // 이 슬롯에서 상장폐지된 횟수
  ```
- `CoinTableSO.cs` `ScriptableObject`: `List<CoinData> slots; List<string> replacementNames; int dailyRoundLimit = 5; float fee = 0.02f`(양방향 베팅세); **`int withdrawLimitMultiplier = 5`**(일일 출금 한도 배수). §7.4 슬롯 + §7.7 이름 풀로 채움. 코드 폴백(`DefaultSlots`) 제공.
- `CoinRoundResult.cs` `struct`: `BetDirection pick; bool win; bool extreme; bool delisted; int stake; int leverage; int fee; bool liquidated; int delta; float swing; float oldPrice; float newPrice; string replacementName`(delisted일 때 새 코인명).
- `CoinSaveData.cs` `[Serializable]`: `bool hasData; int lockedDay; int lockedSlotId; int roundsToday; long dayStartGold; List<CoinSlotState> roster; long totalStaked; long totalProfit; int roundsPlayed; int bestSingleWin; int longestStreak; int delistsWitnessed`. (가격 히스토리 비영속 — 진입 시 재생성. **로스터·`dayStartGold` 영속** — 같은 날 재접속해도 한도 유지.)

### 8.11 `Coin/Testing/CoinTestRunner.cs` (선택)
- `StockTestRunner`처럼 인스펙터 버튼으로 검증: 10,000회 시뮬(레버리지 1·streak 0) → 실측 승률이 0.5에 수렴하는지, 평균 손익률이 **마이너스**(비대칭 캡+하우스 엣지, fee 미반영)인지, 극단 이벤트 빈도가 `extremeChance`에 맞는지. 고배율/연승/일일한도 EV는 실제 플레이로 확인.

---

## 9. 기존 스크립트 수정 명세 (리테마)

> **로직 변경 없음.** 색상 기본값 교체 + 씬 모드 대응만. 기존 직렬화 참조 보존.

### 9.1 `Stock/UI/StockUIController.cs`
- 씬 모드 대응: `panelRoot` = `StockModeRoot`. `MarketSceneController.SetMode`가 `Open()`/`Close()` 위임(이미 공개 메서드 보유 — 큰 변경 불필요).
- 시장 심리: `sentimentFill` 색을 `MarketTheme.SentimentColor`로, 그라데이션 바+마커 배치(시각=에디터, 색=코드).
- 상단 골드 표시는 `MarketSceneController`로 이관 가능(중복 제거) 또는 기존 `goldText` 유지.

### 9.2 `Stock/UI/StockListItemUI.cs`
- `selectedColor`→`rowSelected`, `riseColor`→`up`, `fallColor`→`down`, `flatColor`→`textDim`.
- 선택 좌측 액센트 바용 `Image leftAccent`(`accentCyan`) 추가 + `SetSelected` 토글.

### 9.3 `Stock/UI/CompanyDetailUI.cs`
- `riseColor/fallColor/flatColor` 기본값 → `up/down/textDim`. 태그칩·대형 가격 폰트는 에디터.

### 9.4 `Stock/UI/MiniChartUI.cs`
- `riseColor/fallColor` 기본값 → `up/down`. (선택) 영역 페이드/글로우. `CoinChartUI`와 공통 베이스(`LineChartBase`)로 추출은 선택(우선 복제 허용).

> 색 기본값만 바꿔도 프리팹에 직렬화된 값이 우선하므로, 프리팹 인스펙터도 함께 갱신(에디터 §11). 코드는 신규/리셋 기본값 보장.

---

## 10. 데이터 / 세이브 / 골드 / 날짜 / 로컬라이제이션 연동

- **골드**: 단일 소스 `PlayerStat`. 주식 거래(`PortfolioManager`)와 코인 베팅(`CoinGameManager`)이 동일 `SpendGold/AddGold` → 상단 `GoldDisplay`가 `OnGoldChanged`로 일괄 갱신.
- **날짜(하루 1슬롯 + 하루 N판)**: `DayCycleManager.CurrentDay`로 잠금. `OnDateTimeChanged` 구독해 날짜 변경 시 `RoundsToday=0`·재선택 허용. 세이브엔 `lockedDay`/`lockedSlotId`/`roundsToday` 저장 → 로드 후에도 "오늘의 슬롯·남은 판" 유지(같은 날 재접속해도 한도 우회 불가).
- **동적 로스터(§7.7)**: 슬롯별 현재 코인명·표시가·상장폐지 횟수(`roster: List<CoinSlotState>`)를 영속 → **갈려나간 라인업이 세이브/로드에 유지**. 신규 게임은 `defaultName`으로 초기화.
- **코인 세이브**: `CoinGameManager.CaptureSaveData/ApplySaveData`를 `SaveManager`에 연동(기존 `StockSaveData` 엮인 방식과 동일 패턴으로 `CoinSaveData` 필드 추가).
- **영속 대상**: 데일리 잠금 상태 + 로스터 + 누적 통계/기록. 가격 히스토리는 비영속(재생성).
- 신기능 체크리스트(`Assets/Docs/terrain-feature-checklist.md`) 정신: "씬 이탈/세이브/로드 시 데일리 잠금·통계 보존"을 구현 시 점검.

### 10.1 로컬라이제이션 (주식 ✅ / 코인 ⛔→연동 필요)

현재 프로젝트 로컬라이제이션 구조(확인 결과):
- `LanguageManager`(싱글톤, DontDestroyOnLoad). 언어 **Korean/English/Chinese 3종**. `DataSheetCache`에서 CSV 시트 로드, `L(key)`/`LF(key,args)` 제공.
- CSV는 `Assets/StreamingAssets/Data/*.csv`, 헤더 `key,kr,en,cn`.

**주식 = 이미 잘 적용됨.**
- `Assets/StreamingAssets/Data/Stock_Localization.csv`에 회사명(`STOCK_COMP_*`)·태그(`STOCK_TAG_*`)·뉴스(`NEWS_*`)·UI 문자열(`ui_stock_*`)이 **KR/EN/CN 3개 언어 모두** 채워져 있다.
- `LanguageManager.InitializeLocalization()`이 이 파일을 **인스펙터 목록에 없어도 무조건 자동 로드**(`AddSheetIfMissing(..., "Stock_Localization.csv", ...)`).
- `Stock.UI.StockLoc.L/LF`가 `LanguageManager`를 호출하고, **키 누락 시 한국어 폴백** → CSV에 키가 빠져도 UI가 깨지지 않음.
- 결론: 주식 UI는 코드·데이터 양쪽에서 **정상 다국어 적용**. (리테마 §9에서 텍스트 키는 그대로 두므로 영향 없음.)

**코인 = 아직 없음 → 같은 방식으로 신규 연동(필수 작업).**
1. **`Assets/Scripts/Coin/UI/CoinLoc.cs`** 신규 — `StockLoc`과 동일한 폴백 헬퍼(`L(key,fallback)`, `LF(key,fallback,args)`).
2. **`Assets/StreamingAssets/Data/Coin_Localization.csv`** 신규 — 헤더 `key,kr,en,cn`. **코인명은 번역 대상이 아님**(영문 고정, `CoinData.name`). 번역하는 것은 **플레이버 + UI 문자열**뿐:
   - 플레이버: `COIN_FLAVOR_GRAVEL`(컨셉 설명 문장) — kr/en/cn.
   - UI: `ui_coin_up`(상승/UP), `ui_coin_down`(하락/DOWN), `ui_coin_stake`(베팅액), `ui_coin_payout`(예상 손익), `ui_coin_rounds_left`(오늘 남은 판 {0}/{1}), `ui_coin_delist`(💀 상장폐지), `ui_coin_moon`(🚀 떡상), `ui_coin_locked_today`(오늘은 {0}만 가능), `ui_coin_min_bet`(최소 ₩{0:N0}) 등.
3. **`LanguageManager.InitializeLocalization()`에 한 줄 추가**(수정 항목): Stock과 동일하게
   ```csharp
   bool hasCoin = false;
   AddSheetIfMissing(sheets, cache, "Coin_Localization.csv", ref hasCoin);
   ```
   (또는 인스펙터의 `localizationCsvFiles`에 `Coin_Localization.csv` 추가.)
4. 코인 UI는 UI 문자열을 `CoinLoc.L/LF`로 출력하고, 설명은 `CoinData.flavorKey`로 가져온다. **코인명(`CoinData.name`)은 영문 그대로 출력**(번역 안 함).
5. `LanguageManager.OnLanguageChanged` 구독해 언어 변경 시 코인 UI도 갱신(주식 UI와 동일 패턴).

> 요약: **주식은 손댈 것 없음(이미 다국어 정상).** 코인은 `CoinLoc` + `Coin_Localization.csv` + `LanguageManager` 한 줄 + UI에서 `CoinLoc` 사용, 4가지만 추가하면 동일 수준의 다국어가 된다.

---

## 11. 에디터 제작 절차 (사람이 수행)

1. **씬 생성**: `Assets/Scenes/Demo/MarketScene.unity`(URP 2D), `EventSystem`. **Build Settings 등록.**
2. **Canvas**: Screen Space-Overlay, CanvasScaler 1920×1080, Match 0.5.
3. **배경/프레임**: `ScreenRoot`(Image `screenBg`), (선택) `MonitorBezel`·`ScanlineOverlay`(Raycast Off).
4. **상단바**: Brand 로고(그라데이션), `ModeToggle`(주식/코인), `GoldDisplay`.
5. **주식 계열**: 기존 주식 UI를 `StockModeRoot` 하위로 이식, `StockUIController` 참조 재연결, 색 §3.1로 갱신.
6. **코인 계열**: `CoinModeRoot` 하위에 `CoinSelectView`(CoinCard 프리팹 ×10+), `BettingView`(CoinChartUI·StakePanel·UP/DOWN·PayoutPreview·StreakHUD·NewsTicker·ResultBanner).
7. **매니저**: `MarketSceneController`, `CoinGameManager` 생성 후 참조 연결.
8. **데이터 에셋**: `CoinTableSO` 에셋 생성 → §7.4 슬롯 10종 + §7.7 `replacementNames` 풀 입력. `CoinGameManager.coinTable` 할당.
9. **폰트**: 픽셀 TMP 폰트(숫자) + 한글 본문 폰트.
10. **로컬라이제이션**: `Assets/StreamingAssets/Data/Coin_Localization.csv`(`key,kr,en,cn`) 작성 — 코인명/플레이버/UI 키 3개 언어(§10.1). `LanguageManager`가 로드하도록 한 줄 추가 또는 인스펙터 목록에 등록.
11. **진입 오브젝트**: 정착지 씬에 `MarketTerminalInteractable`(BedInteractable 패턴) → MarketScene Additive 로드/언로드.
12. **사운드**: 극단 이벤트·틱·승/패 SFX를 SoundManager에 등록(선택).

---

## 12. 구현 순서 체크리스트

- [ ] 1. `Market/MarketTheme.cs` (팔레트)
- [ ] 2. `Coin/Data/*` (enums, CoinData, CoinTableSO, CoinRoundResult, CoinSaveData)
- [ ] 3. `Coin/Systems/CoinPriceEngine.cs` (승률50%·비례·극단)
- [ ] 4. `Coin/Core/CoinGameManager.cs` (하루1코인 잠금·베팅·세션)
- [ ] 5. `Coin/UI/CoinChartUI.cs`
- [ ] 6. `Coin/UI/*` (CoinModeUI, CoinSelect/Card, Betting, StreakHUD, NewsTicker, ResultPopup)
- [ ] 7. `Market/MarketSceneController.cs`
- [ ] 8. 코인 로컬라이제이션: `Coin/UI/CoinLoc.cs` + `Coin_Localization.csv` + `LanguageManager` 한 줄 (§10.1)
- [ ] 9. 기존 `Stock.UI` 색상 리테마 (§9)
- [ ] 10. 세이브 연동 (`CoinSaveData` ↔ `SaveManager`)
- [ ] 11. (선택) `Coin/Testing/CoinTestRunner.cs`로 승률·EV·극단빈도 검증
- [ ] 12. 에디터: MarketScene·프리팹·CoinTable·Coin_Localization.csv·진입 단말기·Build Settings (§11)
- [ ] 13. 사람 플레이테스트 → 밸런스 튜닝(§13)
- [ ] 14. `CLAUDE.md`에 Stock/Coin 시스템 + 본 문서 참조 추가

---

## 13. 밸런스 & 안티-익스플로잇 노트

> **설계 전환**: 초기 "공정 게임(EV=0)" 모델 → **EV 항상 마이너스(도파민 싱크, 수입원 방지)**. 승률(부호)은 50% 유지하되 손익 '크기'를 비대칭으로 만든 것이 핵심. 아래는 현재 구현 기준.

- **EV는 항상 마이너스**: `effLossCap > effWinCap×(1−edge)` + 양방향 `fee` 라 부호 50/50이어도 장기 손실. 저배율은 ≈−fee(잔잔), 고배율은 캡이 물려 −7%(GRAVEL)~−37%(ABYSS)/판. "신나게 지르는 방식이 가장 빨리 녹는다".
- **양방향 베팅세(`fee`, 기본 2%)**: 승패 무관 매 베팅마다 `stake×fee` 레이크 = 확실한 money sink. 키우면 본전치기 플레이어도 마름.
- **승률 50%는 손대지 않는다**: 불리함은 전부 손익 크기(`winCap`/`lossCap`/`houseEdge`/`fee`/`streakPayoutDecay`)와 레버리지에서. 승률을 낮추면 도파민·공정성이 깨지고 잭팟이 *작아지지 않고 드물어질 뿐*이라 비효율.
- **연승 복리 차단 2겹**: ① `streakPayoutDecay`(연승 1당 적중 보상↓) + `streakGreed`(연승 시 상폐 확률↑) ② **일일 출금 한도**(`withdrawLimitMultiplier`, 기본 5) — 코인 이익은 `첫 베팅액×5`까지만(골드 상한 = 시작 골드 + 첫 베팅액×5). 베팅·손실은 무제한이라 **전재산 올인(=전액 청산) 드라마는 보존**. `dayStartGold`·`dayFirstStake` 영속(같은 날 재접속 우회 불가).
- **레버리지 = 위험-보상 다이얼**: 배율↑ = `effWinCap`↑(큰 승리) + `effLossCap`↑(마진 초과청산) + EV↓. `effWinCap`도 같이 키워 "캡 닿는 최소 배율만 쓰는" 지배 전략을 없앴다(§7.1). `dailyRoundLimit=5`가 작아야 저배율로 한도를 못 긁으므로 **5판 유지가 레버리지 매력의 기둥**.
- **소프트 초과청산(L1, 빚 없음)**: 빗나감은 마진을 넘어 잃을 수 있지만(`lossCap`) 손실은 **보유 골드에서 바닥**(음수 골드/이자/압류 없음). 골드는 본편 공유 통화라 빚이 진행을 마비시키는 걸 피한 의도적 선택.
- **하루 1코인 잠금 + 하루 N판 제한**(`dailyRoundLimit`, 기본 5)이 그라인딩·마틴게일을 제한. `roundsToday`·`dayStartGold` 세이브로 같은 날 재접속 우회 불가.
- **`maxBet`/`unlimitedMax`**: `maxBet`이 단판 베팅 크기를 제한해 하위 코인은 부자가 돼도 복리가 죽는다. **전재산 올인은 `unlimitedMax`인 ABYSS(최종장 코인)만** — 의도된 하이리스크. 하위 코인 `minBet/maxBet`은 자연 해금 게이트로 유지.
- **튜닝 노브 요약**: 매운맛↑ = `winCap`↓·`lossCap`↑·`fee`↑·`houseEdge`↑. 잭팟 크기 = `withdrawLimitMultiplier`. 연승 납작함 = `streakPayoutDecay`↑. 검증은 `CoinTestRunner`(레버리지 1·streak 0 → 승률≈50%·평균손익률<0).

---

## 부록 A. 신규/수정 파일 목록

**신규**
```
Assets/Scripts/Market/MarketTheme.cs
Assets/Scripts/Market/MarketSceneController.cs
Assets/Scripts/Coin/Core/CoinGameManager.cs
Assets/Scripts/Coin/Systems/CoinPriceEngine.cs
Assets/Scripts/Coin/Data/CoinEnums.cs
Assets/Scripts/Coin/Data/CoinData.cs
Assets/Scripts/Coin/Data/CoinTableSO.cs
Assets/Scripts/Coin/Data/CoinRoundResult.cs
Assets/Scripts/Coin/Data/CoinSaveData.cs
Assets/Scripts/Coin/UI/CoinModeUI.cs
Assets/Scripts/Coin/UI/CoinSelectUI.cs
Assets/Scripts/Coin/UI/CoinCardUI.cs
Assets/Scripts/Coin/UI/CoinBettingUI.cs
Assets/Scripts/Coin/UI/CoinChartUI.cs
Assets/Scripts/Coin/UI/CoinLoc.cs                   (로컬라이제이션 헬퍼, StockLoc 미러)
Assets/Scripts/Coin/UI/StreakHUD.cs                 (선택, 재미요소)
Assets/Scripts/Coin/UI/NewsTicker.cs                (선택, 재미요소)
Assets/Scripts/Coin/UI/CoinResultPopupUI.cs         (선택)
Assets/Scripts/Coin/Testing/CoinTestRunner.cs       (선택)
Assets/Scenes/Demo/MarketScene.unity                (에디터)
Assets/StreamingAssets/Data/Coin_Localization.csv   (key,kr,en,cn — 코인명/플레이버/UI)
Assets/.../CoinTable.asset                           (에디터, SO 인스턴스)
```

**수정**
```
Assets/Scripts/Stock/UI/StockUIController.cs        (씬 모드 대응, 심리바 색)
Assets/Scripts/Stock/UI/StockListItemUI.cs          (색 기본값 + 좌측 액센트 바)
Assets/Scripts/Stock/UI/CompanyDetailUI.cs          (색 기본값)
Assets/Scripts/Stock/UI/MiniChartUI.cs              (색 기본값, 선택적 글로우)
Assets/Scripts/_Core/Managers/LanguageManager.cs    (Coin_Localization.csv 자동 로드 한 줄)
CLAUDE.md                                            (시스템·문서 참조 추가)
```

---

## 14. 주식 UI 거래 동선 개편 (2026-08-30)

기존에는 보유(포트폴리오) 탭에서도 매도가 가능했고, 시세 상세에는 인라인 수량 입력이 있었다.
아래처럼 **"조회는 보유탭, 거래는 시세탭, 수량은 팝업"** 으로 역할을 갈랐다.

### 14.1 보유탭은 조회 전용

- `PortfolioItemUI`의 매도 버튼은 런타임에 숨긴다(`sellButton.gameObject.SetActive(false)`).
  필드·프리팹 연결은 그대로 둔다 — 프리팹을 건드리면 기존 씬 참조가 깨지므로 코드에서만 끈다.
- 대신 **행 전체가 버튼**이 된다(`EnsureRowButton`). 루트에 `Graphic`이 있으면 거기에 `Button`을 붙이고,
  없으면 투명 `ClickArea`를 깔아 붙인다. 클릭 → `StockUIController.ShowInStockTab(companyId)`.
- 키보드도 같다 — W/S로 고른 행에서 **Space**를 누르면 같은 이동이 일어난다.

### 14.2 보유 → 시세 이동 시 필터가 따라간다

`StockUIController.ShowInStockTab()`은 순서대로:

1. 시세 탭으로 전환
2. `StockListUI.ClearSearch()` — 남아 있던 검색어 때문에 방금 고른 종목이 목록에서 사라지지 않게
3. `StockListUI.SetHoldingsFilter(true)` → `StockFilterPanel.SetOnlyHoldings(true)`
4. `StockListUI.SelectCompany(id)` — 상세 패널에 펼치고 스크롤을 맞춘다

**순서를 바꾸지 말 것.** 필터를 켜면 목록이 재생성되므로 선택은 그 뒤여야 커서·스크롤이 맞는다.

### 14.3 필터 패널 구조 — 보유중은 태그와 분리

`StockFilterPanel`은 위에서 아래로 이렇게 쌓인다:

```
[보유]            ← 헤더 (showHeaders)
[보유중]          ← HoldingsFlow (on/off 하나뿐)
───────────────   ← Divider
[정렬]
[기본순][가격 낮은순][가격 높은순][등락률순]  ← SortFlow (라디오, 항상 하나 선택)
───────────────   ← Divider
[태그]
[전체] [태그칩...]  ← TagFlow (3단계 순환: 해제→포함→제외)
```

**순서의 근거**: 위의 둘(보유·정렬)은 칩 개수가 고정이라 항상 같은 자리에 있고,
개수가 종목 데이터에 따라 늘어 여러 줄로 흐르는 태그를 맨 아래에 둔다 —
태그가 늘어도 위 두 섹션의 위치가 흔들리지 않는다.

정렬 칩 라벨은 `StockListUI.BuildSortLabels()`가 `SortCycle` 순서대로 넘기고,
`StockFilterPanel.SortIndex`가 그 인덱스를 돌려준다 — 정렬 상태의 단일 원천은 패널이다.
구 툴바 정렬 버튼은 런타임에 숨겨지고 참조도 null로 놓아, 키보드 포커스가 안 보이는 버튼에 걸리지 않는다.

**보유중을 태그 flow에 섞지 말 것.** 태그가 아니라 보유 여부로 거르는 축이라 3단계 순환도 안 하고,
섞어 두면 [전체]를 눌러 태그를 지울 때 같이 꺼지는 걸로 오해하게 된다.
섹션을 추가하면 `FitHeightToContent`의 `sections` 개수와 높이 합, `ReflowAll`의 flow 목록을
**둘 다** 같이 고쳐야 한다(한쪽만 고치면 패널이 잘리거나 아래가 비어 보인다).

판정은 `StockListUI.GetSortedFiltered`에서 `PortfolioManager.GetHolding(id).Shares > 0`,
태그 필터와 **AND**로 걸린다.

### 14.4 진입 시 필터 초기화

`StockUIController.Open()`이 `stockListUI.Build()` 직후 `ResetFilters()`를 부른다
→ 태그 선택 없음([전체] 켜짐) · 보유중 꺼짐 · 검색어 없음.

즉 **마켓에 들어가 시세탭으로 바로 가면 항상 전 종목이 보이고**, 보유탭에서 종목을 눌러 넘어갈 때만
`ShowInStockTab`이 보유중을 다시 켠다. 지난 방문의 필터가 남아 목록이 텅 비어 보이는 사고를 막는 장치다.

목록 제목도 이 상태를 따라간다 — 씬의 `AllStockText`(`LocalizedTextUI`)의 키를
`UpdateListHeader()`가 `ui_stock_list_header`("전체 종목") ↔ `ui_stock_list_header_holdings`("보유 종목")로
갈아 끼운다(키만 바꾸고 `UpdateText()` 호출 — 언어 전환은 그대로 `LocalizedTextUI`가 처리).
옆에 있던 종목 수 표시(`CountText` / `ui_stock_count`)는 제목이 이미 무엇을 보고 있는지 말하므로 제거했다.

### 14.5 종목 검색

깔때기(필터) 버튼 오른쪽의 `TMP_InputField`. 매칭은 `MatchesSearch` —
**표시 이름(현재 언어) 또는 종목 id의 부분일치**, 공백 제거 + 소문자 비교.
태그·보유중 필터와 AND로 함께 걸린다.

씬에서 직접 만들어 `StockListUI.searchInput`에 연결하는 것이 기본이다(제작 절차는 `ui-build-guide.md`).
색·폰트·글자 크기·플레이스홀더 문구는 `ApplySearchTheme()`이 열 때마다 입히므로 씬에서 맞출 필요가 없다.
입력은 **매 프레임 폴링**(`PollSearchInput`)으로 반영한다. `onValueChanged`는 **확정된** 텍스트에만 오므로
한글은 다음 글자를 쳐야 검색되는데, 폴링에서 `InputField.text` + `Input.compositionString`(조합 중인 글자)을
합쳐 보기 때문에 "데"를 치는 순간 바로 걸러진다. 조합이 확정되면 같은 문자열이라 재생성이 두 번 일어나지 않는다.

⚠ **포커스가 있는 상태에서 검색창 텍스트를 밖에서 갈아끼우지 말 것.** 캐럿·선택 인덱스가 옛 길이를 가리킨 채
남아, 다음 타이핑에서 TMP 내부 `String.Remove`가 `ArgumentOutOfRangeException`을 뱉고 **입력이 통째로 먹통**이 된다.
그래서 `ClearSearch`는 `ClearInputSafely`(포커스 해제 → 텍스트 비움 → 인덱스 0으로)를 거치고,
`PollSearchInput`은 `GuardCaretRange`로 인덱스가 문자열 길이를 넘었는지 매 프레임 값싸게 되돌린다.
연결돼 있으면 코드는 리스너만 붙인다. 비어 있고 `autoCreateSearchField`가 켜져 있을 때만
`CreateSearchField()`가 깔때기의 형제로 임시 검색창을 만든다(툴바에 레이아웃 그룹이 없어
깔때기의 anchoredPosition·폭에서 좌표를 직접 계산한다).

### 14.6 수량은 팝업에서만 정한다

`StockSellDialog` → **`StockTradeDialog`** 로 확장(매수/매도 공용, 전부 코드 생성).

| | 상한 | 기본 수량 | 확정 버튼 |
|---|---|---|---|
| 매수 | 소지금 ÷ 현재가 | 1 | 초록(`MarketTheme.Up`) |
| 매도 | 보유 주수 | 전량 | 빨강(`MarketTheme.Down`) |

- `CompanyDetailUI`의 매수·매도 버튼은 체결하지 않고 `OpenBuy`/`OpenSell`만 부른다.
  인라인 수량 위젯(`quantityInput`/`−`/`+`/`최대`/`orderAmountText`)은 `HideLegacyQuantityUI()`로 숨긴다.
- 상세 버튼은 `UpdateTradeButtons()`가 '가능/불가'만 판정해 `interactable`을 갱신한다(수량 판단 없음).
- 팝업 키보드: W/S = ±1, A/D = ±10, Space = 확정, ESC = 취소.
  **연 프레임의 키는 한 프레임 무시**(`_openFrame`) — 목록에서 누른 Space가 그대로 확정으로 새는 것을 막는다.
- 팝업이 떠 있는 동안 `StockListUI`(키·휠)와 `PortfolioUI`·탭 단축키는 `StockTradeDialog.IsOpen`으로 입력을 넘긴다.

### 14.7 신규 로컬라이제이션 키

`ui_stock_buy_qty` / `ui_stock_buy_max` / `ui_stock_buy_info` / `ui_stock_buy_cost` /
`ui_stock_filter_holdings` / `ui_stock_filter_holdings_header` / `ui_stock_filter_sort_header` /
`ui_stock_list_header_holdings` /
`ui_stock_search_placeholder` (`Stock_Localization.csv`)
