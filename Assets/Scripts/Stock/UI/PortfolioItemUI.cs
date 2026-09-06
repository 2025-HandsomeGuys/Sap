using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Stock.Data;
using Stock.Core;
using Stock.Systems;

namespace Stock.UI
{
    /// <summary>
    /// 포트폴리오 탭의 단일 보유 종목 행 프리팹. UI 배치는 프리팹에서 하고, 이 스크립트는 값만 채운다.
    ///
    /// 기본 필드(name/shares/avg/current/profit)는 필수, A·B·E용 필드는 전부 선택(연결 안 하면 무시).
    ///  A) 손익 강조 — profitText/profitAmountText/accentBar를 손익 색으로.
    ///  B) 카드/배지 — accentBar(좌측 손익색 바), badgeText(종목 이니셜).
    ///  E) 인내심 게이지 — patienceFill(강제매각까지 남은 비율) + patienceText.
    /// </summary>
    public class PortfolioItemUI : MonoBehaviour
    {
        [Header("정보")]
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI sharesText;
        [SerializeField] private TextMeshProUGUI avgPriceText;
        [SerializeField] private TextMeshProUGUI currentPriceText;
        [Tooltip("수익률(%). ▲/▼ + 손익색")]
        [SerializeField] private TextMeshProUGUI profitText;

        [Header("A) 손익 강조 (선택)")]
        [Tooltip("평가손익 금액(+₩342). 비우면 미표시")]
        [SerializeField] private TextMeshProUGUI profitAmountText;

        [Header("B) 카드/배지 (선택)")]
        [Tooltip("좌측 세로 액센트 바. 손익색으로 칠해진다")]
        [SerializeField] private Image accentBar;
        [Tooltip("종목 배지 안의 이니셜 텍스트(종목명 앞 2글자)")]
        [SerializeField] private TextMeshProUGUI badgeText;

        [Header("E) 인내심 게이지 (선택)")]
        [Tooltip("Image Type=Filled, Horizontal, Origin=Left 권장. 남은 여유만큼 채워진다")]
        [SerializeField] private Image patienceFill;
        [Tooltip("인내심 남은 일수 텍스트(예: 인내심 2일)")]
        [SerializeField] private TextMeshProUGUI patienceText;

        [Header("구 매도 버튼 (보유탭에서는 매매하지 않는다 — 코드가 숨긴다)")]
        [Tooltip("보유탭은 조회 전용이라 이 버튼은 런타임에 숨겨진다. 프리팹 연결을 깨지 않으려 필드만 남겨 둔다")]
        [SerializeField] private Button sellButton;
        [SerializeField] private TextMeshProUGUI sellButtonLabel;

        [Header("색상")]
        [SerializeField] private Color riseColor = new Color(0.247f, 0.725f, 0.314f); // #3FB950
        [SerializeField] private Color fallColor = new Color(0.973f, 0.318f, 0.286f); // #F85149
        [SerializeField] private Color flatColor = new Color(0.353f, 0.4f, 0.471f);   // #5A6678
        [Tooltip("인내심 게이지 중간(주의) 색")]
        [SerializeField] private Color patienceWarnColor = new Color(0.941f, 0.533f, 0.243f); // #F0883E

        [Header("슬롯 선택 (W/S 커서 — 비우면 코드 생성 테두리)")]
        [Tooltip("선택 시 켜지는 강조 이미지. 비우면 코드가 라운드 테두리를 자동 생성한다.")]
        [SerializeField] private Image selectionHighlight;

        private string _companyId;
        private Image _selRing;

        public string CompanyId => _companyId;

        private void Awake()
        {
            // 보유탭에서는 매수·매도를 하지 않는다 — 매도 버튼은 숨기고, 행 전체를 '시세로 이동' 버튼으로 만든다.
            if (sellButton) sellButton.gameObject.SetActive(false);
            EnsureRowButton();
        }

        /// <summary>행 전체를 클릭 가능하게 만든다(프리팹에 Button이 없으면 붙인다).
        /// 클릭하면 시세 탭으로 이동해 이 종목을 선택하고 '보유중' 필터를 켠다.
        ///
        /// ⚠ 루트 Graphic이 <b>꺼져 있거나 Raycast Target이 아니면</b> 행의 빈 공간은 클릭을 못 받는다
        /// (자식 텍스트 위만 눌리고 나머지는 안 눌리던 증상). 그래서 존재만 보지 않고 실제로 레이캐스트가
        /// 되는지까지 확인하고, 아니면 투명 판을 깐다. 프리팹 쪽에서도 루트 Image를 켜(알파 0) 맞춰 뒀다.</summary>
        private void EnsureRowButton()
        {
            var btn = GetComponent<Button>();
            if (btn == null)
            {
                var graphic = GetComponent<Graphic>();
                if (graphic != null && graphic.enabled && graphic.raycastTarget)
                {
                    btn = gameObject.AddComponent<Button>();
                    btn.targetGraphic = graphic;
                    btn.transition = Selectable.Transition.None;
                }
                else
                {
                    // 클릭 판정을 받으려면 '켜져 있고 레이캐스트를 받는' Graphic이 필요하다 — 투명 판을 깐다.
                    var hit = CodeUI.CreateImage(transform, "ClickArea", new Color(0f, 0f, 0f, 0f), rounded: false);
                    var hrt = hit.rectTransform;
                    hrt.anchorMin = Vector2.zero;
                    hrt.anchorMax = Vector2.one;
                    hrt.offsetMin = Vector2.zero;
                    hrt.offsetMax = Vector2.zero;
                    hit.raycastTarget = true;
                    hit.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    hit.transform.SetAsFirstSibling(); // 내용 아래에 깔아 자식 위젯을 가리지 않게
                    btn = hit.gameObject.AddComponent<Button>();
                    btn.targetGraphic = hit;
                    btn.transition = Selectable.Transition.None;
                }
            }
            btn.onClick.AddListener(OnRowClicked);
        }

        /// <summary>행 클릭 — 시세 탭에서 이 종목을 펼친다('보유중' 필터가 함께 켜진다).</summary>
        public void OnRowClicked()
        {
            if (string.IsNullOrEmpty(_companyId)) return;
            StockUIController.Instance?.ShowInStockTab(_companyId);
        }

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
            ring.gameObject.AddComponent<LayoutElement>().ignoreLayout = true; // 레이아웃에 끼어들지 않게
            ring.transform.SetAsLastSibling();
            ring.gameObject.SetActive(false);
            _selRing = ring;
            return ring;
        }

        public void Bind(OwnedStock stock)
        {
            if (stock == null) return;
            _companyId = stock.CompanyId;

            var company = StockGameManager.Instance?.CompanyManager?.GetCompany(stock.CompanyId);
            string name = company != null ? company.GetLocalizedName() : stock.CompanyId;
            int current = company != null ? company.CurrentPrice : stock.AvgBuyPrice;

            if (nameText) nameText.text = name;
            if (sharesText) sharesText.text = StockLoc.LF("ui_stock_shares", "{0}주", stock.Shares);
            if (avgPriceText) avgPriceText.text = StockLoc.LF("ui_stock_avg", "평단 ₩{0:N0}", stock.AvgBuyPrice);
            if (currentPriceText) currentPriceText.text = StockLoc.LF("ui_stock_current", "현재 ₩{0:N0}", current);
            if (badgeText) badgeText.text = Initials(name);

            // ---- A) 손익 강조 ----
            float pct = stock.AvgBuyPrice > 0
                ? (float)(current - stock.AvgBuyPrice) / stock.AvgBuyPrice * 100f
                : 0f;
            long amount = (long)(current - stock.AvgBuyPrice) * stock.Shares;
            Color pnl = pct > 0f ? riseColor : (pct < 0f ? fallColor : flatColor);
            string arrow = pct > 0f ? "▲" : (pct < 0f ? "▼" : "–");

            if (profitText)
            {
                profitText.text = $"{arrow} {Mathf.Abs(pct):F1}%";
                profitText.color = pnl;
            }
            if (profitAmountText)
            {
                string sign = amount > 0 ? "+" : (amount < 0 ? "−" : "");
                profitAmountText.text = $"{sign}₩{Mathf.Abs(amount):N0}";
                profitAmountText.color = pnl;
            }

            // ---- B) 좌측 액센트 바도 손익색 ----
            if (accentBar) accentBar.color = pnl;

            // ---- E) 인내심 게이지 ----
            UpdatePatience(stock);
        }

        /// <summary>인내심(보유 기간 제한) 제거 — 게이지·라벨을 숨긴다.</summary>
        private void UpdatePatience(OwnedStock stock)
        {
            if (patienceFill && patienceFill.gameObject.activeSelf) patienceFill.gameObject.SetActive(false);
            if (patienceText && patienceText.gameObject.activeSelf) patienceText.gameObject.SetActive(false);
        }

        private static string Initials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            name = name.Trim();
            return name.Substring(0, Mathf.Min(2, name.Length));
        }
    }
}
