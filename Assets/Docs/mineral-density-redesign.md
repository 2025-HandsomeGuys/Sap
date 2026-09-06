# 광물 스폰 밀도 파라미터 재설계

작성일: 2026-08-06
대상: `MineralRuleJson` 스키마, `MineralGenerator`, `DiggableRock`, `tileData.json`

---

## 1. 배경

청크당 광물이 나오는 양을 조절하려고 `tileData.json`의 `attemptsPerChunk`를 반복해서 손대고 있었다.
값을 만질 때마다 "이게 맞는 노브인가?" 하는 위화감이 있었고, 실제로 조사해 보니 **위화감이 정당했다.**

---

## 2. 문제 — 필드 하나가 두 시스템에서 다른 뜻으로 쓰인다

`MineralRuleJson`은 소비처가 두 곳인데, **같은 필드를 서로 무관한 의미로 해석한다.**

| 필드 | `MineralGenerator` (청크 지형 스폰) | `DiggableRock` (돌 깨기 드랍) |
|---|---|---|
| `chance` | 스폰 확률 (깊이 보정 후 롤) | **깊이 선호 가중치** — `Lerp(chance, 1-chance, t)` |
| `minCount` / `maxCount` | 청크당 스폰 개수 | **돌 1개당 드랍 개수** (깊이 보간) |
| `attemptsPerChunk` | rule 반복 횟수 | 사용 안 함 |
| `rarity` | 깊이 보정 곡선 선택 | 후보 필터 (희귀 우선, 없으면 일반 폴백) |
| `minDepth` / `maxDepth` | 적용 깊이 범위 | 적용 깊이 범위 + 보간 t |

근거:
- `MineralGenerator.cs:49, 76-87` — `attempts` 루프 / `chance` 롤 / `minCount~maxCount` 개수
- `DiggableRock.cs:560` — `weights[i] = Mathf.Lerp(r.chance, 1f - r.chance, rt)`
- `DiggableRock.cs:577-578` — `minCount`/`maxCount`로 드랍 개수 산출

### 2-1. 이것이 만든 결과

**청크 밀도를 올리려고 `chance`나 `minCount`를 만지면 돌 드랍 밸런스가 함께 망가진다.**
Copper의 `chance = 0.6`은 "스폰 확률 60%"가 아니라 "돌 드랍 시 표면 선호 60%"라는 뜻도 동시에 갖는다.

그래서 부작용이 없는 노브는 `attemptsPerChunk` 하나뿐이었다.
`attemptsPerChunk`만 만지게 된 것은 실수가 아니라, **다른 선택지가 전부 오염돼 있었기 때문**이다.

### 2-2. 부차적 문제 — 밀도가 세 노브의 곱으로 흩어져 있다

```
기대 개수 = attemptsPerChunk × chance(깊이보정) × avg(minCount~maxCount)
```

- JSON을 읽어도 "이 광물이 청크당 몇 개 나오는지" 알 수 없다. 곱셈을 해야 한다.
- 현재 값 조합에서 `chance`는 랜덤성을 잃었다. Dirt층 GarbageBag은 `attempts=12, chance=0.9` → 대수의 법칙으로 거의 항상 11~12번 성공한다. 확률 판정이 아니라 상수 배율로 동작 중.
- 층 전체·게임 전체를 한 번에 스케일하는 노브가 없다. "광물 20% 더"를 하려면 29개 rule을 전부 손봐야 한다.

### 2-3. 발견된 밸런스 편차 (이번 작업에서 수정하지 않음)

기존 식으로 층별 청크당 기대 광물 수를 환산한 결과:

| 층 | 층 최상단 | 층 최하단 |
|---|---|---|
| **Dirt** | **약 110개** | **약 109개** |
| Ice | 약 25개 | 약 18개 |
| MagmaRock | 약 25개 | 약 18개 |
| MeteoriteRock | 약 19개 | 약 15개 |

Dirt층이 다른 층의 **4~6배**다. Dirt에만 `attemptsPerChunk`를 6→12로 올린 흔적이 남아 있고,
기존 구조에서는 이 숫자가 드러나지 않아 편차를 인지할 수 없었다.

**이 문서의 작업 범위는 파라미터 구조 변경까지이고, 밸런스 값은 손대지 않는다.**
마이그레이션은 현재 동작을 1:1로 보존한다. 편차 조정은 새로 생기는 `mineralDensity` 노브로 별도 판단한다.

---

## 3. 설계 — 두 시스템의 필드를 분리한다

### 3-1. 새 `MineralRuleJson` 스키마

```json
{
  "mineralType": "Coal",
  "minDepth": 0,
  "maxDepth": 19,
  "rarity": "Common",

  "perChunk": [23, 35],

  "rockDropWeight": 0.8,
  "rockDropCount": [2, 4]
}
```

| 필드 | 소비처 | 의미 |
|---|---|---|
| `perChunk` | `MineralGenerator` **전용** | 청크당 스폰 개수 범위 (깊이·배율 보정 전 기준값) |
| `rockDropWeight` | `DiggableRock` **전용** | 드랍 광물 선택 가중치 (구 `chance`) |
| `rockDropCount` | `DiggableRock` **전용** | 돌 1개당 드랍 개수 범위 (구 `minCount`/`maxCount`) |
| `rarity` | 양쪽 공유 | 깊이 곡선 선택 + 돌 드랍 후보 필터. **의미가 일관되므로 공유 유지** |
| `minDepth`/`maxDepth` | 양쪽 공유 | 적용 깊이 범위. **의미가 일관되므로 공유 유지** |

**제거**: `chance`, `minCount`, `maxCount`, `attemptsPerChunk`

`rarity`와 깊이 범위는 두 시스템에서 뜻이 같으므로 굳이 쪼개지 않는다. 분리하면 같은 값을 두 번 적는 중복만 생긴다.

### 3-2. 밀도 배율 2단

| 위치 | 필드 | 기본값 | 용도 |
|---|---|---|---|
| `TileDataJson` | `mineralDensity` | `1.0` | 층 전체 밀도 |
| `TileDatabaseJson` | `globalMineralDensity` | `1.0` | 게임 전체 밀도 |

`JsonUtility`는 JSON에 없는 필드에 대해 C# 필드 초기화값을 유지한다
(`TileDataJson.wallClimbSpeedRatio = 1.0f`가 이미 같은 방식으로 동작 중).
따라서 기존 JSON에 두 필드가 없어도 `1.0`으로 안전하게 동작한다.

### 3-2-1. 튜닝용 스폰 로그

밀도 값을 맞출 때 "요청한 만큼 실제로 박혔는지"를 봐야 하므로(§6-2) 청크별 리포트를 붙인다.

| 위치 | 필드 | 기본값 |
|---|---|---|
| `TileDatabaseJson` | `logMineralSpawn` | `false` |

`true`면 청크마다 콘솔에 출력한다.

```
[Minerals] chunk(3,-2) Dirt depth=2 density=3 | 배치 287 / 요청 412 (70%)
  ScrapMetal 71/93
  GarbageBag 98/142
  ...
```

**요청 대비 배치 비율이 이 로그의 핵심이다.** 비율이 낮으면 `perChunk`를 더 올려도 소용이 없고,
지형이 성겨서 `IsWellSupported`가 떨어뜨리고 있다는 뜻이다.

재컴파일 없이 켜고 끄도록 코드 상수가 아니라 JSON에 뒀다.
**평소엔 꺼둔다** — 청크 로드는 GC에 민감한 경로이고(`Assets/Docs/performance/chunk-load-gc.md`)
켜면 청크마다 `StringBuilder`와 문자열이 생긴다. 꺼져 있으면 문자열 관련 할당이 전혀 없도록
`logSummary` 분기 안에서만 만든다.

### 3-3. 스폰 개수 계산

`MineralGenerator.TrySpawnMineralsFromRule`을 다음으로 대체한다.

```
if (currentDepth < minDepth || currentDepth > maxDepth) return;

t = (maxDepth > minDepth) ? clamp01((currentDepth - minDepth) / (maxDepth - minDepth)) : 1

depthFactor = IsRare ? t              // Rare: 0 → 1  (깊을수록 많이)
                     : 1 - 0.5 * t     // Common: 1 → 0.5 (깊을수록 적게)

n  = Lerp(perChunk[0], perChunk[1], prng.NextDouble())   // float
n *= depthFactor
n *= tile.mineralDensity * db.globalMineralDensity

count = ProbabilisticRound(n, prng)

for (i in 0..count):
    if GetRandomValidPosition(...) → SpawnAndCarveMineral(...)
```

**`depthFactor` 곡선과 배치 로직(`GetRandomValidPosition` / `IsWellSupported`)은 전혀 건드리지 않는다.**
바뀌는 것은 "몇 개를 요청하는가"뿐이다. 분포 모양은 그대로 균등이다.

`GenerateMinerals`의 `attempts` 루프(`MineralGenerator.cs:47-55`)는 사라진다.
rule 하나당 계산 1회 · 스폰 루프 1회로 단순화된다.

### 3-4. 확률 반올림 (`ProbabilisticRound`)

```
floor = Floor(n)
frac  = n - floor
return floor + (prng.NextDouble() < frac ? 1 : 0)
```

`n = 3.4` → 60% 확률로 3개, 40% 확률로 4개. 기대값은 정확히 `n`.

이게 있어야 **1개 미만의 희소 광물을 정직하게 표현할 수 있다.**
현재 Ice층 Copper의 기대 개수는 `4 × 0.08 × 1.5 = 0.48`개인데, 세 노브의 곱에 숨어 있어 읽히지 않는다.
새 스키마에서는 `"perChunk": [0, 1]`로 그대로 드러난다.

**반드시 청크 시드 `prng`를 쓴다.** `UnityEngine.Random`을 쓰면 청크 결정성이 깨진다.

### 3-5. `DiggableRock` 변경

필드 이름만 바꾸는 기계적 치환이다. 로직은 그대로 둔다.

`rockDropCount` 접근이 3회 반복되므로 `MineralRuleJson`에 읽기 전용 프로퍼티를 두어 널 가드를 한 곳에 모은다.
호출부는 배열을 직접 인덱싱하지 않는다.

```csharp
public int[] rockDropCount;   // JSON: [min, max]

public int RockDropMin => (rockDropCount != null && rockDropCount.Length > 0) ? rockDropCount[0] : 1;
public int RockDropMax => (rockDropCount != null && rockDropCount.Length > 1) ? rockDropCount[1] : RockDropMin;
```

| 위치 | 기존 | 변경 후 |
|---|---|---|
| `DiggableRock.cs:560` | `Mathf.Lerp(r.chance, 1f - r.chance, rt)` | `Mathf.Lerp(r.rockDropWeight, 1f - r.rockDropWeight, rt)` |
| `DiggableRock.cs:577` | `picked.minCount`, `picked.maxCount` | `picked.RockDropMin`, `picked.RockDropMax` |
| `DiggableRock.cs:578` | `picked.minCount` | `picked.RockDropMin` |

`perChunk`는 소수를 허용해야 하므로 `float[]`, `rockDropCount`는 정수 개수이므로 `int[]`로 둔다.

---

## 4. 마이그레이션 표

환산식은 무손실이다.

- `perChunk` 평균 = 기존 `attemptsPerChunk × chance × avg(minCount, maxCount)`
- `rockDropWeight` = 기존 `chance` (그대로)
- `rockDropCount` = 기존 `[minCount, maxCount]` (그대로)

`perChunk`의 폭은 기존 분포의 표준편차를 대략 따라가도록 평균 주변에 잡았다.
(`attempts`회 베르누이 × 균등 개수의 합 → 분산이 `attempts`와 `count` 폭에 비례)

### Dirt (깊이 0~19)

| 광물 | 기존 `attempts × chance × avg` | = 기대값 | `perChunk` | `rockDropWeight` | `rockDropCount` |
|---|---|---|---|---|---|
| ScrapMetal | 6 × 0.9 × 3.0 | 16.2 | `[12, 20]` | 0.9 | `[2, 4]` |
| GarbageBag | 12 × 0.9 × 3.0 | 32.4 | `[26, 39]` | 0.9 | `[2, 4]` |
| PETBottle | 12 × 0.9 × 3.0 | 32.4 | `[26, 39]` | 0.9 | `[2, 4]` |
| Coal | 12 × 0.8 × 3.0 | 28.8 | `[23, 35]` | 0.8 | `[2, 4]` |
| Copper *(Rare)* | 12 × 0.6 × 4.5 | 32.4 | `[20, 45]` | 0.6 | `[1, 8]` |
| Iron *(Rare)* | 12 × 0.4 × 4.5 | 21.6 | `[12, 32]` | 0.4 | `[1, 8]` |

### Ice (깊이 20~39)

| 광물 | 기존 | = 기대값 | `perChunk` | `rockDropWeight` | `rockDropCount` |
|---|---|---|---|---|---|
| Meteorite | 4 × 0.5 × 3.0 | 6.0 | `[3, 9]` | 0.5 | `[2, 4]` |
| Fossil | 4 × 0.5 × 3.0 | 6.0 | `[3, 9]` | 0.5 | `[2, 4]` |
| Silver | 4 × 0.5 × 3.0 | 6.0 | `[3, 9]` | 0.5 | `[2, 4]` |
| Sapphire | 4 × 0.5 × 3.0 | 6.0 | `[3, 9]` | 0.5 | `[2, 4]` |
| Emerald *(Rare)* | 4 × 0.5 × 1.5 | 3.0 | `[1, 5]` | 0.5 | `[1, 2]` |
| Topaz *(Rare)* | 4 × 0.5 × 1.5 | 3.0 | `[1, 5]` | 0.5 | `[1, 2]` |
| Copper *(20~29)* | 4 × 0.08 × 1.5 | 0.48 | `[0, 1]` | 0.08 | `[1, 2]` |
| Iron *(20~29)* | 4 × 0.08 × 1.5 | 0.48 | `[0, 1]` | 0.08 | `[1, 2]` |

### MagmaRock (깊이 40~59)

| 광물 | 기존 | = 기대값 | `perChunk` | `rockDropWeight` | `rockDropCount` |
|---|---|---|---|---|---|
| Obsidian | 4 × 0.5 × 3.0 | 6.0 | `[3, 9]` | 0.5 | `[2, 4]` |
| Quartz | 4 × 0.5 × 3.0 | 6.0 | `[3, 9]` | 0.5 | `[2, 4]` |
| Gold | 4 × 0.5 × 3.0 | 6.0 | `[3, 9]` | 0.5 | `[2, 4]` |
| Ruby | 4 × 0.5 × 3.0 | 6.0 | `[3, 9]` | 0.5 | `[2, 4]` |
| Diamond *(Rare)* | 4 × 0.5 × 1.5 | 3.0 | `[1, 5]` | 0.5 | `[1, 2]` |
| LavaStone *(Rare)* | 4 × 0.5 × 1.5 | 3.0 | `[1, 5]` | 0.5 | `[1, 2]` |
| Emerald *(40~49)* | 4 × 0.08 × 1.5 | 0.48 | `[0, 1]` | 0.08 | `[1, 2]` |
| Topaz *(40~49)* | 4 × 0.08 × 1.5 | 0.48 | `[0, 1]` | 0.08 | `[1, 2]` |

### MeteoriteRock (깊이 60~79)

| 광물 | 기존 | = 기대값 | `perChunk` | `rockDropWeight` | `rockDropCount` |
|---|---|---|---|---|---|
| Mithril | 4 × 0.5 × 3.0 | 6.0 | `[3, 9]` | 0.5 | `[2, 4]` |
| Gravitonium | 4 × 0.5 × 3.0 | 6.0 | `[3, 9]` | 0.5 | `[2, 4]` |
| Uranium | 4 × 0.5 × 3.0 | 6.0 | `[3, 9]` | 0.5 | `[2, 4]` |
| VoidStone *(Rare)* | 4 × 0.5 × 1.5 | 3.0 | `[1, 5]` | 0.5 | `[1, 2]` |
| StarFragment *(Rare)* | 4 × 0.5 × 1.5 | 3.0 | `[1, 5]` | 0.5 | `[1, 2]` |
| Diamond *(60~69)* | 4 × 0.08 × 1.5 | 0.48 | `[0, 1]` | 0.08 | `[1, 2]` |
| LavaStone *(60~69)* | 4 × 0.08 × 1.5 | 0.48 | `[0, 1]` | 0.08 | `[1, 2]` |

---

## 5. 영향 파일

| 파일 | 변경 |
|---|---|
| `Assets/Scripts/_Core/Data/TileDataModels.cs` | `MineralRuleJson` 스키마 교체, `TileDataJson.mineralDensity` / `TileDatabaseJson.globalMineralDensity` 추가 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/MineralGenerator.cs` | `attempts` 루프 제거, 개수 계산 교체, `ProbabilisticRound` 추가, 배율 전달 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/Decorators/MineralDecorator.cs` | `GenerateMinerals`에 `mineralDensity` × `globalMineralDensity` 전달 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/DiggableRock.cs` | 필드명 3곳 치환 |
| `Assets/Scripts/_Core/Managers/TileDataManager.cs` | 로드 시 `perChunk` 유효성 1회 검증 로그 |
| `Assets/StreamingAssets/tileData.json` | 29개 rule 전부 §4 표대로 교체 |

**영향 없음 확인:**
- `MineralUpgradeLadder.cs` — `mineralType` / `IsRare`만 사용. 두 필드 모두 유지되므로 변경 불필요
- `LootTable.cs` / `SpecialChunkSettingsData.cs` — `chance`/`minCount` 이름이 겹치지만 **다른 타입**이다. 무관

---

## 6. 주의사항

### 6-1. ⚠ 기존 세이브의 미방문 청크 광물 배치가 달라진다

`MineralGenerator`는 청크 좌표+월드시드로 만든 `System.Random`을 순서대로 소비한다
(`GetDeterministicRandom`, `MineralGenerator.cs:58-62`).
기존: rule당 `attempts`회 × (롤 1 + 개수 1) 소비 → 신규: rule당 1회 소비.
**난수 스트림 소비 순서가 바뀌므로 같은 시드라도 배치 결과가 달라진다.**

- 이미 방문한 청크는 `PixelInfo`에 스폰·수집 마커가 저장돼 있어 영향 없다
- 아직 로드된 적 없는 청크는 배치가 바뀐다 — 기존 세이브에서 "가본 적 없는 구역의 광물 위치가 달라짐"은 정상이다

### 6-2. `perChunk`는 요청량이지 보장량이 아니다

`GetRandomValidPosition`은 위치를 최대 20회 랜덤 시도하고
`IsWellSupported`(반경 5px 8이웃 전부 솔리드)를 통과 못 하면 **조용히 포기한다**
(`MineralGenerator.cs:137-154`).
공동이 많은 청크일수록 실제 배치 수가 요청량보다 적다.

이는 기존 구조에서도 동일하게 존재하던 현상이고, 이번 작업으로 나빠지지도 좋아지지도 않는다.
`perChunk` 값을 "정확히 이만큼 박힌다"로 읽으면 안 된다.

### 6-3. `JsonUtility` 배열 필드

`JsonUtility`는 최상위 배열은 못 다루지만 **클래스 필드로서의 배열은 지원한다.**
`public float[] perChunk;` ↔ `"perChunk": [23, 35]`는 정상 동작한다.

다만 JSON에 필드가 없으면 `null`이 되므로(초기화값이 있어도 `[]`가 아님) 널 가드가 필요하다.

- **로드 시점**: `TileDataManager`가 전 rule을 1회 검증해 `perChunk` 누락·길이 부족을 콘솔에 모아 출력한다.
  런타임에 매 청크마다 경고를 뿜으면 로그가 범람한다.
- **런타임**: `perChunk`가 `null`이거나 길이가 2 미만이면 해당 rule은 **스폰 0개로 조용히 건너뛴다.**
  기본값 1을 넣어 "설정 안 한 광물이 조금씩 나오는" 상태를 만들면 원인 추적이 어려워진다.
  누락은 로드 로그에서 잡는다.

### 6-4. 밸런스 값은 이 작업에서 바꾸지 않는다

§2-3의 Dirt 편중은 **의도적으로 남긴다.** 구조 변경과 밸런스 변경을 한 커밋에 섞으면
결과가 달라졌을 때 원인이 어느 쪽인지 가릴 수 없다.
마이그레이션 후 `mineralDensity` / `globalMineralDensity`로 별도 조정한다.

---

## 7. 검증

1. **환산 정확성** — `perChunk` 평균이 §4 표의 기대값과 일치하는지 EditMode 테스트
   (`MineralRuleJson` → 기대 개수 계산은 Unity 비의존 순수 함수로 뽑아 테스트 가능하게 한다)
2. **`ProbabilisticRound` 기대값** — 고정 시드로 10,000회 반복 시 평균이 `n`에 수렴하는지
3. **깊이 곡선 보존** — Rare가 층 최상단에서 0개, 최하단에서 최대인지 / Common이 반대인지
4. **`DiggableRock` 드랍 불변** — 치환 전후 같은 시드에서 드랍 광물·개수가 동일한지
5. **육안 확인** — Dirt층 청크 광물 수가 마이그레이션 전후로 비슷한지 (스크린샷 비교)

Unity Test Runner 실행은 사람이 수행한다 (프로젝트 규약).

---

## 8. 하지 않는 것

| 항목 | 이유 |
|---|---|
| 광맥(vein) 배치 — 노이즈 기반 뭉침 | 배치 알고리즘 변경. 이번 범위는 "분포는 그대로, 개수만" |
| 층별 총량 예산 + rule 가중치 방식 | 층당 rule이 6~8개뿐이라 오버엔지니어링. "이 광물은 청크당 N개"라는 절대량 표현을 잃는 손해가 더 크다. 광물 종류가 크게 늘면 재검토 |
| `IsWellSupported` 완화로 배치 실패율 개선 | 별개 문제. 완화하면 광물이 공동에 떠서 튀어나오는 과거 버그가 재발할 수 있다 |
| Dirt층 밀도 편중 수정 | §6-4 |
| `rarity` / `minDepth` / `maxDepth` 분리 | 두 시스템에서 의미가 동일하다. 분리하면 중복 입력만 생긴다 |

---

## 9. 후속 — 층 간 광물 번짐(bleed-over) 제거 (2026-08-18)

각 층은 **바로 윗층 광물 2종**을 자기 층 상단 10칸에 `Common`으로 소량 깔고 있었다
(`rockDropWeight` 0.08 / `perChunk` `[0,1]`~`[1,3]`). §6-2~6-4 표의 아래 두 줄이 그것이다.

| 층 | 제거한 규칙 | 원래 소속 |
|---|---|---|
| Ice (20~29) | Copper, Iron | Dirt `Rare` |
| MagmaRock (40~49) | Emerald, Topaz | Ice `Rare` / `Common` |
| MeteoriteRock (60~69) | Diamond, LavaStone | MagmaRock `Rare` |

**제거 이유** — 윗층에서 희귀했던 광물이 다음 층에서 흔한 광물로 다시 나와, 층이 바뀌어도
"새 광물을 캔다"는 감각과 가격 단계(§`economy/mineral-price-design.md`)가 흐려졌다.
이제 광물은 **층 전용**이다. 층 경계 근처에서 윗층 광물이 나오는 것은
`layerBoundaryNoise`로 타일 타입 자체가 밀려 올라온 경우뿐 — 이건 의도된 지질 경계다.

**영향 범위**
- `MineralGenerator` 청크 스폰 / `DiggableRock` 돌 깨기 드랍 두 경로에서 동시에 빠진다(같은 rule을 공유).
- 층별 기대 개수는 층당 약 1개 감소 — 밀도 튜닝에는 영향 없는 수준.
- `MineralUpgradeLadder`(도깨비 가마솥)는 **원래부터 bleed-over를 무시**했다
  (`_rungOf.ContainsKey` 중복 제거). 승급 사다리 순서는 변화 없음.
- 1층 쓰레기 특수청크(`TrashWallDropper` / `ScrapExplosionDropper` / `CompressedTrashDropper`)의
  Copper·Iron 하드코딩 드랍은 1층 소속이라 그대로 둔다.
