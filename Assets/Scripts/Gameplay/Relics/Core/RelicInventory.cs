using System;
using System.Collections.Generic;
using Relic.Data;

namespace Relic
{
    // 소유 유물·레벨·로드아웃의 단일 소스. 순수 C#(저장 왕복 테스트 용이).
    public class RelicInventory
    {
        public event Action OnChanged;

        private readonly Dictionary<RelicID, int> _owned = new Dictionary<RelicID, int>(); // id -> level
        // 기본 0칸. 유물 칸은 전부 업그레이드(RelicSlotUp)로 열린다 — RelicManager.BaseSlotCount 참고.
        private RelicID[] _loadout = System.Array.Empty<RelicID>();

        public int SlotCount => _loadout.Length;

        /// <summary>
        /// 유물 없이 완파한 돌 수 / 연 던전 상자 수(피티 카운터). 세이브에 함께 실린다.
        /// 드롭 판정부(<c>RelicDropRoller</c>)가 올리고 되돌린다 — 여기선 보관만 한다.
        /// </summary>
        public int RockDropPity  { get; set; }
        public int ChestDropPity { get; set; }

        /// <summary>보유 유물 열람용 (UI). id → 레벨.</summary>
        public IReadOnlyDictionary<RelicID, int> Owned => _owned;

        public void Grant(RelicID id)
        {
            if (id == RelicID.None) return;
            if (!_owned.ContainsKey(id))
            {
                _owned[id] = 1;      // 획득 시 Lv1
                // 도감 발견 기록(유물은 영구 보유라 획득 = 발견). CollectionCodex는 순수 static이라 안전.
                CollectionCodex.Discover(CodexCategory.Relic, id.ToString());
                OnChanged?.Invoke();
            }
        }

        /// <summary>
        /// 보유 해제. 장착 중이면 슬롯에서도 뺀다.
        /// 도감 발견 기록은 남긴다 — "본 적 있다"는 되돌릴 성질이 아니다.
        ///
        /// ⚠ 여기서 슬롯을 비우는 건 <b>안전망일 뿐</b>이다. 런타임 behavior 클론·스폰물 정리는
        /// 매니저만 할 수 있으므로 반드시 <see cref="RelicManager.RevokeAndUnequip"/>로 부를 것 —
        /// 이걸 직접 부르면 회수한 유물의 드론·빔이 그대로 남는다.
        /// 지금은 QA 콘솔(<c>relic off</c>)만 쓰지만, 유물을 잃는 연출이 생기면 그대로 쓰인다.
        /// </summary>
        public bool Revoke(RelicID id)
        {
            if (!_owned.Remove(id)) return false;

            for (int i = 0; i < _loadout.Length; i++)
                if (_loadout[i] == id) _loadout[i] = RelicID.None;

            OnChanged?.Invoke();
            return true;
        }

        public bool IsOwned(RelicID id) => _owned.ContainsKey(id);

        public int GetLevel(RelicID id) => _owned.TryGetValue(id, out var lv) ? lv : 0;

        // 상점 강화: 레벨 +1 (최대치 이하일 때만). 재화 차감은 호출측(상점)이 담당.
        public bool TryUpgrade(RelicID id, int maxLevel)
        {
            if (!_owned.TryGetValue(id, out var lv)) return false;
            if (lv >= maxLevel) return false;
            _owned[id] = lv + 1;
            OnChanged?.Invoke();
            return true;
        }

        public bool Equip(int slot, RelicID id)
        {
            if (slot < 0 || slot >= _loadout.Length) return false;
            if (id != RelicID.None && !IsOwned(id)) return false;
            // 같은 유물 중복 장착 방지
            for (int i = 0; i < _loadout.Length; i++)
                if (i != slot && _loadout[i] == id && id != RelicID.None)
                    return false;
            _loadout[slot] = id;
            OnChanged?.Invoke();
            return true;
        }

        public void Unequip(int slot)
        {
            if (slot < 0 || slot >= _loadout.Length) return;
            if (_loadout[slot] == RelicID.None) return;
            _loadout[slot] = RelicID.None;
            OnChanged?.Invoke();
        }

        public RelicID GetEquipped(int slot)
            => (slot >= 0 && slot < _loadout.Length) ? _loadout[slot] : RelicID.None;

        /// <summary>칸 수를 맞춘다. <b>0도 유효</b>하다(업그레이드 전 상태).
        /// 줄일 때 밀려나는 유물의 정리는 호출측(<c>RelicManager.SyncUpgradeSlotCount</c>) 책임이다.</summary>
        public void SetSlotCount(int count)
        {
            if (count < 0 || count == _loadout.Length) return;
            var next = new RelicID[count];
            for (int i = 0; i < count; i++)
                next[i] = (i < _loadout.Length) ? _loadout[i] : RelicID.None;
            _loadout = next;
            OnChanged?.Invoke();
        }

        // ── 저장 왕복 ──
        public RelicSaveData ToSaveData()
        {
            var data = new RelicSaveData
            {
                hasData = true,
                slotCount = _loadout.Length,
                owned = new List<OwnedRelic>(),
                loadout = new List<RelicID>(_loadout),
                rockDropPity = RockDropPity,
                chestDropPity = ChestDropPity
            };
            foreach (var kv in _owned)
                data.owned.Add(new OwnedRelic(kv.Key, kv.Value));
            return data;
        }

        public void LoadFrom(RelicSaveData data)
        {
            _owned.Clear();
            RockDropPity = 0;
            ChestDropPity = 0;

            if (data == null || !data.hasData)
            {
                _loadout = System.Array.Empty<RelicID>();
                OnChanged?.Invoke();
                return;
            }

            RockDropPity  = data.rockDropPity;
            ChestDropPity = data.chestDropPity;

            if (data.owned != null)
                foreach (var o in data.owned)
                    if (o.id != RelicID.None) _owned[o.id] = o.level;

            // 세이브의 칸 수는 참고값일 뿐이다 — 원본은 업그레이드고,
            // 로드 직후 RelicManager.SyncUpgradeSlotCount가 다시 맞춘다.
            int count = data.slotCount > 0 ? data.slotCount : 0;
            _loadout = new RelicID[count];
            for (int i = 0; i < count; i++)
                _loadout[i] = (data.loadout != null && i < data.loadout.Count) ? data.loadout[i] : RelicID.None;

            OnChanged?.Invoke();
        }
    }
}
