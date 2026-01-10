using UnityEngine;

public class Digger : MonoBehaviour
{
    [Header("채굴 설정")]
    public float digRadius = 0.5f;
    public float digCooldown = 0.1f;
    public float maxDigRange = 3.0f;

    [Header("매니저 연결")]
    public InfinityMapManager mapManager;

    private float nextDigTime = 0f;

    void Start()
    {
        // 혹시 인스펙터에서 연결 안 했을 경우 자동으로 찾기
        if (mapManager == null)
        {
            mapManager = FindObjectOfType<InfinityMapManager>();
        }
    }

    void Update()
    {
        // 클릭 순간 감지
        if (Input.GetMouseButtonDown(0))
        {
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
            nextDigTime = Time.time + digCooldown;
        }
    }

    void DigAt(Vector2 position)
    {
        // [핵심 변경 사항]
        // 기존: Physics2D로 콜라이더를 찾아서 각각 Dig 호출 (경계선에서 부정확할 수 있음)
        // 변경: 매니저에게 좌표와 범위를 주면, 매니저가 알아서 걸쳐있는 모든 청크를 찾아 계산함 (정확함)

        if (mapManager != null)
        {
            mapManager.ModifyTerrain(position, digRadius);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, maxDigRange);
    }
}