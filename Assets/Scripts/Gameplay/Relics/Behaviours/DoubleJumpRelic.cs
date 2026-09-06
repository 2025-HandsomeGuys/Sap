using System;
using UnityEngine;

namespace Relic
{
    // 비둘기 깃털: 공중에서 추가 점프. 레벨별 추가 점프 횟수.
    [Serializable]
    public class DoubleJumpRelic : RelicBehaviour
    {
        [SerializeField] private int[] airJumpsPerLevel = { 1, 1, 2 };

        private int _airJumpsUsed;

        private int MaxAirJumps
        {
            get
            {
                int idx = Mathf.Clamp(level - 1, 0, airJumpsPerLevel.Length - 1);
                return airJumpsPerLevel[idx];
            }
        }

        public override void OnLanded()
        {
            _airJumpsUsed = 0; // 착지 시 리셋
        }

        public override bool TryConsumeAirJump()
        {
            if (_airJumpsUsed >= MaxAirJumps) return false;
            _airJumpsUsed++;
            return true;
        }
    }
}
