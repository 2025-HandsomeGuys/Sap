// @tags: background, chunk, layer, sprite, renderer, terrain, depth
using UnityEngine;

/// <summary>
/// 청크 1개의 배경 렌더러. TerrainChunk가 소유하며 자식 GameObject 하나를 관리한다.
/// TerrainVisualizer / TerrainCollider와 같은 분리 패턴.
///
/// 설계 문서: Assets/Docs/chunk-background-system.md
/// </summary>
public class ChunkBackground
{
    /// <summary>
    /// 층별 배경 테이블. static인 이유는 Instantiate가 non-serialized 필드를 프리팹에서
    /// 복사하지 않기 때문 — TerrainChunk.s_colliderUpdateInterval과 같은 패턴.
    /// InfinityMapManager.Awake에서 1회 주입한다.
    /// </summary>
    public static ChunkBackgroundTableSO Table { get; set; }

    /// <summary>
    /// 배경 자식 GameObject 이름. ChunkSpawner의 자식 정리 루프가 이 이름을 보고 건너뛴다 —
    /// 그 루프는 ROCK_/MINERAL_ 외 자식을 전부 Destroy하는데, 배경은 TerrainChunk.Awake에서
    /// 1회만 생성되므로 파괴되면 그 풀 인스턴스는 영영 배경이 없다.
    /// </summary>
    public const string ChildName = "Background";

    /// <summary>
    /// 벽타기 판정면의 상한 월드 Y. 이 Y 위로는 트리거를 만들지 않는다(지상에서 공중 벽타기 방지).
    /// 구 BackgroundManager의 ceilingLevel(씬 값 9)을 그대로 옮긴 것.
    /// Table과 같은 이유로 static — InfinityMapManager.Awake에서 주입한다.
    /// </summary>
    public static float WallCeilingY { get; set; } = 9f;

    // 청크 1칸 = 10유닛. 청크 스프라이트 pivot이 (0,0)이라 청크 transform = 좌하단 모서리이므로
    // 배경(pivot=Center)을 청크 중앙에 놓으려면 절반만큼 오프셋한다.
    private const float ChunkWorldSize = 10f;
    // 지하 정렬 계획: 청크 배경 -100 < 특수청크 배경 -99 < 플레이어 -49~-30 < 돌·광물 -1 < 지형 0.
    // 플레이어가 지형 뒤로 들어가려면(PlayerSortingController) 배경과 지형 사이에 대역이 필요해서
    // 기존 -3에서 내렸다. 특수청크 프리팹의 배경(-99)보다는 뒤여야 한다.
    private const int BackgroundSortingOrder = -100;

    // 플레이어 PlayerController.wallLayer가 이 레이어만 본다(NewPlayer.prefab: m_Bits = 1 << 21).
    private const string WallLayerName = "Wall";

    private readonly SpriteRenderer _renderer;

    // 벽타기 판정용 트리거. 구 BackgroundTile.prefab이 들고 있던 1x1 트리거를 청크 단위로 합친 것.
    // PlayerController.HandleWallClimbing은 bodyCollider.IsTouchingLayers(wallLayer)로만 판정하므로
    // 배경 스프라이트 유무와 무관하게 살아 있어야 한다 → Refresh의 스프라이트 early-return보다 먼저 갱신한다.
    private readonly BoxCollider2D _wallTrigger;

    /// <summary>배경 렌더러의 GameObject. XRayController가 배경 톤을 따로 입힐 때 쓴다.</summary>
    public GameObject GameObject { get; }

    public ChunkBackground(Transform chunkRoot)
    {
        var go = new GameObject(ChildName);
        GameObject = go;
        go.transform.SetParent(chunkRoot, false);
        go.transform.localPosition = new Vector3(ChunkWorldSize * 0.5f, ChunkWorldSize * 0.5f, 0f);
        go.transform.localRotation = Quaternion.identity;

        _renderer = go.AddComponent<SpriteRenderer>();
        _renderer.sortingOrder = BackgroundSortingOrder; // 지형(order 0)보다 뒤
        _renderer.enabled = false;                       // 스프라이트 할당 전까지 숨김

        // 벽타기 판정면. 레이어는 렌더링에 영향이 없다(카메라 컬링 마스크 = Everything).
        int wallLayer = LayerMask.NameToLayer(WallLayerName);
        if (wallLayer >= 0)
        {
            go.layer = wallLayer;
            _wallTrigger = go.AddComponent<BoxCollider2D>();
            _wallTrigger.isTrigger = true; // 이동을 막으면 안 된다 — 구 BackgroundTile과 동일
            _wallTrigger.enabled = false;  // 크기는 Refresh에서 청크 Y에 맞춰 정한다
        }
        else
        {
            LogOnce("wall-layer-missing",
                $"'{WallLayerName}' 레이어가 없다 → 벽타기 판정면을 만들지 못했다. " +
                "Project Settings > Tags & Layers에서 레이어 21을 확인할 것.");
        }

        LogOnce("created", $"배경 자식 생성됨 (부모='{chunkRoot.name}'). Refresh 로그가 뒤따라야 정상.");
    }

    /// <summary>
    /// 청크 좌표로 층을 판정하고 배경 스프라이트를 결정론적으로 고른다.
    /// 같은 좌표는 항상 같은 배경 → 언로드 후 재로드해도 바뀌지 않는다.
    ///
    /// 반드시 Reuse_Step2_Finalize에서 호출할 것. Reuse_Step1_Prepare는
    /// StandardChunkFactory에서만 불려 특수 청크가 누락된다.
    /// </summary>
    public void Refresh(int chunkX, int chunkY)
    {
        // 스프라이트가 없는 층에서도 벽타기는 되어야 하므로 아래 early-return들보다 먼저 갱신한다.
        UpdateWallTrigger(chunkY);

        if (_renderer == null) { LogOnce("renderer-null", "SpriteRenderer가 null이다."); return; }

        if (Table == null)
        {
            _renderer.enabled = false;
            LogOnce("table-null",
                "Table이 null이다 → InfinityMapManager 인스펙터의 chunkBackgroundTable 슬롯이 비었거나, " +
                "청크가 InfinityMapManager.Awake보다 먼저 생성됐다.");
            return;
        }

        if (TileDataManager.Instance == null)
        {
            _renderer.enabled = false;
            LogOnce("tiledata-null",
                "TileDataManager.Instance가 null이다 → 층 판정 불가. 스크립트 실행 순서 문제.");
            return;
        }

        TileType layer = TileDataManager.Instance.GetTileTypeAtDepth(chunkY);
        Sprite[] candidates = Table.GetSprites(layer);

        // 해당 층 배경이 아직 없으면 조용히 숨긴다(에러 아님).
        if (candidates == null)
        {
            _renderer.enabled = false;
            LogOnce($"no-sprites-{layer}",
                $"층 '{layer}'(청크Y={chunkY})에 스프라이트가 없다 → SO의 해당 엔트리가 비었거나 엔트리 자체가 없다.");
            return;
        }

        int seed = (chunkX * 73856093) ^ (chunkY * 19349663);
        var prng = new System.Random(seed);
        Sprite chosen = candidates[prng.Next(0, candidates.Length)];

        if (chosen == null)
        {
            _renderer.enabled = false;
            LogOnce($"null-entry-{layer}", $"층 '{layer}' 스프라이트 배열에 빈 슬롯(None)이 있다.");
            return;
        }

        // 스프라이트를 먼저 넣어야 drawMode 변경 시 Unity 경고가 안 뜬다.
        _renderer.sprite = chosen;

        // Tiled 드로우 모드는 스프라이트 Mesh Type이 Full Rect여야 동작한다.
        // 기본값 Tight면 Unity가 경고만 내고 제대로 그리지 않는다 — 값은 다 정상인데
        // 화면에 아무것도 안 보이는 전형적 원인. Full Rect는 삼각형 2개(=인덱스 6개)다.
        bool isFullRect = chosen.triangles.Length == 6;

        if (isFullRect)
        {
            _renderer.drawMode = SpriteDrawMode.Tiled;
            _renderer.size = new Vector2(ChunkWorldSize, ChunkWorldSize);
            _renderer.transform.localScale = Vector3.one;
        }
        else
        {
            // 폴백: Simple + 스케일로 청크를 채운다. 타일 반복은 안 되고 늘어난다.
            _renderer.drawMode = SpriteDrawMode.Simple;
            Vector2 b = chosen.bounds.size;
            _renderer.transform.localScale = new Vector3(
                b.x > 0f ? ChunkWorldSize / b.x : 1f,
                b.y > 0f ? ChunkWorldSize / b.y : 1f,
                1f);

            LogOnce($"not-fullrect-{chosen.name}",
                $"스프라이트 '{chosen.name}'의 Mesh Type이 Full Rect가 아니다(Tight로 추정).\n" +
                $"  Tiled 드로우 모드가 동작하지 않아 Simple+스케일로 폴백했다(타일 반복 없이 늘어남).\n" +
                $"  → 해결: 해당 스프라이트 임포트 설정에서 Mesh Type을 'Full Rect'로 바꾸고 Apply.");
        }

        _renderer.enabled = true;

        LogOnce("success",
            $"배경 표시 성공: 층={layer} 청크=({chunkX},{chunkY}) 스프라이트='{chosen.name}'\n" +
            $"  월드위치={_renderer.transform.position} sortingLayer='{SortingLayer.IDToName(_renderer.sortingLayerID)}' " +
            $"order={_renderer.sortingOrder} drawMode={_renderer.drawMode} fullRect={isFullRect}\n" +
            $"  스프라이트크기={chosen.bounds.size} (청크는 10x10 유닛)\n" +
            $"  → 이 로그가 보이는데도 화면에 없으면: ① 씬의 기존 BackgroundManager가 같은 order(-3)로 덮고 있거나 " +
            $"② 지형이 안 파여서 불투명하게 가리고 있는 것이다.");
    }

    /// <summary>
    /// 벽타기 트리거를 청크 Y에 맞춰 크기·위치 조정한다.
    /// 자식이 청크 중앙(로컬 5,5)에 있으므로 WallCeilingY까지만 덮도록 박스를 아래로 깎는다.
    /// 예) ceiling=9, 청크Y=0(월드 0~10) → 높이 9, offset.y=-0.5 → 월드 0~9만 덮음.
    /// </summary>
    private void UpdateWallTrigger(int chunkY)
    {
        if (_wallTrigger == null) return;

        float bottom = chunkY * ChunkWorldSize;
        float height = Mathf.Min(bottom + ChunkWorldSize, WallCeilingY) - bottom;

        if (height <= 0f)
        {
            _wallTrigger.enabled = false; // 천장보다 위 — 지상 청크
            return;
        }

        _wallTrigger.size = new Vector2(ChunkWorldSize, height);
        _wallTrigger.offset = new Vector2(0f, height * 0.5f - ChunkWorldSize * 0.5f);
        _wallTrigger.enabled = true;
    }

    // ── 진단 로그 ────────────────────────────────────────────────
    // 청크가 수백 개라 매번 찍으면 콘솔이 폭발한다. 원인(키)별로 1회만 찍는다.
    // 배경이 정상 동작하는 것을 확인한 뒤 EnableDiagnostics를 false로 두면 된다.

    /// <summary>진단 로그 on/off. 문제 해결 후 false로.</summary>
    public static bool EnableDiagnostics = true;

    private static readonly System.Collections.Generic.HashSet<string> s_loggedKeys
        = new System.Collections.Generic.HashSet<string>();

    /// <summary>씬 재시작 시 로그를 다시 볼 수 있도록 초기화한다.</summary>
    public static void ResetDiagnostics() => s_loggedKeys.Clear();

    private static void LogOnce(string key, string message)
    {
        if (!EnableDiagnostics) return;
        if (!s_loggedKeys.Add(key)) return;
        Debug.Log($"[ChunkBackground] {message}");
    }
}
