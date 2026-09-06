using UnityEngine;
using Stock.Data;

namespace Stock.Systems
{
    /// <summary>
    /// 종목 해금 판정. 땅 레이어(채광 레벨) 진행도를 종목 티어로 환산한다.
    ///
    /// 매핑: <b>종목 티어 = 채광 레벨 + 1</b>
    ///   채광 Lv.0(시작)  → 티어 1 종목만
    ///   채광 Lv.1        → 티어 1~2   (업그레이드 "심층 탐사 면허 I"  = MiningLevel 1)
    ///   채광 Lv.2        → 티어 1~3   (업그레이드 "심층 탐사 면허 II" = MiningLevel 2)
    ///   채광 Lv.3+       → 전 종목
    ///
    /// 채광 레벨은 UpgradeEffectType.MiningLevel 업그레이드로만 오르고
    /// 같은 값이 지형 파기 게이트(<c>PlayerStat.CanDig</c> ↔ <c>tileData.tier</c>)에도 쓰인다.
    /// 즉 "그 땅을 팔 수 있게 된 시점"과 "그 티어 종목이 상장되는 시점"이 같은 값으로 묶인다.
    /// </summary>
    public static class StockUnlockGate
    {
        /// <summary>시작부터 열려 있는 최저 티어.</summary>
        public const int MinTier = 1;

        /// <summary>Companies.csv가 쓰는 최고 티어.</summary>
        public const int MaxTier = 4;

        // PlayerStat 탐색 비용을 아끼기 위한 캐시. 씬 전환으로 파괴되면 Unity null 검사로 걸러 재탐색한다.
        private static PlayerStat _playerStat;

        /// <summary>
        /// 현재 채광 레벨. <b>진실 원천은 업그레이드 시스템(심층 탐사 면허 구매)</b>이다.
        ///
        /// 마켓 씬은 정착지 씬 위에 Additive로 얹히는데, 정착지/마켓 문맥의 PlayerStat에는
        /// 업그레이드 스탯 프로바이더가 붙어 있지 않을 수 있다. 그 경우 <c>PlayerStat.MiningLevel</c>은
        /// 업그레이드 모디파이어가 빠진 <b>베이스 값(0)</b>으로 읽혀, 지하 지형은 열려도(지하 플레이어는
        /// 프로바이더가 있음) 마켓의 주식만 tier 1에 영구 고정되는 버그가 났다.
        ///
        /// <see cref="UpgradeManager"/>는 DontDestroyOnLoad 싱글톤이라 어느 씬에서든 동일한 값을 준다.
        /// 베이스 채광 레벨(프리팹 기본값, 보통 0)에 구매한 면허 효과를 합쳐 씬과 무관하게 계산한다.
        /// </summary>
        public static int CurrentMiningLevel
        {
            get
            {
                float baseLevel = ResolveBaseMiningLevel();

                var upgrades = UpgradeManager.Instance;
                if (upgrades != null)
                    return Mathf.RoundToInt(upgrades.GetStatValue(UpgradeEffectType.MiningLevel, baseLevel));

                // UpgradeManager가 아직 없으면(초기화 전 등) 베이스 값만
                return Mathf.RoundToInt(baseLevel);
            }
        }

        /// <summary>업그레이드 모디파이어를 <b>제외한</b> 베이스 채광 레벨. PlayerStat → 세이브 → 0 순으로 폴백.</summary>
        private static float ResolveBaseMiningLevel()
        {
            if (_playerStat == null)
                _playerStat = Object.FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);

            // GetBaseValue는 최종값(모디파이어 포함)이 아니라 기준값만 준다 → UpgradeManager 합산과 중복되지 않는다.
            if (_playerStat != null)
                return _playerStat.GetBaseValue(StatType.MiningLevel);

            // 마켓 씬이 단독으로 열린 경우(테스트 등) 세이브 베이스 값으로 폴백
            var save = Object.FindFirstObjectByType<SaveManager>(FindObjectsInactive.Include);
            if (save != null && save.playerData != null)
                return save.playerData.miningLevel;

            return 0f;
        }

        /// <summary>현재 해금된 최고 종목 티어(1~4).</summary>
        public static int UnlockedTier => Mathf.Clamp(CurrentMiningLevel + MinTier, MinTier, MaxTier);

        /// <summary>해당 티어를 열기 위해 필요한 채광 레벨.</summary>
        public static int RequiredMiningLevel(int tier) => Mathf.Max(0, tier - MinTier);

        /// <summary>이 종목이 지금 상장되어 있는지. 티어 값이 없는(0) 데이터는 항상 열린 것으로 본다.</summary>
        public static bool IsUnlocked(CompanyData company)
        {
            if (company == null) return false;
            if (company.Tier <= 0) return true;
            return company.Tier <= UnlockedTier;
        }

        /// <summary>씬 전환 등으로 캐시를 강제로 버릴 때 사용.</summary>
        public static void InvalidateCache()
        {
            _playerStat = null;
        }
    }
}
