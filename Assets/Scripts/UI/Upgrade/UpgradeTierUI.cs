using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 업그레이드 트리의 각 계층(Tier) 배경 및 잠금 오버레이를 관리하는 클래스입니다.
/// </summary>
public class UpgradeTierUI : MonoBehaviour
{
    [Header("UI References")]
    public RectTransform backgroundRect;      // 배경 영역의 RectTransform (인스펙터 할당 권장)
    public TextMeshProUGUI tierNumberText;    // 배경의 큰 숫자 (예: 2, 3)
    public TextMeshProUGUI tierNameText;      // 계층 이름 (예: 기초 공업 II)
    public GameObject lockedOverlay;          // 잠금 시 나타나는 오버레이 패널
    public TextMeshProUGUI conditionText;     // 해제 조건 텍스트
    public RectTransform nodesParent;         // 노드들이 배치될 부모 Tr (일반적으로는 자기 자신 또는 전용 영역)

    [Header("Settings")]
    public float tierHeight = 400f;           // 계층의 기본 높이

    private int _tierIndex;
    private TierInfo _tierInfo;

    public void Init(int tierIndex, TierInfo info)
    {
        _tierIndex = tierIndex;
        _tierInfo = info;

        if (tierNumberText != null) 
            tierNumberText.text = tierIndex.ToString();

        if (tierNameText != null && info != null)
            tierNameText.text = info.TierName;

        if (conditionText != null && info != null)
            conditionText.text = info.UnlockConditionText;

        UpdateState();
    }

    /// <summary>
    /// 계층의 잠금 상태를 업데이트합니다.
    /// </summary>
    public void UpdateState()
    {
        if (UpgradeManager.Instance == null) return;

        bool isUnlocked = UpgradeManager.Instance.IsTierUnlocked(_tierIndex);

        // 오버레이 활성화 여부 결정 (잠겨있으면 켬)
        if (lockedOverlay != null)
        {
            lockedOverlay.SetActive(!isUnlocked);
        }

        // 오버레이가 켜져 있으면 하위 노드들과의 상호작용이 막히도록 설정 (CanvasGroup 등을 활용 가능)
        // 여기선 간단히 Overlay가 Raycast Target인 Image를 가지고 있으면 됨.
    }
}
