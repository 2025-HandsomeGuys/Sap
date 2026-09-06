// @tags: editor, vfx, particle, mineral, rock, glow, sparkle
using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 발광 광물돌(잼바위)용 "스파클" ParticleSystem 프리팹을 코드로 생성하는 에디터 유틸.
/// 코어키퍼 광물돌처럼 자체발광(Additive)하는 반짝임을 만든다.
///
/// 메뉴: Tools/Mineral Rock/Create Sparkle VFX
/// 생성 결과:
///   - Assets/Prefabs/VFX/MineralSparkle.prefab   (스파클 트윙클)
///   - Assets/Prefabs/VFX/MineralSparkleAdditive.mat
///   - Assets/Prefabs/VFX/MineralGlowRadial.png   (소프트 원형 텍스처)
///
/// [어둠 위에 그리기] PlayerVisionOverlay 어둠 캔버스가 기본 정렬레이어 order 999 에 그려진다.
/// 그래서 스파클 렌더러를 기본 정렬레이어 order 1000+ 로 올려 어둠 위에 표시한다.
/// 이렇게 해야 어두운 동굴에서 광물돌이 "빛나는" 느낌이 난다.
///
/// 사용법:
///   1) 메뉴 실행 → 프리팹 생성/갱신
///   2) 광물돌 프리팹을 Prefab Edit 모드로 열고, 이 MineralSparkle 프리팹을 자식으로 배치
///   3) MineralRockGlow 컴포넌트의 sparkleSystem 에 할당
///      (발광색은 MineralRockGlow.glowColor 가 런타임에 startColor 로 덮어쓴다)
/// </summary>
public static class MineralSparkleVFXCreator
{
    private const string VfxFolder   = "Assets/Prefabs/VFX";
    private const string PrefabPath  = VfxFolder + "/MineralSparkle.prefab";
    private const string MatPath     = VfxFolder + "/MineralSparkleAdditive.mat";
    private const string TexPath     = VfxFolder + "/MineralGlowRadial.png";

    // 어둠 오버레이(기본 정렬레이어 order 999) 위로 올리기 위한 정렬 순서
    private const string SortLayer    = "Default";
    private const int    SparkleOrder = 1001; // 어둠 오버레이(999) 위

    [MenuItem("Tools/Mineral Rock/Create Sparkle VFX")]
    public static void CreateMineralSparkleVFX()
    {
        EnsureFolder(VfxFolder);

        Texture2D radial = EnsureRadialTexture();
        Material   mat    = CreateOrUpdateAdditiveMaterial(radial);

        // ── 스파클(트윙클) ──
        var root    = new GameObject("MineralSparkle");
        var sparkle = root.AddComponent<ParticleSystem>();
        ConfigureSparkle(sparkle, mat);

        // ── 프리팹으로 저장 ──
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[MineralSparkleVFXCreator] 광물돌 스파클 생성 완료: {PrefabPath}\n" +
                  $"광물돌 프리팹 자식으로 배치 후 MineralRockGlow 의 sparkleSystem 에 할당하세요.");
    }

    // ── 스파클: 작은 점들이 깜빡이는 트윙클 ──
    private static void ConfigureSparkle(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.duration        = 1.0f;
        main.loop            = true;
        main.playOnAwake     = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0f, 0.05f); // 거의 제자리
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
        main.gravityModifier = 0f;
        main.startColor      = new ParticleSystem.MinMaxGradient(Color.white); // 런타임에 glowColor 로 덮어씀
        main.maxParticles    = 24;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = 5f;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = new Vector3(0.6f, 0.6f, 0.01f); // 돌 크기에 맞게 인스펙터에서 조정

        ApplyTwinkleCurves(ps, alphaPeak: 1.0f);

        SetupRenderer(ps, mat, SparkleOrder);
    }

    // Color/Size over Lifetime: alpha 0→peak→0, size 0→1→0 (부드러운 깜빡임·페이드)
    private static void ApplyTwinkleCurves(ParticleSystem ps, float alphaPeak)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alphaPeak, 0.5f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
    }

    private static void SetupRenderer(ParticleSystem ps, Material mat, int order)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode       = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial   = mat;
        r.sortingLayerName = SortLayer;
        r.sortingOrder     = order; // 어둠 오버레이(999) 위
    }

    /// <summary>소프트 원형(라디얼) 텍스처 PNG를 만들거나 로드한다. 헤일로/스파클을 둥글게 보이게 함.</summary>
    private static Texture2D EnsureRadialTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        if (existing != null) return existing;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        float maxR = c;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - c) / maxR;
            float dy = (y - c) / maxR;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);     // 0(중앙)~1(가장자리)
            float a  = Mathf.Clamp01(1f - d);
            a = a * a;                                     // 가장자리 부드럽게
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();

        string absPath = Path.Combine(Application.dataPath, "Prefabs/VFX/MineralGlowRadial.png");
        File.WriteAllBytes(absPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(TexPath);
        var importer = AssetImporter.GetAtPath(TexPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType        = TextureImporterType.Default;
            importer.alphaSource        = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode           = TextureWrapMode.Clamp;
            importer.mipmapEnabled      = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
    }

    /// <summary>Additive 블렌딩 머티리얼을 만들거나(있으면) 갱신한다. 매 실행 idempotent.</summary>
    private static Material CreateOrUpdateAdditiveMaterial(Texture2D baseMap)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Additive");

            mat = new Material(shader) { name = "MineralSparkleAdditive" };
            AssetDatabase.CreateAsset(mat, MatPath);
        }

        // Additive 강제: Src=One, Dst=One, ZWrite off, Transparent 큐
        SetIfHas(mat, "_Surface", 1f);   // 0 Opaque / 1 Transparent
        SetIfHas(mat, "_Blend",   2f);   // 0 Alpha / 1 Premultiply / 2 Additive / 3 Multiply
        SetIfHas(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        SetIfHas(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        SetIfHas(mat, "_ZWrite",   0f);

        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        // 소프트 원형 텍스처 (URP _BaseMap / 레거시 _MainTex 둘 다 시도)
        if (baseMap != null)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", baseMap);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", baseMap);
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void SetIfHas(Material mat, string prop, float value)
    {
        if (mat.HasProperty(prop)) mat.SetFloat(prop, value);
    }

    /// <summary>경로의 모든 중간 폴더를 보장 생성한다.</summary>
    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf   = Path.GetFileName(folder);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, leaf);
    }
}
