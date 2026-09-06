// @tags: trap, special-chunk, hazard, falling, vibration, damage, player
using UnityEngine;

/// <summary>
/// 산화된 공동 종유석 — 플레이어 접근(하향 Raycast) 또는 채굴 진동 감지 시
/// 돌가루 예고 후 낙하. 착지 시 연쇄 진동 + 플레이어 스태미나 피격 + 이동속도 저하.
/// 낙하/예고/착지 공통 로직은 FallingHazardBase가 담당.
/// </summary>
public class StalactiteTrap : FallingHazardBase
{
    [Header("Detection  ※ 런타임에 specialChunkSettings.json 값으로 덮어씀")]
    [Tooltip("플레이어 감지 하향 Raycast 거리 (유닛) — JSON: traps.stalactite.detectionRange")]
    [SerializeField] private float detectionRange = 3f;

    [Tooltip("플레이어 레이어 마스크")]
    [SerializeField] private LayerMask playerLayer;

    [Header("Impact — Player  ※ 런타임에 specialChunkSettings.json 값으로 덮어씀")]
    [Tooltip("충돌 시 플레이어 스태미나 피격량 — JSON: traps.stalactite.staminaDamage")]
    [SerializeField] private float staminaDamage = 20f;

    [Tooltip("이동 속도 저하 배율 (0.6 = 40% 감소) — JSON: traps.stalactite.slowMultiplier")]
    [SerializeField] private float slowMultiplier = 0.6f;

    [Tooltip("이동 속도 저하 지속 시간 (초) — JSON: traps.stalactite.slowDuration")]
    [SerializeField] private float slowDuration = 2.0f;

    private void Update()
    {
        if (_warning || _falling) return;

        // Raycast 하향 감지 — 플레이어가 아래 진입 시 예고 후 낙하
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, detectionRange, playerLayer);
        if (hit.collider != null)
            BeginDropSequence();
    }

    protected override void LoadSettings()
    {
        if (SpecialChunkSettingsLoader.Instance == null) return;

        var s          = SpecialChunkSettingsLoader.Instance.Settings;
        detectionRange = s.traps.stalactite.detectionRange;
        staminaDamage  = s.traps.stalactite.staminaDamage;
        slowMultiplier = s.traps.stalactite.slowMultiplier;
        slowDuration   = s.traps.stalactite.slowDuration;
        impactRadius   = s.traps.stalactite.impactVibrationRadius;
        warningDelay   = s.physics.fallWarningDelay;
    }

    protected override void OnLanded(Collision2D col)
    {
        if (!col.gameObject.CompareTag("Player")) return;

        var target = col.gameObject.GetComponent<IHazardTarget>();
        var buff   = col.gameObject.GetComponent<BuffStatProvider>();

        target?.ApplyHazardDamage(staminaDamage);
        buff?.AddBuff(
            $"StalactiteSlow_{GetInstanceID()}",
            StatType.MoveSpeed,
            ModifierType.Percent,
            slowMultiplier,
            slowDuration
        );

        Debug.Log($"[StalactiteTrap] 플레이어 피격 — 피해 -{staminaDamage}, 이동속도 {slowMultiplier}x ({slowDuration}s)");
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * detectionRange);
    }
#endif
}
