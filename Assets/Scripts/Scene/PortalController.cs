using UnityEngine;
using UnityEngine.SceneManagement; // 씬 전환 기능을 사용하기 위해 추가

public class SceneChanger : MonoBehaviour
{
    // 인스펙터 창에서 전환할 씬의 이름을 설정할 수 있도록 public 변수 선언
    public string nextSceneName;

    // Is Trigger가 활성화된 콜라이더와 충돌했을 때 호출되는 메서드
    void OnTriggerEnter2D(Collider2D other)
    {
        // 충돌한 오브젝트의 태그가 "Player"인지 확인
        if (other.gameObject.CompareTag("Player"))
        {
            // 다음 씬으로 전환\
            Debug.Log("change");
            SceneManager.LoadScene(nextSceneName);
        }
    }
}