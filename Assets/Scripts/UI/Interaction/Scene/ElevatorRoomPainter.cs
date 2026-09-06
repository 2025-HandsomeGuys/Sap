// @tags: elevator, chunk, image, painter, landing, room
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 엘리베이터 착지 청크를 지층별 방 PNG로 교체한다.
/// 구멍 입구 방은 ImageChunkOverrider가 따로 담당한다 — 두 경로가 서로 다른 방을 쓴다.
///
/// 대상은 엘리베이터가 서 있는 청크 그 자체다.
/// (예전 ElevatorManager는 chunkY-1, 즉 한 칸 아래 청크에 칠하고 있었다.
///  청크 피벗은 좌하단이고 엘리베이터는 로컬 (5,5)이므로
///  FloorToInt((coord.y*10+5)/10) == coord.y 가 맞는 좌표다.)
///
/// 칠한 방은 보호 좌표가 되어 절차 데코가 스킵되므로, 광물은 IChunkPostLoadPainter 훅에서
/// "페인팅 직후"에 직접 깐다(spawnMinerals). 훅이 매 로드마다 불리므로 재로드해도 유지된다.
/// </summary>
public class ElevatorRoomPainter : MonoBehaviour, IChunkPostLoadPainter
{
    [Tooltip("정류장별 방 PNG. 인덱스 = ElevatorManager.layers의 layerIndex — 지층마다 상층·중층·하층 3개씩 총 12개.\n" +
             "0 땅 상 / 1 땅 중 / 2 땅 하 / 3 얼음 상 / 4 얼음 중 / 5 얼음 하\n" +
             "6 용암 상 / 7 용암 중 / 8 용암 하 / 9 우주 상 / 10 우주 중 / 11 우주 하\n" +
             "같은 방을 쓰려면 같은 텍스처를 여러 칸에 넣으면 된다. 비운 칸은 기본 지형에 착지한다.\n" +
             "1000×1000, Read/Write Enabled 필수. 텍스처 정중앙(500,500)은 엘리베이터 자리이므로 반드시 투명해야 한다.")]
    [SerializeField] private Texture2D[] layerRoomImages;

    [Tooltip("(선택) 정류장별 테두리 텍스처. 비우거나 null이면 청크 기본 테두리를 유지한다.")]
    [SerializeField] private Texture2D[] layerBorderTextures;

    [Tooltip("페인팅 직후 방에 절차 광물을 생성한다. 지층 tileData의 광물 규칙을 그대로 따른다.")]
    [SerializeField] private bool spawnMinerals = true;

    /// <summary>
    /// 이번 세션에 칠한 방 — 청크 좌표 → layerIndex.
    /// 재로드 시 어느 PNG로 다시 칠할지 복원하는 데 쓴다.
    /// (세션이 바뀌면 그 방은 세이브 기록이 있어 아래 '스킵 2'에 걸리고,
    ///  보호 좌표에도 안 들어가므로 일반 청크로서 절차 광물이 정상 생성된다.)
    /// </summary>
    private readonly Dictionary<Vector2Int, int> _paintedRooms = new Dictionary<Vector2Int, int>();

    private void OnDestroy()
    {
        if (SpecialChunkManager.Instance == null) return;
        foreach (var coord in _paintedRooms.Keys)
            SpecialChunkManager.Instance.UnregisterPostLoadPainter(coord, this);
    }

    /// <summary>
    /// 엘리베이터가 스폰되는 순간 그 좌표를 "내 방"으로 등록만 한다 — 칠하지는 않는다.
    /// <see cref="ElevatorDecorator"/>가 Phase 2 데코 루프의 첫 번째로 호출하므로,
    /// 같은 패스 끝의 <c>ChunkGenerationPipeline.InvokePostLoadPainter</c>가 곧바로
    /// <see cref="OnChunkLoaded"/>를 불러 페인팅 → 광물 순서를 밟는다.
    ///
    /// 이게 있어야 <b>걸어서 도달한 정류장</b>도 방이 생긴다.
    /// (PaintLandingRoom은 텔레포트·지상진입에서만 불려서 착지한 층만 칠했다.)
    ///
    /// 여기서 직접 칠하지 않는 이유: 데코 루프 도중에 픽셀을 덮으면 뒤따르는 Rock·Mineral
    /// 데코레이터가 페인팅 전 지형을 보고 자리를 고른다(규칙 §15와 같은 함정).
    /// 등록만 해두면 보호 좌표가 즉시 서서 그 둘이 아예 스킵되고, 광물은 훅이 페인팅 후에 깐다.
    /// </summary>
    public void RegisterStop(Vector2Int coord, int layerIndex)
    {
        if (_paintedRooms.ContainsKey(coord)) return;          // 이미 내 것 — 훅이 이어서 관리한다
        if (GetAt(layerRoomImages, layerIndex) == null) return; // 그 지층은 기본 지형 그대로 둔다

        var scm = SpecialChunkManager.Instance;
        if (scm == null) return;

        // 다른 컴포넌트가 소유한 보호 좌표(구멍 입구 방 등)는 침범하지 않는다.
        if (scm.IsProtectedCoord(coord)) return;

        scm.RegisterProtectedCoord(coord);
        scm.RegisterPostLoadPainter(coord, this);
        _paintedRooms[coord] = layerIndex;
    }

    /// <summary>
    /// 지정한 X·지층의 엘리베이터 청크를 방 PNG로 교체한다.
    /// 호출 시점에 해당 청크가 이미 로드돼 있어야 한다.
    ///
    /// 정류장 대부분은 <see cref="RegisterStop"/> 경로로 로드 시점에 이미 칠해져 스킵 1에 걸린다.
    /// 페인터 등록이 없는 예외 경로(엘리베이터 데코가 돌기 전 착지 등)의 안전망으로 남겨둔다.
    /// </summary>
    public void PaintLandingRoom(int xChunk, int layerIndex)
    {
        Texture2D room = GetAt(layerRoomImages, layerIndex);
        if (room == null) return;   // 해당 지층은 기본 지형 그대로 둔다

        var elevatorMgr = ElevatorManager.Instance;
        var mapMgr      = InfinityMapManager.Instance;
        if (elevatorMgr == null || mapMgr == null) return;

        if (layerIndex < 0 || layerIndex >= elevatorMgr.layers.Count)
        {
            Debug.LogError($"[ElevatorRoomPainter] 잘못된 layerIndex: {layerIndex}");
            return;
        }

        Vector2Int coord = new Vector2Int(xChunk, elevatorMgr.layers[layerIndex].startDepth);

        // 스킵 1: 다른 컴포넌트가 소유한 보호 좌표.
        // 0층(땅 상층) 정류장은 깊이 0이라 구멍 입구 (0,0)과 X가 겹칠 수 있다.
        // 이 검사가 입구 방을 0층 방 PNG로 덮어쓰는 것을 막는다.
        // (x=5,10,... 의 0층 엘리베이터는 보호 좌표가 아니므로 정상적으로 칠해진다.)
        //
        // 내가 이미 칠한 방(=재방문)도 여기 걸린다. 그 좌표는 훅이 이어서 관리하므로 그대로 두면 된다.
        if (SpecialChunkManager.Instance != null &&
            SpecialChunkManager.Instance.IsProtectedCoord(coord))
            return;

        // 스킵 2: 이미 저장 기록이 있는 청크 = 플레이어가 다녀간 방.
        // 다시 칠하면 판 흔적이 지워지므로 건드리지 않는다.
        // (이 경우 보호 좌표로도 등록하지 않는다 → 일반 청크로서 절차 데코가 정상 동작)
        if (mapMgr.IsChunkVisited(coord)) return;

        var chunk = mapMgr.GetChunk(coord);
        if (chunk == null)
        {
            Debug.LogWarning($"[ElevatorRoomPainter] 청크 {coord}가 로드돼 있지 않아 방을 칠하지 못했다.");
            return;
        }

        if (!ChunkImagePainter.Paint(chunk, room, GetAt(layerBorderTextures, layerIndex)))
            return;

        // 이후 재로드 시 절차 바위·광물이 방 안에 깔리지 않도록 보호 좌표로 등록한다.
        // 광물은 대신 아래 훅이 "페인팅 후" 시점에 직접 깐다.
        if (SpecialChunkManager.Instance != null)
        {
            SpecialChunkManager.Instance.RegisterProtectedCoord(coord);
            SpecialChunkManager.Instance.RegisterPostLoadPainter(coord, this);
        }
        _paintedRooms[coord] = layerIndex;

        // 첫 페인팅은 청크가 이미 로드·데코된 뒤(텔레포트 도중)라 훅이 불리지 않는다 → 여기서 직접 호출.
        // Phase 2의 자식 정리 루프가 이미 지난 시점이라 방금 깐 광물이 쓸려갈 위험도 없다.
        // 이 호출은 방을 칠하기 전에 절차 데코가 깔아둔 광물(이제 방 공동에 떠 있다)을
        // 풀 반납하고 새 픽셀 기준으로 다시 까는 역할도 겸한다.
        if (spawnMinerals)
            mapMgr.SpawnMineralsInChunk(coord);

        Debug.Log($"[ElevatorRoomPainter] 착지 방 페인팅 완료 — 청크 {coord}, layer {layerIndex}, 이미지 '{room.name}'");
    }

    /// <summary>
    /// 청크 로드 파이프라인(Phase 2)이 매 로드마다 호출한다 — 언로드 후 재방문 경로.
    ///
    /// [순서 필수] 페인팅 → 광물. 자세한 이유는 IChunkPostLoadPainter 주석 참고.
    /// </summary>
    public void OnChunkLoaded(TerrainChunk chunk, Vector2Int coord)
    {
        if (!_paintedRooms.TryGetValue(coord, out int layerIndex)) return;

        var mapMgr = InfinityMapManager.Instance;

        // 세이브 기록이 있으면 그쪽이 판 흔적까지 담은 최신 상태다 → 다시 칠하지 않는다.
        // 기록이 없으면 절차 지형으로 새로 생성된 것이므로 방을 다시 칠해야 한다.
        if (mapMgr == null || !mapMgr.IsChunkVisited(coord))
        {
            Texture2D room = GetAt(layerRoomImages, layerIndex);
            if (room != null &&
                !ChunkImagePainter.Paint(chunk, room, GetAt(layerBorderTextures, layerIndex)))
                return; // 실패 원인은 Painter가 로그로 남긴다
        }

        if (spawnMinerals && mapMgr != null)
            mapMgr.SpawnMineralsInChunk(coord);
    }

    /// <summary>배열이 비어 있거나 인덱스를 벗어나면 null을 돌려준다.</summary>
    private static Texture2D GetAt(Texture2D[] arr, int index)
        => (arr != null && index >= 0 && index < arr.Length) ? arr[index] : null;
}
