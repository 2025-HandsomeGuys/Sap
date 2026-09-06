// @tags: stamina, mining, tuning, tool, pickaxe, shovel, debug-panel
using UnityEngine;

/// <summary>
/// 도구(toolIndex)별 채굴 스태미나 비용의 단일 소스.
/// <see cref="ToolCapabilities"/>가 "무엇을 팔 수 있나"라면, 여기는 "한 번 팔 때 얼마를 내나"다.
///
/// 종전에는 곡괭이·삽의 비용이 인스펙터 필드 2개(PlayerMining.pickaxeStaminaReduction,
/// Digger.shovelReductionPerRadius) + 전략 내부 const 2개 + TileData 값으로 흩어져 있어
/// 플레이 중 도구별로 비교 튜닝할 방법이 없었다. 전부 여기를 거치게 모았다.
///
/// static인 이유: F8 진단 패널(StatDiagnosticPanel)이 런타임에 직접 쓰고,
/// 전략·Digger는 매 스윙 여기서 읽는다 → 수정 즉시 반영(플레이 재시작 불필요).
///
/// 기본값은 인스펙터가 Seed*로 "한 번만" 심는다. 씬을 넘나들 때마다 다시 심으면
/// 지하로 내려가는 순간 튜닝값이 날아가기 때문이다(패널은 DontDestroyOnLoad라 살아 있는데).
/// </summary>
public static class MiningStaminaTuning
{
    public const int Shovel  = 1;
    public const int Pickaxe = 2;

    // === 현재 스태미나 소모 배율 (1 = 원래 비용) ===
    // 돌/지형을 나눈 이유: 삽은 지형 파기가 원래 완전 공짜였고(아래 주석), 곡괭이는 아니다.
    // 하나의 배율로 묶으면 삽 지형만 0으로 두는 순간 툴스왑 유물의 돌 비용까지 같이 0이 된다.
    public static float PickaxeRockCostMultiplier    = 1f;  // HardStone 타격 비용에 곱
    public static float PickaxeTerrainCostMultiplier = 1f;  // 깊이별 저항값에 곱
    public static float ShovelRockCostMultiplier     = 1f;
    // ⚠ 삽 지형의 '현재 스태미나' 소모만은 설계 문서에도 없던 항목이라 0(공짜)에서 시작한다.
    //    최대치 감소(아래 ShovelTerrainReductionPerCharge)와는 별개다 — 그쪽은 원래 설계값 0.5로 복구했다.
    //    삽은 이 배율도 '차징 비율'에 곱해진다(반경 아님).
    public static float ShovelTerrainCostMultiplier  = 0f;

    // === 돌 타격 1회당 MaxStamina 감소량 (고정) ===
    public static float PickaxeRockMaxReduction = 2f;
    public static float ShovelRockMaxReduction  = 2f;

    // === 지형 파기 반경 1단위당 MaxStamina 감소량 ===
    public static float PickaxeTerrainReductionPerRadius = 0.5f;

    // ⚠ 삽만 기준이 '반경'이 아니라 '차징 비율'이다(0~1). 풀차징 1회 = 이 값만큼 감소.
    //    반경 기준이면 사거리·ToolRange·크기배율을 올릴 때마다 비용이 따라 올라
    //    "삽을 키웠더니 스태미나가 더 빨리 준다"가 된다. 비용은 얼마나 힘줘 팠는지에만 걸린다.
    //    0.5 = stamina-max-reduction-plan.md의 원래 설계값(구 Digger.shovelReductionPerRadius).
    public static float ShovelTerrainReductionPerCharge  = 0.5f;

    // === 패널 읽기 전용 계측 ===
    // 가장 최근에 실제로 빠져나간 현재 스태미나. 배율만 봐서는 체감이 안 잡히므로
    // "지금 한 대에 얼마 나갔는지"를 같이 띄운다.
    public static float LastPickaxeCost;
    public static float LastShovelCost;

    // === 삽질 횟수 계측 (지하 한정) ===
    // 지상에서 휘두른 스윙은 세지 않는다 — 밸런싱에서 알고 싶은 건 "한 번 내려가서 몇 번 팠나"다.
    // 잠수 카운터는 지상 복귀(ExploreExitController.PrepareSettlement)에서 0으로 돌아가고,
    // 세션 누적은 플레이 종료까지 계속 쌓인다.
    /// <summary>이번 지하 잠수 동안 실제로 나간 삽 스윙 수(CanDig 통과분).</summary>
    public static int ShovelDigsThisTrip;

    /// <summary>그중 지형이나 돌이 실제로 깎인 스윙 수. 둘의 차이 = 헛스윙.</summary>
    public static int ShovelHitsThisTrip;

    /// <summary>플레이 세션 전체 누적 삽 스윙 수(지하 한정).</summary>
    public static int ShovelDigsTotal;

    /// <summary>켜면 스윙마다 차징비율·반경·실제 지불액을 콘솔에 찍는다. F8 패널에서 토글.</summary>
    public static bool LogDigs;

    /// <summary>
    /// 삽이 스윙으로 인정되는 최소 차징 비율. 이 미만에서 버튼을 떼면 스윙 자체가 취소된다
    /// (모션·파기·비용 전부 없음).
    ///
    /// 종전 0.05는 100ms 탭에도 풀 스윙 모션이 나가면서 반경 0.05(5픽셀)만 파고
    /// MaxStamina는 0.025 깎이는 상태를 만들었다 — 판 것도 없는데 값은 치르는 구간.
    /// `SapStrategy.GetDigParameters`(파기 판정)와 `ProcessCharging`(스윙 발사)이
    /// 같은 값을 봐야 "모션은 나갔는데 안 파임"이 다시 안 생긴다.
    /// </summary>
    public static float ShovelMinChargeRatio = 0.25f;

    /// <summary>
    /// 삽 파기 반경 배율. 최종 반경 = 차징비율 × MiningRange × ToolRange × 도구효율 × 이 값.
    ///
    /// MiningRange(F8 '채굴 사거리')는 곡괭이·Digger가 같이 쓰므로 삽만 키울 수 없다.
    /// (드릴은 2026-09-01부터 StatType.DrillRadius를 따로 쓴다 — MiningRange를 안 본다.)
    /// 그래서 삽 전용 배율을 여기 둔다.
    ///
    /// ⚠ 반경은 비용에도 그대로 곱해진다 — 지형 소모(저항×반경)와 최대치 감소(반경당 계수×반경)가
    ///    같이 커진다. 크기를 2배로 하면 판 양은 면적이라 ~4배인데 값은 2배만 낸다.
    /// </summary>
    public static float ShovelRadiusMultiplier = 1f;

    /// <summary>
    /// 삽이 풀차징(비율 1.0)에 도달하기까지 걸리는 시간(초).
    /// 실제 체감은 여기에 MiningSpeed 스탯이 나눠 들어간다 — 스탯 2배면 절반 시간에 찬다.
    ///
    /// 종전엔 PlayerMining 인스펙터 값이 SapStrategy 생성자에 박혀 Start 이후로는 못 바꿨다.
    /// "차징이 너무 길다/짧다"는 최소 차징·파기 크기와 같이 봐야 하는 값이라 여기로 옮겼다.
    /// 0으로 두면 나눗셈이 깨지므로 항상 <see cref="MinChargeTime"/> 이상으로 클램프된다.
    /// </summary>
    public static float ShovelMaxChargeTime = DefaultShovelMaxChargeTime;

    /// <summary>차징 시간 하한. 이보다 짧으면 비율이 발산하거나 0 나눗셈이 난다.</summary>
    public const float MinChargeTime = 0.05f;

    private const float DefaultPickaxeRockMaxReduction = 2f;
    private const float DefaultShovelMaxChargeTime = 1f;

    private static bool _seededPickaxeRock;
    private static bool _seededChargeTime;
    private static float _seedChargeTimeValue = DefaultShovelMaxChargeTime;
    private static bool _chargeTimeOverridden;

    // Seed로 들어온 인스펙터 값. '기본값 복원'의 기준점이라 따로 들고 있는다 —
    // 곡괭이 돌 최대치↓만은 코드 상수가 아니라 PlayerMining 인스펙터가 기본값이기 때문.
    private static float _seedPickaxeRockValue = DefaultPickaxeRockMaxReduction;

    // F8 패널이 저장해 둔 값을 이미 넣었는지. true면 Seed가 그 위를 덮지 않는다.
    private static bool _pickaxeRockOverridden;

    // 도메인 리로드 비활성(Enter Play Mode Options) 환경에서도 정적 상태를 초기화한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _seededPickaxeRock = false;
        _seedPickaxeRockValue = DefaultPickaxeRockMaxReduction;
        _seededChargeTime = false;
        _seedChargeTimeValue = DefaultShovelMaxChargeTime;

        ResetTuningToDefaults();

        LastPickaxeCost = 0f;
        LastShovelCost  = 0f;
        ShovelDigsThisTrip = 0;
        ShovelHitsThisTrip = 0;
        ShovelDigsTotal    = 0;
        LogDigs = false;
    }

    /// <summary>
    /// 튜닝 필드만 기본값으로 되돌린다(계측·카운터는 그대로).
    /// 도메인 리로드 초기화와 F8 패널의 '저장값 삭제'가 같은 기준을 쓰게 하려고 분리했다.
    /// </summary>
    public static void ResetTuningToDefaults()
    {
        PickaxeRockCostMultiplier    = 1f;
        PickaxeTerrainCostMultiplier = 1f;
        ShovelRockCostMultiplier     = 1f;
        ShovelTerrainCostMultiplier  = 0f;
        PickaxeRockMaxReduction = _seedPickaxeRockValue;
        ShovelRockMaxReduction  = 2f;
        PickaxeTerrainReductionPerRadius = 0.5f;
        ShovelTerrainReductionPerCharge  = 0.5f;
        ShovelMinChargeRatio = 0.25f;
        ShovelRadiusMultiplier = 1f;
        ShovelMaxChargeTime = _seedChargeTimeValue;
        _pickaxeRockOverridden = false;
        _chargeTimeOverridden = false;
    }

    /// <summary>PlayerMining 인스펙터 값을 기본값으로 심는다(세션 최초 1회).</summary>
    public static void SeedPickaxeRockMaxReduction(float value)
    {
        if (_seededPickaxeRock) return;
        _seededPickaxeRock = true;
        _seedPickaxeRockValue = value;

        // 패널 저장값이 먼저 들어와 있으면 그쪽이 우선이다.
        // (Seed는 씬의 PlayerMining.Awake, 저장값 로드는 그보다 앞선 패널 Awake에서 일어난다)
        if (_pickaxeRockOverridden) return;
        PickaxeRockMaxReduction = value;
    }

    /// <summary>F8 패널이 저장값을 넣었음을 알린다 — 이후 Seed가 덮어쓰지 않는다.</summary>
    public static void MarkPickaxeRockOverridden()
    {
        _pickaxeRockOverridden = true;
    }

    /// <summary>PlayerMining 인스펙터의 maxChargeTime을 기본값으로 심는다(세션 최초 1회).</summary>
    public static void SeedShovelMaxChargeTime(float value)
    {
        if (_seededChargeTime) return;
        _seededChargeTime = true;
        _seedChargeTimeValue = Mathf.Max(MinChargeTime, value);

        if (_chargeTimeOverridden) return;
        ShovelMaxChargeTime = _seedChargeTimeValue;
    }

    /// <summary>F8 패널이 저장값을 넣었음을 알린다 — 이후 Seed가 덮어쓰지 않는다.</summary>
    public static void MarkChargeTimeOverridden()
    {
        _chargeTimeOverridden = true;
    }

    /// <summary>차징 시간을 안전 범위로 잠근 값. 나눗셈·비교는 전부 이걸 쓴다.</summary>
    public static float SafeShovelMaxChargeTime => Mathf.Max(MinChargeTime, ShovelMaxChargeTime);

    /// <summary>맨손(0)·드릴 등 대상 외 도구는 1배(원래 비용)로 통과시킨다.</summary>
    public static float GetRockCostMultiplier(int toolIndex) => toolIndex switch
    {
        Pickaxe => PickaxeRockCostMultiplier,
        Shovel  => ShovelRockCostMultiplier,
        _       => 1f,
    };

    public static float GetTerrainCostMultiplier(int toolIndex) => toolIndex switch
    {
        Pickaxe => PickaxeTerrainCostMultiplier,
        Shovel  => ShovelTerrainCostMultiplier,
        _       => 1f,
    };

    public static float GetRockMaxReduction(int toolIndex) => toolIndex switch
    {
        Pickaxe => PickaxeRockMaxReduction,
        Shovel  => ShovelRockMaxReduction,
        _       => 0f,
    };

    // 지형 최대치 감소는 도구마다 '곱하는 기준'이 다르다 — 곡괭이=반경당, 삽=차징당.
    // 그래서 공용 getter를 두지 않는다. 하나로 묶으면 호출부가 단위를 착각한다.
    // 각 전략이 자기 필드(PickaxeTerrainReductionPerRadius / ShovelTerrainReductionPerCharge)를 직접 읽는다.

    /// <summary>실제 지불액을 패널 계측용으로 남긴다. 게임 로직에는 영향 없음.</summary>
    public static void ReportCost(int toolIndex, float amount)
    {
        if (toolIndex == Pickaxe) LastPickaxeCost = amount;
        else if (toolIndex == Shovel) LastShovelCost = amount;
    }

    /// <summary>
    /// 삽 스윙 1회를 기록한다. 지상 씬에서는 세지 않는다.
    /// 계측 전용이라 게임 로직에는 영향이 없다.
    /// </summary>
    /// <param name="hitSomething">지형이나 돌이 실제로 깎였는지(헛스윙 구분용).</param>
    public static void ReportShovelDig(bool hitSomething)
    {
        if (SurfaceSceneRegistry.IsActiveSceneSurface()) return;

        ShovelDigsThisTrip++;
        ShovelDigsTotal++;
        if (hitSomething) ShovelHitsThisTrip++;
    }

    /// <summary>지상으로 복귀할 때 잠수 카운터만 되돌린다(누적은 유지).</summary>
    public static void ResetShovelTripCount()
    {
        ShovelDigsThisTrip = 0;
        ShovelHitsThisTrip = 0;
    }
}
