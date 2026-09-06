// @tags: settlement, manager, singleton, exploration, depth, reward
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 하루 동안의 탐험 데이터를 추적하고 관리하는 싱글톤 매니저.
/// </summary>
public class SettlementManager : MonoBehaviour
{
    public static SettlementManager Instance { get; private set; }

    [Header("Tracking Data")]
    public float MaxDepth { get; private set; }
    public float TimeUnderground { get; private set; }
    public Dictionary<MineralID, int> MinedMinerals { get; private set; } = new Dictionary<MineralID, int>();

    private bool _isTracking = false;
    private bool _isAppQuitting = false; // 앱 종료 여부 플래그 추가
    private Transform _playerTransform;
    private float _initialPlayerY;

    // ─── 텔레메트리 ───
    // 100m 단위 최초 도달 기록용. 잠수마다 리셋된다.
    private int _lastDepthMilestone;
    private string _lastReportedRegion = string.Empty;
    // 지역 최초 진입은 세션 내에서만 판정한다. 재시작 시 다시 찍혀도 분석에서 anon_id별 최초 1건만 취하면 된다.
    private static readonly HashSet<string> _visitedRegions = new HashSet<string>();

    // 층별 체류 시간·파기 픽셀. 잠수마다 리셋된다(balance-csv-design.md §6·§7).
    private readonly RegionDwellTracker _dwell = new RegionDwellTracker();
    private int _dugPixels;

    // 스윙(파기 시도) 횟수. 도구 인덱스별로 센다 — 0 맨손 / 1 삽 / 2 곡괭이 / 3 드릴.
    //
    // 왜 픽셀만으로는 부족한가: pixels_dug가 있어도 "한 번 휘두르면 몇 픽셀이 깎이나"와
    // "초당 몇 번 휘두르나"를 가를 수 없다. 밸런스 모델에서 그 둘이 각각
    // 채굴 범위 업그레이드와 채굴 속도 업그레이드에 대응하므로, 스윙 수가 없으면
    // 두 업그레이드의 가치를 구분하지 못한다.
    //
    // 헛스윙(아무것도 안 깎인 스윙)을 따로 세는 것은 조준 난이도의 대리 지표다.
    private readonly int[] _swingsByTool = new int[4];
    private int _swingsHit;

    // 벽타기에 쓴 시간과 스태미나. 잠수마다 리셋된다.
    //
    // 왜 필요한가: 벽타기 속도·스태미나 소모율 업그레이드는 **수입을 안 올린다**.
    // 런 모델의 income()이 그 축을 수식에 안 쓰기 때문에 한계효용이 언제나 0이고,
    // 그래서 "함정인지 편의성인지"를 모델로는 영영 가릴 수 없다
    // (Assets/Docs/economy/upgrade-balance-charter.md §7).
    //
    // 대신 체감 지표로 잰다 — 이 둘이 그 잣대다:
    //   climb_seconds                  -> 벽타기 속도(빨라지면 매달린 시간이 준다)
    //   climb_stamina / climb_seconds  -> 스태미나 소모율(초당 드레인)
    //
    // 게임 시간 기준이다. UseStamina가 Time.fixedDeltaTime(배속 반영)으로 뽑아가므로
    // 같은 축이어야 비율이 의미를 갖는다. 실시간이 필요하면 time_scale_avg로 환산한다.
    private float _climbSeconds;
    private float _climbStamina;

    // 실제로 흐른 시간(배속 무관). TimeUnderground는 Time.deltaTime 누적이라 **게임 시간**이고,
    // 디버그 시간 배속(DebugTimeScaleDriver, `[`/`]`)을 쓰면 실제보다 빨리 흐른다.
    //
    // 배속이 걸린 판을 통째로 버리지 않고 보정해서 쓰기 위해 둘 다 남긴다.
    // 왜 중요한가: 파기량·광물 수·깊이는 배속과 무관하지만, **사람의 입력 속도는
    // 실시간에 묶여 있어** 배속에서는 게임시간당 스윙 수가 줄어든다. 그 사실을
    // 모르면 "한 번 휘두르는 데 3.4초"처럼 시간 지표를 통째로 잘못 읽는다.
    private float _realSeconds;

    // 잠수 시작 시점의 최대 스태미나. 이 게임에서 한 잠수의 소모를 나타내는 값은
    // currentStamina가 아니라 **MaxStamina**다 — 부상·화상·동상·방사능·파기 소모가
    // 전부 StaminaManager의 Flat 감소로 MaxStamina를 깎고, currentStamina는 거기에
    // 클램프되어 따라 내려온다(StaminaManager.GetModifiers / PlayerStat.ClampCurrentStamina).
    //
    // 그래서 "종료 시 current / 종료 시 max"는 거의 항상 1이 되어 아무 정보가 없다.
    // 의미 있는 지표는 **시작 시 max 대비 종료 시 max가 얼마나 남았나**이고,
    // 그걸 재려면 시작값을 여기 붙들어 둬야 한다.
    private float _maxStaminaAtStart;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null); 
            DontDestroyOnLoad(gameObject);
            Debug.Log("[SettlementManager] Singleton Instance Created and DontDestroyOnLoad set.");
        }
        else if (Instance != this)
        {
            Debug.Log("[SettlementManager] Duplicate instance found and destroyed.");
            Destroy(this); // gameObject가 아닌 컴포넌트만 파괴 (같은 오브젝트의 다른 컴포넌트 보존)
        }
    }

    private void OnApplicationQuit()
    {
        _isAppQuitting = true;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            // 앱 종료 중이 아닌데 파괴되는 경우에만 경고를 띄웁니다.
            if (!_isAppQuitting)
            {
                Debug.LogWarning("[SettlementManager] Original Singleton Instance is being destroyed during gameplay! This should not happen.");
            }
            Instance = null;
        }
    }

    private void Update()
    {
        if (!_isTracking) return;

        TimeUnderground += Time.deltaTime;          // 게임 시간 (배속에 비례)
        _realSeconds += Time.unscaledDeltaTime;     // 실제 시간 (배속 무관)
        _dwell.Tick(Time.deltaTime);

        if (_playerTransform != null)
        {
            // 지하로 내려갈수록 y값이 낮아진다고 가정 (0이 지표면)
            // 깊이는 (시작점 - 현재점)으로 계산
            float currentDepth = Mathf.Max(0, _initialPlayerY - _playerTransform.position.y);
            if (currentDepth > MaxDepth)
            {
                MaxDepth = currentDepth;
            }

            UpdateTelemetryContext(currentDepth);
        }
    }

    /// <summary>매 프레임 깊이·지역을 텔레메트리 컨텍스트에 반영하고, 경계를 넘으면 이벤트를 낸다.</summary>
    private void UpdateTelemetryContext(float currentDepth)
    {
        if (Telemetry.Context == null) return;

        Telemetry.Context.Depth = currentDepth;

        // 지역 판정 — GetTileTypeAtDepth는 **청크 Y** 기준이다(월드 Y가 아니다).
        //
        // ⚠ 2026-08-31까지 월드 Y를 그대로 넘기고 있었다. tileData.json의 startDepth가
        //   -20/-40/-60(청크 인덱스, 광물 밴드 20~39·40~59와 같은 단위)이라
        //   **10배 얕은 곳에서 층이 바뀐 것으로 기록됐다** — 월드 깊이 20에서 Ice,
        //   40에서 MagmaRock. 실제로는 각각 청크 20(월드 200)·청크 40(월드 400)이다.
        //
        //   증거 둘: (1) 지형 경로는 GetStaminaReductionAtWorldY가 worldY를 chunkY로
        //   바꿔 조회한다. (2) 실측 113잠수의 광물이 전부 Dirt 광물이었는데도
        //   region은 Ice/MagmaRock/MeteoriteRock을 찍고 있었다.
        //
        //   영향: 모든 이벤트의 공통 필드 region, region_seconds, 그리고 설계가
        //   1순위 지표로 꼽은 region_first_enter가 전부 틀린 값이었다.
        //   배경: Assets/Docs/economy/upgrade-balance-charter.md §7
        string region = _lastReportedRegion;
        if (TileDataManager.Instance != null)
        {
            int chunkY = Mathf.FloorToInt(_playerTransform.position.y / ChunkCoords.WorldSize);
            region = TileDataManager.Instance.GetTileTypeAtDepth(chunkY).ToString();
        }

        if (region != _lastReportedRegion)
        {
            _lastReportedRegion = region;
            Telemetry.Context.Region = region;
            _dwell.SwitchTo(region);

            // 지역 최초 진입 — 도달 시점(playtime)이 콘텐츠 분량 검증의 핵심 지표(설계 §3.1)
            if (!string.IsNullOrEmpty(region) && _visitedRegions.Add(region))
            {
                Telemetry.Log(TelemetryEvents.RegionFirstEnter, TelemetryPayload.New()
                    .Add("region", region)
                    .Add("depth", currentDepth));
            }
        }

        // 100m 단위 최초 도달
        int milestone = Mathf.FloorToInt(currentDepth / 100f) * 100;
        if (milestone > _lastDepthMilestone)
        {
            _lastDepthMilestone = milestone;
            Telemetry.Log(TelemetryEvents.DepthMilestone, TelemetryPayload.New()
                .Add("milestone", milestone)
                .Add("region", region));
        }
    }

    /// <summary>잠수 종료 이벤트. result는 "return"(정상 귀환)·"escape"(긴급 탈출)·"death"(사망).</summary>
    private void LogDiveEnd(string result)
    {
        if (!Telemetry.IsEnabled) return;

        try
        {
            int mineralCount = 0;
            foreach (var kv in MinedMinerals) mineralCount += kv.Value;

            var stat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
            var enc = FindFirstObjectByType<EncumbranceController>(FindObjectsInactive.Include);

            // 종료 시점의 최대 스태미나. 시작값(_maxStaminaAtStart) 대비 얼마나 남았는지가
            // 그 잠수가 얼마나 빡셌는지를 나타내는 핵심 지표다.
            // (종료 시 current/max는 항상 1에 붙으므로 지표로 쓰지 않는다 — _maxStaminaAtStart 주석 참고)
            float maxStaminaEnd = stat != null ? stat.MaxStamina : 0f;
            float maxStaminaPct = _maxStaminaAtStart > 0.01f ? maxStaminaEnd / _maxStaminaAtStart : 0f;

            var stamina = FindFirstObjectByType<StaminaManager>(FindObjectsInactive.Include);

            float threshold = enc != null ? enc.EncumbranceThreshold : 0f;
            float weightRatio = (enc != null && threshold > 0f) ? enc.TotalWeight / threshold : 0f;

            Telemetry.Log(TelemetryEvents.DiveEnd, TelemetryPayload.New()
                .Add("result", result)
                .Add("max_depth", MaxDepth)
                .Add("seconds", TimeUnderground)
                .Add("mineral_kinds", MinedMinerals.Count)
                .Add("mineral_count", mineralCount)
                .Add("stamina_left", stat != null ? stat.CurrentStamina : 0f)
                // 최대 스태미나 잔량 — 잠수 난이도의 주 지표
                .Add("max_stamina_start", _maxStaminaAtStart)
                .Add("max_stamina_end", maxStaminaEnd)
                .Add("max_stamina_pct", maxStaminaPct)
                // 깎인 양의 출처. "왜 최대치가 줄었나"를 원인별로 가른다
                .AddRaw("stamina_loss", BuildStaminaLossObject(stamina))
                .Add("weight_ratio", weightRatio)
                .Add("pixels_dug", _dugPixels)
                .Add("swings", TotalSwings())
                .Add("swings_hit", _swingsHit)
                // 편의성 축 판정용(P13) — 벽타기 속도·스태미나 소모율은 수입을
                // 안 움직여서 런 모델로는 못 잰다. charter §7 참고.
                .Add("climb_seconds", _climbSeconds)
                .Add("climb_stamina", _climbStamina)
                .AddRaw("swings_by_tool", BuildSwingsByToolObject())
                // seconds(게임 시간) / real_seconds(실제 시간) = 그 잠수의 평균 배속.
                // 1.0이 아니면 시간 기반 지표를 그대로 믿으면 안 된다.
                .Add("real_seconds", _realSeconds)
                .Add("time_scale_avg", _realSeconds > 0.01f ? TimeUnderground / _realSeconds : 1f)
                .AddRaw("region_seconds", _dwell.ToPayloadObject())
                .AddRaw("minerals", BuildMineralsObject()));
        }
        catch (Exception e)
        {
            // 텔레메트리 예외가 StopTracking/AbortTracking(게임플레이 경로)으로 새어나가면 안 된다.
            Debug.LogWarning($"[SettlementManager] dive_end 기록 실패: {e.Message}");
        }

        // 지상 복귀 = 알트탭해서 일지를 적기 좋은 시점. 뷰어가 바로 받아볼 수 있게 흘려보낸다
        // (기본 flush 주기는 60초라 그 사이 방금 한 판이 화면에 없다).
        Telemetry.Flush();
    }

    /// <summary>
    /// MaxStamina를 깎은 원인별 감소량을 JSON 오브젝트로.
    /// 합이 max_stamina_start - max_stamina_end와 맞지 않으면 업그레이드 등
    /// 곱연산 배율이 잠수 중에 바뀌었다는 뜻이다(StaminaManager.TotalReduction은 기준값 스케일).
    /// </summary>
    private static string BuildStaminaLossObject(StaminaManager stamina)
    {
        if (stamina == null) return "{}";

        var c = System.Globalization.CultureInfo.InvariantCulture;
        return "{\"injury\":" + stamina.Injury.ToString("0.##", c)
             + ",\"burn\":" + stamina.Burn.ToString("0.##", c)
             + ",\"frostbite\":" + stamina.Frostbite.ToString("0.##", c)
             + ",\"radiation\":" + stamina.Radiation.ToString("0.##", c)
             + ",\"digging\":" + stamina.DiggingReduction.ToString("0.##", c)
             + "}";
    }

    /// <summary>캔 광물 구성을 JSON 오브젝트로. 예: {"Iron":12,"Gold":3}</summary>
    private string BuildMineralsObject()
    {
        var sb = new System.Text.StringBuilder(64);
        sb.Append('{');
        bool first = true;
        foreach (var kv in MinedMinerals)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(TelemetryPayload.EscapeJson(kv.Key.ToString())).Append("\":")
              .Append(kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append('}');
        return sb.ToString();
    }

    public bool IsDataPending { get; private set; } = false;

    /// <summary>
    /// 탐험 시작 시 호출하여 데이터를 초기화합니다.
    /// </summary>
    public void StartTracking(Transform player, float initialY = 0f)
    {
        _playerTransform = player;
        _initialPlayerY = initialY;
        
        MaxDepth = 0f;
        TimeUnderground = 0f;
        MinedMinerals.Clear();
        
        _isTracking = true;
        IsDataPending = false;
        Debug.Log("[SettlementManager] 탐험 추적 시작 - IsDataPending: False");

        _lastDepthMilestone = 0;
        _lastReportedRegion = string.Empty;
        _dwell.Reset();
        _dugPixels = 0;
        System.Array.Clear(_swingsByTool, 0, _swingsByTool.Length);
        _swingsHit = 0;
        _climbSeconds = 0f;
        _climbStamina = 0f;
        _realSeconds = 0f;

        var stat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
        _maxStaminaAtStart = stat != null ? stat.MaxStamina : 0f;

        Telemetry.Log(TelemetryEvents.DiveStart, TelemetryPayload.New()
            .Add("time_of_day", DayCycleManager.Instance != null
                ? DayCycleManager.Instance.CurrentTime.ToString() : "Unknown")
            .Add("stamina", stat != null ? stat.CurrentStamina : 0f)
            .Add("max_stamina", _maxStaminaAtStart)
            .Add("gold", stat != null ? stat.Gold : 0));
    }

    /// <summary>
    /// 탐험 종료 시 호출하여 추적을 중지합니다.
    /// </summary>
    public void StopTracking()
    {
        if (_isTracking)
        {
            _isTracking = false;
            IsDataPending = true;
            Debug.Log($"[SettlementManager] 탐험 추적 중지. 데이터 보존됨 (IsDataPending: True). 최대 깊이: {MaxDepth:F1}m");

            LogDiveEnd("return");
        }
        else
        {
            Debug.LogWarning("[SettlementManager] StopTracking 호출됨 - 이미 추적 중이 아니었습니다.");
        }
    }

    /// <summary>
    /// 정산 처리가 완료되었음을 알리고 데이터를 초기화합니다.
    /// </summary>
    public void ClearPendingData()
    {
        IsDataPending = false;
        // 필요 시 여기서 추가 초기화 수행
    }

    /// <summary>
    /// 긴급 탈출·사망처럼 '정상 종료가 아닌' 이탈 시 호출.
    /// 추적을 즉시 중단하고 누적값(깊이·시간·캔 광물)을 리셋하며, 정산 데이터도 남기지 않습니다(IsDataPending=false).
    /// StopTracking과 달리 IsDataPending을 세우지 않으므로 지상에서 정산 팝업이 뜨지 않습니다
    /// (긴급 탈출은 별도의 EmergencyEscapeOverlayUI가 결과를 보여줍니다).
    /// </summary>
    /// <param name="result">
    /// dive_end에 실을 결과. "escape"(긴급 탈출) 또는 "death"(사망).
    /// 사망도 반드시 여기를 거쳐야 한다 — 안 거치면 그 잠수가 로그에 아예 안 남아
    /// 분석이 '살아 돌아온 잠수'만 보게 된다(생존 편향).
    /// 배경: Assets/Docs/economy/upgrade-balance-charter.md §6-B
    /// </param>
    public void AbortTracking(string result = "escape")
    {
        // 누적값을 리셋하기 전에 기록해야 한다 — 긴급 탈출·사망도 하나의 잠수 결과다
        if (_isTracking) LogDiveEnd(result);

        _isTracking = false;
        IsDataPending = false;
        MaxDepth = 0f;
        TimeUnderground = 0f;
        MinedMinerals.Clear();
        Debug.Log("[SettlementManager] 긴급 탈출 — 추적 중단 및 리셋 (정산 데이터 없음)");
    }

    /// <summary>
    /// 광물 획득 시 호출하여 기록합니다.
    /// </summary>
    public void AddMinedMineral(MineralID id, int count)
    {
        if (!_isTracking) return;

        if (MinedMinerals.ContainsKey(id))
        {
            MinedMinerals[id] += count;
        }
        else
        {
            MinedMinerals[id] = count;
        }
    }

    /// <summary>
    /// 이번 잠수의 파기 픽셀을 누적한다. TerrainChunk.Dig가 호출한다.
    /// 추적 중이 아니면(지상·정산 후) 무시한다.
    /// </summary>
    public void AddDugPixels(int count)
    {
        if (!_isTracking || count <= 0) return;
        _dugPixels += count;
    }

    /// <summary>
    /// 스윙(파기 시도) 1회를 기록한다. 각 도구 전략이 한 번 휘두를 때마다 호출한다.
    /// 추적 중이 아니면(지상·정산 후) 무시한다.
    /// </summary>
    /// <param name="toolIndex">0 맨손 / 1 삽 / 2 곡괭이 / 3 드릴</param>
    /// <param name="hitSomething">지형이나 돌이 실제로 깎였는지(헛스윙 구분).</param>
    public void AddSwing(int toolIndex, bool hitSomething)
    {
        if (!_isTracking) return;
        if (toolIndex >= 0 && toolIndex < _swingsByTool.Length) _swingsByTool[toolIndex]++;
        if (hitSomething) _swingsHit++;
    }

    /// <summary>
    /// 벽타기 1프레임을 누적한다. <see cref="PlayerController"/>가 벽에 매달린 채
    /// 스태미나를 뽑을 때마다 호출한다. 사다리는 스태미나를 안 쓰므로 제외된다.
    /// 추적 중이 아니면(지상·정산 후) 무시한다.
    /// </summary>
    /// <param name="seconds">이번 프레임의 게임 시간(Time.fixedDeltaTime).</param>
    /// <param name="stamina">이번 프레임에 등반으로 소모한 스태미나.</param>
    public void AddWallClimb(float seconds, float stamina)
    {
        if (!_isTracking) return;
        _climbSeconds += seconds;
        _climbStamina += stamina;
    }

    private int TotalSwings()
    {
        int n = 0;
        foreach (int v in _swingsByTool) n += v;
        return n;
    }

    /// <summary>도구별 스윙 수를 JSON 오브젝트로. 예: {"hand":3,"shovel":120,"pickaxe":40,"drill":0}</summary>
    private string BuildSwingsByToolObject()
    {
        // 이름은 toolConfig.json의 순서와 같다(맨손·삽·곡괭이·드릴).
        string[] names = { "hand", "shovel", "pickaxe", "drill" };
        var sb = new System.Text.StringBuilder(64);
        sb.Append('{');
        for (int i = 0; i < _swingsByTool.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(names[i]).Append("\":")
              .Append(_swingsByTool[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// 현재까지 기록된 정산 데이터를 반환합니다.
    /// </summary>
    public SettlementData GetSettlementData()
    {
        return new SettlementData
        {
            maxDepth = MaxDepth,
            timeUnderground = TimeUnderground,
            minedMinerals = new Dictionary<MineralID, int>(MinedMinerals)
        };
    }
}

public struct SettlementData
{
    public float maxDepth;
    public float timeUnderground;
    public Dictionary<MineralID, int> minedMinerals;
}
