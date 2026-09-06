// @tags: special-chunk, digging, entity, cable, mineral
using UnityEngine;

/// <summary>
/// IDiggable을 구현한 구리 전선 엔티티.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
[RequireComponent(typeof(BoxCollider2D))]
public class CableEntity : MonoBehaviour, IDiggable
{
    [Header("케이블 설정")]
    [Tooltip("파괴되기까지의 체력 (타격 횟수)")]
    public float maxHp = 3f;
    
    [Tooltip("연결될 다음 케이블 또는 끝점 Transform")]
    public Transform connectedTarget;
    
    [Tooltip("타격 시 재생될 스파크 파티클 시스템")]
    public ParticleSystem sparkParticle;

    private LineRenderer _line;
    private BoxCollider2D _collider;
    private float _currentHp;
    private float _lastHitTime; // 연타 방지 쿨타임

    private void Awake()
    {
        if (SpecialChunkSettingsLoader.Instance != null)
            maxHp = SpecialChunkSettingsLoader.Instance.Settings.entities.cable.maxHp;
        _currentHp = maxHp;
        _line = GetComponent<LineRenderer>();
        _collider = GetComponent<BoxCollider2D>();
        _collider.isTrigger = false;

        if (_line != null && _line.sharedMaterial == null)
        {
            _line.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _line.material.color = Color.yellow;
        }

        if (_line != null && connectedTarget != null)
        {
            UpdateLine();
            UpdateCollider();
        }
    }

    private void Update()
    {
        // 타겟이 움직이거나 파괴될 경우를 대비해 매 프레임 위치 업데이트
        if (_line != null && connectedTarget != null)
        {
            UpdateLine();
            UpdateCollider();
        }
        else if (_line != null && connectedTarget == null)
            _line.enabled = false; // 타겟이 사라지면 선을 숨김
    }

    private void UpdateLine()
    {
        if (_line.positionCount < 2) _line.positionCount = 2;
        _line.SetPosition(0, transform.position);
        _line.SetPosition(1, connectedTarget.position);
    }

    /// <summary>
    /// BoxCollider2D를 선의 중점·길이·각도에 맞게 업데이트.
    /// Physics2D.OverlapCircleAll이 케이블을 감지할 수 있게 됨.
    /// </summary>
    private void UpdateCollider()
    {
        Vector2 start = transform.position;
        Vector2 end = connectedTarget.position;
        Vector2 mid = (start + end) * 0.5f;
        float length = Vector2.Distance(start, end);
        float angle = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg;

        // collider는 자신의 로컬 좌표계 기준 → 월드 중점을 로컬로 변환
        _collider.offset = transform.InverseTransformPoint(mid);
        _collider.size = new Vector2(length, 0.3f); // 두께 0.3 (필요시 조정)
        transform.localRotation = Quaternion.Euler(0, 0, angle);
    }

    public void Dig(Vector2 worldPos, float radius, int toolIndex)
    {
        Debug.Log($"[CableEntity] 상호작용 감지: {gameObject.name}");
        if (Time.time - _lastHitTime < 0.2f) return;
        _lastHitTime = Time.time;

        _currentHp -= 1f; // 도구 상관없이 1타격당 1 대미지

        if (sparkParticle != null) 
            sparkParticle.Play();
            
        Debug.Log($"[CableEntity] 피격: 남은 HP {_currentHp}/{maxHp}");
        
        if (_currentHp <= 0) Die();
    }

    private void Die()
    {
        if (_line != null) 
            _line.enabled = false;
            
        Debug.Log("[CableEntity] 파괴 → 선 끊어짐");
        Destroy(gameObject);
    }
}
