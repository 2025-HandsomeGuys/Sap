// @tags: special-chunk, cavity, reveal, diggable, carve, chunk, hidden
using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;

/// <summary>
/// 땅에 배치되는 단일 특수청크의 **가운데 공동(cavity)** 만 "돌처럼" 처리한다.
///
/// - 스폰 시엔 청크 전체를 주변 지층 지형(groundPixels)으로 채워 **묻힌 상태**로 시작한다.
///   (공동이 평범한 흙/돌로 감춰져 주변 지형과 이어짐)
/// - 플레이어가 주변(벽)을 파서 **공동 경계가 air와 맞닿으면** 공동만 air로 carve(노출)한다.
///   돌(DiggableRock)이 주변을 파면 드러나는 것과 동일한 컨셉.
///
/// 핵심: 노출 시 **공동 픽셀만** air로 바꾼다. 벽(주변 지형)은 절대 건드리지 않는다.
/// (벽을 다시 solid로 복원하면 플레이어가 파둔 터널이 메워지며 밀려나는 충돌이 발생하므로.)
///
/// 동작 원리:
/// - IChunkInitializer로서 다른 초기화(SpriteCavityInitializer 등)가 완료된 뒤
///   (InitializationOrder = 100) 실행되어, 그 결과의 **air 픽셀 = 공동**을 마스크로 잡고 묻는다.
/// - 노출 판정은 TerrainChunk.Dig()/Explode() 및 재로드 파이프라인에서 호출된다.
///
/// 전제:
/// - 스프라이트 1000×1000, 청크와 1:1. 공동(투명 영역)은 solid 여백으로 둘러싸여 가장자리 미접촉.
/// - 이 컴포넌트가 프리팹에 부착돼 있으면 = "묻혔다가 파면 드러나는" 모드. 없으면 기존 즉시 노출.
/// </summary>
[RequireComponent(typeof(TerrainChunk))]
public class CavityRevealController : MonoBehaviour, IChunkInitializer
{
    [Tooltip("이 픽셀 수 이상의 공동 경계가 파인 air와 맞닿아야 노출된다. (돌의 minExposedPixels와 동일 개념)")]
    [SerializeField] private int minExposedPixels = 12;

    // SpriteCavityInitializer(0) 등 이후에 실행되어 그 결과(air=공동)를 마스크로 잡는다.
    public int InitializationOrder => 100;

    private TerrainChunk _chunk;
    private bool _isRevealed;
    private bool _buried;

    // 공동(air) 마스크: 초기 주입 상태에서 air였던 픽셀 = true. 노출 시 이 픽셀들만 air로 carve.
    private bool[] _cavityMask;
    // 공동 경계 픽셀 인덱스(공동 픽셀 중 상하좌우 하나라도 비공동인 것). 노출 판정 대상.
    private int[] _cavityEdge;

    // 묻힘 동안 숨긴 자식 렌더러/콜라이더 (노출 시 복원). 루트 TerrainChunk 것은 제외.
    private readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
    private readonly List<Collider2D> _hiddenColliders = new List<Collider2D>();

    public bool IsRevealed => _isRevealed;

    // ============================================================================================================
    //  INIT — 공동 마스크 잡고 묻기
    // ============================================================================================================
    public void Initialize(Transform parent)
    {
        _chunk = GetComponent<TerrainChunk>();
        if (_chunk == null)
        {
            Debug.LogError($"[CavityRevealController] TerrainChunk를 찾을 수 없습니다 — {name}");
            return;
        }

        var data = _chunk.GetData();
        if (data == null)
        {
            Debug.LogError($"[CavityRevealController] ChunkData가 null입니다 — {name}");
            return;
        }

        int w = _chunk.width, h = _chunk.height;
        int n = w * h;
        if (data.BasePixels.Length != n)
        {
            Debug.LogError($"[CavityRevealController] BasePixels 크기 불일치 — {name}");
            return;
        }

        _chunk.EnsureJobsCompleted();

        // 1. 공동 마스크 = 현재 air 픽셀 (SpriteCavityInitializer가 투명→air로 주입한 결과)
        _cavityMask = new bool[n];
        for (int i = 0; i < n; i++)
            _cavityMask[i] = data.BasePixels[i].a == 0;
        BuildCavityEdge(w, h);

        // 2. 묻기: 청크 전체를 주변 지층 지형으로 채움 (공동 감춤 + 주변과 이어짐)
        Bury(data, w, h);

        _buried = true;
        _isRevealed = false;

        // 3. TerrainChunk에 등록 (Dig/Explode·재로드 훅에서 호출)
        _chunk.RegisterCavityReveal(this);

        // 4. 공동은 나중에 carve될 자리 → 광물 스폰 금지 영역으로 등록.
        //    (묻힌 동안은 solid라 MineralGenerator가 지형으로 오인해 공동 안에 광물을 박는다)
        _chunk.SetCarveReservedMask(_cavityMask);
    }

    private void BuildCavityEdge(int w, int h)
    {
        var edge = new List<int>();
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int idx = row + x;
                if (!_cavityMask[idx]) continue;
                bool boundary =
                    (x > 0     && !_cavityMask[idx - 1]) ||
                    (x < w - 1 && !_cavityMask[idx + 1]) ||
                    (y > 0     && !_cavityMask[idx - w]) ||
                    (y < h - 1 && !_cavityMask[idx + w]);
                if (boundary) edge.Add(idx);
            }
        }
        _cavityEdge = edge.ToArray();
    }

    private void Bury(ChunkData data, int w, int h)
    {
        int n = w * h;
        Color32[] ground = null;
        var tdm = TileDataManager.Instance;
        if (tdm != null)
        {
            TileType layer = tdm.GetTileTypeAtPosition(_chunk.ChunkX, _chunk.ChunkY);
            ground = tdm.GetGroundPixels(layer);
        }

        if (ground != null && ground.Length == n)
        {
            // 정상 경로: 청크 전체를 주변 지층과 픽셀 단위로 동일하게 채움
            for (int i = 0; i < n; i++)
            {
                Color32 c = ground[i];
                data.BasePixels[i] = c;
                data.PixelInfo[i] = (byte)(c.a == 0 ? 0 : 1);
            }
        }
        else
        {
            // 폴백: groundPixels 없음/크기 불일치 → 공동 자리만 solid로 메워 최소한 구멍은 숨김
            Debug.LogWarning($"[CavityRevealController] groundPixels 사용 불가 — 폴백(공동만 채움) ({name})");
            Color32 fill = new Color32(90, 70, 55, 255);
            for (int i = 0; i < n; i++)
            {
                if (_cavityMask[i])
                {
                    data.BasePixels[i] = fill;
                    data.PixelInfo[i] = 1;
                }
            }
        }

        // 묻힘 동안 파괴불가 마스크 해제 (평범한 흙이 되도록 — 전부 파기 가능)
        if (data.IndestructibleMask.IsCreated && data.IndestructibleMask.Length == n)
            for (int i = 0; i < n; i++) data.IndestructibleMask[i] = 0;
        data.HasIndestructiblePixels = false;

        // 특수 구조물 비주얼/콜라이더 숨김 (자식만 — 루트 TerrainChunk 렌더러/콜라이더는 유지)
        HideSpecialVisuals();

        // 재로드 시 저장된 파진 상태(RestoreSavedPixels)가 덮어쓸 수 있도록 dirty만 예약.
        data.MarkRenderDirty();
        _chunk.isTextureDirty = true;
    }

    private void HideSpecialVisuals()
    {
        _hiddenRenderers.Clear();
        _hiddenColliders.Clear();

        foreach (var rd in GetComponentsInChildren<Renderer>(true))
        {
            if (rd.gameObject == gameObject) continue; // 루트 TerrainChunk 렌더러 제외
            if (!rd.enabled) continue;
            rd.enabled = false;
            _hiddenRenderers.Add(rd);
        }
        foreach (var col in GetComponentsInChildren<Collider2D>(true))
        {
            if (col.gameObject == gameObject) continue; // 루트 콜라이더 제외
            if (!col.enabled) continue;
            col.enabled = false;
            _hiddenColliders.Add(col);
        }
    }

    private void RestoreSpecialVisuals()
    {
        foreach (var rd in _hiddenRenderers) if (rd != null) rd.enabled = true;
        foreach (var col in _hiddenColliders) if (col != null) col.enabled = true;
        _hiddenRenderers.Clear();
        _hiddenColliders.Clear();
    }

    // ============================================================================================================
    //  REVEAL — 공동 경계가 air에 맞닿으면 공동만 carve
    // ============================================================================================================
    /// <summary>
    /// 노출 조건을 검사하고 충족되면 **공동만** air로 carve한다. 벽은 건드리지 않는다.
    /// TerrainChunk.Dig()/Explode() 및 재로드 파이프라인에서 호출.
    /// </summary>
    public void RevealIfExposed()
    {
        if (_isRevealed || !_buried || _chunk == null) return;
        if (CountExposedCavityPixels() < minExposedPixels) return;
        Reveal();
    }

    /// <summary>공동 경계 픽셀 중 파인 air(이웃 청크 포함)에 인접한 픽셀 수.</summary>
    private int CountExposedCavityPixels()
    {
        if (_cavityEdge == null || _chunk == null) return 0;
        int w = _chunk.width;
        int exposed = 0;
        for (int e = 0; e < _cavityEdge.Length; e++)
        {
            int idx = _cavityEdge[e];
            int x = idx % w;
            int y = idx / w;
            if (_chunk.IsTransparent(x - 1, y) || _chunk.IsTransparent(x + 1, y) ||
                _chunk.IsTransparent(x, y - 1) || _chunk.IsTransparent(x, y + 1))
            {
                exposed++;
                if (exposed >= minExposedPixels) return exposed; // 조기 종료
            }
        }
        return exposed;
    }

    private void Reveal()
    {
        var data = _chunk.GetData();
        if (data == null) return;

        _chunk.EnsureJobsCompleted();

        // 재로드된 노출 상태(저장 복원)로 공동이 이미 열려 있으면 carve 생략 — 비주얼만 복원.
        bool alreadyOpen = IsCavityMostlyAir(data);

        if (!alreadyOpen)
        {
            // 공동 픽셀만 air로 carve. 벽(주변 지형)은 절대 건드리지 않는다.
            Color32 air = new Color32(0, 0, 0, 0);
            for (int i = 0; i < _cavityMask.Length; i++)
            {
                if (!_cavityMask[i]) continue;
                if (data.BasePixels[i].a == 0) continue; // 이미 air면 skip
                data.BasePixels[i] = air;
                data.PixelInfo[i] = 0;
            }
            data.MarkDirty(); // 저장 포함 → 재로드 시 노출 유지
        }

        RestoreSpecialVisuals();

        _isRevealed = true;
        _buried = false;

        if (!alreadyOpen)
        {
            // 게임플레이 중 노출 — 즉시 전체 비주얼·콜라이더 갱신.
            _chunk.isTextureDirty = true;
            if (_chunk.Visualizer != null)
            {
                _chunk.Visualizer.UpdateVisualsArea(
                    0, 0, _chunk.width, _chunk.height,
                    _chunk.ChunkX, _chunk.ChunkY, _chunk.SyncBoundaryDistanceWithNeighbors);
            }
            data.IsColliderDirty = true;
            _chunk.ForceUpdateCollider();
        }
        else
        {
            // 재로드된 노출 상태 — 픽셀은 이미 저장 복원됨. 파이프라인 비주얼 마무리에 맡긴다.
            data.MarkRenderDirty();
        }

        // 마스크 해제 (재노출 없음). 공동이 열렸으므로 광물 스폰 금지 영역도 해제.
        _chunk.ClearCarveReservedMask();
        _cavityMask = null;
        _cavityEdge = null;
    }

    /// <summary>공동 픽셀의 대부분이 이미 air면 true(재로드된 노출 상태 판별).</summary>
    private bool IsCavityMostlyAir(ChunkData data)
    {
        if (_cavityMask == null) return false;
        int cavity = 0, air = 0;
        for (int i = 0; i < _cavityMask.Length; i++)
        {
            if (!_cavityMask[i]) continue;
            cavity++;
            if (data.BasePixels[i].a == 0) air++;
        }
        if (cavity == 0) return false;
        return air >= cavity * 0.9f;
    }
}
