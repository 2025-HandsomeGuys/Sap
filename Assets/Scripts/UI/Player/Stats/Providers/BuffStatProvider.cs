using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 버프/디버프 시스템의 StatProvider.
/// 소모 아이템, 환경 효과, 일시적 버프 등의 일시적 스탯 수정치를 관리.
/// 
/// AddBuff/RemoveBuff로 동적으로 Modifier를 추가/제거할 수 있음.
/// </summary>
public class BuffStatProvider : MonoBehaviour, IStatProvider
{
    /// <summary>
    /// 활성 버프 데이터
    /// </summary>
    [System.Serializable]
    public class ActiveBuff
    {
        public string buffId;
        public StatModifier modifier;
        public float duration;     // 0 이하면 영구
        public float remainingTime;

        public bool IsExpired => duration > 0f && remainingTime <= 0f;
    }

    private PlayerStat playerStat;
    private readonly List<ActiveBuff> _activeBuffs = new List<ActiveBuff>();
    private readonly List<StatModifier> _modifierCache = new List<StatModifier>();
    private bool _isDirty = true;

    private void Start()
    {
        playerStat = GetComponentInParent<PlayerStat>();
        if (playerStat == null) playerStat = FindFirstObjectByType<PlayerStat>();

        if (playerStat != null) playerStat.RegisterProvider(this);
    }

    private void OnDestroy()
    {
        if (playerStat != null) playerStat.UnregisterProvider(this);
    }

    private void Update()
    {
        bool anyExpired = false;

        for (int i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            var buff = _activeBuffs[i];
            if (buff.duration <= 0f) continue; // 영구 버프

            buff.remainingTime -= Time.deltaTime;
            if (buff.IsExpired)
            {
                _activeBuffs.RemoveAt(i);
                anyExpired = true;
            }
        }

        if (anyExpired)
        {
            _isDirty = true;
            if (playerStat != null) playerStat.MarkDirty();
        }
    }

    // ===================================================
    // 버프 추가/제거
    // ===================================================
    /// <summary>
    /// 버프 추가. duration이 0 이하면 영구 버프로 처리됨.
    /// </summary>
    public void AddBuff(string buffId, StatType statType, ModifierType modType, float value, float duration = 0f)
    {
        var buff = new ActiveBuff
        {
            buffId = buffId,
            modifier = new StatModifier(statType, modType, value, ModifierSource.Buff),
            duration = duration,
            remainingTime = duration
        };

        _activeBuffs.Add(buff);
        _isDirty = true;
        if (playerStat != null) playerStat.MarkDirty();
    }

    /// <summary>
    /// 특정 ID의 버프를 모두 제거
    /// </summary>
    public void RemoveBuff(string buffId)
    {
        int removed = _activeBuffs.RemoveAll(b => b.buffId == buffId);
        if (removed > 0)
        {
            _isDirty = true;
            if (playerStat != null) playerStat.MarkDirty();
        }
    }

    /// <summary>
    /// 모든 버프 제거
    /// </summary>
    public void ClearAllBuffs()
    {
        _activeBuffs.Clear();
        _isDirty = true;
        if (playerStat != null) playerStat.MarkDirty();
    }

    /// <summary>
    /// 특정 버프가 활성 중인지 확인
    /// </summary>
    public bool HasBuff(string buffId)
    {
        return _activeBuffs.Exists(b => b.buffId == buffId);
    }

    public IReadOnlyList<ActiveBuff> GetActiveBuffs() => _activeBuffs;

    // ===================================================
    // IStatProvider 구현
    // ===================================================
    public IReadOnlyList<StatModifier> GetModifiers()
    {
        if (_isDirty)
        {
            _modifierCache.Clear();
            foreach (var buff in _activeBuffs)
            {
                _modifierCache.Add(buff.modifier);
            }
            _isDirty = false;
        }
        return _modifierCache;
    }
}
