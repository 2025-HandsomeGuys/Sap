using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 퀘스트 진행 상황을 관리하는 매니저
/// </summary>
public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance { get; private set; }
    
    [Header("서브 퀘스트 목록")]
    public List<QuestSO> subQuests = new List<QuestSO>();  // 채광레벨 기반 서브 퀘스트
    
    private Dictionary<int, QuestSO> _allQuestsDict;
    
    // 퀘스트 상태를 저장 (questID -> QuestStatus)
    private Dictionary<int, QuestStatus> questStatuses = new Dictionary<int, QuestStatus>();
    
    // 달성한 퀘스트 플래그 목록 (씬 방문 기록 등)
    private HashSet<string> _unlockedFlags = new HashSet<string>();
    
    // 서브퀘스트 슬롯 (최대 2개)
    public const int MAX_SUB_QUEST_SLOTS = 2;
    private List<int> activeSubQuestSlots = new List<int>(2);
    
    // 캐싱된 채광레벨 (매 프레임 계산 방지)
    private int _cachedMiningLevel = 1;
    private float _lastMiningLevelCheck = -1f;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            InitializeQuests();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // 씬 전환 시 해당 씬 이름을 플래그로 자동 달성 처리 (예: "Scene_DemoMain")
        SetQuestFlag("Scene_" + scene.name, true);
    }
    
    /// <summary>
    /// 특정 퀘스트 이벤트 플래그 달성 처리
    /// </summary>
    public void SetQuestFlag(string flagName, bool value)
    {
        if (string.IsNullOrEmpty(flagName)) return;
        
        if (value)
            _unlockedFlags.Add(flagName);
        else
            _unlockedFlags.Remove(flagName);
            
        Debug.Log($"[QuestManager] 퀘스트 플래그 설정됨: {flagName} = {value}");
    }
    
    /// <summary>
    /// 플래그 달성 여부 확인
    /// </summary>
    public bool HasQuestFlag(string flagName)
    {
        if (string.IsNullOrEmpty(flagName)) return false;
        return _unlockedFlags.Contains(flagName);
    }
    
    /// <summary>
    /// 현재 플레이어의 채광레벨을 반환합니다.
    /// </summary>
    public int GetCurrentMiningLevel()
    {
        // 1초마다 갱신 (성능 최적화)
        if (Time.time - _lastMiningLevelCheck > 1f)
        {
            _lastMiningLevelCheck = Time.time;
            
            if (UpgradeManager.Instance != null)
            {
                // 기본 채광레벨 1 + 업그레이드로 얻은 추가 레벨
                _cachedMiningLevel = Mathf.RoundToInt(
                    UpgradeManager.Instance.GetStatValue(UpgradeEffectType.MiningLevel, 0f)
                );
                // 최소 레벨 1 보장
                if (_cachedMiningLevel < 1) _cachedMiningLevel = 1;
            }
        }
        return _cachedMiningLevel;
    }
    
    /// <summary>
    /// 퀘스트 시스템 초기화 (리스트에는 있지만 딕셔너리에 없는 퀘스트들을 초기 상태로 설정)
    /// </summary>
    public void InitializeQuests()
    {
        // 튜토리얼 씬에서는 시작 시 어떤 퀘스트도 가지지 않도록 초기화를 보류합니다.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == TutorialProgress.SceneName)
        {
            return;
        }

        int currentMiningLevel = GetCurrentMiningLevel();

        // 메인 퀘스트 자동 초기화 로직 제거됨 (수동 관리)
        
        // 서브 퀘스트 초기화
        foreach (var quest in subQuests)
        {
            if (quest == null) continue;
            
            if (!questStatuses.ContainsKey(quest.questID))
            {
                // 요구 레벨이 -1 이하이면 무조건 해금 (항상 보이는 퀘스트)
                // 또는 직전 레벨 ~ 현재 레벨 범위 내에 있으면 해금
                bool isAlwaysAvailable = quest.requiredMiningLevel <= -1;
                bool isLevelMatched = (quest.requiredMiningLevel <= currentMiningLevel && 
                                     quest.requiredMiningLevel >= currentMiningLevel - 1);

                if (isAlwaysAvailable || isLevelMatched)
                {
                    questStatuses[quest.questID] = QuestStatus.Available;
                }
                else
                {
                    questStatuses[quest.questID] = QuestStatus.Locked;
                }
            }
        }
    }
    
    /// <summary>
    /// 특정 퀘스트의 상태 반환
    /// </summary>
    public QuestStatus GetQuestStatus(int questID)
    {
        if (questStatuses.ContainsKey(questID))
        {
            return questStatuses[questID];
        }
        
        // 딕셔너리에 없다는 것은 아직 초기화되지 않은 새 퀘스트일 수 있음
        // 이 경우 다시 한번 초기화를 시도
        InitializeQuests();
        
        if (questStatuses.ContainsKey(questID))
        {
            return questStatuses[questID];
        }

        return QuestStatus.Locked;
    }
    
    private void EnsureAllQuestsLoaded()
    {
        if (_allQuestsDict != null) return;
        _allQuestsDict = new Dictionary<int, QuestSO>();
        var quests = Resources.LoadAll<QuestSO>("");
        foreach (var q in quests)
        {
            if (q != null) _allQuestsDict[q.questID] = q;
        }
    }

    /// <summary>
    /// 퀘스트 ID로 QuestSO 찾기 (Resources 전체 로드 기반)
    /// </summary>
    public QuestSO GetQuestByID(int questID)
    {
        EnsureAllQuestsLoaded();
        if (_allQuestsDict.TryGetValue(questID, out var quest))
            return quest;
        return null;
    }
    
    /// <summary>
    /// 퀘스트 수락 가능한지 확인 (요구사항 충족 여부)
    /// </summary>
    public bool CanAcceptQuest(QuestSO quest)
    {
        if (quest == null) return false;
        
        var status = GetQuestStatus(quest.questID);
        // 상태가 Available(수락 가능)인지만 확인 (수락 시점에 재료가 없어도 됨)
        return status == QuestStatus.Available;
    }
    
    /// <summary>
    /// 퀘스트 요구사항 충족 여부 확인
    /// </summary>
    public bool CheckRequirements(QuestSO quest)
    {
        if (quest == null || quest.requirements == null || quest.requirements.Count == 0)
            return true;
        
        foreach (var req in quest.requirements)
        {
            if (req.type == RequirementType.Mineral)
            {
                if (req.requiredMineral == null) continue;
                
                // 창고에서 해당 광물 수량 확인
                int availableAmount = WarehouseManager.Instance != null ? WarehouseManager.Instance.GetMineralCount(req.requiredMineral) : 0;
                if (availableAmount < req.requiredAmount) return false;
            }
            else if (req.type == RequirementType.SceneVisit)
            {
                // SceneVisit은 "Scene_" 접두어를 붙여서 확인
                if (!HasQuestFlag("Scene_" + req.targetString)) return false;
            }
            else if (req.type == RequirementType.CustomFlag)
            {
                if (!HasQuestFlag(req.targetString)) return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// 퀘스트 수락
    /// </summary>
    public void AcceptQuest(QuestSO quest)
    {
        if (quest == null) return;
        
        if (!CanAcceptQuest(quest))
        {
            Debug.LogWarning($"[QuestManager] 퀘스트 '{quest.QuestName}' 수락 불가 - 수락 가능(Available) 상태가 아닙니다.");
            return;
        }
        
        // 서브퀘스트인 경우 슬롯에 추가
        if (quest.questType == QuestType.Sub)
        {
            if (!CanAcceptSubQuest())
            {
                Debug.LogWarning($"[QuestManager] 서브퀘스트 슬롯이 가득 찼습니다. 최대 {MAX_SUB_QUEST_SLOTS}개");
                return;
            }
            activeSubQuestSlots.Add(quest.questID);
        }
        
        // 퀘스트 수락 처리
        questStatuses[quest.questID] = QuestStatus.Accepted;
        Debug.Log($"[QuestManager] 퀘스트 수락: {quest.QuestName}");
    }

    /// <summary>
    /// 강제 퀘스트 수락 (상태 조건 무시 - 튜토리얼 자동 진행 등에서 사용)
    /// </summary>
    public void ForceAcceptQuest(QuestSO quest)
    {
        if (quest == null) return;
        
        // 서브퀘스트인 경우 슬롯에 추가
        if (quest.questType == QuestType.Sub)
        {
            if (!CanAcceptSubQuest())
            {
                Debug.LogWarning($"[QuestManager] 서브퀘스트 슬롯이 가득 찼습니다. 최대 {MAX_SUB_QUEST_SLOTS}개");
                return;
            }
            if (!activeSubQuestSlots.Contains(quest.questID))
            {
                activeSubQuestSlots.Add(quest.questID);
            }
        }
        
        // 퀘스트 상태를 강제로 수락(Accepted) 처리
        questStatuses[quest.questID] = QuestStatus.Accepted;
        Debug.Log($"[QuestManager] 퀘스트 강제 수락: {quest.QuestName}");
    }
    
    /// <summary>
    /// 퀘스트 완료 (보상 지급 및 다음 퀘스트 잠금 해제)
    /// </summary>
    public void CompleteQuest(QuestSO quest)
    {
        if (quest == null) return;
        
        var status = GetQuestStatus(quest.questID);
        if (status != QuestStatus.Accepted)
        {
            Debug.LogWarning($"[QuestManager] 퀘스트 '{quest.QuestName}' 완료 불가 - 수락되지 않음");
            return;
        }
        
        // 요구사항 다시 확인
        if (!CheckRequirements(quest))
        {
            Debug.LogWarning($"[QuestManager] 퀘스트 '{quest.QuestName}' 완료 불가 - 요구사항 미충족");
            return;
        }
        
        // 필요한 광물 차감
        ConsumeRequirements(quest);
        
        // 보상 지급
        GiveRewards(quest);
        
        // 퀘스트 완료 처리
        questStatuses[quest.questID] = QuestStatus.Completed;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(quest.questType == QuestType.Sub
                ? SfxKeys.QuestComplete    // 서브 = 자전거 벨
                : SfxKeys.MissionSuccess); // 메인 = 미션 성공
        }
        
        // 서브퀘스트인 경우 슬롯에서 제거
        if (quest.questType == QuestType.Sub)
        {
            activeSubQuestSlots.Remove(quest.questID);
        }
        
        
        Debug.Log($"[QuestManager] 퀘스트 완료: {quest.QuestName}");
    }
    
    /// <summary>
    /// 강제 퀘스트 완료 (요구사항 무시, 재료 차감 없음) - 튜토리얼 등에서 사용
    /// </summary>
    public void ForceCompleteQuest(QuestSO quest)
    {
        if (quest == null) return;
        
        // 보상 지급
        GiveRewards(quest);
        
        // 퀘스트 완료 처리
        questStatuses[quest.questID] = QuestStatus.Completed;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(quest.questType == QuestType.Sub
                ? SfxKeys.QuestComplete    
                : SfxKeys.MissionSuccess); 
        }
        
        // 서브퀘스트인 경우 슬롯에서 제거
        if (quest.questType == QuestType.Sub)
        {
            activeSubQuestSlots.Remove(quest.questID);
        }
        
        
        Debug.Log($"[QuestManager] 퀘스트 강제 완료: {quest.QuestName}");
    }
    
    /// <summary>
    /// 퀘스트 요구사항 소모
    /// </summary>
    private void ConsumeRequirements(QuestSO quest)
    {
        foreach (var requirement in quest.requirements)
        {
            if (requirement.type == RequirementType.Mineral)
            {
                if (requirement.requiredMineral == null) continue;
                
                if (WarehouseManager.Instance != null)
                {
                    WarehouseManager.Instance.RemoveMineral(
                        requirement.requiredMineral, 
                        requirement.requiredAmount
                    );
                }
            }
            // SceneVisit나 CustomFlag는 차감(Consume)할 필요가 없음
        }
    }
    
    /// <summary>
    /// 퀘스트 보상 지급
    /// </summary>
    private void GiveRewards(QuestSO quest)
    {
        foreach (var reward in quest.rewards)
        {
            switch (reward.rewardType)
            {
                case QuestRewardType.Money:
                    // TODO: 돈 지급 시스템 연결 필요
                    Debug.Log($"[QuestManager] 돈 보상: {reward.moneyAmount}");
                    break;
                    
                case QuestRewardType.Item:
                    if (reward.itemReward != null)
                    {
                        WarehouseManager.Instance.AddItem(reward.itemReward, reward.rewardAmount);
                        Debug.Log($"[QuestManager] 아이템 보상: {reward.itemReward.DisplayName} x{reward.rewardAmount}");
                    }
                    break;
                    
                case QuestRewardType.Equipment:
                    if (reward.equipmentReward != null)
                    {
                        WarehouseManager.Instance.AddEquipment(reward.equipmentReward, reward.rewardAmount);
                        Debug.Log($"[QuestManager] 장비 보상: {reward.equipmentReward.DisplayName} x{reward.rewardAmount}");
                    }
                    break;
                    
                case QuestRewardType.Mineral:
                    if (reward.mineralReward != null)
                    {
                        WarehouseManager.Instance.AddMineral(reward.mineralReward, reward.rewardAmount);
                        Debug.Log($"[QuestManager] 광물 보상: {reward.mineralReward.DisplayName} x{reward.rewardAmount}");
                    }
                    break;
            }
        }
    }
    
    /// <summary>
    /// 퀘스트 상태를 수동으로 강제 설정 (주로 이벤트나 대화 종료 시 해금용으로 사용)
    /// </summary>
    public void SetQuestStatus(QuestSO quest, QuestStatus status)
    {
        if (quest == null) return;
        questStatuses[quest.questID] = status;
        Debug.Log($"[QuestManager] 퀘스트 '{quest.QuestName}' 상태가 {status}로 수동 변경되었습니다.");
    }

    /// <summary>
    /// 채광 레벨 변경 시 호출되어 퀘스트 해금 상태 갱신
    /// </summary>
    public void RefreshSubQuestUnlocks()
    {
        int currentMiningLevel = GetCurrentMiningLevel();
        
        // 1. 서브 퀘스트 갱신
        foreach (var quest in subQuests)
        {
            if (quest == null) continue;
            
            QuestStatus currentStatus = GetQuestStatus(quest.questID);
            
            if (currentStatus == QuestStatus.Accepted || currentStatus == QuestStatus.Completed)
                continue;
            
            bool isAlwaysAvailable = quest.requiredMiningLevel <= -1;
            bool isLevelMatched = (quest.requiredMiningLevel <= currentMiningLevel && 
                                   quest.requiredMiningLevel >= currentMiningLevel - 1);
            
            bool shouldBeAvailable = isAlwaysAvailable || isLevelMatched;
            
            if (shouldBeAvailable && currentStatus == QuestStatus.Locked)
            {
                questStatuses[quest.questID] = QuestStatus.Available;
                Debug.Log($"[QuestManager] 서브 퀘스트 해금 (채광레벨 {currentMiningLevel}): {quest.QuestName}");
            }
        }
        
        // 메인 퀘스트 자동 해금 갱신 로직 제거됨 (수동 관리)
    }
    
    #region 서브퀘스트 슬롯 관리
    
    /// <summary>
    /// 서브퀘스트 슬롯에 여유가 있는지 확인
    /// </summary>
    public bool CanAcceptSubQuest()
    {
        return activeSubQuestSlots.Count < MAX_SUB_QUEST_SLOTS;
    }
    
    /// <summary>
    /// 현재 활성화된 서브퀘스트 슬롯 수
    /// </summary>
    public int GetActiveSubQuestCount()
    {
        return activeSubQuestSlots.Count;
    }
    
    /// <summary>
    /// 특정 슬롯의 서브퀘스트 반환
    /// </summary>
    public QuestSO GetSubQuestInSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= activeSubQuestSlots.Count)
            return null;
        return GetQuestByID(activeSubQuestSlots[slotIndex]);
    }
    
    /// <summary>
    /// 현재 진행 중인 메인퀘스트 반환 (Accepted 우선 반환)
    /// </summary>
    public QuestSO GetActiveMainQuest()
    {
        EnsureAllQuestsLoaded();
        foreach (var quest in _allQuestsDict.Values)
        {
            if (quest.questType == QuestType.Main && GetQuestStatus(quest.questID) == QuestStatus.Accepted)
                return quest;
        }
        return null;
    }
    
    /// <summary>
    /// 활성 서브퀘스트 슬롯 ID 리스트 반환 (저장용)
    /// </summary>
    public List<int> GetActiveSubQuestSlots()
    {
        return new List<int>(activeSubQuestSlots);
    }
    
    /// <summary>
    /// 서브퀘스트 슬롯 로드
    /// </summary>
    public void LoadSubQuestSlots(List<int> slots)
    {
        activeSubQuestSlots = slots != null ? new List<int>(slots) : new List<int>();
    }
    
    #endregion
    
    #region 하루 시스템
    
    /// <summary>
    /// 수면(하루 종료) 시 호출 - 서브퀘스트 자동 포기
    /// </summary>
    public void OnDayEnd()
    {
        if (activeSubQuestSlots.Count == 0)
        {
            Debug.Log("[QuestManager] 하루 종료: 진행 중인 서브퀘스트 없음");
            return;
        }
        
        // 서브퀘스트 자동 포기 (Available 상태로 되돌림)
        foreach (int questID in activeSubQuestSlots.ToList())
        {
            questStatuses[questID] = QuestStatus.Available;
            Debug.Log($"[QuestManager] 서브퀘스트 자동 포기: {GetQuestByID(questID)?.QuestName}");
        }
        activeSubQuestSlots.Clear();
        
        Debug.Log("[QuestManager] 하루 종료: 모든 서브퀘스트 자동 포기됨");
    }
    
    #endregion
    
    #region 저장/로드
    
    /// <summary>
    /// 세이브 데이터 반환
    /// </summary>
    public Dictionary<int, QuestStatus> GetSaveData()
    {
        return new Dictionary<int, QuestStatus>(questStatuses);
    }
    
    /// <summary>
    /// 세이브 데이터 로드
    /// </summary>
    public void LoadSaveData(Dictionary<int, QuestStatus> savedStatuses)
    {
        if (savedStatuses == null) return;
        
        questStatuses = new Dictionary<int, QuestStatus>(savedStatuses);
    }
    
    /// <summary>
    /// 달성한 플래그 목록 반환
    /// </summary>
    public List<string> GetUnlockedFlags()
    {
        return _unlockedFlags.ToList();
    }
    
    /// <summary>
    /// 저장된 플래그 복구
    /// </summary>
    public void LoadUnlockedFlags(List<string> flags)
    {
        _unlockedFlags.Clear();
        if (flags != null)
        {
            foreach (var f in flags)
                _unlockedFlags.Add(f);
        }
    }
    
    #endregion
}
