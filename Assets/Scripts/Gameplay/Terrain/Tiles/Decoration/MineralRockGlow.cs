// @tags: rock, mineral, glow, vfx, particle, decoration
using UnityEngine;

/// <summary>
/// 광물돌의 항상발광 스파클 + 발광 헤일로를 구동한다. DiggableRock과 같은 GameObject에 부착.
/// Additive/Unlit 파티클이라 자체발광 → 어두운 동굴에서 도드라진다 (광량 감지 없음).
/// 파티클 렌더러가 어둠 오버레이(order 999) 위(order 1000+)에 그려지므로 어둠 속에서 빛난다.
/// 돌이 보이지 않는(렌더러 비활성) 동안에는 발광을 끈다.
/// 발광색은 이 컴포넌트가 직접 보유한다 (각 광물돌 프리팹에서 설정).
///
/// [발광 조건] DiggableRock.IsRevealed가 아니라 SpriteRenderer.enabled를 기준으로 한다.
/// preExposed 광물돌(땅에서 살짝 튀어나온 잼바위 등)은 RevealInTerrain()을 거치지 않아
/// _isRevealed가 false로 남지만 렌더러는 켜져 있다. enabled 기준이면 이 경우도 올바르게 발광한다.
/// (묻힘→OFF, preExposed/파서 노출→ON 세 경로 모두 일관)
/// </summary>
public class MineralRockGlow : MonoBehaviour
{
    [Header("발광")]
    [Tooltip("스파클(트윙클) 파티클 시스템. MineralSparkle 프리팹의 루트")]
    [SerializeField] private ParticleSystem sparkleSystem;

    [Tooltip("발광 헤일로(빛 웅덩이) 파티클 시스템. MineralSparkle 프리팹의 자식 GlowHalo (선택)")]
    [SerializeField] private ParticleSystem glowSystem;

    [Tooltip("이 광물돌의 발광색 (광물별로 직접 설정). 스파클·헤일로 startColor를 덮어씀")]
    [SerializeField] private Color glowColor = new Color(0.4f, 0.9f, 1f, 1f);

    private SpriteRenderer _sr;
    private bool _emitting;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        ApplyColor(sparkleSystem);
        ApplyColor(glowSystem);
        ApplyEmission(ShouldEmit());
    }

    private void Update()
    {
        bool want = ShouldEmit();
        if (want != _emitting)
            ApplyEmission(want);
    }

    // 돌이 실제로 화면에 보일 때(렌더러 활성)만 발광. DiggableRock이 모든 경로에서
    // SpriteRenderer.enabled를 정확히 토글하므로 preExposed/파서노출/복원 모두 일관 처리됨.
    private bool ShouldEmit() => _sr == null || _sr.enabled;

    private void ApplyColor(ParticleSystem ps)
    {
        if (ps == null) return;
        var main = ps.main;
        main.startColor = glowColor;
    }

    private void ApplyEmission(bool on)
    {
        _emitting = on;
        PlayOrStop(sparkleSystem, on);
        PlayOrStop(glowSystem, on);
    }

    private static void PlayOrStop(ParticleSystem ps, bool on)
    {
        if (ps == null) return;
        if (on) ps.Play();
        else    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void OnDisable()
    {
        // 풀 재사용/비활성 시 깔끔히 정리
        _emitting = false;
        if (sparkleSystem != null) sparkleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (glowSystem    != null) glowSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
