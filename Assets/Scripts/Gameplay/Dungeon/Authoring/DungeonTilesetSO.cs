using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Gameplay.Dungeon.Authoring
{
    /// <summary>던전 맵 심볼 → 타일/프리팹 매핑 테이블. 코드 수정 없이 인스펙터에서 확장.</summary>
    [CreateAssetMenu(menuName = "Dungeon/Dungeon Tileset", fileName = "DungeonTileset")]
    public class DungeonTilesetSO : ScriptableObject
    {
        [Serializable]
        public class TileMapping { public string symbol; public TileBase tile; }

        [Serializable]
        public class ObjectMapping { public string symbol; public GameObject prefab; public float zRotation; }

        public struct ObjectEntry { public GameObject prefab; public float zRotation; }

        public List<TileMapping> tileMappings = new List<TileMapping>();
        public List<ObjectMapping> objectMappings = new List<ObjectMapping>();

        private Dictionary<char, TileBase> _tiles;
        private Dictionary<char, ObjectEntry> _objects;

        private void OnEnable() => BuildLookup();

        public void BuildLookup()
        {
            _tiles = new Dictionary<char, TileBase>();
            _objects = new Dictionary<char, ObjectEntry>();
            foreach (var m in tileMappings)
            {
                if (string.IsNullOrEmpty(m.symbol) || m.tile == null) continue;
                _tiles[m.symbol[0]] = m.tile;
            }
            foreach (var m in objectMappings)
            {
                if (string.IsNullOrEmpty(m.symbol) || m.prefab == null) continue;
                _objects[m.symbol[0]] = new ObjectEntry { prefab = m.prefab, zRotation = m.zRotation };
            }
        }

        public bool TryGetTile(char symbol, out TileBase tile)
        {
            if (_tiles == null) BuildLookup();
            return _tiles.TryGetValue(symbol, out tile);
        }

        public bool TryGetObject(char symbol, out ObjectEntry entry)
        {
            if (_objects == null) BuildLookup();
            return _objects.TryGetValue(symbol, out entry);
        }
    }
}
