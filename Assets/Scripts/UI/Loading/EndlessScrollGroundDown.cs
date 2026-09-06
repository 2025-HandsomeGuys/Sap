using UnityEngine;

public class EndlessScrollGroundDown : MonoBehaviour
{
    [Header("이동 설정")]
    public float speed = 5f;
    public bool isUpward = true; // true: 위로 이동(캐릭터 하강 연출), false: 아래로 이동

    private float spriteHeight;
    private SpriteRenderer spriteRenderer;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        // 1. 오브젝트의 실제 Y축 높이(스케일 포함)를 자동으로 계산
        spriteHeight = spriteRenderer.bounds.size.y;
    }

    void Update()
    {
        // Direction 설정 (위로: Vector3.up, 아래로: Vector3.down)
        Vector3 dir = isUpward ? Vector3.up : Vector3.down;
        transform.Translate(dir * speed * Time.deltaTime);

        // 2. 이미지 1장 높이만큼 완전히 이동했는지 확인
        if (isUpward)
        {
            // 위로 이동 중: 1장 높이 이상 올라갔다면
            if (transform.position.y >= spriteHeight)
            {
                // 정확히 2장치 높이 아래로 내려서 순환시킴 (오차 보정 포함)
                transform.position -= new Vector3(0, spriteHeight * 2f, 0);
            }
        }
        else
        {
            // 아래로 이동 중
            if (transform.position.y <= -spriteHeight)
            {
                transform.position += new Vector3(0, spriteHeight * 2f, 0);
            }
        }
    }
}