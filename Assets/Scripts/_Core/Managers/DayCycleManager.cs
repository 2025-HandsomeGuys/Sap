using UnityEngine;
using System;

public class DayCycleManager : MonoBehaviour
{
    public static DayCycleManager Instance { get; private set; }

    [Header("State")]
    [SerializeField] private int currentDay = 1;
    [SerializeField] private TimeOfDay currentTime = TimeOfDay.Morning;

    public int CurrentDay => currentDay;
    public TimeOfDay CurrentTime => currentTime;

    public event Action<int, TimeOfDay> OnDateTimeChanged;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void AdvanceToNextDay()
    {
        if (TutorialProgress.IsCompleted)
        {
            currentDay++;
        }
        else
        {
            Debug.Log("[DayCycleManager] 튜토리얼 중이므로 일차 수를 올리지 않습니다.");
        }
        
        currentTime = TimeOfDay.Morning;

        Debug.Log($"[DayCycleManager] AdvanceToNextDay - Day {currentDay}, Time {currentTime}");
        OnDateTimeChanged?.Invoke(currentDay, currentTime);
    }

    public void SetMorning()
    {
        currentTime = TimeOfDay.Morning;
        OnDateTimeChanged?.Invoke(currentDay, currentTime);
    }

    public void SetAfternoon()
    {
        currentTime = TimeOfDay.Afternoon;
        OnDateTimeChanged?.Invoke(currentDay, currentTime);
    }

    public void LoadData(int day, TimeOfDay time)
    {
        currentDay = day;
        currentTime = time;
        OnDateTimeChanged?.Invoke(currentDay, currentTime);
    }

    public string GetDayText() => $"{currentDay}일차";

    public string GetFormattedDayTimeText()
    {
        string timeStr = currentTime == TimeOfDay.Morning ? "오전" : "오후";
        return $"{currentDay}일차 {timeStr}";
    }
}