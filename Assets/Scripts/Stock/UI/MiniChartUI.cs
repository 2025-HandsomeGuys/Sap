using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Stock.UI
{
    /// <summary>
    /// 가격 히스토리를 라인(스파크라인)으로 그리는 경량 차트.
    /// 자식 오브젝트 없이 단일 Graphic으로 메시를 직접 생성하므로 프리팹 구성이 간단하다.
    /// 사용: 빈 UI 오브젝트에 이 컴포넌트만 부착하면 RectTransform 영역에 라인을 그린다.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class MiniChartUI : MaskableGraphic,
        IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        [SerializeField] private float lineThickness = 8f;
        [SerializeField] private int maxPoints = 40;
        [SerializeField] private Color riseColor = new Color(0.247f, 0.725f, 0.314f); // up #3FB950 (다크 레트로)
        [SerializeField] private Color fallColor = new Color(0.973f, 0.318f, 0.286f); // down #F85149

        [Header("영역 채우기 (라인 아래)")]
        [Range(0f, 1f)]
        [SerializeField] private float fillAlpha = 0.18f;   // 라인 아래 단색 채움 불투명도
        [Range(0f, 0.4f)]
        [SerializeField] private float topPadding = 0.12f;  // 상단 여백 비율 (두꺼운 선 클리핑 방지)

        [Header("호버 툴팁 — 마우스를 올린 틱의 가격 표시")]
        [Tooltip("끄면 예전처럼 표시 전용(레이캐스트 대상 아님)이 된다")]
        [SerializeField] private bool enableHover = true;
        [SerializeField] private float hoverDotRadius = 12f;
        [SerializeField] private Color hoverDotColor = new Color(1f, 1f, 1f, 0.95f);
        [Tooltip("선 '위쪽'으로 이만큼(px)까지는 아직 선으로 친다. 선 아래 채움 영역은 전부 반응 영역")]
        [SerializeField] private float hoverLineTolerance = 16f;
        [Tooltip("점에서 위로 띄울 거리(px)")]
        [SerializeField] private float tooltipOffsetY = 18f;
        [SerializeField] private float tooltipFontSize = 34f;
        [SerializeField] private Color tooltipBgColor = new Color(0.055f, 0.075f, 0.114f, 0.96f);
        [SerializeField] private Color tooltipTextColor = new Color(0.85f, 0.88f, 0.93f);
        [Tooltip("비우면 같은 캔버스의 다른 TMP 텍스트 폰트를 따라간다(메인 폰트 체계 유지)")]
        [SerializeField] private TMP_FontAsset tooltipFont;

        private readonly List<int> _values = new List<int>();

        // OnPopulateMesh가 계산한 꼭짓점(로컬 좌표)과 그때의 플롯 영역 — 호버 히트테스트가 그대로 쓴다.
        // 다시 계산하지 않는 이유: 여백·정규화 규칙이 두 곳으로 갈라지면 점이 선에서 미끄러진다.
        private Vector2[] _pts;
        private Rect _plotRect;

        private RectTransform _dot;
        private RectTransform _tooltip;
        private TextMeshProUGUI _tooltipText;
        private int _hoverIndex = -1;

        // 차트는 표시 전용 — 클릭/레이캐스트 대상에서 제외(클릭 가로채기 방지 +
        // CanvasRenderer 누락 시 GraphicRaycaster 예외 폭주 방지).
        protected override void Awake()
        {
            base.Awake();
            raycastTarget = enableHover;
        }

        public void SetData(IReadOnlyList<int> values)
        {
            _values.Clear();
            if (values != null)
            {
                int start = Mathf.Max(0, values.Count - maxPoints);
                for (int i = start; i < values.Count; i++) _values.Add(values[i]);
            }

            // 전체 추세(시작 대비 종가)에 따라 색을 정한다.
            if (_values.Count >= 2)
                color = _values[_values.Count - 1] >= _values[0] ? riseColor : fallColor;

            SetVerticesDirty();
            if (_hoverIndex >= 0) HideHover(); // 데이터가 바뀌면 예전 틱을 가리키던 표시는 버린다
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_values.Count < 2) return;

            Rect r = GetPixelAdjustedRect();
            int n = _values.Count;

            int min = int.MaxValue, max = int.MinValue;
            foreach (var v in _values) { if (v < min) min = v; if (v > max) max = v; }
            float range = Mathf.Max(1, max - min);

            // 상단에 여백을 둬서 두꺼워진 선이 영역 위로 잘리지 않게 한다.
            float plotBottom = r.yMin;
            float plotHeight = r.height * (1f - topPadding);

            if (_pts == null || _pts.Length != n) _pts = new Vector2[n];
            Vector2[] pts = _pts;
            _plotRect = r;
            for (int i = 0; i < n; i++)
            {
                float x = r.xMin + r.width * (i / (float)(n - 1));
                float y = plotBottom + plotHeight * ((_values[i] - min) / range);
                pts[i] = new Vector2(x, y);
            }

            // 1) 라인 아래 단색 반투명 영역 — 먼저 그려 라인 뒤에 깔린다.
            Color fill = color; fill.a = fillAlpha;
            for (int i = 0; i < n - 1; i++) AddFillQuad(vh, pts[i], pts[i + 1], r.yMin, fill);

            // 2) 라인 본체 + 꼭짓점 이음새(둥근 느낌). 라인 위에 올라온다.
            for (int i = 0; i < n - 1; i++) AddSegment(vh, pts[i], pts[i + 1]);
            for (int i = 0; i < n; i++) AddJoint(vh, pts[i]);
        }

        // 두 점 사이 구간을 바닥까지 단색으로 채우는 사다리꼴.
        private void AddFillQuad(VertexHelper vh, Vector2 a, Vector2 b, float baseY, Color fill)
        {
            int idx = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = fill;

            v.position = new Vector2(a.x, a.y);   vh.AddVert(v);
            v.position = new Vector2(b.x, b.y);   vh.AddVert(v);
            v.position = new Vector2(b.x, baseY); vh.AddVert(v);
            v.position = new Vector2(a.x, baseY); vh.AddVert(v);

            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }

        private void AddSegment(VertexHelper vh, Vector2 a, Vector2 b)
        {
            Vector2 dir = (b - a).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x) * (lineThickness * 0.5f);

            int idx = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = color;

            v.position = a - normal; vh.AddVert(v);
            v.position = a + normal; vh.AddVert(v);
            v.position = b + normal; vh.AddVert(v);
            v.position = b - normal; vh.AddVert(v);

            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }

        // 각 꼭짓점에 선 두께만 한 사각 패치를 깔아 구간 사이 이음새 틈을 메운다.
        private void AddJoint(VertexHelper vh, Vector2 p)
        {
            float h = lineThickness * 0.5f;
            int idx = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = color;

            v.position = p + new Vector2(-h, -h); vh.AddVert(v);
            v.position = p + new Vector2(-h,  h); vh.AddVert(v);
            v.position = p + new Vector2( h,  h); vh.AddVert(v);
            v.position = p + new Vector2( h, -h); vh.AddVert(v);

            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx + 2, idx + 3, idx);
        }

        // ==== 호버 (틱별 가격 표시) ====

        public void OnPointerEnter(PointerEventData e) { if (enableHover) UpdateHover(e); }
        public void OnPointerMove(PointerEventData e)  { if (enableHover) UpdateHover(e); }
        public void OnPointerExit(PointerEventData e)  { HideHover(); }

        protected override void OnDisable()
        {
            base.OnDisable();
            HideHover();
        }

        // 툴팁은 루트 캔버스 자식이라 이 컴포넌트가 사라져도 남는다 — 같이 치운다.
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_tooltip) Destroy(_tooltip.gameObject);
        }

        private void UpdateHover(PointerEventData e)
        {
            if (_pts == null || _values.Count < 2) { HideHover(); return; }

            var cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, e.position, cam, out Vector2 local)) { HideHover(); return; }

            // x가 가장 가까운 틱을 고른다(구간 중앙에서 갈림) — 선 위/아래 어디를 가리켜도 잡힌다.
            int n = _pts.Length;
            float t = _plotRect.width <= 0f ? 0f : (local.x - _plotRect.xMin) / _plotRect.width;
            int idx = Mathf.Clamp(Mathf.RoundToInt(t * (n - 1)), 0, n - 1);

            // 반응 영역 = 선 + 그 아래 채움 영역. 선 위쪽(빈 하늘)만 제외한다 —
            // 채움까지 포함해야 값이 낮은 구간에서 얇은 선을 정확히 맞출 필요가 없다.
            if (local.y > LineYAt(local.x) + lineThickness * 0.5f + hoverLineTolerance)
            {
                HideHover();
                return;
            }

            EnsureHoverObjects();
            if (_dot == null) return;

            if (idx != _hoverIndex)
            {
                _hoverIndex = idx;
                RefreshTooltipText(idx);
            }
            PlaceHover(idx);
        }

        /// <summary>주어진 로컬 x에서 꺾은선의 y — 두 꼭짓점 사이는 선형 보간한다.</summary>
        private float LineYAt(float x)
        {
            int n = _pts.Length;
            if (n == 1) return _pts[0].y;

            float span = _pts[n - 1].x - _pts[0].x;
            float u = span <= 0f ? 0f : (x - _pts[0].x) / span * (n - 1);
            int i = Mathf.Clamp(Mathf.FloorToInt(u), 0, n - 2);
            return Mathf.Lerp(_pts[i].y, _pts[i + 1].y, Mathf.Clamp01(u - i));
        }

        private void HideHover()
        {
            _hoverIndex = -1;
            if (_dot) _dot.gameObject.SetActive(false);
            if (_tooltip) _tooltip.gameObject.SetActive(false);
        }

        private void RefreshTooltipText(int idx)
        {
            if (_tooltipText == null || _tooltip == null) return;

            int ago = _values.Count - 1 - idx;
            string when = ago == 0
                ? StockLoc.L("ui_stock_chart_tick_now", "현재 틱")
                : StockLoc.LF("ui_stock_chart_tick_ago", "{0}틱 전", ago);

            string price = StockLoc.LF("ui_stock_price", "₩{0:N0}", _values[idx]);

            // 전 틱 대비 변화 — 첫 점은 비교 대상이 없어 생략한다.
            string delta = "";
            if (idx > 0 && _values[idx - 1] != 0)
            {
                float pct = (_values[idx] - _values[idx - 1]) * 100f / _values[idx - 1];
                string sign = pct > 0f ? "▲" : (pct < 0f ? "▼" : "-");
                delta = $"\n<size=88%>{sign} {Mathf.Abs(pct):F1}%</size>";
            }

            _tooltipText.text = $"<size=82%>{when}</size>\n{price}{delta}";
            _tooltipText.ForceMeshUpdate();

            _tooltip.sizeDelta = new Vector2(
                Mathf.Ceil(_tooltipText.preferredWidth) + 28f,
                Mathf.Ceil(_tooltipText.preferredHeight) + 20f);
        }

        private void PlaceHover(int idx)
        {
            Vector2 p = _pts[idx];

            _dot.gameObject.SetActive(true);
            _dot.anchoredPosition = p - new Vector2(_plotRect.xMin, _plotRect.yMin);

            if (_tooltip == null) return;
            _tooltip.gameObject.SetActive(true);

            // 툴팁은 루트 캔버스 자식이라(마스크·스크롤에 잘리지 않게) 좌표를 캔버스 로컬로 옮긴다.
            var canvasRT = (RectTransform)_tooltip.parent;
            Vector2 c = canvasRT.InverseTransformPoint(rectTransform.TransformPoint(p));
            c.y += tooltipOffsetY;

            // 캔버스 밖으로 나가면 안쪽으로 밀고, 위가 막히면 점 아래로 뒤집는다.
            Rect b = canvasRT.rect;
            Vector2 size = _tooltip.sizeDelta;
            if (c.y + size.y > b.yMax) c.y -= size.y + tooltipOffsetY * 2f;
            c.x = Mathf.Clamp(c.x, b.xMin + size.x * 0.5f, b.xMax - size.x * 0.5f);
            c.y = Mathf.Clamp(c.y, b.yMin, b.yMax - size.y);
            _tooltip.anchoredPosition = c;
        }

        private void EnsureHoverObjects()
        {
            if (_dot == null)
            {
                var go = new GameObject("HoverDot", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                _dot = (RectTransform)go.transform;
                _dot.anchorMin = _dot.anchorMax = Vector2.zero;
                _dot.pivot = new Vector2(0.5f, 0.5f);
                _dot.sizeDelta = Vector2.one * (hoverDotRadius * 2f);

                var img = go.GetComponent<Image>();
                img.sprite = DotSprite();
                img.color = hoverDotColor;
                img.raycastTarget = false;
            }

            if (_tooltip != null) return;

            var root = canvas != null ? canvas.rootCanvas : GetComponentInParent<Canvas>();
            if (root == null) return;

            var tip = new GameObject("ChartHoverTooltip", typeof(RectTransform), typeof(Image));
            tip.transform.SetParent(root.transform, false);
            tip.transform.SetAsLastSibling();
            _tooltip = (RectTransform)tip.transform;
            _tooltip.anchorMin = _tooltip.anchorMax = new Vector2(0.5f, 0.5f);
            _tooltip.pivot = new Vector2(0.5f, 0f);

            var bg = tip.GetComponent<Image>();
            bg.color = tooltipBgColor;
            bg.raycastTarget = false;

            var tgo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            tgo.transform.SetParent(tip.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14f, 10f); trt.offsetMax = new Vector2(-14f, -10f);

            _tooltipText = tgo.GetComponent<TextMeshProUGUI>();
            _tooltipText.fontSize = tooltipFontSize;
            _tooltipText.color = tooltipTextColor;
            _tooltipText.alignment = TextAlignmentOptions.Center;
            _tooltipText.raycastTarget = false;

            // 폰트는 인스펙터 지정 > 같은 캔버스의 다른 TMP 폰트 순 — 메인 폰트 체계를 그대로 따른다.
            var f = tooltipFont;
            if (f == null)
            {
                var sample = root.GetComponentInChildren<TextMeshProUGUI>(true);
                if (sample != null && sample != _tooltipText) f = sample.font;
            }
            if (f != null) _tooltipText.font = f;
        }

        // 점 스프라이트는 코드로 한 번만 굽는다(원형 스프라이트 에셋을 새로 만들지 않기 위해).
        private static Sprite s_dotSprite;
        private static Sprite DotSprite()
        {
            if (s_dotSprite != null) return s_dotSprite;

            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float c = (S - 1) * 0.5f;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(c - d); // 가장자리 1px 안티앨리어싱
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            s_dotSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return s_dotSprite;
        }

    }
}
