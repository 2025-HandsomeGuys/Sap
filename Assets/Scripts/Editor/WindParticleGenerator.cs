// @tags: editor, wind, particle, generator
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// 에디터 상단 메뉴를 통해 동적 바람 눈보라 파티클 오브젝트를 씬에 생성하는 유틸리티입니다.
/// </summary>
public class WindParticleGenerator : MonoBehaviour
{
    [MenuItem("Tools/Antigravity/Create Blizzard Particle")]
    public static void CreateBlizzardParticle()
    {
        GameObject go = new GameObject("Blizzard_ParticleSystem");
        var ps = go.AddComponent<ParticleSystem>();
        go.AddComponent<WindVisualizer>();

        // Main
        var main = ps.main;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 10f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 500;

        // Emission — 기본값으로도 파티클이 보이도록 0이 아닌 값으로 설정
        // WindVisualizer가 연결되면 이 값을 덮어씀
        var emission = ps.emission;
        emission.rateOverTime = 50f;

        // Shape — 넓은 박스 영역
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(30f, 20f, 1f);

        // Velocity over Lifetime — 기본 왼쪽 바람 방향
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.x = new ParticleSystem.MinMaxCurve(-5f);
        velocity.y = new ParticleSystem.MinMaxCurve(-1f);

        // Color over Lifetime — 페이드 인/아웃
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.8f, 0.2f),
                new GradientAlphaKey(0.8f, 0.8f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = grad;

        // Renderer
        var psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sharedMaterial = GetOrCreateBlizzardMaterial();
        psr.sortingLayerName = "Default";
        psr.sortingOrder = 10;

        Undo.RegisterCreatedObjectUndo(go, "Create Blizzard Particle");
        Selection.activeGameObject = go;
        Debug.Log("[WindParticleGenerator] 블리자드 파티클을 씬에 생성했습니다.");
    }

    private static Material GetOrCreateBlizzardMaterial()
    {
        const string matPath = "Assets/Prefabs/VFX/BlizzardMat.mat";

        var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[WindParticleGenerator] URP Particles/Unlit 셰이더를 찾지 못했습니다. Default-Particle로 대체합니다.");
            return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
        }

        var mat = new Material(shader);
        mat.SetFloat("_Surface", 1f);   // Transparent
        mat.SetFloat("_Blend", 0f);     // Alpha blend
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetColor("_BaseColor", Color.white);

        if (!System.IO.Directory.Exists("Assets/Prefabs/VFX"))
        {
            System.IO.Directory.CreateDirectory("Assets/Prefabs/VFX");
            AssetDatabase.Refresh();
        }

        AssetDatabase.CreateAsset(mat, matPath);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
#endif
