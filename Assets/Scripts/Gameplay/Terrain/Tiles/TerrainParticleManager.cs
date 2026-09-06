// @tags: terrain, vfx, particle, manager, digging
using UnityEngine;

public class TerrainParticleManager : MonoBehaviour
{
    public static TerrainParticleManager Instance;

    [Header("컴포넌트 연결")]
    public ParticleSystem debrisSystem;

    [Header("스폰 설정")]
    [Range(0f, 1f)]
    public float spawnChance = 0.01f;

    [Header("Burst 설정")]
    [Range(1, 8)]
    public int burstCount = 3;          // 픽셀 1회 히트 시 추가로 방출할 파티클 수

    [Range(0f, 15f)]
    public float minSpeed = 3f;
    [Range(0f, 15f)]
    public float maxSpeed = 9f;

    [Range(0f, 3f)]
    public float lateralNoise = 1.2f;   // 방향에 수직한 랜덤 노이즈 범위

    [Header("역방향 파티클 설정")]
    [Range(0f, 1f)]
    public float backScatterRatio = 0.3f;   // 캐릭터 쪽으로 튀는 파티클 비율

    [Header("임팩트 Burst 설정")]
    public ParticleSystem impactSystem;     // 파기 중심 스파크용 (별도 파티클 시스템)
    [Range(0f, 15f)]
    public float impactMinSpeed = 5f;
    [Range(0f, 15f)]
    public float impactMaxSpeed = 14f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// 파티클 1 burst 발생.
    /// worldPos  : 파티클이 터질 월드 위치 (픽셀 위치)
    /// color     : 픽셀 색상
    /// digCenter : 파기 중심 월드 위치 (방향 계산 기준)
    /// playerPos : 플레이어 위치 (역방향 파티클 기준) — 생략 시 역방향 없음
    /// </summary>
    public void SpawnDebris(Vector2 worldPos, Color32 color, Vector2 digCenter, Vector2 playerPos = default)
    {
        if (debrisSystem == null) return;

        // ── 바깥 방향 계산 ─────────────────────────────────────────
        Vector2 outDir = worldPos - digCenter;
        if (outDir.sqrMagnitude < 0.0001f)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            outDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
        else
        {
            outDir.Normalize();
        }

        // ── 역방향 계산 (캐릭터 쪽) ────────────────────────────────
        bool hasPlayerPos = (playerPos != default);
        Vector2 backDir = hasPlayerPos ? (playerPos - worldPos).normalized : -outDir;

        // ── 파티클 burst 방출 ──────────────────────────────────────
        int backCount = hasPlayerPos ? Mathf.RoundToInt(burstCount * backScatterRatio) : 0;

        for (int i = 0; i < burstCount; i++)
        {
            bool isBack = (i < backCount);
            Vector2 baseDir = isBack ? backDir : outDir;
            Vector2 perp = new Vector2(-baseDir.y, baseDir.x);

            float speed   = Random.Range(minSpeed, maxSpeed);
            float lateral = Random.Range(-lateralNoise, lateralNoise);
            Vector2 vel   = baseDir * speed + perp * lateral;

            // 역방향은 위쪽 바이어스 강하게 (공중으로 튀어오르는 느낌)
            vel.y += isBack ? Random.Range(1.0f, 2.0f) : Random.Range(0.3f, 0.7f);
            // 역방향은 속도 약간 느리게
            if (isBack) vel *= 0.7f;

            var ep = new ParticleSystem.EmitParams();
            ep.position      = worldPos;
            ep.startColor    = color;
            ep.startSize     = Random.Range(0.03f, 0.08f);
            ep.startLifetime = Random.Range(0.25f, 0.7f);
            ep.velocity      = vel;

            debrisSystem.Emit(ep, 1);
        }
    }

    /// <summary>
    /// 파기 중심에서 전방향 임팩트 스파크 burst.
    /// toolIndex: 1=삽, 2=곡괭이, 3=드릴
    /// </summary>
    public void SpawnImpact(Vector2 digCenter, Color32 color, int toolIndex)
    {
        int count = toolIndex switch
        {
            1 => 8,   // 삽
            2 => 15,  // 곡괭이
            3 => 5,   // 드릴 (연속이라 작게)
            _ => 6
        };
        SpawnImpact(digCenter, color, count, impactMinSpeed, impactMaxSpeed);
    }

    public void SpawnImpact(Vector2 digCenter, Color32 color, int count, float minSpeed, float maxSpeed)
    {
        ParticleSystem sys = (impactSystem != null) ? impactSystem : debrisSystem;
        if (sys == null) return;

        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float speed = Random.Range(minSpeed, maxSpeed);

            var ep = new ParticleSystem.EmitParams();
            ep.position      = digCenter;
            ep.startColor    = color;
            ep.startSize     = Random.Range(0.05f, 0.12f);
            ep.startLifetime = Random.Range(0.15f, 0.4f);
            ep.velocity      = dir * speed;

            sys.Emit(ep, 1);
        }
    }

    // ── 하위 호환: digCenter 없이 호출하는 레거시 경로 ─────────────
    [System.Obsolete("digCenter를 전달하는 오버로드를 사용하세요.")]
    public void SpawnDebris(Vector2 worldPos, Color32 color)
    {
        // 방향 없이 랜덤 burst (하위 호환용)
        SpawnDebris(worldPos, color, worldPos + new Vector2(0, -1f));
    }
}