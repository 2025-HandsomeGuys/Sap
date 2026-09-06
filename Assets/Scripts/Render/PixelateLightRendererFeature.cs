using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// URP 2D 라이트 텍스처(_ShapeLightTexture)만 픽셀화.
/// 오브젝트/플레이어는 원본 해상도 유지, 빛만 블럭 픽셀 느낌.
/// 타이밍: BeforeRenderingOpaques (라이트 텍스처 생성 후, 오브젝트 렌더 전)
/// </summary>
public class PixelateLightRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("픽셀화 강도. 1=원본, 4=1/4 해상도 (큰 블럭)")]
        [Range(1, 16)]
        public int pixelScale = 4;

        [Tooltip("픽셀화할 URP 2D 라이트 레이어 수 (ShapeLightTexture0부터)")]
        [Range(1, 4)]
        public int lightLayerCount = 1;
    }

    public Settings settings = new Settings();

    private PixelatePass _pass;
    private Material _material;

    public override void Create()
    {
        var shader = Shader.Find("Custom/PixelateLight");
        if (shader == null)
        {
            Debug.LogWarning("[PixelateLightRendererFeature] Custom/PixelateLight 셰이더를 찾을 수 없습니다.");
            return;
        }
        _material = new Material(shader);
        _pass = new PixelatePass(_material, settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null || _material == null) return;
        if (renderingData.cameraData.cameraType != CameraType.Game) return;

        // 라이트 텍스처가 생성된 후, 오브젝트가 렌더되기 전
        _pass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
        if (_material != null) CoreUtils.Destroy(_material);
    }

    // ============================================================
    private class PixelatePass : ScriptableRenderPass
    {
        private readonly Material _material;
        private readonly Settings _settings;

        private RTHandle _lowResRT;
        private RTHandle _pixelatedRT;

        private static readonly string[] LightTexNames =
        {
            "_ShapeLightTexture0",
            "_ShapeLightTexture1",
            "_ShapeLightTexture2",
            "_ShapeLightTexture3",
        };

        public PixelatePass(Material material, Settings settings)
        {
            _material = material;
            _settings = settings;
        }

        // ── RenderGraph 경로 ─────────────────────────────────────
        private class PassData
        {
            internal RTHandle lowResRT;
            internal RTHandle pixelatedRT;
            internal Material material;
            internal int layerCount;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            // 저해상도 RT 할당/갱신
            var lowDesc  = desc;
            lowDesc.width  = Mathf.Max(1, desc.width  / _settings.pixelScale);
            lowDesc.height = Mathf.Max(1, desc.height / _settings.pixelScale);
            RenderingUtils.ReAllocateHandleIfNeeded(ref _lowResRT,    lowDesc, FilterMode.Point, name: "_LightLowRes");
            RenderingUtils.ReAllocateHandleIfNeeded(ref _pixelatedRT, desc,    FilterMode.Point, name: "_LightPixelated");

            // UnsafePass: 전역 셰이더 프로퍼티(_ShapeLightTexture) 읽기·쓰기 허용
            using var builder = renderGraph.AddUnsafePass<PassData>("PixelateLight", out var passData);
            passData.lowResRT    = _lowResRT;
            passData.pixelatedRT = _pixelatedRT;
            passData.material    = _material;
            passData.layerCount  = _settings.lightLayerCount;

            builder.AllowPassCulling(false);
            builder.SetRenderFunc((PassData data, UnsafeGraphContext ctx) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                for (int i = 0; i < data.layerCount; i++)
                    PixelateLayer(cmd, LightTexNames[i], data.lowResRT, data.pixelatedRT, data.material);
            });
        }

        // ── Legacy Execute 경로 ──────────────────────────────────
        [System.Obsolete]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            var lowDesc  = desc;
            lowDesc.width  = Mathf.Max(1, desc.width  / _settings.pixelScale);
            lowDesc.height = Mathf.Max(1, desc.height / _settings.pixelScale);
            RenderingUtils.ReAllocateHandleIfNeeded(ref _lowResRT,    lowDesc, FilterMode.Point, name: "_LightLowRes");
            RenderingUtils.ReAllocateHandleIfNeeded(ref _pixelatedRT, desc,    FilterMode.Point, name: "_LightPixelated");
        }

        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null) return;
            var cmd = CommandBufferPool.Get("PixelateLight");
            for (int i = 0; i < _settings.lightLayerCount; i++)
                PixelateLayer(cmd, LightTexNames[i], _lowResRT, _pixelatedRT, _material);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) { }

        // 라이트 텍스처 1개를 픽셀화해서 글로벌로 재설정
        private static void PixelateLayer(
            CommandBuffer cmd, string texName,
            RTHandle lowRes, RTHandle pixelated, Material mat)
        {
            if (lowRes == null || pixelated == null) return;

            var lightTex = Shader.GetGlobalTexture(texName);
            if (lightTex == null) return;

            // 1. 다운샘플 (저해상도 → 블럭 픽셀 정보 압축)
            cmd.Blit(lightTex, lowRes);

            // 2. Point 업샘플 (저해상도 → 원본 크기, 블럭 픽셀 유지)
            Blitter.BlitCameraTexture(cmd, lowRes, pixelated, mat, 0);

            // 3. 픽셀화된 텍스처로 교체 → 이후 오브젝트 렌더가 이걸 사용
            cmd.SetGlobalTexture(texName, pixelated);
        }

        public void Dispose()
        {
            _lowResRT?.Release();
            _pixelatedRT?.Release();
        }
    }
}
