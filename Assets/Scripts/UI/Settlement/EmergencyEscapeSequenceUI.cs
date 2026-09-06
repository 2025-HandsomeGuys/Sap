// @tags: emergency, escape, gameover, cinematic, presentation, ui, code-generated, shake, vignette, alarm

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 긴급 탈출 '게임오버풍' 연출 — 지하에서 긴급탈출을 확정한 직후, 로딩씬으로 넘어가기 전에
/// 딱 한 번 재생하는 짧은 시네마틱.
/// 화면 흔들림(붕괴) + 붉은 경보 점멸 + 비네트 조임 + 천장 낙석 먼지 + 큼직한 "긴급 탈출" 타이틀 슬램
/// → 완전 암전 순서로 진행하고, 끝에서 <c>onComplete</c>(로딩씬 이동)을 호출한다.
///
/// 전부 코드로 생성한다(씬/프리팹 세팅 불필요, 다른 코드 오버레이와 같은 <see cref="CodeUI"/> 키트 사용).
/// <see cref="Play"/> 한 번이면 스스로 캔버스를 만들고 게임을 <c>timeScale=0</c>으로 얼린 뒤 unscaled 시간으로 연출한다.
/// 씬이 로드되면(비-DontDestroyOnLoad) 오브젝트가 함께 파괴돼 자동 정리된다.
///
/// 전체 흐름:
///   PauseOverlayUI.ExecuteEmergencyEscape
///     → SaveManager.MergeInventoriesToWarehouse (창고 병합 + 광물 60% 페널티, EmergencyEscapeReport 기록)
///     → EmergencyEscapeSequenceUI.Play (이 연출)
///     → SceneLoader.LoadScene (로딩씬)
///     → 지상 도착 → UIStateManager가 EmergencyEscapeReport.HasPending 감지 → EmergencyEscapeOverlayUI (정산창)
/// </summary>
public class EmergencyEscapeSequenceUI : MonoBehaviour
{
    // ===================================================
    // 정적 진입점
    // ===================================================
    private static EmergencyEscapeSequenceUI _current;

    /// <summary>연출이 재생 중인지 — UIStateManager가 재생 동안 전역 단축키(ESC/M/J/Tab 등)를 막는 데 쓴다.</summary>
    public static bool IsPlaying => _current != null;

    /// <summary>
    /// 게임오버풍 연출을 재생하고, 끝나면 <paramref name="onComplete"/>를 호출한다(보통 로딩씬 이동).
    /// 이미 재생 중이면 무시한다(확정 버튼 중복 클릭 방지).
    /// </summary>
    public static void Play(System.Action onComplete)
    {
        if (_current != null) return;

        if (SoundManager.Instance != null)
        {
            // 탈출 직전까지 스태미나가 낮았으면 심장 루프가 울리는 중이다
            SoundManager.Instance.StopLoop(HeartbeatSfx.LoopHandle, 0.1f);
            SoundManager.Instance.PlaySFX(SfxKeys.GameOver);
        }

        var go = new GameObject("EmergencyEscapeSequenceUI");
        _current = go.AddComponent<EmergencyEscapeSequenceUI>();
        _current.StartCoroutine(_current.Run(onComplete));
    }

    // ===================================================
    // 타이밍 / 스타일 (unscaled 초)
    // ===================================================
    private const float ImpactDur = 0.55f; // 초기 충격: 강한 흔들림 + 붉은 섬광
    private const float BuildDur  = 1.05f;  // 비네트/암전 상승 + 타이틀 슬램인
    private const float HoldDur   = 1.15f;  // 경보 점멸 + 텍스트 유지
    private const float FadeDur   = 0.55f;  // 전체 암전 → 로딩씬 핸드오프
    private const float SkipGrace = 0.40f;  // 이 시간 이후 아무 키/클릭으로 스킵

    // 설정(31000)·정산(30820) 위, 로딩씬 캔버스(32767)보단 아래 → 로딩씬이 자연스럽게 이어받는다.
    private const int SortingOrder = 32050;

    /// <summary>SoundDataSO에 이 이름의 SFX가 등록돼 있으면 시작 순간 재생(없으면 무음).</summary>
    private const string AlarmSfx = "emergency_escape";

    // ===================================================
    // 내부 상태 / 위젯 참조
    // ===================================================
    private readonly LocTextBinder _loc = new LocTextBinder();
    private bool _frozen;

    private RectTransform _shakeRoot;
    private Image _dark, _redFlash, _vignette, _finalBlack;
    private CanvasGroup _centerGroup, _subGroup, _debrisGroup;
    private RectTransform _center;
    private TextMeshProUGUI _alert;

    private RectTransform[] _debris;
    private Vector2[] _debrisVel;

    // 낙석 먼지가 노니는 논리 좌표 영역(1920x1080 기준 + 여유). ShakeRoot 중심 기준.
    private const float FieldHalfW = 1100f;
    private const float FieldHalfH = 650f;
    private const float FieldTopY  = 720f;

    private void OnDestroy()
    {
        // 연출 중 예기치 않게 파괴돼도(다른 경로의 씬 전환 등) 게임이 얼어붙지 않도록 복구.
        if (_frozen) Time.timeScale = 1f;
        if (_current == this) _current = null;
    }

    // ===================================================
    // 재생 루틴
    // ===================================================
    private IEnumerator Run(System.Action onComplete)
    {
        BuildUI();

        Time.timeScale = 0f; // 세계를 멈춰 붕괴 순간을 정지시킨다(연출은 unscaled 시간으로 진행).
        _frozen = true;

        CodeUI.PlaySfx(AlarmSfx);

        float t1 = ImpactDur;
        float t2 = t1 + BuildDur;
        float t3 = t2 + HoldDur;

        float elapsed = 0f;
        bool skip = false;

        while (elapsed < t3)
        {
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;

            UpdateShowVisuals(elapsed, t1, t2, t3);
            UpdateDebris(dt);
            ApplyShake(ShakeAmplitude(elapsed, t1, t2));

            if (elapsed > SkipGrace && Input.anyKeyDown) { skip = true; break; }
            yield return null;
        }

        // 완전 암전 (스킵 시 짧게)
        yield return FadeToBlack(skip ? 0.26f : FadeDur);

        // 암전을 잠깐 유지해 씬 전환 프레임 튐을 가린다
        float hold = 0f;
        while (hold < 0.06f) { hold += Time.unscaledDeltaTime; yield return null; }

        Time.timeScale = 1f; // 로딩씬은 정상 속도로 시작 (호출측도 1로 두지만 안전하게 재보장)
        _frozen = false;

        if (onComplete != null) onComplete();
        else Destroy(gameObject);
    }

    // ===================================================
    // 프레임별 비주얼
    // ===================================================
    private void UpdateShowVisuals(float e, float t1, float t2, float t3)
    {
        // 뒤 배경 어둡게 (붕괴 갱도가 붉게 물든 채 희미하게 비치도록 완전 검정까진 안 감)
        float dark = Mathf.SmoothStep(0f, 0.75f, Mathf.Clamp01(e / t2));
        SetAlpha(_dark, dark);

        // 붉은 섬광(초기 충격) + 경보 점멸(이후 지속)
        float spike = Mathf.Clamp01(1f - e / 0.45f) * 0.75f;
        float pulseWave = 0.5f + 0.5f * Mathf.Sin(Mathf.Max(0f, e - 0.30f) * 8.5f);
        float pulse = (e > 0.30f) ? (0.10f + 0.14f * pulseWave) : 0f;
        SetAlpha(_redFlash, Mathf.Max(spike, pulse));

        // 비네트 조임 (붉은 어둠이 가장자리에서 안으로)
        float vig = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / t2));
        SetAlpha(_vignette, vig);

        // 타이틀 슬램인 (살짝 크게 들어와 튕기며 자리 잡음)
        float tp = Mathf.Clamp01((e - 0.32f) / 0.42f);
        if (_centerGroup != null) _centerGroup.alpha = Mathf.Clamp01(tp * 1.4f);
        if (_center != null) _center.localScale = Vector3.one * Mathf.LerpUnclamped(1.35f, 1.0f, EaseOutBack(tp));

        // 부제는 조금 늦게 페이드인
        if (_subGroup != null) _subGroup.alpha = Mathf.Clamp01((e - 0.72f) / 0.40f);

        // 경고 라벨은 경보에 맞춰 점멸
        if (_alert != null)
        {
            var c = _alert.color;
            c.a = 0.55f + 0.45f * pulseWave;
            _alert.color = c;
        }

        // 낙석 먼지 페이드인
        if (_debrisGroup != null) _debrisGroup.alpha = Mathf.Clamp01(e / 0.40f);
    }

    private static float ShakeAmplitude(float e, float t1, float t2)
    {
        if (e < t1) return Mathf.Lerp(40f, 14f, e / t1);                 // 충격: 큰 진동
        if (e < t2) return Mathf.Lerp(14f, 5f, (e - t1) / BuildDur);     // 잦아듦
        return Mathf.Lerp(5f, 3f, (e - t2) / HoldDur);                  // 낮은 잔진동
    }

    private void ApplyShake(float amplitude)
    {
        if (_shakeRoot != null)
            _shakeRoot.anchoredPosition = Random.insideUnitCircle * amplitude;
    }

    private void UpdateDebris(float dt)
    {
        if (_debris == null) return;
        const float gravity = 560f;

        for (int i = 0; i < _debris.Length; i++)
        {
            var rt = _debris[i];
            if (rt == null) continue;

            Vector2 v = _debrisVel[i];
            v.y -= gravity * dt;
            Vector2 p = rt.anchoredPosition + v * dt;

            if (p.y < -FieldHalfH - 80f) // 바닥 밑으로 지나가면 천장에서 다시 떨어뜨린다
            {
                p = new Vector2(Random.Range(-FieldHalfW, FieldHalfW), FieldTopY + Random.Range(0f, 220f));
                v = new Vector2(Random.Range(-40f, 40f), Random.Range(-40f, -140f));
            }

            rt.anchoredPosition = p;
            rt.Rotate(0f, 0f, 180f * dt); // 굴러떨어지는 듯한 회전감
            _debrisVel[i] = v;
        }
    }

    private IEnumerator FadeToBlack(float dur)
    {
        float from = _finalBlack != null ? _finalBlack.color.a : 0f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            SetAlpha(_finalBlack, Mathf.Lerp(from, 1f, k));
            ApplyShake(Mathf.Lerp(3f, 0f, k)); // 흔들림도 함께 잦아들어 완전히 정지
            yield return null;
        }
        SetAlpha(_finalBlack, 1f);
        if (_shakeRoot != null) _shakeRoot.anchoredPosition = Vector2.zero;
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
        // 버튼은 없지만, 연출 중 뒤(일시정지 패널 등)로 클릭이 새지 않도록 레이캐스터로 입력을 삼킨다.
        // (스킵 자체는 Input 폴링으로 처리하므로 EventSystem 유무와 무관하게 동작한다)
        canvasObj.AddComponent<GraphicRaycaster>();
        CodeUI.EnsureEventSystem();

        Transform root = canvas.transform;

        // 흔들리는 컨테이너 — 화면보다 크게 오버스캔해 흔들려도 가장자리(빈틈)가 드러나지 않게.
        _shakeRoot = CodeUI.CreateRect(root, "ShakeRoot");
        _shakeRoot.anchorMin = Vector2.zero;
        _shakeRoot.anchorMax = Vector2.one;
        _shakeRoot.offsetMin = new Vector2(-140f, -140f);
        _shakeRoot.offsetMax = new Vector2(140f, 140f);

        _dark = MakeFullImage(_shakeRoot, "Dark", new Color(0f, 0f, 0f, 0f));
        _dark.raycastTarget = true; // 전체 화면 입력 차단막 (알파 0이어도 클릭은 흡수한다)

        _redFlash = MakeFullImage(_shakeRoot, "RedFlash", new Color(0.75f, 0.06f, 0.06f, 0f));

        _vignette = MakeFullImage(_shakeRoot, "Vignette", new Color(0.40f, 0.02f, 0.03f, 0f));
        _vignette.sprite = VignetteSprite();
        _vignette.type = Image.Type.Simple;

        BuildDebris(_shakeRoot);
        BuildCenter(_shakeRoot);

        // 최종 암전 — 흔들림 밖 최상단. 시작엔 투명, 마지막에 1로 올려 로딩씬으로 깔끔히 넘긴다.
        _finalBlack = MakeFullImage(root, "FinalBlack", new Color(0f, 0f, 0f, 0f));

        _loc.Refresh();
    }

    private static Image MakeFullImage(Transform parent, string name, Color color)
    {
        var img = CodeUI.CreateImage(parent, name, color, rounded: false);
        img.raycastTarget = false;
        CodeUI.StretchFull(img.rectTransform);
        return img;
    }

    private void BuildDebris(Transform parent)
    {
        var container = CodeUI.CreateRect(parent, "Debris");
        CodeUI.StretchFull(container);
        _debrisGroup = container.gameObject.AddComponent<CanvasGroup>();
        _debrisGroup.alpha = 0f;
        _debrisGroup.blocksRaycasts = false;
        _debrisGroup.interactable = false;

        const int count = 16;
        _debris = new RectTransform[count];
        _debrisVel = new Vector2[count];

        Sprite chunk = CodeUI.Rounded(8, 0.35f);
        var dust = new Color(0.42f, 0.35f, 0.29f, 0.65f);

        for (int i = 0; i < count; i++)
        {
            var img = CodeUI.CreateImage(container, "Chunk", dust, rounded: false);
            img.sprite = chunk;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;

            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float s = Random.Range(4f, 11f);
            rt.sizeDelta = new Vector2(s, s);
            rt.anchoredPosition = new Vector2(Random.Range(-FieldHalfW, FieldHalfW), Random.Range(-500f, FieldTopY));

            _debris[i] = rt;
            _debrisVel[i] = new Vector2(Random.Range(-40f, 40f), Random.Range(-40f, -160f));
        }
    }

    private void BuildCenter(Transform parent)
    {
        _center = CodeUI.CreateRect(parent, "Center");
        _center.anchorMin = _center.anchorMax = new Vector2(0.5f, 0.5f);
        _center.pivot = new Vector2(0.5f, 0.5f);
        _center.sizeDelta = new Vector2(1500f, 460f);
        _centerGroup = _center.gameObject.AddComponent<CanvasGroup>();
        _centerGroup.alpha = 0f;
        _centerGroup.blocksRaycasts = false;
        _centerGroup.interactable = false;

        var vlg = _center.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 16f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // 경고 라벨 (점멸)
        _alert = CodeUI.CreateText(_center, "Alert", 30f, FontStyles.Bold, new Color(0.94f, 0.28f, 0.24f),
            TextAlignmentOptions.Center, _loc);
        _alert.characterSpacing = 22f;
        _alert.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        _loc.Bind(_alert, "ui_escape_seq_alert", "경 고");

        // 타이틀
        var title = CodeUI.CreateText(_center, "Title", 96f, FontStyles.Bold, new Color(0.97f, 0.94f, 0.87f),
            TextAlignmentOptions.Center, _loc);
        title.characterSpacing = 8f;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 130f;
        _loc.Bind(title, "ui_escape_seq_title", "긴급 탈출");

        // 붉은 구분선
        var barRow = CodeUI.CreateRow(_center, "Bar", 18f, 0f, TextAnchor.MiddleCenter);
        var bar = CodeUI.CreateImage(barRow, "BarFill", new Color(0.85f, 0.20f, 0.18f, 0.9f), rounded: false);
        bar.raycastTarget = false;
        var barLe = bar.gameObject.AddComponent<LayoutElement>();
        barLe.preferredWidth = 360f;
        barLe.preferredHeight = 3f;

        // 부제 — 별도 CanvasGroup으로 늦게 페이드인
        var subHolder = CodeUI.CreateRect(_center, "SubHolder");
        _subGroup = subHolder.gameObject.AddComponent<CanvasGroup>();
        _subGroup.alpha = 0f;
        subHolder.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        var sub = CodeUI.CreateText(subHolder, "Sub", 26f, FontStyles.Normal, new Color(0.83f, 0.73f, 0.71f),
            TextAlignmentOptions.Center, _loc);
        CodeUI.StretchFull(sub.rectTransform);
        _loc.Bind(sub, "ui_escape_seq_sub", "캔 광물의 일부를 버리고 지상으로 대피합니다…");
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

    /// <summary>0→1 진행에서 1을 살짝 넘겼다 돌아오는 이징(슬램 후 튕김).</summary>
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float xm = x - 1f;
        return 1f + c3 * xm * xm * xm + c1 * xm * xm;
    }

    // 비네트용 방사형 스프라이트 (중심 투명 → 가장자리/모서리 불투명). 정적 캐시.
    private static Sprite _vignetteSprite;

    private static Sprite VignetteSprite()
    {
        if (_vignetteSprite != null) return _vignetteSprite;

        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var px = new Color32[size * size];
        float half = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.41421356f; // 0(중심) → 1(모서리)
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 1.0f, d));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);

        _vignetteSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _vignetteSprite;
    }
}
