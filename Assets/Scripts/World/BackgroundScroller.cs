using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class BackgroundScroller : MonoBehaviour
{


    [Tooltip("The Y position where the top of the background should start. The background will only be visible below this level.")]
    public float groundLevel = 0f;

    [Tooltip("How many times the sprite should repeat horizontally and vertically.")]
    public Vector2 tiling = new Vector2(20f, 20f);

    private SpriteRenderer spriteRenderer;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        UpdateTiling();

        // Set initial position of the background to be static
        float yPosition = groundLevel - (spriteRenderer.size.y / 2f);
        transform.position = new Vector3(transform.position.x, yPosition, transform.position.z);
        spriteRenderer.enabled = true;
    }


    void Update()
    {
        // Background is now static, no updates needed per frame for position or visibility.
    }

    void OnValidate()
    {
        // This allows the tiling to be updated in the editor.
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        UpdateTiling();
    }

    void UpdateTiling()
    {
        if (spriteRenderer == null) return;

        // Ensure draw mode is Tiled
        spriteRenderer.drawMode = SpriteDrawMode.Tiled;

        // Get the original sprite size
        if (spriteRenderer.sprite == null) return; // Can't do anything without a sprite
        Vector2 spriteSize = spriteRenderer.sprite.bounds.size;

        // Set the renderer size based on the sprite size and tiling factor
        spriteRenderer.size = new Vector2(spriteSize.x * tiling.x, spriteSize.y * tiling.y);
    }
}
