// @tags: dungeon, trap, spike, prefab, tileset, editor, tool
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Gameplay.Dungeon.Authoring;

/// <summary>
/// 던전 맵의 '!' 가시를 <c>RetractingSpike</c>에서 <c>SpikeTrap</c> 변형으로 갈아끼우는 셋업.
/// 메뉴: Tools > Dungeon > Setup Dungeon Spike.
///
/// 1) SpikeTrap을 베이스로 <c>SpikeTrap_Dungeon</c> 변형 생성 — 던전 칸 크기에 맞춰 보정한다.
/// 2) 타일셋의 '!' 매핑을 그 변형으로 교체.
///
/// 베이스 SpikeTrap은 특수 청크(1칸 0.25유닛) 기준으로 만들어져 있다. 아트가 33px@100PPU =
/// 0.33유닛이라 1칸 1유닛인 던전 맵에 그대로 놓으면 3분의 1 크기로 박힌다. 그래서 변형에서
/// 루트 스케일을 <see cref="CellScale"/>배 준다. 임포터는 여기에 다시 칸 크기를 곱하므로
/// (<c>localScale *= cellWorld</c>) 0.25칸짜리 맵에 찍어도 베이스 크기로 되돌아간다.
///
/// <c>moveDistance</c>만 따로 곱해주는 이유: 이 값은 월드 기준(<c>Vector3.up * moveDistance</c>)이라
/// 루트 스케일이 안 먹는다. 스케일과 같은 배율을 직접 곱해야 솟는 높이 비율이 유지된다.
/// 반대로 임포터가 0.25칸 맵에 찍을 때는 이 값만 보정에서 빠져 4배 높이 솟는다 — 그런 맵은
/// 베이스 SpikeTrap을 쓰거나 인스펙터에서 moveDistance를 직접 낮춰야 한다.
///
/// 콜라이더를 트리거로 바꾸는 이유: <see cref="DamageDealer"/>는 OnTriggerEnter2D/Stay2D만 쓴다.
/// 베이스는 <c>isTrigger = false</c>라 그대로 두면 데미지가 아예 안 들어간다.
///
/// 이미 있는 프리팹은 덮어쓰지 않는다. 여러 번 실행해도 안전하다.
/// </summary>
public static class DungeonSpikeSetup
{
    private const string BaseSpikePath = "Assets/Prefabs/Dungeon/Traps/SpikeTrap.prefab";
    private const string OutPath = "Assets/Prefabs/Dungeon/Traps/SpikeTrap_Dungeon.prefab";
    private const string TilesetPath = "Assets/GameData/Dungeon/DefaultDungeonTileset.asset";
    private const string SpikeSymbol = "!";

    /// <summary>베이스 아트(0.33유닛)를 던전 1칸(1유닛)에 맞추는 배율.</summary>
    private const float CellScale = 3f;

    [MenuItem("Tools/Dungeon/Setup Dungeon Spike")]
    public static void Run()
    {
        var log = new List<string>();

        var spike = ResolveSpikePrefab(log);
        if (spike == null)
        {
            Debug.LogError("[Spike] 셋업 실패\n" + string.Join("\n", log));
            return;
        }

        RemapSpikeSymbol(spike, log);

        AssetDatabase.SaveAssets();
        Debug.Log("[Spike] 셋업 완료\n" + string.Join("\n", log));
    }

    private static GameObject ResolveSpikePrefab(List<string> log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(OutPath);
        if (existing != null)
        {
            log.Add("· SpikeTrap_Dungeon 이미 존재 — 건너뜀");
            return existing;
        }

        var baseSpike = AssetDatabase.LoadAssetAtPath<GameObject>(BaseSpikePath);
        if (baseSpike == null)
        {
            log.Add($"✗ 베이스 가시를 찾지 못했습니다: {BaseSpikePath}");
            return null;
        }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(baseSpike);
        inst.name = "SpikeTrap_Dungeon";
        inst.transform.localScale = Vector3.one * CellScale;
        log.Add($"  · 루트 스케일 ×{CellScale} (0.33유닛 아트 → 던전 1칸)");

        var movers = inst.GetComponentsInChildren<PeriodicMover>(true);
        if (movers.Length == 0)
            log.Add("⚠ PeriodicMover가 없습니다 — 가시가 움직이지 않습니다.");
        foreach (var mover in movers)
        {
            float before = mover.moveDistance;
            mover.moveDistance = before * CellScale;
            log.Add($"  · moveDistance {before} → {mover.moveDistance} (월드 기준이라 스케일이 안 먹음)");
        }

        // DamageDealer가 붙은 오브젝트의 콜라이더만 트리거로 바꾼다.
        // 다른 콜라이더(발판·지형 충돌용)까지 건드리면 밟고 설 수 없게 된다.
        var dealers = inst.GetComponentsInChildren<DamageDealer>(true);
        if (dealers.Length == 0)
            log.Add("⚠ DamageDealer가 없습니다 — 접촉해도 데미지가 없습니다.");
        foreach (var dealer in dealers)
        {
            var cols = dealer.GetComponents<Collider2D>();
            if (cols.Length == 0)
            {
                log.Add($"⚠ '{dealer.gameObject.name}'에 콜라이더가 없습니다 — 트리거 콜백이 안 옵니다.");
                continue;
            }
            foreach (var col in cols)
            {
                if (col.isTrigger) continue;
                col.isTrigger = true;
                log.Add($"  · '{dealer.gameObject.name}' 콜라이더 → 트리거 (DamageDealer는 OnTrigger만 씀)");
            }
        }

        var variant = PrefabUtility.SaveAsPrefabAsset(inst, OutPath);
        Object.DestroyImmediate(inst);

        if (variant == null)
        {
            log.Add("✗ SpikeTrap_Dungeon 저장 실패");
            return null;
        }

        log.Add($"✓ SpikeTrap_Dungeon 생성 — {OutPath}");
        return variant;
    }

    // '!'를 SpikeTrap 변형으로 갈아끼운다. 기존 RetractingSpike.prefab은 이 시점부터 미사용.
    // 이미 스탬프된 맵의 인스턴스는 그대로 남는다 — 통일하려면 해당 맵을 재임포트해야 한다.
    private static void RemapSpikeSymbol(GameObject spike, List<string> log)
    {
        var tileset = AssetDatabase.LoadAssetAtPath<DungeonTilesetSO>(TilesetPath);
        if (tileset == null)
        {
            log.Add($"✗ 타일셋을 찾지 못했습니다: {TilesetPath}");
            return;
        }

        foreach (var m in tileset.objectMappings)
        {
            if (m == null || m.symbol != SpikeSymbol) continue;

            if (m.prefab == spike)
            {
                log.Add("· 심볼 '!' 이미 SpikeTrap_Dungeon — 건너뜀");
                return;
            }

            string before = m.prefab != null ? m.prefab.name : "비어있음";
            m.prefab = spike;
            m.zRotation = 0f;
            tileset.BuildLookup();
            EditorUtility.SetDirty(tileset);
            log.Add($"✓ 심볼 '!' {before} → SpikeTrap_Dungeon");
            return;
        }

        tileset.objectMappings.Add(new DungeonTilesetSO.ObjectMapping
        {
            symbol = SpikeSymbol,
            prefab = spike,
            zRotation = 0f,
        });
        tileset.BuildLookup();
        EditorUtility.SetDirty(tileset);
        log.Add("✓ 심볼 '!' 신규 등록 → SpikeTrap_Dungeon");
    }
}
