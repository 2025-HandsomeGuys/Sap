using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using Stock.Data;

namespace Stock.UI
{
    /// <summary>주식 리스트의 단일 종목 행 프리팹.</summary>
    public class StockListItemUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [Tooltip("종목 태그 줄(구 섹터 자리). 태그를 구분자로 이어 표시한다.")]
        [FormerlySerializedAs("sectorText")]                 // 구 필드명 — 프리팹 연결 유지
        [SerializeField] private TextMeshProUGUI tagText;
        [Tooltip("한 줄에 표시할 최대 태그 수(0 이하면 전부)")]
        [SerializeField] private int maxTagsShown = 3;
        [Tooltip("태그 사이 구분자")]
        [SerializeField] private string tagSeparator = " · ";
        [SerializeField] private TextMeshProUGUI priceText;
        [SerializeField] private TextMeshProUGUI changeText;
        [SerializeField] private Button button;
        [SerializeField] private Image background;
        [SerializeField] private Image leftAccent; // 선택 시 좌측 시안 액센트 바 (다크 레트로)
        [Tooltip("종목 아이콘. 비워두면 표시 안 함. 스프라이트는 Resources/StockIcons에서 id로 자동 로드된다.")]
        [SerializeField] private Image iconImage;   // 종목별 아이콘 (StockIcons 리졸버가 채움)

        [Header("색상 (다크 레트로 — MarketTheme §3.1)")]
        [SerializeField] private Color normalColor = new Color(0f, 0f, 0f, 0f);
        [SerializeField] private Color selectedColor = new Color(0.106f, 0.165f, 0.239f, 0.9f); // rowSelected #1B2A3D
        [SerializeField] private Color riseColor = new Color(0.247f, 0.725f, 0.314f);           // up #3FB950
        [SerializeField] private Color fallColor = new Color(0.973f, 0.318f, 0.286f);           // down #F85149
        [SerializeField] private Color flatColor = new Color(0.353f, 0.4f, 0.471f);             // textDim #5A6678

        private CompanyData _company;
        private Action<CompanyData> _onClick;

        public string CompanyId => _company?.Id;

        public void Bind(CompanyData company, Action<CompanyData> onClick)
        {
            _company = company;
            _onClick = onClick;
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => _onClick?.Invoke(_company));
            }
            Refresh();
        }

        public void Refresh()
        {
            if (_company == null) return;
            if (iconImage)
            {
                var icon = StockIcons.Get(_company.Id);
                iconImage.sprite = icon;
                iconImage.enabled = icon != null; // 아이콘 없으면 빈 사각형 대신 숨김
            }
            if (nameText) nameText.text = _company.GetLocalizedName();
            if (tagText) tagText.text = BuildTagLine(_company);
            if (priceText) priceText.text = StockLoc.LF("ui_stock_price", "₩{0:N0}", _company.CurrentPrice);

            float change = GetChangePercent(_company);
            if (changeText)
            {
                string sign = change > 0f ? "▲" : (change < 0f ? "▼" : "-");
                changeText.text = $"{sign} {Mathf.Abs(change):F1}%";
                changeText.color = change > 0f ? riseColor : (change < 0f ? fallColor : flatColor);
            }
        }

        public void SetSelected(bool selected)
        {
            if (background) background.color = selected ? selectedColor : normalColor;
            if (leftAccent) leftAccent.enabled = selected; // 좌측 액센트 바 토글
        }

        /// <summary>태그 줄 텍스트(지역화된 태그를 구분자로 연결). maxTagsShown개까지만 표시한다.</summary>
        private string BuildTagLine(CompanyData c)
        {
            if (c?.Tags == null || c.Tags.Length == 0) return string.Empty;
            int n = maxTagsShown > 0 ? Mathf.Min(maxTagsShown, c.Tags.Length) : c.Tags.Length;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(tagSeparator);
                sb.Append(StockLoc.Tag(c.Tags[i]));
            }
            return sb.ToString();
        }

        /// <summary>직전 틱 대비 등락률(%). 히스토리가 부족하면 0.</summary>
        public static float GetChangePercent(CompanyData c)
        {
            if (c == null || c.PriceHistory == null || c.PriceHistory.Count < 2) return 0f;
            int prev = c.PriceHistory[c.PriceHistory.Count - 2];
            if (prev <= 0) return 0f;
            return (float)(c.CurrentPrice - prev) / prev * 100f;
        }
    }
}
