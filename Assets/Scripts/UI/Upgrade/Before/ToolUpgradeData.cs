// using System;
// using System.Collections.Generic;
// using UnityEngine;

// [Serializable]
// public class ToolUpgradeData
// {
//     [Header("도구별 강화 레벨")]
//     public Dictionary<ToolUpgradeType, int> upgradeLevels = new Dictionary<ToolUpgradeType, int>();

//     public ToolUpgradeData()
//     {
//         // 모든 업그레이드 타입을 0 레벨로 초기화
//         foreach (ToolUpgradeType type in System.Enum.GetValues(typeof(ToolUpgradeType)))
//         {
//             upgradeLevels[type] = 0;
//         }
//     }

//     public int GetLevel(ToolUpgradeType type)
//     {
//         if (upgradeLevels.ContainsKey(type))
//             return upgradeLevels[type];
//         return 0;
//     }

//     public void SetLevel(ToolUpgradeType type, int level)
//     {
//         upgradeLevels[type] = level;
//     }

//     public void IncreaseLevel(ToolUpgradeType type)
//     {
//         if (upgradeLevels.ContainsKey(type))
//             upgradeLevels[type]++;
//         else
//             upgradeLevels[type] = 1;
//     }

//     // 강인도 레벨 가져오기 (1~7)
//     public int GetHardnessLevel()
//     {
//         // 새로운 단일 HardnessLevel 타입 사용
//         int level = GetLevel(ToolUpgradeType.HardnessLevel);
//         if (level > 0) return level;
        
//         // 구버전 호환: 기존 HardnessLevel1~7에서 최대 레벨 찾기
//         int maxLevel = 0;
//         for (int i = 1; i <= 7; i++)
//         {
//             ToolUpgradeType hardnessType = (ToolUpgradeType)((int)ToolUpgradeType.HardnessLevel1 + i - 1);
//             if (upgradeLevels.ContainsKey(hardnessType) && upgradeLevels[hardnessType] > 0)
//             {
//                 maxLevel = i;
//             }
//         }
        
//         // 구버전 데이터가 있으면 새 형식으로 마이그레이션
//         if (maxLevel > 0)
//         {
//             SetLevel(ToolUpgradeType.HardnessLevel, maxLevel);
//             return maxLevel;
//         }
        
//         return 0;
//     }

//     // 특정 강인도 레벨이 해금되었는지 확인
//     public bool IsHardnessUnlocked(int level)
//     {
//         if (level < 1 || level > 7) return false;
//         int currentLevel = GetHardnessLevel();
//         return currentLevel >= level;
//     }

//     // JSON 직렬화를 위한 변환 메서드 (mineralInventory처럼 간단한 구조)
//     public ToolUpgradeInventoryData ToInventoryData()
//     {
//         ToolUpgradeInventoryData data = new ToolUpgradeInventoryData();
//         // 모든 업그레이드 타입을 저장 (레벨 0도 포함)
//         foreach (ToolUpgradeType type in System.Enum.GetValues(typeof(ToolUpgradeType)))
//         {
//             // 구버전 호환용 HardnessLevel1~7은 저장하지 않음 (HardnessLevel로 통합)
//             if (type >= ToolUpgradeType.HardnessLevel1 && type <= ToolUpgradeType.HardnessLevel7)
//             {
//                 continue;
//             }
            
//             int level = GetLevel(type);
//             data.slots.Add(new ToolUpgradeSlotData
//             {
//                 upgradeType = type.ToString(),
//                 level = level
//             });
//         }
//         return data;
//     }

//     public void FromInventoryData(ToolUpgradeInventoryData data)
//     {
//         upgradeLevels.Clear();
        
//         // 모든 업그레이드 타입을 0으로 초기화
//         foreach (ToolUpgradeType type in System.Enum.GetValues(typeof(ToolUpgradeType)))
//         {
//             upgradeLevels[type] = 0;
//         }
        
//         // 저장된 데이터 불러오기
//         if (data != null && data.slots != null)
//         {
//             foreach (var slot in data.slots)
//             {
//                 if (System.Enum.TryParse<ToolUpgradeType>(slot.upgradeType, out ToolUpgradeType type))
//                 {
//                     upgradeLevels[type] = slot.level;
//                 }
//             }
//         }
//     }
// }


