// @tags: cauldron, ui, inventory, mineral, selection
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 가마솥 투입 UI. 인벤토리 광물을 버튼 그리드로 표시하고, 1개 선택·투입 시 콜백 후 닫는다.
/// 슬롯 버튼 프리팹(CauldronSlotButton)은 아이콘 Image + 수량 TMP + Button을 갖는다.
/// </summary>
public class CauldronUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;     // 전체 패널 (열기/닫기 토글)
    [SerializeField] private Transform slotContainer;  // 버튼이 채워질 부모
    [SerializeField] private CauldronSlotButton slotButtonPrefab; // 아이콘/수량/버튼 컴포넌트
    [SerializeField] private Button closeButton;

    private MineralInventory _inv;
    private Action<MineralID> _onChosen;
    private readonly List<CauldronSlotButton> _spawned = new List<CauldronSlotButton>();

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Open(MineralInventory inv, Action<MineralID> onChosen)
    {
        _inv = inv;
        _onChosen = onChosen;
        if (panelRoot != null) panelRoot.SetActive(true);
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var b in _spawned) if (b != null) Destroy(b.gameObject);
        _spawned.Clear();

        if (_inv == null || slotButtonPrefab == null || slotContainer == null) return;

        foreach (var slot in _inv.ReadonlyItems)
        {
            if (slot == null || !(slot.item is MineralSO so)) continue;
            var btn = Instantiate(slotButtonPrefab, slotContainer);
            btn.Bind(so, slot.quantity, () => Choose(so));
            _spawned.Add(btn);
        }
    }

    private void Choose(MineralSO so)
    {
        if (_inv == null || so == null) return;
        if (!_inv.RemoveItem(so, 1)) return; // 보유 없으면 무시
        var cb = _onChosen;
        Close();
        cb?.Invoke(so.mineralID);
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        _inv = null;
        _onChosen = null;
    }
}
