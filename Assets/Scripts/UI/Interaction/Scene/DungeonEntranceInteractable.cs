using UnityEngine;
using UnityEngine.SceneManagement;
using Sap.UI.Notification;

/// <summary>
/// 던전 진입을 위한 상호작용 컴포넌트입니다.
/// 플레이어가 F 키로 상호작용하면 현재 위치를 저장하고 던전 씬으로 이동합니다.
/// </summary>
public class DungeonEntranceInteractable : MonoBehaviour, IInteractable, IMapEntrance,
                                           IInteractionAvailability, IInteractionPrompt
{
    [Header("던전 설정")]
    [SerializeField] private string dungeonSceneName = "DungeonScene";

    [Tooltip("켜면 저녁(Afternoon)에는 진입을 막고 '날이 어두워져 들어갈 수 없다'를 띄운다")]
    [SerializeField] private bool blockAtNight = true;

    [Header("상호작용 텍스트")]
    [Tooltip("Localization 키를 못 찾았을 때 쓸 원문")]
    [SerializeField] private string interactionPrompt = "지하로 들어가기";

    public string InteractionPrompt => interactionPrompt;

    // 포탈이나 주요 엔티티처럼 상호작용 우선순위를 높게 설정
    public int InteractionPriority => 5;

    /// <summary>저녁이면 진입 불가 — 느낌표(InteractionIndicator)도 뜨지 않는다.</summary>
    public bool IsInteractionAvailable => !IsClosedForToday();

    /// <summary>
    /// 근접 안내 문구. 아침 = "지하로 들어가기"(미색) / 저녁 = "날이 어두워져 들어갈 수 없다"(붉은색).
    /// </summary>
    public InteractionPromptInfo GetInteractionPrompt()
    {
        return IsClosedForToday()
            ? InteractionPromptInfo.Blocked("interact_dungeon_night", "날이 어두워져 들어갈 수 없다")
            : InteractionPromptInfo.Ok("interact_dungeon_enter", interactionPrompt);
    }

    /// <summary>저녁(Afternoon)에는 지하로 내려갈 수 없다. blockAtNight를 끄면 항상 열려 있다.</summary>
    private bool IsClosedForToday()
    {
        return blockAtNight &&
               DayCycleManager.Instance != null &&
               DayCycleManager.Instance.CurrentTime == TimeOfDay.Afternoon;
    }

    public void Interact(GameObject interactor)
    {
        if (IsClosedForToday())
        {
            if (NotificationUI.Instance != null)
                NotificationUI.Instance.ShowNotification(
                    CodeUI.L("interact_dungeon_night", "날이 어두워져 들어갈 수 없다"));
            Debug.Log("[DungeonEntranceInteractable] 저녁이라 지하로 내려갈 수 없습니다.");
            return;
        }

        if (string.IsNullOrEmpty(dungeonSceneName))
        {
            Debug.LogWarning("[DungeonEntranceInteractable] 이동할 던전 씬 이름이 설정되지 않았습니다.");
            return;
        }

        // 1. 현재 플레이어의 위치 저장 (복귀 시 사용)
        if (GameManager.Instance != null && GameManager.Instance.saveManager != null)
        {
            Vector3 playerPos = interactor.transform.position;
            GameManager.Instance.saveManager.PrepareDungeonEntry(playerPos);
            Debug.Log($"[DungeonEntranceInteractable] 던전({dungeonSceneName})으로 입장을 시작합니다.");
        }
        else
        {
            Debug.LogWarning("[DungeonEntranceInteractable] SaveManager를 찾을 수 없어 위치를 저장하지 못했습니다.");
        }

        // 2. 던전 씬으로 전환
        SceneLoader.LoadScene(dungeonSceneName);
    }
}
