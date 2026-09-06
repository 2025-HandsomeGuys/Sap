using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 중심의 원형 시야 오버레이 시스템 (Canvas UI 기반)
/// 시야 범위 밖을 어둡게 만들어 플레이어가 볼 수 있는 영역을 제한
/// </summary>
public class PlayerVisionOverlay : MonoBehaviour
{
    [Header("Vision Settings")]
    [Tooltip("플레이어 Transform")]
    public Transform playerTransform;
    
    [Tooltip("시야 반경 (타일 단위). 실제 적용값은 프리팹·씬 직렬화 값이 우선한다 — 여기 기본값은 새로 붙이는 인스턴스에만 적용됨")]
    public float visionRadiusTiles = 0.45f;
    
    [Tooltip("시야 밖 어둠 투명도 (0=투명, 1=완전 불투명)")]
    [Range(0f, 1f)]
    public float darknessAlpha = 0.3f;
    
    [Tooltip("부드러운 전환 범위 (타일 단위)")]
    public float falloffTiles = 0.3f;
    
    [Tooltip("어둠 색상")]
    public Color darknessColor = Color.black;

    [Header("References")]
    [Tooltip("비네트 머티리얼 (RadialVignette 셰이더 사용)")]
    public Material vignetteMaterial;

    [Header("강제 활성(맹인 유물) 시 정렬")]
    // 어둠막 캔버스는 평소 Default 레이어 / order 999로 그려진다. 지하는 지형이 전부 Default라
    // 이걸로 충분하지만, 지상 씬은 배경·나무·타일맵이 BackGround/Objects/player 레이어에 있어
    // 어둠막보다 나중에 그려진다 → 어둠막이 완전히 가려진다. 강제 활성 중에만 아래 값으로 올린다.
    [Tooltip("강제 활성 중 어둠막을 올릴 정렬 레이어. 지상 배경이 쓰는 레이어보다 위여야 한다.")]
    public string forcedSortingLayer = "player";
    [Tooltip("강제 활성 중 어둠막 sortingOrder")]
    public int forcedSortingOrder = 999;
    [Tooltip("강제 활성 중 플레이어 스프라이트를 어둠막 위로 끌어올릴 오프셋. 0이면 플레이어도 어둠에 잠긴다 (지하에서는 플레이어가 어둠 위에 보이므로 기본값 유지 권장)")]
    public int forcedPlayerOrderBoost = 2000;

    private Canvas overlayCanvas;
    private RawImage baseImage;
    private RectTransform overlayRect;
    private Camera mainCamera;
    private float tileSize = 1f;
    private PlayerStat _playerStat;

    // 유물 등 외부 시스템이 바깥 어둠 강도를 일시적으로 덮어쓸 때 사용(맹인 유물 등).
    // null이면 인스펙터의 darknessAlpha를 사용. 설정 시 매 프레임 이 값이 우선.
    private float? _darknessOverride;

    // 캔버스 생성 완료 여부. Start가 카메라를 못 잡아도 Update에서 계속 재시도한다
    // (지상 씬처럼 오버레이가 꺼진 채로 시작했다가 나중에 강제로 켜지는 경로 대응).
    private bool _setupDone;

    // 맹인 유물처럼 "이 씬에서 오버레이가 꺼져 있어도 무조건 켜야 하는" 요구.
    // 지상 씬은 시야 제한이 필요 없어 오버레이를 꺼두지만, 맹인 유물은 지상에서도 캄캄해야 한다.
    private bool _forcedActive;
    private bool _stateBeforeForce; // 강제 해제 시 되돌릴 원래 캔버스 상태

    // 강제 활성 중 정렬을 바꾼 대상들 — 해제 시 그대로 되돌린다.
    private int _canvasLayerBeforeForce;
    private int _canvasOrderBeforeForce;
    private SpriteRenderer[] _boostedRenderers;
    private int[] _boostedOrders;

    /// <summary>바깥 어둠 알파를 외부에서 강제(0=투명, 1=완전 암흑). 맹인 유물이 파동 엔벨로프에 맞춰 호출.</summary>
    public void SetDarknessOverride(float alpha) => _darknessOverride = Mathf.Clamp01(alpha);

    /// <summary>어둠 오버라이드 해제 → 기본 darknessAlpha로 복귀.</summary>
    public void ClearDarknessOverride() => _darknessOverride = null;

    /// <summary>
    /// 시야 오버레이 표시 on/off. 던전처럼 시야 제한이 필요 없는 공간에서 끈다.
    /// 컴포넌트를 비활성화하면 마지막 렌더 상태가 화면에 남으므로 생성한 Canvas를 직접 켜고 끈다.
    /// </summary>
    public void SetOverlayActive(bool active)
    {
        if (overlayCanvas != null && overlayCanvas.gameObject.activeSelf != active)
            overlayCanvas.gameObject.SetActive(active);
    }

    /// <summary>
    /// 외부에서 시야 오버레이를 강제로 켠다(맹인 유물). 오브젝트·컴포넌트·캔버스가 모두
    /// 꺼져 있어도 되살리며, 아직 캔버스가 없으면 <see cref="EnsureSetup"/>가 만든 직후 반영된다.
    /// false로 되돌리면 강제 직전 상태로 복원한다.
    /// </summary>
    public void SetForcedActive(bool on)
    {
        if (on == _forcedActive) return;

        if (on)
        {
            _stateBeforeForce = gameObject.activeSelf && enabled
                                && overlayCanvas != null && overlayCanvas.gameObject.activeSelf;
            _forcedActive = true;

            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (!enabled) enabled = true;
            SetOverlayActive(true);
            ApplyForcedSorting(true); // 캔버스가 아직 없으면 EnsureSetup가 생성 직후 다시 호출한다
        }
        else
        {
            ApplyForcedSorting(false);
            _forcedActive = false;
            SetOverlayActive(_stateBeforeForce);
        }
    }

    /// <summary>
    /// 강제 활성 중 어둠막을 지상 배경보다 위 정렬로 올리고, 플레이어 스프라이트는 그 위로 끌어올린다.
    /// (지하는 지형이 Default 레이어라 원래 값으로도 덮이지만, 지상은 배경이 상위 레이어에 있어 필요하다)
    /// </summary>
    void ApplyForcedSorting(bool on)
    {
        if (overlayCanvas != null)
        {
            if (on)
            {
                _canvasLayerBeforeForce = overlayCanvas.sortingLayerID;
                _canvasOrderBeforeForce = overlayCanvas.sortingOrder;

                // 지하 모드에서는 플레이어가 지형과 같은 Default 레이어로 내려가 있다. 이때 어둠막만
                // 상위 레이어(player)로 올리면 order 부스트로도 플레이어를 어둠 위로 못 꺼낸다
                // (정렬 레이어가 order보다 우선). → 어둠막은 항상 플레이어와 같은 레이어에 둔다.
                string targetLayer = forcedSortingLayer;
                var sorting = PlayerSortingController.Instance;
                if (sorting != null && sorting.IsUnderground) targetLayer = sorting.UndergroundSortingLayer;

                if (IsValidSortingLayer(targetLayer))
                    overlayCanvas.sortingLayerName = targetLayer;
                else if (!string.IsNullOrEmpty(targetLayer))
                    Debug.LogWarning($"[PlayerVisionOverlay] 정렬 레이어 '{targetLayer}'가 없습니다 — 기본 레이어 유지");

                overlayCanvas.sortingOrder = forcedSortingOrder;
            }
            else
            {
                overlayCanvas.sortingLayerID = _canvasLayerBeforeForce;
                overlayCanvas.sortingOrder = _canvasOrderBeforeForce;
            }
        }

        BoostPlayerRenderers(on);
    }

    static bool IsValidSortingLayer(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var layers = SortingLayer.layers;
        for (int i = 0; i < layers.Length; i++)
            if (layers[i].name == name) return true;
        return false;
    }

    // 어둠막을 배경 위로 올리면 플레이어까지 잠기므로, 강제 활성 동안만 플레이어 스프라이트 order를 끌어올린다.
    void BoostPlayerRenderers(bool on)
    {
        // 플레이어 sortingOrder의 단일 소유자는 PlayerSortingController(지상/지하 정렬 모드)다.
        // 여기서 직접 order를 저장·복원하면 부스트 도중 모드가 바뀔 때 캡처값이 굳어 오염된다.
        var sorting = PlayerSortingController.Instance;
        if (sorting != null)
        {
            sorting.SetExtraOrderBoost(on ? forcedPlayerOrderBoost : 0);
            return;
        }

        if (on)
        {
            if (_boostedRenderers != null || forcedPlayerOrderBoost == 0 || playerTransform == null) return;

            var rs = playerTransform.GetComponentsInChildren<SpriteRenderer>(true);
            _boostedRenderers = rs;
            _boostedOrders = new int[rs.Length];
            for (int i = 0; i < rs.Length; i++)
            {
                _boostedOrders[i] = rs[i].sortingOrder;
                rs[i].sortingOrder += forcedPlayerOrderBoost;
            }
        }
        else
        {
            if (_boostedRenderers == null) return;
            for (int i = 0; i < _boostedRenderers.Length; i++)
                if (_boostedRenderers[i] != null) _boostedRenderers[i].sortingOrder = _boostedOrders[i];

            _boostedRenderers = null;
            _boostedOrders = null;
        }
    }

    void Awake()
    {
        // 카메라가 아직 없어도 컴포넌트를 죽이지 않는다 — Update/EnsureSetup이 계속 재시도한다.
        // (예전에는 여기서 enabled=false로 꺼버려, 카메라가 뒤늦게 생기는 씬에서 오버레이가 영영 안 살아났다)
        mainCamera = Camera.main;
    }

    void Start()
    {
        EnsureSetup();
    }

    /// <summary>
    /// 캔버스·머티리얼 1회 생성. 카메라가 없으면 false를 반환하고 다음 프레임에 다시 시도한다.
    /// </summary>
    bool EnsureSetup()
    {
        if (_setupDone) return true;

        if (playerTransform == null)
        {
            Debug.LogError("[PlayerVisionOverlay] Player Transform이 할당되지 않았습니다!");
            enabled = false;
            return false;
        }

        if (mainCamera == null || !mainCamera.gameObject.activeInHierarchy)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return false; // 이번 프레임은 포기, 다음 프레임 재시도
        }

        _playerStat = playerTransform.GetComponent<PlayerStat>();

        SetupOverlay();
        _setupDone = true;

        if (_forcedActive) // 캔버스 생성 전에 강제 요청이 들어온 경우 — 여기서 마저 반영
        {
            SetOverlayActive(true);
            ApplyForcedSorting(true);
        }

        UpdateMaterialProperties();
        return true;
    }

    void SetupOverlay()
    {
        // Canvas 생성
        GameObject canvasObj = new GameObject("VisionOverlayCanvas");
        canvasObj.transform.SetParent(transform, false);
        canvasObj.transform.localPosition = Vector3.zero;
        
        overlayCanvas = canvasObj.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.WorldSpace;
        overlayCanvas.sortingOrder = 999; // UI 바로 아래

        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        if (canvasRect != null)
        {
            float height = mainCamera.orthographicSize * 2f;
            float width = height * mainCamera.aspect;
            canvasRect.sizeDelta = new Vector2(width * 100f, height * 100f);
            canvasRect.position = new Vector3(0, 0, 10f);
        }

        Texture2D whiteTexture = new Texture2D(1, 1);
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply();

        // 원형 시야 오버레이 (전체 화면에 그림)
        GameObject baseObj = new GameObject("BaseVisionOverlay");
        baseObj.transform.SetParent(canvasObj.transform, false);
        baseImage = baseObj.AddComponent<RawImage>();
        overlayRect = baseObj.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        baseImage.texture = whiteTexture;

        if (vignetteMaterial != null)
        {
            Material baseMat = new Material(vignetteMaterial);
            baseMat.SetFloat("_UseCone", 0f);
            baseImage.material = baseMat;
        }
    }

    void Update()
    {
        if (playerTransform == null) return;

        // [수정] 씬 전환 시 기존 메인 카메라가 파괴되거나 비활성될 수 있으므로, 없으면 다시 찾음
        if (mainCamera == null || !mainCamera.gameObject.activeInHierarchy)
        {
            mainCamera = Camera.main;
            // 그래도 없으면 이번 프레임은 스킵
            if (mainCamera == null) return;
        }

        if (!EnsureSetup()) return; // Start 시점에 카메라가 없었던 경우 여기서 늦게 생성

        if (overlayCanvas != null && overlayCanvas.renderMode == RenderMode.WorldSpace)
        {
            RectTransform canvasRect = overlayCanvas.GetComponent<RectTransform>();
            if (canvasRect != null)
            {
                canvasRect.position = new Vector3(mainCamera.transform.position.x, mainCamera.transform.position.y, 10f);
            }
        }

        UpdateMaterialProperties();
    }

    void UpdateMaterialProperties()
    {
        if (baseImage != null && baseImage.material != null)
            ApplyPropertiesToMaterial(baseImage.material);
    }

    void ApplyPropertiesToMaterial(Material mat)
    {
        float finalVisionRadius = visionRadiusTiles;
        if (_playerStat != null)
        {
            finalVisionRadius *= _playerStat.GetFinalValue(StatType.VisionRadiusUp);
        }

        // 월드 좌표 기준으로 전달
        if (mat.HasProperty("_VisionRadius"))
        {
            mat.SetFloat("_VisionRadius", finalVisionRadius * tileSize);
        }

        if (mat.HasProperty("_FalloffRange"))
        {
            mat.SetFloat("_FalloffRange", falloffTiles * tileSize);
        }

        if (mat.HasProperty("_DarknessAlpha"))
        {
            mat.SetFloat("_DarknessAlpha", _darknessOverride ?? darknessAlpha);
        }

        if (mat.HasProperty("_DarknessColor"))
        {
            mat.SetColor("_DarknessColor", darknessColor);
        }

        if (mat.HasProperty("_PlayerPosition"))
        {
            // 월드 좌표 직접 전달
            mat.SetVector("_PlayerPosition", new Vector4(
                playerTransform.position.x,
                playerTransform.position.y,
                0, 0
            ));
        }

        // 카메라 정보 전달
        if (mat.HasProperty("_CameraPosition"))
        {
            mat.SetVector("_CameraPosition", new Vector4(
                mainCamera.transform.position.x,
                mainCamera.transform.position.y,
                0, 0
            ));
        }

        if (mat.HasProperty("_CameraSize"))
        {
            mat.SetFloat("_CameraSize", mainCamera.orthographicSize);
        }
        
    }

    /// <summary>
    /// 타일 크기 설정 (외부에서 호출)
    /// </summary>
    public void SetTileSize(float size)
    {
        tileSize = size;
        UpdateMaterialProperties();
    }

    void OnValidate()
    {
        // 에디터에서 값 변경 시 즉시 반영
        if (Application.isPlaying && baseImage != null)
        {
            UpdateMaterialProperties();
        }
    }
    
    // [NEW] 모든 활성 청크의 조명 정보 수집 (미사용 - 추후 구현용)
    void CollectChunkLightData()
    {
        // 방법 2 구현 시 사용
        // 현재는 셰이더 블렌딩으로 처리
    }

    void OnDestroy()
    {
        BoostPlayerRenderers(false); // 강제 활성 중 죽어도 플레이어 정렬은 원복

        if (overlayCanvas != null && overlayCanvas.gameObject != null)
        {
            Destroy(overlayCanvas.gameObject);
        }
    }
}
