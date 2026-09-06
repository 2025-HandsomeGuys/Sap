using System.Collections;
using UnityEngine;

/// <summary>
/// 구멍 입구(지상→지하 진입)의 착지 청크를 PNG 이미지로 교체한다.
/// 투명(alpha=0) = 빈 공간, 불투명(alpha>0) = 땅.
/// 교체 완료 후 씬에 비활성으로 배치된 플레이어를 활성화한다.
///
/// 이 청크는 보호 좌표라 절차 데코(바위·광물)가 스킵되므로, 광물은
/// IChunkPostLoadPainter 훅에서 "페인팅 직후"에 직접 깐다(spawnMinerals).
/// 청크가 언로드→재로드돼도 훅이 매 로드마다 불리므로 광물이 계속 유지된다.
///
/// 엘리베이터 착지 방은 이 컴포넌트가 아니라 ElevatorRoomPainter가 담당한다.
/// (예전에는 ElevatorManager가 이 인스턴스를 SetTargetChunk로 재사용해
///  엘리베이터 착지 지점에도 입구와 같은 PNG가 칠해졌다.)
/// </summary>
[DefaultExecutionOrder(-40)]
public class ImageChunkOverrider : MonoBehaviour, IChunkPostLoadPainter
{
    private const float CHUNK_LOAD_TIMEOUT = 10f;

    [SerializeField] private Vector2Int targetChunkCoord;
    [SerializeField] private Texture2D  terrainImage;       // 1000×1000, Read/Write Enabled
    [SerializeField] private Texture2D  borderTexture;      // (선택) null이면 생성 시 할당된 텍스처 유지
    [SerializeField] private GameObject playerObj;          // 씬에 비활성 상태로 배치된 Player

    [Tooltip("페인팅 직후 이 청크에 절차 광물을 생성한다. 지층 tileData의 광물 규칙을 그대로 따른다.")]
    [SerializeField] private bool spawnMinerals = true;

    /// <summary>이 컴포넌트가 소유한 청크 좌표. ElevatorRoomPainter가 침범하지 않도록 보호 좌표로 등록된다.</summary>
    public Vector2Int TargetChunkCoord => targetChunkCoord;

    /// <summary>첫 페인팅 완료 여부 — 플레이어 활성화 대기와 재로드 판정에 쓰인다.</summary>
    private bool _paintedOnce;

    private void Awake()
    {
        SpecialChunkManager.Instance.RegisterProtectedCoord(targetChunkCoord);
        SpecialChunkManager.Instance.RegisterPostLoadPainter(targetChunkCoord, this);

        // 굴 판정용 등록. 보호 좌표와 따로 두는 이유: 저쪽은 "데코를 깔지 마라"는 런타임 상태고
        // 이쪽은 "이 좌표는 원래 굴을 안 판다"는 배치 정보다. 굴 판정은 이웃 청크도 물어보므로
        // **칠하는 시점이 아니라 Awake 에** 서 있어야 로드 순서와 무관하게 답이 같다.
        CaveCarveLayout.RegisterPaintedCoord(targetChunkCoord);
    }

    private void OnDestroy()
    {
        if (SpecialChunkManager.Instance != null)
        {
            SpecialChunkManager.Instance.UnregisterProtectedCoord(targetChunkCoord);
            SpecialChunkManager.Instance.UnregisterPostLoadPainter(targetChunkCoord, this);
        }

        CaveCarveLayout.UnregisterPaintedCoord(targetChunkCoord);
    }

    private void Start() => StartCoroutine(ActivatePlayerRoutine());

    /// <summary>
    /// 청크 로드 파이프라인(Phase 2)이 매 로드마다 호출한다 — 첫 로드·재로드 공통.
    ///
    /// [순서 필수] 페인팅 → 광물. 광물 자리 판정(MineralGenerator.IsWellSupported)이
    /// 페인팅 '후' 픽셀을 봐야 방 공동에 뜬 광물이 지지검사에 탈락해 튀어나오지 않는다.
    /// 또 ChunkImagePainter가 PixelInfo를 통째로 덮으므로, 먼저 깔면 광물 마커(3)와
    /// 수집 마커(4)가 지워져 캔 광물이 계속 되살아난다.
    /// </summary>
    public void OnChunkLoaded(TerrainChunk chunk, Vector2Int coord)
    {
        if (ShouldPaint(coord))
        {
            if (!ChunkImagePainter.Paint(chunk, terrainImage, borderTexture))
            {
                _paintedOnce = true; // 실패 원인은 Painter가 로그로 남긴다. 플레이어는 붙잡지 않는다.
                return;
            }
        }

        if (spawnMinerals && InfinityMapManager.Instance != null)
            InfinityMapManager.Instance.SpawnMineralsInChunk(coord);

        _paintedOnce = true;
    }

    /// <summary>
    /// 첫 로드는 무조건 칠한다. 재로드는 세이브 기록이 있을 때(=플레이어가 판 흔적이 있을 때)만
    /// 건너뛴다 — 다시 칠하면 파놓은 지형이 원래 PNG로 되돌아간다.
    /// 기록이 없으면 절차 지형으로 새로 생성된 것이므로 다시 칠해야 방이 유지된다.
    /// </summary>
    private bool ShouldPaint(Vector2Int coord)
    {
        if (!_paintedOnce) return true;
        var mgr = InfinityMapManager.Instance;
        return mgr == null || !mgr.IsChunkVisited(coord);
    }

    /// <summary>
    /// 첫 페인팅이 끝나면 플레이어를 활성화한다.
    /// 타임아웃 시에도 활성화한다 — 텍스처 설정 실수로 게임이 영영 시작되지 않는 상태를 막는다.
    /// </summary>
    private IEnumerator ActivatePlayerRoutine()
    {
        if (InfinityMapManager.Instance == null)
        {
            Debug.LogError("[ImageChunkOverrider] InfinityMapManager.Instance가 null! 씬에 InfinityMapManager가 없거나 아직 초기화되지 않음.");
            ActivatePlayer();
            yield break;
        }

        float waited = 0f;
        while (!_paintedOnce)
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

        // 콜라이더 재생성 대기
        yield return null;

        ActivatePlayer();
    }

    private void ActivatePlayer()
    {
        if (playerObj != null)
            playerObj.SetActive(true);
    }
}
