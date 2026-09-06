// @tags: elevator, unlock, stop, discovery, save, static
using System;
using System.Collections.Generic;

/// <summary>
/// 엘리베이터 정류장 <b>해금 기록</b> 전역 저장소(순수 C# static).
///
/// 규칙: <b>그 층의 엘리베이터에 직접 걸어가 본 적이 있어야</b> 그 정류장으로 이동할 수 있다.
/// 예전에는 아직 안 가본 층도 목록에서 그냥 고를 수 있어서, 땅을 파고 내려가는 과정 자체가
/// 건너뛰어졌다. 해금 훅은 <see cref="ElevatorController"/>가 플레이어 근접을 감지한 시점 —
/// 지도 마커(<c>IMapElevator</c>)가 발견되는 시점과 같은 기준이다.
/// 층 이동으로 순간이동하면 목적지 엘리베이터 위에 착지하므로 그 층도 같은 경로로 해금된다.
///
/// <b>키는 layerIndex가 아니라 청크 깊이(Y)</b>다. layerIndex는 <see cref="ElevatorLayerCatalog"/>의
/// 나열 순서라 정류장을 하나 끼워 넣으면 통째로 밀린다(실제로 상층·하층 2개 → 상·중·하층 3개로
/// 바뀌면서 밀렸다). 그때 인덱스로 저장된 세이브는 엉뚱한 층이 열린 채로 복원된다.
/// 깊이는 지층 정의가 그대로면 그대로다.
///
/// 첫 정류장(<see cref="AlwaysUnlockedLayerIndex"/> = 가장 얕은 층)은 <b>항상 해금</b>이다 —
/// 지상 엘리베이터 입구에서 아무 데도 못 내려가면 게임이 시작되지 않는다.
///
/// 세이브는 <see cref="CollectionCodex"/>와 같은 패턴 —
/// <see cref="CaptureSaveData"/>/<see cref="ApplySaveData"/>, PlayerData.elevatorStops. 뉴게임은 <see cref="Clear"/>.
/// </summary>
public static class ElevatorStopUnlockStore
{
    /// <summary>이 인덱스의 정류장은 방문 여부와 무관하게 항상 열려 있다(지상에서의 첫 진입로).</summary>
    public const int AlwaysUnlockedLayerIndex = 0;

    /// <summary>
    /// 디버그 콘솔 전용 표식 깊이. 실제 정류장 깊이(0 이상)와 절대 겹치지 않는 값이라
    /// <see cref="UnlockedCount"/>만 1로 올리고 <b>정류장은 하나도 열지 않는다</b> —
    /// "엘리베이터를 본 적 있다"가 조건인 지상 입구(<c>WorldInteractable.requireElevatorDiscovered</c>)를
    /// 층 이동 목록을 건드리지 않고 여는 용도.
    /// </summary>
    public const int DebugDiscoveryDepth = int.MinValue;

    private static readonly HashSet<int> _depths = new HashSet<int>();

    /// <summary>정류장이 새로 해금될 때 발생(청크 깊이). 열려 있는 UI 갱신·연출용.</summary>
    public static event Action<int> OnUnlocked;

    /// <summary>해금된 정류장 수. 0이면 "엘리베이터를 아직 한 번도 못 봤다"와 같다.</summary>
    public static int UnlockedCount => _depths.Count;

    /// <summary>정류장 하나가 열려 있는지. 첫 정류장은 방문 기록이 없어도 true.</summary>
    public static bool IsUnlocked(LayerInfo stop)
        => stop != null && (stop.layerIndex == AlwaysUnlockedLayerIndex || _depths.Contains(stop.startDepth));

    /// <summary>깊이만으로 판정(첫 정류장 예외는 적용되지 않는다 — LayerInfo가 있으면 위쪽을 쓸 것).</summary>
    public static bool IsDepthUnlocked(int depth) => _depths.Contains(depth);

    /// <summary>해금 기록. 처음 열렸으면 true(연출·토스트 트리거용).</summary>
    public static bool Unlock(int depth)
    {
        if (!_depths.Add(depth)) return false;

        OnUnlocked?.Invoke(depth);
        return true;
    }

    /// <summary>뉴게임 초기화.</summary>
    public static void Clear()
    {
        // 해금은 영구 기록이라 '지워지는 순간'이 곧 버그다. 지울 게 있었을 때만 흔적을 남긴다
        // (Unity 콘솔에서 호출 스택까지 보여 누가 지웠는지 바로 잡힌다).
        if (_depths.Count > 0)
            UnityEngine.Debug.Log($"[ElevatorStopUnlockStore] 해금 기록 {_depths.Count}개 초기화");

        _depths.Clear();
    }

    public static ElevatorStopUnlockData CaptureSaveData()
    {
        var data = new ElevatorStopUnlockData();
        data.depths.AddRange(_depths);
        return data;
    }

    public static void ApplySaveData(ElevatorStopUnlockData data)
    {
        Clear();
        if (data == null || data.depths == null)
        {
            UnityEngine.Debug.Log("[ElevatorStopUnlockStore] 복원: 세이브에 해금 기록 없음");
            return;
        }

        foreach (int d in data.depths) _depths.Add(d);
        UnityEngine.Debug.Log($"[ElevatorStopUnlockStore] 복원: {_depths.Count}개 (depths: {string.Join(", ", data.depths)})");
    }
}

/// <summary>세이브에 실리는 정류장 해금 기록(청크 깊이 목록).</summary>
[Serializable]
public class ElevatorStopUnlockData
{
    public List<int> depths = new List<int>();
}
