// @tags: stock, ui, filter, tag, chip, funnel, panel, include, exclude
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Stock.Data;
using Stock.Systems;
using Market;

namespace Stock.UI
{
    /// <summary>
    /// 깔때기(필터) 아이콘 오른쪽에 뜨는 태그 선택 패널. 전부 코드 생성(프리팹·스프라이트 불필요).
    /// 종목 분류는 태그로 통일되어 있어 섹터 섹션은 없다.
    ///
    /// 칩은 <b>3단계 순환</b>: 아무것도 아님 → <b>포함</b>(하이라이트) → <b>제외</b>(취소선) → 해제.
    /// - 포함 태그는 <b>OR</b>: 하나라도 가지고 있으면 남는다(고를수록 넓어짐).
    /// - 제외 태그는 <b>AND NOT</b>: 하나라도 가지고 있으면 목록에서 빠진다(포함보다 우선).
    ///
    /// 태그 섹션 맨 앞에는 <b>[전체]</b>(태그 선택 해제) 칩이 있다.
    ///
    /// 패널은 위에서 아래로 <b>보유 → 정렬 → 태그</b> 순이다.
    /// 앞의 둘은 칩이 몇 개뿐이라 항상 같은 자리에 고정돼 있고, 개수가 종목 데이터에 따라 늘어나
    /// 여러 줄로 흐르는 태그를 맨 아래에 둔다 — 태그가 늘어도 위 두 섹션의 위치가 흔들리지 않는다.
    ///
    /// <b>[보유중]</b>은 태그가 아니므로 태그 칩과 섞지 않는다 — 구분선으로 갈린 <b>독립 섹션</b>이고,
    /// 3단계 순환 없이 on/off이며 태그 필터와 AND로 걸린다. 보유탭에서 종목을 클릭해 넘어올 때
    /// <see cref="SetOnlyHoldings"/>로 코드에서 켜진다.
    ///
    /// <b>정렬</b>은 하나만 고르는 라디오형이다(칩 목록·라벨은 <see cref="StockListUI"/>가 넘겨준다).
    ///
    /// 사용: 패널 루트 빈 오브젝트에 이 컴포넌트를 붙이고(깔때기 아이콘 오른쪽에 배치),
    ///       <see cref="StockListUI"/>의 filterPanel에 연결한다. StockListUI가 Configure/Toggle을 호출한다.
    /// UI는 처음 열릴 때 1회 생성된다(레이아웃 계산은 활성 상태여야 하므로 지연 생성).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class StockFilterPanel : MonoBehaviour
    {
        [Header("스타일")]
        [Tooltip("비우면 TMP 기본 폰트")]
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("섹션 헤더('태그') 표시 여부")]
        [SerializeField] private bool showHeaders = true;

        // ── 크기: 코드에서 강제(직렬화 안 함 → 인스펙터에 저장된 옛 값 무시, Reset 없이 Play만 하면 반영).
        //    크기를 바꾸려면 아래 숫자를 직접 수정한다. ──
        private float fontSize = 28f;      // 칩 글자 크기 (칩 높이·헤더가 여기에 연동)
        private int paddingX = 20;         // 칩 좌우 안쪽 여백(px)
        private int paddingY = 10;         // 칩 상하 안쪽 여백(px)
        private float chipHSpacing = 10f;  // 칩 사이 가로 간격(px)
        private float chipVSpacing = 12f;  // 줄바꿈 시 세로 간격(px)
        private int cornerRadius = 20;     // 칩 모서리 반경(px)
        private int panelPadding = 18;     // 패널 안쪽 여백(px)
        private float sectionSpacing = 16f;// 섹션(헤더/칩) 사이 세로 간격(px)
        private float fallbackWidth = 600f;// 패널 폭을 못 읽을 때 줄바꿈에 쓸 폭(px)
        // 태그 배경 틴트 강도(0=완전 다크, 1=액센트 원색). 높일수록 배경에 태그 색이 진하게 배어
        // "덜 투명한" 칩이 된다. 사이즈들처럼 코드 강제 → 씬 인스펙터에 저장된 옛 값을 무시한다.
        private float tagBgTint = 0.42f;

        [Header("태그별 색상 (TagChipList와 동일 규칙 — 같은 태그=같은 색)")]
        [Tooltip("[전체] 같은 컨트롤 칩 배경(중립·명확히 보이는 색). 태그의 알록달록함과 구분")]
        [UnityEngine.Serialization.FormerlySerializedAs("sectorChipColor")]
        [SerializeField] private Color neutralChipColor = MarketTheme.PanelBorder; // #243043
        [Header("제외(취소선) 상태 — 눈에 확 띄게")]
        [Tooltip("제외 칩의 글자·취소선 색. 기본은 하락 레드")]
        [SerializeField] private Color excludedTextColor = MarketTheme.Down;       // #F85149
        [Tooltip("제외 칩의 배경색. 원래 태그 색을 지우고 어두운 레드 톤으로 깔아 '꺼진' 느낌을 준다")]
        [SerializeField] private Color excludedChipColor = new Color(0.243f, 0.086f, 0.098f); // #3E1619
        [Tooltip("제외 칩 전체 투명도(0.5~1). 낮출수록 죽은 칩처럼 보인다")]
        [Range(0.3f, 1f)]
        [SerializeField] private float excludedAlpha = 0.85f;

        // 상태
        private CompanyManager _cm;
        private Action _onChanged;
        private bool _built;

        private readonly HashSet<string> _selectedTags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase); // 포함(OR). 비면 전체
        private readonly HashSet<string> _excludedTags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase); // 제외(AND NOT)

        // 값 목록 (Configure에서 종목 데이터로부터 수집)
        private readonly List<string> _tagValues = new List<string>();

        // 버튼·라벨 참조 (하이라이트 갱신용) — 모두 _tagValues와 1:1
        private readonly List<Button> _tagButtons = new List<Button>();
        private readonly List<TextMeshProUGUI> _tagLabels = new List<TextMeshProUGUI>();
        private readonly List<Color> _tagLabelColors = new List<Color>(); // 원래 글자색(제외 해제 시 복원)
        private readonly List<Image> _tagImages = new List<Image>();
        private readonly List<Color> _tagChipColors = new List<Color>();  // 원래 배경색(제외 해제 시 복원)
        private Button _allTagButton;
        private Button _holdingsButton;   // '보유중' — 태그가 아니라 보유 여부로 거르는 컨트롤 칩
        private bool _onlyHoldings;

        private Sprite _chipSprite;
        private RectTransform _tagFlow;
        private LayoutElement _tagFlowLE;
        private RectTransform _holdingsFlow;      // 태그와 분리된 '보유' 섹션
        private LayoutElement _holdingsFlowLE;
        private RectTransform _sortFlow;          // 맨 아래 '정렬' 섹션
        private LayoutElement _sortFlowLE;
        private float dividerHeight = 2f;         // 섹션 구분선 두께(px)

        // 정렬 — 라디오형(항상 하나 선택). 라벨은 StockListUI가 Configure로 넘긴다.
        private readonly List<string> _sortLabels = new List<string>();
        private readonly List<Button> _sortButtons = new List<Button>();
        private int _sortIndex;

        // ==== 키보드(WASD+Space) 조작 ====
        // 칩(전체 + 태그)을 좌표 기준으로 돌아다니는 공용 내비게이터. Space는 칩 onClick(=상태 순환)을 그대로 호출.
        private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();
        private readonly List<ICodeNavItem> _navItems = new List<ICodeNavItem>();
        private bool _navReady;
        private Action _onKeyboardExit; // 패널을 떠날 때(왼쪽 끝 A·바깥 클릭 닫힘) → 상단 툴바로 복귀

        // ==== 외부 API ====

        /// <summary>포함 태그 집합(대소문자 무시). 비어 있으면 전체 통과. 여러 개면 OR.</summary>
        public IReadOnlyCollection<string> SelectedTags => _selectedTags;

        /// <summary>제외 태그 집합(대소문자 무시). 하나라도 가진 종목은 목록에서 빠진다.</summary>
        public IReadOnlyCollection<string> ExcludedTags => _excludedTags;

        /// <summary>선택된 정렬 인덱스. <see cref="StockListUI"/>가 Configure로 넘긴 라벨 목록의 인덱스와 1:1.</summary>
        public int SortIndex => _sortIndex;

        /// <summary>'보유중' 필터. 켜면 지금 보유 중인 종목만 목록에 남는다(태그 필터와 AND).</summary>
        public bool OnlyHoldings => _onlyHoldings;

        /// <summary>'보유중' 필터를 코드에서 켜고 끈다(보유탭에서 종목을 클릭해 넘어올 때 사용).
        /// 값이 실제로 바뀌었을 때만 목록 갱신 콜백을 부른다.</summary>
        public void SetOnlyHoldings(bool on)
        {
            if (_onlyHoldings == on) return;
            _onlyHoldings = on;
            RefreshTagHighlights();
            _onChanged?.Invoke();
        }

        /// <summary>필터를 초기 상태(태그 선택 없음 = [전체] 켜짐, 보유중 꺼짐)로 되돌린다.
        /// 마켓 씬에 들어올 때마다 호출돼, 지난 방문의 필터가 남아 목록이 비어 보이는 일을 막는다.
        /// 바뀐 게 없으면 콜백을 부르지 않는다(불필요한 목록 재생성 방지).</summary>
        public void ResetFilters()
        {
            bool changed = _selectedTags.Count > 0 || _excludedTags.Count > 0 || _onlyHoldings || _sortIndex != 0;
            _selectedTags.Clear();
            _excludedTags.Clear();
            _onlyHoldings = false;
            _sortIndex = 0;
            RefreshTagHighlights();
            if (changed) _onChanged?.Invoke();
        }

        /// <summary>종목 데이터에서 태그 목록을 수집하고 콜백을 등록한다(1회). 패널은 숨긴 상태로 시작.
        /// <paramref name="sortLabels"/>는 정렬 칩으로 그대로 쓰인다(순서 = <see cref="SortIndex"/>).</summary>
        public void Configure(CompanyManager cm, Action onChanged, IReadOnlyList<string> sortLabels)
        {
            _cm = cm;
            _onChanged = onChanged;
            _sortLabels.Clear();
            if (sortLabels != null) foreach (var l in sortLabels) _sortLabels.Add(l);
            CollectValues(cm);
            if (!_built) gameObject.SetActive(false); // 아직 안 지었으면 깔때기 클릭 전까지 숨김
        }

        public bool IsOpen => gameObject.activeSelf;

        public void Toggle() => SetOpen(!gameObject.activeSelf);

        public void SetOpen(bool open)
        {
            bool wasKeyboard = KeyboardActive;
            gameObject.SetActive(open);
            if (open)
            {
                EnsureBuilt();
                PositionNextToAnchor(); // 열 때마다 재계산 → 해상도·창 크기가 바뀌어도 항상 깔때기 옆
            }
            else if (wasKeyboard)
            {
                ClearKeyboard();        // 키보드로 열려 있던 패널이 닫히면 포커스를 놓고
                _onKeyboardExit?.Invoke(); // 소유자에게 툴바 복귀를 알린다
            }
        }

        /// <summary>바깥 클릭 닫힘 감지에서 제외할 대상(깔때기 버튼 등). 이 위에서 눌러도 패널이 닫히지 않는다.</summary>
        public void SetIgnoreTarget(RectTransform rt) => _ignoreRect = rt;
        private RectTransform _ignoreRect;

        /// <summary>이 패널을 오른쪽에 자동으로 붙일 깔때기(필터) 버튼. 지정하면 여는 순간
        /// 버튼 월드 좌표를 기준으로 위치를 잡으므로 해상도·앵커 설정과 무관하게 항상 버튼 바로 옆에 뜬다.</summary>
        public void SetAnchor(RectTransform funnelButton) => _anchorRect = funnelButton;
        private RectTransform _anchorRect;

        // 자동 배치 튜닝 (캔버스 로컬 px). 크기와 마찬가지로 코드에서 강제한다.
        private float anchorGapX = 8f;    // 버튼과 패널 사이 가로 간격
        private float anchorOffsetY = 0f; // 버튼 상단 대비 패널 상단 세로 보정(+면 아래로)
        private float screenMargin = 8f;  // 화면(캔버스) 가장자리 최소 여백

        /// <summary>패널을 키보드로 떠날 때(왼쪽 끝에서 A, 또는 바깥 클릭으로 닫힘) 호출될 복귀 핸들러.
        /// 소유자(StockListUI)는 여기서 포커스를 상단 툴바(깔때기 버튼)로 되돌린다.</summary>
        public void SetKeyboardExitHandler(Action onExit) => _onKeyboardExit = onExit;

        /// <summary>키보드 포커스가 이 패널에 있는지(툴바가 입력을 넘길지 판단용).</summary>
        public bool KeyboardActive { get; private set; }

        /// <summary>상단 툴바에서 이 패널로 키보드 포커스를 넘긴다 — 첫 칩에 커서를 놓는다.
        /// 반드시 열려 있는(SetOpen(true)) 상태에서 호출한다.</summary>
        public void EnterKeyboard()
        {
            EnsureBuilt();
            EnsureNav();
            KeyboardActive = true;
            _nav.Begin();
            if (_navItems.Count > 0) _nav.FocusItem(_navItems[0], scrollIntoView: false);
        }

        /// <summary>이 패널의 키보드 포커스를 해제한다(툴바 복귀·닫기 시).</summary>
        public void ClearKeyboard()
        {
            KeyboardActive = false;
            _nav.End();
        }

        // 열려 있는 동안: 키보드 커서(WASD+Space) 처리 + 바깥 클릭 닫힘. (마켓은 timeScale=0이지만 Update/Input은 동작)
        private void Update()
        {
            if (KeyboardActive) _nav.Update(); // WASD 이동·Space=칩 상태 순환(칩 onClick 호출)

            if (!Input.GetMouseButtonDown(0)) return; // 이 프레임에 좌클릭이 눌렸을 때만
            Vector2 mp = Input.mousePosition;
            if (IsOver((RectTransform)transform, mp)) return; // 패널 안 클릭 → 유지
            if (_ignoreRect && IsOver(_ignoreRect, mp)) return; // 깔때기 클릭 → 토글이 처리
            SetOpen(false);
        }

        /// <summary>스크린 좌표가 해당 RectTransform 영역 안에 있는지(캔버스 렌더모드에 맞는 카메라로 판정).</summary>
        private static bool IsOver(RectTransform rt, Vector2 screenPos)
        {
            if (rt == null) return false;
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, cam);
        }

        // ==== 값 수집 ====

        private void CollectValues(CompanyManager cm)
        {
            _tagValues.Clear();
            if (cm == null) return;

            // 해금된 종목의 태그만 모은다 — 잠긴 종목 전용 태그가 필터에 먼저 노출되면 스포일러가 된다
            foreach (var c in cm.GetUnlockedCompanies())
            {
                if (c.Tags != null)
                    foreach (var tag in c.Tags)
                        if (!string.IsNullOrEmpty(tag) && !ContainsCI(_tagValues, tag))
                            _tagValues.Add(tag);
            }

            // 지역화된 표시 이름 기준으로 정렬해 언어별로 자연스럽게 읽히게 한다.
            // (한글은 유니코드상 가나다 순, 영문은 알파벳 순으로 정렬됨)
            _tagValues.Sort(
                (a, b) => string.Compare(StockLoc.Tag(a), StockLoc.Tag(b), StringComparison.CurrentCultureIgnoreCase));
        }

        private static bool ContainsCI(List<string> list, string v)
        {
            foreach (var s in list)
                if (s != null && string.Equals(s, v, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ==== UI 생성 (지연, 최초 열림 시 1회) ====

        private void EnsureBuilt()
        {
            if (_built) return;
            if (_cm == null) return; // Configure 전이면 생성 보류
            BuildUI();
            _built = true;
        }

        private void BuildUI()
        {
            var rootRT = (RectTransform)transform;

            // 배경
            var bg = GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            bg.sprite = GetChipSprite();
            bg.type = Image.Type.Sliced;
            bg.color = MarketTheme.PanelBg;

            // 세로 스택 (헤더 → 태그 칩). childAlignment=UpperLeft 이므로 VLG는 피벗과 무관하게
            // 박스의 '최상단'부터 아래로 자식을 채운다 → 콘텐츠가 항상 패널 위쪽부터 생성된다.
            var vlg = GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(panelPadding, panelPadding, panelPadding, panelPadding);
            vlg.spacing = sectionSpacing;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // 세로 자동축소는 쓰지 않는다. 세로 ContentSizeFitter는 피벗(가운데) 기준으로 높이를 줄여
            // 콘텐츠를 박스 중앙으로 끌어내려('아래에 생성'되는 것처럼) 보이게 하므로,
            // 패널은 씬에서 그린 박스 크기를 그대로 유지하고 VLG가 위에서부터 채우게 둔다.
            var fitter = GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            // 보유 섹션 — 목록 범위를 가장 크게 가르는 축이라 맨 위. 태그가 아니므로 구분선으로 갈라 둔다.
            if (showHeaders) CreateHeader(StockLoc.L("ui_stock_filter_holdings_header", "보유"));
            _holdingsFlow = CreateFlowArea("HoldingsFlow", out _holdingsFlowLE);
            BuildHoldingsButton();

            // 정렬 섹션 — 칩 개수가 고정이라 위쪽에 둬도 자리가 흔들리지 않는다.
            CreateDivider();
            if (showHeaders) CreateHeader(StockLoc.L("ui_stock_filter_sort_header", "정렬"));
            _sortFlow = CreateFlowArea("SortFlow", out _sortFlowLE);
            BuildSortButtons();

            // 태그 섹션 — 종목 데이터에 따라 개수가 늘어 여러 줄로 흐르므로 맨 아래.
            CreateDivider();
            if (showHeaders) CreateHeader(StockLoc.L("ui_stock_filter_tag_header", "태그"));
            _tagFlow = CreateFlowArea("TagFlow", out _tagFlowLE);
            BuildTagButtons();

            // 칩 크기는 생성 시 실측(GetPreferredValues)으로 이미 확정됨.
            // 1차 리빌드로 루트 폭 확정 → 수동 flow 배치(폭 초과 시 줄바꿈)·flow 높이 반영 →
            // 2차 리빌드로 VLG가 확정된 flow 높이로 위에서부터 재배치.
            LayoutRebuilder.ForceRebuildLayoutImmediate(rootRT);
            ReflowAll();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rootRT);

            // 패널 박스 세로를 콘텐츠에 딱 맞게 자동 조정(상단 모서리는 고정 → 위쪽부터 그대로, 아래만 늘고 줆).
            FitHeightToContent();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rootRT);

            RefreshTagHighlights();
        }

        /// <summary>패널 높이를 콘텐츠(헤더+태그 칩) 높이에 맞춰 코드에서 직접 조정한다.
        /// ContentSizeFitter(피벗 기준 축소로 아래로 내려앉던 문제)를 쓰지 않고, 상단 모서리 위치를
        /// 보존하며 세로 크기만 바꾼다 — 피벗값과 무관하게 '위쪽부터'가 유지된다.
        /// 세로 스트레치 앵커(높이가 부모에 종속)면 건드리지 않는다.</summary>
        private void FitHeightToContent()
        {
            var rt = (RectTransform)transform;
            if (!Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y)) return; // 스트레치면 스킵

            // 섹션 구성: [보유헤더] 보유flow 구분선 [정렬헤더] 정렬flow 구분선 [태그헤더] 태그flow
            int sections = (showHeaders ? 3 : 0) + 5;
            float total = panelPadding * 2f
                        + _tagFlowLE.preferredHeight
                        + _holdingsFlowLE.preferredHeight
                        + _sortFlowLE.preferredHeight
                        + dividerHeight * 2f
                        + sectionSpacing * (sections - 1);
            if (showHeaders) total += (fontSize + 4f) * 3f; // 헤더 3개(CreateHeader와 동일 높이)

            float oldH = rt.rect.height;
            float pivotToTop = 1f - rt.pivot.y;                  // 피벗→상단 모서리까지의 높이 비율
            float top = rt.anchoredPosition.y + pivotToTop * oldH; // 상단 모서리(부모 로컬 Y)
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, total);
            float newTop = rt.anchoredPosition.y + pivotToTop * total;
            rt.anchoredPosition += new Vector2(0f, top - newTop); // 상단 고정
        }

        /// <summary>패널을 깔때기 버튼 오른쪽에 자동으로 붙인다. 버튼의 <b>월드 좌표</b>(GetWorldCorners)를
        /// 기준으로 잡으므로 캔버스 스케일·해상도·창 크기가 달라져도 항상 버튼 바로 옆에 위치한다.
        /// 앵커/피벗을 좌상단 비신축으로 확정하되 현재 크기는 보존하고, 화면 밖으로 나가면 안쪽으로 민다.</summary>
        private void PositionNextToAnchor()
        {
            if (_anchorRect == null) return;
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;

            // 현재 크기 보존 → 좌상단 비신축 앵커/피벗으로 통일(위치 계산을 결정적으로 만든다).
            float w = rt.rect.width;
            float h = rt.rect.height;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);

            // 버튼 우상단(월드) = 패널 좌상단(피벗) 에 맞춘 뒤 로컬 px 간격만큼 민다.
            var corners = new Vector3[4]; // 0 BL, 1 TL, 2 TR, 3 BR
            _anchorRect.GetWorldCorners(corners);
            rt.position = corners[2];
            rt.anchoredPosition += new Vector2(anchorGapX, anchorOffsetY);

            ClampInsideCanvas(rt);
        }

        /// <summary>패널이 캔버스(화면) 밖으로 삐져나가면 안쪽으로 밀어 넣는다. 좌상단 피벗 기준.</summary>
        private void ClampInsideCanvas(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var bounds = (RectTransform)canvas.transform;

            // 캔버스 로컬 프레임에서 패널 좌상단 위치와 경계를 비교(둘 다 캔버스 피벗 기준이라 좌표계 일치).
            Vector3 tl = bounds.InverseTransformPoint(rt.position); // 패널 좌상단
            Rect b = bounds.rect;
            float w = rt.rect.width, h = rt.rect.height, m = screenMargin;

            if (tl.x + w > b.xMax - m) tl.x = b.xMax - m - w;
            if (tl.x < b.xMin + m)     tl.x = b.xMin + m;
            if (tl.y > b.yMax - m)     tl.y = b.yMax - m;
            if (tl.y - h < b.yMin + m) tl.y = b.yMin + m + h;

            rt.position = bounds.TransformPoint(tl);
        }

        private void ReflowAll()
        {
            // 줄바꿈 기준 폭은 패널 자체 폭에서 안쪽 여백을 뺀 값(= VLG가 flow를 늘리는 내부 폭과 동일).
            // flow.rect.width에 의존하면 리빌드 타이밍에 따라 0이 나올 수 있어 루트에서 직접 계산한다.
            float w = ((RectTransform)transform).rect.width - panelPadding * 2f;
            if (w <= 1f) w = fallbackWidth;
            _tagFlowLE.preferredHeight = LayoutFlow(_tagFlow, w);
            _holdingsFlowLE.preferredHeight = LayoutFlow(_holdingsFlow, w);
            _sortFlowLE.preferredHeight = LayoutFlow(_sortFlow, w);
        }

        /// <summary>flow 영역의 자식들을 좌→우, 폭 초과 시 다음 줄로 배치하고 총 높이를 반환한다.</summary>
        private float LayoutFlow(RectTransform area, float width)
        {
            if (width <= 1f) width = fallbackWidth;
            float x = 0f, y = 0f, rowH = 0f;
            for (int i = 0; i < area.childCount; i++)
            {
                var it = (RectTransform)area.GetChild(i);
                if (!it.gameObject.activeSelf) continue;
                Vector2 size = it.rect.size;
                if (x > 0f && x + size.x > width) { x = 0f; y += rowH + chipVSpacing; rowH = 0f; }
                it.anchorMin = it.anchorMax = new Vector2(0f, 1f);
                it.pivot = new Vector2(0f, 1f);
                it.anchoredPosition = new Vector2(x, -y);
                x += size.x + chipHSpacing;
                rowH = Mathf.Max(rowH, size.y);
            }
            return y + rowH;
        }

        private void CreateHeader(string text)
        {
            var go = new GameObject("Header", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(transform, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.text = text;
            label.fontSize = fontSize - 2f;
            label.color = MarketTheme.TextDim;
            label.alignment = TextAlignmentOptions.Left;
            label.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = fontSize + 4f;
            le.preferredHeight = fontSize + 4f;
        }

        /// <summary>태그 섹션과 보유 섹션을 시각적으로 가르는 가로 구분선.</summary>
        private void CreateDivider()
        {
            var go = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.color = MarketTheme.PanelBorder;
            img.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = dividerHeight;
            le.preferredHeight = dividerHeight;
        }

        /// <summary>수동 flow가 자식을 배치할 빈 영역. 폭은 VLG가 채우고, 높이는 LayoutElement로 지정한다.</summary>
        private RectTransform CreateFlowArea(string name, out LayoutElement le)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 40f; // 임시(측정 후 갱신)
            return rt;
        }

        private void BuildTagButtons()
        {
            _tagButtons.Clear();
            _tagLabels.Clear();
            _tagLabelColors.Clear();
            _tagImages.Clear();
            _tagChipColors.Clear();
            // 전체(태그 필터 해제) 칩 — 컨트롤이라 중립색
            _allTagButton = CreateChipButton(_tagFlow,
                StockLoc.L("ui_stock_filter_tag_all", "전체"), neutralChipColor,
                MarketTheme.TextPrimary, OnAllTagsClicked);

            foreach (var tag in _tagValues)
            {
                string t = tag;                       // 로직·매칭은 raw 태그 그대로
                ResolveTagColors(t, out Color bg, out Color fg); // 색상 해시도 raw 기준(언어 무관 동일 색)
                var btn = CreateChipButton(_tagFlow, StockLoc.Tag(t), bg, fg, () => OnTagClicked(t)); // 표시만 지역화
                _tagButtons.Add(btn);
                _tagLabels.Add(btn.GetComponentInChildren<TextMeshProUGUI>(true));
                _tagLabelColors.Add(fg);
                _tagImages.Add(btn.targetGraphic as Image);
                _tagChipColors.Add(bg);
            }
        }

        /// <summary>보유 섹션의 단일 칩. 태그와 섞이지 않도록 전용 flow에만 만든다.</summary>
        private void BuildHoldingsButton()
        {
            _holdingsButton = CreateChipButton(_holdingsFlow,
                StockLoc.L("ui_stock_filter_holdings", "보유중"), neutralChipColor,
                MarketTheme.TextPrimary, OnHoldingsClicked);
        }

        /// <summary>정렬 칩(라디오형). 라벨은 StockListUI가 넘긴 순서 그대로 = SortIndex.</summary>
        private void BuildSortButtons()
        {
            _sortButtons.Clear();
            for (int i = 0; i < _sortLabels.Count; i++)
            {
                int captured = i; // 클로저 캡처 주의 — 루프 변수를 그대로 쓰면 전부 마지막 값이 된다
                var btn = CreateChipButton(_sortFlow, _sortLabels[i], neutralChipColor,
                    MarketTheme.TextPrimary, () => OnSortClicked(captured));
                _sortButtons.Add(btn);
            }
        }

        /// <summary>정렬 선택. 이미 고른 것을 또 누르면 아무 일도 없다(라디오형이라 해제가 없다).</summary>
        private void OnSortClicked(int index)
        {
            if (index < 0 || index >= _sortLabels.Count) return;
            if (_sortIndex == index) return;
            _sortIndex = index;
            RefreshTagHighlights();
            _onChanged?.Invoke();
        }

        /// <summary>칩(전체 + 태그)에 WASD 커서 대상을 붙이고 내비게이터를 구성한다(1회).
        /// Space는 칩의 onClick(=상태 순환)을 그대로 부르므로 별도 실행 로직이 필요 없다.
        /// 왼쪽 끝에서 A → 소유자에게 툴바 복귀를 알린다.</summary>
        private void EnsureNav()
        {
            if (_navReady) return;
            if (!_built) return;

            _navItems.Clear();
            void AddNav(Button b)
            {
                if (b == null) return;
                var nav = CodeNavButton.Attach(b); // 코드 생성 포커스 링(기본 스킨)
                if (nav == null) return;
                nav.SetNavFocus(false);
                _navItems.Add(nav);
            }
            AddNav(_holdingsButton);               // 시각 순서(보유중 → 정렬 → 전체 → 태그들)와 동일하게 등록
            foreach (var b in _sortButtons) AddNav(b);
            AddNav(_allTagButton);
            foreach (var b in _tagButtons) AddNav(b);

            _nav.collect = list => { foreach (var it in _navItems) list.Add(it); };
            _nav.inLineOnly = true; // 좌우 이동이 다른 줄로 새지 않게 — 줄 왼쪽 끝 A는 곧장 onEdge로
            _nav.onEdge = dir => { if (dir.x < -0.5f) SetOpen(false); }; // 왼쪽 끝 A → 닫고 툴바로(닫힘이 복귀 통지)
            _navReady = true;
        }

        /// <summary>태그별 배경/텍스트 색. 색상환 해시(MarketTheme.TagAccent)로 배정해 태그가 많아도 잘 안 겹치고,
        /// TagChipList와 같은 함수를 써서 같은 태그면 상세 칩과 필터 칩 색이 일치한다. 배경은 은은한 틴트.</summary>
        private void ResolveTagColors(string tag, out Color bg, out Color fg)
        {
            Color accent = MarketTheme.TagAccent(tag);
            bg = Color.Lerp(MarketTheme.ChipBg, accent, tagBgTint); // 은은히 틴트된 다크 배경
            fg = accent;                                            // 선명한 액센트 텍스트
        }

        // ==== 클릭 핸들러 ====

        private void OnAllTagsClicked()
        {
            if (_selectedTags.Count == 0 && _excludedTags.Count == 0) return;
            _selectedTags.Clear();
            _excludedTags.Clear();
            RefreshTagHighlights();
            _onChanged?.Invoke();
        }

        /// <summary>'보유중' 토글. 태그 필터와 독립이며 AND로 함께 걸린다.</summary>
        private void OnHoldingsClicked()
        {
            _onlyHoldings = !_onlyHoldings;
            RefreshTagHighlights();
            _onChanged?.Invoke();
        }

        /// <summary>칩 3단계 순환: 해제 → 포함 → 제외 → 해제.</summary>
        private void OnTagClicked(string tag)
        {
            if (_selectedTags.Remove(tag)) _excludedTags.Add(tag); // 포함 → 제외
            else if (!_excludedTags.Remove(tag)) _selectedTags.Add(tag); // 해제 → 포함 (제외였으면 해제만)
            RefreshTagHighlights();
            _onChanged?.Invoke();
        }

        // ==== 하이라이트 ====

        /// <summary>칩 3단계 상태를 화면에 반영한다.
        /// 포함=<see cref="MarketSelectHighlight"/> 하이라이트(시안 배경),
        /// 제외=취소선(<c>&lt;s&gt;</c>) + 레드 글자 + 어두운 레드 배경 + 낮은 투명도, 해제=원래 색.
        /// 제외 스타일은 반드시 하이라이트 해제(Set(false) → 원래 색 복원) <b>뒤에</b> 덧칠해야 한다.</summary>
        private void RefreshTagHighlights()
        {
            if (_allTagButton)
                MarketSelectHighlight.Set(_allTagButton, _selectedTags.Count == 0 && _excludedTags.Count == 0);
            if (_holdingsButton)
                MarketSelectHighlight.Set(_holdingsButton, _onlyHoldings);
            for (int i = 0; i < _sortButtons.Count; i++)
                MarketSelectHighlight.Set(_sortButtons[i], i == _sortIndex);

            for (int i = 0; i < _tagButtons.Count; i++)
            {
                string raw = _tagValues[i];
                bool included = _selectedTags.Contains(raw);
                bool excluded = !included && _excludedTags.Contains(raw);

                MarketSelectHighlight.Set(_tagButtons[i], included);

                // 배경: 제외면 어두운 레드 톤 + 투명도를 낮춰 '꺼진 칩'으로 만든다.
                // (포함 상태 배경은 MarketSelectHighlight가 소유하므로 건드리지 않는다)
                var img = i < _tagImages.Count ? _tagImages[i] : null;
                if (img != null && !included)
                {
                    if (excluded)
                    {
                        var c = excludedChipColor; c.a *= excludedAlpha;
                        img.color = c;
                    }
                    else if (i < _tagChipColors.Count) img.color = _tagChipColors[i];
                }

                var label = i < _tagLabels.Count ? _tagLabels[i] : null;
                if (label == null) continue;
                string shown = StockLoc.Tag(raw);
                label.text = excluded ? "<s>" + shown + "</s>" : shown;

                // 포함 상태의 글자색은 하이라이트가 소유한다. 그 외에는 직접 칠한다 —
                // 하이라이트 컴포넌트는 '한 번도 선택된 적 없는' 칩엔 안 붙으므로 복원을 기대할 수 없다.
                if (excluded)
                {
                    var c = excludedTextColor; c.a *= excludedAlpha;
                    label.color = c;
                }
                else if (!included && i < _tagLabelColors.Count) label.color = _tagLabelColors[i];
            }
        }

        // ==== 칩 버튼 생성 ====

        private Button CreateChipButton(RectTransform parent, string text, Color bg, Color fg, Action onClick)
        {
            var chip = new GameObject("Chip", typeof(RectTransform), typeof(Image), typeof(Button));
            chip.transform.SetParent(parent, false);
            var rt = (RectTransform)chip.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); // 좌상단 기준(수동 flow가 위치 지정)
            rt.pivot = new Vector2(0f, 1f);

            var img = chip.GetComponent<Image>();
            img.sprite = GetChipSprite();
            img.type = Image.Type.Sliced;
            img.color = bg;

            var btn = chip.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None; // 선택 하이라이트는 MarketSelectHighlight가 담당
            btn.onClick.AddListener(() => onClick?.Invoke());

            var textGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(chip.transform, false);
            var label = textGO.GetComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = fg;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;

            // 폭을 TMP 실측(GetPreferredValues)으로 직접 계산해 명시적으로 박는다.
            // ContentSizeFitter/HLG 리빌드 타이밍에 의존하지 않으므로 칩이 겹치거나 어긋나지 않는다.
            float textW = label.GetPreferredValues(text).x;
            if (textW <= 0f) textW = text.Length * fontSize * 0.62f; // 폰트 미준비 시 근사 폴백
            float w = Mathf.Ceil(textW) + paddingX * 2f + 2f;
            float h = ChipHeight();
            rt.sizeDelta = new Vector2(w, h);

            // 라벨은 칩을 좌우 패딩만큼 안쪽으로 채우고 세로 가운데 정렬.
            var lrt = (RectTransform)textGO.transform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = new Vector2(paddingX, 0f);
            lrt.offsetMax = new Vector2(-paddingX, 0f);

            return btn;
        }

        /// <summary>모든 칩이 공유하는 균일 높이(줄이 깔끔하게 정렬되도록).</summary>
        private float ChipHeight() => Mathf.Ceil(fontSize * 1.2f) + paddingY * 2f;

        private Sprite GetChipSprite()
        {
            if (_chipSprite == null) _chipSprite = BuildRoundedSprite(cornerRadius);
            return _chipSprite;
        }

        /// <summary>흰색 둥근 사각형(9-slice) 스프라이트. Image.color로 틴트된다. (TagChipList와 동일 기법)</summary>
        private static Sprite BuildRoundedSprite(int radius)
        {
            radius = Mathf.Max(2, radius);
            int side = radius * 2 + 4;
            var tex = new Texture2D(side, side, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[side * side];
            float half = side / 2f;
            float inset = half - radius;
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inset, 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inset, 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - dist + 0.5f);
                    px[y * side + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            var border = new Vector4(radius, radius, radius, radius);
            return Sprite.Create(tex, new Rect(0, 0, side, side), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);
        }
    }
}
