using UnityEngine;

/// <summary>
/// 도구(toolIndex)별 채굴 능력(땅/돌 파기 가능 여부·효율)의 단일 소스.
/// 모든 채굴 전략(Pickaxe/Sap)이 여기만 참조하므로, 도구 역할 스왑 유물이
/// <see cref="SwapTerrainRock"/> 하나만 토글하면 전 채굴 경로와 관련 유물이 자동 연동된다.
///
/// [SRP] 도구→능력 매핑 한 곳. [OCP] 스왑/추가 규칙을 여기서만 확장.
/// </summary>
public static class ToolCapabilities
{
    /// <summary>도구 역할 스왑(삽=돌, 곡괭이=땅). 스왑 유물이 장착 중일 때 true.</summary>
    public static bool SwapTerrainRock;

    // 도메인 리로드 비활성(Enter Play Mode Options) 환경에서도 정적 상태를 초기화.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => SwapTerrainRock = false;

    /// <summary>toolIndex(0=맨손,1=삽,2=곡괭이)에 대한 채굴 능력을 해석한다.</summary>
    public static void Resolve(int toolIndex, out bool canDigTerrain, out bool canDigRock, out float efficiency)
    {
        switch (toolIndex)
        {
            case 0: canDigTerrain = true;  canDigRock = false; efficiency = 0.1f; break; // 맨손: 흙 10%
            case 1: canDigTerrain = true;  canDigRock = false; efficiency = 1.0f; break; // 삽: 흙
            case 2: canDigTerrain = false; canDigRock = true;  efficiency = 1.0f; break; // 곡괭이: 돌
            default: canDigTerrain = true; canDigRock = false; efficiency = 1.0f; break;
        }

        // 삽(1)·곡괭이(2)만 역할 스왑. 맨손(0)은 그대로.
        if (SwapTerrainRock && (toolIndex == 1 || toolIndex == 2))
        {
            (canDigTerrain, canDigRock) = (canDigRock, canDigTerrain);
        }
    }
}
