using UnityEngine;

/// <summary>
/// LSP: IInteractable 구현 → PlayerInteractor가 광물·엘리베이터를 동일하게 처리한다.
/// SRP: 이 클래스는 시각 피드백 및 ElevatorManager 호출만 담당한다.
///      F 키 입력 처리는 PlayerInteractor에게 위임한다.
/// </summary>
public class ElevatorController : MonoBehaviour, IInteractable, IInteractionPrompt, IMapElevator, IElevatorStop
{
    [Header("엘리베이터 정보")]
    public int xChunkPosition;
    public int yChunkPosition;
    public int layerIndex;

    [Header("상호작용 설정")]
    public float interactionRange = 2f;

    [Header("시각 효과")]
    public SpriteRenderer spriteRenderer;
    public Color idleColor = Color.cyan;
    public Color activeColor = Color.yellow;

    // IInteractable
    public string InteractionPrompt => "엘리베이터";
    // 광물(PickupableItem=0)보다 우선순위 높음 → 같은 범위 내에서 엘리베이터가 먼저 선택됨
    public int InteractionPriority => 10;

    // 근접 안내 문구 (Localization 키로 언어 전환 대응)
    public InteractionPromptInfo GetInteractionPrompt()
        => InteractionPromptInfo.Ok("interact_elevator", "엘리베이터");

    private Transform _player;
    private bool _isPlayerNearby;

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            _player = playerObj.transform;

        if (ElevatorManager.Instance != null)
            ElevatorManager.Instance.RegisterElevator(this);

        // 지도 마커는 '로드'가 아니라 '상호작용 가능 위치 도달/이용' 시점에 찍는다(IMapElevator).
        // PlayerInteractor가 이 엘리베이터를 근접 탐지할 때 MapMarkerRegistry.Discover를 호출한다.

        if (spriteRenderer != null)
            spriteRenderer.color = idleColor;

        Debug.Log($"[ElevatorController] Initialized at X:{xChunkPosition}, Y:{yChunkPosition}, Layer:{layerIndex}");
    }

    private void Update()
    {
        if (_player == null) return;

        // 시각 피드백용 거리 체크 (F 키 처리는 PlayerInteractor가 담당)
        float distance = Vector2.Distance(transform.position, _player.position);
        bool wasNearby = _isPlayerNearby;
        _isPlayerNearby = distance <= interactionRange;

        // 보조 해금 경로 — 주 경로는 PlayerInteractor(콜라이더 겹침)다. 아래 NotifyReached 주석 참고.
        if (_isPlayerNearby && !wasNearby)
            NotifyReached();

        if (spriteRenderer != null)
            spriteRenderer.color = _isPlayerNearby ? activeColor : idleColor;
    }

    /// <summary>
    /// 이 층 정류장 해금 — "직접 가봤다"의 기록(<see cref="ElevatorStopUnlockStore"/>).
    ///
    /// <b>주 호출자는 <c>PlayerInteractor</c></b>(콜라이더 겹침 = 지도 마커 IMapElevator와 같은 기준)이고,
    /// E를 실제로 누른 <see cref="Interact"/>와 이 컴포넌트의 근접 감지가 보조로 따라붙는다.
    ///
    /// 왜 근접 감지만으로는 부족한가: <see cref="interactionRange"/>(2유닛)는 <b>transform 중심</b>
    /// (청크 로컬 5,5)에서 재는 거리인데, PlayerInteractor는 엘리베이터 <b>콜라이더 전체</b>와 겹치는지를 본다.
    /// 엘리베이터는 세로로 길어서 발치에 서면 E는 눌리는데 중심까지는 2유닛을 넘는다 —
    /// 그래서 "가서 UI까지 열었는데 해금이 안 되는" 상태가 됐다. 기준을 지도 마커와 일치시킨다.
    /// </summary>
    public int StopDepth      => yChunkPosition;
    public int StopXChunk     => xChunkPosition;
    public int StopLayerIndex => layerIndex;
    public Transform StopTransform => transform;

    public void NotifyReached()
    {
        if (ElevatorStopUnlockStore.Unlock(yChunkPosition))
        {
            Debug.Log($"[ElevatorController] 정류장 해금: Layer {layerIndex} (깊이 {yChunkPosition})");
            GuideManager.Trigger("elevator_basic");   // 처음 해금한 정류장에서 한 번만
        }
    }

    /// <summary>
    /// PlayerInteractor가 F 키 입력 시 호출한다.
    /// </summary>
    public void Interact(GameObject interactor)
    {
        Debug.Log($"[ElevatorController] Player interacted at Layer {layerIndex}");
        NotifyReached();   // E를 눌렀다면 확실히 가본 것이다
        if (ElevatorManager.Instance != null)
            ElevatorManager.Instance.OpenElevatorUI(xChunkPosition, layerIndex);
    }

    public void TeleportTo(int targetLayer)
    {
        if (ElevatorManager.Instance != null)
            ElevatorManager.Instance.TeleportPlayer(xChunkPosition, targetLayer);
    }

    private void OnDestroy()
    {
        if (ElevatorManager.Instance != null)
            ElevatorManager.Instance.UnregisterElevator(this);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}
