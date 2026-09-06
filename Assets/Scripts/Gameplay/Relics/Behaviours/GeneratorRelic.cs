using System;
using UnityEngine;

namespace Relic
{
    // 발전기(액티브·즉발 + 패시브): 발동하면 드릴 배터리를 즉시 회복하고 쿨다운에 들어간다.
    // 추가로 장착 중이면 매 프레임 배터리가 천천히 자연회복된다(regenPerLevel, units/sec).
    // 지속시간이 없으므로(GetDuration=0) ActiveRelicState가 즉발→Cooldown으로 처리.
    // 배터리 최대치는 약 5.0(maxBattery)이라 회복량도 그 스케일에 맞춘다.
    // RefillBattery는 CurrentBattery setter에서 [0,max] clamp되므로 매프레임 소량 충전이 안전하다.
    [Serializable]
    public class GeneratorRelic : RelicBehaviour
    {
        [SerializeField] private float[] amountPerLevel   = { 2f, 3f, 4f };       // 즉발 회복량(units)
        [SerializeField] private float[] cooldownPerLevel = { 20f, 16f, 12f };    // 쿨다운(초)
        [SerializeField] private float[] regenPerLevel    = { 0.3f, 0.45f, 0.6f };// 자연회복 속도(units/sec)

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override float GetCooldown() => Lv(cooldownPerLevel); // 즉발(Duration=0) → 쿨다운
        public override void OnActivate() => ctx?.mining?.RefillBattery(Lv(amountPerLevel));

        // 장착 중 상시 자연회복. OnUpdate는 릴릭 타입 무관 매 프레임 호출된다.
        public override void OnUpdate()
        {
            ctx?.mining?.RefillBattery(Lv(regenPerLevel) * Time.deltaTime);
        }
    }
}
