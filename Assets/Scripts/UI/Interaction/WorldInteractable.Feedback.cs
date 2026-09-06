// @tags: interaction, feedback, indicator, exclamation, prompt-label, highlight, unified, world
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// <see cref="WorldInteractable"/> 근접 피드백 통합(partial). 구 컴포넌트 3종을 흡수했다:
///  - 느낌표(<c>InteractionIndicator</c>) — 근접 + 가능 상태에서 머리 위로 팝하고 둥실거린다.
///  - 문구 라벨(<c>InteractionPromptLabel</c>) — 근접 시 안내 문구를 월드 스페이스로 띄운다.
///  - 강조(<c>InteractableHighlight</c>) — 근접 시 살짝 확대 + 테두리 오브젝트 토글.
///
/// 셋 다 <b>코드로 생성</b>하므로 씬/에셋 세팅이 필요 없다. 무엇을 띄울지(문구·가능여부)는
/// 이 컴포넌트가 이미 구현한 <see cref="WorldInteractable.GetInteractionPrompt"/> /
/// <see cref="WorldInteractable.IsInteractionAvailable"/>에서 직접 읽는다.
///
/// 근접 판정: 이 오브젝트에 트리거 콜라이더가 있으면 <c>OnTriggerEnter/Exit</c>(정확),
/// 없으면 플레이어와의 거리(<see cref="feedbackRadius"/>)로 판정한다(엘리베이터처럼 큰 콜라이더용).
/// </summary>
public partial class WorldInteractable
{
    // ─────────────────────────── 피드백 설정 ───────────────────────────

    [Header("피드백 — 느낌표")]
    [Tooltip("근접 + '실제로 할 일 있음'일 때 머리 위 느낌표를 띄운다(게시판 서브퀘스트·저녁 침대·오늘 코인 판·팔 광물 등)")]
    [SerializeField] private bool showIndicator = true;
    [Tooltip("거리 제한 없이 상시로 느낌표를 띄운다(근접 판정 무시, 할 일이 있을 때). 멀리서도 위치 파악용")]
    [SerializeField] private bool indicatorAlwaysVisible = false;
    [Tooltip("비워두면 코드로 생성한 기본 느낌표를 사용")]
    [SerializeField] private Sprite indicatorSprite;
    [SerializeField] private Color indicatorFill = new Color(1f, 0.83f, 0.25f);
    [SerializeField] private Color indicatorOutline = new Color(0.13f, 0.11f, 0.2f);
    [Tooltip("느낌표 크기 배율(기본 스프라이트 높이 약 0.72유닛)")]
    [SerializeField] private float indicatorScale = 1f;
    [SerializeField] private Vector2 indicatorOffset = Vector2.zero;
    [SerializeField] private int indicatorSortingOffset = 50;

    [Header("피드백 — 근접 선택지 목록")]
    [Tooltip("근접 시 오브젝트 위에 '지금 할 수 있는 일' 목록을 띄운다([F] + 이름, 어두운 반투명 패널). 불가 사유는 붉게")]
    [SerializeField] private bool showPromptLabel = true;
    [Tooltip("평소엔 문구를 숨기고, 상호작용한 순간에만 잠깐 띄운다")]
    [SerializeField] private bool promptLabelOnInteractOnly = false;
    [Tooltip("상호작용 시 문구를 띄워둘 시간(초). 위 옵션이 켜져 있을 때만 사용")]
    [SerializeField] private float promptLabelFlashDuration = 1.2f;
    [Tooltip("폰트 크기(픽셀 기준, 100px = 1유닛)")]
    [SerializeField] private float labelFontSize = 26f;
    [SerializeField] private float labelScale = 1f;
    [SerializeField] private Vector2 labelOffset = Vector2.zero;
    [Tooltip("지금 가리키는 줄 뒤에만 배경 판을 깐다(나머지 줄은 투명). 끄면 판 없이 글자·아이콘만 뜬다")]
    [SerializeField] private bool labelBackdrop = true;
    [Tooltip("배경으로 쓸 스프라이트(9-슬라이스 권장). 비우면 코드가 그리는 단색 반투명 판")]
    [SerializeField] private Sprite labelBackdropSprite;
    [Tooltip("배경 스프라이트 틴트. 기본값은 코드 생성 오버레이(창고·설정·도감)의 카드 배경(#18223C)과 같은 톤 —\n" +
             "흰 9-슬라이스 도트를 넣으면 다른 UI와 한 벌로 보인다. 이미 색이 입혀진 그림이면 흰색으로 되돌린다")]
    [SerializeField] private Color labelBackdropSpriteColor = new Color(0.094f, 0.133f, 0.235f, 0.92f);
    [Tooltip("배경 스프라이트의 Pixels Per Unit 배율(Image의 Pixels Per Unit Multiplier와 같은 값).\n" +
             "9-슬라이스 테두리가 너무 두껍게/얇게 나올 때 조절한다 — 키우면 테두리가 얇아진다")]
    [SerializeField] private float labelBackdropPixelsPerUnitMultiplier = 1f;
    // RectOffset을 SerializeField 초기화에 쓰면 그 아래 필드 초기화가 통째로 끊긴다(프로젝트 기존 함정).
    // 그래서 여백은 Vector4(x=왼쪽, y=위, z=오른쪽, w=아래)로 받는다.
    [Tooltip("줄 하나의 배경 판 안쪽 여백(px). x=왼 y=위 z=오른 w=아래. 테두리가 두꺼운 그림이면 키운다.\n" +
             "판이 꺼진 줄도 같은 여백을 쓰므로 선택이 바뀌어도 줄이 밀리지 않는다")]
    [SerializeField] private Vector4 labelBackdropPadding = new Vector4(16f, 10f, 18f, 10f);
    [SerializeField] private int labelSortingOffset = 60;

    [Header("피드백 — 선택 줄의 키 아이콘([F])")]
    [Tooltip("비워두면 Resources/UI/InteractPrompt/의 공용 키 아이콘을 쓴다. 이 오브젝트만 다른 그림을 쓰고 싶을 때만 채운다")]
    [SerializeField] private Sprite promptSprite;
    [Tooltip("채워두면 위 스프라이트와 번갈아 표시한다(키 눌림 ↔ 기본 = 딸깍이는 느낌). 비워두면 정지 이미지")]
    [SerializeField] private Sprite promptSpriteAlt;
    [Tooltip("두 스프라이트를 바꾸는 간격(초)")]
    [SerializeField] private float promptSpriteFrameInterval = 0.45f;
    [Tooltip("조건 불충족(불가) 상태에서 쓸 스프라이트. 비워두면 아이콘 없이 문구만 띄운다(권장) — " +
             "못 하는 일에 키를 보여주면 눌러도 되는 것처럼 읽힌다")]
    [SerializeField] private Sprite promptSpriteBlocked;
    [Tooltip("키 아이콘 크기 배율(줄 글자 크기 기준). 1이면 글자보다 살짝 큰 기본 크기")]
    [SerializeField] private float promptSpriteScale = 1f;
    [Tooltip("키 아이콘 위치 미세 조정(px). 글자와 높이가 안 맞을 때만 건드린다")]
    [SerializeField] private Vector2 promptSpriteOffset = Vector2.zero;

    [Header("피드백 — 강조(스케일/테두리)")]
    [Tooltip("근접 시 오브젝트를 살짝 확대한다")]
    [SerializeField] private bool highlightScaleOnNearby = false;
    [SerializeField] private float hoverScaleMultiplier = 1.05f;
    [SerializeField] private float hoverScaleSpeed = 10f;
    [Tooltip("근접 시 켤 테두리 오브젝트(뒤에 깔아둔 것). 없으면 비워둔다")]
    [SerializeField] private GameObject outlineObject;

    [Header("피드백 — 감지")]
    [Tooltip("트리거 콜라이더가 없을 때 근접 판정 반경(유닛)")]
    [SerializeField] private float feedbackRadius = 2.5f;
    [Tooltip("점프로 트리거 '위로' 빠져나갔을 때 봐주는 세로 여유(유닛). 가로는 트리거 폭 그대로다 —\n" +
             "옆으로 걸어 나가면 기존처럼 트리거를 벗어나는 순간 꺼진다. 0이면 트리거 그대로")]
    [SerializeField] private float feedbackTriggerGrace = 1.2f;
    [Tooltip("상점·설정 등 전체 UI가 열려 있는 동안 문구·느낌표를 숨긴다")]
    [SerializeField] private bool hideFeedbackWhileUIOpen = true;

    // ─────────────────────────── 내부 상태 ───────────────────────────

    // ─────────────────── 공용 키 아이콘(프로젝트 기본값) ───────────────────
    // 문구 라벨은 더 이상 쓰지 않으므로, 오브젝트마다 스프라이트를 물리지 않아도
    // 근접 안내가 아이콘으로 뜨도록 여기서 공용 그림을 읽어 폴백으로 쓴다.
    // 파일을 넣지 않으면(둘 다 못 찾으면) 예전 문구 라벨로 되돌아간다 — 안내가 통째로 사라지는 것보다 낫다.
    private const string DefaultPromptSpritePath    = "UI/InteractPrompt/key_up";
    private const string DefaultPromptSpriteAltPath = "UI/InteractPrompt/key_down";

    private static Sprite s_defaultPromptSprite;
    private static Sprite s_defaultPromptSpriteAlt;
    private static bool s_defaultPromptLookupDone;

    private static void EnsureDefaultPromptSprites()
    {
        if (s_defaultPromptLookupDone) return;
        s_defaultPromptLookupDone = true;

        s_defaultPromptSprite    = Resources.Load<Sprite>(DefaultPromptSpritePath);
        s_defaultPromptSpriteAlt = Resources.Load<Sprite>(DefaultPromptSpriteAltPath);
    }

    /// <summary>이 오브젝트가 실제로 띄울 기본 프레임. 인스펙터 지정이 우선, 없으면 공용 아이콘.</summary>
    private Sprite EffectivePromptSprite
    {
        get
        {
            if (promptSprite != null) return promptSprite;
            EnsureDefaultPromptSprites();
            return s_defaultPromptSprite;
        }
    }

    /// <summary>번갈아 낄 프레임. 인스펙터에 자체 그림을 넣은 오브젝트는 그쪽 alt만 쓴다(섞이지 않게).</summary>
    private Sprite EffectivePromptSpriteAlt
    {
        get
        {
            if (promptSprite != null) return promptSpriteAlt;
            EnsureDefaultPromptSprites();
            return s_defaultPromptSpriteAlt;
        }
    }

    private IndicatorView _indicatorView;
    private OptionListView _optionsView;
    private HighlightView _highlightView;
    private bool _feedbackBuilt;
    private bool _hasTriggerCollider;
    private Collider2D _triggerCollider;   // 여유 판정(점프)용 — 트리거 표면까지의 거리를 잰다
    private Transform _feedbackPlayer;
    private float _playerSearchTimer;
    private bool _languageSubscribed;
    private float _labelFlashTimer;
    private bool _labelWasVisible;

    // ─────────────────────────── 생성/구동/정리 ───────────────────────────

    private void BuildFeedback()
    {
        if (_feedbackBuilt) return;
        _feedbackBuilt = true;

        _hasTriggerCollider = HasOwnTriggerCollider();

        if (showIndicator)
            _indicatorView = new IndicatorView(this);

        // 근접 안내 목록. 종류별 동작이 '근접 시 강제'를 요청하면 인스펙터 설정과 무관하게 만든다.
        if (showPromptLabel || (Behaviour != null && Behaviour.ForcePromptLabelOnApproach))
            _optionsView = new OptionListView(this);

        if (highlightScaleOnNearby || outlineObject != null)
            _highlightView = new HighlightView(this);
    }

    private void DriveFeedback()
    {
        if (!_feedbackBuilt) return;

        bool near = IsPlayerNear();
        bool uiBlocked = hideFeedbackWhileUIOpen &&
                         UIStateManager.Instance != null &&
                         UIStateManager.Instance.CurrentState != UIState.None;
        bool proximity = near && !uiBlocked;

        // 느낌표: 기본은 근접 + '실제로 할 일 있음'. '상시' 옵션이 켜지면 근접 판정을 무시하고
        //         할 일이 있을 때 항상 띄운다(UI 열림 중엔 여전히 숨김).
        //         '할 일 있음'(HasPendingTask)은 종류별로 판정한다 — 게시판 서브퀘스트,
        //         저녁 침대, 오늘 남은 코인 판, 상점에 팔 광물 등. 이동용 오브젝트는 '가능=할 일'로 유지.
        // 종류별 동작이 '상시 표시'를 요청할 수 있다(침대 등) — 인스펙터 설정과 OR.
        bool alwaysVisible = indicatorAlwaysVisible ||
                             (Behaviour != null && Behaviour.IndicatorAlwaysVisible);
        bool indicatorNear = alwaysVisible ? !uiBlocked : proximity;
        // '근접하면 느낌표 감춤'(침대) — 멀리선 느낌표로 유도, 가까이 가면 느낌표 대신 문구 라벨.
        if (near && Behaviour != null && Behaviour.IndicatorHidesWhenNear) indicatorNear = false;
        _indicatorView?.Tick(indicatorNear && HasPendingTask);

        // 선택지 목록: 기본은 근접 시. '상호작용 시에만' 옵션이 켜지면 근접 무시하고
        //            상호작용 순간 켜둔 타이머 동안만 띄운다.
        //            띄울 항목이 하나도 없으면(OptionCount 0) 목록이 알아서 숨는다.
        // 종류별 동작이 '근접 시 강제'를 요청하면 promptLabelOnInteractOnly를 무시하고 근접 시 띄운다.
        bool forceApproach = Behaviour != null && Behaviour.ForcePromptLabelOnApproach;
        bool labelWant;
        if (promptLabelOnInteractOnly && !forceApproach)
        {
            if (_labelFlashTimer > 0f) _labelFlashTimer -= Time.unscaledDeltaTime;
            labelWant = _labelFlashTimer > 0f;
        }
        else
        {
            labelWant = proximity;
        }
        // 목록이 닫히면 선택을 맨 위로 되돌린다 — 멀어졌다 다시 다가왔을 때
        // 지난번에 고른 줄이 남아 있으면 "왜 상점이 골라져 있지?"가 된다.
        if (_labelWasVisible && !labelWant) ResetSelectedOption();
        _labelWasVisible = labelWant;

        _optionsView?.Tick(labelWant);

        // 강조: 근접 시 확대/테두리(트리거 근접 기준).
        _highlightView?.Tick(near);
    }

    /// <summary>'상호작용 시에만' 옵션이 켜져 있을 때, 상호작용 순간 문구 라벨을 잠깐 띄운다.</summary>
    private void FlashPromptLabel()
    {
        if (!promptLabelOnInteractOnly || _optionsView == null) return;
        _labelFlashTimer = Mathf.Max(0.05f, promptLabelFlashDuration);
    }

    /// <summary>비활성/풀 반납 시 잔상 제거.</summary>
    private void ResetFeedback()
    {
        _indicatorView?.Reset();
        _optionsView?.Reset();
        _highlightView?.Reset();
        _feedbackPlayer = null;
        _labelFlashTimer = 0f;
        _labelWasVisible = false;
        ResetSelectedOption();
    }

    /// <summary>파괴 시 생성한 텍스처 정리.</summary>
    private void DisposeFeedback()
    {
        _indicatorView?.Dispose();
        _optionsView?.Dispose();
        UnsubscribeLanguage();
    }

    /// <summary>
    /// 근접 판정. 트리거 안이면 근접이고, 트리거를 <b>조금</b> 벗어난 것까지만 봐준다
    /// (<see cref="feedbackTriggerGrace"/> — 트리거 표면에서의 거리).
    ///
    /// 트리거만 믿으면 <b>제자리에서 점프하는 것만으로 목록이 툭 사라진다</b> —
    /// 상호작용 트리거는 대개 발밑에 낮게 깔려 있어서 플레이어 콜라이더가 위로 빠져나간다.
    /// 반대로 그냥 반경으로 바꾸면 멀리 있는 오브젝트까지 잡힌다 — 그래서 기준을
    /// '오브젝트 중심으로부터의 거리'가 아니라 <b>트리거 모양에서 얼마나 벗어났는가</b>로 잡는다.
    /// </summary>
    private bool IsPlayerNear()
    {
        if (_hasTriggerCollider)
        {
            if (_playerInTrigger)
            {
                // 트리거를 벗어난 뒤(=_triggerPlayer가 비워진 뒤)에도 여유 판정을 하려면
                // 안에 있는 동안 플레이어를 기억해둬야 한다.
                if (_triggerPlayer != null) _feedbackPlayer = _triggerPlayer.transform;
                return true;
            }
            return IsJustOutsideTrigger();
        }

        // 트리거 콜라이더가 없으면 거리로 판정(플레이어 미발견 시 1초 간격 재탐색).
        if (_feedbackPlayer == null)
        {
            _playerSearchTimer -= Time.unscaledDeltaTime;
            if (_playerSearchTimer > 0f) return false;
            _playerSearchTimer = 1f;
            GameObject found = GameObject.FindGameObjectWithTag("Player");
            if (found == null) return false;
            _feedbackPlayer = found.transform;
        }
        return (_feedbackPlayer.position - transform.position).sqrMagnitude <= feedbackRadius * feedbackRadius;
    }

    private bool HasOwnTriggerCollider()
    {
        var colliders = GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null && colliders[i].isTrigger) { _triggerCollider = colliders[i]; return true; }
        return false;
    }

    /// <summary>
    /// 트리거 밖이지만 <b>바로 위로</b> 빠져나온 상태인가(=점프).
    ///
    /// 여유를 <b>세로로만</b> 준다. 사방으로 주면 옆으로 걸어 나가도 한동안 켜져 있어서
    /// "닿지도 않았는데 뜬다"가 된다 — 가로는 트리거 폭 그대로(닿을 때 켜짐)를 유지하고,
    /// 점프로 위로 벗어난 <see cref="feedbackTriggerGrace"/>만큼만 봐준다.
    /// </summary>
    private bool IsJustOutsideTrigger()
    {
        if (feedbackTriggerGrace <= 0f || _triggerCollider == null) return false;

        // 한 번이라도 트리거에 들어왔던 플레이어만 대상으로 한다 —
        // 지나가지도 않은 오브젝트가 거리만으로 켜지지 않게 하는 안전장치.
        if (_feedbackPlayer == null && _triggerPlayer != null) _feedbackPlayer = _triggerPlayer.transform;
        if (_feedbackPlayer == null) return false;

        Vector2 p = _feedbackPlayer.position;
        Bounds b = _triggerCollider.bounds;

        // 가로: 트리거 폭 안에 있어야 한다(여유 없음).
        if (p.x < b.min.x || p.x > b.max.x) return false;

        // 세로: 아래로는 여유 없이, 위로만 grace만큼.
        return p.y >= b.min.y && p.y <= b.max.y + feedbackTriggerGrace;
    }

    // ─────────────────────────── 언어 폰트 ───────────────────────────

    private void TrySubscribeLanguage()
    {
        if (_languageSubscribed || LanguageManager.Instance == null) return;
        LanguageManager.Instance.OnLanguageChanged += OnFeedbackLanguageChanged;
        _languageSubscribed = true;
        _optionsView?.ApplyFont();
    }

    private void UnsubscribeLanguage()
    {
        if (!_languageSubscribed || LanguageManager.Instance == null) { _languageSubscribed = false; return; }
        LanguageManager.Instance.OnLanguageChanged -= OnFeedbackLanguageChanged;
        _languageSubscribed = false;
    }

    private void OnFeedbackLanguageChanged(LanguageType _)
    {
        _optionsView?.OnLanguageChanged();
    }

    // ============================================================================
    //  느낌표 뷰 (구 InteractionIndicator)
    // ============================================================================
    private sealed class IndicatorView
    {
        private readonly WorldInteractable _o;
        private Transform _indicator;
        private SpriteRenderer _renderer;
        private Vector3 _baseLocalPos;
        private float _visibility;
        private float _bobTime;
        private Texture2D _generatedTexture;

        private const float AppearDuration = 0.25f;
        private const float DisappearDuration = 0.18f;
        private const float BobAmplitude = 0.07f;
        private const float BobSpeed = 3.5f;

        public IndicatorView(WorldInteractable owner)
        {
            _o = owner;

            SpriteRenderer[] renderers = _o.GetComponentsInChildren<SpriteRenderer>();
            _baseLocalPos = ComputeBaseLocalPosition(renderers);
            Create(renderers.Length > 0 ? renderers[0] : null);
        }

        public void Tick(bool wantVisible)
        {
            float target = wantVisible ? 1f : 0f;
            float speed = wantVisible
                ? 1f / Mathf.Max(0.01f, AppearDuration)
                : 1f / Mathf.Max(0.01f, DisappearDuration);
            _visibility = Mathf.MoveTowards(_visibility, target, speed * Time.deltaTime);

            if (_visibility <= 0f)
            {
                if (_indicator.gameObject.activeSelf) _indicator.gameObject.SetActive(false);
                _bobTime = 0f;
                return;
            }

            if (!_indicator.gameObject.activeSelf) _indicator.gameObject.SetActive(true);
            _bobTime += Time.deltaTime;

            float s = EaseOutBack(_visibility) * Mathf.Max(0.01f, _o.indicatorScale);
            _indicator.localScale = new Vector3(s, s, 1f);

            float bob = Mathf.Sin(_bobTime * BobSpeed) * BobAmplitude * _visibility;
            _indicator.localPosition = _baseLocalPos + new Vector3(0f, bob, 0f);

            _renderer.color = new Color(1f, 1f, 1f, _visibility);
        }

        public void Reset()
        {
            _visibility = 0f;
            _bobTime = 0f;
            if (_indicator != null) _indicator.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (_generatedTexture != null) Object.Destroy(_generatedTexture);
        }

        private Vector3 ComputeBaseLocalPosition(SpriteRenderer[] renderers)
        {
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                var worldTop = new Vector3(bounds.center.x, bounds.max.y + 0.25f, _o.transform.position.z);
                return _o.transform.InverseTransformPoint(worldTop) + (Vector3)_o.indicatorOffset;
            }
            return (Vector3)_o.indicatorOffset;
        }

        private void Create(SpriteRenderer baseRenderer)
        {
            var go = new GameObject("InteractionIndicator");
            go.transform.SetParent(_o.transform, false);
            go.transform.localPosition = _baseLocalPos;
            go.transform.localScale = Vector3.zero;
            _indicator = go.transform;

            _renderer = go.AddComponent<SpriteRenderer>();
            _renderer.sprite = _o.indicatorSprite != null ? _o.indicatorSprite : CreateExclamationSprite();
            _renderer.color = new Color(1f, 1f, 1f, 0f);
            // 오후 빨간 라이팅 필터가 느낌표에 씌워지지 않도록 Unlit 머티리얼 사용
            Material unlit = GetUnlitMaterial();
            if (unlit != null) _renderer.sharedMaterial = unlit;
            if (baseRenderer != null) _renderer.sortingLayerID = baseRenderer.sortingLayerID;
            _renderer.sortingOrder = (baseRenderer != null ? baseRenderer.sortingOrder : 0) + _o.indicatorSortingOffset;

            go.SetActive(false);
        }

        // Sprite-Unlit-Default 머티리얼(라이팅 무시). 모든 느낌표가 공유하도록 정적 캐시.
        private static Material s_unlitMaterial;
        private static bool s_unlitLookupDone;
        internal static Material GetUnlitMaterial()
        {
            if (s_unlitLookupDone) return s_unlitMaterial;
            s_unlitLookupDone = true;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default"); // 폴백(빌트인 파이프라인)
            if (shader != null) s_unlitMaterial = new Material(shader) { name = "InteractionIndicator_Unlit" };
            return s_unlitMaterial;
        }

        /// <summary>느낌표 스프라이트를 코드로 생성(위가 넓은 테이퍼 막대 + 점, SDF 외곽선). 48x72px.</summary>
        private Sprite CreateExclamationSprite()
        {
            const int W = 48, H = 72;
            const float outline = 3.5f;
            const float aa = 1.4f;

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _generatedTexture = tex;

            Vector2 barA = new Vector2(24f, 27f);
            Vector2 barB = new Vector2(24f, 58f);
            Vector2 dotC = new Vector2(24f, 12f);
            const float dotR = 5.5f;

            Vector2 ab = barB - barA;
            var pixels = new Color[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);

                    float t = Mathf.Clamp01(Vector2.Dot(p - barA, ab) / ab.sqrMagnitude);
                    float barDist = Vector2.Distance(p, barA + ab * t) - Mathf.Lerp(5f, 7.5f, t);
                    float dotDist = Vector2.Distance(p, dotC) - dotR;
                    float d = Mathf.Min(barDist, dotDist);

                    float fillA = Mathf.Clamp01(0.5f - d / aa);
                    float outA = Mathf.Clamp01(0.5f - (d - outline) / aa);

                    Color c = Color.Lerp(_o.indicatorOutline, _o.indicatorFill, fillA);
                    c.a = outA;
                    pixels[y * W + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);

            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), 100f);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            t -= 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }
    }

    // ============================================================================
    //  선택지 목록 뷰 (근접하면 '지금 할 수 있는 일'을 전부 띄운다)
    // ============================================================================
    /// <summary>
    /// 근접 안내를 <b>목록</b>으로 띄운다 — 어두운 반투명 패널 위에 한 줄씩
    /// <c>[F] 상점</c> · <c>[F] 지하로 진입</c> 식으로. 지금 고른 줄에만 키 아이콘이 붙고
    /// 나머지 줄은 점으로 자리를 맞춘다. 휠로 줄을 옮기고 F로 실행한다(<see cref="PlayerInteractor"/>).
    ///
    /// 구 <c>PromptLabelView</c>(글자만)·<c>PromptSpriteView</c>(아이콘만)를 대체한다 —
    /// 침대(수면/낮잠)나 NPC(대화/상점/강화)처럼 <b>할 수 있는 일이 둘 이상</b>인 오브젝트를
    /// 한 줄로는 표현할 수 없었다.
    ///
    /// 키 아이콘은 <c>promptSprite</c>(인스펙터) → <c>Resources/UI/InteractPrompt/key_up|key_down</c>
    /// 순으로 찾고, 둘 다 없으면 글자 <c>F</c>를 대신 그린다(안내가 통째로 사라지는 것보다 낫다).
    /// </summary>
    private sealed class OptionListView
    {
        private sealed class Row
        {
            public RectTransform Root;
            public Image Panel;          // 그 줄만의 배경 판 — 지금 가리키는 줄에서만 켜진다
            public RectTransform Slot;   // 레이아웃이 위치를 잡는 아이콘 자리(불가 상태면 통째로 숨긴다)
            public Image Icon;
            public TextMeshProUGUI KeyLabel;   // 키 스프라이트가 없을 때의 폴백 글자('F')
            public TextMeshProUGUI Text;
            public string AppliedText;
            public bool AppliedAvailable = true;
            public bool AppliedSelected;
            public bool StyleApplied;
        }

        private readonly WorldInteractable _o;
        private readonly System.Collections.Generic.List<Row> _rows = new System.Collections.Generic.List<Row>();

        private Transform _root;
        private CanvasGroup _group;
        private RectTransform _container;   // 줄을 세로로 쌓는 틀(배경 없음 — 판은 줄마다 따로)
        private Vector3 _baseLocalPos;
        private float _visibility;
        private float _frameTimer;
        private bool _altFrame;

        private const float PixelUnit = 0.01f;   // 100 PPU
        private const float AppearDuration = 0.18f;
        private const float DisappearDuration = 0.12f;
        private const float RiseDistance = 0.12f;
        private const float IndicatorClearance = 1.05f;

        /// <summary>비선택 줄의 글자 밝기(선택된 줄이 어느 것인지 한눈에 보이게).</summary>
        private const float UnselectedDim = 0.62f;

        /// <summary>선택된 줄을 살짝 키우는 배율. 레이아웃은 스케일을 무시하므로
        /// 줄 간격·목록 크기는 그대로고 그 줄만 도드라진다(왼쪽 정렬이라 피벗을 왼쪽에 두고 오른쪽으로 자란다).</summary>
        private const float SelectedScale = 1.08f;

        /// <summary>비선택 줄의 크기. 선택된 줄과 대비를 주되 글자는 읽히는 선.</summary>
        private const float UnselectedScale = 0.86f;

        /// <summary>비선택 줄 배경 판의 알파 배율. 판을 아예 빼면 밝은 지형 위에서 글자가 안 보인다 —
        /// 아주 연하게라도 깔아 가독성을 지키고, 선택 표시는 알파 차이+크기로 낸다.</summary>
        private const float UnselectedPanelAlpha = 0.35f;

        /// <summary>키 아이콘과 글자 사이 간격(글자 크기 배수). 붙어 보이지 않게 넉넉히 둔다.</summary>
        private const float IconTextGap = 0.85f;

        public OptionListView(WorldInteractable owner)
        {
            _o = owner;

            SpriteRenderer baseRenderer = CollectBaseBounds(out Bounds bounds, out bool hasBounds);
            _baseLocalPos = ComputeBaseLocalPosition(bounds, hasBounds);
            Build(baseRenderer);
        }

        // ─────────────────────────── 구동 ───────────────────────────

        public void Tick(bool near)
        {
            int count = near ? Mathf.Max(0, _o.OptionCount) : 0;
            bool wantVisible = count > 0;

            if (wantVisible)
            {
                AdvanceFrame();
                wantVisible = FillRows(count);
            }
            if (!wantVisible) HideRowsBeyond(0);

            // timeScale=0(마켓·설정)에서도 사라지는 연출은 끝까지 재생돼야 한다.
            float speed = wantVisible
                ? 1f / Mathf.Max(0.01f, AppearDuration)
                : 1f / Mathf.Max(0.01f, DisappearDuration);
            _visibility = Mathf.MoveTowards(_visibility, wantVisible ? 1f : 0f, speed * Time.unscaledDeltaTime);

            if (_visibility <= 0f)
            {
                if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
                // 다음에 뜰 때는 항상 기본 프레임부터 — 뜨자마자 눌린 그림이면 딸깍이 아니라 끊긴 것처럼 보인다.
                _frameTimer = 0f;
                _altFrame = false;
                return;
            }

            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);

            // 부모 스케일 상쇄 — 스프라이트를 크게 늘려 쓴 오브젝트에 붙어도 글자 크기는 항상 일정.
            Vector3 lossy = _o.transform.lossyScale;
            float invX = Mathf.Abs(lossy.x) > 1e-4f ? 1f / Mathf.Abs(lossy.x) : 1f;
            float invY = Mathf.Abs(lossy.y) > 1e-4f ? 1f / Mathf.Abs(lossy.y) : 1f;
            float unit = PixelUnit * Mathf.Max(0.01f, _o.labelScale);
            _root.localScale = new Vector3(unit * invX, unit * invY, 1f);

            float eased = _visibility * _visibility * (3f - 2f * _visibility);
            _group.alpha = eased;
            _root.localPosition = _baseLocalPos + new Vector3(0f, (eased - 1f) * RiseDistance * invY, 0f);
        }

        public void Reset()
        {
            _visibility = 0f;
            _frameTimer = 0f;
            _altFrame = false;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public void Dispose() { }

        public void OnLanguageChanged()
        {
            ApplyFont();
            for (int i = 0; i < _rows.Count; i++) _rows[i].AppliedText = null; // 다음 Tick에서 새 언어로
        }

        public void ApplyFont()
        {
            TMP_FontAsset font = LanguageManager.Instance != null ? LanguageManager.Instance.GetCurrentFont() : null;
            if (font == null) return;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Text != null) _rows[i].Text.font = font;
                if (_rows[i].KeyLabel != null) _rows[i].KeyLabel.font = font;
            }
        }

        // ─────────────────────────── 줄 채우기 ───────────────────────────

        /// <summary>선택지를 줄에 채운다. 띄울 문구가 하나도 없으면 false(=목록을 숨긴다).</summary>
        private bool FillRows(int count)
        {
            int selected = _o.SelectedOption;
            int used = 0;

            for (int i = 0; i < count; i++)
            {
                InteractionPromptInfo info = _o.GetOptionPrompt(i);
                if (!info.HasText) continue;   // 조건이 안 되는 항목은 줄 자체를 만들지 않는다

                Row row = EnsureRow(used);
                ApplyRow(row, info, i == selected);
                used++;
            }

            HideRowsBeyond(used);
            return used > 0;
        }

        private void HideRowsBeyond(int used)
        {
            for (int i = used; i < _rows.Count; i++)
                if (_rows[i].Root != null && _rows[i].Root.gameObject.activeSelf)
                    _rows[i].Root.gameObject.SetActive(false);
        }

        private void ApplyRow(Row row, in InteractionPromptInfo info, bool selected)
        {
            if (!row.Root.gameObject.activeSelf) row.Root.gameObject.SetActive(true);

            string resolved = InteractionPrompts.Resolve(info);
            if (resolved != row.AppliedText)
            {
                row.AppliedText = resolved;
                row.Text.text = resolved;
            }

            // 색: 불가 사유는 붉게, 선택되지 않은 줄은 한 단계 어둡게.
            if (!row.StyleApplied || info.Available != row.AppliedAvailable || selected != row.AppliedSelected)
            {
                row.AppliedAvailable = info.Available;
                row.AppliedSelected = selected;
                row.StyleApplied = true;

                Color c = InteractionPrompts.ColorOf(info.Available);
                if (!selected) c *= UnselectedDim;
                c.a = 1f;
                row.Text.color = c;
                row.Text.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;

                // 배경 판은 모든 줄에 깔되, 비선택 줄은 아주 연하게(글자 가독성만 확보).
                if (row.Panel != null)
                {
                    row.Panel.enabled = _o.labelBackdrop;
                    Color pc = PanelBaseColor;
                    if (!selected) pc.a *= UnselectedPanelAlpha;
                    row.Panel.color = pc;
                }

                // 선택된 줄은 살짝 크게, 나머지는 작게(레이아웃에는 영향 없음).
                float s = selected ? SelectedScale : UnselectedScale;
                row.Root.localScale = new Vector3(s, s, 1f);
            }

            ApplyRowIcon(row, selected, info.Available);
        }

        /// <summary>선택된 줄에는 키 아이콘([F]), 나머지 줄에는 점을 그린다.
        ///
        /// <b>지금 할 수 없는 일(불가)에는 키를 아예 붙이지 않는다</b> — 문구만 붉게 띄운다.
        /// "날이 어두워져 들어갈 수 없다" 옆에 [F]가 있으면 눌러도 되는 것처럼 읽힌다.
        /// 불가 전용 그림(<c>promptSpriteBlocked</c>)을 일부러 지정한 오브젝트만 그것을 그린다.</summary>
        private void ApplyRowIcon(Row row, bool selected, bool available)
        {
            // 불가 + 전용 그림 없음 = 키를 아예 안 그린다(눌러도 되는 것처럼 읽히지 않게).
            // 이때는 아이콘 '자리'까지 접는다 — 자리만 남기면 "아직 사용할 수 없다" 문구가
            // 빈 칸만큼 오른쪽으로 밀려 보인다. 이 줄은 선택/비선택 어느 쪽에서도 아이콘을
            // 그리지 않으므로, 자리를 접어도 선택을 옮길 때 줄이 들썩이지 않는다.
            bool blockedNoIcon = !available && _o.promptSpriteBlocked == null;

            if (row.Slot.gameObject.activeSelf == blockedNoIcon)
                row.Slot.gameObject.SetActive(!blockedNoIcon);

            if (blockedNoIcon)
            {
                row.Icon.enabled = false;
                if (row.KeyLabel.gameObject.activeSelf) row.KeyLabel.gameObject.SetActive(false);
                return;
            }

            if (selected)
            {
                Sprite key;
                if (!available)
                {
                    key = _o.promptSpriteBlocked;   // 여기까지 왔다면 반드시 지정돼 있다(정지 이미지)
                }
                else
                {
                    Sprite alt = _o.EffectivePromptSpriteAlt;
                    key = _altFrame && alt != null ? alt : _o.EffectivePromptSprite;
                }

                if (key != null)
                {
                    row.Icon.sprite = key;
                    row.Icon.color = Color.white;
                    row.Icon.enabled = true;
                    if (row.KeyLabel.gameObject.activeSelf) row.KeyLabel.gameObject.SetActive(false);
                    return;
                }

                // 키 스프라이트가 아직 없을 때: 글자 'F'로 대신 알린다.
                row.Icon.enabled = false;
                if (!row.KeyLabel.gameObject.activeSelf) row.KeyLabel.gameObject.SetActive(true);
                row.KeyLabel.text = InteractionKeys.InteractLabel;
                row.KeyLabel.color = InteractionPrompts.AvailableColor;
                return;
            }

            // 비선택 줄 — 자리를 맞추는 작은 점. (스프라이트 없는 Image를 켜두면 흰 사각형이 그려진다)
            row.Icon.enabled = false;
            if (!row.KeyLabel.gameObject.activeSelf) row.KeyLabel.gameObject.SetActive(true);
            row.KeyLabel.text = "·";
            row.KeyLabel.color = new Color(1f, 1f, 1f, 0.45f);
        }

        /// <summary>선택된 줄 배경 판의 기준색(비선택 줄은 여기서 알파만 낮춘다).
        /// 스프라이트를 넣었으면 그 틴트, 아니면 코드가 그리는 판의 공용 색.</summary>
        private Color PanelBaseColor =>
            _o.labelBackdropSprite != null ? _o.labelBackdropSpriteColor : InteractionPrompts.BackdropColor;

        /// <summary>선택된 줄의 키 아이콘을 번갈아 표시(딸깍) — 보이는 동안에만 시간을 흘린다.</summary>
        private void AdvanceFrame()
        {
            if (_o.EffectivePromptSpriteAlt == null) return;

            float interval = Mathf.Max(0.02f, _o.promptSpriteFrameInterval);
            _frameTimer += Time.unscaledDeltaTime;   // timeScale=0에서도 계속 딸깍이게
            if (_frameTimer < interval) return;

            _frameTimer -= interval;
            _altFrame = !_altFrame;
        }

        // ─────────────────────────── 생성 ───────────────────────────

        private Row EnsureRow(int index)
        {
            while (_rows.Count <= index) _rows.Add(CreateRow(_rows.Count));
            return _rows[index];
        }

        private Row CreateRow(int index)
        {
            float font = Mathf.Max(4f, _o.labelFontSize);
            // 아이콘은 글자와 비슷한 크기로. (1.0 = 글자 높이와 거의 같음 — 예전 1.35는 혼자 커 보였다)
            float iconSize = font * 1.05f * Mathf.Max(0.05f, _o.promptSpriteScale);

            var rowGo = new GameObject($"Option{index}", typeof(RectTransform));
            rowGo.transform.SetParent(_container.transform, false);
            var rowRt = (RectTransform)rowGo.transform;
            // 가운데를 기준으로 커지고 작아진다(줄 내용도 가운데 정렬이라 좌우 대칭으로 움직인다).
            rowRt.pivot = new Vector2(0.5f, 0.5f);

            // 이 줄만의 배경 판. 줄 자신의 Image라 자식(아이콘·글자)보다 먼저 그려져 뒤에 깔린다.
            // 선택된 줄에서만 enabled가 되고, 꺼져도 레이아웃(여백)은 그대로라 줄이 밀리지 않는다.
            var panel = rowGo.AddComponent<Image>();
            panel.raycastTarget = false;
            panel.color = PanelBaseColor;
            if (_o.labelBackdropSprite != null)
            {
                panel.sprite = _o.labelBackdropSprite;
                panel.type = Image.Type.Sliced;   // 스프라이트의 Border 값대로 늘어난다
                panel.pixelsPerUnitMultiplier = Mathf.Max(0.01f, _o.labelBackdropPixelsPerUnitMultiplier);
            }
            panel.enabled = false;

            Vector4 pad = _o.labelBackdropPadding;
            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(
                Mathf.RoundToInt(pad.x), Mathf.RoundToInt(pad.z),
                Mathf.RoundToInt(pad.y), Mathf.RoundToInt(pad.w));
            hlg.spacing = font * IconTextGap;
            // 줄 안에서는 왼쪽 정렬 — 아이콘 열과 글자 시작점이 모든 줄에서 같은 x에 놓인다.
            // 가운데 정렬로 두면 줄마다 글자 길이가 달라 아이콘이 좌우로 튀어(선택을 옮길 때마다 흔들려) 보인다.
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            var rowLe = rowGo.AddComponent<LayoutElement>();
            rowLe.minHeight = iconSize;

            // 아이콘 자리. 레이아웃(HorizontalLayoutGroup)이 위치를 잡는 것은 이 '슬롯'이고,
            // 실제 그림과 폴백 글자는 그 안에 깔린다 — 그래야 promptSpriteOffset 미세 조정이
            // 다음 레이아웃 패스에 덮어써지지 않는다.
            var slotGo = new GameObject("KeySlot", typeof(RectTransform));
            slotGo.transform.SetParent(rowGo.transform, false);
            var slotLe = slotGo.AddComponent<LayoutElement>();
            slotLe.preferredWidth = iconSize;
            slotLe.preferredHeight = iconSize;
            slotLe.minWidth = iconSize;

            var iconGo = new GameObject("Key", typeof(RectTransform));
            iconGo.transform.SetParent(slotGo.transform, false);
            var icon = iconGo.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            StretchWithOffset(icon.rectTransform, _o.promptSpriteOffset);

            // 키 스프라이트가 없을 때만 켜지는 폴백 글자
            var keyLabel = CodeUI.CreateText(slotGo.transform, "KeyLabel", font, FontStyles.Bold,
                InteractionPrompts.AvailableColor, TextAlignmentOptions.Center);
            keyLabel.raycastTarget = false;
            StretchWithOffset(keyLabel.rectTransform, _o.promptSpriteOffset);
            keyLabel.gameObject.SetActive(false);

            var text = CodeUI.CreateText(rowGo.transform, "Text", font, FontStyles.Normal,
                InteractionPrompts.AvailableColor, TextAlignmentOptions.MidlineLeft);
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            var textFitter = text.gameObject.AddComponent<ContentSizeFitter>();
            textFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TMP_FontAsset f = LanguageManager.Instance != null ? LanguageManager.Instance.GetCurrentFont() : null;
            if (f != null) { text.font = f; keyLabel.font = f; }

            rowRt.gameObject.SetActive(false);
            return new Row
            {
                Root = rowRt,
                Panel = panel,
                Slot = (RectTransform)slotGo.transform,
                Icon = icon,
                KeyLabel = keyLabel,
                Text = text,
            };
        }

        /// <summary>부모를 꽉 채우되 지정한 만큼 밀어 놓는다(레이아웃이 잡는 슬롯 안쪽이라 덮어써지지 않는다).</summary>
        private static void StretchWithOffset(RectTransform rt, Vector2 offset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offset;
            rt.offsetMax = offset;
        }

        private void Build(SpriteRenderer baseRenderer)
        {
            var rootGo = new GameObject("InteractionOptions", typeof(RectTransform));
            rootGo.transform.SetParent(_o.transform, false);
            _root = rootGo.transform;

            var rootRt = (RectTransform)_root;
            rootRt.sizeDelta = new Vector2(1f, 1f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.localPosition = _baseLocalPos;
            rootRt.localScale = Vector3.one * (PixelUnit * Mathf.Max(0.01f, _o.labelScale));

            var canvas = rootGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (baseRenderer != null) canvas.sortingLayerID = baseRenderer.sortingLayerID;
            canvas.sortingOrder = (baseRenderer != null ? baseRenderer.sortingOrder : 0) + _o.labelSortingOffset;

            _group = rootGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            // 줄을 세로로 쌓는 '틀'. 배경 판은 여기가 아니라 줄마다 따로 있고
            // (CreateRow), 지금 가리키는 줄에서만 켜진다 — 나머지 줄은 판 없이 투명하다.
            var containerGo = new GameObject("Options", typeof(RectTransform));
            containerGo.transform.SetParent(_root, false);
            _container = (RectTransform)containerGo.transform;

            _container.anchorMin = _container.anchorMax = new Vector2(0.5f, 0.5f);
            _container.pivot = new Vector2(0.5f, 0f);
            _container.anchoredPosition = Vector2.zero;

            var vlg = containerGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = Mathf.Max(2f, _o.labelFontSize * 0.22f);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            // 모든 줄을 '가장 긴 줄'의 너비로 맞춘다 — 배경 판이 줄마다 들쭉날쭉하면 목록이 지저분해 보인다.
            // (컨테이너 너비 = 가장 긴 줄의 preferred width, ContentSizeFitter가 그렇게 잡는다)
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            var fitter = containerGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            rootGo.SetActive(false);
        }

        // ─────────────────────────── 위치 ───────────────────────────

        private SpriteRenderer CollectBaseBounds(out Bounds bounds, out bool hasBounds)
        {
            bounds = default;
            hasBounds = false;
            SpriteRenderer first = null;

            var renderers = _o.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                if (r.gameObject.name == "InteractionIndicator") continue; // 느낌표는 위치 계산에서 제외

                if (first == null) first = r;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return first;
        }

        private Vector3 ComputeBaseLocalPosition(Bounds bounds, bool hasBounds)
        {
            float clearance = _o.showIndicator ? IndicatorClearance : 0f;

            if (hasBounds)
            {
                var worldTop = new Vector3(bounds.center.x, bounds.max.y + 0.3f + clearance, _o.transform.position.z);
                return _o.transform.InverseTransformPoint(worldTop) + WorldToLocalOffset(_o.labelOffset);
            }
            return WorldToLocalOffset(new Vector2(_o.labelOffset.x, _o.labelOffset.y + clearance));
        }

        private Vector3 WorldToLocalOffset(Vector2 worldOffset)
        {
            Vector3 lossy = _o.transform.lossyScale;
            float x = Mathf.Abs(lossy.x) > 1e-4f ? worldOffset.x / lossy.x : worldOffset.x;
            float y = Mathf.Abs(lossy.y) > 1e-4f ? worldOffset.y / lossy.y : worldOffset.y;
            return new Vector3(x, y, 0f);
        }
    }

    // ============================================================================
    //  강조 뷰 (구 InteractableHighlight)
    // ============================================================================
    private sealed class HighlightView
    {
        private readonly WorldInteractable _o;
        private readonly Vector3 _originalScale;

        public HighlightView(WorldInteractable owner)
        {
            _o = owner;
            _originalScale = _o.transform.localScale;
            if (_o.outlineObject != null) _o.outlineObject.SetActive(false);
        }

        public void Tick(bool near)
        {
            if (_o.highlightScaleOnNearby)
            {
                Vector3 target = near ? _originalScale * _o.hoverScaleMultiplier : _originalScale;
                _o.transform.localScale = Vector3.Lerp(_o.transform.localScale, target,
                    Time.deltaTime * _o.hoverScaleSpeed);
            }

            if (_o.outlineObject != null && _o.outlineObject.activeSelf != near)
                _o.outlineObject.SetActive(near);
        }

        public void Reset()
        {
            if (_o.highlightScaleOnNearby) _o.transform.localScale = _originalScale;
            if (_o.outlineObject != null) _o.outlineObject.SetActive(false);
        }
    }
}
