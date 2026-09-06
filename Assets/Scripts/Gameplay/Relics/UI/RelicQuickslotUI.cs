using UnityEngine;
using UnityEngine.UI;
using Relic.Data;

namespace Relic
{
    // 퀵슬롯 아이콘 + 쿨타임/지속 오버레이. 슬롯당 아이콘 Image + fill Image 필요.
    public class RelicQuickslotUI : MonoBehaviour
    {
        [System.Serializable]
        public class SlotView
        {
            public Image icon;           // 유물 아이콘
            public Image fill;           // radial(쿨타임) / 지속 게이지. type=Filled 권장
            public GameObject readyMark; // 선택: Ready 표시
        }

        [SerializeField] private RelicManager manager;
        [SerializeField] private SlotView[] slots;

        private void Start()
        {
            if (manager == null) manager = FindFirstObjectByType<RelicManager>();
            RefreshIcons();
            if (manager != null) manager.Inventory.OnChanged += RefreshIcons;
        }

        private void OnDestroy()
        {
            if (manager != null) manager.Inventory.OnChanged -= RefreshIcons;
        }

        private void RefreshIcons()
        {
            if (manager == null || slots == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                var id = manager.Inventory.GetEquipped(i);
                var so = (RelicDatabase.Instance != null) ? RelicDatabase.Instance.GetRelicByID(id) : null;
                if (slots[i].icon != null)
                {
                    slots[i].icon.enabled = so != null;
                    if (so != null) slots[i].icon.sprite = so.icon;
                }
            }
        }

        private void Update()
        {
            if (manager == null || slots == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                var fill = slots[i].fill;
                if (fill == null) continue;

                var phase = manager.GetSlotPhase(i);
                float timer = manager.GetSlotTimer(i);

                switch (phase)
                {
                    case RelicPhase.Cooldown:
                        // timer = 남은 쿨타임. 정규화는 UI 근사(정확 총량 필요시 Manager 확장)
                        fill.fillAmount = Mathf.Clamp01(timer / 10f);
                        fill.enabled = true;
                        break;
                    case RelicPhase.Active:
                        fill.enabled = true;
                        fill.fillAmount = 1f; // 지속 중 강조
                        break;
                    default: // Ready
                        fill.enabled = false;
                        break;
                }

                if (slots[i].readyMark != null)
                    slots[i].readyMark.SetActive(phase == RelicPhase.Ready
                        && manager.Inventory.GetEquipped(i) != RelicID.None);
            }
        }
    }
}
