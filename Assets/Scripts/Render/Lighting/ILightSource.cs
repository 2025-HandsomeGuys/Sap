using UnityEngine;

/// <summary>
/// 빛을 방출하는 광원의 공통 인터페이스 (SOLID - OCP/DIP 준수)
/// </summary>
public interface ILightSource
{
    /// <summary>광원이 활성화되어 있는지 여부</summary>
    bool IsActive { get; }

    /// <summary>광원의 중심 위치 (월드 좌표)</summary>
    Vector3 Position { get; }

    /// <summary>광원이 향하는 방향 (정규화된 벡터)</summary>
    Vector3 Direction { get; }

    /// <summary>빛이 도달하는 최대 거리</summary>
    float MaxDistance { get; }

    /// <summary>빛의 시야각 (원뿔형은 < 360, 전방향은 360)</summary>
    float Angle { get; }

    /// <summary>빛이 시작되는 내경 (플레이어 주변 시야와 겹침 방지)</summary>
    float InnerRadius { get; }

    /// <summary>그림자 처리를 위해 쏘는 레이(Ray)의 총 개수</summary>
    int RayCount { get; }

    /// <summary>
    /// 각 레이별 충돌 거리를 반환합니다. 
    /// 배열의 길이는 항상 RayCount와 같아야 합니다.
    /// </summary>
    float[] GetRayDistances();
}

