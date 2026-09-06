// @tags: coin, ui, select, lineup, roster
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Coin.Core;

namespace Coin.UI
{
    /// <summary>
    /// 난이도 선택 좌측 코인 목록 (스크롤). 로스터의 현재 코인을 카드로 나열한다.
    /// 카드 클릭 = "선택"(우측 미리보기 갱신 + 카드 강조)이며, 실제 진입은
    /// CoinPreviewUI의 "스테이지 진입" 버튼이 담당한다(설계 §8.6 + 난이도 선택 디자인).
    /// </summary>
    public class CoinSelectUI : MonoBehaviour
    {
        [SerializeField] private CoinModeUI modeUI;
        [SerializeField] private TextMeshProUGUI titleText;  // "코인 목록" 등 목록 제목 (지역화)
        [SerializeField] private Transform cardContainer;
        [SerializeField] private CoinCardUI cardPrefab;
        [SerializeField] private CoinPreviewUI previewUI;   // 우측 미리보기 패널
        [Tooltip("스크롤 시 슬롯 단위 이동에 필요. 미연결 시 스크롤 이동 비활성.")]
        [SerializeField] private ScrollRect scrollRect;

        [Header("키보드 이동 (선택 — None이면 비활성)")]
        [Tooltip("위 코인으로 이동")]
        [SerializeField] private KeyCode upKey = KeyCode.W;
        [Tooltip("아래 코인으로 이동")]
        [SerializeField] private KeyCode downKey = KeyCode.S;
        [Tooltip("오른쪽 — 우측 '스테이지 진입' 버튼으로 포커스 이동")]
        [SerializeField] private KeyCode rightKey = KeyCode.D;
        [Tooltip("왼쪽 — 진입 버튼에서 목록으로 포커스 복귀")]
        [SerializeField] private KeyCode leftKey = KeyCode.A;
        [Tooltip("꾹 눌렀을 때 연속 이동이 시작되기까지의 시간(초)")]
        [SerializeField] private float keyRepeatDelay = 0.35f;
        [Tooltip("연속 이동 간격(초)")]
        [SerializeField] private float keyRepeatInterval = 0.08f;

        private readonly List<CoinCardUI> _cards = new List<CoinCardUI>();
        private readonly List<int> _selectable = new List<int>();  // 재사용 버퍼 (_cards 내 인덱스)
        private int _selectedSlot = -1;
        private float _savedScrollSensitivity;
        private KeyCode _heldKey = KeyCode.None;   // W/S 꾹 누르기 반복용
        private float _nextRepeatTime;
        // 포커스가 우측 '스테이지 진입' 버튼에 가 있는지 (D로 이동, A/W/S로 목록 복귀)
        private bool _focusEnter;

        public void Build()
        {
            if (titleText) titleText.text = CoinLoc.L("ui_coin_list_title", "코인 목록");

            var mgr = CoinGameManager.Instance;
            if (mgr == null || cardContainer == null || cardPrefab == null) return;

            foreach (var c in _cards) if (c) Destroy(c.gameObject);
            _cards.Clear();

            var slots = mgr.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                var card = Instantiate(cardPrefab, cardContainer);
                card.Bind(slots[i].id, OnCardClicked);
                _cards.Add(card);
            }

            // 하루 1코인 잠금 없음 — 진입 가능한 코인은 아무거나 선택 가능.
            // 제목은 하루 단위 상태만 반영: 판 소진(더 못 함) > 출금 한도 도달(이익 +0) > 일반.
            bool startedToday = mgr.LockedDay == mgr.CurrentDay;
            if (titleText)
            {
                if (mgr.RoundsLeft <= 0)
                    titleText.text = CoinLoc.L("ui_coin_list_rounds_done",
                        "오늘 남은 판 없음 (내일 다시)");
                else if (startedToday && mgr.WithdrawLimitHeadroom() <= 0)
                    titleText.text = CoinLoc.L("ui_coin_list_limit_done",
                        "출금 한도 도달 (적중해도 +₩0)");
            }

            // 기본 선택 — 살 수 있는 첫 코인(없으면 첫 코인).
            int pick = -1;
            for (int i = 0; i < slots.Count; i++)
                if (mgr.CanAfford(slots[i].id)) { pick = slots[i].id; break; }
            if (pick < 0 && slots.Count > 0) pick = slots[0].id;
            if (pick >= 0) Select(pick);

            // 스크롤 입력은 Update()에서 직접 처리 — ScrollRect 자체 휠 스크롤 비활성
            if (scrollRect)
            {
                _savedScrollSensitivity = scrollRect.scrollSensitivity;
                scrollRect.scrollSensitivity = 0f;
            }
        }

        public void Refresh()
        {
            foreach (var c in _cards) if (c) c.Refresh();
        }

        private void OnCardClicked(int slotId) => Select(slotId);

        private void Select(int slotId)
        {
            _selectedSlot = slotId;
            _focusEnter = false;   // 선택이 바뀌면 포커스는 다시 목록으로
            foreach (var c in _cards) if (c) c.SetSelected(c.SlotId == slotId);
            if (previewUI) { previewUI.Show(slotId); previewUI.SetEnterFocus(false); }
            ScrollToSelected();
        }

        /// <summary>우측 '스테이지 진입' 버튼으로 포커스를 옮기거나 목록으로 되돌린다.</summary>
        private void SetEnterFocus(bool on)
        {
            _focusEnter = on;
            if (previewUI) previewUI.SetEnterFocus(on);
        }

        // --- 휠 / W·S 입력 → 슬롯 단위 이동 ---
        private void Update()
        {
            if (_cards.Count == 0) return;

            HandleWheelInput();
            HandleKeyInput();
        }

        private void HandleWheelInput()
        {
            if (scrollRect == null) return;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f)) return;

            if (!IsPointerOverScrollRect()) return;

            // 스크롤 위 = 이전 슬롯, 스크롤 아래 = 다음 슬롯
            StepSelection(scroll > 0f ? -1 : +1);
        }

        /// <summary>
        /// W/S로 코인 이동, D로 우측 '스테이지 진입' 버튼으로 포커스 이동, Space로 진입.
        /// 꾹 누르면 연속 이동한다(금액 입력 중에는 무시).
        /// </summary>
        private void HandleKeyInput()
        {
            if (IsTypingInInputField()) { _heldKey = KeyCode.None; return; }

            // ── 포커스가 우측 '스테이지 진입' 버튼에 있을 때 ──
            if (_focusEnter)
            {
                // 버튼이 도중에 비활성(골드 부족 등)이 되면 목록으로 되돌린다
                if (previewUI == null || !previewUI.CanEnter) { SetEnterFocus(false); return; }

                if (Input.GetKeyDown(KeyCode.Space)) { previewUI.ActivateEnter(); return; }
                if ((leftKey != KeyCode.None && Input.GetKeyDown(leftKey))) { SetEnterFocus(false); return; }
                if (upKey != KeyCode.None && Input.GetKeyDown(upKey)) { SetEnterFocus(false); StepSelection(-1); return; }
                if (downKey != KeyCode.None && Input.GetKeyDown(downKey)) { SetEnterFocus(false); StepSelection(+1); return; }
                return;
            }

            // ── 목록에 포커스가 있을 때: D 또는 Space로 진입 버튼으로 넘어간다 ──
            bool goRight = (rightKey != KeyCode.None && Input.GetKeyDown(rightKey)) || Input.GetKeyDown(KeyCode.Space);
            if (goRight && previewUI != null && previewUI.CanEnter)
            {
                _heldKey = KeyCode.None;
                SetEnterFocus(true);
                Market.MarketUISfx.Play(Market.MarketUISfx.Kind.Scroll);
                return;
            }

            int delta = 0;
            if (upKey != KeyCode.None && Input.GetKeyDown(upKey)) delta = -1;
            else if (downKey != KeyCode.None && Input.GetKeyDown(downKey)) delta = +1;

            if (delta != 0)
            {
                _heldKey = delta < 0 ? upKey : downKey;
                // 마켓은 timeScale=0이라 unscaled로 재야 한다.
                _nextRepeatTime = Time.unscaledTime + keyRepeatDelay;
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

        /// <summary>선택을 delta칸 옮긴다(잠긴 카드는 건너뛰고, 범위 밖이면 아무것도 하지 않음).</summary>
        private void StepSelection(int delta)
        {
            // 선택 가능한 카드 목록 구축 (잠긴 카드 건너뛰기)
            _selectable.Clear();
            for (int i = 0; i < _cards.Count; i++)
            {
                if (!_cards[i]) continue;
                var btn = _cards[i].GetComponent<Button>();
                if (btn && !btn.interactable) continue;
                _selectable.Add(i);
            }
            if (_selectable.Count <= 1) return;

            // 현재 선택의 _selectable 내 위치
            int curSelIdx = -1;
            for (int i = 0; i < _selectable.Count; i++)
                if (_cards[_selectable[i]].SlotId == _selectedSlot) { curSelIdx = i; break; }
            if (curSelIdx < 0) curSelIdx = 0;

            int newSelIdx = Mathf.Clamp(curSelIdx + delta, 0, _selectable.Count - 1);
            if (newSelIdx == curSelIdx) return;

            Market.MarketUISfx.Play(Market.MarketUISfx.Kind.Scroll); // 슬롯 이동 리플 "차라라락"
            Select(_cards[_selectable[newSelIdx]].SlotId);
        }

        /// <summary>TMP 입력필드에 포커스가 있으면 W/S를 먹지 않게 한다(타이핑 보호).</summary>
        private static bool IsTypingInInputField()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            var field = go.GetComponent<TMP_InputField>();
            return field != null && field.isFocused;
        }

        // 선택된 슬롯이 뷰포트 안에 보이도록 스크롤 위치 조정
        private void ScrollToSelected()
        {
            if (scrollRect == null || scrollRect.content == null || _cards.Count <= 1) return;
            int idx = -1;
            for (int i = 0; i < _cards.Count; i++)
                if (_cards[i] && _cards[i].SlotId == _selectedSlot) { idx = i; break; }
            if (idx < 0) return;

            float t = (float)idx / (_cards.Count - 1);
            scrollRect.verticalNormalizedPosition = 1f - t;
        }

        private bool IsPointerOverScrollRect()
        {
            var rt = (RectTransform)scrollRect.transform;
            var canvas = scrollRect.GetComponentInParent<Canvas>();
            Camera cam = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam);
        }

        private void OnDestroy()
        {
            if (scrollRect) scrollRect.scrollSensitivity = _savedScrollSensitivity;
        }
    }
}

