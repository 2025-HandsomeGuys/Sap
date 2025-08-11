using UnityEngine;

public class DiggingController : MonoBehaviour
{
    [Header("Digging Settings")]
    public float digRadius = 1.0f;         // 파내는 원의 반지름
    public float digOffset = 0.5f;         // 플레이어 중심에서 파기 시작 위치까지의 거리

    private Vector2 currentDigDirection = Vector2.right; // 현재 파기 방향

    void Update()
    {
        // 마우스 위치를 월드 좌표로 변환합니다.
        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        
        // 플레이어 위치에서 마우스 위치를 향하는 방향 벡터를 계산하고 저장합니다.
        // 마우스가 플레이어와 정확히 같은 위치에 있을 때 0으로 나누는 것을 방지합니다.
        if ((mousePosition - (Vector2)transform.position).sqrMagnitude > 0.01f)
        {
            currentDigDirection = (mousePosition - (Vector2)transform.position).normalized;
        }

        // 마우스 왼쪽 버튼이 눌렸을 때 Dig() 함수를 호출합니다.
        if (Input.GetMouseButtonDown(0)) // 0 = 왼쪽 마우스 버튼
        {
            Dig();
        }
    }

    void Dig()
    {
        // 플레이어 위치를 기준으로, 마우스 방향으로 오프셋을 적용한 파기 중심 위치를 계산합니다.
        Vector2 digCenter = (Vector2)transform.position + (currentDigDirection * digOffset);

        // 파기 중심 위치 주변의 모든 콜라이더를 감지합니다.
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(digCenter, digRadius);

        foreach (var hitCollider in hitColliders)
        {
            // 감지된 콜라이더에서 플레이어 자신은 제외합니다.
            if (hitCollider.transform == this.transform)
            {
                continue;
            }

            // 오브젝트의 태그를 확인하여 파낼 수 있는 타일인지 확인합니다.
            string tag = hitCollider.gameObject.tag;
            if (tag == "dirt" || tag == "stone")
            {
                // 오브젝트를 파괴하는 대신, 오브젝트 풀에 반납합니다.
                ObjectPooler.Instance.ReturnToPool(tag, hitCollider.gameObject);
            }
        }
    }

    // 디버깅용: 파내는 범위를 씬(Scene) 뷰에 시각적으로 표시합니다.
    void OnDrawGizmosSelected()
    {
        // 에디터에서 실시간으로 방향을 보여주기 위해 현재 방향을 사용합니다.
        Vector2 digCenter = (Vector2)transform.position + (currentDigDirection * digOffset);
        
        Gizmos.color = Color.yellow; // 색상을 노란색으로 변경하여 구분
        Gizmos.DrawWireSphere(digCenter, digRadius);
    }
}
