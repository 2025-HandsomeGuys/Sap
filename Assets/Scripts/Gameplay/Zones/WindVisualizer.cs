// @tags: wind, visual, particle, dynamic
using UnityEngine;

/// <summary>
/// DynamicWindZone의 OnWindStateChanged 이벤트에 연결하여
/// 파티클 시스템의 방향, 속도, 방출량을 조절하는 시각화 컴포넌트입니다.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class WindVisualizer : MonoBehaviour
{
    private ParticleSystem _ps;
    private DynamicWindZone _zone;

    private void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
    }

    private void Start()
    {
        // 프리팹 자식으로 배치된 경우 부모 계층에서 먼저 찾음
        _zone = GetComponentInParent<DynamicWindZone>();
        if (_zone == null)
            _zone = FindFirstObjectByType<DynamicWindZone>();

        if (_zone != null)
        {
            _zone.OnWindStateChanged.AddListener(UpdateWindVisual);
            _zone.OnWindPreviewChanged.AddListener(UpdateWindVisual);
        }
        else
            Debug.LogWarning("[WindVisualizer] DynamicWindZone을 찾지 못했습니다. Inspector에서 OnWindStateChanged 이벤트를 수동으로 연결하세요.");
    }

    private void OnDestroy()
    {
        if (_zone != null)
        {
            _zone.OnWindStateChanged.RemoveListener(UpdateWindVisual);
            _zone.OnWindPreviewChanged.RemoveListener(UpdateWindVisual);
        }
    }

    /// <summary>
    /// DynamicWindZone의 UnityEvent에 등록할 메서드입니다.
    /// </summary>
    public void UpdateWindVisual(Vector2 direction, float strength)
    {
        if (_ps == null) return;

        var main = _ps.main;
        var emission = _ps.emission;
        var velocity = _ps.velocityOverLifetime;

        if (strength <= 0.1f)
        {
            // 휴식 상태: 파티클 생성 중지
            emission.rateOverTime = 0f;
        }
        else
        {
            // 미풍 / 강풍 상태: 세기에 비례하여 파티클 방출량과 속도 증가
            float rate = strength * 30f; // 세기 5일 때 150개/초
            emission.rateOverTime = rate;

            main.startSpeed = strength;

            velocity.enabled = true;
            // 바람 방향으로 속도 가이딩
            velocity.x = direction.x * (strength * 1.5f);
            velocity.y = direction.y * (strength * 1.5f);
        }
    }
}
