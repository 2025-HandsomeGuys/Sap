using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 업그레이드 시스템 로직 검증을 위한 테스트 스크립트입니다.
/// </summary>
public class UpgradeSystemTester : MonoBehaviour
{
    [Header("Tests")]
    public bool runTestsOnStart = true;

    void Start()
    {
        if (runTestsOnStart)
        {
            RunLogicTests();
        }
    }

    [ContextMenu("Run Logic Tests")]
    public void RunLogicTests()
    {
        Debug.Log("=== Upgrade System Logic Test Start ===");

        // 1. Setup Mock Data
        UpgradeEffectSO effectSpeed = ScriptableObject.CreateInstance<UpgradeEffectSO>();
        effectSpeed.type = UpgradeEffectType.MiningSpeedMultiplier;
        effectSpeed.value = 1.5f; // 50% increase
        effectSpeed.isPercentage = true;

        UpgradeNodeSO rootNode = ScriptableObject.CreateInstance<UpgradeNodeSO>();
        rootNode.nodeId = "test_root";
        rootNode.cost = 100;
        rootNode.effect = effectSpeed;

        UpgradeNodeSO childNode = ScriptableObject.CreateInstance<UpgradeNodeSO>();
        childNode.nodeId = "test_child";
        childNode.cost = 200;
        childNode.parentNodes = new List<UpgradeNodeSO> { rootNode };
        childNode.effect = effectSpeed; // another 50%

        UpgradeTreeSO testTree = ScriptableObject.CreateInstance<UpgradeTreeSO>();
        testTree.allNodes = new List<UpgradeNodeSO> { rootNode, childNode };
        testTree.rootNodes = new List<UpgradeNodeSO> { rootNode };

        // 2. Inject into Manager (Temporary)
        UpgradeManager manager = UpgradeManager.Instance;
        var originalTree = manager.upgradeTree;
        manager.upgradeTree = testTree;
        // Re-initialize cache
        manager.SendMessage("InitializeNodeCache", SendMessageOptions.DontRequireReceiver);

        // 3. Clear Player Data for Test
        SaveManager saveManager = FindFirstObjectByType<SaveManager>();
        if (saveManager == null || saveManager.playerData == null)
        {
            Debug.LogError("SaveManager or PlayerData missing!");
            return;
        }
        var originalState = saveManager.playerData.upgradeTreeState;
        saveManager.playerData.upgradeTreeState = new UpgradeTreeState(); // Reset
        
        PlayerStat playerStats = FindFirstObjectByType<PlayerStat>();
        if (playerStats == null)
        {
             Debug.LogError("PlayerStat missing!");
             return;
        }
        int originalGold = playerStats.Gold;
        // 테스트용 골드 설정: 기존 골드를 소진 후 1000 추가
        if (originalGold > 0) playerStats.SpendGold(originalGold);
        playerStats.AddGold(1000);

        // 4. Test Logic
        // Test A: Can unlock root?
        if (manager.CanUnlock(rootNode)) Debug.Log("Pass: Root is unlockable");
        else Debug.LogError("Fail: Root should be unlockable");

        // Test B: Can unlock child? (Should be false)
        if (!manager.CanUnlock(childNode)) Debug.Log("Pass: Child is locked");
        else Debug.LogError("Fail: Child should be locked");

        // Test C: Unlock Root
        manager.UnlockNode(rootNode);
        if (manager.IsNodeUnlocked(rootNode.nodeId)) Debug.Log("Pass: Root unlocked");
        else Debug.LogError("Fail: Root failed to unlock");

        // Test D: Check Stats (Base 1.0 * 1.5 = 1.5)
        float stat = manager.GetStatValue(UpgradeEffectType.MiningSpeedMultiplier, 1.0f);
        if (Mathf.Approximately(stat, 1.5f)) Debug.Log($"Pass: Stat correct ({stat})");
        else Debug.LogError($"Fail: Stat wrong. Expected 1.5, Got {stat}");

        // Test E: Unlock Child
        if (manager.CanUnlock(childNode)) Debug.Log("Pass: Child now unlockable");
        else Debug.LogError("Fail: Child should be unlockable now");
        
        manager.UnlockNode(childNode);
        
        // Test F: Check Stats Stacked (1.0 * 1.5 * 1.5 = 2.25)
        stat = manager.GetStatValue(UpgradeEffectType.MiningSpeedMultiplier, 1.0f);
        if (Mathf.Approximately(stat, 2.25f)) Debug.Log($"Pass: Stat stacked correct ({stat})");
        else Debug.LogError($"Fail: Stat stacked wrong. Expected 2.25, Got {stat}");

        // 5. Cleanup
        manager.upgradeTree = originalTree;
        manager.SendMessage("InitializeNodeCache", SendMessageOptions.DontRequireReceiver);
        saveManager.playerData.upgradeTreeState = originalState; // Restore
        // 테스트 골드 복원
        int currentGold = playerStats.Gold;
        if (currentGold > 0) playerStats.SpendGold(currentGold);
        playerStats.AddGold(originalGold);

        Debug.Log("=== Upgrade System Logic Test End ===");
    }
}
