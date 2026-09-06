// @tags: interaction, behaviour, chunk, dungeon, entrance, exit, overlay
using UnityEngine;

/// <summary>
/// 지하 → 청크(던전) 진입. 기존 <c>DungeonDoorChunk</c>와 같은 동작이다.
/// 지하 씬은 그대로 두고 청크 지오메트리를 먼 offset에 생성해 플레이어를 텔레포트하는
/// 오버레이 방식이라, 청크 좌표(<see cref="WorldInteractable.ChunkCoord"/>)가 인스턴스 ID가 된다.
///
/// 이미 탐험한 입구는 재입장 불가 — 문구가 붉게 바뀌고 스프라이트도 회색으로 굳는다.
/// </summary>
public sealed class ChunkEntranceBehaviour : InteractionBehaviour
{
    private bool IsUsed => DungeonStateStore.IsUsed(Owner.ChunkCoord);

    public override bool IsAvailable => !IsUsed;

    /// <summary>탐험 완료면 회색 고정 — 근접 하이라이트로 덮어쓰지 않는다.</summary>
    public override Color? OverrideTint() => IsUsed ? Owner.ExploredColor : (Color?)null;

    public override InteractionPromptInfo GetPrompt()
        => IsUsed
            ? InteractionPromptInfo.Blocked("interact_dungeon_used", "이미 탐험한 던전")
            : InteractionPromptInfo.Ok("interact_chunk_enter", "들어가기");

    public override void Interact(GameObject interactor)
    {
        if (DungeonOverlayController.IsInDungeon) return; // 이미 청크 안이면 무시

        if (IsUsed)
        {
            Debug.Log("[WorldInteractable/ChunkEntrance] 이미 탐험한 청크 — 재입장 불가.");
            return;
        }

        if (Owner.ChunkPrefab == null)
        {
            Debug.LogWarning($"[WorldInteractable/ChunkEntrance] {Owner.name}: 청크 프리팹이 설정되지 않았습니다.");
            return;
        }

        // 모든 방어 분기를 통과해 입장이 확정된 뒤에만 재생
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.PortalEnter);

        DungeonOverlayController.Instance.EnterDungeon(Owner.ChunkPrefab, Owner.ChunkCoord);
    }
}

/// <summary>
/// 청크(던전) → 지하 복귀. 기존 <c>DungeonExitInteractable</c>과 같은 동작이다.
/// 이 문(인스턴스)을 '탐험 완료'로 찍고 저장한 뒤 오버레이를 벗어난다.
/// 저장은 <c>DungeonStateStore.Capture</c>를 유발해 부순 rock·수집 보상까지 확정된다.
/// </summary>
public sealed class ChunkExitBehaviour : InteractionBehaviour
{
    public override InteractionPromptInfo GetPrompt()
        => InteractionPromptInfo.Ok("interact_chunk_exit", "지하로 돌아가기");

    public override void Interact(GameObject interactor)
    {
        if (!DungeonOverlayController.IsInDungeon) return; // 청크 밖이면 무시

        var sm = GameManager.Instance != null ? GameManager.Instance.saveManager : null;

        if (sm != null && sm.playerData != null && Owner.ClearRewardGold > 0)
        {
            sm.playerData.gold += Owner.ClearRewardGold;
            Debug.Log($"[WorldInteractable/ChunkExit] 클리어 보상 +{Owner.ClearRewardGold} 골드 (현재: {sm.playerData.gold})");
        }

        // 이 인스턴스를 탐험 완료로 표시 → 같은 입구로 다시 못 들어간다.
        DungeonStateStore.MarkUsed(DungeonStateStore.CurrentInstance);

        // 지하 씬이 유지되므로 인벤토리·골드도 정상 저장된다(전체 씬 리로드 없음).
        if (sm != null) sm.Save();

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.MissionSuccess);

        DungeonOverlayController.Instance.ExitDungeon();
    }
}
