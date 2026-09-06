// @tags: dungeon, spawn, marker, entry
using UnityEngine;

/// <summary>
/// 던전 입구 스폰 지점 마커. 던전 지오메트리 씬 안, 플레이어가 시작할 위치에 빈 GameObject로 배치한다.
/// DungeonOverlayController가 던전 씬을 로드한 뒤 이 위치로 플레이어를 텔레포트한다.
/// 던전 씬당 하나만 두면 된다.
/// </summary>
public class DungeonEntryPoint : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawLine(transform.position + Vector3.down * 0.5f, transform.position + Vector3.up * 0.5f);
    }
}
