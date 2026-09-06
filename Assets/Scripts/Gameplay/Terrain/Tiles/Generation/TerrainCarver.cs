// @tags: generation, terrain, carving, pixel, decoration, chunk
using UnityEngine;
using Unity.Collections;
using System;

// [New] Separated from TerrainGenerator.cs
// Handles low-level terrain modification (carving/clearing) for decorations.
public static class TerrainCarver
{
    // ============================================================================================================
    //  CONSTANTS
    // ============================================================================================================
    private const byte ALPHA_THRESHOLD = 10;   // Alpha threshold for carving
    private const byte PIXEL_ID_OBJECT = 2;    // Pixel info ID for object (Rock/Mineral)
    private const byte PIXEL_ID_AIR = 0;       // Pixel info ID for air

    // ============================================================================================================
    //  PUBLIC API
    // ============================================================================================================

    public static void CarveHole(TerrainChunk chunk, int cx, int cy, Color32[] mask, int mw, int mh, Vector2 pivot)
    {
        if (chunk == null)
        {
            Debug.LogError("[TerrainCarver] Chunk is null!");
            return;
        }

        if (mask == null || mask.Length != mw * mh)
        {
            Debug.LogError("[TerrainCarver] Invalid mask data!");
            return;
        }

        int startX = cx - (int)pivot.x;
        int startY = cy - (int)pivot.y;

        // [Optimization] Move bounds check outside the loop (Clamping)
        // Calculate valid loop range
        int loopMinY = Mathf.Max(0, -startY);
        int loopMaxY = Mathf.Min(mh, chunk.height - startY);
        
        int loopMinX = Mathf.Max(0, -startX);
        int loopMaxX = Mathf.Min(mw, chunk.width - startX);
        
        // Return early if completely out of bounds
        if (loopMinY >= loopMaxY || loopMinX >= loopMaxX) return;

        var data = chunk.GetData();

        int paintedCount = 0;
        int edgeCount = 0;

        for (int y = loopMinY; y < loopMaxY; y++)
        {
            int targetY = startY + y;
            int rowOffset = targetY * chunk.width;
            int maskRowOffset = y * mw;

            for (int x = loopMinX; x < loopMaxX; x++)
            {
                int maskIdx = maskRowOffset + x;
                
                if (mask[maskIdx].a > ALPHA_THRESHOLD)
                {
                    paintedCount++;
                    int targetX = startX + x;
                    int chunkIdx = rowOffset + targetX;
                    
                    // [Fix] Paint as SOLID rock
                    data.BasePixels[chunkIdx] = mask[maskIdx]; 
                    
                    // [New] Edge Detection for Material Boundary
                    bool isEdge = false;
                    
                    // Check neighbors
                    if (x <= 0 || x >= mw - 1 || y <= 0 || y >= mh - 1) isEdge = true; // Image boundary
                    else
                    {
                        if (mask[maskIdx - 1].a <= ALPHA_THRESHOLD || 
                            mask[maskIdx + 1].a <= ALPHA_THRESHOLD || 
                            mask[maskIdx - mw].a <= ALPHA_THRESHOLD || 
                            mask[maskIdx + mw].a <= ALPHA_THRESHOLD)
                        {
                            isEdge = true;
                        }
                    }

                    if (isEdge)
                    {
                        data.PixelInfo[chunkIdx] = (byte)(PIXEL_ID_OBJECT | 128); // Set Boundary Flag
                        edgeCount++;
                    }
                    else
                        data.PixelInfo[chunkIdx] = PIXEL_ID_OBJECT; 
                }
            }
        }
        
        // [DEBUG] Log carving results
        if (paintedCount > 0)
        {
             //Debug.Log($"[TerrainCarver] Carved hole at {cx},{cy}. Painted: {paintedCount}, Edges: {edgeCount}. Sample Mask Alpha: {mask[0].a}");
        }
    }

    /// <summary>
    /// CarveHole의 역방향. 마스크의 불투명 픽셀 위치를 air(투명)로 클리어합니다.
    /// 돌 파괴 시 지형에 새겨진 픽셀을 제거할 때 사용합니다.
    /// </summary>
    public static void ClearHole(TerrainChunk chunk, int cx, int cy, Color32[] mask, int mw, int mh, Vector2 pivot, Action<Vector2, Color32> spawnParticle = null, Func<int, int, Vector2> pixelToWorldPos = null)
    {
        if (chunk == null || mask == null || mask.Length != mw * mh) return;

        int startX = cx - (int)pivot.x;
        int startY = cy - (int)pivot.y;

        int loopMinY = Mathf.Max(0, -startY);
        int loopMaxY = Mathf.Min(mh, chunk.height - startY);
        int loopMinX = Mathf.Max(0, -startX);
        int loopMaxX = Mathf.Min(mw, chunk.width - startX);

        if (loopMinY >= loopMaxY || loopMinX >= loopMaxX) return;

        // [Fix] NativeArray에 쓰기 전 진행 중인 Job 완료 대기
        chunk.EnsureJobsCompleted();

        var data = chunk.GetData();
        Color32 air = new Color32(0, 0, 0, 0);

        for (int y = loopMinY; y < loopMaxY; y++)
        {
            int targetY = startY + y;
            int rowOffset = targetY * chunk.width;
            int maskRowOffset = y * mw;

            for (int x = loopMinX; x < loopMaxX; x++)
            {
                int maskIdx = maskRowOffset + x;
                if (mask[maskIdx].a > ALPHA_THRESHOLD)
                {
                    int chunkIdx = rowOffset + (startX + x);
                    Color32 originalColor = data.BasePixels[chunkIdx];

                    // [Fix] 이미 air면 스킵 → hasBeenModified 불필요 재설정 방지
                    if (originalColor.a == 0) continue;

                    if (spawnParticle != null && pixelToWorldPos != null)
                    {
                        Vector2 worldPos = pixelToWorldPos(startX + x, targetY);
                        spawnParticle(worldPos, originalColor);
                    }

                    data.BasePixels[chunkIdx] = air;
                    data.PixelInfo[chunkIdx] = PIXEL_ID_AIR;
                }
            }
        }

        // [Fix] 시각 업데이트 및 콜라이더 업데이트를 위해 명시적 갱신 처리
        chunk.isTextureDirty = true;
        chunk.isDirty = true;
        
        // [New] 즉각적인 렌더링 업데이트를 위해 Visualizer 갱신
        if (chunk.Visualizer != null)
        {
            int margin = 20; 
            int minX = Mathf.Clamp(startX - margin, 0, chunk.width);
            int maxX = Mathf.Clamp(startX + mw + margin, 0, chunk.width);
            int minY = Mathf.Clamp(startY - margin, 0, chunk.height);
            int maxY = Mathf.Clamp(startY + mh + margin, 0, chunk.height);
            
            chunk.Visualizer.UpdateVisualsArea(minX, minY, maxX, maxY, chunk.ChunkX, chunk.ChunkY, chunk.SyncBoundaryDistanceWithNeighbors);
            
            // 콜라이더 갱신 예약
            chunk.GetData().IsColliderDirty = true;
        }
    }

    public static void ClearArea(TerrainChunk chunk, int centerX, int centerY, float yOffsetLocal, float radiusLocal)
    {
        // [Temporary] Disable Area Clearing as requested
        return;
        
        /*
        // [Safety] Check if chunk is valid
        if (chunk == null) return;

        int centerPx = centerX;
        int centerPy = centerY;
        
        // Pre-calculate pixel values
        int ppu = (int)chunk.PPU; // Optimizing cast
        int yOffsetPx = Mathf.CeilToInt(yOffsetLocal * ppu);
        centerPy += yOffsetPx; 
        
        int clearRadius = Mathf.CeilToInt(radiusLocal * ppu);

        // [Optimization] Clamp loops to chunk bounds
        int startY = Mathf.Max(0, centerPy - clearRadius);
        int endY = Mathf.Min(chunk.height, centerPy + clearRadius + 1);
        int startX = Mathf.Max(0, centerPx - clearRadius);
        int endX = Mathf.Min(chunk.width, centerPx + clearRadius + 1);
        
        var data = chunk.GetData();

        for (int y = startY; y < endY; y++)
        {
            int rowOffset = y * chunk.width;
            for (int x = startX; x < endX; x++)
            {
                int idx = rowOffset + x;
                data.BasePixels[idx] = new Color32(0, 0, 0, 0); 
                data.PixelInfo[idx] = PIXEL_ID_AIR; 
            }
        }
        */
    }

    public static void CarveFromTexture(TerrainChunk chunk, int x, int y, Texture2D texture, Vector2 pivot)
    {
        if (texture == null) return;
        if (!texture.isReadable)
        {
            Debug.LogError($"[TerrainCarver] Texture '{texture.name}' is not readable!");
            return;
        }

        CarveHoleNative(chunk, x, y, texture.GetPixelData<Color32>(0), texture.width, 0, 0, texture.width, texture.height, pivot);
    }

    public static void CarveFromSprite(TerrainChunk chunk, int x, int y, Sprite sprite, Vector2 pivot)
    {
        if (sprite == null) return;
        Texture2D tex = sprite.texture;
        if (!tex.isReadable)
        {
            Debug.LogError($"[TerrainCarver] Texture for sprite '{sprite.name}' is not readable! Please enable Read/Write in Import Settings.");
            return;
        }

        Rect r = sprite.textureRect;
        CarveHoleNative(chunk, x, y, tex.GetPixelData<Color32>(0), tex.width, (int)r.x, (int)r.y, (int)r.width, (int)r.height, pivot);
    }

    // ============================================================================================================
    //  PRIVATE HELPERS
    // ============================================================================================================

    // Zero-GC carve path: NativeArray view into the texture's native memory, no copy.
    private static void CarveHoleNative(
        TerrainChunk chunk, int cx, int cy,
        NativeArray<Color32> texPixels, int texWidth,
        int rectX, int rectY, int mw, int mh, Vector2 pivot)
    {
        // sprite rect가 텍스처 경계에 걸치는 케이스 방어
        int texDataHeight = texPixels.Length / texWidth;
        mw = Mathf.Min(mw, texWidth - rectX);
        mh = Mathf.Min(mh, texDataHeight - rectY);

        int startX = cx - (int)pivot.x;
        int startY = cy - (int)pivot.y;

        int loopMinY = Mathf.Max(0, -startY);
        int loopMaxY = Mathf.Min(mh, chunk.height - startY);
        int loopMinX = Mathf.Max(0, -startX);
        int loopMaxX = Mathf.Min(mw, chunk.width - startX);

        if (loopMinY >= loopMaxY || loopMinX >= loopMaxX) return;

        var data = chunk.GetData();

        for (int y = loopMinY; y < loopMaxY; y++)
        {
            int targetY = startY + y;
            int rowOffset = targetY * chunk.width;
            int texRowBase = (rectY + y) * texWidth + rectX;

            for (int x = loopMinX; x < loopMaxX; x++)
            {
                Color32 c = texPixels[texRowBase + x];
                if (c.a <= ALPHA_THRESHOLD) continue;

                int chunkIdx = rowOffset + startX + x;
                data.BasePixels[chunkIdx] = c;

                bool isEdge = x <= 0 || x >= mw - 1 || y <= 0 || y >= mh - 1
                    || texPixels[texRowBase + x - 1].a <= ALPHA_THRESHOLD
                    || texPixels[texRowBase + x + 1].a <= ALPHA_THRESHOLD
                    || texPixels[(rectY + y - 1) * texWidth + rectX + x].a <= ALPHA_THRESHOLD
                    || texPixels[(rectY + y + 1) * texWidth + rectX + x].a <= ALPHA_THRESHOLD;

                data.PixelInfo[chunkIdx] = isEdge ? (byte)(PIXEL_ID_OBJECT | 128) : PIXEL_ID_OBJECT;
            }
        }
    }
}
