# NEON 마켓 UI 제작 가이드 (에디터)

> `MarketScene`의 주식·코인·마켓 UI를 **Unity 에디터에서 조립**하는 실전 가이드.
> Hierarchy 구조 + 각 GameObject에 붙일 컴포넌트 + 모든 SerializeField 연결 대상을 정리한다.
> 설계 근거/규칙은 [design.md](design.md), 색 팔레트는 design.md §3.1 (= `Market/MarketTheme.cs`).

코드는 모두 작성 완료(`Assets/Scripts/Market/**`, `Assets/Scripts/Coin/**`, `Stock.UI` 리테마). 이 문서는 **씬·프리팹 조립과 참조 연결**만 다룬다.

---

## 0. 사전 준비

- 스크립트 컴파일 통과 확인(콘솔 에러 0).
- **TMP 폰트 2종**: 숫자 강조용 픽셀 폰트(Galmuri11/NeoDunggeunmo 등 → TMP Font Asset) + 한글 본문 폰트.
- **색은 인스펙터에서 직접 지정**: 코드 기본값은 *새 인스턴스/Reset* 때만 적용된다. 프리팹·씬 오브젝트는 §9 색표(hex)를 보고 인스펙터에서 맞춘다.
- **선행 매니저(메인 게임에 이미 존재해야 함, DontDestroyOnLoad)**: `StockGameManager`, `LanguageManager`, `SaveManager`, `DayCycleManager`, `PlayerStat`.
  - MarketScene은 정착지 위에 **Additive 로드**되므로 이들은 이미 살아있다.
  - MarketScene을 **단독 실행으로 테스트**할 땐 이 매니저들을 임시로 씬에 넣어야 한다(특히 `StockGameManager`, `PlayerStat`).

---

## 1. 제작 순서 (권장)

1. 씬 루트 · Canvas · EventSystem · 매니저 (§2)
2. TopBar (§3)
3. 주식 모드 `StockModeRoot` (§4) + 주식 행 프리팹 3종 (§5)
4. 코인 모드 `CoinModeRoot` (§6) + 코인 카드 프리팹 (§7)
5. 매니저/컨트롤러 참조 일괄 연결 (§8)
6. 색·레이아웃 마감 (§9), Build Settings 등록, 진입 단말기 (§10)

> 컴포넌트는 **자식 텍스트/이미지를 먼저 다 만든 뒤** 부모에 스크립트를 붙이고 필드를 끌어다 연결하면 빠르다.

---

## 2. 씬 루트 · Canvas · 매니저

```
MarketScene
├─ EventSystem                         (Standalone Input Module)
├─ Managers
│   ├─ MarketSceneController           [Market.MarketSceneController]
│   └─ CoinGameManager                 [Coin.Core.CoinGameManager]
└─ Canvas                              (Screen Space - Overlay)
    │  · CanvasScaler: Scale With Screen Size / 1920×1080 / Match 0.5
    │  · GraphicRaycaster
    ├─ MonitorBezel        (Image, 장식, 선택) — Raycast Target ✗
    ├─ ScreenRoot          (Image = screenBg #0D1117) ← 단말기 화면
    │   ├─ TopBar          (§3)
    │   ├─ StockModeRoot   (§4)
    │   └─ CoinModeRoot    (§6)
    └─ ScanlineOverlay     (Image + [Market.CRTEffectController]) — CRT 효과, Raycast Target ✗ (§12)
```

- `StockGameManager`는 **여기 두지 않는다**(DontDestroyOnLoad — 메인 게임에 존재). 단독 테스트 시에만 임시 배치.
- `CoinGameManager`는 **씬에 둔다**. 인스펙터 `Coin Table` 필드는 **비워둬도 동작**(코드 `DefaultSlots()` 폴백). 튜닝하려면 §7-끝의 SO 에셋을 만들어 할당.

### MarketSceneController 필드 연결
| 필드 | 타입 | 연결 대상 |
|------|------|-----------|
| `stockModeRoot` | GameObject | `StockModeRoot` |
| `coinModeRoot` | GameObject | `CoinModeRoot` |
| `stockUI` | StockUIController | `StockModeRoot`의 StockUIController |
| `coinUI` | CoinModeUI | `CoinModeRoot`의 CoinModeUI |
| `stockTabButton` | Button | TopBar의 `Btn_주식` |
| `coinTabButton` | Button | TopBar의 `Btn_코인` |
| `goldText` | TextMeshProUGUI | TopBar의 `GoldText` |
| `closeButton` | Button | TopBar의 `Btn_닫기` |
| `sceneName` | string | `MarketScene` (Additive Unload 대상) |

### CoinGameManager 필드 연결
| 필드 | 타입 | 연결 대상 |
|------|------|-----------|
| `coinTable` | CoinTableSO | (선택) `CoinTable.asset` — 비우면 코드 기본 라인업 |

---

## 3. TopBar (상단바)

레퍼런스: 좌측 브랜드 / 중앙 `주식·코인` 토글 / 우측 골드.

```
TopBar  (가로 Layout 또는 앵커 배치)
├─ Brand
│   ├─ LogoIcon   (Image, brandA→brandB 그라데이션 느낌)
│   ├─ Title      (TMP "NEON 마켓")
│   └─ Subtitle   (TMP "TRADING TERMINAL", textMuted, 대문자+자간)
├─ ModeToggle     (가로 Layout, 알약형 배경)
│   ├─ Btn_주식    (Button + TMP "주식")  ← 활성: gold 채움 / 비활성: textMuted
│   └─ Btn_코인    (Button + TMP "코인")
└─ GoldArea
    ├─ CoinIcon   (Image, gold)
    └─ GoldText   (TMP "{0:N0} G")
```

- 토글 버튼 2개는 `MarketSceneController.stockTabButton/coinTabButton`에 연결(§2). 활성/비활성 색 전환은 버튼의 Color Tint 또는 별도 스크립트로 처리(선택). 기본은 클릭만 동작.
- `GoldText`는 `MarketSceneController.goldText`로 연결 — `PlayerStat.OnGoldChanged` 구독으로 자동 갱신.

---

## 4. 주식 모드 `StockModeRoot` (기존 시스템 이식 + 리테마)

> **로직 불변.** 기존 `Stock.UI` 컴포넌트를 그대로 쓰고 색만 §9로 맞춘다.

```
StockModeRoot                         [Stock.UI.StockUIController]
├─ TabBar
│   ├─ Btn_시세       (Button)        · 활성 시 accentCyan 언더라인
│   ├─ Btn_뉴스       (Button) + Badge(TMP 카운트)
│   ├─ Btn_포트폴리오 (Button)
│   └─ Sentiment
│       ├─ SentimentFill (Image, Filled-Horizontal) · 색 = MarketTheme.SentimentColor
│       └─ SentimentText (TMP "탐욕 0.55")
├─ StocksTab
│   ├─ StockList     [Stock.UI.StockListUI]
│   │   ├─ Toolbar    (선택) 정렬·필터 바 — 좌→우 가로 배치 권장
│   │   │   ├─ SortButton    (Button + 자식 TMP 라벨, 선택적 아이콘 Image) ← 클릭마다 순환
│   │   │   ├─ FilterButton  (Button, 깔때기 아이콘) ← 클릭하면 FilterPanel 토글
│   │   │   └─ FilterPanel   [Stock.UI.StockFilterPanel] (빈 UI 오브젝트 — 태그칩 자동 생성)
│   │   │                    ↑ 깔때기 오른쪽에 배치, 적당한 폭 지정. 초기 숨김은 코드가 처리
│   │   └─ Scroll View → Viewport → Content   ← 행 생성 컨테이너
│   └─ CompanyDetail [Stock.UI.CompanyDetailUI]
│       ├─ Name/MarketCap/Price/Change (TMP)
│       ├─ TagChips  [Stock.UI.TagChipList]  (빈 UI 오브젝트 — 칩 자동 생성)
│       ├─ MiniChart  [Stock.UI.MiniChartUI]  (빈 UI 오브젝트)
│       ├─ LatestNews/Holding (TMP)
│       ├─ QtyRow: QuantityInput(TMP_InputField "6") + Btn_10% / Btn_25% / Btn_50% / Btn_100%
│       ├─ OrderAmount (TMP "₩276,600")
│       └─ Trade: Btn_매도 + Btn_매수
├─ NewsTab           [Stock.UI.NewsTabUI]
│   └─ Scroll View → Content
└─ PortfolioTab      [Stock.UI.PortfolioUI]
    ├─ Summary: TotalValue/ProfitRate/Patience (TMP)
    ├─ Scroll View → Content
    └─ EmptyLabel (TMP "보유 종목 없음", 선택)
```
> 수량은 **TMP_InputField에 직접 입력**한다(Content Type=Integer Number). 비율 버튼(10/25/50/100%)은 **소지금 대비 해당 비율로 현재가에 살 수 있는 최대 주식 수**를 계산해 입력 필드에 채운다(100% = 최대 매수). 주문 금액 = 수량 × 현재가, 자동 계산·표시.

### StockUIController 필드 연결 (StockModeRoot)
| 필드 | 연결 대상 |
|------|-----------|
| `panelRoot` | `StockModeRoot` 자기 자신(또는 내부 패널) |
| `closeButton` | (선택) 비워도 됨 — 닫기는 TopBar가 담당 |
| `toggleKey` | `None` |
| `stocksTab` / `newsTab` / `portfolioTab` | `StocksTab` / `NewsTab` / `PortfolioTab` |
| `stocksTabButton` / `newsTabButton` / `portfolioTabButton` | TabBar의 각 버튼 |
| `stockListUI` | `StockList`의 StockListUI |
| `newsTabUI` | `NewsTab`의 NewsTabUI |
| `portfolioUI` | `PortfolioTab`의 PortfolioUI |
| `goldText` | (선택) 비워도 됨 — TopBar가 골드 표시 |
| `sentimentFill` | `SentimentFill` (Image, Image Type=Filled) |
| `sentimentText` | `SentimentText` |

### StockListUI 필드 연결
| 필드 | 연결 대상 |
|------|-----------|
| `container` | `StockList/Scroll View/Viewport/Content` (Vertical Layout Group + Content Size Fitter — 없으면 코드가 자동 부착·설정) |
| `itemPrefab` | `StockListItem` 프리팹 (§5) |
| `detail` | `CompanyDetail`의 CompanyDetailUI |
| `countText` | (선택) StockList 헤더의 종목 수 TMP — `ui_stock_count`로 자동 지역화 (§11) |
| `sortButton` | (선택) `Toolbar/SortButton` (Button) — 클릭마다 **기본→가격 낮은순→가격 높은순→등락률→(반복)** 순환. 비우면 상장순 고정 |
| `sortLabel` | (선택) 정렬 버튼 라벨 TMP. 비우면 `sortButton` 자식에서 자동 탐색 |
| `sortIcon` | (선택) 정렬 버튼 아이콘 Image. `sortIcons`가 있으면 상태별 스프라이트로 교체 |
| `sortIcons` | (선택) 정렬 상태별 스프라이트 배열 — 순환 순서와 1:1: `[0]`기본 `[1]`가격↓ `[2]`가격↑ `[3]`등락률 |
| `filterButton` | (선택) 깔때기(필터) 아이콘 Button — 클릭하면 `filterPanel`을 토글. 비우면 필터 UI 미표시 |
| `filterPanel` | (선택) `Toolbar/FilterPanel`의 StockFilterPanel — 태그칩 자동 생성. 비우면 필터 없음(전체 표시) |
| `rowHeight` | 한 종목 행 높이(px, 기본 240). Content가 이 값 × 종목 수로 자동 사이징 |

> **정렬 버튼과 필터(깔때기+패널)는 모두 선택 필드**다. 비우면 종전과 동일하게 상장순·전체 표시로 동작한다. 정렬 버튼은 **한 번 누를 때마다 한 단계씩 순환**(기본→가격↓→가격↑→등락률→기본…)하며 라벨(과 `sortIcons` 지정 시 아이콘)이 현재 상태를 보여준다.
>
> **태그 필터**는 `filterButton`(깔때기)을 누르면 오른쪽에 `filterPanel`(StockFilterPanel)이 열린다. 섹터 섹션은 없다 — 종목 분류가 태그로 통일됐다. 칩은 **3단계 순환**: 해제 → **포함**(시안 하이라이트) → **제외**(취소선+레드 글자+어두운 레드 배경, 인스펙터 `excluded*` 3필드로 조정) → 해제. **포함 태그는 OR**(하나라도 가지면 남는다 — 고를수록 넓어진다), **제외 태그는 AND NOT**(하나라도 가지면 목록에서 빠진다, 포함보다 우선). [전체] 칩은 포함·제외를 모두 비운다. 태그 목록·칩·둥근 배경 스프라이트는 종목 데이터에서 **런타임 자동 생성**(프리팹·에셋 불필요), 포함 하이라이트는 `MarketSelectHighlight`(시안 배경)가 담당한다. 정렬·필터는 엔진 틱과 무관하게 **사용자 조작 시에만** 행을 재정렬한다(시세 변동으로 매 틱 튀지 않음). 버튼 클릭 효과음은 `MarketButtonSfx`가 씬 전체 버튼에 자동으로 붙는다.
>
> **크기·색·지역화**: 칩 크기(폰트·여백·간격 등)는 인스펙터 의존 없이 `StockFilterPanel.cs` 상단 코드 필드에서 강제된다(직렬화 안 함 — 값 수정 시 Reset 없이 반영). 태그 칩 색은 `TagChipList`와 동일 팔레트(같은 태그=같은 색), [전체] 칩만 중립색. **태그 표시명은 지역화**된다 — `STOCK_TAG_{대문자}` 키(`Stock_Localization.csv`, 없으면 raw 폴백). 태그 지역화는 필터뿐 아니라 `TagChipList`를 쓰는 **모든 곳**(CompanyDetail·News 칩)에 적용된다. 단 **매칭·저장 로직은 raw 영문 태그** 그대로 쓰고 표시만 번역한다. 칩 나열 순서는 현재 언어의 표시명 기준으로 정렬돼 자연스럽게 읽힌다([전체]는 항상 맨 앞). **패널 박스 세로 높이는 콘텐츠에 맞춰 코드가 자동 조정**(`FitHeightToContent` — 상단 모서리 고정, 아래로만 늘고 줆)하므로 세로 크기는 신경 쓰지 않아도 된다(가로 폭만 넉넉히).
>
> **Content 자동 사이징**: `StockListUI`가 Content에 `VerticalLayoutGroup`(childForceExpandHeight=off) + `ContentSizeFitter`(세로=PreferredSize)를 보장하고, 각 행에 `LayoutElement`(min=preferred=`rowHeight`)를 박는다. 덕분에 Content 높이가 **종목 수 × rowHeight로 딱 맞게** 자동 조정되고(위아래 남는 칸 없음), 종목이 많아도 행 높이가 `rowHeight` 아래로 찌그러지지 않는다.

### CompanyDetailUI 필드 연결
| 필드 | 연결 대상 |
|------|-----------|
| `root` | CompanyDetail 패널 루트 |
| `nameText`/`marketCapText`/`priceText`/`changeText` | 각 TMP |
| `tagChips` | `TagChips`의 TagChipList (연결 시 태그를 알약 칩으로 표시) |
| `tagsText` | (선택) `tagChips` 미연결 시 폴백용 TMP — 비워도 됨 |
| `miniChart` | `MiniChart`의 MiniChartUI |
| `latestNewsText`/`holdingText` | 각 TMP |
| `quantityInput` | `QtyRow/QuantityInput` (TMP_InputField, Content Type=Integer Number) |
| `orderAmountText` | `OrderAmount` (TMP "₩276,600") |
| `percent10Button`/`percent25Button`/`percent50Button`/`percent100Button` | `Btn_10%`/`Btn_25%`/`Btn_50%`/`Btn_100%` (소지금 비율 = 최대 매수 수량) |
| `buyButton`/`sellButton` | `Btn_매수`/`Btn_매도` |
| 색상 3종 | §9 (up/down/textDim) |

> **TagChips 셋업(에디터).** 기존 `Tags` TMP 자리에 빈 UI 오브젝트 `TagChips`를 두고 `TagChipList`를 부착한다.
> `HorizontalLayoutGroup`/`ContentSizeFitter`는 컴포넌트가 런타임에 자동 부착하며, 둥근 배경 스프라이트도 코드에서 생성하므로 **프리팹·스프라이트 에셋 준비 불필요**.
> **태그별 색상**: `colorPerTag`(기본 켜짐)이면 태그 문자열 해시로 `palette`(MarketTheme 네온 8색)에서 색을 자동 배정한다 — 같은 태그는 항상 같은 색, 배경은 `bgTint`(0.2)만큼 은은히 틴트된 다크, 텍스트는 선명한 액센트. 특정 태그 색 고정은 `overrides`(tag→accent)로. 끄면 단색(`chipColor`/`textColor`) 사용.
> 태그가 많아 한 줄을 넘기면 `fontSize`/`paddingX`를 줄이거나 폭을 넓힌다(가로 배치 = 줄바꿈 없음).

### NewsTabUI 필드 연결
| 필드 | 연결 대상 |
|------|-----------|
| `container` | `NewsTab/Scroll View/.../Content` |
| `itemPrefab` | `NewsItem` 프리팹 (§5) |
| `maxHistory` | 15 (기본) |

### PortfolioUI 필드 연결
| 필드 | 연결 대상 |
|------|-----------|
| `totalValueText`/`profitRateText`/`patienceText` | 각 TMP |
| `container` | `PortfolioTab/Scroll View/.../Content` |
| `itemPrefab` | `PortfolioItem` 프리팹 (§5) |
| `emptyLabel` | `EmptyLabel` (선택) |
| 색상 3종 | §9 |

---

## 5. 주식 행 프리팹 3종

각 프리팹은 `Project` 창에서 만들고, 위 컨테이너의 `itemPrefab`에 할당한다(씬에 직접 두지 않음).

### 5-1. `StockListItem` 프리팹 — [Stock.UI.StockListItemUI]
```
StockListItem (Button + Image=background)
├─ LeftAccent (Image, accentCyan)   ← 선택 시만 보임. 기본 Enabled ✗
├─ Name   (TMP)
├─ Tags   (TMP, textMuted)  ← 구 Sector 자리. 태그를 ' · '로 이어 표시
├─ Price  (TMP, 픽셀폰트)
└─ Change (TMP)  "▲ 2.4%"
```
| 필드 | 연결 대상 |
|------|-----------|
| `nameText`/`tagText`/`priceText`/`changeText` | 각 TMP (`tagText`는 구 `sectorText` — `FormerlySerializedAs`로 기존 프리팹 연결 유지) |
| `button` | 루트 Button |
| `background` | 루트 Image |
| `leftAccent` | `LeftAccent` (Image) — **선택 강조 바** |
| `normalColor` | 투명 (0,0,0,0) |
| `selectedColor` | rowSelected `#1B2A3D` |
| `riseColor`/`fallColor`/`flatColor` | up/down/textDim |

### 5-2. `NewsItem` 프리팹 — [Stock.UI.NewsItemUI]
```
NewsItem (Image=phaseBackground, 선택)
├─ Phase (TMP) "[찌라시]"
├─ Title (TMP)
├─ TagChips [Stock.UI.TagChipList] (빈 UI 오브젝트 — 태그 칩 자동 생성, 선택)
└─ Info  (TMP, textMuted) "감정 · 남은틱"
```
| 필드 | 연결 대상 |
|------|-----------|
| `phaseText`/`titleText`/`infoText` | 각 TMP |
| `tagChips` | `TagChips`의 TagChipList (연결 시 태그를 칩으로, Info에서 태그 제외 → `ui_stock_news_active_info_notag`) |
| `phaseBackground` | 루트 Image (선택) |
| `rumorColor`/`officialColor`/`fadeColor` | gold / up / textDim |

> `tagChips`는 CompanyDetail과 동일한 `TagChipList` 컴포넌트다(셋업·색 규칙 §CompanyDetailUI 참고). 미연결 시 기존처럼 Info에 `태그 · 감정 · 남은틱`으로 표시된다(하위 호환). 히스토리 행은 태그가 없어 칩이 자동으로 비워진다(풀 재사용 안전).

### 5-3. `PortfolioItem` 프리팹 — [Stock.UI.PortfolioItemUI]
```
PortfolioItem
├─ Name/Shares/AvgPrice/CurrentPrice (TMP)
└─ Profit (TMP)  "+5.0%"
```
| 필드 | 연결 대상 |
|------|-----------|
| `nameText`/`sharesText`/`avgPriceText`/`currentPriceText`/`profitText` | 각 TMP |
| `riseColor`/`fallColor`/`flatColor` | up/down/textDim |

---

## 6. 코인 모드 `CoinModeRoot` (신규)

```
CoinModeRoot                          [Coin.UI.CoinModeUI]
├─ CoinSelectView                     [Coin.UI.CoinSelectUI]   ← 코인 라인업 선택
│   ├─ Title (TMP "오늘의 코인을 골라라")
│   └─ Scroll View → Viewport → Content (Grid/Vertical Layout)  ← CardContainer
│         (CoinCard 프리팹이 런타임 생성됨)
└─ BettingView                        [Coin.UI.CoinBettingUI]  ← 베팅 테이블
    ├─ Header
    │   ├─ CoinName  (TMP, 영문 코인명)
    │   └─ Price     (TMP, 픽셀폰트 "₩...")
    ├─ Chart         [Coin.UI.CoinChartUI]   (빈 UI 오브젝트, 영역만)
    ├─ NewsTicker    [Coin.UI.NewsTicker]
    │   └─ TickerText (TMP)
    ├─ StreakHUD     [Coin.UI.StreakHUD]
    │   ├─ StreakText (TMP "🔥 3연승")
    │   └─ SessionText(TMP "세션 +₩...")
    ├─ StakePanel   (InputField 없음 — 보유 골드 %로 베팅)
    │   ├─ PercentButtons: Btn_10% / Btn_25% / Btn_50% / Btn_100%  (Button)
    │   ├─ StakeAmount (TMP "₩X (Y%)")
    │   └─ PnlPreview  (TMP "적중 +₩.. / 빗나감 −₩..")
    ├─ LeveragePanel (InputField/드롭다운 없음 — 배율 프리셋 버튼)
    │   ├─ LeverageButtons: Btn_x1 / Btn_x2 / Btn_x5 / Btn_x10 / Btn_x25  (Button)
    │   └─ LeverageText (TMP "배율 x10 · 변동 10%↑ 청산 (최대 x25)")
    ├─ Btn_UP        (Button, up색 ▲)
    ├─ Btn_DOWN      (Button, down색 ▼)
    ├─ RoundsLeft    (TMP "오늘 남은 판 3/5")
    ├─ Btn_뒤로      (Button)
    └─ ResultBanner  [Coin.UI.CoinResultPopupUI]
        └─ Group (CanvasGroup)
            ├─ Title (TMP "🚀 떡상!")
            ├─ Delta (TMP "+₩...")
            └─ Sub   (TMP "🌱 신규 상장: ...")
```

### CoinModeUI 필드 연결
| 필드 | 연결 대상 |
|------|-----------|
| `selectView` | `CoinSelectView` |
| `bettingView` | `BettingView` |
| `selectUI` | `CoinSelectView`의 CoinSelectUI |
| `bettingUI` | `BettingView`의 CoinBettingUI |

### CoinSelectUI 필드 연결
| 필드 | 연결 대상 |
|------|-----------|
| `modeUI` | `CoinModeRoot`의 CoinModeUI |
| `cardContainer` | `CoinSelectView/.../Content` (Grid Layout Group 권장) |
| `cardPrefab` | `CoinCard` 프리팹 (§7) |

### CoinBettingUI 필드 연결
| 필드 | 연결 대상 |
|------|-----------|
| `modeUI` | CoinModeUI |
| `chart` | `Chart`의 CoinChartUI |
| `coinNameText` | `Header/CoinName` |
| `priceText` | `Header/Price` |
| `stakeButtons` | `Btn_10%`/`Btn_25%`/`Btn_50%`/`Btn_100%` (배열, 순서대로) |
| `percentValues` | `[10, 25, 50, 100]` (위 버튼과 같은 순서·길이) |
| `stakeAmountText` | `StakeAmount` (TMP "₩X (Y%)") |
| `leverageButtons` | `Btn_x1`/`Btn_x2`/`Btn_x5`/`Btn_x10`/`Btn_x25` (배열, 순서대로) |
| `leverageValues` | `[1, 2, 5, 10, 25]` (위 버튼과 같은 순서·길이) |
| `leverageText` | `LeverageText` (TMP) |
| `upButton`/`downButton`/`backButton` | `Btn_UP`/`Btn_DOWN`/`Btn_뒤로` |
| `pnlPreviewText` | `PnlPreview` |
| `roundsLeftText` | `RoundsLeft` |
| `resultPopup` | `ResultBanner`의 CoinResultPopupUI |
| `streakHud` | `StreakHUD`의 StreakHUD |
| `revealDuration` | 1.4 (기본) |

### CoinChartUI 필드 (오브젝트 참조 없음)
| 필드 | 값 |
|------|------|
| `lineThickness` | 3 |
| `maxPoints` | 40 |
> 빈 UI 오브젝트에 컴포넌트만 부착(자식 불필요). RectTransform 영역이 곧 차트 영역. 라인 색은 코드가 up/down/teal로 자동 지정.

### CoinResultPopupUI 필드 연결
| 필드 | 연결 대상 |
|------|-----------|
| `group` | `ResultBanner/Group` (CanvasGroup) — 기본 alpha 0 / 비활성 |
| `titleText`/`deltaText`/`subText` | Group 내 TMP 3개 |
| `showSeconds` | 1.2 |
| `fadeSeconds` | 0.3 |
> **주의**: CoinResultPopupUI 스크립트는 **항상 활성인 오브젝트**(`ResultBanner`)에 부착하고, 토글되는 건 자식 `Group`이어야 한다(코루틴이 비활성 오브젝트에서 안 돌기 때문).

### StreakHUD 필드 연결
| 필드 | 연결 대상 |
|------|-----------|
| `streakText` | `StreakHUD/StreakText` |
| `sessionPnLText` | `StreakHUD/SessionText` |

### NewsTicker 필드 연결
| 필드 | 연결 대상 |
|------|-----------|
| `tickerText` | `NewsTicker/TickerText` |
| `interval` | 6 |
| `fallbackHeadlines` | (코드 기본값 사용, 비워도 됨) |

---

## 7. 코인 카드 프리팹 + CoinTable 에셋

### 7-1. `CoinCard` 프리팹 — [Coin.UI.CoinCardUI]
```
CoinCard (Button + Image=card bg)
├─ Name      (TMP, 영문 코인명)
├─ RiskGauge (Image, Filled-Horizontal)   ← 위험 게이지
├─ Swing     (TMP "변동 10%~24%")
├─ BetRange  (TMP "베팅 ₩.. ~ ₩..")
├─ Flavor    (TMP, textMuted)
└─ LockedOverlay (Image 반투명 + TMP "골드 부족")  ← 기본 비활성
```
| 필드 | 연결 대상 |
|------|-----------|
| `nameText`/`swingText`/`betRangeText`/`flavorText` | 각 TMP |
| `riskGaugeFill` | `RiskGauge` (Image, Type=Filled, Horizontal) |
| `button` | 루트 Button |
| `lockedOverlay` | `LockedOverlay` (GameObject) — 코드가 골드 부족 시 켠다 |

### 7-2. `CoinTable.asset` (선택)
- `Project` 창 우클릭 → Create → **Market/Coin Table**.
- 비워두면 코드 `CoinTableSO.DefaultSlots()` / `DefaultReplacementNames()`가 폴백으로 쓰여 **그대로 동작**.
- 밸런스를 인스펙터에서 조절하려면 슬롯 10종(design.md §7.4)·이름 풀(§7.7)을 채우고 `CoinGameManager.coinTable`에 할당.

---

## 8. 참조 일괄 연결 체크 (빠뜨리기 쉬운 것)

- [ ] `MarketSceneController` 9개 필드 (§2)
- [ ] `CoinModeUI` 4개 / `CoinSelectUI` 3개 / `CoinBettingUI` 18개 (§6)
- [ ] `StockUIController` 탭/컴포넌트 참조 (§4)
- [ ] 각 컨테이너의 `itemPrefab`/`cardPrefab`이 **프리팹 에셋**을 가리키는지(씬 인스턴스 ✗)
- [ ] `CoinResultPopupUI`는 활성 오브젝트에, `Group`만 토글 (§6)
- [ ] ScrollView Content에 Layout Group + Content Size Fitter

---

## 9. 색 · 레이아웃 마감 (인스펙터 직접 지정)

| 토큰 | Hex | 쓰는 곳 |
|------|-----|---------|
| `screenBg` | `#0D1117` | ScreenRoot 배경 |
| `panelBg` | `#151B26` | 패널/카드 배경 |
| `panelBgAlt` | `#11161F` | 호가·뉴스·스테이크 카드 |
| `panelBorder` | `#243043` | 테두리 |
| `rowSelected` | `#1B2A3D` | 선택 행/카드 |
| `accentCyan` | `#38BDF8` | 활성 탭 언더라인·선택 좌측 바 |
| `gold` | `#F5C542` | 골드·활성 토글 |
| `up` | `#3FB950` | 상승·UP·매수 |
| `down` | `#F85149` | 하락·DOWN·매도 |
| `textPrimary` | `#E6EDF3` | 본문 |
| `textMuted` | `#8B98A9` | 보조 라벨 |
| `textDim` | `#5A6678` | 단위·flat |

- 큰 숫자(가격/손익/잔액)는 **픽셀 폰트**, 한글 본문은 산세리프.
- 앵커: TopBar=상단 Stretch, StockModeRoot/CoinModeRoot=중앙 Stretch(TopBar 아래), 좌(목록)·우(상세) 분할은 Horizontal Layout 또는 앵커.

---

## 10. 마감 — Build Settings · 진입 단말기

1. **Build Settings**에 `MarketScene` 추가(Additive 로드 대상).
2. **진입 단말기**(`MarketTerminalInteractable`, 미작성): 정착지 씬에 배치 → 상호작용 시
   `SceneManager.LoadSceneAsync("MarketScene", LoadSceneMode.Additive)`,
   닫기는 `MarketSceneController.Exit()`가 처리(저장 후 Unload). 기존 `BedInteractable` 패턴 참고.
3. **폰트 매핑**: `LanguageManager`의 폰트 설정과 TMP 폰트 에셋 연결(다국어 글리프).

---

## 11. 지역화(Localization) 적용

MarketScene의 고정 라벨은 **`LocalizedTextUI` 컴포넌트**(`Assets/Scripts/UI/Core/LocalizedTextUI.cs`)로 지역화한다.
해당 TMP 오브젝트에 컴포넌트를 붙이고 `Localization Key`에 아래 key를 입력하면, `LanguageManager`가 언어·폰트를
자동으로 맞춰준다(참조 연결 불필요 — 같은 오브젝트의 TMP를 스스로 찾는다). key 정의는 `Assets/StreamingAssets/Data/Stock_Localization.csv`.

> `LocalizedTextUI`는 **TMP가 붙은 그 오브젝트**에 부착한다(버튼이면 버튼의 자식 라벨 TMP에 부착). CSV에 key가 없으면 key 문자열이 그대로 표시되므로 오타 주의.

### 고정 라벨 → key (LocalizedTextUI로 부착)
| 위치 | 라벨 | Localization Key |
|------|------|------------------|
| TopBar `Btn_주식` 라벨 | 주식 | `ui_market_tab_stock` |
| TopBar `Btn_코인` 라벨 | 코인 | `ui_market_tab_coin` |
| TabBar `Btn_시세` 라벨 | 시세 | `ui_stock_tab_quotes` |
| TabBar `Btn_뉴스` 라벨(배지 카운트 제외) | 뉴스 | `ui_stock_tab_news` |
| TabBar `Btn_포트폴리오` 라벨 | 포트폴리오 | `ui_stock_tab_portfolio` |
| TabBar `Sentiment` 제목 라벨 | 시장 심리 | `ui_market_sentiment_label` |
| StockList 헤더 | 전체 종목 | `ui_stock_list_header` |
| CompanyDetail 호가 섹션 제목 | 호가 | `ui_stock_orderbook` |
| CompanyDetail 최신 뉴스 섹션 제목 | 최신 뉴스 | `ui_stock_latest_news` |
| QtyRow 수량 라벨(입력 필드 제외) | 수량 | `ui_stock_qty_label` |
| OrderAmount 금액 라벨(값 TMP 제외) | 주문 금액 | `ui_stock_order_amount_label` |
| Trade `Btn_매수` 라벨 | 매수 | `ui_stock_buy` |
| Trade `Btn_매도` 라벨 | 매도 | `ui_stock_sell` |

### 코드가 자동 지역화하는 동적 텍스트 (LocalizedTextUI 불필요)
이 값들은 컨트롤러가 `StockLoc`로 매 갱신마다 채운다. 라벨에 `LocalizedTextUI`를 붙이면 **안 된다**(코드가 덮어씀).
| 값 | 채우는 코드 | Key |
|----|-------------|-----|
| `7종목` (종목 수) | `StockListUI.countText` ← **이 필드를 인스펙터에서 헤더 옆 TMP에 연결** | `ui_stock_count` |
| `탐욕 0.55` (시장심리 값) | `StockUIController.sentimentText` | `ui_stock_sentiment_fear/greed/neutral` |
| `시총 B등급` | `CompanyDetailUI.marketCapText` | `ui_stock_marketcap` |
| `6` (수량 값, 입력 필드) | `CompanyDetailUI.quantityInput` — 순수 정수 문자열(지역화 없음), 비율 버튼/직접 입력으로 채움 | — |
| `₩276,600` (주문 금액 값) | `CompanyDetailUI.orderAmountText` | `ui_stock_order_amount` |

> `StockListUI`에 `countText` 필드가 추가됨(§4 표 참고). 선택 필드라 비워도 동작하지만, 연결하면 `7종목`이 실제 종목 수로 자동 표시·지역화된다.

---

## 12. CRT 모니터 효과 (ScanlineOverlay)

브라운관(CRT) 느낌을 **은은하게** 입히는 풀스크린 오버레이. 주식·코인 양쪽에 공통 적용된다.
화면 콘텐츠를 grab하지 않고(= Screen Space Overlay 호환) 곱셈 블렌드로 주사선·새도우마스크·비네팅·플리커를
**절차적으로** 그린다. 곱셈이라 밝은 글자/그래프는 거의 그대로 두고 어두운 띠만 살짝 깔린다.

- 셰이더: `Assets/Shaders/CRTOverlay.shader` (`"UI/CRTOverlay"`)
- 컨트롤러: `Assets/Scripts/Market/CRTEffectController.cs` (`Market.CRTEffectController`)

### 셋업 (3단계)
1. `Canvas` 최상단(가장 마지막 형제 = 맨 위에 그려짐)에 빈 **Image** 추가 → 이름 `ScanlineOverlay`.
2. RectTransform 앵커를 **Stretch-Stretch**(전체 채움, Left/Right/Top/Bottom = 0)로. 스프라이트는 비워도 됨(흰색).
3. 그 오브젝트에 **`CRTEffectController`** 컴포넌트를 붙인다. 끝.
   - 머티리얼은 컴포넌트가 **런타임/에디터에서 스스로 생성·연결**한다(별도 `.mat` 에셋 불필요).
   - `Raycast Target`은 컴포넌트가 자동으로 끈다(클릭 차단 방지).

### 인스펙터 값 (기본값 = 은은함)
| 그룹 | 필드 | 기본 | 메모 |
|------|------|------|------|
| 전체 | `enableEffect` | ✓ | 끄면 오버레이 Image 자체 비활성(오버드로 0) |
| 전체 | `intensity` | 0.7 | **전체 강도.** 0=무효과. 더 은은하게 0.4~0.5 |
| 전체 | `tint` | 흰색 | 살짝 초록/호박빛 원하면 여기서(흰색=무영향) |
| 주사선 | `scanPeriod` | 3 | 주사선 간격(디바이스 px). 작을수록 촘촘 |
| 주사선 | `scanlineDarkness` | 0.10 | 골의 어두움 |
| 새도우마스크 | `maskStrength` | 0.05 | RGB 줄무늬 세기. 0이면 끔 |
| 비네팅 | `vignette` | 0.18 | 모니터 가장자리 어둑(둥근 브라운관 느낌) |
| 플리커 | `flicker` | 0.015 | 미세 밝기 떨림. 0이면 끔 |
| 롤 밴드 | `rollSpeed` | 0 | **기본 꺼짐.** 0.02~0.08이면 천천히 흐르는 띠 |

> **너무 거슬리면**: `intensity`를 먼저 낮추고, 그래도 거슬리면 `scanlineDarkness`·`maskStrength`를 줄인다.
> **고해상도에서 주사선이 안 보이면**: `scanPeriod`는 디바이스 px 기준이라 해상도 무관하게 일정하다. 더 굵게 하려면 값을 키운다.

### 연출 훅 (선택)
`CRTEffectController.Pulse(duration, extraFlicker, rollSpeedBurst)` — 짧은 화면 동요(플리커 급증 + 롤 밴드)를 1회 일으킨다.
코인 떡상/떡락, 상장폐지 같은 순간에 `CoinResultPopupUI` 등에서 호출하면 좋다. (호출 안 하면 평소엔 정적인 은은한 효과만)
`SetIntensity(float)`로 진입/이탈 시 페이드 인/아웃, `SetEnabled(bool)`로 on/off도 가능.

---

## 13. UI 효과음 (MarketUISfx) — 셋업 불필요

마켓 씬의 모든 버튼/토글/입력필드에 UI 효과음이 **자동으로** 붙는다.
오디오 에셋 없이 부드러운 사인파 차임("톡/딩" 계열)을 코드로 합성한다.

- 합성·재생: `Assets/Scripts/Market/MarketUISfx.cs` (`Market.MarketUISfx`, static)
- 자동 훅: `Assets/Scripts/Market/MarketButtonSfx.cs` — **MarketSceneController가 Awake에서 자동 부착** (에디터 작업 없음). 씬 루트 전체를 0.5초 주기 재스캔하므로 런타임 생성 행(종목 리스트·코인 카드)도 포섭
- 개별 변경: 특정 버튼의 소리를 바꾸거나 음소거하려면 그 버튼에 `MarketSfxOverride`를 붙이고 kind 선택 (None=무음)

### 기본 매핑
| UI | 소리 | 느낌 |
|----|------|------|
| 일반 버튼 | Click | 살짝 내려가는 "톡" |
| 종목 행·코인 카드 | Select | 부드러운 상승 "동↗" |
| 탭·모드 토글 | Tab | "동-딩" 2음 |
| 입력필드 클릭/타이핑 | Select / Type | 작은 키 톡 |
| 매수/매도 성공·실패 | Confirm / Deny | 5도 상승 차임 / 하강 2음 |
| 코인 적중·떡상 / 폭락·상폐 | Win / Crash | 상승 아르페지오 / 하강 스윕 (CoinSfx 폴백) |

같은 프레임에 소리가 겹치면 우선순위 높은 쪽만 재생된다(매수 버튼 = Click+Confirm → Confirm만 들림).

### 실제 음원으로 교체 (사운드 디자이너)
SoundDataSO에 `market_ui_click`/`market_ui_tab`/`market_ui_select`/`market_ui_confirm`/`market_ui_deny`/`market_ui_type`/`market_ui_win`/`market_ui_crash` 이름으로 클립을 등록하면 합성음 대신 그 클립이 재생된다(코드 수정 불필요). 코인 연출음은 기존 `coin_*` 이름 그대로(`CoinSfx` 참고).

---

## 부록 — 컴포넌트 ↔ 부착 위치 빠른 색인

| 컴포넌트 | 부착 GameObject | 신규/기존 |
|----------|-----------------|-----------|
| `MarketSceneController` | Managers | 신규 |
| `CoinGameManager` | Managers | 신규 |
| `StockUIController` | StockModeRoot | 기존 |
| `StockListUI` | StockModeRoot/StocksTab/StockList | 기존 |
| `CompanyDetailUI` | …/CompanyDetail | 기존 |
| `MiniChartUI` | …/CompanyDetail/MiniChart | 기존 |
| `NewsTabUI` | StockModeRoot/NewsTab | 기존 |
| `PortfolioUI` | StockModeRoot/PortfolioTab | 기존 |
| `StockListItemUI`/`NewsItemUI`/`PortfolioItemUI` | 각 행 프리팹 | 기존(리테마) |
| `CoinModeUI` | CoinModeRoot | 신규 |
| `CoinSelectUI` | CoinModeRoot/CoinSelectView | 신규 |
| `CoinCardUI` | CoinCard 프리팹 | 신규 |
| `CoinBettingUI` | CoinModeRoot/BettingView | 신규 |
| `CoinChartUI` | …/BettingView/Chart | 신규 |
| `CoinResultPopupUI` | …/BettingView/ResultBanner | 신규 |
| `StreakHUD` | …/BettingView/StreakHUD | 신규 |
| `NewsTicker` | …/BettingView/NewsTicker | 신규 |
| `CRTEffectController` | Canvas/ScanlineOverlay | 신규 (§12) |

---

## 검색창 배치 (사람이 수행)

`StockListUI`는 연결된 `TMP_InputField`에 `onValueChanged` 리스너만 붙인다. 모양·위치는 전부 씬 마음대로.

1. **Hierarchy**: `StocksTab > StockList` 아래(= `SortButton`·`FilterButton`과 같은 부모)에서
   우클릭 → `UI > Input Field - TextMeshPro` 생성. 이름은 `SearchField`.
2. **배치**: 앵커를 `FilterButton`과 같게(좌상단 `Min/Max = (0, 1)`) 두고 그 오른쪽에 놓는다.
   참고 좌표 — SortButton `x≈1089 (w 202)`, FilterButton `x≈1281 (w 138)`, 높이 124. 그 오른쪽은 비어 있다.
3. **연결**: `StockList` 오브젝트의 `StockListUI` 인스펙터에서 `Search Input` ← 방금 만든 `SearchField`.
   (`Auto Create Search Field`는 기본이 꺼짐 — 켜면 연결이 비었을 때 코드가 임시 검색창을 하나 만든다)

**색·폰트는 씬에서 맞출 필요가 없다.** `StockListUI.ApplySearchTheme()`이 열 때마다 입힌다:

| 대상 | 값 |
|---|---|
| 배경(`targetGraphic` 또는 루트 Image) | `MarketTheme.PanelBgAlt` #11161F |
| 입력 글자 | `TextPrimary` #E6EDF3 |
| 플레이스홀더 | `TextDim` #5A6678 + `ui_stock_search_placeholder` 문구 |
| 커서·선택 영역 | `AccentCyan` #38BDF8 (선택은 알파 0.35) |
| 폰트 | `LanguageManager.GetCurrentFont()` |
| 입력 규칙 | `Single Line`, `Character Limit = 24` |

씬에서 정하는 건 **위치·크기·계층**뿐이다. 문구를 씬에 하드코딩하면 코드가 덮으니 비워 둬도 된다.

검색은 **표시 이름(현재 언어) 또는 종목 id의 부분일치**로 동작하며, 공백은 무시한다.
태그·보유중 필터와 AND로 함께 걸린다. 입력 중에는 `StockListUI`의 W/S 슬롯 이동이 자동으로 막힌다.
