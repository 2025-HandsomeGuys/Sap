// @tags: zone, anti-gravity, trigger, special-chunk, chunk, rigidbody
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 반중력 청크 트리거 존. 정상 → 무중력 → 역중력 순으로 위상을 순환한다.
/// - 플레이어: AntiGravityHandler.SetPhase / ExitZone 호출
/// - 비플레이어 Rigidbody2D: gravityScale 직접 변경 (원본 보관 후 복원)
/// - OnPhaseChanged: 전환 순간 / OnPhaseWarning: 전환 leadTime초 전 (배경 예고용)
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class AntiGravityZone : MonoBehaviour
{
    [Header("Phase Durations (초)")]
    [SerializeField] private float normalDuration  = 4f;
    [Tooltip("저중력(달) 위상 지속 시간. LowNormal·LowInverted 공통")]
    [SerializeField] private float lowDuration     = 3f;
    [SerializeField] private float invertedDuration = 3f;

    [Header("Warning")]
    [Tooltip("전환 몇 초 전부터 배경 예고를 시작할지")]
    [SerializeField] private float warningLeadTime = 1.5f;

    [Header("Physics")]
    [Tooltip("역중력 강도 배수")]
    [SerializeField] private float antiGravityMultiplier = 0.7f;
    [Tooltip("저중력(달) 위상의 중력 배수. 0=무중력, 1=원래 중력")]
    [SerializeField] private float lowGravityFactor = 0.3f;

    /// <summary>전환 순간. 새 위상 전달.</summary>
    public event Action<GravityPhase> OnPhaseChanged;
    /// <summary>전환 leadTime초 전. 곧 올 위상과 남은 시간 전달.</summary>
    public event Action<GravityPhase, float> OnPhaseWarning;

    private GravityPhase _phase = GravityPhase.Normal;
    private int _cycleIndex = 0;
    private AntiGravityHandler _playerHandler;
    private readonly Dictionary<Rigidbody2D, float> _savedGravityScales = new();
    private readonly HashSet<Rigidbody2D> _insideObjects = new();

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (!col.isTrigger)
        {
            Debug.LogWarning("[AntiGravityZone] Collider2D가 Trigger가 아닙니다. 자동으로 isTrigger = true 설정.");
            col.isTrigger = true;
        }
    }

    private void OnEnable() => StartCoroutine(CycleCo());

    private void OnDisable()
    {
        StopAllCoroutines();
        RestoreAll();
        _insideObjects.Clear();
        _playerHandler = null;
        _cycleIndex = 0;
        _phase = GravityPhase.Normal;
    }

    private float DurationOf(GravityPhase phase)
    {
        switch (phase)
        {
            case GravityPhase.LowNormal:
            case GravityPhase.LowInverted: return lowDuration;
            case GravityPhase.Inverted:    return invertedDuration;
            default:                       return normalDuration;
        }
    }

    private IEnumerator CycleCo()
    {
        while (true)
        {
            _phase = GravityPhases.Cycle[_cycleIndex];
            ApplyToAll();
            OnPhaseChanged?.Invoke(_phase);

            float duration = DurationOf(_phase);
            float lead = Mathf.Min(warningLeadTime, duration);
            float beforeWarning = duration - lead;
            if (beforeWarning > 0f) yield return new WaitForSeconds(beforeWarning);

            int nextIndex = GravityPhases.NextIndex(_cycleIndex);
            GravityPhase next = GravityPhases.Cycle[nextIndex];
            OnPhaseWarning?.Invoke(next, lead);
            if (lead > 0f) yield return new WaitForSeconds(lead);

            _cycleIndex = nextIndex;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var handler = other.GetComponent<AntiGravityHandler>();
        if (handler != null)
        {
            _playerHandler = handler;
            handler.SetPhase(_phase);
            return;
        }

        var rb = other.GetComponent<Rigidbody2D>();
        if (rb == null) return;

        _insideObjects.Add(rb);
        ApplyPhase(rb, _phase);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var handler = other.GetComponent<AntiGravityHandler>();
        if (handler != null)
        {
            _playerHandler = null;
            handler.ExitZone();
            return;
        }

        var rb = other.GetComponent<Rigidbody2D>();
        if (rb == null) return;

        _insideObjects.Remove(rb);
        RestoreGravity(rb);
    }

    private void ApplyToAll()
    {
        if (_playerHandler != null) _playerHandler.SetPhase(_phase);
        foreach (var rb in _insideObjects)
            if (rb != null) ApplyPhase(rb, _phase);
    }

    private void RestoreAll()
    {
        if (_playerHandler != null) _playerHandler.ExitZone();
        foreach (var rb in _insideObjects)
            if (rb != null) RestoreGravity(rb);
        _savedGravityScales.Clear();
    }

    private void ApplyPhase(Rigidbody2D rb, GravityPhase phase)
    {
        if (!_savedGravityScales.ContainsKey(rb))
            _savedGravityScales[rb] = rb.gravityScale;

        float original = _savedGravityScales[rb];
        if (phase == GravityPhase.Normal)
        {
            rb.gravityScale = original;
            return;
        }
        float baseScale = Mathf.Approximately(original, 0f) ? 1f : original;
        rb.gravityScale = GravityPhases.ScaleFor(phase, baseScale, antiGravityMultiplier, lowGravityFactor);
    }

    private void RestoreGravity(Rigidbody2D rb)
    {
        if (_savedGravityScales.TryGetValue(rb, out float saved))
        {
            rb.gravityScale = saved;
            _savedGravityScales.Remove(rb);
        }
    }
}
