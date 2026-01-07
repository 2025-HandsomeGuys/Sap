using UnityEngine;

public class Digger : MonoBehaviour
{
    [Header("채굴 설정")]
    public float digRadius = 0.5f;
    public float digCooldown = 0.1f;    // 광클 제한 (0.1초보다 빠르게 클릭하면 무시됨)
    public float maxDigRange = 3.0f;

    [Header("레이어")]
    public LayerMask groundLayer;

    private float nextDigTime = 0f;
    private Camera cam;

    private void Start()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[Digger] Camera.main not found. Assign a Camera tagged MainCamera.");
        }
    }

    void Update()
    {
        // [변경됨] 꾹 누르기가 아니라 '누르는 순간' 감지
        if (Input.GetMouseButtonDown(0))
        {
            // 쿨타임 체크 (너무 빠른 연타 방지)
            if (Time.time >= nextDigTime)
            {
                AttemptDig();
            }
        }
    }

    void AttemptDig()
    {
        if (cam == null)
            return;

        Vector2 mousePos = cam.ScreenToWorldPoint(Input.mousePosition);
        Vector2 playerPos = transform.position;

        // 사거리 체크
        float distance = Vector2.Distance(playerPos, mousePos);

        if (distance <= maxDigRange)
        {
            DigAt(mousePos);
            // 다음 클릭 가능 시간 설정
            nextDigTime = Time.time + digCooldown;
        }
    }

    void DigAt(Vector2 position)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(position, digRadius, groundLayer);
        
        Debug.Log($"Digger: Attempting to dig at {position} with radius {digRadius}. Found {hits.Length} colliders.");

        bool foundTerrainChunk = false;
        foreach (Collider2D hit in hits)
        {
            // 플레이어나 다른 GameObject는 무시
            if (hit.CompareTag("Player"))
            {
                continue;
            }
            
            TerrainChunk chunk = hit.GetComponent<TerrainChunk>();
            if (chunk != null)
            {
                foundTerrainChunk = true;
                Debug.Log($"Digger: Found TerrainChunk at {chunk.transform.position}, calling Dig()");
                chunk.Dig(position, digRadius);
            }
        }
        
        if (!foundTerrainChunk && hits.Length > 0)
        {
            Debug.LogWarning($"Digger: Found {hits.Length} collider(s) but no TerrainChunk component. Check LayerMask and TerrainChunk collider setup.");
        }
        else if (hits.Length == 0)
        {
            Debug.LogWarning($"Digger: No colliders found at {position}. Check LayerMask and TerrainChunk collider setup.");
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, maxDigRange);
    }
}