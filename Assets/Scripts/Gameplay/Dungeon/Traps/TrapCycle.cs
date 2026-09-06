using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>period 주기 중 앞 activeFraction 비율 동안 활성.</summary>
    public struct TrapCycle
    {
        public float period;
        public float activeFraction;

        public bool IsActive(float time)
        {
            if (period <= 0f) return true;
            float phase = Mathf.Repeat(time, period) / period; // [0,1)
            return phase < activeFraction;
        }
    }
}
