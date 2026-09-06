// using UnityEngine;

// /// <summary>
// /// 도구 강화 효과를 관리하고 적용하는 매니저
// /// </summary>
// public class ToolUpgradeManager : MonoBehaviour
// {
//     private static ToolUpgradeManager _instance;
//     public static ToolUpgradeManager Instance
//     {
//         get
//         {
//             if (_instance == null)
//             {
//                 _instance = FindFirstObjectByType<ToolUpgradeManager>();
//                 if (_instance == null)
//                 {
//                     GameObject go = new GameObject("ToolUpgradeManager");
//                     _instance = go.AddComponent<ToolUpgradeManager>();
//                 }
//             }
//             return _instance;
//         }
//     }

//     private ToolUpgradeData upgradeData;

//     void Awake()
//     {
//         if (_instance == null)
//         {
//             _instance = this;
//             DontDestroyOnLoad(gameObject);
//         }
//         else if (_instance != this)
//         {
//             Destroy(gameObject);
//             return;
//         }

//         // LoadUpgradeData() 제거: SaveManager.Load()가 완료된 후 RefreshUpgradeData()를 호출하도록 변경
//         // 초기에는 빈 데이터로 시작 (SaveManager.Load()에서 실제 데이터 로드)
//         upgradeData = new ToolUpgradeData();
//     }

//     // SaveManager에서 데이터를 불러온 후 호출하여 동기화
//     public void RefreshUpgradeData()
//     {
//         SaveManager saveManager = FindFirstObjectByType<SaveManager>();
//         if (saveManager != null)
//         {
//             upgradeData = saveManager.GetToolUpgradeData();
//             if (upgradeData == null)
//             {
//                 upgradeData = new ToolUpgradeData();
//             }
//             Debug.Log("ToolUpgradeManager: 데이터 새로고침 완료");
//         }
//     }

//     // 삽: 스테미나 소모 감소 (퍼센트)
//     public float GetShovelStaminaReduction()
//     {
//         if (upgradeData == null) return 0f;
//         int level = upgradeData.GetLevel(ToolUpgradeType.ShovelStaminaReduction);
//         // 레벨당 5% 감소 (예: 레벨 1 = 5%, 레벨 2 = 10%)
//         return level * 5f;
//     }

//     // 곡괭이: 광물 추가 드랍률 증가 (퍼센트)
//     public float GetPickaxeDropRateIncrease()
//     {
//         if (upgradeData == null) return 0f;
//         int level = upgradeData.GetLevel(ToolUpgradeType.PickaxeDropRateIncrease);
//         // 레벨당 3% 증가 (예: 레벨 1 = 3%, 레벨 2 = 6%)
//         return level * 3f;
//     }

//     // 공통: 파는 범위 증가 (퍼센트)
//     public float GetCommonDigRangeIncrease()
//     {
//         if (upgradeData == null) return 0f;
//         int level = upgradeData.GetLevel(ToolUpgradeType.CommonDigRangeIncrease);
//         // 레벨당 10% 증가 (예: 레벨 1 = 10%, 레벨 2 = 20%)
//         return level * 10f;
//     }

//     // 드릴: 전진 속도 증가 (퍼센트)
//     public float GetDrillSpeedIncrease()
//     {
//         if (upgradeData == null) return 0f;
//         int level = upgradeData.GetLevel(ToolUpgradeType.DrillSpeedIncrease);
//         // 레벨당 15% 증가
//         return level * 15f;
//     }

//     // 드릴: 지속 시간 증가 (퍼센트)
//     public float GetDrillDurationIncrease()
//     {
//         if (upgradeData == null) return 0f;
//         int level = upgradeData.GetLevel(ToolUpgradeType.DrillDurationIncrease);
//         // 레벨당 20% 증가
//         return level * 20f;
//     }

//     // 강인도 레벨 가져오기
//     public int GetHardnessLevel()
//     {
//         if (upgradeData == null) return 0;
//         return upgradeData.GetHardnessLevel();
//     }

//     // 특정 강인도 레벨이 해금되었는지 확인
//     public bool IsHardnessUnlocked(int level)
//     {
//         if (upgradeData == null) return false;
//         return upgradeData.IsHardnessUnlocked(level);
//     }

//     // 강인도: 땅을 판 뒤 다음 땅을 다시 팔 때까지 걸리는 시간 감소 (퍼센트)
//     public float GetHardnessCooldownReduction()
//     {
//         if (upgradeData == null) return 0f;
//         int hardnessLevel = GetHardnessLevel();
//         // 강인도 레벨당 2% 감소 (예: 레벨 1 = 2%, 레벨 2 = 4%)
//         return hardnessLevel * 2f;
//     }

//     // 타일 타입에 필요한 강인도 레벨 반환
//     public int GetRequiredHardnessLevel(TileType tileType)
//     {
//         // TileType을 LayerType으로 변환하여 필요한 강인도 레벨 반환
//         switch (tileType)
//         {
//             case TileType.Dirt:           // Layer 1
//                 return 1;
//             case TileType.HardStone:      // Layer 2
//                 return 2;
//             case TileType.CoolStone:      // Layer 3
//                 return 3;
//             case TileType.Ice:            // Layer 4
//                 return 4;
//             case TileType.HotStone:       // Layer 5
//                 return 5;
//             case TileType.MagmaRock:      // Layer 6
//                 return 6;
//             case TileType.MeteoriteRock:   // Layer 7
//                 return 7;
//             default:
//                 return 0; // Empty나 Bedrock은 강인도 불필요
//         }
//     }

//     // 특정 타일을 파괴할 수 있는지 확인
//     public bool CanBreakTile(TileType tileType)
//     {
//         int requiredLevel = GetRequiredHardnessLevel(tileType);
//         if (requiredLevel == 0) return true; // 강인도 불필요한 타일

//         int currentHardness = GetHardnessLevel();
//         return currentHardness >= requiredLevel;
//     }

//     // 강인도에 따른 이전 단계 타일 파괴 보너스 (퍼센트)
//     public float GetHardnessBonusForTile(TileType tileType)
//     {
//         int requiredLevel = GetRequiredHardnessLevel(tileType);
//         if (requiredLevel == 0) return 0f;

//         int currentHardness = GetHardnessLevel();
//         if (currentHardness <= requiredLevel) return 0f;

//         // 현재 강인도가 필요한 강인도보다 높으면 보너스 제공
//         int bonusLevel = currentHardness - requiredLevel;
//         // 레벨 차이당 5% 보너스 (예: 레벨 3으로 레벨 1 파기 = 10% 보너스)
//         return bonusLevel * 5f;
//     }
// }


