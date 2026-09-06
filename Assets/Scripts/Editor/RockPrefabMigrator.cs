// @tags: editor, rock, prefab, migration, tool, temporary
// ============================================================================================================
//  일회용 마이그레이션 툴 — 실행이 끝나면 이 파일과
//  TileVisualSettings.RockSpriteSet / TileVisualData.rockSpriteSets 를 함께 삭제할 것.
// ============================================================================================================
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 구 TileVisualSettings.rockSpriteSets(스프라이트 묶음)를 돌 프리팹으로 굽고
/// 새 rockPrefabs 리스트를 채운다.
///
/// 만들어지는 프리팹 구조 (전부 root 한 GameObject):
///   SpriteRenderer      — normal 스프라이트
///   PolygonCollider2D   — SpriteRenderer 모양에서 자동 생성
///   DamageStagedVisuals — stages[0]=normal, [1]=crack1, [2]=crack2
///   DiggableRock        — 돌마다 HP를 다르게 주고 싶으면 이후 인스펙터에서 조정
///   RockBreakVFX        — tier, fragmentSets
///   RockHitAnimator     — 히트 연출용. Animator·클립은 사람이 나중에 붙인다
///
/// 리스트 순서는 반드시 보존된다. RockSaveEntry.spriteSetIndex가 이 인덱스이기 때문에
/// 순서가 바뀌면 세이브된 돌의 종류가 뒤바뀐다.
/// </summary>
public static class RockPrefabMigrator
{
    private const string OutputFolder = "Assets/Prefabs/Rocks";

    [MenuItem("Tools/Rock/Migrate RockSpriteSets → Prefabs")]
    private static void Migrate()
    {
        string[] guids = AssetDatabase.FindAssets("t:TileVisualSettings");
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("마이그레이션", "TileVisualSettings 에셋을 찾지 못했습니다.", "확인");
            return;
        }

        EnsureFolder(OutputFolder);

        int createdTotal = 0;
        var report = new System.Text.StringBuilder();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var settings = AssetDatabase.LoadAssetAtPath<TileVisualSettings>(path);
            if (settings?.settings == null) continue;

            // List<struct>는 인덱서가 복사본을 돌려주므로 수정 후 되써야 한다.
            for (int i = 0; i < settings.settings.Count; i++)
            {
                TileVisualSettings.TileVisualData data = settings.settings[i];

                if (data.rockSpriteSets == null || data.rockSpriteSets.Count == 0) continue;
                if (data.rockPrefabs != null && data.rockPrefabs.Count > 0)
                {
                    report.AppendLine($"  [건너뜀] {data.tileType} — rockPrefabs가 이미 채워져 있음");
                    continue;
                }

                var prefabs = new List<GameObject>(data.rockSpriteSets.Count);

                for (int s = 0; s < data.rockSpriteSets.Count; s++)
                {
                    TileVisualSettings.RockSpriteSet set = data.rockSpriteSets[s];
                    GameObject prefab = BuildPrefab(data.tileType, s, set);

                    // 실패해도 null을 그대로 넣어 인덱스를 밀지 않는다 — 세이브 인덱스가 어긋나면
                    // 복원된 돌의 종류가 통째로 바뀐다.
                    prefabs.Add(prefab);
                    if (prefab != null) createdTotal++;
                    else report.AppendLine($"  [실패] {data.tileType}[{s}] — normal 스프라이트 없음, null로 자리만 유지");
                }

                data.rockPrefabs = prefabs;
                settings.settings[i] = data;
                report.AppendLine($"  [완료] {data.tileType} — 프리팹 {prefabs.Count}개");
            }

            EditorUtility.SetDirty(settings);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg = $"프리팹 {createdTotal}개 생성 → {OutputFolder}\n\n{report}\n" +
                     "다음 단계:\n" +
                     "1. 각 프리팹에 Animator + 히트 클립 부착 (RockHitAnimator.duration을 클립 길이에 맞출 것)\n" +
                     "2. 결과 확인 후 TileVisualSettings의 rockSpriteSets 필드와 이 툴을 삭제";
        Debug.Log("[RockPrefabMigrator]\n" + msg);
        EditorUtility.DisplayDialog("마이그레이션 완료", msg, "확인");
    }

    private static GameObject BuildPrefab(TileType tileType, int index, TileVisualSettings.RockSpriteSet set)
    {
        if (set.normal == null) return null;

        var go = new GameObject($"Rock_{tileType}_{index}_{set.tier}");

        // SpriteRenderer를 먼저 넣어야 PolygonCollider2D가 스프라이트 모양을 굽는다.
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = set.normal;
        // 지하 정렬 대역: 청크 배경 -100 < 플레이어 -49~-30 < 돌 -1 < 지형 0 (CLAUDE.md §16).
        // 런타임에도 RockSpawner가 다시 강제하지만, 프리팹만 열어봐도 의도가 보이도록 함께 세팅한다.
        sr.sortingLayerID = SortingLayer.NameToID("Default");
        sr.sortingOrder   = -1;

        go.AddComponent<PolygonCollider2D>();

        var staged = go.AddComponent<DamageStagedVisuals>();
        staged.stages = new[] { set.normal, set.crack1, set.crack2 };

        go.AddComponent<DiggableRock>();

        var vfx = go.AddComponent<RockBreakVFX>();
        vfx.tier         = set.tier;
        vfx.fragmentSets = set.fragmentSets;

        go.AddComponent<RockHitAnimator>();

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/{go.name}.prefab");
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, assetPath);
        Object.DestroyImmediate(go);

        return saved;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
