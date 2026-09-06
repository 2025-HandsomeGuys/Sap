// @tags: zone, physics, trigger, base, special-chunk
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 중력/물리장 등 특수 구역의 기반 클래스.
/// - 트리거 감지 및 구역 내 물체(Rigidbody2D) 관리
/// - 플레이어 감지 및 적용의 뼈대 제공
/// </summary>
[RequireComponent(typeof(Collider2D))]
public abstract class GravityZoneBase : MonoBehaviour
{
    protected readonly HashSet<Rigidbody2D> _insideObjects = new HashSet<Rigidbody2D>();

    protected virtual void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (!col.isTrigger)
        {
            Debug.LogWarning($"[{GetType().Name}] Collider2D가 Trigger가 아닙니다. 자동으로 isTrigger = true 설정.");
            col.isTrigger = true;
        }
    }

    protected virtual void OnDisable()
    {
        RestoreAll();
        _insideObjects.Clear();
    }

    protected virtual void OnTriggerEnter2D(Collider2D other)
    {
        // 1. 플레이어 감지 시 우선 처리
        if (TryHandlePlayerEnter(other)) return;

        // 2. 일반 물리 객체 감지
        var rb = other.GetComponent<Rigidbody2D>();
        if (rb == null) return;

        _insideObjects.Add(rb);
        if (IsZoneActive())
        {
            ApplyPhysicsToRigidbody(rb);
        }
    }

    protected virtual void OnTriggerExit2D(Collider2D other)
    {
        if (TryHandlePlayerExit(other)) return;

        var rb = other.GetComponent<Rigidbody2D>();
        if (rb == null) return;

        _insideObjects.Remove(rb);
        RestorePhysicsFromRigidbody(rb);
    }

    /// <summary>위상이 켜질 때 등 내부 모든 객체에 물리력을 재적용</summary>
    protected virtual void ApplyToAll()
    {
        ActivatePlayerHandler();
        foreach (var rb in _insideObjects)
        {
            if (rb != null) ApplyPhysicsToRigidbody(rb);
        }
    }

    /// <summary>위상이 꺼지거나 존이 비활성화될 때 내부 모든 객체를 원래대로 복구</summary>
    protected virtual void RestoreAll()
    {
        DeactivatePlayerHandler();
        foreach (var rb in _insideObjects)
        {
            if (rb != null) RestorePhysicsFromRigidbody(rb);
        }
        ClearSavedPhysicsData();
    }

    // --- 자식 클래스가 반드시 구현해야 하는 구체적인 물리/핸들러 로직 ---

    /// <summary>현재 존의 능력이 활성화 상태인지 반환 (상시 켜져있다면 true 반환)</summary>
    protected abstract bool IsZoneActive();

    /// <summary>플레이어 진입 처리. 플레이어라면 핸들러를 켜고 true 반환.</summary>
    protected abstract bool TryHandlePlayerEnter(Collider2D other);

    /// <summary>플레이어 이탈 처리. 플레이어라면 핸들러를 끄고 true 반환.</summary>
    protected abstract bool TryHandlePlayerExit(Collider2D other);

    /// <summary>플레이어가 존 내부에 있는 상태에서 존 능력이 켜질 때 핸들러 활성화</summary>
    protected abstract void ActivatePlayerHandler();

    /// <summary>플레이어가 존 내부에 있는 상태에서 존 능력이 꺼질 때 핸들러 비활성화</summary>
    protected abstract void DeactivatePlayerHandler();

    /// <summary>일반 물체에 물리력(중력 반전, 인력 등) 적용</summary>
    protected abstract void ApplyPhysicsToRigidbody(Rigidbody2D rb);

    /// <summary>일반 물체에 적용된 물리력 원상 복구 (gravityScale 원복 등)</summary>
    protected abstract void RestorePhysicsFromRigidbody(Rigidbody2D rb);

    /// <summary>저장된 물리력 백업 데이터 구조 초기화</summary>
    protected abstract void ClearSavedPhysicsData();
}
