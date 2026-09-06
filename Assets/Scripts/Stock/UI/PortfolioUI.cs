using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Stock.Core;
using Stock.Systems;

namespace Stock.UI
{
    /// <summary>포트폴리오 탭. 보유 종목 목록과 요약(평가금액·수익률·인내심)을 표시한다.</summary>
    public class PortfolioUI : MonoBehaviour
    {
        [Header("요약")]
        [SerializeField] private TextMeshProUGUI totalValueLabelText;
        [SerializeField] private TextMeshProUGUI totalValueAmountText;
        [SerializeField] private TextMeshProUGUI profitAmountText;
        [SerializeField] private TextMeshProUGUI profitRateText;
        [SerializeField] private TextMeshProUGUI holdingCountText;
        [SerializeField] private TextMeshProUGUI patienceText;

        [Header("목록")]
        [SerializeField] private Transform container;
        [SerializeField] private GameObject itemPrefab;       // PortfolioItemUI 프리팹
        [SerializeField] private GameObject emptyLabel;       // 보유 종목 없을 때 표시(optional)

        [Header("키보드 슬롯 선택 (선택 — None이면 비활성)")]
        [Tooltip("비우면 container의 부모에서 ScrollRect를 자동으로 찾는다.")]
        [SerializeField] private ScrollRect scrollRect;
        [Tooltip("위 슬롯으로 이동")]
        [SerializeField] private KeyCode upKey = KeyCode.W;
        [Tooltip("아래 슬롯으로 이동")]
        [SerializeField] private KeyCode downKey = KeyCode.S;
        [Tooltip("꾹 눌렀을 때 연속 이동이 시작되기까지의 시간(초)")]
        [SerializeField] private float keyRepeatDelay = 0.35f;
        [Tooltip("연속 이동 간격(초)")]
        [SerializeField] private float keyRepeatInterval = 0.08f;

        [Header("색상")]
        [SerializeField] private Color riseColor = new Color(0.23f, 0.6f, 0.13f);
        [SerializeField] private Color fallColor = new Color(0.64f, 0.18f, 0.18f);
        [SerializeField] private Color flatColor = new Color(0.5f, 0.5f, 0.5f);

        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<PortfolioItemUI> _items = new List<PortfolioItemUI>();
        private int _selectedIndex = -1;               // 슬롯 커서 (시세탭과 같은 방식)
        private KeyCode _heldKey = KeyCode.None;        // W/S 꾹 누르기 반복용
        private float _nextRepeatTime;
        private TextMeshProUGUI _emptyLabelText;

        public void Refresh()
        {
            var pm = StockGameManager.Instance?.PortfolioManager;
            if (pm == null) return;

            var holdings = pm.GetHoldings();

            if (totalValueLabelText) totalValueLabelText.text = StockLoc.L("ui_stock_total_value_label", "총 평가금액");

            long profitAmount = pm.GetProfitAmount();

            if (totalValueAmountText)
            {
                totalValueAmountText.text = StockLoc.LF("ui_stock_total_value_amount", "₩{0:N0}", pm.GetTotalValue());
                totalValueAmountText.color = profitAmount > 0 ? riseColor : (profitAmount < 0 ? fallColor : flatColor);
            }

            if (profitAmountText)
            {
                string sign = profitAmount > 0 ? "+" : "";
                profitAmountText.text = StockLoc.LF("ui_stock_profit_amount", "평가 손익 {0}", $"{sign}₩{profitAmount:N0}");
                profitAmountText.color = profitAmount > 0 ? riseColor : (profitAmount < 0 ? fallColor : flatColor);
            }

            if (profitRateText)
            {
                float rate = pm.GetProfitRate() * 100f;
                string sign = rate > 0f ? "+" : "";
                profitRateText.text = StockLoc.LF("ui_stock_profit_rate", "수익률 {0}", $"{sign}{rate:F1}%");
                profitRateText.color = rate > 0f ? riseColor : (rate < 0f ? fallColor : flatColor);
            }

            if (holdingCountText) holdingCountText.text = StockLoc.LF("ui_stock_holding_count", "보유 종목 {0}개", holdings.Count);

            // 인내심(보유 기간 제한) 제거 — 관련 라벨은 숨긴다.
            if (patienceText && patienceText.gameObject.activeSelf) patienceText.gameObject.SetActive(false);

            // 목록 재생성
            foreach (var r in _rows) if (r) Destroy(r);
            _rows.Clear();
            _items.Clear();

            if (container != null && itemPrefab != null)
            {
                foreach (var h in holdings)
                {
                    var go = Instantiate(itemPrefab, container);
                    var item = go.GetComponent<PortfolioItemUI>();
                    if (item != null) item.Bind(h);
                    _rows.Add(go);
                    if (item != null) _items.Add(item);
                }
            }

            // 시세 변동 등으로 목록이 매 틱 재생성돼도 슬롯 커서를 유지한다(범위 밖이면 클램프).
            if (_items.Count == 0) _selectedIndex = -1;
            else _selectedIndex = Mathf.Clamp(_selectedIndex < 0 ? 0 : _selectedIndex, 0, _items.Count - 1);
            UpdateSelectionVisual();

            if (emptyLabel)
            {
                bool isEmpty = holdings.Count == 0;
                emptyLabel.SetActive(isEmpty);
                if (isEmpty)
                {
                    var lbl = ResolveEmptyLabel();
                    if (lbl) lbl.text = StockLoc.L("ui_stock_portfolio_empty", "보유 중인 종목이 없습니다");
                }
            }
        }

        // ===================================================
        // W/S 슬롯 선택 (시세탭 StockListUI와 같은 방식)
        // ===================================================

        /// <summary>W/S로 보유 슬롯 커서를 위아래로 옮긴다. 꾹 누르면 연속 이동(수량 입력 중엔 무시).</summary>
        private void Update()
        {
            if (_items.Count == 0) return;
            if (StockTradeDialog.IsOpen) { _heldKey = KeyCode.None; return; }
            if (IsTypingInInputField()) { _heldKey = KeyCode.None; return; }

            // 스페이스 = 선택한 보유 종목을 시세 탭에서 열기(마우스 클릭과 같은 동작).
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count && _items[_selectedIndex])
                    _items[_selectedIndex].OnRowClicked();
                return;
            }

            int delta = 0;
            if (upKey != KeyCode.None && Input.GetKeyDown(upKey)) delta = -1;
            else if (downKey != KeyCode.None && Input.GetKeyDown(downKey)) delta = +1;

            if (delta != 0)
            {
                _heldKey = delta < 0 ? upKey : downKey;
                _nextRepeatTime = Time.unscaledTime + keyRepeatDelay; // 마켓은 timeScale=0이라 unscaled
                StepSelection(delta);
                return;
            }

            // 꾹 누르고 있는 동안 반복
            if (_heldKey == KeyCode.None) return;
            if (!Input.GetKey(_heldKey)) { _heldKey = KeyCode.None; return; }
            if (Time.unscaledTime < _nextRepeatTime) return;

            _nextRepeatTime = Time.unscaledTime + keyRepeatInterval;
            StepSelection(_heldKey == upKey ? -1 : +1);
        }

        /// <summary>선택을 delta칸 옮긴다(범위 밖이면 제자리 — 순환하지 않음).</summary>
        private void StepSelection(int delta)
        {
            if (_items.Count == 0) return;
            int cur = _selectedIndex < 0 ? 0 : _selectedIndex;
            int next = Mathf.Clamp(cur + delta, 0, _items.Count - 1);
            if (next == _selectedIndex) return;

            _selectedIndex = next;
            Market.MarketUISfx.Play(Market.MarketUISfx.Kind.Scroll); // 슬롯 이동 리플
            UpdateSelectionVisual();
            ScrollToSelected();
        }

        private void UpdateSelectionVisual()
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i]) _items[i].SetSelected(i == _selectedIndex);
        }

        /// <summary>선택된 슬롯이 뷰포트 안에 보이도록 스크롤 위치를 맞춘다.</summary>
        private void ScrollToSelected()
        {
            var sr = ResolveScrollRect();
            if (sr == null || sr.content == null || _items.Count <= 1 || _selectedIndex < 0) return;
            float t = (float)_selectedIndex / (_items.Count - 1);
            sr.verticalNormalizedPosition = 1f - t;
        }

        /// <summary>인스펙터에 ScrollRect가 비어 있으면 container의 부모에서 찾아 캐시한다.</summary>
        private ScrollRect ResolveScrollRect()
        {
            if (scrollRect) return scrollRect;
            if (container) scrollRect = container.GetComponentInParent<ScrollRect>(true);
            return scrollRect;
        }

        /// <summary>TMP 입력필드에 포커스가 있으면 W/S를 먹지 않게 한다.</summary>
        private static bool IsTypingInInputField()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            var field = go.GetComponent<TMP_InputField>();
            return field != null && field.isFocused;
        }

        /// <summary>emptyLabel(또는 그 자식)의 TMP 라벨을 찾아 캐시한다.</summary>
        private TextMeshProUGUI ResolveEmptyLabel()
        {
            if (_emptyLabelText) return _emptyLabelText;
            if (emptyLabel) _emptyLabelText = emptyLabel.GetComponentInChildren<TextMeshProUGUI>(true);
            return _emptyLabelText;
        }
    }
}
