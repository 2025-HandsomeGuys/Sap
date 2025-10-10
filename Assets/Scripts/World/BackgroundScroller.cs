using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class BackgroundScroller : MonoBehaviour
{




    private SpriteRenderer spriteRenderer;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.drawMode = SpriteDrawMode.Tiled;
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
