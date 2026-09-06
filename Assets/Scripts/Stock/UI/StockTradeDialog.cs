// @tags: stock, trade, buy, sell, dialog, overlay, ui, code-generated
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Stock.Core;
using Market;

namespace Stock.UI
{
    /// <summary>
    /// 종목을 '수량 지정'으로 매수/매도하는 코드 생성 팝업(구 StockSellDialog를 매수까지 확장).
    /// 에디터 셋업이 필요 없다(전부 코드 생성).
    ///
    /// 프로젝트 규칙: <b>수량은 반드시 이 팝업에서만 정한다.</b> 시세 상세(CompanyDetailUI)의
    /// 매수·매도 버튼이 여기로 들어오고, 인라인 수량 입력은 쓰지 않는다.
    ///
    /// - −/+ 버튼·직접 입력으로 수량 조절, [최대]/[전량] 버튼으로 상한 선택.
    /// - 매수 상한 = 보유 골드로 살 수 있는 주수, 매도 상한 = 보유 주수.
    /// - 성공 시 PortfolioManager.OnPortfolioChanged가 관련 UI를 갱신한다.
    /// - 키보드: W/S = ±1, A/D = ±10, Space = 확정, ESC = 취소.
    ///   (연 프레임의 Space를 그대로 먹지 않도록 한 프레임 가드를 둔다)
    /// </summary>
    public class StockTradeDialog : MonoBehaviour
    {
        public enum Mode { Buy, Sell }

        private static StockTradeDialog _instance;

        // 1920x1080 기준 좌표계(자체 CanvasScaler)에서 배치한다.
        private const float RefW = 1920f, RefH = 1080f;

        private TMP_FontAsset _font;

        private GameObject _dimRoot;      // 전체 화면 딤 + 바깥클릭 취소
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _infoText;
        private TextMeshProUGUI _qtyLabel;
        private TextMeshProUGUI _amountText;
        private TMP_InputField _qtyInput;
        private TextMeshProUGUI _maxLabel;
        private Image _confirmImage;
        private TextMeshProUGUI _confirmLabel;

        private Mode _mode = Mode.Sell;
        private string _companyId;
        private int _max = 1;   // 이번 주문의 상한(매수=구매 가능 주수, 매도=보유 주수)
        private int _qty = 1;   // 현재 선택 수량
        private int _openFrame = -1;

        /// <summary>팝업이 떠 있는지. 뒤쪽 화면이 같은 키를 겹쳐 먹지 않게 하는 데 쓴다.</summary>
        public static bool IsOpen => _instance != null && _instance._dimRoot != null && _instance._dimRoot.activeSelf;

        // ===================================================
        // 진입점
        // ===================================================

        /// <summary>매수 팝업. 한 주도 살 수 없으면 거절음만 내고 열지 않는다.</summary>
        public static void OpenBuy(string companyId)
        {
            if (string.IsNullOrEmpty(companyId)) return;
            var company = StockGameManager.Instance?.CompanyManager?.GetCompany(companyId);
            var pm = StockGameManager.Instance?.PortfolioManager;
            if (company == null || pm == null || company.CurrentPrice <= 0) return;

            int affordable = (int)(pm.CurrentGold / company.CurrentPrice);
            if (affordable < 1) { MarketUISfx.Play(MarketUISfx.Kind.Deny); return; }

            EnsureInstance();
            _instance.Show(Mode.Buy, companyId, affordable, 1);
        }

        /// <summary>매도 팝업. 보유분이 없으면 열지 않는다.</summary>
        public static void OpenSell(string companyId)
        {
            if (string.IsNullOrEmpty(companyId)) return;
            var pm = StockGameManager.Instance?.PortfolioManager;
            var holding = pm?.GetHolding(companyId);
            if (holding == null || holding.Shares <= 0) { MarketUISfx.Play(MarketUISfx.Kind.Deny); return; }

            EnsureInstance();
            // 기본값 = 전량(가장 흔한 '포지션 정리'를 1클릭 확정 가능, 필요 시 줄이면 된다)
            _instance.Show(Mode.Sell, companyId, holding.Shares, holding.Shares);
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("StockTradeDialog");
            _instance = go.AddComponent<StockTradeDialog>();
            _instance.Build();
        }

        // ===================================================
        // 열기 / 닫기
        // ===================================================
        private void Show(Mode mode, string companyId, int max, int initialQty)
        {
            _mode = mode;
            _companyId = companyId;
            _max = Mathf.Max(1, max);
            _qty = Mathf.Clamp(initialQty, 1, _max);
            _openFrame = Time.frameCount;

            var company = StockGameManager.Instance?.CompanyManager?.GetCompany(companyId);
            string name = company != null ? company.GetLocalizedName() : companyId;
            if (_titleText) _titleText.text = name;

            ApplyModeStyle();
            if (_dimRoot) _dimRoot.SetActive(true);
            SyncQtyUI();
        }

        /// <summary>매수/매도에 따라 라벨과 확정 버튼 색을 바꾼다(초록=매수, 빨강=매도).</summary>
        private void ApplyModeStyle()
        {
            bool buy = _mode == Mode.Buy;

            if (_qtyLabel)
                _qtyLabel.text = buy
                    ? StockLoc.L("ui_stock_buy_qty", "매수 수량")
                    : StockLoc.L("ui_stock_sell_qty", "매도 수량");

            if (_maxLabel)
                _maxLabel.text = buy
                    ? StockLoc.L("ui_stock_buy_max", "최대")
                    : StockLoc.L("ui_stock_sell_max", "전량");

            if (_confirmLabel)
                _confirmLabel.text = buy
                    ? StockLoc.L("ui_stock_buy", "매수")
                    : StockLoc.L("ui_stock_sell", "매도");

            if (_confirmImage) _confirmImage.color = buy ? MarketTheme.Up : MarketTheme.Down;
        }

        private void Close()
        {
            if (_dimRoot) _dimRoot.SetActive(false);
            _companyId = null;
        }

        // ===================================================
        // 키보드 — W/S ±1, A/D ±10, Space 확정, ESC 취소
        // ===================================================
        private void Update()
        {
            if (_dimRoot == null || !_dimRoot.activeSelf) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CodeUI.PlayBack();
                Close();
                return;
            }

            // 팝업을 연 그 프레임의 키(목록에서 누른 Space 등)를 그대로 먹지 않게 한 프레임 건너뛴다.
            if (Time.frameCount == _openFrame) return;
            if (IsTypingInInputField()) return;

            if (Input.GetKeyDown(KeyCode.Space)) { Confirm(); return; }
            if (Input.GetKeyDown(KeyCode.W)) { SetQty(_qty + 1); return; }
            if (Input.GetKeyDown(KeyCode.S)) { SetQty(_qty - 1); return; }
            if (Input.GetKeyDown(KeyCode.D)) { SetQty(_qty + 10); return; }
            if (Input.GetKeyDown(KeyCode.A)) { SetQty(_qty - 10); return; }
        }

        /// <summary>수량 입력 필드에 타이핑 중이면 W/S/A/D를 먹지 않게 한다.</summary>
        private static bool IsTypingInInputField()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            var field = go.GetComponent<TMP_InputField>();
            return field != null && field.isFocused;
        }

        // ===================================================
        // 수량 조절
        // ===================================================
        private void SetQty(int qty)
        {
            _qty = Mathf.Clamp(qty, 1, _max);
            SyncQtyUI();
        }

        private void OnQtyInputChanged(string text)
        {
            // 입력 중에는 필드 텍스트를 되쓰지 않는다(커서 튐 방지). 상한/하한만 미리 반영해 금액 갱신.
            _qty = int.TryParse(text, out int v) ? Mathf.Clamp(v, 1, _max) : 1;
            UpdateAmounts();
        }

        private void OnQtyInputEndEdit(string text)
        {
            if (!int.TryParse(text, out int v)) v = 1;
            SetQty(v);
        }

        /// <summary>입력 필드·금액 표시를 현재 _qty로 맞춘다.</summary>
        private void SyncQtyUI()
        {
            if (_qtyInput && _qtyInput.text != _qty.ToString())
                _qtyInput.SetTextWithoutNotify(_qty.ToString());
            UpdateAmounts();
        }

        private void UpdateAmounts()
        {
            var company = StockGameManager.Instance?.CompanyManager?.GetCompany(_companyId);
            int price = company != null ? company.CurrentPrice : 0;
            long amount = (long)price * _qty;

            if (_infoText)
            {
                _infoText.text = _mode == Mode.Buy
                    ? StockLoc.LF("ui_stock_buy_info", "구매 가능 {0}주 · 현재 ₩{1:N0}", _max, price)
                    : StockLoc.LF("ui_stock_sell_owned", "보유 {0}주 · 현재 ₩{1:N0}", _max, price);
            }
            if (_amountText)
            {
                _amountText.text = _mode == Mode.Buy
                    ? StockLoc.LF("ui_stock_buy_cost", "주문 금액  ₩{0:N0}", amount)
                    : StockLoc.LF("ui_stock_sell_proceeds", "예상 수령액  ₩{0:N0}", amount);
            }
        }

        // ===================================================
        // 확정
        // ===================================================
        private void Confirm()
        {
            if (string.IsNullOrEmpty(_companyId)) { Close(); return; }
            var pm = StockGameManager.Instance?.PortfolioManager;
            int qty = Mathf.Clamp(_qty, 1, _max);
            bool ok = pm != null && (_mode == Mode.Buy ? pm.Buy(_companyId, qty) : pm.Sell(_companyId, qty));
            // 결과음이 자동 클릭음(MarketButtonSfx)을 같은 프레임 우선순위로 덮는다.
            MarketUISfx.Play(ok ? MarketUISfx.Kind.Confirm : MarketUISfx.Kind.Deny);
            if (ok) Close(); // 실패(당일 매수 등) 시엔 열어둔 채 사용자가 상황 인지
        }

        // ===================================================
        // UI 생성 (전부 코드)
        // ===================================================
        private void Build()
        {
            _font = LanguageManager.Instance != null ? LanguageManager.Instance.GetCurrentFont() : null;

            // --- 오버레이 캔버스 ---
            var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000; // 마켓 패널 위
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;

            // --- 딤 배경(바깥 클릭 = 취소) ---
            _dimRoot = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            _dimRoot.transform.SetParent(canvasGO.transform, false);
            Stretch((RectTransform)_dimRoot.transform, Vector2.zero);
            var dimImg = _dimRoot.GetComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.62f);
            _dimRoot.GetComponent<Button>().onClick.AddListener(Close);

            // --- 패널 테두리 + 본체 ---
            var border = CreateImage(_dimRoot.transform, "Border", MarketTheme.PanelBorder);
            SetCenter(border.rectTransform, 0f, 0f, 692f, 452f);
            var panel = CreateImage(border.transform, "Panel", MarketTheme.PanelBg);
            SetCenter(panel.rectTransform, 0f, 0f, 680f, 440f);
            var panelT = panel.transform;

            // --- 제목(종목명) ---
            _titleText = CreateText(panelT, "Title", 40f, FontStyles.Bold, MarketTheme.TextPrimary, TextAlignmentOptions.Center);
            SetCenter(_titleText.rectTransform, 0f, 150f, 640f, 56f);

            // --- 정보(보유/구매가능 · 현재가) ---
            _infoText = CreateText(panelT, "Info", 26f, FontStyles.Normal, MarketTheme.TextMuted, TextAlignmentOptions.Center);
            SetCenter(_infoText.rectTransform, 0f, 96f, 640f, 36f);

            // --- 수량 라벨 ---
            _qtyLabel = CreateText(panelT, "QtyLabel", 24f, FontStyles.Bold, MarketTheme.TextDim, TextAlignmentOptions.Center);
            SetCenter(_qtyLabel.rectTransform, 0f, 46f, 400f, 30f);

            // --- 수량 조절 행: [−] [입력] [+]   [최대/전량] ---
            var minus = CreateButton(panelT, "Minus", MarketTheme.ChipBg, "−", 40f, () => SetQty(_qty - 1));
            SetCenter(minus.image.rectTransform, -210f, -14f, 66f, 66f);

            _qtyInput = CreateIntInput(panelT, "QtyInput");
            SetCenter((RectTransform)_qtyInput.transform, -100f, -14f, 140f, 66f);

            var plus = CreateButton(panelT, "Plus", MarketTheme.ChipBg, "+", 40f, () => SetQty(_qty + 1));
            SetCenter(plus.image.rectTransform, 10f, -14f, 66f, 66f);

            var maxBtn = CreateButton(panelT, "Max", MarketTheme.BrandA, "", 26f, () => SetQty(_max));
            SetCenter(maxBtn.image.rectTransform, 150f, -14f, 150f, 66f);
            _maxLabel = maxBtn.GetComponentInChildren<TextMeshProUGUI>(true);

            // --- 주문 금액 / 예상 수령액 ---
            _amountText = CreateText(panelT, "Amount", 30f, FontStyles.Bold, MarketTheme.Gold, TextAlignmentOptions.Center);
            SetCenter(_amountText.rectTransform, 0f, -86f, 640f, 40f);

            // --- 하단 버튼: [매수/매도] [취소] — 확인이 왼쪽, 취소가 오른쪽 ---
            var confirmBtn = CreateButton(panelT, "Confirm", MarketTheme.Down, "", 28f, Confirm);
            SetCenter(confirmBtn.image.rectTransform, -105f, -160f, 190f, 68f);
            // 라벨·색은 ApplyModeStyle이 매수(초록)/매도(빨강)로 매번 덮어쓴다 — 여기 색은 초기값일 뿐.
            _confirmImage = confirmBtn.image;
            _confirmLabel = confirmBtn.GetComponentInChildren<TextMeshProUGUI>(true);

            var cancelBtn = CreateButton(panelT, "Cancel", MarketTheme.ChipBg, StockLoc.L("ui_stock_cancel", "취소"), 28f, Close);
            SetCenter(cancelBtn.image.rectTransform, 105f, -160f, 190f, 68f);

            _dimRoot.SetActive(false);
        }

        // ===================================================
        // 저수준 생성 헬퍼
        // ===================================================
        /// <summary>부모 중앙 기준으로 배치(anchor=center). x/y는 중심 오프셋, w/h는 크기.</summary>
        private static void SetCenter(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static void Stretch(RectTransform rt, Vector2 padding)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding.x, padding.y);
            rt.offsetMax = new Vector2(-padding.x, -padding.y);
        }

        private Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, float size, FontStyles style, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }

        private Button CreateButton(Transform parent, string name, Color bg, string label, float labelSize, System.Action onClick)
        {
            var img = CreateImage(parent, name, bg);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var text = CreateText(img.transform, "Label", labelSize, FontStyles.Bold, MarketTheme.TextPrimary, TextAlignmentOptions.Center);
            text.text = label; // CreateText는 내용을 세팅하지 않는다 — 여기서 라벨 문자열을 넣어야 버튼 글자가 보인다
            Stretch(text.rectTransform, Vector2.zero);
            return btn;
        }

        /// <summary>정수 전용 TMP_InputField를 코드로 구성(viewport+text 포함).</summary>
        private TMP_InputField CreateIntInput(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = MarketTheme.PanelBgAlt;

            var input = go.AddComponent<TMP_InputField>();

            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            var vpRect = viewport.GetComponent<RectTransform>();
            Stretch(vpRect, new Vector2(10f, 6f));

            var text = CreateText(viewport.transform, "Text", 32f, FontStyles.Bold, MarketTheme.TextPrimary, TextAlignmentOptions.Center);
            text.raycastTarget = false;
            Stretch(text.rectTransform, Vector2.zero);

            input.textViewport = vpRect;
            input.textComponent = text;
            if (_font != null) input.fontAsset = _font;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.characterLimit = 9;
            input.SetTextWithoutNotify("1");
            input.onValueChanged.AddListener(OnQtyInputChanged);
            input.onEndEdit.AddListener(OnQtyInputEndEdit);
            return input;
        }
    }
}
