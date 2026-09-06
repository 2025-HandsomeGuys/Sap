// @tags: stock, ui, tag, chip, pill, badge, company-detail
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Market;

namespace Stock.UI
{
    /// <summary>
    /// 회사 태그(pharma, bio, vaccine …)를 알약형(pill) 칩으로 렌더링한다.
    /// 태그 수만큼 칩을 런타임 생성·풀링하며, 둥근 배경 스프라이트도 코드에서 만든다.
    /// (프리팹·스프라이트 에셋 불필요. CompanyDetailUI가 SetTags로 갱신)
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class TagChipList : MonoBehaviour
    {
        [Header("칩 스타일 (MarketTheme §3.1 ChipBg / TextMuted)")]
        [Tooltip("비우면 코드에서 둥근 사각형 스프라이트를 생성한다")]
        [SerializeField] private Sprite chipSprite;
        [Tooltip("비우면 TMP 기본 폰트 사용")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private float fontSize = 20f;
        [Tooltip("colorPerTag=false일 때 쓰는 단색 배경/텍스트")]
        [SerializeField] private Color chipColor = MarketTheme.ChipBg;   // #1E2733
        [SerializeField] private Color textColor = MarketTheme.TextMuted; // #8B98A9
        [Tooltip("칩 좌우 안쪽 여백(px)")]
        [SerializeField] private int paddingX = 14;
        [Tooltip("칩 상하 안쪽 여백(px)")]
        [SerializeField] private int paddingY = 5;
        [Tooltip("칩 사이 간격(px)")]
        [SerializeField] private float spacing = 6f;
        [Tooltip("칩 모서리 반경(px). 칩 높이의 절반이면 완전한 알약 모양")]
        [SerializeField] private int cornerRadius = 16;

        [Header("태그별 색상")]
        [Tooltip("켜면 태그 문자열 해시로 색상환 전체에서 색을 자동 배정(같은 태그=항상 같은 색). 끄면 위 단색 사용")]
        [SerializeField] private bool colorPerTag = true;
        [Tooltip("배경 틴트 강도. 0=완전 다크(ChipBg), 1=액센트 원색. 낮게 둘수록 사진처럼 어두운 칩")]
        [Range(0f, 1f)]
        [SerializeField] private float bgTint = 0.20f;
        [Tooltip("특정 태그 색 고정(대소문자 무시). 자동 배정보다 우선")]
        [SerializeField] private TagColorOverride[] overrides;

        [System.Serializable]
        private struct TagColorOverride
        {
            public string tag;
            public Color accent;
        }

        private readonly List<GameObject> _chips = new List<GameObject>();
        private HorizontalLayoutGroup _row;
        private Sprite _runtimeSprite;

        private void Awake() => EnsureLayout();

        /// <summary>바깥 행 레이아웃(칩을 좌→우로 배치)을 보장한다. 없으면 추가.</summary>
        private void EnsureLayout()
        {
            if (_row == null) _row = GetComponent<HorizontalLayoutGroup>();
            if (_row == null) _row = gameObject.AddComponent<HorizontalLayoutGroup>();
            _row.spacing = spacing;
            _row.padding = new RectOffset(0, 0, 0, 0);
            _row.childAlignment = TextAnchor.MiddleLeft;
            _row.childForceExpandWidth = false; // 칩은 늘리지 않고 내용 크기 유지
            _row.childForceExpandHeight = false;
            _row.childControlWidth = true;   // ControlChildSize ON — 칩의 preferred 크기를 읽어 배치
            _row.childControlHeight = true;  // (칩은 ContentSizeFitter 없이 안쪽 HLG가 preferred 크기 보고)

            var fitter = GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // 가로 폭은 부모 세로 레이아웃이 결정
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;   // 세로 높이는 칩 높이에 맞춤
        }

        /// <summary>태그 배열로 칩을 갱신한다. 남는 칩은 비활성화(풀링).</summary>
        public void SetTags(IReadOnlyList<string> tags)
        {
            EnsureLayout();
            int needed = tags?.Count ?? 0;
            while (_chips.Count < needed) _chips.Add(CreateChip());

            for (int i = 0; i < _chips.Count; i++)
            {
                bool on = i < needed;
                _chips[i].SetActive(on);
                if (!on) continue;

                string tag = tags[i];
                ResolveColors(tag, out Color bg, out Color fg); // 색상 해시는 raw 태그 기준(언어 무관 동일 색)

                var img = _chips[i].GetComponent<Image>();
                if (img) img.color = bg;
                var label = _chips[i].GetComponentInChildren<TextMeshProUGUI>();
                if (label) { label.text = StockLoc.Tag(tag); label.color = fg; } // 표시만 지역화
            }
        }

        /// <summary>태그에 대한 배경/텍스트 색을 계산한다.</summary>
        private void ResolveColors(string tag, out Color bg, out Color fg)
        {
            if (!colorPerTag)
            {
                bg = chipColor;
                fg = textColor;
                return;
            }
            Color accent = AccentForTag(tag);
            bg = Color.Lerp(MarketTheme.ChipBg, accent, bgTint); // 은은하게 틴트된 다크 배경
            fg = accent;                                         // 선명한 액센트 텍스트
        }

        /// <summary>오버라이드 우선, 없으면 색상환 해시(MarketTheme.TagAccent)로 색을 고른다.
        /// 필터 패널(StockFilterPanel)과 같은 함수를 써서 같은 태그면 두 화면 색이 일치한다.</summary>
        private Color AccentForTag(string tag)
        {
            if (overrides != null)
            {
                foreach (var o in overrides)
                    if (!string.IsNullOrEmpty(o.tag) && string.Equals(o.tag, tag, System.StringComparison.OrdinalIgnoreCase))
                        return o.accent;
            }
            return MarketTheme.TagAccent(tag);
        }

        private GameObject CreateChip()
        {
            // 칩 크기는 바깥 행(ControlChildSize ON)이 제어하므로 ContentSizeFitter를 두지 않는다.
            // 칩의 preferred 크기는 안쪽 HLG(padding + Label preferred)가 ILayoutElement로 보고한다.
            var chip = new GameObject("Chip",
                typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            chip.transform.SetParent(transform, false);

            var img = chip.GetComponent<Image>();
            img.sprite = chipSprite != null ? chipSprite : GetRuntimeSprite();
            img.type = Image.Type.Sliced;
            img.color = chipColor;

            var lg = chip.GetComponent<HorizontalLayoutGroup>();
            lg.padding = new RectOffset(paddingX, paddingX, paddingY, paddingY);
            lg.childAlignment = TextAnchor.MiddleCenter;
            lg.childControlWidth = true;
            lg.childControlHeight = true;
            lg.childForceExpandWidth = false;
            lg.childForceExpandHeight = false;

            var textGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(chip.transform, false);
            var label = textGO.GetComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.fontSize = fontSize;
            label.color = textColor;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;

            return chip;
        }

        private Sprite GetRuntimeSprite()
        {
            if (_runtimeSprite == null) _runtimeSprite = BuildRoundedSprite(cornerRadius);
            return _runtimeSprite;
        }

        /// <summary>흰색 둥근 사각형(9-slice) 스프라이트를 생성한다. Image.color로 틴트된다.</summary>
        private static Sprite BuildRoundedSprite(int radius)
        {
            radius = Mathf.Max(2, radius);
            int side = radius * 2 + 4;                 // 가운데 4px 스트레치 영역 확보
            var tex = new Texture2D(side, side, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[side * side];
            float half = side / 2f;
            float inset = half - radius;               // 모서리 중심까지의 거리
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inset, 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inset, 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - dist + 0.5f); // 1px 안티에일리어싱
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
