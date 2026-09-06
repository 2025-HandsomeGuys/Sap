using UnityEngine;

/// <summary>
/// DiggableRock의 기본 랜덤 드롭을 대체하고 싶은 컴포넌트가 구현한다.
/// DiggableRock.DestroyRock()이 파괴 시 이 인터페이스를 조회한다.
/// </summary>
public interface IRockDropOverride
{
    /// <summary>드롭을 처리했으면 true. true면 DiggableRock의 기본 드롭(DropRareMinerals)을 건너뛴다.</summary>
    bool TryDropOnDestroy(Vector3 rockCenter);
}
