using UnityEngine;

public class DrillController : MonoBehaviour
{
    // 드릴 본체 이미지나 애니메이션 객체 (필요하다면 연결)
    // public GameObject drillVisualModel; 

    public void SetDrillActive(bool isActive)
    {
        // 1. 드릴 오브젝트 전체를 켜거나 끄기
        // (이 스크립트가 붙은 오브젝트 자체가 껐다 켜져야 한다면 gameObject.SetActive 사용)
        gameObject.SetActive(isActive);

        if (isActive)
        {
            //Debug.Log("드릴 모드 ON: 애니메이션 시작 등 로직 수행");
            // 여기에 드릴 등장 애니메이션 재생 코드 추가 가능
        }
        else
        {
            //Debug.Log("드릴 모드 OFF");
            // 드릴 작동 중지
        }
    }
}