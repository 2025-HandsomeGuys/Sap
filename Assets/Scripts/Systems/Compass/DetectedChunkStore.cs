// @tags: compass, special-chunk, detection, store, save
using System;
using System.Collections.Generic;
using UnityEngine;

public struct DetectedChunk
{
    public SpecialChunkType type;
    public bool visited;
}

[Serializable]
public struct DetectedChunkEntry
{
    public int x, y;
    public int type;       // (int)SpecialChunkType
    public bool visited;
}

[Serializable]
public class DetectedChunkSaveData
{
    public bool hasData;
    public List<DetectedChunkEntry> entries = new List<DetectedChunkEntry>();
}

/// <summary>
/// 탐지 유물이 발견한 특수청크 좌표의 단일 진실 소스. 미니맵·전체지도가 구독한다.
/// 순수 C# 싱글톤(앱 수명). 세이브는 SaveManager가 Capture/Apply로 연동.
/// </summary>
public class DetectedChunkStore
{
    private static DetectedChunkStore _instance;
    public static DetectedChunkStore Instance => _instance ??= new DetectedChunkStore();

    private readonly Dictionary<Vector2Int, DetectedChunk> _found = new Dictionary<Vector2Int, DetectedChunk>();

    public IReadOnlyDictionary<Vector2Int, DetectedChunk> All => _found;

    /// <summary>발견 데이터 변경 시 발행. 뷰가 마커를 갱신한다.</summary>
    public event Action OnChanged;

    /// <summary>
    /// 파동 링이 이 좌표를 훑고 지나간 순간 발행되는 순수 연출 신호.
    /// OnChanged와 의도적으로 분리한다 — OnChanged는 세이브 로드(Apply)에서도
    /// 발행되므로, 여기에 연출을 붙이면 게임 시작 시 전부 한꺼번에 울린다.
    /// </summary>
    public event Action<Vector2Int> OnPinged;

    /// <summary>탐지 유물 전용. 데이터는 건드리지 않고 연출 신호만 쏜다.</summary>
    public void RaisePinged(Vector2Int coord) => OnPinged?.Invoke(coord);

    /// <summary>파동이 특수청크를 발견/재확인했을 때 호출. 좌표 중복은 갱신(visited 최신화).</summary>
    public void Report(Vector2Int coord, SpecialChunkType type, bool visited)
    {
        if (_found.TryGetValue(coord, out var existing) &&
            existing.type == type && existing.visited == visited)
            return; // 변화 없음 — 이벤트 스팸 방지

        _found[coord] = new DetectedChunk { type = type, visited = visited };
        OnChanged?.Invoke();
    }

    /// <summary>이미 발견된 좌표를 방문 상태로 승격(선택적 훅용).</summary>
    public void MarkVisited(Vector2Int coord)
    {
        if (_found.TryGetValue(coord, out var d) && !d.visited)
        {
            d.visited = true;
            _found[coord] = d;
            OnChanged?.Invoke();
        }
    }

    public void Clear()
    {
        if (_found.Count == 0) return;
        _found.Clear();
        OnChanged?.Invoke();
    }

    public DetectedChunkSaveData Capture()
    {
        var save = new DetectedChunkSaveData { hasData = true };
        foreach (var kv in _found)
            save.entries.Add(new DetectedChunkEntry
            {
                x = kv.Key.x, y = kv.Key.y,
                type = (int)kv.Value.type,
                visited = kv.Value.visited,
            });
        return save;
    }

    public void Apply(DetectedChunkSaveData save)
    {
        _found.Clear();
        if (save != null && save.entries != null)
        {
            foreach (var e in save.entries)
                _found[new Vector2Int(e.x, e.y)] =
                    new DetectedChunk { type = (SpecialChunkType)e.type, visited = e.visited };
        }
        OnChanged?.Invoke();
    }
}
