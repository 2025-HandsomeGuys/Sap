using System;
using System.Collections.Generic;

/// <summary>
/// 퀘스트 진행 상황을 저장하기 위한 데이터 클래스
/// </summary>
[Serializable]
public class QuestProgressData
{
    // questID와 상태를 직렬화 가능한 형태로 저장
    public List<int> questIDs = new List<int>();
    public List<int> questStatuses = new List<int>(); // QuestStatus를 int로 저장
    
    // 서브퀘스트 슬롯 정보 (진행 중인 서브퀘스트 ID 목록)
    public List<int> activeSubQuestSlots = new List<int>();

    // 달성한 퀘스트 플래그 (씬 방문, 커스텀 이벤트 등)
    public List<string> unlockedFlags = new List<string>();

    
    /// <summary>
    /// Dictionary로 변환
    /// </summary>
    public Dictionary<int, QuestStatus> ToDictionary()
    {
        Dictionary<int, QuestStatus> result = new Dictionary<int, QuestStatus>();
        
        for (int i = 0; i < questIDs.Count && i < questStatuses.Count; i++)
        {
            result[questIDs[i]] = (QuestStatus)questStatuses[i];
        }
        
        return result;
    }
    
    /// <summary>
    /// Dictionary에서 생성
    /// </summary>
    public static QuestProgressData FromDictionary(Dictionary<int, QuestStatus> dict)
    {
        QuestProgressData data = new QuestProgressData();
        
        if (dict != null)
        {
            foreach (var pair in dict)
            {
                data.questIDs.Add(pair.Key);
                data.questStatuses.Add((int)pair.Value);
            }
        }
        
        return data;
    }
}
