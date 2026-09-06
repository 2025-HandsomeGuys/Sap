// @tags: save, manager, singleton, json, persistence, slot
using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using Stock.Core;
using Coin.Core;

public struct SaveSlotSummary
{
    public bool hasData;
    public int day;
    public TimeOfDay time;
    public int gold;
    // 플레이어가 붙인 표시 이름. null이면 이름 없음 → UI가 "슬롯 N"으로 폴백한다.
    public string name;
    public bool isTutorialCompleted;
}

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; } // 싱글톤 추가

    public int CurrentSlotIndex { get; set; } = 0;
    public const int MaxSlots = 5;

    private string path => Path.Combine(Application.persistentDataPath, $"playerData_{CurrentSlotIndex}.json");

    [Header("기본값 설정용 SO")]
    public PlayerSO playerSO;

    private PlayerStat stats; // 내부에서 관리

    // stats가 '이번 씬에서 실제로 FromData를 거쳤는지' 판정하는 래치.
    // 씬이 바뀌면 PlayerStat 인스턴스가 새로 생기므로, 인스턴스 동일성 비교만으로
    // 씬 전환이 자동 반영된다(별도 리셋 불필요).
    //
    // 이게 없으면: 씬 활성화 ~ Load() 사이 몇 프레임 동안 Save()가 불릴 때
    // 프리팹 기본값을 든 PlayerStat이 세이브의 스탯·골드를 덮어쓴다.
    // 예전 가드(MaxStamina > 0)는 "컴포넌트가 빈 채 생성된 경우"만 잡았는데,
    // 실제로 일어나는 건 "프리팹 기본값(500) 그대로인 경우"라 통과해버렸다.
    private PlayerStat statsLoaded;
    private ItemInventory itemInventory;
    private MineralInventory mineralInventory;
    private EquipmentInventory equipmentInventory;
    
    // 외부 접근을 위한 프로퍼티
    public PlayerData playerData { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null); // 최상위 오브젝트로 분리하여 DontDestroyOnLoad 경고 방지
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 초기 참조 시도 (나중에 Load/Save에서 다시 갱신됨)
        stats = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include); 
        
        if (InventoryUI.Instance != null)
        {
            itemInventory = InventoryUI.Instance.itemInventory;
            mineralInventory = InventoryUI.Instance.mineralInventory;
            equipmentInventory = InventoryUI.Instance.equipmentInventory;
        }
        
        if (itemInventory == null) itemInventory = FindFirstObjectByType<ItemInventory>(FindObjectsInactive.Include);
        if (mineralInventory == null) mineralInventory = FindFirstObjectByType<MineralInventory>(FindObjectsInactive.Include);
        if (equipmentInventory == null) equipmentInventory = FindFirstObjectByType<EquipmentInventory>(FindObjectsInactive.Include);

        // 도감에 '처음' 등록되는 항목이 생기는 순간, 세이브 파일의 도감 칸만 즉시 갱신한다.
        // 지하 사망·강제종료로 전체 Save를 건너뛰어도 '무엇을 봤는지'는 영구히 남는다.
        // (이 인스턴스는 DontDestroyOnLoad 싱글톤이라 세션 내내 살아 구독이 안전하다.)
        CollectionCodex.OnDiscovered += HandleCodexDiscovered;
        ElevatorStopUnlockStore.OnUnlocked += HandleElevatorStopUnlocked;
    }

    private void OnDestroy()
    {
        // 정적 이벤트 리스너 누수 방지. Instance는 기존 동작대로 건드리지 않는다.
        CollectionCodex.OnDiscovered -= HandleCodexDiscovered;
        ElevatorStopUnlockStore.OnUnlocked -= HandleElevatorStopUnlocked;
    }

    // 도감에 새 항목이 처음 발견되면 파일의 collectionCodex만 덧쓴다(빈도 낮음 — 항목당 최초 1회).
    private void HandleCodexDiscovered(CodexCategory cat, string id) => PersistCodexOnly();

    // 정류장 해금도 같은 성격의 영구 기록이다 — 지하에서 죽어 전체 저장을 건너뛰어도
    // "분명 가봤는데 다시 잠김"이 되지 않도록 같은 경로로 즉시 덧쓴다(층당 최초 1회).
    private void HandleElevatorStopUnlocked(int depth) => PersistCodexOnly();

    /// <summary>
    /// 필요한 런타임 컴포넌트(PlayerStat, 인벤토리 등)들을 다시 찾습니다.
    /// 씬 전환 후나 Save/Load 직전에 호출하여 참조 유실을 방지합니다.
    /// </summary>
    public void RefreshReferences()
    {
        // 씬 전환 시 기존 참조가 파손되었을 수 있으므로 매번 재탐색합니다.
        stats = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);

        // 인벤토리는 씬마다 새 인스턴스가 생기므로 항상 최신 참조로 교체
        if (InventoryUI.Instance != null)
        {
            itemInventory      = InventoryUI.Instance.itemInventory;
            mineralInventory   = InventoryUI.Instance.mineralInventory;
            equipmentInventory = InventoryUI.Instance.equipmentInventory;
        }

        // InventoryUI가 없을 때뿐 아니라 '연결이 하나라도 비어 있을 때'도 씬에서 직접 찾는다.
        // 예전에는 InventoryUI가 있으면 그 값만 믿어서, 인스펙터 연결이 빠진 칸은 참조가 null로 덮였다.
        // equipmentInventory가 null이면 Save가 장비 저장을 통째로 건너뛰어(지하 진입 직전 저장!)
        // 지하에서 장비가 사라진다. Awake와 같은 '빈 칸만 채우기' 방식으로 맞춘다.
        if (itemInventory == null) itemInventory = FindFirstObjectByType<ItemInventory>(FindObjectsInactive.Include);
        if (mineralInventory == null) mineralInventory = FindFirstObjectByType<MineralInventory>(FindObjectsInactive.Include);
        if (equipmentInventory == null) equipmentInventory = FindFirstObjectByType<EquipmentInventory>(FindObjectsInactive.Include);
    }

    public bool HasSaveData()
    {
        return File.Exists(path);
    }

    public SaveSlotSummary[] GetSlotSummaries()
    {
        SaveSlotSummary[] summaries = new SaveSlotSummary[MaxSlots];
        for (int i = 0; i < MaxSlots; i++)
        {
            string slotPath = Path.Combine(Application.persistentDataPath, $"playerData_{i}.json");
            if (File.Exists(slotPath))
            {
                try
                {
                    string json = File.ReadAllText(slotPath);
                    PlayerData data = JsonUtility.FromJson<PlayerData>(json);
                    summaries[i] = new SaveSlotSummary
                    {
                        hasData = true,
                        day = data.currentDay,
                        time = data.currentTime,
                        gold = data.gold,
                        // 구버전 세이브는 slotName이 없어 null로 들어온다 (UI가 폴백)
                        name = SaveSlotName.Sanitize(data.slotName),
                        isTutorialCompleted = data.isTutorialCompleted
                    };
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[SaveManager] Failed to read save slot {i}: {e.Message}");
                    summaries[i] = new SaveSlotSummary { hasData = false };
                }
            }
            else
            {
                summaries[i] = new SaveSlotSummary { hasData = false };
            }
        }
        return summaries;
    }

    /// <summary>
    /// 특정 슬롯의 세이브 파일을 삭제합니다.
    /// </summary>
    public void DeleteSave(int slotIndex)
    {
        string slotPath = Path.Combine(Application.persistentDataPath, $"playerData_{slotIndex}.json");
        if (File.Exists(slotPath))
        {
            File.Delete(slotPath);
            Debug.Log($"[SaveManager] 슬롯 {slotIndex} 삭제 완료.");
        }

        // 현재 선택된 슬롯이 삭제된 슬롯이라면 메모리 데이터도 초기화
        if (CurrentSlotIndex == slotIndex)
        {
            playerData = null;
        }
    }

    /// <summary>
    /// 슬롯의 표시 이름만 바꿔 파일에 다시 쓴다. (메인 메뉴의 이름 변경 버튼)
    ///
    /// ⚠ 파일이 없거나 JSON 파싱에 실패하면 아무것도 쓰지 않는다.
    /// 손상된 세이브를 기본값 PlayerData로 덮어써 통째로 날리는 사고를 막는 가드이니 빼지 말 것.
    /// </summary>
    /// <returns>실제로 파일을 갱신했으면 true.</returns>
    public bool RenameSlot(int slotIndex, string newName)
    {
        if (slotIndex < 0 || slotIndex >= MaxSlots) return false;

        string slotPath = Path.Combine(Application.persistentDataPath, $"playerData_{slotIndex}.json");
        if (!File.Exists(slotPath)) return false;

        PlayerData data;
        try
        {
            data = JsonUtility.FromJson<PlayerData>(File.ReadAllText(slotPath));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 슬롯 {slotIndex} 이름 변경 실패(읽기): {e.Message}");
            return false;
        }

        if (data == null)
        {
            Debug.LogWarning($"[SaveManager] 슬롯 {slotIndex} 이름 변경 실패: 세이브를 해석할 수 없다.");
            return false;
        }

        data.slotName = SaveSlotName.Sanitize(newName);

        try
        {
            File.WriteAllText(slotPath, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 슬롯 {slotIndex} 이름 변경 실패(쓰기): {e.Message}");
            return false;
        }

        // 메모리에 올라와 있는 슬롯이면 같이 갱신한다.
        // 안 하면 다음 Save()가 playerData의 옛 이름으로 파일을 되돌린다.
        if (CurrentSlotIndex == slotIndex && playerData != null)
            playerData.slotName = data.slotName;

        Debug.Log($"[SaveManager] 슬롯 {slotIndex} 이름 변경: {data.slotName ?? "(없음)"}");
        return true;
    }

    /// <param name="slotName">
    /// 플레이어가 붙인 세이브 표시 이름. null·공백이면 이름 없이 시작하고 UI가 "슬롯 N"으로 표시한다.
    /// </param>
    public void NewGame(string slotName = null)
    {
        if (playerSO == null)
        {
            Debug.LogError("PlayerSO가 연결되지 않았습니다!");
            return;
        }

        RefreshReferences();

        PlayerData data = new PlayerData(playerSO);
        playerData = data;

        data.slotName = SaveSlotName.Sanitize(slotName);

        // 회차 식별자 발급 — 밸런스 CSV가 이 값으로 플레이스루를 가른다(balance-csv-design.md §4)
        data.runId = System.Guid.NewGuid().ToString("N");
        data.isFixture = false;
        SyncTelemetryRun();

        // 도깨비 가마솥 사용횟수 런타임 맵 초기화 (뉴게임)
        CauldronStateStore.Clear();
        DungeonStateStore.Clear();
        DetectedChunkStore.Instance.Clear();
        CollectionCodex.Clear(); // 뉴게임 — 도감 발견 기록 초기화
        ElevatorStopUnlockStore.Clear(); // 뉴게임 — 정류장 해금 기록 초기화

        // 하루 정산 장부 초기화 (뉴게임 — 시작 골드를 하루 시작값으로)
        DayEarningsLedger.ResetForNewDay(data.gold);

        if (stats != null)
        {
            stats.FromData(data);
            statsLoaded = stats;   // 이 인스턴스는 초기화를 마쳤다 — Save가 신뢰해도 된다
        }
        else
        {
            Debug.LogWarning("[SaveManager] NewGame: PlayerStat을 찾을 수 없습니다. 능력치 초기화가 지연됩니다.");
        }

        // 인벤토리 초기화
        if (itemInventory != null)
        {
            itemInventory.FromData(new ItemInventoryData(), ItemDatabase.Instance);
        }
        if (mineralInventory != null)
        {
            mineralInventory.FromData(new MineralInventoryData(), MineralDatabase.Instance);
        }
        if (equipmentInventory != null)
        {
            equipmentInventory.FromData(new EquipmentInventoryData(), EquipmentDatabase.Instance);
        }
        
        // 창고 초기화
        if (WarehouseManager.Instance != null)
        {
            WarehouseManager.Instance.FromData(new WarehouseData());
        }

        Save();
        Debug.Log("새 게임 시작");
    }

    /// <summary>
    /// 지하 → 지상 씬 전환 직전 호출.
    /// 현재 메모리 상의 인벤토리(광물/아이템/장비)를 WarehouseManager에 직접 병합하고,
    /// 인벤토리를 비운 뒤 세이브합니다.
    /// </summary>
    public void MergeInventoriesToWarehouse()
    {
        RefreshReferences();

        WarehouseManager warehouse = WarehouseManager.Instance;
        if (warehouse == null)
        {
            Debug.LogWarning("[SaveManager] MergeInventoriesToWarehouse: WarehouseManager를 찾을 수 없습니다. 병합을 건너뜁니다.");
            return;
        }

        // 긴급 탈출은 '정상 종료'가 아니므로 탐험 추적을 중단·리셋한다.
        // (지상에서 시간이 계속 흐르는 것을 막고, 정산 팝업 대신 EmergencyEscapeOverlayUI만 뜨게 한다)
        // mineralInventory 유무와 무관하게 처리해야 하므로 아래 페널티 블록보다 앞에 둔다.
        if (LoadingData.IsEmergencyEscapePending)
        {
            SettlementManager.Instance?.AbortTracking();
        }

        // 긴급 탈출 페널티: 병합 전에 가장 비싼 1~2개만 남기고 전부 잃는다.
        // 규칙과 집계는 사망 경로(GameOverHandler)와 공유한다 — CarryLossPenalty 참고.
        if (LoadingData.IsEmergencyEscapePending && mineralInventory != null)
        {
            CarryLossPenalty.Apply(mineralInventory, CarryLossReason.EmergencyEscape);
            LoadingData.IsEmergencyEscapePending = false;
        }

        // [수정] 장비는 창고로 자동 병합하지 않고 장착 상태를 유지합니다. 아이템/광물만 창고로 보냅니다.
        warehouse.DepositAllFromInventory(itemInventory, mineralInventory, null);

        // 병합 후 즉시 저장 (창고 + 빈 인벤토리 상태를 파일에 확정)
        Save();
    }

    /// <summary>
    /// 지상 → 지하 씬 전환 직전 호출.
    /// 지상 데이터를 파일에 확정 저장합니다.
    /// 이 파일이 강제종료/사망 시 복구 기준이 됩니다.
    /// </summary>
    public void PrepareUndergroundEntry()
    {
        RefreshReferences();

        // 지상에서 지하로 진입 시 맵이 새로 생성되므로,
        // 이전 탐험의 좌표 기반 인스턴스 상태(던전, 가마솥)를 초기화합니다.
        DungeonStateStore.Clear();
        CauldronStateStore.Clear();

        // 지도(미니맵·전체지도) 세션 캐시도 매 잠수마다 초기화한다. 특히 엘리베이터 마커는
        // 이번 잠수에서 직접 가본/이용한 것만 다시 쌓여야 하므로, 이전 잠수 기록이 남으면 안 된다.
        // 정상 귀환(ExploreExitController)에서도 비우지만, 사망·긴급탈출 등 그 경로를 안 타는
        // 이탈이 있으므로 '내려가는' 이 한 곳에서도 확실히 비운다.
        // (던전 복귀는 PrepareUndergroundEntry를 타지 않으므로 잠수 중 발견한 마커는 유지된다.)
        MapMarkerRegistry.Reset();   // 마커 + MapRockCache.Clear() 포함
        MapTerrainCache.Clear();

        // 이번 잠수의 엘리베이터 배치를 새로 뽑는다 — 땅에 들어갈 때마다 층별 X가 달라진다.
        // 지형은 enableDiskSave=false라 매 잠수마다 새로 생성되므로 지난 잠수의 승강로 자국은 안 남는다.
        // 난수는 여기 한 곳에서만 돌린다. 이후 잠수 내내 ElevatorStopLayout은 이 값을 읽기만 하므로
        // 청크가 언로드/재로드돼도 엘리베이터가 같은 자리에 다시 선다.
        // (아래 Save()가 파일에 굳히고, 지하 씬의 Load()가 되살린다.)
        if (playerData != null)
        {
            playerData.diveElevatorSeed = UnityEngine.Random.Range(1, int.MaxValue);
            ElevatorStopLayout.Seed = playerData.diveElevatorSeed;
            Debug.Log($"[SaveManager] 이번 잠수 엘리베이터 시드: {playerData.diveElevatorSeed}");
        }

        // 잠수는 항상 스태미나 만땅으로 시작한다.
        // (지상 복귀 경로가 여러 개라 — 정상 귀환/사망/긴급탈출 — 내려가는 이 한 곳에서 확정한다.
        //  Save()보다 먼저 채워야 파일의 currentStamina도 최대치로 굳는다.
        //  던전 복귀는 PrepareUndergroundEntry를 타지 않으므로 회복 악용이 생기지 않는다.)
        if (stats != null) stats.CurrentStamina = stats.MaxStamina;

        Save();
        Debug.Log("[SaveManager] PrepareUndergroundEntry: 지상 데이터 저장 완료. 지하 진입 준비.");
    }

    /// <summary>
    /// 던전 씬 진입 전 호출.
    /// 현재 플레이어 위치를 기록하고 파일에 저장합니다.
    /// </summary>
    public void PrepareDungeonEntry(Vector3 currentPosition)
    {
        RefreshReferences();
        if (playerData != null)
        {
            playerData.preDungeonPosition = currentPosition;
            playerData.isReturningFromDungeon = true;
        }
        Save();
        Debug.Log($"[SaveManager] PrepareDungeonEntry: 던전 진입 준비 완료. 복귀 위치: {currentPosition}");
    }

    /// <summary>
    /// 지하 씬 Load() 직후 호출.
    /// 광물 인벤토리만 메모리에서 비웁니다 (파일에 저장하지 않음) — 채굴은 항상 빈 가방으로 시작.
    /// 강제종료 시 파일의 지상 데이터가 보존되는 것은 '지하에서 저장하지 않는' 규칙이 담당하므로,
    /// 여기서 메모리를 더 지울 이유는 없습니다.
    ///
    /// 아이템·장비는 지상에서 챙긴 그대로 들고 내려갑니다.
    /// (예전에는 아이템까지 비워서, 창고에서 꺼낸 소모품이 지하 진입 순간 사라졌습니다.
    ///  Save()의 "지상에서 꺼낸 아이템 보존" 주석과도 어긋나던 동작입니다.)
    /// </summary>
    public void ClearInventoriesForUnderground()
    {
        RefreshReferences();

        if (mineralInventory != null)
        {
            mineralInventory.FromData(new MineralInventoryData(), MineralDatabase.Instance);
            Debug.Log("[SaveManager] ClearInventoriesForUnderground: 광물 인벤토리 초기화 완료");
        }

        Debug.Log("[SaveManager] ClearInventoriesForUnderground: 아이템·장비는 유지. 파일에 저장하지 않음 (강제종료 시 지상 데이터 보존)");
    }

    public void Save()
    {
        RefreshReferences();

        if (playerData == null)
        {
            // 아직 로드된 데이터가 없다면 파일에서 읽기 시도
            if (File.Exists(path))
            {
                string existingJson = File.ReadAllText(path);
                playerData = JsonUtility.FromJson<PlayerData>(existingJson);
            }
            else
            {
                playerData = new PlayerData(playerSO);
            }
        }
        
        // stats가 있다면 최신 데이터를 playerData에 반영
        if (stats != null)
        {
            // [안전 장치] 이번 씬의 PlayerStat이 아직 FromData(Load/NewGame)를 거치지 않았다면
            // 프리팹 기본값이 들어 있는 상태다. 그대로 ToData를 부르면 메모리·파일의
            // 올바른 playerData(스탯·골드)를 기본값으로 파괴하므로 건너뛴다.
            if (stats == statsLoaded && stats.MaxStamina > 0)
            {
                stats.ToData(playerData);
            }
            else
            {
                Debug.LogWarning($"[SaveManager] Save: PlayerStat이 아직 로드되지 않았거나(loaded={stats == statsLoaded}) 비정상(MaxStamina={stats.MaxStamina})입니다. 스탯 저장을 건너뛰고 기존 데이터를 유지합니다.");
            }
        }
        
        PlayerData data = playerData;
        string currentScene = SceneManager.GetActiveScene().name;
        bool isUpground = (currentScene == "UpgroundScene" || currentScene == "DemoUpground");
        bool isUnderground = (currentScene == "DemoUnderground");

        if (itemInventory != null)
        {
            // 인벤토리 데이터를 항상 저장합니다. (지상에서 꺼낸 아이템 보존)
            data.itemInventory = itemInventory.ToData();
        }
        else if (data.itemInventory == null)
        {
            data.itemInventory = new ItemInventoryData();
        }

        if (mineralInventory != null)
        {
            data.mineralInventory = mineralInventory.ToData();
        }
        else if (data.mineralInventory == null)
        {
            data.mineralInventory = new MineralInventoryData();
        }

        // [장비 보호] 지하씬에서는 장비 인벤토리를 저장하지 않습니다.
        // 지하씬에서는 장비 슬롯을 조작할 수 없으므로 데이터 변경이 없습니다.
        // 지하씬의 (비어있는) equipmentInventory로 지상씬 데이터를 덮어쓰면 장비가 유실됩니다.
        if (!isUnderground && equipmentInventory != null)
        {
            data.equipmentInventory = equipmentInventory.ToData();
        }
        else if (isUnderground)
        {
            Debug.Log("[SaveManager] Save: 지하씬이므로 equipmentInventory 저장 건너뜀 (기존 데이터 보존)");
        }
        else if (data.equipmentInventory == null)
        {
            data.equipmentInventory = new EquipmentInventoryData();
        }

        // 장비 강화 레벨(+N) 저장 — 전역 static 저장소라 지하/지상 무관하게 현재 상태가 정확하다.
        data.equipmentUpgrade = EquipmentUpgradeStore.CaptureSaveData();

        // 도감 발견 기록 저장 — 전역 static 저장소(지하/지상 무관하게 현재 상태가 정확).
        data.collectionCodex = CollectionCodex.CaptureSaveData();

        // 엘리베이터 정류장 해금 기록 저장 (같은 성격의 전역 static 저장소)
        data.elevatorStops = ElevatorStopUnlockStore.CaptureSaveData();


        // 창고 데이터 저장
        if (WarehouseManager.Instance != null)
        {
            // 현재 씬에 창고 매니저가 있다면 최신 상태를 가져옴
            data.warehouseData = WarehouseManager.Instance.ToData();
        }
        // 만약 WarehouseManager가 없다면 (예: 지하 씬), data.warehouseData에 있는 기존 보존 데이터를 유지함
        else if (data.warehouseData == null)
        {
            data.warehouseData = new WarehouseData();
        }
        
        // 퀘스트 데이터 저장
        if (QuestManager.Instance != null)
        {
            data.questProgress = QuestProgressData.FromDictionary(QuestManager.Instance.GetSaveData());
            // 서브퀘스트 슬롯 정보도 함께 저장
            data.questProgress.activeSubQuestSlots = QuestManager.Instance.GetActiveSubQuestSlots();
            // 플래그 정보 저장
            data.questProgress.unlockedFlags = QuestManager.Instance.GetUnlockedFlags();
        }
        else if (data.questProgress == null)
        {
            data.questProgress = new QuestProgressData();
        }

        // 날짜 데이터 저장
        if (DayCycleManager.Instance != null)
        {
            data.currentDay = DayCycleManager.Instance.CurrentDay;
            data.currentTime = DayCycleManager.Instance.CurrentTime;
        }

        // 주식 시뮬레이션 데이터 저장 (엔진이 초기화된 경우에만 갱신, 아니면 기존 데이터 보존)
        if (StockGameManager.Instance != null && StockGameManager.Instance.IsInitialized)
        {
            data.stockSave = StockGameManager.Instance.CaptureSaveData();
        }
        else if (data.stockSave == null)
        {
            data.stockSave = new Stock.Data.StockSaveData();
        }

        // 코인 미니게임 데이터 저장 (마켓 씬이 로드된 경우에만 갱신, 아니면 기존 데이터 보존)
        if (CoinGameManager.Instance != null)
        {
            data.coinSave = CoinGameManager.Instance.CaptureSaveData();
        }
        else if (data.coinSave == null)
        {
            data.coinSave = new Coin.Data.CoinSaveData();
        }

        // 탐지 나침반 발견 데이터 저장
        data.detectedChunks = DetectedChunkStore.Instance.Capture();

        // 유물 시스템 데이터 저장 (플레이어 씬에 매니저가 있을 때만 갱신, 아니면 기존 데이터 보존)
        var relicMgr = FindFirstObjectByType<Relic.RelicManager>();
        if (relicMgr != null)
        {
            data.relicSave = relicMgr.CaptureSaveData();
        }
        else if (data.relicSave == null)
        {
            data.relicSave = new Relic.Data.RelicSaveData();
        }

        // 하루 정산 장부 저장 (침대 정산 연출용 카테고리별 골드 증감)
        data.dayEarnings = DayEarningsLedger.Capture();

        // 도깨비 가마솥 사용횟수 저장 (좌표→남은횟수 런타임 맵을 캡처)
        data.cauldronSave = CauldronStateStore.Capture();

        // 던전 인스턴스 상태 저장 (문 좌표→부서진 rock·수집 보상)
        data.dungeonSave = DungeonStateStore.Capture();

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// 세이브 파일의 영구 발견 기록(collectionCodex + elevatorStops)만 현재 static 저장소로 갱신하고,
    /// 나머지 필드(광물·위치 등)는 파일에 있던 그대로 둔다.
    ///
    /// 왜 전체 <see cref="Save"/>가 아니라 이건가: 지하 사망은 '광물은 잃되 진행은 되돌리는'
    /// 페널티라 저장을 통째로 건너뛴다(<c>GameOverHandler</c>). 그 규칙은 지키면서도
    /// '무엇을 봤는지'라는 영구 기록만은 남겨야 한다. 그래서 첫 발견 순간마다
    /// (<see cref="CollectionCodex.OnDiscovered"/>) 도감 칸 하나만 파일에 덧쓴다.
    /// 항목당 최초 1회만 발생하므로 파일 쓰기 비용도 작다.
    ///
    /// 파일이 있으면 읽어서 도감 필드만 바꿔 쓰므로 잠수 전 지상 데이터(광물 등)가 보존된다
    /// → 사망 시 광물은 여전히 되돌아가고, 발견 기록만 남는다.
    /// </summary>
    public void PersistCodexOnly()
    {
        try
        {
            PlayerData data;
            if (File.Exists(path))
                data = JsonUtility.FromJson<PlayerData>(File.ReadAllText(path));
            else
                data = playerData ?? new PlayerData(playerSO);

            data.collectionCodex = CollectionCodex.CaptureSaveData();
            data.elevatorStops   = ElevatorStopUnlockStore.CaptureSaveData();

            // 메모리 미러도 맞춰 둔다 — 다음 전체 Save()가 헌 도감으로 되돌리지 않도록.
            if (playerData != null)
            {
                playerData.collectionCodex = data.collectionCodex;
                playerData.elevatorStops   = data.elevatorStops;
            }

            File.WriteAllText(path, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] PersistCodexOnly 실패: {e.Message}");
        }
    }

    /// <summary>
    /// 창고 내용만 파일에 덧쓴다. 나머지 필드는 파일에 있던 값(=잠수 전 지상 데이터)을 그대로 둔다.
    ///
    /// 왜 전체 <see cref="Save"/>가 아닌가: 이걸 부르는 곳은 <b>지하에서 죽은 직후</b>다.
    /// 지하 진행은 저장하지 않는 것이 규칙이라(강제 종료와 동일 처리) 전체 Save를 부르면
    /// 지하에서의 스탯·위치·시간이 통째로 파일에 굳어 버린다. 사망 페널티를 통과해 살아남은
    /// 광물 1~2개만 창고에 남기면 되므로, 도감(<see cref="PersistCodexOnly"/>)과 같은 방식으로
    /// 해당 필드만 덧쓴다.
    /// </summary>
    /// <param name="fallbackMinerals">
    /// <see cref="WarehouseManager"/>가 씬에 없을 때(지하 씬이 그렇다) 파일의 창고 데이터에
    /// 직접 합칠 광물. 매니저가 있으면 무시된다(그쪽이 이미 병합을 끝냈다).
    /// </param>
    public void PersistWarehouseOnly(MineralInventory fallbackMinerals = null)
    {
        try
        {
            PlayerData data;
            if (File.Exists(path))
                data = JsonUtility.FromJson<PlayerData>(File.ReadAllText(path));
            else
                data = playerData ?? new PlayerData(playerSO);

            if (WarehouseManager.Instance != null)
            {
                data.warehouseData = WarehouseManager.Instance.ToData();
            }
            else if (fallbackMinerals != null)
            {
                // 지하 씬엔 WarehouseManager가 없다 — 파일의 창고 광물 목록에 직접 더한다.
                if (data.warehouseData == null) data.warehouseData = new WarehouseData();
                if (data.warehouseData.mineralInventory == null)
                    data.warehouseData.mineralInventory = new MineralInventoryData();

                var slots = data.warehouseData.mineralInventory.slots;
                foreach (var slot in fallbackMinerals.ReadonlyItems)
                {
                    if (!(slot.item is MineralSO mineral) || slot.quantity <= 0) continue;

                    string id = mineral.mineralID.ToString();
                    // 스택 상한(MineralSO.MineralMaxStackSize)을 지키며 기존 슬롯부터 채운다 —
                    // 넘겨 담으면 창고 UI가 상한을 넘은 슬롯을 그리게 된다.
                    int remain = slot.quantity;
                    foreach (var ws in slots)
                    {
                        if (remain <= 0) break;
                        if (ws == null || ws.itemId != id) continue;
                        int room = MineralSO.MineralMaxStackSize - ws.quantity;
                        if (room <= 0) continue;
                        int put = Mathf.Min(room, remain);
                        ws.quantity += put;
                        remain -= put;
                    }
                    while (remain > 0)
                    {
                        int put = Mathf.Min(MineralSO.MineralMaxStackSize, remain);
                        slots.Add(new MineralSlotData { itemId = id, quantity = put });
                        remain -= put;
                    }
                }
            }
            else return;

            // 메모리 미러도 맞춰 둔다 — 다음 전체 Save()가 헌 창고로 되돌리지 않도록.
            if (playerData != null)
                playerData.warehouseData = data.warehouseData;

            File.WriteAllText(path, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] PersistWarehouseOnly 실패: {e.Message}");
        }
    }

    /// <summary>
    /// 1회성 스폰 오버라이드 플래그(<c>spawnAtElevator</c>·<c>isReturningFromDungeon</c>)만
    /// 파일에 덧쓴다. 나머지 필드는 파일에 있던 값을 그대로 둔다.
    ///
    /// 왜 전체 <see cref="Save"/>가 아닌가: 이 플래그를 소진하는 <c>PlayerSpawner</c>는
    /// <b>지하 씬 활성화 직후</b>(= LoadingSceneController Step 4.5)에 돈다. 그 시점의
    /// PlayerStat은 아직 <see cref="Load"/>를 거치지 않아 프리팹 기본값을 들고 있어서,
    /// 전체 Save를 부르면 <c>ToData</c>가 세이브의 스탯·골드를 기본값으로 덮어썼다
    /// (엘리베이터로 내려가면 최대 스태미나가 500으로 돌아가던 버그).
    ///
    /// 그런데도 파일에 써야 하는 이유는, 곧이어 Step 5의 <see cref="Load"/>가 파일을 다시
    /// 읽어 <c>playerData</c>를 통째로 교체하기 때문이다 — 메모리만 false로 바꾸면
    /// 파일의 true가 되살아나 오버라이드가 두 번 소비된다.
    /// </summary>
    public void PersistSpawnFlagsOnly()
    {
        if (playerData == null) return;

        try
        {
            PlayerData data;
            if (File.Exists(path))
                data = JsonUtility.FromJson<PlayerData>(File.ReadAllText(path));
            else
                data = playerData;

            // 메모리(= PlayerSpawner가 방금 소진한 상태)를 파일로 미러링한다.
            data.spawnAtElevator       = playerData.spawnAtElevator;
            data.isReturningFromDungeon = playerData.isReturningFromDungeon;

            File.WriteAllText(path, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] PersistSpawnFlagsOnly 실패: {e.Message}");
        }
    }

    public void Load()
    {
        // 1. 컴포넌트 새로 찾기 (null일 수 있음)
        RefreshReferences();

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[SaveManager] 저장 파일 없음. 새 게임으로 시작.");
            NewGame();
            return;
        }

        string json = File.ReadAllText(path);
        PlayerData data = JsonUtility.FromJson<PlayerData>(json);

        // [데이터 보정] 기존 파일이 0으로 오염되었거나 새 필드가 추가된 경우를 대비해 기본값 적용
        if (data.maxStamina <= 0) data.maxStamina = 500.0f;
        if (data.currentStamina <= 0) data.currentStamina = data.maxStamina;
        if (data.staminaCostPerSecond <= 0) data.staminaCostPerSecond = 10.0f;
        if (data.moveSpeed <= 0) data.moveSpeed = 2.0f;
        if (data.jumpForce <= 0) data.jumpForce = 5.0f;
        if (data.wallClimbingSpeed <= 0) data.wallClimbingSpeed = 2.0f;
        if (data.encumberedSpeedMultiplier <= 0) data.encumberedSpeedMultiplier = 0.5f;

        // [구버전 승격] 튜토리얼 완료를 기록하는 코드가 없던 시절의 세이브는
        // isTutorialCompleted가 영영 false라 이어하기마다 튜토리얼 씬으로 끌려갔다.
        // 그런 세이브(= 플래그를 관리한 적 없는 세이브)는 튜토리얼을 이미 지난 것으로 본다.
        if (!data.tutorialFlagInitialized)
        {
            data.isTutorialCompleted = true;
            data.tutorialFlagInitialized = true;
        }

        if (data.miningLevel < 0) data.miningLevel = 0;
        if (data.miningRange <= 0) data.miningRange = 0.5f;
        if (data.miningCooldown <= 0) data.miningCooldown = 0.05f;

        playerData = data;

        // 이번 잠수의 엘리베이터 배치를 되살린다. 지하 씬은 이 Load로만 시드를 받으므로,
        // 청크 생성(코루틴이라 최소 1프레임 뒤)보다 먼저인 여기서 밀어넣어야 한다.
        ElevatorStopLayout.Seed = data.diveElevatorSeed;

        string currentScene = SceneManager.GetActiveScene().name;
        bool isUpground = (currentScene == "UpgroundScene" || currentScene == "DemoUpground");
        bool isUnderground = (currentScene == "DemoUnderground");

        // (참고) 지하에서 지상으로 올라올 때의 데이터 통합은 
        // 씬 전환을 트리거하는 곳(포탈 등)에서 수행하는 것이 더 안전합니다.


        // 3. 창고 데이터 즉시 적용 (인벤토리보다 먼저 수행하여 안정성 확보)
        if (WarehouseManager.Instance != null)
        {
            WarehouseManager.Instance.FromData(data.warehouseData ?? new WarehouseData());
        }
        else
        {
            Debug.LogWarning("[SaveManager] WarehouseManager 인스턴스를 찾을 수 없습니다! (지하 씬일 수 있음)");
        }

        // 4. 플레이어 및 인벤토리 데이터 복원 (필드 할당된 객체가 있을 때만)
        if (stats != null)
        {
            stats.FromData(data);
            statsLoaded = stats;   // 이 인스턴스는 로드를 마쳤다 — Save가 신뢰해도 된다
        }
        
        if (itemInventory != null)
            itemInventory.FromData(data.itemInventory ?? new ItemInventoryData(), ItemDatabase.Instance);

        if (mineralInventory != null)
            mineralInventory.FromData(data.mineralInventory ?? new MineralInventoryData(), MineralDatabase.Instance);

        if (equipmentInventory != null)
        {
            equipmentInventory.FromData(data.equipmentInventory ?? new EquipmentInventoryData(), EquipmentDatabase.Instance);
        }
        else if (!isUnderground)
        {
            Debug.LogWarning("[SaveManager] Load: equipmentInventory 참조가 null입니다! 장비 복원 실패.");
        }

        // 장비 강화 레벨(+N) 복원 (전역 static 저장소)
        EquipmentUpgradeStore.ApplySaveData(data.equipmentUpgrade);

        // 도감 발견 기록 복원 (전역 static 저장소)
        CollectionCodex.ApplySaveData(data.collectionCodex);

        // 엘리베이터 정류장 해금 기록 복원 (전역 static 저장소)
        ElevatorStopUnlockStore.ApplySaveData(data.elevatorStops);

        // 퀘스트 데이터 복원
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.LoadSaveData(data.questProgress?.ToDictionary() ?? new Dictionary<int, QuestStatus>());
            // 서브퀘스트 슬롯 정보도 함께 복원
            QuestManager.Instance.LoadSubQuestSlots(data.questProgress?.activeSubQuestSlots ?? new List<int>());
            // 플래그 정보 복원
            QuestManager.Instance.LoadUnlockedFlags(data.questProgress?.unlockedFlags ?? new List<string>());
        }
        else if (!isUnderground)
        {
            Debug.LogWarning("[SaveManager] QuestManager 인스턴스를 찾을 수 없습니다!");
        }

        // 날짜 데이터 복원
        if (DayCycleManager.Instance != null)
        {
            DayCycleManager.Instance.LoadData(data.currentDay, data.currentTime);
        }

        // 주식 시뮬레이션 데이터 복원 (엔진 초기화 전이면 내부에서 보류 후 초기화 시 자동 적용)
        if (StockGameManager.Instance != null && data.stockSave != null && data.stockSave.hasData)
        {
            StockGameManager.Instance.ApplySaveData(data.stockSave);
        }

        // 코인 미니게임 데이터 복원 (마켓 씬 미로드면 CoinGameManager.Start가 playerData에서 직접 읽음)
        if (CoinGameManager.Instance != null && data.coinSave != null && data.coinSave.hasData)
        {
            CoinGameManager.Instance.ApplySaveData(data.coinSave);
        }

        // 유물 시스템 데이터 복원 (플레이어 씬에 매니저가 있을 때만)
        var relicMgrLoad = FindFirstObjectByType<Relic.RelicManager>();
        if (relicMgrLoad != null && data.relicSave != null && data.relicSave.hasData)
        {
            relicMgrLoad.ApplySaveData(data.relicSave);
        }

        // 탐지 나침반 발견 데이터 복원
        if (data.detectedChunks != null && data.detectedChunks.hasData)
            DetectedChunkStore.Instance.Apply(data.detectedChunks);
        else
            DetectedChunkStore.Instance.Clear(); // 신규 게임 = 발견 없음

        // 하루 정산 장부 복원 (구버전 세이브면 현재 골드를 하루 시작값으로 초기화)
        DayEarningsLedger.Apply(data.dayEarnings, data.gold);

        // 도깨비 가마솥 사용횟수 복원 (가마솥 스폰 시 좌표로 GetRemainingUses 조회)
        CauldronStateStore.Apply(data.cauldronSave);

        // 던전 인스턴스 상태 복원 (던전 씬 진입 시 문 좌표로 조회)
        DungeonStateStore.Apply(data.dungeonSave);

        // 회차 식별자 백필 — 구버전 세이브는 runId가 비어 있다. 그 자리에서 발급해 흡수한다.
        // 텔레메트리 사유의 디스크 쓰기라 실패해도 Load()를 끊으면 안 된다 — try/catch로 감싼다.
        // 메모리의 runId는 Save()보다 먼저 세워서, 저장이 실패해도 이번 세션 기록은 정상으로 나간다.
        if (string.IsNullOrEmpty(playerData.runId))
        {
            try
            {
                playerData.runId = System.Guid.NewGuid().ToString("N");
                Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] 회차 식별자 백필 저장 실패: {e.Message}");
            }
        }
        SyncTelemetryRun();
    }

    /// <summary>
    /// 현재 세이브의 회차 정보를 텔레메트리 컨텍스트에 반영한다.
    /// 이후 기록되는 모든 이벤트가 run_id·is_fixture를 달고 나간다.
    /// Telemetry가 아직 Init 전이면 Context가 null이므로 조용히 넘어간다.
    /// </summary>
    public void SyncTelemetryRun()
    {
        if (Telemetry.Context == null || playerData == null) return;
        Telemetry.Context.RunId = playerData.runId ?? string.Empty;
        Telemetry.Context.IsFixture = playerData.isFixture;
        Telemetry.Context.Day = playerData.currentDay;
    }

    /// <summary>
    /// 현재 회차를 '디버그 조작됨'으로 마킹한다. 한 번 서면 되돌리지 않는다 —
    /// 조작된 회차는 이후 일차의 곡선도 신뢰할 수 없다(balance-csv-design.md §4).
    /// 실패는 삼킨다. 마킹 실패로 QA 흐름이 끊기면 안 된다(Save()의 디스크 IO 포함).
    /// </summary>
    public void MarkRunAsFixture()
    {
        try
        {
            if (playerData == null || playerData.isFixture) return;
            playerData.isFixture = true;
            Save();
            SyncTelemetryRun();
            Debug.Log("[SaveManager] 이 회차를 디버그 조작됨(isFixture)으로 마킹했다.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 픽스처 마킹 실패: {e.Message}");
        }
    }

    /// <summary>
    /// 지정 슬롯의 세이브 파일을 열어 isFixture만 true로 바꿔 다시 쓴다.
    /// fx load처럼 '파일을 갈아끼운 뒤 씬을 재시작'하는 경로용 — 메모리에는 세울 수 없다.
    /// 실패는 삼킨다. 마킹 실패로 QA 흐름이 끊기면 안 된다.
    /// </summary>
    public static void MarkSlotFileAsFixture(int slot)
    {
        try
        {
            string slotPath = Path.Combine(Application.persistentDataPath, $"playerData_{slot}.json");
            if (!File.Exists(slotPath)) return;

            PlayerData data = JsonUtility.FromJson<PlayerData>(File.ReadAllText(slotPath));
            if (data == null || data.isFixture) return;

            data.isFixture = true;
            File.WriteAllText(slotPath, JsonUtility.ToJson(data, true));
            Debug.Log($"[SaveManager] 슬롯 {slot} 세이브를 디버그 조작됨(isFixture)으로 마킹했다.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 픽스처 마킹 실패: {e.Message}");
        }
    }
}