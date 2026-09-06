// @tags: market, theme, palette, color, dark-retro, ui
using UnityEngine;

namespace Market
{
    /// <summary>
    /// NEON 마켓 다크 레트로 팔레트 중앙 정의 (설계 §3.1). 주식·코인 UI가 동일 색을 공유한다.
    /// 프리팹에 색을 하드코딩하지 말고 여기서 가져온다.
    /// </summary>
    public static class MarketTheme
    {
        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static readonly Color ScreenBg     = Hex("#0D1117");
        public static readonly Color PanelBg       = Hex("#151B26");
        public static readonly Color PanelBgAlt    = Hex("#11161F");
        public static readonly Color PanelBorder   = Hex("#243043");
        public static readonly Color RowSelected    = Hex("#1B2A3D");
        public static readonly Color AccentCyan     = Hex("#38BDF8");
        public static readonly Color AccentTeal     = Hex("#2DD4BF");
        public static readonly Color BrandA         = Hex("#5B6CF0");
        public static readonly Color BrandB         = Hex("#8B5CF6");
        public static readonly Color Gold           = Hex("#F5C542");
        public static readonly Color Up             = Hex("#3FB950");
        public static readonly Color Down           = Hex("#F85149");
        public static readonly Color TextPrimary    = Hex("#E6EDF3");
        public static readonly Color TextMuted      = Hex("#8B98A9");
        public static readonly Color TextDim        = Hex("#5A6678");
        public static readonly Color BadgeRed       = Hex("#F85149");
        public static readonly Color ChipBg         = Hex("#1E2733");

        // 시장 심리(공포 0 → 탐욕 1) 그라데이션 색.
        private static readonly Color Fear    = Hex("#F85149");
        private static readonly Color Caution = Hex("#F0883E");
        private static readonly Color Warm    = Hex("#F5C542");
        private static readonly Color Greed   = Hex("#3FB950");

        /// <summary>t: 0(공포/빨강) → 0.5(노랑) → 1(탐욕/초록).</summary>
        public static Color SentimentColor(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.33f) return Color.Lerp(Fear, Caution, t / 0.33f);
            if (t < 0.66f) return Color.Lerp(Caution, Warm, (t - 0.33f) / 0.33f);
            return Color.Lerp(Warm, Greed, (t - 0.66f) / 0.34f);
        }

        // 현재 태그 체계(24종) 큐레이션 팔레트. 목표는 색상환 전체를 고르게 쓰는 것 —
        // 예전엔 녹색·틸·시안에 10종이 몰리고 빨강·주황·보라가 비어 "색을 다 안 쓰는" 느낌이었다.
        // 그래서 분야가 굳이 그 색이 아니어도 되는 태그(food·consumer 등)는 비어 있던 따뜻한 쪽으로 옮겨
        // 따뜻(빨강~노랑) / 초록 / 시안~파랑 / 보라~마젠타 네 구역에 고르게 나눴다.
        // 같은 소분야(바이오 트리오 등)는 명도로 구분하되 같은 계열 안에 둔다.
        // 채도·명도는 다크 레트로 네온 톤에 맞춰 높게 유지(어두운 칩에서 묻히지 않게).
        // 해시 분산 방식은 태그가 적을 땐 인접 색이 겹쳐 보여서, 고정 목록엔 이 표를 우선 쓴다.
        // 여기 없는 태그(미래 추가분)는 아래 해시 색상환 폴백이 색을 배정한다.
        private static readonly System.Collections.Generic.Dictionary<string, Color> s_tagPalette =
            new System.Collections.Generic.Dictionary<string, Color>(System.StringComparer.OrdinalIgnoreCase)
        {
            // 중공업·소재 (주황 → 브론즈 → 앰버 / 하드웨어·화학은 보라 구역으로 분리)
            { "construction",  Hex("#F2661F") }, // 선명한 오렌지
            { "mining",        Hex("#C6772E") }, // 브론즈
            { "materials",     Hex("#F5A623") }, // 골드-앰버
            { "hardware",      Hex("#7C6CFF") }, // 페리윙클(인디고~바이올렛)
            { "chemical",      Hex("#C24FE6") }, // 바이올렛-마젠타(실험실 톤)
            // 테크·디지털 (블루 → 인디고)
            { "software",      Hex("#2E8BF7") }, // 블루
            { "semiconductor", Hex("#565BF0") }, // 인디고
            // 에너지 (옐로우골드 → 라임)
            { "energy",        Hex("#F5D338") }, // 옐로우-골드
            { "battery",       Hex("#B9E637") }, // 라임
            // 농식품·그린 (food는 따뜻한 토마토색으로 빼서 초록 밀집 완화)
            { "food",          Hex("#EF5D3C") }, // 토마토 레드-오렌지
            { "agriculture",   Hex("#5EBE33") }, // 그래스 그린
            { "renewable",     Hex("#22C56A") }, // 그린
            // 바이오·헬스 (에메랄드 → 민트 → 틸, 명도로 구분)
            { "bio",           Hex("#10C79A") }, // 에메랄드
            { "vaccine",       Hex("#5AD9C6") }, // 민트
            { "pharma",        Hex("#0FA6C0") }, // 틸-시안
            // 물류·이동 (아주어 → 스카이)
            { "logistics",     Hex("#2597D6") }, // 아주어
            { "transport",     Hex("#46B4EC") }, // 스카이-블루
            { "tourism",       Hex("#78D2F2") }, // 라이트 스카이
            // 커머스·소비 (코랄 → 핫핑크 → 마젠타)
            { "consumer",      Hex("#FF6A48") }, // 코랄
            { "retail",        Hex("#F2478C") }, // 핫핑크
            { "ecommerce",     Hex("#E650C6") }, // 마젠타
            // 금융·통신·방산 (남은 구역: 샴페인 골드 / 순시안 / 건메탈)
            { "finance",       Hex("#EFE36A") }, // 샴페인 골드(energy 옐로우보다 밝고 채도 낮게)
            { "telecom",       Hex("#00E5FF") }, // 퓨어 시안(tourism 라이트스카이보다 채도 높게)
            { "defense",       Hex("#6E7F8C") }, // 건메탈 슬레이트(유일한 무채색 계열)
        };

        /// <summary>
        /// 태그 문자열 → 고유 액센트 색. 현재 태그(24종)는 <see cref="s_tagPalette"/>의 큐레이션 색을 쓰고,
        /// 목록에 없는 태그는 해시를 HSV 색상환에 분산시켜 배정한다(황금비 회전).
        /// 어느 쪽이든 <b>같은 태그면 언제 어디서든 항상 같은 색</b> — 주식 상세 칩(TagChipList)과
        /// 필터 패널(StockFilterPanel)이 이 함수를 공유해 두 화면의 색이 일치한다.
        /// 채도·명도는 다크 레트로 네온 톤에 맞춰 높게 유지한다.
        /// </summary>
        public static Color TagAccent(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return TextMuted;

            // 현재 태그 체계는 손으로 고른 팔레트 우선.
            if (s_tagPalette.TryGetValue(tag, out var curated)) return curated;

            // 폴백: FNV-1a 32bit — 플랫폼·세션 무관하게 같은 문자열이면 같은 값.
            uint h = 2166136261u;
            foreach (char c in tag) { h ^= c; h *= 16777619u; }

            // 황금비(0.618…)를 곱해 색상환에 고르게 흩뿌린다. double로 계산해 프랙션 정밀도 확보.
            double g = h * 0.618033988749895;
            float hue = (float)(g - System.Math.Floor(g));            // 0~1 전체 색상환
            float sat = 0.60f + ((h >> 8) & 0xFF) / 255f * 0.25f;     // 0.60~0.85
            float val = 0.85f + ((h >> 16) & 0xFF) / 255f * 0.13f;    // 0.85~0.98
            return Color.HSVToRGB(hue, sat, val);
        }
    }
}
