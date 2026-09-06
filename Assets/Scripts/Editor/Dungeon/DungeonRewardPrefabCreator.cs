// @tags: dungeon, reward, prefab, editor, tool
using UnityEditor;
using UnityEngine;

/// <summary>
/// DungeonRewardPickup 껍데기를 만드는 1회용 에디터 툴. **구 방식(접촉 수집)** 이다.
/// 메뉴: Tools > Dungeon > Legacy > Create Contact Reward Prefab.
/// 지금 보상 심볼 '$'는 DungeonChestSetup이 만드는 상자(F키)를 쓴다 — 새로 만들 일이 있으면 그쪽을 쓸 것.
/// </summary>
public static class DungeonRewardPrefabCreator
{
    private const string Dir = "Assets/Prefabs/Dungeon";

    [MenuItem("Tools/Dungeon/Legacy/Create Contact Reward Prefab (구 방식)")]
    public static void Create()
    {
        EnsureFolder();

        var go = new GameObject("DungeonRewardPickup");
        go.AddComponent<SpriteRenderer>(); // 스프라이트는 사용자가 지정
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;              // 접촉 수집용 트리거
        col.size = Vector2.one;            // 1셀 크기 기본값
        go.AddComponent<DungeonRewardPickup>();

        string path = AssetDatabase.GenerateUniqueAssetPath($"{Dir}/DungeonRewardPickup.prefab");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[Dungeon] Reward pickup 프리팹 생성: {path}\n" +
                  "→ 인스펙터에서 보물상자 Sprite, Mineral Rewards(광물+수량), Destination(인벤토리/창고)을 지정하세요.");
    }

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(Dir)) return;
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        AssetDatabase.CreateFolder("Assets/Prefabs", "Dungeon");
    }
}
