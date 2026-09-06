// using UnityEngine;

// /// <summary>
// /// 업그레이드 슬롯의 툴팁 정보를 제공
// /// </summary>
// public class UpgradeTooltipProvider : MonoBehaviour, ITooltipProvider
// {
//     private ToolUpgradeStat upgradeStat;

//     public void Initialize(ToolUpgradeStat upgradeStat)
//     {
//         this.upgradeStat = upgradeStat;
//     }

//     public string GetTooltipTitle()
//     {
//         if (upgradeStat == null) return "";
        
//         switch (upgradeStat.upgradeType)
//         {
//             case ToolUpgradeType.ShovelStaminaReduction:
//                 return "삽: 스테미나 소모 감소";
//             case ToolUpgradeType.PickaxeDropRateIncrease:
//                 return "곡괭이: 광물 드랍률 증가";
//             case ToolUpgradeType.CommonDigRangeIncrease:
//                 return "공통: 파는 범위 증가";
//             case ToolUpgradeType.DrillSpeedIncrease:
//                 return "드릴: 전진 속도 증가";
//             case ToolUpgradeType.DrillDurationIncrease:
//                 return "드릴: 지속 시간 증가";
//             case ToolUpgradeType.HardnessLevel:
//                 return "강인도 레벨";
//             default:
//                 return "업그레이드";
//         }
//     }

//     public string GetTooltipContent()
//     {
//         if (upgradeStat == null) return "";

//         string content = "";
//         int level = upgradeStat.Level;
//         int currentCost = upgradeStat.CurrentCost;

//         switch (upgradeStat.upgradeType)
//         {
//             case ToolUpgradeType.ShovelStaminaReduction:
//                 content = "삽으로 땅을 팔 때 스테미나 소모량을 감소시킵니다.\n";
//                 content += $"현재 레벨: {level}\n";
//                 content += $"효과: 스테미나 소모 -{level * 5f:F1}%\n";
//                 content += $"다음 레벨 비용: {currentCost}골드";
//                 break;
//             case ToolUpgradeType.PickaxeDropRateIncrease:
//                 content = "곡괭이로 광물을 캘 때 추가 드랍 확률을 증가시킵니다.\n";
//                 content += $"현재 레벨: {level}\n";
//                 content += $"효과: 광물 드랍률 +{level * 3f:F1}%\n";
//                 content += $"다음 레벨 비용: {currentCost}골드";
//                 break;
//             case ToolUpgradeType.CommonDigRangeIncrease:
//                 content = "삽과 곡괭이의 파는 범위를 증가시킵니다.\n";
//                 content += $"현재 레벨: {level}\n";
//                 content += $"효과: 파는 범위 +{level * 10f:F1}%\n";
//                 content += $"다음 레벨 비용: {currentCost}골드";
//                 break;
//             case ToolUpgradeType.DrillSpeedIncrease:
//                 content = "드릴의 전진 속도를 증가시킵니다.\n";
//                 content += $"현재 레벨: {level}\n";
//                 content += $"효과: 전진 속도 +{level * 15f:F1}%\n";
//                 content += $"다음 레벨 비용: {currentCost}골드";
//                 break;
//             case ToolUpgradeType.DrillDurationIncrease:
//                 content = "드릴의 지속 시간을 증가시킵니다.\n";
//                 content += $"현재 레벨: {level}\n";
//                 content += $"효과: 지속 시간 +{level * 20f:F1}%\n";
//                 content += $"다음 레벨 비용: {currentCost}골드";
//                 break;
//             case ToolUpgradeType.HardnessLevel:
//                 content = "땅을 파는 강인도 레벨을 증가시킵니다.\n";
//                 content += $"현재 레벨: {level}\n";
//                 content += $"효과: 쿨다운 -{level * 2f:F1}%\n";
//                 content += $"다음 레벨 비용: {currentCost}골드\n\n";
//                 content += "해금 조건: 모든 도구 강화를 현재 강인도 레벨 이상으로 올려야 합니다.";
//                 break;
//         }

//         return content;
//     }
// }

