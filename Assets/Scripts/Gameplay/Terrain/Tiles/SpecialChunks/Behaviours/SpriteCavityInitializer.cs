// @tags: special-chunk, chunk, pixel, initialization, sprite, interface
using UnityEngine;

/// <summary>
/// 특수 청크 프리팹의 스프라이트 픽셀을 TerrainChunk 픽셀 데이터로 주입하는 초기화 컴포넌트.
/// - 불투명 픽셀 → 해당 색상으로 지형 픽셀 설정 (파기 가능)
/// - 투명 픽셀  → 공기(air) 픽셀 설정 (공동)
/// 프리팹 자식 오브젝트 및 비주얼이 그대로 보존된다.
/// </summary>
[RequireComponent(typeof(TerrainChunk))]
public class SpriteCavityInitializer : MonoBehaviour, IChunkInitializer
{
    [Tooltip("TerrainChunk 픽셀 데이터의 원본이 될 스프라이트. 투명 픽셀은 공동, 불투명 픽셀은 지형이 된다.")]
    [SerializeField] private Sprite sourceSprite;

    public int InitializationOrder => 0;

    public void Initialize(Transform parent)
    {
        TerrainChunk chunk = GetComponent<TerrainChunk>();
        if (chunk == null)
        {
            Debug.LogError("[SpriteCavityInitializer] TerrainChunk 컴포넌트를 찾을 수 없습니다.");
            return;
        }

        if (sourceSprite == null)
        {
            Debug.LogError($"[SpriteCavityInitializer] sourceSprite가 할당되지 않았습니다 — {gameObject.name}");
            return;
        }

        Texture2D tex = sourceSprite.texture;
        if (!tex.isReadable)
        {
            Debug.LogError($"[SpriteCavityInitializer] Texture '{tex.name}'의 Read/Write가 비활성화되어 있습니다. Import Settings에서 활성화하세요.");
            return;
        }

        Rect r = sourceSprite.textureRect;
        int sw = (int)r.width;
        int sh = (int)r.height;

        if (sw != chunk.width || sh != chunk.height)
        {
            Debug.LogWarning($"[SpriteCavityInitializer] 스프라이트 크기({sw}x{sh})가 청크 크기({chunk.width}x{chunk.height})와 다릅니다. 범위 내만 복사합니다.");
        }

        // GetPixels32는 포맷에 관계없이 자동 압축 해제 후 실제 RGBA 픽셀 배열을 반환한다.
        // GetPixelData<Color32>는 GPU 원시 데이터(압축 블록 포함)를 반환하므로
        // 압축 텍스처(DXT, ETC2 등)에서 잘못된 stride·높이 계산으로 바코드 패턴이 생긴다.
        Color32[] texPixels = tex.GetPixels32(0);
        int texWidth = tex.width;
        int rx = (int)r.x, ry = (int)r.y;
        sw = Mathf.Min(sw, texWidth - rx);
        sh = Mathf.Min(sh, tex.height - ry);

        var data = chunk.GetData();
        if (data == null)
        {
            Debug.LogError($"[SpriteCavityInitializer] ChunkData가 null입니다 — {gameObject.name}");
            return;
        }

        Color32 air = new Color32(0, 0, 0, 0);
        int cw = chunk.width;
        int ch = chunk.height;
        int copyW = Mathf.Min(sw, cw);
        int copyH = Mathf.Min(sh, ch);

        for (int y = 0; y < copyH; y++)
        {
            int rowOffset = y * cw;
            int texRow = (ry + y) * texWidth + rx;
            for (int x = 0; x < copyW; x++)
            {
                Color32 c = texPixels[texRow + x];
                int idx = rowOffset + x;

                if (c.a < 10)
                    data.BasePixels[idx] = air;
                else
                    data.BasePixels[idx] = c;
            }
        }

        // 비주얼·콜라이더 갱신 예약
        chunk.isTextureDirty = true;
        chunk.isDirty = true;

        //Debug.Log($"[SpriteCavityInitializer] 스프라이트 픽셀 주입 완료 ({chunk.ChunkX},{chunk.ChunkY}) {copyW}x{copyH}");
    }
}
