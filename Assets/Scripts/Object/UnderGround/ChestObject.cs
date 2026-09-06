using UnityEngine;
using UnityEngine.Events;

public class ChestObject : MonoBehaviour
{
    private Animator _animator;

    [Header("상자 상태")]
    public bool isOpen = false; // 상자가 열렸는지 여부

    [Header("상자 오픈 이벤트")]
    // 인스펙터에서 아이템 드랍, 효과음 재생 등의 함수를 연결할 수 있습니다.
    public UnityEvent onChestOpened;

    private bool _isPlayerInRange = false; // 플레이어가 근처에 있는지 확인

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    private void Update()
    {
        // 설정 오버레이는 E를 '선택' 키로 쓴다 — 열려 있는 동안 월드 상호작용으로 새지 않게 차단.
        if (SettingsOverlayUI.IsOpen) return;

        // 플레이어가 근처에 있고, 상자가 아직 열리지 않았으며, F키를 눌렀을 때
        if (_isPlayerInRange && !isOpen && InteractionKeys.InteractPressed)
        {
            OpenChest();
        }
    }

    private void OpenChest()
    {
        isOpen = true; // 상태 변경 (중복 오픈 방지)

        // 상자 열림 애니메이션 실행
        if (_animator != null)
        {
            _animator.Play("Open");
        }

        Debug.Log("상자가 열렸습니다!");

        // 아이템 드랍 등 상자가 열렸을 때 일어날 이벤트 실행
        onChestOpened?.Invoke();
    }

    // 플레이어가 상자 근처(트리거 영역)에 들어왔을 때
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 플레이어 태그를 확인 (플레이어 오브젝트의 태그가 "Player"로 설정되어 있어야 함)
        if (collision.CompareTag("Player"))
        {
            _isPlayerInRange = true;
            // 팁: 여기서 "F키를 눌러 상자 열기" 같은 UI를 띄워줄 수도 있습니다.
        }
    }

    // 플레이어가 상자 근처(트리거 영역)에서 벗어났을 때
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            _isPlayerInRange = false;
            // 팁: 여기서 띄워둔 UI를 숨길 수 있습니다.
        }
    }
}