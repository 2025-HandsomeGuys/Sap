// @tags: chunk, terrain, manager, digging, special-chunk, static
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ITerrainManager 경량 구현체.
/// InfinityMapManager 없이 씬에 수동 배치된 TerrainChunk만으로 땅파기를 지원한다.
/// 특수 청크 테스트 씬(Specialchunk 등)에서 사용.
///
/// [SOLID]
///   SRP: 수동 배치 청크 목록 관리 + ModifyTerrain 라우팅만 담당.
///   OCP: 청크 목록에 추가하는 것만으로 확장 가능.
///   DIP: Digger는 이 클래스가 아닌 ITerrainManager 인터페이스에 의존.
/// </summary>
public class StaticChunkTerrainManager : MonoBehaviour, ITerrainManager
{
    [Header("청크 설정")]
    [Tooltip("씬에 배치된 TerrainChunk 목록. Inspector에서 직접 할당.")]
    public List<TerrainChunk> chunks = new List<TerrainChunk>();

    [Tooltip("청크 월드 높이(유닛). Digger의 깊이 계산용. 기본값: 10 (1000px / 100PPU)")]
    public float chunkHeight = 10f;

    public float chunkHeightWorld => chunkHeight;

    private void Awake()
    {
        // chunks가 비어 있으면 씬에서 자동 수집
        if (chunks.Count == 0)
        {
            chunks.AddRange(FindObjectsByType<TerrainChunk>(FindObjectsSortMode.None));
            Debug.Log($"[StaticChunkTerrainManager] 자동 수집 — {chunks.Count}개 TerrainChunk 등록");
        }
        else
        {
            Debug.Log($"[StaticChunkTerrainManager] 초기화 완료 — {chunks.Count}개 TerrainChunk");
        }
    }

    private void Start()
    {
        Debug.Log($"[StaticChunkTerrainManager] ===== 청크 진단 시작 =====");
        foreach (var chunk in chunks)
        {
            if (chunk == null) { Debug.LogWarning("[StaticChunkTerrainManager] null 청크 발견!"); continue; }

            var data = chunk.GetData();

            // 1. 위치 & 크기
            float chunkW = chunk.width / chunk.pixelsPerUnit;
            float chunkH = chunk.height / chunk.pixelsPerUnit;
            Vector2 chunkMin = chunk.transform.position;
            Vector2 chunkMax = chunkMin + new Vector2(chunkW, chunkH);
            Debug.Log($"[StaticChunkTerrainManager] [{chunk.name}] 위치: {chunkMin} ~ {chunkMax}  크기: {chunkW}x{chunkH} 유닛");

            // 2. 픽셀 데이터 상태
            if (data == null)
            {
                Debug.LogError($"[StaticChunkTerrainManager] [{chunk.name}] ChunkData가 null — 초기화 실패!");
                continue;
            }

            bool baseCreated = data.BasePixels.IsCreated;
            Debug.Log($"[StaticChunkTerrainManager] [{chunk.name}] BasePixels.IsCreated={baseCreated}  HasBeenModified={data.HasBeenModified}");

            if (!baseCreated) continue;

            // 3. 픽셀 내용 샘플링 (전체 순회 대신 균등 샘플 100개)
            int total = data.BasePixels.Length;
            int step  = Mathf.Max(1, total / 100);
            int solidCount = 0, airCount = 0;
            for (int i = 0; i < total; i += step)
                if (data.BasePixels[i].a > 0) solidCount++; else airCount++;

            float solidPct = solidCount * 100f / (solidCount + airCount);
            Debug.Log($"[StaticChunkTerrainManager] [{chunk.name}] 픽셀 샘플(100개) — solid:{solidCount} air:{airCount}  solid비율≈{solidPct:F1}%");

            if (solidCount == 0)
                Debug.LogWarning($"[StaticChunkTerrainManager] [{chunk.name}] ⚠ 모든 샘플이 air — 이미지가 적용되지 않았거나 전체 투명!");
            else if (airCount == 0)
                Debug.LogWarning($"[StaticChunkTerrainManager] [{chunk.name}] ⚠ 모든 샘플이 solid — 이미지가 흰 사각형이거나 구멍이 없음!");
        }
        Debug.Log($"[StaticChunkTerrainManager] ===== 청크 진단 완료 =====");
    }

    /// <summary>
    /// InfinityMapManager가 없는 씬에서 ExplodeTerrain/Dig 결과가 화면에 반영되도록
    /// LateUpdate에서 IsVisualDirty인 청크의 ApplyTexture()를 직접 호출한다.
    /// </summary>
    private void LateUpdate()
    {
        foreach (var chunk in chunks)
        {
            if (chunk == null) continue;
            var data = chunk.GetData();
            if (data != null && data.IsVisualDirty)
                chunk.ApplyTexture();
        }
    }

    public void ModifyTerrain(Vector2 worldPos, float radius, int toolIndex, bool applySmoothing = true)
    {
        // 삽 마스크가 radius*2를 넘어 뻗으면 그 너머 청크가 호출되지 않아 경계에서 잘린다.
        // 삽이 아닌 도구는 영향 0. (현재 삽은 SapStrategy가 직접 chunk.Dig를 부르므로 이 경로를
        //  타지 않지만, 배선이 바뀌었을 때 조용히 깨지지 않도록 방어해둔다.)
        float maxScale = (toolIndex == 1) ? Mathf.Max(2.0f, ShovelDigMask.ExtentMultiplier) : 2.0f;
        float searchRadius = radius * maxScale + 1.0f;

        foreach (var chunk in chunks)
        {
            if (chunk == null) continue;

            // 청크 AABB와 searchRadius 원이 겹치는지 빠른 체크
            float chunkW = chunk.width / chunk.pixelsPerUnit;
            float chunkH = chunk.height / chunk.pixelsPerUnit;
            Vector2 chunkMin = chunk.transform.position;
            Vector2 chunkMax = chunkMin + new Vector2(chunkW, chunkH);

            float closestX = Mathf.Clamp(worldPos.x, chunkMin.x, chunkMax.x);
            float closestY = Mathf.Clamp(worldPos.y, chunkMin.y, chunkMax.y);
            float distSqr = (worldPos.x - closestX) * (worldPos.x - closestX)
                          + (worldPos.y - closestY) * (worldPos.y - closestY);

            if (distSqr <= searchRadius * searchRadius)
            {
                Debug.Log($"[StaticChunkTerrainManager] Dig → {chunk.name} @ {worldPos}");
                chunk.Dig(worldPos, radius, toolIndex, applySmoothing);
            }
        }
    }

    public void ExplodeTerrain(Vector2 worldPos, float radius)
    {
        float maxScale = 1.0f; // 원형이므로 별도 배율 없음
        float searchRadius = radius * maxScale + 1.0f;

        foreach (var chunk in chunks)
        {
            if (chunk == null) continue;

            float chunkW = chunk.width / chunk.pixelsPerUnit;
            float chunkH = chunk.height / chunk.pixelsPerUnit;
            Vector2 chunkMin = chunk.transform.position;
            Vector2 chunkMax = chunkMin + new Vector2(chunkW, chunkH);

            float closestX = Mathf.Clamp(worldPos.x, chunkMin.x, chunkMax.x);
            float closestY = Mathf.Clamp(worldPos.y, chunkMin.y, chunkMax.y);
            float distSqr = (worldPos.x - closestX) * (worldPos.x - closestX)
                          + (worldPos.y - closestY) * (worldPos.y - closestY);

            if (distSqr <= searchRadius * searchRadius)
            {
                Debug.Log($"[StaticChunkTerrainManager] Explode → {chunk.name} @ {worldPos}");
                chunk.Explode(worldPos, radius);
            }
        }
    }
}
