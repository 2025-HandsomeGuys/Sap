using System;
using UnityEngine;

namespace Relic
{
    // 엑스레이(액티브·지속형): 발동 시 duration 동안 화면이 저채도 청록 톤으로 가라앉고, 화면 내
    // 흙 너머의 숨은 광물·특수청크·함정이 밝게 투시된다. 쿨타임 2분.
    //   화면 효과: XRayRendererFeature(전역 _XRayAmount) — URP Renderer에 피처 1회 등록 필요.
    //   투시·페이드·어둠막 해제: XRayController(코드 생성 런타임 오브젝트).
    [Serializable]
    public class XRayRelic : RelicBehaviour
    {
        [SerializeField] private float[] durationPerLevel = { 3f, 4f, 5f }; // Lv1=3초(기획)
        [SerializeField] private float cooldown = 120f;                     // 2분(기획)

        [SerializeField] private Color highlightColor = new Color(0.4f, 1f, 0.9f, 1f);
        [SerializeField] private float highlightBlend = 0.8f; // 배경보다 확실히 밝아야 셰이더가 원본 색을 살린다
        [SerializeField] private float fadeSpeed = 6f;      // _XRayAmount 페이드 속도(초당)
        [SerializeField] private float cameraPadding = 3f;  // 화면 밖 여유(유닛)

        [Header("화면 톤")]
        [SerializeField] private Color toneTint = new Color(0.05f, 0.95f, 0.85f, 1f); // 배경 청록(R을 죽일수록 청록이 짙어짐)
        [SerializeField, Range(0f, 1f)] private float toneDim = 0.6f;                 // 배경 감광
        [SerializeField, Range(0f, 1f)] private float toneHighlightCut = 0.45f;      // 원본 색 유지 밝기 문턱

        [Header("3톤 단색화")]
        [SerializeField] private Material flatTerrainMat;     // XRayFlat_Terrain.mat
        [SerializeField] private Material flatBackgroundMat;  // XRayFlat_Background.mat
        [SerializeField] private Material flatObjectMat;      // XRayFlat_Object.mat
        [SerializeField] private Color flatTerrainColor = new Color(0.22f, 0.30f, 0.34f, 1f);    // 중간
        [SerializeField] private Color flatBackgroundColor = new Color(0.05f, 0.09f, 0.11f, 1f); // 가장 어둡게
        [SerializeField] private Color flatObjectColor = new Color(0.55f, 1f, 0.92f, 1f);        // 가장 밝게

        private XRayController _controller;

        private float Dur() => durationPerLevel[Mathf.Clamp(level - 1, 0, durationPerLevel.Length - 1)];

        public override float GetDuration() => Dur();       // 지속형
        public override float GetCooldown() => cooldown;

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            EnsureController();
        }

        public override void OnUnequip()
        {
            if (_controller != null)
            {
                _controller.ForceOff();
                UnityEngine.Object.Destroy(_controller.gameObject);
            }
            _controller = null;
        }

        public override void OnActivate()
        {
            EnsureController();
            _controller?.Begin();
        }

        public override void OnActiveEnd()
        {
            _controller?.End();
        }

        private void EnsureController()
        {
            if (_controller != null) return;
            var go = new GameObject("XRayController");
            _controller = go.AddComponent<XRayController>();
            _controller.Configure(highlightColor, highlightBlend, fadeSpeed, cameraPadding);
            _controller.ConfigureTone(toneTint, toneDim, toneHighlightCut);

            // 머티리얼이 하나라도 비어 있으면 팔레트가 불완전하다.
            // 컨트롤러가 스왑 대신 하이라이트 틴트 폴백으로 동작하므로 유물은 계속 쓸 수 있다.
            var palette = new XRayFlatPalette(flatTerrainMat, flatBackgroundMat, flatObjectMat);
            if (!palette.IsComplete)
            {
                Debug.LogWarning("[XRayRelic] 3톤 머티리얼이 지정되지 않아 단색화를 건너뜁니다. " +
                                 "유물 에셋 인스펙터에서 XRayFlat_* 머티리얼 3종을 연결하세요.");
            }
            _controller.ConfigureFlat(palette, flatTerrainColor, flatBackgroundColor, flatObjectColor);
        }
    }
}
