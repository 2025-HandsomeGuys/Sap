using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PolygonCollider2D))]
public class MineralItemController : MonoBehaviour
{
    private Rigidbody2D rb;
    // [Collider] 스프라이트 모양대로 충돌시켜 구슬처럼 굴러다니는 것을 막기 위해 PolygonCollider2D 사용.
    // isTrigger 토글만 하므로 Collider2D 기반 타입으로 참조.
    private Collider2D col;
    private InfinityMapManager mapManager;

    [Header("Physics Settings")]
    // [중력] 프리팹의 Rigidbody2D > Gravity Scale이 유일한 기준값이다.
    // 과거엔 여기에 별도 public gravityScale 필드가 있어 WakeUp()이 매번 Rigidbody2D 값을 덮어썼고,
    // 프리팹에서 Rigidbody2D 중력을 조정해도 첫 낙하에서 1.0으로 되돌아가는 버그가 있었다.
    // Start()에서 1회 캡처해 두고, WakeUp()에서 이 값으로 복원한다
    // (AntiGravityZone이 중력을 바꿔둔 채 풀 반납된 광물이 반전 중력을 물고 재사용되는 것을 막기 위함).
    private float _baseGravityScale = 1f;
    public float supportCheckRadius = 0.04f; // 자신의 크기보다 작게 (작을수록 더 쉽게 떨어짐)
    [Range(0f, 1f)]
    public float structuralIntegrityThreshold = 0.8f; // 80% 이상이 지지해야 박혀있음 (높을수록 더 쉽게 떨어짐)

    [Header("Inventory")]
    public MineralSO mineralData;
    public int amount = 1;

    // ── 오브젝트 풀 신원 ──────────────────────────────────────────────────
    // MineralGenerator.SpawnMineralObject가 스폰할 때만 채운다 = "그 풀 소속"이라는 표시.
    // IceBreakable·SnowmanEntity·DokkaebiCauldron이 직접 Instantiate한 광물은 비어 있어
    // ReturnToPool에서 자동으로 걸러진다(엉뚱한 큐 오염 방지).
    // ⚠ 이건 상태가 아니라 신원이다 — 풀 재사용 리셋(OnEnable)에서 지우지 말 것.
    [System.NonSerialized] public string poolKey;

    private bool isEmbedded = true;
    private WaitForSeconds checkInterval = new WaitForSeconds(0.5f);

    // ── 돌에서 튀어나오는 연출 ────────────────
    // 돌이 부서지면 나온 광물이 살짝 위로 튀었다가 내려온다.
    // 최고점은 대략 v^2/(2g) = 0.2~0.46 유닛(20~46px) — 돌이 있던 자리를 크게 벗어나지 않는다.
    private const float PopSpeedMin = 2.0f;
    private const float PopSpeedMax = 3.0f;
    private const float PopAngleDeg = 30f; // 수직 기준 좌우 최대 발사각

    // Instantiate 직후에는 Start()가 아직 안 돌아 rb가 null이다.
    // 그 시점에 들어온 PopOut 요청을 담아 두었다가 Start() 끝에서 적용한다.
    private bool    _popPending;
    private Vector2 _popVelocity;

    /// <summary>
    /// 아직 지형에 박혀 있는가. false면 WakeUp()으로 땅에서 떨어져 나온 상태(= "MineralDug" 태그).
    /// 줍기(PickupableItem)·자석(MagnetRelic)이 이 기준으로 매설 광물을 걸러낸다.
    /// </summary>
    public bool IsEmbedded => isEmbedded;

    /// <summary>
    /// 지금 지형 밖으로 드러나 있는가(= 화면에 보이는가). 중심 픽셀이 지형에 덮여 있으면 false.
    ///
    /// isEmbedded만으로는 부족하다. WakeUp() 조건은 "지지율 &lt; structuralIntegrityThreshold(0.8)"라
    /// 가장자리 광물은 주변이 조금만 파여도 깨어나는데, 그 뒤 지형에 갇혀 못 움직이면
    /// 화면상으론 여전히 파묻혀 있으면서 상태만 "떨어져나옴"이 된다.
    ///
    /// 청크가 없는 좌표(던전 오버레이 등)는 IsWorldPositionEmpty가 true를 주므로 정상 픽업된다.
    /// </summary>
    public bool IsExposed
    {
        get
        {
            if (mapManager == null) mapManager = InfinityMapManager.Instance;
            if (mapManager == null) return true; // 판정 불가 시 막지 않는다
            return mapManager.IsWorldPositionEmpty(transform.position);
        }
    }

    // ── 진단용 노출도 측정 ────────────────────────────────────────────────────
    // IsExposed는 중심 픽셀 1개만 보는 boolean이라 "얼마나 드러났나"를 알 수 없다.
    // 아래는 광물 자기 크기(콜라이더 extents)만큼 퍼뜨린 9점을 샘플해 빈 픽셀 비율을 낸다.
    // 판정 로직에는 쓰지 않는다 — MineralLifetime의 로그 전용이다.
    // (s_supportOffsets를 그대로 재사용: 중심 + 상하좌우 + 대각 4)

    /// <summary>진단용. 0 = 완전히 지형에 묻힘, 1 = 9점 전부 빈 공간.</summary>
    public float ExposureRatio
    {
        get
        {
            if (mapManager == null) mapManager = InfinityMapManager.Instance;
            if (mapManager == null) return 1f;

            Vector2 origin = transform.position;
            float r = SampleRadius;
            int emptyCount = 0;
            for (int i = 0; i < s_supportOffsets.Length; i++)
            {
                if (mapManager.IsWorldPositionEmpty(origin + s_supportOffsets[i] * r))
                    emptyCount++;
            }
            return (float)emptyCount / s_supportOffsets.Length;
        }
    }

    /// 노출도 샘플 반경. 광물 자기 몸집 기준(콜라이더가 없으면 스프라이트 대략치).
    private float SampleRadius => col != null ? Mathf.Max(col.bounds.extents.x, col.bounds.extents.y) * 0.7f : 0.15f;

    /// <summary>
    /// 진단용. 광물 스프라이트가 차지하는 사각형을 n×n 격자로 훑어 "어디가 가려졌는지"를 그림으로 만든다.
    /// '.' = 빈 픽셀(구멍 → 화면에 보임), '#' = 지형 픽셀(광물 order -1 < 지형 0 이라 가려짐).
    ///
    /// IsExposed는 중심 픽셀 1개만 보므로, 구멍이 광물보다 작으면 "EMPTY 판정 = 거의 안 보임"이 된다.
    /// 그 간극을 눈으로 확인하기 위한 것이다.
    /// </summary>
    public string DescribeExposureMap(int n = 9)
    {
        if (mapManager == null) mapManager = InfinityMapManager.Instance;
        if (mapManager == null) return "(mapManager 없음)";

        Vector2 c = transform.position;
        Vector2 e = col != null ? (Vector2)col.bounds.extents : new Vector2(0.15f, 0.15f);

        var sb = new System.Text.StringBuilder();
        int emptyCount = 0;

        for (int row = n - 1; row >= 0; row--) // 위 → 아래 (화면과 같은 방향)
        {
            sb.Append("\n      ");
            for (int cx = 0; cx < n; cx++)
            {
                float fx = (cx / (float)(n - 1)) * 2f - 1f;
                float fy = (row / (float)(n - 1)) * 2f - 1f;
                bool isEmpty = mapManager.IsWorldPositionEmpty(c + new Vector2(fx * e.x, fy * e.y));
                if (isEmpty) emptyCount++;
                sb.Append(isEmpty ? '.' : '#');
            }
        }

        sb.Insert(0, $"visible={(float)emptyCount / (n * n):P0}  extents=({e.x:F3},{e.y:F3})  '.'=구멍(보임) '#'=지형(가려짐)");
        return sb.ToString();
    }

    /// <summary>진단용. Scene 뷰에 광물 위치를 X자로 그린다(Gizmos 토글 필요).</summary>
    public void DrawDebugMarker(float duration)
    {
        Vector3 p = transform.position;
        float s = col != null ? Mathf.Max(col.bounds.extents.x, col.bounds.extents.y) : 0.15f;
        Color c = IsExposed ? Color.green : Color.red;
        Debug.DrawLine(p + new Vector3(-s, -s), p + new Vector3(s, s), c, duration, false);
        Debug.DrawLine(p + new Vector3(-s, s), p + new Vector3(s, -s), c, duration, false);
    }

    /// <summary>진단용 한 줄 요약. 중심 픽셀 판정·9점 노출도·청크 상태를 한꺼번에 보여준다.</summary>
    public string DescribeExposure()
    {
        if (mapManager == null) mapManager = InfinityMapManager.Instance;
        Vector3 p = transform.position;
        if (mapManager == null) return $"pos={p} mapManager=null";

        var coord = new Vector2Int(
            Mathf.FloorToInt(p.x / mapManager.chunkWidthWorld),
            Mathf.FloorToInt(p.y / mapManager.chunkHeightWorld));

        // 청크가 아예 없으면 IsWorldPositionEmpty가 무조건 true를 준다 → 그 사실을 로그에 남긴다.
        bool chunkLoaded = mapManager.IsChunkLoaded(coord);

        return $"pos=({p.x:F2},{p.y:F2}) chunk={coord} loaded={chunkLoaded} " +
               $"center={(IsExposed ? "EMPTY" : "SOLID")} exposure={ExposureRatio:P0} " +
               $"r={SampleRadius:F3} embedded={isEmbedded} settled={isSettled} tag={gameObject.tag}";
    }

    // [Settle→Kinematic] 낙하가 멈추면 동적 바디를 Kinematic으로 되돌려 물리 solve island에서 제외한다.
    // 지형 콜라이더가 0.2초마다 재생성돼도 깨어나지 않으며, 발밑이 파일 때만 NotifyTerrainDug로 재검사된다.
    // 드릴 연속 파기 시 수백 개의 낙하물이 영구히 awake 상태로 남아 SolveDiscreteIsland가 폭증하던 문제 해결.
    private bool isSettled;
    private float _restTimer;
    private const float SETTLE_TIME        = 0.4f;    // 이 시간 이상 정지 시 안착 처리
    private const float SETTLE_LINEAR_SQR  = 0.0025f; // 선속도 0.05 unit/s 이하 (제곱 비교)
    private const float SETTLE_ANGULAR     = 5f;      // 각속도 5 deg/s 이하

    // 던전 오버레이 등 "청크가 아예 없는 영역"에서 광물이 void 얼림에 걸리지 않게 하는 전역 토글.
    // DungeonOverlayController가 진입/이탈 시 켜고/끈다. 켜져 있으면 청크 로딩 여부와 무관하게 물리를 유지한다.
    public static bool SuppressVoidFreeze;

    void OnEnable()
    {
        // Start()가 아직 실행되지 않은 첫 활성화는 Start()가 처리함
        if (rb == null) return;

        // 풀에서 재사용될 때 물리 상태와 코루틴을 처음 스폰과 동일하게 초기화
        if (mapManager == null) mapManager = InfinityMapManager.Instance;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;
        if (col != null) col.isTrigger = true;
        isEmbedded = true;
        gameObject.tag = "Untagged";

        StopAllCoroutines();
        StartCoroutine(CheckStructuralIntegrityRoutine());
    }

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<PolygonCollider2D>();
        mapManager = InfinityMapManager.Instance;

        // 프리팹에 설정된 Rigidbody2D 중력을 기준값으로 캡처 (아직 어떤 존도 값을 건드리지 않은 시점)
        _baseGravityScale = rb.gravityScale;

        // [레이어 설정] "Mineral" 레이어로 지정 → Physics 2D 매트릭스에서 Player와 충돌 제외 가능
        int mineralLayer = LayerMask.NameToLayer("Mineral");
        if (mineralLayer >= 0)
            gameObject.layer = mineralLayer;
        else
            Debug.LogWarning("[MineralItemController] 'Mineral' 레이어가 없습니다! Project Settings > Tags & Layers에서 추가해주세요.");

        // 초기 상태: 박혀있음 (물리 X), 트리거 상태 (플레이어가 겹쳐도 밀려나지 않음)
        rb.bodyType = RigidbodyType2D.Kinematic;
        if (col != null) col.isTrigger = true; 
        rb.linearVelocity = Vector2.zero;
        isEmbedded = true;

        // 즉시 한 번 체크 (혹시 이미 공중에 스폰됐을 수도 있으니)
        StartCoroutine(CheckStructuralIntegrityRoutine());

        Vector3 pos = transform.position;
        pos.z = -1f;
        transform.position = pos;

        // Instantiate 직후 예약된 튀어오름을 여기서 적용한다(rb·col·기준중력이 모두 준비된 뒤).
        if (_popPending)
        {
            _popPending = false;
            ApplyPop(_popVelocity);
        }
    }

    void FixedUpdate()
    {
        // 매설(Kinematic) 또는 안착(Kinematic) 상태는 물리에서 빠져 있으므로 아무 처리도 하지 않는다.
        // 이 상태의 재낙하는 오직 NotifyTerrainDug(파기 이벤트)로만 트리거된다.
        if (isEmbedded || isSettled) return;

        // 낙하 중: 청크 로딩 여부에 따라 물리 on/off
        CheckChunkLoadingConfig();

        // [Settle] 충분히 오래 멈춰 있으면 Kinematic으로 전환해 solve island에서 제거.
        // 낙하물이 지형에 얹혀 쉬면 velocity가 0에 수렴하므로, 콜라이더 재생성으로 깨어나도 timer는 계속 누적된다.
        if (rb.linearVelocity.sqrMagnitude <= SETTLE_LINEAR_SQR &&
            Mathf.Abs(rb.angularVelocity) <= SETTLE_ANGULAR)
        {
            _restTimer += Time.fixedDeltaTime;
            if (_restTimer >= SETTLE_TIME)
            {
                // 지형에 박힌 채 안착하면 그대로 영구 고정된다 (TryEjectFromTerrain 주석 참고).
                if (TryEjectFromTerrain())
                    _restTimer = 0f;   // 꺼냈으니 낙하 판정을 처음부터 다시
                else
                    Settle();
            }
        }
        else
        {
            _restTimer = 0f;
        }
    }

    // ── 지형에 박힌 채 안착하는 것 방지 ────────────────────────────
    // 높은 곳에서 떨어진 광물이 지형 속에 박혀 못 주워 먹는 사고가 있었다.
    //
    // ① 왜 박히나: 프리팹 Rigidbody2D가 Discrete였다. 패 내려간 구멍으로 5u만 떨어져도
    //    v≈10u/s = 한 스텝(0.02s)에 0.2u로, 광물 콜라이더(0.2u)보다 많이 움직여 지형 표면을 관통한다.
    //    → 프리팹 30개를 Continuous로 바꿔 관통 자체를 막았다.
    // ② 그래도 남는 경로: 픽셀은 solid인데 콜라이더가 아직 없는 자리(청크 로드 직후 등).
    //    막을 콜라이더가 없으므로 CCD로도 못 막는다.
    // ③ 왜 스스로 못 빠져나오나: Box2D의 침투 해소는 position solver가 하고 velocity를 안 건드린다.
    //    즉 "깊게 박혀 밀려나오는 중"에도 linearVelocity는 0이라 위 안착 판정을 그대로 통과하고,
    //    Settle()이 Kinematic으로 바꾸는 순간 position solver도 멈춰 영구히 지형 속에 고정된다.
    //    그 상태는 IsExposed=false라 픽업 불가 + MineralLifetime도 정지 → 파낼 때까지 남는다.
    //
    // 그래서 안착 직전에 한 번 더 본다. 판정은 반드시 픽업 게이트(IsExposed)와 같은
    // "중심 픽셀" 기준 — 둘이 어긋나면 "못 먹는데 사라지지도 않는" 구멍이 다시 생긴다.
    private const float EJECT_STEP      = 0.01f; // 1px (PPU 100)
    private const float EJECT_CLEARANCE = 0.02f; // 빠져나온 뒤 여유 2px (경계 픽셀 재진입 방지)
    private const int   EJECT_MAX_STEPS = 64;    // 0.64u까지만 — 그 이상 깊으면 구출이 아니라 텔레포트다
    private const int   EJECT_MAX_TRIES = 8;     // 한 번의 낙하에서 반복 구출 상한(무한루프 방지)
    private int _ejectTries;

    /// <summary>
    /// 지형 픽셀 속에 박혀 있으면 빈 공간이 나올 때까지 1px씩 위로 밀어 올린다.
    /// 실제로 옮겼으면 true — 호출측은 안착을 미루고 낙하 판정을 다시 돌려야 한다.
    /// </summary>
    bool TryEjectFromTerrain()
    {
        if (_ejectTries >= EJECT_MAX_TRIES) return false;
        if (mapManager == null) mapManager = InfinityMapManager.Instance;
        if (mapManager == null) return false;

        Vector2 p = transform.position;
        var coord = new Vector2Int(
            Mathf.FloorToInt(p.x / mapManager.chunkWidthWorld),
            Mathf.FloorToInt(p.y / mapManager.chunkHeightWorld));

        // TerrainChunk가 아닌 좌표(미로드·던전 오버레이·LargeStatic)는 픽셀 판정을 신뢰할 수 없다.
        // IsWorldPositionEmpty가 각각 무조건 true/false를 돌려주므로 여기서 걸러낸다.
        if (mapManager.GetChunk(coord) == null) return false;

        if (mapManager.IsWorldPositionEmpty(p)) return false; // 안 박혔다

        for (int i = 1; i <= EJECT_MAX_STEPS; i++)
        {
            float y = p.y + i * EJECT_STEP;
            if (!mapManager.IsWorldPositionEmpty(new Vector2(p.x, y))) continue;

            _ejectTries++;
            Vector2 target = new Vector2(p.x, y + EJECT_CLEARANCE);
            // autoSyncTransforms=0이므로 물리 바디는 rb.position으로 옮긴다.
            // transform은 IsExposed·픽업이 이번 프레임부터 바로 같은 값을 보도록 함께 맞춰 둔다.
            rb.position = target;
            transform.position = new Vector3(target.x, target.y, -1f);
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            return true;
        }

        return false; // 0.64u 위까지 전부 solid = 지형 깊숙이 매몰. 건드리지 않는다
    }

    /// <summary>
    /// 낙하 후 안착 처리. Kinematic으로 전환해 물리 solve island에서 빠진다.
    /// 콜라이더는 solid(non-trigger) 유지 → 다른 낙하물이 위에 착지 가능하고 F키 픽업도 정상.
    /// </summary>
    void Settle()
    {
        isSettled = true;
        _restTimer = 0f;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;

        StartCoroutine(WatchSettledSupportRoutine());
    }

    // ── 안착 후 발밑 감시 ─────────────────────────────────────────────────────
    // Settle()은 광물을 물리에서 통째로 빼버리므로, 그 뒤 발밑이 사라져도 스스로는 절대 못 깨어난다.
    // 깨우는 경로가 NotifyTerrainDug(파기 이벤트) 하나뿐이라, 거기 안 걸리는 지지 상실은
    // 전부 "공중에 멈춘 광물"로 남는다. 실제로 걸린 경로들:
    //   · 얹혀 있던 게 지형이 아니라 돌(DiggableRock — layer=Ground)이었고, 그 돌이 풀 반납되며
    //     콜라이더가 꺼진 경우 (DestroyRock만 통지하고 언로드는 통지하지 않는다)
    //   · 픽셀은 이미 air인데 콜라이더가 아직 안 갱신된 자리에 얹혀 0.4초를 채워 Settle된 경우
    //     → 콜라이더가 따라잡는 순간 받침이 사라지는데, 그 시점엔 통지가 이미 지나갔다
    //   · CheckFloatingIslandsInArea의 플러드필이 통지 박스 밖 지형까지 지운 경우
    // 이벤트 목록을 늘리는 방식으로는 계속 구멍이 남으므로, 안착 상태에서도 저빈도로 발밑을 직접 본다
    // (0.5초 지지 코루틴을 도는 매설 상태와 같은 방식).
    //
    // ⚠ 여기서 CheckSupport()를 쓰면 안 된다 — 표면에 얹힌 광물은 9점 중 아래 3점만 solid라
    //    지지율이 임계값(0.5~0.8)을 절대 못 넘겨, 정상적으로 바닥에 놓인 광물까지 전부 깨운다.
    private const float SETTLED_WATCH_INTERVAL = 1f;
    private const float SETTLED_GROUND_PROBE   = 0.05f; // 5px. 콜라이더 단순화·contact offset 여유분
    private static readonly WaitForSeconds s_settledWatchInterval = new WaitForSeconds(SETTLED_WATCH_INTERVAL);
    private static readonly RaycastHit2D[] s_groundHits = new RaycastHit2D[4];
    private static ContactFilter2D s_groundFilter;
    private static bool s_groundFilterReady;

    // 발밑 픽셀 샘플 x 오프셋 배율(콜라이더 반폭 기준). 좌·중앙·우 3점.
    private static readonly float[] s_groundSampleX = { -0.6f, 0f, 0.6f };

    IEnumerator WatchSettledSupportRoutine()
    {
        while (isSettled)
        {
            yield return s_settledWatchInterval;
            if (!isSettled) yield break;      // 그 사이 NotifyTerrainDug로 이미 깨어남

            if (!HasAnythingBelow())
            {
                WakeUp();
                yield break;
            }
        }
    }

    /// <summary>
    /// 안착 상태를 유지해도 되는가. 콜라이더와 픽셀 <b>둘 다</b> 비었을 때만 false.
    /// 한쪽만 보면 안 된다 — 콜라이더가 아직 안 생긴 자리(픽셀은 solid)에서 깨우면 지형 속으로 떨어지고,
    /// 픽셀만 보면 돌(layer=Ground) 위에 정상적으로 얹힌 광물이 전부 떨어진다.
    /// </summary>
    bool HasAnythingBelow()
    {
        // 1. 콜라이더 — 나를 실제로 떠받칠 수 있는 레이어만 본다(충돌 행렬 그대로 사용).
        if (col != null)
        {
            if (!s_groundFilterReady)
            {
                s_groundFilter = new ContactFilter2D
                {
                    useTriggers  = false, // 매설 광물·존 트리거는 받침이 아니다
                    useLayerMask = true,
                    layerMask    = Physics2D.GetLayerCollisionMask(gameObject.layer)
                };
                s_groundFilterReady = true;
            }

            if (col.Cast(Vector2.down, s_groundFilter, s_groundHits, SETTLED_GROUND_PROBE) > 0)
                return true;
        }

        // 2. 지형 픽셀
        if (mapManager == null) mapManager = InfinityMapManager.Instance;
        if (mapManager == null) return true; // 판정 불가 시 건드리지 않는다

        Vector3 p = transform.position;
        var coord = new Vector2Int(
            Mathf.FloorToInt(p.x / mapManager.chunkWidthWorld),
            Mathf.FloorToInt(p.y / mapManager.chunkHeightWorld));

        // 청크가 없으면 IsWorldPositionEmpty가 무조건 true(=비었다)를 준다.
        // 그걸 믿고 깨우면 언로드 지역 광물이 전부 낙하 상태로 뒤집힌다.
        if (!mapManager.IsChunkLoaded(coord)) return true;

        float halfW  = col != null ? col.bounds.extents.x : 0.05f;
        float bottom = col != null ? col.bounds.min.y : p.y;

        for (int i = 0; i < s_groundSampleX.Length; i++)
        {
            float x = p.x + halfW * s_groundSampleX[i];
            for (float d = 0f; d <= SETTLED_GROUND_PROBE; d += 0.01f) // 1px 간격 (PPU 100)
            {
                if (!mapManager.IsWorldPositionEmpty(new Vector2(x, bottom - d)))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 주변 지형이 파였을 때 TerrainChunk가 공간 쿼리로 필터링해 호출한다.
    /// 매설·안착 상태에서 발밑 지지가 사라졌으면 다시 낙하(Dynamic)시킨다. 이미 낙하 중이면 무시.
    /// </summary>
    public void OnNearbyTerrainDug()
    {
        if (rb == null) return;
        if (!isEmbedded && !isSettled) return; // 이미 낙하 중
        if (!CheckSupport())
            WakeUp();
    }

    void CheckChunkLoadingConfig()
    {
        if (mapManager == null) return;

        // 던전 오버레이 등 청크 없는 영역: void 얼림을 끄고 물리를 유지(발판 위로 정상 낙하).
        if (SuppressVoidFreeze)
        {
            if (!rb.simulated) rb.simulated = true;
            return;
        }

        Vector2Int currentChunkCoord = new Vector2Int(
            Mathf.FloorToInt(transform.position.x / mapManager.chunkWidthWorld),
            Mathf.FloorToInt(transform.position.y / mapManager.chunkHeightWorld)
        );

        // 청크가 로딩되어 있으면 물리 켜기, 아니면 끄기
        if (mapManager.IsChunkLoaded(currentChunkCoord))
        {
            if (!rb.simulated) rb.simulated = true;
        }
        else
        {
            if (rb.simulated) rb.simulated = false; // Freeze in void
        }
    }

    IEnumerator CheckStructuralIntegrityRoutine()
    {
        // 박혀있는 동안에는 주기적으로 지지 기반 확인
        while (isEmbedded)
        {
            if (CheckSupport())
            {
                // 아직 지지받고 있음
            }
            else
            {
                // 지지대 붕괴 -> 물리 활성화
                WakeUp();
            }
            yield return checkInterval;
        }
    }

    // CheckSupport의 3x3 샘플 오프셋(단위 벡터). supportCheckRadius를 곱해서 쓴다.
    // 과거엔 호출마다 new Vector2[9]를 만들었는데, 매설 광물마다 0.5초 주기로 도는
    // 코루틴이라 광물 수에 비례해 쓰레기가 계속 쌓였다(광물 1000개 기준 약 190KB/s).
    // 배경: Assets/Docs/performance/README.md §3.5 — 프레임 시간이 예산 안이어도 할당은 따로 본다.
    private static readonly Vector2[] s_supportOffsets =
    {
        new Vector2( 0f,  0f), // Center
        new Vector2( 1f,  0f), new Vector2(-1f,  0f),
        new Vector2( 0f,  1f), new Vector2( 0f, -1f),
        new Vector2( 1f,  1f), new Vector2(-1f,  1f), // Corners
        new Vector2( 1f, -1f), new Vector2(-1f, -1f),
    };

    // "나를 지지하는 흙이 얼마나 남았나?" 체크
    bool CheckSupport()
    {
        if (mapManager == null) return false;

        // 1. 내 위치(월드)가 빈 공간인지 확인
        // 더 정확하게 하려면: 내 콜라이더 범위 내의 픽셀들을 샘플링하여 
        // 고체 픽셀(Alpha > 0)의 비율을 계산해야 함.
        
        // 간단한 구현: 내 중심점 + 상하좌우 약간의 포인트 체크 (3x3 샘플링)
        int solidPixelCount = 0;
        float r = supportCheckRadius;
        Vector2 origin = transform.position;

        for (int i = 0; i < s_supportOffsets.Length; i++)
        {
            // 맵 매니저에게 "이 좌표에 땅이 있나요?" 물어봄
            // 주의: 자기 자신(광물 오브젝트)과는 충돌하지 않으므로,
            // IsWorldPositionEmpty는 픽셀 데이터만 확인해야 함.
            if (!mapManager.IsWorldPositionEmpty(origin + s_supportOffsets[i] * r))
                solidPixelCount++; // 비어있지 않음 = 땅이 있음
        }

        float supportRatio = (float)solidPixelCount / s_supportOffsets.Length;

        // 임계값보다 지지대가 적으면 false (떨어져라)
        // 임계값보다 지지대가 적으면 false (떨어져라)
        bool isSupported = supportRatio > structuralIntegrityThreshold;
        
        // [Debug Physics]
        // {
        //      int dbgChunkX = mapManager != null ? Mathf.FloorToInt(transform.position.x / mapManager.chunkWidthWorld) : -999;
        //      int dbgChunkY = mapManager != null ? Mathf.FloorToInt(transform.position.y / mapManager.chunkHeightWorld) : -999;
        //      Debug.Log($"[Mineral] CheckSupport: {name} -> Solid: {solidPixelCount}/{s_supportOffsets.Length} (Ratio: {supportRatio:F2}). IsSupported: {isSupported} | pos={transform.position} chunkCoord=({dbgChunkX},{dbgChunkY})");
        //
        //      if (solidPixelCount == 0 && isSupported)
        //         Debug.LogError($"[Mineral] Logic Error! Count is 0 but Supported?");
        //
        //      if (solidPixelCount > 0 && solidPixelCount < s_supportOffsets.Length)
        //         Debug.Log($"[Mineral] Destabilizing... {solidPixelCount}/{s_supportOffsets.Length}");
        // }
        
        return isSupported;
    }

    void WakeUp()
    {
        isEmbedded = false;
        isSettled = false;
        _restTimer = 0f;
        _ejectTries = 0;
        gameObject.tag = "MineralDug";
        if (col != null) col.isTrigger = false; // [핵심] 떨어질 때는 고체여야 땅에 부딪힘
        rb.bodyType = RigidbodyType2D.Dynamic; // 물리 켜기
        rb.gravityScale = _baseGravityScale;   // 프리팹 Rigidbody2D 값 (반전 중력 잔류 방어)

        // 약간의 랜덤 회전/힘 추가 (자연스럽게)
        float randomRot = Random.Range(-30f, 30f);
        rb.angularVelocity = randomRot;

        // 광물이 땅에서 떨어져 처음으로 주울 수 있는 상태가 되었을 때 1초 뒤에 가이드를 띄움
        StartCoroutine(TriggerGuideWithDelay("mining_basic", 1f));

        // [DelayedBlast] 광물이 땅에서 떨어지는 순간 폭발 타이머 시작
        DelayedBlast blast = GetComponent<DelayedBlast>();
        if (blast != null) blast.Activate();

        // [Fix] Visuals: 물리가 활성화되면(떨어지면) 땅 앞으로 튀어나오게 함
        // 그래야 땅 뒤로 숨지 않고 "떨어진 아이템"처럼 보임
        Vector3 pos = transform.position;
        pos.z = -1f;
        transform.position = pos;

        // 청크가 언로드되지 않은 상태(플레이어 근처)에서 파진 경우:
        // DetachMineralsToWorld가 호출되지 않으므로 여기서 분리 + 60초 타이머를 직접 시작한다.
        var parentChunk = transform.parent != null ? transform.parent.GetComponent<TerrainChunk>() : null;
        if (parentChunk != null)
        {
            string[] parts = gameObject.name.Split('_');
            if (parts.Length >= 4 &&
                int.TryParse(parts[parts.Length - 2], out int px) &&
                int.TryParse(parts[parts.Length - 1], out int py))
            {
                MineralGenerator.MarkMineralCollected(parentChunk, px, py);
            }
            transform.SetParent(null, true);
            if (!TryGetComponent<MineralLifetime>(out var lifetime))
                gameObject.AddComponent<MineralLifetime>(); // OnEnable이 null 부모 확인 후 타이머 시작
            else
            {
                // 풀 재사용 케이스: MineralLifetime은 있지만 OnEnable이 TerrainChunk 부모 상태에서 실행돼 타이머가 안 시작된 경우.
                // enabled 토글로 OnEnable을 재실행한다 (이미 parent=null이므로 타이머가 시작된다).
                lifetime.enabled = false;
                lifetime.enabled = true;
            }
        }
    }

    private IEnumerator TriggerGuideWithDelay(string guideId, float delay)
    {
        yield return new WaitForSeconds(delay);
        GuideManager.Trigger(guideId);
    }

    /// <summary>
    /// 돌이 부서져 튀어나온 광물. 매설 상태를 건너뛰고 즉시 낙하 상태로 만든 뒤
    /// 위쪽으로 살짝 쏘아 올린다 — 무엇이 나왔는지가 돌 조각에 묻히지 않고 바로 보이게 하는 연출.
    ///
    /// <see cref="MineralDropHelper"/>가 Instantiate 직후에 호출하므로 Start()보다 먼저 들어올 수 있다.
    /// 그 경우 예약만 해 두고 Start() 끝에서 적용한다.
    /// </summary>
    public void PopOut()
    {
        float speed = Random.Range(PopSpeedMin, PopSpeedMax);
        float angle = Random.Range(-PopAngleDeg, PopAngleDeg) * Mathf.Deg2Rad;
        PopOut(new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * speed);
    }

    /// <summary>발사 속도를 직접 지정하는 버전.</summary>
    public void PopOut(Vector2 velocity)
    {
        if (rb == null) // Start() 이전 — 예약해 두고 Start()가 적용한다
        {
            _popPending  = true;
            _popVelocity = velocity;
            return;
        }
        ApplyPop(velocity);
    }

    private void ApplyPop(Vector2 velocity)
    {
        WakeUp();                     // Dynamic 전환 + 태그·콜라이더·기준중력 복원까지 한 번에
        rb.linearVelocity = velocity; // WakeUp은 각속도만 주므로 선속도는 여기서
    }

    // ── 이벤트 기반 재낙하: 파기 영역과 겹치는 광물만 지지 재검사 ──────────────
    // 물리 자동 wake(콜라이더 재생성) 대신, 파기가 실제로 일어난 영역만 1회 쿼리해
    // 그 안의 매설·안착 광물만 CheckSupport한다. GC-free (버퍼·필터 재사용).
    private static readonly Collider2D[] s_digOverlap = new Collider2D[128];
    private static ContactFilter2D s_digFilter;
    private static bool s_digFilterReady;

    /// <summary>
    /// 파기 영역(월드 박스)과 겹치는 매설·안착 광물의 지지를 재검사한다.
    /// TerrainChunk.Dig/Explode 등 파기 성공 시 1회 호출. 물리 쿼리 1회로 주변 광물만 처리한다.
    /// worldSize는 박스의 전체 크기(half-extent 아님).
    /// </summary>
    public static void NotifyTerrainDug(Vector2 worldCenter, Vector2 worldSize)
    {
        if (!s_digFilterReady)
        {
            int mineralLayer = LayerMask.NameToLayer("Mineral");
            s_digFilter = new ContactFilter2D
            {
                useTriggers  = true,               // 매설 광물은 trigger 콜라이더이므로 반드시 포함
                useLayerMask = mineralLayer >= 0,
                layerMask    = mineralLayer >= 0 ? (1 << mineralLayer) : ~0
            };
            s_digFilterReady = true;
        }

        int count = Physics2D.OverlapBox(worldCenter, worldSize, 0f, s_digFilter, s_digOverlap);
        for (int i = 0; i < count; i++)
        {
            Collider2D c = s_digOverlap[i];
            if (c == null) continue;
            if (c.TryGetComponent<MineralItemController>(out var m))
                m.OnNearbyTerrainDug();
        }
    }

    // 플레이어 충돌 시 획득 로직 (나중에 인벤토리 연동 시 사용)
    void OnCollisionEnter2D(Collision2D other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            // TODO: Add to inventory
            // Destroy(gameObject);
        }
    }

    // [Debug] Scene View에서 지지대를 시각적으로 확인하기 위함
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, supportCheckRadius);

        if (!Application.isPlaying) return;

        // 실제 체크 포인트 시각화
        float r = supportCheckRadius;
        Vector2[] testPoints = new Vector2[]
        {
            (Vector2)transform.position,
            (Vector2)transform.position + new Vector2(r, 0),
            (Vector2)transform.position + new Vector2(-r, 0),
            (Vector2)transform.position + new Vector2(0, r),
            (Vector2)transform.position + new Vector2(0, -r),
            (Vector2)transform.position + new Vector2(r, r),
            (Vector2)transform.position + new Vector2(-r, r),
            (Vector2)transform.position + new Vector2(r, -r),
            (Vector2)transform.position + new Vector2(-r, -r),
        };

        foreach (var p in testPoints)
        {
            bool hasGround = false;
            // 에디터에서는 mapManager가 null일 수 있으므로 안전하게 접근
            if (mapManager != null) 
            {
               hasGround = !mapManager.IsWorldPositionEmpty(p);
            }
            
            // 땅이 있으면 빨강(지지됨), 없으면 초록(공중)
            Gizmos.color = hasGround ? Color.red : Color.green;
            Gizmos.DrawSphere(p, 0.02f);
        }
    }
}
