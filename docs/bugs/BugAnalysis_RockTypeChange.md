# 버그 분석: 청크 재로드 시 암석 종류/위치 변경
@tags: bug, rock, DiggableRock, type-change, chunk-reload, SpawnedRocks, worldSeed

> 작성일: 2026-03-17

## 증상

- 청크를 벗어났다가 돌아오면 암석의 종류가 바뀜 (`rockSpriteSets[0]` → `rockSpriteSets[1]` 등)
- 동시에 암석의 위치도 변경됨
- 아무것도 파지 않고 단순히 왔다 갔다만 해도 발생

---

## 근본 원인 분석

### 원인 1: `prng` 공유로 인한 타입/위치 시퀀스 오염

`RockLayoutCalculator.GetRockLayout()`:

```csharp
System.Random prng = GetDeterministicRandom(coord, worldSeed);

for (int i = 0; ...; i++)
{
    selectedSet = rockSpriteSets[prng.Next(...)]; // ① 타입 선택
    posX = ... prng.NextDouble() ...;              // ② 위치 X
    posY = ... prng.NextDouble() ...;              // ③ 위치 Y
    if (collision || !isGround) continue;          // 실패 → prng 3번 소비 후 재시도
}
```

타입 선택과 위치 선택이 동일한 `prng`를 공유하기 때문에, 배치 실패 시마다 prng가 3번 소비되어 이후 모든 암석의 타입과 위치 시퀀스가 어긋남.

### 원인 2: Fix 1이 BasePixels를 변경해 Ground Check 실패 횟수가 달라짐

`BugAnalysis_RockDisappear.md`의 Fix 1 (`DiggableRock.Start()` → `RevealInTerrain()`)은
청크 로드 시 자동으로 `ClearHole()`을 호출해 `BasePixels`에 구멍을 냄.

```
첫 방문 → Start() → RevealInTerrain() → ClearHole() → BasePixels에 구멍
         → 청크 저장 (구멍 포함)
재방문 → GetRockLayout() → Ground Check → 구멍 위치 실패
       → prng 추가 소비 → 타입/위치 시퀀스 어긋남
```

아무것도 파지 않아도 발생하는 이유: Fix 1이 모든 방문마다 `ClearHole()`을 자동 실행하기 때문.

### 원인 3: `ReturnToPool()` 풀 키 오류 (스프라이트 이름에 `_` 포함 시)

```csharp
// ReturnToPool — name.Split('_')[1] 방식
// 스프라이트 이름 "rock_large" → parts[1] = "rock" (틀림)
// SpawnRockObject — poolKey = sprite.name = "rock_large" (맞음)
// → 반납/조회 키 불일치 → 풀 재사용 불가 + 잠재적 타입 교차오염
```

---

## 적용된 수정 (2026-03-17)

### Fix A — `RockLayoutCalculator.cs`: `typePrng` / `posPrng` 분리

단일 `prng` → 두 개로 분리. 루프 구조를 **슬롯(외부) + 위치 재시도(내부)**로 변경.

```csharp
int baseSeed = (coord.x * HASH_X) ^ (coord.y * HASH_Y) ^ worldSeed;
System.Random typePrng = new System.Random(baseSeed);
System.Random posPrng  = new System.Random(baseSeed ^ 0x5F3759DF);

for (int slot = 0; slot < quadrantTarget; slot++)
{
    // 타입은 슬롯당 1번 고정 (위치 실패와 무관)
    int typeIdx = typePrng.Next(rockSpriteSets.Count);
    ...
    for (int attempt = 0; attempt < 10; attempt++)  // 위치만 재시도
    {
        float posX = ... posPrng.NextDouble() ...;   // posPrng만 소비
        float posY = ... posPrng.NextDouble() ...;
        if (valid) { results.Add(...); break; }
    }
}
```

**효과**: 위치 배치 실패가 아무리 많아도 타입 시퀀스 불변.

---

### Fix C — `RockSpawner.cs` + `DiggableRock.cs`: 풀 키 수정

```csharp
// DiggableRock에 poolKey 필드 추가
[HideInInspector] public string poolKey;

// SpawnRockObject — 스폰 시 캐시
dr.poolKey = rock.sprite.name;

// ReturnToPool — Split 방식 → 캐시된 값 사용
DiggableRock drComp = rock.GetComponent<DiggableRock>();
string poolKey = (drComp != null && !string.IsNullOrEmpty(drComp.poolKey))
    ? drComp.poolKey : "Default";
```

또한 `SpawnRockObject`에서 풀 재사용 시 스프라이트 명시적 재할당 추가 (방어):
```csharp
SpriteRenderer srReuse = rockObj.GetComponent<SpriteRenderer>();
if (srReuse != null && srReuse.sprite != rock.sprite) srReuse.sprite = rock.sprite;
```

---

### Fix D (최종 해결) — 암석 배치 저장/복원 시스템

Fix A+C로 타입/위치 안정성을 개선했지만, Fix 1의 `ClearHole()`이 `BasePixels`를
변경하는 한 `GetRockLayout()`의 Ground Check 결과는 재로드마다 달라질 수 있음.
→ **재로드 시 `GetRockLayout()` 자체를 호출하지 않는 것**이 근본적 해결.

#### 데이터 흐름

```
언로드 시:
  WorldPersistenceSystem.SaveChunkDataToMemory()
    → tChunk.SpawnedRocks 순회
    → RockSaveEntry[] { boundsX/Y/W/H, spriteSetIndex } 저장
    → ChunkSaveData.savedRocks 에 보관
    (파괴된 암석은 SpawnedRocks에서 이미 제거됨 → 자동으로 저장 안 됨)

재로드 시:
  StandardChunkFactory → ChunkInitializationData.SavedRocks 전달
  → TerrainChunk.Reuse_Step1_Prepare() → _pendingSavedRocks 보관
  → RockDecorator.Decorate()
      → chunk.GetAndClearSavedRocks() != null
          → TerrainDecorator.RestoreRocks()  ← GetRockLayout() 호출 안 함
      → null (첫 방문)
          → TerrainDecorator.GenerateRocks() ← 기존 경로
```

#### 저장 구조

```csharp
// ChunkSaveData.cs
public RockSaveEntry[] savedRocks;
// null     = 아직 저장된 적 없는 신규 청크 → GenerateRocks 실행
// 빈 배열  = 저장됐지만 암석 없음 (모두 파괴) → RestoreRocks 실행 (0개)
// 배열     = 저장된 암석 목록 → RestoreRocks 실행

// RockSaveEntry
public class RockSaveEntry
{
    public int boundsX, boundsY, boundsW, boundsH;
    public int spriteSetIndex;  // TileVisualSettings.rockSpriteSets[] 인덱스
}
```

#### 복원 메서드 (`TerrainDecorator.RestoreRocks`)

```csharp
// GetRockLayout() 없이 저장된 데이터 그대로 사용
foreach (var entry in savedRocks)
{
    var set = rockSpriteSets[entry.spriteSetIndex];
    RockData rock = BuildRockData(entry, set);
    RockSpawner.SpawnRockObject(chunk, rock, tileType);
    // → Start() → RevealInTerrain() → BasePixels 기반 자동 노출 (Fix 1)
}
```

#### spriteSetIndex 흐름 (`DiggableRock.spriteSetIndex`)

```
GetRockLayout() → RockData.spriteSetIndex = typeIdx
  → SpawnRockObject() → dr.spriteSetIndex = rock.spriteSetIndex
    → SaveChunkDataToMemory() → entry.spriteSetIndex = dr.spriteSetIndex
      → RestoreRocks() → rockSpriteSets[entry.spriteSetIndex]
```

#### 파괴된 암석 처리

암석이 파괴되면 `TerrainChunk.RemoveSpawnedRock()` 호출 → `_spawnedRocks`에서 제거.
언로드 시 `SpawnedRocks`를 저장하므로 파괴된 암석은 자동으로 제외됨.
재로드 시 해당 자리에는 암석이 복원되지 않음 ✓

---

## 수정된 파일 목록

| 파일 | 변경 내용 |
|------|-----------|
| `ChunkSaveData.cs` | `RockSaveEntry` 클래스 추가, `savedRocks` 필드 추가 |
| `TerrainDecorator.cs` | `RockData.spriteSetIndex` 추가, `RestoreRocks()` 메서드 추가 |
| `RockLayoutCalculator.cs` | `typePrng`/`posPrng` 분리, 슬롯 루프 구조 변경, `spriteSetIndex` 저장 |
| `DiggableRock.cs` | `poolKey`, `spriteSetIndex` 필드 추가 |
| `RockSpawner.cs` | `ReturnToPool` 키 수정, 스프라이트 재할당, `spriteSetIndex` 캐싱 |
| `WorldPersistenceSystem.cs` | `SaveChunkDataToMemory()`에 암석 스냅샷 저장 |
| `ChunkInitializationData.cs` | `SavedRocks` 필드 추가 |
| `StandardChunkFactory.cs` | `SavedRocks` 전달 |
| `TerrainChunk.cs` | `_pendingSavedRocks`, `GetAndClearSavedRocks()` 추가 |
| `RockDecorator.cs` | 저장 있으면 `RestoreRocks`, 없으면 `GenerateRocks` |

---

## 현재 조치 상태

**✅ Fix A + Fix C + Fix D 모두 적용 완료 (2026-03-17)**

- 재로드 시 `GetRockLayout()` 호출 없이 저장된 위치/종류 그대로 복원
- 파괴된 암석은 재로드 후 재등장하지 않음
- 세션 내에서만 유효 (메모리 캐시) — 디스크 저장 미지원 (게임이 매 세션 초기화됨)
