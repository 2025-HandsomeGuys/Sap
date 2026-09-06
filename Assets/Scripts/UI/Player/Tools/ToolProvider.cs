using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 현재 장착된 도구와 파는 타일 종류에 따라 동적 스탯 보너스를 제공합니다.
///
/// [인스펙터 연결 방법]
/// 1. toolDataPerIndex 배열에 ToolController의 도구 슬롯 순서(0:맨손, 1:삽, 2:곡괭이, 3:드릴)에 맞게
///    미리 만들어둔 ToolData 에셋(ScriptableObject)을 드래그해 넣습니다.
/// 2. playerStat에 PlayerStat 컴포넌트를 연결합니다.
/// 3. toolController에 ToolController 컴포넌트를 연결합니다.
/// </summary>
public class ToolProvider : MonoBehaviour, IStatProvider
{
    [Header("References")]
    public PlayerStat playerStat;
    public ToolController toolController;

    [Header("Tool Data per Index")]
    [Tooltip("0:맨손, 1:삽, 2:곡괭이, 3:드릴 순서로 각 도구의 ToolData 에셋을 넣어주세요.")]
    public ToolData[] toolDataPerIndex;

    // ─── 내부 상태 ────────────────────────────────────────
    private readonly List<StatModifier> _modifiers = new List<StatModifier>();
    private ToolData _cachedTool;
    private TileType _cachedTile;

    private void Awake()
    {
        if (playerStat != null)
        {
            playerStat.RegisterProvider(this);
        }
    }

    private void OnDestroy()
    {
        if (playerStat != null)
        {
            playerStat.UnregisterProvider(this);
        }
    }

    // ─── 현재 도구 반환 ────────────────────────────────────
    public ToolData GetCurrentToolData()
    {
        if (toolController == null) return null;

        int idx = toolController.currentToolIndex;
        if (toolDataPerIndex == null || idx >= toolDataPerIndex.Length) return null;

        return toolDataPerIndex[idx];
    }

    /// <summary>
    /// 전략 1 (삽/곡괭이): 클릭 직전 한 번 호출.
    /// 배율을 적용하고 → MarkDirty → 계산 → ClearModifiers 순서로 사용.
    /// </summary>
    public void ApplyAndLock(TileType targetTile)
    {
        ToolData tool = GetCurrentToolData();
        RebuildModifiers(tool, targetTile);
    }

    /// <summary>
    /// 전략 2 (드릴): 타일이 바뀔 때만 호출.
    /// 같은 도구+같은 타일이면 재계산 없이 빠르게 리턴.
    /// </summary>
    public void UpdateIfChanged(TileType targetTile)
    {
        ToolData tool = GetCurrentToolData();

        if (_cachedTool == tool && _cachedTile == targetTile) return; // 변화 없음

        RebuildModifiers(tool, targetTile);
    }

    /// <summary>
    /// 모디파이어 초기화 (타격이 끝났거나 드릴이 꺼졌을 때)
    /// </summary>
    public void ClearModifiers()
    {
        if (_modifiers.Count == 0) return;

        _cachedTool = null;
        _cachedTile = TileType.Empty;
        _modifiers.Clear();

        playerStat?.MarkDirty();
    }

    // ─── IStatProvider ─────────────────────────────────────
    public IReadOnlyList<StatModifier> GetModifiers()
    {
        return _modifiers;
    }

    // ─── 내부: 모디파이어 재생성 ───────────────────────────
    private void RebuildModifiers(ToolData tool, TileType tile)
    {
        _cachedTool = tool;
        _cachedTile = tile;
        _modifiers.Clear();

        if (tool != null)
        {
            var m = tool.GetMultiplier(tile);

            // 암석 데미지 배율
            if (m.damageMultiplier != 1.0f)
            {
                _modifiers.Add(new StatModifier(
                    StatType.RockDamageMultiplier,
                    ModifierType.Percent,
                    m.damageMultiplier,
                    ModifierSource.Equipment));
            }

            // 채굴 반경 배율
            if (m.radiusMultiplier != 1.0f)
            {
                _modifiers.Add(new StatModifier(
                    StatType.MiningRange,
                    ModifierType.Percent,
                    m.radiusMultiplier,
                    ModifierSource.Equipment));
            }
        }

        playerStat?.MarkDirty();
    }
}
