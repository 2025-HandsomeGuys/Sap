using System.Collections.Generic;
using UnityEngine;

namespace Relic
{
    // 엑스레이 유물의 런타임 구동체(프리팹 불필요, 유물이 코드로 생성).
    //  1) 전역 셰이더 프로퍼티 _XRayAmount(0..1)를 페이드 → XRayRendererFeature가 저채도 청록 톤 합성.
    //  2) 화면 내 지형·배경·오브젝트의 SpriteRenderer를 단색 머티리얼 3종으로 스왑(XRayFlatPalette)
    //     → 광물(밝음)/지형(중간)/배경(어두움) 3톤. 지형 알파가 곧 '안 판 땅' 실루엣이라
    //     판 굴은 자동으로 뚫려 배경 톤이 보인다. 오브젝트는 정렬순서도 지형 위로 올린다. 종료 시 원복.
    //  3) 발동 중 PlayerVisionOverlay의 바깥 어둠을 0으로 덮어쓴다. 어둠막은 XRay 풀스크린 패스보다
    //     먼저 그려지므로, 켜둔 채로는 투시한 오브젝트를 그대로 가려버린다(BlindRelic과 동일 경로).
    public class XRayController : MonoBehaviour
    {
        private static readonly int XRayAmountID = Shader.PropertyToID("_XRayAmount");
        private static readonly int XRayTintColorID = Shader.PropertyToID("_XRayTintColor");
        private static readonly int XRayDimID = Shader.PropertyToID("_XRayDim");
        private static readonly int XRayHighlightCutID = Shader.PropertyToID("_XRayHighlightCut");

        private bool _active;
        private float _amount;
        private float _amountTarget;

        private Color _highlightColor = new Color(0.4f, 1f, 0.9f, 1f);
        private float _highlightBlend = 0.55f;
        private float _fadeSpeed = 6f;
        private float _cameraPadding = 3f;
        // 오브젝트는 FindObjectsByType 비용이 있어 기존 주기를 유지한다.
        private float _rescanInterval = 0.4f;
        private float _rescanTimer;
        // 지형·배경은 매니저 컬렉션 순회라 저렴하다. 새로 로드된 청크가 원본 색으로
        // 남는 시간을 줄이려고 더 짧은 주기로 돈다.
        private float _worldRescanInterval = 0.15f;
        private float _worldRescanTimer;

        // 풀스크린 톤 파라미터(XRayRelic이 Configure로 주입).
        private Color _tintColor = new Color(0.05f, 0.95f, 0.85f, 1f);
        private float _dim = 0.6f;
        private float _highlightCut = 0.45f;

        private PlayerVisionOverlay _overlay;
        private PlayerVisionOverlay Overlay =>
            _overlay != null ? _overlay : (_overlay = FindFirstObjectByType<PlayerVisionOverlay>());

        // 정렬 기준(지형 SpriteRenderer 위로 올리기 위해 1회 해석).
        private int _revealSortingLayerId;
        private int _revealSortingOrder = 300;
        private bool _sortingResolved;

        private struct Entry
        {
            public SpriteRenderer sr;
            public int layerId;
            public int order;
            public Color color;
            public bool enabled;
            public Material material;   // 원본 sharedMaterial — 복원용
        }
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<SpriteRenderer> _tracked = new HashSet<SpriteRenderer>();

        public void Configure(Color highlight, float highlightBlend, float fadeSpeed, float cameraPadding)
        {
            _highlightColor = highlight;
            _highlightBlend = highlightBlend;
            _fadeSpeed = fadeSpeed;
            _cameraPadding = cameraPadding;
        }

        public void ConfigureTone(Color tint, float dim, float highlightCut)
        {
            _tintColor = tint;
            _dim = dim;
            _highlightCut = highlightCut;
        }

        // 3톤 단색화(설계: xray-flat-tone). 팔레트가 null이거나 불완전하면
        // 머티리얼 스왑을 통째로 건너뛰고 기존 정렬순서 투시만 동작한다.
        private XRayFlatPalette _palette;
        private Color _flatTerrain = new Color(0.22f, 0.30f, 0.34f, 1f);
        private Color _flatBackground = new Color(0.05f, 0.09f, 0.11f, 1f);
        private Color _flatObject = new Color(0.55f, 1f, 0.92f, 1f);

        public void ConfigureFlat(XRayFlatPalette palette, Color terrain, Color background, Color obj)
        {
            _palette = palette;
            _flatTerrain = terrain;
            _flatBackground = background;
            _flatObject = obj;
        }

        public void Begin()
        {
            _active = true;
            _amountTarget = 1f;
            _rescanTimer = 0f;
            _worldRescanTimer = 0f;
            ApplyToneGlobals();
            _palette?.Apply(_flatTerrain, _flatBackground, _flatObject);
            Overlay?.SetDarknessOverride(0f); // 어둠막이 투시 대상을 가리지 않게
            ResolveSorting();
            ScanWorld();
            Scan();
        }

        public void End()
        {
            _active = false;
            _amountTarget = 0f;
            Overlay?.ClearDarknessOverride();
            // RestoreAll은 Update가 페이드 아웃 완료를 확인한 뒤 호출한다.
            // 언릿 전환 때문에 amount>0 구간에서 되돌리면 조명 있던 픽셀이 톡 튄다.
        }

        private void ApplyToneGlobals()
        {
            Shader.SetGlobalColor(XRayTintColorID, _tintColor);
            Shader.SetGlobalFloat(XRayDimID, _dim);
            Shader.SetGlobalFloat(XRayHighlightCutID, _highlightCut);
        }

        // 즉시 원복(해제·비활성·파괴 시 안전망).
        public void ForceOff()
        {
            _active = false;
            _amountTarget = 0f;
            _amount = 0f;
            Shader.SetGlobalFloat(XRayAmountID, 0f);
            Overlay?.ClearDarknessOverride();
            RestoreAll();
        }

        private void Update()
        {
            _amount = Mathf.MoveTowards(_amount, _amountTarget, _fadeSpeed * Time.deltaTime);
            Shader.SetGlobalFloat(XRayAmountID, _amount);

            if (_active)
            {
                _rescanTimer -= Time.deltaTime;
                if (_rescanTimer <= 0f)
                {
                    _rescanTimer = _rescanInterval;
                    Scan(); // 발동 중 새로 로드된 청크의 오브젝트도 투시에 편입
                }

                _worldRescanTimer -= Time.deltaTime;
                if (_worldRescanTimer <= 0f)
                {
                    _worldRescanTimer = _worldRescanInterval;
                    ScanWorld(); // 새로 로드된 지형·배경도 단색으로 편입
                }
            }
            else if (_entries.Count > 0 && _amount <= 0f)
            {
                // 페이드 아웃이 '완료된 뒤'에 원복한다(End의 주석 참고).
                RestoreAll();
            }
        }

        private void OnDisable() => ForceOff();
        private void OnDestroy() => ForceOff();

        private void ResolveSorting()
        {
            if (_sortingResolved) return;

            var tc = FindFirstObjectByType<TerrainChunk>();
            if (tc != null)
            {
                var sr = tc.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    _revealSortingLayerId = sr.sortingLayerID; // 지형과 같은 레이어에
                    _revealSortingOrder = sr.sortingOrder + 300; // order를 크게 올려 지형 위로
                    _sortingResolved = true;
                    return;
                }
            }
            // 폴백: Default 레이어의 높은 order
            _revealSortingLayerId = SortingLayer.NameToID("Default");
            _revealSortingOrder = 300;
        }

        private void Scan()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Bounds view = ComputeViewBounds(cam);

            AddTargets<MineralItemController>(view);  // 광물
            AddTargets<DiggableBlockBase>(view);      // 크리스탈 등 특수 블록
            AddTargets<FallingHazardBase>(view);      // 고드름 등 낙하 함정
            AddTargets<RollingRockEntity>(view);      // 구르는 바위
            AddTargets<InteractableBlockBase>(view);  // 상호작용 특수 블록
        }

        // 지형·배경은 화면 컬링하지 않는다. 카메라가 움직일 때 화면 가장자리에서
        // 원본 색이 번쩍이는 것을 막기 위함이다. FindObjectsByType을 쓰지 않아
        // 매니저 컬렉션 순회 비용만 든다.
        private void ScanWorld()
        {
            if (_palette == null) return;

            var map = InfinityMapManager.Instance;
            if (map != null)
            {
                foreach (var chunk in map.GetAllActiveChunks())
                {
                    if (chunk == null) continue;

                    if (chunk.BackgroundObject != null)
                        SwapRenderers(chunk.BackgroundObject, XRayTone.Background, forceEnable: false);

                    // 루트 렌더러만. 자식(광물·바위 등)은 각자의 스캔이 담당한다.
                    SwapRenderers(chunk.gameObject, XRayTone.Terrain, forceEnable: false,
                                  includeChildren: false);
                }
            }

            var bg = FindFirstObjectByType<BackgroundManager>();
            if (bg != null)
            {
                foreach (var tile in bg.ActiveTiles)
                {
                    if (tile == null) continue;
                    SwapRenderers(tile, XRayTone.Background, forceEnable: false);
                }
            }
        }

        // 한 GameObject의 SpriteRenderer를 지정 톤 머티리얼로 스왑하고
        // 원본 상태를 _entries에 적재한다. 이미 추적 중인 렌더러는 건너뛴다.
        //
        // includeChildren=false면 root 자신의 렌더러만 건드린다. 청크가 그렇다 —
        // 청크의 지형 스프라이트는 루트 렌더러 하나뿐이고, 자식은 광물·바위 같은
        // 별개 엔티티다. 서브트리를 통째로 지형 취급하면 자식 광물 렌더러까지
        // 선점해버려, 뒤따르는 오브젝트 스캔이 _tracked 때문에 건너뛰고
        // 광물이 지형 색인 채 꺼진 상태로 남는다(= 투시가 안 되는 버그).
        // 테스트 진입점이기도 하다(XRayControllerSwapTests).
        public void SwapRenderers(GameObject root, XRayTone tone, bool forceEnable, bool includeChildren = true)
        {
            var mat = _palette?.Get(tone);
            var srs = includeChildren
                ? root.GetComponentsInChildren<SpriteRenderer>(true)
                : root.GetComponents<SpriteRenderer>();

            for (int i = 0; i < srs.Length; i++)
            {
                var sr = srs[i];
                if (sr == null || _tracked.Contains(sr)) continue;

                _tracked.Add(sr);
                _entries.Add(new Entry
                {
                    sr = sr,
                    layerId = sr.sortingLayerID,
                    order = sr.sortingOrder,
                    color = sr.color,
                    enabled = sr.enabled,
                    material = sr.sharedMaterial
                });

                if (mat != null)
                {
                    // material이 아니라 sharedMaterial — 렌더러마다 인스턴스가 생기면 누수된다.
                    sr.sharedMaterial = mat;
                }
                else if (tone == XRayTone.Object)
                {
                    // 폴백: 머티리얼이 없으면(인스펙터 미연결) 단색화를 못 하므로
                    // 최소한 예전처럼 하이라이트 틴트라도 입혀 오브젝트를 구분시킨다.
                    sr.color = Color.Lerp(sr.color, _highlightColor, _highlightBlend);
                }

                if (forceEnable) sr.enabled = true;
            }
        }

        private void AddTargets<T>(Bounds view) where T : Component
        {
            var comps = FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;

                Vector3 p = c.transform.position; p.z = view.center.z;
                if (!view.Contains(p)) continue;

                // 흙 속에서 렌더러가 꺼져 있던 경우가 있어 forceEnable.
                // 색 Lerp는 하지 않는다 — 단색은 이제 머티리얼이 담당한다.
                SwapRenderers(c.gameObject, XRayTone.Object, forceEnable: true);

                // 지형 위로 끌어올린다. 이미 _tracked에 있으므로 중복 적재는 없다.
                var srs = c.GetComponentsInChildren<SpriteRenderer>(true);
                for (int j = 0; j < srs.Length; j++)
                {
                    var sr = srs[j];
                    if (sr == null) continue;
                    sr.sortingLayerID = _revealSortingLayerId;
                    sr.sortingOrder = _revealSortingOrder;
                }
            }
        }

        private void RestoreAll()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.sr == null) continue;
                e.sr.sortingLayerID = e.layerId;
                e.sr.sortingOrder = e.order;
                e.sr.color = e.color;
                e.sr.enabled = e.enabled;
                e.sr.sharedMaterial = e.material;
            }
            _entries.Clear();
            _tracked.Clear();
        }

        private Bounds ComputeViewBounds(Camera cam)
        {
            Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0f, 0f, cam.nearClipPlane));
            Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1f, 1f, cam.nearClipPlane));
            var b = new Bounds();
            b.SetMinMax(
                new Vector3(Mathf.Min(bl.x, tr.x) - _cameraPadding, Mathf.Min(bl.y, tr.y) - _cameraPadding, -100f),
                new Vector3(Mathf.Max(bl.x, tr.x) + _cameraPadding, Mathf.Max(bl.y, tr.y) + _cameraPadding, 100f));
            return b;
        }
    }
}
