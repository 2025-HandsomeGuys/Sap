using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Stock.Core;

namespace Stock.UI
{
    /// <summary>뉴스 탭. 활성 뉴스를 상단에, 최근 히스토리를 하단에 표시한다.</summary>
    public class NewsTabUI : MonoBehaviour
    {
        [SerializeField] private Transform container;     // ScrollView Content
        [SerializeField] private GameObject itemPrefab;   // NewsItemUI 프리팹
        [SerializeField] private int recentHistoryTicks = 2; // 소멸(종료) 후 이 틱수가 지나면 목록에서 자동 제거

        // 소멸([소멸]=Fade) 상태로 들어간 뉴스는 3틱까지만 보여준다.
        // 활성 목록의 Fade 뉴스와 히스토리 양쪽에 같은 상한을 걸어야 한다
        // (인스펙터 recentHistoryTicks를 크게 잡아도 이 값을 넘지 못한다).
        private const int MaxFadeVisibleTicks = 3;

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

        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<NewsItemUI> _items = new List<NewsItemUI>();
        private int _selectedIndex = -1;               // 슬롯 커서 (시세탭과 같은 방식)
        private KeyCode _heldKey = KeyCode.None;        // W/S 꾹 누르기 반복용
        private float _nextRepeatTime;

        public void Refresh()
        {
            var nm = StockGameManager.Instance?.NewsManager;
            if (nm == null || container == null || itemPrefab == null) return;

            foreach (var r in _rows) if (r) Destroy(r);
            _rows.Clear();
            _items.Clear();

            // 1. 활성 뉴스 (최신순으로 보이도록 역순)
            var active = nm.GetActiveNews();
            for (int i = active.Count - 1; i >= 0; i--)
            {
                // 소멸 페이즈에 들어간 지 3틱이 넘은 뉴스는 숨긴다.
                if (active[i].CurrentPhase == Stock.Data.NewsPhase.Fade
                    && active[i].Template.DurationTicks - active[i].RemainingTicks >= MaxFadeVisibleTicks)
                    continue;

                var go = Instantiate(itemPrefab, container);
                var item = go.GetComponent<NewsItemUI>();
                if (item != null) item.BindActive(active[i]);
                _rows.Add(go);
                if (item != null) _items.Add(item);
            }

            // 2. 히스토리 (종료 후 recentHistoryTicks 틱 이내인 것만, 최신순 — 오래된 뉴스는 자동으로 사라짐)
            var history = nm.GetHistory();
            int currentTick = StockGameManager.Instance.CurrentTick;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var entry = history[i];
                if (currentTick - entry.ExpiredTick > Mathf.Min(recentHistoryTicks, MaxFadeVisibleTicks)) break; // 히스토리는 시간순이므로 이후는 전부 더 오래됨

                var go = Instantiate(itemPrefab, container);
                var item = go.GetComponent<NewsItemUI>();
                if (item != null) item.BindHistory(entry);
                _rows.Add(go);
                if (item != null) _items.Add(item);
            }

            // 뉴스 목록이 갱신돼도 슬롯 커서를 유지한다(범위 밖이면 클램프).
            if (_items.Count == 0) _selectedIndex = -1;
            else _selectedIndex = Mathf.Clamp(_selectedIndex < 0 ? 0 : _selectedIndex, 0, _items.Count - 1);
            UpdateSelectionVisual();
        }

        // ===================================================
        // W/S 슬롯 선택 (시세탭 StockListUI와 같은 방식)
        // ===================================================

        /// <summary>W/S로 뉴스 슬롯 커서를 위아래로 옮긴다. 꾹 누르면 연속 이동(입력 중엔 무시).</summary>
        private void Update()
        {
            if (_items.Count == 0) return;
            if (IsTypingInInputField()) { _heldKey = KeyCode.None; return; }

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
    }
}
