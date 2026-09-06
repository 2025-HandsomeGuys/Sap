// @tags: cauldron, save, state, store
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 가마솥 좌표→남은횟수 런타임 맵. SaveManager가 Capture/Apply로 PlayerData.cauldronSave와 동기화.
/// 코인·주식 세이브와 동일하게 영속 데이터는 SaveManager 경유.
/// </summary>
public static class CauldronStateStore
{
    private static readonly Dictionary<Vector2Int, int> _remaining = new Dictionary<Vector2Int, int>();

    public static int GetRemainingUses(Vector2Int coord, int defaultUses)
        => _remaining.TryGetValue(coord, out int n) ? n : defaultUses;

    public static void SetRemainingUses(Vector2Int coord, int uses)
        => _remaining[coord] = uses;

    public static CauldronSaveData Capture()
    {
        var data = new CauldronSaveData();
        foreach (var kv in _remaining)
            data.entries.Add(new CauldronEntry { x = kv.Key.x, y = kv.Key.y, remainingUses = kv.Value });
        return data;
    }

    public static void Apply(CauldronSaveData data)
    {
        _remaining.Clear();
        if (data?.entries == null) return;
        foreach (var e in data.entries)
            _remaining[new Vector2Int(e.x, e.y)] = e.remainingUses;
    }

    public static void Clear() => _remaining.Clear();
}
