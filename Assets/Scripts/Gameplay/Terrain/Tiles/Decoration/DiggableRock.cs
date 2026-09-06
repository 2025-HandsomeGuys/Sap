// @tags: rock, digging, decoration, collider, mineral, loot, special-chunk
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(PolygonCollider2D))]
public class DiggableRock : MonoBehaviour, IDiggable, IMapRock
{
    private SpriteRenderer _spriteRenderer;
    private PolygonCollider2D _polyCollider;

    // 광물돌(MineralRock)이 같은 GameObject에 붙어있는지 — 지도 채움 색 구분용. Awake에서 캐시.
    private bool _isMineralRock;
    private TerrainChunk _chunk;
    private Color32[] _mask;
    private int _maskWidth, _maskHeight;
    private Vector2? _maskPivot;
    
    [Header("Hidden Status")]
    [Tooltip("If true, the collider is disabled until Reveal() is called.")]
    public bool isHidden = false;

    [Tooltip("true 시 Start()에서 부모 TerrainChunk에 자동 등록. 청크 프리팹에 직접 배치한 경우 사용.")]
    public bool selfRegister = false;

    [Tooltip("true 시 Start/OnEnable에서 Renderer·Collider를 숨기지 않음. 이미 비어있는 공동 안에 배치된 봉투처럼 처음부터 노출 상태인 경우 사용.")]
    public bool preExposed = false;

    // RevealInTerrain() 또는 RestoreState()가 호출된 적 있으면 true.
    // OnEnable()에서 노출 상태를 덮어쓰지 않도록 보호.
    private bool _isRevealed = false;

    // [Fix] 풀 반납 시 올바른 키로 돌아가도록 스프라이트 인스턴스 ID를 캐시
    [HideInInspector] public int poolKey;

    // [Save] 이 암석이 속한 rockPrefabs[] 인덱스 — 언로드 시 위치와 함께 저장
    [HideInInspector] public int spriteSetIndex = -1;

    [Header("Reveal Settings")]
    [Tooltip("이 픽셀 수 이상이 공기에 노출돼야 바위가 드러남 (0 = 즉시 드러남)")]
    public int minExposedPixels = 15;

    // ============================================================================================================
    //  TILE TYPE (드롭 광물 결정)
    // ============================================================================================================
    [Header("Drop Settings")]
    public TileType tileType = TileType.HardStone; // RockSpawner에서 지층 정보 주입

    // ============================================================================================================
    //  HP CONFIGURATION
    // ============================================================================================================
    [Header("HP Configuration")]
    [Tooltip("이 돌의 최대 HP. 크기(대/중/소)에 따른 차이는 프리팹마다 이 값을 직접 넣어 만든다. 크기 랜덤 배율은 없으므로 여기 적은 값이 그대로 MaxHp가 된다.")]
    public float baseHp = 5f;

    [System.NonSerialized] public float MaxHp = 5f;  // Awake()에서 baseHp로 설정(광물돌만 예외).
    private float _currentHp;

    // 프리팹이 정의한 크기 티어(RockBreakVFX.tier). 광물 드롭 개수의 기준. Awake에서 캐시.
    private RockSizeTier _tier = RockSizeTier.Small;

    /// <summary>이 돌의 크기 티어. 프리팹의 RockBreakVFX가 단일 진실 소스다.</summary>
    public RockSizeTier Tier => _tier;

    // 스폰 시 RockSpawner가 주입하는 시각 스케일. 크기 랜덤이 제거돼 현재는 항상 1이며
    // (구버전 세이브 복원만 예외) 마스크·조각 VFX 크기에만 쓰인다. HP·드롭에는 쓰지 않는다.
    [System.NonSerialized] public float SizeScale = 1f;

    // ============================================================================================================
    //  HP RECOVERY
    // ============================================================================================================
    [Header("HP Recovery")]
    [Tooltip("마지막 데미지 후 회복이 시작되기까지 대기 시간 (초)")]
    public float recoveryDelay = 10f;

    [Tooltip("회복 시작 후 초당 HP 회복량")]
    public float recoveryRate = 1f;

    private float _lastDamageTime = float.NegativeInfinity;

    // 저장용 Getter
    public float CurrentHp      => _currentHp;
    public float LastDamageTime => _lastDamageTime;
    public bool  IsRevealed     => _isRevealed;

    // ============================================================================================================
    //  DAMAGE STAGE LISTENERS (IDamageStageable)
    //  DIP: DiggableRock은 IDamageStageable 인터페이스만 알고, DamageStagedVisuals 등 구체 타입을 모른다.
    // ============================================================================================================
    private IDamageStageable[] _stageListeners;

    // ============================================================================================================
    //  HIT REACTORS (IRockHitReactor)
    //  "지금 맞았다"에만 반응하는 구독체(RockHitAnimator 등). IDamageStageable과 달리
    //  스폰·풀 재사용·HP 회복 때는 호출되지 않는다.
    // ============================================================================================================
    private IRockHitReactor[] _hitReactors;

    // ============================================================================================================
    //  배치 확정값 (PLACEMENT)
    //  RockSpawner가 주입하는 "이 돌이 지형에서 차지하는 자리"의 단일 진실 소스.
    //
    //  transform을 대신 읽으면 안 된다 — RockHitAnimator가 히트 연출로 transform을
    //  흔드는 동안 구멍이 엉뚱한 곳에 뚫리고(DestroyRock), 그 순간 청크가 언로드되면
    //  흔들림 오프셋이 세이브에 영구히 구워진다(WorldPersistenceSystem).
    // ============================================================================================================
    private Vector2 _anchorLocal;   // 청크 로컬 좌표(유닛). 픽셀 변환의 기준점
    private float   _baseAngleZ;    // 배치 단계에서 확정된 Z 회전(도)
    private bool    _hasPlacement;

    /// <summary>배치 단계에서 확정된 Z 회전(도). 세이브가 transform 대신 이 값을 쓴다.</summary>
    public float BaseAngleZ => _hasPlacement ? _baseAngleZ : transform.localEulerAngles.z;

    /// <summary>
    /// RockSpawner가 위치·회전을 확정한 직후 주입한다.
    /// 프리팹에 직접 배치된 돌은 호출되지 않고, Start()에서 자기 transform으로 자동 확정된다.
    /// </summary>
    public void SetPlacement(Vector2 anchorLocal, float baseAngleZ)
    {
        _anchorLocal  = anchorLocal;
        _baseAngleZ   = baseAngleZ;
        _hasPlacement = true;
    }

    /// <summary>픽셀 좌표 계산의 기준점. 흔들림에 영향받지 않는다.</summary>
    private Vector2 AnchorLocal => _hasPlacement ? _anchorLocal : (Vector2)transform.localPosition;

    // 청크 픽셀 좌표 기준 바위 경계 — 회전 전 원본 w×h 사각형.
    // 스폰 위치 앵커(bounds.x + sprite.pivot.x)와 세이브(RockSaveEntry.boundsX/Y/W/H) 전용.
    // 스프라이트 pivot이 좌하단이라 실제 회전된 돌과 최대 130px까지 어긋나므로
    // 노출 판정에는 쓰지 말 것 — 그 용도는 RevealBounds.
    [HideInInspector] public RectInt PixelBoundsInChunk;

    // 회전·스케일이 적용된 실제 마스크가 청크 픽셀 좌표에서 차지하는 사각형. SetMask()에서 갱신.
    [HideInInspector] public RectInt MaskBoundsInChunk;

    /// <summary>
    /// 노출(Reveal) 판정에 쓰는 실제 footprint.
    /// 마스크가 주입된 일반 암석은 회전 적용된 사각형, 마스크가 없는 프리팹 직접 배치 암석은 원본 사각형.
    /// </summary>
    public RectInt RevealBounds =>
        MaskBoundsInChunk.width > 0 ? MaskBoundsInChunk : PixelBoundsInChunk;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _polyCollider = GetComponent<PolygonCollider2D>();
        // OCP/DIP: 같은 GameObject의 모든 IDamageStageable 구현체를 수집
        _stageListeners = GetComponents<IDamageStageable>();
        // 히트 순간에만 반응하는 구독체. 비활성 컴포넌트도 수집된다
        // (RockHitAnimator는 평소 비활성이지만 메서드 직접 호출은 정상 동작).
        _hitReactors = GetComponents<IRockHitReactor>();
        _isMineralRock = GetComponent<MineralRock>() != null;

        // 크기 티어는 프리팹이 정의한다. VFX 컴포넌트가 없으면 소 티어로 본다.
        var breakVfx = GetComponent<RockBreakVFX>();
        if (breakVfx != null) _tier = breakVfx.tier;

        // 광물돌은 specialChunkSettings.json이 MaxHp를 광물별로 주입한다(MineralRock.Awake, -140).
        // 그 값을 덮지 않도록 일반 돌만 프리팹 값을 반영한다.
        if (!_isMineralRock)
            MaxHp = baseHp;
    }

    // ============================================================================================================
    //  IMapRock: 노출된 동안 지도에 '안 파진 땅'처럼 그 자리를 채워 그린다 (파괴 시 자연 소멸)
    // ============================================================================================================
    Vector2 IMapRock.MapWorldCenter =>
        _spriteRenderer != null ? (Vector2)_spriteRenderer.bounds.center : (Vector2)transform.position;

    float IMapRock.MapWorldRadius =>
        _spriteRenderer != null
            ? Mathf.Max(0.15f, (_spriteRenderer.bounds.extents.x + _spriteRenderer.bounds.extents.y) * 0.5f)
            : 0.4f;

    bool IMapRock.IsMineralRock => _isMineralRock;

    // 회전·스케일이 적용된 마스크가 있으면 실제 돌 모양을 지도에 그린다. 없으면(프리팹 직접 배치 등) 원반 폴백.
    bool IMapRock.HasMapShape => _mask != null && _chunk != null && _spriteRenderer != null;

    Rect IMapRock.MapWorldBounds
    {
        get
        {
            if (_spriteRenderer == null) return new Rect(transform.position.x - 0.4f, transform.position.y - 0.4f, 0.8f, 0.8f);
            var b = _spriteRenderer.bounds;
            return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
        }
    }

    bool IMapRock.ContainsWorldPoint(Vector2 world)
    {
        if (_mask == null || _chunk == null) return false;
        // 월드 → 청크 픽셀 (CountExposedRockPixels/ClearHole과 동일 좌표계) → 마스크 인덱스
        Vector2 chunkPos = _chunk.transform.position;
        float ppu = _chunk.PPU;
        int cpx = Mathf.FloorToInt((world.x - chunkPos.x) * ppu);
        int cpy = Mathf.FloorToInt((world.y - chunkPos.y) * ppu);
        RectInt b = MaskBoundsInChunk; // (startX, startY, maskW, maskH)
        int mx = cpx - b.xMin;
        int my = cpy - b.yMin;
        if ((uint)mx >= (uint)_maskWidth || (uint)my >= (uint)_maskHeight) return false;
        return _mask[my * _maskWidth + mx].a > 10;
    }

    /// <summary>
    /// 회전·스케일이 적용된 마스크를 주입한다.
    /// 호출 전에 AssignChunk()와 transform.localPosition이 확정돼 있어야 한다
    /// (여기서 노출 판정용 MaskBoundsInChunk를 계산하므로).
    /// </summary>
    public void SetMask(Color32[] mask, int width, int height, Vector2? pivotOverride = null)
    {
        _mask = mask;
        _maskWidth = width;
        _maskHeight = height;
        _maskPivot = pivotOverride;

        MaskBoundsInChunk = GetMaskBoundsInChunk();
    }

    /// <summary>
    /// RockSpawner가 스폰/재사용 시 소속 청크를 주입한다.
    /// 풀 재사용 시 Start()가 재실행되지 않아 _chunk가 이전 청크를 가리키는
    /// stale 버그를 방지한다. (구멍이 엉뚱한 청크에 뚫려 돌과 위치가 어긋나던 문제)
    /// </summary>
    public void AssignChunk(TerrainChunk chunk) => _chunk = chunk;

    private Vector2 GetEffectivePivot() =>
        _maskPivot ?? (_spriteRenderer?.sprite != null ? _spriteRenderer.sprite.pivot : Vector2.zero);

    /// <summary>
    /// RockSpawner가 스폰/재사용 시 시각 스케일을 주입하고 HP를 가득 채운다.
    /// HP·드롭 개수는 이 값이 아니라 프리팹 티어(RockTierStats)가 정한다 — 스케일은
    /// 마스크·조각 VFX 크기 전용이며, 크기 랜덤 제거 후 신규 스폰은 항상 1이다.
    /// 풀 재사용 시 OnEnable()이 이전 상태로 _currentHp를 채운 뒤 호출되므로,
    /// 반드시 여기서 _currentHp를 다시 채워야 한다.
    /// </summary>
    public void ApplySizeScale(float scale)
    {
        SizeScale = scale <= 0f ? 1f : scale;
        _currentHp = MaxHp;
        _lastDamageTime = float.NegativeInfinity;

        if (_stageListeners != null)
            foreach (var s in _stageListeners) s.OnHpRatioChanged(1f);
    }

    private void Start()
    {
        _chunk = GetComponentInParent<TerrainChunk>();

        // 프리팹·씬에 직접 배치된 돌은 RockSpawner를 거치지 않아 배치값이 주입되지 않는다.
        // 히트 연출이 transform을 흔들기 전에 여기서 확정해 둔다.
        if (!_hasPlacement)
            SetPlacement(transform.localPosition, transform.localEulerAngles.z);

        // HP는 OnEnable()에서 초기화. RestoreState() 이후 Start()가 실행돼도 덮어쓰지 않음.

        // 스폰 시 땅에 묻혀 있으므로 콜라이더 및 렌더러 비활성화 (RevealInTerrain() 호출 시 활성화)
        // preExposed=true 또는 RestoreState()로 이미 노출 복원된 경우에는 숨기지 않음
        if (!preExposed && !_isRevealed)
        {
            if (_polyCollider != null)
                _polyCollider.enabled = false;
            // 지형 텍스처 경계면 근처 air 픽셀을 통해 보이는 문제 방지
            if (_spriteRenderer != null)
                _spriteRenderer.enabled = false;
        }

        if (selfRegister)
        {
            if (_chunk != null)
            {
                Rect    r     = _spriteRenderer.sprite.textureRect;
                Vector2 pivot = _spriteRenderer.sprite.pivot;
                int cx = Mathf.RoundToInt(AnchorLocal.x * _chunk.PPU);
                int cy = Mathf.RoundToInt(AnchorLocal.y * _chunk.PPU);
                int sx = cx - Mathf.RoundToInt(pivot.x);
                int sy = cy - Mathf.RoundToInt(pivot.y);
                PixelBoundsInChunk = new RectInt(sx, sy, (int)r.width, (int)r.height);
                _chunk.AddSpawnedRock(this);
                _chunk.AddRockBound(new Rect(sx, sy, (int)r.width, (int)r.height));
            }
            else
            {
                Debug.LogWarning($"[DiggableRock] selfRegister=true 이지만 부모에 TerrainChunk 가 없습니다 — {gameObject.name}");
            }
        }

        // [Fix] 청크 재로드 후 이미 노출됐어야 할 암석 자동 복원
        // RevealInTerrain() 내부의 CountExposedRockPixels()가 실제 지형 픽셀을 확인해 게이팅함
        if (!preExposed)
            RevealInTerrain();
    }

    private void OnEnable()
    {
        // 풀에서 재사용될 때 HP와 비주얼 초기화
        if (_stageListeners != null)
        {
            _currentHp = MaxHp;
            _lastDamageTime = float.NegativeInfinity;
            // 풀HP(1.0)로 브로드캐스트 → DamageStagedVisuals가 정상 스프라이트로 복원
            foreach (var s in _stageListeners) s.OnHpRatioChanged(1f);
        }

        // 재사용 시에도 묻힌 상태로 시작 (preExposed 또는 이미 노출된 상태면 유지)
        if (!preExposed && !_isRevealed)
        {
            if (_polyCollider != null)
                _polyCollider.enabled = false;
            if (_spriteRenderer != null)
                _spriteRenderer.enabled = false;
        }

        // 처음부터 노출된 돌(공동 안 배치 등)은 즉시 지도 실루엣 스냅샷.
        if (preExposed)
            MapRockCache.Capture(this);
    }

    private void OnDisable()
    {
        // 풀 반납 시 노출 상태 초기화 → 다음 스폰에서 묻힌 상태로 시작
        _isRevealed = false;
        // 언로드(풀 반납)만으로는 지도에서 지우지 않는다 — 안 캔 돌은 멀어져도 스냅샷으로 남는다.
        // 실제로 캐졌을 때(DestroyRock)만 MapRockCache.Remove로 지운다.
    }

    // ============================================================================================================
    //  IDiggable: HP 기반 파괴
    // ============================================================================================================
    /// <summary>
    /// 유물 빔(플라즈마 커터 등)이 원거리로 바위를 녹일 때 쓰는 toolIndex.
    /// 도구 슬롯 인덱스(0~3)와 겹치지 않는 값 — 유물은 ToolController의 도구가 아니다.
    /// </summary>
    public const int ToolIndexRelicBeam = 10;

    public void Dig(Vector2 worldPos, float damage, int toolIndex)
    {
        // 삽(1)·곡괭이(2)·드릴(3)·유물 빔만 바위에 데미지를 줄 수 있음.
        // 삽이 실제로 돌을 캘 수 있는지는 ToolCapabilities(툴스왑 유물)가 판정하며,
        // 호출부가 DigParameters.CanDigRock으로 이미 게이팅한다. 여기서는 맨손(0)만 거른다.
        if (toolIndex != 1 && toolIndex != 2 && toolIndex != 3 && toolIndex != ToolIndexRelicBeam) return;

        // damage에 이미 도구별 BaseDamage * PlayerStat 배율이 계산되어 있음
        _currentHp -= damage;
        _lastDamageTime = Time.time;

        float ratio = Mathf.Max(_currentHp, 0f) / MaxHp;
        string stage = ratio > 0.66f ? "정상" : (ratio > 0.33f ? "균열1" : "균열2");
        //Debug.Log($"[DiggableRock] HP={_currentHp:F1}/{MaxHp} ({ratio * 100f:F0}%) [{stage}]");

        // OCP/DIP: IDamageStageable 구독체에 브로드캐스트 (DamageStagedVisuals 등)
        foreach (var listener in _stageListeners)
            listener.OnHpRatioChanged(ratio);

        if (_currentHp <= 0f)
        {
            DestroyRock();
            return;
        }

        // 치명타가 아닐 때만 히트 연출. 파괴되는 순간은 RockBreakVFX가 대신한다.
        if (_hitReactors != null)
            foreach (var reactor in _hitReactors)
                reactor.OnRockHit(worldPos, damage);
    }

    // 균열 스프라이트는 이제 프리팹의 DamageStagedVisuals.stages[]에 직접 들어간다.
    // 코드 주입(구 SetCrackSprites)은 제거됐다 — 스포너가 스프라이트를 알 이유가 없어졌다.

    /// <summary>
    /// 회전·스케일이 적용된 실제 마스크가 청크 픽셀 좌표에서 차지하는 사각형을 계산한다.
    /// 좌표 수식은 CountExposedRockPixels()·TerrainCarver.ClearHole()과 동일해야 한다.
    /// </summary>
    private RectInt GetMaskBoundsInChunk()
    {
        if (_chunk == null || _mask == null) return new RectInt(0, 0, 0, 0);

        Vector2 localPos = AnchorLocal;
        int cx = Mathf.RoundToInt(localPos.x * _chunk.PPU);
        int cy = Mathf.RoundToInt(localPos.y * _chunk.PPU);
        Vector2 pivot = GetEffectivePivot();

        return new RectInt(
            cx - Mathf.RoundToInt(pivot.x),
            cy - Mathf.RoundToInt(pivot.y),
            _maskWidth, _maskHeight);
    }

    // 노출 시점 검증용 로그 스위치. 확인 끝나면 false로 끄거나 이 로그를 제거할 것.
    public static bool LogRevealTiming = true;

    /// <summary>
    /// 현재 terrain에서 이 바위의 픽셀 영역 중 air(투명)인 픽셀 수를 반환한다.
    /// </summary>
    private int CountExposedRockPixels()
    {
        if (_chunk == null || _mask == null) return 0;

        Color32[] mask = _mask;
        int mw = _maskWidth;
        int mh = _maskHeight;

        Vector2 localPos = AnchorLocal;
        int cx = Mathf.RoundToInt(localPos.x * _chunk.PPU);
        int cy = Mathf.RoundToInt(localPos.y * _chunk.PPU);

        Vector2 pivot = GetEffectivePivot();
        int startX = cx - Mathf.RoundToInt(pivot.x);
        int startY = cy - Mathf.RoundToInt(pivot.y);

        int exposed = 0;
        for (int ry = 0; ry < mh; ry++)
        {
            for (int rx = 0; rx < mw; rx++)
            {
                if (mask[ry * mw + rx].a <= 10) continue;
                if (_chunk.GetPixelAlpha(startX + rx, startY + ry) == 0)
                    exposed++;
            }
        }
        return exposed;
    }

    /// <summary>
    /// 인접한 지형이 파졌을 때 TerrainChunk.Dig()에서 호출.
    /// 바위 모양대로 지형 픽셀을 제거하고 콜라이더를 활성화한다.
    /// 이미 노출된 경우 또는 노출 픽셀이 minExposedPixels 미만이면 실행하지 않는다.
    /// </summary>
    public void RevealInTerrain()
    {
        // 이미 콜라이더가 활성화돼 있으면 이미 노출된 상태 → 스킵
        if (_polyCollider != null && _polyCollider.enabled) return;

        // 노출된 픽셀 수가 임계값 미만이면 아직 충분히 파지지 않음 → 스킵
        int exposed = CountExposedRockPixels();
        if (exposed < minExposedPixels)
        {
            //if (LogRevealTiming && exposed > 0)
            //    Debug.Log($"[RockReveal][f{Time.frameCount}] 노출 대기 — {name} ({exposed}/{minExposedPixels}px)");
            return;
        }

        //if (LogRevealTiming)
        //    Debug.Log($"[RockReveal][f{Time.frameCount}] ★ 노출 실행 — {name} exposed={exposed}/{minExposedPixels}px " +
        //              $"(RevealBounds={RevealBounds} / PixelBounds={PixelBoundsInChunk})");

        if (_chunk != null && _mask != null)
        {
            Vector2 localPos = AnchorLocal;
            int cx = Mathf.RoundToInt(localPos.x * _chunk.PPU);
            int cy = Mathf.RoundToInt(localPos.y * _chunk.PPU);

            TerrainCarver.ClearHole(_chunk, cx, cy, _mask, _maskWidth, _maskHeight, GetEffectivePivot());
        }

        if (_polyCollider != null)
            _polyCollider.enabled = true;
        if (_spriteRenderer != null)
            _spriteRenderer.enabled = true;

        _isRevealed = true;
        MapRockCache.Capture(this); // 지도에 실루엣 스냅샷 저장 (언로드돼도 유지)
    }

    // ============================================================================================================
    //  HP RECOVERY: 비전투 시간이 지나면 선형 회복
    // ============================================================================================================
    private void Update()
    {
        if (!_isRevealed) return;
        if (_currentHp >= MaxHp) return;
        if (Time.time - _lastDamageTime < recoveryDelay) return;

        _currentHp = Mathf.Min(_currentHp + recoveryRate * Time.deltaTime, MaxHp);

        float ratio = _currentHp / MaxHp;
        foreach (var listener in _stageListeners)
            listener.OnHpRatioChanged(ratio);
    }

    // ============================================================================================================
    //  RESTORE: 청크 재로드 시 저장된 상태 복원
    // ============================================================================================================
    /// <summary>
    /// TerrainDecorator.RestoreRocks()에서 호출.
    /// OnEnable()의 초기화를 덮어쓰고 저장된 상태를 복원한다.
    /// </summary>
    public void RestoreState(float savedHp, float savedLastDamageTime, bool savedIsRevealed)
    {
        _currentHp      = savedHp;
        _lastDamageTime = savedLastDamageTime;
        _isRevealed     = savedIsRevealed;

        float ratio = Mathf.Max(_currentHp, 0f) / MaxHp;
        if (_stageListeners != null)
            foreach (var s in _stageListeners) s.OnHpRatioChanged(ratio);

        if (savedIsRevealed)
        {
            if (_polyCollider != null)   _polyCollider.enabled   = true;
            if (_spriteRenderer != null) _spriteRenderer.enabled = true;
            MapRockCache.Capture(this); // 재로드된 노출 돌도 지도 스냅샷 갱신(같은 좌표키 → 중복 없음)
        }
    }

    // ============================================================================================================
    //  DESTRUCTION: 지형 픽셀 제거 후 오브젝트 파괴
    // ============================================================================================================
    private void DestroyRock()
    {
        // [필수·첫 줄] 아래 전부가 _spriteRenderer.bounds / transform 을 읽는다.
        // 히트 연출이 진행 중이면 RockHitAnimator.Update()가 이번 프레임 transform을
        // 원점으로 비워 둔 상태이고(합성은 LateUpdate라 아직 안 왔다), 그대로 읽으면
        // 조각·효과음·광물이 청크 원점에 쏟아진다. 읽기 전에 배치 확정값으로 되돌린다.
        if (_hitReactors != null)
            foreach (var reactor in _hitReactors)
                reactor.OnRockBreaking();

        MapRockCache.Remove(this); // 실제로 캐졌으니 지도에서 제거 (스프라이트 유효할 때 좌표키로 매칭)

        if (_chunk != null && _mask != null)
        {
            Vector2 localPos = AnchorLocal;
            int cx = Mathf.RoundToInt(localPos.x * _chunk.PPU);
            int cy = Mathf.RoundToInt(localPos.y * _chunk.PPU);
            Vector2 rockCenter = (Vector2)transform.position;
            TerrainCarver.ClearHole(_chunk, cx, cy, _mask, _maskWidth, _maskHeight, GetEffectivePivot(),
                (pos, color) =>
                {
                    if (TerrainParticleManager.Instance != null && Random.value <= TerrainParticleManager.Instance.spawnChance)
                        TerrainParticleManager.Instance.SpawnDebris(pos, color, rockCenter);
                },
                (px, py) => _chunk.transform.TransformPoint(_chunk.GetLocalPositionForPixel(px, py))
            );
        }

        if (_chunk != null)
        {
            _chunk.RemoveSpawnedRock(this);
            // 암석 콜라이더가 사라지기 전에 지형 콜라이더를 즉시 갱신.
            // throttle을 기다리면 최대 0.2초 공백이 생겨 플레이어가 뚫고 내려갈 수 있음.
            _chunk.ForceUpdateCollider();

            // [이벤트 기반 재낙하] 바위가 있던 자리 위/주변에 얹혀 있던 광물의 지지 재검사
            if (_spriteRenderer != null)
            {
                Bounds b = _spriteRenderer.bounds;
                MineralItemController.NotifyTerrainDug(b.center, (Vector2)b.size + new Vector2(0.3f, 0.3f));
            }
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(SfxKeys.RockBreak, _spriteRenderer.bounds.center);

        // 지형 픽셀 제거 + 콜라이더 갱신 후 조각 스폰
        // (스폰 시점에 지형 콜라이더가 남아있으면 반발로 위치가 튐)
        GetComponent<RockBreakVFX>()?.Play(_spriteRenderer.bounds.center, SizeScale);

        // 드롭 오버라이드(광물돌 등)가 있으면 위임, 없으면 기존 랜덤 드롭
        var dropOverride = GetComponent<IRockDropOverride>();
        if (dropOverride == null || !dropOverride.TryDropOnDestroy(_spriteRenderer.bounds.center))
            DropRareMinerals();

        // 유물 탐험 드롭. 광물 드롭과 배타가 아니라 별도 판정이다 —
        // IRockDropOverride로 만들면 광물돌의 확정 광물 드롭을 잡아먹는다.
        Relic.Drop.RelicDropRoller.TryDropFromRock(
            ResolveChunkY(),
            isMineralRock: GetComponent<MineralRock>() != null,
            worldPos: _spriteRenderer.bounds.center);

        Destroy(gameObject);
    }

    /// <summary>
    /// 이 돌이 속한 청크의 Y(지상 0, 아래로 음수). 무한맵 청크가 없으면 정적 대형 청크에서 읽는다 —
    /// <see cref="DropRareMinerals"/>의 깊이 해석과 같은 폴백이다(그쪽은 절대값만 쓴다).
    /// </summary>
    private int ResolveChunkY()
    {
        if (_chunk != null) return _chunk.ChunkY;
        LargeStaticTerrainChunk largeChunk = GetComponentInParent<LargeStaticTerrainChunk>();
        return largeChunk != null ? largeChunk.Coord.y : 0;
    }

    // ============================================================================================================
    //  RARE MINERAL DROP
    // ============================================================================================================

    private void DropRareMinerals()
    {
        if (TileDataManager.Instance == null) { Debug.LogWarning("[DiggableRock] TileDataManager 없음"); return; }

        int depth;
        if (_chunk != null)
            depth = Mathf.Abs(_chunk.ChunkY);
        else
        {
            LargeStaticTerrainChunk largeChunk = GetComponentInParent<LargeStaticTerrainChunk>();
            depth = largeChunk != null ? Mathf.Abs(largeChunk.Coord.y) : 0;
        }

        TileDataJson tileData = TileDataManager.Instance.GetData(tileType);

        // RockSpawner를 안 거치고 프리팹 자식으로 직접 배치된 암석은 tileType이 기본값(HardStone)이라
        // tileData.json에 항목이 없다 → 깊이로 층을 역산해 그 층의 광물 테이블을 쓴다.
        if (tileData?.minerals == null)
            tileData = TileDataManager.Instance.GetData(TileDataManager.Instance.GetTileTypeAtDepth(-depth));

        if (tileData?.minerals == null) { Debug.LogWarning($"[DiggableRock] tileType={tileType} minerals 데이터 없음"); return; }

        // 깊이 범위 안에 있는 희귀 광물 후보 수집.
        // 비면 같은 깊이의 Common으로 폴백 — 돌을 깼는데 빈손인 구간을 없애기 위함.
        var eligibleRares = new System.Collections.Generic.List<MineralRuleJson>();
        CollectEligibleRules(tileData.minerals, depth, rare: true, eligibleRares);
        if (eligibleRares.Count == 0)
            CollectEligibleRules(tileData.minerals, depth, rare: false, eligibleRares);

        if (eligibleRares.Count == 0) return;

        // 깊이 기반 가중치로 1종류 선택 (rockDropWeight=표면 가중치, 1-rockDropWeight=깊이 가중치)
        // 예: Copper 0.6 → 표면 60%, 깊이 40% / Iron 0.4 → 표면 40%, 깊이 60%
        float totalWeight = 0f;
        var weights = new float[eligibleRares.Count];
        for (int i = 0; i < eligibleRares.Count; i++)
        {
            var r = eligibleRares[i];
            float rt = (r.maxDepth > r.minDepth)
                ? Mathf.Clamp01((float)(depth - r.minDepth) / (r.maxDepth - r.minDepth))
                : 1f;
            weights[i] = Mathf.Lerp(r.rockDropWeight, 1f - r.rockDropWeight, rt);
            totalWeight += weights[i];
        }

        totalWeight = ApplyValueBias(eligibleRares, weights, totalWeight);

        float weightRoll = Random.value * totalWeight;
        float cumulative = 0f;
        MineralRuleJson picked = eligibleRares[eligibleRares.Count - 1];
        for (int i = 0; i < eligibleRares.Count; i++)
        {
            cumulative += weights[i];
            if (weightRoll <= cumulative) { picked = eligibleRares[i]; break; }
        }

        // 드롭 개수 범위는 돌 크기 티어가 정한다 (소 1~3 / 중 2~5 / 대 3~7).
        // 그 범위 안에서 층 안쪽으로 들어갈수록 상한이 min→max로 램프된다.
        // tileData.json의 rockDropCount는 더 이상 개수에 쓰이지 않는다(종류 선택만 담당).
        var (tierMin, tierMax) = RockTierStats.DropRange(_tier);

        float t = (picked.maxDepth > picked.minDepth)
            ? Mathf.Clamp01((float)(depth - picked.minDepth) / (picked.maxDepth - picked.minDepth))
            : 1f;
        int scaledMaxCount = Mathf.Max(tierMin, Mathf.RoundToInt(Mathf.Lerp(tierMin, tierMax, t)));

        // 티어 min은 "이 크기의 돌을 깼으면 최소 이만큼은 나온다"는 보장 하한이다.
        int count = Mathf.Max(tierMin, Random.Range(tierMin, scaledMaxCount + 1));

        // 업그레이드 보너스는 마지막에 얹는다 — 티어 램프 결과 위에 더해야 대·소 격차가 유지된다.
        count = RockDropBonus.Apply(count);

        for (int i = 0; i < count; i++)
            TryDropMineral(picked.mineralType);
    }

    /// <summary>깊이 범위에 걸리는 규칙을 rarity로 걸러 result에 담는다.</summary>
    private static void CollectEligibleRules(System.Collections.Generic.List<MineralRuleJson> rules,
        int depth, bool rare, System.Collections.Generic.List<MineralRuleJson> result)
    {
        foreach (var rule in rules)
        {
            if (rule == null || rule.IsRare != rare) continue;
            if (depth < rule.minDepth || depth > rule.maxDepth) continue;
            result.Add(rule);
        }
    }

    private void TryDropMineral(string typeName)
    {
        if (!System.Enum.TryParse(typeName, true, out MineralID id))
        {
            Debug.LogWarning($"  [TryDrop] MineralID 파싱 실패: {typeName}");
            return;
        }

        // 스프라이트의 실제 시각적 중심에서 드롭 (pivot 위치와 무관)
        Vector3 rockCenter = _spriteRenderer != null
            ? _spriteRenderer.bounds.center
            : new Vector3(transform.position.x, transform.position.y, 0f);

        Vector3 dropPos = new Vector3(
            rockCenter.x + Random.Range(-0.05f, 0.05f),
            rockCenter.y,
            -1f
        );

        MineralDropHelper.Drop(id, dropPos);
    }

    /// <summary>
    /// '감별사의 눈'(<see cref="UpgradeEffectType.RareMineralChance"/>)만큼 드랍 가중치를
    /// **비싼 광물 쪽으로 기울인다**. 새 광물을 만들어 내는 게 아니라, 같은 후보 안에서
    /// 무엇이 뽑히느냐만 바꾼다.
    ///
    /// 계수 0이면 아무 일도 안 한다(=업그레이드 없음). 1이면 평균가 대비 비율만큼 그대로 기운다.
    /// 싼 광물도 완전히 사라지지 않게 하한(<see cref="ValueBiasFloor"/>)을 둔다 —
    /// 0으로 만들면 "저 광물은 이제 영영 안 나온다"가 되어 도감·수집이 막힌다.
    ///
    /// 기저 가격을 쓰는 이유는 <see cref="MineralPriceDatabase.GetBasePrice"/> 주석 참고.
    /// </summary>
    private static float ApplyValueBias(
        System.Collections.Generic.List<MineralRuleJson> rules, float[] weights, float totalWeight)
    {
        var mgr = UpgradeManager.Instance;
        if (mgr == null || rules.Count < 2 || totalWeight <= 0f) return totalWeight;

        float bias = mgr.GetStatValue(UpgradeEffectType.RareMineralChance, 0f) / 100f;
        if (bias <= 0f) return totalWeight;

        var db = (PriceDataLoader.Instance != null) ? PriceDataLoader.Instance.MineralPrices : null;
        if (db == null) return totalWeight;

        var prices = new float[rules.Count];
        float sum = 0f;
        for (int i = 0; i < rules.Count; i++)
        {
            prices[i] = System.Enum.TryParse(rules[i].mineralType, true, out MineralID id)
                ? db.GetBasePrice(id) : 0f;
            sum += prices[i];
        }

        float avg = sum / rules.Count;
        if (avg <= 0f) return totalWeight;   // 가격표가 비어 있으면 손대지 않는다

        float newTotal = 0f;
        for (int i = 0; i < rules.Count; i++)
        {
            float scale = Mathf.Max(ValueBiasFloor, 1f + bias * (prices[i] / avg - 1f));
            weights[i] *= scale;
            newTotal += weights[i];
        }
        return newTotal;
    }

    /// <summary>가중치를 기울일 때 남겨두는 최소 배수. 싼 광물이 0이 되지 않게.</summary>
    private const float ValueBiasFloor = 0.15f;

}
