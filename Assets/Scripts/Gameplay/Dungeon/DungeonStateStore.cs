// @tags: dungeon, save, state, store
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 던전 문 좌표 → 인스턴스 상태(부서진 rock·수집 보상) 런타임 맵.
/// SaveManager가 Capture/Apply로 PlayerData.dungeonSave와 동기화 (CauldronStateStore 패턴).
/// CurrentInstance는 진입 시 DungeonDoorChunk가 세팅하고, 던전 씬 컴포넌트가 Mark로 기록한다.
/// </summary>
public static class DungeonStateStore
{
    private static readonly Dictionary<Vector2Int, DungeonInstanceEntry> _map
        = new Dictionary<Vector2Int, DungeonInstanceEntry>();

    public static Vector2Int CurrentInstance { get; private set; }

    public static void SetCurrentInstance(Vector2Int coord) => CurrentInstance = coord;

    private static DungeonInstanceEntry GetOrCreate(Vector2Int coord)
    {
        if (!_map.TryGetValue(coord, out var e))
        {
            e = new DungeonInstanceEntry { x = coord.x, y = coord.y };
            _map[coord] = e;
        }
        return e;
    }

    public static bool IsRockBroken(Vector2Int coord, int rockId)
        => _map.TryGetValue(coord, out var e) && e.brokenRockIds.Contains(rockId);

    public static bool IsRewardCollected(Vector2Int coord, string rewardId)
        => _map.TryGetValue(coord, out var e) && e.collectedRewardIds.Contains(rewardId);

    /// <summary>이 문(인스턴스)을 이미 탐험 완료했는가 → true면 재입장 불가.</summary>
    public static bool IsUsed(Vector2Int coord)
        => _map.TryGetValue(coord, out var e) && e.used;

    /// <summary>이 문(인스턴스)을 탐험 완료로 표시한다.</summary>
    public static void MarkUsed(Vector2Int coord) => GetOrCreate(coord).used = true;

    public static void MarkRockBroken(int rockId)
    {
        var e = GetOrCreate(CurrentInstance);
        if (!e.brokenRockIds.Contains(rockId)) e.brokenRockIds.Add(rockId);
    }

    public static void MarkRewardCollected(string rewardId)
    {
        if (string.IsNullOrEmpty(rewardId)) return;
        var e = GetOrCreate(CurrentInstance);
        if (!e.collectedRewardIds.Contains(rewardId)) e.collectedRewardIds.Add(rewardId);
    }

    public static DungeonSaveData Capture()
    {
        var data = new DungeonSaveData();
        foreach (var kv in _map) data.entries.Add(kv.Value);
        return data;
    }

    public static void Apply(DungeonSaveData data)
    {
        _map.Clear();
        if (data?.entries == null) return;
        foreach (var e in data.entries)
            _map[new Vector2Int(e.x, e.y)] = e;
    }

    public static void Clear() => _map.Clear();
}
