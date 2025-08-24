using UnityEngine;

public class BackgroundScroller : MonoBehaviour
{
    public Transform playerTransform;
    public float scrollSpeed = 0.1f; // How fast the background scrolls relative to the player

    private Vector3 lastPlayerPosition;

    void Start()
    {
        if (playerTransform == null)
        {
            Debug.LogError("Player Transform not assigned to BackgroundScroller!");
            enabled = false;
            return;
        }
        lastPlayerPosition = playerTransform.position;
    }

    void Update()
    {
        // Calculate how much the player has moved since the last frame
        Vector3 playerMovement = playerTransform.position - lastPlayerPosition;

        // Apply a fraction of the player's movement to the background
        // This creates the scrolling effect
        transform.position += playerMovement * scrollSpeed;

        // Update lastPlayerPosition for the next frame
        lastPlayerPosition = playerTransform.position;
    }
}
