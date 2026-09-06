using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F8 키를 통해 토글되는 인게임 업그레이드 및 스탯 자가 진단 디버그 패널입니다.
/// 유니티 OnGUI API를 사용하여 프리팹 리소스 의존성 없이 완벽하게 독립적으로 즉시 렌더링됩니다.
///
/// 진단(읽기) 외에 밸런싱용 라이브 튜닝(쓰기)도 제공한다.
/// PlayerStat.SetBaseValue가 내부에서 MarkDirty()를 호출하므로 값을 바꾼 즉시 반영된다
/// — 플레이 재시작도, 리임포트도 필요 없다.
/// </summary>
public class StatDiagnosticPanel : MonoBehaviour
{
    // 씬마다 배치하지 않아도 되도록 자동 생성한다(SoundManager와 같은 패턴).
    // 종전엔 DemoUpground 씬에만 놓여 있어서 지하 씬에서는 F8이 먹지 않았다.
    private static StatDiagnosticPanel _instance;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// 씬의 Awake보다 먼저 도는 시점에 영속 인스턴스를 만든다.
    /// 여기서 만들어야 씬에 이미 놓인 중복 인스턴스가 자기 Awake에서 스스로 물러날 수 있다.
    /// 릴리스 빌드에는 치트 패널이 실려 나가면 안 되므로 자동 생성을 컴파일에서 제외한다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        AutoCreate();
        SceneManager.sceneLoaded -= OnSceneLoadedEnsure;
        SceneManager.sceneLoaded += OnSceneLoadedEnsure;
    }

    private static void OnSceneLoadedEnsure(Scene scene, LoadSceneMode mode) => AutoCreate();

    /// <summary>
    /// ⚠ 자가복구: GameManager.OpenMainMenu의 DestroyPersistentObjects()가
    /// DontDestroyOnLoad 씬의 루트를 전부 파괴해 이 오브젝트도 같이 죽는다.
    /// [RuntimeInitializeOnLoadMethod]는 세션당 한 번만 돌아 재생성되지 않으므로,
    /// 메인메뉴를 다녀오면 F8 패널이 영구히 안 열렸다. static 이벤트 구독은 파괴와
    /// 무관하게 살아남으므로 씬 로드마다 되살린다(SoundManager와 같은 패턴).
    /// </summary>
    private static void AutoCreate()
    {
        if (_instance != null) return;

        var go = new GameObject("[StatDiagnosticPanel]");
        go.AddComponent<StatDiagnosticPanel>();
        DontDestroyOnLoad(go);
    }
#endif

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        LoadPrefs();
        LoadKnobPrefs();
    }

    /// <summary>
    /// 저장해 둔 도구 노브 값을 static 튜닝에 되돌려 놓는다.
    /// 이 Awake는 BeforeSceneLoad에서 도므로 씬 컴포넌트(PlayerMining 등)보다 먼저다.
    ///
    /// 릴리스 빌드에서는 적용하지 않는다 — 패널이 씬에 남아 있더라도
    /// 예전에 만졌던 치트 값이 다음 실행에 조용히 되살아나면 안 된다.
    /// </summary>
    private void LoadKnobPrefs()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        foreach (var k in MiningKnobs)
        {
            string pref = PrefKnob + k.key;
            if (!PlayerPrefs.HasKey(pref)) continue;

            k.set(PlayerPrefs.GetFloat(pref));

            // 곡괭이 돌 최대치↓는 PlayerMining.Awake가 인스펙터 값으로 심는다(Seed*).
            // 저장값이 우선이라고 알려 두지 않으면 씬이 로드되는 순간 되돌아간다.
            if (k.key == KnobPickaxeRockRed)  MiningStaminaTuning.MarkPickaxeRockOverridden();
            if (k.key == KnobShovelChargeSec) MiningStaminaTuning.MarkChargeTimeOverridden();
        }
#endif
    }

    /// <summary>노브 값 변경 창구. 여기 한 곳만 거치게 해야 저장 누락이 안 생긴다.</summary>
    private void SetKnob(FloatKnob k, float value)
    {
        k.set(value);
        PlayerPrefs.SetFloat(PrefKnob + k.key, value);
        _knobPrefsDirty = true;
    }

    private void FlushKnobPrefs()
    {
        if (!_knobPrefsDirty) return;
        _knobPrefsDirty = false;
        PlayerPrefs.Save();
    }

    /// <summary>저장값을 지우고 코드(·인스펙터) 기본값으로 되돌린다.</summary>
    private void ClearKnobPrefs()
    {
        foreach (var k in MiningKnobs) PlayerPrefs.DeleteKey(PrefKnob + k.key);
        PlayerPrefs.Save();
        _knobPrefsDirty = false;

        MiningStaminaTuning.ResetTuningToDefaults();

        // 숫자칸 버퍼도 비워야 새 값으로 다시 채워진다.
        _knobBuffers.Clear();

        // '기본' 열과 ↺의 기준도 방금 되돌린 값으로 다시 잡는다.
        // 안 그러면 지운 저장값이 기준으로 남아 ↺가 그리로 되돌린다.
        foreach (var k in MiningKnobs) _knobSnapshot[k.key] = k.get();
    }

    private void OnDisable()
    {
        if (_instance == this)
        {
            _knobPrefsDirty = false;
            PlayerPrefs.Save();
        }
    }

    private bool _showPanel = false;
    private PlayerStat _playerStat;
    private PlayerController _playerController;
    private Rigidbody2D _playerRb;
    private StaminaManager _staminaManager;

    // 창 전체 본문 스크롤. 종전엔 진단 테이블만 자체 스크롤을 갖고 나머지는 창 밖으로 잘려나갔다.
    private Vector2 _scrollPosition;

    // ===================================================
    // 섹션 접기 (foldout)
    // ===================================================
    // 섹션 하나하나가 길어서 다 펴 두면 창을 최대로 키워도 안 들어간다.
    // 지금 보는 것만 펴 두고 나머지는 접는 용도. 접힘 상태는 PlayerPrefs에 남는다.
    private const string SecSummary = "summary";
    private const string SecTuning  = "tuning";
    private const string SecMining  = "mining";
    private const string SecTable   = "table";
    private const string SecCheat   = "cheat";

    private static readonly string[] SectionKeys = { SecSummary, SecTuning, SecMining, SecTable, SecCheat };

    private readonly Dictionary<string, bool> _folds = new Dictionary<string, bool>();
    private GUIStyle _headerStyle;

    // ===================================================
    // 창 크기 / 배율
    // ===================================================
    private const float DefaultWidth = 560f;
    private const float DefaultHeight = 1040f;
    private const float MinWidth = 420f;
    private const float MinHeight = 240f;
    private const float MinScale = 0.6f;
    private const float MaxScale = 3f;

    private Rect _windowRect = new Rect(20, 20, DefaultWidth, DefaultHeight);

    // GUI.matrix 배율. 창만 키우면 글자는 그대로라 고해상도에서 안 보인다 —
    // 배율은 글자·버튼·클릭 판정까지 통째로 확대한다.
    private float _uiScale = 1f;

    // 우하단 그립 드래그 상태. 드래그는 창 밖으로도 나가므로 화면 좌표로 추적한다.
    private bool _resizing = false;
    private Vector2 _resizeStartMouseScreen;
    private Vector2 _resizeStartSize;

    private const string PrefScale = "StatDiagPanel.scale";
    private const string PrefWidth = "StatDiagPanel.w";
    private const string PrefHeight = "StatDiagPanel.h";
    private const string PrefX = "StatDiagPanel.x";
    private const string PrefY = "StatDiagPanel.y";
    private const string PrefFold = "StatDiagPanel.fold.";

    // 도구 노브(⛏ 섹션) 튜닝값도 같은 PlayerPrefs에 남긴다.
    // 밸런싱 값은 여러 번 플레이해 보며 다듬는 것이라 플레이 정지마다 날아가면 쓸 수가 없다
    // (MiningStaminaTuning은 static이라 ResetStatics에서 매 플레이 초기화된다).
    private const string PrefKnob = "StatDiagPanel.knob.";

    // PlayerPrefs.Save()는 디스크(레지스트리) 쓰기라 슬라이더를 끄는 매 프레임 부를 수 없다.
    // SetFloat만 해 두고 패널을 닫을 때·비활성화될 때 한 번 flush 한다.
    private bool _knobPrefsDirty;

    private void LoadPrefs()
    {
        _uiScale = Mathf.Clamp(PlayerPrefs.GetFloat(PrefScale, 1f), MinScale, MaxScale);
        _windowRect = new Rect(
            PlayerPrefs.GetFloat(PrefX, 20f),
            PlayerPrefs.GetFloat(PrefY, 20f),
            Mathf.Max(MinWidth, PlayerPrefs.GetFloat(PrefWidth, DefaultWidth)),
            Mathf.Max(MinHeight, PlayerPrefs.GetFloat(PrefHeight, DefaultHeight)));

        foreach (string key in SectionKeys)
        {
            _folds[key] = PlayerPrefs.GetInt(PrefFold + key, 1) != 0;
        }
    }

    private void SavePrefs()
    {
        PlayerPrefs.SetFloat(PrefScale, _uiScale);
        PlayerPrefs.SetFloat(PrefWidth, _windowRect.width);
        PlayerPrefs.SetFloat(PrefHeight, _windowRect.height);
        PlayerPrefs.SetFloat(PrefX, _windowRect.x);
        PlayerPrefs.SetFloat(PrefY, _windowRect.y);
    }

    private bool IsOpen(string key)
    {
        return !_folds.TryGetValue(key, out bool open) || open;
    }

    private void SetOpen(string key, bool open)
    {
        _folds[key] = open;
        PlayerPrefs.SetInt(PrefFold + key, open ? 1 : 0);
    }

    private void SetAllSections(bool open)
    {
        foreach (string key in SectionKeys) SetOpen(key, open);
    }

    /// <summary>
    /// 접기 가능한 섹션 머리글. 반환값이 false면 그 섹션 본문은 그리지 않는다.
    /// 줄 전체가 버튼이라 어디를 눌러도 토글된다.
    /// </summary>
    private bool DrawSectionHeader(string key, string title)
    {
        if (_headerStyle == null)
        {
            _headerStyle = new GUIStyle(GUI.skin.button)
            {
                richText = true,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };
            _headerStyle.padding = new RectOffset(8, 8, 3, 3);
            _headerStyle.normal.textColor = Color.white;
            _headerStyle.hover.textColor = Color.cyan;
        }

        bool open = IsOpen(key);
        if (GUILayout.Button((open ? "▼  " : "▶  ") + title, _headerStyle, GUILayout.Height(22)))
        {
            open = !open;
            SetOpen(key, open);
        }
        return open;
    }

    // ===================================================
    // 라이브 튜닝 대상 정의
    // ===================================================
    private struct Tunable
    {
        public StatType type;
        public string label;
        public float min;
        public float max;

        public Tunable(StatType type, string label, float min, float max)
        {
            this.type = type; this.label = label; this.min = min; this.max = max;
        }
    }

    // 슬라이더 범위는 '탐색 구간'일 뿐이다. 범위를 벗어난 값이 필요하면 숫자칸에 직접 입력하면 된다
    // (입력값은 클램프하지 않는다).
    private static readonly Tunable[] Tunables =
    {
        new Tunable(StatType.MoveSpeed,                 "이동 속도",          0f,   20f),
        new Tunable(StatType.JumpForce,                 "점프 力",            0f,   30f),
        new Tunable(StatType.WallClimbSpeed,            "벽타기 속도",        0f,   20f),
        new Tunable(StatType.EncumberedSpeedMultiplier, "과적 시 속도배율",   0.1f,  1f),
        new Tunable(StatType.MaxStamina,                "최대 스태미나",      0f, 2000f),
        new Tunable(StatType.StaminaCostPerSecond,      "스태미나 초당소모",  0f,   50f),
        new Tunable(StatType.MiningRange,               "채굴 사거리",        0f,    5f),
        new Tunable(StatType.MiningCooldown,            "채굴 쿨타임",     0.05f,    1f),
    };

    // 텍스트 입력 버퍼. 값을 매 프레임 ToString으로 덮으면 "5." 같은 입력 중간 상태가 지워진다.
    private readonly Dictionary<StatType, string> _editBuffers = new Dictionary<StatType, string>();

    // 이번 플레이 세션에서 패널을 처음 연 시점의 값 — 행별 되돌리기(↺)용.
    // 패널을 닫았다 열어도 다시 찍지 않는다. 튜닝을 여러 번 오가다 원래 값으로
    // 돌아갈 길이 사라지는 게 더 나쁘기 때문.
    private readonly Dictionary<StatType, float> _snapshot = new Dictionary<StatType, float>();
    private bool _snapshotTaken = false;

    private string _bakeMessage = "";
    private float _bakeMessageTime = -100f;

    // ===================================================
    // 도구별 채굴 스태미나 (PlayerStat이 아니라 MiningStaminaTuning의 static 값)
    // ===================================================
    /// <summary>
    /// PlayerStat 밖의 임의 float를 같은 UI로 굴리기 위한 행 정의.
    /// StatType 키를 못 쓰므로 문자열 키 + get/set 델리게이트로 간다.
    /// </summary>
    private struct FloatKnob
    {
        public string key;
        public string label;
        public float min;
        public float max;
        public Func<float> get;
        public Action<float> set;

        public FloatKnob(string key, string label, float min, float max, Func<float> get, Action<float> set)
        {
            this.key = key; this.label = label; this.min = min; this.max = max;
            this.get = get; this.set = set;
        }
    }

    // 저장값 로드가 Seed(인스펙터 값 심기)와 충돌하는 항목들. 키를 상수로 뽑아 둔다.
    private const string KnobPickaxeRockRed  = "pick_rock_red";
    private const string KnobShovelChargeSec = "shov_chg_sec";

    // 캡처가 없는 람다라 정적 초기화에 그대로 둬도 매 프레임 할당이 생기지 않는다.
    private static readonly FloatKnob[] MiningKnobs =
    {
        // 돌과 지형의 슬라이더 범위가 다른 이유: 기저값이 20배 차이 난다
        // (HardStone 2.0 vs Dirt 0.1). 지형을 0~5로 두면 최대로 밀어도 체감이 안 온다.
        new FloatKnob("pick_rock_mul",  "곡괭이 돌 소모×",    0f, 10f,
            () => MiningStaminaTuning.PickaxeRockCostMultiplier,
            v  => MiningStaminaTuning.PickaxeRockCostMultiplier = v),
        new FloatKnob("pick_terr_mul",  "곡괭이 지형 소모×",  0f, 100f,
            () => MiningStaminaTuning.PickaxeTerrainCostMultiplier,
            v  => MiningStaminaTuning.PickaxeTerrainCostMultiplier = v),
        new FloatKnob(KnobPickaxeRockRed, "곡괭이 돌 최대치↓", 0f, 10f,
            () => MiningStaminaTuning.PickaxeRockMaxReduction,
            v  => MiningStaminaTuning.PickaxeRockMaxReduction = v),
        new FloatKnob("pick_terr_red",  "곡괭이 지형 최대치↓", 0f, 5f,
            () => MiningStaminaTuning.PickaxeTerrainReductionPerRadius,
            v  => MiningStaminaTuning.PickaxeTerrainReductionPerRadius = v),

        new FloatKnob("shov_rock_mul",  "삽 돌 소모×",        0f, 10f,
            () => MiningStaminaTuning.ShovelRockCostMultiplier,
            v  => MiningStaminaTuning.ShovelRockCostMultiplier = v),
        new FloatKnob("shov_terr_mul",  "삽 지형 소모×",      0f, 100f,
            () => MiningStaminaTuning.ShovelTerrainCostMultiplier,
            v  => MiningStaminaTuning.ShovelTerrainCostMultiplier = v),
        new FloatKnob("shov_rock_red",  "삽 돌 최대치↓",      0f, 10f,
            () => MiningStaminaTuning.ShovelRockMaxReduction,
            v  => MiningStaminaTuning.ShovelRockMaxReduction = v),
        new FloatKnob("shov_terr_red",  "삽 지형 최대치↓/차징", 0f, 5f,
            () => MiningStaminaTuning.ShovelTerrainReductionPerCharge,
            v  => MiningStaminaTuning.ShovelTerrainReductionPerCharge = v),
        // 차징 시간만 단위가 초다. 0으로 내려가면 비율 계산이 깨지므로 하한을 튜닝쪽 상수에 맞춘다.
        new FloatKnob(KnobShovelChargeSec, "삽 풀차징 시간(초)", MiningStaminaTuning.MinChargeTime, 3f,
            () => MiningStaminaTuning.ShovelMaxChargeTime,
            v  => MiningStaminaTuning.ShovelMaxChargeTime = v),
        new FloatKnob("shov_minchg",    "삽 최소 차징",       0f, 1f,
            () => MiningStaminaTuning.ShovelMinChargeRatio,
            v  => MiningStaminaTuning.ShovelMinChargeRatio = v),
        new FloatKnob("shov_radius",    "삽 파기 크기×",      0f, 10f,
            () => MiningStaminaTuning.ShovelRadiusMultiplier,
            v  => MiningStaminaTuning.ShovelRadiusMultiplier = v),
    };

    private readonly Dictionary<string, string> _knobBuffers = new Dictionary<string, string>();
    private readonly Dictionary<string, float> _knobSnapshot = new Dictionary<string, float>();
    private bool _knobSnapshotTaken = false;

    private void Update()
    {
        // F8 단축키를 눌러 디버그 자가진단 패널 토글
        if (Input.GetKeyDown(KeyCode.F8))
        {
            _showPanel = !_showPanel;
            if (_showPanel)
            {
                FindPlayerStat();
                TakeSnapshot();
                TakeKnobSnapshot();
            }
            else
            {
                // 닫을 때 한 번만 디스크에 쓴다(슬라이더 드래그 중에는 SetFloat만).
                FlushKnobPrefs();
            }
        }
    }

    private void FindPlayerStat()
    {
        _playerStat = FindFirstObjectByType<PlayerStat>();

        if (_playerStat != null)
        {
            _playerController = _playerStat.GetComponent<PlayerController>();
            _playerRb = _playerStat.GetComponent<Rigidbody2D>();

            // StaminaManager는 플레이어 루트가 아닌 다른 오브젝트에 붙어 있을 수 있다
            // (전략들도 같은 폴백을 쓴다). OnGUI는 프레임당 여러 번 도니 여기서 한 번만 잡는다.
            _staminaManager = _playerStat.GetComponent<StaminaManager>()
                              ?? FindFirstObjectByType<StaminaManager>();
        }
    }

    /// <summary>세션 최초로 패널을 연 시점의 기준값을 기록한다. 되돌리기 버튼의 기준점.</summary>
    private void TakeSnapshot()
    {
        if (_playerStat == null || _snapshotTaken) return;

        foreach (var t in Tunables)
        {
            _snapshot[t.type] = _playerStat.GetBaseValue(t.type);
        }
        _snapshotTaken = true;
    }

    /// <summary>
    /// 도구 노브의 기준값 기록. PlayerStat과 무관하므로 별도 플래그로 관리한다
    /// (플레이어가 아직 없는 씬에서 패널을 열어도 노브는 되돌리기가 살아 있어야 한다).
    /// </summary>
    private void TakeKnobSnapshot()
    {
        if (_knobSnapshotTaken) return;

        foreach (var k in MiningKnobs)
        {
            _knobSnapshot[k.key] = k.get();
        }
        _knobSnapshotTaken = true;
    }

    private void OnGUI()
    {
        if (!_showPanel) return;

        // 배율은 GUI.matrix로 건다. 폰트 크기를 일일이 곱하는 방식과 달리
        // 레이아웃·클릭 판정까지 함께 확대되므로 어긋날 여지가 없다.
        // 다른 컴포넌트의 OnGUI가 영향받지 않도록 끝나면 반드시 원복한다.
        Matrix4x4 prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_uiScale, _uiScale, 1f));

        // 다크 테마 스타일링 적용
        GUI.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        _windowRect = GUI.Window(9999, _windowRect, DrawDiagnosticWindow, "📊 인게임 스탯 & 업그레이드 자가진단 시스템 (F8로 종료)");

        HandleResizeDrag();
        ClampWindowToScreen();

        GUI.matrix = prevMatrix;
    }

    /// <summary>
    /// 그립 드래그 처리. 창 콜백 밖에서 돌려야 커서가 창을 벗어나도 계속 따라온다.
    /// </summary>
    private void HandleResizeDrag()
    {
        if (!_resizing) return;

        Event e = Event.current;

        // 화면 좌표로 비교한다 — 창 안/밖, 배율 변화에 상관없이 같은 기준이 된다.
        // 창 크기는 GUI 단위이므로 배율로 나눠 되돌린다.
        Vector2 nowScreen = GUIUtility.GUIToScreenPoint(e.mousePosition);
        Vector2 deltaGui = (nowScreen - _resizeStartMouseScreen) / _uiScale;

        _windowRect.width = Mathf.Max(MinWidth, _resizeStartSize.x + deltaGui.x);
        _windowRect.height = Mathf.Max(MinHeight, _resizeStartSize.y + deltaGui.y);

        // rawType이라야 창이 이벤트를 먼저 먹은 경우에도 버튼 뗌을 놓치지 않는다.
        if (e.rawType == EventType.MouseUp)
        {
            _resizing = false;
            SavePrefs();
        }
    }

    /// <summary>창이 화면 밖으로 완전히 빠져나가 잡을 수 없게 되는 것을 막는다.</summary>
    private void ClampWindowToScreen()
    {
        float maxW = Screen.width / _uiScale;
        float maxH = Screen.height / _uiScale;

        _windowRect.width = Mathf.Clamp(_windowRect.width, MinWidth, Mathf.Max(MinWidth, maxW));
        _windowRect.height = Mathf.Clamp(_windowRect.height, MinHeight, Mathf.Max(MinHeight, maxH));

        // 제목 표시줄만은 항상 화면 안에 남겨 둔다(드래그로 되돌릴 수 있어야 하므로).
        _windowRect.x = Mathf.Clamp(_windowRect.x, -_windowRect.width + 100f, maxW - 100f);
        _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, maxH - 26f));
    }

    /// <summary>
    /// 우하단 크기 조절 그립. 창 콜백 안에서 그려야 창 로컬 좌표가 맞는다.
    /// </summary>
    private void DrawResizeGrip()
    {
        const float S = 20f;
        Rect grip = new Rect(_windowRect.width - S - 2f, _windowRect.height - S - 2f, S, S);

        var style = GetRichLabelStyle(_resizing ? Color.cyan : new Color(0.65f, 0.65f, 0.75f), 15);
        GUI.Label(grip, "◢", style);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && grip.Contains(e.mousePosition))
        {
            _resizing = true;
            _resizeStartMouseScreen = GUIUtility.GUIToScreenPoint(e.mousePosition);
            _resizeStartSize = new Vector2(_windowRect.width, _windowRect.height);
            e.Use();
        }
    }

    /// <summary>
    /// 배율 조절 줄. 슬라이더 대신 버튼을 쓴다 — 배율을 바꾸면 슬라이더 자신이
    /// 커서 밑에서 같이 움직여서 드래그가 튀기 때문.
    /// </summary>
    private void DrawScaleRow()
    {
        GUILayout.BeginHorizontal();

        GUILayout.Label($"🔍 배율 <b>{_uiScale:F2}x</b>", GetRichLabelStyle(Color.white, 12), GUILayout.Width(90));

        if (GUILayout.Button("−", GUILayout.Width(28))) SetScale(_uiScale - 0.1f);
        if (GUILayout.Button("+", GUILayout.Width(28))) SetScale(_uiScale + 0.1f);

        GUILayout.Space(6);
        if (GUILayout.Button("1x", GUILayout.Width(34))) SetScale(1f);
        if (GUILayout.Button("1.5x", GUILayout.Width(42))) SetScale(1.5f);
        if (GUILayout.Button("2x", GUILayout.Width(34))) SetScale(2f);

        GUILayout.Space(6);
        if (GUILayout.Button("창 초기화", GUILayout.Width(70)))
        {
            _uiScale = 1f;
            _windowRect = new Rect(20f, 20f, DefaultWidth, DefaultHeight);
            SavePrefs();
        }

        GUILayout.Space(6);
        if (GUILayout.Button("▶전부", GUILayout.Width(54))) SetAllSections(false);
        if (GUILayout.Button("▼전부", GUILayout.Width(54))) SetAllSections(true);

        GUILayout.FlexibleSpace();
        GUILayout.Label($"{(int)_windowRect.width}×{(int)_windowRect.height}  (우하단 ◢ 드래그)",
                        GetRichLabelStyle(Color.gray, 11));

        GUILayout.EndHorizontal();
    }

    private void SetScale(float value)
    {
        _uiScale = Mathf.Clamp(value, MinScale, MaxScale);
        SavePrefs();
    }

    /// <summary>
    /// 디버그 GUI 윈도우 내용 그리기
    /// </summary>
    private void DrawDiagnosticWindow(int windowId)
    {
        if (_playerStat == null)
        {
            FindPlayerStat();
            TakeSnapshot();
        }
        TakeKnobSnapshot();

        GUILayout.BeginVertical();

        // 0. 창 배율 / 크기 조절 — 스크롤 밖 고정. 창이 아무리 작아져도 여긴 잡을 수 있어야 한다.
        DrawScaleRow();

        GUILayout.Space(4);

        // 본문 전체 스크롤.
        // 높이를 명시하는 이유: 창 콜백 안의 GUILayout은 ExpandHeight를 창 크기 변화 도중
        // 한 프레임 늦게 반영해 그립과 겹쳐 보이는 일이 있다. 남는 높이를 직접 계산한다.
        // 뺀 값 = 제목줄(20) + 배율줄(24) + 여백(8) + 그립 자리(24).
        float bodyHeight = Mathf.Max(80f, _windowRect.height - 76f);
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(bodyHeight));

        // 1. 헤더: 업그레이드 상태 요약
        if (DrawSectionHeader(SecSummary, "📊 현재 상태 요약")) DrawSummaryHeader();

        GUILayout.Space(6);

        // 2. 라이브 튜닝 패널 (밸런싱용 — 즉시 반영)
        if (DrawSectionHeader(SecTuning, "🎚️ 라이브 밸런싱 (수정 즉시 반영)")) DrawLiveTuningPanel();

        GUILayout.Space(6);

        // 2-B. 도구별 채굴 스태미나 (곡괭이 / 삽)
        if (DrawSectionHeader(SecMining, "⛏️ 도구별 채굴 스태미나 (수정 즉시 반영)")) DrawMiningStaminaPanel();

        GUILayout.Space(6);

        // 3. 중요 스탯 정밀 자가진단 테이블
        if (DrawSectionHeader(SecTable, "🩺 스탯 보정량 정밀 대조 테이블")) DrawStatDiagnosticTable();

        GUILayout.Space(6);

        // 4. 테스트 및 밸런싱 편의를 위한 퀵 치트 패널
        if (DrawSectionHeader(SecCheat, "🛠️ 스탯 점검용 치트 및 테스트 도구")) DrawQuickCheatPanel();

        GUILayout.Space(8);

        GUILayout.EndScrollView();

        GUILayout.EndVertical();

        // 그립은 레이아웃 밖 고정 좌표라 마지막에 덧그린다.
        DrawResizeGrip();

        // 마우스로 창 드래그 가능하게 설정.
        // 그립 영역(우하단)은 y 범위가 겹치지 않으므로 서로 간섭하지 않는다.
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    private void DrawSummaryHeader()
    {
        GUILayout.BeginVertical("box");

        int unlockedCount = 0;
        if (UpgradeManager.Instance != null)
        {
            var state = UpgradeManager.Instance.GetState();
            if (state != null && state.unlockedNodeIds != null)
            {
                unlockedCount = state.unlockedNodeIds.Count;
            }
        }

        GUILayout.Label($"🔓 현재 해금된 업그레이드 개수: <b>{unlockedCount}</b> / 34 개", GetRichLabelStyle(Color.green, 13));

        if (_playerStat != null)
        {
            GUILayout.Label($"💰 보유한 금화(Gold): <b>{_playerStat.Gold}</b> Gold", GetRichLabelStyle(Color.yellow, 13));
            GUILayout.Label($"🔋 스태미나: <b>{Mathf.RoundToInt(_playerStat.CurrentStamina)}</b> / {Mathf.RoundToInt(_playerStat.MaxStamina)}", GetRichLabelStyle(Color.cyan, 13));
        }
        else
        {
            GUILayout.Label("⚠️ 경고: 씬 내에서 활성화된 PlayerStat을 감지할 수 없습니다.", GetRichLabelStyle(Color.red, 12));
        }

        GUILayout.EndVertical();
    }

    // ===================================================
    // 라이브 튜닝
    // ===================================================
    private void DrawLiveTuningPanel()
    {
        GUILayout.BeginVertical("box");

        if (_playerStat == null)
        {
            GUILayout.Label("PlayerStat이 없어 튜닝할 수 없습니다.", GetRichLabelStyle(Color.gray, 12));
            GUILayout.EndVertical();
            return;
        }

        DrawEffectiveSpeedReadout();
        GUILayout.Space(4);

        // 열 머리글
        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>항목</b>", GetRichLabelStyle(Color.white, 11), GUILayout.Width(120));
        GUILayout.Label("<b>기준값</b>", GetRichLabelStyle(Color.white, 11), GUILayout.Width(58));
        GUILayout.Label("", GUILayout.Width(140));
        GUILayout.Label("<b>최종</b>", GetRichLabelStyle(Color.white, 11), GUILayout.Width(55));
        GUILayout.Label("", GUILayout.Width(30));
        GUILayout.EndHorizontal();

        foreach (var t in Tunables)
        {
            DrawTunableRow(t);
        }

        GUILayout.Space(4);
        DrawBakeRow();

        GUILayout.EndVertical();
    }

    /// <summary>
    /// 실제로 발이 나가는 속도. PlayerStat의 Final값은 곱해지기 전 값이라
    /// 여기서 도구·과적·현재 속도까지 같이 보여줘야 튜닝 판단이 선다.
    /// </summary>
    private void DrawEffectiveSpeedReadout()
    {
        float finalSpeed = _playerStat.GetFinalValue(StatType.MoveSpeed);

        if (_playerController == null)
        {
            GUILayout.Label($"실효 이동속도: <b>{finalSpeed:F2}</b> (PlayerController 미검출 — 배율 미반영)",
                            GetRichLabelStyle(Color.gray, 12));
            return;
        }

        float toolMul = _playerController.speedMultiplier;
        float encMul = _playerController.encumbranceMultiplier;
        float envMul = _playerController.EnvironmentSpeedMultiplier;
        float effective = finalSpeed * toolMul * encMul * envMul;

        // 경사 감속(slopeSpeedRatio)은 PlayerController 내부 지역변수라 여기서 못 읽는다.
        // 대신 실제 강체 속도를 같이 띄워서 눈으로 대조하게 한다.
        float actual = _playerRb != null ? Mathf.Abs(_playerRb.linearVelocity.x) : 0f;

        Color c = Mathf.Approximately(toolMul * encMul * envMul, 1f) ? Color.cyan : new Color(1f, 0.8f, 0.3f);

        GUILayout.Label(
            $"실효 이동속도: <b>{effective:F2}</b>   = 최종 {finalSpeed:F2} × 도구 {toolMul:F2} × 과적 {encMul:F2} × 지형 {envMul:F2}",
            GetRichLabelStyle(c, 12));
        GUILayout.Label(
            $"현재 실제 수평속도 |vx| = <b>{actual:F2}</b>   (경사 감속·바람·빙판이 섞이면 실효값과 벌어짐)",
            GetRichLabelStyle(Color.gray, 11));
    }

    private void DrawTunableRow(Tunable t)
    {
        float baseVal = _playerStat.GetBaseValue(t.type);
        float finalVal = _playerStat.GetFinalValue(t.type);

        string ctrlName = "tune_" + t.type;

        // 버퍼 초기화 / 외부 변경 동기화.
        // 편집 중(포커스 보유)인 칸은 건드리지 않는다 — 입력 중간 상태가 날아가면 타이핑이 불가능해진다.
        if (!_editBuffers.TryGetValue(t.type, out string buf))
        {
            buf = baseVal.ToString("0.###", CultureInfo.InvariantCulture);
            _editBuffers[t.type] = buf;
        }
        else if (GUI.GetNameOfFocusedControl() != ctrlName)
        {
            if (!TryParse(buf, out float shown) || !Mathf.Approximately(shown, baseVal))
            {
                buf = baseVal.ToString("0.###", CultureInfo.InvariantCulture);
                _editBuffers[t.type] = buf;
            }
        }

        GUILayout.BeginHorizontal();

        GUILayout.Label(t.label, GetRichLabelStyle(Color.white, 12), GUILayout.Width(120));

        // 숫자 직접 입력 (슬라이더 범위 밖 값도 허용)
        GUI.SetNextControlName(ctrlName);
        string typed = GUILayout.TextField(buf, GUILayout.Width(58));
        if (typed != buf)
        {
            _editBuffers[t.type] = typed;
            if (TryParse(typed, out float parsed))
            {
                _playerStat.SetBaseValue(t.type, parsed);
                baseVal = parsed;
            }
        }

        // 슬라이더.
        // HorizontalSlider는 넘긴 값을 항상 [min,max]로 잘라서 되돌려준다.
        // 그래서 범위 밖 값을 그대로 넘기면 "사용자가 드래그한 것"과 구분이 안 돼
        // 숫자칸에 직접 넣은 범위 밖 값이 매 프레임 잘려나간다.
        // 미리 클램프한 값을 넘기고 그것과 비교해야 실제 드래그만 걸러진다.
        float clamped = Mathf.Clamp(baseVal, t.min, t.max);
        float slid = GUILayout.HorizontalSlider(clamped, t.min, t.max, GUILayout.Width(140));
        if (!Mathf.Approximately(slid, clamped))
        {
            _playerStat.SetBaseValue(t.type, slid);
            _editBuffers[t.type] = slid.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // 최종값 — 기준값과 다르면(업그레이드·유물 보정) 초록으로
        bool modified = !Mathf.Approximately(finalVal, baseVal);
        GUILayout.Label(finalVal.ToString("F2"),
                        GetRichLabelStyle(modified ? Color.green : Color.white, 12),
                        GUILayout.Width(55));

        // 세션 최초 값으로 되돌리기
        if (_snapshot.TryGetValue(t.type, out float original))
        {
            GUI.enabled = !Mathf.Approximately(original, baseVal);
            if (GUILayout.Button("↺", GUILayout.Width(26)))
            {
                _playerStat.SetBaseValue(t.type, original);
                _editBuffers[t.type] = original.ToString("0.###", CultureInfo.InvariantCulture);
            }
            GUI.enabled = true;
        }

        GUILayout.EndHorizontal();
    }

    private void DrawBakeRow()
    {
        GUILayout.BeginHorizontal();

#if UNITY_EDITOR
        if (GUILayout.Button("💾 현재 값을 PlayerSO에 굽기 (뉴게임 기본값)", GUILayout.Height(26)))
        {
            BakeToPlayerSO();
        }
#else
        GUILayout.Label("(PlayerSO 굽기는 에디터에서만 가능)", GetRichLabelStyle(Color.gray, 11));
#endif

        GUILayout.EndHorizontal();

        if (Time.unscaledTime - _bakeMessageTime < 6f && !string.IsNullOrEmpty(_bakeMessage))
        {
            GUILayout.Label(_bakeMessage, GetRichLabelStyle(new Color(1f, 0.85f, 0.4f), 11));
        }
        else
        {
            GUILayout.Label("튜닝한 값은 런타임 기준값이라 세이브 시 저장되고, 기존 세이브를 다시 불러오면 그 값으로 덮어써진다.",
                            GetRichLabelStyle(Color.gray, 11));
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 현재 기준값을 PlayerSO 에셋에 영구 기록한다.
    /// 세이브 파일이 있으면 로드 시 FromData가 이 값을 덮어쓰므로, 실제로 확인하려면
    /// 세이브를 지우고 뉴게임을 시작해야 한다 — 그 사실을 메시지로 함께 알린다.
    /// </summary>
    private void BakeToPlayerSO()
    {
        PlayerSO so = SaveManager.Instance != null ? SaveManager.Instance.playerSO : null;

        if (so == null)
        {
            _bakeMessage = "⚠ SaveManager.playerSO가 비어 있어 저장하지 못했습니다.";
            _bakeMessageTime = Time.unscaledTime;
            return;
        }

        UnityEditor.Undo.RecordObject(so, "Bake player stats from diagnostic panel");

        so.moveSpeed = _playerStat.GetBaseValue(StatType.MoveSpeed);
        so.jumpForce = _playerStat.GetBaseValue(StatType.JumpForce);
        so.wallClimbingSpeed = _playerStat.GetBaseValue(StatType.WallClimbSpeed);
        so.encumberedSpeedMultiplier = _playerStat.GetBaseValue(StatType.EncumberedSpeedMultiplier);
        so.maxStamina = _playerStat.GetBaseValue(StatType.MaxStamina);
        so.staminaCostPerSecond = _playerStat.GetBaseValue(StatType.StaminaCostPerSecond);
        so.miningRange = _playerStat.GetBaseValue(StatType.MiningRange);
        so.miningCooldown = _playerStat.GetBaseValue(StatType.MiningCooldown);
        so.miningLevel = Mathf.RoundToInt(_playerStat.GetBaseValue(StatType.MiningLevel));

        UnityEditor.EditorUtility.SetDirty(so);
        UnityEditor.AssetDatabase.SaveAssets();

        _bakeMessage = $"✅ '{so.name}'에 기록 완료. 기존 세이브가 있으면 로드 시 세이브 값이 우선하므로, 뉴게임으로 확인하세요.";
        _bakeMessageTime = Time.unscaledTime;

        Debug.Log($"[StatDiagnosticPanel] PlayerSO '{so.name}'에 현재 기준값을 기록했습니다. moveSpeed={so.moveSpeed}, jumpForce={so.jumpForce}");
    }
#endif

    // ===================================================
    // 도구별 채굴 스태미나
    // ===================================================
    /// <summary>
    /// 곡괭이·삽이 한 번 팔 때 나가는 값들. MiningStaminaTuning의 static 필드를 직접 쓴다.
    /// 전략과 Digger가 매 스윙 그 값을 읽으므로 수정 즉시 반영된다.
    /// </summary>
    private void DrawMiningStaminaPanel()
    {
        GUILayout.BeginVertical("box");

        // 배율만 봐서는 체감이 안 잡히므로 실제로 나간 값을 같이 띄운다.
        GUILayout.Label(
            $"마지막 스윙 소모  곡괭이 <b>{MiningStaminaTuning.LastPickaxeCost:F3}</b>   삽 <b>{MiningStaminaTuning.LastShovelCost:F3}</b>",
            GetRichLabelStyle(Color.cyan, 12));

        DrawShovelDigCountRow();

        // 기저값을 안 보여주면 배율을 올려도 왜 체감이 없는지 알 길이 없다.
        // 지형 저항은 층마다 다르므로(Dirt 0.1 → 최하층 2.0) 지금 서 있는 깊이 기준으로 뽑는다.
        DrawDigCostBreakdown();

        // MaxStamina를 깎는 항목은 5개다. 하나만 보여주면 "삽 때문에 줄었다"는 오진을 낳는다
        // — 낙하(injury)·화상·동상·방사선도 같은 MaxStamina modifier에 합산된다.
        if (_staminaManager != null)
        {
            GUILayout.Label(
                $"MaxStamina 감소 내역   채굴 <b>{_staminaManager.DiggingReduction:F3}</b>" +
                $" | 부상 <b>{_staminaManager.Injury:F3}</b>" +
                $" | 화상 <b>{_staminaManager.Burn:F3}</b>" +
                $" | 동상 <b>{_staminaManager.Frostbite:F3}</b>" +
                $" | 방사선 <b>{_staminaManager.Radiation:F3}</b>",
                GetRichLabelStyle(new Color(1f, 0.8f, 0.3f), 11));
        }

        // 스윙마다 콘솔에 차징비율·반경·지불액을 찍는다. 원인 추적용이라 기본 꺼짐.
        GUILayout.BeginHorizontal();
        bool log = GUILayout.Toggle(MiningStaminaTuning.LogDigs, " 채굴 스윙 콘솔 로그", GUILayout.Width(160));
        if (log != MiningStaminaTuning.LogDigs) MiningStaminaTuning.LogDigs = log;
        GUILayout.Label("(차징 중 파기가 도는지 / 최소차징을 넘었는지 확인용)", GetRichLabelStyle(Color.gray, 11));
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        // 열 머리글 — 라이브 튜닝 표와 폭을 맞춰 시선이 끊기지 않게 한다.
        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>항목</b>", GetRichLabelStyle(Color.white, 11), GUILayout.Width(120));
        GUILayout.Label("<b>값</b>", GetRichLabelStyle(Color.white, 11), GUILayout.Width(58));
        GUILayout.Label("", GUILayout.Width(140));
        GUILayout.Label("<b>기본</b>", GetRichLabelStyle(Color.white, 11), GUILayout.Width(55));
        GUILayout.Label("", GUILayout.Width(30));
        GUILayout.EndHorizontal();

        foreach (var k in MiningKnobs)
        {
            DrawKnobRow(k);
        }

        DrawKnobPersistRow();

        GUILayout.Label(
            "'소모×'는 현재 스태미나 비용 배율(지형=깊이별 저항값, 돌=HardStone 비용에 곱).\n" +
            "'최대치↓'는 파면 영구히 줄어드는 MaxStamina 페널티(돌=타격당 고정, 지형=반경당). 지상 복귀 시 초기화된다.\n" +
            "'삽 지형 소모×'만 기본 0이다 — 설계에 없던 항목이라 공짜에서 시작해 올려 잡는다.\n" +
            "'삽 최소 차징'(0~1) 미만에서 버튼을 떼면 스윙 자체가 취소된다 — 모션·파기·비용 전부 없음. 0.25 = 250ms.\n" +
            "'삽 파기 크기×'는 삽에만 걸린다(채굴 사거리는 곡괭이·드릴 공용). 크기는 비용에 영향을 주지 않는다.\n" +
            "삽 비용 기준은 '차징 비율'(0~1)이다 — 반경·사거리·크기와 무관하게 얼마나 힘줘 팠는지에만 비례. 곡괭이는 종전대로 반경 기준.",
            GetRichLabelStyle(Color.gray, 11));

        GUILayout.EndVertical();
    }

    /// <summary>
    /// 위 노브 값들이 저장된다는 사실과 되돌릴 방법을 같이 둔다.
    /// 저장이 눈에 안 보이면 "어제 만진 값이 왜 아직 걸려 있지"가 되기 때문.
    /// </summary>
    private void DrawKnobPersistRow()
    {
        GUILayout.BeginHorizontal();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GUILayout.Label("💾 위 값은 저장되어 플레이를 다시 켜도 유지됩니다 (에디터·개발 빌드 한정).",
                        GetRichLabelStyle(new Color(0.6f, 0.9f, 0.6f), 11));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("↺ 저장값 삭제(기본값)", GUILayout.Width(146)))
        {
            ClearKnobPrefs();
        }
#else
        GUILayout.Label("(튜닝값 저장·복원은 에디터·개발 빌드에서만 적용됩니다)", GetRichLabelStyle(Color.gray, 11));
#endif

        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// 지하에서 삽을 몇 번 휘둘렀는지. '잠수'는 지상 복귀(정산) 시 0으로 돌아가고,
    /// '누적'은 플레이 세션 내내 쌓인다. 지상에서 휘두른 스윙은 애초에 세지 않는다.
    /// 헛스윙(스윙은 나갔는데 지형·돌이 안 깎임)은 최소 차징·불괴 픽셀 진단에 쓰인다.
    /// </summary>
    private void DrawShovelDigCountRow()
    {
        int digs = MiningStaminaTuning.ShovelDigsThisTrip;
        int hits = MiningStaminaTuning.ShovelHitsThisTrip;
        int miss = digs - hits;

        GUILayout.BeginHorizontal();

        GUILayout.Label(
            $"⛏ 지하 삽질  이번 잠수 <b>{digs}</b>회  (실제 파임 {hits} / 헛스윙 {miss})" +
            $"   세션 누적 <b>{MiningStaminaTuning.ShovelDigsTotal}</b>회",
            GetRichLabelStyle(new Color(1f, 0.85f, 0.5f), 12));

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("↺ 횟수", GUILayout.Width(56)))
        {
            MiningStaminaTuning.ResetShovelTripCount();
            MiningStaminaTuning.ShovelDigsTotal = 0;
        }

        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// 지금 서 있는 깊이에서 1스윙에 얼마가 나가는지를 식 그대로 보여준다.
    /// 배율 숫자만으로는 판단이 안 서기 때문 — 기저값(지형 저항)이 층마다 20배까지 벌어진다.
    /// </summary>
    private void DrawDigCostBreakdown()
    {
        if (_playerStat == null || TileDataManager.Instance == null) return;

        var map = InfinityMapManager.Instance;
        if (map == null || map.chunkHeightWorld <= 0f) return;

        float playerY = _playerStat.transform.position.y;
        float baseRes = TileDataManager.Instance.GetStaminaReductionAtWorldY(playerY, map.chunkHeightWorld);

        // 크기(반경)와 비용이 이제 서로 독립이다 — 크기는 반경식, 비용은 차징식.
        // 두 줄로 나눠 보여줘야 "크기를 키웠는데 왜 비용이 그대로지?"를 안 묻게 된다.
        float range = Mathf.Max(0.1f, _playerStat.MiningRange);
        float toolRange = _playerStat.GetFinalValue(StatType.ToolRange);
        float shovelRadius = range * toolRange * MiningStaminaTuning.ShovelRadiusMultiplier;

        // 아래는 전부 풀차징(1.0) 기준.
        float shovelCost = baseRes * 1f * MiningStaminaTuning.ShovelTerrainCostMultiplier;
        float shovelMaxCut = MiningStaminaTuning.ShovelTerrainReductionPerCharge * 1f;

        GUILayout.Label(
            $"삽 풀차징 반경 <b>{shovelRadius:F2}</b>  (= 사거리 {range:F2} × ToolRange {toolRange:F2} × 크기× " +
            $"{MiningStaminaTuning.ShovelRadiusMultiplier:F2})   지름 약 <b>{shovelRadius * 200f:F0}px</b>  — 비용과 무관",
            GetRichLabelStyle(new Color(0.6f, 0.9f, 1f), 11));

        GUILayout.Label(
            $"└ 풀차징 1스윙  현재소모 <b>{shovelCost:F2}</b> (깊이저항 {baseRes:F2} × 차징 1.00 × 소모배율 " +
            $"{MiningStaminaTuning.ShovelTerrainCostMultiplier:F1})   최대치↓ <b>{shovelMaxCut:F3}</b>" +
            $"   [MaxStamina {_playerStat.MaxStamina:F0}]  ※ 차징 절반이면 둘 다 절반",
            GetRichLabelStyle(new Color(0.6f, 0.9f, 1f), 11));

        // 차징 타이머는 deltaTime에 MiningSpeed를 곱해 오르므로 체감 시간은 설정값 ÷ 채굴속도다.
        // 슬라이더 값만 보면 "1초로 뒀는데 왜 0.5초 만에 차지?"가 되므로 실측 시간을 같이 띄운다.
        float chargeSec = MiningStaminaTuning.SafeShovelMaxChargeTime;
        float miningSpeed = Mathf.Max(0.01f, _playerStat.GetFinalValue(StatType.MiningSpeed));
        float minRatio = MiningStaminaTuning.ShovelMinChargeRatio;

        GUILayout.Label(
            $"└ 풀차징까지 실제 <b>{chargeSec / miningSpeed:F2}초</b> (설정 {chargeSec:F2}초 ÷ 채굴속도 {miningSpeed:F2})" +
            $"   최소차징 {minRatio:F2} = <b>{chargeSec * minRatio / miningSpeed:F2}초</b> 미만은 스윙 취소",
            GetRichLabelStyle(new Color(0.6f, 0.9f, 1f), 11));
    }

    private void DrawKnobRow(FloatKnob k)
    {
        float value = k.get();
        string ctrlName = "knob_" + k.key;

        // 편집 중인 칸은 덮어쓰지 않는다 — "0." 같은 입력 중간 상태가 날아가면 타이핑이 막힌다.
        if (!_knobBuffers.TryGetValue(k.key, out string buf))
        {
            buf = value.ToString("0.###", CultureInfo.InvariantCulture);
            _knobBuffers[k.key] = buf;
        }
        else if (GUI.GetNameOfFocusedControl() != ctrlName)
        {
            if (!TryParse(buf, out float shown) || !Mathf.Approximately(shown, value))
            {
                buf = value.ToString("0.###", CultureInfo.InvariantCulture);
                _knobBuffers[k.key] = buf;
            }
        }

        GUILayout.BeginHorizontal();

        GUILayout.Label(k.label, GetRichLabelStyle(Color.white, 12), GUILayout.Width(120));

        GUI.SetNextControlName(ctrlName);
        string typed = GUILayout.TextField(buf, GUILayout.Width(58));
        if (typed != buf)
        {
            _knobBuffers[k.key] = typed;
            if (TryParse(typed, out float parsed))
            {
                SetKnob(k, parsed);
                value = parsed;
            }
        }

        // 슬라이더는 넘긴 값을 항상 [min,max]로 잘라서 돌려준다.
        // 미리 클램프한 값과 비교해야 숫자칸에 넣은 범위 밖 값이 매 프레임 잘려나가지 않는다.
        float clamped = Mathf.Clamp(value, k.min, k.max);
        float slid = GUILayout.HorizontalSlider(clamped, k.min, k.max, GUILayout.Width(140));
        if (!Mathf.Approximately(slid, clamped))
        {
            SetKnob(k, slid);
            _knobBuffers[k.key] = slid.ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (_knobSnapshot.TryGetValue(k.key, out float original))
        {
            bool modified = !Mathf.Approximately(original, value);

            GUILayout.Label(original.ToString("F2"),
                            GetRichLabelStyle(modified ? Color.green : Color.white, 12),
                            GUILayout.Width(55));

            GUI.enabled = modified;
            if (GUILayout.Button("↺", GUILayout.Width(26)))
            {
                SetKnob(k, original);
                _knobBuffers[k.key] = original.ToString("0.###", CultureInfo.InvariantCulture);
            }
            GUI.enabled = true;
        }

        GUILayout.EndHorizontal();
    }

    private static bool TryParse(string s, out float value)
    {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void DrawStatDiagnosticTable()
    {
        // 자체 스크롤을 두지 않는다 — 본문 전체가 스크롤이고, 길면 섹션째로 접으면 된다.
        // 중첩 스크롤은 휠이 어느 쪽에 먹는지 예측이 안 돼 오히려 쓰기 나빴다.
        GUILayout.BeginVertical("box");

        if (_playerStat == null)
        {
            GUILayout.Label("PlayerStat 컴포넌트를 로드하는 중입니다...", GetRichLabelStyle(Color.gray, 12));
            GUILayout.EndVertical();
            return;
        }

        // 테이블 헤더
        GUILayout.BeginHorizontal("box");
        GUILayout.Label("<b>스탯 분류 (StatType)</b>", GUILayout.Width(180));
        GUILayout.Label("<b>기본(Base)</b>", GUILayout.Width(75));
        GUILayout.Label("<b>최종(Final)</b>", GUILayout.Width(75));
        GUILayout.Label("<b>보정량 (Δ)</b>", GUILayout.Width(90));
        GUILayout.EndHorizontal();

        // 진단할 핵심 스탯 리스트 정의
        var diagnosticStats = new List<StatType>()
        {
            // === 채광 관련 ===
            StatType.MiningLevel,
            StatType.MiningSpeed,
            StatType.MiningRange,
            StatType.MiningCooldown,
            StatType.PickaxeDamageUp,
            StatType.ToolRange,
            StatType.ToolChargeTimeReduce,
            StatType.RareMineralChance,
            StatType.MineralExtraDropChance,
            StatType.RockMineralCountUp,
            StatType.ShovelStaminaReduce,
            StatType.PickaxeStaminaReduce,

            // === 이동 관련 ===
            StatType.MoveSpeed,
            StatType.JumpForce,
            StatType.WallClimbSpeed,
            StatType.FallDamageReduce,

            // === 스태미나 관련 ===
            StatType.MaxStamina,
            StatType.StaminaCostPerSecond,
            StatType.StaminaCostReduce,

            // === 드릴 관련 ===
            StatType.DrillBatteryCapacity,
            StatType.DrillBatteryRegen,
            StatType.DrillDrainReduce,
            StatType.DrillRadius,

            // === 전투 관련 ===
            StatType.Damage,
            StatType.CritChanceUp,
            StatType.Defense,

            // === 인벤토리/경제 관련 ===
            StatType.InventoryWeightUp,
            StatType.InventorySlotUp,
            StatType.WarehouseCapacityUp,
            StatType.ConsumableSlotUnlock,
            StatType.MineralSellBonus,

            // === 환경/탐색 관련 ===
            StatType.VisionRadiusUp,
            StatType.FlashlightRangeUp,
            StatType.EnvironmentResistance,
            StatType.HazardFrostResist,
            StatType.HazardBurnResist,
            StatType.HazardRadiationResist
        };

        foreach (StatType type in diagnosticStats)
        {
            float baseVal = _playerStat.GetBaseValue(type);
            float finalVal = _playerStat.GetFinalValue(type);
            float diff = finalVal - baseVal;

            GUILayout.BeginHorizontal();

            // 이름과 색상 스타일링
            string statName = type.ToString();
            Color statColor = diff > 0.001f ? Color.green : (diff < -0.001f ? Color.red : Color.white);
            if ((type == StatType.StaminaCostPerSecond || type == StatType.StaminaCostReduce) && diff < -0.001f)
            {
                // 스태미나 소모 속도는 줄어들수록 좋은 수치이므로 초록색 처리
                statColor = Color.green;
            }

            GUILayout.Label(statName, GetRichLabelStyle(statColor, 12), GUILayout.Width(180));
            GUILayout.Label(baseVal.ToString("F2"), GUILayout.Width(75));
            GUILayout.Label(finalVal.ToString("F2"), GUILayout.Width(75));

            // 보정량 표시
            string diffText = "0.00";
            if (diff > 0.001f) diffText = $"+{diff:F2}";
            else if (diff < -0.001f) diffText = $"{diff:F2}";

            GUILayout.Label(diffText, GetRichLabelStyle(statColor, 12, true), GUILayout.Width(90));

            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        GUILayout.EndVertical();
    }

    private void DrawQuickCheatPanel()
    {
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("🪙 +5,000 골드 추가", GUILayout.Height(30)))
        {
            if (_playerStat != null)
            {
                _playerStat.AddGold(5000);
                Debug.Log("[StatDiagnosticPanel] 디버그 치트: 골드 5,000 추가 완료.");
            }
        }

        if (GUILayout.Button("🔄 업그레이드 전면 초기화", GUILayout.Height(30)))
        {
            if (UpgradeManager.Instance != null)
            {
                var state = UpgradeManager.Instance.GetState();
                if (state != null)
                {
                    state.ClearNodes();   // 해금 목록 + 다단계 레벨
                    state.unlockedTiers.Clear();
                    state.unlockedTiers.Add(0); // 기본 티어 0만 해금

                    // 강제 동기화
                    _playerStat?.MarkDirty();
                    UpgradeManager.Instance.UnlockTier(0); // 이벤트 발화 트리거

                    Debug.Log("[StatDiagnosticPanel] 디버그 치트: 업그레이드 데이터 강제 전면 초기화 완료.");
                }
            }
        }

        GUILayout.EndHorizontal();
    }

    private GUIStyle GetRichLabelStyle(Color color, int fontSize, bool isBold = false)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.richText = true;
        style.fontSize = fontSize;
        style.normal.textColor = color;
        if (isBold)
        {
            style.fontStyle = FontStyle.Bold;
        }
        return style;
    }
}
