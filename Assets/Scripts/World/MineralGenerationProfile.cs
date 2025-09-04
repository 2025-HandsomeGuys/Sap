using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MineralGenerationProfile", menuName = "World/Mineral Generation Profile")]
public class MineralGenerationProfile : ScriptableObject
{
    // This class holds the settings for a single type of minable item.
    [System.Serializable]
    public class MinableSpawnConfig
    {
        [Tooltip("인스펙터에서 알아보기 쉽도록 설명을 적어두세요.")]
        public string description;
        public PoolableType minableType;
        
        [Tooltip("가로축(X): 깊이(양수), 세로축(Y): 이 깊이의 청크에 광맥이 나타날 확률(0-1)")]
        public AnimationCurve spawnChanceByDepth;

        [Tooltip("청크당 생성할 광맥의 최소/최대 개수")]
        public Vector2Int veinsPerChunk;

        [Tooltip("광맥 하나의 최소/최대 길이")]
        public Vector2Int veinLength;

        [Tooltip("광맥 내 광물 사이의 최소 간격 (기본값: 1)")]
        public int veinSpacing = 1;
    }

    public List<MinableSpawnConfig> minableSpawnConfigs;
}
