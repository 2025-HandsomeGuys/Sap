// @tags: dungeon, reward, chest, prefab, tileset, editor, tool
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Gameplay.Dungeon.Authoring;

/// <summary>
/// 보상을 상자 하나로 일원화하는 에디터 셋업. 메뉴: Tools > Dungeon > Setup Dungeon Chest.
///
/// 1) Level1Chest를 베이스로 <c>DungeonChest</c> 변형 생성 — ChestObject를 끄고
///    <see cref="DungeonRewardPickup"/>을 얹는다. 스프라이트·Animator("Open")·트리거 콜라이더는
///    베이스 것을 그대로 쓴다.
/// 2) 타일셋의 '$' 매핑을 그 상자로 교체.
///
/// ChestObject를 제거하지 않고 **끄기만** 하는 이유: 변형에서 컴포넌트를 지우면 베이스가 바뀔 때
/// 되살아나는 등 다루기 번거롭고, 비활성 MonoBehaviour는 Update도 트리거 콜백도 받지 않아
/// F키 이중 처리 걱정이 없다. 원본 Level1Chest와 그걸 쓰는 테스트 씬은 손대지 않는다.
///
/// 이미 있는 프리팹은 덮어쓰지 않는다. 여러 번 실행해도 안전하다.
/// </summary>
public static class DungeonChestSetup
{
    private const string BaseChestPath = "Assets/Prefabs/SpecialChunks/DunObject/Level1Chest.prefab";
    private const string OutPath = "Assets/Prefabs/Dungeon/DungeonChest.prefab";
    private const string TilesetPath = "Assets/GameData/Dungeon/DefaultDungeonTileset.asset";
    private const string RewardSymbol = "$";

    [MenuItem("Tools/Dungeon/Setup Dungeon Chest")]
    public static void Run()
    {
        var log = new List<string>();

        var chest = ResolveChestPrefab(log);
        if (chest == null)
        {
            Debug.LogError("[Chest] 셋업 실패\n" + string.Join("\n", log));
            return;
        }

        RemapRewardSymbol(chest, log);

        AssetDatabase.SaveAssets();
        Debug.Log("[Chest] 셋업 완료\n" + string.Join("\n", log));
    }

    private static GameObject ResolveChestPrefab(List<string> log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(OutPath);
        if (existing != null)
        {
            log.Add("· DungeonChest 이미 존재 — 건너뜀");
            return existing;
        }

        EnsureFolder();

        var baseChest = AssetDatabase.LoadAssetAtPath<GameObject>(BaseChestPath);
        if (baseChest == null)
        {
            log.Add($"✗ 베이스 상자를 찾지 못했습니다: {BaseChestPath}");
            return null;
        }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(baseChest);
        inst.name = "DungeonChest";

        foreach (var co in inst.GetComponentsInChildren<ChestObject>(true))
        {
            co.enabled = false;
            log.Add("  · ChestObject 비활성 (F키 폴링·UnityEvent 지급 경로를 쓰지 않음)");
        }

        if (inst.GetComponent<DungeonRewardPickup>() == null)
            inst.AddComponent<DungeonRewardPickup>();

        // PlayerInteractor는 근처 콜라이더에서 IInteractable을 찾는다 — 트리거가 없으면 후보로 안 잡힌다.
        var col = inst.GetComponentInChildren<Collider2D>(true);
        if (col == null)
            log.Add("⚠ 콜라이더가 없습니다. 트리거 Collider2D를 붙여야 F키 후보로 잡힙니다.");
        else if (!col.isTrigger)
            log.Add($"⚠ '{col.gameObject.name}' 콜라이더가 트리거가 아닙니다 — 상호작용 탐지가 안 될 수 있습니다.");

        var variant = PrefabUtility.SaveAsPrefabAsset(inst, OutPath);
        Object.DestroyImmediate(inst);

        if (variant == null)
        {
            log.Add("✗ DungeonChest 저장 실패");
            return null;
        }

        log.Add($"✓ DungeonChest 생성 — {OutPath}");
        return variant;
    }

    // '$'를 상자로 갈아끼운다. 기존 접촉형 DungeonRewardPickup.prefab은 이 시점부터 미사용.
    private static void RemapRewardSymbol(GameObject chest, List<string> log)
    {
        var tileset = AssetDatabase.LoadAssetAtPath<DungeonTilesetSO>(TilesetPath);
        if (tileset == null)
        {
            log.Add($"✗ 타일셋을 찾지 못했습니다: {TilesetPath}");
            return;
        }

        foreach (var m in tileset.objectMappings)
        {
            if (m == null || m.symbol != RewardSymbol) continue;

            if (m.prefab == chest)
            {
                log.Add("· 심볼 '$' 이미 DungeonChest — 건너뜀");
                return;
            }

            string before = m.prefab != null ? m.prefab.name : "비어있음";
            m.prefab = chest;
            m.zRotation = 0f;
            tileset.BuildLookup();
            EditorUtility.SetDirty(tileset);
            log.Add($"✓ 심볼 '$' {before} → DungeonChest");
            return;
        }

        tileset.objectMappings.Add(new DungeonTilesetSO.ObjectMapping
        {
            symbol = RewardSymbol,
            prefab = chest,
            zRotation = 0f,
        });
        tileset.BuildLookup();
        EditorUtility.SetDirty(tileset);
        log.Add("✓ 심볼 '$' 신규 등록 → DungeonChest");
    }

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder("Assets/Prefabs/Dungeon")) return;
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        AssetDatabase.CreateFolder("Assets/Prefabs", "Dungeon");
    }
}
