using UnityEngine;

/// <summary>
/// 용암 점프맵 바닥의 용암(Magma)을 밟았을 때 화상(Burn) 피해를 입히는 스크립트
/// </summary>
public class MagmaFloorDamage : MonoBehaviour
{
    [Header("Damage Settings")]
    [Tooltip("초당 가해지는 화상 누적량 (스태미나 감소)")]
    public float burnDamagePerSecond = 30.0f;
    
    [Header("Detection")]
    public string playerTag = "Player";

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other.CompareTag(playerTag))
        {
            // 플레이어 객체(또는 부모)에서 StaminaManager를 찾음
            StaminaManager staminaManager = other.GetComponent<StaminaManager>();
            if (staminaManager == null)
            {
                staminaManager = other.GetComponentInParent<StaminaManager>();
            }

            if (staminaManager != null)
            {
                // 프레임 경과 시간에 비례하여 화상 수치 증가
                staminaManager.AddBurn(burnDamagePerSecond * Time.deltaTime);
            }
        }
    }
}
