// @tags: surface, spawn, return, entrance, scene, save
using UnityEngine;

/// <summary>
/// 지상으로 올라왔을 때 플레이어가 나올 지점. 값은 PlayerData.undergroundEntryKind와 동일하다.
/// </summary>
public enum SurfaceReturnPoint
{
    Default  = 0,   // 씬에 배치된 그대로 (집 앞)
    Hole     = 1,   // 구멍(땅굴 입구)으로 내려갔었다
    Elevator = 2,   // 지상 엘리베이터로 내려갔었다
}

/// <summary>
/// "어느 입구로 내려갔는가"를 기록해 두었다가 지상 복귀 위치로 넘겨주는 중계소.
///
/// 흐름:
///  1. 내려갈 때  — <see cref="Stamp"/>가 PlayerData에 기록. 반드시 Save() 호출 **전에** 부를 것
///                 (PrepareUndergroundEntry가 저장하므로 그 앞에 두면 파일에 실린다).
///  2. 올라올 때  — <see cref="HandOff"/>가 파일 기록을 static <see cref="Pending"/>으로 옮기고 필드를 비운다.
///                 지상 복귀 경로 셋(정상 귀환 ExploreExitController·긴급 탈출 PauseOverlayUI/PauseUI·
///                 사망 GameOverHandler)에서 모두 부른다.
///  3. 지상 씬     — <see cref="SurfaceSpawnPlacer"/>가 <see cref="Consume"/>로 소비해 위치를 잡는다.
///
/// 3단계에서 파일이 아니라 static을 읽는 이유:
/// 지상 씬의 Start 시점은 SettlementSceneController가 SaveManager.Load()를 부르기 전이라 파일 값을
/// 신뢰할 수 없고, 그 시점에 Save()를 하면 아직 채워지지 않은 창고 데이터를 덮어쓴다.
/// static은 씬 로드를 넘어 살아남으므로 이 구간의 전달에 딱 맞는다.
/// 중간에 게임을 껐다 켜도 1단계 기록이 세이브에 남아 있어 다음 복귀에서 정상 동작한다.
/// </summary>
public static class SurfaceReturnRouter
{
    /// <summary>지상 씬이 아직 소비하지 않은 복귀 지점.</summary>
    public static SurfaceReturnPoint Pending { get; private set; } = SurfaceReturnPoint.Default;

    private static PlayerData Data =>
        GameManager.Instance != null && GameManager.Instance.saveManager != null
            ? GameManager.Instance.saveManager.playerData
            : null;

    /// <summary>지하 진입 시 사용한 입구를 기록한다. 저장(Save) 직전에 호출할 것.</summary>
    public static void Stamp(SurfaceReturnPoint point)
    {
        var data = Data;
        if (data == null)
        {
            Debug.LogWarning("[SurfaceReturnRouter] PlayerData가 없어 입구를 기록하지 못했습니다.");
            return;
        }

        data.undergroundEntryKind = (int)point;
        Debug.Log($"[SurfaceReturnRouter] 지하 진입 입구 기록: {point}");
    }

    /// <summary>지상 복귀 직전에 호출. 기록을 Pending으로 옮기고 세이브 필드는 비운다.</summary>
    public static void HandOff()
    {
        var data = Data;
        if (data == null) return;

        Pending = (SurfaceReturnPoint)data.undergroundEntryKind;
        data.undergroundEntryKind = (int)SurfaceReturnPoint.Default;   // 1회성 — 다음 잠수에서 다시 찍힌다
        Debug.Log($"[SurfaceReturnRouter] 지상 복귀 지점 예약: {Pending}");
    }

    /// <summary>지상 씬에서 한 번만 소비한다. 소비 후에는 Default로 되돌아간다.</summary>
    public static SurfaceReturnPoint Consume()
    {
        var point = Pending;
        Pending = SurfaceReturnPoint.Default;
        return point;
    }
}
