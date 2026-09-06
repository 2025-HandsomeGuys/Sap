# 바위 노출 트리거 — 청크 재로드 시 자동 복원
@tags: rock, DiggableRock, reveal, RevealInTerrain, chunk-reload, neighbor-chunk, dig

## 문제

바위(`DiggableRock`)는 처음엔 땅에 묻혀 안 보이다가,
**인접 지형을 파야** 비로소 나타남.

청크를 벗어났다가 돌아오면 바위가 다시 숨겨진 상태로 리셋되고,
또 파야 보이는 문제 발생.

---

## 원인

| 시점 | 무슨 일이 일어나나 |
|------|------------------|
| 최초 스폰 | `DiggableRock.Start()` → `RevealInTerrain()` 호출 (1회) |
| 풀 반납 | `DiggableRock.OnDisable()` → `_isRevealed = false` 리셋 |
| 청크 재로드 | `DiggableRock.OnEnable()` → 바위 숨김. `RevealInTerrain()` **호출 안 함** |
| 파기 | `TerrainChunk.Dig()` → 인접 바위 `RevealInTerrain()` 호출 ← **현재 유일한 재노출 트리거** |

`Start()`는 오브젝트 생애 동안 **딱 한 번**만 실행되므로,
풀에서 재사용될 때는 다시 실행되지 않음.

---

## 수정 1 — `TerrainChunk.cs`에 메서드 추가

**파일:** `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs`

`Reuse_Step2_Finalize()` 아래 (또는 `SpawnedRocks` 프로퍼티 근처)에 추가:

```csharp
/// <summary>
/// 청크 재로드 시 이미 주변 지형이 파여 노출됐어야 할 바위를 자동 복원.
/// RevealInTerrain() 내부에서 CountExposedRockPixels()로 게이팅하므로
/// 멀쩡히 묻혀있는 바위는 자동으로 스킵됨.
/// </summary>
public void RevealExposedRocks()
{
    for (int i = _spawnedRocks.Count - 1; i >= 0; i--)
    {
        DiggableRock rock = _spawnedRocks[i];
        if (rock == null) { _spawnedRocks.RemoveAt(i); continue; }
        rock.RevealInTerrain();
    }
}
```

---

## 수정 2 — `ChunkGenerationPipeline.cs` 호출 추가

**파일:** `Assets/Scripts/Gameplay/Terrain/Tiles/Generation/Pipeline/ChunkGenerationPipeline.cs`

`ExecutePhase2_FinalizeAndDecorate()` 메서드 안, 데코레이션 완료 직후에 한 줄 추가:

```csharp
public void ExecutePhase2_FinalizeAndDecorate(Vector2Int coord, IChunk chunk)
{
    var unityObj = chunk as UnityEngine.Object;
    if (unityObj == null) return;

    if (chunk is TerrainChunk tChunk)
    {
        tChunk.Reuse_Step2_Finalize();

        bool isSpecialPrebuilt = tChunk.GetComponent<IChunkInitializer>() != null;
        if (!isSpecialPrebuilt)
            _spawner.DecorateChunk_Phase2(tChunk, coord);

        // ↓ 추가: 데코레이션(바위 스폰) 완료 후 노출 복원
        tChunk.RevealExposedRocks();
    }
}
```

> **왜 DecorateChunk_Phase2 이후인가?**
> 바위는 Phase 2 데코레이션 중에 스폰되어 `_spawnedRocks`에 등록됨.
> 그 전에 호출하면 리스트가 비어있어 아무것도 처리 안 됨.

---

## 동작 원리

```
청크 재로드
  └─ ExecutePhase2_FinalizeAndDecorate()
       ├─ Reuse_Step2_Finalize()       → SetActive(true), 콜라이더 갱신
       ├─ DecorateChunk_Phase2()       → 바위 스폰, _spawnedRocks 채움
       └─ RevealExposedRocks()         ← 추가
            └─ 각 바위 RevealInTerrain()
                 ├─ 이미 노출됨? → 스킵 (_polyCollider.enabled 체크)
                 ├─ 노출 픽셀 < minExposedPixels? → 스킵 (아직 묻혀있음)
                 └─ 조건 통과 → 바위 모양 지형 픽셀 제거 + 렌더러/콜라이더 활성화
```

기존 파기 트리거(`TerrainChunk.Dig()`)는 그대로 유지됨.
이 수정은 **추가적인 트리거**를 넣는 것이므로 기존 동작에 영향 없음.

---

## 특수 청크 고려사항

`isSpecialPrebuilt == true`인 특수 청크는 `DecorateChunk_Phase2`를 스킵하지만,
`RevealExposedRocks()`는 그 이후에 호출하므로 특수 청크 내 바위도 처리됨.
특수 청크 바위가 없으면 리스트가 비어 그냥 통과.
