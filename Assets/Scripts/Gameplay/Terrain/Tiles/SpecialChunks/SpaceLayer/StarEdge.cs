using UnityEngine;
using System;

namespace Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer
{
    /// <summary>
    /// 별빛 잇기 퍼즐에서 두 노드 간의 연결 선(Edge)을 나타내는 데이터 구조체입니다.
    /// 양방향이 동일한 것으로 취급합니다. (A-B == B-A)
    /// </summary>
    [Serializable]
    public struct StarEdge : IEquatable<StarEdge>
    {
        public string NodeAId;
        public string NodeBId;

        public StarEdge(string a, string b)
        {
            // 정렬해서 항상 동일한 순서를 유지 (A-B == B-A). 로케일 무관하도록 Ordinal 비교.
            if (string.CompareOrdinal(a, b) < 0)
            {
                NodeAId = a;
                NodeBId = b;
            }
            else
            {
                NodeAId = b;
                NodeBId = a;
            }
        }

        public bool Equals(StarEdge other)
        {
            // 생성자에서 이미 정렬되므로 순서대로 비교하면 됨
            return NodeAId == other.NodeAId && NodeBId == other.NodeBId;
        }

        public override bool Equals(object obj)
        {
            return obj is StarEdge other && Equals(other);
        }

        public override int GetHashCode()
        {
            if (string.IsNullOrEmpty(NodeAId) || string.IsNullOrEmpty(NodeBId))
                return 0;

            return NodeAId.GetHashCode() ^ NodeBId.GetHashCode();
        }

        public bool Contains(string nodeId)
        {
            return NodeAId == nodeId || NodeBId == nodeId;
        }
    }
}
