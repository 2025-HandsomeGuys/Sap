using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonShapeAssetTests
{
    private const string AssetPath = "Assets/DungeonMaps/Shapes/basic_shapes.txt";

    private static ShapeParseResult Load()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
        Assert.IsNotNull(asset, $"{AssetPath} 를 찾지 못했습니다.");
        return DungeonShapeParser.Parse(asset.text, "basic_shapes");
    }

    [Test]
    public void BasicShapes_ParseWithoutErrorOrWarning()
    {
        var r = Load();
        Assert.IsTrue(r.Success, string.Join("\n", r.Errors));
        Assert.AreEqual(0, r.Warnings.Count, string.Join("\n", r.Warnings));
        Assert.GreaterOrEqual(r.Masks.Count, 3);
    }

    // 칸 수가 늘면 탐험 시간이 그대로 늘어난다. 눈에 띄게 커지면 의도한 것인지 확인하게 만든다.
    [Test]
    public void BasicShapes_StayWithinRoomBudget()
    {
        var r = Load();
        foreach (var m in r.Masks)
        {
            int rooms = 0;
            for (int row = 0; row < m.Height; row++)
                for (int col = 0; col < m.Width; col++)
                    if (m.IsDungeon(row, col)) rooms++;

            Assert.LessOrEqual(rooms, 14, $"'{m.Name}' 방 {rooms}칸 — 14칸을 넘으면 던전이 너무 커진다");
            Assert.GreaterOrEqual(rooms, 6, $"'{m.Name}' 방 {rooms}칸 — 너무 작다");
        }
    }
}
