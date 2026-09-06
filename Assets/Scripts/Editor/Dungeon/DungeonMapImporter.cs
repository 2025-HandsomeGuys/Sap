using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Gameplay.Dungeon.Authoring;
using Gameplay.Dungeon.Authoring.Generation;
using Gameplay.Dungeon.Traps;

/// <summary>
/// 던전 텍스트 맵을 "하나의 Map Root" 아래에 스탬프하는 에디터 툴.
/// Map Root 아래에서 Tilemap을 찾아 타일을 칠하고, Root 아래 "Objects" 컨테이너에 오브젝트를 배치한다.
/// → 맵 전체(타일+오브젝트)가 Map Root 하나로 묶여 위치 이동/관리가 쉬워진다.
/// 좌표: 텍스트(row,col) → tilemap cell (col, -row). 오브젝트는 셀 중심 월드좌표에 Instantiate.
/// reward/rock id는 인스턴스별 유일값 자동 부여. Clear 옵션 시 타일과 Objects 자식을 모두 비운 뒤 재생성.
/// </summary>
public class DungeonMapImporter : EditorWindow
{
    private const string ObjectsContainerName = "Objects";

    private Transform _mapRoot;
    private Tilemap _tilemapOverride;
    private DungeonTilesetSO _tileset;
    private TextAsset _mapAsset;
    private bool _clearFirst = true;

    // --- Generate 섹션 ---
    private const string GeneratedFolder = "Assets/DungeonMaps/Generated";

    private DungeonGenPresetSO _preset;
    private int _seed = 12345;
    private DungeonMapData _generated;
    private string _previewText = "";
    private readonly List<string> _genErrors = new List<string>();
    private readonly List<string> _genWarnings = new List<string>();
    private Vector2 _previewScroll;
    private GUIStyle _monoStyle;

    /// <summary>ASCII 프리뷰는 등폭 폰트여야 격자가 어긋나 보이지 않는다.</summary>
    private GUIStyle MonoStyle
    {
        get
        {
            if (_monoStyle == null)
            {
                _monoStyle = new GUIStyle(EditorStyles.label) { wordWrap = false, richText = false };
                var mono = Font.CreateDynamicFontFromOSFont("Consolas", 11);
                if (mono != null) _monoStyle.font = mono;
            }
            return _monoStyle;
        }
    }

    [MenuItem("Tools/Dungeon/Map Importer")]
    public static void Open() => GetWindow<DungeonMapImporter>("Dungeon Map Importer");

    private void OnGUI()
    {
        DrawGenerateSection();

        EditorGUILayout.Space(10);
        var line = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(line, new Color(0.35f, 0.35f, 0.35f));
        EditorGUILayout.Space(10);

        DrawImportSection();
    }

    private void DrawImportSection()
    {
        EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Map Root: 맵 전체를 담을 빈 오브젝트. 이 아래의 Tilemap을 찾아 타일을 칠하고,\n" +
            "'Objects' 컨테이너에 오브젝트를 배치합니다. Tilemap이 없으면 자동 생성(콜라이더/스케일은 직접 설정).",
            MessageType.Info);

        _mapRoot = (Transform)EditorGUILayout.ObjectField("Map Root", _mapRoot, typeof(Transform), true);
        _tilemapOverride = (Tilemap)EditorGUILayout.ObjectField(
            new GUIContent("Tilemap (선택)", "비우면 Map Root 아래에서 자동으로 찾습니다. " +
                                             "격자가 여러 개인 프리팹은 여기서 직접 골라야 의도한 곳에 찍힙니다."),
            _tilemapOverride, typeof(Tilemap), true);
        _tileset = (DungeonTilesetSO)EditorGUILayout.ObjectField("Tileset", _tileset, typeof(DungeonTilesetSO), false);
        _mapAsset = (TextAsset)EditorGUILayout.ObjectField("Map (.txt)", _mapAsset, typeof(TextAsset), false);
        _clearFirst = EditorGUILayout.Toggle("Clear Before Import", _clearFirst);

        using (new EditorGUI.DisabledScope(_mapRoot == null || _tileset == null || _mapAsset == null))
            if (GUILayout.Button("Import"))
                Import();
    }

    private void DrawGenerateSection()
    {
        EditorGUILayout.LabelField("Generate", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "프리셋과 시드로 던전 맵을 생성합니다. Generate는 디스크를 건드리지 않고 프리뷰만 갱신합니다.\n" +
            "Save & Stamp는 아래 Import 섹션의 Map Root·Tileset 설정을 그대로 사용합니다.",
            MessageType.Info);

        _preset = (DungeonGenPresetSO)EditorGUILayout.ObjectField("Preset", _preset, typeof(DungeonGenPresetSO), false);

        using (new EditorGUILayout.HorizontalScope())
        {
            _seed = EditorGUILayout.IntField("Seed", _seed);
            using (new EditorGUI.DisabledScope(_preset == null))
                if (GUILayout.Button("Random", GUILayout.Width(70)))
                {
                    _seed = new System.Random().Next(1, int.MaxValue);
                    DoGenerate();
                }
        }

        using (new EditorGUI.DisabledScope(_preset == null))
            if (GUILayout.Button("Generate"))
                DoGenerate();

        if (_genErrors.Count > 0)
            EditorGUILayout.HelpBox("에러:\n" + string.Join("\n", _genErrors), MessageType.Error);
        if (_genWarnings.Count > 0)
            EditorGUILayout.HelpBox($"경고 {_genWarnings.Count}건:\n" + string.Join("\n", _genWarnings), MessageType.Warning);

        if (!string.IsNullOrEmpty(_previewText))
        {
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.Height(240));
            float h = MonoStyle.CalcSize(new GUIContent(_previewText)).y;
            EditorGUILayout.SelectableLabel(_previewText, MonoStyle, GUILayout.ExpandWidth(true), GUILayout.Height(h));
            EditorGUILayout.EndScrollView();
        }

        using (new EditorGUI.DisabledScope(_generated == null))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save Only"))
                SaveGenerated();

            using (new EditorGUI.DisabledScope(_mapRoot == null || _tileset == null))
                if (GUILayout.Button("Save & Stamp"))
                {
                    if (SaveGenerated() != null) StampMap(_generated);
                }
        }
    }

    private void DoGenerate()
    {
        _genErrors.Clear();
        _genWarnings.Clear();
        _generated = null;
        _previewText = "";

        if (_preset == null) return;

        var result = DungeonGenerator.Generate(_preset, _seed);
        _genErrors.AddRange(result.Errors);
        _genWarnings.AddRange(result.Warnings);
        if (!result.Success) return;

        _generated = result.Data;
        _previewText = BuildPreview(result.Data);
    }

    // 오브젝트가 있는 칸은 오브젝트 심볼로, 나머지는 타일 심볼로 그린다.
    private static string BuildPreview(DungeonMapData d)
    {
        var sb = new System.Text.StringBuilder(d.Height * (d.Width + 1));
        for (int r = 0; r < d.Height; r++)
        {
            for (int c = 0; c < d.Width; c++)
                sb.Append(d.Objects[r, c] != '.' ? d.Objects[r, c] : d.Tiles[r, c]);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>생성 결과를 txt로 저장하고 에셋 경로를 반환한다. 실패 시 null.</summary>
    private string SaveGenerated()
    {
        if (_generated == null) return null;

        if (!Directory.Exists(GeneratedFolder))
        {
            Directory.CreateDirectory(GeneratedFolder);
            AssetDatabase.Refresh();
        }

        string path = $"{GeneratedFolder}/gen_{_seed}.txt";
        string text = DungeonMapWriter.Write(_generated, _preset != null ? _preset.name : "", _seed);

        File.WriteAllText(path, text);
        AssetDatabase.ImportAsset(path);

        Debug.Log($"[DungeonGen] 저장: {path} ({_generated.Width}x{_generated.Height})");
        return path;
    }

    private void Import()
    {
        var result = DungeonMapParser.Parse(_mapAsset.text);
        if (!result.Success)
        {
            EditorUtility.DisplayDialog("Import 실패", string.Join("\n", result.Errors), "확인");
            return;
        }
        StampMap(result.Data);
    }

    /// <summary>파싱된 맵을 Map Root 아래에 스탬프한다. 텍스트 임포트와 생성 결과가 공유하는 경로.</summary>
    private void StampMap(DungeonMapData data)
    {
        _tileset.BuildLookup();

        Undo.RegisterFullObjectHierarchyUndo(_mapRoot.gameObject, "Import Dungeon Map");

        Tilemap tilemap = ResolveTilemap(_mapRoot, _tilemapOverride);
        Transform objectsParent = ResolveObjectsContainer(_mapRoot);

        // 오브젝트 프리팹은 "한 칸 = 1유닛" 기준으로 만들어져 있다. 특수 청크처럼 격자가 더 촘촘한
        // 곳(청크 1칸=10유닛, 타일 0.25유닛)에 그대로 놓으면 4배 크게 박히고, 루트 조각은
        // 이웃끼리 맞물리지 못해 체인이 끊긴다. 타일맵의 실제 칸 크기를 재서 스케일로 흡수한다.
        float cellWorld = MeasureCellWorldSize(tilemap);

        if (_clearFirst)
        {
            tilemap.ClearAllTiles();
            for (int i = objectsParent.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(objectsParent.GetChild(i).gameObject);
        }

        int entryCount = 0;
        int rockAuto = 0, rewardAuto = 0;
        var placed = new List<(char linkId, GameObject go)>();

        for (int row = 0; row < data.Height; row++)
        for (int col = 0; col < data.Width; col++)
        {
            var cellPos = new Vector3Int(col, -row, 0);

            char t = data.Tiles[row, col];
            if (t != '.' && _tileset.TryGetTile(t, out var tile))
                tilemap.SetTile(cellPos, tile);
            else if (t != '.')
                Debug.LogWarning($"[DungeonImport] 미매핑 타일 심볼 '{t}' @({row},{col})");

            char o = data.Objects[row, col];
            if (o == '.') continue;
            if (!_tileset.TryGetObject(o, out var entry))
            {
                Debug.LogWarning($"[DungeonImport] 미매핑 오브젝트 심볼 '{o}' @({row},{col})");
                continue;
            }

            Vector3 world = tilemap.GetCellCenterWorld(cellPos);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, objectsParent);
            Undo.RegisterCreatedObjectUndo(go, "Import Dungeon Object");
            go.transform.position = world;
            go.transform.rotation = Quaternion.Euler(0, 0, entry.zRotation);
            if (!Mathf.Approximately(cellWorld, 1f))
                go.transform.localScale *= cellWorld;

            if (o == 'E') entryCount++;
            AssignUniqueId(go, ref rockAuto, ref rewardAuto);

            // 루트 조각이 놓인 칸의 [LINKS]는 링크 그룹이 아니라 구간 파라미터다 → 배선 대상에서 뺀다.
            char link = data.Links[row, col];
            var segment = go.GetComponent<RouteSegment>();
            if (segment != null)
                ApplyRouteModifier(segment, link, row, col);
            else
                placed.Add((link, go));
        }

        WireLinks(placed);
        tilemap.RefreshAllTiles(); // RuleTile 방향 재평가 보장

        if (entryCount != 1)
            Debug.LogWarning($"[DungeonImport] DungeonEntryPoint(E)가 정확히 1개여야 하는데 {entryCount}개입니다.");

        Debug.Log($"[DungeonImport] 완료: {data.Name} ({data.Width}x{data.Height}) → '{_mapRoot.name}'");
        EditorSceneMarkDirty();
    }

    /// <summary>
    /// 이웃 칸 두 개의 중심 거리로 칸의 월드 크기를 잰다.
    /// Grid의 cellSize만 보면 Map Root나 Grid에 걸린 스케일을 놓친다.
    /// </summary>
    private static float MeasureCellWorldSize(Tilemap tilemap)
    {
        Vector3 a = tilemap.GetCellCenterWorld(Vector3Int.zero);
        Vector3 b = tilemap.GetCellCenterWorld(new Vector3Int(1, 0, 0));
        float size = Mathf.Abs(b.x - a.x);
        return size > 0.0001f ? size : 1f;
    }

    /// <summary>
    /// 찍을 Tilemap을 정한다. 지정이 있으면 그것, 없으면 Map Root 아래에서 자동 탐색.
    /// 격자가 여러 개인 프리팹(예: 배경용 cellSize 2 + 오브젝트용 cellSize 1)에서
    /// 자동 탐색은 "먼저 찾은 것"을 집어서 조용히 엉뚱한 곳에 찍힌다 → 여러 개면 경고한다.
    /// </summary>
    private static Tilemap ResolveTilemap(Transform mapRoot, Tilemap explicitTilemap)
    {
        if (explicitTilemap != null) return explicitTilemap;

        var found = mapRoot.GetComponentsInChildren<Tilemap>(true);
        if (found.Length > 1)
        {
            var names = new List<string>();
            foreach (var t in found)
            {
                Vector3 cell = t.layoutGrid != null ? t.layoutGrid.cellSize : Vector3.one;
                names.Add($"'{t.name}'(부모 '{t.transform.parent?.name}', cellSize {cell.x}×{cell.y})");
            }
            Debug.LogWarning($"[DungeonImport] Map Root 아래에 Tilemap이 {found.Length}개입니다 — " +
                             $"먼저 찾은 {names[0]}에 찍습니다.\n후보: {string.Join(" / ", names)}\n" +
                             "의도한 곳이 아니면 창의 'Tilemap (선택)'에 직접 지정하세요.");
        }
        if (found.Length > 0) return found[0];

        Tilemap tilemap = null;

        var gridGo = new GameObject("Grid");
        Undo.RegisterCreatedObjectUndo(gridGo, "Create Grid");
        gridGo.transform.SetParent(mapRoot, false);
        gridGo.AddComponent<Grid>();

        var tmGo = new GameObject("Tilemap");
        tmGo.transform.SetParent(gridGo.transform, false);
        tilemap = tmGo.AddComponent<Tilemap>();
        tmGo.AddComponent<TilemapRenderer>();

        Debug.LogWarning("[DungeonImport] Map Root 아래 Tilemap이 없어 Grid+Tilemap을 자동 생성했습니다. " +
                         "콜라이더(TilemapCollider2D)·스케일을 직접 설정하세요.");
        return tilemap;
    }

    // Map Root 아래 "Objects" 컨테이너를 찾거나 생성한다.
    private static Transform ResolveObjectsContainer(Transform mapRoot)
    {
        var existing = mapRoot.Find(ObjectsContainerName);
        if (existing != null) return existing;

        var go = new GameObject(ObjectsContainerName);
        Undo.RegisterCreatedObjectUndo(go, "Create Objects Container");
        go.transform.SetParent(mapRoot, false);
        return go.transform;
    }

    // [LINKS] 그룹별로 소스(PressurePlate)의 targets에 같은 그룹 타겟(ILinkTarget)을 배선한다.
    // linkId '.'은 링크 없음. PressurePlate는 ILinkTarget 미구현이라 타겟 집합에 자기 자신이 섞이지 않는다.
    private static void WireLinks(List<(char linkId, GameObject go)> placed)
    {
        foreach (var group in placed.Where(p => p.linkId != '.').GroupBy(p => p.linkId))
        {
            var plates = group
                .Select(p => p.go.GetComponent<PressurePlate>())
                .Where(x => x != null)
                .ToList();
            var targets = group
                .SelectMany(p => p.go.GetComponents<MonoBehaviour>())
                .Where(m => m is ILinkTarget)
                .ToList();

            if (plates.Count == 0)
            {
                Debug.LogWarning($"[DungeonImport] 링크 그룹 '{group.Key}'에 압력판(PressurePlate)이 없습니다.");
                continue;
            }
            if (targets.Count == 0)
            {
                Debug.LogWarning($"[DungeonImport] 링크 그룹 '{group.Key}'에 타겟(ILinkTarget)이 없습니다.");
                continue;
            }

            foreach (var plate in plates)
            {
                plate.targets = targets.ToList(); // 압력판별 독립 복사본(공유 참조 방지)
                EditorUtility.SetDirty(plate);
            }
        }
    }

    /// <summary>
    /// 루트 조각이 놓인 칸의 [LINKS] 문자를 구간 파라미터로 해석한다.
    /// 압력판 링크와 격자를 공유하지만 루트 칸과 링크 칸은 겹칠 일이 없어 의미 충돌이 없다.
    /// 이 방식이 아니면 "빠른 직선"·"느린 코너"마다 프리팹 변형과 심볼을 따로 만들어야 한다.
    ///
    ///   '.'      기본        '1'~'9'  속도 ×1 ~ ×9
    ///   's' ×0.5   'x' ×0.25   'p'  이 칸에서 잠깐 정지
    /// </summary>
    private const float RoutePauseSeconds = 1f;

    private static void ApplyRouteModifier(RouteSegment segment, char link, int row, int col)
    {
        if (link == '.') return;

        if (link >= '1' && link <= '9')
            segment.speedMultiplier = link - '0';
        else if (link == 's')
            segment.speedMultiplier = 0.5f;
        else if (link == 'x')
            segment.speedMultiplier = 0.25f;
        else if (link == 'p')
            segment.pauseSeconds = RoutePauseSeconds;
        else
        {
            Debug.LogWarning($"[DungeonImport] 루트 칸의 알 수 없는 [LINKS] 문자 '{link}' @({row},{col}) — 무시");
            return;
        }

        EditorUtility.SetDirty(segment);
    }

    // reward/rock의 private 유일 id 자동 부여.
    private static void AssignUniqueId(GameObject go, ref int rockAuto, ref int rewardAuto)
    {
        var rock = go.GetComponent<DungeonRock>();
        if (rock != null)
        {
            var so = new SerializedObject(rock);
            so.FindProperty("rockId").intValue = rockAuto++;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        var reward = go.GetComponent<DungeonRewardPickup>();
        if (reward != null)
        {
            var so = new SerializedObject(reward);
            so.FindProperty("rewardId").stringValue = $"reward_{rewardAuto++}";
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void EditorSceneMarkDirty()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
