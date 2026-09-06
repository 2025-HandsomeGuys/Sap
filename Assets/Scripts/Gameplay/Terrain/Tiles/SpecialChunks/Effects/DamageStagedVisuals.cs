// @tags: vfx, damage, special-chunk, sprite, interface
using UnityEngine;

/// <summary>
/// HP 단계별 스프라이트 교체를 담당하는 재사용 컴포넌트.
///
/// SOLID:
///  - SRP: 스프라이트 교체 비주얼만 담당.
///  - OCP: IDamageStageable 구현 → HP 엔티티 교체 없이 확장 가능.
///  - DIP: 사용처(TrashWallEntity, DiggableRock 등)는 IDamageStageable 인터페이스만 알면 됨.
///
/// 사용 방법:
///  1. SpriteRenderer가 붙은 프리팹에 이 컴포넌트 추가.
///  2. Inspector에서 stages[0~2] 에 스프라이트 연결, 또는 코드에서 SetStages() 호출.
///  3. HP 엔티티의 Awake에서 GetComponents&lt;IDamageStageable&gt;() 로 자동 수집됨.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class DamageStagedVisuals : MonoBehaviour, IDamageStageable
{
    [Tooltip("0=정상(67~100%), 1=균열1(34~66%), 2=균열2(0~33%)")]
    public Sprite[] stages = new Sprite[3];

    private SpriteRenderer _sr;
    // PolygonCollider2D가 있을 경우 스프라이트 교체 시 자동 갱신을 방지하기 위해 캐시
    private PolygonCollider2D _polyCollider;

    // HP 단계 전환 기준 (JSON 로드 후 덮어써짐)
    private float _threshold0 = 0.66f; // 이 비율 초과 → 정상
    private float _threshold1 = 0.33f; // 이 비율 초과 → crack1, 이하 → crack2

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _polyCollider = GetComponent<PolygonCollider2D>(); // 없으면 null (TrashWall은 BoxCollider)

        // JSON 설정 적용
        if (SpecialChunkSettingsLoader.Instance != null)
        {
            var thresholds = SpecialChunkSettingsLoader.Instance.Settings.physics.damageStagedThresholds;
            if (thresholds != null && thresholds.Length >= 2)
            {
                _threshold0 = thresholds[0];
                _threshold1 = thresholds[1];
            }
        }

        // Inspector에서 stages[0]을 비워뒀을 경우 현재 스프라이트를 기본값으로 사용
        if (stages[0] == null && _sr.sprite != null)
            stages[0] = _sr.sprite;
    }

    /// <summary>
    /// 코드에서 스프라이트를 주입할 때 사용.
    /// 돌(DiggableRock)은 프리팹 인스펙터에서 stages[]를 직접 채우므로 이 경로를 쓰지 않는다.
    /// </summary>
    public void SetStages(Sprite normal, Sprite crack1, Sprite crack2)
    {
        stages[0] = normal;
        stages[1] = crack1;
        stages[2] = crack2;

        // 현재 풀HP 상태이므로 즉시 정상 스프라이트 적용
        if (normal != null) ApplySprite(normal);
    }

    // IDamageStageable
    public void OnHpRatioChanged(float hpRatio)
    {
        int idx = hpRatio > _threshold0 ? 0
                : hpRatio > _threshold1 ? 1
                : 2;

        // 폴백: 해당 단계 스프라이트가 없으면 이전 단계를 사용 (DiggableRock 기존 동작 유지)
        Sprite target = stages[idx];
        if (target == null && idx > 0) target = stages[idx - 1];
        if (target == null && idx > 1) target = stages[idx - 2];

        if (target != null && _sr.sprite != target)
            ApplySprite(target);
    }

    /// <summary>
    /// 스프라이트 교체 + PolygonCollider2D 경로 보존.
    /// Unity는 SpriteRenderer.sprite를 교체하면 PolygonCollider2D를 자동 갱신한다.
    /// 그러면 파기 판정 형태가 변경되므로, 교체 전 경로를 저장하고 교체 후 복원한다.
    /// (BoxCollider2D는 이 문제가 없으므로 TrashWall에서는 경로 저장/복원 로직이 건너뛰어진다.)
    /// </summary>
    private void ApplySprite(Sprite target)
    {
        if (_polyCollider == null)
        {
            // PolygonCollider2D 없음 (TrashWall 등) — 단순 교체
            _sr.sprite = target;
            return;
        }

        // PolygonCollider2D 경로 보존 (DiggableRock용)
        int pathCount = _polyCollider.pathCount;
        Vector2[][] savedPaths = new Vector2[pathCount][];
        for (int i = 0; i < pathCount; i++)
            savedPaths[i] = _polyCollider.GetPath(i);

        _sr.sprite = target;

        _polyCollider.pathCount = pathCount;
        for (int i = 0; i < pathCount; i++)
            _polyCollider.SetPath(i, savedPaths[i]);
    }
}
