using NUnit.Framework;
using Relic;

public class RelicBehaviourCloneTests
{
    // 테스트 전용 더미 behavior (값형 런타임 상태 보유)
    private class DummyRelic : RelicBehaviour
    {
        public int counter;
        public void Bump() => counter++;
    }

    [Test]
    public void Clone_ProducesIndependentInstance()
    {
        var origin = new DummyRelic { counter = 0 };
        var clone = (DummyRelic)origin.Clone();

        clone.Bump();   // 클론 상태만 변경

        Assert.AreEqual(1, clone.counter, "클론 상태가 변경돼야 함");
        Assert.AreEqual(0, origin.counter, "원본(SO 틀) 상태는 불변이어야 함");
        Assert.AreNotSame(origin, clone, "서로 다른 인스턴스여야 함");
    }

    [Test]
    public void TwoClones_AreIndependent()
    {
        var origin = new DummyRelic();
        var a = (DummyRelic)origin.Clone();
        var b = (DummyRelic)origin.Clone();

        a.Bump();

        Assert.AreEqual(1, a.counter);
        Assert.AreEqual(0, b.counter, "동일 유물 2슬롯 장착 시 상태 충돌 없어야 함");
    }
}
