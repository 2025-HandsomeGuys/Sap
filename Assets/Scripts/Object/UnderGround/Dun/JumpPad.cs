using UnityEngine;

public class JumpPad : MonoBehaviour
{
    [Header("점프대 설정")]
    [Tooltip("인스펙터에서 점프 힘을 조절하세요.")]
    public float jumpForce = 15f;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 1. 충돌한 오브젝트가 플레이어인지 태그로 확인
        if (collision.gameObject.CompareTag("Player"))
        {
            // 2. 플레이어가 점프대보다 위쪽에 있는지 확인 (옆이나 아래에서 닿으면 무시)
            if (collision.transform.position.y > transform.position.y)
            {
                Rigidbody2D playerRb = collision.gameObject.GetComponent<Rigidbody2D>();

                if (playerRb != null)
                {
                    // 3. 기존의 떨어지던 가속도를 초기화해야 항상 일정한 높이로 점프합니다.
                    playerRb.linearVelocity = new Vector2(playerRb.linearVelocity.x, 0f);

                    // 4. 위쪽(Vector2.up)으로 순간적인 힘(Impulse)을 가함
                    playerRb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
                }
            }
        }
    }
}