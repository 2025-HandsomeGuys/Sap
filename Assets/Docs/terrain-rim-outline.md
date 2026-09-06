# 지형 최외곽 테두리(rim) — 균일 두께 단색 라인

작성일: 2026-07-13

## 목표

땅을 팠을 때 **공기와 맞닿는 최외곽 2픽셀**을 균일한 두께의 짙은 단색 라인으로 렌더한다.
현재는 이 구간의 두께가 0~2px로 요동쳐 톱니처럼 보인다.

---

## 1. 현재 구조와 문제 원인

테두리는 `VisualUpdateJob`이 `v = dist / 5` 로 `borderTexture`의 행을 골라 그린다
(`Assets/Scripts/_Core/Managers/TerrainJobs.cs` — VisualUpdateJob).

여기서 `dist`는 **half-res 거리장을 nearest 업샘플한 값**이다 (`UpsampleDistanceJob`).
배경: `Assets/Docs/half-res-distance-field.md`

이 때문에 최외곽 밴드가 세 가지 이유로 균일할 수 없다.

### 1-1. 2×2 블록 계단
`UpsampleDistanceJob`은 `hx = x >> 1` 최근접 확대다. 거리값이 2픽셀 블록 단위로 뭉텅이지므로
밴드 경계가 곧 2px 계단이 된다.

### 1-2. 표면 위치가 픽셀 패리티에 따라 흔들림
`DownsampleMaskJob`은 **"2×2 중 하나라도 air면 air"** 로 다운샘플한다.
실제 표면이 2×2 셀의 어디를 지나느냐(좌표의 짝/홀)에 따라 공기가 최대 1px 안쪽으로 부풀고,
그 부푸는 양이 위치마다 달라진다 → 톱니.

### 1-3. 최외곽 행 건너뜀
half 거리값은 5(직교)·7(대각)의 조합이라 0, 5, 7, 10, 12, 14, 15… 이고,
업샘플 ×2 후 `v = dist / 5` 하면 `0, 2, 2, 4, 4, 5, 6, 6, 7…` 이 된다.
즉 **v=1 과 v=3 은 절대 나오지 않는다** (더 깊은 홀수 행은 나온다).

문제는 이게 정확히 최외곽에서 터진다는 점이다. 가장 바깥 `v=0` 행은
"2×2 셀에 공기가 섞인 solid 픽셀"에만 걸려 두께가 0~2px로 요동치고,
그 다음은 v=1 을 건너뛰고 v=2 로 점프한다.

거기에 [오목 모서리 보정](../Scripts/_Core/Managers/TerrainJobs.cs) (`bestOrtho` 블록)이
이 값을 한 번 더 흔들어 노이즈를 더한다.

### 결론
**얇은 최외곽 라인은 절반 해상도 거리장으로는 원리적으로 표현할 수 없다.**
안쪽의 두꺼운 그라데이션 밴드에는 half-res가 충분하지만, 1~2px 라인에는 부족하다.

---

## 2. 채택한 접근 — VisualUpdateJob 안에서 full-res 직접 판정

최외곽 rim만 half-res 거리장에서 떼어내, **full-res `baseData`를 직접 보고** 판정한다.

- 새 Burst 잡 없음
- 새 NativeArray 버퍼 없음
- 새 잡 의존성 없음 → 최근 정리한 Job 파이프라인(16→11)을 건드리지 않음

### 검토 후 기각한 대안

| 대안 | 기각 이유 |
|---|---|
| 전용 `RimMaskJob` + `RimMask` 버퍼 | rim은 비주얼에만 쓰인다. 잡 +1, 청크당 영구 버퍼 +1의 값어치가 없음 (YAGNI) |
| 거리장을 full-res로 복구 | half-res 최적화(청크 로드 chamfer 스파이크 완화)를 통째로 되돌리는 것 |

---

## 3. 렌더 로직

### 3-1. 삽입 위치

`VisualUpdateJob.Execute()` 에서 **`debugDistanceField` 분기 직후, 오목 모서리 보정(`bestOrtho`) 앞**.
(공기 early-return → debug 분기 → **여기** → bestOrtho 보정 → borderTexture)

debug 시각화가 rim보다 우선이어야 등고선을 그대로 볼 수 있고,
bestOrtho 보정은 rim 판정을 흔들기만 하므로 우회한다.

```csharp
ushort rawDist = distanceField[index];   // half-res 업샘플 값 — 게이트 용도로만 쓴다

if (rimThicknessPx > 0 && rawDist <= rimGateDist)   // §3-4 게이트
{
    int t = rimThicknessPx;
    bool isEdge = px < t || px >= width - t || py < t || py >= height - t;

    // 청크 가장자리 t픽셀 이내는 원판이 OOB 로 나가므로 거리장 폴백 (§4)
    bool rim = isEdge ? (rawDist <= rimEdgeFallbackDist)
                      : IsRimDisc(px, py);           // §3-2

    if (rim)
    {
        outputTexture[index] = rimColor;
        return;                                      // rim 이 borderTexture 를 이긴다
    }
}

// ... 이하 기존 bestOrtho 보정 + borderTexture 로직 그대로
```

`IsRimDisc` 는 OOB 를 만나지 않는다 — `isEdge` 분기가 그 경우를 이미 걸러내므로
원판 스캔 안에서는 경계 검사가 불필요하다(핫 루프에서 분기 제거).

### 3-2. IsRimDisc — 유클리드 원판 스캔

```csharp
// 반경 t 안에 공기(또는 indestructible)가 하나라도 있으면 rim.
// dx² + dy² ≤ rimR2  →  t=2 일 때 13탭
// 호출 시점에 OOB 는 불가능하다 (isEdge 분기가 이미 걸러냄).
for (int dy = -t; dy <= t; dy++)
for (int dx = -t; dx <= t; dx++)
{
    if (dx*dx + dy*dy > rimR2) continue;
    int ni = (py + dy) * width + (px + dx);
    if (baseData[ni].a == 0) return true;                              // 공기
    if (pixelInfo.Length > 0 && (pixelInfo[ni] & 128) != 0) return true; // indestructible (§3-3)
}
return false;
```

**유클리드 원판이 이 설계의 핵심이다.**
5×5 정사각형(체비셰프)으로 하면 45° 경사면에서 수직 두께가 2.8px가 되어
지금과 똑같이 울퉁불퉁해 보인다. 원판이어야 경사와 무관하게 수직 두께가 t로 유지된다.

**잡 안전성 (Play 모드에서 확인할 것):** `baseData`·`pixelInfo` 는 `[ReadOnly]` 이므로
`IJobParallelFor` 에서 임의 인덱스 읽기가 허용될 **것으로 예상된다** —
기존 코드도 이미 rect-local `i` 와 다른 `index` 로 읽고 있다.

> ⚠ **EditMode 테스트로는 이걸 검증할 수 없다.** `IJobParallelForExtensions.Run(n)` 은
> 메인 스레드 순차 실행이라 parallel-for 인덱스 제약을 적용하지 않는다.
> `[NativeDisableParallelForRestriction]` 을 빠뜨려도 **EditMode 는 초록이고 Play 에서만 터진다.**
> → Play 모드에서 `InvalidOperationException` 이 없는지 반드시 직접 확인한다.
> 터지면 `baseData`/`pixelInfo` 에도 `[NativeDisableParallelForRestriction]` 을 붙인다
> (`distanceField` 가 이미 그렇게 돼 있다).

### 3-3. rim 소스 = 공기뿐 (`pixelInfo & 128` 을 쓰지 않는다)

rim 은 `baseData[ni].a == 0` 만 소스로 본다.

> ⚠️ **`pixelInfo & 128` 은 "파괴 불가" 마커가 아니다.** 이 설계 문서의 초기 버전이 그렇게 가정했고,
> 그건 틀렸다. 코드로 확인한 사실:
>
> - `& 128` 을 세팅하는 곳은 **`TerrainCarver` 두 줄뿐**이다 (`TerrainCarver.cs:91`, `:293`,
>   주석 `// Set Boundary Flag`, `// Edge Detection for Material Boundary`).
>   지형에 박아넣은 **카빙 오브젝트(바위 스프라이트)의 실루엣 외곽선** 마커다. 파괴 가능한 픽셀이다.
> - 진짜 파괴 불가는 `IndestructibleOverlayInit.cs:95` 의 `IndestructibleMask[idx] = 1` 이고
>   **`PixelInfo` 를 전혀 건드리지 않는다.** `TerrainModifier` 도 `IndestructibleMask` 로 파기를 막는다.
>
> 두 배열은 완전히 별개다.

chamfer(`ChamferForwardPassJob`)는 `& 128` 을 거리 0 시드로 써서 카빙 오브젝트 실루엣에
borderTexture 를 그린다(의도된 머티리얼 경계 표현). **rim 은 이걸 쓰면 안 된다** —
쓰면 공기가 반경 t 안에 하나도 없는데도, **땅속에 완전히 묻힌 바위마다 2px 단색 라인**이 생긴다.
"공기와 맞닿는 최외곽"이라는 rim 의 정의가 깨지고, 기존의 부드러운 그라데이션이
하드한 단색 라인으로 바뀐다.

**결과:** 카빙 오브젝트 경계는 종전대로 borderTexture 가 그린다(회귀 없음).
파괴 불가 오버레이는 `BasePixels` 에서 불투명하게 유지되는 솔리드 픽셀이므로,
그 옆을 파서 공기가 생기면 **자연스럽게 rim 이 붙는다** — 별도 처리가 필요 없다.

회귀 방지: `TerrainVisualJobRimTests.CarvedObjectBoundaryMarker_IsNotRimSource`

### 3-4. 게이트가 안전한 이유

```
rimGateDist = (t + 2) * 10     // t=2 → 40
```

게이트가 rim 픽셀을 하나라도 놓치면 라인이 끊긴다. 따라서
**"실제로 rim인 픽셀의 업샘플 dist 최댓값"** 의 상한이 필요하다.

> ⚠️ **"half-res 거리장은 과소평가만 한다"는 것은 거짓이다.** 다운샘플의 공기 팽창은
> 거리를 줄이지만 half 격자 양자화는 거리를 늘린다. 두 효과가 공존한다.
>
> 반례: P가 full x=1, 공기가 full x=4 (실제 chamfer 거리 D=15).
> half 셀 기준 P는 셀0, 공기는 셀2(full x=4,5)이고 셀1(x=2,3)은 solid.
> half 거리 = 10 → 업샘플 ×2 = **20 > 15**. 과대평가다.

**과대평가 상한.** chamfer(5,7)에서 `D = 5a + 2b` (a=max(dx,dy), b=min).
half 좌표는 `hdx ≤ ceil(dx/2)` 이므로 `ha ≤ ceil(a/2)`, `hb ≤ ceil(b/2)`.

```
upsampled = 2 · (5·ha + 2·hb) = 10·ha + 4·hb
          ≤ 10·(a+1)/2 + 4·(b+1)/2
          = (5a + 2b) + 7
          = D + 7
```

→ **업샘플 dist는 실제 거리를 최대 +7까지만 과대평가한다** (= 1.4px 상당의 격자 양자화 오차).

**적용.** rim 후보의 실제 D는 원판 r=t 안의 최대 chamfer 거리 = `5t`
(t=2일 때 오프셋 (2,0) → D=10; (1,1)은 7이라 더 작다).
따라서 rim 픽셀의 업샘플 dist ≤ `5t + 7` = t=2일 때 **17**.

게이트 `(t+2)*10 = 40` 은 이 상한의 2배 이상이다. 안전.
(t=3이면 상한 22 vs 게이트 50 — 공식이 여유를 유지한다.)

반대로 게이트는 불필요한 픽셀도 일부 통과시키지만 그래봐야 표면 근처 8px 밴드뿐이다.
→ 게이트를 통과한 픽셀만 13탭을 돈다. 1000×1000 청크에서 표면 밴드는 수천 픽셀 수준.

**게이트 상수를 바꾸려면 이 유도(`5t + 7` 상한)를 반드시 다시 확인할 것.**

---

## 4. 청크 경계 처리

`baseData`는 자기 청크 1000×1000뿐이므로, 원판이 청크 밖으로 나가면 이웃 픽셀을 볼 수 없다.

### 왜 이웃 BasePixels를 그냥 넘기지 않는가

배관 문제가 아니라 **성능 문제**다.

`TerrainChunk.Dig` → `EnsureJobsCompleted()` 는 **자기 청크의 잡만** 완료시킨다.
우리 VisualJob이 이웃의 `BasePixels`를 `[ReadOnly]` 로 읽는다면,
이웃을 팔 때 그쪽이 우리 VisualJob 완료를 기다려야 안전하다
(`RegisterDistanceFieldReader` 와 동일한 external-reader 등록이 필요한 이유).

즉 **seam 근처를 팔 때마다 최대 4개 이웃의 VisualJob에 동기화 포인트가 새로 생긴다.**
이건 Job 파이프라인 최적화(`Assets/Docs/job-pipeline-waste-removal.md`)로 없앤 바로 그 종류의 스톨이다.
드릴 연속 파기는 매 FixedUpdate마다 일어나므로 비용이 누적된다.

→ **Phase 2는 "나중에 하면 되는 개선"이 아니라 웬만하면 하지 말아야 할 것이다.**

### Phase 1 — 경계 폴백 (채택)

가장자리 t픽셀 이내(`px < t || px >= width-t || py < t || py >= height-t`)의 픽셀은
원판 스캔을 포기하고, **BoundarySync가 이웃과 이어놓은 `distanceField`** 로 판정한다.

```csharp
rim = rawDist <= rimEdgeFallbackDist;   // = t * 5  (t=2 → 10)
```

**이 임계값은 유도된 값이다.** `BoundarySyncJob` 은 half 격자에서 돌고
(`SYNC_DEPTH = 25` half-cells = 50 full px), 전파 비용은 `candidate = bestEdge + BOUNDARY_COST + k*5`,
`BOUNDARY_COST = 5` = half 격자 1스텝이다. 이웃 edge 셀이 air(dist 0)일 때:

| half cell k | half dist | full dist (×2) | 해당 full px | 실제 공기까지 | `≤ 10` 판정 |
|---|---|---|---|---|---|
| 0 | `0 + 5 + 0` = 5 | 10 | 0, 1 | 1px, 2px | **rim** ✅ |
| 1 | `0 + 5 + 5` = 10 | 20 | 2, 3 | 3px, 4px | 제외 ✅ |

→ t=2에서 정확히 2px가 나온다. `BOUNDARY_COST` 가 half 1스텝이라 성립하는 것이므로,
**그 상수가 바뀌면 이 표를 다시 계산할 것.**

지표면이 seam 근처에서 대각선일 때는 half 양자화 때문에 정확도가 떨어지지만,
폴백 영역 자체가 2px 폭이라 영향이 제한된다.

**폴백과 §3-3 의 불일치 — 실제로는 도달 불가.**
내부는 "공기만" 을 rim 소스로 보지만(§3-3), 폴백이 쓰는 `distanceField` 는 chamfer 가
`pixelInfo & 128`(카빙 오브젝트 실루엣)로도 시드한 값이다. 따라서 **청크 가장자리 2px 안에
카빙 오브젝트가 있으면** 공기가 없어도 rim 이 생긴다 — 원칙적으로는 불일치다.

그러나 `RockDecorator.cs:37` 이 `padding = Clamp(textureThickness × PPU, 5, 50) + 5 = 55px` 로
바위를 생성하므로 **카빙 오브젝트는 청크 가장자리 55px 안으로 들어오지 않는다.**
폴백 strip 은 2px 이므로 닿을 수 없다. (`textureThickness` 를 크게 줄이거나 padding 규칙을
바꾸면 이 논거가 깨지므로, 그때 재확인할 것.)

**보너스:** `isSkyAbove`(cy==0 이고 위쪽 이웃 없음 → 거리 `BOUNDARY_COST`) 덕분에
**최상단 청크의 하늘 경계도 이 폴백이 그대로 처리한다.** 별도 분기 불필요.

- OOB 탭을 solid로 간주하는 방법도 있으나, 그러면 seam에서 rim이 **끊긴다**. 기각.
  (끊긴 라인이 두꺼운 라인보다 훨씬 눈에 띈다.)

### Phase 2 — 원칙적으로 하지 않음

seam 아티팩트가 실제 플레이에서 명확히 거슬릴 때만 재검토한다.
그때도 위의 동기화 비용을 먼저 측정할 것.

---

## 5. 파라미터

`Assets/StreamingAssets/worldSettings.json` 의 `chunk` 섹션 (`textureThickness` 옆):

```json
"chunk": {
  ...
  "textureThickness": 4.0,
  "rimThicknessPx": 2,
  "rimColor": "#241009"
}
```

### 5-1. JsonUtility 제약

`worldSettings.json` 은 `JsonUtility.FromJson<WorldSettingsData>` 로 파싱된다.
**`JsonUtility` 는 hex 문자열을 `Color32` 로 역직렬화하지 못한다.**

```csharp
// WorldSettingsData.ChunkSection  ← 클래스명 주의 (ChunkSettings 아님)
public int    rimThicknessPx = 2;
public string rimColor       = "#241009";   // ⚠ string 이어야 한다. Color32 불가
```

파싱은 적용 시점에 `ColorUtility.TryParseHtmlString(s.chunk.rimColor, out Color c)` 로 한다.
실패 시 기본색으로 폴백하고 경고 1회.

`JsonUtility` 는 json에 없는 필드를 기본값으로 두고 모르는 필드는 무시하므로,
기존 `worldSettings.json` 을 안 고쳐도 동작한다(하위 호환).

### 5-2. 전달 경로 — TerrainVisualizer 를 거치지 않는다

`ChunkJobScheduler.ScheduleVisualJob()` 은 **이미 `TerrainChunk` 의 static 을 직접 읽는다**:

```csharp
debugDistanceField = TerrainChunk.DebugDistanceField,   // ChunkJobScheduler.cs:198
```

rim 도 이 선례를 그대로 따른다. 덕분에 `ScheduleVisualJob` 시그니처를 바꿀 필요도,
`TerrainVisualizer` 의 **3개 호출부**(`texPx` 를 각자 계산하는 곳)를 고칠 필요도 없다.

```
worldSettings.json  (chunk.rimThicknessPx / chunk.rimColor)
  └─ WorldSettingsData.ChunkSection
       └─ InfinityMapManager.ApplySettings()          // ← SetDefaultColliderUpdateInterval 바로 옆 (InfinityMapManager.cs:323)
            └─ TerrainChunk.SetRimSettings(px, color) // → static s_rimThicknessPx, s_rimColor
                 └─ ChunkJobScheduler.ScheduleVisualJob()   // static 직접 읽기 — 배관 없음
                      └─ VisualUpdateJob.rimThicknessPx / rimR2 / rimGateDist / rimEdgeFallbackDist / rimColor
```

`rimGateDist`(§3-4), `rimEdgeFallbackDist`(§4), `rimR2 = rimThicknessPx²` 는 모두
`rimThicknessPx` 에서 유도되므로 json에 노출하지 않는다.
`ScheduleVisualJob` 안에서 계산해 잡에 넘긴다 (`rimR2` 는 원판 판정에서 루프마다 곱하지 않기 위한 사전 계산값).

`rimThicknessPx: 0` → rim 완전 비활성(기존 동작 그대로).

**주의:** non-serialized 필드는 `Instantiate` 시 프리팹에서 복사되지 않으므로 반드시 static이어야 한다
(CLAUDE.md 아키텍처 제약 §4).

---

## 6. borderTexture와의 관계

rim이 이기고 즉시 `return` 하므로:

- 최외곽 2px = 짙은 단색 rim
- 그 안쪽부터 기존 `borderTexture` 밴드가 그대로 이어짐
- rim에 가려지는 건 `borderTexture` 의 **최외곽 2~3행** (v=0, 그리고 v=2 행의 일부).
  §1-3에서 봤듯 v=1은 원래부터 샘플되지 않았고, rim 바깥 첫 픽셀이 v=2 또는 v=4를 받으므로
  실제로 보이기 시작하는 행은 v=2 근처다.

아트 에셋은 수정하지 않는다. rim만 위에 얹는 형태다.

---

## 7. 검증

1. `rimColor`를 형광색(`#FF00FF`)으로 바꿔 육안 확인 — 평지 / 45° 경사 / 오목 코너 / 볼록 코너
   모두에서 두께가 2px인지.
2. EditMode 테스트: `TerrainVisualJob` 을 `.Run()` 으로 직접 호출.
   인공 마스크(평면·단일 공기·카빙마커·가장자리)에 대해 rim 판정이 기대치와 일치하는지 검사.
   (테스트 실행은 사람이 직접 — CLAUDE.md 테스트 규약)
   **단, 이 테스트는 job safety 를 검증하지 못한다** (§3-2 경고 참고).
3. Play 모드에서 `InvalidOperationException` 이 없는지 — EditMode 로 대체 불가.
4. 청크 seam 위에서 rim이 끊기지 않는지 확인 (Phase 2 필요 여부 판단).

---

## 8. 변경 파일

| 파일 | 변경 |
|---|---|
| `Assets/StreamingAssets/worldSettings.json` | `chunk.rimThicknessPx`, `chunk.rimColor` 추가 |
| `Assets/Scripts/_Core/Data/WorldSettingsData.cs` | `ChunkSection` 에 두 필드 추가 (`rimColor` 는 **string**) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs` | `ApplySettings()` 에 `TerrainChunk.SetRimSettings()` 호출 1줄 (`:323` 옆) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` | static `s_rimThicknessPx` / `s_rimColor` + `SetRimSettings()` + public getter |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs` | `ScheduleVisualJob()` 에서 static 읽어 잡 필드 세팅 (시그니처 변경 없음) + 잡 개명 반영 |
| `Assets/Scripts/_Core/Managers/TerrainJobs.cs` | `VisualUpdateJob` → **`TerrainVisualJob` 개명** + rim 필드 5개 + `IsRimDisc()` + Execute 분기 |

> **개명은 필수 절차다.** Burst 는 잡 struct 의 **필드가 바뀌면 옛 커널을 재사용**할 수 있고
> **Unity 재시작으로도 안 풀린다.** 이 프로젝트는 여기에 2시간을 날렸고
> (`InitBFSJob` → `InitDistanceFieldJob` 개명이 그 해법이었다), rim 은 필드를 5개 추가한다.
> 이름을 바꿔 Burst 캐시 엔트리를 새로 만든다.

`TerrainVisualizer.cs` 는 **건드리지 않는다** (§5-2).

---

## 9. 리스크

| 리스크 | 대응 |
|---|---|
| 게이트 상수가 작으면 rim이 끊긴다 | 상한 `5t + 7` (§3-4 유도) 대비 `(t+2)*10` 은 2배 이상 여유. **값 변경 시 유도를 반드시 재확인** |
| `rimThicknessPx` 를 키우면 탭 수가 (2t+1)² 로 증가 | 2~3px 용도로 제한. 그 이상이 필요하면 전용 잡으로 분리 |
| 청크 seam 2px strip의 계단 | Phase 1의 의도된 한계. Phase 2는 동기화 비용 때문에 원칙적으로 하지 않음 (§4) |
| `pixelInfo & 128` 을 rim 소스로 되돌림 | 그건 파괴 불가가 아니라 **카빙 오브젝트 실루엣 마커**다. 되돌리면 묻힌 바위마다 단색 라인이 생긴다 (§3-3). `CarvedObjectBoundaryMarker_IsNotRimSource` 가 막는다 |
| `rimColor` 를 `Color32` 필드로 선언 | `JsonUtility` 가 파싱 못 한다. 반드시 `string` + `ColorUtility.TryParseHtmlString` (§5-1) |
| `rendering.enableRound1Preview = true` 로 켜면 seam rim이 1프레임 튄다 | Round 1 Visual 은 BoundarySync 이전이라 §4 폴백의 거리장이 아직 이웃과 안 이어져 있다. 기본값 `false` 라 현재는 무해 — 켤 일이 생기면 재확인 |
| **Burst 스테일 커널** — rim 필드를 추가해도 옛 커널이 돌아 필드가 무시된다 | 잡 struct 를 `TerrainVisualJob` 으로 개명해 캐시 엔트리를 새로 만든다 (§8). 재시작으로는 안 풀린다 |
| EditMode 그린을 job safety 의 증거로 오인 | `.Run(n)` 은 parallel-for 제약을 적용하지 않는다. Play 모드 확인 필수 (§3-2, §7-3) |
