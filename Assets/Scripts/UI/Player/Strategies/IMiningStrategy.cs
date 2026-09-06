using UnityEngine;

public interface IMiningStrategy
{
    void Enter(PlayerMining context);
    void Exit();
    void HandleUpdate();
    void HandleFixedUpdate();
    void HandleLateUpdate();
    bool CanSwitchTool();
    float GetChargeRatio();
    bool IsCharging { get; }
    bool IsAttacking { get; }
    DigParameters GetDigParameters(float baseRadius, TileType targetTileType);
}

public struct DigParameters
{
    public bool CanDig;              // 채굴 가능 여부
    public float RadiusMultiplier;   // 범위 배율
    public float DamageMultiplier;   // 데미지 배율(고정 데미지 도구=곡괭이 돌 데미지에 적용). 유효 파라미터는 반드시 1f로 초기화(구조체라 미설정 시 0)
    public int ToolIndex;            // 도구 종류
    public bool CanDigTerrain;       // TerrainChunk(타일 지형) 파기 가능 여부
    public bool CanDigRock;          // DiggableRock(바위 오브젝트) 파기 가능 여부
    public bool IgnoreStaminaCost;   // 스태미나 소모 무시 (드릴 전용)

    // ── 채광 면허 게이트 (MiningLevelGate) ──
    // 판정 결과만 실어 나른다. 알림(OnBlocked 발화)은 실제로 판 지점에서
    // MiningLevelGate.Notify(in p)로 한다 — GetDigParameters는 스윙 한 번에 여러 번 불린다.
    public bool MiningLevelBlocked;  // 채광 레벨이 모자라 반경이 깎였는가
    public int RequiredMiningLevel;  // 그 층이 요구하는 채광 레벨
    public int CurrentMiningLevel;   // 플레이어의 현재 채광 레벨
    public TileType BlockedTileType; // 막힌 지점의 층
}
