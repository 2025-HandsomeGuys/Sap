// @tags: map, minimap, terrain, cache, snapshot, chunk, worldmap
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지도(미니맵·전체 지도)용 지형 스냅샷 캐시.
///
/// 문제: 청크가 로드돼 있으면 픽셀 단위 지형을 읽어 터널을 또렷하게 그릴 수 있지만,
/// 플레이어가 멀어져 청크가 언로드되면 픽셀 데이터가 사라져 지도가 실제 지형 모양을
/// 알 수 없다. 그러면 탐사 셀(coarse, 3유닛)만으로 칠하게 돼 굵고 흐릿한 덩어리가 된다.
///
/// 해결: 청크가 로드돼 있을 때 그 지형(빈 공간 여부)을 청크당 RES×RES로 다운샘플해 캐시한다.
/// 언로드된 뒤에도 지도는 이 캐시를 읽어 로드된 곳과 동일하게 또렷한 터널을 그린다.
/// (미니맵이 매 프레임 근처 청크를 캐시에 넣어두므로, 전체 지도를 열면 지나온 길이 전부 또렷하다.)
/// </summary>
public static class MapTerrainCache
{
    /// <summary>청크당 스냅샷 해상도. 지도 텍셀보다 촘촘하면 충분히 또렷하다.</summary>
    public const int RES = 96;

    /// <summary>메모리 상한 — 이 개수를 넘으면 새 청크는 캐시하지 않는다(기존 것은 계속 갱신).</summary>
    private const int MaxChunks = 4000;

    // 청크 좌표 → RES×RES 빈공간 마스크 (1 = 빈 공간/통로, 0 = 암석)
    private static readonly Dictionary<Vector2Int, byte[]> _cache = new Dictionary<Vector2Int, byte[]>();

    // 청크 좌표 → 파기로 낡아진 RES 격자 영역(누적 병합, max는 배타적).
    // Invalidate가 채우고 Capture가 그 영역만 다시 구운 뒤 제거한다. 항목 없음 = 캐시가 최신.
    private static readonly Dictionary<Vector2Int, RectInt> _dirtyBoxes = new Dictionary<Vector2Int, RectInt>();

    /// <summary>
    /// 지형이 파여 스냅샷이 낡았음을 알린다. <see cref="InfinityMapManager.MarkChunkDirty"/>가 호출한다.
    ///
    /// 이게 없으면 미니맵이 "혹시 팠을지 모르니" 근처 청크를 매 렌더 통째로 다시 굽는 수밖에 없어
    /// 렌더당 RES×RES×9 = 약 83,000회의 IsTransparent 호출이 낭비된다. 파인 영역만 정확히
    /// 알려주면 실제로 바뀐 수십~수백 텍셀만 다시 구우면 된다.
    /// </summary>
    /// <param name="rect">청크 픽셀 좌표계의 변경 영역 (xMax/yMax 배타적).</param>
    public static void Invalidate(TerrainChunk chunk, RectInt rect)
    {
        if (chunk == null) return;
        var coord = new Vector2Int(chunk.ChunkX, chunk.ChunkY);

        // 아직 한 번도 안 구운 청크는 첫 Capture가 전 영역을 굽는다 — 여기서 기록할 필요 없다.
        if (!_cache.ContainsKey(coord)) return;
        if (!TryGetChunkPixelSize(chunk, out int chunkPx, out int chunkPy)) return;

        // 픽셀 → RES 격자. 다운샘플 경계에 걸친 텍셀이 누락되지 않도록 사방 1칸 여유를 둔다.
        int bx0 = Mathf.Clamp(rect.xMin * RES / chunkPx - 1, 0, RES - 1);
        int by0 = Mathf.Clamp(rect.yMin * RES / chunkPy - 1, 0, RES - 1);
        int bx1 = Mathf.Clamp(rect.xMax * RES / chunkPx + 1, 0, RES - 1);
        int by1 = Mathf.Clamp(rect.yMax * RES / chunkPy + 1, 0, RES - 1);

        if (_dirtyBoxes.TryGetValue(coord, out RectInt cur))
        {
            int x0 = Mathf.Min(cur.xMin, bx0), y0 = Mathf.Min(cur.yMin, by0);
            int x1 = Mathf.Max(cur.xMax, bx1 + 1), y1 = Mathf.Max(cur.yMax, by1 + 1);
            _dirtyBoxes[coord] = new RectInt(x0, y0, x1 - x0, y1 - y0);
        }
        else
        {
            _dirtyBoxes[coord] = new RectInt(bx0, by0, bx1 - bx0 + 1, by1 - by0 + 1);
        }
    }

    /// <summary>
    /// 청크 전체가 통째로 갈렸음을 알린다(PNG 청크 교체 등 파기가 아닌 경로).
    /// 파기처럼 영역이 좁지 않으므로 다음 Capture에서 전 영역을 다시 굽는다.
    /// </summary>
    public static void InvalidateAll(TerrainChunk chunk)
    {
        if (chunk == null) return;
        var coord = new Vector2Int(chunk.ChunkX, chunk.ChunkY);
        if (!_cache.ContainsKey(coord)) return;
        _dirtyBoxes[coord] = new RectInt(0, 0, RES, RES);
    }

    /// <summary>로드된 청크의 지형을 캐시에 스냅샷한다.</summary>
    /// <param name="force">
    /// true면 이미 캐시된 청크도 전 영역을 다시 굽는다. 파기는 <see cref="Invalidate"/>가 이미
    /// 처리하므로 상시 렌더 경로에서는 false가 정상이고, true는 훅을 타지 않는 경로
    /// (PNG 청크 교체·풀 재사용 등)를 자가 치유하는 저빈도 순번 재베이크에만 쓴다.
    /// </param>
    public static void Capture(Vector2Int coord, TerrainChunk chunk, bool force)
    {
        if (chunk == null || !chunk.baseData.IsCreated) return;

        bool existed = _cache.TryGetValue(coord, out var buf);
        bool hasDirty = _dirtyBoxes.TryGetValue(coord, out RectInt box);
        if (existed && !hasDirty && !force) return;

        if (!TryGetChunkPixelSize(chunk, out int chunkPx, out int chunkPy)) return;

        if (!existed)
        {
            if (_cache.Count >= MaxChunks) return;
            buf = new byte[RES * RES];
            _cache[coord] = buf;
        }

        // 처음 굽는 청크와 순번 재베이크는 전 영역, 그 외에는 파인 영역만.
        if (!existed || force) box = new RectInt(0, 0, RES, RES);
        _dirtyBoxes.Remove(coord);

        for (int y = box.yMin; y < box.yMax; y++)
        {
            int sy = Mathf.Min((int)((y + 0.5f) / RES * chunkPy), chunkPy - 1);
            int row = y * RES;
            for (int x = box.xMin; x < box.xMax; x++)
            {
                int sx = Mathf.Min((int)((x + 0.5f) / RES * chunkPx), chunkPx - 1);
                buf[row + x] = chunk.IsTransparent(sx, sy) ? (byte)1 : (byte)0;
            }
        }
    }

    private static bool TryGetChunkPixelSize(TerrainChunk chunk, out int px, out int py)
    {
        px = py = 0;
        var imm = InfinityMapManager.Instance;
        if (imm == null) return false;
        px = Mathf.Max(1, Mathf.RoundToInt(imm.chunkWidthWorld * chunk.PPU));
        py = Mathf.Max(1, Mathf.RoundToInt(imm.chunkHeightWorld * chunk.PPU));
        return true;
    }

    /// <summary>
    /// 청크의 RES×RES 마스크를 직접 얻는다(없으면 null). 1 = 빈 공간/통로, 0 = 암석.
    ///
    /// 미니맵 텍셀 루프처럼 같은 청크를 수만 번 연속 조회하는 쪽에서 쓴다. 호출측이 마스크를
    /// 들고 있으면 청크가 바뀔 때만 Dictionary를 타고, 나머지는 배열 인덱싱 한 번으로 끝난다.
    /// </summary>
    public static byte[] GetMask(Vector2Int coord)
        => _cache.TryGetValue(coord, out var buf) ? buf : null;

    /// <summary>월드 청크의 (fracX,fracY) 지점이 빈 공간인지. 캐시에 있으면 true 반환+empty 채움.</summary>
    public static bool TrySampleEmpty(Vector2Int coord, float fracX, float fracY, out bool empty)
    {
        if (_cache.TryGetValue(coord, out var buf))
        {
            int x = Mathf.Clamp((int)(fracX * RES), 0, RES - 1);
            int y = Mathf.Clamp((int)(fracY * RES), 0, RES - 1);
            empty = buf[y * RES + x] != 0;
            return true;
        }
        empty = false;
        return false;
    }

    public static void Clear()
    {
        _cache.Clear();
        _dirtyBoxes.Clear();
    }
}
