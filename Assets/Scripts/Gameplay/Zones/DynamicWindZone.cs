// @tags: zone, wind, dynamic, push, pattern, particle, visualizer, izone-effect
using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// JSON 설정 기반으로 바람의 세기와 방향이 시간에 따라 변하는 IZoneEffect 구현체.
/// ZoneEffectTrigger와 함께 부착하여 사용합니다.
/// </summary>
public class DynamicWindZone : MonoBehaviour, IZoneEffect
{
    [Header("패턴 설정")]
    [Tooltip("windPatterns.json에 정의된 patternId 입력")]
    public string patternId = "Blizzard_Lvl1";

    [Header("이벤트 (시각 효과 연동용)")]
    [Tooltip("바람 상태가 변경될 때마다 호출됨. (방향, 현재세기)")]
    public UnityEvent<Vector2, float> OnWindStateChanged;

    [Tooltip("실제 바람 전환 몇 초 전에 파티클 방향을 미리 변경할지 (0 = 미리보기 없음)")]
    public float particlePreviewSeconds = 1.5f;
    [Tooltip("바람 전환 전 파티클에 미리 방향을 알릴 때 호출됨. (다음 방향, 예고 세기)")]
    public UnityEvent<Vector2, float> OnWindPreviewChanged;

    private WindPatternData _pattern;
    private Coroutine _windRoutine;
    
    // 현재 존에 있는 플레이어 추적
    private HashSet<PlayerController> _activePlayers = new HashSet<PlayerController>();

    private Vector2 _currentWindDirection;
    private float _currentWindStrength;

    private void Start()
    {
        _pattern = WindPatternLoader.Instance.GetPattern(patternId);
        if (_pattern != null && _pattern.phases != null && _pattern.phases.Count > 0)
        {
            _windRoutine = StartCoroutine(WindCycleRoutine());
        }
        else
        {
            Debug.LogWarning($"[DynamicWindZone] 패턴 '{patternId}'을(를) 로드하지 못했거나 페이즈가 비어있습니다. 작동 중지.");
        }
    }

    private IEnumerator WindCycleRoutine()
    {
        int phaseIndex = 0;
        while (true)
        {
            var phase = _pattern.phases[phaseIndex];

            // 1. 미풍 (Breeze / Warning)
            if (phase.breezeDuration > 0)
            {
                ApplyWind(phase.direction, phase.breezeStrength);
                yield return new WaitForSeconds(phase.breezeDuration);
            }

            // 2. 강풍 (Active)
            if (phase.activeDuration > 0)
            {
                ApplyWind(phase.direction, phase.activeStrength);
                yield return new WaitForSeconds(phase.activeDuration);
            }

            // 3. 휴식 (Rest)
            if (phase.restDuration > 0)
            {
                ApplyWind(Vector2.zero, 0f);

                int nextIndex = (phaseIndex + 1) % _pattern.phases.Count;
                float previewAt = phase.restDuration - particlePreviewSeconds;
                if (particlePreviewSeconds > 0f && previewAt > 0f)
                {
                    yield return new WaitForSeconds(previewAt);
                    var nextPhase = _pattern.phases[nextIndex];
                    OnWindPreviewChanged?.Invoke(nextPhase.direction.normalized, nextPhase.breezeStrength * 0.4f);
                    yield return new WaitForSeconds(particlePreviewSeconds);
                }
                else
                {
                    yield return new WaitForSeconds(phase.restDuration);
                }
            }

            // 다음 페이즈로
            phaseIndex = (phaseIndex + 1) % _pattern.phases.Count;
        }
    }

    private void ApplyWind(Vector2 direction, float strength)
    {
        _currentWindDirection = direction.normalized;
        _currentWindStrength = strength;
        
        Vector2 windVel = _currentWindDirection * _currentWindStrength;

        // 현재 존 안에 있는 모든 플레이어에게 바람 갱신
        foreach (var controller in _activePlayers)
        {
            if (controller != null)
            {
                controller.windVelocity = windVel;
            }
        }

        // 외부 파티클/UI 등에 상태 전달
        OnWindStateChanged?.Invoke(_currentWindDirection, _currentWindStrength);
    }

    public void OnEnter(PlayerStat player)
    {
        var controller = player.GetComponent<PlayerController>();
        if (controller == null) return;

        _activePlayers.Add(controller);
        
        // 들어올 때 현재 바람 상태 적용
        controller.windVelocity = _currentWindDirection * _currentWindStrength;
    }

    public void OnExit(PlayerStat player)
    {
        var controller = player.GetComponent<PlayerController>();
        if (controller == null) return;

        _activePlayers.Remove(controller);
        
        // 나갈 때 바람 영향 제거
        controller.windVelocity = Vector2.zero;
    }

    private void OnDisable()
    {
        if (_windRoutine != null)
        {
            StopCoroutine(_windRoutine);
            _windRoutine = null;
        }
        
        // 비활성화 시 모든 플레이어의 바람 강제 해제
        foreach (var controller in _activePlayers)
        {
            if (controller != null)
            {
                controller.windVelocity = Vector2.zero;
            }
        }
        _activePlayers.Clear();
    }
}
