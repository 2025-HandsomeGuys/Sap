// @tags: game-over, death, cinematic, presentation, ui, code-generated, spotlight, vignette, fade, emergency-escape, animator

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>이 연출을 부른 이유 — 끝난 뒤 무엇이 이어지는지가 달라진다.</summary>
public enum GameOverReason
{
    /// <summary>스태미나 고갈 등 지하 사망. 가방 광물·소비아이템을 잃고 지상으로 돌아간다.</summary>
    Death,

    /// <summary>일시정지 메뉴의 '긴급 탈출' 버튼. 지상 도착 후 탈출 정산창이 뜬다.</summary>
    EmergencyEscape,
}

/// <summary>
/// 게임오버 시네마틱 — 지하에서 판이 끝나는 모든 경로가 공유하는 단 하나의 연출.
/// 로딩씬으로 넘어가기 직전에 딱 한 번 재생한다.
///
/// 진행 순서:
///   1. 화면의 UI(HUD)가 페이드아웃으로 사라진다
///   2. 플레이어를 제외한 주변이 서서히 어두워진다 — 끝에는 **검은 배경에 플레이어만** 남는다
///      (플레이어보다 아래에 검은 장막을 깔아 배경만 실루엣 단위로 지운다. 원형 마스크로는 불가능)
///   3. 플레이어 죽는 모션(Animator "Die" 스테이트)이 **2와 동시에**(페이드인과 함께) 바로 시작된다
///   4. 완전 암전 → <c>onComplete</c>(보통 <see cref="SceneLoader.LoadScene"/>)
///   5. 로딩씬이 지상 씬을 띄운다
///   6. 긴급 탈출로 들어온 경우, 지상 도착 후 UIStateManager가
///      <see cref="EmergencyEscapeReport"/>.HasPending을 보고 정산창을 띄운다 (이미 구현되어 있음)
///
/// 전부 코드로 생성한다(씬/프리팹 세팅 불필요). 다른 코드 오버레이와 같은 <see cref="CodeUI"/> 키트를 쓴다.
/// 씬이 로드되면(비-DontDestroyOnLoad) 오브젝트가 함께 파괴돼 자동 정리된다.
///
/// 호출 지점:
///   • <see cref="GameOverHandler"/>          — PlayerStat.OnStaminaDepleted (사망)
///   • <c>PauseOverlayUI.ExecuteEmergencyEscape</c> — 긴급 탈출 버튼
/// </summary>
public class GameOverSequenceUI : MonoBehaviour
{
    // ===================================================
    // 정적 진입점
    // ===================================================
    private static GameOverSequenceUI _current;

    /// <summary>연출이 재생 중인지 — UIStateManager가 재생 동안 전역 단축키(ESC/M/J/Tab 등)를 막는 데 쓴다.</summary>
    public static bool IsPlaying => _current != null;

    /// <summary>플레이어를 자동으로 찾아 재생한다.</summary>
    public static void Play(GameOverReason reason, System.Action onComplete)
        => Play(reason, null, onComplete);

    /// <summary>
    /// 게임오버 연출을 재생하고, 끝나면 <paramref name="onComplete"/>를 호출한다(보통 로딩씬 이동).
    /// 이미 재생 중이면 무시한다(사망과 탈출이 겹치거나 버튼이 연타되는 경우 방지).
    /// </summary>
    /// <param name="player">죽는 모션을 재생할 플레이어 루트. null이면 PlayerStat으로 자동 탐색.</param>
    public static void Play(GameOverReason reason, Transform player, System.Action onComplete)
    {
        if (_current != null) return;

        var go = new GameObject("GameOverSequenceUI");
        _current = go.AddComponent<GameOverSequenceUI>();
        _current._reason = reason;
        _current._player = player != null ? player : FindPlayer();
        _current.StartCoroutine(_current.Run(onComplete));
    }

    private static Transform FindPlayer()
    {
        var stat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
        if (stat != null) return stat.transform;

        var tagged = GameObject.FindGameObjectWithTag("Player");
        return tagged != null ? tagged.transform : null;
    }

    // ===================================================
    // 타이밍 / 스타일 (unscaled 초)
    // ===================================================
    private const float HudFadeDur  = 0.75f; // 1. HUD 페이드아웃
    private const float DarkenDelay = 0.40f; // HUD가 사라지기 시작한 뒤 어둠이 깔리기 시작 (살짝 겹친다)
    private const float DarkenDur   = 2.40f; // 2. 주변이 서서히 어두워짐 — 이 연출의 중심, 가장 길게 끈다
    private const float DieDur      = 2.60f; // 3. Die 모션 (클립 실동작 ≈1.55초 + 여운. 3초에서 루프하므로 그 전에 끊는다)
    private const float BlackoutDur = 0.95f; // 4. 완전 암전
    private const float BlackHold   = 0.10f; // 암전 유지 — 씬 전환 프레임 튐을 가린다
    private const float SkipGrace   = 1.00f; // 이 시간 이후 아무 키/클릭으로 스킵

    // Die 클립을 얼릴 시점(재생 후 경과 초). Die.anim은 실동작이 ≈1.53초에 끝나고 3.0초까지
    // 같은 포즈를 유지하다 루프한다 → 그 정지 구간 안에서 speed=0으로 얼리면 티가 안 나면서
    // 암전 도중에 시체가 벌떡 일어나는 일이 없다. (클립이 더 짧으면 끝나기 직전에 얼린다)
    private const float DieFreezeAt = 2.00f;

    // 스포트라이트 구멍 크기.
    // 시작은 화면 높이 비율(넓어서 아무것도 안 가림 = 평소 화면 그대로),
    // 끝은 **플레이어 스프라이트의 실제 화면 크기 배수** — 화면 비율로 고정하면 카메라 줌·해상도에 따라
    // 구멍이 캐릭터보다 커져서 주변 지형이 그대로 보인다(실제로 그랬다).
    private const float SpotStartFrac      = 0.80f; // 시작: 화면을 거의 다 덮어 어둠이 안 보임
    private const float SpotEndScale       = 1.60f; // 끝: 플레이어 반경의 1.6배. 배경을 지우는 건 장막이
                                                    // 하므로 여기선 캐릭터가 절대 안 잘릴 만큼 넉넉히 둔다
    private const float SpotEndFracFallback = 0.14f; // 스프라이트를 못 찾았을 때만 쓰는 화면 비율
    private const float SpotDieShrink      = 0.92f; // Die 모션 동안 아주 조금 더 조여 긴장을 준다
    private const float MinHolePx          = 24f;   // 너무 좁아져 캐릭터가 사라지는 것 방지

    // 설정(31000)·정산(30820) 위, 로딩씬 캔버스(32767)보단 아래 → 로딩씬이 자연스럽게 이어받는다.
    private const int SortingOrder = 32050;

    /// <summary>
    /// 검은 장막의 정렬 순서. 플레이어 스프라이트는 이 위로 올라간다.
    /// 시야 어둠 캔버스(999)·손전등 메시(900)·광물 스파클(1000+)보다 확실히 위여야 전부 덮인다.
    /// 이 셋은 전부 Default 레이어라, 장막을 플레이어와 같은 레이어(지하=Default)에 order 이 값으로 깔면 덮인다.
    /// </summary>
    private const int VeilOrder = 20000;

    /// <summary>
    /// 소유자(PlayerSortingController)에게 걸 임시 order 부스트. 소유자가 매 프레임 (애니메이션이 쓴 order + 지하오프셋 + 이 값)으로
    /// 재적용하므로, 어떤 파츠의 기본 order가 음수 대역이어도 장막(VeilOrder) 위로 확실히 올라오도록 넉넉히 잡는다.
    /// </summary>
    private const int VeilPlayerBoost = VeilOrder + 2000;

    /// <summary>Animator에서 재생할 죽는 모션 스테이트 이름 (Base Layer).</summary>
    private const string DieState = "Die";

    /// <summary>SoundDataSO에 이 이름의 SFX가 등록돼 있으면 시작 순간 재생(없으면 무음).</summary>
    private const string DeathSfx = "game_over";
    private const string EscapeSfx = "emergency_escape";

    // ===================================================
    // 내부 상태 / 위젯 참조
    // ===================================================
    private GameOverReason _reason;
    private Transform _player;
    private Camera _cam;
    private Rigidbody2D _rb;

    // 검은 장막 — 플레이어보다 아래, 나머지 월드보다 위에 깔려 배경만 지운다
    private SpriteRenderer _veil;
    private int[] _origSortLayer;
    private int[] _origSortOrder;

    // 플레이어 sortingOrder의 단일 소유자(규칙 16). 있으면 이쪽 API로 플레이어를 장막 위로 올린다 —
    // 소유자를 끄면 Die 클립 재생 중 Animator가 Body/RightHand의 order를 매 프레임 기본값으로 되돌려
    // 플레이어가 다시 장막에 묻히기 때문이다. 소유자가 없을 때만(지상 등) 직접 정렬을 올린다.
    private PlayerSortingController _sorting;

    // Die 클립이 3초에서 루프해 시체가 벌떡 일어나는 걸 막으려고 정지 구간에서 얼린다
    private Animator _anim;
    private float _dieFreezeAt = DieFreezeAt;

    private RawImage _spot;   // 플레이어 중심 스포트라이트(구멍 뚫린 어둠)
    private Image _black;     // 최종 암전

    // 매 프레임 UpdateSpotlight가 읽어 uvRect/색으로 반영하는 구동값
    private float _closeK;   // 0 = 화면만큼 넓음(평소 화면) → 1 = 플레이어에 딱 붙음
    private float _dieK;     // 0 → 1 : Die 모션 동안 아주 조금 더 조임
    private float _darkness;

    private SpriteRenderer[] _playerRenderers;

    // HUD 페이드용 — 씬의 캔버스에 CanvasGroup을 붙여 알파를 내린다
    private readonly List<CanvasGroup> _hudGroups = new List<CanvasGroup>();
    private readonly List<float> _hudStartAlpha = new List<float>();

    private void OnDestroy()
    {
        if (_current == this) _current = null;
        RestorePlayerSorting(); // 씬 전환 없이 중간에 파괴돼도 플레이어가 최상위에 뜬 채 남지 않게
    }

    // ===================================================
    // 재생 루틴
    // ===================================================
    private IEnumerator Run(System.Action onComplete)
    {
        BuildUI();
        CollectHud();

        // 일시정지 메뉴(timeScale=0)에서 들어와도 Die 모션이 재생되도록 세계를 돌려 놓는다.
        // (연출 자체는 unscaled 시간으로 진행하므로 어디서 불려도 흐름은 같다)
        Time.timeScale = 1f;

        FreezePlayer();
        CodeUI.PlaySfx(_reason == GameOverReason.EmergencyEscape ? EscapeSfx : DeathSfx);

        _cam = Camera.main;
        BuildVeil();          // 장막은 알파 0으로 시작 — 이 순간 화면은 평소 그대로다
        RaisePlayerAboveVeil();
        Tick();

        bool skip = false;
        float elapsed = 0f; // 스킵 유예는 연출 전체 기준 — 단계가 바뀌어도 다시 잠기지 않는다

        // ─── 1 + 2 + 3. HUD 페이드아웃 + 주변이 어두워짐 + 죽는 모션을 함께 재생 ───
        // 죽는 모션은 어둠이 다 깔리길 기다리지 않고 페이드인과 동시에 바로 시작한다.
        PlayDieMotion();

        float span = Mathf.Max(HudFadeDur, DarkenDelay + DarkenDur);
        float total = Mathf.Max(span, DieDur); // 어둠과 죽는 모션 중 긴 쪽까지 돈다
        for (float t = 0f; t < total; t += Time.unscaledDeltaTime, elapsed += Time.unscaledDeltaTime)
        {
            SetHudAlpha(1f - Mathf.Clamp01(t / HudFadeDur));

            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - DarkenDelay) / DarkenDur));
            _darkness = k;
            _closeK = k;

            _dieK = Mathf.Clamp01(t / DieDur);
            FreezeDieMotionIfDone(t); // 죽는 모션이 끝나면 정지 구간에서 얼려 루프(벌떡 일어남)를 막는다

            Tick();
            if (elapsed > SkipGrace && Input.anyKeyDown) { skip = true; break; }
            yield return null;
        }

        SetHudAlpha(0f);
        _darkness = 1f;
        _closeK = 1f;

        // ─── 4. 완전 암전 ───────────────────────────────────────────────
        // 암전에도 시간이 걸리므로 그 사이에 클립이 루프하지 않도록 먼저 확실히 얼린다.
        FreezeDieMotionIfDone(float.MaxValue);
        yield return Blackout(skip ? 0.28f : BlackoutDur);

        for (float t = 0f; t < BlackHold; t += Time.unscaledDeltaTime)
            yield return null;

        // ─── 5. 로딩씬 → 지상 (6. 긴급 탈출이면 지상에서 정산창이 뜬다) ────
        if (onComplete != null) onComplete();
        else Destroy(gameObject);
    }

    /// <summary>매 프레임 공통 처리 — 카메라가 움직여도 스포트라이트가 플레이어를 따라간다.</summary>
    private void Tick()
    {
        // 조작이 꺼진 뒤에도 관성으로 미끄러지지 않도록 가로 속도만 죽인다(중력은 그대로 → 바닥에 눕는다).
        if (_rb != null) _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        UpdateVeil();
        UpdateSpotlight();
    }

    private IEnumerator Blackout(float dur)
    {
        float from = _black != null ? _black.color.a : 0f;
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            SetAlpha(_black, Mathf.Lerp(from, 1f, Mathf.Clamp01(t / dur)));
            Tick();
            yield return null;
        }
        SetAlpha(_black, 1f);
    }

    // ===================================================
    // 1. HUD 페이드아웃
    // ===================================================
    /// <summary>
    /// 씬에 떠 있는 모든 루트 캔버스를 모아 페이드 대상으로 삼는다(인스펙터 연결 불필요).
    /// 자기 연출 캔버스와 <see cref="PlayerVisionOverlay"/>의 캔버스는 제외한다 —
    /// 시야 오버레이는 HUD가 아니라 게임 화면의 일부이고, 어차피 검은 장막 아래라 같이 덮인다.
    /// </summary>
    private void CollectHud()
    {
        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            if (c == null) continue;
            if (c.rootCanvas != c) continue;                 // 중첩 캔버스는 루트만 다루면 된다
            if (c.transform.IsChildOf(transform)) continue;  // 내 연출 캔버스
            if (c.GetComponentInParent<PlayerVisionOverlay>() != null) continue;

            var cg = c.GetComponent<CanvasGroup>();
            if (cg == null) cg = c.gameObject.AddComponent<CanvasGroup>();

            _hudGroups.Add(cg);
            _hudStartAlpha.Add(cg.alpha);
        }
    }

    private void SetHudAlpha(float k)
    {
        for (int i = 0; i < _hudGroups.Count; i++)
        {
            var cg = _hudGroups[i];
            if (cg == null) continue;

            cg.alpha = _hudStartAlpha[i] * k;
            if (k <= 0f)
            {
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
        }
    }

    // ===================================================
    // 2-a. 검은 장막 — "검은 배경에 플레이어만"을 만드는 핵심
    // ===================================================
    /// <summary>
    /// 원형 마스크로는 절대 "플레이어만 남기기"가 안 된다 — 구멍을 아무리 좁혀도 **구멍 안에는 배경이 보인다**.
    /// 그래서 가리는 단위를 원이 아니라 **그리기 순서**로 바꾼다.
    ///
    ///   월드(지형·광물·시야 오버레이·손전등)  →  [검은 장막]  →  플레이어 스프라이트
    ///
    /// 장막을 플레이어보다 아래·나머지 전부보다 위에 깔고 알파를 올리면
    /// 배경만 정확히 실루엣 단위로 지워지고 플레이어는 그대로 남는다.
    /// 광물 스파클이 어둠 캔버스(order 999) 위로 올라가는 것과 같은 방식이다.
    ///
    /// 부수 효과로 시야 오버레이·손전등을 따로 건드릴 필요가 없어진다 — 전부 장막 아래라 같이 덮인다.
    /// </summary>
    private void BuildVeil()
    {
        if (_playerRenderers == null && _player != null)
            _playerRenderers = _player.GetComponentsInChildren<SpriteRenderer>(true);

        _sorting = _player != null ? _player.GetComponentInChildren<PlayerSortingController>() : null;

        var go = new GameObject("Veil");
        go.transform.SetParent(transform, false);

        _veil = go.AddComponent<SpriteRenderer>();
        _veil.sprite = WhitePixelSprite();
        _veil.color = new Color(0f, 0f, 0f, 0f); // 시작은 투명 — 화면은 평소 그대로

        // 소유자가 있으면 장막을 플레이어와 같은 정렬 레이어에 두고 order로 앞뒤를 가른다.
        // 다른 레이어(최상위)에 두면 소유자가 매 프레임 플레이어를 Default 레이어로 되돌려
        // 레이어 우선순위 때문에 플레이어가 장막에 통째로 묻힌다. 소유자가 없을 때만(지상 등)
        // 최상위 레이어에 깔고 플레이어를 직접 올린다.
        _veil.sortingLayerID = PlayerActiveSortingLayer();
        _veil.sortingOrder = VeilOrder;
    }

    /// <summary>
    /// 몸통 스프라이트가 현재 놓인 정렬 레이어. 소유자(PlayerSortingController)가 지하에선 Default,
    /// 지상에선 원래 레이어로 몸통을 한 레이어에 모아둔다 — 장막을 같은 레이어에 깔아야 order로 겨룰 수 있다.
    ///
    /// 첫 렌더러나 최소 order로 고르면 틀린다: EncumbranceIcon은 항상 Default/0, Effect(곡괭이 이펙트)는
    /// player 레이어/1000이라 몸통과 다른 레이어에 산다. 그래서 **order&lt;100 렌더러 중 가장 많이 쓰인
    /// 레이어**(=몸통 레이어, 소유자가 boost로 올리는 그 파츠들)를 고른다. 소유자가 없거나 못 고르면 최상위 폴백.
    /// </summary>
    private int PlayerActiveSortingLayer()
    {
        if (_sorting == null || _playerRenderers == null) return TopSortingLayer();

        int bestId = 0, bestCount = 0;
        foreach (var sr in _playerRenderers)
        {
            if (sr == null || sr.sortingOrder >= 100) continue; // Effect 등 상시-상단 렌더러 제외
            int id = sr.sortingLayerID;
            int count = 0;
            foreach (var o in _playerRenderers)
                if (o != null && o.sortingOrder < 100 && o.sortingLayerID == id) count++;
            if (count > bestCount) { bestCount = count; bestId = id; }
        }

        return bestCount > 0 ? bestId : TopSortingLayer();
    }

    /// <summary>
    /// 플레이어 스프라이트를 장막 위로 올린다.
    ///
    /// 소유자(PlayerSortingController)가 있으면 <see cref="PlayerSortingController.SetExtraOrderBoost"/>로 맡긴다 —
    /// 소유자가 LateUpdate에서 (애니메이션이 쓴 order + 지하오프셋 + 부스트)를 매 프레임 재적용하므로,
    /// Die 클립 재생 중 Animator가 Body/RightHand의 order를 기본값으로 되돌려도 다시 장막 위로 끌어올려준다.
    /// 직접 order를 만지면(구 방식) 그 writeback에 밀려 플레이어가 장막 뒤로 사라진다(규칙 16).
    ///
    /// 소유자가 없을 때만(지상 등 정렬 소유자가 없는 상황) 예전처럼 최상위 레이어로 직접 올린다.
    /// 파츠 간 앞뒤(order 차)와 레이어 구분은 보존한다.
    /// </summary>
    private void RaisePlayerAboveVeil()
    {
        if (_sorting != null)
        {
            _sorting.SetExtraOrderBoost(VeilPlayerBoost);
            return;
        }

        if (_playerRenderers == null) return;

        int top = TopSortingLayer();
        _origSortLayer = new int[_playerRenderers.Length];
        _origSortOrder = new int[_playerRenderers.Length];

        for (int i = 0; i < _playerRenderers.Length; i++)
        {
            var sr = _playerRenderers[i];
            if (sr == null) continue;

            _origSortLayer[i] = sr.sortingLayerID;
            _origSortOrder[i] = sr.sortingOrder;

            int layerValue = SortingLayer.GetLayerValueFromID(sr.sortingLayerID);
            sr.sortingLayerID = top;
            sr.sortingOrder = VeilOrder + 1 + layerValue * 100 + sr.sortingOrder;
        }
    }

    /// <summary>올려 둔 정렬을 되돌린다. 정상 흐름(씬 전환)에선 의미 없지만, 연출이 중간에 파괴되는 경우 대비.</summary>
    private void RestorePlayerSorting()
    {
        if (_sorting != null)
        {
            _sorting.SetExtraOrderBoost(0);
            _sorting = null;
            return;
        }

        if (_playerRenderers == null || _origSortOrder == null) return;

        for (int i = 0; i < _playerRenderers.Length; i++)
        {
            var sr = _playerRenderers[i];
            if (sr == null) continue;
            sr.sortingLayerID = _origSortLayer[i];
            sr.sortingOrder = _origSortOrder[i];
        }
        _origSortOrder = null;
        _origSortLayer = null;
    }

    /// <summary>장막을 카메라 화면 전체에 맞춘다(카메라가 움직이거나 줌이 바뀌어도 빈틈 없게 넉넉히).</summary>
    private void UpdateVeil()
    {
        if (_veil == null) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        var t = _veil.transform;
        Vector3 c = _cam.transform.position;
        t.position = new Vector3(c.x, c.y, 0f);

        float halfH = _cam.orthographic ? _cam.orthographicSize : 20f;
        float halfW = halfH * Mathf.Max(0.1f, _cam.aspect);
        t.localScale = new Vector3(halfW * 2.6f, halfH * 2.6f, 1f); // 여유 있게 오버스캔

        _veil.color = new Color(0f, 0f, 0f, Mathf.Clamp01(_darkness));
    }

    private static int TopSortingLayer()
    {
        var layers = SortingLayer.layers;
        return (layers != null && layers.Length > 0) ? layers[layers.Length - 1].id : 0;
    }

    private static Sprite _whitePixel;

    private static Sprite WhitePixelSprite()
    {
        if (_whitePixel != null) return _whitePixel;

        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply(false, true);

        // pixelsPerUnit = 1 → localScale이 곧 월드 크기가 된다
        _whitePixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return _whitePixel;
    }

    // ===================================================
    // 2. 플레이어 중심 스포트라이트
    // ===================================================
    /// <summary>
    /// 화면 전체를 덮은 RawImage의 <c>uvRect</c>만 갱신해 '구멍 뚫린 어둠'을 움직인다.
    /// 텍스처는 wrapMode=Clamp + 가장자리 알파 1이라 uv가 [0,1] 밖으로 나가면 그대로 불투명 —
    /// 플레이어가 화면 어디에 있든 구멍 바깥은 빈틈없이 덮인다(스케일·리사이즈 대응 불필요).
    /// </summary>
    private void UpdateSpotlight()
    {
        if (_spot == null) return;

        float w = Screen.width;
        float h = Screen.height;
        if (w < 1f || h < 1f) return;

        // 플레이어의 화면 위치(0~1)와 화면상 크기(px).
        Vector2 n = new Vector2(0.5f, 0.5f);
        float playerRadiusPx = -1f;
        MeasurePlayerOnScreen(w, h, ref n, ref playerRadiusPx);

        // 넓게 시작(=평소 화면 그대로) → 플레이어에 딱 붙는 크기까지 조인다.
        float startPx = SpotStartFrac * h;
        float endPx = (playerRadiusPx > 0f)
            ? playerRadiusPx * SpotEndScale         // 실제 스프라이트 크기 기준 (줌·해상도 무관)
            : SpotEndFracFallback * h;              // 스프라이트를 못 찾았을 때만 화면 비율
        endPx = Mathf.Clamp(endPx, MinHolePx, startPx);

        float holePx = Mathf.Lerp(startPx, endPx, _closeK);
        holePx *= Mathf.Lerp(1f, SpotDieShrink, _dieK);
        holePx = Mathf.Max(1f, holePx);

        // 구멍 반경(px) → uvRect 크기. x/y의 '픽셀당 uv'가 같아야 타원이 아닌 원이 된다.
        float uw = HoleUV * w / holePx;
        float uh = HoleUV * h / holePx;

        _spot.uvRect = new Rect(0.5f - n.x * uw, 0.5f - n.y * uh, uw, uh);
        _spot.color = new Color(0f, 0f, 0f, Mathf.Clamp01(_darkness));
    }

    /// <summary>
    /// 플레이어가 화면 어디에 얼마만한 크기로 보이는지 잰다.
    ///
    /// <c>transform.position</c>을 그대로 쓰면 안 된다 — 2D 캐릭터의 피벗은 보통 발밑이라
    /// 구멍을 좁히는 순간 머리가 잘린다. 그래서 **스프라이트들의 실제 월드 바운즈**를 쓴다.
    /// Die 모션으로 눕는 동안 바운즈가 같이 변하므로 매 프레임 다시 잰다.
    ///
    /// 반경도 이 바운즈에서 뽑는다 — 화면 높이 비율로 고정하면 카메라 줌이나 해상도가 달라졌을 때
    /// 구멍이 캐릭터보다 커져서 "플레이어 빼고 전부 까맣게"가 안 된다.
    /// </summary>
    private void MeasurePlayerOnScreen(float w, float h, ref Vector2 n, ref float radiusPx)
    {
        if (_player == null) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        if (_playerRenderers == null)
            _playerRenderers = _player.GetComponentsInChildren<SpriteRenderer>(true);

        Bounds b = default;
        bool has = false;
        foreach (var sr in _playerRenderers)
        {
            if (sr == null || !sr.enabled || sr.sprite == null) continue;
            if (has) b.Encapsulate(sr.bounds);
            else { b = sr.bounds; has = true; }
        }

        Vector3 center = has ? b.center : _player.position;
        Vector3 sp = _cam.WorldToScreenPoint(center);
        if (sp.z <= 0f) return;

        n = new Vector2(sp.x / w, sp.y / h);

        if (!has) return;

        // 바운즈 모서리를 같이 투영해 픽셀 반경을 구한다(직교·원근 모두 동작).
        Vector3 corner = _cam.WorldToScreenPoint(center + new Vector3(b.extents.x, b.extents.y, 0f));
        radiusPx = Vector2.Distance(new Vector2(sp.x, sp.y), new Vector2(corner.x, corner.y));
    }

    // ===================================================
    // 3. 죽는 모션
    // ===================================================
    /// <summary>
    /// Animator를 "Die" 스테이트로 강제 전환한다.
    /// Base Layer에는 <c>isGrounded == false</c> 조건의 AnyState 전이가 있어서, 공중에서 죽으면
    /// Die가 바로 낙하 모션에 끊긴다. 그래서 모든 파라미터를 중립값으로 리셋하고 isGrounded만 true로 고정한다.
    /// Leg Layer(마스크 레이어)도 가중치를 0으로 내려 다리가 Die 포즈를 덮어쓰지 않게 한다.
    /// </summary>
    private void PlayDieMotion()
    {
        if (_player == null) return;

        var anim = _player.GetComponentInChildren<Animator>();
        if (anim == null || anim.runtimeAnimatorController == null) return;

        if (!anim.HasState(0, Animator.StringToHash(DieState)))
        {
            Debug.LogWarning($"[GameOverSequenceUI] Animator에 '{DieState}' 스테이트가 없어 죽는 모션을 건너뜁니다.");
            return;
        }

        foreach (var p in anim.parameters)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Trigger: anim.ResetTrigger(p.nameHash); break;
                case AnimatorControllerParameterType.Bool:    anim.SetBool(p.nameHash, false); break;
                case AnimatorControllerParameterType.Int:     anim.SetInteger(p.nameHash, 0); break;
                case AnimatorControllerParameterType.Float:   anim.SetFloat(p.nameHash, 0f); break;
            }

            // 공중 판정 AnyState 전이가 Die를 끊지 않도록 '땅에 있는' 상태로 고정.
            if (p.type == AnimatorControllerParameterType.Bool && p.name == "isGrounded")
                anim.SetBool(p.nameHash, true);
        }

        for (int i = 1; i < anim.layerCount; i++)
            anim.SetLayerWeight(i, 0f);

        anim.Play(DieState, 0, 0f);
        anim.Update(0f); // 다음 프레임까지 기다리지 않고 즉시 첫 포즈를 반영

        // 클립 길이를 읽어 '루프 직전'을 얼릴 시점으로 잡는다.
        // Die.anim(3초)은 1.53초 뒤가 정지 구간이라 DieFreezeAt(2초)에 얼려도 티가 안 난다.
        _anim = anim;
        float len = anim.GetCurrentAnimatorStateInfo(0).length;
        _dieFreezeAt = (len > 0.1f) ? Mathf.Min(DieFreezeAt, len - 0.05f) : DieFreezeAt;
    }

    /// <summary>
    /// Die 클립은 루프 설정이 켜져 있어서 그냥 두면 3초에 처음(선 자세)으로 되돌아간다.
    /// 암전이 끝나기 전에 그 순간이 오면 시체가 벌떡 일어난다 — 그래서 정지 구간에서 speed를 0으로 세운다.
    /// (Animator를 끄지 않고 speed만 0으로 둬 마지막 포즈가 매 프레임 계속 적용되게 한다)
    /// </summary>
    private void FreezeDieMotionIfDone(float t)
    {
        if (_anim == null || t < _dieFreezeAt) return;

        _anim.speed = 0f;
        _anim = null;
    }

    /// <summary>
    /// 조작·채굴·낙하 처리를 끄고 시체가 스스로 움직이지 않게 한다.
    /// 2D IK가 켜져 있으면 LateUpdate에서 다리 본을 타깃으로 되돌려 Die 포즈가 어긋나므로 함께 끈다
    /// (패키지 어셈블리를 참조하지 않으려고 타입 이름으로 찾는다 — 없으면 아무 일도 하지 않는다).
    /// </summary>
    private void FreezePlayer()
    {
        if (_player == null) return;

        Disable(_player.GetComponentInChildren<PlayerController>());
        Disable(_player.GetComponentInChildren<PlayerMining>());
        Disable(_player.GetComponentInChildren<PlayerInputHandler>());
        Disable(_player.GetComponentInChildren<AntiGravityHandler>());
        // 손전등·시야 오버레이는 건드리지 않는다 — 둘 다 검은 장막 아래라 알아서 같이 덮인다.

        foreach (var b in _player.GetComponentsInChildren<Behaviour>(true))
            if (b != null && b.GetType().Name == "IKManager2D") b.enabled = false;

        _rb = _player.GetComponentInChildren<Rigidbody2D>();
    }

    private static void Disable(Behaviour b)
    {
        if (b != null) b.enabled = false;
    }

    // ===================================================
    // UI 생성
    // ===================================================
    private void BuildUI()
    {
        var canvasObj = new GameObject("Canvas");
        canvasObj.transform.SetParent(transform, false);
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        // 버튼은 없지만, 연출 중 뒤(HUD·일시정지 패널 등)로 클릭이 새지 않도록 레이캐스터로 입력을 삼킨다.
        // (스킵 자체는 Input 폴링으로 처리하므로 EventSystem 유무와 무관하게 동작한다)
        canvasObj.AddComponent<GraphicRaycaster>();
        CodeUI.EnsureEventSystem();

        Transform root = canvas.transform;

        var spotObj = new GameObject("Spotlight");
        spotObj.transform.SetParent(root, false);
        _spot = spotObj.AddComponent<RawImage>();
        _spot.texture = SpotlightTexture();
        _spot.color = new Color(0f, 0f, 0f, 0f);
        _spot.raycastTarget = true; // 전체 화면 입력 차단막 (알파 0이어도 클릭은 흡수한다)
        CodeUI.StretchFull(_spot.rectTransform);

        _black = CodeUI.CreateImage(root, "Blackout", new Color(0f, 0f, 0f, 0f), rounded: false);
        _black.raycastTarget = false;
        CodeUI.StretchFull(_black.rectTransform);
    }

    // ===================================================
    // 헬퍼
    // ===================================================
    private static void SetAlpha(Graphic g, float a)
    {
        if (g == null) return;
        var c = g.color;
        c.a = a;
        g.color = c;
    }

    // 스포트라이트 텍스처 — 가운데가 뚫린 방사형 알파 마스크. 정적 캐시(한 번만 굽는다).
    // 알파는 uv 반경 기준: HoleUV까지 0(구멍) → EdgeUV부터 1(완전 불투명).
    // 텍스처 가장자리(반경 0.5~0.707)는 전부 1이라 Clamp 샘플링으로 화면 밖까지 안전하게 덮인다.
    //
    // **EdgeUV/HoleUV 비가 곧 '완전 검정이 시작되는 거리 ÷ 구멍 반경'이다.**
    // 이 값이 3.3배였을 땐 구멍을 아무리 좁혀도 플레이어 반경 3.3배까지 지형이 비쳐서
    // 주변이 훤히 보였다. 1.5배면 구멍 바로 바깥부터 검정으로 떨어진다.
    private const int TexSize = 256;
    private const float HoleUV = 0.13f;
    private const float EdgeUV = 0.20f;

    private static Texture2D _spotTexture;

    private static Texture2D SpotlightTexture()
    {
        if (_spotTexture != null) return _spotTexture;

        var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var px = new Color32[TexSize * TexSize];
        for (int y = 0; y < TexSize; y++)
        {
            for (int x = 0; x < TexSize; x++)
            {
                float u = (x + 0.5f) / TexSize - 0.5f;
                float v = (y + 0.5f) / TexSize - 0.5f;
                float d = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HoleUV, EdgeUV, d));
                px[y * TexSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, true);

        _spotTexture = tex;
        return tex;
    }
}
