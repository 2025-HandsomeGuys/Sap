// @tags: sound, heartbeat, stamina, audio, loop, monobehaviour, player
using UnityEngine;

/// <summary>
/// 스태미나가 낮을 때 심장 박동 루프를 재생한다.
///
/// 플레이어 루트에 직접 붙여도 되고, 오디오 컴포넌트를 모아둔 자식 오브젝트
/// (예: Player/Audio)에 붙여도 된다.
///
/// 비율이 threshold 아래로 내려가면 루프 시작, 위로 올라오면 정지.
/// 낮을수록 pitch를 올려(1.0 → 1.35) 박동이 빨라지는 느낌을 준다.
///
/// 게임오버·긴급탈출 연출 진입 시에는 그쪽에서 StopLoop(LoopHandle)로 즉시 끈다.
/// </summary>
public class HeartbeatSfx : MonoBehaviour
{
    /// <summary>SoundManager.Loop의 핸들. 외부(게임오버)에서 정지할 때 쓴다.</summary>
    public const string LoopHandle = "heartbeat";

    [Header("발동 조건")]
    [Tooltip("스태미나 비율이 이 값 미만이면 심장 소리가 시작된다.")]
    [SerializeField, Range(0.05f, 0.6f)] private float threshold = 0.25f;

    [Header("박동 가속")]
    [SerializeField] private float pitchAtThreshold = 1.0f;
    [SerializeField] private float pitchAtZero = 1.35f;

    [Header("참조 (비우면 런타임 탐색)")]
    [SerializeField] private StaminaManager staminaManager;

    private bool _active;

    private void Update()
    {
        var sm = SoundManager.Instance;
        if (sm == null) return;

        if (staminaManager == null)
        {
            // 부모 계층 우선(자기 자신 포함) → 자식 오브젝트에 붙여도 자기 플레이어 것을 잡는다.
            // 못 찾으면 씬 전체에서 탐색(플레이어 밖에 둔 경우 대비).
            // Unity 오브젝트라 ?? 대신 명시적 == null 비교를 쓴다(fake-null 우회 방지).
            staminaManager = GetComponentInParent<StaminaManager>();
            if (staminaManager == null)
                staminaManager = Object.FindFirstObjectByType<StaminaManager>();
            if (staminaManager == null) return;
        }

        var stats = staminaManager.PlayerStats;
        if (stats == null) return;

        float max = stats.MaxStamina;
        if (max <= 0f) return;
        float ratio = Mathf.Clamp01(stats.CurrentStamina / max);

        if (ratio < threshold)
        {
            if (!_active)
            {
                sm.Loop(LoopHandle, SfxKeys.PlayerHeartbeat);
                _active = true;
            }

            // threshold에서 0으로 갈수록 pitch 상승
            float k = threshold > 0f ? 1f - (ratio / threshold) : 1f;
            sm.SetLoopPitch(LoopHandle, Mathf.Lerp(pitchAtThreshold, pitchAtZero, k));
        }
        else if (_active)
        {
            sm.StopLoop(LoopHandle);
            _active = false;
        }
    }

    private void OnDisable()
    {
        if (_active && SoundManager.Instance != null)
            SoundManager.Instance.StopLoop(LoopHandle, 0.1f);
        _active = false;
    }
}
