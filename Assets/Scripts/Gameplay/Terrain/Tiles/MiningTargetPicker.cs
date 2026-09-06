// @tags: mining, dig, rock, target, physics, nonalloc
using UnityEngine;

/// <summary>
/// 파기 1회에 <see cref="IDiggable"/> 하나만 맞도록 대상을 고르는 선정기 +
/// 파기 계열 물리 쿼리의 NonAlloc 헬퍼.
///
/// **왜 필요한가.** 파기 호출부는 원 안에 걸린 콜라이더를 전부 훑고 있었다.
/// <c>PickaxeStrategy</c>·<c>SapStrategy</c>는 걸린 돌을 **전부** 때렸고(스태미나도 돌 개수만큼
/// 빠졌다), <c>Digger</c> 쪽은 첫 히트에서 return이라 하나만 때리긴 했지만 그 '첫'은
/// Physics2D가 돌려준 순서 — 보장이 없어서 가까운 돌이 아니었다.
///
/// **방향 판정을 따로 넣지 않는 이유.** 호출부의 digCenter가 이미
/// <c>플레이어 위치 + 바라보는 방향 × 반경</c>이다. 그 점에 가장 가까운 하나를 고르면
/// 그게 곧 "보는 방향에서 제일 가까운 돌"이다. 별도의 각도 비교는 중복이다.
///
/// **거리 기준이 transform이 아니라 <see cref="Collider2D.ClosestPoint"/>인 이유.**
/// 돌마다 크기가 제각각이라 중심 거리로 재면 코앞의 큰 돌 대신 멀리 있는 작은 돌이 뽑힌다.
/// 히트 애니메이션(<c>RockHitAnimator</c>)이 transform을 흔드는 동안 값이 어긋나는 문제도
/// 함께 피한다(CLAUDE.md §17).
/// </summary>
public static class MiningTargetPicker
{
    /// <summary>오버랩 한 번에 받아볼 콜라이더 최대 개수. 파기 반경(≈1유닛)에 이만큼 겹칠 일은 없다.</summary>
    public const int MaxHits = 64;

    /// <summary>
    /// <see cref="PickNearest"/> 전용 버퍼. **밖으로 절대 내보내지 않는다** —
    /// 선정이 끝난 뒤에야 <c>Dig()</c>가 불리므로, 그 안에서 폭발 등이 다시 오버랩을 돌려도
    /// 우리가 읽는 중인 배열을 덮어쓸 수 없다. 호출부가 버퍼를 들고 루프를 도는 구조로
    /// 되돌리지 말 것.
    /// </summary>
    private static readonly Collider2D[] s_pickBuffer = new Collider2D[MaxHits];

    /// <summary>
    /// 구 <c>OverlapCircleAll</c>/<c>CircleCastAll</c>과 결과가 같아지는 필터.
    /// useTriggers를 전역 설정에서 읽는 게 핵심이다 — <see cref="ContactFilter2D"/> 기본값은
    /// false라 그냥 두면 트리거 콜라이더(광물 등)가 통째로 안 잡힌다.
    /// </summary>
    public static ContactFilter2D DefaultFilter =>
        new ContactFilter2D { useTriggers = Physics2D.queriesHitTriggers };

    /// <summary>
    /// <c>Physics2D.OverlapCircleAll</c>의 NonAlloc 대체. 결과를 <paramref name="buffer"/>에 담고
    /// 개수를 돌려준다. 여러 대상을 다 훑어야 하는 곳(지형 청크)에서만 쓴다.
    /// </summary>
    public static int Overlap(Vector2 center, float radius, Collider2D[] buffer)
        => Physics2D.OverlapCircle(center, radius, DefaultFilter, buffer);

    /// <summary>
    /// <paramref name="center"/> 반경 안에서 <paramref name="center"/>에 가장 가까운
    /// <see cref="IDiggable"/> 하나. 없으면 null. 겹친 돌이 몇 개든 하나만 돌아온다.
    /// </summary>
    public static IDiggable PickNearest(Vector2 center, float radius)
    {
        int count = Overlap(center, radius, s_pickBuffer);

        IDiggable best = null;
        float bestSurfaceSqr = float.MaxValue;
        float bestCenterSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider2D c = s_pickBuffer[i];
            if (c == null) continue;
            if (!c.TryGetComponent(out IDiggable diggable)) continue;

            float surfaceSqr = ((Vector2)c.ClosestPoint(center) - center).sqrMagnitude;

            // center가 콜라이더 안이면 ClosestPoint가 center를 그대로 돌려줘 거리가 0이 된다.
            // 겹친 돌 여러 개 안에 동시에 들어가 있으면 전부 0으로 동점 → 그때는 콜라이더
            // 중심까지의 거리로 가른다. 안 그러면 Physics2D 반환 순서(보장 없음)에 따라
            // 매 타격마다 대상이 바뀌어 손맛이 흔들린다.
            float centerSqr = ((Vector2)c.bounds.center - center).sqrMagnitude;

            bool better = surfaceSqr < bestSurfaceSqr
                          || (Mathf.Approximately(surfaceSqr, bestSurfaceSqr) && centerSqr < bestCenterSqr);
            if (!better) continue;

            bestSurfaceSqr = surfaceSqr;
            bestCenterSqr = centerSqr;
            best = diggable;
        }

        return best;
    }
}
