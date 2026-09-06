// @tags: interface, special-chunk, chunk, spawn, multi-chunk
using UnityEngine;

/// <summary>
/// 멀티청크 스페셜의 구성 청크에 붙이는 인터페이스.
/// 앵커가 스폰될 때 각 청크(앵커 포함)에 자신의 상대 위치가 주입된다.
/// </summary>
public interface IMultiChunkPart
{
    /// <summary>
    /// 멀티청크 스폰 직후 호출된다.
    /// </summary>
    /// <param name="anchorCoord">앵커 청크의 청크 좌표</param>
    /// <param name="relativeOffset">앵커 기준 이 청크의 오프셋 (청크 단위). 앵커 본인은 (0,0)</param>
    void OnMultiChunkSpawned(Vector2Int anchorCoord, Vector2Int relativeOffset);
}
