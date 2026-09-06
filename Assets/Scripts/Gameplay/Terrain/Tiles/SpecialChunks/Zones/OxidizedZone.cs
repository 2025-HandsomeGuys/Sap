// @tags: zone, stamina, damage, special-chunk, trigger, player
using System.Collections;
using UnityEngine;

/// <summary>
/// 산화된 공동 페널티: 플레이어가 동굴 내에서 벽타기 중일 때 MaxStamina 기준값을 영구 감소시킨다.
/// ZoneEffectTrigger와 동일 GameObject에 부착.
///
/// [SOLID]
///   SRP: MaxStamina 영구 감소 페널티만 담당. 트리거 감지는 ZoneEffectTrigger에 위임.
///   OCP: IZoneEffect 구현체 — ZoneEffectTrigger 수정 없이 확장.
///   DIP: PlayerStat.SetBaseValue()로 기준값 직접 조작 (BuffStatProvider 미사용 — 원복 불필요).
///
/// 주의: 이탈 시 원복 없음. SetBaseValue()는 영구 변경이므로 SaveData에도 반영됨.
/// </summary>
public class OxidizedZone : MonoBehaviour, IZoneEffect
{
    [Header("Penalty Settings")]
    [Tooltip("벽타기 1틱당 MaxStamina 영구 감소량")]
    [SerializeField] private float penaltyPerTick = 5f;

    [Tooltip("감소 주기 (초)")]
    [SerializeField] private float tickInterval = 1.0f;

    [Tooltip("MaxStamina 최솟값 — 이 이하로는 내려가지 않음")]
    [SerializeField] private float minMaxStamina = 20f;

    private WaitForSeconds _wait;
    private Coroutine _penaltyCoroutine;

    private PlayerStat _playerStat;
    private PlayerController _playerController;

    #region Unity Lifecycle

    private void Awake()
    {
        // JSON 설정 적용 (tickInterval 변경 전에 먼저 적용)
        if (SpecialChunkSettingsLoader.Instance != null)
        {
            var s = SpecialChunkSettingsLoader.Instance.Settings.zones.oxidized;
            penaltyPerTick = s.penaltyPerTick;
            tickInterval   = s.tickInterval;
            minMaxStamina  = s.minMaxStamina;
        }
        _wait = new WaitForSeconds(tickInterval);
    }

    private void OnDisable()
    {
        StopPenalty();
    }

    #endregion

    #region IZoneEffect

    public void OnEnter(PlayerStat player)
    {
        _playerStat       = player;
        _playerController = player.GetComponent<PlayerController>();

        if (_playerController == null)
        {
            Debug.LogWarning("[OxidizedZone] PlayerController를 찾을 수 없습니다. 페널티 비활성화.");
            return;
        }

        StopPenalty();
        _penaltyCoroutine = StartCoroutine(PenaltyCoroutine());
        Debug.Log("[OxidizedZone] 진입 — 벽타기 페널티 감시 시작.");
    }

    public void OnExit(PlayerStat player)
    {
        StopPenalty();
        _playerStat       = null;
        _playerController = null;
        Debug.Log("[OxidizedZone] 이탈 — 페널티 원복 없음 (영구 감소).");
    }

    #endregion

    #region Private

    private void StopPenalty()
    {
        if (_penaltyCoroutine == null) return;
        StopCoroutine(_penaltyCoroutine);
        _penaltyCoroutine = null;
    }

    private IEnumerator PenaltyCoroutine()
    {
        while (true)
        {
            yield return _wait;

            if (_playerStat == null || _playerController == null) yield break;
            if (!_playerController.IsWallClimbing) continue;

            float current = _playerStat.GetBaseValue(StatType.MaxStamina);
            if (current <= minMaxStamina) continue;

            float next = Mathf.Max(minMaxStamina, current - penaltyPerTick);
            _playerStat.SetBaseValue(StatType.MaxStamina, next);

            Debug.Log($"[OxidizedZone] MaxStamina 영구 감소: {current} → {next}");
        }
    }

    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return;
        Gizmos.color = new Color(0.8f, 0.2f, 0f, 0.25f);
        Gizmos.DrawCube(transform.position, col.bounds.size);
    }
#endif
}
