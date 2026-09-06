# 청크 Instantiate 프레임 스파이크 최적화 구현 계획
@tags: chunk-loading, instantiate, spike, optimization, performance, plan, ChunkPool

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 청크 경계 이동 시 발생하는 61ms 프레임 스파이크를 제거한다 — 4MB GC 할당 제거, TraceShape 차단, Pool Pre-warming.

**Architecture:** `TerrainChunk.InitializeTextures()`의 관리 힙 할당을 static 공유 버퍼와 `LoadRawTextureData()`로 대체하고 `SpriteMeshType.FullRect`로 `TraceShape`를 차단한다. `ChunkLoadingRunner.PrewarmPoolAsync()`로 로딩 화면 중 풀을 미리 채워 게임플레이 중 `Instantiate()` 호출을 최소화한다.

**Tech Stack:** Unity 2D, C#, NUnit (Unity Test Framework EditMode), Unity Profiler

---

## 수정 파일 맵

| 파일 | 역할 | 작업 |
|------|------|------|
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` | 청크 렌더링·물리 MonoBehaviour | `InitializeTextures()` 수정, `s_clearBuffer` 필드 추가 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Core/ChunkLoadingRunner.cs` | 청크 로딩 코루틴 | `PrewarmPoolAsync()` 메서드 추가 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs` | 청크 생명주기 총괄 | `InitializeCoroutine()`에 pre-warm 호출 삽입 |
| `Assets/Tests/EditMode/ChunkTextureInitTests.cs` | 신규 EditMode 테스트 | `s_clearBuffer` 투명 초기화 + `FullRect` 4-vertex 검증 |

---

## Task 1: 실패하는 테스트 작성 — `InitializeTextures()` 두 가지 핵심 동작

**Files:**
- Create: `Assets/Tests/EditMode/ChunkTextureInitTests.cs`

- [ ] **Step 1: 테스트 파일 생성**

아래 내용으로 `Assets/Tests/EditMode/ChunkTextureInitTests.cs`를 생성한다.

```csharp
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// TerrainChunk.InitializeTextures() 최적화 검증.
/// 1) zero-byte 버퍼로 투명 텍스처 초기화 (GC 없음)
/// 2) SpriteMeshType.FullRect = 4 vertices (TraceShape 없음)
/// </summary>
public class ChunkTextureInitTests
{
    private const int W = 8;
    private const int H = 8;

    [Test]
    public void ClearBuffer_AllZeroBytes_ProducesTransparentTexture()
    {
        var clearBuffer = new byte[W * H * 4]; // 기본값 = 0
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(clearBuffer);
        tex.Apply();

        var pixels = tex.GetPixels32();
        foreach (var p in pixels)
            Assert.AreEqual(0, p.a, $"alpha는 0이어야 하는데 {p.a}");

        Object.DestroyImmediate(tex);
    }

    [Test]
    public void FullRectSprite_OnTransparentTexture_HasExactlyFourVertices()
    {
        var clearBuffer = new byte[W * H * 4];
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(clearBuffer);
        tex.Apply();

        var sprite = Sprite.Create(
            tex, new Rect(0, 0, W, H), Vector2.zero, 100f,
            0, SpriteMeshType.FullRect);

        // FullRect는 항상 4-vertex quad. Tight + 완전 투명이면 0 vertex.
        Assert.AreEqual(4, sprite.vertices.Length,
            "SpriteMeshType.FullRect는 반드시 4 vertices여야 한다");

        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(sprite);
    }
}
```

- [ ] **Step 2: 테스트 실행 — PASS 확인 (베이스라인)**

Unity Editor 메뉴: **Window > General > Test Runner > EditMode 탭 > Run All**

`ChunkTextureInitTests` 2개 테스트가 PASS인지 확인한다.  
(아직 실제 코드를 건드리지 않았으므로, 테스트 자체의 논리가 올바른지 확인하는 단계)

- [ ] **Step 3: 커밋**

```bash
git add Assets/Tests/EditMode/ChunkTextureInitTests.cs
git commit -m "test: TerrainChunk 텍스처 초기화 동작 검증 테스트 추가"
```

---

## Task 2: `TerrainChunk.InitializeTextures()` 수정 — GC 제거 + FullRect 적용

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs`

- [ ] **Step 1: `s_clearBuffer` static 필드 추가**

`TerrainChunk.cs`에서 아래 기존 필드 블록 바로 아래에 한 줄을 추가한다.

찾을 위치 (line ~81, `s_colliderUpdateInterval` 선언부 근처):
```csharp
    private static float s_colliderUpdateInterval = 0.2f;
```

그 아래에 추가:
```csharp
    private static byte[] s_clearBuffer;
```

- [ ] **Step 2: `InitializeTextures()` 메서드 교체**

`TerrainChunk.cs` 내 `InitializeTextures()` (line ~251) 전체를 아래로 교체한다.

기존:
```csharp
    private void InitializeTextures()
    {
        _mainTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        _mainTexture.filterMode = FilterMode.Point;
        _mainTexture.wrapMode = TextureWrapMode.Clamp;
        
        // White init
        Color32[] initialPixels = new Color32[width * height];
        Color32 white = new Color32(255, 255, 255, 255);
        for (int i = 0; i < initialPixels.Length; i++) initialPixels[i] = white;
        
        _mainTexture.SetPixels32(initialPixels);
        _mainTexture.Apply(false);
        
        _spriteRenderer.sprite = Sprite.Create(_mainTexture, 
            new Rect(0, 0, width, height), 
            new Vector2(0f, 0f), pixelsPerUnit); // [Refactored] Pivot set to (0,0) Bottom-Left
    }
```

교체 후:
```csharp
    private void InitializeTextures()
    {
        _mainTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        _mainTexture.filterMode = FilterMode.Point;
        _mainTexture.wrapMode = TextureWrapMode.Clamp;

        // [Opt-A1] GC 0: static 버퍼를 재사용해 투명(alpha=0)으로 초기화.
        // Reuse_Step1_Prepare()가 즉시 덮어쓰므로 초기 내용은 영향 없음.
        int byteCount = width * height * 4;
        if (s_clearBuffer == null || s_clearBuffer.Length < byteCount)
            s_clearBuffer = new byte[byteCount];
        _mainTexture.LoadRawTextureData(s_clearBuffer);
        _mainTexture.Apply(false);

        // [Opt-A2] FullRect: SpriteMeshGenerator.TraceShape 호출 차단 (~8ms 절감).
        // 지형 청크는 텍스처 전체를 사용하므로 Tight 메시 불필요.
        _spriteRenderer.sprite = Sprite.Create(
            _mainTexture,
            new Rect(0, 0, width, height),
            new Vector2(0f, 0f), pixelsPerUnit,
            0, SpriteMeshType.FullRect);
    }
```

- [ ] **Step 3: 컴파일 확인**

Unity Editor Console에 빨간 오류가 없는지 확인한다.  
(`SpriteMeshType`은 `UnityEngine` 네임스페이스에 포함되어 있으므로 별도 `using` 불필요)

- [ ] **Step 4: 테스트 재실행 — 여전히 PASS**

**Window > General > Test Runner > EditMode > Run All**

`ChunkTextureInitTests` 2개 테스트가 여전히 PASS인지 확인한다.

- [ ] **Step 5: 커밋**

```bash
git add Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs
git commit -m "perf: InitializeTextures GC 제거(static 버퍼) + FullRect TraceShape 차단"
```

---

## Task 3: `ChunkLoadingRunner.PrewarmPoolAsync()` 추가

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Core/ChunkLoadingRunner.cs`

- [ ] **Step 1: `PrewarmPoolAsync()` 메서드 추가**

`ChunkLoadingRunner.cs`에서 `StartLoadingIfNeeded()` 메서드(line ~127) 바로 뒤에 삽입한다.

찾을 위치:
```csharp
    public void StartLoadingIfNeeded()
    {
        bool hasWork = _priorityQueue.Count > 0 || _readHead < _loadQueue.Count;
        if (hasWork && !_isLoadingRoutineRunning)
        {
            StartCoroutine(ProcessChunkQueue());
        }
    }
```

그 아래에 추가:
```csharp
    /// <summary>
    /// 로딩 화면 중 청크 풀을 미리 채운다. 프레임당 1개씩 분산하여 스파이크 없음.
    /// InfinityMapManager.InitializeCoroutine()에서 UpdateChunks() 이전에 호출한다.
    /// </summary>
    public IEnumerator PrewarmPoolAsync(GameObject prefab, Transform parent, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var obj = UnityEngine.Object.Instantiate(prefab, parent);
            var chunk = obj.GetComponent<TerrainChunk>();
            _pool.Return(chunk);
            yield return null;
        }
    }
```

- [ ] **Step 2: 컴파일 확인**

Unity Editor Console에 빨간 오류가 없는지 확인한다.

- [ ] **Step 3: 커밋**

```bash
git add Assets/Scripts/Gameplay/Terrain/Tiles/Core/ChunkLoadingRunner.cs
git commit -m "feat: ChunkLoadingRunner.PrewarmPoolAsync() 추가"
```

---

## Task 4: `InfinityMapManager.InitializeCoroutine()`에 Pre-warm 호출 삽입

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs`

- [ ] **Step 1: Pre-warm 호출 삽입**

`InfinityMapManager.cs`의 `InitializeCoroutine()`에서 아래 블록을 찾는다 (line ~310):

```csharp
        _loadingRunner.Initialize(
            _chunkRegistry,
            _chunkSpawner,
            _chunkPool,
            _chunkDataProvider,
            _gridSystem,
            player,
            chunkSpawningBatchSize,
            maxTimePerBatchMs,
            maxTimePerFramePh2Ms
        );
        //Debug.Log("[INIT_DEBUG] ✓ ChunkLoadingRunner initialized");
        
        //Debug.Log("[INIT_DEBUG] Triggering initial UpdateChunks()...");
        UpdateChunks();
```

`UpdateChunks()` 호출 바로 위에 두 줄을 삽입한다:

```csharp
        _loadingRunner.Initialize(
            _chunkRegistry,
            _chunkSpawner,
            _chunkPool,
            _chunkDataProvider,
            _gridSystem,
            player,
            chunkSpawningBatchSize,
            maxTimePerBatchMs,
            maxTimePerFramePh2Ms
        );
        //Debug.Log("[INIT_DEBUG] ✓ ChunkLoadingRunner initialized");

        // [Pre-warm] 로딩 화면 중 풀 사전 적재 — 게임플레이 중 Instantiate 차단
        yield return StartCoroutine(_loadingRunner.PrewarmPoolAsync(chunkPrefab, transform, 6));

        //Debug.Log("[INIT_DEBUG] Triggering initial UpdateChunks()...");
        UpdateChunks();
```

- [ ] **Step 2: 컴파일 확인**

Unity Editor Console에 빨간 오류가 없는지 확인한다.

- [ ] **Step 3: 커밋**

```bash
git add Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs
git commit -m "perf: 로딩 화면 중 청크 풀 6개 사전 적재(PrewarmPoolAsync) 추가"
```

---

## Task 5: 프로파일러 검증

**Files:** 없음 (런타임 검증)

- [ ] **Step 1: Unity Profiler 열기**

**Window > Analysis > Profiler** 를 열고 **Record** 버튼을 켠다.

- [ ] **Step 2: Play 모드 진입 및 청크 경계 이동**

Play 모드에서 플레이어를 청크 경계 방향으로 이동시켜 새 청크 로딩을 유발한다.

- [ ] **Step 3: GC Alloc 확인**

Profiler의 **CPU** 탭에서 `ChunkLoadingRunner.ProcessChunkQueue()`를 클릭.  
**GC Alloc** 컬럼이 **0B 또는 수백 Byte 이하**인지 확인한다. (기존: 4.9MB)

- [ ] **Step 4: TraceShape 부재 확인**

콜 스택에 `SpriteMeshGenerator.TraceShape`가 **없는지** 확인한다. (기존: 7.95ms)

- [ ] **Step 5: 전체 프레임 시간 확인**

청크 경계 이동 시 프레임 스파이크가 기존 61ms에서 **10ms 미만**으로 감소했는지 확인한다.

---

## 셀프 리뷰

**스펙 커버리지 체크**:
- ✓ `InitializeTextures()` 4MB GC 제거 → Task 2
- ✓ `SpriteMeshType.FullRect`로 TraceShape 차단 → Task 2
- ✓ Pool Pre-warming 메서드 추가 → Task 3
- ✓ `InitializeCoroutine()`에 Pre-warm 호출 삽입 → Task 4
- ✓ 프로파일러 검증 → Task 5

**플레이스홀더 없음**: 모든 스텝에 실제 코드 포함. ✓

**타입 일관성**:
- `PrewarmPoolAsync(GameObject, Transform, int)` — Task 3 정의, Task 4에서 동일 시그니처로 호출. ✓
- `s_clearBuffer` — Task 2 Step 1 필드 추가, Step 2 메서드 내 사용. ✓
- `SpriteMeshType.FullRect` — UnityEngine 기본 enum, 추가 using 불필요. ✓
