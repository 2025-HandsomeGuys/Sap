using UnityEngine;
using System.Collections;

[RequireComponent(typeof(PlayerStatsController))]
public class StaminaManager : MonoBehaviour
{
    [Header("Stamina Regeneration Settings")]
    [Tooltip("스태미나 회복 시작까지의 대기 시간(초)")]
    public float staminaRegenDelay = 3f;

    [Tooltip("초당 스태미나 회복량")]
    public float staminaRegenRate = 20f;

    private PlayerStatsController playerStats;
    private IPlayerController playerController; // IPlayerController 인터페이스 사용
    private Coroutine regenCoroutine;
    private float lastStaminaValue;

    void Start()
    {
        playerStats = GetComponent<PlayerStatsController>();
        // IPlayerController "자격증"을 가진 컴포넌트를 찾음 (Player3Controller든 뭐든 상관 없음)
        playerController = GetComponent<IPlayerController>(); 
        
        if (playerStats == null || playerController == null)
        {
            Debug.LogError("PlayerStats 또는 IPlayerController를 구현한 컴포넌트를 찾을 수 없습니다!");
            this.enabled = false; // 컴포넌트 비활성화
            return;
        }

        lastStaminaValue = playerStats.currentStamina;

        TryStartRegeneration();
    }

    void Update()
    {
        if (playerStats.currentStamina < lastStaminaValue)
        {
            TryStartRegeneration();
        }

        lastStaminaValue = playerStats.currentStamina;
    }

    private void TryStartRegeneration()
    {
        if (regenCoroutine != null)
        {
            StopCoroutine(regenCoroutine);
        }

        regenCoroutine = StartCoroutine(RegenerateStamina());
    }

    private IEnumerator RegenerateStamina()
    {
        yield return new WaitForSeconds(staminaRegenDelay);

        while (playerStats.currentStamina < playerStats.maxStamina)
        {
            // 인터페이스의 IsWallClimbing 속성을 사용하여 벽 타기 상태 확인
            if (!playerController.IsWallClimbing)
            {
                playerStats.RecoverStamina(staminaRegenRate * Time.deltaTime);
            }
            yield return null; // 다음 프레임까지 대기
        }

        regenCoroutine = null;
    }
}
