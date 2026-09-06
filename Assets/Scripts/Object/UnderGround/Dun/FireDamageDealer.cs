using UnityEngine;
using Gameplay.Dungeon.Traps;

public class FireDamageDealer : MonoBehaviour
{
    [Header("불 데미지 설정")]
    [Tooltip("1초 동안 불에 머물렀을 때 깎일 총 데미지")]
    public float damagePerSecond = 10f;

    [Tooltip("데미지가 들어가는 간격 (0.1초면 1초에 10번 파파파팍 깎임)")]
    public float tickRate = 0.1f;

    // 다음 데미지가 들어갈 시간을 기록하는 변수
    private float _nextDamageTime = 0f;

    // 트리거 영역 안에 겹쳐서 머물고 있는 동안 매 프레임 실행
    private void OnTriggerStay2D(Collider2D collider)
    {
        // 닿은 오브젝트가 플레이어인지 확인
        if (collider.CompareTag("Player"))
        {
            // 현재 시간이 '다음 데미지를 줄 시간'에 도달했는지 확인
            if (Time.time >= _nextDamageTime)
            {
                // 1. 이번 틱(Tick)에 들어갈 데미지 계산 
                // (예: 1초에 10데미지, 0.1초 간격이면 한 번에 1씩 깎임)
                float damagePerTick = damagePerSecond * tickRate;

                // 2. 무적이나 넉백 무시하고 스태미나/피만 직접적으로 깎음
                TrapDamage.ApplyInjury(collider, damagePerTick);

                // 3. 다음 데미지 타이머 갱신
                _nextDamageTime = Time.time + tickRate;
            }
        }
    }
}