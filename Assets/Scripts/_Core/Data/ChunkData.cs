// @tags: chunk, data-container, terrain, native-array, pixel, dirty, burst
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

/// <summary>
/// Pure data container for terrain chunk.
/// NO game logic, only data + IDisposable pattern for NativeArray management.
/// This is the "Model" in the MVC/Composition architecture.
/// </summary>
public class ChunkData : IDisposable
{
    // ============================================================================================================
    //  CORE DIMENSIONS
    // ============================================================================================================
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int TotalPixels => Width * Height;

    // [half-res] 거리장을 절반 해상도로 계산하기 위한 차원. 청크는 짝수(1000) 가정이나 +1로 방어.
    // (배경: Assets/Docs/half-res-distance-field.md)
    public int HalfWidth => (Width + 1) / 2;
    public int HalfHeight => (Height + 1) / 2;
    public int HalfTotalPixels => HalfWidth * HalfHeight;
    public int HalfToIndex(int hx, int hy) => hy * HalfWidth + hx;

    // ============================================================================================================
    //  PIXEL DATA (NativeArrays for performance)
    //  NOTE: These are public fields (not properties) because NativeArray elements
    //  cannot be modified through property getters in C#
    // ============================================================================================================
    
    /// <summary>Base terrain color data (source of truth — read by all Jobs)</summary>
    public NativeArray<Color32> BasePixels;

    /// <summary>Pixel material info (1=Dirt, 2=Rock, etc.)</summary>
    public NativeArray<byte> PixelInfo;
    
    /// <summary>Chamfer distance field (for borders). full 해상도. UpsampleDistanceJob이 채운다.</summary>
    public NativeArray<ushort> DistanceField;

    /// <summary>[half-res] 절반 해상도(HalfWidth×HalfHeight) 거리장 계산 버퍼.
    /// Init/Chamfer/BoundarySync가 여기에 쓰고, UpsampleDistanceJob이 DistanceField(full)로 ×2 펼친다.
    /// (배경: Assets/Docs/half-res-distance-field.md)</summary>
    public NativeArray<ushort> DistanceFieldHalf;

    /// <summary>[half-res] BasePixels/PixelInfo를 2×2 다운샘플한 버퍼(DownsampleMaskJob이 채움).
    /// alpha=0(air)는 "2×2 중 하나라도 air"면 0, PixelInfoHalf는 indestructible 비트(&amp;128)만 OR.
    /// Init/Chamfer가 half 격자에서 이 두 버퍼를 읽는다(full 잡을 half 데이터로 그대로 재사용).</summary>
    public NativeArray<Color32> BasePixelsHalf;
    public NativeArray<byte> PixelInfoHalf;

    /// <summary>
    /// 파기 불가 픽셀 마스크. 1 = 파기 불가(IndestructibleOverlay), 0 = 일반 지형.
    /// BasePixels는 불투명 유지 → BFS 빛 차단. TerrainModifier/TerrainCollider에서 스킵.
    /// </summary>
    public NativeArray<byte> IndestructibleMask;
    
    /// <summary>Primary border texture data (used for rim texturing)</summary>
    public NativeArray<Color32> BorderData;

    public int BorderWidth;
    public int BorderHeight;

    /// <summary>
    /// Optional secondary border texture data (e.g. blended lower layer).
    /// Currently not used by jobs but kept for future extension and
    /// for compatibility with higher‑level systems.
    /// </summary>
    public NativeArray<Color32> SecondaryBorderData;

    public int SecondaryBorderWidth;
    public int SecondaryBorderHeight;
    public byte SecondaryTileId;

    // ============================================================================================================
    //  STATE FLAGS (Dirty Flags for Update Management)
    // ============================================================================================================
    
    /// <summary>True if visual texture needs GPU upload</summary>
    public bool IsVisualDirty { get; set; }
    
    /// <summary>True if collider mesh needs regeneration</summary>
    public bool IsColliderDirty { get; set; }
    
    /// <summary>True if chunk has been modified by user (for save system)</summary>
    public bool HasBeenModified { get; set; }

    /// <summary>True if this chunk contains any IndestructibleMask pixels.
    /// Used to skip the adjacency protection check in TerrainModifier for normal chunks.</summary>
    public bool HasIndestructiblePixels { get; set; }

    /// <summary>지형 픽셀 수정 후 세 플래그 일괄 세팅 (저장 포함)</summary>
    public void MarkDirty()
    {
        IsVisualDirty = true;
        IsColliderDirty = true;
        HasBeenModified = true;
    }

    /// <summary>렌더링 갱신만 필요할 때 두 플래그 세팅 (저장 제외)</summary>
    public void MarkRenderDirty()
    {
        IsVisualDirty = true;
        IsColliderDirty = true;
    }

    // ============================================================================================================
    //  CONSTRUCTION & DISPOSAL
    // ============================================================================================================
    
    /// <summary>
    /// Allocates all NativeArrays with Persistent allocator.
    /// </summary>
    public ChunkData(int width, int height)
    {
        Width = width;
        Height = height;
        
        int totalPixels = TotalPixels;
        
        // Allocate pixel data arrays (only what's needed for rendering)
        BasePixels = new NativeArray<Color32>(totalPixels, Allocator.Persistent);
        PixelInfo = new NativeArray<byte>(totalPixels, Allocator.Persistent);
        DistanceField = new NativeArray<ushort>(totalPixels, Allocator.Persistent);
        DistanceFieldHalf = new NativeArray<ushort>(HalfTotalPixels, Allocator.Persistent);
        BasePixelsHalf = new NativeArray<Color32>(HalfTotalPixels, Allocator.Persistent);
        PixelInfoHalf = new NativeArray<byte>(HalfTotalPixels, Allocator.Persistent);
        // [Fix] Initialize optional/texture arrays as 0-length to avoid Job System errors & invalid state
        BorderData = new NativeArray<Color32>(0, Allocator.Persistent);
        SecondaryBorderData = new NativeArray<Color32>(0, Allocator.Persistent);
        IndestructibleMask = new NativeArray<byte>(totalPixels, Allocator.Persistent);
        
        // Initialize flags
        IsVisualDirty = false;
        IsColliderDirty = false;
        HasBeenModified = false;
    }

    /// <summary>
    /// Copies pixel data from source array into BasePixels.
    /// </summary>
    public void LoadPixelData(Color32[] sourcePixels)
    {
        if (sourcePixels == null || sourcePixels.Length != TotalPixels)
        {
            Debug.LogError($"[ChunkData] LoadPixelData: Invalid source size. Expected {TotalPixels}, got {sourcePixels?.Length ?? 0}");
            return;
        }
        
        BasePixels.CopyFrom(sourcePixels);
    }

    /// <summary>
    /// Copies pixel info data from source array.
    /// </summary>
    public void LoadPixelInfo(byte[] sourceInfo)
    {
        if (sourceInfo == null || sourceInfo.Length != TotalPixels)
        {
            Debug.LogWarning($"[ChunkData] LoadPixelInfo: Invalid source, generating default from BasePixels. Expected {TotalPixels}, got {sourceInfo?.Length ?? 0}");
            for (int i = 0; i < TotalPixels; i++)
            {
                PixelInfo[i] = (byte)(BasePixels[i].a == 0 ? 0 : 1);
            }
        }
        else
        {
            PixelInfo.CopyFrom(sourceInfo);
        }
    }

    /// <summary>
    /// PixelInfo 전체를 단일 ID로 채운다.
    /// [GC] 신규 절차적 청크는 PixelInfo가 전부 같은 값(= TileType ID)이라 매니지드
    /// byte[TotalPixels] 스테이징 배열을 만들 필요가 없다. 청크당 1MB 할당 + 100만 회
    /// C# 루프를 MemSet 한 번으로 대체한다. (ChunkDataProvider.GenerateNewData 참조)
    /// </summary>
    public unsafe void FillPixelInfo(byte id)
    {
        if (!PixelInfo.IsCreated) return;
        UnsafeUtility.MemSet(PixelInfo.GetUnsafePtr(), id, PixelInfo.Length);
    }

    /// <summary>
    /// Sets or resizes the secondary border data buffer from a managed array.
    /// Safe to call repeatedly; underlying NativeArray is reused when possible.
    /// </summary>
    public void SetSecondaryBorderData(Color32[] sourcePixels, int width, int height, byte tileId)
    {
        if (sourcePixels == null || sourcePixels.Length == 0)
        {
            Debug.LogWarning("[ChunkData] SetSecondaryBorderData: sourcePixels is null or empty.");
            return;
        }

        SecondaryBorderWidth = width;
        SecondaryBorderHeight = height;
        SecondaryTileId = tileId;

        if (SecondaryBorderData.Length != sourcePixels.Length)
        {
            SecondaryBorderData.Dispose();
            SecondaryBorderData = new NativeArray<Color32>(sourcePixels, Allocator.Persistent);
        }
        else
        {
            SecondaryBorderData.CopyFrom(sourcePixels);
        }
    }

    /// <summary>
    /// Sets or resizes the primary border data buffer from a managed array.
    /// Safe to call repeatedly; underlying NativeArray is reused when possible.
    /// Symmetric with SetSecondaryBorderData but for primary border texture.
    /// </summary>
    public void SetBorderData(Color32[] sourcePixels, int width, int height)
    {
        if (sourcePixels == null || sourcePixels.Length == 0)
        {
            Debug.LogWarning("[ChunkData] SetBorderData: sourcePixels is null or empty.");
            return;
        }

        BorderWidth = width;
        BorderHeight = height;

        if (BorderData.Length != sourcePixels.Length)
        {
            BorderData.Dispose();
            BorderData = new NativeArray<Color32>(sourcePixels, Allocator.Persistent);
        }
        else
        {
            BorderData.CopyFrom(sourcePixels);
        }
    }

    public void ClearSecondaryBorderData()
    {
        if (SecondaryBorderData.IsCreated && SecondaryBorderData.Length > 0)
        {
            SecondaryBorderData.Dispose();
            SecondaryBorderData = new NativeArray<Color32>(0, Allocator.Persistent);
            SecondaryBorderWidth = 0;
            SecondaryBorderHeight = 0;
            SecondaryTileId = 0;
        }
    }


    // ============================================================================================================
    //  IDISPOSABLE IMPLEMENTATION
    // ============================================================================================================
    
    private bool _disposed = false;

    public void Dispose()
    {
        if (_disposed) return;
        
        // Dispose all NativeArrays
        if (BasePixels.IsCreated) BasePixels.Dispose();
        if (PixelInfo.IsCreated) PixelInfo.Dispose();
        if (DistanceField.IsCreated) DistanceField.Dispose();
        if (DistanceFieldHalf.IsCreated) DistanceFieldHalf.Dispose();
        if (BasePixelsHalf.IsCreated) BasePixelsHalf.Dispose();
        if (PixelInfoHalf.IsCreated) PixelInfoHalf.Dispose();
        if (BorderData.IsCreated) BorderData.Dispose();
        if (SecondaryBorderData.IsCreated) SecondaryBorderData.Dispose();
        if (IndestructibleMask.IsCreated) IndestructibleMask.Dispose();
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~ChunkData()
    {
        // Safety net: warn if Dispose wasn't called
        if (!_disposed)
        {
            Debug.LogWarning("[ChunkData] Finalizer called without Dispose! This may cause memory leaks.");
            Dispose();
        }
    }

    // ============================================================================================================
    //  UTILITY METHODS
    // ============================================================================================================
    
    /// <summary>
    /// Validates if a pixel coordinate is within bounds.
    /// </summary>
    public bool IsValid(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
    
    /// <summary>
    /// Converts 2D coordinate to 1D array index.
    /// </summary>
    public int ToIndex(int x, int y) => y * Width + x;
}
