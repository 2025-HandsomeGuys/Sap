// @tags: codex, collection, discovery, registry, save, static, mineral, equipment, item, relic
using System;
using System.Collections.Generic;

/// <summary>도감 카테고리 — 탭 순서와 동일. 세이브 리스트 인덱스로도 쓰이므로 값을 바꾸지 말 것.</summary>
public enum CodexCategory
{
    Mineral = 0,
    Equipment = 1,
    Item = 2,
    Relic = 3,
}

/// <summary>
/// 도감 '발견' 기록 전역 저장소(static). 광물·장비·아이템·유물 네 카테고리를 한 벌로 다룬다.
///
/// 발견 시점(획득 훅):
///  - 광물: <see cref="MineralInventory.AddItem"/>
///  - 아이템: <see cref="ItemInventory.AddItem"/>
///  - 장비: <see cref="EquipmentInventory.AddItem"/>
///  - 유물: 훅 없음 — 유물은 영구 보유(팔 수 없음)라 도감을 열 때
///    <c>RelicManager.Inventory.Owned</c>에서 백필하면 정확하다(<see cref="CodexOverlayUI"/>).
///
/// 세이브는 <see cref="EquipmentUpgradeStore"/>와 같은 패턴 —
/// <see cref="CaptureSaveData"/>/<see cref="ApplySaveData"/>, PlayerData.collectionCodex. 뉴게임은 <see cref="Clear"/>.
///
/// id는 각 카테고리 enum의 <c>ToString()</c>(예: "Gold", "IronHelmet", "Magnet")을 그대로 쓴다.
/// 이 클래스는 Unity 타입에 의존하지 않는 순수 C#이라 EditMode 테스트·순수 로직에서 안전하게 참조된다.
/// </summary>
public static class CollectionCodex
{
    private const int CategoryCount = 4;

    private static readonly HashSet<string>[] _sets =
    {
        new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
    };

    /// <summary>새 항목이 처음 발견될 때 발생(카테고리, id). 도감이 열려 있으면 갱신에 쓴다.</summary>
    public static event Action<CodexCategory, string> OnDiscovered;

    public static int DiscoveredCount(CodexCategory cat) => _sets[(int)cat].Count;

    public static bool IsDiscovered(CodexCategory cat, string id)
        => !string.IsNullOrEmpty(id) && _sets[(int)cat].Contains(id);

    /// <summary>
    /// 발견 기록. 이미 발견돼 있거나 빈/None id면 false. 처음 발견이면 true(연출·뱃지 트리거용).
    /// </summary>
    public static bool Discover(CodexCategory cat, string id)
    {
        if (string.IsNullOrEmpty(id) || id == "None") return false;
        if (!_sets[(int)cat].Add(id)) return false;

        OnDiscovered?.Invoke(cat, id);
        return true;
    }

    /// <summary>뉴게임 초기화.</summary>
    public static void Clear()
    {
        for (int i = 0; i < CategoryCount; i++) _sets[i].Clear();
    }

    public static CollectionCodexData CaptureSaveData()
    {
        var data = new CollectionCodexData();
        data.minerals.AddRange(_sets[(int)CodexCategory.Mineral]);
        data.equipment.AddRange(_sets[(int)CodexCategory.Equipment]);
        data.items.AddRange(_sets[(int)CodexCategory.Item]);
        data.relics.AddRange(_sets[(int)CodexCategory.Relic]);
        return data;
    }

    public static void ApplySaveData(CollectionCodexData data)
    {
        Clear();
        if (data == null) return;
        Fill(CodexCategory.Mineral, data.minerals);
        Fill(CodexCategory.Equipment, data.equipment);
        Fill(CodexCategory.Item, data.items);
        Fill(CodexCategory.Relic, data.relics);
    }

    private static void Fill(CodexCategory cat, List<string> ids)
    {
        if (ids == null) return;
        var set = _sets[(int)cat];
        foreach (var id in ids)
            if (!string.IsNullOrEmpty(id) && id != "None") set.Add(id);
    }
}

/// <summary>PlayerData에 실리는 도감 세이브 데이터.</summary>
[Serializable]
public class CollectionCodexData
{
    public List<string> minerals = new List<string>();
    public List<string> equipment = new List<string>();
    public List<string> items = new List<string>();
    public List<string> relics = new List<string>();
}
