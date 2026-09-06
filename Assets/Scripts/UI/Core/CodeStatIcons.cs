// @tags: ui, stat, icon, code-generated, catalog, player, symbol

using System.Collections.Generic;
using UnityEngine;

/// <summary>플레이어 스탯을 묶는 카테고리 — 아이콘·색·헤더가 이 단위로 붙는다.</summary>
public enum StatCategory
{
    Mining,       // 채광
    Movement,     // 이동
    Stamina,      // 스태미나
    Drill,        // 드릴
    Combat,       // 전투
    Economy,      // 경제
    Exploration   // 탐색
}

/// <summary>스탯 수치를 어떻게 표기할지.</summary>
public enum StatValueFormat
{
    Plain,       // 그대로(정수/소수)
    Multiplier,  // ×1.20
    Percent01,   // 0~1 → 20%
    Seconds      // 0.5s
}

/// <summary>
/// 인스펙터에서 카테고리별 스탯 아이콘 스프라이트를 덮어쓰고 싶을 때 쓰는 묶음.
/// 비운 항목은 <see cref="CodeStatIcons"/>의 코드 생성 심볼로 폴백한다(UISkin과 같은 규칙).
/// </summary>
[System.Serializable]
public class StatIconSet
{
    public Sprite mining;
    public Sprite movement;
    public Sprite stamina;
    public Sprite drill;
    public Sprite combat;
    public Sprite economy;
    public Sprite exploration;

    public Sprite Get(StatCategory cat)
    {
        switch (cat)
        {
            case StatCategory.Mining: return mining;
            case StatCategory.Movement: return movement;
            case StatCategory.Stamina: return stamina;
            case StatCategory.Drill: return drill;
            case StatCategory.Combat: return combat;
            case StatCategory.Economy: return economy;
            case StatCategory.Exploration: return exploration;
        }
        return null;
    }
}

/// <summary>한 줄로 표시되는 스탯의 표기 메타데이터.</summary>
public struct StatDisplay
{
    public StatType type;
    public StatCategory category;
    public string nameKey;       // 로컬라이제이션 키 (없으면 nameFallback 사용)
    public string nameFallback;  // 한글 폴백
    public StatValueFormat format;
    public bool higherIsBetter;  // 값이 클수록 좋은가 (강화량 색 판정용)

    public StatDisplay(StatType type, StatCategory category, string nameKey, string nameFallback,
        StatValueFormat format, bool higherIsBetter)
    {
        this.type = type;
        this.category = category;
        this.nameKey = nameKey;
        this.nameFallback = nameFallback;
        this.format = format;
        this.higherIsBetter = higherIsBetter;
    }
}

/// <summary>
/// 플레이어에게 보여줄 스탯 목록·카테고리 색·헤더를 한곳에 모은 정적 카탈로그.
/// 업그레이드·장비로 강화되는 핵심 스탯을 카테고리별로 정리한 것으로,
/// 순서가 곧 <see cref="CodeStatPanel"/>의 표시 순서다.
/// </summary>
public static class StatCatalog
{
    // 표시 순서 = 배열 순서. 카테고리가 바뀌는 지점에서 헤더가 삽입된다.
    public static readonly StatDisplay[] Stats =
    {
        // === 채광 ===
        new StatDisplay(StatType.MiningLevel,            StatCategory.Mining, "ui_stat_mining_level",   "채광 레벨",   StatValueFormat.Plain,      true),
        new StatDisplay(StatType.MiningSpeed,            StatCategory.Mining, "ui_stat_mining_speed",   "채굴 속도",   StatValueFormat.Multiplier, true),
        new StatDisplay(StatType.MiningRange,            StatCategory.Mining, "ui_stat_mining_range",   "채굴 범위",   StatValueFormat.Multiplier, true),
        new StatDisplay(StatType.MiningCooldown,         StatCategory.Mining, "ui_stat_mining_cd",      "채굴 쿨다운", StatValueFormat.Seconds,    false),
        new StatDisplay(StatType.PickaxeDamageUp,        StatCategory.Mining, "ui_stat_pickaxe_dmg",    "곡괭이 피해", StatValueFormat.Multiplier, true),
        new StatDisplay(StatType.ToolRange,              StatCategory.Mining, "ui_stat_tool_range",     "도구 사거리", StatValueFormat.Multiplier, true),
        new StatDisplay(StatType.RareMineralChance,      StatCategory.Mining, "ui_stat_rare_chance",    "희귀 광물 확률", StatValueFormat.Percent01, true),
        new StatDisplay(StatType.MineralExtraDropChance, StatCategory.Mining, "ui_stat_extra_drop",     "추가 드롭 확률", StatValueFormat.Percent01, true),
        new StatDisplay(StatType.RockMineralCountUp,     StatCategory.Mining, "ui_stat_rock_drop_count","돌 광물 수",   StatValueFormat.Plain,      true),

        // === 이동 ===
        new StatDisplay(StatType.MoveSpeed,       StatCategory.Movement, "ui_stat_move_speed", "이동 속도",   StatValueFormat.Plain,      true),
        new StatDisplay(StatType.JumpForce,       StatCategory.Movement, "ui_stat_jump",       "점프력",     StatValueFormat.Plain,      true),
        new StatDisplay(StatType.WallClimbSpeed,  StatCategory.Movement, "ui_stat_climb",      "벽타기 속도", StatValueFormat.Plain,      true),
        new StatDisplay(StatType.FallDamageReduce,StatCategory.Movement, "ui_stat_fall_dmg",   "낙하 피해",   StatValueFormat.Multiplier, false),

        // === 스태미나 ===
        new StatDisplay(StatType.MaxStamina,           StatCategory.Stamina, "ui_stat_max_stamina", "최대 스태미나", StatValueFormat.Plain, true),
        new StatDisplay(StatType.StaminaCostPerSecond, StatCategory.Stamina, "ui_stat_stamina_cost","스태미나 소모", StatValueFormat.Plain, false),
        // StaminaRegen은 일부러 뺐다 — 이 스탯의 최종값은 더 이상 "초당 회복량"이 아니라
        // StaminaManager.CurrentRegenPerSecond가 쓰는 배율의 기준점일 뿐이라(기준값 20 = 1배),
        // 그대로 숫자로 띄우면 실제 회복 속도와 다른 값이 표시된다.
        // 되살리려면 배율(최종값/기준값) 표기로 바꾸거나 CurrentRegenPerSecond를 직접 그릴 것.
        new StatDisplay(StatType.ShovelStaminaReduce,  StatCategory.Stamina, "ui_stat_shovel_stamina","삽질 스태미나", StatValueFormat.Multiplier, false),
        new StatDisplay(StatType.PickaxeStaminaReduce, StatCategory.Stamina, "ui_stat_pickaxe_stamina","곡괭이 스태미나", StatValueFormat.Multiplier, false),
        new StatDisplay(StatType.StaminaCostReduce,    StatCategory.Stamina, "ui_stat_stamina_scale","행동 스태미나", StatValueFormat.Multiplier, false),

        // === 드릴 ===
        new StatDisplay(StatType.DrillBatteryCapacity, StatCategory.Drill, "ui_stat_drill_cap",   "드릴 배터리", StatValueFormat.Plain, true),
        new StatDisplay(StatType.DrillBatteryRegen,    StatCategory.Drill, "ui_stat_drill_regen", "배터리 회복", StatValueFormat.Plain, true),
        new StatDisplay(StatType.DrillDrainReduce,     StatCategory.Drill, "ui_stat_drill_drain", "배터리 소모", StatValueFormat.Multiplier, false),
        new StatDisplay(StatType.DrillRadius,          StatCategory.Drill, "ui_stat_drill_radius","드릴 파기 범위", StatValueFormat.Plain,      true),

        // === 전투 ===
        new StatDisplay(StatType.MaxHp,       StatCategory.Combat, "ui_stat_max_hp", "최대 체력",   StatValueFormat.Plain,     true),
        new StatDisplay(StatType.Damage,      StatCategory.Combat, "ui_stat_damage", "공격력",     StatValueFormat.Plain,     true),
        new StatDisplay(StatType.CritChanceUp,StatCategory.Combat, "ui_stat_crit",   "치명타 확률", StatValueFormat.Percent01, true),
        new StatDisplay(StatType.Defense,     StatCategory.Combat, "ui_stat_defense","방어력",     StatValueFormat.Plain,     true),

        // === 경제 ===
        new StatDisplay(StatType.MineralSellBonus,  StatCategory.Economy, "ui_stat_sell_bonus", "판매 보너스", StatValueFormat.Multiplier, true),
        new StatDisplay(StatType.InventoryWeightUp, StatCategory.Economy, "ui_stat_weight",     "무게 한도",   StatValueFormat.Plain,      true),
        new StatDisplay(StatType.InventorySlotUp,   StatCategory.Economy, "ui_stat_bag_slots",  "인벤 슬롯",   StatValueFormat.Plain,      true),
        new StatDisplay(StatType.WarehouseCapacityUp,StatCategory.Economy,"ui_stat_wh_slots",   "창고 확장",   StatValueFormat.Plain,      true),

        // === 탐색 ===
        new StatDisplay(StatType.VisionRadiusUp,       StatCategory.Exploration, "ui_stat_vision",     "시야 범위",   StatValueFormat.Multiplier, true),
        new StatDisplay(StatType.FlashlightRangeUp,    StatCategory.Exploration, "ui_stat_flashlight", "손전등 사거리", StatValueFormat.Multiplier, true),
        new StatDisplay(StatType.EnvironmentResistance,StatCategory.Exploration, "ui_stat_env_resist", "환경 저항",   StatValueFormat.Plain,      true),
        new StatDisplay(StatType.HazardFrostResist,    StatCategory.Exploration, "ui_stat_frost_resist","빙결 저항",   StatValueFormat.Plain,      true),
        new StatDisplay(StatType.HazardBurnResist,     StatCategory.Exploration, "ui_stat_burn_resist", "화상 저항",   StatValueFormat.Plain,      true),
        new StatDisplay(StatType.HazardRadiationResist,StatCategory.Exploration, "ui_stat_rad_resist",  "방사선 저항", StatValueFormat.Plain,      true),
    };

    public static Color CategoryColor(StatCategory cat)
    {
        switch (cat)
        {
            case StatCategory.Mining:      return CodeUI.Hex(0xD8A05F); // 앰버
            case StatCategory.Movement:    return CodeUI.Hex(0x8FD35F); // 그린
            case StatCategory.Stamina:     return CodeUI.Hex(0x5FC7D8); // 시안
            case StatCategory.Drill:       return CodeUI.Hex(0x7B8FD4); // 블루
            case StatCategory.Combat:      return CodeUI.Hex(0xE06C6C); // 레드
            case StatCategory.Economy:     return CodeUI.Hex(0xF5C63F); // 골드
            case StatCategory.Exploration: return CodeUI.Hex(0xB98FD4); // 퍼플
        }
        return CodeUI.LabelColor;
    }

    public static string CategoryKey(StatCategory cat)
    {
        switch (cat)
        {
            case StatCategory.Mining: return "ui_statcat_mining";
            case StatCategory.Movement: return "ui_statcat_movement";
            case StatCategory.Stamina: return "ui_statcat_stamina";
            case StatCategory.Drill: return "ui_statcat_drill";
            case StatCategory.Combat: return "ui_statcat_combat";
            case StatCategory.Economy: return "ui_statcat_economy";
            case StatCategory.Exploration: return "ui_statcat_exploration";
        }
        return null;
    }

    public static string CategoryFallback(StatCategory cat)
    {
        switch (cat)
        {
            case StatCategory.Mining: return "채광";
            case StatCategory.Movement: return "이동";
            case StatCategory.Stamina: return "스태미나";
            case StatCategory.Drill: return "드릴";
            case StatCategory.Combat: return "전투";
            case StatCategory.Economy: return "경제";
            case StatCategory.Exploration: return "탐색";
        }
        return "";
    }
}

/// <summary>
/// 스탯 카테고리 아이콘을 코드로 그려주는 정적 팩토리(크기별 캐시).
/// 스프라이트는 흰색 실루엣으로 만들고, 색은 사용처(<see cref="CodeStatPanel"/>)에서 Image.color로 입힌다.
/// CodeUI.Rounded/Triangle과 같은 방식(one-shot 텍스처 → 캐시)이라 런타임 부담이 거의 없다.
/// </summary>
public static class CodeStatIcons
{
    private const int S = 48; // 아이콘 텍스처 한 변
    private static readonly Dictionary<StatCategory, Sprite> _cache = new Dictionary<StatCategory, Sprite>();

    public static Sprite Get(StatCategory cat)
    {
        if (_cache.TryGetValue(cat, out var cached) && cached != null) return cached;
        var sprite = Build(cat);
        _cache[cat] = sprite;
        return sprite;
    }

    private static Sprite Build(StatCategory cat)
    {
        var cov = new float[S * S]; // 0~1 커버리지 누적

        switch (cat)
        {
            case StatCategory.Mining: DrawPickaxe(cov); break;
            case StatCategory.Movement: DrawChevrons(cov); break;
            case StatCategory.Stamina: DrawBolt(cov); break;
            case StatCategory.Drill: DrawBattery(cov); break;
            case StatCategory.Combat: DrawSword(cov); break;
            case StatCategory.Economy: DrawCoin(cov); break;
            case StatCategory.Exploration: DrawEye(cov); break;
        }

        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        for (int i = 0; i < px.Length; i++)
        {
            byte a = (byte)(Mathf.Clamp01(cov[i]) * 255f);
            px[i] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    // ===================================================
    // 카테고리별 심볼 (normalized 좌표: (0,0) 좌하단 ~ (1,1) 우상단)
    // ===================================================
    private static void DrawPickaxe(float[] c)
    {
        // 손잡이(좌하→우상) + 곡괭이 머리(완만한 아치)
        Seg(c, 0.32f, 0.16f, 0.58f, 0.60f, 0.052f);
        Seg(c, 0.20f, 0.54f, 0.52f, 0.72f, 0.055f);
        Seg(c, 0.52f, 0.72f, 0.84f, 0.54f, 0.055f);
    }

    private static void DrawChevrons(float[] c)
    {
        // 이중 꺾쇠 »  — 속도감
        Seg(c, 0.30f, 0.26f, 0.48f, 0.50f, 0.058f);
        Seg(c, 0.48f, 0.50f, 0.30f, 0.74f, 0.058f);
        Seg(c, 0.52f, 0.26f, 0.70f, 0.50f, 0.058f);
        Seg(c, 0.70f, 0.50f, 0.52f, 0.74f, 0.058f);
    }

    private static void DrawBolt(float[] c)
    {
        // 번개
        Seg(c, 0.58f, 0.84f, 0.40f, 0.52f, 0.070f);
        Seg(c, 0.38f, 0.52f, 0.54f, 0.48f, 0.052f);
        Seg(c, 0.56f, 0.48f, 0.40f, 0.16f, 0.070f);
    }

    private static void DrawBattery(float[] c)
    {
        // 배터리 몸통(테두리) + 단자 + 충전 막대
        BoxBorder(c, 0.16f, 0.36f, 0.72f, 0.64f, 0.030f);
        FillBox(c, 0.72f, 0.45f, 0.80f, 0.55f);
        FillBox(c, 0.26f, 0.43f, 0.34f, 0.57f);
        FillBox(c, 0.40f, 0.43f, 0.48f, 0.57f);
        FillBox(c, 0.54f, 0.43f, 0.62f, 0.57f);
    }

    private static void DrawSword(float[] c)
    {
        // 칼날 + 코등이 + 손잡이 + 자루끝
        Seg(c, 0.50f, 0.34f, 0.50f, 0.84f, 0.048f);
        Seg(c, 0.34f, 0.34f, 0.66f, 0.34f, 0.044f);
        Seg(c, 0.50f, 0.18f, 0.50f, 0.34f, 0.052f);
        Disc(c, 0.50f, 0.16f, 0.050f);
    }

    private static void DrawCoin(float[] c)
    {
        // 동전(고리) + 안쪽 표식
        Ring(c, 0.50f, 0.50f, 0.28f, 0.050f);
        Seg(c, 0.50f, 0.36f, 0.50f, 0.64f, 0.036f);
        Seg(c, 0.43f, 0.60f, 0.57f, 0.60f, 0.032f);
        Seg(c, 0.43f, 0.40f, 0.57f, 0.40f, 0.032f);
    }

    private static void DrawEye(float[] c)
    {
        // 눈/타깃(고리 + 동공)
        Ring(c, 0.50f, 0.50f, 0.28f, 0.045f);
        Disc(c, 0.50f, 0.50f, 0.10f);
    }

    // ===================================================
    // 그리기 프리미티브 (SDF 기반, 1px 안티앨리어싱)
    // ===================================================
    private static void Seg(float[] c, float ax, float ay, float bx, float by, float halfWidth)
    {
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float px = (x + 0.5f) / S, py = (y + 0.5f) / S;
                float d = SegDist(px, py, ax, ay, bx, by);
                Accumulate(c, x, y, (halfWidth - d) * S + 0.5f);
            }
    }

    private static void Disc(float[] c, float cx, float cy, float r)
    {
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float px = (x + 0.5f) / S, py = (y + 0.5f) / S;
                float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                Accumulate(c, x, y, (r - d) * S + 0.5f);
            }
    }

    private static void Ring(float[] c, float cx, float cy, float r, float halfWidth)
    {
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float px = (x + 0.5f) / S, py = (y + 0.5f) / S;
                float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                Accumulate(c, x, y, (halfWidth - Mathf.Abs(d - r)) * S + 0.5f);
            }
    }

    private static void FillBox(float[] c, float x0, float y0, float x1, float y1)
    {
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float px = (x + 0.5f) / S, py = (y + 0.5f) / S;
                Accumulate(c, x, y, (-BoxSD(px, py, x0, y0, x1, y1)) * S + 0.5f);
            }
    }

    private static void BoxBorder(float[] c, float x0, float y0, float x1, float y1, float halfWidth)
    {
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float px = (x + 0.5f) / S, py = (y + 0.5f) / S;
                Accumulate(c, x, y, (halfWidth - Mathf.Abs(BoxSD(px, py, x0, y0, x1, y1))) * S + 0.5f);
            }
    }

    private static void Accumulate(float[] c, int x, int y, float coverage)
    {
        float v = Mathf.Clamp01(coverage);
        int idx = y * S + x;
        if (v > c[idx]) c[idx] = v;
    }

    private static float SegDist(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float len2 = dx * dx + dy * dy;
        float t = len2 > 1e-6f ? Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / len2) : 0f;
        float qx = ax + dx * t, qy = ay + dy * t;
        float ex = px - qx, ey = py - qy;
        return Mathf.Sqrt(ex * ex + ey * ey);
    }

    private static float BoxSD(float px, float py, float x0, float y0, float x1, float y1)
    {
        float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
        float hx = (x1 - x0) * 0.5f, hy = (y1 - y0) * 0.5f;
        float qx = Mathf.Abs(px - cx) - hx, qy = Mathf.Abs(py - cy) - hy;
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        float outside = Mathf.Sqrt(ox * ox + oy * oy);
        float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
        return outside + inside;
    }
}
