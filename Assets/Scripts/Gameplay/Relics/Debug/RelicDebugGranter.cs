using UnityEngine;
using Relic.Data;

namespace Relic
{
    // 슬라이스 검증용 치트. bindings 배열로 "키 → 유물/슬롯"을 인스펙터에서 자유 지정.
    // 각 키: 첫 입력 = 지급 + 해당 슬롯 장착 / 재입력 = 레벨업.
    // 새 유물은 코드 수정 없이 인스펙터 bindings에 항목만 추가하면 됨(DefaultBindings는 초기값).
    public class RelicDebugGranter : MonoBehaviour
    {
        [System.Serializable]
        public struct Binding
        {
            public KeyCode key;
            public RelicID relic;
            public int slot;   // 장착 슬롯
        }

        [SerializeField] private RelicManager manager;

        [Tooltip("키 → 유물/슬롯 매핑. 인스펙터에서 자유롭게 편집. 첫 입력=지급/장착, 재입력=레벨업.")]
        [SerializeField] private Binding[] bindings = DefaultBindings();

        private void Start()
        {
            if (manager == null) manager = FindFirstObjectByType<RelicManager>();

            // 씬에 이미 있던 컴포넌트는 새 필드가 빈 배열로 역직렬화되어 초기값이 무시됨 → 폴백.
            if (bindings == null || bindings.Length == 0) bindings = DefaultBindings();
        }

        private void Update()
        {
            if (manager == null || bindings == null) return;

            for (int i = 0; i < bindings.Length; i++)
            {
                var b = bindings[i];
                if (b.key == KeyCode.None || b.relic == RelicID.None) continue;
                if (Input.GetKeyDown(b.key)) Apply(b);
            }
        }

        private void Apply(Binding b)
        {
            bool wasOwned = manager.Inventory.IsOwned(b.relic);
            if (!wasOwned)
            {
                manager.Inventory.Grant(b.relic);

                // 새로 입수한 유물이면 왼쪽 아래 획득 알림에 띄운다.
                var so = RelicDatabase.Instance != null ? RelicDatabase.Instance.GetRelicByID(b.relic) : null;
                if (so != null) AcquisitionNotifier.NotifyRelic(so);
            }

            // 유물 칸은 업그레이드(RelicSlotUp)로만 열린다 — 아직 안 샀으면 칸이 0개라
            // EquipSlot이 조용히 무시된다. 왜 아무 일도 안 일어나는지 알려준다.
            if (b.slot >= manager.Inventory.SlotCount)
            {
                Debug.LogWarning($"[Relic] {b.relic} 지급은 됐지만 슬롯{b.slot}이 없다 " +
                                 $"(열린 칸 {manager.Inventory.SlotCount}개). " +
                                 "콘솔에서 `node RelicSlot_T0_01`로 유물 칸을 먼저 열 것.");
            }
            // 아직 그 슬롯에 없으면 장착(이미 있으면 재장착 안 함 → 런타임 상태 보존)
            else if (manager.Inventory.GetEquipped(b.slot) != b.relic)
                manager.EquipSlot(b.slot, b.relic);

            if (wasOwned)
            {
                if (manager.Inventory.TryUpgrade(b.relic, MaxLevel(b.relic)))
                {
                    manager.RefreshLevel(b.relic);
                    Debug.Log($"[Relic] {b.relic} Lv{manager.Inventory.GetLevel(b.relic)}");
                }
            }
            else
            {
                Debug.Log($"[Relic] 슬롯{b.slot} → {b.relic} 장착 (재입력 시 Lv+)");
            }
        }

        private static int MaxLevel(RelicID id)
        {
            var so = RelicDatabase.Instance != null ? RelicDatabase.Instance.GetRelicByID(id) : null;
            return so != null ? so.maxLevel : 3;
        }

        // 인스펙터 초기값 — 기존 키 매핑을 그대로 이식. 인스펙터에서 얼마든지 수정 가능.
        private static Binding[] DefaultBindings() => new[]
        {
            new Binding { key = KeyCode.F6,      relic = RelicID.PigeonFeather,  slot = 0 },
            new Binding { key = KeyCode.F7,      relic = RelicID.Magnet,         slot = 1 },
            new Binding { key = KeyCode.F8,      relic = RelicID.TestStatRelic,  slot = 0 },
            new Binding { key = KeyCode.F5,      relic = RelicID.Anvil,          slot = 0 },
            new Binding { key = KeyCode.F2,      relic = RelicID.BlackMarket,    slot = 1 },
            new Binding { key = KeyCode.F1,      relic = RelicID.PlasmaCutter,   slot = 0 },
            new Binding { key = KeyCode.F3,      relic = RelicID.GamblerGlasses, slot = 0 },
            new Binding { key = KeyCode.F11,     relic = RelicID.Invincibility,  slot = 0 },
            new Binding { key = KeyCode.Keypad1, relic = RelicID.Steroid,        slot = 0 },
            new Binding { key = KeyCode.Keypad2, relic = RelicID.DrillDrone,     slot = 0 },
            new Binding { key = KeyCode.Keypad3, relic = RelicID.SpiderGlove,    slot = 0 },
            new Binding { key = KeyCode.Keypad4, relic = RelicID.Generator,      slot = 0 },
            new Binding { key = KeyCode.Keypad5, relic = RelicID.JunkSpring,     slot = 0 },
            new Binding { key = KeyCode.Keypad6, relic = RelicID.GravityFlip,    slot = 0 },
            new Binding { key = KeyCode.Keypad7, relic = RelicID.DashBomb,       slot = 0 },
            new Binding { key = KeyCode.Keypad8, relic = RelicID.Furnace,        slot = 0 },
            new Binding { key = KeyCode.Keypad9, relic = RelicID.Lightning,      slot = 0 },
            new Binding { key = KeyCode.Keypad0,    relic = RelicID.ToolSwap,    slot = 0 },
            new Binding { key = KeyCode.KeypadPlus,  relic = RelicID.MinerDrone,  slot = 0 },
            new Binding { key = KeyCode.KeypadMinus, relic = RelicID.Jetpack,     slot = 0 },
            new Binding { key = KeyCode.F12,     relic = RelicID.DetectionPulse, slot = 0 },
            new Binding { key = KeyCode.KeypadMultiply, relic = RelicID.OverloadBattery, slot = 1 },
            new Binding { key = KeyCode.KeypadDivide,   relic = RelicID.Mp3,            slot = 0 },
            new Binding { key = KeyCode.B,              relic = RelicID.Blind,          slot = 0 },
            new Binding { key = KeyCode.KeypadPeriod,   relic = RelicID.XRay,           slot = 1 },
            new Binding { key = KeyCode.KeypadEnter,    relic = RelicID.Hourglass,      slot = 1 }, // 슬롯0 액티브와 조합 테스트
            new Binding { key = KeyCode.Home,           relic = RelicID.OneWayPortal,   slot = 0 }, // Q: 1차 설치, 2차 귀환
            new Binding { key = KeyCode.T,              relic = RelicID.Trident,        slot = 0 }, // 삼지창 (T=Trident)
        };
    }
}
