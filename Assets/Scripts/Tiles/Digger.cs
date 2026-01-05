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
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
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

        foreach (Collider2D hit in hits)
        {
            TerrainChunk chunk = hit.GetComponent<TerrainChunk>();
            if (chunk != null)
            {
                chunk.Dig(position, digRadius);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, maxDigRange);
    }
}