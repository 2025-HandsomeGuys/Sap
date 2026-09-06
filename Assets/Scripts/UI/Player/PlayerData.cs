using System;
using UnityEngine;
using Stock.Data;
using Coin.Data;

[Serializable]
public class PlayerData
{
    [Header("Save Slot")]
    // 플레이어가 붙인 세이브 표시 이름. 비어 있으면(구버전 세이브 포함) UI가 "슬롯 N"으로 폴백한다.
    // 정규화 규칙은 SaveSlotName.Sanitize — 직접 대입하지 말고 그쪽을 통할 것.
    public string slotName;

    [Header("Progress")]
    public bool isTutorialCompleted = false;

    // 위 플래그를 이 세이브가 실제로 관리한 적 있는가.
    // 튜토리얼 완료를 기록하는 코드가 없던 시절의 세이브는 이 값이 false로 읽히고,
    // SaveManager.Load가 그런 세이브를 "튜토리얼은 이미 지났다"로 승격한다.
    // (아니면 한참 진행한 세이브가 이어하기마다 튜토리얼 씬으로 끌려간다)
    public bool tutorialFlagInitialized = false;

    [Header("Stamina")]
    public float maxStamina;
    public float currentStamina;
    public float staminaCostPerSecond;

    [Header("Movement")]
    public float moveSpeed;
    public float jumpForce;
    public float wallClimbingSpeed;
    public float encumberedSpeedMultiplier;

    [Header("Inventory")]
    public ItemInventoryData itemInventory = new ItemInventoryData();
    public MineralInventoryData mineralInventory = new MineralInventoryData();
    public EquipmentInventoryData equipmentInventory = new EquipmentInventoryData();
    public EquipmentUpgradeSaveData equipmentUpgrade = new EquipmentUpgradeSaveData();

    [Header("Mining")]
    public int miningLevel;
    public float miningRange;
    public float miningCooldown;

    [Header("Gold")]
    public int gold;

    [Header("Telemetry")]
    // 회차(playthrough) 식별자. 뉴게임마다 GUID 발급 → 밸런스 CSV의 기본키.
    // 구버전 세이브는 빈 문자열로 로드되고 SaveManager.Load()에서 발급된다.
    public string runId;
    // QA 픽스처·디버그 명령이 한 번이라도 손댄 회차. 한 번 서면 되돌리지 않는다.
    public bool isFixture;

    [Header("Warehouse")]
    public WarehouseData warehouseData = new WarehouseData();

    [Header("Codex (도감 — 광물·장비·아이템·유물 발견 기록)")]
    public CollectionCodexData collectionCodex = new CollectionCodexData();

    [Header("Elevator (정류장 해금 기록 — 직접 가본 층만 이동 가능)")]
    public ElevatorStopUnlockData elevatorStops = new ElevatorStopUnlockData();

    [Header("New Upgrade Tree")]
    public UpgradeTreeState upgradeTreeState = new UpgradeTreeState();

    [Header("Quest Progress")]
    public QuestProgressData questProgress = new QuestProgressData();

    [Header("Day Cycle")]
    public int currentDay = 1;
    public TimeOfDay currentTime = TimeOfDay.Morning;
    // 낮잠 사용량과 그것이 '어느 날짜의 것인지'. NapManager가 현재 날짜와 napDay가 다르면
    // 새 하루로 보고 napUsedToday를 0으로 리셋한다(napDay를 함께 저장하므로 로드 시 오탐 리셋이 없다).
    // (하루 가능 횟수는 UpgradeEffectType.NapCount 업그레이드로 결정 — 세이브에는 '사용량'만 남긴다)
    public int napUsedToday = 0;
    public int napDay = 0;

    [Header("Guide")]
    // 이미 본 가이드 id 목록 (GuideManager가 1회성 가이드 중복 노출 방지에 사용)
    public System.Collections.Generic.List<string> seenGuideIds = new System.Collections.Generic.List<string>();

    [Header("Stock Simulation")]
    public StockSaveData stockSave = new StockSaveData();

    [Header("Coin Minigame")]
    public CoinSaveData coinSave = new CoinSaveData();

    [Header("Day Earnings Ledger")]
    public DayEarningsSaveData dayEarnings = new DayEarningsSaveData();

    [Header("Dokkaebi Cauldron")]
    public CauldronSaveData cauldronSave = new CauldronSaveData();

    [Header("Dungeon Instance")]
    public bool isReturningFromDungeon = false;
    public Vector3 preDungeonPosition = Vector3.zero;
    public DungeonSaveData dungeonSave = new DungeonSaveData();

    // 지상 엘리베이터 입구로 지하에 진입할 때, PlayerSpawner가 기본 스폰 대신
    // 이 청크 좌표(엘리베이터 위치)에 착지시키도록 하는 1회성 오버라이드.
    // 지상 UI가 세팅 → 지하 씬 로드 → PlayerSpawner가 소비 후 false로 리셋.
    [Header("Elevator Entry (지상→지하 엘리베이터)")]
    public bool spawnAtElevator = false;
    public int elevatorEntryXChunk = 0;
    public int elevatorEntryYChunk = 0;

    // 이번 잠수의 엘리베이터 배치 시드. 층별 엘리베이터 X(ElevatorStopLayout)를 흔든다.
    // SaveManager.PrepareUndergroundEntry가 지하로 내려갈 때마다 새로 뽑고, Load가 되살린다.
    // 잠수 중에는 절대 안 바뀐다 — 청크는 언로드/재로드가 잦아서 매번 같은 값이 나와야 한다.
    // 0이면 시드 없는 기본 배치.
    public int diveElevatorSeed = 0;

    // 지하로 내려갈 때 사용한 입구. 지상 복귀 시 SurfaceSpawnPlacer가 나올 위치를 고르는 데 쓴다.
    // 값은 SurfaceReturnPoint enum과 동일 (0=기본/집 앞, 1=구멍, 2=엘리베이터).
    // 기록은 SurfaceReturnRouter.Stamp, 소진은 SurfaceReturnRouter.HandOff.
    [Header("Surface Return (지하 진입 입구 기록)")]
    public int undergroundEntryKind = 0;

    [Header("Relic")]
    public Relic.Data.RelicSaveData relicSave = new Relic.Data.RelicSaveData();

    // Detection compass (탐지파동 유물)
    [Header("Detection")] public DetectedChunkSaveData detectedChunks = new DetectedChunkSaveData();

    // JSON 역직렬화용 기본 생성자 (필드는 0/default)
    public PlayerData() { }

    // 뉴게임용: SO가 유일한 초기값 원천
    public PlayerData(PlayerSO so)
    {
        // 새로 만든 데이터는 플래그를 이 세이브가 관리한다 — 구버전 승격 대상이 아니다.
        isTutorialCompleted = false;
        tutorialFlagInitialized = true;

        if (so == null) return;

        // Stamina
        maxStamina = so.maxStamina;
        currentStamina = so.currentStamina > 0 ? so.currentStamina : so.maxStamina;
        staminaCostPerSecond = so.staminaCostPerSecond;

        // Movement
        moveSpeed = so.moveSpeed;
        jumpForce = so.jumpForce;
        wallClimbingSpeed = so.wallClimbingSpeed;
        encumberedSpeedMultiplier = so.encumberedSpeedMultiplier;

        // Inventory
        itemInventory = new ItemInventoryData();
        mineralInventory = new MineralInventoryData();
        equipmentInventory = new EquipmentInventoryData();

        // Mining
        miningLevel = so.miningLevel;
        miningRange = so.miningRange;
        miningCooldown = so.miningCooldown;

        // Gold
        gold = so.gold;

        // Warehouse
        warehouseData = new WarehouseData();

        // New Upgrade Tree
        upgradeTreeState = new UpgradeTreeState();

        // Quest Progress
        questProgress = new QuestProgressData();

        // Day Cycle
        currentDay = 1;
        currentTime = TimeOfDay.Morning;

        // Guide (아직 아무 가이드도 안 봄)
        seenGuideIds = new System.Collections.Generic.List<string>();
    }
}

[Serializable]
public class UpgradeTreeState
{
    // 해금된 노드의 ID 목록
    public System.Collections.Generic.List<string> unlockedNodeIds = new System.Collections.Generic.List<string>();
    
    // 해금된 계층(Tier) 목록 (0부터 시작, 0은 기본적으로 해금됨)
    public System.Collections.Generic.List<int> unlockedTiers = new System.Collections.Generic.List<int>();

    // 다단계 노드의 현재 레벨. Lv1은 여기 적지 않는다 —
    // unlockedNodeIds에 있으면 곧 Lv1이므로, 구버전 세이브도 그대로 Lv1로 읽힌다.
    // (JsonUtility가 Dictionary를 못 다뤄서 리스트로 둔다)
    public System.Collections.Generic.List<NodeLevelEntry> nodeLevels = new System.Collections.Generic.List<NodeLevelEntry>();

    public UpgradeTreeState()
    {
        // 생성자에서 한 번 더 초기화 보장
        if (unlockedNodeIds == null) unlockedNodeIds = new System.Collections.Generic.List<string>();
        if (unlockedTiers == null) unlockedTiers = new System.Collections.Generic.List<int>();
        if (nodeLevels == null) nodeLevels = new System.Collections.Generic.List<NodeLevelEntry>();
        
        // Tier 0은 기본적으로 항상 해금
        if (!unlockedTiers.Contains(0)) unlockedTiers.Add(0);
    }

    public bool IsUnlocked(string nodeId)
    {
        if (unlockedNodeIds == null) unlockedNodeIds = new System.Collections.Generic.List<string>();
        return unlockedNodeIds.Contains(nodeId);
    }

    public void Unlock(string nodeId)
    {
        if (unlockedNodeIds == null) unlockedNodeIds = new System.Collections.Generic.List<string>();
        if (!IsUnlocked(nodeId))
        {
            unlockedNodeIds.Add(nodeId);
        }
    }

    /// <summary>
    /// 현재 레벨. 안 샀으면 0, 해금만 되어 있으면 1.
    /// </summary>
    public int GetLevel(string nodeId)
    {
        if (!IsUnlocked(nodeId)) return 0;
        if (nodeLevels != null)
        {
            for (int i = 0; i < nodeLevels.Count; i++)
                if (nodeLevels[i] != null && nodeLevels[i].nodeId == nodeId)
                    return UnityEngine.Mathf.Max(1, nodeLevels[i].level);
        }
        return 1;
    }

    /// <summary>
    /// 레벨을 1 올리고 올라간 레벨을 반환한다. 첫 구매면 해금 목록에도 넣는다.
    /// </summary>
    public int AddLevel(string nodeId)
    {
        int next = GetLevel(nodeId) + 1;
        SetLevel(nodeId, next);
        return next;
    }

    /// <summary>
    /// 레벨을 직접 세팅한다. 0 이하면 해금 자체를 취소한다(디버그 되돌리기용).
    /// </summary>
    public void SetLevel(string nodeId, int level)
    {
        if (nodeLevels == null) nodeLevels = new System.Collections.Generic.List<NodeLevelEntry>();

        if (level <= 0)
        {
            unlockedNodeIds?.Remove(nodeId);
            nodeLevels.RemoveAll(e => e == null || e.nodeId == nodeId);
            return;
        }

        Unlock(nodeId);

        // Lv1은 unlockedNodeIds만으로 표현된다 — 항목을 남기면 구버전 세이브와 표현이 갈린다
        if (level <= 1)
        {
            nodeLevels.RemoveAll(e => e == null || e.nodeId == nodeId);
            return;
        }

        for (int i = 0; i < nodeLevels.Count; i++)
        {
            if (nodeLevels[i] != null && nodeLevels[i].nodeId == nodeId)
            {
                nodeLevels[i].level = level;
                return;
            }
        }
        nodeLevels.Add(new NodeLevelEntry { nodeId = nodeId, level = level });
    }

    /// <summary>해금·레벨을 전부 지운다(디버그 초기화용).</summary>
    public void ClearNodes()
    {
        unlockedNodeIds?.Clear();
        nodeLevels?.Clear();
    }

    /// <summary>
    /// 특정 계층이 해금되었는지 확인합니다.
    /// </summary>
    public bool IsTierUnlocked(int tier)
    {
        if (unlockedTiers == null) 
        {
            unlockedTiers = new System.Collections.Generic.List<int> { 0 };
        }
        
        // Tier 0은 항상 해금된 상태여야 함
        if (!unlockedTiers.Contains(0)) unlockedTiers.Add(0);
        
        return unlockedTiers.Contains(tier);
    }

    /// <summary>
    /// 계층을 해금합니다.
    /// </summary>
    public void UnlockTier(int tier)
    {
        if (unlockedTiers == null) unlockedTiers = new System.Collections.Generic.List<int> { 0 };
        if (!IsTierUnlocked(tier))
        {
            unlockedTiers.Add(tier);
            Debug.Log($"[UpgradeTreeState] 계층 {tier} 해금됨");
        }
    }
}

/// <summary>
/// 다단계 업그레이드 노드의 레벨 한 칸. Lv2 이상만 저장된다(<see cref="UpgradeTreeState.SetLevel"/>).
/// </summary>
[Serializable]
public class NodeLevelEntry
{
    public string nodeId;
    public int level;
}

