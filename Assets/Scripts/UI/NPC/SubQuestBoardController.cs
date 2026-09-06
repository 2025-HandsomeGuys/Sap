using UnityEngine;

/// <summary>
/// 게시판 NPC 상호작용 컨트롤러
/// 엘리베이터 패턴을 적용하여 거리 기반 + 키보드 + 클릭 상호작용 지원
/// 서브퀘스트 수락 전용
/// </summary>
public class SubQuestBoardController : MonoBehaviour
{
    [Header("상호작용 설정")]
    public float interactionRange = 2f;
    public KeyCode interactKey = InteractionKeys.Interact;
    
    [Header("시각 효과")]
    public SpriteRenderer spriteRenderer;
    public Color idleColor = Color.white;
    public Color activeColor = Color.yellow;
    public GameObject interactionPrompt; // "F키를 눌러 게시판 확인" 텍스트
    
    [Header("UI 참조")]
    public SubQuestBoardUI subQuestBoardUI;
    
    private Transform player;
    private bool isPlayerNearby = false;
    
    void Start()
    {
        // 플레이어 찾기
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
        
        // UI 참조 자동 찾기
        if (subQuestBoardUI == null)
        {
            subQuestBoardUI = FindFirstObjectByType<SubQuestBoardUI>();
        }
        
        // 시각 효과 초기화
        if (spriteRenderer != null)
        {
            spriteRenderer.color = idleColor;
        }
        
        // 프롬프트 숨기기
        if (interactionPrompt != null)
        {
            interactionPrompt.SetActive(false);
        }
    }
    
    void Update()
    {
        if (player == null) return;
        
        // 다른 UI가 열려있으면 상호작용 불가
        if (UIStateManager.Instance != null && 
            UIStateManager.Instance.CurrentState != UIState.None && 
            UIStateManager.Instance.CurrentState != UIState.SubQuestBoard)
        {
            if (interactionPrompt != null) interactionPrompt.SetActive(false);
            return;
        }
        
        // 플레이어와의 거리 체크
        float distance = Vector2.Distance(transform.position, player.position);
        isPlayerNearby = distance <= interactionRange;
        
        // 시각 효과 업데이트
        if (spriteRenderer != null)
        {
            spriteRenderer.color = isPlayerNearby ? activeColor : idleColor;
        }
        
        // 프롬프트 표시
        if (interactionPrompt != null)
        {
            interactionPrompt.SetActive(isPlayerNearby && UIStateManager.Instance?.CurrentState == UIState.None);
        }
        
        // 상호작용 입력 체크
        if (isPlayerNearby && Input.GetKeyDown(interactKey))
        {
            OnPlayerInteract();
        }
    }
    
    /// <summary>
    /// 마우스 클릭으로 상호작용
    /// </summary>
    void OnMouseDown()
    {
        if (isPlayerNearby && UIStateManager.Instance?.CurrentState == UIState.None)
        {
            OnPlayerInteract();
        }
    }
    
    /// <summary>
    /// 플레이어가 게시판과 상호작용했을 때
    /// </summary>
    void OnPlayerInteract()
    {
        Debug.Log("[SubQuestBoardController] 플레이어가 게시판과 상호작용");

        // 코드 생성 오버레이를 쓰는 씬이면 UIStateManager 경유로 연다 (프리팹보다 우선)
        if (UIStateManager.Instance != null && UIStateManager.Instance.useCodeBuiltSubQuestUI)
        {
            UIStateManager.Instance.SetState(UIState.SubQuestBoard, this.transform);
        }
        else if (subQuestBoardUI != null)
        {
            subQuestBoardUI.OpenSubQuestBoard();
        }
        else if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.SetState(UIState.SubQuestBoard);
        }
    }
    
    // [디버그용] Gizmo로 상호작용 범위 표시
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}
