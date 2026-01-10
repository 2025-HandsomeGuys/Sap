using UnityEngine;

public class TerrainParticleManager : MonoBehaviour
{
    // 어디서든 접근할 수 있게 싱글톤으로 만듭니다.
    public static TerrainParticleManager Instance;

    [Header("컴포넌트 연결")]
    public ParticleSystem debrisSystem; // 인스펙터에서 드래그해서 넣어주세요

    [Header("설정")]
    [Range(0f, 1f)]
    public float spawnChance = 0.0001f; // 픽셀 1개당 파티클이 나올 확률 (0.3 = 30%)
                                     // 1.0으로 하면 너무 많아서 렉이 걸릴 수 있어요.

    void Awake()
    {
        // 싱글톤 초기화
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void SpawnDebris(Vector2 worldPos, Color32 color)
    {
        // 확률 체크
        if (Random.value > spawnChance) return;

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();

        emitParams.position = worldPos;
        emitParams.startColor = color;

        // [수정 1] 크기 줄이기 (기존보다 작게, 예를 들어 0.1 ~ 0.2 정도)
        emitParams.startSize = Random.Range(0.03f, 0.08f);

        // [수정 2] 수명 줄이기 (0.5초 ~ 1.0초 뒤에 사라지게)
        emitParams.startLifetime = Random.Range(0.5f, 2.0f);

        // (선택) 튀는 속도 조절 (너무 멀리 안 튀게 하려면 범위를 줄이세요)
        emitParams.velocity = new Vector3(Random.Range(-1f, 1f), Random.Range(1f, 3f), 0);

        debrisSystem.Emit(emitParams, 1);
    }
}