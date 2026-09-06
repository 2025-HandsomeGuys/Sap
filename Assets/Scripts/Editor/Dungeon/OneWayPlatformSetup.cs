using System.IO;
using UnityEditor;
using UnityEngine;
using Gameplay.Dungeon.Authoring;

/// <summary>
/// 일방향 플랫폼(아래에서 점프해 통과 / 위에서는 못 내려옴) 프리팹을 만들고
/// 던전 타일셋의 '=' 심볼에 등록한다. 언제 실행해도 같은 결과가 나오도록 매번 전체를 재구성한다.
///
/// 동작은 전적으로 PlatformEffector2D(useOneWay, surfaceArc=180)가 담당한다 — 전용 스크립트 없음.
/// surfaceArc 180 이므로 '윗면'만 충돌한다 → 옆에서 지나가거나 아래에서 뚫고 올라오는 건 자유.
/// 아래 방향 통과(드롭다운)는 지원하지 않는다. 필요해지면 플레이어 쪽에서
/// 아래+점프 입력에 맞춰 EffectorCollider를 잠깐 무시하는 처리를 추가해야 한다.
///
/// 구조:  루트(BoxCollider2D + PlatformEffector2D, layer=Ground)
///          └─ Visual (SpriteRenderer)   ← 콜라이더 박스와 같은 자리·같은 크기
/// 스프라이트를 루트에 두면 셀 중심(로컬 0)에 그려지는데 콜라이더 윗면은 +0.5라
/// '보이는 판자보다 한참 위를 밟는' 현상이 생긴다. 그래서 자식으로 분리해 맞춘다.
/// </summary>
public static class OneWayPlatformSetup
{
    private const string PrefabPath = "Assets/Prefabs/Dungeon/OneWayPlatform.prefab";
    private const string TilesetPath = "Assets/GameData/Dungeon/DefaultDungeonTileset.asset";
    private const string SpriteDonorPath = "Assets/Prefabs/Dungeon/Traps/CollapsingPlatform.prefab";
    private const string Symbol = "=";
    private const string GroundLayerName = "Ground";
    private const string VisualChildName = "Visual";

    // 발판 콜라이더. 임포터가 프리팹을 '셀 중심'에 놓으므로 로컬 y=0 이 칸 한가운데,
    // 칸 윗변은 +0.5 다. one-way 는 윗면만 충돌에 쓰이므로 두께는 보이는 대로 얇게 두고,
    // 대신 윗면이 정확히 칸 윗변(+0.5)에 오도록 offset 을 잡는다
    //   → offset.y + size.y/2 = 0.375 + 0.125 = 0.5
    // 이래야 옆의 벽(W) 타일 윗면과 높이가 같아 평평하게 걸어 넘어갈 수 있다.
    // 폭은 반드시 1(한 칸 꽉) — 좁히면 = 를 가로로 이어 놨을 때 사이에 구멍이 생긴다.
    private static readonly Vector2 ColliderSize = new Vector2(1f, 0.25f);
    private static readonly Vector2 ColliderOffset = new Vector2(0f, 0.375f);

    // surfaceArc 는 '윗면으로 인정하는' 각도 범위(위쪽 기준 ±arc/2). 180 = 위쪽 절반만 충돌.
    //
    // 한때 이 값을 의심해 120·90 까지 낮춰봤지만 옆으로 못 지나가는 증상은 그대로였다.
    // 진짜 원인은 물리가 아니라 PlayerController.CheckWallContacts 였다 —
    // Collider2D.GetContacts 는 이펙터가 무시한 접촉도 그대로 돌려주기 때문에
    // 발판 옆면이 '급경사 벽'으로 분류돼 수평 이동과 상승 속도가 0으로 깎였다.
    // 그쪽을 고쳤으므로 여기서는 의미 그대로의 기본값을 쓴다.
    private const float SurfaceArc = 180f;

    [MenuItem("Tools/Dungeon/Create One-Way Platform ('=')")]
    public static void Run()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null && CreateEmptyPrefab() == null)
            return;

        Rebuild();   // 이미 있는 프리팹도 매번 교정한다(과거에 잘못 저장된 것 복구)

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null) Register(prefab);
    }

    /// <summary>
    /// 프리팹 내용을 열어 레이어·콜라이더·이펙터·비주얼을 확정하고, 저장 후 다시 읽어 검증한다.
    ///
    /// 왜 GameObject에 대고 바로 대입하지 않고 여기서 다시 쓰는가:
    /// SpriteRenderer가 붙은 오브젝트에 BoxCollider2D를 AddComponent하면 Unity가 콜라이더를
    /// 스프라이트 바운드로 자동 맞춤하면서 직후의 대입값을 덮어쓴다. 실제로 첫 생성분은
    /// usedByEffector=0 / size=(0.19, 0.1) 로 저장돼 '=' 가 사방 막힌 벽이 됐었다.
    /// </summary>
    private static void Rebuild()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // PlayerController.isGrounded 는 groundCollider.IsTouchingLayers(groundLayer) 이고
            // groundLayer 는 Ground(15) 하나만 켜져 있다. Default(0)에 두면 충돌은 하는데
            // 착지 판정이 안 나서 그 위에서 재점프·마찰·지면 스냅이 전부 죽는다.
            int ground = LayerMask.NameToLayer(GroundLayerName);
            if (ground >= 0) root.layer = ground;
            else Debug.LogWarning($"[OneWay] '{GroundLayerName}' 레이어가 없습니다. 프리팹 레이어를 직접 지정하세요.");

            // 스케일은 반드시 1 — 콜라이더 크기 계산의 기준을 하나로 유지한다.
            // 루트에 스케일이 걸려 있으면 size 값과 실제 폭이 어긋나 '한 칸 꽉'을 맞출 수 없다.
            root.transform.localScale = Vector3.one;

            var box = root.GetComponent<BoxCollider2D>() ?? root.AddComponent<BoxCollider2D>();
            var eff = root.GetComponent<PlatformEffector2D>() ?? root.AddComponent<PlatformEffector2D>();

            var sb = new SerializedObject(box);
            sb.FindProperty("m_Size").vector2Value = ColliderSize;
            sb.FindProperty("m_Offset").vector2Value = ColliderOffset;
            sb.FindProperty("m_UsedByEffector").boolValue = true;   // false면 이펙터가 통째로 무시된다
            sb.ApplyModifiedPropertiesWithoutUndo();

            var se = new SerializedObject(eff);
            se.FindProperty("m_UseOneWay").boolValue = true;
            se.FindProperty("m_SurfaceArc").floatValue = SurfaceArc;
            se.FindProperty("m_RotationalOffset").floatValue = 0f;
            se.FindProperty("m_UseSideFriction").boolValue = true;
            se.ApplyModifiedPropertiesWithoutUndo();

            BuildVisual(root, ground);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
        Verify();
    }

    /// <summary>
    /// SpriteRenderer를 'Visual' 자식으로 몰아넣고 콜라이더 박스에 정확히 겹치게 배치한다.
    /// 루트에 렌더러가 남아 있으면(구버전 프리팹) 값을 옮기고 제거한다.
    /// </summary>
    private static void BuildVisual(GameObject root, int layer)
    {
        var visual = root.transform.Find(VisualChildName);
        if (visual == null)
        {
            var go = new GameObject(VisualChildName);
            go.transform.SetParent(root.transform, false);
            visual = go.transform;
        }
        if (layer >= 0) visual.gameObject.layer = layer;

        var target = visual.GetComponent<SpriteRenderer>() ?? visual.gameObject.AddComponent<SpriteRenderer>();

        var legacy = root.GetComponent<SpriteRenderer>();
        if (legacy != null)
        {
            CopyRenderer(legacy, target);
            Object.DestroyImmediate(legacy);   // 루트에는 렌더러를 남기지 않는다
        }
        else if (target.sprite == null)
        {
            CopyLook(target);
        }

        visual.localPosition = new Vector3(ColliderOffset.x, ColliderOffset.y, 0f);
        visual.localScale = FitScale(target.sprite);
    }

    /// <summary>
    /// 스프라이트 원본 크기를 콜라이더 박스에 맞추는 스케일.
    /// 지금 아트는 CollapsingPlatform에서 빌려온 0.19 x 0.1 짜리 임시라 크게 늘어난다
    /// (픽셀이 뭉개지지만 '보이는 것 = 밟는 것'이 되어 배치 검증엔 오히려 낫다).
    /// 전용 아트를 셀 크기(가로 1유닛) 기준으로 그리면 이 값이 1에 가까워진다.
    /// </summary>
    private static Vector3 FitScale(Sprite sprite)
    {
        if (sprite == null) return Vector3.one;
        Vector2 nat = sprite.bounds.size;
        if (nat.x <= 0.0001f || nat.y <= 0.0001f) return Vector3.one;
        return new Vector3(ColliderSize.x / nat.x, ColliderSize.y / nat.y, 1f);
    }

    private static GameObject CreateEmptyPrefab()
    {
        string dir = Path.GetDirectoryName(PrefabPath);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        var go = new GameObject("OneWayPlatform");
        try
        {
            var saved = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Debug.Log($"[OneWay] 프리팹 생성: {PrefabPath}");
            return saved;   // 실제 구성은 Rebuild()가 채운다
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // 전용 아트가 아직 없으므로 CollapsingPlatform 의 겉모습을 빌려온다(임포트 직후 눈에 보이도록).
    // 색만 살짝 다르게 줘서 일반 발판과 구분한다. 아트가 나오면 Visual 의 Sprite를 교체할 것.
    private static void CopyLook(SpriteRenderer target)
    {
        var donor = AssetDatabase.LoadAssetAtPath<GameObject>(SpriteDonorPath);
        var src = donor != null ? donor.GetComponentInChildren<SpriteRenderer>(true) : null;
        if (src == null)
        {
            Debug.LogWarning($"[OneWay] 참고할 스프라이트를 찾지 못했습니다({SpriteDonorPath}). " +
                             $"'{VisualChildName}' 자식에 스프라이트를 직접 지정하세요.");
            return;
        }
        CopyRenderer(src, target);
        target.color = new Color(0.72f, 0.88f, 1f);
    }

    private static void CopyRenderer(SpriteRenderer from, SpriteRenderer to)
    {
        to.sprite = from.sprite;
        to.sharedMaterial = from.sharedMaterial;
        to.color = from.color;
        to.sortingLayerID = from.sortingLayerID;
        to.sortingOrder = from.sortingOrder;
    }

    /// <summary>저장 결과를 디스크에서 다시 읽어 확인한다. 값이 반영됐다고 가정하지 않는다.</summary>
    private static void Verify()
    {
        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var b = saved != null ? saved.GetComponent<BoxCollider2D>() : null;
        var e = saved != null ? saved.GetComponent<PlatformEffector2D>() : null;
        var v = saved != null ? saved.transform.Find(VisualChildName) : null;
        if (b == null || e == null || v == null)
        {
            Debug.LogError($"[OneWay] 저장된 프리팹 구성이 불완전합니다 " +
                           $"(collider={b != null}, effector={e != null}, {VisualChildName}={v != null}).");
            return;
        }

        string state = $"layer={LayerMask.LayerToName(saved.layer)}({saved.layer}), " +
                       $"usedByEffector={b.usedByEffector}, size={b.size}, offset={b.offset}, " +
                       $"useOneWay={e.useOneWay}, surfaceArc={e.surfaceArc}, " +
                       $"visualPos={v.localPosition}, visualScale={v.localScale}";

        bool ok = b.usedByEffector && e.useOneWay
                  && b.size == ColliderSize && b.offset == ColliderOffset
                  && Mathf.Approximately(e.surfaceArc, SurfaceArc)
                  && saved.transform.localScale == Vector3.one
                  && saved.layer == LayerMask.NameToLayer(GroundLayerName);

        if (ok) Debug.Log($"[OneWay] 검증 통과 — {state}");
        else Debug.LogError($"[OneWay] 설정이 반영되지 않았습니다 — {state}");
    }

    private static void Register(GameObject prefab)
    {
        var tileset = AssetDatabase.LoadAssetAtPath<DungeonTilesetSO>(TilesetPath);
        if (tileset == null)
        {
            Debug.LogError($"[OneWay] 타일셋을 찾지 못했습니다: {TilesetPath}");
            return;
        }

        var mapping = tileset.objectMappings.Find(m => m != null && m.symbol == Symbol);
        if (mapping == null)
        {
            mapping = new DungeonTilesetSO.ObjectMapping { symbol = Symbol };
            tileset.objectMappings.Add(mapping);
        }
        mapping.prefab = prefab;
        mapping.zRotation = 0f;

        tileset.BuildLookup();
        EditorUtility.SetDirty(tileset);
        AssetDatabase.SaveAssets();
        Debug.Log($"[OneWay] 타일셋에 '{Symbol}' 심볼 등록 완료 → {TilesetPath}");
    }
}
