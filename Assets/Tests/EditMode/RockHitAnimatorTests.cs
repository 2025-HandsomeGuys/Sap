// @tags: rock, hit, animation, test, editmode
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// RockHitAnimator의 base ∘ clip 합성 규칙 회귀 테스트.
///
/// 지키려는 것: 돌의 배치 회전(RockLayoutCalculator가 확정하고 마스크·세이브가 물려 있는 값)이
/// 히트 애니메이션 때문에 사라지지 않는다.
/// </summary>
public class RockHitAnimatorTests
{
    private const float Eps = 0.001f;

    private static readonly Vector3    BasePos   = new Vector3(3.2f, -1.5f, 0f);
    private static readonly Quaternion BaseRot   = Quaternion.Euler(0f, 0f, -73f);
    private static readonly Vector3    BaseScale = new Vector3(1.2f, 1.2f, 1f);

    [Test]
    public void Compose_IdentityClip_ReturnsBaseUnchanged()
    {
        // 클립이 아무것도 애니메이트하지 않은 프레임(= Update가 되돌려 놓은 원점 상태).
        // 이때 결과가 base와 정확히 같아야 흔들림이 끝난 뒤 돌이 제자리에 선다.
        RockHitAnimator.Compose(
            BasePos, BaseRot, BaseScale,
            Vector3.zero, Quaternion.identity, Vector3.one,
            out Vector3 pos, out Quaternion rot, out Vector3 scale);

        Assert.AreEqual(BasePos.x, pos.x, Eps);
        Assert.AreEqual(BasePos.y, pos.y, Eps);
        Assert.AreEqual(0f, Quaternion.Angle(BaseRot, rot), Eps, "배치 회전이 보존돼야 한다");
        Assert.AreEqual(BaseScale.x, scale.x, Eps);
        Assert.AreEqual(BaseScale.y, scale.y, Eps);
    }

    [Test]
    public void Compose_PositionOffset_AddsToBase()
    {
        RockHitAnimator.Compose(
            BasePos, BaseRot, BaseScale,
            new Vector3(0.05f, -0.02f, 0f), Quaternion.identity, Vector3.one,
            out Vector3 pos, out _, out _);

        Assert.AreEqual(BasePos.x + 0.05f,  pos.x, Eps);
        Assert.AreEqual(BasePos.y + -0.02f, pos.y, Eps);
    }

    [Test]
    public void Compose_RotationOffset_StacksOnBaseRotation()
    {
        // 클립이 +10도 흔들면 최종 회전은 배치 회전 -73도에서 -63도가 돼야 한다.
        // (클립 값이 절대 회전으로 덮어써서 +10도가 되면 배치 회전이 소실된 것 — 이게 막으려는 회귀)
        RockHitAnimator.Compose(
            BasePos, BaseRot, BaseScale,
            Vector3.zero, Quaternion.Euler(0f, 0f, 10f), Vector3.one,
            out _, out Quaternion rot, out _);

        Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(0f, 0f, -63f), rot), Eps);
    }

    [Test]
    public void Compose_ScaleOffset_MultipliesBaseScale()
    {
        RockHitAnimator.Compose(
            BasePos, BaseRot, BaseScale,
            Vector3.zero, Quaternion.identity, new Vector3(1.1f, 0.9f, 1f),
            out _, out _, out Vector3 scale);

        Assert.AreEqual(1.2f * 1.1f, scale.x, Eps);
        Assert.AreEqual(1.2f * 0.9f, scale.y, Eps);
    }

    /// <summary>
    /// 2026-09-04 회귀: 흔들림이 진행 중인 프레임에 치명타가 들어오면
    /// DestroyRock()이 Update()가 비워 둔 원점 transform을 읽어
    /// 조각·효과음·광물이 청크 원점(예: 청크 (2,-7)의 월드 (20,-70))에 쏟아졌다.
    /// 파괴 직전 통지가 배치 확정값을 먼저 되돌려야 한다.
    /// </summary>
    [Test]
    public void OnRockBreaking_RestoresBasePlacement_WhenClipHasZeroedTransform()
    {
        var go   = new GameObject("Rock");
        var anim = go.AddComponent<RockHitAnimator>();
        anim.SetBase(BasePos, BaseRot, BaseScale);

        // Animator 평가 직전 Update()가 transform을 원점으로 되돌린 상태를 재현한다.
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;

        ((IRockHitReactor)anim).OnRockBreaking();

        Assert.AreEqual(BasePos.x, go.transform.localPosition.x, Eps, "파괴 좌표가 배치값으로 복원돼야 한다");
        Assert.AreEqual(BasePos.y, go.transform.localPosition.y, Eps);
        Assert.AreEqual(0f, Quaternion.Angle(BaseRot, go.transform.localRotation), Eps);
        Assert.AreEqual(BaseScale.x, go.transform.localScale.x, Eps);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void Compose_IsIdempotentAcrossFrames()
    {
        // Update가 매 프레임 원점으로 되돌리므로, 같은 클립 값이면 몇 프레임이 지나도
        // 결과가 같아야 한다. 발산(누적 곱)이 없다는 것을 고정한다.
        Vector3    clipPos   = new Vector3(0.03f, 0.01f, 0f);
        Quaternion clipRot   = Quaternion.Euler(0f, 0f, 5f);
        Vector3    clipScale = new Vector3(1.05f, 1.05f, 1f);

        RockHitAnimator.Compose(BasePos, BaseRot, BaseScale, clipPos, clipRot, clipScale,
            out Vector3 pos1, out Quaternion rot1, out Vector3 scale1);
        RockHitAnimator.Compose(BasePos, BaseRot, BaseScale, clipPos, clipRot, clipScale,
            out Vector3 pos2, out Quaternion rot2, out Vector3 scale2);

        Assert.AreEqual(pos1.x, pos2.x, Eps);
        Assert.AreEqual(0f, Quaternion.Angle(rot1, rot2), Eps);
        Assert.AreEqual(scale1.x, scale2.x, Eps);
    }
}
