# 광물 생명주기 — 청크 언로딩과 풀 재사용
@tags: mineral, chunk-lifecycle, MineralGenerator, pooling, MineralLifetime, unload, WorldPersistenceSystem

> 분석 기준 코드: `MineralGenerator.cs`, `ChunkSpawner.cs`, `ChunkPool.cs`, `WorldPersistenceSystem.cs`, `InfinityMapManager.cs`, `MineralLifetime.cs`

---

## 1. 광물 스폰 흐름

```
ChunkSpawner.DecorateChunk_Phase2_Steps()
└── MineralDecorator.Decorate()
    └── MineralGenerator.GenerateMinerals()
        └── SpawnAndCarveMineral(x, y)
            ├── SpawnMineralObject()      → GameObject를 chunk.transform 자식으로 생성
            └── MarkPixelAsMineral()      → PixelInfo[idx] = 3 마킹
```

광물 오브젝트 이름 규격: `MINERAL_{SOName}_{x}_{y}` (풀 반납·좌표 파싱에 사용)

결정론적 시드: `seed = (coord.x * 73856093) ^ (coord.y * 19349663) ^ worldSeed`  
→ 동일 좌표이면 항상 동일 위치에 동일 광물 생성

---

## 2. PixelInfo 인코딩

| 값 | 의미 |
|----|------|
| 0 | Air (빈 픽셀) |
| 1 | Dirt (일반 지형) |
| 2 | Rock (암석) |
| 3 | `MINERAL_PIXEL_TYPE` — 광물이 이 픽셀에 존재 |
| 4 | `MINERAL_COLLECTED_PIXEL_TYPE` — 수집 완료 또는 청크 분리로 이미 월드에 존재. 재로드 시 스폰 금지 |

---

## 3. 광물 오브젝트 풀

```csharp
// MineralGenerator
private static Dictionary<string, Queue<GameObject>> _mineralPools;
// Key = MineralSO.name (Gold_MineralSO 등)
```

- **정적(static)** — 세션 전체에서 공유
- 반납: `MineralGenerator.ReturnToPool()` → `SetActive(false)` + 큐에 넣음
- 꺼내기: `SpawnMineralObject()` → `pool.Dequeue()` 후 null 체크 → 없으면 `Instantiate`

---

## 4. 청크 언로드 흐름

```
DoUnloadChunk(coord)
├── DetachMineralsToWorld(tcPool)          ← 저장 전에 실행
│   └── 활성 MINERAL_* 자식마다:
│       ├── MineralGenerator.MarkMineralCollected() → PixelInfo[idx] = 4
│       ├── child.SetParent(null, true)   → 월드 오브젝트로 분리
│       └── AddComponent<MineralLifetime> → 60초 타이머 시작
├── [hasBeenModified=true 청크만] SaveChunkDataToMemory()
│   └── PixelInfo=4 마커가 저장에 반영됨
└── _chunkPool.Return(chunk)
    └── chunk.gameObject.SetActive(false)
```

### 풀이 가득 찰 경우 (MAX_POOL_SIZE = 32)

`Destroy(chunk.gameObject)` 시 이미 분리된 광물은 영향 없음. 분리 전에 `DetachMineralsToWorld`가 실행되므로 소멸 경로에서 광물 손실 없음.

---

## 5. MineralLifetime — 자동 소멸 타이머

```csharp
// MineralLifetime.cs (MonoBehaviour)
// 위치: Assets/Scripts/UI/Items/Minerals/MineralLifetime.cs

private void OnEnable()
{
    StopAllCoroutines();
    if (GetComponentInParent<TerrainChunk>() != null) return; // 지형 내장 상태 → 타이머 안 걸음
    StartCoroutine(DespawnCoroutine()); // 60초 후 MineralGenerator.ReturnToPool()
}
```

**타이머가 시작되는 조건**: 부모가 TerrainChunk가 아닌 모든 활성화 시점

| 경우 | 타이머 동작 |
|------|-------------|
| TerrainChunk 자식으로 스폰 (지형 내장) | ❌ 시작 안 함 |
| 청크 언로드 → `DetachMineralsToWorld`로 분리 | ✅ 60초 시작 |
| `DiggableRock.TryDropMineral()` 드롭 | ✅ 60초 시작 (Instantiate 직후 `AddComponent`) |
| `_mineralPools`에서 재활성화 → TerrainChunk 자식 | ❌ 시작 안 함 (부모 체크) |

60초 경과 시 → `MineralGenerator.ReturnToPool()` → `_mineralPools`에 반납 후 `SetActive(false)`.

---

## 6. 청크 재사용 시 광물 처리

풀에서 꺼낸 청크가 Phase 2 장식 단계에 진입하면:

```csharp
// ChunkSpawner.DecorateChunk_Phase2_Steps()
for (int i = chunk.transform.childCount - 1; i >= 0; i--)
{
    GameObject child = chunk.transform.GetChild(i).gameObject;
    if (child.name.StartsWith("MINERAL_"))
        MineralGenerator.ReturnToPool(child);   // _mineralPools에 반납
    else if (child.name.StartsWith("ROCK_"))
        RockSpawner.ReturnToPool(child);
    else
        Destroy(child);
}
```

`DetachMineralsToWorld`가 언로드 시 분리했으므로 재사용 청크에 남은 `MINERAL_*` 자식은 없다. 루프는 신규 생성 전 방어적 정리 역할.

---

## 7. 저장/로드와 광물

`ChunkSaveData`에 저장되는 것:

| 항목 | 저장 여부 |
|------|-----------|
| `modifiedPixels` (지형 픽셀) | ✅ |
| `pixelInfo` (광물 마커 포함, 값 3·4) | ✅ |
| `savedRocks` (암석 위치·HP) | ✅ |
| 광물 GameObject 위치·종류 | ❌ |

광물 오브젝트는 저장되지 않는다. 재로드 시 `MineralDecorator`가 시드로 재생성하되 `PixelInfo=4`인 위치는 건너뜀.

---

## 8. 수집/소멸 경로별 재로드 동작

| 상황 | PixelInfo | 재로드 결과 |
|------|-----------|-------------|
| 미수정 청크, 광물 미수집 | 3 | 시드로 동일 위치 재생성 ✅ |
| 광물 E키 수집 (`PickupableItem`) | 4 (수집 시 마킹) | 재스폰 안 함 ✅ |
| 청크 언로드로 분리 → 60초 내 수집 | 4 (분리 시 마킹) | 재스폰 안 함 ✅ |
| 청크 언로드로 분리 → 60초 경과 소멸 | 4 (저장됨) | 재스폰 안 함 (영구 소멸) |
| 파기로 픽셀 제거 (`TerrainModifier`) | 0 (파기 시 초기화) | `BasePixels.a=0` → 재스폰 안 함 ✅ |

> **설계 의도**: 분리된 광물은 60초 안에 수집하지 않으면 영구 소멸한다. 재방문 시 해당 위치에 광물이 다시 나타나지 않는다.

---

## 9. 흐름 요약 다이어그램

```
[광물 스폰] Phase 2 Decorate
    ↓
chunk.transform 자식으로 부착 (PixelInfo=3)
    ↓
[청크 언로드] DoUnloadChunk
    └── DetachMineralsToWorld
        ├── PixelInfo=4 마킹
        ├── SetParent(null) → 월드 오브젝트
        └── MineralLifetime 부착 → 60초 타이머
    ↓
[60초 내 수집] PickupableItem.Interact
    └── NotifyMineralCollected (이미 PixelInfo=4) → 인벤토리 추가
[60초 경과] MineralLifetime.DespawnCoroutine
    └── MineralGenerator.ReturnToPool → _mineralPools

[청크 재로드] MineralDecorator.Decorate
    └── GenerateMinerals → IsGroundPixel
        ├── BasePixels.a == 0 → 스킵
        ├── PixelInfo == 4   → 스킵
        └── 그 외            → 광물 스폰
```

---

## 10. 주의점

### ① 분리 광물의 영구 소멸
분리 시점에 `PixelInfo=4`가 저장되므로, 60초 내 미수집 광물은 이후 재방문해도 다시 나타나지 않는다. 의도된 설계이나 플레이어에게 명확한 피드백이 없으면 혼란스러울 수 있다.

### ② `_mineralPools`의 null 방어
`SpawnMineralObject`에서 `pool.Dequeue()` 후 `if (pooledObj != null)` 체크로 씬 전환 등으로 소멸된 오브젝트를 정상 처리한다.

### ③ 풀 한도 초과 시 광물 재활용
청크 Destroy 경로에서도 `DetachMineralsToWorld`가 선행되므로, 광물은 `_mineralPools`에 반납되거나 타이머로 반납 대기 중이다. 청크 Destroy 자체로 광물이 즉시 소멸되는 경우는 없다.
