# 청크 Instantiate 프레임 스파이크 최적화 설계
@tags: chunk-loading, instantiate, spike, optimization, performance, design, spec, ChunkPool

**날짜**: 2026-04-30  
**상태**: 승인됨  
**대상 파일**: `TerrainChunk.cs`, `ChunkLoadingRunner.cs`, `InfinityMapManager.cs`

---

## 문제 정의

플레이어가 청크 경계를 지날 때 마다 약 61ms의 프레임 스파이크가 반복 발생.

### 프로파일러 지표

| 구간 | 시간 |
|------|------|
| `ChunkLoadingRunner.ProcessChunkQueue()` | 56.68ms |
| `Instantiate()` | 55.68ms |
| `TerrainChunk.Awake()` Self | 46.55ms |
| `SpriteMeshGenerator.TraceShape` | 7.95ms |
| GC Alloc (Instantiate 1회) | 4.9MB |

### 근본 원인

`ChunkPool`이 비어 있을 때 `Instantiate()`가 호출되고, `TerrainChunk.Awake()`의 두 가지 작업이 과도하게 무겁다.

1. **`InitializeTextures()` — 4MB GC 할당**  
   `Color32[] initialPixels = new Color32[1000 * 1000]`로 4MB 관리 힙 배열을 매 Instantiate마다 생성. GC 스파이크 원인.

2. **`Sprite.Create()` — `TraceShape` 7.95ms**  
   흰색(불투명)으로 초기화된 텍스처의 100만 픽셀 전체를 스캔해 외곽선 메시 생성. 지형 청크에는 불필요.

---

## 해결 방안

### Part A — `InitializeTextures()` 경량화

**변경 1: `SpriteMeshType.FullRect`로 TraceShape 차단**

```csharp
_spriteRenderer.sprite = Sprite.Create(
    _mainTexture, new Rect(0, 0, width, height),
    new Vector2(0f, 0f), pixelsPerUnit,
    0, SpriteMeshType.FullRect   // ← 추가
);
```

- `FullRect`는 4-vertex quad를 강제, `SpriteMeshGenerator.TraceShape` 미호출
- 지형 청크는 텍스처 전체를 사용하는 불투명 스프라이트이므로 `Tight` 메시가 불필요
- 7.95ms 절감

**변경 2: static 공유 버퍼로 GC 0 달성**

```csharp
// TerrainChunk 클래스 필드 (앱 수명 동안 1회만 할당)
private static byte[] s_clearBuffer;

private void InitializeTextures()
{
    _mainTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
    _mainTexture.filterMode = FilterMode.Point;
    _mainTexture.wrapMode = TextureWrapMode.Clamp;

    int byteCount = width * height * 4;
    if (s_clearBuffer == null || s_clearBuffer.Length < byteCount)
        s_clearBuffer = new byte[byteCount]; // 최초 1회만 할당, 이후 재사용
    _mainTexture.LoadRawTextureData(s_clearBuffer); // alpha=0, GC 없음
    _mainTexture.Apply(false);

    _spriteRenderer.sprite = Sprite.Create(
        _mainTexture, new Rect(0, 0, width, height),
        new Vector2(0f, 0f), pixelsPerUnit,
        0, SpriteMeshType.FullRect
    );
}
```

- `s_clearBuffer`는 모든 byte=0 → 투명(alpha=0) 텍스처로 초기화
- `LoadRawTextureData(byte[])` 내부는 memcpy, 관리 힙 할당 없음
- 기존 흰색 1~2프레임 플래시 → 투명 1~2프레임으로 변경 (`Reuse_Step1_Prepare()`가 즉시 덮어씀)

### Part B — Pool Pre-warming

**`ChunkLoadingRunner`에 신규 메서드 추가**

```csharp
/// <summary>
/// 게임 시작 전 청크 풀을 미리 채운다. 프레임당 1개씩 분산.
/// </summary>
public IEnumerator PrewarmPoolAsync(GameObject prefab, Transform parent, int count)
{
    for (int i = 0; i < count; i++)
    {
        var obj = UnityEngine.Object.Instantiate(prefab, parent);
        var chunk = obj.GetComponent<TerrainChunk>();
        _pool.Return(chunk); // SetActive(false) 후 풀에 적재
        yield return null;   // 프레임 분산
    }
}
```

**`InfinityMapManager.InitializeCoroutine()`에 호출 삽입**

삽입 위치: `_loadingRunner.Initialize(...)` 직후, `UpdateChunks()` 이전.

```csharp
_loadingRunner.Initialize(...);

// [Pre-warm] 로딩 화면 중 풀 사전 적재 (게임플레이 중 Instantiate 차단)
yield return StartCoroutine(_loadingRunner.PrewarmPoolAsync(chunkPrefab, transform, 6));

UpdateChunks();
```

- count=6 근거: 동시 로드 4개 + 방향 전환 여유 2개
- 삽입 시점은 `LoadingData.IsReady = false` 구간이므로 플레이어 이동 불가 → 스파이크 영향 없음
- 이후 게임플레이 중 `Instantiate()` 호출 횟수 대폭 감소

---

## 변경 범위

| 파일 | 변경 내용 | 기존 아키텍처 영향 |
|------|-----------|-------------------|
| `TerrainChunk.cs` | `InitializeTextures()` 수정, `s_clearBuffer` 필드 추가 | 없음 |
| `ChunkLoadingRunner.cs` | `PrewarmPoolAsync()` 메서드 추가 | 없음 |
| `InfinityMapManager.cs` | `InitializeCoroutine()`에 2줄 삽입 | 없음 |

청크 파이프라인, dirty 플래그 패턴, Job 스케줄링, 콜라이더/비주얼 분리 아키텍처 변경 없음.

---

## 기대 효과

| 항목 | 이전 | 이후 |
|------|------|------|
| GC Alloc per Instantiate | 4.9MB | ~0MB |
| `Sprite.Create()` 시간 | ~8ms (TraceShape) | ~0ms (FullRect) |
| 게임플레이 중 Instantiate 빈도 | 청크 경계마다 | 거의 0 |
| `TerrainChunk.Awake()` Self 시간 | 46.55ms | 대폭 감소 |
