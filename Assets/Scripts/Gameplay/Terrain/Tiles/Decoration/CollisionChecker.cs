// @tags: decoration, collider, rock, spawn, utility
using UnityEngine;
using System.Collections.Generic;

public static class CollisionChecker
{
    // [Constants] Moved from TerrainDecorator if needed, but passed as arguments usually.
    private const float EXTRA_SAFETY_MARGIN = 40f; 

    // [New] Radial Collision Check
    public static bool IsCollidingRadial(Vector2 center, float radius, List<TerrainDecorator.RockData> existing, float buffer)
    {
        foreach (var rock in existing)
        {
            float requiredDist = radius + rock.radius + buffer + EXTRA_SAFETY_MARGIN;
            if (Vector2.Distance(center, rock.center) < requiredDist) return true;
        }
        return false;
    }

    // [Helper] Rect List와의 충돌 체크 (Legacy Support)
    public static bool IsCollidingWithRects(Rect rect, List<Rect> targets, float buffer)
    {
        if (targets == null) return false;
        // Rect 충돌 시 Buffer 고려: rect를 buffer만큼 확장해서 검사
        Rect expanded = new Rect(rect.x - buffer, rect.y - buffer, rect.width + buffer * 2, rect.height + buffer * 2);

        foreach (var target in targets)
        {
            if (expanded.Overlaps(target)) return true;
        }
        return false;
    }

    // [Helper] 충돌 여부만 판단 (단일 책임)
    public static bool IsColliding(Rect target, List<Rect> others)
    {
        if (others == null) return false;
        foreach (var other in others)
        {
            if (target.Overlaps(other)) return true;
        }
        return false;
    }
}
