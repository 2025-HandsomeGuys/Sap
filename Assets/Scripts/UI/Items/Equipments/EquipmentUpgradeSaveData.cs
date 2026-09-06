using System.Collections.Generic;

/// <summary>
/// 장비 강화 레벨 한 항목(저장용). id = <see cref="EquipmentID"/> 문자열.
/// </summary>
[System.Serializable]
public class EquipmentUpgradeEntry
{
    public string id;
    public int level;
}

/// <summary>
/// 장비 강화 레벨 전체(저장용). <see cref="EquipmentUpgradeStore"/>가 왕복시킨다.
/// 유물 강화 레벨은 <see cref="Relic.Data.RelicSaveData"/>(relicSave)에 이미 담기므로 여기 없다.
/// </summary>
[System.Serializable]
public class EquipmentUpgradeSaveData
{
    public List<EquipmentUpgradeEntry> entries = new List<EquipmentUpgradeEntry>();
}
