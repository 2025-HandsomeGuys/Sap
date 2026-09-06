// @tags: background, chunk, layer, scriptable-object, sprite, terrain, depth
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 층(TileType)별 청크 배경 스프라이트 매핑 테이블.
/// 한 층에 여러 장을 넣으면 청크 좌표 해시로 결정론적으로 하나가 선택된다.
///
/// 배경 스프라이트는 pivot=Center로 임포트해야 한다. (런타임에서 pivot 보정 없음)
/// 설계 문서: Assets/Docs/chunk-background-system.md
/// </summary>
[CreateAssetMenu(fileName = "ChunkBackgroundTable", menuName = "Terrain/Chunk Background Table")]
public class ChunkBackgroundTableSO : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public TileType layer;
        public Sprite[] sprites;
    }

    [SerializeField] private Entry[] entries;

    // TileType enum에 구 7층 잔재(HardStone/CoolStone/HotStone)가 남아 있어
    // 필드 4개로 고정하지 않고 엔트리 배열을 쓴다. enum이 정리돼도 구조가 깨지지 않는다.
    private Dictionary<TileType, Sprite[]> _lookup;

    private void OnEnable() => BuildLookup();

    // 인스펙터에서 엔트리를 수정하면 캐시를 다시 만든다.
    private void OnValidate() => BuildLookup();

    private void BuildLookup()
    {
        _lookup = new Dictionary<TileType, Sprite[]>();
        if (entries == null) return;

        foreach (var e in entries)
        {
            if (e == null) continue;
            if (_lookup.ContainsKey(e.layer))
            {
                Debug.LogWarning($"[ChunkBackgroundTable] 중복 엔트리: {e.layer} — 첫 번째만 사용한다.");
                continue;
            }
            _lookup.Add(e.layer, e.sprites);
        }
    }

    /// <summary>
    /// 해당 층의 배경 후보를 반환한다. 엔트리가 없거나 비어 있으면 null.
    /// </summary>
    public Sprite[] GetSprites(TileType layer)
    {
        if (_lookup == null) BuildLookup();

        if (_lookup.TryGetValue(layer, out var sprites) && sprites != null && sprites.Length > 0)
            return sprites;

        return null;
    }

#if UNITY_EDITOR
    /// <summary>에디터 생성 툴이 빈 엔트리를 미리 채울 때 사용한다.</summary>
    public void Editor_SetLayers(TileType[] layers)
    {
        entries = new Entry[layers.Length];
        for (int i = 0; i < layers.Length; i++)
            entries[i] = new Entry { layer = layers[i], sprites = new Sprite[0] };

        BuildLookup();
    }
#endif
}
