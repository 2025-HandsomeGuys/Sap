using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Stock.Data;
using Stock.Core;
using Stock.Systems;

namespace Stock.UI
{
    /// <summary>주식 리스트(좌측 패널). CompanyManager의 종목을 행 프리팹으로 생성하고 선택을 관리한다.</summary>
    public class StockListUI : MonoBehaviour
    {
        [SerializeField] private Transform container;          // 행이 들어갈 ScrollView Content
        [SerializeField] private GameObject itemPrefab;        // StockListItemUI 프리팹
        [SerializeField] private CompanyDetailUI detail;       // 우측 상세 패널
        [Tooltip("목록 제목('전체 종목'). 보유중 필터가 켜지면 '보유 종목'으로 바뀐다. 비우면 자식에서 자동 탐색")]
        [SerializeField] private LocalizedTextUI listHeaderLabel;
        [Tooltip("스크롤 시 슬롯 단위 이동에 필요. 미연결 시 스크롤 이동 비활성.")]
        [SerializeField] private ScrollRect scrollRect;

        [Header("구 정렬 버튼 — 정렬은 필터 패널로 옮겼다(코드가 숨긴다)")]
        [Tooltip("정렬 칩이 필터 패널 안으로 들어가서 이 버튼은 런타임에 숨겨진다. 씬 연결을 깨지 않으려 필드만 남겨 둔다")]
        [SerializeField] private Button sortButton;
        [SerializeField] private TextMeshProUGUI sortLabel;
        [SerializeField] private Image sortIcon;
        [SerializeField] private Sprite[] sortIcons;

        [Header("태그 필터 (선택)")]
        [Tooltip("깔때기(필터) 아이콘 버튼. 클릭하면 filterPanel을 토글한다. 미연결 시 필터 UI 없음.")]
        [SerializeField] private Button filterButton;
        [Tooltip("태그 선택 패널(StockFilterPanel). 깔때기 아이콘 오른쪽에 배치. 미연결 시 필터 없음.")]
        [SerializeField] private StockFilterPanel filterPanel;

        [Header("검색")]
        [Tooltip("종목명 검색 입력 필드. 씬에서 직접 만들어 여기에 연결한다(권장). 연결하면 자동 생성은 하지 않는다.")]
        [SerializeField] private TMP_InputField searchInput;
        [Tooltip("searchInput이 비어 있을 때만 — 깔때기 버튼 오른쪽에 검색창을 코드로 만들어 준다. 씬에 직접 배치하는 게 기본이라 꺼져 있다.")]
        [SerializeField] private bool autoCreateSearchField = false;
        [Tooltip("자동 생성 시 깔때기 버튼과의 가로 간격(px)")]
        [SerializeField] private float searchGapX = 16f;
        [Tooltip("자동 생성 시 검색창 폭(px)")]
        [SerializeField] private float searchWidth = 420f;

        [Header("키보드 이동 (선택 — None이면 비활성)")]
        [Tooltip("위 슬롯으로 이동")]
        [SerializeField] private KeyCode upKey = KeyCode.W;
        [Tooltip("아래 슬롯으로 이동")]
        [SerializeField] private KeyCode downKey = KeyCode.S;
        [Tooltip("오른쪽 — 우측 상세 패널(−/+/매수/매도)로 포커스 이동")]
        [SerializeField] private KeyCode rightKey = KeyCode.D;
        [Tooltip("왼쪽 — 상세 패널에서 목록으로 포커스 복귀")]
        [SerializeField] private KeyCode leftKey = KeyCode.A;
        [Tooltip("꾹 눌렀을 때 연속 이동이 시작되기까지의 시간(초)")]
        [SerializeField] private float keyRepeatDelay = 0.35f;
        [Tooltip("연속 이동 간격(초)")]
        [SerializeField] private float keyRepeatInterval = 0.08f;

        [Header("리스트 행 크기")]
        [Tooltip("한 종목 행의 높이(px). Content가 이 값 × 종목 수로 자동 사이징된다.")]
        [SerializeField] private float rowHeight = 240f;

        /// <summary>정렬 모드.</summary>
        public enum SortMode { Default = 0, PriceDesc = 1, PriceAsc = 2, ChangeDesc = 3, NameAsc = 4 }

        /// <summary>정렬 버튼 순환 순서. 요청 순서: 기본 → 가격 낮은순 → 가격 높은순 → 등락률.</summary>
        private static readonly SortMode[] SortCycle =
        {
            SortMode.Default, SortMode.PriceAsc, SortMode.PriceDesc, SortMode.ChangeDesc
        };

        private readonly List<StockListItemUI> _items = new List<StockListItemUI>();
        private string _selectedId;
        private float _savedScrollSensitivity;
        private bool _scrollSaved;
        private KeyCode _heldKey = KeyCode.None;   // W/S 꾹 누르기 반복용
        private float _nextRepeatTime;

        // 키보드 포커스 구역. 목록(W/S 슬롯) ↔ 상단 툴바(정렬·필터, A/D) ↔ 상세(매수/매도) ↔ 필터 패널.
        //  · 목록 첫 행에서 W → 툴바,  툴바에서 S → 목록
        //  · 목록에서 D/Space → 상세,  상세 왼쪽 끝에서 A → 목록
        //  · 툴바 '필터'에서 Space → 필터 패널(태그·보유·정렬) 열고 진입,  패널 왼쪽 끝 A/닫힘 → 툴바
        private enum Zone { List, Detail, Toolbar, Filter }
        private Zone _zone = Zone.List;
        private CodeNavButton _filterNav;           // 툴바 버튼 포커스 링(정렬은 필터 패널로 들어갔다)
        private int _toolbarIndex = 1;              // 1=필터 (0번 자리였던 정렬은 패널로 이동)
        private int _keyGuardFrame = -1;            // 이 프레임엔 키 입력 무시(패널→툴바 전환 프레임의 A 중복 방지)

        // 정렬·태그·보유 필터는 전부 filterPanel이 소유한다(패널 안 칩으로 통일).
        private bool _controlsHooked;
        private string _searchQuery = string.Empty; // 종목명 부분일치(대소문자 무시). 비면 전체

        private SortMode CurrentSort =>
            SortCycle[filterPanel != null ? Mathf.Clamp(filterPanel.SortIndex, 0, SortCycle.Length - 1) : 0];

        /// <summary>필터 패널의 정렬 섹션에 넣을 칩 라벨(SortCycle 순서 = SortIndex).</summary>
        private static string[] BuildSortLabels()
        {
            var labels = new string[SortCycle.Length];
            for (int i = 0; i < SortCycle.Length; i++) labels[i] = GetSortLabel(SortCycle[i]);
            return labels;
        }

        public void Build()
        {
            var cm = StockGameManager.Instance?.CompanyManager;
            if (cm == null || itemPrefab == null || container == null) { ClearRows(); return; }

            EnsureControls(cm);
            EnsureContentLayout();
            RebuildRows(cm);

            // 열릴 때 키보드 포커스는 항상 목록에서 시작(이전 세션의 툴바/필터 잔여 포커스 정리).
            _zone = Zone.List;
            UpdateToolbarFocus();

            // 스크롤 입력은 Update()에서 직접 처리 — ScrollRect 자체 휠 스크롤 비활성.
            // 감도 저장은 최초 1회만(재빌드 때 0을 원본으로 덮어쓰지 않도록).
            if (scrollRect && !_scrollSaved)
            {
                _savedScrollSensitivity = scrollRect.scrollSensitivity;
                scrollRect.scrollSensitivity = 0f;
                _scrollSaved = true;
            }
        }

        /// <summary>현재 정렬/필터를 적용해 행만 다시 생성한다(드롭다운 옵션은 EnsureControls가 유지).</summary>
        private void RebuildRows(CompanyManager cm)
        {
            ClearRows();

            var companies = GetSortedFiltered(cm);

            // 제목을 '전체 종목' ↔ '보유 종목'으로 갈아 끼운다(종목 수 표시는 쓰지 않는다).
            UpdateListHeader(filterPanel != null && filterPanel.OnlyHoldings);
            foreach (var c in companies)
            {
                var go = Instantiate(itemPrefab, container);
                EnsureRowHeight(go);
                var item = go.GetComponent<StockListItemUI>();
                if (item == null) continue;
                item.Bind(c, OnSelect);
                _items.Add(item);
            }

            // 선택된 종목이 없으면 첫 종목을 자동 선택. 필터로 걸러진 경우 기존 선택(상세)은 유지한다.
            if (string.IsNullOrEmpty(_selectedId) && companies.Count > 0)
                OnSelect(companies[0]);
            else
                UpdateSelectionVisual();
        }

        // ===================================================
        // 정렬 / 필터
        // ===================================================

        /// <summary>필터를 적용하고 정렬한 종목 리스트를 만든다(원본은 건드리지 않음).
        /// 포함 태그는 OR(하나라도 가지면 통과, 비면 전체 통과), 제외 태그는 AND NOT(하나라도 가지면 탈락).
        /// 제외가 포함보다 우선한다.</summary>
        private List<CompanyData> GetSortedFiltered(CompanyManager cm)
        {
            var tags = filterPanel != null ? filterPanel.SelectedTags : null;   // 비면 전체
            var excluded = filterPanel != null ? filterPanel.ExcludedTags : null;
            bool hasTagFilter = tags != null && tags.Count > 0;
            bool hasExcludeFilter = excluded != null && excluded.Count > 0;
            bool onlyHoldings = filterPanel != null && filterPanel.OnlyHoldings;
            var pm = onlyHoldings ? StockGameManager.Instance?.PortfolioManager : null;
            bool hasSearch = !string.IsNullOrEmpty(_searchQuery);

            var src = cm.GetUnlockedCompanies();   // 잠긴 티어 종목은 리스트에 아예 뜨지 않는다
            var list = new List<CompanyData>(src.Count);
            foreach (var c in src)
            {
                if (hasExcludeFilter && HasAnyTag(c, excluded)) continue;
                if (hasTagFilter && !HasAnyTag(c, tags)) continue;
                // 보유중: 보유 주수가 1주 이상인 종목만 (태그·검색과 AND)
                if (onlyHoldings && (pm?.GetHolding(c.Id)?.Shares ?? 0) <= 0) continue;
                if (hasSearch && !MatchesSearch(c, _searchQuery)) continue;
                list.Add(c);
            }
            ApplySort(list, CurrentSort);
            return list;
        }

        /// <summary>정렬 모드에 따라 정렬한다. Default는 원본(ListingOrder) 순서를 유지하므로 정렬하지 않는다.
        /// 동점은 ListingOrder로 안정 처리한다.</summary>
        private static void ApplySort(List<CompanyData> list, SortMode mode)
        {
            switch (mode)
            {
                case SortMode.PriceDesc:
                    list.Sort((a, b) => { int r = b.CurrentPrice.CompareTo(a.CurrentPrice); return r != 0 ? r : a.ListingOrder.CompareTo(b.ListingOrder); });
                    break;
                case SortMode.PriceAsc:
                    list.Sort((a, b) => { int r = a.CurrentPrice.CompareTo(b.CurrentPrice); return r != 0 ? r : a.ListingOrder.CompareTo(b.ListingOrder); });
                    break;
                case SortMode.ChangeDesc:
                    list.Sort((a, b) => { int r = StockListItemUI.GetChangePercent(b).CompareTo(StockListItemUI.GetChangePercent(a)); return r != 0 ? r : a.ListingOrder.CompareTo(b.ListingOrder); });
                    break;
                case SortMode.NameAsc:
                case SortMode.Default:
                    // 기본순 = 종목명 가나다(ㄱㄴㄷ)순. 한글 음절은 유니코드상 이미 ㄱㄴㄷ으로 배열되어
                    // CurrentCulture 비교가 자연스러운 가나다 정렬을 준다. 동점은 ListingOrder로 안정 처리.
                    list.Sort((a, b) => { int r = string.Compare(a.GetLocalizedName(), b.GetLocalizedName(), StringComparison.CurrentCulture); return r != 0 ? r : a.ListingOrder.CompareTo(b.ListingOrder); });
                    break;
            }
        }

        /// <summary>주어진 태그 중 <b>하나라도</b> 가지고 있으면 true(OR 매칭).
        /// 포함 필터(고를수록 넓어짐)와 제외 필터(하나라도 걸리면 탈락) 양쪽에서 쓴다.</summary>
        private static bool HasAnyTag(CompanyData c, IReadOnlyCollection<string> tags)
        {
            if (tags == null || tags.Count == 0) return false;
            if (c?.Tags == null) return false;
            foreach (var want in tags)
                if (c.HasTag(want)) return true;
            return false;
        }

        // 목록 제목 지역화 키. 보유중 필터 on/off로 갈아 끼운다(LocalizedTextUI가 언어 전환도 처리).
        private const string ListHeaderKey = "ui_stock_list_header";
        private const string ListHeaderHoldingsKey = "ui_stock_list_header_holdings";

        /// <summary>목록 제목을 현재 필터 상태에 맞춰 '전체 종목' ↔ '보유 종목'으로 바꾼다.</summary>
        private void UpdateListHeader(bool onlyHoldings)
        {
            var label = ResolveListHeader();
            if (label == null) return;

            string key = onlyHoldings ? ListHeaderHoldingsKey : ListHeaderKey;
            if (label.localizationKey == key) return;
            label.localizationKey = key;
            label.UpdateText();
        }

        /// <summary>인스펙터에 헤더가 비어 있으면 자식에서 'ui_stock_list_header' 키를 가진 라벨을 찾아 캐시한다.</summary>
        private LocalizedTextUI ResolveListHeader()
        {
            if (listHeaderLabel != null) return listHeaderLabel;

            foreach (var l in GetComponentsInChildren<LocalizedTextUI>(true))
            {
                if (l == null || string.IsNullOrEmpty(l.localizationKey)) continue;
                if (l.localizationKey == ListHeaderKey || l.localizationKey == ListHeaderHoldingsKey)
                {
                    listHeaderLabel = l;
                    break;
                }
            }
            return listHeaderLabel;
        }

        /// <summary>검색어 일치 판정 — 표시 이름(현재 언어) 또는 종목 id의 부분일치(대소문자 무시).
        /// 공백은 무시해 띄어 써도 찾히게 한다.</summary>
        private static bool MatchesSearch(CompanyData c, string query)
        {
            if (c == null) return false;
            string q = Normalize(query);
            if (q.Length == 0) return true;
            return Normalize(c.GetLocalizedName()).Contains(q) || Normalize(c.Id).Contains(q);
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace(" ", string.Empty).ToLowerInvariant();
        }

        /// <summary>검색어가 바뀌면 목록을 다시 만든다(입력 필드 onValueChanged가 부른다).</summary>
        private void OnSearchChanged(string text)
        {
            string q = text ?? string.Empty;
            if (q == _searchQuery) return;
            _searchQuery = q;
            var cm = StockGameManager.Instance?.CompanyManager;
            if (cm != null) RebuildRows(cm);
        }

        /// <summary>마켓에 들어올 때마다 목록을 기본 상태로 되돌린다 — 태그 선택 없음([전체] 켜짐),
        /// 보유중 꺼짐, 검색어 없음. 지난 방문의 필터가 남아 목록이 비어 보이는 일을 막는다.</summary>
        public void ResetFilters()
        {
            ClearSearch();
            if (filterPanel != null) filterPanel.ResetFilters();
        }

        /// <summary>'보유중' 필터를 켜고 끈다(보유탭에서 종목을 클릭해 넘어올 때 사용).</summary>
        public void SetHoldingsFilter(bool on)
        {
            if (filterPanel == null) return;
            filterPanel.SetOnlyHoldings(on); // 값이 바뀌면 OnFilterChanged로 목록이 다시 만들어진다
        }

        /// <summary>검색어를 코드에서 비운다(다른 탭에서 넘어올 때 잔여 검색으로 목록이 비어 보이지 않게).</summary>
        public void ClearSearch()
        {
            if (searchInput) ClearInputSafely(searchInput);

            if (string.IsNullOrEmpty(_searchQuery)) return;
            _searchQuery = string.Empty;
            var cm = StockGameManager.Instance?.CompanyManager;
            if (cm != null) RebuildRows(cm);
        }

        /// <summary>검색창을 비운다. <b>포커스가 있으면 먼저 입력을 끝낸다</b> —
        /// 조합 중인 IME를 남긴 채 텍스트만 갈아끼우면 캐럿 인덱스가 어긋나
        /// 다음 타이핑에서 TMP가 예외를 뱉고 입력이 막힌다(<see cref="GuardCaretRange"/>와 같은 사고).</summary>
        private static void ClearInputSafely(TMP_InputField input)
        {
            if (input.isFocused) input.DeactivateInputField();
            input.SetTextWithoutNotify(string.Empty);
            input.stringPosition = 0;
            input.selectionStringAnchorPosition = 0;
            input.selectionStringFocusPosition = 0;
            input.caretPosition = 0;
            input.selectionAnchorPosition = 0;
            input.selectionFocusPosition = 0;
        }

        /// <summary>특정 종목을 목록에서 선택하고 상세에 펼친다(보유탭 → 시세탭 이동용).
        /// 현재 필터에 걸려 행이 없어도 상세는 펼친다.</summary>
        public void SelectCompany(string companyId)
        {
            var cm = StockGameManager.Instance?.CompanyManager;
            var company = cm?.GetCompany(companyId);
            if (company == null) return;

            _zone = Zone.List;
            UpdateToolbarFocus();
            OnSelect(company);
        }

        /// <summary>정렬 버튼·깔때기 버튼 리스너를 최초 1회 연결하고, 필터 패널을 설정한다.</summary>
        private void EnsureControls(CompanyManager cm)
        {
            if (!_controlsHooked)
            {
                // 정렬은 필터 패널 안 칩으로 옮겼다 — 툴바 버튼은 숨기고 참조도 놓아
                // 키보드 포커스가 안 보이는 버튼에 걸리지 않게 한다.
                if (sortButton)
                {
                    sortButton.gameObject.SetActive(false);
                    sortButton = null;
                }
                if (filterButton && filterPanel)
                {
                    filterButton.onClick.AddListener(filterPanel.Toggle);
                    var funnelRT = (RectTransform)filterButton.transform;
                    filterPanel.SetIgnoreTarget(funnelRT); // 바깥클릭 닫힘에서 깔때기 제외
                    filterPanel.SetAnchor(funnelRT);       // 깔때기 오른쪽에 자동 배치(해상도 무관)
                    filterPanel.SetKeyboardExitHandler(OnFilterKeyboardExit); // 패널을 떠나면 툴바로 복귀
                    _filterNav = CodeNavButton.Attach(filterButton);
                    _filterNav?.SetNavFocus(false);
                }
                if (filterPanel) filterPanel.Configure(cm, OnFilterChanged, BuildSortLabels());
                _controlsHooked = true;
            }

            // 매번 호출해도 안전하다(리스너는 중복 등록되지 않는다). 열 때마다 다시 불러
            // 언어가 바뀌었을 때 폰트·플레이스홀더 문구가 따라오게 한다.
            EnsureSearchField();
        }

        /// <summary>검색 입력 필드를 준비한다. 인스펙터에 연결돼 있으면 그것을 쓰고,
        /// 비어 있으면 깔때기 버튼의 형제로 <b>오른쪽에</b> 코드로 만들어 붙인다
        /// (툴바는 레이아웃 그룹 없이 좌상단 앵커로 배치돼 있어 좌표를 직접 계산한다).</summary>
        private void EnsureSearchField()
        {
            if (searchInput == null)
            {
                if (!autoCreateSearchField) return; // 씬에서 직접 배치할 예정 — 코드가 끼어들지 않는다
                var anchorBtn = filterButton != null ? (RectTransform)filterButton.transform
                              : (sortButton != null ? (RectTransform)sortButton.transform : null);
                if (anchorBtn == null) return; // 붙일 기준이 없으면 검색 기능 없음
                searchInput = CreateSearchField(anchorBtn);
                if (searchInput == null) return;
            }

            ApplySearchTheme(searchInput);

            searchInput.onValueChanged.RemoveListener(OnSearchChanged);
            searchInput.onValueChanged.AddListener(OnSearchChanged);
        }

        /// <summary>씬에서 만든 검색창에 마켓 다크 팔레트(MarketTheme)와 언어별 폰트를 입힌다.
        /// 배치·크기는 건드리지 않는다 — 색·폰트·플레이스홀더 문구·입력 규칙만 코드가 소유해서
        /// 언어를 바꾸거나 팔레트를 손봐도 씬을 다시 만지지 않아도 되게 한다.</summary>
        private static void ApplySearchTheme(TMP_InputField input)
        {
            if (input == null) return;

            var font = LanguageManager.Instance != null ? LanguageManager.Instance.GetCurrentFont() : null;

            // 배경 — targetGraphic이 있으면 그것을, 없으면 루트 Image를 칠한다.
            var bg = input.targetGraphic as Image;
            if (bg == null) bg = input.GetComponent<Image>();
            if (bg != null) bg.color = Market.MarketTheme.PanelBgAlt;

            // 글자 크기는 검색창 높이에 비례시킨다 — 씬에서 박스를 키우면 글자도 따라 커진다.
            // (TMP 기본 프리팹의 14pt는 이 캔버스(1920 기준)에서 읽을 수 없을 만큼 작다)
            float boxH = ((RectTransform)input.transform).rect.height;
            float size = Mathf.Clamp(boxH * 0.5f, 16f, 40f);

            // 입력 글자
            var text = input.textComponent as TextMeshProUGUI;
            if (text != null)
            {
                text.color = Market.MarketTheme.TextPrimary;
                if (font != null) text.font = font;
                text.fontSize = size;
                text.alignment = TextAlignmentOptions.Left;
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }

            // 플레이스홀더 — 문구까지 지역화 키로 덮어쓴다(씬에 하드코딩된 한국어를 남기지 않는다).
            var ph = input.placeholder as TextMeshProUGUI;
            if (ph != null)
            {
                ph.color = Market.MarketTheme.TextDim;
                if (font != null) ph.font = font;
                ph.fontSize = size;
                ph.alignment = TextAlignmentOptions.Left;
                ph.textWrappingMode = TextWrappingModes.NoWrap;
                ph.text = StockLoc.L("ui_stock_search_placeholder", "종목 검색...");
            }

            // 커서·선택 영역도 팔레트로
            input.customCaretColor = true;
            input.caretColor = Market.MarketTheme.AccentCyan;
            input.caretWidth = 2;
            input.selectionColor = new Color(Market.MarketTheme.AccentCyan.r, Market.MarketTheme.AccentCyan.g,
                                             Market.MarketTheme.AccentCyan.b, 0.35f);

            // 입력 규칙 — 한 줄, 너무 길어지지 않게
            if (font != null) input.fontAsset = font;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 24;
        }

        /// <summary>깔때기 버튼 오른쪽에 검색 입력 필드를 코드로 만든다(마켓 다크 팔레트).</summary>
        private TMP_InputField CreateSearchField(RectTransform anchorBtn)
        {
            var parent = anchorBtn.parent as RectTransform;
            if (parent == null) return null;

            var font = LanguageManager.Instance != null ? LanguageManager.Instance.GetCurrentFont() : null;
            float h = anchorBtn.rect.height;

            var go = new GameObject("SearchField", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorBtn.anchorMin;
            rt.anchorMax = anchorBtn.anchorMax;
            rt.pivot = anchorBtn.pivot;
            rt.sizeDelta = new Vector2(searchWidth, h);
            // 깔때기 오른쪽 끝 + 간격에서 시작하도록 피벗을 보정해 위치를 잡는다.
            float leftEdge = anchorBtn.anchoredPosition.x + anchorBtn.rect.width * (1f - anchorBtn.pivot.x) + searchGapX;
            rt.anchoredPosition = new Vector2(leftEdge + searchWidth * rt.pivot.x, anchorBtn.anchoredPosition.y);

            var bg = go.GetComponent<Image>();
            bg.color = Market.MarketTheme.PanelBgAlt;

            var input = go.AddComponent<TMP_InputField>();

            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            var vp = (RectTransform)viewport.transform;
            vp.anchorMin = Vector2.zero;
            vp.anchorMax = Vector2.one;
            vp.offsetMin = new Vector2(16f, 8f);
            vp.offsetMax = new Vector2(-16f, -8f);

            // 색·문구는 EnsureSearchField의 ApplySearchTheme이 최종적으로 한 번 더 덮는다(씬 배치본과 동일 규칙).
            var text = CreateInputLabel(vp, "Text", font, Market.MarketTheme.TextPrimary);
            var placeholder = CreateInputLabel(vp, "Placeholder", font, Market.MarketTheme.TextDim);

            input.textViewport = vp;
            input.textComponent = text;
            input.placeholder = placeholder;
            if (font != null) input.fontAsset = font;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 24;
            input.SetTextWithoutNotify(string.Empty);
            return input;
        }

        private static TextMeshProUGUI CreateInputLabel(RectTransform parent, string name, TMP_FontAsset font, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontSize = 30f;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return tmp;
        }

        /// <summary>필터 패널에서 태그 선택이 바뀌면 호출된다 — 행만 다시 생성.</summary>
        private void OnFilterChanged()
        {
            var cm = StockGameManager.Instance?.CompanyManager;
            if (cm != null) RebuildRows(cm);
        }

        /// <summary>정렬 모드 → 지역화 버튼 텍스트. 키가 없어도 한국어 폴백으로 표시된다.</summary>
        private static string GetSortLabel(SortMode m)
        {
            switch (m)
            {
                case SortMode.PriceAsc:   return StockLoc.L("ui_stock_sort_price_asc", "가격 낮은순");
                case SortMode.PriceDesc:  return StockLoc.L("ui_stock_sort_price_desc", "가격 높은순");
                case SortMode.ChangeDesc: return StockLoc.L("ui_stock_sort_change_desc", "등락률순");
                case SortMode.NameAsc:    return StockLoc.L("ui_stock_sort_name", "이름순");
                default:                  return StockLoc.L("ui_stock_sort_default", "기본순");
            }
        }

        /// <summary>시세 변동 시 모든 행과 상세 패널 갱신.</summary>
        public void Refresh()
        {
            foreach (var it in _items) if (it) it.Refresh();
            if (detail) detail.Refresh();
        }

        private void OnSelect(CompanyData c)
        {
            if (c == null) return;
            _selectedId = c.Id;
            if (_zone == Zone.Detail) _zone = Zone.List; // 상세에 있었다면 선택 변경 시 목록으로
            if (detail) { detail.Show(c); detail.ClearKeyboard(); }
            UpdateSelectionVisual();
            ScrollToSelected();
        }

        private void UpdateSelectionVisual()
        {
            foreach (var it in _items) if (it) it.SetSelected(it.CompanyId == _selectedId);
        }

        // --- 휠 / W·S 입력 → 슬롯 단위 이동 ---
        // 종목이 0개여도(필터로 전부 걸러진 경우) 툴바·필터 키 조작은 살아 있어야 하므로 여기서 막지 않는다.
        // 목록 이동 자체는 HandleWheelInput/StepSelection이 각자 빈 목록을 가드한다.
        private void Update()
        {
            PollSearchInput();
            HandleWheelInput();
            HandleKeyInput();
        }

        /// <summary>검색창 내용이 바뀌었는지 매 프레임 확인해 즉시 목록에 반영한다.
        /// onValueChanged는 <b>확정된</b> 텍스트에만 오므로 한글은 다음 글자를 쳐야 검색되던 문제가 있었다 —
        /// 조합 중인 글자(<see cref="Input.compositionString"/>)까지 합쳐서 본다. 문자열 비교 한 번이라 비용은 무시할 수준.</summary>
        private void PollSearchInput()
        {
            if (searchInput == null) return;
            GuardCaretRange(searchInput);
            OnSearchChanged(CurrentSearchText()); // 값이 같으면 OnSearchChanged가 즉시 빠져나간다
        }

        /// <summary>확정 텍스트 + 지금 조합 중인 한글을 합친 '보이는 그대로'의 검색어.
        /// 조합 중 문자는 아직 <c>InputField.text</c>에 들어오지 않기 때문에 이렇게 합쳐야
        /// "데"를 치는 순간 바로 걸러진다. 조합이 확정되면 같은 문자열이 되므로 재생성이 두 번 일어나지 않는다.</summary>
        private string CurrentSearchText()
        {
            string committed = searchInput.text ?? string.Empty;
            if (!searchInput.isFocused) return committed;

            string composing = Input.compositionString;
            if (string.IsNullOrEmpty(composing)) return committed;

            int caret = Mathf.Clamp(searchInput.stringPosition, 0, committed.Length);
            return committed.Insert(caret, composing);
        }

        /// <summary>TMP_InputField의 캐럿·선택 인덱스가 실제 문자열 길이를 넘어서 있으면 되돌린다.
        /// 탭 전환·필터 이동 등으로 <b>포커스가 있는 상태에서 텍스트가 밖에서 바뀌면</b> 인덱스가 옛 길이를
        /// 가리킨 채 남고, 다음 타이핑에서 TMP 내부의 <c>String.Remove</c>가
        /// ArgumentOutOfRangeException으로 터지며 입력이 통째로 먹통이 된다. 그 상태를 매 프레임 값싸게 막는다.</summary>
        private static void GuardCaretRange(TMP_InputField input)
        {
            int len = (input.text ?? string.Empty).Length;
            if (input.stringPosition <= len && input.selectionStringAnchorPosition <= len
                && input.selectionStringFocusPosition <= len && input.caretPosition <= len) return;

            input.stringPosition = len;
            input.selectionStringAnchorPosition = len;
            input.selectionStringFocusPosition = len;
            input.caretPosition = len;
            input.selectionAnchorPosition = len;
            input.selectionFocusPosition = len;
        }

        private void HandleWheelInput()
        {
            if (StockTradeDialog.IsOpen) return;
            if (scrollRect == null || _items.Count <= 1) return;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f)) return;

            // 마우스가 ScrollRect 영역 위에 있을 때만 처리
            if (!IsPointerOverScrollRect()) return;

            // 스크롤 위 = 이전 슬롯, 스크롤 아래 = 다음 슬롯
            StepSelection(scroll > 0f ? -1 : +1);
        }

        /// <summary>구역별 키 처리. 필터 패널은 자체 Update(CodeSlotNavigator)에서 처리하므로 여기선 넘긴다.</summary>
        private void HandleKeyInput()
        {
            // 수량 팝업이 떠 있는 동안은 그쪽이 키를 전담한다(뒤에서 목록이 같이 움직이지 않게).
            if (StockTradeDialog.IsOpen) { _heldKey = KeyCode.None; return; }
            if (IsTypingInInputField()) { _heldKey = KeyCode.None; return; }
            if (Time.frameCount == _keyGuardFrame) return; // 구역 전환 프레임의 잔여 키 무시

            switch (_zone)
            {
                case Zone.Detail:  HandleDetailKeys();  return;
                case Zone.Toolbar: HandleToolbarKeys(); return;
                case Zone.Filter:  return; // StockFilterPanel이 담당
                default:           HandleListKeys();    return;
            }
        }

        /// <summary>상세 패널 포커스: A/D=버튼 이동, A 왼쪽 끝=목록 복귀, Space=실행(수량 팝업이 뜬다).
        /// 수량은 팝업(StockTradeDialog)이 담당하므로 여기서 W/S로 수량을 만지지 않는다.</summary>
        private void HandleDetailKeys()
        {
            if (detail == null || !detail.HasContent) { SetDetailFocus(false); return; }

            if (Input.GetKeyDown(KeyCode.Space)) { detail.ActivateKb(); return; }
            if (rightKey != KeyCode.None && Input.GetKeyDown(rightKey)) { detail.MoveKbHorizontal(+1); return; }
            if (leftKey != KeyCode.None && Input.GetKeyDown(leftKey))
            {
                if (!detail.MoveKbHorizontal(-1)) SetDetailFocus(false); // 왼쪽 끝 → 목록으로
            }
        }

        /// <summary>목록 포커스: W/S=슬롯 이동(첫 행에서 W=툴바), D/Space=상세로, 꾹 누르면 연속 이동.</summary>
        private void HandleListKeys()
        {
            // D 또는 Space로 상세 패널로 넘어간다
            bool goRight = (rightKey != KeyCode.None && Input.GetKeyDown(rightKey)) || Input.GetKeyDown(KeyCode.Space);
            if (goRight && detail != null && detail.HasContent)
            {
                _heldKey = KeyCode.None;
                SetDetailFocus(true);
                Market.MarketUISfx.Play(Market.MarketUISfx.Kind.Scroll);
                return;
            }

            // 첫 행에서 W → 상단 툴바(정렬·필터)로
            if (upKey != KeyCode.None && Input.GetKeyDown(upKey) && GetSelectedIndex() <= 0 && HasToolbar())
            {
                _heldKey = KeyCode.None;
                EnterToolbar();
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

        // ===================================================
        // 상단 툴바(정렬·필터) 키보드 조작
        // ===================================================

        /// <summary>툴바에 조작할 버튼이 하나라도 있는지(없으면 W로 진입하지 않는다).</summary>
        private bool HasToolbar() => ToolbarButtonExists(0) || ToolbarButtonExists(1);

        private bool ToolbarButtonExists(int idx) =>
            idx == 1 && filterButton != null && filterPanel != null;

        /// <summary>목록에서 툴바로 진입. 마지막 위치가 없는 버튼이면 존재하는 버튼으로 보정.</summary>
        private void EnterToolbar()
        {
            _zone = Zone.Toolbar;
            _toolbarIndex = ClampToolbarIndex(_toolbarIndex);
            UpdateToolbarFocus();
            Market.MarketUISfx.Play(Market.MarketUISfx.Kind.Scroll);
        }

        private void HandleToolbarKeys()
        {
            if (leftKey != KeyCode.None && Input.GetKeyDown(leftKey))   { MoveToolbar(-1); return; }
            if (rightKey != KeyCode.None && Input.GetKeyDown(rightKey)) { MoveToolbar(+1); return; }
            if (downKey != KeyCode.None && Input.GetKeyDown(downKey))   { ExitToolbarToList(); return; }
            if (Input.GetKeyDown(KeyCode.Space)) ActivateToolbar();
        }

        /// <summary>정렬 ↔ 필터 좌우 이동(없는 버튼은 건너뜀, 끝에선 제자리).</summary>
        private void MoveToolbar(int dir)
        {
            int next = _toolbarIndex + dir;
            while (next >= 0 && next <= 1 && !ToolbarButtonExists(next)) next += dir;
            if (next < 0 || next > 1 || !ToolbarButtonExists(next)) return; // 끝 — 순환하지 않음
            _toolbarIndex = next;
            UpdateToolbarFocus();
            Market.MarketUISfx.Play(Market.MarketUISfx.Kind.Scroll);
        }

        /// <summary>툴바에서 목록으로 복귀(S).</summary>
        private void ExitToolbarToList()
        {
            _zone = Zone.List;
            UpdateToolbarFocus(); // 링 끔
            Market.MarketUISfx.Play(Market.MarketUISfx.Kind.Scroll);
        }

        /// <summary>Space: 필터 패널을 열고 키보드로 진입한다(정렬 칩도 그 안에 있다).</summary>
        private void ActivateToolbar()
        {
            if (_toolbarIndex == 1 && filterButton != null && filterPanel != null)
            {
                _zone = Zone.Filter;
                UpdateToolbarFocus();        // Filter 구역이라 툴바 링은 모두 꺼진다
                filterPanel.SetOpen(true);
                filterPanel.EnterKeyboard(); // 첫 칩에 커서(진입 프레임의 Space는 패널이 무시)
            }
        }

        private int ClampToolbarIndex(int idx)
        {
            if (ToolbarButtonExists(idx)) return idx;
            if (ToolbarButtonExists(1)) return 1;
            return 0;
        }

        /// <summary>현재 툴바 인덱스에 맞춰 포커스 링을 켜고 끈다(Toolbar 구역일 때만 켜짐).</summary>
        private void UpdateToolbarFocus()
        {
            bool inToolbar = _zone == Zone.Toolbar;
            if (_filterNav) _filterNav.SetNavFocus(inToolbar && _toolbarIndex == 1);
        }

        /// <summary>필터 패널이 키보드로 닫히면(왼쪽 끝 A·바깥 클릭) 툴바 '필터'로 복귀한다.</summary>
        private void OnFilterKeyboardExit()
        {
            if (_zone != Zone.Filter) return;
            _zone = Zone.Toolbar;
            _toolbarIndex = ToolbarButtonExists(1) ? 1 : ClampToolbarIndex(1);
            UpdateToolbarFocus();
            _keyGuardFrame = Time.frameCount; // 패널을 닫은 그 프레임의 A가 툴바에서 또 처리되지 않게
        }

        /// <summary>선택을 delta칸 옮긴다(범위 밖이면 아무것도 하지 않음).</summary>
        private void StepSelection(int delta)
        {
            if (_items.Count == 0) return; // 빈 목록 가드(필터로 전부 걸러진 경우)
            int curIdx = GetSelectedIndex();
            if (curIdx < 0) curIdx = 0;

            int newIdx = Mathf.Clamp(curIdx + delta, 0, _items.Count - 1);
            if (newIdx == curIdx) return;

            var cm = StockGameManager.Instance?.CompanyManager;
            var company = cm?.GetCompany(_items[newIdx].CompanyId);
            if (company == null) return;

            Market.MarketUISfx.Play(Market.MarketUISfx.Kind.Scroll); // 슬롯 이동 리플 "차라라락"
            OnSelect(company);
        }

        /// <summary>우측 상세 패널(−/+/매수/매도)로 포커스를 옮기거나 목록으로 되돌린다.</summary>
        private void SetDetailFocus(bool on)
        {
            _zone = on ? Zone.Detail : Zone.List;
            if (detail == null) return;
            if (on) detail.EnterKeyboard();
            else detail.ClearKeyboard();
        }

        /// <summary>TMP 입력필드에 포커스가 있으면 W/S를 먹지 않게 한다(수량 타이핑 보호).</summary>
        private static bool IsTypingInInputField()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            var field = go.GetComponent<TMP_InputField>();
            return field != null && field.isFocused;
        }

        private int GetSelectedIndex()
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i] && _items[i].CompanyId == _selectedId) return i;
            return -1;
        }

        // 선택된 슬롯이 뷰포트 안에 보이도록 스크롤 위치 조정
        private void ScrollToSelected()
        {
            if (scrollRect == null || scrollRect.content == null || _items.Count <= 1) return;
            int idx = GetSelectedIndex();
            if (idx < 0) return;

            float t = (float)idx / (_items.Count - 1);
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
            // ScrollRect 원래 감도 복원
            if (scrollRect) scrollRect.scrollSensitivity = _savedScrollSensitivity;
        }

        private void ClearRows()
        {
            foreach (var it in _items) if (it) Destroy(it.gameObject);
            _items.Clear();
        }

        // ===================================================
        // Content 자동 사이징 — 종목 수 × rowHeight 로 딱 맞게
        // ===================================================

        /// <summary>Content(container)에 VerticalLayoutGroup + ContentSizeFitter(세로=Preferred)를 보장한다.
        /// 이 조합 덕분에 Content 높이가 (행 수 × rowHeight)로 자동 확정된다 — 위아래 남는 칸 없음.
        /// childForceExpandHeight=false 로 행을 세로로 늘리지 않아, 종목이 적어도 각 행 높이는 rowHeight 그대로다.</summary>
        private void EnsureContentLayout()
        {
            if (container == null) return;

            var vlg = container.GetComponent<VerticalLayoutGroup>();
            bool created = vlg == null;
            if (created) vlg = container.gameObject.AddComponent<VerticalLayoutGroup>();

            // 정확성에 필수인 두 필드는 항상 강제(기존 씬 셋업이 어긋나 있어도 붕괴 방지).
            vlg.childControlHeight = true;      // 행 높이를 LayoutElement에서 읽음
            vlg.childForceExpandHeight = false; // 남는 공간이 있어도 행을 늘리지 않음

            if (created)
            {
                vlg.childControlWidth = true;
                vlg.childForceExpandWidth = true; // 행은 가로로 뷰포트 폭에 맞춤
                vlg.spacing = 0f;
                vlg.padding = new RectOffset(0, 0, 0, 0);
                vlg.childAlignment = TextAnchor.UpperCenter;
            }

            var fitter = container.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = container.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; // 높이 = 내용에 딱 맞게
            if (created) fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        /// <summary>행 프리팹에 LayoutElement로 min=preferred=rowHeight 를 박아, 종목 수와 무관하게
        /// 행 높이가 rowHeight로 고정되게 한다(240보다 작아져 UI가 깨지는 상황 원천 차단).</summary>
        private void EnsureRowHeight(GameObject go)
        {
            if (go == null || rowHeight <= 0f) return;
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minHeight = rowHeight;
            le.preferredHeight = rowHeight;
            le.flexibleHeight = 0f; // 남는 공간에도 늘어나지 않음
        }
    }
}

