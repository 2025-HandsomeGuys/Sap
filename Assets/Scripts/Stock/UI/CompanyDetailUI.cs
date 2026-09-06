using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Stock.Data;
using Stock.Core;

namespace Stock.UI
{
    /// <summary>회사 상세 패널(우측). 정보·미니차트·최신뉴스·보유현황·매수/매도를 담당한다.</summary>
    public class CompanyDetailUI : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [Tooltip("(선택) 종목 아이콘. 비워두면 표시 안 함. Resources/StockIcons에서 id로 자동 로드된다.")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI marketCapText;
        [Tooltip("태그를 알약 칩으로 표시. 연결하면 tagsText 대신 사용된다")]
        [SerializeField] private TagChipList tagChips;
        [Tooltip("(선택) tagChips 미연결 시 폴백 — 쉼표 구분 한 줄 텍스트")]
        [SerializeField] private TextMeshProUGUI tagsText;
        [SerializeField] private TextMeshProUGUI priceText;
        [SerializeField] private TextMeshProUGUI changeText;
        [SerializeField] private MiniChartUI miniChart;

        [Header("거래 — 매수/매도 (수량은 팝업에서 정한다)")]
        [Tooltip("매수 버튼. 클릭하면 수량 지정 팝업(StockTradeDialog)이 뜬다")]
        [SerializeField] private Button buyButton;
        [Tooltip("매도 버튼. 클릭하면 수량 지정 팝업(StockTradeDialog)이 뜬다")]
        [SerializeField] private Button sellButton;

        [Header("구 인라인 수량 UI (연결돼 있으면 코드가 숨긴다)")]
        [Tooltip("수량은 팝업에서만 정하므로 더 이상 쓰지 않는다. 프리팹 연결을 깨지 않으려 필드만 남겨 두고 런타임에 숨긴다")]
        [SerializeField] private TMP_InputField quantityInput;
        [SerializeField] private TextMeshProUGUI orderAmountText;
        [SerializeField] private Button minusButton;
        [SerializeField] private Button plusButton;
        [SerializeField] private Button maxButton;

        [Header("색상 (다크 레트로 — MarketTheme §3.1)")]
        [SerializeField] private Color riseColor = new Color(0.247f, 0.725f, 0.314f); // up #3FB950
        [SerializeField] private Color fallColor = new Color(0.973f, 0.318f, 0.286f); // down #F85149
        [SerializeField] private Color flatColor = new Color(0.353f, 0.4f, 0.471f);   // textDim #5A6678

        [Header("스크롤 (세로) — 내용이 한 화면에 안 들어갈 때")]
        [Tooltip("상세 내용을 감싸는 ScrollRect. 새 종목 선택 시 맨 위로 리셋된다")]
        [SerializeField] private ScrollRect detailScroll;
        [Tooltip("VerticalLayoutGroup + ContentSizeFitter가 붙은 스크롤 콘텐츠")]
        [SerializeField] private RectTransform detailContent;

        private CompanyData _company;

        // 거래 버튼 라벨 — 비활성일 때 배경뿐 아니라 글자도 어둡게 만들기 위해 원래 색을 기억해둔다.
        private static readonly Color DisabledLabelDim = new Color(0.42f, 0.42f, 0.42f, 0.75f);
        private TextMeshProUGUI _buyLabel, _sellLabel;
        private Color _buyLabelColor, _sellLabelColor;
        private bool _buyLabelColorCached, _sellLabelColorCached;

        public CompanyData Current => _company;

        private void Awake()
        {
            if (buyButton) buyButton.onClick.AddListener(OnBuy);
            if (sellButton) sellButton.onClick.AddListener(OnSell);

            HideLegacyQuantityUI();
            BuildKbControls();
        }

        /// <summary>수량은 팝업(StockTradeDialog)에서만 정한다 — 프리팹에 남아 있는 인라인 수량 위젯을 숨긴다.
        /// 필드를 지우지 않는 이유: 씬·프리팹의 기존 연결을 깨지 않기 위해서다.</summary>
        private void HideLegacyQuantityUI()
        {
            if (quantityInput) quantityInput.gameObject.SetActive(false);
            if (orderAmountText) orderAmountText.gameObject.SetActive(false);
            if (minusButton) minusButton.gameObject.SetActive(false);
            if (plusButton) plusButton.gameObject.SetActive(false);
            if (maxButton) maxButton.gameObject.SetActive(false);
        }

        // ===================================================
        // 키보드(WASD) 조작 — 좌측 목록에서 D로 넘어와 −/+/매수/매도를 스페이스로 누른다.
        // ===================================================
        private readonly List<(Button btn, CodeNavButton nav)> _kbControls = new List<(Button, CodeNavButton)>();
        private int _kbIndex = -1;

        private void BuildKbControls()
        {
            _kbControls.Clear();
            // A/D 좌우 이동 대상은 '매수 → 매도'뿐이다(매수가 맨 왼쪽). 수량 조절은 팝업이 담당하므로
            // 여기엔 −/+/최대를 넣지 않는다 — 넣으면 매수에서 A를 눌렀을 때 목록으로 못 돌아간다.
            foreach (var b in new[] { buyButton, sellButton })
            {
                if (b == null) continue;
                var nav = CodeNavButton.Attach(b);   // 코드 생성 포커스 링(기본 스킨)
                if (nav == null) continue;
                nav.SetNavFocus(false);
                _kbControls.Add((b, nav));
            }
        }

        /// <summary>지금 상세 패널에 종목이 펼쳐져 있는지(키보드 진입 가능 여부).</summary>
        public bool HasContent => _company != null && (root == null || root.activeSelf);

        /// <summary>좌측 목록에서 D로 진입 — 매수 버튼에 포커스를 준다.</summary>
        public void EnterKeyboard()
        {
            if (_kbControls.Count == 0) return;
            int idx = _kbControls.FindIndex(c => c.btn == buyButton);
            if (idx < 0) idx = 0;
            SetKbFocus(idx);
        }

        /// <summary>키보드 포커스 링을 모두 끈다(목록으로 복귀·닫기 시).</summary>
        public void ClearKeyboard()
        {
            _kbIndex = -1;
            foreach (var c in _kbControls) if (c.nav != null) c.nav.SetNavFocus(false);
        }

        /// <summary>매수/매도 사이 좌우 이동. 왼쪽 끝에서 더 가면 false(목록으로 복귀 신호).</summary>
        public bool MoveKbHorizontal(int dir)
        {
            if (_kbControls.Count == 0) return false;
            if (_kbIndex < 0) { SetKbFocus(0); return true; }
            int next = _kbIndex + dir;
            if (next < 0) return false;                     // 목록으로 복귀
            if (next >= _kbControls.Count) return true;     // 오른쪽 끝은 제자리
            SetKbFocus(next);
            return true;
        }

        /// <summary>스페이스바 = 포커스된 버튼 실행.</summary>
        public void ActivateKb()
        {
            if (_kbIndex < 0 || _kbIndex >= _kbControls.Count) return;
            var b = _kbControls[_kbIndex].btn;
            if (b != null && b.interactable) b.onClick.Invoke();
        }

        private void SetKbFocus(int idx)
        {
            _kbIndex = idx;
            for (int i = 0; i < _kbControls.Count; i++)
                if (_kbControls[i].nav != null) _kbControls[i].nav.SetNavFocus(i == idx);
        }

        public void Show(CompanyData company)
        {
            _company = company;
            if (root) root.SetActive(company != null);
            Refresh();
            ResetScrollToTop();
        }

        /// <summary>새 종목을 펼칠 때 레이아웃을 즉시 재계산하고 스크롤을 맨 위로 보낸다.</summary>
        private void ResetScrollToTop()
        {
            if (_company == null) return;
            if (detailContent) LayoutRebuilder.ForceRebuildLayoutImmediate(detailContent);
            if (detailScroll) detailScroll.verticalNormalizedPosition = 1f; // 1 = 최상단
        }

        public void Clear()
        {
            _company = null;
            if (root) root.SetActive(false);
        }

        public void Refresh()
        {
            if (_company == null) return;

            if (iconImage)
            {
                var icon = StockIcons.Get(_company.Id);
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }
            if (nameText) nameText.text = _company.GetLocalizedName();
            if (marketCapText) marketCapText.text = StockLoc.LF("ui_stock_marketcap", "시총 {0}등급", _company.MarketCap);
            if (tagChips) tagChips.SetTags(_company.Tags);
            else if (tagsText) tagsText.text = _company.Tags != null ? string.Join(", ", System.Array.ConvertAll(_company.Tags, StockLoc.Tag)) : "";
            if (priceText) priceText.text = StockLoc.LF("ui_stock_price", "₩{0:N0}", _company.CurrentPrice);

            float change = StockListItemUI.GetChangePercent(_company);
            if (changeText)
            {
                string sign = change > 0f ? "▲" : (change < 0f ? "▼" : "-");
                changeText.text = $"{sign} {Mathf.Abs(change):F1}%";
                changeText.color = change > 0f ? riseColor : (change < 0f ? fallColor : flatColor);
            }

            if (miniChart) miniChart.SetData(_company.PriceHistory);

            UpdateTradeButtons();
        }

        /// <summary>매수/매도 버튼의 활성 여부를 현재 소지금·보유 주수로 갱신한다.
        /// (수량은 팝업에서 정하므로 여기선 '가능/불가'만 본다)</summary>
        private void UpdateTradeButtons()
        {
            var pm = StockGameManager.Instance?.PortfolioManager;
            int price = _company != null ? _company.CurrentPrice : 0;

            SetTradeButtonState(buyButton, ref _buyLabel, pm != null && price > 0 && pm.CurrentGold >= price);
            SetTradeButtonState(sellButton, ref _sellLabel,
                (pm?.GetHolding(_company != null ? _company.Id : null)?.Shares ?? 0) > 0);
        }

        /// <summary>거래 버튼의 활성 여부를 적용하면서 <b>라벨 글자까지 함께 어둡게</b> 만든다.
        /// Button의 disabledColor는 배경만 죽이기 때문에, 글자가 그대로면 아직 누를 수 있어 보인다.</summary>
        private void SetTradeButtonState(Button button, ref TextMeshProUGUI cachedLabel, bool enabled)
        {
            if (button == null) return;
            button.interactable = enabled;

            if (cachedLabel == null) cachedLabel = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (cachedLabel == null) return;

            // 원래 색은 처음 한 번만 기억한다(비활성 색을 원본으로 덮어쓰지 않도록).
            if (cachedLabel == _buyLabel && !_buyLabelColorCached) { _buyLabelColor = cachedLabel.color; _buyLabelColorCached = true; }
            if (cachedLabel == _sellLabel && !_sellLabelColorCached) { _sellLabelColor = cachedLabel.color; _sellLabelColorCached = true; }

            Color baseColor = cachedLabel == _buyLabel ? _buyLabelColor : _sellLabelColor;
            cachedLabel.color = enabled ? baseColor : baseColor * DisabledLabelDim;
        }

        /// <summary>매수 — 수량은 팝업에서 정한다(직접 체결하지 않는다).</summary>
        private void OnBuy()
        {
            if (_company == null) return;
            StockTradeDialog.OpenBuy(_company.Id);
        }

        /// <summary>매도 — 수량은 팝업에서 정한다(직접 체결하지 않는다).</summary>
        private void OnSell()
        {
            if (_company == null) return;
            StockTradeDialog.OpenSell(_company.Id);
        }
    }
}
