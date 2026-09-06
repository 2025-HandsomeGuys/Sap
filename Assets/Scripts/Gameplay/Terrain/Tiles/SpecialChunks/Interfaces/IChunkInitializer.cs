// @tags: interface, special-chunk, spawn, chunk, initialization
using UnityEngine;

/// <summary>
/// 특수 청크 스폰 직후 호출되는 초기화 인터페이스.
/// SpecialChunkManager는 이 인터페이스에만 의존하며, 구체 구현 클래스를 알지 못한다.
/// OCP/DIP: 새로운 초기화 동작은 이 인터페이스를 구현하기만 하면 된다.
/// </summary>
public interface IChunkInitializer
{
    /// <summary>낮을수록 먼저 실행. 기본값 0.</summary>
    int InitializationOrder { get; }
    void Initialize(Transform parent);
}
