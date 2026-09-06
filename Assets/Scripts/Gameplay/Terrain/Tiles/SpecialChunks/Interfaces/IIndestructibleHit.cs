// @tags: interface, indestructible, digging, feedback, special-chunk
using UnityEngine;

/// <summary>
/// 파괴 불가 영역에 도구로 타격 시도 시 피드백을 제공하는 인터페이스.
/// IndestructibleOverlayInit이 구현하며, Digger가 ModifyTerrain 호출 직전에 감지한다.
/// </summary>
public interface IIndestructibleHit
{
    /// <summary>
    /// 파괴 불가 영역이 타격됐을 때 호출된다.
    /// </summary>
    /// <param name="worldPos">타격 월드 좌표</param>
    /// <param name="toolIndex">사용 도구 인덱스 (0=삽, 2=곡괭이, 3=드릴 등)</param>
    void OnHitAttempt(Vector2 worldPos, int toolIndex);
}
