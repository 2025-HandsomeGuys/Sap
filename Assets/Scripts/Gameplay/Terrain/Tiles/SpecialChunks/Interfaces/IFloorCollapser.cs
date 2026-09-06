// @tags: interface, trap, collapse, pixel, special-chunk
/// <summary>
/// 바닥 픽셀 소멸 처리를 추상화하는 인터페이스.
/// DIP: CollapseFloor는 구체 구현(TerrainChunk 직접 조작)이 아닌 이 인터페이스에 의존한다.
/// SRP: 픽셀 소멸 책임을 CollapseFloor 오케스트레이터에서 분리한다.
/// </summary>
public interface IFloorCollapser
{
    /// <summary>
    /// 청크 상단 <paramref name="floorThicknessPx"/> 행의 픽셀을 소멸시킨다.
    /// </summary>
    /// <param name="floorThicknessPx">소멸시킬 바닥 두께 (픽셀 단위).</param>
    void CollapseFloor(int floorThicknessPx);
}
