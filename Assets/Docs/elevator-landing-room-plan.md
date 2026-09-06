# 엘리베이터 착지 방 분리 — 구현 계획

> 설계 문서: [elevator-landing-room.md](elevator-landing-room.md)

**Goal:** 구멍 입구 착지 지점과 엘리베이터 착지 지점을 서로 다른 방으로 만들고, 엘리베이터 방은 지층별 PNG를 쓴다. 착지 직후 낙하 버그를 함께 수정한다.

**Architecture:** 페인팅 코어를 `ChunkImagePainter` static 유틸로 추출하고, 구멍 입구용 `ImageChunkOverrider`와 엘리베이터용 `ElevatorRoomPainter` 두 컴포넌트가 이를 공유한다. `ElevatorManager.TeleportPlayer`를 코루틴화해 목적지 청크 로드까지 플레이어를 Kinematic으로 묶는다.

**Tech Stack:** Unity 6000.3.2f1 / C# / 기존 `GameScripts` 어셈블리

## Global Constraints

- **버전 관리**: UVCS. `git` 명령을 쓰지 않는다. 커밋 단계 없음.
- **테스트**: Unity Test Runner 실행은 사람이 한다. 각 태스크의 검증은 에디터 Play 모드 + Console 확인으로 한다.
- **더티 플래그**: `IsVisualDirty`/`IsColliderDirty`/`HasBeenModified`를 직접 나열하지 않는다. `ChunkData.MarkDirty()`(저장 포함) 또는 `MarkRenderDirty()`(저장 제외)를 쓴다.
- **Rigidbody2D 속도**: Unity 6 API인 `linearVelocity`를 쓴다 (`velocity` 아님).
- **청크 상수**: 청크 1칸 = 10 월드유닛 = 1000px (`ChunkCoords.WorldSize`). 하드코딩 대신 `ChunkCoords.WorldSize`를 참조한다.
- **파일 위치**: 신규 스크립트 2개는 `Assets/Scripts/UI/Interaction/Scene/` 에 둔다 (기존 `ImageChunkOverrider`와 동일 디렉토리).

---

## File Structure

| 파일 | 책임 |
|---|---|
| `Assets/Scripts/UI/Interaction/Scene/ChunkImagePainter.cs` | **신규.** PNG → 청크 픽셀 교체 코어. 상태 없음, static. 청크 로드 대기·보호좌표 등록·게임 흐름은 포함하지 않는다. |
| `Assets/Scripts/UI/Interaction/Scene/ImageChunkOverrider.cs` | **수정.** 구멍 입구 방 전용. 고정 좌표 + 플레이어 활성화. 페인팅은 `ChunkImagePainter`에 위임. `SetTargetChunk` 제거. |
| `Assets/Scripts/UI/Interaction/Scene/ElevatorRoomPainter.cs` | **신규.** 엘리베이터 착지 방. 지층별 PNG 배열 보유, 스킵 조건 판정, 보호좌표 등록. |
| `Assets/Scripts/UI/Interaction/Elevator/ElevatorManager.cs` | **수정.** `TeleportPlayer` 코루틴화 + 착지 보호. overrider 재사용 블록·진단 코루틴 제거. `ElevatorRoomPainter` 참조 추가. |

---

## Task 1: `ChunkImagePainter` 추출 + `ImageChunkOverrider` 위임

기존 동작을 바꾸지 않고 페인팅 로직만 분리한다. 이 태스크가 끝나면 구멍 입구는 지금과 똑같이 동작해야 한다.

**Files:**
- Create: `Assets/Scripts/UI/Interaction/Scene/ChunkImagePainter.cs`
- Modify: `Assets/Scripts/UI/Interaction/Scene/ImageChunkOverrider.cs` (전체 교체)

**Interfaces:**
- Consumes: `TerrainChunk.EnsureJobsCompleted()`, `TerrainChunk.width/height`, `TerrainChunk.LoadChunkData(Color32[], byte[], Color32[], int, int)`, `TerrainChunk.GetData()`, `ChunkData.MarkDirty()`, `InfinityMapManager.HasChunk(Vector2Int)`, `InfinityMapManager.GetChunk(Vector2Int) → TerrainChunk`
- Produces: `ChunkImagePainter.Paint(TerrainChunk chunk, Texture2D terrain, Texture2D border) → bool` — Task 3이 이 시그니처를 그대로 호출한다.

- [ ] **Step 1: `ChunkImagePainter.cs` 생성**

```csharp
// @tags: chunk, image, painter, terrain, override
using UnityEngine;

/// <summary>
/// PNG 이미지로 청크의 픽셀 데이터를 통째로 교체하는 공용 유틸.
/// 투명(alpha ≤ 10) = 빈 공간, 불투명 = 땅.
///
/// 호출측 책임 (여기 포함하지 않음):
/// - 청크 로드 대기 (InfinityMapManager.HasChunk)
/// - 보호 좌표 등록 (SpecialChunkManager.RegisterProtectedCoord)
/// - 페인팅 이후의 게임 흐름 (플레이어 활성화 등)
/// </summary>
public static class ChunkImagePainter
{
    /// <summary>불투명 판정 기준 — 이 값보다 큰 alpha를 땅으로 취급한다.</summary>
    private const byte SOLID_ALPHA_THRESHOLD = 10;

    /// <summary>
    /// 청크 픽셀을 terrain 이미지로 교체한다.
    /// </summary>
    /// <param name="border">null이면 청크가 이미 가진 BorderData를 유지한다.</param>
    /// <returns>교체 성공 시 true. 인자가 유효하지 않으면 에러 로그 후 false.</returns>
    public static bool Paint(TerrainChunk chunk, Texture2D terrain, Texture2D border)
    {
        if (chunk == null)
        {
            Debug.LogError("[ChunkImagePainter] chunk가 null.");
            return false;
        }
        if (terrain == null)
        {
            Debug.LogError("[ChunkImagePainter] terrain 이미지가 null! Inspector에서 PNG를 할당했는지 확인.");
            return false;
        }
        if (!terrain.isReadable)
        {
            Debug.LogError($"[ChunkImagePainter] '{terrain.name}' — Read/Write Enabled가 꺼져 있음! Import Settings에서 켜야 함.");
            return false;
        }

        chunk.EnsureJobsCompleted();

        Color32[] pixels = ResampleToChunk(terrain, chunk.width, chunk.height);

        // 픽셀 alpha → PixelInfo(solid/air 플래그) 생성.
        // null로 넘기면 기존 PixelInfo가 유지되어 투명 영역도 solid로 남는다.
        byte[] pixelInfo = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            pixelInfo[i] = (byte)(pixels[i].a > SOLID_ALPHA_THRESHOLD ? 1 : 0);

        // 테두리 텍스처: 할당됐으면 적용, 없으면 기존 BorderData 유지
        Color32[] borderPixels = null;
        int borderW = 0, borderH = 0;
        if (border != null)
        {
            if (!border.isReadable)
                Debug.LogError($"[ChunkImagePainter] '{border.name}' — Border 텍스처의 Read/Write Enabled가 꺼져 있음! 기존 테두리를 유지한다.");
            else
            {
                borderPixels = border.GetPixels32();
                borderW = border.width;
                borderH = border.height;
            }
        }

        chunk.LoadChunkData(pixels, pixelInfo, borderPixels, borderW, borderH);

        // CLAUDE.md 아키텍처 제약 3 — 더티 플래그는 ChunkData 메서드로 세팅한다.
        // (비주얼 + 콜라이더 + 저장 대상 표시)
        chunk.GetData().MarkDirty();
        return true;
    }

    /// <summary>이미지가 청크 해상도와 다르면 최근접 샘플링으로 맞춘다.</summary>
    private static Color32[] ResampleToChunk(Texture2D src, int chunkW, int chunkH)
    {
        Color32[] pixels = src.GetPixels32();
        if (pixels.Length == chunkW * chunkH) return pixels;

        Debug.LogWarning($"[ChunkImagePainter] 픽셀 수 불일치 — 이미지 {src.width}x{src.height} → 청크 {chunkW}x{chunkH} 리샘플링.");

        Color32[] resampled = new Color32[chunkW * chunkH];
        float scaleX = (float)src.width  / chunkW;
        float scaleY = (float)src.height / chunkH;
        for (int y = 0; y < chunkH; y++)
        {
            int srcY = Mathf.Clamp(Mathf.RoundToInt(y * scaleY), 0, src.height - 1);
            int dstRow = y * chunkW;
            int srcRow = srcY * src.width;
            for (int x = 0; x < chunkW; x++)
            {
                int srcX = Mathf.Clamp(Mathf.RoundToInt(x * scaleX), 0, src.width - 1);
                resampled[dstRow + x] = pixels[srcRow + srcX];
            }
        }
        return resampled;
    }
}
```

- [ ] **Step 2: `ImageChunkOverrider.cs` 전체 교체**

기존 파일 내용을 아래로 통째로 바꾼다.

```csharp
using System.Collections;
using UnityEngine;

/// <summary>
/// 구멍 입구(지상→지하 진입)의 착지 청크를 PNG 이미지로 교체한다.
/// 투명(alpha=0) = 빈 공간, 불투명(alpha>0) = 땅.
/// 교체 완료 후 씬에 비활성으로 배치된 플레이어를 활성화한다.
///
/// 엘리베이터 착지 방은 이 컴포넌트가 아니라 ElevatorRoomPainter가 담당한다.
/// (예전에는 ElevatorManager가 이 인스턴스를 SetTargetChunk로 재사용해
///  엘리베이터 착지 지점에도 입구와 같은 PNG가 칠해졌다.)
/// </summary>
[DefaultExecutionOrder(-40)]
public class ImageChunkOverrider : MonoBehaviour
{
    private const float CHUNK_LOAD_TIMEOUT = 10f;

    [SerializeField] private Vector2Int targetChunkCoord;
    [SerializeField] private Texture2D  terrainImage;       // 1000×1000, Read/Write Enabled
    [SerializeField] private Texture2D  borderTexture;      // (선택) null이면 생성 시 할당된 텍스처 유지
    [SerializeField] private GameObject playerObj;          // 씬에 비활성 상태로 배치된 Player

    /// <summary>이 컴포넌트가 소유한 청크 좌표. ElevatorRoomPainter가 침범하지 않도록 보호 좌표로 등록된다.</summary>
    public Vector2Int TargetChunkCoord => targetChunkCoord;

    private void Awake()
    {
        SpecialChunkManager.Instance.RegisterProtectedCoord(targetChunkCoord);
    }

    private void OnDestroy()
    {
        if (SpecialChunkManager.Instance != null)
            SpecialChunkManager.Instance.UnregisterProtectedCoord(targetChunkCoord);
    }

    private void Start() => StartCoroutine(OverrideRoutine());

    private IEnumerator OverrideRoutine()
    {
        var mgr = InfinityMapManager.Instance;
        if (mgr == null)
        {
            Debug.LogError("[ImageChunkOverrider] InfinityMapManager.Instance가 null! 씬에 InfinityMapManager가 없거나 아직 초기화되지 않음.");
            ActivatePlayer();
            yield break;
        }

        // 청크 로드 대기
        float waited = 0f;
        while (!mgr.HasChunk(targetChunkCoord))
        {
            waited += Time.deltaTime;
            if (waited > CHUNK_LOAD_TIMEOUT)
            {
                Debug.LogError($"[ImageChunkOverrider] {CHUNK_LOAD_TIMEOUT}초 초과 — 청크 {targetChunkCoord}가 로드되지 않음. targetChunkCoord가 플레이어 위치와 맞는지, InfinityMapManager viewDistance가 충분한지 확인.");
                ActivatePlayer();
                yield break;
            }
            yield return null;
        }

        ChunkImagePainter.Paint(mgr.GetChunk(targetChunkCoord), terrainImage, borderTexture);

        // 콜라이더 재생성 대기
        yield return null;

        ActivatePlayer();
    }

    /// <summary>
    /// 페인팅 성공 여부와 무관하게 플레이어를 활성화한다.
    /// (텍스처 설정 실수로 게임이 영영 시작되지 않는 상태를 막는다 —
    ///  실패 원인은 위에서 이미 에러 로그로 남는다.)
    /// </summary>
    private void ActivatePlayer()
    {
        if (playerObj != null)
            playerObj.SetActive(true);
    }
}
```

**동작 변경 1건 (의도적)**: 기존에는 텍스처 설정이 잘못되면 `yield break`로 빠져나가 플레이어가 영영 활성화되지 않았다. 이제는 에러 로그를 남기고 플레이어를 활성화한다. 게임이 멈추는 것보다 낫다.

- [ ] **Step 3: 컴파일 확인**

Unity 에디터로 전환해 컴파일이 끝나기를 기다린다.
기대: Console에 컴파일 에러 0건.

- [ ] **Step 4: 구멍 입구 회귀 검증 (Play 모드)**

`Assets/Scenes/Demo/DemoUnderground.unity`를 열고 Play.

기대 결과:
1. 플레이어가 정상 활성화되어 시작 방에 서 있다.
2. 시작 방 지형이 기존과 동일한 PNG 모양이다.
3. Console에 `[ChunkImagePainter]` 에러/경고가 없다.

하나라도 어긋나면 다음 태스크로 넘어가지 않는다.

---

## Task 2: 착지 보호 — `TeleportPlayer` 코루틴화

낙하 버그를 수정한다. 이 태스크만으로 "얼음층 가면 엘리베이터가 안 보인다"가 해결된다. 페인팅은 아직 손대지 않는다.

**Files:**
- Modify: `Assets/Scripts/UI/Interaction/Elevator/ElevatorManager.cs`

**Interfaces:**
- Consumes: `InfinityMapManager.HasChunk(Vector2Int)`, `ChunkCoords.WorldSize`, `ElevatorManager.GetElevatorAt(int, int)`, `ElevatorManager.CalculateElevatorPosition(int, int)`
- Produces: `ElevatorManager.TeleportPlayer(int xChunk, int targetLayer)` — 시그니처 유지(void). 내부에서 코루틴을 시작한다. `ElevatorUI.OnLayerButtonClicked` 호출부는 변경하지 않는다.

- [ ] **Step 1: 진단 코드 제거**

`ElevatorManager.cs`에서 두 곳을 지운다.

지울 것 1 — `TeleportPlayer` 안의 호출부:

```csharp
        // [임시 진단] 착지 후 낙하 여부 확인용 — 원인 확정되면 제거할 것
        StartCoroutine(LogLandingDiagnostics(xChunk, targetLayer));
```

지울 것 2 — `LogLandingDiagnostics` 메서드 전체 (`/// <summary>[임시 진단] 텔레포트 착지 후 2초간...` 주석 블록부터 메서드 닫는 중괄호까지).

- [ ] **Step 2: `TeleportPlayer` 본문을 코루틴 위임으로 교체**

현재 `TeleportPlayer`의 본문 중 **플레이어 탐색·레이어 검증 이후 전부**를 교체한다. 즉 `var targetElevator = GetElevatorAt(...)` 부터 메서드 끝(`ImageChunkOverrider` 블록 포함)까지를 아래 한 줄로 바꾼다.

```csharp
        StartCoroutine(TeleportRoutine(xChunk, targetLayer));
    }
```

교체 후 `TeleportPlayer` 전체 모습:

```csharp
    /// <summary>
    /// 플레이어를 특정 지층으로 텔레포트.
    /// 실제 이동은 TeleportRoutine이 수행한다 — 목적지 청크가 로드될 때까지
    /// 플레이어를 Kinematic으로 묶어야 하므로 프레임을 넘겨야 한다.
    /// </summary>
    public void TeleportPlayer(int xChunk, int targetLayer)
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
            else
            {
                Debug.LogError("[ElevatorManager] Player reference is null and player not found in scene!");
                return;
            }
        }

        if (targetLayer < 0 || targetLayer >= layers.Count)
        {
            Debug.LogError($"[ElevatorManager] Invalid target layer: {targetLayer}");
            return;
        }

        StartCoroutine(TeleportRoutine(xChunk, targetLayer));
    }
```

- [ ] **Step 3: `TeleportRoutine` 추가**

`TeleportPlayer` 바로 아래에 넣는다. 파일 상단에 `using System.Collections;`가 없으면 추가한다 (현재 `using UnityEngine; using System.Collections.Generic; using System.Linq;` 만 있으므로 **추가 필요**).

```csharp
    /// <summary>
    /// 착지 보호 텔레포트.
    ///
    /// 배경: 예전에는 player.position만 대입하고 목적지 청크 로드를 기다리지 않았다.
    /// 그 순간 착지 지점엔 콜라이더가 없어 플레이어가 중력으로 낙하했고,
    /// 몇 백 ms 뒤 청크가 로드되면 지형 속에 파묻혔다.
    /// (계측: 얼음층 이동 시 -195 → -199.8, 엘리베이터보다 4.8유닛 아래)
    ///
    /// 씬 시작 경로인 PlayerSpawner와 같은 패턴으로 막는다:
    /// Kinematic 고정 → 청크 로드 대기 → 콜라이더 1프레임 대기 → Dynamic 복귀.
    /// </summary>
    private IEnumerator TeleportRoutine(int xChunk, int targetLayer)
    {
        const float CHUNK_LOAD_TIMEOUT = 5f;

        var targetLayerInfo = layers[targetLayer];
        Vector2Int targetCoord = new Vector2Int(xChunk, targetLayerInfo.startDepth);

        // 1. 물리 정지 — 청크가 준비될 때까지 낙하를 막는다.
        var rb = player.GetComponentInChildren<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        // 2. 추정 위치로 이동 (로드된 엘리베이터가 있으면 그 위치를 우선)
        var targetElevator = GetElevatorAt(xChunk, targetLayer);
        player.position = targetElevator != null
            ? targetElevator.transform.position
            : (Vector3)CalculateElevatorPosition(xChunk, targetLayerInfo.startDepth);

        // 3. 목적지 청크 로드 대기
        float waited = 0f;
        var mgr = InfinityMapManager.Instance;
        while (mgr != null && !mgr.HasChunk(targetCoord))
        {
            waited += Time.deltaTime;
            if (waited > CHUNK_LOAD_TIMEOUT)
            {
                Debug.LogError($"[ElevatorManager] {CHUNK_LOAD_TIMEOUT}초 초과 — 목적지 청크 {targetCoord}가 로드되지 않음. 물리를 복구하고 진행한다.");
                break;
            }
            yield return null;
        }

        // 4. 로드된 실제 엘리베이터 위치로 재보정.
        //    추정 좌표와 현재는 일치하지만, 향후 엘리베이터 배치 규칙이 바뀌어도 어긋나지 않게 한다.
        targetElevator = GetElevatorAt(xChunk, targetLayer);
        if (targetElevator != null)
            player.position = targetElevator.transform.position;

        // 5. 콜라이더 생성 대기 후 물리 복귀
        yield return null;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Dynamic;
        }

        Debug.Log($"[ElevatorManager] Teleported player to X:{xChunk}, Layer:{targetLayer} at {player.position} (대기 {waited:F2}초)");
    }
```

- [ ] **Step 4: 컴파일 확인**

기대: Console 컴파일 에러 0건. `using System.Collections;` 누락 시 `IEnumerator를 찾을 수 없음` 에러가 나므로 그때 추가한다.

- [ ] **Step 5: 낙하 수정 검증 (Play 모드)**

`DemoUnderground` Play → 시작 지점 엘리베이터에서 E → **얼음땅** 선택.

기대 결과:
1. Console에 `[ElevatorManager] Teleported player to X:0, Layer:1 at (5.00, -195.00, 0.00) (대기 N초)`
2. 플레이어가 **엘리베이터 바로 옆**에 서 있다. 낙하하지 않는다.
3. Hierarchy에서 Player의 Transform Y ≈ **-195** (이전엔 -199.8)
4. 용암땅·우주도 각각 -395 / -595 근처에 착지

여기서 Y가 여전히 -195보다 한참 아래면 다음 태스크로 넘어가지 않는다.

---

## Task 3: `ElevatorRoomPainter` + 지층별 방 페인팅

**Files:**
- Create: `Assets/Scripts/UI/Interaction/Scene/ElevatorRoomPainter.cs`
- Modify: `Assets/Scripts/UI/Interaction/Elevator/ElevatorManager.cs` (필드 1개 + `TeleportRoutine`에 한 단계 삽입)

**Interfaces:**
- Consumes: `ChunkImagePainter.Paint(TerrainChunk, Texture2D, Texture2D) → bool` (Task 1), `SpecialChunkManager.IsProtectedCoord(Vector2Int) → bool`, `SpecialChunkManager.RegisterProtectedCoord(Vector2Int)`, `InfinityMapManager.IsChunkVisited(Vector2Int) → bool`, `InfinityMapManager.GetChunk(Vector2Int) → TerrainChunk`, `ElevatorManager.layers[i].startDepth`
- Produces: `ElevatorRoomPainter.PaintLandingRoom(int xChunk, int layerIndex)` — `ElevatorManager.TeleportRoutine`이 청크 로드 직후 호출한다.

- [ ] **Step 1: `ElevatorRoomPainter.cs` 생성**

```csharp
// @tags: elevator, chunk, image, painter, landing, room
using UnityEngine;

/// <summary>
/// 엘리베이터 착지 청크를 지층별 방 PNG로 교체한다.
/// 구멍 입구 방은 ImageChunkOverrider가 따로 담당한다 — 두 경로가 서로 다른 방을 쓴다.
///
/// 대상은 엘리베이터가 서 있는 청크 그 자체다.
/// (예전 ElevatorManager는 chunkY-1, 즉 한 칸 아래 청크에 칠하고 있었다.
///  청크 피벗은 좌하단이고 엘리베이터는 로컬 (5,5)이므로
///  FloorToInt((coord.y*10+5)/10) == coord.y 가 맞는 좌표다.)
/// </summary>
public class ElevatorRoomPainter : MonoBehaviour
{
    [Tooltip("지층별 방 PNG. 인덱스 = ElevatorManager.layers의 layerIndex (0 땅 / 1 얼음 / 2 용암 / 3 우주).\n" +
             "1000×1000, Read/Write Enabled 필수. 텍스처 정중앙(500,500)은 엘리베이터 자리이므로 반드시 투명해야 한다.")]
    [SerializeField] private Texture2D[] layerRoomImages;

    [Tooltip("(선택) 지층별 테두리 텍스처. 비우거나 null이면 청크 기본 테두리를 유지한다.")]
    [SerializeField] private Texture2D[] layerBorderTextures;

    /// <summary>
    /// 지정한 X·지층의 엘리베이터 청크를 방 PNG로 교체한다.
    /// 호출 시점에 해당 청크가 이미 로드돼 있어야 한다.
    /// </summary>
    public void PaintLandingRoom(int xChunk, int layerIndex)
    {
        Texture2D room = GetAt(layerRoomImages, layerIndex);
        if (room == null) return;   // 해당 지층은 기본 지형 그대로 둔다

        var elevatorMgr = ElevatorManager.Instance;
        var mapMgr      = InfinityMapManager.Instance;
        if (elevatorMgr == null || mapMgr == null) return;

        if (layerIndex < 0 || layerIndex >= elevatorMgr.layers.Count)
        {
            Debug.LogError($"[ElevatorRoomPainter] 잘못된 layerIndex: {layerIndex}");
            return;
        }

        Vector2Int coord = new Vector2Int(xChunk, elevatorMgr.layers[layerIndex].startDepth);

        // 스킵 1: 다른 컴포넌트가 소유한 보호 좌표.
        // 0층 엘리베이터는 x % spawnInterval == 0 이므로 구멍 입구 (0,0)에도 존재한다.
        // 이 검사가 입구 방을 0층 방 PNG로 덮어쓰는 것을 막는다.
        // (x=5,10,... 의 0층 엘리베이터는 보호 좌표가 아니므로 정상적으로 칠해진다.)
        if (SpecialChunkManager.Instance != null &&
            SpecialChunkManager.Instance.IsProtectedCoord(coord))
            return;

        // 스킵 2: 이미 저장 기록이 있는 청크 = 플레이어가 다녀간 방.
        // 다시 칠하면 판 흔적이 지워지므로 건드리지 않는다.
        if (mapMgr.IsChunkVisited(coord)) return;

        var chunk = mapMgr.GetChunk(coord);
        if (chunk == null)
        {
            Debug.LogWarning($"[ElevatorRoomPainter] 청크 {coord}가 로드돼 있지 않아 방을 칠하지 못했다.");
            return;
        }

        if (!ChunkImagePainter.Paint(chunk, room, GetAt(layerBorderTextures, layerIndex)))
            return;

        // 이후 재로드 시 절차 바위·광물이 방 안에 깔리지 않도록 보호 좌표로 등록한다.
        if (SpecialChunkManager.Instance != null)
            SpecialChunkManager.Instance.RegisterProtectedCoord(coord);

        Debug.Log($"[ElevatorRoomPainter] 착지 방 페인팅 완료 — 청크 {coord}, layer {layerIndex}, 이미지 '{room.name}'");
    }

    /// <summary>배열이 비어 있거나 인덱스를 벗어나면 null을 돌려준다.</summary>
    private static Texture2D GetAt(Texture2D[] arr, int index)
        => (arr != null && index >= 0 && index < arr.Length) ? arr[index] : null;
}
```

- [ ] **Step 2: `ElevatorManager`에 참조 필드 추가**

`[Header("지층 정보")]` 블록 **위**에 넣는다.

```csharp
    [Header("착지 방")]
    [Tooltip("엘리베이터 착지 청크에 지층별 방 PNG를 칠한다. 비워두면 페인팅 없이 기본 지형에 착지한다.")]
    public ElevatorRoomPainter roomPainter;
```

- [ ] **Step 3: `TeleportRoutine`에 페인팅 단계 삽입**

Task 2에서 만든 `TeleportRoutine`의 **3단계(청크 로드 대기)와 4단계(위치 재보정) 사이**에 넣는다.

```csharp
        // 3-B. 착지 방 페인팅.
        //      콜라이더 재생성(5단계의 yield)보다 먼저여야 새 픽셀 기준으로 콜라이더가 만들어진다.
        if (roomPainter != null)
            roomPainter.PaintLandingRoom(xChunk, targetLayer);
```

삽입 후 순서 확인:

```
1. Kinematic 고정
2. 추정 위치로 이동
3. HasChunk(목적지) 대기
3-B. roomPainter.PaintLandingRoom(...)   ← 여기
4. 실제 엘리베이터 위치로 재보정
5. yield 1프레임 → Dynamic 복귀
```

- [ ] **Step 4: 컴파일 확인**

기대: Console 컴파일 에러 0건.

- [ ] **Step 5: 씬 세팅**

`DemoUnderground.unity`에서:

1. Hierarchy 빈 곳 우클릭 → Create Empty → 이름 `ElevatorRoomPainter`
2. `ElevatorRoomPainter` 컴포넌트 부착
3. `Layer Room Images` 크기를 **4**로 설정하고 지층별 PNG 할당
   - `[0]` 땅 / `[1]` 얼음 / `[2]` 용암 / `[3]` 우주
   - 아직 없는 지층은 비워둔다(그 지층은 기본 지형에 착지)
4. `ElevatorManager` 오브젝트를 선택해 `Room Painter` 필드에 위 오브젝트를 연결
5. 씬 저장

**PNG 준비 체크리스트** (각 이미지마다):
- 크기 1000×1000
- Import Settings → **Read/Write Enabled 체크**
- 알파 > 10인 픽셀 = 땅, 그 이하 = 빈 공간
- **정중앙 (500,500) 주변이 투명해야 한다.** 엘리베이터가 서는 자리이며, `ElevatorSpawner`의 굴착 반경은 150px다. 그보다 넉넉히 비운다.
- 중앙 아래에 발판(불투명 픽셀)이 있어야 플레이어가 낙하하지 않는다

- [ ] **Step 6: 전체 검증 (Play 모드)**

설계 문서 §6의 6개 항목을 순서대로 확인한다.

1. **낙하 없음** — 얼음층 이동 후 Player Transform Y ≈ -195
2. **방 위치** — 착지 청크(엘리베이터가 있는 그 청크)에 방 PNG가 칠해졌다. 한 칸 아래가 아니다.
3. **지층별 구분** — 용암층·우주층이 각각 다른 방 모양이다.
4. **입구 방 보존** — 엘리베이터로 여러 층 다녀온 뒤 0층 `x=0`으로 돌아왔을 때 시작 방이 원래 PNG 그대로다. Console에 `[ElevatorRoomPainter] 착지 방 페인팅 완료 — 청크 (0,0)` 로그가 **찍히지 않아야** 한다.
5. **판 흔적 보존** — 얼음층 방을 조금 판 뒤 다른 층 갔다가 돌아온다. 판 자국이 남아 있고, `[ElevatorRoomPainter]` 페인팅 로그가 재출력되지 않는다.
6. **방 안 스폰 없음** — 방 안에 절차 바위·광물이 생기지 않는다.

---

## Task 4: 문서 갱신

**Files:**
- Modify: `CLAUDE.md` (핵심 파일 표)

- [ ] **Step 1: `CLAUDE.md` 핵심 파일 표에 3줄 추가**

표의 마지막 행(`Assets/Docs/`) **바로 위**에 넣는다.

```markdown
| `Assets/Scripts/UI/Interaction/Scene/ChunkImagePainter.cs` | PNG → 청크 픽셀 교체 코어(static). 리샘플링·PixelInfo 생성·`LoadChunkData`·`MarkDirty`까지. 청크 로드 대기와 보호좌표 등록은 호출측 책임 |
| `Assets/Scripts/UI/Interaction/Scene/ImageChunkOverrider.cs` | **구멍 입구** 착지 청크 전용 PNG 교체 + 플레이어 활성화. 좌표는 인스펙터 고정 |
| `Assets/Scripts/UI/Interaction/Scene/ElevatorRoomPainter.cs` | **엘리베이터** 착지 방 PNG 교체. 지층별 `layerRoomImages[]`. 보호좌표·방문청크는 스킵(판 흔적 보존). `ElevatorManager.TeleportRoutine`이 호출 |
```

- [ ] **Step 2: 아키텍처 제약 항목 추가**

`### 12. 골드 증감 지점 추가 시...` 항목 **뒤**에 추가한다.

```markdown
### 13. 플레이어를 먼 좌표로 텔레포트할 때는 착지 보호 필수
청크가 로드되지 않은 좌표로 순간이동시키면 콜라이더가 없어 플레이어가 낙하하고,
몇 백 ms 뒤 청크가 로드되면서 지형 속에 파묻힌다.

`Rigidbody2D`를 Kinematic으로 고정 → `InfinityMapManager.HasChunk(목적지)` 대기 →
1프레임 대기(콜라이더 생성) → Dynamic 복귀 순서를 지킨다.

구현 참고: `ElevatorManager.TeleportRoutine()`, `PlayerSpawner.SpawnRoutine()`
배경: `Assets/Docs/elevator-landing-room.md` §1-A
```

- [ ] **Step 3: 확인**

`CLAUDE.md`를 다시 읽어 표 정렬이 깨지지 않았는지, 항목 번호가 중복되지 않는지 본다.

---

## 자체 점검 결과

**설계 커버리지**

| 설계 항목 | 담당 태스크 |
|---|---|
| §2-A 착지 보호 | Task 2 |
| §2-B 페인팅 코어 추출 (`MarkDirty` 교체 포함) | Task 1 |
| §2-C 두 컴포넌트 분리 (`SetTargetChunk` 제거, overrider 재사용 블록 제거) | Task 1 Step 2, Task 2 Step 2 |
| §2-D 지층별 PNG + 스킵 조건 2개 + 보호좌표 등록 | Task 3 Step 1 |
| §2-E 호출 흐름 순서 | Task 3 Step 3 |
| §3 PNG 제작 제약 | Task 3 Step 5 |
| §4 수정 대상 파일 4개 | Task 1~3 전부 |
| §5 씬 세팅 | Task 3 Step 5 |
| §6 검증 항목 6개 | Task 3 Step 6 |
| 진단 코루틴 제거 | Task 2 Step 1 |

**타입 일관성**: `ChunkImagePainter.Paint(TerrainChunk, Texture2D, Texture2D) → bool` 이 Task 1에서 정의되고 Task 1 Step 2·Task 3 Step 1에서 같은 시그니처로 호출된다. `PaintLandingRoom(int, int)`은 Task 3 Step 1 정의, Step 3 호출로 일치한다. `roomPainter` 필드명은 Step 2 정의, Step 3 사용으로 일치한다.
