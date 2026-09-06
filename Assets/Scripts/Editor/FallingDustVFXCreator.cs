// @tags: editor, vfx, particle, special-chunk, falling, dust, stalactite
using UnityEngine;
using UnityEditor;

/// <summary>
/// 종유석 낙하 예고용 "돌가루" ParticleSystem 프리팹을 코드로 생성하는 에디터 유틸.
///
/// 메뉴: Tools/Special Chunks/Create Falling Dust VFX
/// 생성 결과: Assets/Prefabs/VFX/FallingDustWarning.prefab (+ FallingDustMat.mat)
///
/// 사용법:
///   1) 메뉴 실행 → 프리팹 생성
///   2) 종유석 프리팹(StalactiteTrap / IcicleHazard)을 Prefab Edit 모드로 열고,
///      이 돌가루 프리팹을 자식으로 배치(종유석 끝/아래 지점).
///   3) FallingHazardBase의 Warning Particle 슬롯에 자식 ParticleSystem을 할당.
///
/// 외형/방향은 생성 후 인스펙터에서 자유롭게 미세조정 가능.
/// </summary>
public static class FallingDustVFXCreator
{
    private const string VfxFolder  = "Assets/Prefabs/VFX";
    private const string PrefabPath = VfxFolder + "/FallingDustWarning.prefab";
    private const string MatPath    = VfxFolder + "/FallingDustMat.mat";

    [MenuItem("Tools/Special Chunks/Create Falling Dust VFX")]
    public static void CreateFallingDustVFX()
    {
        EnsureFolder(VfxFolder);

        // ── 머티리얼 (URP → 폴백 순으로 셰이더 탐색) ──
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");

            mat = new Material(shader) { name = "FallingDustMat" };
            AssetDatabase.CreateAsset(mat, MatPath);
        }

        // ── 임시 GameObject + ParticleSystem 구성 ──
        var go = new GameObject("FallingDustWarning");
        var ps = go.AddComponent<ParticleSystem>();

        // Main: 짧은 1회 버스트, 코드에서 Play() 호출하므로 PlayOnAwake off
        var main = ps.main;
        main.duration        = 1.0f;
        main.loop            = false;
        main.playOnAwake     = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.gravityModifier = 0.9f;   // 2D 중력처럼 아래로 흘러내림
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color32(125, 105, 85, 255),   // 밝은 돌가루(갈색기)
                                   new Color32(85, 72, 58, 255));    // 어두운 돌가루
        main.maxParticles    = 64;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // 종유석이 움직여도 먼지는 제자리 낙하

        // Emission: rate 0 + 시작 시 burst 1회
        var emission = ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

        // Shape: 아래로 향한 좁은 콘(약간 흩어짐)
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = 18f;
        shape.radius    = 0.12f;
        shape.rotation  = new Vector3(90f, 0f, 0f); // 콘 축을 -Y(아래)로

        // 수명 동안 서서히 투명해지며 사라짐
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // 렌더러: 빌보드 + 위 머티리얼
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode    = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = mat;
        renderer.sortingOrder  = 50; // 지형 위에 보이도록(프로젝트 정렬 기준에 맞게 조정)

        // ── 프리팹으로 저장 ──
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[FallingDustVFXCreator] 돌가루 예고 파티클 생성 완료: {PrefabPath}\n" +
                  $"종유석 프리팹 자식으로 배치 후 Warning Particle 슬롯에 할당하세요.");
    }

    /// <summary>경로의 모든 중간 폴더를 보장 생성한다.</summary>
    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf   = System.IO.Path.GetFileName(folder);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
