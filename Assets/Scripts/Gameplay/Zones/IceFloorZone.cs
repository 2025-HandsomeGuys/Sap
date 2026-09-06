// @tags: zone, ice, friction, glacier, player, trigger, environment
using UnityEngine;

/// <summary>
/// 존 진입 시 플레이어를 빙판 이동 모드로 전환하는 IZoneEffect 구현체.
/// PlayerController.isOnIce 플래그를 켜고, 이탈 시 끈다.
/// 실제 미끄러짐 수치(iceFriction, iceAcceleration)는 PlayerController Inspector에서 조정.
///
/// 사용법: ZoneEffectTrigger와 동일 GameObject에 부착.
/// </summary>
public class IceFloorZone : MonoBehaviour, IZoneEffect
{
    public void OnEnter(PlayerStat player)
    {
        var controller = player.GetComponent<PlayerController>();
        if (controller == null) return;

        controller.isOnIce = true;
        Debug.Log($"[IceFloorZone] '{player.name}' 진입 — 빙판 모드 ON");
    }

    public void OnExit(PlayerStat player)
    {
        var controller = player.GetComponent<PlayerController>();
        if (controller == null) return;

        controller.isOnIce = false;
        Debug.Log($"[IceFloorZone] '{player.name}' 이탈 — 빙판 모드 OFF");
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return;
        Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.25f);
        Gizmos.DrawCube(transform.position, col.bounds.size);
    }
#endif
}
