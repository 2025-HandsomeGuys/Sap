// @tags: indestructible, special-chunk, chunk, pixel, initialization, interface
using UnityEngine;
using Unity.Collections;

/// <summary>
/// 하이브리드 청크의 파괴 불가 오버레이 초기화 컴포넌트.
/// TerrainChunk 자식 오브젝트에 부착하며, IChunkInitializer 계약으로 초기화된다.
///
/// SOLID:
///  - SRP: 픽셀 초기화(BasePixels + IndestructibleMask 기록)만 담당.
///  - ISP: IChunkInitializer만 구현. 타격 피드백은 IndestructibleHitFeedback에 분리.
///
/// 동작 원리:
/// - 자신의 SpriteRenderer 스프라이트를 읽어, 불투명 픽셀 위치를
///   부모 TerrainChunk의 BasePixels에 덮어쓰고 IndestructibleMask[idx]=1 설정.
/// - BasePixels는 불투명 유지 → BFS 조명 차단.
/// - TerrainModifier / TerrainCollider는 mask=1 픽셀을 건너뜀.
/// - 이 오브젝트의 PolygonCollider2D(수동 설정)가 물리 충돌을 담당.
///
/// 주의:
/// - 오버레이 스프라이트의 PPU는 TerrainChunk PPU(100)와 동일해야 한다.
/// - 스프라이트 Import Settings: Filter Mode = Point, Read/Write = Enabled.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class IndestructibleOverlayInit : MonoBehaviour, IChunkInitializer
{
    private const float PPU = 100f;

    public int InitializationOrder => 0;

    public void Initialize(Transform parent)
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null)
        {
            Debug.LogError($"[IndestructibleOverlayInit] SpriteRenderer 또는 Sprite가 없습니다 — {gameObject.name}");
            return;
        }

        TerrainChunk chunk = GetComponentInParent<TerrainChunk>();
        if (chunk == null)
        {
            Debug.LogError($"[IndestructibleOverlayInit] 부모에서 TerrainChunk를 찾을 수 없습니다 — {gameObject.name}");
            return;
        }

        var data = chunk.GetData();
        if (data == null)
        {
            Debug.LogError($"[IndestructibleOverlayInit] ChunkData가 null입니다 — {gameObject.name}");
            return;
        }

        Sprite sprite = sr.sprite;
        Texture2D tex = sprite.texture;
        if (!tex.isReadable)
        {
            Debug.LogError($"[IndestructibleOverlayInit] Texture '{tex.name}' Read/Write 비활성화. Import Settings에서 활성화하세요.");
            return;
        }

        Rect r = sprite.textureRect;
        int sw = (int)r.width;
        int sh = (int)r.height;
        NativeArray<Color32> texPixels = tex.GetPixelData<Color32>(0);
        int texWidth = tex.width;
        int rx = (int)r.x, ry = (int)r.y;
        int texDataHeight = texPixels.Length / texWidth;
        sw = Mathf.Min(sw, texWidth - rx);
        sh = Mathf.Min(sh, texDataHeight - ry);

        Vector3 localPos = transform.localPosition;
        float pivotX = sprite.pivot.x;
        float pivotY = sprite.pivot.y;

        int originX = Mathf.RoundToInt(localPos.x * PPU - pivotX);
        int originY = Mathf.RoundToInt(localPos.y * PPU - pivotY);

        int written = 0;
        for (int sy = 0; sy < sh; sy++)
        {
            int texRow = (ry + sy) * texWidth + rx;
            for (int sx = 0; sx < sw; sx++)
            {
                Color32 c = texPixels[texRow + sx];
                if (c.a < 10) continue;

                int cx = originX + sx;
                int cy = originY + sy;

                if (!data.IsValid(cx, cy)) continue;

                int idx = data.ToIndex(cx, cy);
                data.BasePixels[idx] = c;
                data.IndestructibleMask[idx] = 1;
                written++;
            }
        }

        if (written > 0)
            data.HasIndestructiblePixels = true;

        chunk.isTextureDirty = true;
        chunk.isDirty = true;

        Debug.Log($"[IndestructibleOverlayInit] 초기화 완료 — {gameObject.name}, 픽셀 {written}개 마킹");
    }
}
