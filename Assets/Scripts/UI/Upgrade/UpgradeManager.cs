using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 업그레이드 시스템의 전반적인 로직을 담당하는 매니저입니다.
/// 노드 해금, 효과 계산, 데이터 동기화를 처리합니다.
/// </summary>
public class UpgradeManager : MonoBehaviour
{
    private static UpgradeManager _instance;
    private static bool _appQuitting = false;

    /// <summary>
    /// 디버그: 잠긴 노드를 눌러 사면 그 아래(선행) 노드까지 통째로 공짜로 열리는 치트 모드.
    /// 콘솔 명령 <c>upg</c>로 켠다.
    ///
    /// static 이라 세이브에 남지 않는다 — 잠긴 기능 하나를 보려고 트리를 밑에서부터
    /// 다 사야 하는 QA 병목을 없애기 위한 것이다.
    /// </summary>
    public static bool DebugChainUnlock = false;

    public static UpgradeManager Instance
    {
        get
        {
            if (_appQuitting) return null;

            if (_instance == null)
            {
                _instance = FindFirstObjectByType<UpgradeManager>();
                if (_instance == null)
                {
                    // 여기서 만들어진 인스턴스는 upgradeTree가 비어 있다 — _nodeCache가 텅 비므로
                    // GetNodeFromCache가 전부 null을 주고, 그러면 UpgradeStatProvider가 모디파이어를
                    // 하나도 못 만들어 산 업그레이드가 조용히 아무 효과도 못 낸다.
                    // Awake의 인계 로직이 뒤늦게 뜬 제대로 된 매니저에게 자리를 넘겨주지만,
                    // 그 전에 읽힌 값은 이미 틀려 있다. 씬에 매니저를 두는 게 맞다.
                    Debug.LogWarning("[UpgradeManager] 씬에 UpgradeManager가 없어 빈 인스턴스를 임시 생성한다. " +
                                     "upgradeTree가 없으므로 이 인스턴스로 조회한 업그레이드 효과는 전부 무시된다.");
                    GameObject go = new GameObject("UpgradeManager");
                    _instance = go.AddComponent<UpgradeManager>();
                }
            }
            return _instance;
        }
    }

    [Header("Data")]
    public UpgradeTreeSO upgradeTree; // 에디터에서 할당 필요

    // 런타임 캐싱용 (NodeID -> NodeSO)
    private Dictionary<string, UpgradeNodeSO> _nodeCache;

    // 이벤트
    public event System.Action OnUpgradeStateChanged; // 업그레이드 변경 시 알림

    void Awake()
    {
        // ⚠ 싱글턴 승자는 '먼저 뜬 쪽'이 아니라 '트리를 물고 있는 쪽'이다.
        //    upgradeTree가 비어 있으면 _nodeCache도 비고, 그러면 GetNodeFromCache가 모든 노드에
        //    null을 준다. UpgradeUI는 자기 인스펙터의 트리로 그리므로 구매는 정상으로 보이지만
        //    (골드도 나가고 해금 표시도 뜬다) UpgradeStatProvider가 노드를 못 찾아
        //    스탯 모디파이어를 하나도 못 만든다 — 산 업그레이드가 통째로 증발한다.
        //    부팅 씬(MainMenuScene)의 매니저에 트리가 안 물려 있어 실제로 이 사고가 났다(2026-08-30).
        if (_instance != null && _instance != this)
        {
            if (_instance.upgradeTree == null && upgradeTree != null)
            {
                Debug.LogWarning($"[UpgradeManager] 트리가 비어 있는 인스턴스('{_instance.gameObject.name}')를 " +
                                 $"'{gameObject.name}'이 인계한다. 먼저 뜨는 씬의 UpgradeManager에 " +
                                 "_UpgradeTree를 물려두면 이 경고가 사라진다.");
                // GameObject가 아니라 컴포넌트만 지운다 — 남의 오브젝트에 얹혀 있을 수 있다.
                Destroy(_instance);
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        // _instance == this로 들어오는 경우가 있다: Instance 게터가 Awake보다 먼저
        // FindFirstObjectByType으로 이걸 집어간 상황. 그때도 DontDestroyOnLoad는 걸어야 한다.
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        InitializeNodeCache();
    }

    private void OnApplicationQuit()
    {
        _appQuitting = true;
    }

    private void InitializeNodeCache()
    {
        _nodeCache = new Dictionary<string, UpgradeNodeSO>();
        if (upgradeTree != null && upgradeTree.allNodes != null)
        {
            foreach (var node in upgradeTree.allNodes)
            {
                if (node != null && !string.IsNullOrEmpty(node.nodeId))
                {
                    if (!_nodeCache.ContainsKey(node.nodeId))
                        _nodeCache.Add(node.nodeId, node);
                }
            }
        }
    }

    /// <summary>
    /// 노드 캐시에서 특정 ID의 노드를 반환합니다.
    /// </summary>
    public UpgradeNodeSO GetNodeFromCache(string nodeId)
    {
        if (_nodeCache != null && _nodeCache.TryGetValue(nodeId, out UpgradeNodeSO node))
            return node;
        return null;
    }

    /// <summary>
    /// 플레이어 데이터에서 현재 업그레이드 상태를 가져옵니다.
    /// </summary>
    public UpgradeTreeState GetState()
    {
        SaveManager saveManager = ResolveSaveManager();
        if (saveManager != null && saveManager.playerData != null)
        {
            return saveManager.playerData.upgradeTreeState;
        }
        return null;
    }

    // 노드 하나당 레벨·최대치·다음 가격을 각각 묻게 되면서 조회 횟수가 노드 수의 몇 배가 됐다.
    // FindFirstObjectByType은 씬 전수 검색이라 트리 UI를 한 번 갱신할 때마다 수백 번 돌게 된다.
    // 파괴된 오브젝트는 Unity의 == null이 true를 주므로, 씬이 바뀌면 알아서 다시 찾는다.
    private SaveManager _saveManagerCache;
    private PlayerStat _playerStatCache;

    private SaveManager ResolveSaveManager()
    {
        if (_saveManagerCache == null) _saveManagerCache = FindFirstObjectByType<SaveManager>();
        return _saveManagerCache;
    }

    private PlayerStat ResolvePlayerStat()
    {
        if (_playerStatCache == null) _playerStatCache = FindFirstObjectByType<PlayerStat>();
        return _playerStatCache;
    }

    /// <summary>
    /// 특정 효과의 현재 합산/곱산된 수치를 반환합니다.
    /// </summary>
    /// <param name="type">효과 타입</param>
    /// <param name="baseValue">기본값</param>
    public float GetStatValue(UpgradeEffectType type, float baseValue)
    {
        UpgradeTreeState state = GetState();
        if (state == null || upgradeTree == null) return baseValue;

        float multiplier = 1f;
        float additive = 0f;

        // 해금된 모든 노드를 순회하며 효과 적용
        // 더 최적화하려면 해금 시 미리 계산된 값을 캐싱할 수 있음
        foreach (string nodeId in state.unlockedNodeIds)
        {
            if (_nodeCache.TryGetValue(nodeId, out UpgradeNodeSO node))
            {
                if (node.effect != null && node.effect.type == type)
                {
                    // 레벨만큼 같은 효과를 거듭 적용한다 — 노드를 레벨 수만큼 따로 산 것과 결과가 같다
                    int level = Mathf.Max(1, state.GetLevel(nodeId));
                    if (node.effect.isPercentage)
                    {
                        multiplier *= Mathf.Pow(node.effect.value, level);
                    }
                    else
                    {
                        additive += node.effect.value * level;
                    }
                }
            }
        }

        // 기본 로직: (기본값 + 합연산) * 곱연산
        return (baseValue + additive) * multiplier;
    }

    /// <summary>
    /// 이 광물의 판매가에 더해질 업그레이드 보너스(골드).
    ///
    /// <see cref="UpgradeEffectType.MineralPriceUp"/> 노드 중 대상 광물이 일치하는 것만 더한다.
    /// <see cref="GetStatValue"/>로는 못 하는 이유: 그쪽은 효과 타입만 보고 대상을 안 본다.
    ///
    /// 전역 배율(MineralSellBonus)이 아니라 **광물별 가산**인 것이 핵심이다 —
    /// 배율은 무엇을 캐든 항상 이득이라 업그레이드 선택을 지배해 버리는데,
    /// 광물별 가산은 그 광물을 실제로 캐는 구간에서만 값어치가 있어
    /// "지금 무엇을 캐고 있나"와 자연스럽게 엮인다.
    /// </summary>
    public int GetMineralPriceBonus(MineralID mineralID)
    {
        if (mineralID == MineralID.None) return 0;

        UpgradeTreeState state = GetState();
        if (state == null || upgradeTree == null) return 0;

        float bonus = 0f;
        foreach (string nodeId in state.unlockedNodeIds)
        {
            if (!_nodeCache.TryGetValue(nodeId, out UpgradeNodeSO node)) continue;
            var eff = node.effect;
            if (eff == null || eff.type != UpgradeEffectType.MineralPriceUp) continue;
            if (!eff.HitsMineral(mineralID)) continue;

            bonus += eff.value * Mathf.Max(1, state.GetLevel(nodeId));
        }
        return Mathf.RoundToInt(bonus);
    }

    /// <summary>
    /// 노드를 해금할 수 있는지 확인합니다.
    /// </summary>
    public bool CanUnlock(UpgradeNodeSO node)
    {
        if (node == null) return false;
        
        UpgradeTreeState state = GetState();
        if (state == null) return false;

        // 최대 레벨 도달 (단일 레벨 노드면 = 이미 해금됨)
        if (IsMaxed(node)) return false;

        // 계층 잠금 확인: 해당 노드의 계층이 해금되지 않았으면 불가능
        if (!state.IsTierUnlocked(node.tier))
        {
            return false;
        }

        // 선행 노드 확인 (선행은 Lv1 이상이면 충족 — 레벨까지 맞출 필요는 없다)
        if (node.parentNodes != null)
        {
            foreach (var parent in node.parentNodes)
            {
                if (!state.IsUnlocked(parent.nodeId)) return false;
            }
        }

        // 비용 확인 (다음 레벨 가격)
        PlayerStat playerStats = ResolvePlayerStat();
        if (playerStats == null || playerStats.Gold < GetNextCost(node)) return false;

        return true;
    }

    /// <summary>
    /// 노드의 현재 레벨. 안 샀으면 0, 단일 레벨 노드는 해금 시 1.
    /// </summary>
    public int GetNodeLevel(string nodeId)
    {
        UpgradeTreeState state = GetState();
        return state != null ? state.GetLevel(nodeId) : 0;
    }

    /// <summary>노드의 현재 레벨.</summary>
    public int GetNodeLevel(UpgradeNodeSO node) => node != null ? GetNodeLevel(node.nodeId) : 0;

    /// <summary>최대 레벨까지 올렸는가.</summary>
    public bool IsMaxed(UpgradeNodeSO node)
    {
        if (node == null) return false;
        return GetNodeLevel(node) >= node.EffectiveMaxLevel;
    }

    /// <summary>
    /// 다음 레벨을 사는 데 드는 골드. 이미 최대면 마지막 레벨 가격을 돌려준다(표시용).
    /// </summary>
    public int GetNextCost(UpgradeNodeSO node)
    {
        if (node == null) return 0;
        int level = GetNodeLevel(node);
        int next = Mathf.Min(level + 1, node.EffectiveMaxLevel);
        return node.CostForLevel(Mathf.Max(1, next));
    }

    /// <summary>
    /// 이미 산 노드를 한 단계 더 올릴 수 있는가 (UI 강조용).
    /// </summary>
    public bool CanLevelUp(UpgradeNodeSO node)
    {
        if (node == null || !node.IsMultiLevel) return false;
        return GetNodeLevel(node) >= 1 && CanUnlock(node);
    }

    // upgrade_blocked 연타 중복 제거용. 노드가 같고 골드도 그대로면 같은 '욕구'다.
    private string _lastBlockedNodeId;
    private int _lastBlockedGold = -1;

    /// <summary>
    /// 노드를 해금합니다.
    ///
    /// 살 수 없는 노드를 넘겨도 된다 — false를 돌려주면서 <c>upgrade_blocked</c>를 남긴다.
    /// UI가 CanUnlock으로 미리 걸러 이 함수를 안 부르면 그 기록이 통째로 사라지므로,
    /// **호출부에서 막지 말고 여기로 넘길 것.**
    /// </summary>
    public bool UnlockNode(UpgradeNodeSO node)
    {
        if (!CanUnlock(node))
        {
            // 사고 싶었지만 못 산 것 = 욕구. 부족 금액 분포가 골드 커브를 정량화한다(설계 §3.3)
            //
            // ⚠ 이 분기는 2026-08-31까지 **도달 불가능한 코드**였다. 호출부 세 곳이
            //   전부 CanUnlock으로 먼저 막아서, 텔레메트리 98잠수 동안 upgrade_blocked가
            //   한 건도 안 찍혔다. 호출부를 열었으니 여기로 들어온다.
            //   (배경: Assets/Docs/economy/upgrade-balance-charter.md §6-A)
            if (node != null)
            {
                PlayerStat blockedStat = ResolvePlayerStat();
                int heldGold = blockedStat != null ? blockedStat.Gold : 0;
                int blockedCost = GetNextCost(node);

                // 같은 노드를 골드 변화 없이 연타하면 한 번만 남긴다.
                // 클릭은 반복되지만 '욕구'는 하나이고, 중복이 쌓이면 부족 금액
                // 분포가 연타 습관 쪽으로 끌려간다.
                if (_lastBlockedNodeId == node.nodeId && _lastBlockedGold == heldGold)
                    return false;
                _lastBlockedNodeId = node.nodeId;
                _lastBlockedGold = heldGold;

                Telemetry.Log(TelemetryEvents.UpgradeBlocked, TelemetryPayload.New()
                    .Add("node", node.nodeId)
                    .Add("cost", blockedCost)
                    .Add("level", GetNodeLevel(node) + 1)
                    .Add("gold", heldGold)
                    .Add("short_by", Mathf.Max(0, blockedCost - heldGold))
                    // 골드 부족과 선행조건 미충족을 섞으면 골드 커브 분석이 오염된다
                    .Add("reason", heldGold < blockedCost ? "gold" : "requirement"));
            }
            return false;
        }

        UpgradeTreeState state = GetState();
        PlayerStat playerStats = ResolvePlayerStat();

        // 비용 차감 (다음 레벨 가격)
        int paid = GetNextCost(node);
        playerStats.SpendGold(paid);
        DayEarningsLedger.Report(DayEarningsCategory.Upgrade, -paid);

        // 상태 업데이트 (레벨 +1, 첫 구매면 해금)
        int newLevel = state.AddLevel(node.nodeId);

        // 채광 레벨 효과 확인 및 계층 해금
        if (node.effect != null && node.effect.type == UpgradeEffectType.MiningLevel)
        {
            // 노드 자체의 tier가 0이고 MiningLevel이 1이면 Tier 1을 해금하는 방식
            // 혹은 효과 수치(value)를 기준으로 계층을 해금
            int nextTier = Mathf.RoundToInt(node.effect.value);
            state.UnlockTier(nextTier);
            PlayTierUnlockSfx();
            Debug.Log($"[UpgradeManager] 채광 레벨 업그레이드로 인한 계층 {nextTier} 해금!");
            
            // 서브퀘스트 해금 갱신
            if (QuestManager.Instance != null)
            {
                QuestManager.Instance.RefreshSubQuestUnlocks();
            }
        }

        // 저장
        SaveManager saveManager = ResolveSaveManager();
        if (saveManager != null)
        {
            saveManager.Save();
        }
        // 이벤트 발생
        OnUpgradeStateChanged?.Invoke();

        Telemetry.Log(TelemetryEvents.UpgradePurchased, TelemetryPayload.New()
            .Add("node", node.nodeId)
            .Add("cost", paid)
            .Add("level", newLevel));

        Debug.Log($"[UpgradeManager] 노드 해금 성공: {node.DisplayName}" +
                  (node.IsMultiLevel ? $" (Lv {newLevel}/{node.EffectiveMaxLevel})" : ""));
        return true;
    }

    /// <summary>
    /// 조건 검사 없이 nodeId를 즉시 해금합니다. 테스트 전용.
    /// level을 주면 다단계 노드를 그 레벨까지 바로 올린다(노드의 최대 레벨로 클램프).
    /// </summary>
    public void DebugForceUnlock(string nodeId, int level = 1)
    {
        UpgradeTreeState state = GetState();
        if (state == null)
        {
            Debug.LogWarning($"[UpgradeManager] DebugForceUnlock 실패: 플레이어 데이터 없음.");
            return;
        }

        var node = GetNodeFromCache(nodeId);
        int capped = Mathf.Max(1, level);
        if (node != null) capped = Mathf.Min(capped, node.EffectiveMaxLevel);

        state.SetLevel(nodeId, capped);
        OnUpgradeStateChanged?.Invoke();
        Debug.Log($"[UpgradeManager] [DEBUG] 강제 해금: {nodeId} (Lv {capped})");
    }

    /// <summary>
    /// 디버그: 이 노드와 그 아래(선행) 노드를 전부 해금합니다. 골드는 들지 않습니다.
    ///
    /// 선행부터(post-order) 열고, 도중에 채광 레벨 노드가 있으면 그 계층도 같이 연다.
    /// 계층 잠금은 선행 관계와 별개라(트리에 없는 노드가 계층을 여는 경우가 있다)
    /// 마지막에 목표 노드의 계층까지 한 번 더 보장한다 — 안 그러면 "다 열었는데
    /// 계층 잠김이라 여전히 못 산다"가 된다.
    /// </summary>
    /// <returns>새로 열린 노드 수</returns>
    public int DebugForceUnlockChain(UpgradeNodeSO node, bool levelUpTarget = true)
    {
        if (node == null) return 0;

        UpgradeTreeState state = GetState();
        if (state == null)
        {
            Debug.LogWarning("[UpgradeManager] DebugForceUnlockChain 실패: 플레이어 데이터 없음.");
            return 0;
        }

        int opened = 0;
        var visited = new HashSet<string>();
        UnlockAncestorsRecursive(node, state, visited, ref opened);

        // 목표 노드 자신 (다단계면 한 단계 올린다 — 이미 산 노드를 또 눌렀을 때의 기대 동작)
        int cur = state.GetLevel(node.nodeId);
        int next = (cur == 0) ? 1 : (levelUpTarget ? Mathf.Min(cur + 1, node.EffectiveMaxLevel) : cur);
        if (next != cur)
        {
            state.SetLevel(node.nodeId, next);
            if (cur == 0) opened++;
            ApplyTierEffectOf(node, state);
        }

        for (int t = 0; t <= node.tier; t++)
            if (!state.IsTierUnlocked(t)) state.UnlockTier(t);

        if (QuestManager.Instance != null) QuestManager.Instance.RefreshSubQuestUnlocks();

        SaveManager saveManager = ResolveSaveManager();
        if (saveManager != null) saveManager.Save();

        OnUpgradeStateChanged?.Invoke();
        Debug.Log($"[UpgradeManager] [DEBUG] 연쇄 해금: {node.nodeId} (새로 열린 노드 {opened}개)");
        return opened;
    }

    /// <summary>선행 노드를 더 아래쪽부터 Lv1로 연다. 순환·중복 방문은 visited로 막는다.</summary>
    private void UnlockAncestorsRecursive(UpgradeNodeSO node, UpgradeTreeState state,
                                          HashSet<string> visited, ref int opened)
    {
        if (node.parentNodes == null) return;

        foreach (var parent in node.parentNodes)
        {
            if (parent == null || string.IsNullOrEmpty(parent.nodeId)) continue;
            if (!visited.Add(parent.nodeId)) continue;

            UnlockAncestorsRecursive(parent, state, visited, ref opened);

            if (state.GetLevel(parent.nodeId) == 0)
            {
                state.SetLevel(parent.nodeId, 1);
                opened++;
                ApplyTierEffectOf(parent, state);
            }
        }
    }

    /// <summary>채광 레벨 노드면 그 노드가 여는 계층을 연다(UnlockNode의 정규 경로와 같은 규칙).</summary>
    private static void ApplyTierEffectOf(UpgradeNodeSO node, UpgradeTreeState state)
    {
        if (node.effect == null || node.effect.type != UpgradeEffectType.MiningLevel) return;
        state.UnlockTier(Mathf.RoundToInt(node.effect.value));
    }

    /// <summary>
    /// nodeId를 다시 잠급니다. 테스트 전용 — 해금 전 상태를 다시 보기 위한 되돌리기다.
    /// 이 노드에 의존하는 하위 노드는 건드리지 않는다(해금 판정이 노드별 독립이라 그대로 남는다).
    /// </summary>
    public bool DebugForceLock(string nodeId)
    {
        UpgradeTreeState state = GetState();
        if (state == null)
        {
            Debug.LogWarning($"[UpgradeManager] DebugForceLock 실패: 플레이어 데이터 없음.");
            return false;
        }
        if (state.unlockedNodeIds == null || !state.IsUnlocked(nodeId)) return false;
        state.SetLevel(nodeId, 0);

        OnUpgradeStateChanged?.Invoke();
        Debug.Log($"[UpgradeManager] [DEBUG] 강제 잠금: {nodeId}");
        return true;
    }

    /// <summary>
    /// 특정 노드가 해금되었는지 확인합니다.
    /// </summary>
    public bool IsNodeUnlocked(string nodeId)
    {
        UpgradeTreeState state = GetState();
        return state != null && state.IsUnlocked(nodeId);
    }

    /// <summary>
    /// 특정 계층이 해금되었는지 확인합니다.
    /// </summary>
    public bool IsTierUnlocked(int tier)
    {
        UpgradeTreeState state = GetState();
        return state != null && state.IsTierUnlocked(tier);
    }

    /// <summary>
    /// 계층을 해금합니다.
    /// </summary>
    public void UnlockTier(int tier)
    {
        UpgradeTreeState state = GetState();
        if (state != null)
        {
            state.UnlockTier(tier);
            PlayTierUnlockSfx();
            OnUpgradeStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// 계층 해금 확인음. 해금 경로가 두 개라(UnlockNode의 채광레벨 분기 / 외부 UnlockTier 호출)
    /// 양쪽에서 부른다. 노드 해금음(upgrade_unlock)과 키가 달라 스로틀에 서로 걸리지 않고,
    /// 계층 해금은 노드 해금보다 큰 사건이라 두 소리가 겹쳐 울리는 것이 의도다.
    /// </summary>
    private static void PlayTierUnlockSfx()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.UpgradeTier);
    }

    /// <summary>
    /// 노드의 상태를 반환합니다 (잠김, 해금가능, 해금됨).
    /// </summary>
    public NodeLockState GetNodeLockState(UpgradeNodeSO node)
    {
        if (node == null) return NodeLockState.Locked;

        UpgradeTreeState state = GetState();
        if (state == null) return NodeLockState.Locked;

        // 이미 해금됨. 다단계 노드는 최대 레벨에 닿아야 '완료'다 —
        // 아직 여유가 있고 지금 살 수 있으면 Unlockable로 알려 UI가 구매를 유도한다.
        if (state.IsUnlocked(node.nodeId))
        {
            if (!IsMaxed(node) && CanUnlock(node)) return NodeLockState.Unlockable;
            return NodeLockState.Unlocked;
        }

        // 계층이 잠김
        if (!state.IsTierUnlocked(node.tier))
        {
            return NodeLockState.TierLocked;
        }

        // 해금 가능한지 확인
        if (CanUnlock(node))
        {
            return NodeLockState.Unlockable;
        }

        return NodeLockState.Locked;
    }
}

/// <summary>
/// 노드의 잠금 상태를 나타냅니다.
/// </summary>
public enum NodeLockState
{
    Locked,       // 잠김 (선행 조건 미충족)
    TierLocked,   // 계층 잠김
    Unlockable,   // 해금 가능
    Unlocked      // 해금됨
}
