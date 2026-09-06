using System;
using UnityEngine;

namespace Relic
{
    // 용광로(패시브): 상태이상(부상+화상+동상+방사선) 수치가 높을수록 채굴 속도·범위 증가.
    // 위험을 감수할수록 강해지는 리스크·리워드. 속도·범위를 각각 독립 조절.
    // 배율은 항상 1.0 이상(기본보다 절대 작아지지 않음). 버프가 없으면 수정치를 완전히 제거.
    [Serializable]
    public class FurnaceRelic : RelicBehaviour
    {
        [Header("범위 버프 (MiningRange)")]
        [SerializeField] private float[] rangeBonusPerStatus = { 0.04f, 0.05f, 0.06f }; // 상태 1당 증가율
        [SerializeField] private float[] rangeMaxBonus       = { 2.5f, 3.5f, 4.5f };     // 상한(mult 최대 ×3.5~5.5)

        [Header("채굴속도 버프 (MiningSpeed = 삽 차징속도)")]
        [SerializeField] private float[] speedBonusPerStatus = { 0.05f, 0.06f, 0.07f };
        [SerializeField] private float[] speedMaxBonus       = { 3.0f, 4.0f, 5.0f };      // 상한(mult 최대 ×4~6)

        private const string SpeedKey = "relic:furnace:speed";
        private const string RangeKey = "relic:furnace:range";

        private StaminaManager _stamina;
        private float _lastRangeMult = -1f;
        private float _lastSpeedMult = -1f;

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            _lastRangeMult = -1f;
            _lastSpeedMult = -1f;
            Resolve();
        }

        public override void OnUpdate()
        {
            if (_stamina == null) { Resolve(); if (_stamina == null) return; }

            float status = _stamina.Injury + _stamina.Burn + _stamina.Frostbite + _stamina.Radiation;

            float rangeMult = 1f + Mathf.Clamp(status * Lv(rangeBonusPerStatus), 0f, Lv(rangeMaxBonus));
            float speedMult = 1f + Mathf.Clamp(status * Lv(speedBonusPerStatus), 0f, Lv(speedMaxBonus));

            ApplyStat(RangeKey, StatType.MiningRange, rangeMult, ref _lastRangeMult);
            ApplyStat(SpeedKey, StatType.MiningSpeed, speedMult, ref _lastSpeedMult);
        }

        // 배율이 바뀔 때만 갱신. 1.0 초과일 때만 수정치 적용(아니면 제거) → 기본보다 절대 작아지지 않음.
        private void ApplyStat(string key, StatType stat, float mult, ref float last)
        {
            if (Mathf.Abs(mult - last) < 0.01f) return;
            last = mult;

            if (mult > 1.001f)
                ctx.statProvider.Set(key, stat, ModifierType.Percent, mult);
            else
                ctx.statProvider.Clear(key);
        }

        public override void OnUnequip()
        {
            ctx?.statProvider?.Clear(SpeedKey);
            ctx?.statProvider?.Clear(RangeKey);
            _lastRangeMult = -1f;
            _lastSpeedMult = -1f;
        }

        private void Resolve()
        {
            if (_stamina != null || ctx?.player == null) return;
            _stamina = ctx.player.GetComponentInParent<StaminaManager>();
            if (_stamina == null) _stamina = UnityEngine.Object.FindFirstObjectByType<StaminaManager>();
        }
    }
}
