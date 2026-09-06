// @tags: elevator, layout, column, random, jitter, spawn
using UnityEngine;

/// <summary>
/// 엘리베이터 정류장의 <b>청크 X 좌표 단일 원천</b>.
///
/// <b>층(Y)마다 엘리베이터는 정확히 하나</b>다. 예전엔 X가 <c>offsetX + interval*n</c>인
/// 기둥이 무한히 서서 같은 높이에 엘리베이터가 여러 개였는데, 지금은 층당 1개만 두고
/// 그 X를 <see cref="MinX"/>~<see cref="MaxX"/> 안에서 층마다 다르게 뽑는다.
/// 그래서 아래로 내려갈수록 엘리베이터가 좌우로 지그재그로 어긋난다.
///
/// X는 <see cref="UnityEngine.Random"/>이 아니라 <b>(층 인덱스, <see cref="Seed"/>) 해시</b>로 뽑는다.
/// 청크는 플레이어가 멀어지면 언로드되고 다시 로드되므로, 매번 같은 값이 나오지 않으면
/// 엘리베이터가 재로드 때마다 다른 자리에 생긴다. 그래서 난수는 <b>잠수 시작 때 딱 한 번</b>
/// 밖에서 뽑아 <see cref="Seed"/>로 받고, 여기서는 그 값을 섞기만 한다.
///
/// <c>worldSettings.json</c>의 seed(worldSeed)를 안 쓰는 이유: 그건 0이면 세션마다 새로 뽑히고
/// 세이브에 남지 않아서, 잠수 도중 게임을 다시 열면 엘리베이터가 통째로 옮겨간다.
/// 게다가 이 계산은 <b>지상 씬에서도</b> 필요한데(지상 엘리베이터 입구 → 지하 착지 X),
/// 지상엔 InfinityMapManager도 ElevatorManager도 없다.
///
/// 그래서 이 클래스는 매니저·시드에 의존하지 않는 <b>순수 함수</b>로만 구성한다.
/// </summary>
public static class ElevatorStopLayout
{
    /// <summary>
    /// 정류장이 놓일 수 있는 청크 X 범위(양 끝 포함).
    ///
    /// 층당 엘리베이터가 하나뿐이라 이 범위가 곧 <b>"그 층에서 엘리베이터를 찾으러 걸어야 하는
    /// 최대 거리"</b>다. 넓히면 탐색이 길어지고, 좁히면 층마다 X가 겹쳐 지그재그가 사라진다.
    /// 플레이어의 지하 시작 청크는 (0,0)이므로 0을 가운데 두고 좌우로 잡는다.
    /// (청크 1칸 = 10유닛 → ±5청크 = ±50유닛)
    ///
    /// 상수인 이유: 지상·지하 양쪽이 같은 값을 봐야 착지 X가 일치하는데,
    /// 지상 씬에는 인스펙터를 들고 있을 ElevatorManager가 없다.
    /// </summary>
    public const int MinX = -5;
    public const int MaxX =  5;

    /// <summary>
    /// 위아래로 이웃한 층끼리 최소 몇 청크는 떨어뜨릴지.
    /// 0이면 연속한 두 층이 같은 X에 걸릴 수 있어 "어긋난다"는 느낌이 사라진다.
    /// 범위 폭보다 크게 잡으면 후보가 없어지므로 <see cref="MinSeparation"/> * 2 &lt; 범위 폭 이어야 한다.
    /// </summary>
    public const int MinSeparation = 2;

    /// <summary>
    /// 이번 잠수의 배치 시드. <b>땅에 들어갈 때마다</b> 엘리베이터 X가 달라지게 한다.
    ///
    /// <c>SaveManager.PrepareUndergroundEntry</c>가 지하 진입 때 새로 뽑아 넣고(그 자리에서
    /// PlayerData에 저장), <c>SaveManager.Load</c>가 저장된 값을 되살린다.
    /// <b>잠수 도중에는 절대 바뀌면 안 된다</b> — 청크는 멀어지면 언로드됐다 다시 로드되는데
    /// 그때 값이 달라지면 엘리베이터가 순간이동하고, 지상에서 고른 착지 좌표와도 어긋난다.
    /// 그래서 여기서 난수를 뽑지 않고 밖에서 한 번 정해진 값을 받기만 한다.
    ///
    /// 0이면 시드 없는 기본 배치(뉴게임 직후, 아직 한 번도 안 내려간 상태).
    /// </summary>
    public static int Seed
    {
        get => _seed;
        set
        {
            if (_seed == value) return;
            _seed = value;
            _stopCacheValid = false;   // 시드가 바뀌면 정류장 좌표가 통째로 바뀐다
        }
    }
    private static int _seed;

    /// <summary>
    /// 플레이어가 지하로 처음 떨어지는 <b>구멍 입구 청크</b>의 좌표.
    /// 그 자리는 <c>ImageChunkOverrider</c>가 PNG로 칠하는 보호 좌표라, 엘리베이터가 겹치면
    /// 승강로로 뚫어 놓은 픽셀을 페인팅이 덮어버린다(엘리베이터만 지형에 박힌 채 남는다).
    /// 그래서 그 층에서는 이 X를 후보에서 뺀다.
    /// (구멍 입구는 청크 (0,0) — Y=0은 ElevatorLayerCatalog의 첫 정류장 '땅 상층'이다.)
    /// </summary>
    private const int EntranceX          = 0;
    private const int EntranceLayerIndex = 0;

    // 해시 상수 — RockLayoutCalculator와 같은 계열(큰 소수).
    private const int HASH_L = 19349663;
    private const int SALT   = 0x1F3A5C7D;

    /// <summary>
    /// 그 층 정류장의 청크 X. 스폰 판정·층 이동·지상 진입이 모두 이걸 통해야 좌표가 일치한다.
    ///
    /// 이웃 층과 떨어뜨리는 규칙 때문에 층 0부터 순서대로 계산한다(정류장은 12개뿐이라 값싸다).
    /// </summary>
    /// <param name="layerIndex">정류장 인덱스 (ElevatorLayerCatalog.Build 순서)</param>
    public static int XForLayer(int layerIndex)
    {
        if (layerIndex < 0) return MinX;

        int prev = int.MinValue;   // 층 0은 제약 없음
        int x    = MinX;

        for (int l = 0; l <= layerIndex; l++)
        {
            x = Pick(l, prev);
            prev = x;
        }

        return x;
    }

    /// <summary>
    /// 한 층의 X를 고른다 — [MinX, MaxX] 중 <paramref name="prev"/>에서 MinSeparation 이상
    /// 떨어진 후보만 대상으로, 층 해시로 결정적으로 하나 뽑는다.
    /// </summary>
    private static int Pick(int layerIndex, int prev)
    {
        // 후보 개수를 먼저 센다(리스트를 만들지 않아 GC 할당 없음).
        int count = 0;
        for (int x = MinX; x <= MaxX; x++)
            if (IsAllowed(x, prev, layerIndex)) count++;

        // 범위가 너무 좁아 후보가 없으면 이웃 층 제약을 풀어서라도 자리를 준다(정류장 누락 방지).
        // 구멍 입구 제외는 지형이 덮이는 문제라 이때도 유지한다.
        if (count == 0)
        {
            prev = int.MinValue;
            for (int x = MinX; x <= MaxX; x++)
                if (IsAllowed(x, prev, layerIndex)) count++;
        }

        int pick = Hash(layerIndex) % count;

        for (int x = MinX; x <= MaxX; x++)
        {
            if (!IsAllowed(x, prev, layerIndex)) continue;
            if (pick == 0) return x;
            pick--;
        }

        return MinX;   // 도달 불가 — 위 루프가 반드시 반환한다
    }

    private static bool IsAllowed(int x, int prev, int layerIndex)
    {
        if (layerIndex == EntranceLayerIndex && x == EntranceX) return false;
        return prev == int.MinValue || Mathf.Abs(x - prev) >= MinSeparation;
    }

    /// <summary>층 인덱스 → 0 이상의 결정적 해시.</summary>
    private static int Hash(int layerIndex)
    {
        // layerIndex가 0이면 곱이 0이 되어 층이 섞이지 않으므로 +1.
        int h = ((layerIndex + 1) * HASH_L) ^ SALT ^ Seed;

        // 하위 비트만 쓰면 층 번호가 그대로 비쳐 패턴이 반복된다 — 섞어서 흩뿌린다.
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;

        return h & 0x7FFFFFFF;
    }

    // ─── 정류장 좌표 조회 ────────────────────────────────────────────────────

    /// <summary>
    /// 이번 잠수의 정류장 청크 좌표 전체(층당 1개). <see cref="IsStopCoord"/> 전용 캐시.
    /// 층은 많아야 12개라 배열 선형 탐색이 HashSet보다 싸다(GC 할당 0).
    /// </summary>
    private static Vector2Int[] _stopCoords = System.Array.Empty<Vector2Int>();
    private static bool _stopCacheValid;

    /// <summary>
    /// 그 좌표가 어느 층의 정류장인지.
    ///
    /// <b>특수청크 선택이 정류장을 피하려고 쓴다</b>(<c>SpecialChunkSelector.TrySelect</c>).
    /// 특수청크 당첨은 청크 생성 1-B에서 결정되고 엘리베이터는 한참 뒤 Phase 2 데코에서 생기는데,
    /// 특수청크는 일반 데코 경로를 아예 안 타므로 정류장 좌표가 당첨되면 그 층 엘리베이터가
    /// <b>조용히 통째로 사라진다</b>(대형 앵커의 서브좌표에 걸리면 IsBlocked로 더 조용히).
    ///
    /// 반대로 엘리베이터가 특수청크를 피해 X를 옮기게 만들 수는 없다 — 이 계산은 지상 씬에서도
    /// 필요한데(지상 입구 → 지하 착지 X) 지상엔 SpecialChunkManager가 없다.
    /// </summary>
    public static bool IsStopCoord(Vector2Int coord)
    {
        EnsureStopCache();

        for (int i = 0; i < _stopCoords.Length; i++)
            if (_stopCoords[i] == coord) return true;

        return false;
    }

    /// <summary>정류장 좌표 캐시를 강제로 버린다(지층 정의가 런타임에 바뀐 경우 등).</summary>
    public static void InvalidateStopCache() => _stopCacheValid = false;

    private static void EnsureStopCache()
    {
        if (_stopCacheValid) return;

        var layers = ElevatorLayerCatalog.Build();
        if (_stopCoords.Length != layers.Count)
            _stopCoords = new Vector2Int[layers.Count];

        for (int i = 0; i < layers.Count; i++)
            _stopCoords[i] = new Vector2Int(XForLayer(i), layers[i].startDepth);

        // TileDataManager가 아직 없으면 카탈로그가 fallback 깊이를 쓴다 — 그 값을 굳히면
        // 매니저가 뜬 뒤에도 틀린 깊이가 남는다. 준비됐을 때만 캐시를 확정한다.
        _stopCacheValid = TileDataManager.Instance != null;
    }
}
