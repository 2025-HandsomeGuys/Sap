//using UnityEngine;
//using UnityEngine.Events;

//public class InventoryUIController : MonoBehaviour
//{
//    [Header("References")]
//    [SerializeField] private GameObject inventoryPanel; // InventoryPanel
//    [SerializeField] private InventoryUI inventoryUI; // 슬롯 갱신/설명 초기화를 담당하는 기존 UI 스크립트
//    //[SerializeField] private InvenUITest inventoryUI;


//    [Header("Behavior")]
//    [SerializeField] private bool pauseOnOpen = true;     // 열 때 게임 일시정지
//    [SerializeField] private bool manageCursor = true;    // 커서 보이기/잠금 제어

//    public UnityEvent onOpened;   // 필요 시 추가 동작 훅
//    public UnityEvent onClosed;

//    public bool IsOpen => inventoryPanel != null && inventoryPanel.activeSelf;

//    private void Awake()
//    {
//        if (inventoryPanel != null) inventoryPanel.SetActive(false);
//        if (manageCursor) SetCursor(false);
//    }

//    public void Toggle()
//    {
//        if (inventoryPanel == null) return;

//        bool next = !inventoryPanel.activeSelf;
//        inventoryPanel.SetActive(next);

//        if (pauseOnOpen) Time.timeScale = next ? 0f : 1f;
//        if (manageCursor) SetCursor(next);

//        if (next)
//        {
//            // 패널이 켜질 때 슬롯 갱신 + 설명 초기화
//            inventoryUI?.UpdateSlots();
//            inventoryUI?.UpdateDescription();
//            onOpened?.Invoke();
//        }
//        else
//        {
//            // 꺼질 때 설명 비우기 정도만
//            inventoryUI?.UpdateDescription();
//            onClosed?.Invoke();
//        }
//    }

//    private void SetCursor(bool visible)
//    {
//        Cursor.visible = visible;
//        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
//    }
//}