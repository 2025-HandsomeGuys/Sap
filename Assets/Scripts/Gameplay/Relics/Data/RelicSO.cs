using System;
using UnityEngine;

namespace Relic.Data
{
    [Serializable]
    public struct RelicUpgradeCost
    {
        public int    gold;
        public ItemID material;      // 전역 ItemID enum
        public int    materialCount;
    }

    [CreateAssetMenu(fileName = "Relic", menuName = "Game Data/Relic SO")]
    public class RelicSO : ScriptableObject
    {
        public RelicID  id;
        public string   displayNameKey;
        public string   descriptionKey;
        public Sprite   icon;
        public RelicType type;
        public int       maxLevel = 3;

        [Header("업그레이드 트리로 지급되는 유물")]
        [Tooltip("이 노드를 해금하면 유물이 지급된다(비워두면 상점 등 다른 경로로만 얻는다). " +
                 "도구(toolConfig.json)·시설(WorldInteractable)과 같은 'nodeId 문자열로 건다' 규칙이다 — " +
                 "오타를 내면 조용히 지급되지 않으므로 UpgradeTreeCostTests가 짝을 검사한다")]
        public string unlockNodeId;

        // 경제 데이터만 (Lv1→2, Lv2→3). length == maxLevel-1
        [Header("⚠ 런타임에 priceData.json이 덮어씀")]
        public RelicUpgradeCost[] upgradeCosts;

        // 유물별 고유 로직 + 게임 파라미터. 런타임엔 Clone해서 사용.
        [SerializeReference] public RelicBehaviour behaviour;
    }
}
