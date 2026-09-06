// @tags: special-chunk, hazard, falling, icicle, vibration
using UnityEngine;

/// <summary>
/// 천장 고드름 — 진동 수신 시 돌가루 예고 후 낙하. 착지 시 연쇄 진동 전파.
/// 낙하/예고/착지 로직은 FallingHazardBase가 담당. 본 클래스는 JSON 설정만 적용.
/// 피격 없음(OnLanded 오버라이드 안 함).
/// </summary>
public class IcicleHazard : FallingHazardBase
{
    protected override void LoadSettings()
    {
        if (SpecialChunkSettingsLoader.Instance == null) return;

        var s        = SpecialChunkSettingsLoader.Instance.Settings;
        impactRadius = s.physics.icicleImpactRadius;
        warningDelay = s.physics.fallWarningDelay;
    }
}
