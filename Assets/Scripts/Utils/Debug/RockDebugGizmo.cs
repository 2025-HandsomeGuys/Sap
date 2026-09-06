using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class RockDebugGizmo : MonoBehaviour
{
    public TerrainChunk targetChunk;
    public int targetPixelX;
    public int targetPixelY;
    public Vector3 calculatedLocalPos;

    void OnDrawGizmos()
    {
        if (targetChunk == null) return;

        // 1. Calculate Target World Position from pixel coords
        // pixel coords are 0-based index. Center of pixel is +0.5
        float ppu = targetChunk.pixelsPerUnit;
        if (ppu <= 0) ppu = 16f; // Fallback

        // Target Local Position (Center of pixel)
        float localX = (targetPixelX + 0.5f) / ppu;
        float localY = (targetPixelY + 0.5f) / ppu;
        Vector3 targetLocalPos = new Vector3(localX, localY, 0);

        // Convert to World
        Vector3 targetWorldPos = targetChunk.transform.TransformPoint(targetLocalPos);

        // 2. Draw Target (Green Sphere) - "Where it SHOULD be" (The Hole)
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(targetWorldPos, 0.1f);

        // 3. Draw Actual (Red Cube) - "Where it IS" (The Object)
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.15f);

        // 4. Draw Error Line
        float dist = Vector3.Distance(targetWorldPos, transform.position);
        if (dist > 0.001f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, targetWorldPos);

            #if UNITY_EDITOR
            string label = $"Err: {dist:F4}\nLocErr: {Vector3.Distance(transform.localPosition, calculatedLocalPos):F4}";
            Handles.Label(Vector3.Lerp(transform.position, targetWorldPos, 0.5f), label);
            #endif
        }
    }
}
