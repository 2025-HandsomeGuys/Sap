// @tags: map, minimap, worldmap, marker, poi, registry, elevator, entrance, rock, chunk
using System.Collections.Generic;
using UnityEngine;

/// <summary>지도(미니맵·전체지도)에 아이콘으로 표기되는 관심지점 종류.</summary>
public enum MapMarkerKind
{
    Elevator,       // 엘리베이터 — 상호작용 범위에 도달하거나 실제로 이용해야 발견됨
    ChunkEntrance   // 청크(던전) 입구 — 상호작용 범위에 도달해야 발견됨
}

/// <summary>
/// 엘리베이터 표식용 마커 인터페이스. 이걸 구현한 IInteractable이 플레이어의 상호작용
/// 범위 안에 들어오면 PlayerInteractor가 <see cref="MapMarkerRegistry.Discover"/>를
/// <see cref="MapMarkerKind.Elevator"/>로 호출한다.
/// (엘리베이터로 순간이동하면 목적지 엘리베이터 위에 착지하므로, 이용한 층도 이 경로로 발견된다.)
/// <see cref="IMapEntrance"/>와 같은 발상: 청크 로드만으로는 발견되지 않고,
/// 실제로 '상호작용 가능한 위치'에 도달했을 때만 지도에 남는다.
/// (ElevatorController, WorldInteractable(Elevator)이 구현)
/// </summary>
public interface IMapElevator { }

/// <summary>
/// 엘리베이터 여부를 <b>런타임에</b> 정하는 보조 인터페이스(<see cref="IMapEntranceToggle"/>와 동일한 이유).
/// 통합 컴포넌트(<see cref="WorldInteractable"/>)는 인스펙터에서 종류가 바뀌므로,
/// 이걸 구현하면 <see cref="IsMapElevatorNow"/>가 true일 때만 엘리베이터 마커로 등록된다.
/// </summary>
public interface IMapElevatorToggle
{
    bool IsMapElevatorNow { get; }
}

/// <summary>
/// 청크 입구 표식용 마커 인터페이스. 이걸 구현한 IInteractable이 플레이어의 상호작용
/// 범위 안에 들어오면 PlayerInteractor가 <see cref="MapMarkerRegistry.Discover"/>를 호출한다.
/// (DungeonEntranceInteractable, DungeonDoorChunk가 구현)
/// </summary>
public interface IMapEntrance { }

/// <summary>
/// 입구 여부를 <b>런타임에</b> 정하는 보조 인터페이스.
/// <see cref="IMapEntrance"/>는 멤버가 없는 표식이라 "지금은 입구가 아니다"를 말할 수 없는데,
/// 통합 컴포넌트(<see cref="WorldInteractable"/>)는 인스펙터에서 종류가 바뀌므로 이게 필요하다.
/// 이걸 구현하면 <see cref="IsMapEntranceNow"/>가 true일 때만 입구 마커로 등록된다.
/// 구현하지 않은 기존 오브젝트는 종전대로 항상 입구로 취급된다.
/// </summary>
public interface IMapEntranceToggle
{
    bool IsMapEntranceNow { get; }
}

/// <summary>
/// 지도에 '안 파진 땅'처럼 그 자리를 채워 그릴 살아있는 돌.
/// 등록돼 있는 동안만 지도에 채워지고, 파괴/언로드로 등록 해제되면 자연스럽게 사라진다.
/// </summary>
public interface IMapRock
{
    /// <summary>돌의 시각적 중심(월드 좌표). 모양 마스크가 없을 때 원반 폴백에 사용.</summary>
    Vector2 MapWorldCenter { get; }
    /// <summary>지도에 채울 대략적 반경(월드 유닛). 모양 마스크가 없을 때 원반 폴백에 사용.</summary>
    float MapWorldRadius { get; }
    /// <summary>광물돌이면 true — 별도 색으로 그린다.</summary>
    bool IsMineralRock { get; }

    /// <summary>실제 돌 실루엣 마스크를 지도에 그릴 수 있는지(마스크·청크 보유). false면 원반 폴백.</summary>
    bool HasMapShape { get; }
    /// <summary>모양 채움 시 훑을 월드 AABB.</summary>
    Rect MapWorldBounds { get; }
    /// <summary>월드 점이 이 돌의 실루엣(불투명 픽셀) 안에 있는지.</summary>
    bool ContainsWorldPoint(Vector2 world);
}

/// <summary>
/// 지도 마커/돌 채움을 위한 세션 레지스트리(정적).
///  · 엘베·청크입구: 발견되면 좌표를 <b>영속</b> 기록한다(청크 언로드와 무관하게 지도에 남음).
///  · 돌: 현재 노출된 살아있는 IMapRock 목록을 들고 있다가 파괴 시 제거 → 지도에서 사라짐.
/// DigPathTracker/MapTerrainCache와 동일하게 씬 세션 동안만 유지된다(디스크 세이브 없음).
/// </summary>
public static class MapMarkerRegistry
{
    public struct Marker
    {
        public Vector2 world;
        public MapMarkerKind kind;
    }

    // ── 발견된 영속 마커 (엘베·청크입구) ──
    private static readonly List<Marker> _markers = new List<Marker>();
    private static readonly HashSet<(int, int, MapMarkerKind)> _seen = new HashSet<(int, int, MapMarkerKind)>();

    public static IReadOnlyList<Marker> Markers => _markers;

    /// <summary>지도 텍스처 재렌더가 필요한 마커 변경이 있었는지 (돌은 <see cref="MapRockCache.IsDirty"/> 별도).</summary>
    public static bool IsDirty { get; private set; }
    public static void ClearDirty() => IsDirty = false;

    // 같은 종류 마커의 중복 판정 셀 크기(월드 유닛). 이 격자 내 같은 종류는 하나로 취급.
    private const float DedupCell = 2f;

    /// <summary>엘베·청크입구를 발견(영속 기록)한다. 이미 근처에 같은 종류가 있으면 무시.</summary>
    public static void Discover(Vector2 worldPos, MapMarkerKind kind)
    {
        var key = (Mathf.RoundToInt(worldPos.x / DedupCell),
                   Mathf.RoundToInt(worldPos.y / DedupCell),
                   kind);
        if (!_seen.Add(key)) return;
        _markers.Add(new Marker { world = worldPos, kind = kind });
        IsDirty = true;
    }

    /// <summary>
    /// 이 청크 입구가 '이미 탐험해 재입장 불가' 상태인지 (지도에 다른 스프라이트로 표시하기 위함).
    /// 마커 월드 좌표 → 청크 좌표로 변환해 <see cref="DungeonStateStore.IsUsed"/>를 조회한다.
    /// (씬 로드형 입구 등 DungeonStateStore를 쓰지 않는 입구는 항상 false → 일반 아이콘 유지)
    /// </summary>
    public static bool IsEntranceUsed(Vector2 worldPos)
    {
        var imm = InfinityMapManager.Instance;
        if (imm == null) return false;
        int cx = Mathf.FloorToInt(worldPos.x / imm.chunkWidthWorld);
        int cy = Mathf.FloorToInt(worldPos.y / imm.chunkHeightWorld);
        return DungeonStateStore.IsUsed(new Vector2Int(cx, cy));
    }

    /// <summary>
    /// 새 게임/잠수 리셋 시 전체 초기화. 지상 복귀 시 <see cref="ExploreExitController"/>가,
    /// 지하 진입 시 <see cref="SaveManager.PrepareUndergroundEntry"/>가 호출한다.
    /// 잠수마다 새 지형이므로 이전 잠수의 마커·돌 기록이 남으면 안 된다. 특히 엘리베이터는
    /// 진입 시점에 반드시 비워져, 이번 잠수에서 직접 가본/이용한 것만 다시 쌓이게 한다.
    /// (던전 복귀는 PrepareUndergroundEntry를 타지 않으므로 잠수 중 발견한 마커는 유지된다.)
    /// </summary>
    public static void Reset()
    {
        _markers.Clear();
        _seen.Clear();
        MapRockCache.Clear();
        IsDirty = true;
    }
}
