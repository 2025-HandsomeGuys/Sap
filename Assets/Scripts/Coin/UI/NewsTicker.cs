// @tags: coin, ui, news, ticker, marquee, scroll, flavor
using UnityEngine;
using TMPro;

namespace Coin.UI
{
    /// <summary>
    /// 하단에 가짜 코인 뉴스를 좌→우로 흐르게(마퀴) 보여주는 연출용 티커 (설계 §7.6). 결과에 영향 없음.
    /// 텍스트는 viewport(마스크 창)의 오른쪽 끝 '밖'에서 입장해 왼쪽으로 흐르고,
    /// 창 왼쪽으로 완전히 빠져나가면 다음 헤드라인으로 교체되어 다시 오른쪽 끝 밖에서 나온다.
    /// 시작/종료 기준을 viewport의 '실제 가장자리'(월드 코너→부모 로컬 변환)로 잡으므로
    /// 텍스트가 viewport보다 넓은 부모에 들어가 있어도 항상 화면 밖에서 시작한다.
    /// 셋업: viewport에 RectMask2D를 두고, tickerText를 그 자식으로 둘 것.
    /// </summary>
    public class NewsTicker : MonoBehaviour
    {
        [Tooltip("스크롤이 보이는 마스크 영역(RectMask2D 부착). 비우면 tickerText의 부모 사용")]
        [SerializeField] private RectTransform viewport;
        [SerializeField] private TextMeshProUGUI tickerText;
        [Tooltip("스크롤 속도(px/초)")]
        [SerializeField] private float scrollSpeed = 80f;
        [Tooltip("헤드라인이 완전히 빠진 뒤 다음 것이 나올 때까지의 추가 여백(px)")]
        [SerializeField] private float trailingGap = 60f;
        [Tooltip("부모 수직 중앙 기준 오프셋(px). 0이면 정중앙")]
        [SerializeField] private float verticalOffset = 0f;
        [SerializeField] private string[] fallbackHeadlines =
        {
            "익명의 고래, 전량 매도 후 잠적…",
            "개발자 깃허브 삭제 — 먹튀 의혹 확산",
            "거래소 상장 루머에 커뮤니티 들썩",
            "재단 지갑에서 대규모 이체 포착",
            "인플루언서 '가즈아' 한마디에 출렁",
            "백서 표절 논란… 신뢰도 흔들",
        };

        private RectTransform _rt;       // tickerText의 RectTransform(움직이는 대상)
        private RectTransform _parent;   // tickerText의 부모(anchoredPosition 좌표계)
        private readonly Vector3[] _corners = new Vector3[4];
        private float _x;        // 텍스트 왼쪽 가장자리의 현재 부모-로컬 X
        private float _width;    // 현재 헤드라인 폭(px)
        private int _lastIdx = -1;
        private bool _loaded;

        /// <summary>티커 표시 토글 — 베팅 연출(리빌) 중 숨기기용. 스크롤 진행 상태는 유지된다.</summary>
        public void SetVisible(bool visible)
        {
            if (tickerText) tickerText.gameObject.SetActive(visible);
        }

        private void OnEnable()
        {
            if (tickerText != null)
            {
                _rt = tickerText.rectTransform;
                _parent = _rt.parent as RectTransform;
                tickerText.textWrappingMode = TextWrappingModes.NoWrap; // 한 줄 유지
                // 좌-중앙 기준 고정 → anchoredPosition.x = 왼쪽 오프셋, y = 수직 중앙 오프셋.
                _rt.anchorMin = new Vector2(0f, 0.5f);
                _rt.anchorMax = new Vector2(0f, 0.5f);
                _rt.pivot = new Vector2(0f, 0.5f);
            }
            // viewport 미지정(또는 실수로 텍스트 자신 지정) 시 부모를 사용.
            if (viewport == null || viewport == _rt) viewport = _parent;
            _loaded = false;
        }

        private void Update()
        {
            if (_rt == null || _parent == null || viewport == null) return;

            GetViewportEdges(out float leftX, out float rightX);
            if (rightX - leftX <= 1f) return; // 레이아웃 준비 전(폭 0) 대기

            if (!_loaded) { LoadNext(rightX); _loaded = true; }

            _x -= scrollSpeed * Time.unscaledDeltaTime;
            // 텍스트 오른쪽 끝(_x + _width)이 창 왼쪽 가장자리를 완전히 지나면 다음 헤드라인.
            if (_x + _width + Mathf.Max(0f, trailingGap) <= leftX) LoadNext(rightX);

            SetLeftEdgeX(_x);
        }

        private void LoadNext(float rightX)
        {
            if (tickerText == null || fallbackHeadlines == null || fallbackHeadlines.Length == 0) return;

            int idx = Random.Range(0, fallbackHeadlines.Length);
            if (fallbackHeadlines.Length > 1 && idx == _lastIdx)
                idx = (idx + 1) % fallbackHeadlines.Length; // 같은 헤드라인 연속 회피
            _lastIdx = idx;

            tickerText.text = CoinLoc.L("ui_coin_news_" + idx, fallbackHeadlines[idx]);
            tickerText.ForceMeshUpdate();

            _width = tickerText.preferredWidth;
            _rt.sizeDelta = new Vector2(_width, tickerText.preferredHeight); // 텍스트 실제 크기로 맞춤
            _x = rightX;        // 창 오른쪽 끝 '밖'(텍스트 왼쪽 끝을 창 오른쪽 가장자리에)에서 입장
            SetLeftEdgeX(_x);
        }

        // 텍스트 왼쪽 가장자리를 '부모-로컬 X = localX' 위치에 둔다(anchor/pivot 좌-중앙 전제).
        private void SetLeftEdgeX(float localX)
        {
            float anchoredX = localX - _parent.rect.xMin; // anchor가 부모 왼쪽이므로 보정
            _rt.anchoredPosition = new Vector2(anchoredX, verticalOffset);
        }

        // viewport의 왼/오른쪽 가장자리를 '텍스트 부모'의 로컬 X로 변환(중첩·스케일 무관).
        private void GetViewportEdges(out float leftX, out float rightX)
        {
            viewport.GetWorldCorners(_corners); // 0=BL, 1=TL, 2=TR, 3=BR (월드)
            float a = _parent.InverseTransformPoint(_corners[0]).x;
            float b = _parent.InverseTransformPoint(_corners[3]).x;
            leftX = Mathf.Min(a, b);
            rightX = Mathf.Max(a, b);
        }
    }
}
