// @tags: dungeon, exit, interactable, scene, return
using UnityEngine;

/// <summary>
/// 던전 출구 — 플레이어가 F키로 상호작용하면 원래 지형 씬으로 복귀한다.
/// DungeonDoorChunk(입구)와 동일한 InteractableBlockBase 패턴(E 프롬프트·근접 하이라이트).
/// 저장(Save)이 DungeonStateStore.Capture를 유발해 이 던전 인스턴스 상태가 확정된다.
/// 복귀 위치(문 앞)는 진입 시 PrepareDungeonEntry가 기록해둔 것을 원래 씬의 PlayerSpawner가 사용한다.
/// (트리거 접촉 방식이 필요하면 기존 DungeonExitTrigger를 대신 사용)
/// </summary>
public class DungeonExitInteractable : InteractableBlockBase
{
    [Header("Dungeon Exit")]
    [Tooltip("클리어 보상 골드 (DungeonRewardPickup으로 따로 주면 0 권장)")]
    [SerializeField] private int clearRewardGold = 0;

    protected override void HandleInteraction(GameObject interactor)
    {
        if (!DungeonOverlayController.Instance.InDungeon) return; // 던전 밖이면 무시

        var sm = GameManager.Instance != null ? GameManager.Instance.saveManager : null;
        if (sm != null && sm.playerData != null && clearRewardGold > 0)
        {
            sm.playerData.gold += clearRewardGold;
            Debug.Log($"[DungeonExit] 클리어 보상 +{clearRewardGold} 골드 (현재: {sm.playerData.gold})");
        }

        // 탐험 완료 표시 → 저장 → 오버레이 이탈. 순서가 중요해서 DungeonEscape가 단독으로 소유한다.
        // 보상 골드는 여기서 먼저 지급해야 Save에 실린다.
        DungeonEscape.TryLeave(out _);
    }
}
