# 밸런스판 CSV 설계 — 세션 메트릭 로거

작성일: 2026-08-10

밸런싱을 "감"이 아니라 곡선으로 하기 위해, 플레이 데이터를 `days.csv` / `dives.csv` 두 표로 굽는다.

상위 설계: [`design.md`](design.md) (텔레메트리 전체) · Phase 1 구현 계획: [`phase1-plan.md`](phase1-plan.md)

---

## 1. 출발점 — 새 시스템을 만들지 않는다

이 프로젝트에는 이미 텔레메트리 Phase 1이 구현되어 있다.
`Telemetry.Log(name, payload)` 진입점 + 세션별 JSONL 파일(`persistentDataPath/telemetry/`),
60초 플러시, 50MB 용량 상한, 계측 훅 19곳.

따라서 "세션 메트릭 로거를 만든다"는 **수집기를 새로 만드는 일이 아니라
이미 흐르고 있는 JSONL을 밸런스판 모양으로 굽는 일**이다.

수집 파이프라인(`Telemetry` / `TelemetryBuffer` / `TelemetryFileSink` / `TelemetryRunner`)은
이 작업에서 **한 줄도 수정하지 않는다.** 이 경계를 지키는 것이 본 설계의 제약이다.

### 요청 KPI 대조

| KPI | 현재 상태 |
|---|---|
| 하루 시작/종료 골드, 카테고리별 증감 | ✅ `day_settled` |
| 잠수 소요시간, 최대 깊이, 광물 개수 | ✅ `dive_end` |
| 스태미나 소진 횟수 | ✅ `stamina_depleted` 카운트 |
| 코인 순손익 | ✅ `coin_bet` + `day_settled.coin` |
| 층별 체류 시간 | ❌ region 컨텍스트만 있고 누적 안 함 |
| 픽셀 파기량 | ❌ 카운터 없음 |
| 채광 레벨 · 해금 노드 수 | ❌ 설계엔 있으나 구현에서 누락 |
| 주식 비중 | ❌ |
| 귀환 사유 | ⚠ `return`/`escape` 2종뿐 |

실제 작업은 셋이다.

1. **회차 식별자(`run_id`) 도입** — 밸런스판의 기본키
2. **`dive_end` / `day_settled` 필드 확충** — 기존 호출부에 `.Add()` 추가
3. **파이썬 변환기** — JSONL → `days.csv` + `dives.csv`

---

## 2. 왜 CSV를 게임이 직접 쓰지 않는가

검토한 세 가지:

- **(A) 런타임 CSV append** — `day_settled` 시점에 게임이 직접 CSV 한 줄을 쓴다.
  빌드 돌리고 파일 열면 끝이라 가장 직관적이다.
  그러나 **컬럼 정의를 바꿀 때마다 코드 수정 + 재빌드**이고, 과거 세션은 새 컬럼이 영구히 빈다.
- **(B) JSONL → CSV 변환기** ← **채택**
  수집은 JSONL 그대로 두고, 밖에서 굽는다. 컬럼 정의가 바뀌면 **과거 로그까지 소급해 다시 굽는다.**
- **(C) 에디터 툴로 굽기** — (B)와 같지만 변환기가 Unity 안에 있다.
  에디터를 띄워야 하고, 어차피 분석은 엑셀/파이썬에서 하므로 왕복이 는다.

밸런싱은 **컬럼 정의가 계속 바뀌는 작업**이다. "다시 구울 수 있다"가 결정적이라 (B)를 택했고,
변환기는 파이썬으로 게임 밖에 둔다.

---

## 3. 표 두 개 — `days.csv` / `dives.csv`

요청 KPI가 두 단위로 갈린다.

- **하루 단위** — 골드 커브, 도달 층, 채광 레벨, 노드 수, 코인 손익
- **잠수 단위** — 층별 체류 시간, 시간당 광물, 픽셀 파기량, 귀환 시 상태

하루에 잠수가 여러 번 일어나므로 한 표에 합치면 잠수 컬럼이 평균으로 뭉개지거나
행 절반이 빈 표가 된다. 파일 두 개를 만드는 비용은 파이썬 쪽에서 사실상 0이고,
`dives.csv`를 `run_id + day`로 group-by하면 하루 요약이 그대로 나오므로 잃는 것이 없다.

`sessions.csv`(게임 실행 1회 = 1행, 이탈 지점 분석)는 **이번 범위에서 제외한다.**
이탈 분석은 밸런싱과 다른 질문이고, 필요해지면 `session_start`/`session_end`/`heartbeat`가
이미 쌓이고 있으므로 변환기에 함수 하나를 더하는 일이 된다.

---

## 4. 회차 식별 (`run_id`) — 표의 기본키

### 문제

현재 텔레메트리 공통 필드는 `anon_id`(PC당 고정) / `session_id`(게임 실행당) / `day`뿐이다.
**회차(playthrough)를 식별할 수단이 없다.** 밸런싱 중 다음이 계속 벌어진다.

- 3일차까지 하다 껐다 켬 → `session_id`가 달라진다. 세션으로 자르면 곡선이 끊긴다
- 세이브 슬롯 2개를 번갈아 플레이 → `anon_id`가 같다. **3일차가 두 줄** 나와 골드 중앙값이 섞인다
- QA 픽스처(`fx`)로 10일차 점프 → 자연 플레이 곡선에 조작 데이터가 섞인다

`slot_index`만 싣는 안도 검토했으나, 슬롯을 지우고 새 게임을 하면 이전 회차와 키가 같아진다.
파이썬에서 "day가 되돌아가면 새 회차"로 추론하는 안은 슬롯 번갈아 플레이에서 완전히 틀린다.

**회차 식별이 밸런스판의 기본키다. 여기가 틀리면 위에 쌓는 모든 곡선이 조용히 오염된다.**

### 해결

`PlayerData`에 필드 2개를 추가한다. `[Serializable]` JSON 세이브라 구버전 세이브는
빈 값으로 로드되고, 그때 발급하면 흡수된다.

```csharp
[Header("Telemetry")]
public string runId;      // 뉴게임 시 GUID 발급. 회차의 정체성
public bool isFixture;    // QA 픽스처가 한 번이라도 손댄 회차 → 마킹 시점 이후 이벤트만 분석에서 제외
```

`TelemetrySession`에 `RunId` / `IsFixture` 프로퍼티를 추가하고 `BuildLine`의 공통 필드로 싣는다.
`day`·`region`과 같은 자리다 — **모든 이벤트가 자기 회차를 스스로 갖게 된다.**

발급 지점:

| 시점 | 처리 |
|---|---|
| 뉴게임 (`GameManager`, 빈 슬롯 배정 직후) | GUID 발급 |
| 세이브 로드 | `runId`가 비어 있으면 그 자리에서 발급 후 저장 (구버전 세이브 흡수) |
| 세이브 적용 후 | `Telemetry.Context.RunId` / `.IsFixture`에 반영 |

`isFixture`는 **디버그 콘솔이 게임 상태를 조작하면** `true`로 세우고 **되돌리지 않는다.**
한 번 조작된 회차는 이후 일차의 곡선도 신뢰할 수 없기 때문이다.

단, 익스포터가 실제로 버리는 단위는 회차 전체가 아니라 **이벤트**다(§8).
마킹 이전에 이미 기록된 이벤트는 `is_fixture=false`인 채로 남아 표에 살아남는다 —
조작이 있기 전의 데이터는 여전히 진짜 플레이 기록이라 버릴 이유가 없다.

마킹 대상 명령: `fx load`(픽스처 복원) · `gold`(골드 증감) · `day`(날짜 조작) · `stamina`(스태미나 회복).
`time`(배속) · `pos`(좌표 출력) · `help` / `clear`는 **마킹하지 않는다** — 밸런스 수치를 바꾸지 않는다.
배속까지 마킹하면 QA 플레이 대부분이 걸러져 플래그가 무의미해진다.

`fx load`는 디스크의 세이브 파일을 갈아끼운 뒤 씬을 재시작하므로 메모리에 세울 수 없다.
`SaveManager`에 슬롯 파일을 열어 `isFixture`만 세우고 다시 쓰는 헬퍼를 두고 그것을 호출한다
(파일 경로 지식을 디버그 콘솔이 아니라 `SaveManager`가 갖게 한다).

Phase 2 Supabase 스키마에는 `run_id text` / `is_fixture bool` 컬럼이 대응된다
(`design.md` §5의 컬럼 1:1 대응 규칙).

---

## 5. 계측 보강

### 5.1 `dive_end` 추가 필드 — 잠수 1회 = `dives.csv` 한 줄

| 필드 | 출처 |
|---|---|
| `region_seconds` | 층별 체류 초 (JSON object, §6) |
| `pixels_dug` | 이번 잠수 누적 파기 픽셀 (§7) |
| `max_stamina_start` | 잠수 시작 시 `PlayerStat.MaxStamina` |
| `max_stamina_end` | 잠수 종료 시 `PlayerStat.MaxStamina` |
| `max_stamina_pct` | `max_stamina_end / max_stamina_start` — **잠수 소모의 주 지표** |
| `stamina_loss` | 최대치를 깎은 원인별 감소량 (JSON object, §5.1.1) |
| `weight_ratio` | `EncumbranceController.TotalWeight / EncumbranceThreshold` |
| `minerals` | 캔 광물 구성 (JSON object, 예: `{"Iron":12,"Gold":3}`) |

기존 필드(`result` / `max_depth` / `seconds` / `mineral_kinds` / `mineral_count` / `stamina_left`)는 유지한다.

**판매가 합(`mineral_value`)은 넣지 않는다.** 가격은 `MineralPriceDatabase`(ScriptableObject)에 있고
그 참조를 가진 `ShopManager`는 싱글톤이 아니라 **지상 씬에만 존재한다** —
`dive_end`는 지하에서 발화하므로 안정적으로 조회할 수 없다. 없을 때 0을 쓰면 그건 거짓말이다.

대신 **구성(`minerals`)을 그대로 남긴다.** 밸런싱 중에는 가격 자체가 바뀌므로
로그 시점에 굳힌 금액보다 구성이 더 오래 쓸모 있고, 금액이 필요하면
그 시점의 가격표를 곱하면 된다.

`stamina_left`(절대값)와 `max_stamina_pct`(비율)를 둘 다 두는 것은 중복이 아니다.
최대 스태미나가 업그레이드로 오르므로 **"50 남았다"와 "10% 남았다"는 다른 사실**이고,
비율만으로는 절대값을 복원할 수 없다.

#### 5.1.1 왜 `current / max`가 아니라 `max` 자체를 재는가

**이 게임에서 한 잠수의 소모를 나타내는 값은 `MaxStamina`다.**
부상·화상·동상·방사능·파기 소모가 전부 `StaminaManager.GetModifiers()`에서
`MaxStamina`에 붙는 **Flat 음수 Modifier**로 들어가고, `currentStamina`는
`PlayerStat.ClampCurrentStamina()`로 새 상한에 끌려 내려온다.

그래서 종료 시점에는 `current == max`가 되어 `current / max`가 **항상 1**이다.
초기 계측에서 이 값이 16회 잠수 중 13회 정확히 `1`, 나머지 3회(완전 소진) `0`으로만
찍혀 지표로서 아무 정보가 없었다. 실제로 무너지고 있던 건 최대치 쪽으로,
같은 구간에서 `44.1 → 6.5`까지 깎이고 있었다.

`stamina_loss`는 그 감소를 원인별로 가른다(`injury` / `burn` / `frostbite` /
`radiation` / `digging`). 값은 **기준값 스케일**이라 곱연산 배율이 빠져 있으므로,
합이 `max_stamina_start - max_stamina_end`와 정확히 일치하지는 않는다 —
비율 배분을 보는 용도다.

### 5.2 `day_settled` 추가 필드 — 하루 1회 = `days.csv` 한 줄

| 필드 | 출처 |
|---|---|
| `mining_level` | `PlayerStat.MiningLevel` |
| `unlocked_nodes` | `UpgradeTreeState.unlockedNodeIds.Count` |
| `warehouse_count` | `PlayerData.warehouseData` 보관 아이템 **총 개수**(종류 수 아님) |
| `stock_value` | `PortfolioManager.GetTotalValue()` |
| `stock_cost` | `PortfolioManager.GetTotalCost()` |

기존 필드(`ended_day` / `gold_start` / `gold_end` / 카테고리별 증감 / `max_depth`)는 유지한다.

### 5.3 의도적으로 넣지 않는 것

**하루 잠수 횟수 · 하루 파기량 · 하루 스태미나 소진 횟수.**
전부 `dives.csv`를 `run_id + day`로 group-by하면 나온다.
같은 수치를 두 군데서 재면 언젠가 어긋나고, 어긋난 순간 **어느 쪽이 맞는지 판정할 방법이 없다.**

**귀환 사유 컬럼.** 현재 게임에는 강제 귀환이 없다(긴급 탈출 제외).
자발·스태미나·무게가 전부 플레이어가 스스로 올라온 것이므로 **사유는 직접 관측이 불가능하다.**
`stamina_pct` / `weight_ratio` / `mineral_count`로 사후 추론한다.
잘못 라벨링된 컬럼은 없는 컬럼보다 나쁘다.

**파산 근접 횟수.** 임계값 정의가 밸런싱 중 계속 바뀔 값이고,
이 게임은 파산에 페널티가 없어 애초에 지표가 아니라 상태다.
`gold_end`가 있으므로 필요하면 파이썬에서 임계값을 바꿔가며 센다.

---

## 6. 층별 체류 시간 — `RegionDwellTracker` (순수 C#)

`SettlementManager.Update()`는 매 프레임 도는 코드다. 여기에 Dictionary 조회를 박고 싶지 않고,
MonoBehaviour 안에 있으면 EditMode 테스트도 불가능하다.

`Assets/Scripts/_Core/Telemetry/RegionDwellTracker.cs` — Unity 의존 없는 순수 C#으로 분리한다.

```csharp
tracker.Tick(deltaTime);      // 현재 지역에 누적 — float += 하나
tracker.SwitchTo(region);     // 전환 시에만 dictionary flush
tracker.ToPayloadObject();    // {"Dirt":124.5,"Stone":88.2}
tracker.Reset();
```

**불변식:** 현재 지역의 누적값은 `SwitchTo` 또는 `ToPayloadObject` 호출 전까지
dictionary가 아니라 필드에 있다. `ToPayloadObject`는 호출 시점에 현재 구간을 flush한 스냅샷을 낸다
(호출 후에도 계속 누적 가능).

`SettlementManager` 변경은 세 줄이다.

| 위치 | 추가 |
|---|---|
| `Update()` | `_dwell.Tick(Time.deltaTime)` |
| `UpdateTelemetryContext()` 지역 전환 분기 | `_dwell.SwitchTo(region)` |
| `StartTracking()` | `_dwell.Reset()` |

프레임당 비용은 `float +=` 하나다.

### ⚠ `region_seconds` 합계는 `seconds`보다 작을 수 있다

`dives.csv`를 다룰 때 `sec_*` 컬럼을 전부 더하면 `seconds`(잠수 총 시간)와 같아질 거라 가정하기 쉽지만,
둘은 파티션 관계가 아니다. 두 경로로 어긋난다.

첫째, `Update()`에서 `_dwell.Tick()`이 `UpdateTelemetryContext()`(그 안에서 `SwitchTo`를 호출)보다
**먼저** 실행된다. 잠수를 시작한 첫 프레임은 아직 `_dwell`의 현재 지역이 비어 있는 상태라
`Tick()`이 조용히 버린다(`RegionDwellTracker.Tick`은 `_current`가 비어 있으면 그대로 리턴). 딱 1프레임이라
곡선에 영향을 줄 크기는 아니다.

둘째, `TileDataManager.Instance`가 `null`인 구간에서는 `UpdateTelemetryContext()`의 지역 판정 자체가
멈춘다 — `region`이 계속 `_lastReportedRegion`과 같게 나와 `SwitchTo`가 영영 호출되지 않고,
`_dwell`의 현재 지역은 빈 문자열에 고정된 채 `Tick()`이 매 프레임 버려진다. 이 구간에서도
`TimeUnderground`(→`seconds`)는 `Update()` 맨 앞에서 무조건 누적되므로, 이 경우는 1프레임 수준이 아니라
**그 구간 전체가 통째로 `region_seconds`에서 빠진다.**

JSON 자체는 항상 유효하므로 파싱이 깨지지는 않는다. 다만 `sec_*` 합계를 `seconds`와 비교해
차이가 나면 그 자체가 신호다 — 지역 판정이 멈춘 구간이 있었다는 뜻이므로, "체류 시간 파티션이
안 맞는다"를 버그로 보고할 게 아니라 분석 시 둘을 별개 수치로 다뤄야 한다.

---

## 7. 픽셀 파기량 — `DigResult`에 개수를 태운다

현재 `TerrainModifier.ProcessDigPixels`는 `bool`을 반환하고 `DigResult.WasModified`로만 남는다.
이를 `int`(제거한 픽셀 수)로 바꾸고 `DigResult`에 `RemovedPixels` 필드를 추가한다.
픽셀 데이터를 이미 쓰고 있는 루프이므로 `int++` 하나는 측정 불가능한 비용이다.

`TerrainChunk.Dig()` 호출부에서 `SettlementManager.AddDugPixels(result.RemovedPixels)`로 누적하고,
`StartTracking()`에서 리셋, `dive_end`에서 소비한다.

### `OnPixelDestroyed` 이벤트를 구독하지 않는 이유

`TerrainModifier.OnPixelDestroyed` static 이벤트가 이미 있고 `TileEventDispatcher`가 구독 중이지만,
**그 이벤트에는 부유섬 제거·침식·폭발로 사라진 픽셀까지 전부 올라온다.**
"플레이어가 곡괭이로 판 픽셀"을 재려면 플레이어 파기 경로인 `Dig()` 반환값이 맞다.

### ⚠ 동작 보존

`WasModified`의 소비처가 여러 곳이므로 **`WasModified`를 `RemovedPixels > 0`으로 치환하지 않는다.**
기존 필드는 그대로 두고 필드만 추가한다. 게임 동작 변화는 0이어야 한다.

---

## 8. 파이썬 변환기

`Tools/telemetry/export_csv.py` (프로젝트 루트 기준 — `Assets/`와 형제) — **Unity `Assets/` 밖에 둔다.**
안에 넣으면 Unity가 임포트하고 빌드 산출물에 딸려 간다.

```
python export_csv.py <telemetry_dir> -o <out_dir> [--include-fixture]
```

동작:

1. 디렉토리의 모든 `*.jsonl`을 읽는다. 세션 파일이 여러 개여도 `run_id`로 합쳐진다
2. `event` 필드로 분류 → `day_settled` → `days.csv`, `dive_end` → `dives.csv`
3. `run_id, day` 순으로 정렬
4. 중첩 오브젝트는 컬럼으로 펼친다 — `region_seconds` → `sec_Dirt` / `sec_Stone` / …,
   `minerals` → `min_Iron` / `min_Gold` / ….
   등장하는 키 집합은 입력 전체를 한 번 훑어 결정한다(2-pass)
5. **기본적으로 `is_fixture == true`가 붙은 이벤트를 제외한다** — 회차 전체가 아니라
   마킹 시점 이후의 이벤트만 걸러진다(마킹 전 이벤트는 조작되지 않은 실데이터이므로 남는다).
   `--include-fixture`로 켠다
6. 없는 필드는 빈칸으로 둔다 — 구버전 로그가 섞여도 죽지 않는다
7. 깨진 줄(JSON 파싱 실패)은 건너뛰고 개수만 stderr에 보고한다.
   크래시로 잘린 마지막 줄이 흔하다

**표준 라이브러리만 사용한다** (`json` / `csv` / `argparse` / `pathlib`).
pandas 의존이 없어야 아무 데서나 굽는다.

### 파생 컬럼을 넣지 않는 이유

시간당 광물, 긴급탈출률, 파산 임계값 도달 여부 등은 CSV에 넣지 않는다.
전부 원시 컬럼에서 계산되고, **정의가 계속 바뀔 값을 스크립트에 박으면 바꿀 때마다 다시 구워야 한다.**
엑셀/노트북에서 계산한다.

---

## 9. 성능

지형 파이프라인이 프레임 예산에 민감하므로 프레임당 비용을 명시한다.

| 항목 | 프레임당 비용 |
|---|---|
| `RegionDwellTracker.Tick` | `float +=` 하나 |
| 픽셀 카운터 | 이미 도는 파기 루프 안 `int++` 하나 |
| `dive_end` 필드 확충 | 잠수 1회당 1건 |
| `day_settled` 필드 확충 | 하루 1건 |
| 파일 I/O | **변화 없음** — 플러시 주기·경로 모두 그대로 |

GC 할당은 `ToPayloadObject()`의 문자열 조립뿐이고, 잠수 종료 시 1회다.

---

## 10. 테스트

EditMode 테스트를 작성한다. **실행은 사람이 직접 한다**(`CLAUDE.md` 규칙).

| 테스트 | 대상 |
|---|---|
| `RegionDwellTrackerTests` | 전환·누적·리셋, `ToPayloadObject` 후 계속 누적되는지 |
| `TelemetrySessionTests` (추가) | `run_id` / `is_fixture`가 라인에 실리는지, 미설정 시 빈 값 |
| `TelemetryPayloadTests` (추가) | `AddRaw`가 중첩 오브젝트를 따옴표 없이 넣는지 |
| `TerrainDigPixelCountTests` | `RemovedPixels`가 실제 제거 수와 일치, `WasModified` 동작 불변 |

마지막 항목의 두 번째 단언이 **회귀 가드**다. 빈 공간을 팠을 때 `WasModified`가 false로 남는지를 못 박아,
`RemovedPixels` 도입이 기존 의미를 바꾸지 않았음을 계속 검증한다.

파이썬 변환기는 샘플 JSONL 몇 줄로 손으로 확인한다. 별도 테스트 하네스는 과하다.

---

## 11. 이번 범위에서 제외

- `sessions.csv` (이탈 분석)
- 파산 근접 횟수
- 귀환 사유 라벨
- 원격 전송 — `design.md` Phase 2
- 미구현 훅 `coin_day_summary` / `stock_trade` / `guide_skipped` — 이번 KPI에 쓰이지 않음

---

## 12. 알려진 한계

- **`region`은 `TileType` 이름 문자열이다.** 7층→4층 리팩터링이 반영되면 값이 바뀌어
  이전 데이터와 층 비교가 불가능해진다 (`design.md` §9와 동일한 이슈).
  `version` 필드로 구간을 나눠 보는 수밖에 없다.
- **`anon_id`는 PC당 하나다.** 같은 PC의 여러 회차는 `run_id`로 갈리지만,
  다른 PC의 데이터를 합치려면 파일을 수동으로 모아야 한다 (Phase 2 전까지).
- **`dive_end`가 없는 잠수가 존재할 수 있다.** 지하에서 알트+F4로 끄면 기록되지 않는다.
  `dive_start` 대비 `dive_end` 건수 차이로 크기를 볼 수 있다.
