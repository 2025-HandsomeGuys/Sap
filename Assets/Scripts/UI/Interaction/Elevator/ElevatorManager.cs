using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 엘리베이터 시스템 전체를 관리하는 싱글톤 매니저
/// - 엘리베이터 등록/해제
/// - 지층 정보 관리
/// - 플레이어 텔레포트
/// - UI 연동
/// </summary>
public class ElevatorManager : MonoBehaviour
{
    public static ElevatorManager Instance { get; private set; }

    [Header("설정")]
    public GameObject elevatorPrefab;  // 엘리베이터 프리팹

    // 엘리베이터가 설 청크 X는 층마다 하나씩 ElevatorStopLayout이 정한다.
    // 예전의 elevatorSpawnInterval·elevatorSpawnOffsetX(X축 몇 칸마다 기둥을 세울지)는
    // 같은 높이에 엘리베이터가 여러 개 생기는 구조였어서 제거했다.

    [Header("착지 방")]
    [Tooltip("엘리베이터 착지 청크에 지층별 방 PNG를 칠한다. 비워두면 페인팅 없이 기본 지형에 착지한다.")]
    public ElevatorRoomPainter roomPainter;

    [Header("지층 정보")]
    public List<LayerInfo> layers = new List<LayerInfo>();

    [Header("연출")]
    [Tooltip("층 이동 시 화면 페이드 아웃/인에 걸리는 시간(초). 던전 진입/이탈과 동일한 ScreenFader를 쓴다.")]
    public float fadeDuration = 0.5f;

    // [내부 데이터] X 좌표별 엘리베이터 리스트
    // Key: X 청크 좌표, Value: 해당 X에 있는 모든 엘리베이터들
    // 값 타입이 IElevatorStop인 이유: 엘리베이터 구현이 두 갈래다(구 ElevatorController /
    // 통합 WorldInteractable). 실제 프리팹에는 후자만 붙어 있어서 ElevatorController로 잠그면
    // 등록이 통째로 비고, 그러면 목록이 전부 '미로드'가 되고 착지 좌표도 추정값만 쓴다.
    private Dictionary<int, List<IElevatorStop>> elevatorsByX = new Dictionary<int, List<IElevatorStop>>();

    private Transform player;

    // 페이드 도중 버튼이 다시 눌려 텔레포트가 겹치는 것을 막는다.
    private bool _teleporting;

    void Awake()
    {
        // 싱글톤 패턴
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[ElevatorManager] Duplicate ElevatorManager detected on {gameObject.name}! Destroying COMPONENT only. Please remove ElevatorManager from the Elevator Prefab.");
            Destroy(this); // Safely remove component, keep the elevator active
            return;
        }
        Instance = this;
        // Don't DestroyOnLoad by default unless intended. If it survives scene load, it should be root.
        // transform.SetParent(null);
        // DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // TileDataManager는 Awake에서 초기화되므로 Start에서 읽어야 안전
        InitializeLayers();

        // 플레이어 찾기
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
    }

    /// <summary>
    /// 엘리베이터 정류장 목록 초기화.
    /// 지층마다 상층·중층·하층 3개씩 만든다(지층 두께를 3등분).
    /// → 4지층 × 3 = 12정류장. layers의 인덱스가 곧 layerIndex이자 UI 버튼 순서다.
    /// </summary>
    void InitializeLayers()
    {
        // 정류장 계산은 지상 엘리베이터 입구 UI와 공유한다(ElevatorLayerCatalog).
        // 좌표가 같으면 ShouldSpawnElevator·GetLayerIndexByDepth가 구분하지 못하므로
        // 깊이가 겹치는 정류장은 카탈로그에서 이미 제외된다.
        layers = ElevatorLayerCatalog.Build();

        Debug.Log($"[ElevatorManager] Initialized {layers.Count} stops (depths: {string.Join(", ", layers.ConvertAll(l => l.startDepth))})");
    }

    /// <summary>
    /// 엘리베이터 등록
    /// </summary>
    public void RegisterElevator(IElevatorStop elevator)
    {
        int x = elevator.StopXChunk;

        if (!elevatorsByX.ContainsKey(x))
        {
            elevatorsByX[x] = new List<IElevatorStop>();
        }

        if (!elevatorsByX[x].Contains(elevator))
        {
            elevatorsByX[x].Add(elevator);
            Debug.Log($"[ElevatorManager] Registered elevator at X:{x}, Layer:{elevator.StopLayerIndex}. Total at X={x}: {elevatorsByX[x].Count}");
        }
    }

    /// <summary>
    /// 엘리베이터 등록 해제
    /// </summary>
    public void UnregisterElevator(IElevatorStop elevator)
    {
        int x = elevator.StopXChunk;

        if (elevatorsByX.ContainsKey(x))
        {
            elevatorsByX[x].Remove(elevator);
            if (elevatorsByX[x].Count == 0)
            {
                elevatorsByX.Remove(x);
            }
        }
    }

    /// <summary>
    /// 특정 X 좌표의 모든 엘리베이터 가져오기
    /// </summary>
    public List<IElevatorStop> GetElevatorsAtX(int xChunk)
    {
        if (elevatorsByX.ContainsKey(xChunk))
        {
            return elevatorsByX[xChunk];
        }
        return new List<IElevatorStop>();
    }

    /// <summary>
    /// 특정 X, 특정 지층의 엘리베이터 찾기
    /// </summary>
    public IElevatorStop GetElevatorAt(int xChunk, int layerIndex)
    {
        var elevators = GetElevatorsAtX(xChunk);

        for (int i = 0; i < elevators.Count; i++)
        {
            var e = elevators[i];
            if (e == null || e.StopLayerIndex != layerIndex) continue;

            // 정적 타입이 인터페이스면 Unity의 == 오버로드를 안 타서 파괴된 오브젝트가 null로 안 잡힌다.
            // 등록 해제는 OnDisable에서 하지만, 그 사이에 낀 프레임까지 막으려면 여기서 한 번 더 본다.
            if (e is Component c && c == null) continue;

            return e;
        }

        return null;
    }

    /// <summary>
    /// 엘리베이터 UI 열기
    /// </summary>
    public void OpenElevatorUI(int xChunk, int currentLayer)
    {
        Debug.Log($"[ElevatorManager] Opening elevator UI at X:{xChunk}, Current Layer:{currentLayer}");

        // 코드 생성 오버레이가 씬에 있으면 그쪽이 우선 (구 ElevatorUI 프리팹은 폴백)
        var overlay = ElevatorOverlayUI.FindInScene();
        if (overlay != null)
        {
            overlay.Show(xChunk, currentLayer);
            return;
        }

        // UI 패널 표시
        if (ElevatorUI.Instance != null)
        {
            ElevatorUI.Instance.ShowPanel(xChunk, currentLayer);
        }
        else
        {
            Debug.LogError("[ElevatorManager] ElevatorUI.Instance is null! Make sure ElevatorUI is in the scene.");
            
            // Fallback: 콘솔에 정보 표시
            var availableElevators = GetElevatorsAtX(xChunk);
            Debug.Log($"[ElevatorManager] Available elevators at X={xChunk}:");
            
            foreach (var elev in availableElevators)
            {
                if (elev.StopLayerIndex >= 0 && elev.StopLayerIndex < layers.Count)
                {
                    var layer = layers[elev.StopLayerIndex];
                    Debug.Log($"  - Layer {layer.layerIndex}: {layer.layerName} (Y:{layer.startDepth})");
                }
            }
        }
    }

    /// <summary>
    /// 플레이어를 특정 지층으로 텔레포트.
    /// 실제 이동은 TeleportRoutine이 수행한다 — 목적지 청크가 로드될 때까지
    /// 플레이어를 Kinematic으로 묶어야 하므로 프레임을 넘겨야 한다.
    /// </summary>
    public void TeleportPlayer(int xChunk, int targetLayer)
    {
        if (_teleporting) return;   // 페이드 진행 중 중복 호출 차단

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

        // 안전망 — UI가 잠긴 줄을 막지만, 다른 경로(디버그·유물 등)로 들어와도 규칙은 지킨다.
        // 해금은 '그 층 엘리베이터에 직접 가본 적 있음'이다(ElevatorStopUnlockStore).
        if (!ElevatorStopUnlockStore.IsUnlocked(layers[targetLayer]))
        {
            Debug.LogWarning($"[ElevatorManager] 아직 가본 적 없는 정류장이라 이동할 수 없다: " +
                             $"{layers[targetLayer].layerName} (깊이 {layers[targetLayer].startDepth})");
            return;
        }

        StartCoroutine(TeleportRoutine(xChunk, targetLayer));
    }

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
    ///
    /// 이 전 과정(청크 로드 대기·방 페인팅·카메라 컷)을 던전 진입/이탈과 동일하게
    /// 페이드아웃 → 이동 → 페이드인으로 감싼다. 이동이 끝난 시점에 페이드인이 실행되므로
    /// 로딩 중 지형이 채워지는 모습이나 카메라 점프가 화면에 보이지 않는다.
    /// </summary>
    private IEnumerator TeleportRoutine(int xChunk, int targetLayer)
    {
        const float CHUNK_LOAD_TIMEOUT = 5f;

        // 목적지 층의 X로 갈아탄다 — 층마다 엘리베이터 X가 달라서 xChunk(지금 선 층의 X)를
        // 그대로 쓰면 엘리베이터가 없는 생지형 한복판에 내린다.
        // 이 뒤의 착지 좌표·청크 로드 대기·방 페인팅·엘리베이터 조회가 전부 이 값을 따른다.
        xChunk = GetStopXForLayer(targetLayer);

        var targetLayerInfo = layers[targetLayer];
        Vector2Int targetCoord = new Vector2Int(xChunk, targetLayerInfo.startDepth);

        _teleporting = true;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.ElevatorMove);

        // 0. 화면 어둡게 — 이후 모든 이동/로딩을 가린다.
        yield return Fade(true);

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
            ? targetElevator.StopTransform.position
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

        // 3-B. 착지 방 페인팅.
        //      콜라이더 재생성(5단계의 yield)보다 먼저여야 새 픽셀 기준으로 콜라이더가 만들어진다.
        if (roomPainter != null)
            roomPainter.PaintLandingRoom(xChunk, targetLayer);

        // 4. 로드된 실제 엘리베이터 위치로 재보정.
        //    추정 좌표와 현재는 일치하지만, 향후 엘리베이터 배치 규칙이 바뀌어도 어긋나지 않게 한다.
        targetElevator = GetElevatorAt(xChunk, targetLayer);
        if (targetElevator != null)
            player.position = targetElevator.StopTransform.position;

        // 4-B. 카메라 컷 — 먼 거리를 damping으로 따라오며 훑는(swoop) 것을 막는다.
        //      던전 진입/이탈과 동일하게 텔레포트 직후 상태를 무효화해 즉시 스냅시킨다.
        var vCam = FindFirstObjectByType<Unity.Cinemachine.CinemachineCamera>();
        if (vCam != null) vCam.PreviousStateIsValid = false;

        // 5. 콜라이더 생성 대기 후 물리 복귀
        yield return null;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Dynamic;
        }

        Debug.Log($"[ElevatorManager] Teleported player to X:{xChunk}, Layer:{targetLayer} at {player.position} (대기 {waited:F2}초)");

        // 6. 이동 완료 → 화면 복구
        yield return Fade(false);
        _teleporting = false;
    }

    /// <summary>
    /// 화면 페이드 헬퍼. ScreenFader가 씬에 없으면 아무것도 하지 않고 즉시 진행한다.
    /// </summary>
    private IEnumerator Fade(bool toBlack)
    {
        var fader = ScreenFader.Instance;
        if (fader == null) yield break;
        yield return toBlack ? fader.FadeOut(fadeDuration) : fader.FadeIn(fadeDuration);
    }

    /// <summary>
    /// 엘리베이터 위치 계산 (청크 좌표 -> 월드 좌표).
    /// 목표 엘리베이터가 아직 로드되지 않았을 때의 착지 위치 추정용.
    /// 로드된 엘리베이터의 transform.position과 반드시 일치해야 착지가 어긋나지 않는다.
    /// </summary>
    public Vector2 CalculateElevatorPosition(int xChunk, int yChunk)
    {
        // InfinityMapManager의 청크 크기 참조
        if (InfinityMapManager.Instance != null)
        {
            float chunkWidthWorld = InfinityMapManager.Instance.chunkWidthWorld;
            float chunkHeightWorld = InfinityMapManager.Instance.chunkHeightWorld;

            // 청크 피벗은 좌하단(0,0) (ChunkCoords 규칙). 엘리베이터는 ElevatorSpawner가
            // 청크 로컬 ChunkLocalPosition에 배치하므로, 그 로컬 오프셋을 그대로 더해야
            // 로드된 엘리베이터(transform.position)와 착지 위치가 정확히 일치한다.
            // (청크 중앙 가정으로 하드코딩하면 엘리베이터 위치를 옮겼을 때 어긋난다.)
            float worldX = xChunk * chunkWidthWorld  + ElevatorSpawner.ChunkLocalPosition.x;
            float worldY = yChunk * chunkHeightWorld + ElevatorSpawner.ChunkLocalPosition.y;

            return new Vector2(worldX, worldY);
        }

        Debug.LogError("[ElevatorManager] InfinityMapManager.Instance is null!");
        return Vector2.zero;
    }

    /// <summary>
    /// 청크 좌표가 엘리베이터를 생성해야 하는 위치인지 확인.
    ///
    /// Y가 정류장 깊이와 일치하고, X가 <b>그 층에 배정된 유일한 X</b>와 같아야 한다.
    /// 층당 엘리베이터는 하나다 — X 계산은 <see cref="ElevatorStopLayout"/>이 단독으로 갖는다.
    /// </summary>
    public bool ShouldSpawnElevator(int xChunk, int yChunk)
    {
        // Y 좌표가 정류장 깊이와 일치하는지 먼저 확인 (지층마다 상층·중층·하층 3개)
        int layerIndex = GetLayerIndexByDepth(yChunk);
        if (layerIndex < 0) return false;

        return ElevatorStopLayout.XForLayer(layerIndex) == xChunk;
    }

    /// <summary>그 층 정류장의 청크 X. 층 이동·지상 진입이 착지 좌표를 잡을 때 쓴다.</summary>
    public int GetStopXForLayer(int targetLayerIndex) => ElevatorStopLayout.XForLayer(targetLayerIndex);

    /// <summary>
    /// 지층 Y 좌표로 레이어 인덱스 찾기
    /// </summary>
    public int GetLayerIndexByDepth(int yChunk)
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i].startDepth == yChunk)
            {
                return i;
            }
        }
        return -1;
    }
}
