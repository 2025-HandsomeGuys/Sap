using System.Collections.Generic;
using UnityEngine;

namespace Stock.UI
{
    /// <summary>
    /// 종목 id → 아이콘 스프라이트 리졸버. 아이콘은 <c>Assets/Resources/StockIcons/</c>에서
    /// 런타임 로드하므로 인스펙터 와이어링이 필요 없다(SoundManager의 Resources 자동 해석과 동일 패턴).
    /// 파일명은 종목 표시명 기준이라 id와 다르므로 아래 매핑으로 연결한다.
    /// </summary>
    public static class StockIcons
    {
        private const string ResourcePath = "StockIcons/";

        // 종목 id(Companies.csv) → Resources/StockIcons 하위 파일명(확장자 제외)
        private static readonly Dictionary<string, string> IdToFile = new Dictionary<string, string>
        {
            { "comp_dailymart",   "DailyMart" },
            { "comp_deeprock",    "DeepRockMining" },
            { "comp_futurefood",  "FutureFoodTech" },
            { "comp_sunwell",     "SunWellOil" },
            { "comp_ecoenergy",   "EcoEnergy" },
            { "comp_aerolink",    "AeroLink" },
            { "comp_medigen",     "MediGenBio" },
            { "comp_pixelworks",  "PixelWorks" },
            { "comp_titanium",    "TitaniumDefense" },
            { "comp_frostline",   "FrostLine" },
            { "comp_steelcore",   "SteelCore" },
            { "comp_cybernet",    "CyberNetEnter" },
            { "comp_biopure",     "BioPure" },
            { "comp_vitabank",    "VitaFintech" },
            { "comp_voltcell",    "VoltCell" },
            { "comp_nexchip",     "NexChip" },
            { "comp_luxora",      "Luxora" },
            { "comp_aegis",       "AegisDynamics" },
            { "comp_starspace",   "StarSpace" },
            { "comp_goldbridge",  "GoldBridgeHoldings" },
            { "comp_helios",      "HeliosFusion" },
            { "comp_genobio",     "GenoBio" },
            { "comp_quantumcore", "QuantumCore" },
            { "comp_deepai",      "DeepAi" },
            { "comp_coredrill",   "CoreDrillHeavy" },
            { "comp_gaia",        "GaiaEnergy" },
            { "comp_orbitalx",    "OrbitalX" },
            { "comp_hypernet",    "HyperNet" },
            { "comp_eterna",      "EternaBio" },
            { "comp_atlas",       "AtlasCapital" },
            { "comp_leesung",     "LeeSung" },
        };

        // 로드한 스프라이트 캐시(파일명 기준). 없는 아이콘은 null로 캐시해 반복 로드를 막는다.
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        /// <summary>종목 id에 해당하는 아이콘. 매핑·리소스가 없으면 null.</summary>
        public static Sprite Get(string companyId)
        {
            if (string.IsNullOrEmpty(companyId)) return null;
            if (!IdToFile.TryGetValue(companyId, out var file)) return null;
            return Load(file);
        }

        private static Sprite Load(string file)
        {
            if (_cache.TryGetValue(file, out var cached)) return cached;

            // Multiple 스프라이트 시트로 임포트돼 있어도 첫 서브스프라이트를 얻도록 폴백을 둔다.
            var sprite = Resources.Load<Sprite>(ResourcePath + file);
            if (sprite == null)
            {
                var all = Resources.LoadAll<Sprite>(ResourcePath + file);
                if (all != null && all.Length > 0) sprite = all[0];
            }

            _cache[file] = sprite;
            return sprite;
        }
    }
}
