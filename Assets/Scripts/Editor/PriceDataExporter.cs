// @tags: editor, price, export, json, tool, economy
using System.Collections.Generic;
using System.IO;
using Relic.Data;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 현재 에셋의 가격 값을 읽어 StreamingAssets/priceData.json 으로 뽑는다.
///
/// 초기 마이그레이션용이자, 나중에 에셋 쪽에서 값을 만졌을 때 다시 뽑는 용도.
/// 노드 비용만 예외로 UpgradeTree.csv(트리의 단일 원본)에서 읽는다 — 이유는 ExportNodes 주석.
///
/// 설계: Assets/Docs/economy/price-data-json-design.md §6
/// </summary>
public static class PriceDataExporter
{
    private const string OutPath = "Assets/StreamingAssets/priceData.json";

    private const string MineralPriceDbPath = "Assets/GameData/ShopData/MineralPriceDatabase.asset";
    private const string ShopDbPath         = "Assets/GameData/ShopData/ShopItemDatabase.asset";
    private const string MineralDir         = "Assets/GameData/MineralData";
    private const string RelicDir           = "Assets/GameData/Relics";
    private const string NodeDir            = "Assets/GameData/UpgradeData/Node";
    private const string TileDataPath       = "Assets/StreamingAssets/tileData.json";

    /// <summary>어느 지층에도 속하지 않는 항목의 layer 라벨.</summary>
    private const string UnlistedLabel = "Unlisted";

    /// <summary>
    /// 기본 동작: **JSON에 이미 있는 값은 보존**하고 id 목록만 동기화한다.
    ///
    /// priceData.json은 사람이 직접 고치는 튜닝 창구다(설계 §2 — 빌드 후 게임을
    /// 안 고치고 튜닝). 예전처럼 에셋 값으로 통째로 덮으면 Export를 돌릴 때마다
    /// 손으로 맞춘 가격이 낡은 에셋 값으로 되돌아간다 — 실제로 두 번 그랬다(2026-08-21/22).
    ///
    /// 예외: upgradeNodes는 항상 UpgradeTree.csv에서 새로 굽는다.
    /// 노드 가격의 튜닝 창구는 JSON이 아니라 CSV(트리의 단일 원본)이기 때문이다.
    /// </summary>
    [MenuItem("Tools/Economy/Export Prices to JSON")]
    public static void Export()
    {
        bool overwriteAll = false;
        if (File.Exists(OutPath))
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "가격 JSON 내보내기",
                "priceData.json은 손으로 튜닝하는 파일이다.\n\n" +
                "· 값 보존(권장): JSON의 가격·무게를 그대로 두고, 새로 생긴 항목 추가와\n" +
                "  사라진 항목 정리, 노드 절(UpgradeTree.csv) 갱신만 한다.\n" +
                "· 에셋 값으로 덮어쓰기: 예전 동작. JSON의 수동 튜닝이 전부 에셋 값으로 돌아간다.",
                "값 보존 (권장)", "취소", "에셋 값으로 전부 덮어쓰기");
            if (choice == 1) return;
            overwriteAll = choice == 2;
        }

        var data = new PriceData
        {
            minerals      = ExportMinerals(),
            shopItems     = ExportShop(ShopItemType.Item),
            shopEquipment = ExportShopEquipment(),
            shopRelics    = ExportShop(ShopItemType.Relic),
            relicUpgrades = ExportRelicUpgrades(),
            upgradeNodes  = ExportNodes(),
        };

        int preserved = 0;
        if (!overwriteAll && File.Exists(OutPath))
            preserved = PreserveJsonValues(data);

        Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
        File.WriteAllText(OutPath, JsonUtility.ToJson(data, true));
        AssetDatabase.Refresh();

        Debug.Log($"[PriceDataExporter] 저장 완료: {OutPath}\n" +
                  $"광물 {data.minerals.Length} / 소모품 {data.shopItems.Length} / 장비 {data.shopEquipment.Length} / " +
                  $"유물 {data.shopRelics.Length} / 유물강화 {data.relicUpgrades.Length} / 노드 {data.upgradeNodes.Length}" +
                  (overwriteAll ? "\n(에셋 값으로 전부 덮어씀)"
                                : $"\n(JSON 수동 튜닝 보존 {preserved}건 — 노드 절만 CSV 기준)"));
    }

    /// <summary>
    /// 기존 JSON에 있는 항목의 값(가격·무게·강화비)을 새 데이터 위에 되살린다.
    /// upgradeNodes는 건드리지 않는다 — 그쪽 원본은 UpgradeTree.csv다.
    /// 반환값은 보존한 항목 수.
    /// </summary>
    private static int PreserveJsonValues(PriceData fresh)
    {
        PriceData old;
        try { old = JsonUtility.FromJson<PriceData>(File.ReadAllText(OutPath)); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PriceDataExporter] 기존 JSON 파싱 실패 — 보존 없이 진행: {e.Message}");
            return 0;
        }
        if (old == null) return 0;

        int kept = 0;

        if (old.minerals != null && fresh.minerals != null)
        {
            var prev = new Dictionary<string, MineralPriceEntry>();
            foreach (var e in old.minerals) if (!string.IsNullOrEmpty(e.id)) prev[e.id] = e;
            foreach (var e in fresh.minerals)
            {
                if (!prev.TryGetValue(e.id, out var p)) continue;
                if (e.price != p.price || !Mathf.Approximately(e.weight, p.weight)) kept++;
                e.price = p.price;
                e.weight = p.weight;
            }
        }

        kept += PreserveIdPrices(old.shopItems, fresh.shopItems);
        kept += PreserveIdPrices(old.shopEquipment, fresh.shopEquipment);
        kept += PreserveIdPrices(old.shopRelics, fresh.shopRelics);

        if (old.relicUpgrades != null && fresh.relicUpgrades != null)
        {
            var prev = new Dictionary<string, RelicUpgradeEntry>();
            foreach (var e in old.relicUpgrades) if (!string.IsNullOrEmpty(e.id)) prev[e.id] = e;
            foreach (var e in fresh.relicUpgrades)
            {
                if (!prev.TryGetValue(e.id, out var p) || p.gold == null) continue;
                e.gold = p.gold;
                kept++;
            }
        }

        return kept;
    }

    private static int PreserveIdPrices(IdPriceEntry[] old, IdPriceEntry[] fresh)
    {
        if (old == null || fresh == null) return 0;
        var prev = new Dictionary<string, int>();
        foreach (var e in old) if (!string.IsNullOrEmpty(e.id)) prev[e.id] = e.price;

        int kept = 0;
        foreach (var e in fresh)
        {
            if (!prev.TryGetValue(e.id, out int p)) continue;
            if (e.price != p) kept++;
            e.price = p;
        }
        return kept;
    }

    private static MineralPriceEntry[] ExportMinerals()
    {
        var priceDb = AssetDatabase.LoadAssetAtPath<MineralPriceDatabase>(MineralPriceDbPath);
        var layers  = MineralLayerIndex.Build();
        var list    = new List<MineralPriceEntry>();

        foreach (string guid in AssetDatabase.FindAssets("t:MineralSO", new[] { MineralDir }))
        {
            var so = AssetDatabase.LoadAssetAtPath<MineralSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so == null || so.mineralID == MineralID.None) continue;

            string id = so.mineralID.ToString();
            list.Add(new MineralPriceEntry
            {
                id     = id,
                price  = priceDb != null ? priceDb.GetPrice(so.mineralID) : 0,
                weight = so.weight,
                layer  = layers.LabelOf(id),
            });
        }

        // 지층 순(얕은 곳 → 깊은 곳) → 층 안에서는 tileData.json에 적힌 순서.
        // 층에 없는 광물(던전 보상 등)은 맨 뒤에 이름순으로 모인다.
        list.Sort((a, b) =>
        {
            int cmp = layers.SortKeyOf(a.id).CompareTo(layers.SortKeyOf(b.id));
            return cmp != 0 ? cmp : string.CompareOrdinal(a.id, b.id);
        });
        return list.ToArray();
    }

    /// <summary>
    /// tileData.json을 읽어 "광물 → 처음 나오는 지층"을 만든다.
    ///
    /// 같은 광물이 여러 층에 걸쳐 나오는 경우(Copper는 Dirt·Ice, Diamond는 Magma·Meteorite)가 있어
    /// **가장 얕은 층**을 그 광물의 소속으로 본다. 층 순서는 JSON에 적힌 순서가 아니라
    /// startDepth 내림차순(0 → -20 → -40 → -60)으로 다시 세워, 항목 순서가 바뀌어도 흔들리지 않는다.
    /// </summary>
    private class MineralLayerIndex
    {
        private const int PerLayerStride = 1000;
        private const int UnlistedKey    = int.MaxValue;

        private readonly Dictionary<string, string> _label = new Dictionary<string, string>();
        private readonly Dictionary<string, int>    _key   = new Dictionary<string, int>();

        public string LabelOf(string id) => _label.TryGetValue(id, out string s) ? s : UnlistedLabel;
        public int SortKeyOf(string id)  => _key.TryGetValue(id, out int k) ? k : UnlistedKey;

        public static MineralLayerIndex Build()
        {
            var index = new MineralLayerIndex();
            var tiles = ReadLayersShallowToDeep();

            for (int layer = 0; layer < tiles.Count; layer++)
            {
                var tile = tiles[layer];
                if (tile?.minerals == null) continue;

                string label = LayerLabel(layer, tile);

                for (int i = 0; i < tile.minerals.Count; i++)
                {
                    string id = tile.minerals[i].mineralType;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (index._label.ContainsKey(id)) continue;   // 더 얕은 층이 이미 가져갔다

                    index._label[id] = label;
                    index._key[id]   = layer * PerLayerStride + i;
                }
            }

            return index;
        }
    }

    /// <summary>
    /// tileData.json의 지층을 얕은 곳 → 깊은 곳(startDepth 내림차순: 0 → -20 → -40 → -60)으로
    /// 세워 돌려준다. 광물과 장비가 같은 층 번호·라벨을 쓰도록 한 곳에서만 읽는다.
    /// 파일이 없거나 깨졌으면 빈 리스트다 — 부르는 쪽이 라벨 없이 진행한다.
    /// </summary>
    private static List<TileDataJson> ReadLayersShallowToDeep()
    {
        var empty = new List<TileDataJson>();

        if (!File.Exists(TileDataPath))
        {
            Debug.LogWarning($"[PriceDataExporter] {TileDataPath} 없음 — layer 라벨 없이 뽑는다.");
            return empty;
        }

        TileDatabaseJson db;
        try
        {
            db = JsonUtility.FromJson<TileDatabaseJson>(File.ReadAllText(TileDataPath));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PriceDataExporter] tileData.json 파싱 실패 — layer 라벨 생략. 오류: {e.Message}");
            return empty;
        }

        if (db == null || db.tiles == null) return empty;

        var tiles = new List<TileDataJson>(db.tiles);
        tiles.Sort((a, b) => b.startDepth.CompareTo(a.startDepth));   // 0, -20, -40, -60
        return tiles;
    }

    private static string LayerLabel(int layer, TileDataJson tile) => $"L{layer}_{tile.tileType}";

    /// <summary>
    /// 장비 세트 → 소속 지층(0-based, 광물 라벨과 같은 번호). 설계 문서의 "1층"이 여기 0이다.
    ///
    /// 광물은 tileData.json에 층이 적혀 있지만 장비에는 그런 원본이 없다 —
    /// EquipmentSO.level이 27종 전부 0이라 쓸 수 없어 설계 표를 그대로 옮겨 적는다.
    /// 근거: Assets/Docs/economy/equipment-relic-price-design.md §4
    /// </summary>
    private static readonly Dictionary<string, int> EquipmentSetLayer = new Dictionary<string, int>
    {
        { "Miner",     0 },
        { "Winter",    1 }, { "Climber",  1 }, { "Carrier", 1 },
        { "Heat",      2 }, { "Engineer", 2 }, { "Shovel",  2 },
        { "Scientist", 3 }, { "Marathon", 3 },
    };

    /// <summary>표에 없는 장비(레거시 Leather·Iron)를 맨 뒤로 보내는 값.</summary>
    private const int UnlistedLayer = int.MaxValue;

    private static int EquipmentLayerOf(string id)
    {
        foreach (var pair in EquipmentSetLayer)
            if (id.StartsWith(pair.Key, System.StringComparison.Ordinal)) return pair.Value;
        return UnlistedLayer;
    }

    /// <summary>소모품·유물 상점가. 장비는 층 라벨이 붙으므로 ExportShopEquipment가 따로 뽑는다.</summary>
    private static IdPriceEntry[] ExportShop(ShopItemType kind)
    {
        var shop = AssetDatabase.LoadAssetAtPath<ShopItemDatabase>(ShopDbPath);
        var list = new List<IdPriceEntry>();
        if (shop == null || shop.shopItems == null) return list.ToArray();

        foreach (var row in shop.shopItems)
        {
            if (row.itemType != kind) continue;

            string id = kind == ShopItemType.Item ? row.itemID.ToString()
                                                  : row.relicID.ToString();

            list.Add(new IdPriceEntry { id = id, price = row.price });
        }
        return list.ToArray();
    }

    /// <summary>
    /// 장비 상점가. 항목마다 소속 지층 라벨을 붙이고 **지층별로 묶어 층 안에서는 가격 오름차순**으로 뽑는다.
    ///
    /// 28종을 에셋에 적힌 순서대로 늘어놓으면 "이 층 장비가 그 층 벌이에 맞나"가 눈에 안 들어온다.
    /// 층으로 나눠 싼 것부터 세우면 세트 사다리(equipment-relic-price-design.md §5 가격표)와
    /// 같은 모양으로 읽힌다 — minerals 절이 층 순으로 나오는 것과 같은 이유다.
    ///
    /// unlockNodeId도 같이 적는다. 가격만 봐서는 "이 값을 감당할 때쯤 상점에 떠 있나"를 알 수 없고,
    /// 그 판정은 상점 에셋(ShopItemData.unlockNodeId)에만 있어 튜닝할 때 따로 열어봐야 했다.
    /// </summary>
    private static EquipmentPriceEntry[] ExportShopEquipment()
    {
        var shop  = AssetDatabase.LoadAssetAtPath<ShopItemDatabase>(ShopDbPath);
        var tiles = ReadLayersShallowToDeep();
        var list  = new List<EquipmentPriceEntry>();
        if (shop == null || shop.shopItems == null) return list.ToArray();

        foreach (var row in shop.shopItems)
        {
            if (row.itemType != ShopItemType.Equipment) continue;

            string id    = row.equipmentID.ToString();
            int    layer = EquipmentLayerOf(id);

            list.Add(new EquipmentPriceEntry
            {
                id           = id,
                price        = row.price,
                layer        = layer < tiles.Count ? LayerLabel(layer, tiles[layer]) : UnlistedLabel,
                unlockNodeId = row.unlockNodeId ?? string.Empty,
            });
        }

        // 지층 순(얕은 곳 → 깊은 곳) → 층 안에서는 가격 오름차순, 같은 값이면 id 순.
        // 층이 없는 레거시 장비(Leather·Iron)는 Unlisted로 맨 뒤에 모인다.
        list.Sort((a, b) =>
        {
            int cmp = EquipmentLayerOf(a.id).CompareTo(EquipmentLayerOf(b.id));
            if (cmp != 0) return cmp;
            cmp = a.price.CompareTo(b.price);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.id, b.id);
        });
        return list.ToArray();
    }

    private static RelicUpgradeEntry[] ExportRelicUpgrades()
    {
        var list = new List<RelicUpgradeEntry>();

        foreach (string guid in AssetDatabase.FindAssets("t:RelicSO", new[] { RelicDir }))
        {
            var so = AssetDatabase.LoadAssetAtPath<RelicSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so == null || so.id == RelicID.None) continue;
            if (so.id == RelicID.TestStatRelic) continue;   // 테스트용 — JSON에 넣지 않는다
            if (so.upgradeCosts == null) continue;

            var gold = new int[so.upgradeCosts.Length];
            for (int i = 0; i < gold.Length; i++) gold[i] = so.upgradeCosts[i].gold;

            list.Add(new RelicUpgradeEntry { id = so.id.ToString(), gold = gold });
        }

        list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return list.ToArray();
    }

    /// <summary>
    /// 노드 비용을 UpgradeTree.csv에서 읽는다 — 노드 에셋이 아니다.
    ///
    /// 에셋에서 읽으면 생성기를 안 돌린 상태에서 export했을 때 낡은 값이 JSON에 굳고,
    /// 그 JSON이 런타임에 에셋을 덮어써서 원본과 영구히 갈라진다.
    /// 2026-08-21 시점에 실제로 그 상태였다 — 50개 중 48개가 어긋났고,
    /// 면허 I이 원본 1,280G인데 게임은 700G로 돌고 있었다.
    /// 설계: specs/2026-08-21-upgrade-tree-autogen-design.md §5.1
    /// </summary>
    private static NodeCostEntry[] ExportNodes()
    {
        var errors = new List<string>();
        var nodes = UpgradeTreeCsvReader.Read(UpgradeTreeCsvReader.DefaultPath, errors);

        foreach (string e in errors)
            Debug.LogWarning($"[PriceDataExporter] CSV: {e}");

        if (nodes.Count == 0)
        {
            Debug.LogError($"[PriceDataExporter] {UpgradeTreeCsvReader.DefaultPath} 에서 " +
                           "노드를 못 읽었다. upgradeNodes 절이 비게 된다.");
            return new NodeCostEntry[0];
        }

        var list = new List<NodeCostEntry>(nodes.Count);
        foreach (var n in nodes)
            list.Add(new NodeCostEntry { id = n.nodeId, cost = n.cost });

        // 가격 오름차순 — 트리 진행 순서를 눈으로 훑기 위한 정렬. 같은 값이면 id 순.
        list.Sort((a, b) => a.cost != b.cost ? a.cost.CompareTo(b.cost) : string.CompareOrdinal(a.id, b.id));
        return list.ToArray();
    }
}
