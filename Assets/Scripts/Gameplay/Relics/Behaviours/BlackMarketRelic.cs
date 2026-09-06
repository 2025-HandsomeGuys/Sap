using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Relic
{
    // 암시장: 지하에서 인벤토리 광물을 전량 즉시 현금화(정가×레벨배율). 지하 1회/하강.
    // 지상엔 상점이 있으므로 지하 씬(DemoUnderground)에서만 발동 가능.
    [Serializable]
    public class BlackMarketRelic : RelicBehaviour
    {
        private const string UndergroundScene = "DemoUnderground";
        private const float   CooldownSentinel = 999999f; // 소진 후 이번 하강 내내 락

        [Tooltip("레벨별 판매 배율(정가 대비). Lv1=0.8 → Lv3=1.0")]
        [SerializeField] private float[] sellRatePerLevel = { 0.8f, 0.9f, 1.0f };

        [Tooltip("레벨별 하강당 사용 횟수. 지금은 전 레벨 1회.")]
        [SerializeField] private int[] usesPerLevel = { 1, 1, 1 };

        [Tooltip("가격 데이터베이스. 생성기가 자동 연결(런타임 폴백: ShopManager).")]
        [SerializeField] private MineralPriceDatabase priceDb;

        // ── 런타임 전용(비직렬화). 씬 재장착마다 OnEquip에서 리셋 → 하강당 자동 충전. ──
        private MineralInventory _inv;
        private int _usesLeft;

        // 생성기(에디터)에서 가격DB 주입용.
        public BlackMarketRelic Configure(MineralPriceDatabase db)
        {
            priceDb = db;
            return this;
        }

        private float SellRate() => sellRatePerLevel[Mathf.Clamp(level - 1, 0, sellRatePerLevel.Length - 1)];
        private int   UsesForLevel() => usesPerLevel[Mathf.Clamp(level - 1, 0, usesPerLevel.Length - 1)];

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            ResolveRefs();
            _usesLeft = UsesForLevel();
        }

        public override void OnLevelChanged(int lv)
        {
            base.OnLevelChanged(lv);
            // 하강 중 강화 시 남은 횟수를 새 레벨 최대치로 맞춘다(현재 값 동일이라 무해).
            _usesLeft = Mathf.Max(_usesLeft, UsesForLevel());
        }

        private void ResolveRefs()
        {
            if (_inv == null) _inv = UnityEngine.Object.FindFirstObjectByType<MineralInventory>();
            if (priceDb == null)
            {
                var shop = UnityEngine.Object.FindFirstObjectByType<ShopManager>();
                if (shop != null) priceDb = shop.priceDatabase;
            }
        }

        private static bool IsUnderground() => SceneManager.GetActiveScene().name == UndergroundScene;

        private bool HasAnyMineral()
        {
            if (_inv == null) return false;
            foreach (var slot in _inv.ReadonlyItems)
                if (slot != null && slot.item is MineralSO) return true;
            return false;
        }

        // 발동 게이트: 지하 && 광물 보유 && 남은 횟수 && 가격DB 존재.
        public override bool CanActivate()
        {
            ResolveRefs();
            return IsUnderground() && _usesLeft > 0 && priceDb != null && HasAnyMineral();
        }

        public override float GetDuration() => 0f; // 즉발
        // 이번 발동으로 소진되면 센티넬로 UI 락, 아직 남으면 즉시 재사용(미래 N회 대비).
        public override float GetCooldown() => (_usesLeft <= 1) ? CooldownSentinel : 0f;

        // 판매액 계산(순수 함수, 테스트 대상).
        public static int ComputeGold(long raw, float rate) => Mathf.FloorToInt((float)(raw * rate));

        public override void OnActivate()
        {
            ResolveRefs();
            if (_inv == null || priceDb == null) return;

            long raw = 0;
            var distinct = new HashSet<MineralSO>();
            foreach (var slot in _inv.ReadonlyItems)
            {
                if (slot == null || !(slot.item is MineralSO m)) continue;
                raw += (long)priceDb.GetPrice(m.mineralID) * slot.quantity;
                distinct.Add(m);
            }
            if (raw <= 0 || distinct.Count == 0) return;

            int gold = ComputeGold(raw, SellRate());
            ctx?.stat?.AddGold(gold);
            DayEarningsLedger.Report(DayEarningsCategory.MineralSale, gold);

            foreach (var m in distinct) _inv.RemoveAllOf(m);

            _usesLeft--;
            Debug.Log($"[BlackMarket] 광물 전량 판매 → {gold} gold (rate {SellRate():0.##}, 남은 횟수 {_usesLeft})");
        }
    }
}
