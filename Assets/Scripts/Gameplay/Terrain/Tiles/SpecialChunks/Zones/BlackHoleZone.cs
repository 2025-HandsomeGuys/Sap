// @tags: zone, black-hole, trigger, special-chunk, physics
using UnityEngine;

/// <summary>
/// 블랙홀 청크 트리거 존.
/// - 일반 객체: FixedUpdate에서 블랙홀 중심을 향해 지속적인 AddForce를 가함.
/// - 플레이어: BlackHoleHandler에 처리를 위임하여 중심 도달 시 데미지 및 넉백을 처리하게 함.
/// </summary>
public class BlackHoleZone : GravityZoneBase
{
    private SpecialChunkSettingsData.BlackHoleZoneData _data;
    private BlackHoleHandler _playerHandler;
    private Transform _centerPoint; // 블랙홀의 중심점

    protected override void Awake()
    {
        base.Awake();
        // 보통 이 스크립트가 붙은 오브젝트의 중심을 블랙홀의 눈(중심점)으로 사용합니다.
        _centerPoint = transform; 
    }

    private void Start()
    {
        // 런타임에 JSON 설정 데이터를 끌어옵니다.
        if (SpecialChunkSettingsLoader.Instance != null)
        {
            _data = SpecialChunkSettingsLoader.Instance.Settings.zones.blackHole;
        }
        else
        {
            Debug.LogWarning("[BlackHoleZone] Settings Loader를 찾을 수 없어 기본값을 사용합니다.");
            _data = new SpecialChunkSettingsData.BlackHoleZoneData();
        }
    }

    private void FixedUpdate()
    {
        if (!IsZoneActive() || _data == null) return;

        // 존 안에 들어온 모든 일반 물리 객체(돌멩이, 아이템 등)를 중심으로 빨아들입니다.
        foreach (var rb in _insideObjects)
        {
            if (rb == null) continue;
            
            // 물체 위치에서 중심점으로 향하는 방향 벡터
            Vector2 direction = ((Vector2)_centerPoint.position - rb.position).normalized;
            
            // 설정된 인력(pullForce)만큼 지속적으로 힘을 가합니다.
            // 거리에 따라 힘을 다르게 주고 싶다면 (예: 가까울수록 강하게) 여기서 공식을 수정할 수 있습니다.
            rb.AddForce(direction * _data.pullForce);
        }
    }

    // 블랙홀은 반중력과 다르게 켜지고 꺼지는 주기가 없으므로 항상 켜져 있음(true)으로 설정합니다.
    protected override bool IsZoneActive() => true;

    protected override bool TryHandlePlayerEnter(Collider2D other)
    {
        var handler = other.GetComponent<BlackHoleHandler>();
        if (handler != null)
        {
            _playerHandler = handler;
            // 핸들러에게 당겨야 할 중심점과 힘(데이터)을 전달해 줍니다.
            handler.Activate(this, _centerPoint, _data);
            return true;
        }
        return false;
    }

    protected override bool TryHandlePlayerExit(Collider2D other)
    {
        var handler = other.GetComponent<BlackHoleHandler>();
        if (handler != null)
        {
            _playerHandler = null;
            handler.Deactivate();
            return true;
        }
        return false;
    }

    protected override void ActivatePlayerHandler()
    {
        if (_playerHandler != null) _playerHandler.Activate(this, _centerPoint, _data);
    }

    protected override void DeactivatePlayerHandler()
    {
        if (_playerHandler != null) _playerHandler.Deactivate();
    }

    protected override void ApplyPhysicsToRigidbody(Rigidbody2D rb)
    {
        // 최초 진입 시 한 번만 가해지는 물리력이 없습니다.
        // 블랙홀의 힘은 FixedUpdate에서 매 프레임 지속적으로 가해지기 때문에 여기서는 비워둡니다.
    }

    protected override void RestorePhysicsFromRigidbody(Rigidbody2D rb)
    {
        // 빠져나갈 때 중력을 되돌려줄 필요가 없습니다. (gravityScale을 건드리지 않음)
    }

    protected override void ClearSavedPhysicsData()
    {
        _playerHandler = null;
    }
}
