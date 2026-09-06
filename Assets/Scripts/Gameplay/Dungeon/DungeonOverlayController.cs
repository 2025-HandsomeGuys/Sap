// @tags: dungeon, overlay, prefab, teleport, controller, fade
using System.Collections;
using UnityEngine;

/// <summary>
/// 던전을 "지하씬을 켜둔 채" 오버레이하는 컨트롤러. 던전 하나 = 프리팹 하나.
/// 진입 시 던전 프리팹을 먼 offset에 Instantiate하고 플레이어를 그 입구(DungeonEntryPoint)로 텔레포트한다.
/// 이탈 시 인스턴스를 Destroy하고 플레이어를 문 위치로 되돌린다. 씬 로드/저장/복원 불필요.
///
///  - 지하 지형과 물리 충돌 방지: 던전을 먼 offset에 배치(Unity 2D 물리는 씬/오브젝트 무관 전역 하나).
///  - InfinityMapManager는 문 위치의 더미 앵커를 추적 → 지하 청크 유지 + 던전 좌표엔 지형 미생성.
///  - "들어간 던전만" 존재 → 상태(부순 rock·보상) 격리가 자동. DungeonStateStore는 문 좌표로 인스턴스 구분.
///
/// 진입/이탈은 페이드아웃 → 처리 → 페이드인(ScreenFader)으로 감싼다.
/// </summary>
public class DungeonOverlayController : MonoBehaviour
{
    private static DungeonOverlayController _instance;

    /// <summary>필요 시 자동 생성되는 싱글톤. 에디터 배치 불필요.</summary>
    public static DungeonOverlayController Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("DungeonOverlayController");
                _instance = go.AddComponent<DungeonOverlayController>();
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private const float FadeDuration = 0.7f;

    // 지하 지형이 생성되지 않는 먼 좌표. 던전 프리팹을 여기에 Instantiate한다.
    private static readonly Vector3 DungeonOffset = new Vector3(0f, 10000f, 0f);

    // 던전 동안 카메라를 이만큼 위로 올려 플레이어를 화면 아래쪽에 둔다(마리오식 플랫포머 구도).
    // orthographicSize 2.5(=화면 높이 5유닛) 기준 1.2면 플레이어가 중앙보다 약 24% 아래.
    private const float DungeonCameraYOffset = 1.2f;

    // 진입 인트로(카메라가 입구 아래에서 떠오름) 파라미터. 상승 폭이 크면 "위로 솟구쳤다 멈추는" 느낌이 난다.
    // IntroRiseDistance = 0이면 인트로 없이 입구로 즉시 컷.
    private const float IntroRiseDistance = 0f;
    private const float IntroRiseDuration = 0f;

    public bool InDungeon { get; private set; }

    /// <summary>던전 안인지 — 인스턴스를 자동 생성하지 않고 확인(맵 차단·미니맵 No Signal용).</summary>
    public static bool IsInDungeon => _instance != null && _instance.InDungeon;

    private bool _busy; // 전환(페이드/생성) 진행 중

    private GameObject _player;
    private Vector3 _returnPosition;
    private Transform _prevTrackedPlayer;
    private GameObject _anchorObj;
    private GameObject _dungeonInstance;
    private PlayerVisionOverlay _visionOverlay; // 던전 중 끄고 복귀 시 되살릴 시야 오버레이
    private Unity.Cinemachine.CinemachineConfiner2D _suspendedConfiner; // 던전 중 일시 비활성화한 컨파이너
    private Unity.Cinemachine.CinemachineFollow _offsetBody;            // 던전 중 FollowOffset을 올려둔 바디
    private Vector3 _savedFollowOffset;

    /// <summary>던전 진입. dungeonPrefab = 이 문이 여는 던전 지오메트리 프리팹.</summary>
    public void EnterDungeon(GameObject dungeonPrefab, Vector2Int instanceCoord)
    {
        if (InDungeon || _busy) return;
        if (dungeonPrefab == null)
        {
            Debug.LogWarning("[DungeonOverlay] dungeonPrefab이 비어있습니다.");
            return;
        }

        _player = GameObject.FindGameObjectWithTag("Player");
        if (_player == null)
        {
            Debug.LogError("[DungeonOverlay] 'Player' 태그 오브젝트를 찾지 못했습니다.");
            return;
        }

        StartCoroutine(EnterRoutine(dungeonPrefab, instanceCoord));
    }

    private IEnumerator EnterRoutine(GameObject dungeonPrefab, Vector2Int coord)
    {
        _busy = true;
        yield return Fade(true); // 어두워짐

        DungeonStateStore.SetCurrentInstance(coord);
        MineralItemController.SuppressVoidFreeze = true; // 청크 없는 던전에서 광물이 얼지 않고 낙하하도록
        _returnPosition = _player.transform.position;

        // InfinityMapManager가 문 위치를 계속 추적하도록 더미 앵커로 교체.
        // 플레이어를 멀리 옮겨도 (1) 지하 청크가 언로드되지 않고 (2) 던전 좌표엔 지형이 생성되지 않는다.
        var map = InfinityMapManager.Instance;
        if (map != null)
        {
            _prevTrackedPlayer = map.player;
            _anchorObj = new GameObject("DungeonMapAnchor");
            _anchorObj.transform.position = _returnPosition;
            map.player = _anchorObj.transform;
        }

        // 던전 프리팹을 먼 offset에 생성. 텔레포트는 카메라 컷 이후에 한다(아래 주석 참고).
        _dungeonInstance = Instantiate(dungeonPrefab, DungeonOffset, Quaternion.identity);
        Vector3 entryPos = FindEntry(_dungeonInstance, DungeonOffset);

        // --- 카메라 셋업: 반드시 플레이어 텔레포트보다 '먼저', 같은 프레임에 끝낸다 ---
        // 순서가 반대이면(텔레포트 → 다음 프레임에 컷) 그 사이 한 프레임 동안 Cinemachine이
        // 아직 플레이어를 Follow하는 상태로 지하(y≈-30) → 던전(y≈10000)의 10,000유닛 점프를
        // damping으로 추격하기 시작한다. 그 잔여 이동/속도가 "위로 솟구쳤다 돌아오는" 움직임이 된다.
        // 컷을 먼저 끝내면 LateUpdate(=Cinemachine 갱신) 시점엔 이미 더미를 보고 있어 추격이 없다.
        var vCam = FindFirstObjectByType<Unity.Cinemachine.CinemachineCamera>();
        Transform oldFollow = null;
        GameObject dummyTarget = null;
        Unity.Cinemachine.CinemachineFollow followBody = null;
        Vector3 savedDamping = Vector3.zero;
        bool dampingOverridden = false;

        if (vCam != null)
        {
            // 컨파이너가 지상 경계(y≈0)로 카메라를 가두면 던전(y=10000)으로 올라오지 못하고
            // 파이프 상승 연출도 경계 밖이라 전부 클램프되어 무효화된다. → 던전 동안 비활성화.
            var confiner = vCam.GetComponent<Unity.Cinemachine.CinemachineConfiner2D>();
            if (confiner != null && confiner.enabled)
            {
                _suspendedConfiner = confiner;
                confiner.enabled = false;
            }

            oldFollow = vCam.Follow;

            // CinemachineFollow의 PositionDamping(=1)이 크면 더미 상승(1초)을 카메라가 거의 따라오지 못해
            // 연출이 뭉개진다. 인트로 동안만 댐핑을 0으로 두어 더미를 정밀 추종시킨 뒤 복원한다.
            followBody = vCam.GetComponent<Unity.Cinemachine.CinemachineFollow>();
            if (followBody != null)
            {
                savedDamping = followBody.TrackerSettings.PositionDamping;
                var ts = followBody.TrackerSettings;
                ts.PositionDamping = Vector3.zero;
                followBody.TrackerSettings = ts;
                dampingOverridden = true;

                // 던전 구도: 카메라를 위로 올려 플레이어를 화면 아래쪽에 둔다.
                // 인트로가 어차피 카메라를 컷(PreviousStateIsValid=false)하므로 즉시 적용해도 튀지 않는다.
                _offsetBody = followBody;
                _savedFollowOffset = followBody.FollowOffset;
                followBody.FollowOffset = _savedFollowOffset + new Vector3(0f, DungeonCameraYOffset, 0f);
            }

            // 더미 앵커 생성 (시작 위치: 입구보다 IntroRiseDistance 아래)
            dummyTarget = new GameObject("DungeonIntroCameraTarget");
            dummyTarget.transform.position = entryPos + new Vector3(0f, -IntroRiseDistance, 0f);
            vCam.Follow = dummyTarget.transform;
            vCam.PreviousStateIsValid = false; // 즉시 더미 위치로 카메라 컷 (텔레포트 추격 없음)
        }

        TeleportPlayer(entryPos);
        InDungeon = true;
        SetVisionOverlayActive(false); // 던전 안에선 시야 제한 오버레이 끔

        // 던전 지오메트리는 Default/0(타일맵)·Objects·Map 레이어를 쓴다. 지하 모드(Default 음수 order)를
        // 그대로 들고 오면 플레이어가 던전 바닥·벽 타일맵에 통째로 가려진다 → 지상 정렬로 되돌린다.
        SetPlayerUndergroundSorting(false);

        // --- 마리오 파이프 진입 스타일: 카메라가 발 아래에서 올라옴 ---
        if (vCam != null)
        {
            // 페이드 인 시작 (대기하지 않고 카메라 연출과 동시 진행)
            StartCoroutine(Fade(false));

            float elapsed = 0f;
            float duration = IntroRiseDuration;
            Vector3 startPos = dummyTarget.transform.position;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                dummyTarget.transform.position = Vector3.Lerp(startPos, entryPos, t);
                yield return null;
            }

            // 원래 타겟으로 복구. 인트로 1초 동안 플레이어가 낙하해 입구보다 아래에 있을 수 있으므로
            // 여기서도 컷을 넣어 카메라가 그 차이만큼 다시 내려오는(=되돌아오는) 움직임을 막는다.
            vCam.Follow = oldFollow;
            vCam.PreviousStateIsValid = false;
            Destroy(dummyTarget);

            // 댐핑 복원
            if (dampingOverridden)
            {
                var ts = followBody.TrackerSettings;
                ts.PositionDamping = savedDamping;
                followBody.TrackerSettings = ts;
            }
        }
        else
        {
            yield return Fade(false); // 밝아짐
        }

        _busy = false;
    }

    /// <summary>던전 이탈. 플레이어를 문 위치로 되돌리고 던전 인스턴스를 파괴한다.</summary>
    public void ExitDungeon()
    {
        if (!InDungeon || _busy) return;
        StartCoroutine(ExitRoutine());
    }

    private IEnumerator ExitRoutine()
    {
        _busy = true;
        yield return Fade(true);

        TeleportPlayer(_returnPosition);
        
        // 진입 때 올려둔 던전 구도 오프셋 복원 (카메라 컷보다 먼저 — 복원된 오프셋으로 스냅되도록)
        if (_offsetBody != null)
        {
            _offsetBody.FollowOffset = _savedFollowOffset;
            _offsetBody = null;
        }

        // 텔레포트 후 카메라가 먼 거리를 부드럽게 이동(Swoop)하는 것을 방지하기 위해 즉시 위치 갱신
        var vCam = FindFirstObjectByType<Unity.Cinemachine.CinemachineCamera>();
        if (vCam != null) vCam.PreviousStateIsValid = false;

        // 진입 때 꺼둔 컨파이너 복원
        if (_suspendedConfiner != null)
        {
            _suspendedConfiner.enabled = true;
            _suspendedConfiner.InvalidateBoundingShapeCache();
            _suspendedConfiner = null;
        }

        if (_dungeonInstance != null) Destroy(_dungeonInstance);
        _dungeonInstance = null;
        RestoreTracking();
        MineralItemController.SuppressVoidFreeze = false;

        InDungeon = false;
        SetVisionOverlayActive(true); // 지하로 복귀 → 시야 오버레이 복원
        SetPlayerUndergroundSorting(true); // 지하 지형(Default/0) 뒤로 다시 내림
        yield return null;
        yield return Fade(false);
        _busy = false;
    }

    // 플레이어 정렬 모드 전환. 컨트롤러가 없으면(구 프리팹 등) 무시 — 정렬은 기존 그대로 동작한다.
    private static void SetPlayerUndergroundSorting(bool on)
    {
        var sorting = PlayerSortingController.Instance;
        if (sorting != null) sorting.SetUnderground(on);
    }

    // 시야 제한 오버레이 on/off. 씬에 없으면 무시.
    private void SetVisionOverlayActive(bool active)
    {
        if (_visionOverlay == null)
            _visionOverlay = FindFirstObjectByType<PlayerVisionOverlay>(FindObjectsInactive.Include);

        if (_visionOverlay != null) _visionOverlay.SetOverlayActive(active);
    }

    private IEnumerator Fade(bool toBlack)
    {
        var f = ScreenFader.Instance;
        if (f == null) yield break;
        yield return toBlack ? f.FadeOut(FadeDuration) : f.FadeIn(FadeDuration);
    }

    private void RestoreTracking()
    {
        var map = InfinityMapManager.Instance;
        if (map != null && _prevTrackedPlayer != null) map.player = _prevTrackedPlayer;
        if (_anchorObj != null) Destroy(_anchorObj);
        _anchorObj = null;
        _prevTrackedPlayer = null;
    }

    private Vector3 FindEntry(GameObject dungeonInstance, Vector3 fallback)
    {
        var entry = dungeonInstance.GetComponentInChildren<DungeonEntryPoint>();
        if (entry != null) return entry.transform.position;

        Debug.LogWarning("[DungeonOverlay] 던전 프리팹에서 DungeonEntryPoint를 못 찾음 — offset 위치로 폴백.");
        return fallback;
    }

    private void TeleportPlayer(Vector3 pos)
    {
        if (_player == null) return;
        var rb = _player.GetComponentInChildren<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
        _player.transform.position = pos;

        // 좌표 대입은 물리 이동이 아니다 — 이음매 감시기가 TUNNEL로 오인해
        // 버그 리포트 창을 자동으로 띄우지 않도록 잠시 재운다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
        GameDiagnostics.TerrainSeamWatchdog.SuppressFor();
#endif
    }
}
