using System.Collections.Generic;

namespace Relic
{
    // 유물 스탯 modifier 집계. RelicManager가 소유하고 PlayerStat에 등록.
    // MonoBehaviour 아님(순수) — 등록/해제·MarkDirty는 RelicManager가 담당.
    public class RelicStatProvider : IStatProvider
    {
        private readonly Dictionary<string, StatModifier> _byKey = new Dictionary<string, StatModifier>();
        private readonly List<StatModifier> _cache = new List<StatModifier>();
        private bool _dirty = true;

        // 콜백: modifier가 바뀌면 RelicManager가 PlayerStat.MarkDirty() 호출하도록 통지
        public System.Action OnChanged;

        public void Set(string key, StatType statType, ModifierType modType, float value)
        {
            _byKey[key] = new StatModifier(statType, modType, value, ModifierSource.Relic);
            _dirty = true;
            OnChanged?.Invoke();
        }

        public void Clear(string key)
        {
            if (_byKey.Remove(key))
            {
                _dirty = true;
                OnChanged?.Invoke();
            }
        }

        public void ClearAll()
        {
            if (_byKey.Count == 0) return;
            _byKey.Clear();
            _dirty = true;
            OnChanged?.Invoke();
        }

        public IReadOnlyList<StatModifier> GetModifiers()
        {
            if (_dirty)
            {
                _cache.Clear();
                foreach (var kv in _byKey) _cache.Add(kv.Value);
                _dirty = false;
            }
            return _cache;
        }
    }
}
