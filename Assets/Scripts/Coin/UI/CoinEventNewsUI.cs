// @tags: coin, ui, news, breaking, event, hold
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Market;

namespace Coin.UI
{
    /// <summary>
    /// 떡상/떡락/청산 시 "영향을 준 뉴스"를 띄우는 1회성 패널.
    /// 하단의 흐르는 NewsTicker(상시 연출용)와는 **별개**다 — 이건 결정적 순간에만 뜬다.
    /// 그래프가 흔들리다 **잠깐 멈춘 순간**(NewsHold) 호출되어:
    /// 결과를 감춘 정적(복권 까기 직전) → 헤드라인 페이드+슬라이드 등장 → 아주 짧은 리드 후
    /// onDone(그래프 공개=크래시 리빌) 발동 → 크래시가 진행되는 동안 뉴스는 계속 떠 있다가 닫힌다.
    /// 뉴스와 그래프 이동이 사실상 동시에 터지게 설계. 스킵 없음(자동 진행).
    /// </summary>
    public class CoinEventNewsUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TextMeshProUGUI labelText;     // "📰 BREAKING"
        [SerializeField] private TextMeshProUGUI headlineText;  // 영향 뉴스 본문 (상승=녹색 / 하락=적색)
        [Tooltip("(구 스킵 힌트) 스킵 연출이 제거되어 항상 숨긴다 — 씬에 남아 있어도 무해")]
        [SerializeField] private TextMeshProUGUI hintText;
        [Tooltip("(구 스킵 버튼) 스킵 연출이 제거되어 항상 비활성 — 씬에 남아 있어도 무해")]
        [SerializeField] private Button skipButton;

        [Header("타이밍(초)")]
        [Tooltip("크래시 리빌이 시작된 뒤 뉴스가 자동으로 닫히기까지 유지 시간. 크래시가 진행되는 동안 계속 떠 있는다")]
        [SerializeField] private float holdSeconds = 4f;
        [SerializeField] private float fadeSeconds = 0.25f;

        [Header("긴장 연출")]
        [Tooltip("그래프가 멈춘 뒤 헤드라인이 뜨기까지의 정적(긴장) 시간 — 복권 까기 직전처럼 결과를 계속 숨긴다")]
        [SerializeField] private float suspenseDelay = 1.2f;
        [Tooltip("헤드라인 등장(페이드+슬라이드) 시간")]
        [SerializeField] private float headlineEnterSeconds = 0.15f;
        [Tooltip("헤드라인이 지나가듯 슬라이드해 들어오는 시작 X오프셋(px). +면 오른쪽에서 들어온다")]
        [SerializeField] private float headlineSlideFrom = 60f;
        [Tooltip("헤드라인 등장 후 그래프(크래시)가 움직이기까지의 아주 짧은 리드 — 뉴스와 그래프 이동을 사실상 동시에 터뜨린다")]
        [SerializeField] private float leadBeforeReveal = 0.15f;

        [Header("헤드라인 풀 (지역화 키 ui_coin_break_bull/bear_N 우선)")]
        [SerializeField] private string[] bullHeadlines =
        {
            "🚀 거물 인플루언서 '풀매수' 인증에 매수벽 폭발",
            "🐳 정체불명 고래 대량 매집 — 호가창이 비었다",
            "📈 대형 거래소 상장 확정 루머에 패닉 바잉",
            "🔥 커뮤니티 밈 떡상 — 신규 유입 폭증",
        };
        [SerializeField] private string[] bearHeadlines =
        {
            "💀 개발팀 잠적 — 공식 채널 전체 삭제",
            "🐳 초기 투자자 전량 덤핑 후 잠적",
            "📉 규제 당국 조사 착수 소식에 투매",
            "🧨 유동성 증발 — 출구가 막혔다",
        };

        private Action _onDone;
        private Coroutine _co;
        private Vector2 _headlineHome;   // 헤드라인 제자리(슬라이드 등장 복귀 지점)

        private void Awake()
        {
            if (headlineText) _headlineHome = headlineText.rectTransform.anchoredPosition;
            // 스킵 연출 제거 — 구 씬에 남은 힌트 텍스트·투명 스킵 버튼은 숨긴다(레이캐스트 차단 방지).
            if (hintText) hintText.gameObject.SetActive(false);
            if (skipButton) skipButton.gameObject.SetActive(false);
            if (group) { group.alpha = 0f; group.gameObject.SetActive(false); }
        }

        /// <summary>영향 뉴스를 띄운다. 정적 → 헤드라인 등장 → 짧은 리드 후 onDone(그래프 공개=크래시 리빌)을 호출하고,
        /// 뉴스는 크래시가 진행되는 동안 계속 떠 있다가 닫힌다. bullish=가격 상승 방향.</summary>
        public void Show(bool bullish, Action onDone)
        {
            _onDone = onDone;
            if (group == null) { onDone?.Invoke(); return; } // 패널 미연결 시 즉시 공개(흐름 안전)

            if (labelText) labelText.text = CoinLoc.L("ui_coin_break_label", "📰 BREAKING");
            if (headlineText)
            {
                headlineText.text = PickHeadline(bullish);
                headlineText.color = bullish ? MarketTheme.Up : MarketTheme.Down;
            }

            // Awake에서 group.gameObject(=이 컴포넌트가 붙은 GameObject)를 SetActive(false) 하므로,
            // 코루틴 호스트가 비활성 상태다. 먼저 켜지 않으면 StartCoroutine이 실패해 onDone(=차트 TriggerReveal)이
            // 영영 호출되지 않고, 차트가 뉴스 대기(NewsHold) 상태로 정지한다(안전망 시간까지).
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(Routine());
        }

        /// <summary>표시 중인 뉴스를 즉시 닫는다 — 다음 베팅 시작 등 새 라운드 정리용.
        /// (베팅 버튼은 리빌 완료 후에야 풀리므로, 이 시점의 onDone은 항상 이미 소비된 상태다.)</summary>
        public void HideImmediate()
        {
            if (_co != null) { StopCoroutine(_co); _co = null; }
            _onDone = null;
            if (group) { group.alpha = 0f; group.gameObject.SetActive(false); }
        }

        private string PickHeadline(bool bullish)
        {
            string[] pool = bullish ? bullHeadlines : bearHeadlines;
            if (pool == null || pool.Length == 0) return "";
            int idx = UnityEngine.Random.Range(0, pool.Length);
            string key = (bullish ? "ui_coin_break_bull_" : "ui_coin_break_bear_") + idx;
            return CoinLoc.L(key, pool[idx]);
        }

        private IEnumerator Routine()
        {
            group.alpha = 1f;
            group.gameObject.SetActive(true);
            SetHeadlineAlpha(0f);                   // 결과(헤드라인·라벨)는 숨긴다 — 복권 까기 직전
            if (headlineText) headlineText.rectTransform.anchoredPosition = _headlineHome;

            // 1) 긴장 정적 — 결과를 감춘 채(그래프 잠깐 멈춤) 뜸을 들인다. 복권 까기 직전의 침묵.
            float s = 0f;
            while (s < suspenseDelay)
            {
                s += Time.unscaledDeltaTime;
                yield return null;
            }

            // 2) 헤드라인 등장 — 페이드+슬라이드.
            yield return EnterHeadline();

            // 3) 아주 짧은 리드 후 그래프 공개(크래시 리빌) → 뉴스와 그래프 이동이 사실상 동시에.
            float lead = 0f;
            while (lead < leadBeforeReveal)
            {
                lead += Time.unscaledDeltaTime;
                yield return null;
            }
            var trigger = _onDone;
            _onDone = null;
            trigger?.Invoke();                      // chart.TriggerReveal — 지금 그래프가 움직인다(뉴스는 그대로 떠 있음)

            // 4) 크래시가 진행되는 동안 뉴스는 계속 떠서 읽힌다(안도/실망).
            float held = 0f;
            while (held < holdSeconds)
            {
                held += Time.unscaledDeltaTime;
                yield return null;
            }

            // 5) 퇴장
            yield return Fade(1f, 0f);
            group.gameObject.SetActive(false);
            _co = null;
        }

        // 헤드라인 등장: 결과(헤드라인·라벨)가 페이드인되며 헤드라인이 지나가듯 제자리로 슬라이드한다(EaseOut).
        private IEnumerator EnterHeadline()
        {
            RectTransform hrt = headlineText != null ? headlineText.rectTransform : null;
            float dur = Mathf.Max(0.01f, headlineEnterSeconds);
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = 1f - Mathf.Pow(1f - k, 3f);   // EaseOutCubic — 빠르게 들어와 부드럽게 정착
                SetHeadlineAlpha(e);
                if (hrt != null) hrt.anchoredPosition = _headlineHome + new Vector2(headlineSlideFrom * (1f - e), 0f);
                yield return null;
            }
            SetHeadlineAlpha(1f);
            if (hrt != null) hrt.anchoredPosition = _headlineHome;
        }

        // 결과 텍스트(헤드라인+라벨)의 알파를 함께 조절한다.
        private void SetHeadlineAlpha(float a)
        {
            if (headlineText) headlineText.alpha = a;
            if (labelText) labelText.alpha = a;
        }

        private IEnumerator Fade(float from, float to)
        {
            if (fadeSeconds <= 0f) { group.alpha = to; yield break; }
            float t = 0f;
            while (t < fadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, t / fadeSeconds);
                yield return null;
            }
            group.alpha = to;
        }
    }
}
