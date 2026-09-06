// @tags: decoration, special-chunk, spawn, chunk, pipeline, digging
using UnityEngine;

/// <summary>
/// 압축 쓰레기 벽(CompressedTrashWallEntity)을 지형 청크에 배치하는 데코레이터.
///
/// DiggableRock 패턴과 동일:
///   - TerrainChunk의 자식으로 스폰.
///   - 주변 지형 픽셀은 그대로 남아 있어 삽/곡괭이로 파기 가능.
///   - 쓰레기 덩어리 자체는 IDiggable(toolIndex=2)로 곡괭이만 통함.
///
/// SOLID — SRP: 배치 로직(위치 선정, 확률 체크)만 담당.
/// </summary>
public class CompressedTrashWallDecorator : IChunkDecorator
{
    private readonly GameObject _prefab;
    private readonly float _spawnChance;

    /// <param name="prefab">CompressedTrashWall 프리팹</param>
    /// <param name="spawnChance">청크당 생성 확률 (0 ~ 1)</param>
    public CompressedTrashWallDecorator(GameObject prefab, float spawnChance = 0.15f)
    {
        _prefab = prefab;
        _spawnChance = spawnChance;
    }

    // ─── IChunkDecorator ─────────────────────────────────────────────
    public void Decorate(TerrainChunk chunk, DecorationContext context)
    {
        if (_prefab == null) return;

        // 이미 수정된 청크(플레이어가 이미 판 청크)는 스킵
        if (context.IsModified) return;

        // 시드 기반 확률 체크 — 결정론적
        int seed = HashCoord(context.Coord, context.WorldSeed);
        var rng = new System.Random(seed);
        if (rng.NextDouble() >= _spawnChance) return;

        // 테두리에서 150px 이상 안쪽에서만 배치 (경계 텍스처/암석과 겹침 방지)
        const int margin = 150;
        if (chunk.width <= margin * 2 || chunk.height <= margin * 2) return;

        int px = rng.Next(margin, chunk.width - margin);
        int py = rng.Next(margin, chunk.height - margin);

        // 해당 픽셀에 지형이 있는지 확인 (alpha > 0 = 고체)
        var data = chunk.GetData();
        int idx = py * chunk.width + px;
        if (idx < 0 || idx >= data.BasePixels.Length) return;
        if (data.BasePixels[idx].a == 0) return;

        // 청크 로컬 좌표로 변환 (좌하단 기준)
        float localX = px / (float)chunk.PPU;
        float localY = py / (float)chunk.PPU;

        GameObject obj = Object.Instantiate(_prefab, Vector3.zero, Quaternion.identity, chunk.transform);
        obj.transform.localPosition = new Vector3(localX, localY, 0f);
        obj.name = $"TRASH_{context.Coord.x}_{context.Coord.y}";
        obj.SetActive(true);
    }

    // ─── 내부 ────────────────────────────────────────────────────────
    private static int HashCoord(Vector2Int coord, int seed)
    {
        unchecked
        {
            int h = seed;
            h = h * 31 + coord.x;
            h = h * 31 + coord.y;
            h ^= (int)0x9e3779b9;
            return h;
        }
    }
}
