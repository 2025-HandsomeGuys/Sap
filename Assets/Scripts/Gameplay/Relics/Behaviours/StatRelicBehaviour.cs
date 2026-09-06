using System;
using UnityEngine;
using Relic.Data;

namespace Relic
{
    // 슬라이스 검증용 패시브-스탯 유물. 인스펙터에서 statType·레벨별 값 지정.
    [Serializable]
    public class StatRelicBehaviour : RelicBehaviour
    {
        [SerializeField] private RelicID keyId = RelicID.TestStatRelic; // 고유 키(중복 방지)
        [SerializeField] private StatType statType = StatType.MiningRange;
        [SerializeField] private ModifierType modifierType = ModifierType.Percent;
        [SerializeField] private float[] valuePerLevel = { 1.1f, 1.2f, 1.3f };

        private string Key => $"relic:{keyId}:stat";

        // 생성기에서 슬라이스 기본값을 코드로 지정할 때 사용(SerializeReference 기본값으로 직렬화됨).
        // 예: new StatRelicBehaviour().Configure(RelicID.SpiderGlove, StatType.WallClimbSpeed, ModifierType.Percent, new[]{1.15f,1.3f,1.5f})
        public StatRelicBehaviour Configure(RelicID key, StatType type, ModifierType mod, float[] values)
        {
            keyId = key; statType = type; modifierType = mod; valuePerLevel = values;
            return this;
        }

        private void Apply(int lv)
        {
            int idx = Mathf.Clamp(lv - 1, 0, valuePerLevel.Length - 1);
            ctx.statProvider.Set(Key, statType, modifierType, valuePerLevel[idx]);
        }

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            Apply(lv);
        }

        public override void OnLevelChanged(int lv)
        {
            base.OnLevelChanged(lv);
            Apply(lv);
        }

        public override void OnUnequip()
        {
            ctx?.statProvider?.Clear(Key);
        }
    }
}
