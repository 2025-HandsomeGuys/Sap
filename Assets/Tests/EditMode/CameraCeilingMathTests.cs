using NUnit.Framework;

/// <summary>
/// CameraCeilingMath — 카메라 천장 클램프 수식 검증.
/// 상한선은 '화면 위쪽 가장자리'가 멈추는 월드 Y이므로,
/// 카메라 중심의 허용 최대값은 항상 (ceilingY - halfHeight)다.
/// </summary>
public class CameraCeilingMathTests
{
    private const float Tol = 1e-4f;

    [Test]
    public void BelowCeiling_PassesThroughUnchanged()
    {
        float result = CameraCeilingMath.ClampCenterY(-10f, 0f, 2.5f);
        Assert.AreEqual(-10f, result, Tol);
    }

    [Test]
    public void AboveCeiling_ClampsToCeilingMinusHalfHeight()
    {
        float result = CameraCeilingMath.ClampCenterY(5f, 0f, 2.5f);
        Assert.AreEqual(-2.5f, result, Tol);
    }

    [Test]
    public void ExactlyAtLimit_StaysPut()
    {
        float result = CameraCeilingMath.ClampCenterY(-2.5f, 0f, 2.5f);
        Assert.AreEqual(-2.5f, result, Tol);
    }

    [Test]
    public void ZoomedIn_AllowsCameraCenterHigher()
    {
        // 드릴 대시 줌(orthoSize 2.5 → 1.8): 화면이 작아지므로 중심은 더 위로 갈 수 있다.
        float normal = CameraCeilingMath.ClampCenterY(5f, 0f, 2.5f);
        float zoomed = CameraCeilingMath.ClampCenterY(5f, 0f, 1.8f);

        Assert.AreEqual(-1.8f, zoomed, Tol);
        Assert.Greater(zoomed, normal, "줌 인 시 카메라 중심 허용 높이가 더 높아야 한다");
    }

    [Test]
    public void NegativeCeiling_ClampsCorrectly()
    {
        float result = CameraCeilingMath.ClampCenterY(0f, -20f, 2.5f);
        Assert.AreEqual(-22.5f, result, Tol);
    }

    [Test]
    public void ScreenTopEdge_NeverExceedsCeiling()
    {
        // 어떤 입력이든 (결과 + halfHeight) <= ceilingY 를 만족해야 한다.
        float[] inputs = { -100f, -2.5f, 0f, 3.7f, 1000f };
        const float ceiling = 4f;
        const float half = 2.5f;

        foreach (float y in inputs)
        {
            float topEdge = CameraCeilingMath.ClampCenterY(y, ceiling, half) + half;
            Assert.LessOrEqual(topEdge, ceiling + Tol, $"입력 {y}에서 화면 상단이 천장을 넘었다");
        }
    }
}
