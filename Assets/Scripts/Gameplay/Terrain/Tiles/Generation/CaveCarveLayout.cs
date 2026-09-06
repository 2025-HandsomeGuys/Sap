// @tags: cave, terrain, generation, chunk, layout, deterministic
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "그 좌표의 청크가 절차적 굴을 파는가"의 **단일 원천**.
///
/// 왜 따로 두는가 — 이 질문은 두 자리에서 나온다:
///   1. 청크가 자기 자신에게: "나는 굴을 파나?"        (ResolveCaveSettings)
///   2. 청크가 이웃에게:     "옆 청크도 굴을 파나?"     (연결 통로를 뻗을지)
///
/// 예전엔 이 둘이 **각자 다른 규칙표**를 봤다. 자기 판단만 PNG 페인팅 청크(보호 좌표)를 제외하고
/// 이웃 판단은 그 줄이 빠져 있어서, 페인팅 청크 옆 청크가 "쟤도 판다"고 믿고 통로를 뻗었다가
/// 경계에서 수직 벽으로 뚝 끊겼다. 같은 질문에 답하는 함수가 둘이면 언젠가 갈린다.
///
/// ⚠ 여기 들어오는 판정은 전부 **좌표만 보고 항상 같은 답**이어야 한다.
///   청크는 로드 순서가 제각각이고 이웃이 아직 안 떠 있어도 답이 나와야 하기 때문이다.
///   런타임 상태(로드 여부, 이미 칠했는지, 매니저가 떠 있는지)에 의존하면 로드 순서에 따라
///   양쪽 답이 갈려 그 자리에 잘린 벽이 생긴다.
///   그래서 페인팅 좌표도 "칠할 때"가 아니라 **Awake 에서 인스펙터 값으로** 등록한다.
/// </summary>
public static class CaveCarveLayout
{
    /// <summary>
    /// PNG 로 픽셀을 덮어쓰는 청크 좌표. <see cref="ImageChunkOverrider"/> 같은 씬 배치
    /// 컴포넌트가 Awake 에서 자기 인스펙터 좌표를 등록한다.
    ///
    /// SpecialChunkManager 의 보호 좌표와 목적이 다르다 — 그쪽은 "데코를 깔지 마라"는
    /// 런타임 상태고(칠하는 시점에 등록된다), 이쪽은 "이 좌표는 원래 굴을 안 판다"는 배치 정보다.
    /// </summary>
    private static readonly HashSet<Vector2Int> s_paintedCoords = new HashSet<Vector2Int>();

    /// <summary>씬 배치 컴포넌트가 Awake 에서 부른다. 청크 로딩이 시작되기 전이어야 한다.</summary>
    public static void RegisterPaintedCoord(Vector2Int coord) => s_paintedCoords.Add(coord);

    public static void UnregisterPaintedCoord(Vector2Int coord) => s_paintedCoords.Remove(coord);

    public static bool IsPaintedCoord(Vector2Int coord) => s_paintedCoords.Contains(coord);

    /// <summary>
    /// 그 좌표의 청크가 절차적 굴을 파는지. 자기 판단과 이웃 판단이 **반드시 이걸 함께** 쓴다.
    ///
    /// 엘리베이터 정류장은 매니저(ShouldSpawnElevator)가 아니라 <see cref="ElevatorStopLayout"/>
    /// 을 직접 본다 — 매니저가 아직 안 떠 있어도 답이 나와야 하고, 그게 정류장 좌표의 단일 원천이다.
    /// </summary>
    public static bool Carves(Vector2Int coord, int worldSeed)
    {
        // 천장 청크. 여기 굴이 뚫리면 지상과 통하는 구멍이 생겨 입구 연출과 충돌한다.
        if (coord.y >= 0) return false;

        if (TileDataManager.Instance == null) return false;

        TileType layer = TileDataManager.Instance.GetTileTypeAtPosition(coord.x, coord.y);
        if (!TileDataManager.Instance.GetCaveSettings(layer).enabled) return false;

        // PNG 로 방을 그리는 청크. 재로드 때 IsChunkVisited 면 다시 칠하지 않으므로,
        // 굴을 뚫으면 방 벽이 뚫린 채 아무도 복구하지 않는다.
        if (IsPaintedCoord(coord)) return false;

        // 엘리베이터 정류장. ElevatorDecorator 는 지형을 안 보고 승강로를 놓기 때문에
        // 굴 한가운데 엘리베이터가 떠 있을 수 있다. 정류장은 반드시 도달해야 하는 자리라
        // 운에 맡기지 않는다. (착지 방 PNG 도 전부 이 좌표에 칠해진다.)
        if (ElevatorStopLayout.IsStopCoord(coord)) return false;

        // 특수청크·서브청크·링크피스는 프리팹 픽셀을 쓰므로 굴을 안 판다.
        // 결정론적 예측 API — 나침반 유물(PredictAnchorsInRadius)이 쓰는 것과 같다.
        var scm = SpecialChunkManager.Instance;
        if (scm != null)
        {
            if (scm.GetSpecialChunk(coord, layer, worldSeed) != null) return false;
            if (scm.IsSubChunkCoord(coord, layer, worldSeed)) return false;
        }

        return true;
    }
}
