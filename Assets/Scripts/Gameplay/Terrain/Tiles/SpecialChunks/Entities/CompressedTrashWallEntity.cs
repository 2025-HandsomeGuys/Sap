// @tags: special-chunk, digging, entity, damage, loot, vfx, explosion, collapse, chain-reaction
using System.Collections;
using UnityEngine;

/// <summary>
/// 압축 쓰레기 벽 엔티티.
/// 쓰레기가 암석처럼 압축된 특수 블록으로, 곡괭이로만 채굴 가능.
/// DiggableBlockBase가 HP·HitCooldown·IDamageStageable 브로드캐스트를 처리한다.
/// 이 클래스는 VFX·드롭·연쇄 붕괴만 담당한다 (SRP).
///
/// 연쇄 붕괴 — 런타임 반경 탐색 방식:
///  - 블록이 부서지면 주변 일정 반경을 Physics2D로 탐색한다.
///  - 아직 안 무너진(_isCollapsing=false) 가장 가까운 이웃을 최대 _maxPropagateCount개 골라 신호 전달.
///  - 신호받은 블록은 랜덤 지연 후 무너지고, 다시 자기 주변을 탐색 → 도미노처럼 누적 지연으로 퍼진다.
///  - _isCollapsing 플래그로 이미 신호받은 블록은 자동 제외 → 되돌아오는 bounce 없음.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CompressedTrashWallEntity : DiggableBlockBase
{
    [Header("연쇄 붕괴 설정")]
    [SerializeField]
    [Tooltip("연쇄 붕괴가 전파되는 탐색 반경(월드 유닛). 청크 1칸 = 10유닛.")]
    private float _propagateRadius = 12f;

    [SerializeField]
    [Tooltip("한 블록이 연쇄를 전달할 최대 이웃 수.")]
    private int _maxPropagateCount = 2;

    [SerializeField]
    [Tooltip("다음 블록으로 연쇄 붕괴가 전달되는 최소 지연 시간")]
    private float _collapseDelayMin = 0.15f;

    [SerializeField]
    [Tooltip("다음 블록으로 연쇄 붕괴가 전달되는 최대 지연 시간")]
    private float _collapseDelayMax = 0.35f;

    private CompressedTrashVFX _vfx;
    private ILootDropper _dropper;

    private bool _isCollapsing = false;

    // 여러 인스턴스가 순차적으로(메인 스레드) 재사용하는 공용 탐색 버퍼.
    private static readonly Collider2D[] s_overlapBuffer = new Collider2D[32];

    protected override void Awake()
    {
        _vfx     = GetComponent<CompressedTrashVFX>();
        _dropper = GetComponent<ILootDropper>();
        base.Awake();
    }

    protected override void OnHit() => _vfx?.PlayHit();

    protected override void OnDie(Vector3 center)
    {
        _dropper?.Drop(center);
        _vfx?.PlayDestroy(center);

        PropagateCollapse(center);
    }

    /// <summary>주변 반경을 탐색해 아직 안 무너진 가장 가까운 이웃 최대 N개에게 연쇄 신호를 전달한다.</summary>
    private void PropagateCollapse(Vector3 center)
    {
        _isCollapsing = true;

        // 전 레이어 탐색 후 GetComponent로 필터 — 압축 쓰레기 벽만 걸러진다.
        // (LayerMask에 의존하지 않아 마스크 미설정으로 인한 연쇄 실패가 없다.)
        ContactFilter2D filter = new ContactFilter2D() { useLayerMask = true, layerMask = Physics2D.AllLayers };
        int hitCount = Physics2D.OverlapCircle(center, _propagateRadius, filter, s_overlapBuffer);

        // 가장 가까운 순으로 최대 _maxPropagateCount개 선택.
        // 선택된 블록은 즉시 _isCollapsing=true가 되므로 다음 반복에서 자동 제외 → 중복 없음.
        int propagated = 0;
        while (propagated < _maxPropagateCount)
        {
            CompressedTrashWallEntity best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                var col = s_overlapBuffer[i];
                if (col == null) continue;

                var wall = col.GetComponent<CompressedTrashWallEntity>();
                if (wall == null || wall == this) continue;
                if (wall._isCollapsing || wall._currentHp <= 0f) continue;

                float sqr = ((Vector2)(wall.transform.position - center)).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = wall;
                }
            }

            if (best == null) break;

            // 약간의 랜덤 지연으로 도미노처럼 하나씩 펑펑 터지도록 연출
            best.Collapse(Random.Range(_collapseDelayMin, _collapseDelayMax));
            propagated++;
        }
    }

    /// <summary>이웃으로부터 연쇄 신호를 받아 일정 지연 후 무너진다.</summary>
    public void Collapse(float delay)
    {
        if (_isCollapsing || _currentHp <= 0f) return;
        _isCollapsing = true;
        StartCoroutine(CollapseRoutine(delay));
    }

    private IEnumerator CollapseRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_currentHp > 0f)
        {
            _currentHp = 0f;
            Die();
        }
    }
}
