// @tags: ui, player, preview, live, uiplayer, rendertexture, camera, skinshell, inventory, warehouse, code-generated

using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// UiPlayer 프리팹(장비·모션이 실시간 반영되는 플레이어 껍데기)을 UI 오버레이 안에 보여주는 렌더 장치.
///
/// UiPlayer는 SpriteRenderer 기반 월드 오브젝트라 ScreenSpaceOverlay 캔버스에 직접 못 올린다.
/// 그래서 화면 밖 스테이징 좌표에 프리팹을 복제해 두고, 그 클론만 비추는 전용 직교 카메라로
/// <see cref="RenderTexture"/>에 그린 뒤 <see cref="RawImage"/>로 UI에 띄운다.
///
/// - 이 컴포넌트가 붙은 오브젝트가 켜질 때만(=오버레이가 열릴 때) 카메라가 돌아간다(꺼지면 렌더 중지).
/// - 클론 스프라이트는 Unlit 머티리얼로 바꿔, 스테이징 지점에 2D 광원이 없어도 밝게 나오고
///   월드 조명(GlobalLightingManager)에도 영향을 주지 않는다.
/// - 클론의 "Player" 태그를 지운다 — FindWithTag("Player")가 클론을 실제 플레이어로 오인하지 않게.
/// - <see cref="SkinShell.targetPlayer"/>를 매번 실제 플레이어의 <see cref="SkinManager"/>로 다시 연결해
///   장비/의상 변화를 그대로 따라간다.
///
/// 사용법: <see cref="CodeLivePlayerPreview"/>가 RawImage를 만든 뒤 이 컴포넌트를 붙이고 <see cref="Configure"/>를 호출한다.
/// </summary>
[DisallowMultipleComponent]
public class LivePlayerPreviewRig : MonoBehaviour
{
    // 전용 렌더 레이어(TagManager의 미사용 슬롯). 프리뷰 카메라는 이 레이어만 비춘다.
    private const int PreviewLayer = 31;

    // 스테이징 기본 좌표 — 월드에서 한참 떨어진 빈 공간(청크가 로드되지 않는 곳).
    // 프리뷰가 둘 이상(인벤토리+창고)일 때 클론이 겹치지 않도록 인스턴스마다 X를 벌린다.
    private static readonly Vector3 StageBase = new Vector3(10000f, 10000f, 0f);
    private static int s_stageCounter;

    // 클론 스프라이트에 공통으로 물릴 Unlit 머티리얼(전 프리뷰 공유).
    private static Material s_unlitMat;

    // ── 설정값 ──
    private GameObject _prefab;
    private RawImage _image;
    private int _rtWidth = 384;
    private int _rtHeight = 512;
    private float _orthoSize = 0.42f;
    private Color _background = new Color(0.043f, 0.071f, 0.141f, 1f); // CodeUI.BoxBg 계열

    // ── 런타임 ──
    private GameObject _rig;   // 카메라 + 클론을 담는 루트 (DontDestroyOnLoad)
    private GameObject _clone; // UiPlayer 클론 (카메라를 이 스프라이트 바운즈 중심에 맞춘다)
    private Camera _cam;
    private RenderTexture _rt;
    private RectTransform _imageRect; // 상자 안 RawImage — 이 rect 비율에 RT·카메라 aspect를 맞춘다
    private float _rtAspect;          // 현재 RT 가로/세로 비
    private Bounds _cloneBounds;      // 클론 스프라이트 합산 바운즈(스테이지 로컬 기준)
    private bool _hasBounds;          // 바운즈 캐시 확보 여부
    private SkinShell _shell;
    private Vector3 _stagePos;
    private bool _built;

    // 캐릭터 바운즈 대비 여백 배수(1=꽉 참, <1=더 크게 확대). 작을수록 플레이어가 크게 보인다.
    private const float FramePadding = 0.82f;

    // 세로 조준 바이어스: 바운즈 중심보다 위(+)를 조준하면 캐릭터가 프레임에서 아래로 내려간다.
    // → 머리가 화면 가운데쯤 오게 살짝 아래로 배치.
    private const float VerticalAimBias = 0.18f;

    /// <summary>프리뷰 파라미터 지정. orthoSize/rt 크기에 0을 넘기면 기본값을 유지한다.</summary>
    public void Configure(GameObject prefab, RawImage image, float orthoSize, int rtWidth, int rtHeight, Color background)
    {
        _prefab = prefab;
        _image = image;
        if (orthoSize > 0f) _orthoSize = orthoSize;
        if (rtWidth > 0) _rtWidth = rtWidth;
        if (rtHeight > 0) _rtHeight = rtHeight;
        _background = background;
    }

    private void OnEnable()
    {
        if (_prefab == null || _image == null) return;
        EnsureRig();

        // 클론이 꺼져 있는 동안 targetPlayer를 세팅해야 SkinShell.OnEnable의 구독/동기화가 올바르게 맞물린다.
        if (_shell != null) _shell.targetPlayer = ResolvePlayerSkin();

        if (_rig != null) _rig.SetActive(true);
        if (_cam != null) _cam.enabled = true;

        // 활성화 직후 스킨 스프라이트가 확정된 상태에서 프레임을 잡는다.
        CaptureBounds();
        ApplyFraming();
    }

    private void OnDisable()
    {
        // 오버레이가 닫히면 렌더를 멈추고 클론을 재운다.
        if (_cam != null) _cam.enabled = false;
        if (_rig != null) _rig.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_rig != null) Destroy(_rig);
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
    }

    /// <summary>지금 즉시 실제 플레이어 스킨으로 다시 동기화(장비 변경 이벤트를 못 받는 경우 대비).</summary>
    public void SyncNow()
    {
        if (_shell == null) return;
        _shell.targetPlayer = ResolvePlayerSkin();
        _shell.SyncWithPlayer();
        CaptureBounds();  // 장비 교체로 실루엣이 바뀌면 프레임을 다시 잡는다
        ApplyFraming();
    }

    // 상자(RawImage) 비율이 바뀌면 RT·카메라 aspect를 맞추고(왜곡·베젤 제거),
    // 캐릭터를 상자 모양과 무관하게 항상 정중앙에 꽉 차게 프레이밍한다.
    private void LateUpdate()
    {
        if (_cam == null || _imageRect == null) return;
        var size = _imageRect.rect.size;
        if (size.x < 1f || size.y < 1f) return;

        float aspect = size.x / size.y;
        if (Mathf.Abs(aspect - _rtAspect) > 0.01f)
            ResizeRT(aspect);

        if (!_hasBounds) CaptureBounds();
        ApplyFraming();
    }

    // RT를 상자 비율에 맞춰 다시 만든다(세로 고정, 가로만 aspect로). 카메라 aspect도 같이 맞춰 왜곡 방지.
    private void ResizeRT(float aspect)
    {
        _rtAspect = aspect;
        int h = Mathf.Max(16, _rtHeight);
        int w = Mathf.Clamp(Mathf.RoundToInt(h * aspect), 16, 2048);

        if (_rt != null)
        {
            _cam.targetTexture = null;
            _rt.Release();
            Destroy(_rt);
        }

        _rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
        {
            name = "LivePlayerPreviewRT",
            antiAliasing = 1
        };
        _rt.Create();

        _cam.aspect = aspect;
        _cam.targetTexture = _rt;
        if (_image != null) _image.texture = _rt;
    }

    // 클론 스프라이트들의 합쳐진 바운즈를 "처음 유효할 때 한 번만" 캐시하고 잠근다.
    // 이후 장비 장착으로 실루엣이 커지거나 idle 호흡이 있어도 프레임 크기·위치가 고정된다
    // → 장비를 껴도 플레이어 크기가 바뀌지 않는다.
    private void CaptureBounds()
    {
        if (_hasBounds) return; // 한 번 잡으면 고정
        if (_clone == null) return;

        var renderers = _clone.GetComponentsInChildren<SpriteRenderer>(true);
        bool has = false;
        Bounds b = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (!r.enabled || r.sprite == null) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!has) return;

        // 클론 로컬 기준(스테이지 위치를 뺀 값)으로 저장 → 카메라가 스테이지 원점 기준으로 계산.
        b.center -= _stagePos;
        _cloneBounds = b;
        _hasBounds = true;
    }

    // 캐시된 바운즈 + 현재 상자 aspect로 카메라 줌·위치를 정해 캐릭터를 정중앙에 꽉 채운다.
    private void ApplyFraming()
    {
        if (_cam == null || !_hasBounds) return;

        var ext = _cloneBounds.extents;
        // 세로로도, 가로로도 다 담기는 orthoSize(=세로 반높이). 가로 제약은 aspect로 세로 환산.
        float aspect = _cam.aspect > 0.01f ? _cam.aspect : 1f;
        float halfForHeight = ext.y;
        float halfForWidth = ext.x / aspect;
        float ortho = Mathf.Max(halfForHeight, halfForWidth) * FramePadding;
        if (ortho > 0.001f) _cam.orthographicSize = ortho;

        // 가로는 클론 루트(캐릭터 피벗) 기준으로 고정 → 팔 동작·장비로 바운즈가 쏠려도 항상 정중앙.
        // 세로는 바운즈 중심보다 살짝 위를 조준 → 캐릭터가 아래로 내려가 머리가 가운데쯤 온다.
        float aimY = _cloneBounds.center.y + _cloneBounds.extents.y * VerticalAimBias;
        var c = _stagePos + new Vector3(0f, aimY, 0f);
        _cam.transform.position = new Vector3(c.x, c.y, _stagePos.z - 10f);
    }

    private void EnsureRig()
    {
        if (_built) return;
        _built = true;

        _stagePos = StageBase + new Vector3(50f * (s_stageCounter++), 0f, 0f);

        _rig = new GameObject("LivePlayerPreviewRig");
        _rig.SetActive(false); // 클론 OnEnable을 targetPlayer 세팅 뒤로 미루려고 꺼진 채로 조립
        DontDestroyOnLoad(_rig);

        // ── RenderTexture ──
        _rt = new RenderTexture(_rtWidth, _rtHeight, 16, RenderTextureFormat.ARGB32)
        {
            name = "LivePlayerPreviewRT",
            antiAliasing = 1
        };
        _rt.Create();

        // ── 전용 카메라 (그 층만 비추고 RT에 그린다) ──
        var camObj = new GameObject("PreviewCamera");
        camObj.transform.SetParent(_rig.transform, false);
        camObj.transform.position = _stagePos + new Vector3(0f, 0f, -10f);

        _cam = camObj.AddComponent<Camera>();
        _cam.orthographic = true;
        _cam.orthographicSize = _orthoSize;
        _cam.aspect = (float)_rtWidth / _rtHeight;
        _rtAspect = (float)_rtWidth / _rtHeight;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        // 투명 배경으로 클리어 → RawImage(RT) 뒤의 상자 배경 스프라이트가 그대로 비친다.
        _cam.backgroundColor = new Color(_background.r, _background.g, _background.b, 0f);
        _cam.cullingMask = 1 << PreviewLayer;
        _cam.targetTexture = _rt;
        _cam.nearClipPlane = 0.01f;
        _cam.farClipPlane = 100f;
        _cam.allowMSAA = false;
        _cam.allowHDR = false;

        // URP 카메라 부가 데이터 — 기본 Base 카메라, 후처리 불필요.
        var urp = camObj.GetComponent<UniversalAdditionalCameraData>();
        if (urp == null) urp = camObj.AddComponent<UniversalAdditionalCameraData>();
        urp.renderType = CameraRenderType.Base;
        urp.renderPostProcessing = false;

        // ── UiPlayer 클론 ──
        var clone = Instantiate(_prefab, _stagePos, Quaternion.identity, _rig.transform);
        _clone = clone;
        clone.name = "UiPlayerPreviewClone";
        clone.tag = "Untagged"; // 프리팹 루트 태그가 "Player"라 그대로 두면 FindWithTag가 오인한다
        SetLayerRecursive(clone, PreviewLayer);
        SwapToUnlit(clone);
        _shell = clone.GetComponentInChildren<SkinShell>(true);

        // 오버레이가 Time.timeScale=0으로 게임을 멈춰도 숨쉬는 idle 모션이 계속 돌게 한다.
        // (Normal 모드면 timeScale 0에서 애니메이터가 얼어붙는다)
        var anim = clone.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            anim.updateMode = AnimatorUpdateMode.UnscaledTime;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        _image.texture = _rt;
        _image.color = Color.white;
        _imageRect = _image.rectTransform;
    }

    /// <summary>실제 플레이어(클론이 아닌) 본체의 SkinManager. 클론엔 SkinShell만 있어 혼동되지 않는다.</summary>
    private SkinManager ResolvePlayerSkin() =>
        Object.FindFirstObjectByType<SkinManager>(FindObjectsInactive.Include);

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i).gameObject, layer);
    }

    private static void SwapToUnlit(GameObject clone)
    {
        var mat = UnlitMat;
        if (mat == null) return;
        var renderers = clone.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sharedMaterial = mat;
    }

    private static Material UnlitMat
    {
        get
        {
            if (s_unlitMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                if (sh != null) s_unlitMat = new Material(sh) { name = "LivePlayerPreviewUnlit" };
            }
            return s_unlitMat;
        }
    }
}
