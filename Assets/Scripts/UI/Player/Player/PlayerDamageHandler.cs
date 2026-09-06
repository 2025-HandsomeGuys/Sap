using System.Collections;
using UnityEngine;

public class PlayerDamageHandler : MonoBehaviour
{
    [Header("피격 설정")]
    public Vector2 knockbackForce = new Vector2(3f, 5f);
    public float invincibilityDuration = 1.5f;
    public float blinkInterval = 0.1f;
    public float knockbackDuration = 0.2f;

    private SpriteRenderer[] sprites;
    private PlayerController playerController;

    // ⚠️ 수정된 부분: private -> public { get; private set; } 으로 변경하여 
    // DamageDealer 스크립트에서 이 값을 읽을 수 있게(get) 허용합니다.
    public bool isInvincible { get; private set; } = false;

    void Start()
    {
        sprites = GetComponentsInChildren<SpriteRenderer>();
        playerController = GetComponent<PlayerController>();
    }

    public void TakeDamage(Vector2 hitPoint)
    {
        if (isInvincible) return;

        // 1. 방향 계산 (맞은 곳의 반대 방향)
        float dirX = transform.position.x > hitPoint.x ? 1f : -1f;
        Vector2 knockbackVel = new Vector2(dirX * knockbackForce.x, knockbackForce.y);

        // 2. 플레이어 컨트롤러의 넉백 함수 호출 (지상/공중 상관없이 강제로 속도 적용)
        if (playerController != null)
        {
            playerController.ApplyKnockback(knockbackVel, knockbackDuration);
        }

        // 3. 무적 & 깜빡임 실행
        StartCoroutine(DamageRoutine());
    }

    IEnumerator DamageRoutine()
    {
        isInvincible = true;
        float elapsedTime = 0f;

        while (elapsedTime < invincibilityDuration)
        {
            SetSpritesAlpha(0.3f);
            yield return new WaitForSeconds(blinkInterval);
            elapsedTime += blinkInterval;

            SetSpritesAlpha(1f);
            yield return new WaitForSeconds(blinkInterval);
            elapsedTime += blinkInterval;
        }

        SetSpritesAlpha(1f);
        isInvincible = false;
    }

    void SetSpritesAlpha(float alpha)
    {
        foreach (SpriteRenderer sr in sprites)
        {
            Color c = sr.color;
            c.a = alpha;
            sr.color = c;
        }
    }
}