using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// 특정 타겟 지형(PolygonCollider2D / TerrainChunk가 있는 오브젝트)을 대상으로 하는 엔티티.
/// TerrainChunk면 동그랗게 파내고, 일반 오브젝트면 부딪혔을 때 통째로 파괴합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class TargetedRollingHoleEntity : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────
    [Header("이동 설정")]
    [Tooltip("+1 = 오른쪽, -1 = 왼쪽.")]
    public float moveDirection = 1f;

    [Tooltip("발사 시 초기 속도 크기 (유닛/초).")]
    public float launchSpeed = 8f;

    [Header("스플라인 곡선 경로 (선택사항)")]
    [Tooltip("돌이 굴러갈 곡선(Spline Container)입니다. 지정하면 물리법칙을 무시하고 곡선을 따라갑니다.")]
    public SplineContainer pathSpline;

    [Tooltip("경로의 '모양'만 사용하고, 시작점을 바위의 현재 위치에 맞춰 평행이동합니다.\n" +
             "특수 청크는 런타임에 임의 좌표로 생성되므로 켜두는 것을 권장합니다.")]
    public bool anchorPathToRock = true;

    [Header("지형 파괴 설정")]
    [Tooltip("구멍을 내거나 파괴할 대상 오브젝트를 인스펙터에서 드래그해서 넣어주세요.")]
    public GameObject targetTerrainObject;

    [Tooltip("이동 중 지형을 파괴할지 여부.")]
    public bool destroyTerrainOnMove = true;

    [Tooltip("이 거리 이상 이동했을 때만 구멍을 냅니다 (TerrainChunk 전용).")]
    public float clearInterval = 0.15f;

    [Tooltip("동그란 모양으로 구멍이 나는 반경 (TerrainChunk 전용).")]
    public float clearRadius = 0.4f;

    [Header("수명 설정")]
    [Tooltip("활성화(Activate) 된 후 몇 초 뒤에 바위가 스스로 사라질지 설정합니다. (0이면 사라지지 않음)")]
    public float lifetime = 5f;

    [Header("플레이어 피격 (선택사항)")]
    public float staminaDamage = 30f;
    public float knockbackForce = 12f;

    // ─── 런타임 ──────────────────────────────────────────────
    private Rigidbody2D _rb;
    private ITerrainManager _terrainManager;
    private TerrainChunk _terrainChunk;
    private Vector2 _lastClearPos;
    private bool _isActive;

    // SplineContainer가 바위 자신(또는 함께 움직이는 부모)에 붙어 있어도 경로가 따라 움직이지 않도록,
    // Activate 시점의 변환을 고정해 스플라인을 월드 공간에 못박는다.
    private Matrix4x4 _splineToWorld;

    // anchorPathToRock 사용 시, 경로 시작점 → 바위 위치로 맞추는 평행이동량.
    private Vector3 _pathOffset;

    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.bodyType = RigidbodyType2D.Kinematic;
        _rb.gravityScale = 0f;
    }

    public void Activate()
    {
        Debug.Log("[TEST_ROCK] [TargetedRollingHoleEntity] Activate() called!");
        if (_isActive) 
        {
            Debug.Log("[TEST_ROCK] [TargetedRollingHoleEntity] Already active. Ignoring.");
            return;
        }
        _isActive = true;

        // 구멍을 낼 타겟 오브젝트에서 Terrain 관련 컴포넌트 찾기
        if (targetTerrainObject != null)
        {
            _terrainManager = targetTerrainObject.GetComponent<ITerrainManager>();
            _terrainChunk = targetTerrainObject.GetComponent<TerrainChunk>();
            
            if (_terrainManager == null && _terrainChunk == null)
            {
                Debug.Log("[TEST_ROCK] [TargetedRollingHoleEntity] 타겟이 일반 오브젝트입니다. 부딪히면 통째로 파괴됩니다!");
            }
            else
            {
                Debug.Log($"[TEST_ROCK] [TargetedRollingHoleEntity] 타겟 지형 연결 성공! (TerrainChunk: {_terrainChunk != null}, Manager: {_terrainManager != null})");
            }
        }
        else
        {
            Debug.LogWarning("[TEST_ROCK] [TargetedRollingHoleEntity] 파괴할 타겟(targetTerrainObject)이 할당되지 않았습니다!");
        }

        _lastClearPos = transform.position;

        // pathSpline 미지정 = 물리 모드를 의도한 것. 지정됐는데 망가졌다면 엉뚱한 궤도로 굴리는 대신 멈춘다.
        if (pathSpline == null)
        {
            LaunchPhysicsMode();
            return;
        }

        if (!TryStartSplineMode())
            Debug.LogError("[TEST_ROCK] [TargetedRollingHoleEntity] 스플라인 경로가 유효하지 않아 바위를 발사하지 않습니다.", this);
    }

    private bool TryStartSplineMode()
    {
        int knotCount = pathSpline.Spline != null ? pathSpline.Spline.Count : 0;
        if (knotCount < 2)
        {
            Debug.LogError($"[TEST_ROCK] [TargetedRollingHoleEntity] pathSpline '{pathSpline.name}'의 노드가 {knotCount}개입니다. " +
                           "Scene 뷰 2D 모드에서 경로를 2개 이상 그려주세요.", this);
            return false;
        }

        _splineToWorld = pathSpline.transform.localToWorldMatrix;

        _pathOffset = Vector3.zero;
        if (anchorPathToRock)
        {
            _pathOffset = transform.position - EvaluateSplineWorld(0f);
            _pathOffset.z = 0f;
            Debug.Log($"[TEST_ROCK] [TargetedRollingHoleEntity] 경로를 바위 위치에 고정 — 평행이동 {_pathOffset}", this);
        }

        if (!BuildArcLengthTable(out float[] tValues, out float[] distances, out float worldLength))
        {
            Debug.LogError($"[TEST_ROCK] [TargetedRollingHoleEntity] pathSpline '{pathSpline.name}'의 XY 경로 길이가 0입니다. " +
                           "노드가 전부 같은 XY 좌표에 겹쳐 있거나 Z축으로만 뻗어 있습니다.", this);
            return false;
        }

        ValidateSplineAuthoring(worldLength);

        _rb.bodyType = RigidbodyType2D.Kinematic;
        _rb.useFullKinematicContacts = true; // 키네마틱 상태에서도 정적(Static) 장애물과 충돌 이벤트를 발생시키도록 설정!
        _rb.gravityScale = 0f;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // 빠른 속도에서 타겟·플레이어를 통과해버리는 것 방지

        StartCoroutine(FollowSplineRoutine(tValues, distances, worldLength));
        Debug.Log($"[TEST_ROCK] [TargetedRollingHoleEntity] 스플라인 경로 이동 시작! 길이 {worldLength:F2}u, 속도 {launchSpeed}u/s → 예상 {worldLength / Mathf.Max(launchSpeed, 0.01f):F1}초", this);
        return true;
    }

    private void LaunchPhysicsMode()
    {
        _rb.bodyType = RigidbodyType2D.Dynamic;
        _rb.gravityScale = 1f;

        Vector2 dir = new Vector2(moveDirection, -1f).normalized;
        _rb.AddForce(dir * launchSpeed, ForceMode2D.Impulse);
        Debug.Log($"[TEST_ROCK] [TargetedRollingHoleEntity] 물리 발사 완료! 방향: {dir}, 속도: {launchSpeed}");

        // 물리 모드는 스스로 끝나지 않으므로 수명으로만 회수한다.
        // (스플라인 모드는 완주 시 코루틴이 파괴하므로 lifetime을 적용하지 않는다 — 경로 중간에 잘리는 것을 막기 위함)
        if (lifetime > 0f)
            Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        if (!_isActive || !destroyTerrainOnMove) return;

        Vector2 cur = transform.position;
        if (Vector2.Distance(cur, _lastClearPos) >= clearInterval)
        {
            MakeHole(cur);
            _lastClearPos = cur;
        }
    }

    private const int SplineSampleCount = 100;

    /// <summary>
    /// 경로가 에디터에서 그린 모양과 다르게 나오는 대표적인 셋업 실수 3가지를 잡아낸다.
    /// </summary>
    private void ValidateSplineAuthoring(float worldLength)
    {
        Transform st = pathSpline.transform;

        // 1) 스케일된 오브젝트에 SplineContainer가 있으면 노드·탄젠트가 전부 그 배율만큼 부풀어 곡선이 폭주한다.
        Vector3 scale = st.lossyScale;
        if (Mathf.Abs(scale.x - 1f) > 0.01f || Mathf.Abs(scale.y - 1f) > 0.01f)
        {
            Debug.LogError($"[TEST_ROCK] [TargetedRollingHoleEntity] SplineContainer '{st.name}'의 월드 스케일이 {scale}입니다. " +
                           $"경로 길이·곡률이 그만큼 증폭됩니다(현재 길이 {worldLength:F1}u). 스케일 1인 오브젝트로 옮기세요.", this);
        }

        // 2) anchorPathToRock을 끈 경우, 경로 시작점이 바위와 떨어져 있으면 출발 즉시 순간이동한다.
        if (!anchorPathToRock)
        {
            float startGap = Vector2.Distance(transform.position, EvaluateSplineWorld(0f));
            if (startGap > 0.5f)
            {
                Debug.LogError($"[TEST_ROCK] [TargetedRollingHoleEntity] 스플라인 시작점이 바위에서 {startGap:F2}u 떨어져 있습니다. " +
                               "출발 즉시 시작점으로 순간이동합니다. 첫 노드를 바위 위치에 맞추거나 anchorPathToRock을 켜세요.", this);
            }
        }

        // 3) 청크 1칸 = 10유닛. 경로가 이보다 훨씬 길면 3D 뷰에서 그리다 Z축 탄젠트가 섞인 경우가 대부분이다.
        if (worldLength > 40f)
        {
            Debug.LogWarning($"[TEST_ROCK] [TargetedRollingHoleEntity] 경로 길이가 {worldLength:F1}u로 비정상적으로 깁니다. " +
                             "Scene 뷰 2D 모드에서 노드·탄젠트를 다시 확인하세요.", this);
        }
    }

    /// <summary>
    /// 스플라인 t(0~1)는 호길이에 비례하지 않으므로, 등속 이동을 위해 t ↔ 누적 XY 거리 테이블을 만든다.
    /// </summary>
    private bool BuildArcLengthTable(out float[] tValues, out float[] distances, out float worldLength)
    {
        tValues = new float[SplineSampleCount + 1];
        distances = new float[SplineSampleCount + 1];

        float accumulated = 0f;
        Vector3 prevPos = EvaluateSplineWorld(0f);

        for (int i = 1; i <= SplineSampleCount; i++)
        {
            float sampleT = i / (float)SplineSampleCount;
            Vector3 currentPos = EvaluateSplineWorld(sampleT);

            accumulated += Vector2.Distance(prevPos, currentPos); // Vector2 캐스팅으로 Z축 무시 (2D 화면상의 실제 거리)

            tValues[i] = sampleT;
            distances[i] = accumulated;

            prevPos = currentPos;
        }

        worldLength = accumulated;
        return worldLength > 0.01f;
    }

    /// <summary>
    /// Activate 시점에 고정한 행렬로 스플라인 로컬 → 월드 변환.
    /// pathSpline.transform을 매번 읽으면 스플라인이 바위 본인에게 붙어 있을 때
    /// 이동할 때마다 경로 원점이 함께 끌려가 좌표가 발산한다.
    /// </summary>
    private Vector3 EvaluateSplineWorld(float t)
    {
        return _splineToWorld.MultiplyPoint3x4(pathSpline.EvaluatePosition(t)) + _pathOffset;
    }

    private System.Collections.IEnumerator FollowSplineRoutine(float[] tValues, float[] distances, float worldLength)
    {
        float distanceTraveled = 0f;
        float originalZ = transform.position.z;

        while (distanceTraveled < worldLength)
        {
            distanceTraveled += launchSpeed * Time.fixedDeltaTime;
            if (distanceTraveled > worldLength) distanceTraveled = worldLength;

            float t = 1f; // 누적합 부동소수 오차로 어느 구간에도 안 걸리면 끝점으로 (0f이면 시작점으로 순간이동한다)
            for (int i = 0; i < SplineSampleCount; i++)
            {
                if (distanceTraveled <= distances[i + 1])
                {
                    float segmentLength = distances[i + 1] - distances[i];
                    float segmentRatio = (segmentLength <= 0f) ? 0f : (distanceTraveled - distances[i]) / segmentLength;
                    t = Mathf.Lerp(tValues[i], tValues[i + 1], segmentRatio);
                    break;
                }
            }

            Vector3 worldPosition = EvaluateSplineWorld(t);
            worldPosition.z = originalZ; // 2D 환경이므로 Z는 원래 값 고정

            // 물리 충돌 이벤트를 받기 위해 Rigidbody를 통해 이동
            _rb.MovePosition(worldPosition);
            yield return new WaitForFixedUpdate();
        }

        // 물리 엔진이 마지막 충돌을 계산할 수 있도록 2스텝 대기
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        Debug.Log($"[TEST_ROCK] [TargetedRollingHoleEntity] 경로 완주 → 바위 파괴. 최종 좌표 {transform.position}", this);
        Destroy(gameObject);
    }

    private void MakeHole(Vector2 pos)
    {
        if (_terrainChunk != null)
        {
            // 단일 TerrainChunk를 직접 파괴
            _terrainChunk.Explode(pos, clearRadius);
        }
        else if (_terrainManager != null)
        {
            // 매니저를 통해 파괴
            _terrainManager.ExplodeTerrain(pos, clearRadius);
        }
    }

    private void OnCollisionEnter2D(Collision2D col)
    {
        Debug.Log($"[TEST_ROCK] [TargetedRollingHoleEntity] 어떤 물체와 충돌함: {col.gameObject.name} (태그: {col.gameObject.tag}), 실제 충돌 콜라이더: {col.collider.gameObject.name}");

        if (col.gameObject.CompareTag("Player"))
        {
            ApplyPlayerImpact(col);
        }

        // 대상이 일반 오브젝트(지형 청크가 아님)이고 충돌했다면 통째로 파괴
        if (_terrainChunk == null && _terrainManager == null && targetTerrainObject != null)
        {
            if (col.gameObject == targetTerrainObject || col.transform.IsChildOf(targetTerrainObject.transform) ||
                col.collider.gameObject == targetTerrainObject || col.collider.transform.IsChildOf(targetTerrainObject.transform))
            {
                Debug.Log("[TEST_ROCK] [TargetedRollingHoleEntity] 타겟 오브젝트와 충돌했습니다! 오브젝트를 통째로 파괴합니다.");
                Destroy(targetTerrainObject);
                targetTerrainObject = null; // 중복 파괴 방지
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D col)
    {
        Debug.Log($"[TEST_ROCK] [TargetedRollingHoleEntity] 어떤 트리거 영역에 들어감: {col.gameObject.name} (태그: {col.gameObject.tag})");

        // 대상이 트리거 콜라이더인 경우에도 파괴될 수 있도록 처리
        if (_terrainChunk == null && _terrainManager == null && targetTerrainObject != null)
        {
            if (col.gameObject == targetTerrainObject || col.transform.IsChildOf(targetTerrainObject.transform))
            {
                Debug.Log("[TEST_ROCK] [TargetedRollingHoleEntity] 타겟 트리거와 접촉했습니다! 오브젝트를 통째로 파괴합니다.");
                Destroy(targetTerrainObject);
                targetTerrainObject = null; // 중복 파괴 방지
            }
        }
    }

    private void ApplyPlayerImpact(Collision2D col)
    {
        IHazardTarget target = col.gameObject.GetComponentInParent<IHazardTarget>();
        if (target == null) target = col.gameObject.GetComponentInChildren<IHazardTarget>();
        if (target != null)
        {
            target.ApplyHazardDamage(staminaDamage);
        }

        IPlayerController controller = col.gameObject.GetComponentInParent<IPlayerController>();
        if (controller != null)
        {
            Vector2 dir = (col.transform.position - transform.position).normalized;
            controller.ApplyExternalKnockback(dir * knockbackForce, 0.3f);
        }
    }
}
