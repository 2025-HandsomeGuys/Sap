// @tags: dungeon, route, prefab, tileset, editor, tool
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Gameplay.Dungeon.Authoring;

/// <summary>
/// 루트(왕복 경로) 시스템의 에디터 셋업을 한 번에 처리하는 툴.
/// 메뉴: Tools > Dungeon > Setup Route System.
///
/// 1) 루트 시작 프리팹 변형 4종 생성 — Route_type2를 베이스로 RouteAutoLinker를 달고
///    firstSegment(자기 자신)·obstaclePrefab·이동 파라미터를 프리팹 안에 박아둔다.
///    → 임포터는 심볼대로 Instantiate만 하면 되고 배선이 필요 없다.
/// 2) 타일셋에 루트 심볼 10종 등록 — 경로 6종(- | F L J 7) + 시작 4종(@ & * +).
///    코너는 Route_type4 하나를 zRotation 4방향으로 돌려 쓴다(type3은 MiddlePoint가 없어
///    코너를 대각선으로 잘라먹으므로 쓰지 않는다).
///
/// 이미 있는 프리팹·심볼은 덮어쓰지 않고 건너뛴다. 여러 번 실행해도 안전하다.
/// </summary>
public static class DungeonRouteSetup
{
    private const string RouteDir = "Assets/Prefabs/Dungeon/Traps/Route";
    private const string TrapDir = "Assets/Prefabs/Dungeon/Traps";
    private const string TilesetPath = "Assets/GameData/Dungeon/DefaultDungeonTileset.asset";

    private const string StraightPrefab = RouteDir + "/Route_type2.prefab";
    private const string CornerPrefab = RouteDir + "/Route_type4.prefab";

    /// <summary>루트 시작 조각 1종의 정의. 심볼 하나 = 태울 장애물 하나.</summary>
    private struct StartVariant
    {
        public string name;
        public string symbol;
        public string obstaclePath;
        public float rotationSpeed;
        public float speed;
        public float waitAtEnds;
    }

    private static readonly StartVariant[] Variants =
    {
        new StartVariant { name = "RouteStart_Blade",   symbol = "@", obstaclePath = TrapDir + "/Rotating blade.prefab", rotationSpeed = -360f, speed = 5f,   waitAtEnds = 1.5f },
        new StartVariant { name = "RouteStart_Fire",    symbol = "&", obstaclePath = TrapDir + "/FireTrap.prefab",       rotationSpeed = 0f,    speed = 3.5f, waitAtEnds = 1.5f },
        new StartVariant { name = "RouteStart_Spike",   symbol = "*", obstaclePath = TrapDir + "/SpikeTrap.prefab",      rotationSpeed = 0f,    speed = 4f,   waitAtEnds = 1f },
    };

    // 경로 조각 심볼 → (프리팹, zRotation). 회전은 반시계.
    private static readonly (string symbol, string prefab, float z, string shape)[] PathSymbols =
    {
        ("-",  StraightPrefab, 0f,   "─"),
        ("|",  StraightPrefab, 90f,  "│"),
        ("F",  CornerPrefab,   0f,   "┌"),
        ("L",  CornerPrefab,   90f,  "└"),
        ("J",  CornerPrefab,   180f, "┘"),
        ("7",  CornerPrefab,   270f, "┐"),
    };

    [MenuItem("Tools/Dungeon/Setup Route System")]
    public static void Run()
    {
        var log = new List<string>();

        var created = CreateStartVariants(log);
        RegisterSymbols(created, log);

        AssetDatabase.SaveAssets();
        Debug.Log("[Route] 셋업 완료\n" + string.Join("\n", log));
    }

    /// <summary>
    /// 열려 있는 씬의 모든 루트를 검사해 체인 해석 결과를 로그로 찍는다.
    /// 기즈모로는 "붙어 보이는 것"과 "실제로 이어진 것"을 눈으로 구분하기 어려워서,
    /// 좌표·웨이포인트 수·구간 배율을 숫자로 확인하는 경로를 따로 둔다.
    /// </summary>
    [MenuItem("Tools/Dungeon/Validate Routes")]
    public static void ValidateRoutes()
    {
        var linkers = CollectLinkers(out string where);
        if (linkers.Length == 0)
        {
            Debug.LogWarning($"[Route] RouteAutoLinker를 찾지 못했습니다 (검사 범위: {where}).\n" +
                             "던전 맵이 프리팹 안에 있으면 그 프리팹을 Project 창에서 선택하거나 " +
                             "더블클릭해 Prefab 모드로 연 뒤 다시 실행하세요.");
            return;
        }

        var log = new List<string> { $"루트 {linkers.Length}개  (검사 범위: {where})" };
        foreach (var linker in linkers)
        {
            var p = linker.transform.position;
            log.Add($"· {linker.name} @({p.x:0.##}, {p.y:0.##})\n    {linker.DescribeChain()}");
        }

        Debug.Log("[Route] 검사\n" + string.Join("\n", log));
    }

    /// <summary>
    /// 루트를 어디서 찾을지 결정한다. 던전 맵은 보통 **프리팹 하나 = 던전 하나**로 저장되므로
    /// 열린 씬만 뒤지면 아무것도 못 찾는다(프리팹 모드의 오브젝트는 미리보기 씬에 있어
    /// FindObjectsByType에 안 잡힌다).
    /// 우선순위: Prefab 모드 → 선택한 오브젝트(프리팹 에셋 포함) → 열린 씬.
    /// </summary>
    private static RouteAutoLinker[] CollectLinkers(out string where)
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null)
        {
            where = $"Prefab 모드 — {stage.assetPath}";
            return stage.prefabContentsRoot.GetComponentsInChildren<RouteAutoLinker>(true);
        }

        var selected = Selection.gameObjects;
        if (selected != null && selected.Length > 0)
        {
            var found = new List<RouteAutoLinker>();
            foreach (var go in selected)
            {
                if (go == null) continue;
                found.AddRange(go.GetComponentsInChildren<RouteAutoLinker>(true));
            }
            if (found.Count > 0)
            {
                where = $"선택한 오브젝트 {selected.Length}개 아래";
                return found.ToArray();
            }
        }

        where = "열린 씬";
        return Object.FindObjectsByType<RouteAutoLinker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    // --- 루트에 태울 장애물 변형 ------------------------------------------------

    /// <summary>
    /// 루트를 왕복할 함정 변형(RouteRider_*)을 만들고, 대응하는 RouteStart_*가 이걸 태우도록 다시 배선한다.
    /// 원본 함정 프리팹은 건드리지 않는다 — 제자리에 놓는 용도로 계속 쓰이기 때문.
    ///
    /// 자기 이동 로직이 있는 함정은 반드시 꺼야 한다. MovingObstacle이 transform.position을 쓰는데
    /// PeriodicMover도 같은 값을 써서 서로 덮어쓴다(가시가 루트를 못 따라가고 제자리에서 떤다).
    ///
    /// CrusherBlock은 빠져 있다. 자체 낙하·감지·데미지를 전부 스스로 하는 구조라
    /// 스크립트를 끄면 피해를 못 주고, 켜두면 이동이 싸운다 — 설계 판단이 필요하다.
    /// </summary>
    [MenuItem("Tools/Dungeon/Create Route Riders")]
    public static void CreateRiders()
    {
        var log = new List<string>();

        var fire = CreateRider("RouteRider_Fire", TrapDir + "/FireTrap.prefab", false, log);
        var spike = CreateRider("RouteRider_Spike", TrapDir + "/SpikeTrap.prefab", true, log);

        if (fire != null) RebindStart("RouteStart_Fire", fire, log);
        if (spike != null) RebindStart("RouteStart_Spike", spike, log);

        RemoveCrusherStart(log);

        AssetDatabase.SaveAssets();
        Debug.Log("[Route] 라이더 변형\n" + string.Join("\n", log));
    }

    private static GameObject CreateRider(string name, string sourcePath, bool disableSelfMover, List<string> log)
    {
        string path = $"{RouteDir}/{name}.prefab";

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            log.Add($"· {name} 이미 존재 — 건너뜀");
            return existing;
        }

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null)
        {
            log.Add($"✗ {name}: 원본 프리팹 없음 ({sourcePath})");
            return null;
        }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(source);
        inst.name = name;

        if (inst.GetComponent<MovingObstacle>() == null)
            inst.AddComponent<MovingObstacle>();

        if (disableSelfMover)
        {
            foreach (var pm in inst.GetComponentsInChildren<PeriodicMover>(true))
            {
                pm.enabled = false;
                log.Add($"  · {name}: PeriodicMover 비활성 (MovingObstacle과 위치를 다툼)");
            }
        }

        // DamageDealer는 트리거 콜백만 구현한다 — 솔리드 콜라이더면 피해가 안 들어간다.
        foreach (var col in inst.GetComponentsInChildren<Collider2D>(true))
        {
            if (col.isTrigger) continue;
            col.isTrigger = true;
            log.Add($"  · {name}: '{col.gameObject.name}' 콜라이더를 트리거로 전환");
        }

        var variant = PrefabUtility.SaveAsPrefabAsset(inst, path);
        Object.DestroyImmediate(inst);

        if (variant == null)
        {
            log.Add($"✗ {name}: 저장 실패");
            return null;
        }

        log.Add($"✓ {name} 생성 (원본 '{source.name}'은 그대로)");
        return variant;
    }

    /// <summary>
    /// 압쇄 블록 루트를 되돌린다. CrusherBlock은 낙하·감지·데미지를 스스로 하는 구조라
    /// 루트에 태우면 이동 로직이 싸우고, 스크립트를 끄면 피해를 못 준다 → 제자리 함정('C')으로만 쓴다.
    /// </summary>
    private static void RemoveCrusherStart(List<string> log)
    {
        string path = $"{RouteDir}/RouteStart_Crusher.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            log.Add(AssetDatabase.DeleteAsset(path)
                ? "✓ RouteStart_Crusher 삭제 — 압쇄는 제자리 함정 'C'로만"
                : "✗ RouteStart_Crusher 삭제 실패");
        }

        var tileset = AssetDatabase.LoadAssetAtPath<DungeonTilesetSO>(TilesetPath);
        if (tileset == null) return;

        int removed = tileset.objectMappings.RemoveAll(m => m != null && m.symbol == "+");
        if (removed > 0)
        {
            tileset.BuildLookup();
            EditorUtility.SetDirty(tileset);
            log.Add("✓ 타일셋에서 심볼 '+' 제거");
        }
    }

    // RouteStart_*의 obstaclePrefab을 라이더 변형으로 교체한다.
    private static void RebindStart(string startName, GameObject rider, List<string> log)
    {
        string path = $"{RouteDir}/{startName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            log.Add($"✗ {startName} 없음 — 'Setup Route System'을 먼저 실행하세요.");
            return;
        }

        var contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var linker = contents.GetComponent<RouteAutoLinker>();
            if (linker == null)
            {
                log.Add($"✗ {startName}: RouteAutoLinker가 없습니다.");
                return;
            }
            if (linker.obstaclePrefab == rider)
            {
                log.Add($"· {startName} 이미 '{rider.name}'를 태우고 있음");
                return;
            }

            linker.obstaclePrefab = rider;
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            log.Add($"✓ {startName} → 태울 장애물을 '{rider.name}'로 교체");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // --- 1) 시작 프리팹 변형 ---------------------------------------------------

    private static Dictionary<string, GameObject> CreateStartVariants(List<string> log)
    {
        var result = new Dictionary<string, GameObject>();

        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StraightPrefab);
        if (basePrefab == null)
        {
            log.Add($"✗ 베이스 프리팹을 찾지 못했습니다: {StraightPrefab}");
            return result;
        }

        foreach (var v in Variants)
        {
            string path = $"{RouteDir}/{v.name}.prefab";

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                log.Add($"· {v.name} 이미 존재 — 건너뜀");
                result[v.symbol] = existing;
                continue;
            }

            var obstacle = AssetDatabase.LoadAssetAtPath<GameObject>(v.obstaclePath);
            if (obstacle == null)
            {
                log.Add($"✗ {v.name}: 장애물 프리팹 없음 ({v.obstaclePath}) — 생성 안 함");
                continue;
            }
            if (obstacle.GetComponent<MovingObstacle>() == null)
                log.Add($"⚠ {v.name}: '{obstacle.name}'에 MovingObstacle이 없습니다. " +
                        "붙이지 않으면 스폰만 되고 움직이지 않습니다.");

            // 프리팹 인스턴스를 저장하므로 결과물은 Prefab Variant가 된다
            // (베이스 Route_type2의 수정이 그대로 전파된다).
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            inst.name = v.name;

            var linker = inst.AddComponent<RouteAutoLinker>();
            linker.firstSegment = inst.GetComponent<RouteSegment>(); // 프리팹 내부 자기참조
            linker.obstaclePrefab = obstacle;
            linker.obstacleSpeed = v.speed;
            linker.waitTimeAtEnds = v.waitAtEnds;
            linker.obstacleRotationSpeed = v.rotationSpeed;

            var variant = PrefabUtility.SaveAsPrefabAsset(inst, path);
            Object.DestroyImmediate(inst);

            if (variant == null)
            {
                log.Add($"✗ {v.name}: 저장 실패");
                continue;
            }

            log.Add($"✓ {v.name} 생성 — 심볼 '{v.symbol}', 태울 장애물 '{obstacle.name}'");
            result[v.symbol] = variant;
        }

        return result;
    }

    // --- 2) 타일셋 심볼 등록 ---------------------------------------------------

    private static void RegisterSymbols(Dictionary<string, GameObject> startVariants, List<string> log)
    {
        var tileset = AssetDatabase.LoadAssetAtPath<DungeonTilesetSO>(TilesetPath);
        if (tileset == null)
        {
            log.Add($"✗ 타일셋을 찾지 못했습니다: {TilesetPath} — 심볼 등록을 건너뜁니다.");
            return;
        }

        int added = 0;

        foreach (var (symbol, prefabPath, z, shape) in PathSymbols)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                log.Add($"✗ 심볼 '{symbol}': 프리팹 없음 ({prefabPath})");
                continue;
            }
            if (TryAdd(tileset, symbol, prefab, z, log, $"{shape} 경로")) added++;
        }

        foreach (var v in Variants)
        {
            if (!startVariants.TryGetValue(v.symbol, out var prefab)) continue;
            if (TryAdd(tileset, v.symbol, prefab, 0f, log, "루트 시작")) added++;
        }

        if (added > 0)
        {
            tileset.BuildLookup();
            EditorUtility.SetDirty(tileset);
            log.Add($"→ 타일셋에 심볼 {added}종 추가");
        }
    }

    // 이미 매핑된 심볼은 덮어쓰지 않는다 — 손으로 조정한 매핑을 날리지 않기 위함.
    private static bool TryAdd(
        DungeonTilesetSO tileset, string symbol, GameObject prefab, float z,
        List<string> log, string note)
    {
        foreach (var m in tileset.objectMappings)
        {
            if (m != null && m.symbol == symbol)
            {
                log.Add($"· 심볼 '{symbol}' 이미 매핑됨 ({(m.prefab != null ? m.prefab.name : "비어있음")}) — 건너뜀");
                return false;
            }
        }

        tileset.objectMappings.Add(new DungeonTilesetSO.ObjectMapping
        {
            symbol = symbol,
            prefab = prefab,
            zRotation = z,
        });
        log.Add($"✓ 심볼 '{symbol}' → {prefab.name} (z={z}) — {note}");
        return true;
    }
}
