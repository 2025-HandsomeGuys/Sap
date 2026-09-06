// @tags: interface, event, digging, terrain, listener
using UnityEngine;

/// <summary>
/// 타일(픽셀) 파괴 이벤트를 수신하는 리스너 인터페이스.
/// TileEventDispatcher에 Register/Unregister하여 OnTileDestroyed 콜백을 받는다.
///
/// [SOLID]
///   ISP: 단일 메서드만 선언 — 구현체는 필요한 것만 담당.
///   DIP: TileEventDispatcher는 구체 클래스 대신 이 인터페이스에만 의존.
///   OCP: 새 반응(ExplosiveMineralReactor, IcicleSpawner 등)은 이 인터페이스 구현만으로 확장.
/// </summary>
public interface ITileDestroyListener
{
    /// <summary>
    /// 픽셀이 파괴될 때 TileEventDispatcher가 호출한다.
    /// </summary>
    /// <param name="worldPos">파괴된 픽셀의 월드 좌표</param>
    /// <param name="destroyedColor">파괴된 픽셀의 원래 색상 (타일 타입 판별에 활용 가능)</param>
    void OnTileDestroyed(Vector2 worldPos, Color32 destroyedColor);
}
