using UnityEngine;

namespace Relic
{
    // 유물 발동 입력 전담. RelicManager와 같은 오브젝트에 부착.
    // 슬롯별 발동 키를 인스펙터에서 지정하거나 런타임(설정 UI)에서 리바인딩할 수 있다.
    // 리바인딩 값은 PlayerPrefs에 슬롯별로 저장/복원된다.
    [RequireComponent(typeof(RelicManager))]
    public class RelicInputHandler : MonoBehaviour
    {
        [Tooltip("슬롯별 발동 키. 인덱스 = 슬롯 번호. 배열 길이보다 큰 슬롯은 발동 키 없음.")]
        [SerializeField] private KeyCode[] slotKeys = { KeyCode.Q, KeyCode.R };

        [Tooltip("유물 슬롯 업그레이드로 칸이 늘었을 때 새 칸에 붙는 키. 앞에서부터 하나씩 쓴다. " +
                 "여기까지 바닥나면 그 칸은 발동 키 없이 남는다(패시브 유물은 그대로 동작).")]
        [SerializeField] private KeyCode[] extraSlotKeys = { KeyCode.F, KeyCode.G };

        private const string PrefKeyFormat = "relic.slotkey.{0}";

        private RelicManager _manager;

        private void Awake()
        {
            _manager = GetComponent<RelicManager>();
            LoadOverrides();
        }

        private void Update()
        {
            if (UIStateManager.Instance != null &&
                UIStateManager.Instance.CurrentState != UIState.None)
                return;

            // 유물 슬롯 업그레이드로 칸이 늘면 키 배열도 따라 늘린다.
            // 인스펙터 배열은 프리팹에 2칸으로 직렬화돼 있어 코드 기본값을 고쳐도 안 먹는다.
            if (_manager != null && _manager.Inventory.SlotCount > slotKeys.Length)
                EnsureSize(_manager.Inventory.SlotCount);

            for (int i = 0; i < slotKeys.Length; i++)
            {
                if (slotKeys[i] == KeyCode.None) continue;
                if (Input.GetKeyDown(slotKeys[i]))
                    _manager.ActivateSlot(i);
            }
        }

        // ── 런타임 리바인딩 API (설정 UI 등에서 호출) ──
        public KeyCode GetSlotKey(int slot)
            => (slot >= 0 && slot < slotKeys.Length) ? slotKeys[slot] : KeyCode.None;

        public void SetSlotKey(int slot, KeyCode key)
        {
            if (slot < 0) return;
            EnsureSize(slot + 1);
            slotKeys[slot] = key;
            PlayerPrefs.SetInt(string.Format(PrefKeyFormat, slot), (int)key);
            PlayerPrefs.Save();
        }

        public int SlotKeyCount => slotKeys.Length;

        // 늘어난 칸은 extraSlotKeys에서 순서대로 키를 받는다. 저장된 리바인딩이 있으면 그쪽이 이긴다 —
        // LoadOverrides는 Awake 시점 길이만 훑으므로 나중에 생긴 칸은 여기서 직접 읽어야 한다.
        private void EnsureSize(int size)
        {
            if (slotKeys.Length >= size) return;
            int old = slotKeys.Length;
            var next = new KeyCode[size];
            for (int i = 0; i < next.Length; i++)
                next[i] = i < old ? slotKeys[i] : KeyCode.None;
            slotKeys = next;

            for (int i = old; i < size; i++)
            {
                int extra = i - old;
                if (extraSlotKeys != null && extra < extraSlotKeys.Length)
                    slotKeys[i] = extraSlotKeys[extra];

                string k = string.Format(PrefKeyFormat, i);
                if (PlayerPrefs.HasKey(k)) slotKeys[i] = (KeyCode)PlayerPrefs.GetInt(k);
            }
        }

        // 저장된 리바인딩이 있으면 인스펙터 기본값 위에 덮어쓴다.
        private void LoadOverrides()
        {
            for (int i = 0; i < slotKeys.Length; i++)
            {
                string k = string.Format(PrefKeyFormat, i);
                if (PlayerPrefs.HasKey(k))
                    slotKeys[i] = (KeyCode)PlayerPrefs.GetInt(k);
            }
        }
    }
}
