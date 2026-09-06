using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>
    /// 던전 함정 공용 피해 처리.
    /// 함정은 '부상(injury)'을 입혀 <b>MaxStamina를 깎는다</b> — 현재 스태미나를 쓰는 판정이 아니다.
    /// (낙하 데미지와 동일한 경로: StaminaManager.AddInjury → IStatProvider → MaxStamina Flat 감소)
    /// 무적(i-frame) 중에는 무시하고, 적용 시 피격 플래시 + 무적을 건다.
    /// </summary>
    public static class TrapDamage
    {
        /// <summary>플레이어에게 부상 피해를 준다. 실제로 적용됐으면 true.</summary>
        public static bool ApplyInjury(Collider2D playerCollider, float amount)
        {
            if (playerCollider == null || amount <= 0f) return false;

            var stat = playerCollider.GetComponent<PlayerStat>();
            if (stat == null) stat = playerCollider.GetComponentInParent<PlayerStat>();

            // i-frame 중에는 중복 피해 없음
            if (stat != null && stat.IsInvincible) return false;

            var stamina = playerCollider.GetComponent<StaminaManager>();
            if (stamina == null) stamina = playerCollider.GetComponentInParent<StaminaManager>();
            if (stamina == null) return false;

            stamina.AddInjury(amount); // MaxStamina 감소(부상)
            HitFlashUI.Instance?.Flash(0.5f, 0.2f);

            if (stat != null) stat.StartInvincibility();
            return true;
        }
    }
}
