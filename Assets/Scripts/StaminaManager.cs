using UnityEngine;
using System.Collections;

[RequireComponent(typeof(PlayerStats))]
public class StaminaManager : MonoBehaviour
{
    [Header("Stamina Regeneration Settings")]
    [Tooltip("스태미나 회복 시작까지의 대기 시간(초)")]
    public float staminaRegenDelay = 3f;

    [Tooltip("초당 스태미나 회복량")]
    public float staminaRegenRate = 20f;

    private PlayerStats playerStats;
    private PlayerController playerController; // PlayerController 참조 추가
    private Coroutine regenCoroutine;
    private float lastStaminaValue;

    void Start()
    {
        playerStats = GetComponent<PlayerStats>();
        playerController = GetComponent<PlayerController>(); // 컴포넌트 가져오기
        if (playerStats == null)
        {
            Debug.LogError("PlayerStats 컴포넌트를 찾을 수 없습니다!");
            this.enabled = false; // 컴포넌트 비활성화
            return;
        }

        lastStaminaValue = playerStats.currentStamina;

        // 게임 시작 시 스태미나가 가득 차 있지 않을 경우를 대비해 바로 회복 코루틴 시작
        TryStartRegeneration();
    }

    void Update()
    {
        // 스태미나가 사용되었는지 감지 (현재 스태미나가 이전 프레임보다 낮아졌을 때)
        if (playerStats.currentStamina < lastStaminaValue)
        {
            TryStartRegeneration();
        }

        // 현재 스태미나 값을 다음 프레임을 위해 저장
        lastStaminaValue = playerStats.currentStamina;
    }

    private void TryStartRegeneration()
    {
        // 이미 실행 중인 회복 코루틴이 있다면 중지
        if (regenCoroutine != null)
        {
            StopCoroutine(regenCoroutine);
        }

        // 새로운 회복 코루틴 시작
        regenCoroutine = StartCoroutine(RegenerateStamina());
    }

    private IEnumerator RegenerateStamina()
    {
        // 1. 설정된 시간만큼 대기
        yield return new WaitForSeconds(staminaRegenDelay);

        // 2. 스태미나가 최대치에 도달할 때까지 매 프레임 회복
        while (playerStats.currentStamina < playerStats.maxStamina && !playerController.isWallClimbing)
        {
            // PlayerStats에 있는 회복 함수를 호출
            playerStats.RecoverStamina(staminaRegenRate * Time.deltaTime);
            yield return null; // 다음 프레임까지 대기
        }

        // 코루틴 완료 후 null로 초기화
        regenCoroutine = null;
    }
}
