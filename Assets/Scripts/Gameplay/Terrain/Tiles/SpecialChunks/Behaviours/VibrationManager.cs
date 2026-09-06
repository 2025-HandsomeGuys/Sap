// @tags: vibration, special-chunk, trap, hazard, manager, singleton
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 진동 신호를 반경 내 IVibrationReceiver에 전파하는 싱글턴 매니저.
///
/// SOLID:
///  - SRP: 진동 전파만 담당.
///  - OCP: 새 IVibrationReceiver 구현체(유리블록, 눈덩이 등)를 추가해도 이 클래스는 불변.
///  - DIP: IVibrationReceiver 인터페이스에만 의존.
///
/// 주의: 씬에 1개만 배치. DontDestroyOnLoad 불필요 — 수정동굴 씬 한정.
/// _hitBuffer 기본 32 — 동굴 내 종유석 최대 개수 고려.
/// </summary>
public class VibrationManager : MonoBehaviour
{
    public static VibrationManager Instance { get; private set; }

    [SerializeField] private float defaultImpactRadius = 3f;

    private readonly List<Collider2D> _hitBuffer = new List<Collider2D>(32);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // JSON 설정 적용
        if (SpecialChunkSettingsLoader.Instance != null)
            defaultImpactRadius = SpecialChunkSettingsLoader.Instance.Settings.physics.vibrationDefaultRadius;
    }

    /// <summary>pos 기준 radius 반경 내 모든 IVibrationReceiver에 OnVibration()을 전파한다.</summary>
    public void TriggerVibration(Vector3 pos, float radius)
    {
        int count = Physics2D.OverlapCircle(pos, radius, ContactFilter2D.noFilter, _hitBuffer);
        for (int i = 0; i < count; i++)
        {
            if (_hitBuffer[i] == null) continue;
            _hitBuffer[i].GetComponent<IVibrationReceiver>()?.OnVibration();
        }
    }

    /// <summary>defaultImpactRadius를 사용하는 오버로드.</summary>
    public void TriggerVibration(Vector3 pos) => TriggerVibration(pos, defaultImpactRadius);
}
