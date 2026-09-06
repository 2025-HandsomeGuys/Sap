// @tags: zone, trigger, player, interface, event
using UnityEngine;

/// <summary>
/// 트리거 진입/이탈 감지만 담당하는 컴포넌트.
/// 동일 GameObject에 붙은 모든 IZoneEffect 구현체에 이벤트를 전달한다.
///
/// [SOLID]
///   SRP: 감지(Detection)만 담당. 효과 적용은 IZoneEffect 구현체에 위임.
///   OCP: 새 효과를 추가할 때 이 클래스를 수정하지 않아도 됨.
///   DIP: 구체 클래스(BuffZone 등) 대신 IZoneEffect 인터페이스에만 의존.
///
/// 사용법: BoxCollider2D(isTrigger=true) + ZoneEffectTrigger + IZoneEffect 구현체 함께 부착.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ZoneEffectTrigger : MonoBehaviour
{
    private IZoneEffect[] _effects;

    private void Awake()
    {
        _effects = GetComponents<IZoneEffect>();

        var col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[ZoneEffectTrigger] '{name}'의 Collider2D가 Trigger가 아닙니다. 자동으로 isTrigger = true 설정.");
            col.isTrigger = true;
        }

        // ── 진단 로그 ──────────────────────────────────────────────
        Vector3 ws = transform.lossyScale;
        Vector3 wp = transform.position;
        string colInfo = col != null
            ? $"type={col.GetType().Name} bounds={col.bounds.size} (worldPos={wp} lossyScale={ws})"
            : "Collider 없음";
        Debug.Log($"[ZoneEffectTrigger] Awake — GO='{name}' " +
                  $"효과 수={_effects.Length} | {colInfo}");
        // ────────────────────────────────────────────────────────────
    }

    private void OnEnable()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return;
        // OnEnable 시점에 bounds가 실제로 계산됨 (SetActive(true) 후)
        Debug.Log($"[ZoneEffectTrigger] OnEnable — GO='{name}' bounds={col.bounds.size} lossyScale={transform.lossyScale} worldPos={transform.position}");
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var player = other.GetComponent<PlayerStat>();
        if (player == null) return;

        Debug.Log($"[ZoneEffectTrigger] '{other.name}' 진입 — {_effects.Length}개 효과 OnEnter 호출");
        foreach (var effect in _effects)
            effect.OnEnter(player);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var player = other.GetComponent<PlayerStat>();
        if (player == null) return;

        Debug.Log($"[ZoneEffectTrigger] '{other.name}' 이탈 — {_effects.Length}개 효과 OnExit 호출");
        foreach (var effect in _effects)
            effect.OnExit(player);
    }
}
