using System;
using System.Collections.Generic;
using UnityEngine;
using Stock.Data;

namespace Stock.Systems
{
    public class StockPriceEngine
    {
        private CompanyManager _companyManager;
        private TagMatchingSystem _tagMatcher;

        // 가격 히스토리 보관 상한 (설계 §9: 최근 100틱). 초과분은 앞에서부터 버려 메모리·세이브 비대화를 막는다.
        private const int MaxPriceHistory = 100;

        // 추세 관성 계수와 1틱당 모멘텀 기여 상한. 양의 피드백 폭주를 막기 위해 약하게 + 캡.
        private const float MomentumFactor = 0.1f;
        private const float MomentumCap = 0.02f;

        // 평균회귀(시간 램프): 기준가에서 벗어난 채로 '틱이 쌓일수록' 되돌리는 힘이 세진다.
        // 목표 = 뉴스로 벌어진 가격이 '아주 천천히'(수십 틱에 걸쳐) 기준가로 복귀(여러 뉴스가 겹쳐도 결국 복귀).
        //   벌어진 가격이 오래 남아 있어야 사고팔 시간이 짧지 않다 → 돈 벌기 난이도를 올린다(즉시 복귀 = 너무 쉬움).
        //  - 첫 틱(갓 벌어짐)엔 약하게(ReversionBase) → 뉴스 스파이크가 보이도록.
        //  - 데드밴드(±ReversionDeadbandPct) 밖에 연속으로 머문 틱 수만큼 ReversionRampPerTick씩 가산, ReversionMax로 상한.
        //  - '연속 틱 수'는 PriceHistory에서 파생하므로 세이브에 새 필드가 필요 없다.
        // 데드밴드는 평상시 틱 노이즈(평균 ±20%)가 램프를 건드리지 않을 만큼 넉넉히 잡아,
        // 뉴스급(>35%) 이탈만 강하게 되돌린다(틱변동 자체는 유지).
        private const float ReversionBase = 0.06f;         // away 1틱째 강도(초반부터 '적당한' 되돌림이 들어오게)
        private const float ReversionRampPerTick = 0.05f;  // 이탈 지속 1틱당 가산
        private const float ReversionMax = 0.26f;          // 강도 상한
        private const float ReversionDeadbandPct = 0.18f;  // 기준가 ±18% 안은 "복귀 완료"로 간주
        // ⚠ 2026-08-27: 0.35→0.18. 데드밴드가 넓으면 기준가에서 ±35%까지는 아무 힘도 안 받아
        //    가격이 그 안에서 한쪽으로 표류하다 밴드 끝(0.4배/10배)에 눌러앉는다(차트가 '한 방향 붕괴'로 보인 원인).
        //    좁히면 평상시에도 약한 복원력이 항상 걸려 기준가 둘레를 오르내린다(= 다채로운 등락).
        // 하락 대칭 부스트: 예전엔 −X%가 +X%보다 로그거리가 커 복귀가 느린 걸 1.4로 맞췄다.
        // 지금은 "낙폭 과대주가 확실히 되돌아오면 안 된다"(줍줍 무지성 매수 방지)가 우선이라 1.0(중립)으로 뒀다
        //  → 하락은 상승보다 되돌림이 오히려 느려도 됨(떨어지는 칼날). 배경: economy/stock-falling-knife.md
        private const float ReversionDownsideBoost = 1.0f;

        // 되돌림 즉발 방지: 데드밴드를 갓 벗어난 직후 이 틱 수만큼은 회귀 램프를 미룬다(eff_away = away − 이값).
        // 뉴스로 확 빠진 주식이 '다음날 바로' 튀어오르지 않게 해서 1일 스캘핑을 약화(방향 예측을 어렵게).
        private const int ReversionDelayTicks = 2;

        // 회귀의 '한 틱' 이동 상한. 기준가에서 아무리 멀어도 회귀는 한 번에 이만큼까지만 되돌린다
        // → 최대값으로 팍 당기지 않고 '적당한 값을 여러 틱에 걸쳐' 올린다(요청). 깊은 이탈일수록 틱수만 늘어남.
        private const float ReversionMaxStepPerTick = 0.05f;

        // ── 떨어지는 칼날(Capitulation) ─────────────────────────────────────────
        // "제일 많이 내린 주식 사서 다음날 팔면 무조건 먹는다"를 깨기 위한 변칙.
        // '자기 변동성 대비 비정상적으로 급락 중'인 종목(=뉴스 크래시)만, 기준가 아래에 있을 때
        // 되돌리는 대신 '더 빠질' 확률을 준다. 정규화 게이트(자기 σ 대비)라 고변동주의 평범한
        // 노이즈 하락엔 안 걸려 시장 전체를 침하시키지 않는다(장기 평균회귀·기준가 앵커 불변).
        // 효과: 낙폭 과대주 매수가 '확정 반등'에서 '고위험 도박'(때로 반등 +, 때로 continuation −)이 된다.
        // ⚠ 반드시 자기 자신을 제한해야 한다(안 그러면 시장이 바닥으로 침몰). 두 가지 안전장치:
        //   (1) 1틱 게이트: '직전 1틱' 급락만 본다(3틱 lookback은 큰 크래시 1번이 이후 3틱 연속 재발동하는
        //       '에코 래칫'을 만들어 바닥까지 민다 — 2026-08-25 회귀 버그).
        //   (2) 깊이 상한(MaxDepth): 이미 충분히 빠진(−이만큼) 종목엔 발동 안 함 → 그 아래는 회귀가
        //       맡아 되돌린다. 지속 하락 뉴스가 와도 capitulation이 바닥까지 못 민다.
        // 정규화 게이트(자기 σ 대비)라 고변동주의 평범한 노이즈 하락엔 안 걸린다. 배경: economy/stock-falling-knife.md
        private const float CapitulationSigmaK = 1.6f;         // 정상 1틱 변동의 이 배수 넘게 빠진 '그 틱'만 크래시로 판정
        private const float CapitulationMinDepth = 0.10f;      // 기준가 대비 최소 이만큼(로그) 아래여야 후보(줍줍 구간)
        private const float CapitulationMaxDepth = 0.45f;      // 이보다 깊으면 발동 정지(아래는 회귀가 복구) — 바닥 침몰 방지
        private const float CapitulationContChance = 0.4f;     // 크래시 종목의 continuation(추가 하락) 발동 확률
        private const float CapitulationContMin = 0.06f;       // continuation 크기 하한
        private const float CapitulationContMax = 0.22f;       // continuation 크기 상한
        private const float CapitulationSevereChance = 0.02f;  // 드문 상폐급 대급락 확률
        private const float CapitulationSevere = 0.5f;         // 그 대급락 크기(×[0.6,1.0])

        // 실시간 15분·지하복귀·수면마다 1틱 도는 템포 → 게임적으로 변동성을 크게 잡는다.
        // 평상시 흔들림은 균등난수가 아니라 '정규분포(가우시안) 로그워크'다:
        // 대부분 틱은 중간 폭, 가끔 크게 → 실제 주가처럼 자연스러운 종 모양 분포.
        //   tickSigma = Volatility × NoiseSigmaFactor
        //   기대 |틱변동| ≈ 0.8 × tickSigma  (E|N(0,σ)| = σ√(2/π))
        // 종목 volatility 평균 0.29 → 평균 틱변동 ≈ 13%(저변동주 ~6%, 고변동주 ~21%→상한에 걸림).
        //   (0.58 = 0.135 / (0.8 × 0.29). 예전 0.72는 평균 16.5%, 0.86은 20%였다.)
        // ⚠ 2026-08-27: 0.58→0.40. 평균 틱변동 13%→8%. 25틱 화면 안 고/저 배율이 2.3배→1.7배로 줄어
        //    "저점은 너무 저점, 고점은 너무 고점"(=아무 데서나 사서 아무 데서나 팔아도 벌림)을 완화한다.
        private const float NoiseSigmaFactor = 0.40f;
        // 평상시 흔들림(노이즈+심리+관성+회귀)의 소프트 안전 상한 = min(tickSigma×이값, AmbientMaxMove).
        private const float AmbientCapSigma = 2.5f;
        // 기본(뉴스 없는) 틱변동의 '절대' 상한. 요청 사양 = 기본 ±10~20% → 어떤 종목도 노이즈만으론
        // 한 틱에 ±20%를 못 넘는다(예전엔 고변동주가 dailySigma×2.5로 ±80%까지 튀었다 = "80% 상승"의 정체).
        private const float AmbientMaxMove = 0.13f;
        // 뉴스는 평상시 상한과 별개의 더 큰 자기 상한을 가진다(뉴스 틱 스윙). 0.7→0.35(총 40% 안에서 여유).
        private const float NewsTickCap = 0.24f;
        // 한 틱 총변동(노이즈+뉴스+hype+점프+칼날 합)의 '하드' 절대 상한. 요청 = 어떤 틱도 최대 ±40%.
        // hype·희귀점프까지 포함해 한 틱에 ±40%를 못 넘게 최종 클램프한다.
        private const float MaxTickChange = 0.28f;

        // 뉴스 신호 배율. 뉴스의 주가 반영을 전역으로 키운다(체감상 "뉴스 읽는 보람").
        // 1.0=원본. 일반 뉴스는 이 배율을 곱한 뒤 뉴스 전용 상한(NewsTickCap=0.7)에 갇힌다.
        // 급등(HypeBypass>0) 뉴스만 상한을 우회해 여러 틱 누적된다.
        // 3.0 = "뉴스 영향 ~30~40%" 튜닝(요청 사양 뉴스 ±30~50%에 맞춤, 예전 4.5는 40~70%였다):
        // 태그가 맞는 공식(Official) 헤드라인 뉴스의 주가 영향이 ~0.30~0.45(강한 건은 0.40 상한에 붙음).
        //   예) priceEffect 0.12 × impact 1.0 × 3.0 = 0.36, priceEffect 0.15 × 1.0 × 3.0 = 0.45(→0.40 클램프)
        //   (루머·선반영·'flat' 결과는 설계상 더 작게 남는다.) 여기에 기본 노이즈(≤0.20)가 더해져도
        //   최종 총변동 하드캡(MaxTickChange=0.50)에서 잘리므로 뉴스 틱도 ±50%를 안 넘는다.
        // 근거·페르소나 시뮬: Assets/Docs/economy/stock-vs-coin-role-and-tuning.md
        private const float NewsEffectMultiplier = 2.2f;

        // 희귀 대형 점프(jump-diffusion). 최종 총변동 상한(MaxTickChange=0.50) 안에 들도록 범위를 낮췄다
        // (예전 +100%/−60%는 이제 ±50% 하드캡에 잘려 항상 벽에 붙었을 것).
        private const float JumpChance = 0.030f;   // 종목별·틱별 점프 발생 확률
        private const float JumpUpMin = 0.10f;     // 상승 점프 최소 +10%
        private const float JumpUpMax = 0.24f;     // 상승 점프 최대 +24% (잭팟, 매우 드묾; 총 28% 하드캡 안)
        private const float JumpDownMin = 0.09f;   // 하락 점프 최소 -9%
        private const float JumpDownMax = 0.22f;   // 하락 점프 최대 -22%
        private const float JumpSkew = 2.5f;       // 클수록 작은 점프가 흔하고 극단 점프는 드물어짐

        // 예열(WarmUp) 중에는 희귀 점프를 끈다 — 플레이어가 아무것도 안 했는데
        // 시작 보드에 잭팟 종목이 뜨는 걸 막고 '평범한 시작 시세'를 만들기 위해서.
        private bool _suppressJumps;

        /// <summary>
        /// 희귀 대형 점프 억제 여부. WarmUp이 내부에서 켰다 끄지만,
        /// StockGameManager가 '뉴스 태운 예열 틱'을 직접 돌릴 때도 잭팟을 막으려고 켠다.
        /// </summary>
        public bool SuppressJumps { get => _suppressJumps; set => _suppressJumps = value; }

        // 예열용 빈 뉴스 리스트 (매 틱 새 리스트 할당 방지)
        private static readonly List<ActiveNews> s_emptyNews = new List<ActiveNews>();

        public event Action<string, int, int> OnPriceChanged; // companyId, oldPrice, newPrice

        public void Initialize(CompanyManager companyManager, TagMatchingSystem tagMatcher)
        {
            _companyManager = companyManager;
            _tagMatcher = tagMatcher;
        }

        /// <summary>
        /// 새 게임 시작 시, 뉴스 없이 가격 시뮬레이션만 N틱 미리 굴려
        /// 각 종목의 PriceHistory를 채운다. 시작하자마자 주식창을 열어도
        /// 1틱짜리 밋밋한 그래프가 아니라 '이미 진행 중인' 살아있는 차트가 보인다.
        /// CurrentTick·뉴스는 건드리지 않는다(게임적으로는 방금 시작 = 뉴스 fresh).
        /// </summary>
        public void WarmUp(int ticks)
        {
            if (ticks <= 0 || _companyManager == null) return;

            _suppressJumps = true;
            for (int i = 0; i < ticks; i++)
                ProcessTick(0, s_emptyNews, 0.5f); // 중립 심리·무뉴스 → 순수 노이즈+회귀 워크
            _suppressJumps = false;
        }

        // 가우시안(표준정규) 난수 — Box-Muller.
        private static float NextGaussian()
        {
            float u1 = 1f - UnityEngine.Random.value; // (0,1] 로 log(0) 방지
            float u2 = UnityEngine.Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        public void ProcessTick(int currentTick, List<ActiveNews> activeNews, float globalSentiment)
        {
            var companies = _companyManager.GetAllCompanies();
            foreach (var company in companies)
            {
                // 이 종목의 일일 표준편차와 평상시 흔들림의 소프트 상한.
                // 상한 = min(σ 비례 상한, 절대 상한 0.20) → 기본 틱변동이 ±20%를 못 넘는다.
                float dailySigma = Mathf.Max(0.01f, company.Volatility * NoiseSigmaFactor);
                float ambientCap = Mathf.Min(dailySigma * AmbientCapSigma, AmbientMaxMove);

                // 평상시 노이즈 = 정규분포(가우시안). 대부분 중간, 가끔 큰 폭.
                float baseNoise = NextGaussian() * dailySigma;
                // 변동성 드래그 보정(Itô / 로그정규 drift correction).
                // 가격은 곱셈적이다(newPrice = old × (1 + change)). 그래서 평균 0인 '대칭' 노이즈라도
                // 로그공간 기대수익은 −½σ² 만큼 '음(−)'이다(Jensen 부등식: log는 오목).
                // 이 −½σ²가 매 틱 전 종목에 보이지 않는 하락 압력으로 쌓여 두 가지 버그를 만든다:
                //  (1) 시장 전체가 진행할수록 기준가 아래로 서서히 가라앉는다("전부 하락").
                //  (2) 데드밴드(±35%) 안의 약한 회귀력(ReversionBase=0.08 → 밴드 가장자리 +0.034/틱)을
                //      드래그(고변동주는 −0.05~−0.08/틱)가 상쇄·역전해서, vol≳0.3 종목은 한 번 기준가
                //      아래로 밀리면 기준가로 '되돌아오지 못하고' 바닥까지 침몰한다("안 돌아옴").
                // +½σ²를 더해 노이즈의 로그 기대수익을 0으로 만들면 순수 노이즈는 기준가 둘레를
                // 대칭으로 랜덤워크하고, 회귀력이 온전히 살아 기준가가 진짜 앵커가 된다.
                // ⚠ 결정론적 감쇠(노이즈 OFF) 시뮬에선 드래그가 없어 이 버그가 안 보였다 → 설계 문서의
                //    "3~4틱 복귀" 측정이 라이브(노이즈 ON)와 어긋났던 원인. 배경: economy/stock-volatility-drag.md
                float dragCompensation = 0.5f * dailySigma * dailySigma;
                // 뉴스 영향을 '일반'과 '급등(상한 우회)'으로 분리해서 받는다.
                CalcNewsEffect(company, activeNews, out float newsEffect, out float hypeEffect);
                float sentimentEffect = CalcSentimentEffect(globalSentiment);
                float momentumEffect = CalcMomentumEffect(company);
                float reversionEffect = CalcMeanReversion(company);

                // 평상시 흔들림(노이즈+드래그보정+심리+관성+회귀)만 자기 상한(ambientCap) 안에서 합친다.
                float ambient = Mathf.Clamp(
                    baseNoise + dragCompensation + sentimentEffect + momentumEffect + reversionEffect,
                    -ambientCap, ambientCap);

                // 일반 뉴스는 평상시 상한과 별개의 더 큰 상한(NewsTickCap)으로 얹는다.
                float news = Mathf.Clamp(newsEffect, -NewsTickCap, NewsTickCap);

                float totalChange = ambient + news;

                // 급등(테마주) 뉴스는 상한을 무시하고 추가 → 여러 틱 누적되면 몇 배까지 오른다.
                totalChange += hypeEffect;

                // 희귀 대형 점프는 상한을 무시하고 추가 (최종 가격은 아래 basePrice*0.1~10 한계로만 제한)
                totalChange += CalcRareJump();

                // 떨어지는 칼날: 비정상 급락 중인 낙폭주는 되돌림 대신 더 빠질 수 있다(상한 무시).
                totalChange += CalcCapitulation(company);

                // 최종 하드 상한: hype·희귀점프·칼날까지 전부 합친 한 틱 총변동을 ±MaxTickChange로 자른다.
                // → 뉴스·급등이 겹쳐도 한 틱에 ±50%를 넘지 않는다(hype는 여러 틱에 걸쳐 누적은 계속 가능).
                totalChange = Mathf.Clamp(totalChange, -MaxTickChange, MaxTickChange);

                int oldPrice = company.CurrentPrice;
                int newPrice = Mathf.RoundToInt(oldPrice * (1f + totalChange));

                // 가격 밴드는 '비대칭'이다: 하한 basePrice×0.4, 상한 basePrice×10.
                // 상한(×10)은 hype(급등) 연출이 쓰는 값이라 그대로 두고(=최대 +900% 폭등 가능),
                // ⚠ 하한만 0.1→0.4로 올렸다(2026-08-25). 이유: 하한(×0.1)에서 산 주식이 기준가로만
                // 돌아와도 +900% 잭팟이 되는 '바닥 매수 익스플로잇'을 막기 위해서다(평단 215→2339 사례).
                // 하한 0.4면 바닥에서 기준가 복귀 이득이 최대 +150%로 잘리고, 크래시도 −60%까지만 → 시장이
                // 기준가의 40% 밑으로 침하하지 않는다. 배경: economy/stock-floor-buy-exploit.md
                int minPrice = Mathf.Max(1, Mathf.RoundToInt(company.BasePrice * 0.4f));
                int maxPrice = Mathf.RoundToInt(company.BasePrice * 10.0f);
                newPrice = Mathf.Clamp(newPrice, minPrice, maxPrice);

                company.CurrentPrice = newPrice;
                company.PriceHistory.Add(newPrice);
                if (company.PriceHistory.Count > MaxPriceHistory)
                    company.PriceHistory.RemoveRange(0, company.PriceHistory.Count - MaxPriceHistory);

                OnPriceChanged?.Invoke(company.Id, oldPrice, newPrice);
            }
        }

        /// <summary>
        /// 희귀 대형 점프. 대부분 0을 반환하고, 낮은 확률로 큰 폭(+30~100% / -25~60%)을 반환한다.
        /// 작은 점프가 흔하고 극단(예: +100%)은 매우 드물도록 지수 스큐를 적용한다.
        /// </summary>
        private float CalcRareJump()
        {
            if (_suppressJumps) return 0f;
            if (UnityEngine.Random.value >= JumpChance) return 0f;

            // 0~1 난수를 거듭제곱해 작은 값 쪽으로 치우치게 만든다.
            float t = Mathf.Pow(UnityEngine.Random.value, JumpSkew);
            bool up = UnityEngine.Random.value < 0.5f;
            return up
                ? Mathf.Lerp(JumpUpMin, JumpUpMax, t)
                : -Mathf.Lerp(JumpDownMin, JumpDownMax, t);
        }

        /// <summary>
        /// 떨어지는 칼날. 기준가 아래에서 '자기 변동성 대비 비정상적으로 급락 중'인 종목만
        /// 낮은 확률로 되돌림 대신 추가 하락(continuation) 혹은 드문 상폐급 대급락을 준다.
        /// 정규화 게이트(최근 3틱 수익률 < −K·σ₃)라 고변동주의 평범한 노이즈 하락엔 안 걸려
        /// 시장 전체를 침하시키지 않는다. 대부분 0을 반환한다. 예열(WarmUp) 중엔 끈다.
        /// </summary>
        private float CalcCapitulation(CompanyData company)
        {
            if (_suppressJumps) return 0f; // 예열 중엔 조용히(평범한 시작 시세)
            if (company.BasePrice <= 0 || company.CurrentPrice <= 0) return 0f;

            // 1) 기준가 대비 '줍줍 구간'(MinDepth~MaxDepth)일 때만 후보.
            //    너무 깊으면(MaxDepth 초과) 발동 정지 → 그 아래는 회귀가 복구(바닥 침몰 방지).
            float depth = -Mathf.Log((float)company.CurrentPrice / company.BasePrice);
            if (depth < CapitulationMinDepth || depth > CapitulationMaxDepth) return 0f;

            // 2) '직전 1틱'이 자기 변동성 대비 비정상적으로 급락한 그 틱만(=뉴스 크래시 순간).
            //    1틱만 보므로 큰 크래시가 이후 여러 틱 연속 재발동하는 '에코 래칫'이 없다.
            var hist = company.PriceHistory;
            if (hist == null || hist.Count < 2) return 0f;
            float prev = hist[hist.Count - 2];
            if (prev <= 0) return 0f;
            float mom1 = ((float)company.CurrentPrice - prev) / prev;

            float sigma1 = Mathf.Max(0.01f, company.Volatility * NoiseSigmaFactor);
            if (mom1 >= -CapitulationSigmaK * sigma1) return 0f;

            // 3) 크래시존 — 되돌리는 대신 더 빠진다
            if (UnityEngine.Random.value < CapitulationSevereChance)
                return -CapitulationSevere * UnityEngine.Random.Range(0.6f, 1.0f); // 드문 상폐급
            if (UnityEngine.Random.value < CapitulationContChance)
                return -UnityEngine.Random.Range(CapitulationContMin, CapitulationContMax); // continuation
            return 0f;
        }

        /// <summary>
        /// 활성 뉴스의 주가 영향을 계산한다. 두 갈래로 나눠 돌려준다.
        ///  - normalEffect: 일반 뉴스(HypeBypass==0). 여러 건은 1순위 100% + 나머지 로그감쇄로 합치고,
        ///    호출부에서 뉴스 전용 상한(NewsTickCap)에 갇힌다(평상시 상한과 별개, 더 큼).
        ///  - hypeEffect: 급등(테마주) 뉴스(HypeBypass>0). 상한을 우회하므로 여러 틱 누적되면 몇 배까지 간다.
        /// 두 값 모두 태그 매칭 점수와 전역 NewsEffectMultiplier가 곱해져 있다.
        /// </summary>
        private void CalcNewsEffect(CompanyData company, List<ActiveNews> activeNews,
            out float normalEffect, out float hypeEffect)
        {
            normalEffect = 0f;
            hypeEffect = 0f;
            if (activeNews == null || activeNews.Count == 0) return;

            List<float> effects = new List<float>();
            foreach (var news in activeNews)
            {
                float matchScore = _tagMatcher.CalculateTagMatchScore(news.Template.Tags, company.Tags);
                if (matchScore <= 0f) continue;

                float contribution = news.GetEffectiveImpact() * matchScore * NewsEffectMultiplier;

                if (news.Template.HypeBypass > 0f)
                    hypeEffect += contribution; // 상한 우회 — 동시 급등은 드물어 단순 합산
                else
                    effects.Add(contribution);
            }

            if (effects.Count == 0) return;

            // 절대값을 기준으로 영향도가 가장 큰 뉴스 순으로 내림차순 정렬
            effects.Sort((a, b) => Mathf.Abs(b).CompareTo(Mathf.Abs(a)));

            float primaryEffect = effects[0];
            if (effects.Count == 1)
            {
                normalEffect = primaryEffect;
                return;
            }

            // 다중 뉴스 중첩(Overlap) 통합 처리:
            // 영향도가 가장 큰 1순위 뉴스는 100% 반영하고, 나머지 뉴스들의 합은 로그 감쇄(Log Decay) 처리
            float secondarySum = 0f;
            for (int i = 1; i < effects.Count; i++)
            {
                secondarySum += effects[i];
            }

            float decayScale = 0.5f; // 감쇄 정도 조절 변수
            float logDecaySecondary = Mathf.Sign(secondarySum) * Mathf.Log(1f + Mathf.Abs(secondarySum)) * decayScale;

            normalEffect = primaryEffect + logDecaySecondary;
        }

        private float CalcSentimentEffect(float globalSentiment)
        {
            // 시장 전체 공포/탐욕 분위기 영향: neutral(0.5f) 기준 편차에 1% 스케일 적용
            return (globalSentiment - 0.5f) * 0.01f;
        }

        private float CalcMomentumEffect(CompanyData company)
        {
            int historyCount = company.PriceHistory.Count;
            if (historyCount < 2) return 0f;

            // 최대 최근 3틱 동안의 주가 변화율의 평균치 계산
            int steps = Mathf.Min(3, historyCount - 1);
            float sumOfChanges = 0f;
            for (int i = 0; i < steps; i++)
            {
                int index = historyCount - 1 - i;
                float oldPrice = company.PriceHistory[index - 1];
                float newPrice = company.PriceHistory[index];
                if (oldPrice > 0)
                {
                    sumOfChanges += (newPrice - oldPrice) / oldPrice;
                }
            }

            float avgChange = sumOfChanges / steps;

            // 추세 관성 반영 (약화 + 1틱 기여 캡으로 양의 피드백 폭주 방지)
            return Mathf.Clamp(avgChange * MomentumFactor, -MomentumCap, MomentumCap);
        }

        private float CalcMeanReversion(CompanyData company)
        {
            if (company.BasePrice <= 0 || company.CurrentPrice <= 0) return 0f;

            // 기준가 대비 로그 비율(배수 공간에서 대칭: 2배↑와 0.5배↓가 같은 크기)
            float logRatio = Mathf.Log((float)company.CurrentPrice / company.BasePrice);

            // 데드밴드 밖에 '연속으로' 머문 틱 수만큼 회귀 강도를 램프시킨다.
            // 단, 갓 벗어난 직후 ReversionDelayTicks틱은 램프를 미뤄(eff_away) '즉발 반등'을 막는다.
            int ticksAway = CountConsecutiveTicksAway(company);
            int effAway = Mathf.Max(0, ticksAway - ReversionDelayTicks);
            float strength = Mathf.Min(ReversionMax, ReversionBase + ReversionRampPerTick * effAway);

            // 기준가 아래(logRatio<0)면 하락 대칭 부스트로 강도(및 상한)를 키워 +X%와 복귀 틱을 맞춘다.
            if (logRatio < 0f)
                strength = Mathf.Min(ReversionMax * ReversionDownsideBoost, strength * ReversionDownsideBoost);

            // 한 틱 되돌림은 최대 ReversionMaxStepPerTick까지만 → 팍 당기지 않고 여러 틱에 걸쳐 완만히 복귀.
            return Mathf.Clamp(-logRatio * strength, -ReversionMaxStepPerTick, ReversionMaxStepPerTick);
        }

        /// <summary>
        /// PriceHistory의 뒤에서부터, 기준가 데드밴드(±ReversionDeadbandPct) 밖에 머문 '연속' 틱 수를 센다.
        /// 데드밴드 안으로 한 번 들어오면 0으로 리셋된다. 별도 상태 저장이 필요 없다(히스토리에서 파생).
        /// 반환값은 '지금까지' 벗어나 있던 틱 수 → 이번 틱의 회귀 강도를 결정한다.
        /// </summary>
        private int CountConsecutiveTicksAway(CompanyData company)
        {
            var hist = company.PriceHistory;
            if (hist == null || hist.Count == 0) return 0;

            float deadband = Mathf.Log(1f + ReversionDeadbandPct);
            int count = 0;
            for (int i = hist.Count - 1; i >= 0; i--)
            {
                int p = hist[i];
                if (p <= 0) break;
                if (Mathf.Abs(Mathf.Log((float)p / company.BasePrice)) > deadband)
                    count++;
                else
                    break;
            }
            return count;
        }
    }
}
