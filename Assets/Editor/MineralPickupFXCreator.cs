using UnityEngine;
using UnityEditor;

/// <summary>
/// Tools > Sap-Sap > Create Mineral Pickup FX 메뉴로 파티클 프리팹을 생성한다.
/// </summary>
public static class MineralPickupFXCreator
{
    private const string SAVE_PATH = "Assets/Prefabs/Particles/MineralPickupFX.prefab";

    [MenuItem("Tools/Sap-Sap/Create Mineral Pickup FX")]
    static void CreateFX()
    {
        var go = new GameObject("MineralPickupFX");
        var ps = go.AddComponent<ParticleSystem>();
        var rend = go.GetComponent<ParticleSystemRenderer>();

        // ── Main ──────────────────────────────────────────────────────────
        var main = ps.main;
        main.duration          = 0.3f;
        main.loop              = false;
        main.startLifetime     = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed        = new ParticleSystem.MinMaxCurve(1.5f,  3.5f);
        main.startSize         = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
        main.startRotation     = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.startColor        = new ParticleSystem.MinMaxGradient(
                                     new Color(1f, 0.95f, 0.45f, 1f),
                                     new Color(1f, 1f,    1f,    1f));
        main.gravityModifier   = 0.6f;
        main.simulationSpace   = ParticleSystemSimulationSpace.World;
        main.maxParticles      = 16;
        main.stopAction        = ParticleSystemStopAction.Destroy; // 재생 끝나면 자동 제거
        main.playOnAwake       = true;

        // ── Emission: 단발 버스트 ─────────────────────────────────────────
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });

        // ── Shape: 작은 원 확산 ───────────────────────────────────────────
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.12f;

        // ── Color Over Lifetime: 알파 페이드 아웃 ─────────────────────────
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 0.8f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // ── Renderer: 픽셀 아트용 설정 ────────────────────────────────────
        rend.renderMode    = ParticleSystemRenderMode.Billboard;
        rend.sortingOrder  = 10; // 광물 위에 렌더
        rend.minParticleSize = 0f;
        rend.maxParticleSize = 0.5f;

        // ── 저장 ─────────────────────────────────────────────────────────
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, SAVE_PATH);
        Object.DestroyImmediate(go);
        AssetDatabase.Refresh();

        Debug.Log($"[MineralPickupFXCreator] 프리팹 생성 완료: {SAVE_PATH}");
        EditorGUIUtility.PingObject(prefab);
        Selection.activeObject = prefab;
    }
}
