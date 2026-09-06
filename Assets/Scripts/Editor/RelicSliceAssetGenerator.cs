using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Relic;
using Relic.Data;

// 유물 슬라이스 에셋(RelicSO 3종 + RelicDatabase)을 코드로 생성하는 에디터 툴.
// 메뉴: Tools > Relic > Generate Slice Assets  (한 번 클릭)
// SerializeReference behaviour는 in-code 기본값으로 채워진다(각 behaviour의 필드 초기화값).
public static class RelicSliceAssetGenerator
{
    private const string RootFolder = "Assets/GameData";
    private const string RelicFolder = "Assets/GameData/Relics";

    [MenuItem("Tools/Relic/Generate Slice Assets")]
    public static void Generate()
    {
        EnsureFolder(RootFolder, "Assets", "GameData");
        EnsureFolder(RelicFolder, RootFolder, "Relics");

        var stat   = CreateRelic(RelicID.TestStatRelic, "테스트 스탯 유물", RelicType.Passive, new StatRelicBehaviour());
        var pigeon = CreateRelic(RelicID.PigeonFeather, "비둘기 깃털",       RelicType.Passive, new DoubleJumpRelic());
        var magnet = CreateRelic(RelicID.Magnet,        "자석",             RelicType.Passive, new MagnetRelic()); // 상시 발동
        var invinc = CreateRelic(RelicID.Invincibility, "무적",             RelicType.Active,  new InvincibilityRelic());
        var anvil  = CreateRelic(RelicID.Anvil,         "모루",             RelicType.Passive, new AnvilRelic()); // 착지 시 지형 파괴(무게비례)
        var plasma = CreateRelic(RelicID.PlasmaCutter,  "플라즈마 커터",     RelicType.Active,  new PlasmaCutterRelic());
        var gambler = CreateRelic(RelicID.GamblerGlasses, "도박꾼의 안경",     RelicType.Passive, new GamblerGlassesRelic()); // 파기 범위 동전던지기(0.5배 or 2배)
        var steroid = CreateRelic(RelicID.Steroid,      "스테로이드",        RelicType.Active,  new SteroidRelic());      // 삽 광역파기 버프
        var drone   = CreateRelic(RelicID.DrillDrone,   "굴착 드론",         RelicType.Passive, new DrillDroneRelic());    // 자율 파괴 드론
        var spider  = CreateRelic(RelicID.SpiderGlove,  "거미줄 장갑",       RelicType.Passive, // 벽타기 속도↑ = WallClimbSpeed 스탯
            new StatRelicBehaviour().Configure(RelicID.SpiderGlove, StatType.WallClimbSpeed, ModifierType.Percent, new[] { 1.15f, 1.30f, 1.50f }));
        var genr    = CreateRelic(RelicID.Generator,    "발전기",           RelicType.Active,  new GeneratorRelic());     // 배터리 회복(즉발)
        var spring  = CreateRelic(RelicID.JunkSpring,   "고물 스프링",       RelicType.Passive, new JunkSpringRelic());     // 점프키 홀드-차징 슈퍼 점프
        var gravFlip = CreateRelic(RelicID.GravityFlip, "반중력 장치",       RelicType.Active,  new GravityFlipRelic());   // 중력 반전(AntiGravityHandler)
        var dashBomb = CreateRelic(RelicID.DashBomb,    "폭발 드릴",         RelicType.Passive, new DashBombRelic());      // 드릴 대쉬 끝점 폭발
        var furnace  = CreateRelic(RelicID.Furnace,     "용광로",           RelicType.Passive, new FurnaceRelic());       // 상태이상↑ → 채굴↑
        var lightning = CreateRelic(RelicID.Lightning,  "번개",             RelicType.Active,  new LightningRelic());     // 주변 광물 연쇄 파괴
        var toolSwap = CreateRelic(RelicID.ToolSwap,    "도구 역할 스왑",    RelicType.Passive, new ToolSwapRelic());      // 삽=돌, 곡괭이=땅 (ToolCapabilities 토글)

        // 암시장: 가격DB 에셋을 자동 탐색해 behaviour에 주입(지하엔 상점이 없어 직접 참조 필요).
        var priceDb = FindFirstAsset<MineralPriceDatabase>();
        if (priceDb == null)
            Debug.LogWarning("[Relic] MineralPriceDatabase 에셋을 찾지 못함 — 암시장은 런타임에 ShopManager 폴백을 시도합니다.");
        var blackMarket = CreateRelic(RelicID.BlackMarket, "암시장", RelicType.Active,
            new BlackMarketRelic().Configure(priceDb));
        var minerDrone = CreateRelic(RelicID.MinerDrone, "채굴 드론",     RelicType.Passive, new MinerDroneRelic()); // 로밍 탐색→이동→파기 동행 드론
        var jetpack    = CreateRelic(RelicID.Jetpack,    "제트팩",       RelicType.Passive, new JetpackRelic());    // 드릴 대체 비행(드릴배터리 소모)
        var detectionPulse = CreateRelic(RelicID.DetectionPulse, "탐지파동", RelicType.Active, new DetectionPulseRelic()); // 반경 내 특수청크 예측 탐지
        var overloadBattery = CreateRelic(RelicID.OverloadBattery, "과부하 배터리", RelicType.Passive, new OverloadBatteryRelic()); // 드릴 대시 배터리 효율 2배·방향 무작위
        var mp3 = CreateRelic(RelicID.Mp3, "mp3", RelicType.Passive, new Mp3Relic()); // 머리 위 비트 인디케이터·삽 발사 타이밍 판정(Perfect/Good/Bad)으로 파기 범위↑
        var blind = CreateRelic(RelicID.Blind, "맹인", RelicType.Passive, new BlindRelic()); // 시야 극축소·삽/곡괭이 파동으로 순간 주변 확보(대가: 스태미나↓·차징/스윙↑·범위↓)
        var xray = CreateRelic(RelicID.XRay, "엑스레이", RelicType.Active, new XRayRelic()); // 3초 화면 흑백반전 + 흙 너머 광물·특수청크·함정 투시(쿨 2분)
        var hourglass = CreateRelic(RelicID.Hourglass, "모래시계", RelicType.Passive, new HourglassRelic()); // 다른 슬롯 유물 쿨타임 −30/40/50%
        var oneWayPortal = CreateRelic(RelicID.OneWayPortal, "일회용 포탈", RelicType.Active, new OneWayPortalRelic()); // 설치→재발동 귀환+소멸(토글, 쿨 9/7/5분)
        var trident = CreateRelic(RelicID.Trident, "삼지창", RelicType.Passive, new TridentRelic()); // 삽 파기 → 창날 3줄기 3연타

        // 엘리베이터 신호기: 상점 매물이 아니라 업그레이드 트리로 지급되는 첫 유물이다.
        // unlockNodeId를 채워 두면 그 노드를 산 순간 RelicManager가 보유로 넣어 준다.
        var elevatorTracker = CreateRelic(RelicID.ElevatorTracker, "엘리베이터 신호기", RelicType.Passive,
            new ElevatorTrackerRelic(), unlockNodeId: "RelicUnlock_ElevatorTracker_T0"); // 주변 엘베를 지도에 자동 표시

        var db = FindOrCreate<RelicDatabase>($"{RelicFolder}/RelicDatabase.asset");
        db.allRelics = new List<RelicSO> { stat, pigeon, magnet, invinc, anvil, plasma, gambler, steroid, drone, spider, genr, spring, gravFlip, dashBomb, furnace, lightning, toolSwap, blackMarket, minerDrone, jetpack, detectionPulse, overloadBattery, mp3, blind, xray, hourglass, oneWayPortal, trident, elevatorTracker };
        EditorUtility.SetDirty(db);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Relic] 슬라이스 에셋 생성 완료 → {RelicFolder} (RelicDatabase + 3종). " +
                  "DatabaseLoader의 relicDatabase 필드에 RelicDatabase.asset을 할당하세요.");
        Selection.activeObject = db;
    }

    private static RelicSO CreateRelic(RelicID id, string name, RelicType type, RelicBehaviour behaviour,
                                      string unlockNodeId = null)
    {
        string path = $"{RelicFolder}/{id}.asset";
        var so = AssetDatabase.LoadAssetAtPath<RelicSO>(path);
        if (so == null)
        {
            so = ScriptableObject.CreateInstance<RelicSO>();
            AssetDatabase.CreateAsset(so, path);
        }

        so.id = id;
        // 이름/설명은 Localization 키로 관리한다(실제 텍스트는 StreamingAssets/Data/Relics.csv).
        // 이미 키가 채워진 에셋은 건드리지 않는다 — 재실행 때 수동 지정한 키가 한글로 되돌아가는 것을 방지.
        // (name 파라미터는 어떤 유물인지 알려주는 주석 겸 문서용으로만 남겨둔다.)
        if (string.IsNullOrEmpty(so.displayNameKey))
            so.displayNameKey = ToLocKey(id, "name");
        if (string.IsNullOrEmpty(so.descriptionKey))
            so.descriptionKey = ToLocKey(id, "desc");
        so.type = type;
        so.maxLevel = 3;
        // 업그레이드로 지급되는 유물만 채운다. 빈 값으로 덮어쓰지 않는다 —
        // 인스펙터에서 직접 물린 노드 id가 재실행 때 지워지면 유물이 조용히 안 나온다.
        if (!string.IsNullOrEmpty(unlockNodeId)) so.unlockNodeId = unlockNodeId;
        so.upgradeCosts = new[]
        {
            new RelicUpgradeCost { gold = 100, material = ItemID.None, materialCount = 0 }, // Lv1→2
            new RelicUpgradeCost { gold = 250, material = ItemID.None, materialCount = 0 }, // Lv2→3
        };
        // [SerializeReference] behaviour는 '없거나 타입이 바뀐 경우'에만 새로 꽂는다.
        // 무조건 대입하면 재실행 때마다 인스펙터에서 튜닝한 파라미터(bpm·오프셋 등)가
        // 전부 코드 기본값으로 초기화된다. 값을 일부러 되돌리려면 에셋을 지우고 다시 생성할 것.
        if (so.behaviour == null || so.behaviour.GetType() != behaviour.GetType())
            so.behaviour = behaviour;

        EditorUtility.SetDirty(so);
        return so;
    }

    // RelicID(PascalCase) → "relic_snake_case_{suffix}" Localization 키 변환.
    private static string ToLocKey(RelicID id, string suffix)
    {
        string s = id.ToString();
        var sb = new System.Text.StringBuilder("relic_");
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLower(c));
        }
        sb.Append('_').Append(suffix);
        return sb.ToString();
    }

    private static T FindOrCreate<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null)
        {
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
        }
        return a;
    }

    private static T FindFirstAsset<T>() where T : Object
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        if (guids == null || guids.Length == 0) return null;
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<T>(path);
    }

    private static void EnsureFolder(string full, string parent, string leaf)
    {
        if (!AssetDatabase.IsValidFolder(full))
            AssetDatabase.CreateFolder(parent, leaf);
    }
}
