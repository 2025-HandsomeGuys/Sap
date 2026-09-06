// using UnityEngine;
// using UnityEngine.UI;
// using TMPro;

// /// <summary>
// /// 도구별 강화 슬롯 컴포넌트
// /// </summary>
// public class ToolUpgradeStat : MonoBehaviour
// {
//     [Header("References")]
//     public PlayerStatsController playerStats;
//     public Button button;
//     public TextMeshProUGUI costText;
//     public TextMeshProUGUI levelText;
//     // statValueText는 툴팁으로 대체되어 제거됨

//     [Header("Upgrade Settings")]
//     public ToolUpgradeType upgradeType;
//     public int baseCost = 10;
//     public int costIncrease = 5;
//     public float increaseAmount = 10; // 레벨당 증가량 (퍼센트 또는 절대값)

//     private int level = 0;
//     private int currentCost;
//     private ToolUpgradeData upgradeData;

//     // 툴팁에서 접근하기 위한 프로퍼티
//     public int Level => level;
//     public int CurrentCost => currentCost;

//     void Start()
//     {
//         if (playerStats == null)
//             playerStats = FindFirstObjectByType<PlayerStatsController>();

//         LoadUpgradeData();

//         if (playerStats != null)
//         {
//             playerStats.OnGoldChanged += OnGoldChanged;
//         }

//         CalculateCurrentCost();
//         UpdateUI();
        
//         if (button != null)
//             button.onClick.AddListener(OnClickUpgrade);

//         // 툴팁 트리거 추가
//         SetupTooltip();
//     }

//     private void SetupTooltip()
//     {
//         // 툴팁 트리거 추가
//         TooltipTrigger tooltipTrigger = GetComponent<TooltipTrigger>();
//         if (tooltipTrigger == null)
//         {
//             tooltipTrigger = gameObject.AddComponent<TooltipTrigger>();
//         }

//         // 툴팁 제공자 추가
//         UpgradeTooltipProvider tooltipProvider = GetComponent<UpgradeTooltipProvider>();
//         if (tooltipProvider == null)
//         {
//             tooltipProvider = gameObject.AddComponent<UpgradeTooltipProvider>();
//         }
//         tooltipProvider.Initialize(this);
//         tooltipTrigger.tooltipProvider = tooltipProvider;
//     }

//     // UI가 활성화될 때마다 데이터를 다시 로드 (업그레이드 탭을 열 때마다)
//     void OnEnable()
//     {
//         LoadUpgradeData();
//         CalculateCurrentCost();
//         UpdateUI();
//     }

//     void OnDestroy()
//     {
//         if (playerStats != null)
//         {
//             playerStats.OnGoldChanged -= OnGoldChanged;
//         }
//     }

//     private void OnGoldChanged(int newGold)
//     {
//         UpdateUI();
//     }

//     public void LoadUpgradeData()
//     {
//         SaveManager saveManager = FindFirstObjectByType<SaveManager>();
//         if (saveManager != null)
//         {
//             upgradeData = saveManager.GetToolUpgradeData();
//             if (upgradeData != null)
//             {
//                 if (upgradeType == ToolUpgradeType.HardnessLevel)
//                 {
//                     // 강인도는 현재 레벨 가져오기
//                     level = upgradeData.GetHardnessLevel();
//                 }
//                 else
//                 {
//                     level = upgradeData.GetLevel(upgradeType);
//                 }
//             }
//         }
        
//         if (upgradeData == null)
//         {
//             upgradeData = new ToolUpgradeData();
//         }
        
//         // 레벨이 로드되었으므로 비용 재계산
//         CalculateCurrentCost();
//     }

//     private void SaveUpgradeData()
//     {
//         SaveManager saveManager = FindFirstObjectByType<SaveManager>();
//         if (saveManager != null)
//         {
//             if (upgradeType == ToolUpgradeType.HardnessLevel)
//             {
//                 // 강인도는 HardnessLevel 타입으로 저장
//                 saveManager.SetToolUpgradeLevel(ToolUpgradeType.HardnessLevel, level);
//             }
//             else
//             {
//                 saveManager.SetToolUpgradeLevel(upgradeType, level);
//             }
//         }
//     }

//     private void CalculateCurrentCost()
//     {
//         currentCost = baseCost + (level * costIncrease);
//     }

//     // 강인도 해금 조건 확인 (다음 레벨로 업그레이드 가능한지)
//     private bool CanUpgradeHardness()
//     {
//         if (upgradeType != ToolUpgradeType.HardnessLevel) return true;

//         SaveManager saveManager = FindFirstObjectByType<SaveManager>();
//         if (saveManager == null) return false;

//         ToolUpgradeData data = saveManager.GetToolUpgradeData();
//         if (data == null) return false;

//         int currentHardnessLevel = data.GetHardnessLevel();
//         int nextLevel = currentHardnessLevel + 1;

//         // 최대 레벨 체크
//         if (nextLevel > 7) return false;

//         // 이전 단계의 모든 도구 강화가 완료되었는지 확인
//         // 삽, 곡괭이, 공통, 드릴(속도, 지속시간) 모두 최소 currentHardnessLevel 이상이어야 함
//         int requiredLevel = currentHardnessLevel; // 현재 강인도 레벨과 같은 레벨의 도구 강화 필요
        
//         int shovelLevel = data.GetLevel(ToolUpgradeType.ShovelStaminaReduction);
//         int pickaxeLevel = data.GetLevel(ToolUpgradeType.PickaxeDropRateIncrease);
//         int commonLevel = data.GetLevel(ToolUpgradeType.CommonDigRangeIncrease);
//         int drillSpeedLevel = data.GetLevel(ToolUpgradeType.DrillSpeedIncrease);
//         int drillDurationLevel = data.GetLevel(ToolUpgradeType.DrillDurationIncrease);

//         // 모든 도구 강화가 최소 requiredLevel 이상이어야 함
//         if (shovelLevel < requiredLevel || 
//             pickaxeLevel < requiredLevel || 
//             commonLevel < requiredLevel || 
//             drillSpeedLevel < requiredLevel || 
//             drillDurationLevel < requiredLevel)
//         {
//             return false;
//         }

//         return true;
//     }

//     void OnClickUpgrade()
//     {
//         if (playerStats == null) return;

//         // 골드 확인
//         if (playerStats.gold < currentCost)
//         {
//             Debug.Log("골드가 부족합니다!");
//             return;
//         }

//         // 강인도 해금 조건 확인
//         if (upgradeType == ToolUpgradeType.HardnessLevel && !CanUpgradeHardness())
//         {
//             int currentLevel = upgradeData != null ? upgradeData.GetHardnessLevel() : 0;
//             Debug.Log($"강인도 레벨 {currentLevel + 1}을 해금하려면 모든 도구 강화를 레벨 {currentLevel} 이상으로 올려야 합니다!");
//             return;
//         }

//         // 골드 차감
//         if (!playerStats.SpendGold(currentCost))
//         {
//             return;
//         }

//         // 업그레이드 실행
//         level++;
//         CalculateCurrentCost();
//         SaveUpgradeData();
//         UpdateUI();

//         // 강인도 업그레이드 시 이벤트 발생
//         if (upgradeType == ToolUpgradeType.HardnessLevel)
//         {
//             OnHardnessUpgraded?.Invoke(level);
//         }
        
//         // 모든 업그레이드 후 해금 조건 갱신 (강인도 해금 조건이 변경될 수 있음)
//         ToolUpgradeUI upgradeUI = FindFirstObjectByType<ToolUpgradeUI>();
//         if (upgradeUI != null)
//         {
//             upgradeUI.UpdateUnlockConditionText();
//         }
//     }


//     public static System.Action<int> OnHardnessUpgraded;

//     public void UpdateUI()
//     {
//         if (playerStats == null) return;

//         if (costText != null)
//             costText.text = $"비용: {currentCost}골드";
        
//         if (levelText != null)
//             levelText.text = $"Lv. {level}";

//         // 상세 스텟 정보는 툴팁에서 표시됨

//         // 버튼 활성화 상태
//         if (button != null)
//         {
//             bool canAfford = playerStats.gold >= currentCost;
//             bool canUpgrade = upgradeType != ToolUpgradeType.HardnessLevel || CanUpgradeHardness();
//             button.interactable = canAfford && canUpgrade;

//             // 해금 불가능한 경우 시각적 표시 (선택사항)
//             if (upgradeType == ToolUpgradeType.HardnessLevel && !canUpgrade)
//             {
//                 // 버튼을 회색 처리하거나 잠금 아이콘 표시 가능
//             }
//         }
//     }

//     // GetStatDescription 메서드는 툴팁으로 대체되어 제거됨
//     // 상세 정보는 UpgradeTooltipProvider에서 제공됨
// }


