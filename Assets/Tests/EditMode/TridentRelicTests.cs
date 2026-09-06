using NUnit.Framework;
using UnityEngine;
using Relic;

// 삼지창 순수 기하 로직 검증 (코루틴·지형 파괴는 인게임 검증 영역).
public class TridentRelicTests
{
    [Test]
    public void Rotate_PreservesMagnitude()
    {
        Vector2 v = new Vector2(1.7f, -0.4f);
        Vector2 r = TridentRelic.Rotate(v, 20f);
        Assert.AreEqual(v.magnitude, r.magnitude, 1e-4f);
    }

    [Test]
    public void Rotate_PlusMinus_SymmetricAroundAim()
    {
        Vector2 aim = Vector2.right;
        Vector2 left  = TridentRelic.Rotate(aim, 20f);
        Vector2 right = TridentRelic.Rotate(aim, -20f);
        // 조준축 대칭: y 부호 반대, x 동일
        Assert.AreEqual(left.x, right.x, 1e-4f);
        Assert.AreEqual(left.y, -right.y, 1e-4f);
        // +20° 회전은 반시계(위쪽 = 좌 갈래)
        Assert.Greater(left.y, 0f);
    }

    [Test]
    public void Rotate_Zero_ReturnsSameDirection()
    {
        Vector2 v = new Vector2(0.6f, 0.8f);
        Vector2 r = TridentRelic.Rotate(v, 0f);
        Assert.AreEqual(v.x, r.x, 1e-5f);
        Assert.AreEqual(v.y, r.y, 1e-5f);
    }

    [Test]
    public void StampCount_CoversLength_WithStepSpacing()
    {
        // 길이 2.0, 반경 0.3, step비 0.7 → step=0.21, n=Ceil(2/0.21)=10 → 스탬프 11개(양 끝 포함)
        int n = TridentRelic.StampCount(2.0f, 0.3f, 0.7f);
        Assert.AreEqual(10, n);
        // 간격이 반경×비율 이하 → 캡슐에 구멍 없음
        Assert.LessOrEqual(2.0f / n, 0.3f * 0.7f + 1e-4f);
    }

    [Test]
    public void StampCount_ZeroDistance_IsZero()
    {
        Assert.AreEqual(0, TridentRelic.StampCount(0f, 0.3f, 0.7f));
    }

    [Test]
    public void DefaultBehaviour_IgnoresNonShovel()
    {
        var relic = new TridentRelic();
        // ctx 미주입 상태: 삽이 아니면 false, 삽이라도 ctx 없으면 false(안전)
        Assert.IsFalse(relic.TryOverrideTerrainDig(Vector2.zero, Vector2.right, 1f, 2)); // 곡괭이
        Assert.IsFalse(relic.TryOverrideTerrainDig(Vector2.zero, Vector2.right, 1f, 3)); // 드릴
        Assert.IsFalse(relic.TryOverrideTerrainDig(Vector2.zero, Vector2.right, 1f, 1)); // 삽 + ctx null
    }
}
