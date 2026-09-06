// @tags: climb, wallclimb, mining, upgrade, unlock, gate

using UnityEngine;

/// <summary>
/// '벽타기 중 채굴'의 업그레이드 배선 한 곳.
///
/// 이 노드를 사기 전에는 매달린 채로 삽·곡괭이를 쓸 수 없다 —
/// 벽타기에 들어가면 예전처럼 전략이 <c>EmptyStrategy</c>로 비워지고 도구 교체도 잠긴다.
/// 시설 노드(<see cref="MapUnlockGate"/>)와 같은 방식으로 <see cref="UpgradeEffectType"/>가
/// 아니라 <b>nodeId 문자열</b>로 걸린다 — 켜고 끄는 스위치라 수치로 표현할 게 없다.
///
/// ⚠ <c>UpgradeManager.Instance</c>를 쓰지 않는다. 그 프로퍼티는 매니저가 없으면 빈 오브젝트를
/// 새로 만들어 <c>DontDestroyOnLoad</c>로 박는데, 그게 씬의 진짜 매니저보다 먼저 생기면
/// 진짜 쪽이 중복으로 자폭해 <c>upgradeTree</c>가 통째로 날아간다(MapUnlockGate와 같은 이유).
///
/// ⚠ 조회 결과를 <see cref="CacheSeconds"/> 동안 캐시한다. 소비처가 채굴 전략의
/// <c>HandleUpdate</c>(매 프레임)라서, 캐시 없이 부르면 매 프레임 세이브 상태 조회가 돈다.
/// </summary>
public static class ClimbMiningUnlockGate
{
    /// <summary>벽타기 채굴 해금 노드. UpgradeTree.csv의 id와 문자열로 맞춰져 있다.</summary>
    public const string UnlockNodeId = "ClimbMiningUnlock_T0";

    /// <summary>캐시 수명(초). 노드를 산 직후 최대 이만큼 늦게 반영된다.</summary>
    private const float CacheSeconds = 0.25f;

    private static UpgradeManager _cachedManager;
    private static bool _cachedValue;
    private static float _cacheUntil = -1f;

    /// <summary>매달린 채로 캘 수 있는가. 매니저가 없으면 잠긴 것으로 본다.</summary>
    public static bool IsUnlocked
    {
        get
        {
            if (Time.unscaledTime < _cacheUntil) return _cachedValue;

            if (_cachedManager == null)
                _cachedManager = UnityEngine.Object.FindFirstObjectByType<UpgradeManager>();

            _cachedValue = _cachedManager != null && _cachedManager.IsNodeUnlocked(UnlockNodeId);
            _cacheUntil = Time.unscaledTime + CacheSeconds;
            return _cachedValue;
        }
    }

    /// <summary>캐시를 즉시 버린다. 세이브 로드·뉴게임처럼 상태가 통째로 갈릴 때 쓴다.</summary>
    public static void InvalidateCache()
    {
        _cachedManager = null;
        _cacheUntil = -1f;
    }
}
