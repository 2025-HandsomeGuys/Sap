# 플레이어 텔레메트리 설계

작성일: 2026-07-25

얼리액세스/데모 빌드에서 플레이어 행동 데이터를 수집해 밸런스·UX 문제를 관측으로 찾아내는 시스템.

---

## 1. 목적과 배경

플레이테스트가 충분히 진행되지 않은 상태로 발매하므로, **추측 대신 관측으로** 고칠 점을 찾는다.

가장 먼저 답해야 할 질문:

| 질문 | 답을 주는 데이터 |
|---|---|
| 정말 5시간짜리 분량인가? | `region_first_enter`의 playtimeTotal 분포 |
| 난이도가 학습 가능한가? | 일차별 긴급탈출률 곡선 |
| 골드 커브가 빡빡한가 널널한가? | `day_settled` 골드 중앙값 + `upgrade_blocked` 부족 금액 |
| 코인 EV가 설계대로인가? | `coin_bet` 실측 손익 집계 |
| 어디서 게임을 끄는가? | 마지막 `heartbeat` 위치 |

### 배포 컨텍스트 (결정됨)

- **얼리액세스 (유료, 스팀 공개 배포)** 가 본 빌드. 4지역, 추정 5시간 분량.
- **데모**는 EA 빌드에서 파생 — 5일차 컷 + 던전/유물/가마솥 잠금.
  데모는 EA 상점 페이지로 위시리스트를 유입시키는 역할.
- 두 빌드는 `buildType` 필드(`ea` / `demo`)로만 구분하고, **이벤트 스키마는 완전히 동일**하다.

### 데모 파생 규칙 (참고)

- 5일차 정산(`DaySummaryUI`) 직후 데모 종료 카드 → 위시리스트 유도
- 열림: 지하 탐험 전체 루프, 상점·창고·업그레이드 Tier 1, 퀘스트 메인 초반, 마켓(주식·코인)
- 잠금: 던전, 유물, 도깨비 가마솥 — 아이콘은 노출하되 "정식 버전" 표시(그 자체가 훅)

---

## 2. 수집 방식

**이벤트 로그 + 일일 스냅샷** 방식.

### 단계 구분 (중요)

| 단계 | 범위 | 상태 |
|---|---|---|
| **Phase 1** | 이벤트 정의 + 수집 + **로컬 JSONL 파일 기록** | **이번에 구현** |
| **Phase 2** | 원격 배치 업로드, 재시도, 프라이버시 UI, 백엔드 | 나중 |

Phase 1만으로도 **자체 플레이테스트·내부 QA에서 즉시 쓸 수 있다.**
빌드를 돌려보고 `persistentDataPath`의 JSONL을 직접 열어 분석하면 된다.

Phase 2는 파일을 읽어 전송하는 **소비자 하나를 추가하는 일**이며,
게임플레이 호출부(`Telemetry.Log`)는 단 한 줄도 바뀌지 않는다.
이 경계를 지키는 것이 Phase 1 설계의 핵심 제약이다.

- **이벤트 로그**: 정의된 이벤트를 시간순으로 기록 → 퍼널·이탈 지점·사망 원인 재구성 가능
- **스냅샷**: 수면 시점마다 플레이어 상태 전체를 1건 기록 → 밸런스 커브를 가장 싸게 확보

검토 후 배제한 대안:

- **스냅샷 단독**: 구현은 거의 공짜지만 "어쩌다 그렇게 됐는지"를 못 본다.
  3일차 골드 200이 긴급탈출 5회 때문인지 코인 올인 때문인지 구분 불가 → 고칠 점을 못 찾는다.
- **풀 세션 리플레이** (초당 위치·스태미나 샘플링): 사망 히트맵까지 가능하나 데이터량·구현비 과다.
  YAGNI. "특정 지역에서만 유독 죽는다"가 관측되면 그때 해당 지역만 켠다.

---

## 3. 이벤트 스키마

### 3.0 공통 페이로드

모든 이벤트에 자동 부착한다. 각 이벤트가 "누가·언제·어디서"를 스스로 갖게 되어,
나중에 어떤 각도로 잘라 봐도 재구성된다.

| 필드 | 설명 |
|---|---|
| `anon_id` | 최초 실행 시 생성해 저장하는 익명 GUID |
| `session_id` | 세션마다 새로 생성 |
| `run_id` | 회차(플레이스루) 식별자. 뉴게임마다 발급되어 세이브에 영속 |
| `is_fixture` | QA 픽스처·디버그 명령이 손댄 회차인지 (분석 시 제외) |
| `seq` | 세션 내 이벤트 순번 (순서 복원·유실 감지용) |
| `build_type` | `ea` / `demo` |
| `version` | 빌드 버전 |
| `playtime` | 누적 플레이 시간(초) |
| `day` | 현재 일차 |
| `region` | 현재 지역 — `TileDataManager.GetTileTypeAtDepth(y)`가 반환하는 `TileType` 이름 |
| `depth` | 현재 깊이 |

**원칙: 답할 질문이 없는 이벤트는 넣지 않는다.**

### 3.1 지역 진행 — "정말 5시간짜리인가, 어느 지역이 늘어지는가"

| 이벤트 | 고유 필드 | 삽입 위치 |
|---|---|---|
| `region_first_enter` | 지역 id | 지역 판정 지점 (`SettlementManager` 계열) |
| `depth_milestone` | 100m 단위 최초 도달 깊이 | `InfinityMapManager` 또는 깊이 추적부 |
| `dive_start` | 오전/오후, 시작 스태미나, 장비 구성 | `BedInteractable` / 하강 진입부 |
| `dive_end` | 결과(귀환/긴급탈출), 최대 깊이, 소요 시간, 획득 광물(구성), 스태미나 잔량·비율, 무게 비율, 파기 픽셀, 층별 체류 시간 | 귀환 처리부 |

핵심 지표는 `region_first_enter`의 **playtimeTotal 분포**.
4지역 도달 중앙값이 12시간이면 EA 분량 주장이 바뀌고, 2시간이면 지역 3~4가 얇다는 뜻이다.

채굴처럼 고빈도 행위는 개별 이벤트로 찍지 않고 `dive_end`에 집계값으로 넣는다.

### 3.2 스태미나·실패 — "난이도가 불공정한가, 학습 가능한가"

| 이벤트 | 고유 필드 | 삽입 위치 |
|---|---|---|
| `stamina_depleted` | 소지 광물 무게, 귀환 거리 | `StaminaManager` |
| `emergency_escape` | 잃은 광물 가치, 유지분 | `EmergencyEscapeReport` (데이터가 이미 모여 있음) |
| `encumbered_enter` | 초과 무게, 지속 시간 | `EncumbranceController` |

핵심 지표는 **일차별 긴급탈출률 곡선**. 우하향해야 정상(학습됨).
5일차에도 평평하면 플레이어가 스태미나 예산을 못 읽고 있다는 뜻이며,
이는 밸런스가 아니라 **UI 문제** — 남은 스태미나로 귀환 가능한지 보여주는 표시의 부재 신호.

### 3.3 경제 — "골드가 막히는가, 넘치는가"

| 이벤트 | 고유 필드 | 삽입 위치 |
|---|---|---|
| `day_settled` **(스냅샷)** | 시작/종료 골드, 카테고리별 증감, 창고 재고, 채광 레벨, 해금 노드 수, 주식 평가액·원가, 당일 최대 깊이 | `DayEarningsLedger` 소비 시점 |
| `shop_transaction` | 매수/매도, 아이템, 수량, 금액 | `ShopManager` |
| `upgrade_purchased` | 노드 id, tier, 비용 | `UpgradeOverlayUI` |
| `upgrade_blocked` | 노드 id, **부족 금액** | `UpgradeOverlayUI` 구매 실패 분기 |

`upgrade_blocked`가 핵심이다. 플레이어가 **사고 싶어했지만 못 산 것**이 곧 욕구이고,
부족 금액 분포가 골드 커브의 빡빡함을 정량화한다.
`day_settled` 스냅샷 하나로 "N일차 골드 중앙값" 커브가 통째로 나온다.

### 3.4 마켓 — "설계한 EV대로 실제로 굴러가는가"

| 이벤트 | 고유 필드 | 삽입 위치 |
|---|---|---|
| `coin_bet` | 코인 id, 스테이크, 배율, 손익, 청산 여부, 연승 수 | `CoinGameManager.TryBet` |
| `coin_day_summary` | 당일 총 손익, 판수, 출금 한도 도달 여부 | `CoinGameManager` |
| `stock_trade` | 종목, 매수/매도, 금액 | Stock 체결부 |
| `market_visit` | 방문 횟수, 체류 시간 | `MarketSceneController` |

코인 EV는 설계상 항상 마이너스여야 한다(`market-scene/design.md` §7.3).
`coin_bet` 집계가 **실측 EV와 설계값의 일치를 검증하는 유일한 수단**이다.
`market_visit`이 바닥이면 "공들인 기능을 아무도 안 쓴다"도 하나의 결론이다.

### 3.5 이탈·튜토리얼 — "어디서 껐는가"

| 이벤트 | 고유 필드 | 삽입 위치 |
|---|---|---|
| `session_start` | OS, 해상도, 언어, 하드웨어 사양 | 부트스트랩 |
| `session_end` | 세션 길이, **마지막 화면**(지하/정착지/마켓/메뉴) | 부트스트랩 |
| `heartbeat` | 없음 (공통 필드만, 60초 주기) | 텔레메트리 코어 |
| `guide_shown` / `guide_skipped` | 가이드 id, 체류 시간 | `GuideManager` |

`heartbeat`는 필수다. 알트+F4로 끄면 `session_end`가 기록되지 않는데,
그러면 **가장 크게 실망해서 나간 플레이어의 데이터가 통째로 사라진다** — 정확히 제일 알아야 할 표본이다.
마지막 하트비트의 위치가 그 답을 준다.

### 3.6 안정성

`Application.logMessageReceived`를 훅해 예외·스택트레이스를 `error` 이벤트로 전송.

공개 배포에서는 밸런스보다 이쪽이 먼저 터진다.
지형 시스템이 Burst/NativeArray를 쓰므로 특정 하드웨어에서만 발생하는 크래시가 있을 수 있고,
재현 정보 없이는 손도 못 댄다.

---

## 4. 구현 구조

```
Assets/Scripts/_Core/Telemetry/
├── Telemetry.cs          — 정적 진입점. Telemetry.Log("dive_end", payload)
├── TelemetryEvents.cs    — 이벤트 이름 상수 모음
├── TelemetryPayload.cs   — 이벤트별 필드를 JSON 조각으로 누적하는 빌더
├── TelemetrySession.cs   — anonId·sessionId·seq·playtime 등 공통 필드 소유, JSONL 한 줄 조립
├── TelemetryBuffer.cs    — 순수 C#. 메모리 버퍼 + 플러시 판단
├── TelemetryFileSink.cs  — 순수 C#(경로만 주입). JSONL append, 세션별 파일·용량 상한
└── TelemetryRunner.cs    — MonoBehaviour. 주기 플러시, heartbeat, 예외 캡처, 종료 훅
```

`DayEarningsLedger.Report()`와 동형 패턴이라 호출부 습관이 그대로 이어진다.
게임플레이 코드는 `Telemetry.Log()` 한 줄만 알면 되고, 버퍼·직렬화·파일 기록은 전부 뒤에서 이뤄진다.

### 컴포넌트 경계

| 컴포넌트 | 역할 | 의존 | 테스트 |
|---|---|---|---|
| `Telemetry` | 정적 진입점, 활성화 게이트 | `TelemetryBuffer`, `TelemetrySession`, `TelemetryFileSink` | — |
| `TelemetryEvents` | 이벤트 이름 상수 | 없음 | — |
| `TelemetryPayload` | 필드 → JSON 조각, 이스케이프 | 없음 (순수 C#) | EditMode |
| `TelemetrySession` | 공통 필드 소유·주입, anonId 영속화, 라인 조립 | `TelemetryPayload` | EditMode |
| `TelemetryBuffer` | 버퍼링, JSON 직렬화, 플러시 시점 판단 | 없음 (순수 C#) | EditMode |
| `TelemetryFileSink` | JSONL append, 세션별 파일, 용량 상한 정리 | 없음 (경로 주입) | EditMode |
| `TelemetryRunner` | 60초 플러시, `heartbeat`, `OnApplicationQuit` | 위 전부 | — |

Phase 2에서 추가될 `TelemetryUploader`는 `TelemetryFileSink`가 남긴 **파일을 읽는 별도 소비자**다.
따라서 위 구조는 Phase 2에서 수정되지 않는다.

순수 C# 컴포넌트 4개(`TelemetryPayload` / `TelemetrySession` / `TelemetryBuffer` / `TelemetryFileSink`)는
EditMode 테스트가 가능하다.
(테스트 실행은 사람이 직접 수행 — `CLAUDE.md` 규칙)

### 성능

지형 파이프라인이 프레임 예산에 민감하므로:

- **파일 I/O는 플러시 시점에만** 발생한다. `Log()`는 메모리 버퍼에 문자열 조각을 넣을 뿐이다.
- 고빈도 행위(채굴 등)는 개별 이벤트가 아니라 집계값으로 기록한다.
- 이벤트 빈도가 낮으므로(분당 수 건) 값 직렬화는 호출 시점에 수행한다.
  `Dictionary<string, object>`로 미루면 박싱과 할당이 오히려 커진다.

---

## 5. 저장 (Phase 1)

1. 이벤트 → 메모리 버퍼
2. 60초마다 / 씬 전환 / `OnApplicationQuit` 시 → JSONL 파일에 append
3. 파일은 **세션별로 분리**한다:
   `persistentDataPath/telemetry/<yyyyMMdd-HHmmss>_<sessionId8>.jsonl`
4. 총 용량이 **50MB를 넘으면 오래된 세션 파일부터 삭제**

JSONL append 방식이라 크래시로 프로세스가 죽어도 직전까지가 디스크에 남는다.
크래시 직전 데이터야말로 가장 중요하다.

세션별 파일 분리가 중요한 이유는 두 가지다.
하나는 분석할 때 세션 경계가 파일 경계와 일치해 다루기 쉽다는 것.
다른 하나는 **Phase 2에서 "아직 전송하지 않은 파일 목록"이 그대로 업로드 큐가 된다**는 것이다.
단일 `pending.jsonl`이었다면 전송 성공분을 파일 중간에서 잘라내야 해서 훨씬 성가시다.

### 에디터 지원

- 에디터에서는 이벤트를 콘솔에도 출력하는 토글(기본 꺼짐)
- 메뉴 아이템으로 텔레메트리 폴더 열기 — 플레이테스트 직후 바로 확인

### 백엔드: Supabase (Phase 2)

- 무료 티어로 충분. 5시간 세션 ≈ 200KB, 1000명 ≈ 200MB
- Postgres이므로 **퍼널 쿼리를 SQL로 직접 작성**할 수 있다
- anon key에 insert-only RLS를 걸어 읽기를 차단

검토 후 배제:

- **Unity Analytics**: 무료지만 커스텀 이벤트 스키마가 경직되고, 대시보드에서 원하는 각도로 자르기 어렵다
- **Google Sheets 웹훅**: 시작은 쉬우나 수만 행에서 무너진다

`upgrade_blocked` 부족 금액 분포처럼 자유로운 탐색이 필요하므로 SQL이 필수다.

### 스키마 (Phase 2)

단일 `events` 테이블 + JSONB 페이로드.
**Phase 1의 JSONL 한 줄이 이 테이블의 한 행과 1:1로 대응**하도록 키 이름을 맞춰 둔다.
그러면 나중에 로컬에 쌓인 로그도 그대로 밀어넣을 수 있다.

```sql
create table events (
  id          bigserial primary key,
  received_at timestamptz default now(),
  anon_id     uuid        not null,
  session_id  uuid        not null,
  run_id      text,
  is_fixture  boolean     default false,
  seq         int         not null,
  event       text        not null,
  build_type  text        not null,
  version     text        not null,
  playtime    int,
  day         int,
  region      text,
  depth       real,
  payload     jsonb
);

create index on events (event, build_type);
create index on events (anon_id, session_id, seq);
```

이벤트별 컬럼을 나누지 않고 JSONB로 두면, 새 이벤트를 추가할 때 마이그레이션이 필요 없다.
분석 축이 되는 공통 필드만 컬럼으로 승격했다.

---

## 6. 프라이버시 (Phase 2)

**Phase 1은 데이터가 플레이어 PC 밖으로 나가지 않으므로 고지·동의 의무가 발생하지 않는다.**
따라서 고지 팝업과 옵트아웃 UI는 Phase 1 범위에서 제외한다.

다만 `Telemetry`에 **활성화 게이트는 Phase 1부터 넣어 둔다.**
나중에 옵트아웃 토글이 붙을 자리를 미리 만들어 두는 것이며, 게이트 자체는 한 줄짜리 조건문이다.

아래는 Phase 2에서 전송을 켜는 시점의 필수 요건이다.

- **수집**: 익명 GUID, 게임플레이 수치, 하드웨어 사양
- **미수집**: 계정 정보, IP, 파일 경로, 그 외 개인 식별 정보 일체
- 최초 실행 시 1회 고지 팝업
- `SettingsOverlayUI`에 옵트아웃 토글 추가.
  이 파일의 **pending 커밋 패턴**(`_p*` 대기값 → '적용' 버튼에서만 매니저에 반영)을 따를 것
- 옵트아웃 시 큐를 비활성화하고 잔여 `pending.jsonl`도 삭제
- 스팀 상점 페이지에 개인정보처리방침 링크 (EU 이용자 대응)

옵트아웃은 반드시 넣는다. 없으면 리뷰에서 "몰래 데이터 수집"으로 문제가 되고,
그 평판 손해가 데이터의 가치보다 크다.

---

## 7. 분석 — 미리 준비할 질문

Phase 2에서 SQL로 작성할 쿼리 목록이다.
Phase 1 단계에서도 **같은 질문을 로컬 JSONL에 스크립트로 던져 볼 수 있다** — 자체 플레이테스트 분석이 그것이다.

1. **지역 도달 시간 분포** — `region_first_enter`의 playtime 사분위수 (지역별)
2. **일차별 긴급탈출률** — `emergency_escape` 건수 / `dive_end` 건수 (day별)
3. **골드 커브** — `day_settled`의 종료 골드 중앙값 (day별)
4. **업그레이드 구매 순서** — `upgrade_purchased`의 노드별 최초 구매 시점 중앙값
5. **좌절 지점** — `upgrade_blocked` 노드별 발생 횟수 + 부족 금액 중앙값
6. **이탈 지점** — 세션별 마지막 이벤트의 화면·day 분포
7. **코인 실측 EV** — `coin_bet` 손익 합계 / 스테이크 합계 (코인 티어별)
8. **예외 빈발 순위** — `error` 스택트레이스별 발생 횟수·영향 플레이어 수

---

## 8. 구현 순서

### Phase 1 — 이번 범위

1. **코어** — `TelemetryEvent` / `TelemetrySession` / `TelemetryBuffer` / `TelemetryFileSink`
2. **진입점·러너** — `Telemetry` 정적 API, `TelemetryRunner` 부트스트랩 부착
3. **세션 축** (`session_start` / `session_end` / `heartbeat`) — 파이프라인 검증용 최소 세트
4. **에디터 지원** — 콘솔 출력 토글, 폴더 열기 메뉴
5. **안정성 축** (`error`) — 가장 먼저 효용이 나온다
6. **루프 축** (`dive_start` / `dive_end` / `region_first_enter` / `depth_milestone`)
7. **실패 축** (`stamina_depleted` / `emergency_escape` / `encumbered_enter`)
8. **경제 축** (`day_settled` / `shop_transaction` / `upgrade_purchased` / `upgrade_blocked`)
9. **마켓 축 · 가이드 축**

3~4번까지 끝나면 실제로 파일이 쌓이는지 눈으로 확인할 수 있다.
이벤트를 전부 붙인 뒤에 파이프라인이 안 돈다는 걸 발견하는 상황을 피한다.

각 축은 서로 독립적이므로 5~9번은 순서를 바꿔도 되고, 일부만 먼저 붙여도 된다.

### Phase 2 — 나중

10. Supabase 테이블 + insert-only RLS
11. `TelemetryUploader` — 미전송 파일 스캔, 배치 전송, 재시도 백오프
12. 프라이버시 UI (고지 팝업 + 옵트아웃 토글)
13. 분석 쿼리 작성

---

## 9. 미결정 사항

- **7층→4층 리팩터링과의 정합성** — `LayerType`은 현재 7개 값이지만 발매는 4지역 기준이다.
  리팩터링이 반영되면 enum 값이 바뀌어 이전 데이터와 지역 비교가 불가능해진다.
  `version` 필드로 구간을 나눠 보면 되지만, **리팩터링을 텔레메트리 도입보다 먼저 끝내는 편이 낫다.**
- **데모 5일차 컷의 구현 지점** — 별도 빌드 심볼로 처리할지, 런타임 플래그로 처리할지
- **Supabase 프로젝트 생성 및 키 관리** — anon key를 빌드에 포함하되 insert-only 권한으로 제한
