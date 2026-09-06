// @tags: diagnostics, qa, chunk-seam, test
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using NUnit.Framework;
using GameDiagnostics;

/// <summary>
/// "F12를 안 눌렀는데 버그 리포트 창이 켜진다" 버그 회귀 방지.
/// 원인: 엘리베이터·스폰·던전 출입의 좌표 대입이 TUNNEL(심각)으로 잡혀
/// TerrainSeamWatchdog의 AutoCapture가 리포트를 자동으로 띄웠다.
/// </summary>
public class TerrainSeamWatchdogScriptedMoveTests
{
    private const float Dt = 0.02f;

    [Test]
    public void 텔레포트는_스크립트_이동으로_판정된다()
    {
        // 엘리베이터 층 이동: 정지 상태에서 수십 유닛 점프
        Assert.IsTrue(TerrainSeamWatchdog.IsScriptedMove(120f, 0f, Dt));
    }

    [Test]
    public void 낙하중_텔레포트도_스크립트_이동으로_판정된다()
    {
        // 속도가 있어도 vel*dt로 설명 안 되는 크기면 스크립트 이동
        Assert.IsTrue(TerrainSeamWatchdog.IsScriptedMove(50f, 30f, Dt));
    }

    [Test]
    public void 빠른_낙하는_물리_이동으로_판정된다()
    {
        // 30u/s * 0.02s = 0.6u — 실제 관통 후보이므로 걸러지면 안 된다
        Assert.IsFalse(TerrainSeamWatchdog.IsScriptedMove(0.6f, 30f, Dt));
    }

    [Test]
    public void 경계값_여유안의_이동은_물리로_본다()
    {
        // 여유(slack)만큼의 튐은 물리로 허용 — 오탐 방지용
        Assert.IsFalse(TerrainSeamWatchdog.IsScriptedMove(0.2f, 0f, Dt));
    }
}
#endif
