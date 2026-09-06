// @tags: player, skin, equipment, costume, bridge, skinmanager, equipmentinventory, runtime, code-generated

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 장비 인벤토리 ↔ 플레이어 외형(SkinManager)을 이어주는 런타임 브릿지.
///
/// 이 게임은 장비(EquipmentSO)와 외형(SkinManager 의상)이 코드상 분리돼 있어서,
/// 창고·인벤토리에서 장비를 장착/해제해도 플레이어 몸이 바뀌지 않았다.
/// 이 브릿지가 <see cref="EquipmentInventory.OnInventoryChanged"/>를 구독해,
/// 머리/옷/신발 슬롯의 장비를 부위에 맞는 의상으로 <see cref="SkinManager"/>에 실시간 반영한다.
///
/// 매핑은 데이터 추가 없이 이름 규칙으로 자동 연결한다:
///  - 장비ID의 테마명(MinerHelmet→"Miner", ClimberArmor→"Climber")과
///    SkinManager.costumeList의 의상명(Miner(1), Climber(0)…)을 접두사(4자↑)로 매칭.
///  - 매칭되는 의상이 없거나 슬롯이 비면 그 부위는 기본 모습으로 되돌린다.
///
/// 프리팹/씬 세팅 없이 <see cref="Bootstrap"/>가 자동 생성한다(DontDestroyOnLoad).
/// SkinManager의 OnSkinChanged를 구독하는 UiPlayer 프리뷰(SkinShell)도 이 변경을 그대로 따라간다.
/// </summary>
[DisallowMultipleComponent]
public class EquipmentSkinSync : MonoBehaviour
{
    // 접두사 매칭 최소 일치 글자 수. Leather/Iron/Carrier/Shovel처럼 대응 의상이 없는 테마는 걸러진다.
    private const int MinPrefixMatch = 4;

    private EquipmentInventory _equip;
    private SkinManager _skin;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // 이미 있으면 중복 생성 방지
        if (FindFirstObjectByType<EquipmentSkinSync>(FindObjectsInactive.Include) != null) return;

        var go = new GameObject("EquipmentSkinSync");
        DontDestroyOnLoad(go);
        go.AddComponent<EquipmentSkinSync>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Rebind();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_equip != null) _equip.OnInventoryChanged -= Apply;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Rebind();

    /// <summary>현재 씬의 EquipmentInventory·SkinManager를 다시 찾아 구독하고 즉시 한 번 반영.</summary>
    private void Rebind()
    {
        var equip = FindFirstObjectByType<EquipmentInventory>(FindObjectsInactive.Include);
        var skin = FindFirstObjectByType<SkinManager>(FindObjectsInactive.Include);

        if (equip != _equip)
        {
            if (_equip != null) _equip.OnInventoryChanged -= Apply;
            _equip = equip;
            if (_equip != null) _equip.OnInventoryChanged += Apply;
        }

        _skin = skin;
        Apply();
    }

    /// <summary>장착 상태를 읽어 부위별 의상을 SkinManager에 반영한다.</summary>
    private void Apply()
    {
        if (_equip == null || _skin == null) return;

        ApplyPart(EquipmentInventory.SlotHead, EquipmentType.Head);
        ApplyPart(EquipmentInventory.SlotClothes, EquipmentType.Clothes);
        ApplyPart(EquipmentInventory.SlotShoes, EquipmentType.Shoes);
    }

    private void ApplyPart(int slotIndex, EquipmentType type)
    {
        var list = _equip.ReadonlyItems;
        EquipmentSO eq = (slotIndex >= 0 && slotIndex < list.Count) ? list[slotIndex].item as EquipmentSO : null;

        string costumeName = eq != null ? FindCostumeName(ThemeOf(eq)) : null;

        switch (type)
        {
            case EquipmentType.Head:
                if (costumeName != null) _skin.EquipHair(costumeName); else _skin.RemoveHair();
                break;
            case EquipmentType.Clothes:
                if (costumeName != null) _skin.EquipBody(costumeName); else _skin.RemoveBody();
                break;
            case EquipmentType.Shoes:
                if (costumeName != null) _skin.EquipShoes(costumeName); else _skin.RemoveShoes();
                break;
        }
    }

    /// <summary>장비ID에서 테마명만 뽑는다. 예: MinerHelmet→"Miner", ClimberArmor→"Climber", WinterBoots→"Winter".</summary>
    private static string ThemeOf(EquipmentSO eq)
    {
        string id = eq.equipmentID.ToString();
        foreach (var suffix in new[] { "Helmet", "Armor", "Boots" })
            if (id.EndsWith(suffix)) return id.Substring(0, id.Length - suffix.Length);
        return id;
    }

    /// <summary>테마명과 접두사가 <see cref="MinPrefixMatch"/>자 이상 일치하는 의상의 정확한 이름을 찾는다. 없으면 null.</summary>
    private string FindCostumeName(string theme)
    {
        if (string.IsNullOrEmpty(theme) || _skin.costumeList == null) return null;

        foreach (var costume in _skin.costumeList)
        {
            if (costume == null || string.IsNullOrEmpty(costume.costumeName)) continue;
            if (CommonPrefixLength(theme, LeadingAlpha(costume.costumeName)) >= MinPrefixMatch)
                return costume.costumeName; // SkinManager.Equip*은 정확한 costumeName으로 조회한다
        }
        return null;
    }

    /// <summary>문자열 앞쪽 알파벳만. "Miner(1)"→"Miner", "WinterGear"→"WinterGear".</summary>
    private static string LeadingAlpha(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsLetter(s[i])) i++;
        return s.Substring(0, i);
    }

    private static int CommonPrefixLength(string a, string b)
    {
        int n = Mathf.Min(a.Length, b.Length);
        int i = 0;
        while (i < n && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) i++;
        return i;
    }
}
