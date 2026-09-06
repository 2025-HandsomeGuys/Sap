// @tags: elevator, stop, unlock, interface
/// <summary>
/// "이 오브젝트는 어느 정류장의 엘리베이터인가"를 알려주는 창구.
///
/// 엘리베이터 상호작용 구현이 두 갈래여서 필요하다 — 예전 <see cref="ElevatorController"/>와
/// 통합 컴포넌트 <c>WorldInteractable</c>(<c>ElevatorBehaviour</c>). 실제 프리팹
/// (<c>Assets/Prefabs/World/ElevatorPrefab.prefab</c>)에는 후자만 붙어 있다.
/// 해금 판정(<see cref="ElevatorStopUnlockStore"/>)이 한쪽 타입만 보면 조용히 아무 일도 안 일어난다.
/// </summary>
public interface IElevatorStop
{
    /// <summary>이 엘리베이터가 선 정류장의 청크 Y(깊이). 해금 기록의 키다.</summary>
    int StopDepth { get; }

    /// <summary>정류장의 청크 X. <c>ElevatorManager</c>의 등록 키다.</summary>
    int StopXChunk { get; }

    /// <summary>정류장 인덱스(ElevatorLayerCatalog 순서).</summary>
    int StopLayerIndex { get; }

    /// <summary>착지 좌표 보정에 쓰는 실제 월드 위치.</summary>
    UnityEngine.Transform StopTransform { get; }

    /// <summary>도달했음을 알린다 — 처음이면 그 정류장이 해금된다.</summary>
    void NotifyReached();
}
