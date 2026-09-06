// @tags: interaction, behaviour, elevator, entrance, surface, scene, travel
using UnityEngine;

/// <summary>
/// 지상 엘리베이터 입구 — 지상(DemoUpground)에서 E로 상호작용하면 층 선택 UI를 띄우고,
/// 고른 정류장(지하 엘리베이터)에 착지하도록 지하 씬을 로드한다.
///
/// <b>땅굴 입구(<see cref="TunnelEntranceBehaviour"/>)와의 차이</b> — 땅굴 입구는 고정
/// 스폰 지점으로 바로 내려가지만, 이 입구는 <see cref="ElevatorEntryUI"/>로 목표 정류장을
/// 먼저 고르게 한 뒤 그 엘리베이터 청크 중앙에 착지시킨다.
///
/// <b>세팅</b> — WorldInteractable에서 Kind='엘리베이터 입구'를 고르고,
///  · [엘리베이터] elevatorXChunk — <b>착지 좌표와 무관하다.</b> 층마다 엘리베이터가
///    하나뿐이고 그 X도 층이 정하므로(ElevatorStopLayout), 실제 착지 X는
///    ElevatorEntryUI.SelectStop이 고른 정류장에서 구한다.
///  · [땅굴 입구] sceneToLoad = 지하 씬 이름(기본 "DemoUnderground"),
///                blockAtNight = 저녁 진입 차단 여부
/// 만 채우면 된다. 실제 착지는 PlayerData.spawnAtElevator 오버라이드를 PlayerSpawner가 소비.
/// </summary>
public sealed class SurfaceElevatorEntranceBehaviour : InteractionBehaviour
{
    /// <summary>저녁엔 지하로 못 내려간다(땅굴 입구와 동일 규칙).</summary>
    private bool IsClosedForToday() => Owner.BlockAtNight && IsEvening();

    public override bool IsAvailable => !IsClosedForToday();

    public override InteractionPromptInfo GetPrompt()
    {
        return IsClosedForToday()
            ? InteractionPromptInfo.Blocked("interact_dungeon_night", "날이 어두워져 들어갈 수 없다")
            : InteractionPromptInfo.Ok("interact_elevator_enter", "엘리베이터로 내려가기");
    }

    public override void Interact(GameObject interactor)
    {
        if (IsClosedForToday())
        {
            if (Sap.UI.Notification.NotificationUI.Instance != null)
                Sap.UI.Notification.NotificationUI.Instance.ShowNotification(
                    CodeUI.L("interact_dungeon_night", "날이 어두워져 들어갈 수 없다"));
            Debug.Log("[SurfaceElevatorEntrance] 저녁이라 지하로 내려갈 수 없습니다.");
            return;
        }

        if (IsAnyUIOpen()) return;

        string scene = Owner.SceneToLoad;
        if (string.IsNullOrEmpty(scene))
        {
            Debug.LogWarning($"[SurfaceElevatorEntrance] {Owner.name}: 이동할 지하 씬 이름이 비어 있습니다.");
            return;
        }

        ElevatorEntryUI.Open(Owner.ElevatorXChunk, scene);
    }
}
