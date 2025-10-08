using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// MinableSpawnConfig 클래스를 인스펙터에서 어떻게 그릴지 정의하는 PropertyDrawer입니다.
[CustomPropertyDrawer(typeof(MineralGenerationProfile.MinableSpawnConfig))]
public class MinableSpawnConfigDrawer : PropertyDrawer
{
    // LayerType별로 포함되는 MineralID를 미리 정의해두는 맵입니다.
    private static readonly Dictionary<LayerType, MineralID[]> _layerMineralMapping;

    static MinableSpawnConfigDrawer()
    {
        _layerMineralMapping = new Dictionary<LayerType, MineralID[]>
        {
            [LayerType.SoftGround] = new[] { MineralID.Garbage, MineralID.PETBottle, MineralID.ScrapMetal, MineralID.Magnet, MineralID.Coal, MineralID.HalfCoin, MineralID.GearFragment },
            [LayerType.HardGround] = new[] { MineralID.Copper, MineralID.Obsidian },
            [LayerType.CoolGround] = new[] { MineralID.IronOre, MineralID.Silver, MineralID.BlueCrystal, MineralID.CashCoin },
            [LayerType.IceAgeGround] = new[] { MineralID.AncientFish, MineralID.Cryptomoning, MineralID.Dooly },
            [LayerType.HotGround] = new[] { MineralID.GoldOre, MineralID.Fossil, MineralID.HumanSkeleton },
            [LayerType.MagmaGround] = new[] { MineralID.EssenceOfLava, MineralID.Basalt },
            [LayerType.FinalGround] = new[] { MineralID.MeteoriteFragment, MineralID.Sapphire, MineralID.Ruby, MineralID.Vibranium, MineralID.LightCoin }
        };
    }

    // 인스펙터에 GUI를 그리는 메인 함수입니다.
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Foldout (접고 펴기) UI를 그립니다.
        var foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

        // Foldout이 펼쳐져 있을 때만 내부 필드들을 그립니다.
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            
            var spacing = 5f; // 늘어난 간격
            // 시작 Rect를 Foldout 바로 아래로 설정합니다. 높이는 각 필드에 맞게 동적으로 설정될 것입니다.
            var currentRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + spacing, position.width, 0);

            // 1. 'description' 필드
            var descriptionProp = property.FindPropertyRelative("description");
            currentRect.height = EditorGUI.GetPropertyHeight(descriptionProp);
            EditorGUI.PropertyField(currentRect, descriptionProp);
            currentRect.y += currentRect.height + spacing;

            // 2. 'minableType' 필드를 필터링된 드롭다운으로 그립니다.
            LayerType parentLayerType = GetParentLayerType(property);
            _layerMineralMapping.TryGetValue(parentLayerType, out MineralID[] filteredMinerals);
            if (filteredMinerals == null) filteredMinerals = new MineralID[0];

            var minableTypeProp = property.FindPropertyRelative("minableType");
            // BUG FIX: Use intValue instead of enumValueIndex for enums with explicit values.
            var currentMineralID = (MineralID)minableTypeProp.intValue;

            int selectedIndex = System.Array.IndexOf(filteredMinerals, currentMineralID);
            if (selectedIndex < 0) selectedIndex = 0;

            string[] filteredMineralNames = filteredMinerals.Select(m => m.ToString()).ToArray();
            
            currentRect.height = EditorGUIUtility.singleLineHeight; // Popup은 한 줄 높이입니다.
            int newSelectedIndex = EditorGUI.Popup(currentRect, "Minable Type", selectedIndex, filteredMineralNames);

            if (newSelectedIndex >= 0 && newSelectedIndex < filteredMinerals.Length)
            {
                // BUG FIX: Assign the actual integer value of the enum, not its index.
                minableTypeProp.intValue = (int)filteredMinerals[newSelectedIndex];
            }
            currentRect.y += currentRect.height + spacing;

            // 3. 나머지 필드들을 순서대로 그립니다.
            var spawnChanceProp = property.FindPropertyRelative("spawnChanceByDepth");
            currentRect.height = EditorGUI.GetPropertyHeight(spawnChanceProp);
            EditorGUI.PropertyField(currentRect, spawnChanceProp, true);
            currentRect.y += currentRect.height + spacing;

            var veinsPerChunkProp = property.FindPropertyRelative("veinsPerChunk");
            currentRect.height = EditorGUI.GetPropertyHeight(veinsPerChunkProp);
            EditorGUI.PropertyField(currentRect, veinsPerChunkProp, true);
            currentRect.y += currentRect.height + spacing;
            
            var veinLengthProp = property.FindPropertyRelative("veinLength");
            currentRect.height = EditorGUI.GetPropertyHeight(veinLengthProp);
            EditorGUI.PropertyField(currentRect, veinLengthProp, true);
            currentRect.y += currentRect.height + spacing;

            var veinSpacingProp = property.FindPropertyRelative("veinSpacing");
            currentRect.height = EditorGUI.GetPropertyHeight(veinSpacingProp);
            EditorGUI.PropertyField(currentRect, veinSpacingProp, true);

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    // PropertyDrawer의 전체 높이를 동적으로 다시 계산합니다.
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float totalHeight = EditorGUIUtility.singleLineHeight; // Foldout 자체의 높이

        if (property.isExpanded)
        {
            var spacing = 5f; // 늘어난 간격
            // 펼쳐졌을 때, 모든 자식 필드들의 높이를 더해줍니다.
            totalHeight += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("description")) + spacing;
            totalHeight += EditorGUIUtility.singleLineHeight + spacing; // minableType Popup 높이
            totalHeight += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("spawnChanceByDepth")) + spacing;
            totalHeight += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("veinsPerChunk")) + spacing;
            totalHeight += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("veinLength")) + spacing;
            totalHeight += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("veinSpacing")) + spacing;
        }

        return totalHeight;
    }

    // 현재 프로퍼티의 부모인 TerrainLayer에서 LayerType을 가져오는 함수입니다.
    private LayerType GetParentLayerType(SerializedProperty property)
    {
        string path = property.propertyPath;
        string parentPath = path.Substring(0, path.LastIndexOf(".mineralConfigs"));

        SerializedProperty parentLayerProp = property.serializedObject.FindProperty(parentPath);
        if (parentLayerProp != null)
        {
            SerializedProperty layerTypeProp = parentLayerProp.FindPropertyRelative("layerType");
            if (layerTypeProp != null)
            {
                return (LayerType)layerTypeProp.enumValueIndex;
            }
        }
        return LayerType.SoftGround;
    }
}