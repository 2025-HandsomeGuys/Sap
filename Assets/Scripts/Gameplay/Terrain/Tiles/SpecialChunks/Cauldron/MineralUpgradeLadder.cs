// @tags: cauldron, mineral, upgrade, ladder, special-chunk
using System;
using System.Collections.Generic;

/// <summary>
/// 도깨비 가마솥 승급 사다리. tileData.json의 층·등급(rarity)을 깊이순으로 펼쳐
/// [층N 일반][층N 희귀] 2칸씩 쌓은 순서표. 한 광물은 가장 얕은 층 첫 등장 위치에만 배치(중복 제거).
/// Unity 비의존 — EditMode 테스트 대상.
/// </summary>
public class MineralUpgradeLadder
{
    private readonly List<List<MineralID>> _rungs = new List<List<MineralID>>();
    private readonly Dictionary<MineralID, int> _rungOf = new Dictionary<MineralID, int>();

    public MineralUpgradeLadder(List<TileDataJson> tilesShallowToDeep)
    {
        if (tilesShallowToDeep == null) return;

        foreach (var tile in tilesShallowToDeep)
        {
            var commonRung = new List<MineralID>();
            var rareRung   = new List<MineralID>();

            if (tile?.minerals != null)
            {
                foreach (var rule in tile.minerals)
                {
                    if (!TryParse(rule?.mineralType, out MineralID id)) continue;
                    if (_rungOf.ContainsKey(id)) continue; // 이미 더 얕은 칸에 배치됨 (bleed-over 무시)

                    var target = rule.IsRare ? rareRung : commonRung;
                    target.Add(id);
                    _rungOf[id] = -1; // 칸 인덱스는 아래에서 일괄 부여, 여기선 등록만
                }
            }

            _rungs.Add(commonRung);
            _rungs.Add(rareRung);
        }

        // 칸 인덱스 확정
        for (int i = 0; i < _rungs.Count; i++)
            foreach (var id in _rungs[i])
                _rungOf[id] = i;
    }

    public int RungCount => _rungs.Count;

    public int RungOf(MineralID id)
        => _rungOf.TryGetValue(id, out int i) ? i : -1;

    public IReadOnlyList<MineralID> Rung(int index)
        => (index >= 0 && index < _rungs.Count) ? _rungs[index] : Array.Empty<MineralID>();

    public MineralID Resolve(MineralID input, int steps, System.Random rng, out bool clamped)
    {
        clamped = false;
        int i = RungOf(input);
        if (i < 0) return MineralID.None;

        int target = i + steps;
        if (target >= _rungs.Count)
        {
            target = _rungs.Count - 1;
            clamped = true;
        }

        var rung = _rungs[target];
        if (rung.Count == 0) return input; // 빈 칸 방어: 입력 그대로
        return rung[rng.Next(rung.Count)];
    }

    private static bool TryParse(string typeName, out MineralID id)
    {
        id = MineralID.None;
        if (string.IsNullOrEmpty(typeName)) return false;
        return Enum.TryParse(typeName, true, out id) && id != MineralID.None;
    }
}
