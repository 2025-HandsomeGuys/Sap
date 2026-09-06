using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

/// <summary>
/// 엑스레이 유물 — 카메라 컬러를 흑백 반전(Custom/XRay)으로 합성하는 풀스크린 패스.
/// 전역 float _XRayAmount(0..1)가 0이면 패스를 건너뛴다(유물 미발동 시 0 비용).
/// 유물(XRayController)이 매 프레임 _XRayAmount를 페이드해 켜고 끈다.
///
/// URP 설정: URP Renderer 에셋의 Renderer Features에 이 피처를 1회 추가하면 된다
/// (PixelateLightRendererFeature와 동일 방식). Injection Point = After Rendering Transparents 권장.
/// </summary>
public class XRayRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;

    private XRayPass _pass;
    private Material _material;
    private static readonly int XRayAmountID = Shader.PropertyToID("_XRayAmount");

    public override void Create()
    {
        var shader = Shader.Find("Custom/XRay");
        if (shader == null)
        {
            Debug.LogWarning("[XRayRendererFeature] Custom/XRay 셰이더를 찾을 수 없습니다.");
            return;
        }
        _material = CoreUtils.CreateEngineMaterial(shader);
        _pass = new XRayPass(_material) { renderPassEvent = injectionPoint };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null || _material == null) return;
        if (renderingData.cameraData.cameraType != CameraType.Game) return;
        // 유물 미발동 시 합성 자체를 건너뛴다.
        if (Shader.GetGlobalFloat(XRayAmountID) <= 0.0001f) return;
        _pass.renderPassEvent = injectionPoint;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
        if (_material != null) CoreUtils.Destroy(_material);
    }

    // ============================================================
    private class XRayPass : ScriptableRenderPass
    {
        private readonly Material _material;
        private RTHandle _temp;

        public XRayPass(Material material) { _material = material; }

        // ── RenderGraph 경로 (URP 17 기본) ──
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            var source = resourceData.activeColorTexture;

            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;

            var dest = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_XRayTarget", false);

            var blitParams = new RenderGraphUtils.BlitMaterialParameters(source, dest, _material, 0);
            renderGraph.AddBlitPass(blitParams, "XRay Invert");

            // 합성 결과를 새 카메라 컬러로 스왑(이후 패스가 이걸 사용).
            resourceData.cameraColor = dest;
        }

        // ── Legacy(Compatibility Mode) 경로 ──
        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null) return;

            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _temp, desc, FilterMode.Bilinear, name: "_XRayTemp");

            var cmd = CommandBufferPool.Get("XRay");
            var camColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            Blitter.BlitCameraTexture(cmd, camColor, _temp);                  // 원본 복사
            Blitter.BlitCameraTexture(cmd, _temp, camColor, _material, 0);    // 반전 합성 후 되돌림
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) { }

        public void Dispose()
        {
            _temp?.Release();
        }
    }
}
