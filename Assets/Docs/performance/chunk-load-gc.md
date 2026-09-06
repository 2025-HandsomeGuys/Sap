# 청크 로딩 GC 할당 제거
@tags: performance, gc, allocation, chunk-loading, PixelInfo, ChunkDataProvider, TerrainBlender, MemSet, pooling

작성일: 2026-07-31
대상: `ChunkDataProvider`, `TerrainBlender`, `ChunkData`, `TerrainChunk`, `StandardChunkFactory`, `ChunkLoadingRunner`

관련 문서: [README.md](README.md) (성능 이력 인덱스), [job-pipeline-waste-removal.md](../job-pipeline-waste-removal.md)

---

## 1. 발단 — "눈에는 안 보이는데 프로파일러가 튄다"

`ChunkLoadingRunner.ProcessChunkQueue()` 코루틴이 잡힌 프레임:

```
CPU 15.02ms
└─ ChunkLoadingRunner.ProcessChunkQueue   68.6%   10.30ms   Self 42.1% (6.33ms)   GC Alloc 1.3 MB
   ├─ GameObject.Activate                 12.6%    1.90ms   (85 calls)
   ├─ Instantiate                          4.2%    0.64ms   (12 calls)
   └─ GC.Alloc                                              (608 calls, 1.2 MB)
```

**프레임 시간 자체는 문제가 아니다.** 15.02ms는 60fps 예산(16.67ms) 안이고, 그래서 눈에 안 보인다.
문제는 옆 칸의 **GC Alloc 1.3MB**다.

`Self 42.1%`는 Deep Profile이 꺼져 있어서 **프로파일러 마커가 없는 매니지드 코드가 전부 self로 뭉친 것**이다.
아래 §2의 100만 회 루프가 여기 들어 있었다.

---

## 2. 원인

청크 1칸 = **1000×1000 = 100만 픽셀** (`TerrainChunk._width = 1000`).

`ChunkDataProvider.GenerateNewData()`가 신규 청크마다:

```csharp
// 변경 전
context.PixelInfo = new byte[SourceWidth * SourceHeight];   // ← 1MB 매니지드 할당
...
for (int i = 0; i < context.PixelInfo.Length; i++)          // ← 100만 회 비-Burst C# 루프
    context.PixelInfo[i] = currentTypeId;
```

이 배열은 `TerrainChunk.Reuse_Step1_Prepare` → `ChunkData.LoadPixelInfo` → `PixelInfo.CopyFrom(...)`으로
Persistent NativeArray에 복사된 **직후 그냥 쓰레기가 된다.** 순수 스테이징 버퍼인데 매번 새로 만들고 있었다.

층 경계 청크(블렌딩 발생)는 여기에 더해:

| 할당 | 크기 | 위치 |
|------|------|------|
| `belowPixelInfo = new byte[1M]` | 1MB | `ChunkDataProvider` |
| `sourcePixels.Clone()` → `Color32[1M]` | 4MB | `TerrainBlender.CreateBlendedPixels` |

→ **신규 청크 1개당 일반 1MB / 층 경계 6MB.**

### 왜 위험한가

| 항목 | 위험도 |
|------|--------|
| 프레임 스파이크 자체 | **낮음** — 예산 안. 청크 로딩은 이미 코루틴 + 시간 예산으로 잘 분산돼 있다 |
| 청크당 1~6MB 매니지드 할당 | **높음** — 계속 이동하면 Boehm 힙이 계속 커지고 몇 초 주기로 GC가 **눈에 보이는** 랙으로 터진다. 1MB/4MB 대형 블록이라 파편화 + 상주 메모리도 안 돌아온다 |

즉 **관측된 스파이크가 문제가 아니라, 그 스파이크가 GC 쓰레기를 계속 쌓는다는 게 문제**였다.

---

## 3. 수정

### 3.1 비블렌딩 경로 — 배열을 아예 만들지 않는다

신규 절차적 청크의 `PixelInfo`는 **전체가 같은 값(= TileType ID)** 이다.
배열 대신 ID 하나만 넘기고, NativeArray를 `MemSet`으로 직접 채운다.

```csharp
// ChunkData.cs — asmdef의 allowUnsafeCode: true 필요
public unsafe void FillPixelInfo(byte id)
{
    if (!PixelInfo.IsCreated) return;
    UnsafeUtility.MemSet(PixelInfo.GetUnsafePtr(), id, PixelInfo.Length);
}
```

전달 경로에 `UniformPixelInfoId` 필드를 추가했다 (**-1 = 미사용, 기존 배열 경로**).

```
ChunkDataProvider.GenerateNewData
  → ChunkGenerationContext.UniformPixelInfoId
    → StandardChunkFactory.InitializeChunkData
      → ChunkInitializationData.UniformPixelInfoId
        → TerrainChunk.Reuse_Step1_Prepare  →  _data.FillPixelInfo(id)   // 배열 경로보다 우선
```

**결과: 1MB 할당 + 100만 회 루프 → MemSet 1회.**

### 3.2 블렌딩 경로

| 변경 | 절감 |
|------|------|
| `byte[] belowPixelInfo` → `byte belowTypeId` | 1MB |
| `sourcePixels.Clone()` → static `s_blendBuffer` 재사용 + `Array.Copy` | 4MB |
| `context.PixelInfo` → static `s_pixelInfoBuffer` 재사용 | 1MB |

`belowPixelInfo`는 **애초에 필요 없는 배열이었다.** 호출측이 `belowTypeId` 단일값으로 가득 채워서 넘겼고,
`ApplyGradientBlend`도 `otherPixelInfo[otherIdx]` 한 값만 읽었다 — 항상 `belowTypeId`.
배열을 만들 이유가 없어서 시그니처를 `byte`로 바꿨다.

### 3.3 매 프레임 쓰레기

`ChunkLoadingRunner.UpdateLoadQueue`는 `UpdateChunks`를 타고 **매 프레임 호출된다** (로딩 중이 아닐 때도).
`new HashSet<Vector2Int>(newQueue)` + `new List<Vector2Int>()`가 이동 내내 쌓이고 있었다.
→ 재사용 필드 + `Clear()`. `ProcessChunkQueue`의 배치 리스트 2개도 동일 처리.

### 3.4 변경 파일

| 파일 | 변경 |
|------|------|
| `_Core/Data/ChunkData.cs` | `FillPixelInfo(byte)` 추가 (`UnsafeUtility.MemSet`) |
| `.../Pipeline/ChunkGenerationContext.cs` | `UniformPixelInfoId` 필드 |
| `.../Core/ChunkInitializationData.cs` | `UniformPixelInfoId` 필드 |
| `.../Pipeline/ChunkDataProvider.cs` | 비블렌딩 경로 할당 제거, `s_pixelInfoBuffer` 재사용 |
| `.../Generation/TerrainBlender.cs` | `belowPixelInfo` 배열 제거, `s_blendBuffer` 재사용 |
| `.../Pipeline/StandardChunkFactory.cs` | uniform ID 전달 |
| `.../Chunk/TerrainChunk.cs` | `Reuse_Step1_Prepare` uniform ID 우선 분기 |
| `.../Core/ChunkLoadingRunner.cs` | 큐/배치 버퍼 재사용 |

---

## 4. ⚠ 함께 고친 동작 변경 — `PixelInfo`가 전부 0이던 버그

작업 중 발견. **성능과 무관한 실제 버그이므로 회귀 의심 시 여기부터 볼 것.**

변경 전 코드 구조:

```csharp
if (_enableLayerBlending) { ...블렌딩... }
else {
    // [Fix] Populate PixelInfo with current Tile ID if not blending
    for (...) context.PixelInfo[i] = currentTypeId;
}
```

`enableLayerBlending`은 **`worldSettings.json` 기본값이 `true`** (`WorldSettingsData.RenderingSection`).
따라서 저 `else`는 **한 번도 실행되지 않았다.**
그리고 블렌딩 분기는 블렌딩 구역 일부만 덮어쓰므로, 결과적으로
**모든 신규 청크의 `PixelInfo`가 전부 0 (= `PIXEL_ID_AIR`)** 이었다.

영향받던 곳 — `1=Dirt, 2=Rock` 재료 판정을 읽는 코드가 전부 0을 보고 있었다:

- `TerrainModifier` — `pixelType == 2 && toolIndex == 1` (삽으로 돌 못 파는 판정)
- `RockSpawner` — `pixelType = chunk.GetData().PixelInfo[centerIdx]`

**수정 후**: 항상 TileType ID로 채워진다 (`Dirt=10` ~ `MeteoriteRock=16`).

- 돌(`PIXEL_ID_OBJECT=2`)은 `TerrainCarver`가 명시적으로 쓰므로 그대로다.
- `TileType.Empty=0`/`Rock=2`는 층 타입이 아니라 `GetTileTypeAtPosition`에서 나오지 않는다 → ID 충돌 없음.
- 파기/카빙 시 0으로 되돌리는 동작은 그대로 → "air = 0" 규약 유지.

> **되돌리려면**: `ChunkDataProvider.GenerateNewData`의 `context.UniformPixelInfoId = currentTypeId;` → `= 0;`

---

## 5. ⚠ 재사용 버퍼 불변식

`ChunkDataProvider.s_pixelInfoBuffer` / `TerrainBlender.s_blendBuffer`는 **static 공유 버퍼**다.

안전한 이유:
Phase 1(`ChunkGenerationPipeline.ExecutePhase1_SpawnAndInitialize`)은 메인 스레드에서 **동기 실행**되고,
`TerrainChunk.Reuse_Step1_Prepare`가 **같은 호출 안에서** `CopyFrom`으로 NativeArray에 복사한다.
따라서 다음 청크가 같은 버퍼를 덮어써도 문제가 없다.

> **이 배열의 참조를 프레임을 넘겨 보관하는 코드를 추가하면 즉시 깨진다.**
> Phase 1을 비동기/워커 스레드로 옮기는 변경을 할 때도 이 가정이 먼저 깨진다.

---

## 6. 검증

- `dotnet build GameScripts.csproj` → **0 Error / 0 Warning**
- 플레이 검증(사람이 수행): 여러 층을 걸어다니며
  1. 프로파일러 GC Alloc 열이 청크 로딩 프레임에서 0에 붙는지
  2. 지형 색·층 경계 블렌딩이 이전과 동일한지
  3. 삽 vs 곡괭이 판정, 채굴 이펙트/드랍이 정상인지 (§4 동작 변경)

## 7. 남은 것

같은 프레임의 `GameObject.Activate` 85회 / 1.90ms는 손대지 않았다. [README.md §2.1](README.md) 참고.
