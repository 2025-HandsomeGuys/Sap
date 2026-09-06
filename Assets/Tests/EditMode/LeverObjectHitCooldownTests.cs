using NUnit.Framework;
using UnityEngine;

/// <summary>
/// LeverObject 연속 타격 중복 방지 쿨다운 테스트.
/// 곡괭이 홀드 자동 공격(0.3s 간격)이 가까운 레버를 한 클릭에 두 번 토글하는 버그 회귀 방지.
/// EditMode에서는 Time.time이 테스트 내내 사실상 동일하므로,
/// 연속 Dig 호출 = "쿨다운 이내 재타격" 시나리오가 된다.
/// </summary>
public class LeverObjectHitCooldownTests
{
    private GameObject _go;
    private LeverObject _lever;

    [SetUp]
    public void SetUp()
    {
        _go = new GameObject("Lever_Test");
        _go.AddComponent<Animator>(); // Awake의 GetComponent<Animator>() 대상
        _lever = _go.AddComponent<LeverObject>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_go);
    }

    [Test]
    public void Dig_WithinCooldown_TogglesOnlyOnce()
    {
        Assert.IsFalse(_lever.isOn);

        _lever.Dig(Vector2.zero, 1f, 2); // 1타: ON
        _lever.Dig(Vector2.zero, 1f, 2); // 쿨다운 이내 2타: 무시되어야 함

        Assert.IsTrue(_lever.isOn, "쿨다운 이내 연속 타격 시 두 번째 Dig는 무시되어 ON을 유지해야 한다");
    }

    [Test]
    public void Dig_AfterCooldown_TogglesAgain()
    {
        _lever.hitCooldown = 0f; // 쿨다운 해제 = 시간 경과 시뮬레이션

        _lever.Dig(Vector2.zero, 1f, 2); // ON
        _lever.Dig(Vector2.zero, 1f, 2); // OFF

        Assert.IsFalse(_lever.isOn, "쿨다운이 지난 뒤의 타격은 정상적으로 다시 토글되어야 한다");
    }

    [Test]
    public void HitCooldown_Default_CoversPickaxeAutoRepeatInterval()
    {
        // PickaxeStrategy._attackCooldown(0.3s)보다 길어야 홀드 2연타를 차단한다.
        Assert.Greater(_lever.hitCooldown, 0.3f);
    }
}
