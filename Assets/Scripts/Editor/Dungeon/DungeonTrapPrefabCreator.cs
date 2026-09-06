// @tags: dungeon, trap, prefab, editor, tool
using UnityEditor;
using UnityEngine;
using Gameplay.Dungeon.Traps;

/// <summary>
/// 던전 함정 프리팹 껍데기를 콜라이더/Rigidbody2D 세팅까지 맞춰 일괄 생성하는 에디터 툴.
/// 메뉴: Tools > Dungeon > Create Trap Prefabs.
/// 생성 후 각 프리팹의 스프라이트와 파라미터(주기·데미지·이동량 등)를 인스펙터에서 지정한다.
/// DartTrap의 projectilePrefab은 생성된 DartProjectile로 자동 배선된다.
/// </summary>
public static class DungeonTrapPrefabCreator
{
    private const string Dir = "Assets/Prefabs/Dungeon/Traps";

    [MenuItem("Tools/Dungeon/Create Trap Prefabs")]
    public static void CreateAll()
    {
        EnsureFolder();

        // 다트 투사체(트리거) — DartTrap이 참조하므로 먼저 생성.
        var dartProjectile = CreatePrefab("DartProjectile", go =>
        {
            AddTriggerCollider(go);
            go.AddComponent<SpriteRenderer>();
            go.AddComponent<DartProjectile>();
        });
        var dartProjectileComp = dartProjectile != null ? dartProjectile.GetComponent<DartProjectile>() : null;

        // 개폐식 가시(트리거)
        CreatePrefab("RetractingSpike", go =>
        {
            AddTriggerCollider(go);
            go.AddComponent<SpriteRenderer>();
            go.AddComponent<RetractingSpike>();
        });

        // 압력판(트리거)
        CreatePrefab("PressurePlate", go =>
        {
            AddTriggerCollider(go);
            go.AddComponent<SpriteRenderer>();
            go.AddComponent<PressurePlate>();
        });

        // 문(솔리드)
        CreatePrefab("DungeonGate", go =>
        {
            AddSolidCollider(go);
            go.AddComponent<SpriteRenderer>();
            go.AddComponent<DungeonGate>();
        });

        // 압쇄 블록(솔리드 + Kinematic Rigidbody2D — transform 이동으로 충돌 이벤트 발생시키기 위함)
        CreatePrefab("CrusherBlock", go =>
        {
            AddSolidCollider(go);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            // 빠른 낙하(dropSpeed) 시 플레이어를 뚫고 지나가는 터널링 방지.
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            go.AddComponent<SpriteRenderer>();
            go.AddComponent<CrusherBlock>();
        });

        // 무너지는 발판(솔리드, 밟는 발판)
        CreatePrefab("CollapsingPlatform", go =>
        {
            AddSolidCollider(go);
            go.AddComponent<SpriteRenderer>();
            go.AddComponent<CollapsingPlatform>();
        });

        // 다트 발사기(콜라이더 없음) — projectilePrefab 자동 배선
        CreatePrefab("DartTrap", go =>
        {
            go.AddComponent<SpriteRenderer>();
            var trap = go.AddComponent<DartTrap>();
            if (dartProjectileComp != null)
            {
                var so = new SerializedObject(trap);
                so.FindProperty("projectilePrefab").objectReferenceValue = dartProjectileComp;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        });

        AssetDatabase.SaveAssets();
        Debug.Log($"[Dungeon] 함정 프리팹 7종 생성 완료: {Dir}\n" +
                  "→ 각 프리팹의 Sprite와 파라미터를 인스펙터에서 지정하세요. " +
                  "타일셋 SO objectMappings에 심볼(!,>,C,_,P,G 등)로 매핑하면 임포터가 배치합니다.");
    }

    private static GameObject CreatePrefab(string name, System.Action<GameObject> configure)
    {
        string path = $"{Dir}/{name}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            Debug.LogWarning($"[Dungeon] '{path}' 이미 존재 — 건너뜀(덮어쓰지 않음).");
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        var go = new GameObject(name);
        configure(go);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    private static void AddTriggerCollider(GameObject go)
    {
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = Vector2.one;
    }

    private static void AddSolidCollider(GameObject go)
    {
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = false;
        col.size = Vector2.one;
    }

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(Dir)) return;
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Dungeon"))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Dungeon");
        AssetDatabase.CreateFolder("Assets/Prefabs/Dungeon", "Traps");
    }
}
