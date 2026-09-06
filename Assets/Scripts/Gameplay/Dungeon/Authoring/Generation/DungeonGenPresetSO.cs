// @tags: dungeon, generation, preset, scriptableobject, config
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// 던전 생성 파라미터. 확률 타일·오브젝트 슬롯 같은 "문법"을 코드가 아닌 데이터로 두어
    /// 심볼을 추가·변경할 때 코드 수정이 필요 없게 한다.
    /// Unity는 char를 직렬화하지 않으므로 심볼은 전부 string으로 선언한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon/Generation Preset", fileName = "DungeonGenPreset")]
    public class DungeonGenPresetSO : ScriptableObject
    {
        /// <summary>확률 타일 1종. chance 확률로 onHit, 아니면 onMiss로 치환된다.</summary>
        [Serializable]
        public class ChanceTile
        {
            public string symbol = "0";
            [Range(0f, 1f)] public float chance = 0.5f;
            public string onHit = "W";
            public string onMiss = ".";
        }

        /// <summary>슬롯 후보 1종. weight는 같은 슬롯의 후보끼리만 비교되는 상대값.</summary>
        [Serializable]
        public class WeightedSymbol
        {
            public string symbol = "!";
            [Min(1)] public int weight = 1;
        }

        /// <summary>
        /// 오브젝트 슬롯 1종. [OBJECTS]의 symbol 자리를 fillChance로 굴려 candidates 중 하나로
        /// 치환하고, 실패하면 지운다. 함정을 템플릿에 직접 박으면 그 방은 매번 똑같이 나오므로
        /// "자리"만 템플릿에 두고 "내용"은 여기서 굴린다.
        /// </summary>
        [Serializable]
        public class ObjectSlot
        {
            public string symbol = "?";
            [Range(0f, 1f)] public float fillChance = 0.6f;
            public List<WeightedSymbol> candidates = new List<WeightedSymbol>();
        }

        [Header("방 하나 크기 (타일)")]
        [Min(3)] public int roomWidth = 10;
        [Min(3)] public int roomHeight = 6;

        [Header("방 템플릿 파일")]
        public List<TextAsset> roomTemplateFiles = new List<TextAsset>();

        [Header("형태 마스크 파일")]
        [Tooltip("던전 격자의 모양. 매판 weight로 하나 뽑는다. 가로·세로·ㄱ자를 텍스트로 그린다.")]
        public List<TextAsset> shapeFiles = new List<TextAsset>();

        [Header("미로")]
        [Tooltip("모든 칸을 잇는 트리를 만든 뒤, 남은 벽을 이 확률로 더 뚫어 순환로를 만든다. " +
                 "0이면 갈림길 없는 나무 구조(막다른 길만 있는 미로), 1이면 격자가 통째로 뚫린다.")]
        [Range(0f, 1f)] public float extraConnectionChance = 0.25f;

        [Header("기본 심볼")]
        public string wallSymbol = "W";
        public string emptySymbol = ".";

        [Header("확률 타일")]
        public List<ChanceTile> chanceTiles = new List<ChanceTile>
        {
            new ChanceTile { symbol = "0", chance = 0.50f, onHit = "W", onMiss = "." },
            new ChanceTile { symbol = "1", chance = 0.25f, onHit = "W", onMiss = "." },
            new ChanceTile { symbol = "2", chance = 0.75f, onHit = "W", onMiss = "." },
        };

        [Header("오브젝트 슬롯")]
        [Tooltip("슬롯 심볼은 타일셋에 매핑된 오브젝트 심볼과 겹치면 안 된다 — 겹치면 템플릿이 직접 놓은 " +
                 "그 오브젝트가 슬롯으로 잡혀 굴림에 덮인다('^'는 점프대라 슬롯으로 못 쓴다). " +
                 "다트(>, <)는 발사 방향이 심볼에 박혀 있어 슬롯을 좌우로 나눈다 — " +
                 "'D'는 왼쪽 벽에 붙는 자리(오른쪽 발사), 'd'는 오른쪽 벽에 붙는 자리(왼쪽 발사). " +
                 "압력판 P ↔ 문 G, 루트 조각처럼 짝·체인이 맞아야 성립하는 기믹은 슬롯에 넣지 말 것.")]
        public List<ObjectSlot> objectSlots = new List<ObjectSlot>
        {
            new ObjectSlot
            {
                symbol = "?", fillChance = 0.60f,
                candidates = new List<WeightedSymbol>
                {
                    new WeightedSymbol { symbol = "!", weight = 3 }, // 개폐가시
                    new WeightedSymbol { symbol = "f", weight = 2 }, // 화염 (상시 점화)
                    new WeightedSymbol { symbol = "_", weight = 2 }, // 무너지는 발판
                    new WeightedSymbol { symbol = "C", weight = 1 }, // 압쇄블록
                    new WeightedSymbol { symbol = "w", weight = 1 }, // 바람 (windLength 기본 1이라 체감 약함)
                },
            },
            new ObjectSlot
            {
                symbol = "D", fillChance = 0.50f,
                candidates = new List<WeightedSymbol> { new WeightedSymbol { symbol = ">", weight = 1 } },
            },
            new ObjectSlot
            {
                symbol = "d", fillChance = 0.50f,
                candidates = new List<WeightedSymbol> { new WeightedSymbol { symbol = "<", weight = 1 } },
            },
        };

        [Header("보상")]
        [Tooltip("맵 전체의 보상 후보 중 여기 적힌 개수만 실제 보상이 된다. " +
                 "방마다 독립으로 굴리면 판마다 보상이 0개~N개로 들쭉날쭉해진다.")]
        [Min(0)] public int rewardCount = 3;
        public string rewardSlotSymbol = "%";
        public string rewardSymbol = "$";

        [Header("안전망")]
        [Tooltip("인접한 경로 방 사이 경계에 통로를 강제로 뚫는다. 템플릿이 다듬어지면 꺼도 된다.")]
        public bool forceCarveBoundaries = true;

        public char Wall => FirstChar(wallSymbol, 'W');
        public char Empty => FirstChar(emptySymbol, '.');
        public char RewardSlot => FirstChar(rewardSlotSymbol, '%');
        public char Reward => FirstChar(rewardSymbol, '$');

        private Dictionary<char, ChanceTile> _chanceLookup;
        private Dictionary<char, ObjectSlot> _slotLookup;

        private void OnEnable() => BuildLookup();
        private void OnValidate() => BuildLookup();

        public void BuildLookup()
        {
            _chanceLookup = new Dictionary<char, ChanceTile>();
            if (chanceTiles != null)
            {
                foreach (var c in chanceTiles)
                {
                    if (c == null || string.IsNullOrEmpty(c.symbol)) continue;
                    _chanceLookup[c.symbol[0]] = c;
                }
            }

            _slotLookup = new Dictionary<char, ObjectSlot>();
            if (objectSlots == null) return;
            foreach (var s in objectSlots)
            {
                if (s == null || string.IsNullOrEmpty(s.symbol)) continue;
                _slotLookup[s.symbol[0]] = s;
            }
        }

        public bool TryGetChanceTile(char symbol, out ChanceTile tile)
        {
            if (_chanceLookup == null) BuildLookup();
            return _chanceLookup.TryGetValue(symbol, out tile);
        }

        public bool TryGetObjectSlot(char symbol, out ObjectSlot slot)
        {
            if (_slotLookup == null) BuildLookup();
            return _slotLookup.TryGetValue(symbol, out slot);
        }

        private static char FirstChar(string s, char fallback)
            => string.IsNullOrEmpty(s) ? fallback : s[0];
    }
}
