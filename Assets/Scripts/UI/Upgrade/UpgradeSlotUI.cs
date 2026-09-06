using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// 개별 업그레이드 노드의 UI를 담당합니다.
/// 아이콘 표시, 클릭 이벤트 처리, 상태(잠김/해금가능/해금됨)에 따른 비주얼 업데이트를 수행합니다.
/// </summary>
public class UpgradeSlotUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI References")]
    public Image iconImage;
    public Image frameImage; // 배경/프레임
    public Image lockIcon;   // 잠금 상태 아이콘
    public TextMeshProUGUI nameText; // 업그레이드 이름 표시

    [Header("Colors")]
    public Color lockedColor = new Color(0.2f, 0.2f, 0.2f, 1f);   // 잠김: 어두운 회색
    public Color unlockableColor = new Color(0.5f, 0.5f, 0.5f, 1f); // 해금가능: 회색
    public Color unlockedColor = Color.white;                    // 해금됨: 흰색 (또는 강조색)
    public Color selectedColor = new Color(1f, 0.5f, 0f, 1f);    // 선택됨 (이미지의 주황색 강조)

    private UpgradeNodeSO _node;
    
    // 상태
    private bool _isUnlocked;

    // 툴팁 컴포넌트
    private TooltipTrigger _tooltipTrigger;
    private UpgradeSlotTooltipProvider _tooltipProvider;

    public void Init(UpgradeNodeSO node, UpgradeUI mainUI)
    {
        _node = node;

        if (_node != null)
        {
            if (iconImage != null) iconImage.sprite = _node.icon;
            if (nameText != null) nameText.text = _node.DisplayName;
        }

        // 툴팁 설정
        SetupTooltip();

        UpdateState();
    }

    private void SetupTooltip()
    {
        // TooltipTrigger가 없으면 추가
        _tooltipTrigger = GetComponent<TooltipTrigger>();
        if (_tooltipTrigger == null)
        {
            _tooltipTrigger = gameObject.AddComponent<TooltipTrigger>();
        }

        // UpgradeSlotTooltipProvider 추가
        _tooltipProvider = GetComponent<UpgradeSlotTooltipProvider>();
        if (_tooltipProvider == null)
        {
            _tooltipProvider = gameObject.AddComponent<UpgradeSlotTooltipProvider>();
        }

        _tooltipProvider.Initialize(_node);
        _tooltipTrigger.tooltipProvider = _tooltipProvider;
    }

    public void UpdateState()
    {
        if (_node == null) return;

        NodeLockState lockState = UpgradeManager.Instance.GetNodeLockState(_node);

        _isUnlocked = (lockState == NodeLockState.Unlocked);

        // 비주얼 업데이트
        switch (lockState)
        {
            case NodeLockState.Unlocked:
                // 해금됨: 밝게 표시
                if (frameImage != null) frameImage.color = unlockedColor;
                if (iconImage != null) iconImage.color = Color.white;
                if (lockIcon != null) lockIcon.gameObject.SetActive(false);
                if (nameText != null) nameText.color = Color.white;
                break;

            case NodeLockState.Unlockable:
                // 해금 가능: 약간 밝게 (클릭 유도)
                if (frameImage != null) frameImage.color = unlockableColor;
                if (iconImage != null) iconImage.color = new Color(1, 1, 1, 0.8f);
                if (lockIcon != null) lockIcon.gameObject.SetActive(false);
                if (nameText != null) nameText.color = new Color(1, 1, 1, 0.9f);
                break;

            case NodeLockState.TierLocked:
                // 계층 잠김: 매우 어둡게 (이미지 참고 - 회색)
                if (frameImage != null) frameImage.color = new Color(0.15f, 0.15f, 0.15f, 1f);
                if (iconImage != null) iconImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
                if (lockIcon != null) lockIcon.gameObject.SetActive(true);
                if (nameText != null) nameText.color = new Color(0.4f, 0.4f, 0.4f, 1f);
                break;

            case NodeLockState.Locked:
            default:
                // 잠김: 어둡게
                if (frameImage != null) frameImage.color = lockedColor;
                if (iconImage != null) iconImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                if (lockIcon != null) lockIcon.gameObject.SetActive(true);
                if (nameText != null) nameText.color = Color.gray;
                break;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_node == null) return;
        
        // 클릭하면 해금 시도.
        //
        // 살 수 있는지 미리 거르지 않는다 — 못 사는 노드를 눌러 본 것이야말로
        // "사고 싶었지만 못 샀다"는 기록(upgrade_blocked)이고, 걸러 내면 그 기록이
        // 통째로 사라진다. UnlockNode 가 안에서 판정하고 false 를 돌려준다.
        // (배경: Assets/Docs/economy/upgrade-balance-charter.md §6-A)
        // 디버그 연쇄 해금(콘솔 upg) — 선행·계층·골드 무시하고 이 노드까지 통째로 연다
        if (UpgradeManager.DebugChainUnlock && !UpgradeManager.Instance.IsMaxed(_node))
        {
            UpgradeManager.Instance.DebugForceUnlockChain(_node);
            UpdateState();
            return;
        }

        if (!_isUnlocked)
        {
            if (UpgradeManager.Instance.UnlockNode(_node))
            {
                // 해금 성공 시 툴팁 업데이트 (다시 호버하면 업데이트됨)
                UpdateState();
            }
        }
    }
}
