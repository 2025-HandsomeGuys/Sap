using UnityEngine;

public class PickaxeEffect : MonoBehaviour
{
    [Header("각도 보정 설정")]
    [Tooltip("이펙트 스프라이트 원본이 0도(우측)를 바라보지 않을 경우 더해줄 오프셋 각도")]
    public float angleOffset = 0f;

    [Header("회전 방식 설정")]
    [Tooltip("체크 시 부모(플레이어)의 회전을 무시하고 오직 마우스를 향해 절대적인 월드 각도를 가집니다.")]
    public bool useWorldRotation = true;

    /// <summary>
    /// 마우스(또는 타겟) 좌표를 전달받아 이펙트의 각도를 회전시킵니다.
    /// </summary>
    /// <param name="targetPos">바라볼 목표 지점 (예: 마우스 월드 좌표)</param>
    /// <param name="extraOffset">콤보별로 각도를 약간씩 비틀고 싶을 때 추가할 각도</param>
    public void RotateToTarget(Vector3 targetPos, float extraOffset = 0f)
    {
        // 1. 이펙트 위치에서 타겟을 향하는 방향 벡터와 각도 계산
        Vector2 direction = targetPos - transform.position;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // 2. 부모(플레이어)가 왼쪽을 보고 있어 스케일이 음수(-1)가 되었다면 각도 보정
        if (transform.lossyScale.x < 0)
        {
            angle += 180f;

        }

        float finalAngle = angle + angleOffset + extraOffset;

        // 3. 각도 적용
        if (useWorldRotation)
        {
            // 부모 뼈대의 회전과 상관없이 무조건 타겟을 향하도록 월드 회전 적용
            transform.rotation = Quaternion.Euler(0f, 0f, finalAngle);
        }
        else
        {
            // 부모 기준 로컬 회전 적용
            transform.localRotation = Quaternion.Euler(0f, 0f, finalAngle);
        }
    }

    /// <summary>
    /// 간편 호출용: 현재 마우스 위치를 자동으로 찾아서 회전합니다.
    /// </summary>
    public void RotateToMouse(float extraOffset = 0f)
    {
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mousePos.z = 0f;
        RotateToTarget(mousePos, extraOffset);
    }
}