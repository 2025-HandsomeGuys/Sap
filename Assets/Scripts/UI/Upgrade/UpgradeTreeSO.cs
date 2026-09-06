using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전체 업그레이드 트리의 구성을 담는 컨테이너 SO입니다.
/// </summary>
[CreateAssetMenu(fileName = "NewUpgradeTree", menuName = "Upgrade/Tree")]
public class UpgradeTreeSO : ScriptableObject
{
    [Tooltip("트리에 포함된 모든 노드 목록 (검색 및 초기화용)")]
    public List<UpgradeNodeSO> allNodes;

    [Tooltip("계층별 정보 (이름, 해금 조건 등)")]
    public List<TierInfo> tiers;

    [Tooltip("트리의 최상위 시작 노드들 (UI 생성 진입점)")]
    public List<UpgradeNodeSO> rootNodes;

    /// <summary>
    /// ID로 노드를 찾습니다.
    /// </summary>
    public UpgradeNodeSO GetNodeById(string id)
    {
        if (allNodes == null) return null;
        return allNodes.Find(node => node.nodeId == id);
    }

    public TierInfo GetTierInfo(int tierIndex)
    {
        if (tiers == null) return null;
        return tiers.Find(t => t.tierIndex == tierIndex);
    }
}

[System.Serializable]
public class TierInfo
{
    public int tierIndex;
    public string tierNameKey;              // Localization key
    public string unlockConditionTextKey;   // Localization key

    /// <summary>
    /// 이 지층 띠의 **윗변** uiY를 손으로 정한 값. false면 예전처럼 자동
    /// (앞 지층 맨 위 노드와 다음 지층 맨 아래 노드의 중간)으로 잡는다.
    ///
    /// 두 지층의 uiY가 겹치면 그 중간값이 양쪽 노드를 가로지른다 — 그때 쓰는
    /// 탈출구다. 값은 UpgradeTierBands.csv에서 오고, 편집기에서 선을 끌면 적힌다.
    /// </summary>
    public bool hasBandTop;
    public float bandTop;

    /// <summary>
    /// 현재 언어로 계층 이름 반환
    /// </summary>
    public string TierName => LanguageManager.Instance?.L(tierNameKey) ?? tierNameKey;

    /// <summary>
    /// 현재 언어로 해금 조건 텍스트 반환
    /// </summary>
    public string UnlockConditionText => LanguageManager.Instance?.L(unlockConditionTextKey) ?? unlockConditionTextKey;
}
