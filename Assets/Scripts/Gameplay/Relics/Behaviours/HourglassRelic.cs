using System;
using UnityEngine;

namespace Relic
{
    // 모래시계: 패시브. 장착 중 다른 슬롯 유물의 쿨타임 최대치를 레벨별 %만큼 줄인다.
    // 적용점: RelicManager.ActivateSlot이 발동 시점에 GetCooldownScale 곱을 GetCooldown()에 반영.
    // (이미 진행 중인 쿨다운은 그대로 — 다음 발동부터 적용.)
    [Serializable]
    public class HourglassRelic : RelicBehaviour
    {
        [SerializeField] private float[] reductionPerLevel = { 0.30f, 0.40f, 0.50f };

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override float GetCooldownScale() => 1f - Mathf.Clamp01(Lv(reductionPerLevel));
    }
}
