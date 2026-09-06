// @tags: editor, vfx, particle, cauldron, prefab, generator
using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 도깨비 가마솥용 "부글부글" ParticleSystem 프리팹과 가마솥 Object 프리팹 골격을 코드로 생성하는 에디터 유틸.
///
/// 메뉴:
///   Tools/Dokkaebi Cauldron/1. Create Bubble VFX   — 부글부글 파티클 프리팹 생성/갱신
///   Tools/Dokkaebi Cauldron/2. Build Cauldron Prefab — 가마솥 Object 프리팹 조립(컴포넌트·참조 자동 연결)
///
/// 생성 결과:
///   - Assets/Prefabs/VFX/CauldronBubbleFX.prefab        (부글부글 파티클)
///   - Assets/Prefabs/VFX/CauldronBubbleMat.mat
///   - Assets/Prefabs/VFX/CauldronBubbleSoft.png         (소프트 원형 텍스처)
///   - Assets/Prefabs/SpecialChunks/Object/DokkaebiCauldron.prefab (가마솥 본체)
///
/// 사용법:
///   1) 메뉴 1번 실행 → 부글부글 파티클 생성
///   2) 메뉴 2번 실행 → 가마솥 프리팹 골격 생성(부글부글 자식 포함, 참조 자동 연결)
///   3) 가마솥 프리팹을 열어 SpriteRenderer에 가마솥 이미지, explosiveHazardPrefab/ashMineral 할당
///   4) World 특수청크 프리팹에 이 가마솥을 자식으로 배치 후 SpecialChunkManager 풀에 등록
/// </summary>
public static class DokkaebiCauldronVFXCreator
{
    private const string VfxFolder    = "Assets/Prefabs/VFX";
    private const string ObjFolder    = "Assets/Prefabs/SpecialChunks/Object";
    private const string BubblePrefab = VfxFolder + "/CauldronBubbleFX.prefab";
    private const string BubbleMat    = VfxFolder + "/CauldronBubbleMat.mat";
    private const string BubbleTex    = VfxFolder + "/CauldronBubbleSoft.png";
    private const string CauldronPath = ObjFolder + "/DokkaebiCauldron.prefab";

    // 어둠 오버레이(기본 정렬레이어 order 999) 위로 올리기 위한 정렬 순서
    private const string SortLayer   = "Default";
    private const int    BubbleOrder = 1001;

    // ──────────────────────────────────────────────────────────────
    // 1. 부글부글 파티클
    // ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/Dokkaebi Cauldron/1. Create Bubble VFX")]
    public static GameObject CreateBubbleVFX()
    {
        EnsureFolder(VfxFolder);

        Texture2D soft = EnsureSoftTexture();
        Material   mat = CreateOrUpdateBubbleMaterial(soft);

        var root = new GameObject("CauldronBubbleFX");
        var ps   = root.AddComponent<ParticleSystem>();
        ConfigureBubbles(ps, mat);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, BubblePrefab);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[DokkaebiCauldronVFXCreator] 부글부글 파티클 생성 완료: {BubblePrefab}");
        return prefab;
    }

    // 작은 거품들이 바닥에서 솟아 올라 팝 — 끓는 느낌
    private static void ConfigureBubbles(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.duration        = 1.0f;
        main.loop            = true;
        main.playOnAwake     = false; // DokkaebiCauldron.Anim이 Play/Stop 제어
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.5f, 1.0f); // 위로 솟음
        main.startSize       = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.gravityModifier = 0f;
        main.startColor      = new ParticleSystem.MinMaxGradient(new Color(0.65f, 1f, 0.7f)); // 연두 끓는 빛
        main.maxParticles    = 40;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = 14f;

        // 바닥의 좁은 박스에서 방출
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = new Vector3(0.5f, 0.05f, 0.01f);
        shape.position  = new Vector3(0f, -0.1f, 0f);

        // 위로 떠오르며 살짝 좌우로.
        // 주의: x/y/z MinMaxCurve는 모두 같은 모드여야 함("Velocity curves must all be in the same mode").
        // x,y를 TwoConstants로 쓰므로 z도 TwoConstants(0,0)로 맞춘다.
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vel.y = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        // 알파 0→1→0 (떠오르며 나타났다 팝)
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.4f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // 크기 0→1→1.2 (부풀다 팝)
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.2f), new Keyframe(0.6f, 1f), new Keyframe(1f, 1.2f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode       = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial   = mat;
        r.sortingLayerName = SortLayer;
        r.sortingOrder     = BubbleOrder;
    }

    // ──────────────────────────────────────────────────────────────
    // 2. 가마솥 Object 프리팹 조립
    // ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/Dokkaebi Cauldron/2. Build Cauldron Prefab")]
    public static void BuildCauldronPrefab()
    {
        EnsureFolder(ObjFolder);

        // 부글부글 프리팹 보장
        var bubblePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BubblePrefab);
        if (bubblePrefab == null) bubblePrefab = CreateBubbleVFX();

        // 루트
        var root = new GameObject("DokkaebiCauldron");
        var body = root.AddComponent<SpriteRenderer>();
        body.color = Color.white;

        // 상호작용 감지용 트리거 콜라이더 (PlayerInteractor는 플레이어 콜라이더와의 오버랩으로 탐지).
        // 넉넉한 크기로 두어 가마솥 근처에서 E가 먹히게 한다. 지형 막힘은 World 청크 지형이 담당.
        var col = root.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(2.5f, 2.5f);

        var cauldron = root.AddComponent<DokkaebiCauldron>();

        // 자식: SpawnPoint
        var spawnPoint = new GameObject("SpawnPoint");
        spawnPoint.transform.SetParent(root.transform, false);
        spawnPoint.transform.localPosition = new Vector3(0f, 0.5f, 0f);

        // 자식: BubbleFX (프리팹 인스턴스)
        var bubbleGo = (GameObject)PrefabUtility.InstantiatePrefab(bubblePrefab);
        bubbleGo.name = "BubbleFX";
        bubbleGo.transform.SetParent(root.transform, false);
        bubbleGo.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        var bubblePs = bubbleGo.GetComponent<ParticleSystem>();

        // 직렬화 필드 자동 연결 (private [SerializeField]는 SerializedObject로 세팅)
        var so = new SerializedObject(cauldron);
        SetRef(so, "bodyRenderer", body);
        SetRef(so, "spriteRenderer", body);     // InteractableBlockBase 근접 하이라이트용 (동일 렌더러)
        SetRef(so, "spawnPoint", spawnPoint.transform);
        SetRef(so, "bubbleFx", bubblePs);
        SetColor(so, "idleColor", Color.white); // 평상시 원래 스프라이트 색 유지
        SetString(so, "promptText", "광물 제련");
        so.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, CauldronPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[DokkaebiCauldronVFXCreator] 가마솥 프리팹 생성 완료: {CauldronPath}\n" +
                  $"→ SpriteRenderer에 가마솥 이미지, explosiveHazardPrefab/ashMineral을 할당하세요.\n" +
                  $"→ World 특수청크 프리팹에 자식으로 배치 후 SpecialChunkManager 풀에 등록하세요.");
    }

    // ──────────────────────────────────────────────────────────────
    // 헬퍼
    // ──────────────────────────────────────────────────────────────
    private static void SetRef(SerializedObject so, string prop, Object value)
    {
        var p = so.FindProperty(prop);
        if (p != null) p.objectReferenceValue = value;
        else Debug.LogWarning($"[DokkaebiCauldronVFXCreator] 직렬화 필드 '{prop}'를 찾지 못했습니다.");
    }

    private static void SetColor(SerializedObject so, string prop, Color value)
    {
        var p = so.FindProperty(prop);
        if (p != null) p.colorValue = value;
    }

    private static void SetString(SerializedObject so, string prop, string value)
    {
        var p = so.FindProperty(prop);
        if (p != null) p.stringValue = value;
    }

    /// <summary>소프트 원형(거품) 텍스처 PNG를 만들거나 로드한다.</summary>
    private static Texture2D EnsureSoftTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(BubbleTex);
        if (existing != null) return existing;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - c) / c;
            float dy = (y - c) / c;
            float d  = Mathf.Sqrt(dx * dx + dy * dy); // 0(중앙)~1(가장자리)
            float a  = Mathf.Clamp01(1f - d);
            a = a * a; // 가장자리 부드럽게
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();

        string absPath = Path.Combine(Application.dataPath, "Prefabs/VFX/CauldronBubbleSoft.png");
        File.WriteAllBytes(absPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(BubbleTex);
        var importer = AssetImporter.GetAtPath(BubbleTex) as TextureImporter;
        if (importer != null)
        {
            importer.textureType         = TextureImporterType.Default;
            importer.alphaSource         = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode            = TextureWrapMode.Clamp;
            importer.mipmapEnabled       = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(BubbleTex);
    }

    /// <summary>알파블렌드 머티리얼을 만들거나 갱신한다.</summary>
    private static Material CreateOrUpdateBubbleMaterial(Texture2D baseMap)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(BubbleMat);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");

            mat = new Material(shader) { name = "CauldronBubbleMat" };
            AssetDatabase.CreateAsset(mat, BubbleMat);
        }

        // 알파 블렌드 (거품은 반투명)
        SetIfHas(mat, "_Surface", 1f);   // Transparent
        SetIfHas(mat, "_Blend",   0f);   // Alpha
        SetIfHas(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        SetIfHas(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        SetIfHas(mat, "_ZWrite",   0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

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

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf   = Path.GetFileName(folder);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
