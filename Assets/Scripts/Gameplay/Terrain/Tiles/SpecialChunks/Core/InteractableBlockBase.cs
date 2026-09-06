// @tags: base, interaction, puzzle, block, special_chunk
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// [OCP/LSP] 파괴 불가능하고 상호작용(F키 등)이 가능한 블록의 범용 베이스 클래스입니다.
/// 엘리베이터(ElevatorController)와 동일하게 IInteractable 인터페이스를 구현합니다.
/// 별빛 잇기 퍼즐의 노드나, 스위치, 표지판 등 다양한 곳에 상속하여 사용할 수 있습니다.
/// </summary>
public abstract class InteractableBlockBase : MonoBehaviour, IInteractable, IInteractionPrompt
{
    [Header("Interaction Settings")]
    [Tooltip("플레이어와 상호작용 가능한 최대 거리")]
    public float interactionRange = 2f;
    [Tooltip("플레이어에게 표시할 UI 안내 문구 (Localization 키를 못 찾았을 때 쓰는 원문)")]
    [SerializeField] protected string promptText = "상호작용";
    [Tooltip("안내 문구의 Localization 키. 비우면 위 원문을 그대로 쓴다")]
    [SerializeField] protected string promptKey = "";
    [Tooltip("상호작용 우선순위 (높을수록 겹쳤을 때 우선 선택됨)")]
    [SerializeField] protected int priority = 5;

    [Header("Visual Feedback")]
    public SpriteRenderer spriteRenderer;
    public Color idleColor = Color.white;
    public Color nearbyColor = Color.yellow;

    [Header("Events")]
    [Tooltip("에디터 인스펙터에서 추가적인 이벤트 연결 시 사용")]
    public UnityEvent<GameObject> onInteract;

    private Transform _player;
    protected bool isPlayerNearby;

    // IInteractable 구현부
    public virtual string InteractionPrompt => promptText;
    public virtual int InteractionPriority => priority;

    /// <summary>
    /// 근접 안내 문구 (InteractionPromptLabel·HUD 공용).
    /// 조건에 따라 문구·색이 달라져야 하는 블록은 이걸 override한다(예: DungeonDoorChunk).
    /// </summary>
    public virtual InteractionPromptInfo GetInteractionPrompt()
        => InteractionPromptInfo.Ok(promptKey, promptText);

    protected virtual void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            _player = playerObj.transform;

        if (spriteRenderer != null)
            spriteRenderer.color = idleColor;
    }

    protected virtual void Update()
    {
        if (_player == null) return;

        // ElevatorController와 동일한 거리 기반 상호작용 가능 여부 체크
        float distance = Vector2.Distance(transform.position, _player.position);
        isPlayerNearby = distance <= interactionRange;

        UpdateVisuals();
    }

    /// <summary>
    /// 상태에 따른 시각적 피드백 (색상 변경 등)을 처리합니다.
    /// 하위 클래스(예: StarNode)에서 override하여 선택 상태(Selected), 연결 상태(Connected) 등을 표현할 수 있습니다.
    /// </summary>
    protected virtual void UpdateVisuals()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = isPlayerNearby ? nearbyColor : idleColor;
        }
    }

    /// <summary>
    /// PlayerInteractor에 의해 F 키를 눌렀을 때 호출됩니다.
    /// </summary>
    public void Interact(GameObject interactor)
    {
        Debug.Log($"[{gameObject.name}] Interacted by {interactor.name}");
        onInteract?.Invoke(interactor);
        HandleInteraction(interactor);
    }

    /// <summary>
    /// 하위 클래스에서 실제 고유 상호작용 로직(예: 별 노드 선택)을 구현합니다.
    /// </summary>
    protected abstract void HandleInteraction(GameObject interactor);

    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}
