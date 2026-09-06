// @tags: background, chunk, layer, editor, tool, scriptable-object, terrain
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ChunkBackgroundTable 에셋 생성 툴.
/// tileData.json에 정의된 층으로 엔트리를 미리 채워준다(스프라이트는 비움).
///
/// 설계 문서: Assets/Docs/chunk-background-system.md
/// </summary>
public static class ChunkBackgroundTableCreator
{
    private const string AssetDir  = "Assets/Data";
    private const string AssetPath = AssetDir + "/ChunkBackgroundTable.asset";
    private const string JsonPath  = "Assets/StreamingAssets/tileData.json";

    [MenuItem("Tools/Terrain/Create Chunk Background Table")]
    public static void CreateTable()
    {
        // 이미 있으면 덮어쓰지 않고 선택만 한다 — 채워둔 스프라이트를 날리면 안 된다.
        var existing = AssetDatabase.LoadAssetAtPath<ChunkBackgroundTableSO>(AssetPath);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log($"[ChunkBackgroundTable] 이미 존재한다: {AssetPath}");
            return;
        }

        TileType[] layers = ReadLayersFromJson();
        if (layers == null || layers.Length == 0)
        {
            Debug.LogError($"[ChunkBackgroundTable] {JsonPath}에서 층을 읽지 못했다. 에셋을 만들지 않는다.");
            return;
        }

        if (!Directory.Exists(AssetDir))
            Directory.CreateDirectory(AssetDir);

        var table = ScriptableObject.CreateInstance<ChunkBackgroundTableSO>();
        table.Editor_SetLayers(layers);

        AssetDatabase.CreateAsset(table, AssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = table;
        EditorGUIUtility.PingObject(table);

        Debug.Log($"[ChunkBackgroundTable] 생성 완료: {AssetPath} (층 {layers.Length}개: {string.Join(", ", layers)})\n" +
                  "인스펙터에서 층별 배경 스프라이트를 채운 뒤 InfinityMapManager에 연결할 것. " +
                  "스프라이트는 pivot=Center로 임포트해야 한다.");
    }

    /// <summary>tileData.json의 tiles에서 TileType 목록을 startDepth 얕은→깊은 순으로 읽는다.</summary>
    private static TileType[] ReadLayersFromJson()
    {
        if (!File.Exists(JsonPath))
        {
            Debug.LogError($"[ChunkBackgroundTable] 파일 없음: {JsonPath}");
            return null;
        }

        TileDatabaseJson db;
        try
        {
            db = JsonUtility.FromJson<TileDatabaseJson>(File.ReadAllText(JsonPath));
        }
        catch (Exception e)
        {
            Debug.LogError($"[ChunkBackgroundTable] tileData.json 파싱 실패: {e.Message}");
            return null;
        }

        if (db?.tiles == null) return null;

        var tiles = new List<TileDataJson>(db.tiles);
        tiles.Sort((a, b) => b.startDepth.CompareTo(a.startDepth)); // 얕은(0) → 깊은(-60)

        var layers = new List<TileType>();
        foreach (var t in tiles)
        {
            if (!Enum.TryParse(t.tileType, true, out TileType type))
            {
                Debug.LogWarning($"[ChunkBackgroundTable] 알 수 없는 tileType 건너뜀: '{t.tileType}'");
                continue;
            }
            if (!layers.Contains(type))
                layers.Add(type);
        }

        return layers.ToArray();
    }
}
