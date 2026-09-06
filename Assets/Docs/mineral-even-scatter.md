# 광물 균등 배치 (지터 격자)

작성일: 2026-08-08
대상: `MineralGenerator` 배치 경로, 신규 `MineralScatter`
선행 문서: `Assets/Docs/mineral-density-redesign.md` (개수 노브 재설계 — 이 문서는 그 위에 얹힌다)

---

## 1. 문제

광물이 눈에 띄게 뭉치고, 사이사이에 빈 구멍이 생긴다.

원인은 두 가지다.

### 1-1. 균등난수는 균등분포가 아니다

`GetRandomValidPosition`이 청크 전체에서 좌표를 그냥 균등난수로 뽑는다.

```csharp
int tx = prng.Next(lo, hiX);
int ty = prng.Next(lo, hiY);
```

균등난수로 점을 뿌리면 통계적으로 **반드시** 뭉치와 공백이 생긴다(Poisson point process).
버그가 아니라 난수의 정상 동작이며, 점 개수를 늘려도 해소되지 않는다.

### 1-2. rule마다 따로 배치한다

`GenerateMinerals`는 rule 하나의 개수를 계산하면 곧바로 그 rule의 광물을 전부 심는다.
GarbageBag을 심는 시점에 PETBottle이 어디 깔릴지 모르므로 서로 피할 수 없다.
각 광물이 개별적으로 고르게 깔려도 **합쳐놓으면 겹치고 뭉친다.**

---

## 2. 기법 선택

이 문제는 그래픽스에서 **blue noise** 로 연구된 영역이다. 실무 선택지와 우리 제약의 적합성:

| 기법 | 대표 근거 | 우리 케이스 적합성 |
|---|---|---|
| **지터 격자 (stratified)** | Cook 1986, *Stochastic Sampling in Computer Graphics* | **채택.** 아래 참조 |
| Poisson disk | Bridson 2007, *Fast Poisson Disk Sampling in Arbitrary Dimensions* | 품질 최상. 단 반경으로 개수를 역산해야 해 개수 제어가 간접적 |
| Best-candidate | Mitchell 1991 | 품질 중간, O(n·k) + 거리 쿼리 |
| blue noise 마스크 (프리베이크) | 실시간 렌더링 디더링·샘플링의 사실상 표준 | 아래 §2-2 |

### 2-1. 지터 격자를 고른 이유

**품질이 가장 좋아서가 아니다.** 품질만 줄 세우면 `Poisson disk ≈ 마스크 > 지터 격자`다.
제약 적합성으로 고른 것이다.

| 우리 제약 | 지터 격자 | Poisson disk | 마스크 |
|---|---|---|---|
| **정확한 개수** (rule별로 N개) | ✅ 셀당 1개 | ❌ 반경→개수 역산 | ❌ 임계값 간접 제어 |
| **지형 검증 실패 시 재시도** | ✅ 셀이 재시도 범위를 정의 | ⚠️ 가능하나 복잡 | ❌ 위치 고정 |
| **청크별 결정론** | ✅ 시드만 있으면 됨 | ✅ | ⚠️ 타일 반복 |
| **가변 밀도** (깊이·층·전역 배율) | ✅ 격자 크기 조정 | ⚠️ 반경 재계산 | ⚠️ 임계값 상관관계 |
| 런타임 비용 | 무의미 | 무의미 | 무의미 |

`mineral-density-redesign.md`에서 `perChunk` 하나로 개수 노브를 정리한 직후다.
Poisson disk나 마스크를 쓰면 그 노브가 다시 간접 제어로 돌아간다 — 정리한 것을 되돌리는 셈이다.

### 2-2. blue noise 마스크가 우리 케이스에 안 맞는 이유

마스크가 압도적인 영역은 **픽셀당 샘플링**(디더링·SSAO·TAA 지터)이다.
프레임마다 수백만 픽셀을 판정하므로 텍스처 조회 O(1)이 결정적이다.

우리 문제는 **청크 로드 1회당 점 500개**다. 규모가 4~5자릿수 차이라 그 최적화가 사는 지점이 아니다.
**마스크의 유일한 압도적 장점이 무의미해지면 남는 것은 단점뿐이다:**

- 개수를 정할 수 없다 (임계값이 밀도를 정하고 개수는 결과로 나온다)
- 지형을 모른다. `IsWellSupported` 탈락 시 마스크는 고정이라 근처에서 다시 뽑을 수 없다
- 1000×1000 청크마다 같은 패턴이 반복된다. 플레이어가 오래 들여다보는 대상이라 눈에 띈다
- 밀도가 청크마다 달라 임계값을 바꿔야 하는데, 그러면 저밀도 청크 광물이 고밀도 청크의 부분집합이 된다

### 2-3. 교체 가능성을 남겨둔다

셀 크기는 약 43px이고 광물 스프라이트는 그보다 훨씬 작아 격자 티가 안 날 것으로 본다.
실제로 규칙성이 보이면 **`ScatterGrid.PointAt` 한 함수만 교체**해 Poisson disk로 넘어간다.
격자 계산을 `MineralScatter`로 분리하는 이유가 이 교체 지점을 좁히는 것이다.

---

## 3. 설계

### 3-1. 3단계 구조

`GenerateMinerals`를 계산과 배치로 끊는다. **rule별 독립 배치를 버리는 것이 핵심이다.**

```
[1단계 — 집계]
foreach rule:
    requested[r] = ProbabilisticRound(ExpectedCount(rule, ...))   // 난수 2개 소비, 기존과 동일
    total += requested[r]

[2단계 — 격자]
grid = ScatterGrid.Create(total, x0, y0, w, h)     // cellCount >= total
cellOrder = [0 .. cellCount-1]
Fisher-Yates 셔플 (prng)

[3단계 — 배치]
pointIndex = 0
foreach rule:
    for i in 0 .. requested[r]-1:
        cell = cellOrder[pointIndex++]
        if TryFindPositionInCell(cell) → SpawnAndCarveMineral
        else → 이 셀은 건너뜀 (광물 1개 손실)
```

**셀 순서가 섞여 있으므로 각 rule은 자동으로 청크 전역에 흩어진다.**
셔플 한 번으로 "종류별 균등"과 "전체 균등"이 동시에 성립한다 — 종류별로 따로 셔플할 필요가 없다.

현재 밀도 기준 감각: `total ≈ 510`, 사용영역 980×980 → 23×23 격자(529셀), 셀 약 43px.
광물 지지 마진이 5px이므로 여유가 충분하다.

### 3-2. `ScatterGrid` (신규, Unity 비의존)

```csharp
public struct ScatterGrid
{
    public int Cols, Rows, X0, Y0, Width, Height;
    public int CellCount => Cols * Rows;

    /// count개 이상을 담는 최소 격자. 영역 종횡비를 따라간다.
    public static ScatterGrid Create(int count, int x0, int y0, int width, int height);

    /// 셀 중심 기준 지터 적용 점. jitter 0=정확히 중심, 1=셀 전체.
    public void PointAt(int cellIndex, double rollX, double rollY, float jitter, out int x, out int y);
}
```

`Create`: `cols = ceil(sqrt(count × w / h))`, `rows = ceil(count / cols)` → `cols × rows >= count` 보장.
`PointAt`: `center + (roll - 0.5) × cellSize × jitter`, 영역 밖으로 나가지 않게 클램프.

셔플도 여기 둔다 — `ShuffleInPlace(int[] buffer, int length, System.Random prng)` (Fisher-Yates).

난수를 전부 주입받으므로 `System.Random` 없이 경계값을 직접 찍어 테스트할 수 있다.
`MineralDensity`와 같은 패턴이다.

### 3-3. 셀 안 위치 탐색

`GetRandomValidPosition`(청크 전역 20회 시도)을 `TryFindPositionInCell`(셀 내부 6회 시도)로 교체한다.
검증 조건(`IsWellSupported`, `excludedAreas`)은 **그대로**다.

**첫 시도는 설정된 지터를 쓰고, 재시도는 지터 1.0(셀 전체)로 넓힌다.**
`jitter = 0`이면 재시도가 전부 같은 중심점이 되어 무의미해지기 때문이다.
의도한 미감은 성공하는 경우에 유지되고, 실패할 때만 셀 전체를 뒤진다.

6회인 이유: 셀이 43px로 작고 갓 생성된 청크는 거의 꽉 차 있다.
그 안에서 6번 다 실패하면 셀이 실제로 빈 공간이라는 뜻이므로 더 시도할 이유가 없다.

### 3-4. 튜닝 파라미터

| 위치 | 필드 | 기본값 |
|---|---|---|
| `TileDatabaseJson` | `mineralScatterJitter` | `1.0` (0~1 클램프) |

`globalMineralDensity`와 같이 `tileData.json`에 둔다 — 재컴파일 없이 돌린다.
`1.0`은 셀 안 아무데나(자연스러움 유지, 뭉침만 제거), 낮출수록 격자에 가까워진다.

### 3-5. 파라미터 묶음 정리

`GenerateMinerals`의 인자가 밀도·로그·지터까지 붙어 9개가 된다. 구조체로 묶는다.

```csharp
public struct MineralSpawnSettings
{
    public float DensityMultiplier;   // 층 배율 × 전역 배율
    public float ScatterJitter;       // 0~1
    public bool  LogSummary;

    public static MineralSpawnSettings Default =>
        new MineralSpawnSettings { DensityMultiplier = 1f, ScatterJitter = 1f, LogSummary = false };
}
```

시그니처: `GenerateMinerals(chunk, rules, coord, worldSeed, tileType, excludedAreas, settings)`

값을 채우는 곳은 `MineralDecorator` 한 곳뿐이다.

### 3-6. GC — 정적 재사용 버퍼 필수

셀 인덱스 배열을 청크마다 새로 만들면 안 된다.
`CheckSupport`의 `new Vector2[9]`를 방금 같은 이유로 제거했다(`performance/README.md` §1.3).
청크 로드는 GC 민감 경로다(`performance/chunk-load-gc.md`).

```csharp
private static int[] s_cellOrder = new int[1024];   // 필요 시 성장, 축소 없음
private static int[] s_requested = new int[16];     // rule 개수
```

`MineralGenerator`는 데코레이션 단계에서 메인 스레드로만 호출되므로 정적 버퍼가 안전하다.

**상한 클램프**: `total`이 비정상적으로 크면 `s_cellOrder`가 무한정 커진다.
물리적으로 들어갈 수 있는 최대치는 `(w / (MINERAL_SUPPORT_MARGIN × 2)) × (h / (MINERAL_SUPPORT_MARGIN × 2))`
(= 98 × 98 ≈ 9604)이므로 여기서 자르고 경고를 1회 남긴다. 그 이상은 어차피 배치되지 않는다.

클램프가 걸리면 `requested[]`의 합이 `CellCount`를 넘는다.
3단계 배치 루프는 **`pointIndex >= grid.CellCount`이면 즉시 중단**한다.
클램프는 비정상 설정에 대한 방어이지 정상 경로가 아니므로, 남은 rule을 버리는 것으로 충분하다
(경고 로그가 원인을 알려준다).

---

## 4. 동작 변화

### 4-1. ⚠ 난수 스트림이 다시 바뀐다

배치 단계의 난수 소비 순서가 완전히 달라진다.
**아직 로드된 적 없는 청크의 광물 배치가 기존 세이브와 달라진다.**
이미 방문한 청크는 `PixelInfo` 마커로 보존되므로 영향이 없다.
`mineral-density-redesign.md` §6-1과 같은 성격의 변화다.

### 4-2. ⚠ 공동이 큰 청크에서 총 개수가 줄어든다

지금은 위치 탐색이 실패하면 청크 어디든 다시 시도해서, 결국 **남은 흙에 몰아넣는다.**
그게 뭉침의 직접 원인이다.

새 방식은 셀이 통째로 빈 공간이면 그 셀을 건너뛴다 → 광물 1개가 손실된다.
"파여 있는 공간을 빼고 남은 흙에 대해 고르게" 라는 의도대로지만, **총량은 줄어든다.**

일반 청크는 데코레이션 시점에 갓 생성돼 거의 꽉 차 있으므로 영향이 작다.
실제 손실률은 스폰 로그(`배치 N / 요청 M`)의 `%`로 확인한다.

### 4-3. 광물 간 최소 간격이 생긴다

셀당 1개이므로 두 광물이 셀 크기 이하로 붙는 경우가 크게 준다(경계를 사이에 둔 두 점 제외).
겹쳐 박히는 현상이 줄어든다 — 의도한 개선이다.

---

## 5. 알려진 한계 — 청크 경계 이음매

격자를 **청크 로컬**로 짠다. 청크마다 `total`이 다르므로(깊이·층에 따라 밀도가 다름) 셀 크기도 다르다.
따라서 인접한 두 청크의 격자가 경계에서 어긋나 광물이 붙거나 벌어질 수 있다.

정석 해법은 **월드 좌표 기준 고정 격자**다. 그러나 그러면 셀 크기가 고정돼야 하고,
그 순간 밀도 노브가 "청크당 개수"가 아니라 "셀 크기"가 된다 — `perChunk`를 다시 뒤집는 것이다.

경계는 23줄 중 한 줄이라 눈에 띄지 않을 것으로 보고 **청크 로컬로 간다.**
실제로 거슬리면 그때 월드 격자로 올린다. 이 판단을 뒤집을 근거는 "경계선이 보인다"는 육안 확인이다.

---

## 6. 영향 파일

| 파일 | 변경 |
|---|---|
| `Assets/Scripts/Gameplay/Terrain/Tiles/MineralScatter.cs` | **신규** — `ScatterGrid` + `ShuffleInPlace` |
| `Assets/Scripts/Gameplay/Terrain/Tiles/MineralGenerator.cs` | 3단계 구조로 교체, `GetRandomValidPosition` → `TryFindPositionInCell`, 정적 버퍼 |
| `Assets/Scripts/_Core/Data/TileDataModels.cs` | `TileDatabaseJson.mineralScatterJitter` |
| `Assets/Scripts/_Core/Managers/TileDataManager.cs` | `MineralScatterJitter` 노출 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/Decorators/MineralDecorator.cs` | `MineralSpawnSettings` 구성해 전달 |
| `Assets/StreamingAssets/tileData.json` | `"mineralScatterJitter": 1.0` |
| `Assets/Tests/EditMode/MineralScatterTests.cs` | **신규** |

**영향 없음**: `MineralDensity`(개수 계산은 그대로), `DiggableRock`(드랍은 배치와 무관), `MineralItemController`.

---

## 7. 검증

1. **격자 크기** — `Create(count, ...)`의 `CellCount >= count`, 그리고 필요 이상으로 크지 않을 것
2. **셀 배타성** — 서로 다른 셀 인덱스의 점이 서로 다른 셀 영역 안에 있을 것 (겹침 없음)
3. **지터 경계** — `jitter=0`이면 정확히 셀 중심, `jitter=1` + `roll` 0/1이면 셀 양 끝
4. **영역 이탈 없음** — 모든 `jitter`·`roll` 조합에서 점이 `[x0, x0+w)` 안
5. **셔플 정당성** — 같은 시드면 같은 결과, 원소 다중집합 보존(순열일 것)
6. **육안** — 뭉침·빈 구멍이 사라졌는지. 격자 규칙성이 보이는지(보이면 §2-3)
7. **손실률** — 스폰 로그 `%`가 기존 대비 크게 나빠지지 않았는지 (§4-2)

Unity Test Runner 실행은 사람이 수행한다(프로젝트 규약).

---

## 8. 하지 않는 것

| 항목 | 이유 |
|---|---|
| Poisson disk / blue noise 마스크 | §2-1, §2-2. 교체 지점은 `PointAt` 하나로 좁혀둔다 |
| 월드 좌표 고정 격자 | §5. `perChunk` 개수 노브를 뒤집는다 |
| 광맥(vein) 배치 | 균등의 정반대 방향. 탐색 재미를 원하게 되면 그때 별도 설계 |
| `IsWellSupported` 완화 | 별개 문제. 완화하면 광물이 공동에 떠서 튀어나오는 과거 버그가 재발한다 |
| 셀 건너뜀 보상(이웃 셀 재배치) | §4-2의 손실을 메우려는 것. 로그로 손실률을 먼저 보고 판단한다. 지금 넣으면 뭉침이 부분적으로 되살아난다 |
