using UnityEngine;

/// <summary>
/// 광물이 획득 가능 상태(지형 낙하 완료)가 되면
/// 테두리(Outline) 발광 효과를 표시한다.
///
/// 활성 조건: MineralItemController.WakeUp() 호출 후
///            CircleCollider2D.isTrigger == false 상태
/// 비활성 조건: 지형에 박혀있는 동안 (isTrigger == true)
///
/// 성능: 아웃라인 레이어(GameObject + 인스턴스 머티리얼)는 청크 스폰 시점이 아니라
///       이 광물이 실제로 처음 하이라이트되는 순간에만 지연 생성한다.
///       하이라이트는 PlayerInteractor가 "가장 가까운 타깃" 1개에만 켜므로,
///       대부분의 광물은 아웃라인을 끝까지 만들지 않는다.
///
/// 사용법: 광물 프리팹 루트에 이 컴포넌트를 추가.
///         Inspector에서 Outline Shader 슬롯에 Custom/SpriteOutline 연결.
/// </summary>
public class MineralPickupGlow : MonoBehaviour, IHighlightable
{
    [Header("테두리 (Outline)")]
    public Color  outlineColor = new Color(0.4f, 0.9f, 1f, 1f);
    [Range(1f, 4f)]
    public float  outlineWidth = 1f;
    [Range(0f, 1f)] public float outlineAlpha = 1f;

    [Header("셰이더")]
    [Tooltip("Assets/Shaders/SpriteOutline.shader 를 연결하세요.")]
    public Shader outlineShader;

    [Header("자석 약한 하이라이트")]
    [Tooltip("자석에 끌려오는 광물의 아웃라인 세기(평소 대비). F키 상호작용 하이라이트가 있으면 그쪽이 우선.")]
    [Range(0f, 1f)] public float magnetAlphaScale = 0.35f;
    [Range(0f, 1f)] public float magnetWidthScale = 0.6f;

    // ── 전 인스턴스 공유 캐시 ──────────────────────────────────────
    // 셰이더 조회(Shader.Find)와 프로퍼티 문자열 해싱을 1회로 줄인다.
    private static Shader s_cachedShader;
    private static readonly int s_colorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int s_widthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int s_alphaId = Shader.PropertyToID("_OutlineAlpha");

    // ── 런타임 캐시 ────────────────────────────────────────────────
    private SpriteRenderer   _mainRenderer;
    // [Collider] 광물이 CircleCollider2D → PolygonCollider2D로 교체됨. isTrigger 상태만 참조하므로 Collider2D 기반.
    private Collider2D       _col;
    private bool             _interactorOn; // F키 상호작용 하이라이트(강함, 우선)
    private bool             _magnetOn;     // 자석 흡인 하이라이트(약함)
    private bool             _built;        // 아웃라인 레이어 지연 생성 여부

    private GameObject     _outlineGo;
    private SpriteRenderer _outlineSR;
    private Material       _outlineMat;   // 인스턴스 머티리얼 (OnDestroy에서 해제)

    // ── 초기화 ────────────────────────────────────────────────────
    // 값싼 컴포넌트 캐시만 수행한다. 아웃라인 레이어는 여기서 만들지 않는다.
    private void Awake()
    {
        _col          = GetComponent<PolygonCollider2D>();
        _mainRenderer = GetComponent<SpriteRenderer>();
        if (_mainRenderer == null)
            _mainRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private Shader ResolveShader()
    {
        if (outlineShader != null) return outlineShader;

        if (s_cachedShader == null)
            s_cachedShader = Shader.Find("Custom/SpriteOutline");
        return s_cachedShader;
    }

    // 최초 하이라이트 시점에 단 한 번만 생성한다.
    private void BuildLayers()
    {
        _built = true;

        Shader shader = ResolveShader();
        if (shader == null)
        {
            Debug.LogError("[MineralPickupGlow] Custom/SpriteOutline 셰이더를 찾을 수 없습니다. " +
                           "Inspector에서 직접 연결하거나 Assets/Shaders/SpriteOutline.shader 경로를 확인하세요.");
            return;
        }

        string sortLayer = _mainRenderer ? _mainRenderer.sortingLayerName : "Default";
        int    sortOrder = _mainRenderer ? _mainRenderer.sortingOrder     : 0;
        Sprite sprite    = _mainRenderer ? _mainRenderer.sprite           : null;

        // ── 테두리 레이어 ──────────────────────────────────────────
        // 셰이더가 몸통 픽셀을 투명 처리하므로 스프라이트 앞에 두어도 가려지지 않음
        _outlineGo = new GameObject("__Outline");
        _outlineGo.transform.SetParent(transform, false);
        _outlineGo.transform.localPosition = Vector3.zero;
        _outlineGo.transform.localRotation = Quaternion.identity;
        _outlineGo.transform.localScale    = Vector3.one;

        _outlineSR                  = _outlineGo.AddComponent<SpriteRenderer>();
        _outlineSR.sprite           = sprite;
        _outlineSR.sortingLayerName = sortLayer;
        _outlineSR.sortingOrder     = sortOrder + 1; // 메인보다 앞 (몸통은 투명)
        _outlineSR.color            = Color.white;

        _outlineMat = new Material(shader);
        _outlineMat.SetColor(s_colorId, outlineColor);
        _outlineMat.SetFloat(s_widthId, outlineWidth);
        _outlineMat.SetFloat(s_alphaId, outlineAlpha);
        _outlineSR.material = _outlineMat;

        _outlineGo.SetActive(false);
    }

    // ── IHighlightable ────────────────────────────────────────────
    /// <summary>
    /// PlayerInteractor가 이 광물이 가장 가까운 타깃이 될 때 true,
    /// 범위를 벗어나거나 다른 타깃으로 바뀔 때 false를 전달한다.
    /// </summary>
    public void SetHighlighted(bool highlighted)
    {
        _interactorOn = highlighted;
        Refresh();
    }

    /// <summary>
    /// 자석 유물이 이 광물을 흡인 대상으로 잡을 때 true, 놓칠 때 false.
    /// 아웃라인을 평소보다 약하게(magnetAlphaScale/WidthScale) 표시한다.
    /// F키 상호작용 하이라이트가 동시에 켜지면 그쪽(강함)이 우선한다.
    /// </summary>
    public void SetMagnetHighlighted(bool highlighted)
    {
        _magnetOn = highlighted;
        Refresh();
    }

    // 두 하이라이트 소스를 합쳐 실제 표시를 갱신한다.
    private void Refresh()
    {
        bool interactor = _interactorOn;
        bool magnet     = _magnetOn && !interactor; // 상호작용 우선
        bool show = (interactor || magnet) && _col != null && !_col.isTrigger;

        // 실제로 켜야 하는 순간에만 레이어를 생성한다.
        if (show && !_built)
            BuildLayers();

        if (_outlineGo == null) return;

        if (show)
        {
            // 풀 재사용으로 메인 스프라이트가 교체됐을 수 있으므로 동기화
            Sprite current = _mainRenderer ? _mainRenderer.sprite : null;
            if (current != null && _outlineSR.sprite != current)
                _outlineSR.sprite = current;

            // 상호작용=원래 세기, 자석=약한 세기
            float alpha = interactor ? outlineAlpha : outlineAlpha * magnetAlphaScale;
            float width = interactor ? outlineWidth : outlineWidth * magnetWidthScale;
            _outlineMat.SetFloat(s_alphaId, alpha);
            _outlineMat.SetFloat(s_widthId, width);
        }

        _outlineGo.SetActive(show);
    }

    // ── 풀 재사용 시 리셋 ─────────────────────────────────────────
    private void OnEnable()
    {
        _interactorOn = false;
        _magnetOn     = false;
        if (_outlineGo != null) _outlineGo.SetActive(false);
    }

    // ── 인스턴스 머티리얼 메모리 해제 ────────────────────────────
    private void OnDestroy()
    {
        if (_outlineMat != null)
            Destroy(_outlineMat);
    }
}
