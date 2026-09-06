// @tags: dungeon, exit, escape, return, save
using UnityEngine;

/// <summary>
/// 던전 이탈의 단일 경로. 클리어(출구 문)든 중도 탈출이든 여기를 통한다.
///
/// 순서가 전부다 — <c>MarkUsed</c> → <c>Save</c> → <c>ExitDungeon</c>.
/// <see cref="SaveManager.Save"/>가 <see cref="DungeonStateStore.Capture"/>를 유발하므로
/// MarkUsed를 Save보다 뒤로 미루면 "탐험 완료"가 세이브에 안 남아 던전이 다시 열린다.
/// 이 3단을 호출부마다 복붙하면 그 순서가 조용히 어긋나므로 반드시 이 메서드를 쓴다.
///
/// 중도 탈출도 클리어와 동일하게 재입장 불가로 처리한다(탈출 = 포기하고 목숨만 건짐).
/// 보상 골드처럼 경로마다 다른 처리는 호출부 책임이며, 이 메서드를 부르기 전에 끝내야 한다.
/// </summary>
public static class DungeonEscape
{
    /// <summary>
    /// 현재 던전에서 나간다. 던전 밖이면 아무것도 하지 않고 false.
    /// </summary>
    /// <param name="message">콘솔·로그에 그대로 쓸 수 있는 결과 문구</param>
    public static bool TryLeave(out string message)
    {
        if (!DungeonOverlayController.IsInDungeon)
        {
            message = "던전 안이 아님";
            return false;
        }

        var coord = DungeonStateStore.CurrentInstance;

        // 이 문(인스턴스)을 "탐험 완료"로 표시 → 다시 못 들어감.
        DungeonStateStore.MarkUsed(coord);

        // Save가 DungeonStateStore.Capture를 포함 → 던전 인스턴스 상태(부순 rock·수집 보상·탐험완료) 확정 저장.
        // 지하씬이 유지되므로 인벤토리·골드도 정상 저장된다(전체 씬 리로드 없음 = 인벤토리 초기화 없음).
        var sm = GameManager.Instance != null ? GameManager.Instance.saveManager : null;
        if (sm != null) sm.Save();

        // Additive 오버레이 이탈: 플레이어를 문 위치로 되돌리고 던전 씬 언로드.
        DungeonOverlayController.Instance.ExitDungeon();

        message = $"던전 이탈 — 문 {coord} 탐험 완료 처리(재입장 불가)";
        return true;
    }
}
