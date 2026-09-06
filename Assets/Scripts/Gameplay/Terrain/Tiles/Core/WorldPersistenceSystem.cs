// @tags: save, load, persistence, world-data, disk, binary, chunk-save, rock-save
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using System.Runtime.InteropServices;

public class WorldPersistenceSystem
{
    private readonly string _saveFileName = "worldData.bin";
    private readonly Dictionary<Vector2Int, ChunkSaveData> _worldData = new Dictionary<Vector2Int, ChunkSaveData>();
    private readonly GridCoordinateSystem _grid;

    // Optional dependency for active chunks to save them all
    private ActiveChunkRegistry _activeChunks;

    public WorldPersistenceSystem(GridCoordinateSystem grid)
    {
        _grid = grid;
    }

    public void SetActiveChunkRegistry(ActiveChunkRegistry registry)
    {
        _activeChunks = registry;
    }

    /// <summary>
    /// Retrieves chunk data if available.
    /// </summary>
    public bool TryGetChunkData(Vector2Int coord, out ChunkSaveData data)
    {
        return _worldData.TryGetValue(coord, out data);
    }

    /// <summary>
    /// Saves individual chunk data into the dictionary (memory).
    /// isNormalChunk: DoUnloadChunk에서 이미 계산한 GetComponent 결과를 재사용 — 중복 호출 방지.
    /// 호출 전에 hasBeenModified 체크를 완료한 상태여야 한다.
    /// </summary>
    public void SaveChunkDataToMemory(Vector2Int coord, TerrainChunk tChunk, bool isNormalChunk)
    {
        if (tChunk == null) return;

        if (!_worldData.TryGetValue(coord, out ChunkSaveData data))
        {
            data = new ChunkSaveData();
            _worldData.Add(coord, data);
        }

        // 1. Pixel Data (Copy) — 배열 크기가 같으면 재사용, 달라졌을 때만 새로 할당
        var basePixels = tChunk.baseData;
        if (data.modifiedPixels == null || data.modifiedPixels.Length != basePixels.Length)
            data.modifiedPixels = new Color32[basePixels.Length];
        basePixels.CopyTo(data.modifiedPixels);

        // 2. Pixel Info (Copy) — 동일 규칙
        var pixelInfo = tChunk.pixelInfo;
        if (pixelInfo.IsCreated)
        {
            if (data.pixelInfo == null || data.pixelInfo.Length != pixelInfo.Length)
                data.pixelInfo = new byte[pixelInfo.Length];
            pixelInfo.CopyTo(data.pixelInfo);
        }

        data.hasChanges = true;
        data.wasNormalChunk = isNormalChunk;

        // 3. Rock Layout Snapshot — 2패스로 Array.Resize 제거
        // SpawnedRocks에 남아있는 암석만 저장 (파괴된 암석은 이미 제거됨)
        // null → 신규 청크, 빈 배열 → 저장됐지만 암석 없음 (모두 파괴)
        var spawnedRocks = tChunk.SpawnedRocks;
        int validCount = 0;
        for (int i = 0; i < spawnedRocks.Count; i++)
        {
            var dr = spawnedRocks[i];
            if (dr != null && dr.spriteSetIndex >= 0) validCount++;
        }
        if (data.savedRocks == null || data.savedRocks.Length != validCount)
            data.savedRocks = new RockSaveEntry[validCount];
        int idx = 0;
        for (int i = 0; i < spawnedRocks.Count; i++)
        {
            DiggableRock dr = spawnedRocks[i];
            if (dr == null || dr.spriteSetIndex < 0) continue;
            data.savedRocks[idx++] = new RockSaveEntry
            {
                boundsX        = dr.PixelBoundsInChunk.x,
                boundsY        = dr.PixelBoundsInChunk.y,
                boundsW        = dr.PixelBoundsInChunk.width,
                boundsH        = dr.PixelBoundsInChunk.height,
                spriteSetIndex = dr.spriteSetIndex,
                savedHp        = dr.CurrentHp,
                lastDamageTime = dr.LastDamageTime,
                isRevealed     = dr.IsRevealed,
                // [필수] transform이 아니라 배치 확정값을 저장한다.
                // 히트 애니메이션(RockHitAnimator)이 흔드는 중에 청크가 언로드되면
                // transform에는 흔들림 오프셋이 섞여 있어 그대로 구우면 영구 오차가 된다.
                angle          = dr.BaseAngleZ,
                scale          = dr.SizeScale,
            };
        }
    }

    /// <summary>
    /// Persists all data to disk.
    /// Includes currently active chunks.
    /// </summary>
    public void SaveAllDataToDisk()
    {
        // 1. Make sure all active chunks are up-to-date in memory
        if (_activeChunks != null)
        {
            foreach (var chunk in _activeChunks.GetAll())
            {
                // We need coordinate. Since chunk doesn't verify its own coord, 
                // we'd rely on registry or calculate it. 
                // However, registry stores by array index. 
                // Let's iterate coordinates from registry if possible, or calculate from position.
                // Better approach: Registry should provide (Coord, Chunk) pairs.
                
                // For now, let's reverse calculate from position as fallback, 
                // OR ask Registry to iterate with coords.
                // Let's assume Registry has GetAllWithCoords() or we calculate it.
                // Calculating from position is safe if standard grid is used.
                
                // Actually, let's use the Registry's internal list of active coordinates if available.
                // Current design of Registry (planned) has a list of coordinates.
            }
            
            // Re-approach: Iterate known active coordinates from Registry
            foreach (var coord in _activeChunks.GetActiveCoordinates())
            {
                var chunk = _activeChunks.Get(coord);
                if (chunk is TerrainChunk tc && tc.hasBeenModified)
                {
                    bool isNormalChunk = !tc.IsSpecialChunkInstance;
                    SaveChunkDataToMemory(coord, tc, isNormalChunk);
                }
            }
        }

        string path = Path.Combine(Application.persistentDataPath, _saveFileName);
        
        try
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                writer.Write(_worldData.Count);

                foreach (var kvp in _worldData)
                {
                    writer.Write(kvp.Key.x);
                    writer.Write(kvp.Key.y);

                    ChunkSaveData data = kvp.Value;
                    
                    // Pixel Data
                    int pixelCount = (data.modifiedPixels != null) ? data.modifiedPixels.Length : 0;
                    writer.Write(pixelCount);

                    if (pixelCount > 0)
                    {
                        var pixelSpan = MemoryMarshal.Cast<Color32, byte>(data.modifiedPixels.AsSpan());
                        writer.Write(pixelSpan);
                    }

                    // Pixel Info
                    int infoCount = (data.pixelInfo != null) ? data.pixelInfo.Length : 0;
                    writer.Write(infoCount);
                    if (infoCount > 0)
                    {
                        writer.Write(data.pixelInfo);
                    }
                }
            }
            Debug.Log($"[WorldPersistenceSystem] Saved {_worldData.Count} chunks to {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorldPersistenceSystem] Save Failed: {e.Message}");
        }
    }

    /// <summary>
    /// Loads all data from disk into memory.
    /// </summary>
    public void LoadAllDataFromDisk()
    {
        string path = Path.Combine(Application.persistentDataPath, _saveFileName);
        if (!File.Exists(path)) 
        {
            Debug.Log("[WorldPersistenceSystem] No save file found.");
            return;
        }

        try
        {
            using (BinaryReader reader = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                int chunkCount = reader.ReadInt32();
                _worldData.Clear();

                for (int i = 0; i < chunkCount; i++)
                {
                    int x = reader.ReadInt32();
                    int y = reader.ReadInt32();
                    Vector2Int coord = new Vector2Int(x, y);

                    ChunkSaveData data = new ChunkSaveData();

                    // Pixels
                    int pixelCount = reader.ReadInt32();
                    if (pixelCount > 0)
                    {
                        data.modifiedPixels = new Color32[pixelCount];
                        byte[] rawBytes = reader.ReadBytes(pixelCount * 4);
                        var byteSpan = new ReadOnlySpan<byte>(rawBytes);
                        var colorSpan = MemoryMarshal.Cast<byte, Color32>(byteSpan);
                        colorSpan.CopyTo(data.modifiedPixels);
                    }

                    // Info
                    int infoCount = reader.ReadInt32();
                    if (infoCount > 0)
                    {
                        data.pixelInfo = reader.ReadBytes(infoCount);
                    }

                    data.hasChanges = true;
                    _worldData.Add(coord, data);
                }
            }
            Debug.Log($"[WorldPersistenceSystem] Loaded {_worldData.Count} chunks from {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WorldPersistenceSystem] Load Failed: {e.Message}");
        }
    }
}
