using UnityEngine;

/// <summary>
/// @tags: vfx, light, shaft, sunlight, beam, entrance, decoration
///
/// 위(천장 구멍)에서 새어드는 햇빛 기둥 연출. 스프라이트를 코드로 굽는다(에셋 불필요).
///
/// <b>기준점은 "빛이 닿는 바닥"이다.</b> 트랜스폼 위치에 바닥 웅덩이가 생기고
/// 기둥은 거기서 위로 <see cref="height"/>만큼 뻗는다. 레벨에 놓을 때
/// "여기가 빛 웅덩이고 여기서 E를 누른다"가 바로 보이게 하기 위함.
///
/// <b>왜 Light2D가 아닌가</b> — 이 프로젝트의 지하 어둠은 URP Light2D가 아니라
/// PlayerVisionOverlay의 월드 캔버스 어둠막(sortingOrder 999)이 화면을 덮는 방식이다.
/// Light2D를 넣어도 어둠막 아래에 깔려 보이지 않는다. 그래서 어둠막보다 높은
/// sortingOrder(기본 1000)를 가진 스프라이트로 그린다 → 멀리서도 보이는 랜드마크가 된다.
///
/// 게임플레이 의존이 없다. F키 복귀는 SurfaceExitBeacon이 따로 담당하며 서로를 모른다.
/// 설계: Assets/Docs/surface-light-shaft.md
/// </summary>
[ExecuteAlways]
public class SurfaceLightShaft : MonoBehaviour
{
    // 굽는 텍스처 해상도. 알파 그라데이션만 담으므로 이 정도면 충분하다.
    private const int BEAM_W = 64,  BEAM_H = 128;
    private const int POOL_W = 64,  POOL_H = 32;
    private const int DUST_S = 16;

    [Header("모양 (월드 유닛)")]
    [Tooltip("바닥에서 천장 구멍까지의 길이. 구멍에 닿게 맞춘다.")]
    [SerializeField] private float height      = 6f;
    [Tooltip("구멍 쪽(위) 폭.")]
    [SerializeField] private float topWidth    = 1.2f;
    [Tooltip("바닥 쪽(아래) 폭. 보통 위보다 넓게 퍼진다.")]
    [SerializeField] private float bottomWidth = 3.2f;

    [Header("빛")]
    [SerializeField] private Color beamColor = new Color(1f, 0.95f, 0.75f);
    [Tooltip("전체 밝기.")]
    [Range(0f, 2f)] [SerializeField] private float intensity = 0.55f;
    [Tooltip("바닥 쪽 잔광 세기(위쪽 대비). 0이면 바닥에서 완전히 사라진다.")]
    [Range(0f, 1f)] [SerializeField] private float bottomFade = 0.18f;
    [Tooltip("세로 감쇠 곡선. 클수록 위쪽에만 빛이 몰린다.")]
    [Range(0.5f, 4f)] [SerializeField] private float verticalFalloff = 1.6f;
    [Tooltip("좌우 가장자리 부드러움. 클수록 중심에 심지가 생긴다.")]
    [Range(0.5f, 4f)] [SerializeField] private float edgeSoftness = 1.6f;

    [Header("맥동")]
    [Range(0f, 0.5f)] [SerializeField] private float pulseAmount = 0.12f;
    [SerializeField] private float pulseSpeed = 0.7f;

    [Header("먼지")]
    [Tooltip("0이면 먼지를 아예 만들지 않는다.")]
    [Range(0, 40)] [SerializeField] private int dustCount = 10;
    [SerializeField] private float dustFallSpeed = 0.35f;
    [SerializeField] private float dustSize      = 0.07f;
    [Range(0f, 1f)] [SerializeField] private float dustAlpha = 0.7f;
    [Tooltip("좌우 흔들림 폭(월드 유닛)과 속도.")]
    [SerializeField] private float dustWobble      = 0.12f;
    [SerializeField] private float dustWobbleSpeed = 0.9f;

    [Header("바닥 웅덩이")]
    [SerializeField] private bool  showFloorPool = true;
    [Tooltip("웅덩이 세로 두께. 가로는 bottomWidth를 따라간다.")]
    [SerializeField] private float floorPoolHeight = 0.9f;
    [Range(0f, 2f)] [SerializeField] private float floorPoolIntensity = 0.8f;

    [Header("렌더")]
    [Tooltip("지하는 전부 Default 레이어를 쓴다.")]
    [SerializeField] private string sortingLayerName = "Default";
    [Tooltip("PlayerVisionOverlay 어둠막이 999다. 그보다 낮추면 어둠에 묻힌다.")]
    [SerializeField] private int sortingOrder = 1000;
    [Tooltip("비우면 Custom/SpriteAdditive를 찾고, 그것도 없으면 Sprites/Default로 degrade한다.")]
    [SerializeField] private Shader beamShader;
    [Tooltip("지정하면 코드 생성 기둥 대신 이 스프라이트를 쓴다.")]
    [SerializeField] private Sprite beamSpriteOverride;

    // ── 생성물 (전부 OnDestroy에서 해제) ─────────────────────────
    private Transform      _beamTr,  _poolTr;
    private SpriteRenderer _beamSr,  _poolSr;
    private Texture2D      _beamTex, _poolTex, _dustTex;
    private Sprite         _beamSprite, _poolSprite, _dustSprite;
    private Material       _mat;

    private Transform[]      _dustTr;
    private SpriteRenderer[] _dustSr;
    private DustMote[]       _motes;

    private bool _built;
    private bool _rebuildQueued;

    /// <summary>먼지 알갱이 하나의 상태. 풀 없이 위로 되돌려 재사용한다(할당 0).</summary>
    private struct DustMote
    {
        public float NormY;   // 0=바닥, 1=구멍
        public float NormX;   // -1~1, 그 높이의 반폭 기준
        public float Phase;   // 흔들림 위상
        public float Speed;   // 개체별 낙하 속도 배수
    }

    // ===================================================
    // 생명주기
    // ===================================================
    private void OnEnable()  => Build();
    private void OnDisable() => Teardown();

    /// <summary>인스펙터 우클릭에서 수동으로 다시 굽는다. 도메인 리로드 직후 안 보일 때 쓴다.</summary>
    [ContextMenu("빛기둥 다시 굽기")]
    private void ForceRebuild()
    {
        Teardown();
        CleanupStrayChildren();
        Build();
        Debug.Log($"[SurfaceLightShaft] 다시 구움 — 자식 {transform.childCount}개, " +
                  $"셰이더 '{(_mat != null && _mat.shader != null ? _mat.shader.name : "없음")}'", this);
    }

    private void OnValidate()
    {
        // 인스펙터로 모양을 만지는 중이면 다음 프레임에 다시 굽는다.
        // (OnValidate 안에서 Destroy를 부르면 Unity가 경고를 낸다)
        if (_built) _rebuildQueued = true;
    }

    private void Update()
    {
        if (_rebuildQueued)
        {
            _rebuildQueued = false;
            Teardown();
            Build();
        }

        if (!_built) return;

        // realtimeSinceStartup은 에디터에서도 흐른다 → ExecuteAlways 프리뷰에서 맥동이 보인다.
        float t     = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        float pulse = 1f + pulseAmount * Mathf.Sin(t * pulseSpeed * Mathf.PI * 2f);

        if (_beamSr != null)
            _beamSr.color = Tint(intensity * pulse);

        if (_poolSr != null)
            _poolSr.color = Tint(intensity * floorPoolIntensity * pulse);

        UpdateDust(t);
    }

    private Color Tint(float alpha) =>
        new Color(beamColor.r, beamColor.g, beamColor.b, Mathf.Clamp01(alpha));

    private void OnDestroy() => Teardown();

    // ===================================================
    // 생성
    // ===================================================
    private void Build()
    {
        if (_built) return;

        Shader shader = ResolveShader();
        if (shader == null)
        {
            // 여기서 _built를 세우지 않는다 — 셰이더가 들어오면 다음 기회에 다시 시도한다.
            Debug.LogError("[SurfaceLightShaft] 셰이더를 찾을 수 없습니다. " +
                           "인스펙터 beamShader 슬롯에 Assets/Shaders/SpriteAdditive.shader를 연결하세요.", this);
            return;
        }

        _built = true;
        CleanupStrayChildren();

        _mat = new Material(shader) { hideFlags = HideFlags.DontSave };

        float maxWidth = Mathf.Max(topWidth, bottomWidth, 0.01f);

        // ── 기둥 ──
        _beamSprite = beamSpriteOverride != null ? beamSpriteOverride : BuildBeamSprite(maxWidth);
        _beamTr = CreateChild("__Beam", out _beamSr);
        _beamSr.sprite = _beamSprite;
        // material이 아니라 sharedMaterial — _mat은 이미 우리가 만든 인스턴스다.
        // 에디터에서 material에 대입하면 Unity가 사본을 또 만들며 경고를 낸다.
        _beamSr.sharedMaterial = _mat;
        ApplySorting(_beamSr, 0);

        if (beamSpriteOverride != null)
        {
            // 지정 스프라이트는 pivot·PPU를 알 수 없으므로 바운즈로 월드 크기를 맞춘다.
            Vector2 size = _beamSprite.bounds.size;
            _beamTr.localScale = new Vector3(
                size.x > 0f ? maxWidth / size.x : 1f,
                size.y > 0f ? height   / size.y : 1f, 1f);

            // 스케일 후 스프라이트 아랫변은 원점에서 -pivotYNorm*height에 있다.
            // 그만큼 올려야 코드 생성 기둥(pivot 아래-중앙)과 같은 자리에 선다.
            float pivotYNorm = _beamSprite.pivot.y / Mathf.Max(_beamSprite.rect.height, 1f);
            _beamTr.localPosition = new Vector3(0f, pivotYNorm * height, 0f);
        }
        else
        {
            // PPU=1로 구웠으므로 스프라이트 월드 크기 = 텍셀 수 → localScale이 곧 크기 비율이다.
            // pivot이 아래-중앙이라 위치는 0 그대로 두면 바닥에서 위로 자란다.
            _beamTr.localScale    = new Vector3(maxWidth / BEAM_W, height / BEAM_H, 1f);
            _beamTr.localPosition = Vector3.zero;
        }

        // ── 바닥 웅덩이 ──
        if (showFloorPool && floorPoolHeight > 0f)
        {
            _poolSprite = BuildPoolSprite();
            _poolTr = CreateChild("__FloorPool", out _poolSr);
            _poolSr.sprite         = _poolSprite;
            _poolSr.sharedMaterial = _mat;
            ApplySorting(_poolSr, -1); // 기둥보다 뒤 (기둥 밑동이 웅덩이 위에 얹히게)
            _poolTr.localScale    = new Vector3(bottomWidth * 1.15f / POOL_W, floorPoolHeight / POOL_H, 1f);
            _poolTr.localPosition = Vector3.zero; // pivot 중앙 → 바닥선에 걸쳐 깔린다
        }

        BuildDust();
    }

    private Shader ResolveShader()
    {
        if (beamShader != null) return beamShader;

        Shader additive = Shader.Find("Custom/SpriteAdditive");
        if (additive != null) return additive;

        // 가산 합성을 못 쓰면 알파 블렌딩으로 degrade한다 — 분홍색으로 깨지지는 않는다.
        return Shader.Find("Sprites/Default");
    }

    // HideInHierarchy는 일부러 안 쓴다 — 안 보일 때 자식이 생겼는지조차 확인할 수 없다.
    // DontSave만 걸어 Hierarchy에는 보이되 씬에는 저장되지 않게 한다.
    private Transform CreateChild(string name, out SpriteRenderer sr)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        sr = go.AddComponent<SpriteRenderer>();
        return go.transform;
    }

    // 스크립트 재컴파일(도메인 리로드)로 _built가 false로 돌아가면 이전 자식이 남은 채
    // 새로 만들어져 기둥이 겹쳐 쌓인다. 이름으로 찾아 먼저 치운다.
    private void CleanupStrayChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c.name == "__Beam" || c.name == "__FloorPool" || c.name == "__Dust")
                DestroySafe(c.gameObject);
        }
    }

    private void ApplySorting(SpriteRenderer sr, int orderOffset)
    {
        if (IsValidSortingLayer(sortingLayerName))
            sr.sortingLayerName = sortingLayerName;
        else if (!string.IsNullOrEmpty(sortingLayerName))
            Debug.LogWarning($"[SurfaceLightShaft] 정렬 레이어 '{sortingLayerName}'가 없습니다 — 기본 레이어 유지");

        sr.sortingOrder = sortingOrder + orderOffset;
    }

    // NameToID는 Default에 0을 돌려주므로 ID로는 유효성을 못 가른다. 목록을 직접 훑는다.
    private static bool IsValidSortingLayer(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        var layers = SortingLayer.layers;
        for (int i = 0; i < layers.Length; i++)
            if (layers[i].name == name) return true;

        return false;
    }

    // ===================================================
    // 텍스처 굽기
    // ===================================================
    // RGB는 항상 흰색으로 굽고 색은 SpriteRenderer.color로 입힌다.
    // → 인스펙터에서 색·밝기를 바꿔도 다시 구울 필요가 없다.

    private Sprite BuildBeamSprite(float maxWidth)
    {
        _beamTex = NewTexture(BEAM_W, BEAM_H);
        var px = new Color32[BEAM_W * BEAM_H];

        // 텍스처 폭 전체가 maxWidth에 대응한다. 각 행의 반폭을 0~0.5로 정규화.
        float topHalfN    = topWidth    / maxWidth * 0.5f;
        float bottomHalfN = bottomWidth / maxWidth * 0.5f;

        for (int y = 0; y < BEAM_H; y++)
        {
            float t     = y / (float)(BEAM_H - 1);            // 0=바닥, 1=구멍
            float halfN = Mathf.Lerp(bottomHalfN, topHalfN, t);

            // 세로 밝기 — 구멍 쪽이 최대, 바닥으로 갈수록 bottomFade까지 감쇠
            float vi = Mathf.Lerp(bottomFade, 1f, Mathf.Pow(t, verticalFalloff));

            // 맨 위 6%는 살짝 죽인다. 천장에서 싹둑 잘린 직선이 보이면 연출이 깨진다.
            vi *= Mathf.Lerp(1f, 0.6f, Mathf.InverseLerp(0.94f, 1f, t));

            int row = y * BEAM_W;
            for (int x = 0; x < BEAM_W; x++)
            {
                float u = (x + 0.5f) / BEAM_W - 0.5f;          // -0.5 ~ 0.5
                float d = Mathf.Abs(u) / Mathf.Max(halfN, 1e-4f); // 0=중심, 1=가장자리

                float hi = d >= 1f ? 0f : Mathf.Pow(1f - d * d, edgeSoftness);
                px[row + x] = White(vi * hi);
            }
        }

        _beamTex.SetPixels32(px);
        _beamTex.Apply(false, true);

        // pivot 아래-중앙 + PPU=1 → 바닥에 세워두면 위로 자란다.
        return NewSprite(_beamTex, BEAM_W, BEAM_H, new Vector2(0.5f, 0f));
    }

    private Sprite BuildPoolSprite()
    {
        _poolTex = NewTexture(POOL_W, POOL_H);
        var px = new Color32[POOL_W * POOL_H];

        for (int y = 0; y < POOL_H; y++)
        {
            float ny  = (y + 0.5f) / POOL_H - 0.5f;
            int   row = y * POOL_W;
            for (int x = 0; x < POOL_W; x++)
            {
                float nx = (x + 0.5f) / POOL_W - 0.5f;
                // 타원 거리 (가로 반경 0.5, 세로 반경 0.5)
                float d = Mathf.Sqrt(nx * nx + ny * ny) * 2f;
                float a = d >= 1f ? 0f : Mathf.Pow(1f - d * d, 1.4f);
                px[row + x] = White(a);
            }
        }

        _poolTex.SetPixels32(px);
        _poolTex.Apply(false, true);
        return NewSprite(_poolTex, POOL_W, POOL_H, new Vector2(0.5f, 0.5f));
    }

    private Sprite BuildDustSprite()
    {
        _dustTex = NewTexture(DUST_S, DUST_S);
        var px = new Color32[DUST_S * DUST_S];

        for (int y = 0; y < DUST_S; y++)
        {
            float ny  = (y + 0.5f) / DUST_S - 0.5f;
            int   row = y * DUST_S;
            for (int x = 0; x < DUST_S; x++)
            {
                float nx = (x + 0.5f) / DUST_S - 0.5f;
                float d  = Mathf.Sqrt(nx * nx + ny * ny) * 2f;
                float a  = d >= 1f ? 0f : Mathf.Pow(1f - d * d, 2f);
                px[row + x] = White(a);
            }
        }

        _dustTex.SetPixels32(px);
        _dustTex.Apply(false, true);
        return NewSprite(_dustTex, DUST_S, DUST_S, new Vector2(0.5f, 0.5f));
    }

    private static Color32 White(float alpha) =>
        new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));

    private static Texture2D NewTexture(int w, int h) =>
        new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode  = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

    // PPU=1 → 스프라이트의 월드 크기가 텍셀 수와 같아진다. localScale이 곧 월드 크기 비율.
    private static Sprite NewSprite(Texture2D tex, int w, int h, Vector2 pivot)
    {
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, 1f);
        s.hideFlags = HideFlags.DontSave;
        return s;
    }

    // ===================================================
    // 먼지
    // ===================================================
    private void BuildDust()
    {
        if (dustCount <= 0) return;

        _dustSprite = BuildDustSprite();
        _dustTr = new Transform[dustCount];
        _dustSr = new SpriteRenderer[dustCount];
        _motes  = new DustMote[dustCount];

        for (int i = 0; i < dustCount; i++)
        {
            _dustTr[i] = CreateChild("__Dust", out _dustSr[i]);
            _dustSr[i].sprite         = _dustSprite;
            _dustSr[i].sharedMaterial = _mat;
            ApplySorting(_dustSr[i], 1); // 기둥보다 앞

            _dustTr[i].localScale = new Vector3(dustSize / DUST_S, dustSize / DUST_S, 1f);

            // 처음부터 골고루 흩어놓는다 (전부 꼭대기에서 동시에 떨어지면 티가 난다)
            _motes[i] = NewMote(Random.value);
        }
    }

    private DustMote NewMote(float startNormY) => new DustMote
    {
        NormY = startNormY,
        NormX = Random.Range(-0.85f, 0.85f),
        Phase = Random.Range(0f, Mathf.PI * 2f),
        Speed = Random.Range(0.7f, 1.3f)
    };

    private void UpdateDust(float t)
    {
        if (_motes == null) return;

        float dt = Application.isPlaying ? Time.deltaTime : 0.016f;

        for (int i = 0; i < _motes.Length; i++)
        {
            ref DustMote m = ref _motes[i];

            m.NormY -= dustFallSpeed * m.Speed * dt / Mathf.Max(height, 0.01f);
            if (m.NormY <= 0f) m = NewMote(1f);   // 바닥에 닿으면 꼭대기로 되돌린다

            // 기둥이 위로 갈수록 좁아지므로 반폭에 비례해 x를 잡아야 밖으로 새지 않는다.
            float halfW = Mathf.Lerp(bottomWidth, topWidth, m.NormY) * 0.5f;
            float x = m.NormX * halfW + Mathf.Sin(t * dustWobbleSpeed + m.Phase) * dustWobble;
            x = Mathf.Clamp(x, -halfW, halfW);

            _dustTr[i].localPosition = new Vector3(x, m.NormY * height, 0f);

            // 위아래 끝에서 서서히 나타나고 사라진다 (팝인이 보이지 않게)
            float fade = Mathf.InverseLerp(0f, 0.18f, m.NormY) *
                         Mathf.InverseLerp(1f, 0.82f, m.NormY);
            _dustSr[i].color = Tint(dustAlpha * fade * intensity);
        }
    }

    // ===================================================
    // 해제
    // ===================================================
    // 코드로 만든 GameObject·Texture·Sprite·Material은 씬 저장/언로드로 자동 정리되지 않는다.
    private void Teardown()
    {
        if (!_built) return;
        _built = false;

        DestroySafe(_beamTr  != null ? _beamTr.gameObject : null);
        DestroySafe(_poolTr  != null ? _poolTr.gameObject : null);
        if (_dustTr != null)
            for (int i = 0; i < _dustTr.Length; i++)
                DestroySafe(_dustTr[i] != null ? _dustTr[i].gameObject : null);

        // 인스펙터에서 받은 스프라이트는 우리 것이 아니므로 지우지 않는다.
        if (_beamSprite != null && beamSpriteOverride == null) DestroySafe(_beamSprite);
        DestroySafe(_poolSprite);
        DestroySafe(_dustSprite);
        DestroySafe(_beamTex);
        DestroySafe(_poolTex);
        DestroySafe(_dustTex);
        DestroySafe(_mat);

        _beamTr = _poolTr = null;
        _beamSr = _poolSr = null;
        _beamSprite = _poolSprite = _dustSprite = null;
        _beamTex = _poolTex = _dustTex = null;
        _mat = null;
        _dustTr = null;
        _dustSr = null;
        _motes  = null;
    }

    private static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    // ===================================================
    // 기즈모 — 배치할 때 기둥 범위가 보이게
    // ===================================================
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.95f, 0.6f, 0.9f);

        Vector3 p  = transform.position;
        float   hb = bottomWidth * 0.5f;
        float   ht = topWidth    * 0.5f;

        Vector3 bl = p + new Vector3(-hb, 0f);
        Vector3 br = p + new Vector3( hb, 0f);
        Vector3 tl = p + new Vector3(-ht, height);
        Vector3 tr = p + new Vector3( ht, height);

        Gizmos.DrawLine(bl, tl);
        Gizmos.DrawLine(br, tr);
        Gizmos.DrawLine(tl, tr);
        Gizmos.DrawLine(bl, br);
    }
}
