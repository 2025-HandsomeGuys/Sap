using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class BackgroundScroller : MonoBehaviour
{




    private SpriteRenderer spriteRenderer;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        
        // Sprite Tiling 모드 설정
        // 참고: Sprite의 Import Settings에서 Mesh Type을 "Full Rect"로 설정하면 경고가 사라집니다.
        // 하지만 경고가 나타나도 기능은 정상적으로 작동합니다.
        if (spriteRenderer.sprite != null)
        {
            // Sprite가 있는 경우에만 Tiled 모드 설정
            // Unity 내부 경고는 억제할 수 없지만, 기능에는 영향이 없습니다
            spriteRenderer.drawMode = SpriteDrawMode.Tiled;
        }
    }

    public void SetTileProperties(Vector3 position, Vector2 size)
    {
        transform.position = position;
        spriteRenderer.size = size;
        spriteRenderer.enabled = true;
    }

    void Update()
    {
        // This method is intentionally left empty as the background is managed externally.
    }
}
