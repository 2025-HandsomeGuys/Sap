using UnityEngine;

public class ColorChangeZone : MonoBehaviour
{
    // 이제 나갔을 때의 색상을 여기서 강제하지 않고 DayCycleManager를 따르므로, 
    // outsideColor 변수는 삭제해도 됩니다!

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 들어온 오브젝트의 태그가 "Player"인지 확인
        if (collision.CompareTag("Player"))
        {
            PlayerColorManager colorManager = collision.GetComponent<PlayerColorManager>();
            if (colorManager != null)
            {
                // 가로등/빛 영역 안으로 들어가면 하얀색(밝은 색)으로 스르륵 변경
                colorManager.SetAllColors(Color.white);
            }
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        // 나간 오브젝트의 태그가 "Player"인지 확인
        if (collision.CompareTag("Player"))
        {
            PlayerColorManager colorManager = collision.GetComponent<PlayerColorManager>();
            if (colorManager != null)
            {
                // [핵심] 영역 밖으로 나가면, 현재 시간(오전인지 오후인지)에 맞는 원래 색상으로 스르륵 복구!
                colorManager.RestoreTimeColor();
            }
        }
    }
}