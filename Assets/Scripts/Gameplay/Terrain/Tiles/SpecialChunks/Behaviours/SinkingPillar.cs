using UnityEngine;

/// <summary>
/// 용암 점프맵에서 기둥을 밟으면 천천히 가라앉고,
/// 플레이어가 떠난 뒤 유예 시간이 지나면 다시 천천히 떠오르는 기믹.
/// 상승 속도는 하강 속도보다 느리게 설정한다.
///
/// 플레이어 운반·접지(IsTouchingLayers) 유지를 위해 Kinematic Rigidbody2D를
/// FixedUpdate에서 MovePosition으로 이동시킨다. transform 직접 이동은
/// 콜라이더가 순간이동해 플레이어 접촉이 끊기고(점프 불가) 운반도 되지 않는다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class SinkingPillar : MonoBehaviour
{
    [Header("Sink / Rise")]
    [Tooltip("플레이어가 밟고 있을 때 가라앉는 속도 (유닛/초)")]
    public float sinkSpeed = 1.0f;

    [Tooltip("플레이어가 떠난 뒤 떠오르는 속도 (유닛/초). 보통 하강보다 느리게.")]
    public float riseSpeed = 0.5f;

    [Tooltip("최대로 가라앉는 거리 (유닛). 너무 크면 시야 밖으로 묻혀 사라진 것처럼 보인다.")]
    public float maxSinkDistance = 2.0f;

    [Tooltip("플레이어가 밟은 뒤 하강을 시작하기까지의 대기 시간 (초)")]
    public float sinkDelay = 0.5f;

    [Tooltip("플레이어가 떠난 뒤 상승을 시작하기까지의 유예 시간 (초)")]
    public float riseDelay = 1.0f;

    [Header("Detection")]
    public string playerTag = "Player";

    private Rigidbody2D _rb;
    private Rigidbody2D _playerRb;   // 밟고 있는 플레이어 (sleep 방지용)
    private Vector2 _startPos;

    // 0 = 원위치(상단), maxSinkDistance = 최대로 가라앉은 상태
    private float _sinkAmount = 0f;

    // 플레이어 콜라이더 접촉 수 (솔리드+트리거 동시 사용 안전)
    private int _contactCount = 0;

    // 플레이어가 떠난 뒤 남은 상승 유예 시간
    private float _graceTimer = 0f;

    // 플레이어가 밟은 뒤 남은 하강 대기 시간
    private float _sinkDelayTimer = 0f;

    private bool _initialized = false;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        // 무빙 플랫폼은 반드시 Kinematic. 매 프레임 transform로 옮기는 Static 콜라이더는
        // 물리 캐시를 재빌드하고 플레이어를 운반하지 못한다.
        _rb.bodyType = RigidbodyType2D.Kinematic;
        _rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        // JSON 설정 적용 (specialChunkSettings.json > sinkingPillar)
        if (SpecialChunkSettingsLoader.Instance != null)
        {
            var s = SpecialChunkSettingsLoader.Instance.Settings.sinkingPillar;
            sinkSpeed       = s.sinkSpeed;
            riseSpeed       = s.riseSpeed;
            maxSinkDistance = s.maxSinkDistance;
            sinkDelay       = s.sinkDelay;
            riseDelay       = s.riseDelay;
        }
    }

    private void Start()
    {
        _startPos = _rb.position;
        _initialized = true;
    }

    private void FixedUpdate()
    {
        bool playerOn = _contactCount > 0;
        float dt = Time.fixedDeltaTime;

        if (playerOn)
        {
            // 밟고 있는 동안 플레이어가 잠들면 OnCollisionExit가 누락돼 영영 내려간 채로
            // 멈춘다 → 강제로 깨워 이탈 감지를 보장한다.
            if (_playerRb != null) _playerRb.WakeUp();

            if (_sinkDelayTimer > 0f)
            {
                // 밟은 직후 잠깐 대기 후 하강 시작
                _sinkDelayTimer -= dt;
            }
            else
            {
                // 밟고 있는 동안 가라앉음
                _sinkAmount = Mathf.Min(maxSinkDistance, _sinkAmount + sinkSpeed * dt);
            }
        }
        else if (_graceTimer > 0f)
        {
            // 유예 시간 동안은 정지
            _graceTimer -= dt;
        }
        else
        {
            // 유예 종료 후 천천히 상승
            _sinkAmount = Mathf.Max(0f, _sinkAmount - riseSpeed * dt);
        }

        // MovePosition: 스윕 이동으로 위 플레이어와의 접촉을 유지한다.
        _rb.MovePosition(_startPos + Vector2.down * _sinkAmount);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag(playerTag)) AddContact(collision.rigidbody);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag(playerTag)) RemoveContact();
    }

    // 상단에 트리거를 배치하는 방식 지원
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(playerTag)) AddContact(other.attachedRigidbody);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag(playerTag)) RemoveContact();
    }

    private void AddContact(Rigidbody2D playerRb)
    {
        if (_contactCount == 0)
        {
            if (playerRb != null) _playerRb = playerRb;
            // 새로 밟기 시작 → 하강 대기 타이머 시작
            _sinkDelayTimer = sinkDelay;
        }
        _contactCount++;
    }

    private void RemoveContact()
    {
        _contactCount = Mathf.Max(0, _contactCount - 1);
        // 마지막 접촉이 떨어지는 순간 유예 타이머 시작
        if (_contactCount == 0)
        {
            _graceTimer = riseDelay;
            _playerRb = null;
        }
    }

    private void OnEnable()
    {
        // 청크 풀링 대비 초기화
        _sinkAmount = 0f;
        _contactCount = 0;
        _graceTimer = 0f;
        _sinkDelayTimer = 0f;
        _playerRb = null;
        if (_initialized) _rb.position = _startPos;
    }
}
