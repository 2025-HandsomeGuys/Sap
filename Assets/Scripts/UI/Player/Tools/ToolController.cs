using UnityEngine;

public class ToolController : MonoBehaviour
{
    [Header("도구 설정")]
    // 0:손, 1:삽, 2:곡괭이, 3:드릴
    public Sprite[] toolSprites;

    [Header("드릴 & 왼손 연결")]
    public DrillController drillController;
    public SpriteRenderer leftArmRenderer;
    public int drillIndex = 3;

    [Header("씬 설정")]
    [Tooltip("지하씬 이름. 이 오브젝트가 속한 씬 이름과 비교해 지하 여부를 판별한다.")]
    public string undergroundSceneName = "DemoUnderground";
    [Tooltip("지하씬에서 사용 불가한 도구 인덱스 (손 = 0)")]
    public int handToolIndex = 0;

    [Tooltip("해당 씬에서는 맨손(handToolIndex)만 사용 가능하게 강제합니다.")]
    public string handOnlySceneName = "DemoSurface";

    [Header("도구 전환 설정")]
    [Tooltip("도구 스왑 시 적용되는 쿨타임 (초)")]
    public float switchCooldown = 0.2f;
    private float lastSwitchTime = -9999f;

    // ★ [추가] 스왑 효과음 설정
    [Header("효과음 설정")]
    [Tooltip("도구 교체 시 재생될 효과음 이름 (SoundDataSO에 등록된 이름)")]
    public string switchSfxName = "ui_move"; // 기본값으로 원하는 효과음 이름을 넣으셔도 됩니다.

    public int currentToolIndex { get; private set; } = 0;

    [HideInInspector] public bool canSwitchTool = true;

    // 무기가 스왑될 때 발생할 이벤트 (이전 인덱스, 새 인덱스)
    public event System.Action<int, int> OnToolSwapped;

    private SpriteRenderer myRenderer;

    public bool IsDrillMode => currentToolIndex == drillIndex;

    /// <summary>드릴이 업그레이드 트리에서 해금(획득)되었는지 여부. 통합 스태미나 HUD의 드릴 게이지 상시 표시 판정용.</summary>
    public bool IsDrillUnlocked => IsToolUnlocked(drillIndex);

    void Awake()
    {
        myRenderer = GetComponent<SpriteRenderer>();
    }

    void Start()
    {
        int startIndex = currentToolIndex;

        if (!IsToolSelectable(currentToolIndex))
            currentToolIndex = FindNextSelectableTool(+1);

        UpdateToolSprite(currentToolIndex != startIndex ? startIndex : -1);
    }

    void Update()
    {
        if (LoadingData.IsLoading) return;

        if (UIStateManager.IsInputBlocked)
        {
            return;
        }

        if (canSwitchTool)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");

            if (scroll != 0f)
            {
                if (Time.time - lastSwitchTime < switchCooldown)
                    return;

                int previousIndex = currentToolIndex;
                int next = scroll > 0f ? FindNextSelectableTool(+1) : FindNextSelectableTool(-1);

                if (next != currentToolIndex)
                {
                    currentToolIndex = next;
                    UpdateToolSprite(previousIndex);

                    lastSwitchTime = Time.time;
                }
            }
        }
    }

    public void EquipTool(int index)
    {
        if (index < 0 || index >= toolSprites.Length) return;
        if (!IsToolSelectable(index)) return;

        int previousIndex = currentToolIndex;
        currentToolIndex = index;
        UpdateToolSprite(previousIndex);

        lastSwitchTime = Time.time;
    }

    public void RefreshSelection()
    {
        if (IsToolSelectable(currentToolIndex)) return;

        int previousIndex = currentToolIndex;
        currentToolIndex = FindNextSelectableTool(+1);
        UpdateToolSprite(previousIndex);
    }

    void UpdateToolSprite(int previousIndex = -1)
    {
        bool isDrillMode = (currentToolIndex == drillIndex);

        if (isDrillMode)
        {
            if (myRenderer != null) myRenderer.enabled = false;
            if (leftArmRenderer != null) leftArmRenderer.enabled = false;
            if (drillController != null) drillController.SetDrillActive(true);
        }
        else
        {
            if (drillController != null) drillController.SetDrillActive(false);

            if (myRenderer != null)
            {
                myRenderer.enabled = true;
                if (currentToolIndex < toolSprites.Length)
                {
                    myRenderer.sprite = toolSprites[currentToolIndex];
                }
            }
            if (leftArmRenderer != null) leftArmRenderer.enabled = true;
        }

        // 스왑 이벤트 호출 및 효과음 재생 (초기화가 아닐 때만)
        if (previousIndex != -1 && previousIndex != currentToolIndex)
        {
            OnToolSwapped?.Invoke(previousIndex, currentToolIndex);

            // ★ [추가] 효과음 재생 로직 (SoundManager가 존재하고 효과음 이름이 비어있지 않을 때)
            if (!string.IsNullOrEmpty(switchSfxName) && SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySFX(switchSfxName);
            }
        }
    }

    public Sprite GetToolSprite(int index)
    {
        if (index >= 0 && index < toolSprites.Length)
            return toolSprites[index];
        return null;
    }

    // ===== 도구 해금 / 선택 가능 여부 =====

    private bool IsUndergroundScene()
        => gameObject.scene.name == undergroundSceneName;

    private bool IsHandOnlyScene()
        => gameObject.scene.name == handOnlySceneName;

    private bool IsToolUnlocked(int index)
    {
        if (ToolConfigLoader.Instance == null) return true;
        string[] nodeIds = ToolConfigLoader.Instance.Config.unlockNodeIds;
        if (nodeIds == null || index >= nodeIds.Length) return true;
        string nodeId = nodeIds[index];
        return string.IsNullOrEmpty(nodeId) ||
               (UpgradeManager.Instance != null &&
                UpgradeManager.Instance.IsNodeUnlocked(nodeId));
    }

    private bool IsToolSelectable(int index)
    {
        if (IsHandOnlyScene())
        {
            return index == handToolIndex;
        }

        if (IsUndergroundScene() && index == handToolIndex) return false;

        return IsToolUnlocked(index);
    }

    private int FindNextSelectableTool(int direction)
    {
        int count = toolSprites.Length;
        int next = currentToolIndex;
        for (int i = 0; i < count - 1; i++)
        {
            next = (next + direction + count) % count;
            if (IsToolSelectable(next)) return next;
        }
        return currentToolIndex;
    }
}