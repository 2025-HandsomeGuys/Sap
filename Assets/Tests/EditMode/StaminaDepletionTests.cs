using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 쓰러짐(OnStaminaDepleted) 발화 조건 검증.
///
/// 규칙은 하나다 — <b>MaxStamina가 0일 때만</b> 쓰러진다.
/// 현재 스태미나를 벽타기·채굴로 다 써서 0이 되는 건 죽음이 아니다(리젠으로 회복).
/// 배경: Assets/Docs/stamina-max-reduction-plan.md
/// </summary>
public class StaminaDepletionTests
{
    /// <summary>MaxStamina에 Flat 감소를 넣는 테스트용 프로바이더(StaminaManager 대역).</summary>
    private class ReductionProvider : IStatProvider
    {
        public float reduction;
        private readonly List<StatModifier> _mods = new List<StatModifier>();

        public IReadOnlyList<StatModifier> GetModifiers()
        {
            _mods.Clear();
            if (reduction > 0f)
                _mods.Add(new StatModifier(StatType.MaxStamina, ModifierType.Flat, -reduction, ModifierSource.Other));
            return _mods;
        }
    }

    private GameObject _go;
    private PlayerStat _stat;
    private int _depletedCount;

    [SetUp]
    public void Setup()
    {
        _go = new GameObject("PlayerStat");
        _stat = _go.AddComponent<PlayerStat>();
        _stat.SetBaseValue(StatType.MaxStamina, 100f);
        _stat.CurrentStamina = 100f;

        _depletedCount = 0;
        _stat.OnStaminaDepleted += () => _depletedCount++;
    }

    [TearDown]
    public void Teardown() => Object.DestroyImmediate(_go);

    [Test]
    public void UseStamina_ToZero_DoesNotTriggerGameOver()
    {
        _stat.UseStamina(100f);

        Assert.AreEqual(0f, _stat.CurrentStamina, 0.001f);
        Assert.AreEqual(100f, _stat.MaxStamina, 0.001f, "최대치는 그대로여야 한다");
        Assert.AreEqual(0, _depletedCount, "현재 스태미나를 다 쓴 것만으로는 쓰러지지 않는다");
    }

    [Test]
    public void UseStamina_ToZero_ThenRecovers()
    {
        _stat.UseStamina(100f);
        _stat.RecoverStamina(40f);

        Assert.AreEqual(40f, _stat.CurrentStamina, 0.001f);
        Assert.AreEqual(0, _depletedCount);
    }

    [Test]
    public void MaxStaminaReachesZero_TriggersGameOver()
    {
        var provider = new ReductionProvider();
        _stat.RegisterProvider(provider);

        provider.reduction = 100f;
        _stat.MarkDirty();
        _stat.ClampCurrentStamina();

        Assert.AreEqual(0f, _stat.MaxStamina, 0.001f);
        Assert.AreEqual(1, _depletedCount, "최대치가 0이 되면 쓰러진다");
    }

    /// <summary>
    /// 예전 코드가 놓치던 경로: 이미 지쳐(current 0) 있는 상태에서 최대치가 0까지 깎이는 경우.
    /// `currentStamina > max`가 false라 발화 자체가 없었다.
    /// </summary>
    [Test]
    public void MaxStaminaReachesZero_WhileAlreadyEmpty_StillTriggersGameOver()
    {
        _stat.UseStamina(100f);
        Assert.AreEqual(0, _depletedCount);

        var provider = new ReductionProvider();
        _stat.RegisterProvider(provider);
        provider.reduction = 100f;
        _stat.MarkDirty();
        _stat.ClampCurrentStamina();

        Assert.AreEqual(1, _depletedCount);
    }

    [Test]
    public void MaxStaminaZero_FiresOnlyOnce()
    {
        var provider = new ReductionProvider();
        _stat.RegisterProvider(provider);

        provider.reduction = 100f;
        _stat.MarkDirty();
        _stat.ClampCurrentStamina();
        _stat.ClampCurrentStamina();
        _stat.ClampCurrentStamina();

        Assert.AreEqual(1, _depletedCount, "최대치가 0으로 머무는 동안 연출이 여러 번 돌면 안 된다");
    }

    [Test]
    public void PartialReduction_DoesNotTriggerGameOver()
    {
        var provider = new ReductionProvider();
        _stat.RegisterProvider(provider);

        provider.reduction = 99f;
        _stat.MarkDirty();
        _stat.ClampCurrentStamina();

        Assert.AreEqual(1f, _stat.MaxStamina, 0.001f);
        Assert.AreEqual(1f, _stat.CurrentStamina, 0.001f, "현재치가 새 상한으로 클램프된다");
        Assert.AreEqual(0, _depletedCount);
    }

    /// <summary>기준값이 아직 0인 초기화 전 상태를 쓰러짐으로 오인하면 안 된다.</summary>
    [Test]
    public void UninitializedBaseValue_DoesNotTriggerGameOver()
    {
        _stat.SetBaseValue(StatType.MaxStamina, 0f);
        _stat.ClampCurrentStamina();

        Assert.AreEqual(0, _depletedCount);
    }

    /// <summary>회복 뒤에는 다시 쓰러질 수 있어야 한다(1회 발화 래치가 풀린다).</summary>
    [Test]
    public void AfterRecovery_CanTriggerGameOverAgain()
    {
        var provider = new ReductionProvider();
        _stat.RegisterProvider(provider);

        provider.reduction = 100f;
        _stat.MarkDirty();
        _stat.ClampCurrentStamina();
        Assert.AreEqual(1, _depletedCount);

        provider.reduction = 0f;   // 회복
        _stat.MarkDirty();
        _stat.ClampCurrentStamina();

        provider.reduction = 100f; // 다시 소진
        _stat.MarkDirty();
        _stat.ClampCurrentStamina();

        Assert.AreEqual(2, _depletedCount);
    }
}
