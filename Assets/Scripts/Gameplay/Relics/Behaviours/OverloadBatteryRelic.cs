using System;
using UnityEngine;

namespace Relic
{
    // 과부하 배터리(패시브): 드릴 대시 중 배터리 효율이 크게 오르지만(소모 절반 이하),
    // 대가로 대시 방향이 무작위로 바뀐다. 상시 스탯이 아니라 드릴 대시에만 영향을 주므로
    // OnUpdate 없이 장착/해제 시 PlayerMining의 드릴 전략 토글만 세팅한다.
    // 레벨↑ = 효율↑(소모↓) + 방향 재추첨 주기↑(덜 자주 흔들려 다루기 쉬움).
    [Serializable]
    public class OverloadBatteryRelic : RelicBehaviour
    {
        // 대시 배터리 소모 배수(작을수록 효율↑). Lv1=0.5 → 효율 2배(기획 명세).
        [SerializeField] private float[] drainMultPerLevel     = { 0.5f, 0.42f, 0.33f };
        // 방향 재추첨 주기(초). 클수록 한 방향을 오래 유지 → 통제감↑.
        [SerializeField] private float[] randomIntervalPerLevel = { 0.35f, 0.5f, 0.7f };
        // 재추첨 시 직전 방향 대비 최소 회전각(도). 클수록 매번 크게 꺾여 더 미쳐날뛴다.
        // 레벨↑ = 최소 회전각↓(재추첨 주기↑와 함께 통제감↑). 180 미만.
        [SerializeField] private float[] randomMinDeltaPerLevel = { 120f, 100f, 80f };

        // 빈/누락 배열(에셋 재생성 전 등)에는 fallback 반환 → 인덱스 예외 방지.
        private float Lv(float[] a, float fallback = 0f) =>
            (a == null || a.Length == 0) ? fallback : a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            Apply();
        }

        public override void OnLevelChanged(int lv)
        {
            level = lv;
            Apply();
        }

        public override void OnUnequip()
        {
            if (ctx?.mining == null) return;
            // 원상 복구: 소모 배수 1배 + 무작위 방향 off.
            ctx.mining.SetDrillDashDrainMultiplier(1f);
            ctx.mining.SetDrillDashRandomDirection(false);
        }

        private void Apply()
        {
            if (ctx?.mining == null) return;
            ctx.mining.SetDrillDashDrainMultiplier(Lv(drainMultPerLevel));
            ctx.mining.SetDrillDashRandomDirection(true, Lv(randomIntervalPerLevel), Lv(randomMinDeltaPerLevel));
        }
    }
}
