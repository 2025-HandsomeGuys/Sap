using UnityEngine;

public class DiggingController : MonoBehaviour
{
    [Header("Digging Settings")]
    public KeyCode digKey = KeyCode.Space; // 땅 파기 키
    public float digRadius = 1.0f;         // 파내는 원의 반지름
    public float digOffset = 0.5f;         // 플레이어 중심에서 파기 시작 위치까지의 거리

    private Vector2 lastDirection = Vector2.right; // 마지막으로 입력된 방향 (기본값: 오른쪽)

    void Update()
    {
        UpdateDirection();

        // 지정된 키가 눌렸을 때 Dig() 함수 호출
        if (Input.GetKeyDown(digKey))
        {
            Dig();
        }
    }

    void UpdateDirection()
    {
        // 키보드 입력 받기 (W,A,S,D 또는 방향키)
        float horizontalInput = Input.GetAxisRaw("Horizontal");
        float verticalInput = Input.GetAxisRaw("Vertical");

        Vector2 currentDirection = new Vector2(horizontalInput, verticalInput).normalized;

        // 실제 입력이 있을 때만 마지막 방향을 업데이트
        if (currentDirection != Vector2.zero)
        {
            lastDirection = currentDirection;
        }
    }

    void Dig()
    {
        // 플레이어 위치를 기준으로, 마지막 입력 방향으로 오프셋을 적용한 파기 중심 위치 계산
        Vector2 digCenter = (Vector2)transform.position + (lastDirection * digOffset);

        // 파기 중심 위치 주변의 모든 콜라이더를 감지
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(digCenter, digRadius);

        foreach (var hitCollider in hitColliders)
        {
            // 감지된 콜라이더에서 플레이어 자신은 제외
            if (hitCollider.transform == this.transform)
            {
                continue;
            }

            // 해당 콜라이더의 게임 오브젝트가 흙 타일인지 확인
            DirtTile dirtTile = hitCollider.GetComponent<DirtTile>();
            if (dirtTile != null)
            {
                // 숨겨진 보석이 있다면 활성화
                if (dirtTile.hiddenGem != null)
                { 
                    dirtTile.hiddenGem.SetActive(true);
                }
                // 흙 오브젝트 파괴
                Destroy(hitCollider.gameObject);
            }
        }
    }

    // 디버깅용: 파내는 범위를 씬(Scene) 뷰에 시각적으로 표시
    void OnDrawGizmosSelected()
    {
        Vector2 digCenter = (Vector2)transform.position + (lastDirection * digOffset);
        
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(digCenter, digRadius);
    }
}