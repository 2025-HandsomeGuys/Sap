// @tags: shop, equipment, layer, stratum, sort, ui

using System.Collections.Generic;

/// <summary>
/// 상점 장비 탭을 "지층 → 세트 가격" 순으로 묶기 위한 세트↔지층 대응표.
///
/// 근거는 <c>Assets/Docs/economy/equipment-relic-price-design.md</c> §4·§5.1이고,
/// 같은 표를 <c>Assets/Tests/EditMode/ShopPriceTests.cs</c>가 가격 검증용으로 따로 들고 있다.
/// 셋 중 하나를 고치면 나머지 둘도 같이 고쳐야 한다 — 여기서 지층만 쓰고 가격은 쓰지 않는 이유는,
/// 실제 판매가가 priceData.json에서 부위마다 다르게 튜닝돼 설계 문서의 "부위당 가격"과 더는 같지 않기 때문이다.
/// 정렬에 쓰는 가격은 항상 런타임 값(ShopItemData.price)에서 다시 더한다.
///
/// 표에 없는 장비(레거시 LeatherHelmet 3001 등)는 <see cref="Unknown"/>으로 떨어져 목록 맨 아래 '기타'에 모인다.
/// </summary>
public static class ShopEquipmentLayers
{
    /// <summary>표에 없는 장비. 지층 번호가 아니라 '기타' 자리를 뜻한다.</summary>
    public const int Unknown = 0;

    /// <summary>세트 이름 → (지층, 머리·옷·신발). 설계 문서 §5.1의 표 그대로.</summary>
    private static readonly (string set, int layer, EquipmentID[] pieces)[] Sets =
    {
        ("Miner",     1, new[] { EquipmentID.MinerHelmet,     EquipmentID.MinerArmor,     EquipmentID.MinerBoots     }),
        ("Winter",    2, new[] { EquipmentID.WinterHelmet,    EquipmentID.WinterArmor,    EquipmentID.WinterBoots    }),
        ("Climber",   2, new[] { EquipmentID.ClimberHelmet,   EquipmentID.ClimberArmor,   EquipmentID.ClimberBoots   }),
        ("Carrier",   2, new[] { EquipmentID.CarrierHelmet,   EquipmentID.CarrierArmor,   EquipmentID.CarrierBoots   }),
        ("Heat",      3, new[] { EquipmentID.HeatHelmet,      EquipmentID.HeatArmor,      EquipmentID.HeatBoots      }),
        ("Engineer",  3, new[] { EquipmentID.EngineerHelmet,  EquipmentID.EngineerArmor,  EquipmentID.EngineerBoots  }),
        ("Shovel",    3, new[] { EquipmentID.ShovelHelmet,    EquipmentID.ShovelArmor,    EquipmentID.ShovelBoots    }),
        ("Scientist", 4, new[] { EquipmentID.ScientistHelmet, EquipmentID.ScientistArmor, EquipmentID.ScientistBoots }),
        ("Marathon",  4, new[] { EquipmentID.MarathonHelmet,  EquipmentID.MarathonArmor,  EquipmentID.MarathonBoots  }),
    };

    private static readonly Dictionary<EquipmentID, (int layer, string set)> Index = BuildIndex();

    private static Dictionary<EquipmentID, (int layer, string set)> BuildIndex()
    {
        var map = new Dictionary<EquipmentID, (int, string)>();
        foreach (var s in Sets)
            foreach (var id in s.pieces)
                map[id] = (s.layer, s.set);
        return map;
    }

    /// <summary>장비가 속한 지층(1~4). 표에 없으면 <see cref="Unknown"/>.</summary>
    public static int LayerOf(EquipmentID id)
        => Index.TryGetValue(id, out var e) ? e.layer : Unknown;

    /// <summary>장비가 속한 세트 이름. 표에 없으면 null — 같은 세트로 묶이지 않는다.</summary>
    public static string SetOf(EquipmentID id)
        => Index.TryGetValue(id, out var e) ? e.set : null;

    /// <summary>세트 안에서의 부위 순서: 머리(30xx)=0, 옷(31xx)=1, 신발(32xx)=2.</summary>
    public static int PartOrder(EquipmentID id) => ((int)id / 100) % 10;

    /// <summary>
    /// 구분 줄에 찍을 지층 이름. 이름은 엘리베이터 정류장 표기(<see cref="ElevatorLayerCatalog"/>의
    /// 땅·얼음땅·용암땅·우주)와 맞춘다 — 같은 지층을 두 화면이 다르게 부르지 않게.
    /// </summary>
    public static string LayerLabel(int layer)
    {
        switch (layer)
        {
            case 1:  return CodeUI.L("ui_shop_layer_1", "1층 · 땅");
            case 2:  return CodeUI.L("ui_shop_layer_2", "2층 · 얼음땅");
            case 3:  return CodeUI.L("ui_shop_layer_3", "3층 · 용암땅");
            case 4:  return CodeUI.L("ui_shop_layer_4", "4층 · 우주");
            default: return CodeUI.L("ui_shop_layer_other", "기타");
        }
    }
}
