using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.SceneManagement;

public class FogOfWarFeature : ScriptableRendererFeature
{
    class FogOfWarPass : ScriptableRenderPass
    {
        // 이 패스에서 사용할 머티리얼
        private Material material;
        private static readonly int PlayerScreenPosID = Shader.PropertyToID("_PlayerScreenPos");

        public FogOfWarPass(Material material)
        {
            this.material = material;
            // 렌더링 시점을 지정합니다. 화면에 모든 것이 그려진 후(AfterRenderingTransparents)에 이 효과를 적용합니다.
            this.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        // 이 메서드는 URP에 의해 매 프레임 호출됩니다 (Compatibility Mode용)
        [System.Obsolete("Use RecordRenderGraph instead. This method is kept for compatibility with older URP versions.")]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Debug.Log($"FogOfWarPass.Execute 호출됨 (Frame {Time.frameCount})");
            
            if (material == null)
            {
                Debug.LogWarning("FogOfWarPass: Material이 null입니다. FogOfWarFeature의 Material을 할당해주세요.");
                return;
            }

            // CommandBuffer를 가져옵니다.
            CommandBuffer cmd = CommandBufferPool.Get("FogOfWar");
            Debug.Log($"FogOfWarPass: CommandBuffer 생성 완료 (Frame {Time.frameCount})");

            // FogOfWarController에서 설정한 플레이어 위치를 가져옵니다
            Vector4 playerPos = material.GetVector(PlayerScreenPosID);
            Debug.Log($"FogOfWarPass: Material에서 플레이어 위치 가져옴: {playerPos} (Frame {Time.frameCount})");
            
            // Global property가 설정되어 있으면 그것을 사용
            try
            {
                playerPos = Shader.GetGlobalVector(PlayerScreenPosID);
                Debug.Log($"FogOfWarPass: Global property에서 플레이어 위치 가져옴: {playerPos} (Frame {Time.frameCount})");
            }
            catch
            {
                // Global property가 없으면 머티리얼의 값을 사용
                playerPos = material.GetVector(PlayerScreenPosID);
                Debug.Log($"FogOfWarPass: Global property 없음, Material 값 사용: {playerPos} (Frame {Time.frameCount})");
            }
            
            // 머티리얼 프로퍼티를 명시적으로 설정 (렌더링 직전에)
            material.SetVector(PlayerScreenPosID, playerPos);
            Debug.Log($"FogOfWarPass: 머티리얼 프로퍼티 설정 완료 (Frame {Time.frameCount})");

            // 렌더링 소스(카메라가 렌더링한 화면)와 목적지를 가져옵니다.
            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var destination = renderingData.cameraData.renderer.cameraColorTargetHandle;
            Debug.Log($"FogOfWarPass: 렌더 타겟 가져옴 - Source: {source.nameID}, Destination: {destination.nameID} (Frame {Time.frameCount})");

            // URP에서 Blit을 사용할 때는 임시 RenderTexture를 사용하는 것이 더 안정적입니다
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0; // 깊이 버퍼는 필요 없음
            Debug.Log($"FogOfWarPass: RenderTexture Descriptor 생성 완료 - {descriptor.width}x{descriptor.height} (Frame {Time.frameCount})");
            
            int tempRT = Shader.PropertyToID("_FogOfWarTemp");
            
            // 임시 RenderTexture를 생성
            cmd.GetTemporaryRT(tempRT, descriptor);
            Debug.Log($"FogOfWarPass: 임시 RenderTexture 생성 완료 (Frame {Time.frameCount})");
            
            // 소스를 임시 텍스처로 Blit (셰이더 적용)
            cmd.Blit(source, tempRT, material);
            Debug.Log($"FogOfWarPass: 첫 번째 Blit 완료 (source -> tempRT) (Frame {Time.frameCount})");
            
            // 임시 텍스처를 목적지로 Blit (셰이더 없이 복사)
            cmd.Blit(tempRT, destination);
            Debug.Log($"FogOfWarPass: 두 번째 Blit 완료 (tempRT -> destination) (Frame {Time.frameCount})");
            
            // 임시 텍스처를 해제
            cmd.ReleaseTemporaryRT(tempRT);
            Debug.Log($"FogOfWarPass: 임시 RenderTexture 해제 완료 (Frame {Time.frameCount})");

            // CommandBuffer를 실행하고 해제합니다.
            context.ExecuteCommandBuffer(cmd);
            Debug.Log($"FogOfWarPass: CommandBuffer 실행 완료 (Frame {Time.frameCount})");
            CommandBufferPool.Release(cmd);
            Debug.Log($"FogOfWarPass: CommandBuffer 해제 완료 (Frame {Time.frameCount})");
        }

        // RenderGraph를 사용하는 경우를 위한 메서드
        // 참고: RenderGraph API는 Unity 버전에 따라 다를 수 있습니다.
        // Compatibility Mode를 사용하는 것을 권장합니다 (Edit > Project Settings > Graphics > URP)
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Debug.LogWarning($"FogOfWarPass.RecordRenderGraph 호출됨 (Frame {Time.frameCount}) - RenderGraph 모드가 활성화되어 있습니다! Compatibility Mode를 사용하도록 설정하세요.");
            // RenderGraph 구현이 복잡하므로, Compatibility Mode에서 Execute 메서드를 사용하도록 권장합니다.
            // RenderGraph를 사용하려면 Unity 버전에 맞는 API를 사용해야 합니다.
            // 여기서는 빈 구현으로 두고, Execute 메서드를 사용하도록 합니다.
            // Unity가 RenderGraph를 사용하지 않으면 자동으로 Execute 메서드가 호출됩니다.
        }

    }

    [System.Serializable]
    public class FogOfWarSettings
    {
        [Tooltip("Fog of War 효과에 사용될 머티리얼 (비어있으면 자동으로 찾습니다)")]
        public Material material = null;
    }

    public FogOfWarSettings settings = new FogOfWarSettings();
    private FogOfWarPass fogOfWarPass;

    // Unity가 이 렌더러 기능을 생성할 때 호출됩니다.
    public override void Create()
    {
        Debug.Log("FogOfWarFeature.Create() 호출됨");
        
        // 머티리얼이 할당되지 않았으면 자동으로 찾기
        if (settings.material == null)
        {
            Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
            foreach (Material mat in materials)
            {
                if (mat.name == "FogMaterial" && mat.shader.name == "Custom/FogOfWar")
                {
                    settings.material = mat;
                    Debug.Log("FogOfWarFeature: FogMaterial을 자동으로 찾았습니다.");
                    break;
                }
            }
        }
        
        if (settings.material == null)
        {
            Debug.LogError("FogOfWarFeature: Material을 찾을 수 없습니다!");
            return;
        }
        
        fogOfWarPass = new FogOfWarPass(settings.material);
        Debug.Log($"FogOfWarFeature: Pass 생성 완료. Material: {settings.material.name}");
    }

    // Unity가 카메라에 렌더러를 추가할 때 호출됩니다.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Scene View 카메라에는 효과를 적용하지 않음
        if (renderingData.cameraData.isSceneViewCamera)
        {
            return;
        }

        // 특정 씬("khbScene 1")에서만 효과 적용
        if (SceneManager.GetActiveScene().name != "khbScene 1")
        {
            return;
        }

        // 디버깅: AddRenderPasses가 호출되는지 확인
        Debug.Log("FogOfWarFeature.AddRenderPasses 호출됨");
        
        if (settings.material == null)
        {
            Debug.LogWarningFormat("Fog of War 머티리얼이 할당되지 않았습니다.");
            return;
        }
        
        if (fogOfWarPass == null)
        {
            Debug.LogWarning("FogOfWarFeature: fogOfWarPass가 null입니다. Create()가 호출되었는지 확인하세요.");
            return;
        }
        
        Debug.Log("FogOfWarFeature: Pass를 Enqueue합니다.");
        renderer.EnqueuePass(fogOfWarPass);
    }
}