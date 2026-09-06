using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Stock.Data;
using Market;

namespace Stock.UI
{
    /// <summary>뉴스 탭의 단일 뉴스 행 프리팹. 활성 뉴스/히스토리 양쪽을 표시할 수 있다.</summary>
    public class NewsItemUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI phaseText;   // [찌라시]/[공식]/[소멸]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI infoText;    // 감정 · 남은틱 (tagChips 연결 시 태그 제외)
        [Tooltip("태그를 알약 칩으로 표시. 연결하면 infoText에서 태그가 빠진다")]
        [SerializeField] private TagChipList tagChips;
        [SerializeField] private Image phaseBackground;       // optional

        [Header("페이즈 색상")]
        [SerializeField] private Color rumorColor = new Color(0.91f, 0.62f, 0.15f);
        [SerializeField] private Color officialColor = new Color(0.23f, 0.6f, 0.13f);
        [SerializeField] private Color fadeColor = new Color(0.5f, 0.5f, 0.5f);

        [Header("슬롯 선택 (W/S 커서 — 비우면 코드 생성 테두리)")]
        [Tooltip("선택 시 켜지는 강조 이미지. 비우면 코드가 라운드 테두리를 자동 생성한다.")]
        [SerializeField] private Image selectionHighlight;

        private Image _selRing;

        /// <summary>W/S 슬롯 커서가 이 행에 있는지 — 강조 테두리를 켜고 끈다(시세탭 선택과 같은 감각).</summary>
        public void SetSelected(bool on)
        {
            var ring = selectionHighlight != null ? selectionHighlight : EnsureSelectionRing();
            if (ring != null) ring.gameObject.SetActive(on);
        }

        /// <summary>인스펙터 강조 이미지가 없을 때 코드로 라운드 테두리를 한 번 만들어 재사용한다.</summary>
        private Image EnsureSelectionRing()
        {
            if (_selRing != null) return _selRing;

            var ring = CodeUI.CreateImage(transform, "SelectionRing", new Color(0.62f, 0.89f, 0.96f, 1f), rounded: false);
            ring.sprite = CodeUI.Outline();
            ring.type = Image.Type.Sliced;
            ring.raycastTarget = false;
            var rt = ring.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-2f, -2f);
            rt.offsetMax = new Vector2(2f, 2f);
            ring.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            ring.transform.SetAsLastSibling();
            ring.gameObject.SetActive(false);
            _selRing = ring;
            return ring;
        }

        public void BindActive(ActiveNews news)
        {
            if (news == null) return;
            if (titleText) titleText.text = news.GetSynthesizedTitle();

            string phaseLabel = GetPhaseLabel(news.CurrentPhase);
            Color phaseColor = GetPhaseColor(news.CurrentPhase);
            if (phaseText) { phaseText.text = $"[{phaseLabel}]"; phaseText.color = phaseColor; }
            if (phaseBackground) phaseBackground.color = new Color(phaseColor.r, phaseColor.g, phaseColor.b, 0.18f);

            string sentiment = GetSentimentLabelColored(news.Template.Sentiment);
            if (infoText) infoText.richText = true; // 감정 색상 리치텍스트가 리터럴로 새지 않게
            if (tagChips)
            {
                tagChips.SetTags(news.Template.Tags);
                if (infoText) infoText.text = StockLoc.LF("ui_stock_news_active_info_notag", "{0} · 남은 {1}틱", sentiment, news.RemainingTicks);
            }
            else
            {
                string tags = (news.Template.Tags != null) ? string.Join(", ", System.Array.ConvertAll(news.Template.Tags, StockLoc.Tag)) : "";
                if (infoText) infoText.text = StockLoc.LF("ui_stock_news_active_info", "{0} · {1} · 남은 {2}틱", tags, sentiment, news.RemainingTicks);
            }
        }

        public void BindHistory(NewsHistoryEntry entry)
        {
            if (entry == null) return;
            if (titleText) titleText.text = entry.SynthesizedTitle;
            if (tagChips) tagChips.SetTags(null); // 히스토리엔 태그 없음 — 풀 재사용 시 이전 칩 제거

            string label = GetPhaseLabel(NewsPhase.Fade);
            Color phaseColor = fadeColor;
            if (phaseText) { phaseText.text = $"[{label}]"; phaseText.color = phaseColor; }
            if (phaseBackground) phaseBackground.color = new Color(phaseColor.r, phaseColor.g, phaseColor.b, 0.18f);
            if (infoText) infoText.text = StockLoc.LF("ui_stock_news_history_info", "발생 {0}틱 · {1}", entry.OccurredTick, entry.OutcomeLabel);
        }

        public static string GetPhaseLabel(NewsPhase phase) => phase switch
        {
            NewsPhase.Rumor => StockLoc.L("ui_stock_phase_rumor", "찌라시"),
            NewsPhase.Official => StockLoc.L("ui_stock_phase_official", "공식"),
            NewsPhase.Fade => StockLoc.L("ui_stock_phase_fade", "소멸"),
            _ => ""
        };

        public static string GetSentimentLabel(NewsSentiment s) => s switch
        {
            NewsSentiment.Positive => StockLoc.L("ui_stock_sentiment_positive", "긍정적"),
            NewsSentiment.Negative => StockLoc.L("ui_stock_sentiment_negative", "부정적"),
            _ => StockLoc.L("ui_stock_sentiment_neutral_news", "중립")
        };

        /// <summary>긍정=상승 그린 / 부정=하락 레드 / 중립=뮤트. TMP 리치텍스트로 감정 단어에만 색을 입힌다.</summary>
        public static Color GetSentimentColor(NewsSentiment s) => s switch
        {
            NewsSentiment.Positive => MarketTheme.Up,   // #3FB950
            NewsSentiment.Negative => MarketTheme.Down, // #F85149
            _ => MarketTheme.TextMuted
        };

        /// <summary>감정 라벨을 색상 리치텍스트로 감싼다(infoText는 리치텍스트 활성 상태로 표시됨).</summary>
        public static string GetSentimentLabelColored(NewsSentiment s)
        {
            string label = GetSentimentLabel(s);
            return $"<color=#{ColorUtility.ToHtmlStringRGB(GetSentimentColor(s))}>{label}</color>";
        }

        private Color GetPhaseColor(NewsPhase phase) => phase switch
        {
            NewsPhase.Rumor => rumorColor,
            NewsPhase.Official => officialColor,
            _ => fadeColor
        };
    }
}
