using UnityEngine;
using UnityEngine.Events;

public class LeverObject : MonoBehaviour, IPuzzleDiggable
{
    private Animator _animator;

    [Header("레버 상태")]
    public bool isOn = false;

    [Header("연결할 대상 (선택)")]
    [Tooltip("작동시킬 문의 Animator를 여기에 드래그 앤 드롭 하세요.")]
    public Animator targetDoorAnimator; // 🔥 문을 연결할 변수 추가

    [Header("레버 작동 이벤트 (선택사항)")]
    public UnityEvent onLeverTurnedOn;
    public UnityEvent onLeverTurnedOff;

    [Header("타격")]
    [Tooltip("연속 타격 중복 방지 쿨다운(초). 곡괭이 홀드 자동 공격 간격(0.3s)보다 길어야 " +
             "가까이서 칠 때 한 클릭에 두 번 토글되는 것을 막는다. (StarResetLever와 동일 패턴)")]
    public float hitCooldown = 0.4f;

    private float _lastHitTime = float.NegativeInfinity;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    public void Dig(Vector2 digCenter, float damage, int toolIndex)
    {
        if (Time.time - _lastHitTime < hitCooldown) return;
        _lastHitTime = Time.time;

        ToggleLever();
    }

    private void ToggleLever()
    {
        isOn = !isOn;

        if (isOn)
        {
            _animator.Play("LeverOn");

            // 🔥 문이 연결되어 있다면 DoorOpen 애니메이션 실행
            if (targetDoorAnimator != null)
            {
                targetDoorAnimator.Play("DoorOpen");
            }

            onLeverTurnedOn?.Invoke();
            Debug.Log("레버 작동 : ON");
        }
        else
        {
            _animator.Play("LeverOff");

            // 🔥 문이 연결되어 있다면 DoorClose 애니메이션 실행
            if (targetDoorAnimator != null)
            {
                targetDoorAnimator.Play("DoorClose");
            }

            onLeverTurnedOff?.Invoke();
            Debug.Log("레버 작동 : OFF");
        }
    }
}