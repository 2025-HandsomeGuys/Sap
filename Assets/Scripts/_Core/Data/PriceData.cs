// @tags: price, data, json, dto, economy, balance
using System;
using System.Collections.Generic;

/// <summary>광물 한 종의 가격과 무게. id는 MineralID enum 이름.</summary>
[Serializable]
public class MineralPriceEntry
{
    public string id;
    public int    price;
    public float  weight;

    /// <summary>
    /// 이 광물이 처음 나오는 지층 라벨("L0_Dirt" 등). 밸런싱할 때 눈으로 층을 가르려고 넣은
    /// 읽기 전용 주석이다 — PriceApplier는 이 값을 쓰지 않는다.
    /// tileData.json에서 뽑히므로 손으로 고쳐도 다음 export에서 덮어써진다.
    /// </summary>
    public string layer;
}

/// <summary>id ↔ 가격 한 쌍. 소모품·장비·유물 상점가에 공용.</summary>
[Serializable]
public class IdPriceEntry
{
    public string id;
    public int    price;
}

/// <summary>
/// 장비 상점가. id ↔ 가격에 소속 지층 라벨이 하나 더 붙는다.
/// </summary>
[Serializable]
public class EquipmentPriceEntry : IdPriceEntry
{
    /// <summary>
    /// 이 장비 세트가 쓰이는 지층 라벨("L1_Ice" 등). 광물의 layer와 같은 표기를 쓴다 —
    /// 밸런싱할 때 층끼리 가격을 나란히 놓고 보려고 넣은 읽기 전용 주석이다.
    /// PriceApplier는 이 값을 쓰지 않고, 다음 export에서 덮어써진다.
    /// 소속 근거: Assets/Docs/economy/equipment-relic-price-design.md §4
    /// </summary>
    public string layer;

    /// <summary>
    /// 이 장비를 상점에 여는 업그레이드 노드 id(ShopItemData.unlockNodeId). 비면 처음부터 판다.
    /// layer와 마찬가지로 읽기 전용 주석이다 — 실제 판정은 ShopUnlockGate가 에셋 값으로 하고,
    /// 여기 값을 고쳐도 게임은 안 바뀐다. "이 가격을 볼 때쯤 무엇을 열었나"를 같이 보려고 적는다.
    /// </summary>
    public string unlockNodeId;
}

/// <summary>유물 강화비. gold 길이는 maxLevel - 1 (=2).</summary>
[Serializable]
public class RelicUpgradeEntry
{
    public string id;
    public int[]  gold;
}

/// <summary>업그레이드 노드 비용. id는 enum이 아니라 nodeId 문자열.</summary>
[Serializable]
public class NodeCostEntry
{
    public string id;
    public int    cost;
}

/// <summary>
/// StreamingAssets/priceData.json 의 스키마.
///
/// JsonUtility는 Dictionary를 지원하지 않으므로 전부 배열이다.
/// JSON에 없는 절은 null로 남으므로 소비 측이 null을 견뎌야 한다.
///
/// 설계: Assets/Docs/economy/price-data-json-design.md §3
/// </summary>
[Serializable]
public class PriceData
{
    public MineralPriceEntry[]   minerals;
    public IdPriceEntry[]        shopItems;
    public EquipmentPriceEntry[] shopEquipment;
    public IdPriceEntry[]        shopRelics;
    public RelicUpgradeEntry[]   relicUpgrades;
    public NodeCostEntry[]       upgradeNodes;
}

/// <summary>적용 결과 요약. 로그와 테스트가 함께 쓴다.</summary>
public class PriceApplyResult
{
    public int applied;
    public readonly List<string> warnings = new List<string>();
}
