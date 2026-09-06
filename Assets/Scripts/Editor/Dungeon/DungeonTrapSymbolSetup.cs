// @tags: dungeon, trap, symbol, tileset, editor, tool
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Gameplay.Dungeon.Authoring;

/// <summary>
/// 새로 만든 함정 프리팹을 던전 맵 심볼에 등록한다. 메뉴: Tools > Dungeon > Setup New Trap Symbols.
///
///   f  화염방사기(FireTrap)      — 로컬 +X로 분사
///   ^  점프대(JumpTrap)
///   w  바람발사대(WindSpawner)   — 로컬 +X로 밂
///
/// 화염과 바람은 다트(<c>&gt;</c>/<c>&lt;</c>)처럼 방향이 있는 함정이다. 반대 방향이 필요하면
/// 프리팹을 새로 만들 필요 없이 <see cref="Entries"/>에 같은 프리팹을 다른 심볼·zRotation으로
/// 한 줄 더 추가하면 된다(180 = 왼쪽, 90 = 위, 270 = 아래).
///
/// 이미 같은 프리팹이 걸려 있으면 건너뛴다. 여러 번 실행해도 안전하다.
/// </summary>
public static class DungeonTrapSymbolSetup
{
    private const string TilesetPath = "Assets/GameData/Dungeon/DefaultDungeonTileset.asset";
    private const string TrapDir = "Assets/Prefabs/Dungeon/Traps";

    private class Entry
    {
        public string Symbol;
        public string PrefabPath;
        public float ZRotation;
        public string Label;
    }

    private static readonly Entry[] Entries =
    {
        new Entry { Symbol = "f", PrefabPath = TrapDir + "/FireTrap.prefab",
                    ZRotation = 0f, Label = "화염방사기(오른쪽 분사)" },
        new Entry { Symbol = "^", PrefabPath = TrapDir + "/JumpTrap.prefab",
                    ZRotation = 0f, Label = "점프대" },
        new Entry { Symbol = "w", PrefabPath = TrapDir + "/WindTrap/WindSpawner.prefab",
                    ZRotation = 0f, Label = "바람발사대(오른쪽으로 밂)" },
    };

    [MenuItem("Tools/Dungeon/Setup New Trap Symbols")]
    public static void Run()
    {
        var log = new List<string>();

        var tileset = AssetDatabase.LoadAssetAtPath<DungeonTilesetSO>(TilesetPath);
        if (tileset == null)
        {
            Debug.LogError($"[TrapSymbol] 타일셋을 찾지 못했습니다: {TilesetPath}");
            return;
        }

        int registered = 0;
        foreach (var e in Entries)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.PrefabPath);
            if (prefab == null)
            {
                log.Add($"✗ '{e.Symbol}' 프리팹을 찾지 못했습니다: {e.PrefabPath}");
                continue;
            }

            Validate(prefab, e, log);
            if (Register(tileset, e, prefab, log)) registered++;
        }

        if (registered > 0)
        {
            tileset.BuildLookup();
            EditorUtility.SetDirty(tileset);
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[TrapSymbol] 등록 {registered}건\n" + string.Join("\n", log));
    }

    /// <summary>등록/교체했으면 true, 이미 같은 프리팹이라 건너뛰었으면 false.</summary>
    private static bool Register(DungeonTilesetSO tileset, Entry e, GameObject prefab, List<string> log)
    {
        foreach (var m in tileset.objectMappings)
        {
            if (m == null || m.symbol != e.Symbol) continue;

            if (m.prefab == prefab && Mathf.Approximately(m.zRotation, e.ZRotation))
            {
                log.Add($"· '{e.Symbol}' 이미 {prefab.name} — 건너뜀");
                return false;
            }

            string before = m.prefab != null ? m.prefab.name : "비어있음";
            m.prefab = prefab;
            m.zRotation = e.ZRotation;
            log.Add($"✓ '{e.Symbol}' {before} → {prefab.name}  [{e.Label}]");
            return true;
        }

        tileset.objectMappings.Add(new DungeonTilesetSO.ObjectMapping
        {
            symbol = e.Symbol,
            prefab = prefab,
            zRotation = e.ZRotation,
        });
        log.Add($"✓ '{e.Symbol}' 신규 등록 → {prefab.name}  [{e.Label}]");
        return true;
    }

    /// <summary>
    /// 프리팹이 실제로 동작할 조건인지 본다. 콜라이더 조건이 컴포넌트마다 반대라 한 번에 못 묶는다.
    ///   FireDamageDealer  OnTriggerStay2D  → 트리거여야 한다
    ///   JumpPad           OnCollisionEnter2D → 트리거면 안 된다(밟고 튕겨야 하므로 솔리드)
    ///   WindGenerator     Awake에서 콜라이더를 직접 트리거로 잡는다 → 크기·트리거는 확인 불필요
    /// </summary>
    private static void Validate(GameObject prefab, Entry e, List<string> log)
    {
        foreach (var fire in prefab.GetComponentsInChildren<FireDamageDealer>(true))
            RequireCollider(fire, wantTrigger: true, why: "OnTriggerStay2D", log: log);

        foreach (var pad in prefab.GetComponentsInChildren<JumpPad>(true))
            RequireCollider(pad, wantTrigger: false, why: "OnCollisionEnter2D", log: log);

        // WindGenerator는 Assets/Prefabs 아래에 있어 Assembly-CSharp에 들어간다 —
        // 이 에디터 어셈블리(GameScripts.Editor)에서는 타입으로 못 잡으므로 이름으로 찾는다.
        // 스크립트를 Assets/Scripts/Object/UnderGround/Dun/으로 옮기면 이 우회가 없어도 된다.
        foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "WindGenerator") continue;

            var so = new SerializedObject(mb);
            var windPrefab = so.FindProperty("windPrefab");
            if (windPrefab != null && windPrefab.objectReferenceValue == null)
                log.Add($"⚠ '{e.Symbol}' {prefab.name}: windPrefab이 비어 있습니다 — 바람 연출이 안 나옵니다.");

            var len = so.FindProperty("windLength");
            if (len != null)
                log.Add($"  · windLength = {len.intValue} (1칸 1유닛 기준 {len.intValue}칸짜리 바람 구역)");
        }
    }

    private static void RequireCollider(Component c, bool wantTrigger, string why, List<string> log)
    {
        var cols = c.GetComponents<Collider2D>();
        if (cols.Length == 0)
        {
            log.Add($"⚠ '{c.gameObject.name}'에 Collider2D가 없습니다 — {c.GetType().Name}이 반응하지 않습니다.");
            return;
        }

        foreach (var col in cols)
        {
            if (col.isTrigger == wantTrigger) continue;
            log.Add($"⚠ '{c.gameObject.name}' 콜라이더 isTrigger={col.isTrigger} — " +
                    $"{c.GetType().Name}은 {why}을 쓰므로 {wantTrigger}여야 합니다.");
        }
    }
}
