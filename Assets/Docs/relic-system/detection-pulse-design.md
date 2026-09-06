# 탐지파동(DetectionPulse) 유물 설계 — RelicID 4021

특수청크를 **로딩 범위 밖까지** 온디맨드로 탐지하는 액티브 유물. 발동 시 플레이어 주위로 파동이
퍼지며 특수청크 위치를 미니맵·전체지도에 마커로 남긴다. 기존 방향 화살표 나침반을 대체한다.

관련 문서: `adding-a-relic.md`(유물 추가 진입점), `design.md`(유물 전체 설계).

---

## 1. 핵심 요구사항 (확정)

| 항목 | 결정 |
|------|------|
| 탐지 범위 | 청크 좌표 기준 반경 **4칸**(레벨 1~3: 4/5/6). 화면보다 넓음 |
| 탐지 방식 | **결정론적 예측** — 로드 안 된 청크도 탐지 |
| 탐지 대상 | **모든 특수청크** 앵커 (타입 무관) |
| 방문 여부 | 세이브 데이터로 판정. 미방문=밝게 / 클리어=흐리게 |
| 표시 (주변) | **미니맵**(`UndergroundMinimap`) 마커 |
| 표시 (전역) | **전체지도**(`WorldMapOverlay`) 마커 — **영구 저장** |
| 발동 | 액티브 Q/R, 쿨타임 **60초 고정**, 즉발(Duration=0) |
| 파동 연출 | 월드 링 VFX + 걸린 청크 위 순간 핑 |
| 화살표 나침반 | **제거** — `HeadCompassArrow` 삭제, 항상-켜짐 자동 스캔 폐지 |

---

## 2. 아키텍처

```
DetectionPulseRelic (액티브, 쿨 60s)
   └─ OnActivate
        ├─ 1) 예측 스캔: SpecialChunkManager.PredictAnchorsInRadius(origin, radius, out list)
        ├─ 2) 각 앵커 방문여부 판정 → DetectedChunkStore.Report(coord, type, visited)
        │        └─→ WorldMapOverlay / UndergroundMinimap (마커 레이어, Store 구독)
        └─ 3) PulseVFX 코루틴 스폰 (ctx.runner) — 월드 파동 링 + 핑
```

**단위 책임**
- `DetectionPulseRelic` — 유물 훅. 발동 트리거·파라미터·VFX 소유. 탐지 로직은 매니저에 위임.
- `SpecialChunkManager.PredictAnchorsInRadius` — 결정론적 예측 산출. 내부 `_selector`/`_registry` 래핑.
- `DetectedChunkStore` — 발견 좌표의 단일 진실 소스(좌표→타입·방문여부). 뷰가 구독. 세이브 연동.
- `WorldMapOverlay` / `UndergroundMinimap` — Store를 읽어 마커만 그림. 탐지 로직 없음.
- `PulseVFX` — 코드 생성 월드 연출. 게임플레이 영향 없음.

---

## 3. 탐지 코어 — 결정론적 예측

`SpecialChunkManager`에 공개 API 신설:

```csharp
public void PredictAnchorsInRadius(
    Vector2Int origin, int radius,
    List<(Vector2Int coord, SpecialChunkType type)> results)
{
    results.Clear();
    int seed = InfinityMapManager.Instance != null ? InfinityMapManager.Instance.worldSeed : 0;
    for (int dy = -radius; dy <= radius; dy++)
    for (int dx = -radius; dx <= radius; dx++)
    {
        var coord = new Vector2Int(origin.x + dx, origin.y + dy);
        var layer = TileDataManager.Instance.GetTileTypeAtPosition(coord.x, coord.y);
        var def = _selector.TrySelect(coord, layer, seed, _registry);
        if (def != null) results.Add((coord, def.Value.chunkType));
    }
}
```

**왜 정확한가**: `_selector.TrySelect`는 청크 생성 파이프라인(`ChunkDataProvider` →
`SpawnSpecialChunkIfPossible`)이 **실제로 스폰 여부를 판정하는 바로 그 함수**다. 좌표·레이어·시드만
입력이므로 로드 여부와 무관하게 동일 결과를 낸다.

**언로드 영역 정확도**: `TrySelect`의 충돌 배제(exclusion)는 `_registry`(실제 스폰된 서브청크)를
참조하지만, 언로드 영역은 레지스트리가 비어 결정론적 해시 타이브레이크(`neighborHash > myHash`)로
폴백한다. 실제 스폰 시 미세한 차이가 날 수 있으나(레지스트리 상태 의존 경계 케이스) 나침반 정확도로 충분.

**성능**: 반경 4 → 9×9 = 81 coord. 각 `TrySelect`는 소형 PRNG + `minChunkSpacing`(≈2) 배제 루프.
쿨타임 60초당 1회 → 무시 가능.

**레이어 풀 제약**: 특정 레이어에 특수청크 풀이 없으면 `TrySelectRaw`가 null 반환(정상). 반경이
레이어 경계를 걸쳐도 각 coord가 자기 레이어로 조회되므로 안전.

---

## 4. 방문 여부 판정

**소유 구조(코드 확인됨)**: `WorldPersistenceSystem`은 싱글톤이 아니다. `InfinityMapManager`가
private `_persistenceSystem` 필드로 소유하고([InfinityMapManager.cs:45,249]), `ChunkDataProvider`에
주입한다. `SpecialChunkManager`는 이걸 갖고 있지 않다.

→ **`InfinityMapManager`에 공개 래퍼 신설**:

```csharp
// InfinityMapManager
public bool IsChunkVisited(Vector2Int coord)
    => _persistenceSystem != null
       && _persistenceSystem.TryGetChunkData(coord, out _);
```

- 앵커 좌표에 저장된 청크 데이터가 있으면 = 플레이어가 방문·수정함 → `visited=true`.
- 지도 마커는 영구로 남되 `visited` 플래그로 시각 구분(미방문 밝게 / 클리어 흐리게).
- 유물은 `InfinityMapManager.Instance.IsChunkVisited(coord)`로 호출.

---

## 5. DetectedChunkStore (신규)

`Assets/Scripts/Systems/Compass/DetectedChunkStore.cs`

```csharp
public struct DetectedChunk { public SpecialChunkType type; public bool visited; }

public class DetectedChunkStore   // 싱글톤 (씬 상주 or 코드 부트스트랩)
{
    // 좌표 → 발견 정보. 재보고 시 갱신(visited 최신화).
    IReadOnlyDictionary<Vector2Int, DetectedChunk> All { get; }
    event Action OnChanged;                       // 뷰 갱신 트리거
    void Report(Vector2Int coord, SpecialChunkType type, bool visited);
    void MarkVisited(Vector2Int coord);           // 청크 진입/파기 시 상태 승격(선택)
    // 세이브 연동
    IEnumerable<...> Capture();  void Apply(...);
}
```

- 파동 결과를 누적. 재발동 시 같은 좌표는 갱신, 신규는 추가.
- **저장**: `PlayerData.detectedChunks`(신규 필드) ↔ `SaveManager`가 `coinSave`/`relicSave`와
  동일 패턴(Capture/Apply, hasData 플래그)으로 연동. 재접속에도 영구.

---

## 6. 지도 마커 렌더링

두 뷰는 마커 배치 방식이 **다르다**(코드 확인됨). 각각에 맞는 신규 마커 레이어를 추가한다.

### 6-1. 전체지도 `WorldMapOverlay` — 기존 변환 재사용
플레이어 마커가 이미 월드→로컬 변환으로 배치된다(`UpdateMarker()`: `(p - _center) * scale`,
화면 밖이면 가장자리 클램프). 이 변환을 그대로 써서 `UpdateDetectedMarkers()`를 추가.
- Store를 순회하며 자식 `Image` 마커 풀(재사용 리스트)로 그림. 열려 있을 때만 갱신. 영구 표시.

### 6-2. 미니맵 `UndergroundMinimap` — 신규 마커 레이어 필요
미니맵은 **플레이어가 중앙 고정**이고 지형이 `_mapTex`에 베이크되는 구조라
([UndergroundMinimap.cs:213-220]) 오프센터 마커용 변환이 **없다**. 새로 만든다:
- 각 Store 항목: `local = (chunkWorld - playerWorld) * pxPerWorld`. `pxPerWorld`는 미니맵이 지형을
  텍스처에 그릴 때 쓰는 월드→텍셀 스케일에서 도출(구현 시 해당 상수 확인).
- 원형 반경 밖이면 스킵하거나 테두리에 클램프(방향 힌트). `_root` 자식으로 `Image` 마커 배치.
- 반경 내에서만 보이므로 "플레이어 주변" 채널 역할. 마커 풀 재사용.

### 6-3. 공통
- 마커 스프라이트: 코드 생성(기존 `MakeDiamondSprite` 재사용 가능). `visited`면 알파 낮춤.
- (선택) 타입별 색은 후속 — 이번 스코프는 단일 아이콘 + 방문 알파 구분.

---

## 7. 파동 VFX (월드, "플레이어 주변")

`ctx.runner`로 코루틴 스폰. 기존 Anvil/Steroid 충격파 링(LineRenderer 코드 생성, 월드 배치,
`OnUpdate`로 확장·페이드) 패턴 재사용.

- 플레이어 중심 링 반경 0 → `radius*chunkWorldSize`(≈40유닛)로 ~0.8초 확장 + 페이드아웃.
- 링 현재 반경이 각 발견 청크까지 거리를 지나는 순간, 그 월드 좌표 위에 순간 핑 아이콘(월드
  스프라이트) 팝 + 페이드. 조준점은 `CompassPoi` 있으면 그 지점, 없으면 청크 중심.
- 게임플레이 영향 없음(순수 연출). 코루틴 종료 시 자동 정리.

---

## 8. 유물 파라미터

```csharp
[SerializeField] int[]   radiusPerLevel   = { 4, 5, 6 };   // 청크 좌표 반경
[SerializeField] float   cooldown         = 60f;           // 고정
public override float GetCooldown() => cooldown;
public override float GetDuration() => 0f;                 // 즉발
```

---

## 9. 화살표 나침반 대체 (제거 범위)

`Systems/Compass/`는 5파일 자기완결(외부 참조 없음, `SpecialChunkManager`만 의존):

- **제거**: `HeadCompassArrow.cs`(방향 화살표 뷰), `SpecialChunkCompass.cs`의 항상-켜짐 자동 스캔.
- **재사용/유지**: `CompassPoi.cs`(프리팹 조준점 마커 — 마커 위치 산출에 계속 사용),
  `CompassTarget`/`ISpecialChunkLocator`/`ActiveAnchorLocator`(예측·좌표 헬퍼로 재활용 가능).
- 씬에서 `HeadCompassArrow`/`SpecialChunkCompass` 오브젝트 제거는 사용자가 Unity에서 수행.

---

## 10. 수정·신규 파일

**신규**
- `Assets/Scripts/Gameplay/Relics/Behaviours/DetectionPulseRelic.cs`
- `Assets/Scripts/Systems/Compass/DetectedChunkStore.cs`
- 파동 VFX (DetectionPulseRelic 내부 or `Systems/Compass/DetectionPulseVFX.cs`)

**수정**
- `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunkManager.cs` — `PredictAnchorsInRadius`
- `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs` — `IsChunkVisited` 공개 래퍼
  (partial class — `.cs`/`.Data.cs` 양쪽 확인)
- `Assets/Scripts/UI/Player/WorldMapOverlay.cs` — 발견 마커 레이어(기존 변환 재사용)
- `Assets/Scripts/UI/Player/UndergroundMinimap.cs` — 발견 마커 레이어(신규 오프센터 변환)
- `Assets/Scripts/Gameplay/Relics/Data/RelicID.cs` — `DetectionPulse = 4021`
- `Assets/Scripts/Editor/RelicSliceAssetGenerator.cs` — `CreateRelic(...Active, new DetectionPulseRelic())` + db 등록
- `Assets/Scripts/Gameplay/Relics/Debug/RelicDebugGranter.cs` — 빈 F키(F1~F3/F5 중) 바인딩
- `PlayerData` + `SaveManager` — `detectedChunks` 저장 필드 + Capture/Apply

**삭제**
- `Assets/Scripts/Systems/Compass/HeadCompassArrow.cs`
- (`SpecialChunkCompass.cs`는 자동 스캔 로직 제거 — 파일 삭제 or 헬퍼로 축소, 구현 시 확정)

---

## 11. 리스크·미결

- **예측 vs 실제 스폰 경계 오차**: 언로드 영역에서 exclusion 레지스트리 미반영으로 극소수 경계
  케이스 오탐 가능. 나침반 용도라 허용. (필요 시 방문 시점에 Store를 실제 앵커로 보정)
- **방문 판정 정확도**: "세이브 데이터 존재 = 방문"은 부분 파기도 방문으로 봄. 의도상 OK.
- **타입별 아이콘 구분**: 이번 스코프 제외(후속).
- **미니맵 마커 밀도**: 반경 4로 좁아 과밀 우려 낮음.
- **미니맵 스케일 상수**: 오프센터 변환용 `pxPerWorld`를 미니맵 렌더 코드에서 정확히 도출해야 함
  (지형 텍스처 스케일과 일치시키지 않으면 마커가 지형과 어긋남). 구현 시 최우선 확인.
- **버전 관리**: 이 프로젝트는 UVCS. git 커밋 안 함 — 체크인은 사용자가 수행.
