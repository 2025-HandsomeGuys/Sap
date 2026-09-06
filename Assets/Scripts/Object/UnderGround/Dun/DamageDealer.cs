using UnityEngine;
using Gameplay.Dungeon.Traps; // TrapDamage 로직을 가져오기 위한 네임스페이스

public class DamageDealer : MonoBehaviour
{
    [Header("함정 설정")]
    public float damageAmount = 10f; // 깎을 데미지 수치

    // 1. 트리거 영역에 처음 들어온 순간
    private void OnTriggerEnter2D(Collider2D collider)
    {
        DealDamage(collider);
    }

    // 2. 트리거 영역 안에 겹쳐서 머물고 있는 동안 계속
    private void OnTriggerStay2D(Collider2D collider)
    {
        DealDamage(collider);
    }

    // 중복되는 데미지 처리 코드를 하나의 함수로 묶음
    private void DealDamage(Collider2D collider)
    {
        // 닿은 오브젝트의 태그가 Player인지 확인
        if (collider.CompareTag("Player"))
        {
            PlayerDamageHandler player = collider.GetComponent<PlayerDamageHandler>();

            if (player != null)
            {
                // ⚠️ 핵심: 플레이어가 무적 상태가 아닐 때만 데미지와 넉백을 적용합니다.
                // 안 그러면 Stay2D 때문에 1초에 60번씩 데미지가 들어갑니다!
                if (!player.isInvincible)
                {
                    // 1. 실제 데미지 적용 (부상 -> MaxStamina 감소 등)
                    TrapDamage.ApplyInjury(collider, damageAmount);

                    // 2. 피격 위치 계산 (트리거이므로 ClosestPoint 사용)
                    Vector2 hitPoint = collider.ClosestPoint(transform.position);

                    // 3. 넉백 및 무적(깜빡임) 코루틴 실행
                    player.TakeDamage(hitPoint);
                }
            }
        }
    }
}