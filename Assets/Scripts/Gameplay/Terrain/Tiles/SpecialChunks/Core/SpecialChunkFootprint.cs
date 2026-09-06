// @tags: special-chunk, chunk, data-container, generation, multi-chunk
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 특수 청크 풋프린트·서브좌표·앵커 역산을 담당하는 순수 수학 클래스.
/// MonoBehaviour 의존 없음 — Edit Mode 테스트 가능.
///
/// 좌표 규칙:
///   - Y축은 아래 방향이 음수 (지하 = 음수 Y)
///   - 앵커는 청크의 좌상단 (가장 작은 dx=0, dy=0)
///   - 서브청크는 앵커에서 +X, -Y 방향으로 확장
///
/// [SOLID] SRP: 좌표 계산만. 선택·스폰·레지스트리와 무관.
/// </summary>
public static class SpecialChunkFootprint
{
    /// <summary>
    /// 앵커 좌표와 크기로 점유 rect의 모든 좌표를 set에 채운다.
    /// 예: anchor=(2,-3), size=(3,2) → {(2,-3),(3,-3),(4,-3),(2,-4),(3,-4),(4,-4)}
    /// </summary>
    public static void Build(Vector2Int anchor, Vector2Int size, HashSet<Vector2Int> result)
    {
        for (int dx = 0; dx < size.x; dx++)
            for (int dy = 0; dy > -size.y; dy--)
                result.Add(anchor + new Vector2Int(dx, dy));
    }

    /// <summary>
    /// 앵커 + 크기 → 서브좌표 목록 (앵커 자신 제외).
    /// 예: anchor=(2,-3), size=(3,2) → {(3,-3),(4,-3),(2,-4),(3,-4),(4,-4)}
    /// </summary>
    public static List<Vector2Int> GetSubCoords(Vector2Int anchor, Vector2Int size)
    {
        var result = new List<Vector2Int>();
        for (int dx = 0; dx < size.x; dx++)
            for (int dy = 0; dy > -size.y; dy--)
            {
                if (dx == 0 && dy == 0) continue;
                result.Add(anchor + new Vector2Int(dx, dy));
            }
        return result;
    }

    /// <summary>
    /// subCoord가 size 크기 앵커의 서브좌표라고 가정했을 때
    /// 가능한 앵커 후보 좌표를 열거한다.
    /// - 지상 위 (y > 0) 후보는 제외
    /// - subCoord 자신은 제외
    /// 실제 당첨 여부는 Selector가 판단한다.
    /// </summary>
    public static IEnumerable<Vector2Int> GetCandidateAnchors(Vector2Int subCoord, Vector2Int size)
    {
        for (int ax = subCoord.x - (size.x - 1); ax <= subCoord.x; ax++)
        {
            for (int ay = subCoord.y; ay <= subCoord.y + (size.y - 1); ay++)
            {
                var candidate = new Vector2Int(ax, ay);
                if (candidate.y > 0) continue;
                if (candidate == subCoord) continue;
                yield return candidate;
            }
        }
    }

    /// <summary>
    /// 레이어 경계 근처인지 확인한다.
    /// Abs(coord.y - boundaryY) &lt; spacing 이면 true (경계 자체 포함).
    /// </summary>
    public static bool IsNearLayerBoundary(Vector2Int coord, int[] boundaryYCoords, int spacing)
    {
        foreach (int boundaryY in boundaryYCoords)
            if (Mathf.Abs(coord.y - boundaryY) < spacing)
                return true;
        return false;
    }
}
