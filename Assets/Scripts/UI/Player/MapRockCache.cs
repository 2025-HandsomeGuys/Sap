// @tags: map, minimap, worldmap, rock, cache, snapshot, persist
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지도에 '안 캔 돌'을 영속 표시하기 위한 실루엣 스냅샷 캐시(정적).
///
/// 문제: 돌(<see cref="IMapRock"/>)은 청크가 언로드되면 파괴/풀 반납돼 참조가 사라진다.
/// 그래서 라이브 목록만 쓰면 멀어질 때 지도에서 사라졌다가 가까이 가면 다시 생긴다.
///
/// 해결: 돌이 노출될 때 그 실루엣(마스크)을 GRID×GRID 불리언으로 스냅샷해 월드 AABB와 함께 저장한다.
/// 돌은 파괴(캐짐) 전까지 모양이 변하지 않으므로 스냅샷 하나로 충분하다.
///  · 노출  → <see cref="Capture"/>  (재로드 시 같은 좌표키로 갱신 → 중복 없음)
///  · 캐짐  → <see cref="Remove"/>   (지도에서 사라짐)
///  · 언로드 → 아무것도 안 함        (스냅샷 유지 → 멀어져도 지도에 남음)
/// DigPathTracker/MapTerrainCache와 동일하게 세션 동안만 유지된다(디스크 세이브 없음).
/// </summary>
public static class MapRockCache
{
    /// <summary>돌 하나의 스냅샷.</summary>
    public class Entry
    {
        public Vector2 min;       // 월드 AABB 좌하단
        public Vector2 size;      // 월드 AABB 크기
        public byte[] grid;       // GRID×GRID, 1 = 돌 픽셀
        public bool isMineral;    // 광물돌이면 별도 색
    }

    /// <summary>돌 실루엣 스냅샷 해상도.</summary>
    private const int GRID = 32;
    /// <summary>메모리 상한 — 이 개수를 넘으면 새 돌은 캐시하지 않는다.</summary>
    private const int MaxRocks = 6000;

    private static readonly Dictionary<long, Entry> _rocks = new Dictionary<long, Entry>();

    /// <summary>지도가 폴링하는 스냅샷 목록(할당 없는 struct enumerator).</summary>
    public static Dictionary<long, Entry>.ValueCollection Entries => _rocks.Values;

    /// <summary>돌 추가/제거 시 지도 재렌더 트리거.</summary>
    public static bool IsDirty { get; private set; }
    public static void ClearDirty() => IsDirty = false;

    // 돌 중심을 0.25유닛 격자로 반올림한 좌표키 (재로드 중복 방지·제거 매칭).
    private static long Key(Vector2 c)
    {
        long x = Mathf.RoundToInt(c.x * 4f);
        long y = Mathf.RoundToInt(c.y * 4f) & 0xFFFFFFFFL;
        return (x << 32) ^ y;
    }

    /// <summary>노출된 돌의 실루엣을 스냅샷한다(이미 있으면 갱신).</summary>
    public static void Capture(IMapRock rock)
    {
        if (rock == null) return;
        if (rock is UnityEngine.Object uo && uo == null) return;

        Vector2 rc = rock.MapWorldCenter;
        float rr = rock.MapWorldRadius;
        bool shaped = rock.HasMapShape;
        Rect aabb = shaped ? rock.MapWorldBounds : new Rect(rc.x - rr, rc.y - rr, 2f * rr, 2f * rr);
        if (aabb.width <= 0.0001f || aabb.height <= 0.0001f) return;

        long key = Key(rc);
        if (!_rocks.TryGetValue(key, out var e))
        {
            if (_rocks.Count >= MaxRocks) return;
            e = new Entry { grid = new byte[GRID * GRID] };
            _rocks[key] = e;
        }
        e.min = aabb.min;
        e.size = aabb.size;
        e.isMineral = rock.IsMineralRock;

        float r2 = rr * rr;
        for (int gy = 0; gy < GRID; gy++)
        {
            float wy = aabb.yMin + (gy + 0.5f) / GRID * aabb.height;
            int row = gy * GRID;
            for (int gx = 0; gx < GRID; gx++)
            {
                float wx = aabb.xMin + (gx + 0.5f) / GRID * aabb.width;
                bool inside = shaped
                    ? rock.ContainsWorldPoint(new Vector2(wx, wy))
                    : (wx - rc.x) * (wx - rc.x) + (wy - rc.y) * (wy - rc.y) <= r2;
                e.grid[row + gx] = inside ? (byte)1 : (byte)0;
            }
        }
        IsDirty = true;
    }

    /// <summary>돌이 캐졌을 때(파괴) 지도에서 제거한다.</summary>
    public static void Remove(IMapRock rock)
    {
        if (rock == null) return;
        if (rock is UnityEngine.Object uo && uo == null) return;
        if (_rocks.Remove(Key(rock.MapWorldCenter))) IsDirty = true;
    }

    /// <summary>월드 좌표가 이 돌 실루엣 안인지.</summary>
    public static bool Sample(Entry e, float wx, float wy)
    {
        float u = (wx - e.min.x) / e.size.x;
        float v = (wy - e.min.y) / e.size.y;
        if (u < 0f || u >= 1f || v < 0f || v >= 1f) return false;
        int gx = (int)(u * GRID); if (gx >= GRID) gx = GRID - 1;
        int gy = (int)(v * GRID); if (gy >= GRID) gy = GRID - 1;
        return e.grid[gy * GRID + gx] != 0;
    }

    public static void Clear()
    {
        _rocks.Clear();
        IsDirty = true;
    }
}
