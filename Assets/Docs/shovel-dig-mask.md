# 삽 파기 모양 — 이미지 마스크 설계

작성일: 2026-07-20
상태: 설계 확정, 구현 전

삽(`toolIndex = 1`)의 파기 모양을 코드 계산 타원에서 **이미지 알파 마스크**로 교체한다.
크기는 단일 배율(`maskScale`)로 조정하며 기존 스탯 파이프라인에 연동된다.

---

## 1. 현재 구조

파기 모양은 [`TerrainModifier.ProcessDigPixels`](../Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs) 내부에서
**마우스 방향으로 회전한 비대칭 타원**으로 계산된다.

```csharp
float localX = dx * cos - dy * sin;
float localY = dx * sin + dy * cos;
float currentScale = (localX >= 0) ? VerticalScale : 1.0f;  // 앞쪽만 1.5배
float localXScaled = localX / currentScale;
if ((localXScaled * localXScaled) + (localY * localY) <= sqrRadius) { /* 파기 */ }
```

### 크기 전파 경로

```
PlayerStat.MiningRange → Digger.digRadius
   × RadiusMultiplier (차징비율 × MiningRange × ToolRange × toolEfficiency)
   = effectiveRadius
```

`effectiveRadius`는 모양뿐 아니라 아래 6곳을 함께 결정한다. 이 스칼라를 기준 크기로 유지해야
비용·중심위치·바위판정 밸런스가 보존된다.

| 사용처 | 위치 |
|---|---|
| 파기 중심을 앞으로 밀어내는 거리 | `Digger.cs:226` |
| 실제 픽셀 판정 반경 | `TerrainModifier.cs:154` |
| indestructible 사전 CircleCast 스윕 | `Digger.cs:330` |
| 스태미나 비용 `PayCost(pos, radius)` | `Digger.cs:320` |
| 삽 MaxStamina 감소량 | `Digger.cs:350` |
| 프리뷰 UI 크기·거리 | `DigRangePreview.cs:169` |

### 회전각 출처

`ModifyTerrain` 시그니처에는 방향 인자가 없다.
[`TerrainChunk.Dig`](../Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs)가 `player.position`을 직접 읽어
`TerrainModifier.Dig`에 넘기고, 거기서 `angle`을 만든다.

→ **마스크 회전에 필요한 각도가 이미 그 자리에 있다. 시그니처 변경 불필요.**

`Digger`가 넘기는 중심(`playerPos + dir × effectiveRadius`)과
`TerrainModifier`가 역산하는 `trueCenterWorld`는 정확히 일치함을 확인했다.

---

## 2. 설계 결정

| 결정 | 선택 | 근거 |
|---|---|---|
| 회전 | **한다** | 기존 타원과 동일한 감각 유지 |
| 크기 축 | **단일 배율** | 종횡비는 이미지 자체에 담긴다. 코드 축은 중복 |
| 스탯 연동 | **한다** | `effectiveRadius`를 기준 크기로 사용 → 업그레이드·차징 감각 보존 |
| 구현 위치 | **`ProcessDigPixels` 판정식 교체** | 회전 좌표가 이미 계산되어 있음 (아래 참조) |
| 에셋 위치 | **`DigShapeSO` + static 등록** | `ToolSO`는 등급별 에셋이라 부적합 |
| 샘플링 | **최근접 이웃** | 픽셀아트 톤 유지. 경계는 어차피 `ErodeEdges`가 정리 |

### `TerrainCarver`를 쓰지 않는 이유

[`TerrainCarver.ClearHole`](../Scripts/Gameplay/Terrain/Tiles/Generation/TerrainCarver.cs)이
"마스크 알파 > 임계값인 픽셀을 air로" 처리하는 기능을 이미 갖고 있다(돌 파괴 시 사용).

그러나 **회전도 스케일도 없는 축 정렬 1:1 복사**다. 재사용하려면 회전·스케일·청크 경계 분할·
스무딩·섬 제거·indestructible 처리를 전부 새로 배선해야 하는데, `TerrainModifier.Dig`가 이미 다 하고 있다.

반면 `ProcessDigPixels`는 **이미 회전된 로컬 좌표를 계산 중**이라 판정 한 줄만 바꾸면 회전이 공짜로 따라온다.
회귀 위험이 가장 낮은 경로다.

### `ToolSO`를 쓰지 않는 이유

`ToolSO`는 도구 **등급별 에셋**이다(`level`, `price`, `icon`). 삽 1강·2강마다 별도 에셋이므로
마스크를 여기 두면 전 등급에 복제해야 하고, 하나 누락 시 그 등급만 모양이 달라진다.
또한 `TerrainModifier`는 `TerrainChunk` 내부의 순수 C# 클래스라 `ToolSO` 접근 경로가 없다
(`Dig()`가 받는 것은 `toolIndex` 정수뿐).

### static 등록을 쓰는 이유

`TerrainModifier`는 **청크마다 인스턴스**가 생기고 청크는 풀에서 재사용된다.
인스턴스 필드로 마스크를 들면 로드·풀 재사용마다 전 청크에 밀어넣어야 하며,
한 청크가 누락되면 **거기서만 원형으로 파이는** 버그가 된다.

CLAUDE.md 제약 4번(`s_colliderUpdateInterval`)이 경고하는 바로 그 유형이므로 동일한 static 패턴을 따른다.

`toolIndex`를 키로 두면 `ToolCapabilities.SwapTerrainRock`(도구 역할 스왑 유물)이 자동 연동된다.

---

## 3. 데이터 흐름

```
DigShapeSO (에셋)  ──[시작 시 1회 베이크]──▶  BakedDigShape
   Sprite  maskSprite                          byte[] Alpha (Width × Height)
   float   maskScale                           int    Width, Height
   byte    alphaThreshold                      float  MaxExtent   (실측 최대반경, 정규화)
   Vector2 pivot  (기본 0.5, 0.5)              float  Scale, Threshold
                                               Vector2 Pivot

                    TerrainModifier.SetDigShape(toolIndex, baked)   [static]
                                        │
        ┌───────────────────────────────┼───────────────────────────────┐
        ▼                               ▼                               ▼
   Digger 사전검사              두 ITerrainManager                모든 TerrainChunk
   CircleCast 반경               searchRadius                    ProcessDigPixels 판정
```

베이크를 1회로 하는 이유: 매 파기마다 `Texture2D`를 읽으면 느리고,
`TerrainCarver`가 겪는 `isReadable` 문제를 런타임마다 다시 만난다.
시작 시 한 번 `byte[]`로 굽고 최대반경도 함께 계산해 bounds 보정에 재사용한다.

---

## 4. 핵심 교체 — `ProcessDigPixels`

기존 회전 좌표(`localX`/`localY`)를 그대로 쓰고 판정만 교체한다.

```csharp
float localX = dx * cos - dy * sin;   // 기존 그대로
float localY = dx * sin + dy * cos;   // 기존 그대로

// 기존: currentScale(VerticalScale) 적용 후 distSqr <= sqrRadius
// 신규:
float halfW = radiusPx * shape.Scale;
float halfH = halfW * ((float)shape.Height / shape.Width);   // ★ 종횡비 보존

int u = (int)((localX / halfW * 0.5f + shape.Pivot.x) * shape.Width);
int v = (int)((localY / halfH * 0.5f + shape.Pivot.y) * shape.Height);

if (u < 0 || v < 0 || u >= shape.Width || v >= shape.Height) continue;
if (shape.Alpha[v * shape.Width + u] <= shape.Threshold) continue;
```

`radiusPx`가 여전히 기준 크기이므로 차징·`MiningRange`·`ToolRange` 연동이 그대로 살아있다.

### 종횡비 (필수)

`halfW`/`halfH`를 **분리하지 않으면** 비정사각 마스크가 정사각형으로 늘어난다.
100×60 이미지가 세로로 잡아늘여져 파인다. 단일 배율 설계의 전제(종횡비는 이미지가 갖는다)가 무너지므로
`halfH` 계산을 생략하면 안 된다.

### 이미지 방향 규약

`angle = 0`(오른쪽으로 파기)일 때 `localX = dx`, `localY = dy`이고,
Unity 텍스처는 아래에서 위로 행이 쌓인다. 따라서

- **이미지 오른쪽 = 파는 방향**
- **이미지 위쪽 = 월드 위쪽**

플립 보정은 불필요하다.

---

## 5. bounds 보정 (수정 4곳)

마스크는 **임의 각도로 회전**하므로, 축 정렬 반경으로 bounds를 잡으면
특정 각도에서 마스크가 bounds 밖으로 삐져나가 **잘린다.**

회전에 안전한 상한은 반대각선 `sqrt(halfW² + halfH²)`이다.
다만 이는 이미지 구석까지 포함해 헐렁하므로, **베이크 시점에 pivot→불투명 픽셀 최대 거리를 실측**한다.
알파를 어차피 한 번 훑으므로 비용이 없고, bounds가 타이트해져 픽셀 루프가 줄어든다.
실측값은 반대각선으로 상한 클램프한다.

`MaxExtent`의 단위는 **`halfW = 1`인 정규화 공간**이다. 따라서 `radiusPx`에 곱해지는 배율로 바로 쓸 수 있고,
기존 `frontScale`(= `VerticalScale`, 1.5)이 있던 자리에 그대로 들어간다.

| # | 위치 | 변경 |
|---|---|---|
| 1 | `TerrainModifier.cs:159-160` | `frontScale` → `shape.MaxExtent × shape.Scale` |
| 2 | `Digger.cs:330` | 스윕 거리의 `VERTICAL_SCALE_DEFAULT` → 최대반경 |
| 3 | `InfinityMapManager.cs:1008` | `maxScale = 2.0f` → `Max(2.0f, 최대반경)` |
| 4 | `StaticChunkTerrainManager.cs:103` | 동일 |

**매니저가 둘이다.** `ITerrainManager` 구현이 `InfinityMapManager`와 `StaticChunkTerrainManager` 두 개이며
양쪽에 같은 `maxScale = 2.0f`가 있다. 후자를 빠뜨리면 정적 청크 씬에서만 경계 잘림이 나는,
재현 조건이 까다로운 버그가 된다.

셋 다 **보수적으로 크게** 잡는다. 작으면 청크 누락으로 파기가 경계에서 잘리거나
indestructible 보호가 새지만, 크면 검사 비용만 조금 늘고 결과는 정확하다.

---

## 6. `ROCK_DIG_THRESHOLD` 재정의

삽이 rock 픽셀(`PixelInfo == 2`)을 만나면 중심 근처만 파는 규칙이 있다(`TerrainModifier.cs:343-346`).
현재는 `distSqr / sqrRadius` 비율로 판정하는데, 마스크에는 반지름 개념이 없다.

→ **pivot으로부터의 정규화 거리**로 대체한다. 의미("중심부만")가 보존되고 모양과 무관하게 동작한다.

---

## 7. 건드리지 않는 것

- `IndestructibleMask` 스킵 · 인접 보호(`IsAdjacentToIndestructible`)
- 섬 제거(`CheckFloatingIslandsInArea`) · `ErodeEdges` · `RemoveNarrowProtrusions`
- 파티클 콜백 · `OnPixelDestroyed` 이벤트
- 청크 경계 분할 — 각 청크가 동일한 `trueCenterWorld`·`angle`로 같은 마스크를 샘플링하므로 이음매 없이 맞는다

모두 판정식 **위쪽**의 스킵 로직이라 그대로 통과한다.

### `ImmediateDig`의 `VERTICAL_SCALE_DEFAULT`는 그대로 둔다

`Digger.cs:160`은 드릴 전용이며 `toolIndex = 3`으로 호출된다(`DrillStrategy.cs:258`).
마스크는 인덱스 1에만 등록되므로 **타원 폴백이 정상 동작**이다.
구현 시 "여기도 고쳐야 하나" 싶겠지만 아니다.

---

## 8. 폴백

| 상황 | 동작 |
|---|---|
| 마스크 미등록 도구(0·2·3) | 기존 타원 경로 |
| `maskSprite == null` | 기존 타원 경로 |
| `texture.isReadable == false` | 기존 타원 경로 + 베이크 시점 1회 에러 로그 |

삽 마스크 로딩이 실패해도 게임은 정상 동작한다.

---

## 9. 알려진 리스크 — 스태미나 비용 불일치

**범위 밖으로 두되, 증상이 나오면 이것이 원인이다.**

비용은 `PayCost(pos, effectiveRadius)`로 **반지름 기준**인데, 실제 파인 양은 마스크가 정한다.
마스크 면적이 원래 타원과 다르면 "너무 싸게 많이 파진다 / 적게 파진다"는 감각이 생긴다.

- 1차 대응: `maskScale`로 조정
- 정공법: 실제 파인 픽셀 수 기반 과금으로 전환 (`DigResult`에 파괴 픽셀 수 추가)

`Digger.cs:350`의 삽 MaxStamina 감소량(`shovelReductionPerRadius × effectiveRadius`)도 동일한 성격이다.

---

## 10. 구현 체크리스트

- [ ] `DigShapeSO` 정의 (sprite / scale / threshold / pivot)
- [ ] 베이크 유틸: Sprite → `byte[]` 알파 + 실측 MaxExtent, `isReadable` 검사
- [ ] `TerrainModifier.SetDigShape(toolIndex, baked)` static + 조회
- [ ] 시작 시 등록 지점 배선 (`Digger.Start` 또는 부트스트랩)
- [ ] `ProcessDigPixels` 판정 교체 (**종횡비 분리 필수**)
- [ ] `Dig` bounds → MaxExtent
- [ ] `Digger` CircleCast 스윕 → MaxExtent
- [ ] `InfinityMapManager` + `StaticChunkTerrainManager` searchRadius (**양쪽**)
- [ ] `ROCK_DIG_THRESHOLD` 정규화 거리로 재정의
- [ ] 폴백 경로 확인 (마스크 없이 곡괭이·드릴 정상 동작)
- [ ] `DigRangePreview` — **이번 범위 밖.** 프리뷰는 별도 UI 스프라이트라 실제 파기 모양과
      원래도 정확히 일치하지 않는다. 마스크 도입 후 괴리가 커지면 프리뷰 스프라이트를
      같은 이미지로 교체하는 것이 가장 간단한 대응이다.

### 검증 시나리오

1. 삽으로 8방향 파기 → 모양이 방향을 따라 회전하는가
2. 청크 경계(x·y 양쪽)에서 파기 → 이음매·잘림 없는가
3. 차징 30% / 100% → 크기만 변하고 모양은 동일한가
4. indestructible 오버레이 인접 파기 → 보호가 새지 않는가
5. 곡괭이·드릴 → 기존과 동일한가 (회귀)
6. 정적 청크 씬(`StaticChunkTerrainManager`)에서 1·2번 재확인
