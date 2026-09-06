// @tags: sound, sfx, keys, constants, audio
/// <summary>
/// 효과음 키 상수. SoundDataSO에 등록하는 soundName과 1:1로 대응한다.
///
/// 문자열 리터럴을 흩뿌리면 오타가 무음으로 조용히 넘어가서 디버깅이 어렵다.
/// 재생 호출은 반드시 이 상수를 통해서만 한다.
///
/// 명명 규칙: &lt;도메인&gt;_&lt;동작&gt; snake_case. 기존 market_ui_* / coin_* 와 맞춘다.
/// 클립이 없으면 HasSFX 가드에서 조용히 무음 처리된다 — 키를 먼저 정의하고
/// 음원은 나중에 채워도 된다.
///
/// 음원 추가: Assets/Audio/SFX/ 에 키와 같은 파일명으로 넣고
/// Tools/Sound/Rescan SFX Folder 실행.
/// </summary>
public static class SfxKeys
{
    // ── 플레이어 ──
    // 점프·착지도 발소리처럼 표면에 따라 갈린다. 선택은 FootstepSurfaceSelector가 맡는다.
    public const string UpgroundGrass   = "upground_grass";
    public const string JumpGrass       = "jump_grass";
    public const string JumpDirt        = "jump_dirt";
    public const string LandGrass       = "land_grass";
    public const string LandDirt        = "land_dirt";
    public const string PlayerHeartbeat = "player_heartbeat"; // 루프
    public const string PlayerHurt      = "player_hurt";      // 위험물 피격
    // 발소리는 왼발/오른발을 번갈아 낸다. B가 등록돼 있지 않으면 A만 반복한다(폴백).
    public const string StepHomeA       = "step_home_a";
    public const string StepHomeB       = "step_home_b";
    public const string StepGrass       = "step_grass";
    public const string StepGrassB      = "step_grass_b";
    public const string StepDirt        = "step_dirt";       // 지하 기본
    public const string StepDirtB       = "step_dirt_b";
    public const string StepHardStone   = "step_hardstone";  // 지하 2층(TileType.HardStone)
    public const string StepHardStoneB  = "step_hardstone_b";

    // ── 채굴 ──
    public const string DigSwing   = "dig_swing";  // 곡괭이 휘두르기
    public const string DigSap     = "dig_sap";    // 삽 파기
    public const string DigHit     = "dig_hit";
    public const string DigHit2 = "dig_hit2";
    public const string DigHit3 = "dig_hit3";
    public const string DigBlocked = "dig_blocked";
    public const string RockBreak  = "rock_break";
    public const string IceBreak   = "ice_break";
    public const string Explosion  = "explosion";

    // ── 도구 · 유물 ──
    public const string DrillOn           = "drill_on";
    public const string DrillMotor        = "drill_motor";    // 루프
    public const string PowerDown         = "power_down";
    public const string JetpackThrust     = "jetpack_thrust"; // 루프
    public const string RelicLightning    = "relic_lightning";
    public const string RelicDetectPing   = "relic_detect_ping"; // DetectionPingSfx가 이미 조회
    public const string RelicPortalReturn = "relic_portal_return";
    public const string RelicInvincible   = "relic_invincible";
    public const string RelicGravity      = "relic_gravity";   // 반중력 지속 루프
    public const string RelicActivate     = "relic_activate";
    public const string DroneMove         = "drone_move";     // 드론(굴착·채굴) 비행 중 간헐적 모터음

    // ── 이동 · 전환 ──
    public const string ElevatorMove = "elevator_move";
    public const string PortalEnter  = "portal_enter";

    // ── 경제 · UI ──
    public const string UiButton      = "ui_button";  // 공용 버튼음 — 전용 키가 없을 때의 폴백
    public const string UiClick       = "ui_click";   // 코드 생성 오버레이 공통 클릭
    public const string UiBack        = "ui_back";    // 닫기·취소(ESC)
    public const string UiTerminalOn  = "ui_terminal_on";
    public const string MineralPickup = "mineral_pickup"; // 월드에서 광물 줍기
    public const string ShopSell      = "shop_sell";
    public const string ShopBuy       = "shop_buy";
    public const string UpgradeUnlock = "upgrade_unlock"; // 업그레이드 트리 노드 해금
    public const string UpgradeTier   = "upgrade_tier";   // 상위 계층(Tier) 해금 — 노드보다 큰 사건
    public const string EquipEnhance  = "equip_enhance";  // 장비 강화 (+N) — 별개 시스템
    public const string CauldronBrew  = "cauldron_brew";
    public const string CoinTick      = "coin_tick"; // CoinSfx.Tick이 이미 조회

    // ── 하루 사이클 · 결과 ──
    public const string SleepSnore       = "sleep_snore";
    public const string DaySummaryProfit = "daysummary_profit";
    public const string DaySummaryLoss   = "daysummary_loss";
    public const string QuestComplete    = "quest_complete";
    public const string MissionSuccess   = "mission_success";
    public const string GameOver         = "game_over";

    // ── 앰비언스 (루프) ──
    public const string AmbSurfaceDayCicada    = "amb_surface_day_cicada";
    public const string AmbSurfaceDayBirds     = "amb_surface_day_birds";
    public const string AmbSurfaceNightInsects = "amb_surface_night_insects";
    public const string AmbLayerWind           = "amb_layer_wind";
    public const string AmbCaveDrip            = "amb_cave_drip";

    /// <summary>
    /// 에디터 툴(SfxFolderImporter)이 "아직 음원이 없는 키" 목록을 뽑을 때 쓴다.
    /// 새 키를 추가하면 여기에도 넣어야 리포트에 잡힌다.
    /// </summary>
    public static readonly string[] All =
    {
        JumpGrass, JumpDirt, LandGrass, LandDirt, PlayerHeartbeat, PlayerHurt,
        StepGrass, StepGrassB, StepDirt, StepDirtB, StepHardStone, StepHardStoneB,
        DigSwing, DigSap, DigHit, DigBlocked, RockBreak, IceBreak, Explosion,
        DrillOn, DrillMotor, PowerDown, JetpackThrust,
        RelicLightning, RelicDetectPing, RelicPortalReturn, RelicInvincible, RelicGravity,
        RelicActivate, DroneMove,
        ElevatorMove, PortalEnter,
        UiButton, UiClick, UiBack, UiTerminalOn, MineralPickup, ShopSell, ShopBuy,
        UpgradeUnlock, UpgradeTier, EquipEnhance,
        CauldronBrew, CoinTick,
        SleepSnore, DaySummaryProfit, DaySummaryLoss,
        QuestComplete, MissionSuccess, GameOver,
        AmbSurfaceDayCicada, AmbSurfaceDayBirds, AmbSurfaceNightInsects,
        AmbLayerWind, AmbCaveDrip,
    };
}
