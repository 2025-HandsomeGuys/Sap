using UnityEngine;
using UnityEngine.SceneManagement; // 씬 전환을 위해 반드시 필요합니다.
using Sap.UI.Notification;

public class SceneTransitionTrigger : MonoBehaviour, IInteractionAvailability, IInteractionPrompt
{
    /// <summary>이 이름들로 가는 건 "지상으로 나가기", 나머지는 "지하로 들어가기"로 본다.</summary>
    private static readonly string[] SurfaceScenes = { "UpgroundScene", "DemoUpground", "SettlementScene" };

    [Header("UI 설정")]
    public GameObject buttonPrompt; // 띄워줄 E 버튼 오브젝트

    [Header("씬 설정")]
    public string sceneToLoad; // 이동할 씬의 이름

    [Header("시간 제한")]
    [Tooltip("켜면 저녁(Afternoon)에는 지하로 내려갈 수 없다. 지상으로 나가는 건 언제나 가능")]
    [SerializeField] private bool blockUndergroundAtNight = true;

    private bool isPlayerInRange = false; // 플레이어가 범위 안에 있는지 확인하는 변수

    /// <summary>지상 → 지하 방향인지. (지하 → 지상은 시간 제한 없음)</summary>
    private bool IsGoingUnderground()
    {
        for (int i = 0; i < SurfaceScenes.Length; i++)
        {
            if (sceneToLoad == SurfaceScenes[i]) return false;
        }
        return true;
    }

    /// <summary>저녁에 지하로 내려가려는 상황인가.</summary>
    private bool IsClosedForToday()
    {
        return blockUndergroundAtNight &&
               IsGoingUnderground() &&
               DayCycleManager.Instance != null &&
               DayCycleManager.Instance.CurrentTime == TimeOfDay.Afternoon;
    }

    /// <summary>저녁이면 진입 불가 — InteractionIndicator 느낌표도 뜨지 않는다.</summary>
    public bool IsInteractionAvailable => !IsClosedForToday();

    /// <summary>
    /// 근접 안내 문구 (InteractionPromptLabel이 표시).
    /// 지하 방향: 아침 "지하로 들어가기"(미색) / 저녁 "날이 어두워져 들어갈 수 없다"(붉은색).
    /// 지상 방향: 항상 "지상으로 나가기".
    /// </summary>
    public InteractionPromptInfo GetInteractionPrompt()
    {
        if (!IsGoingUnderground())
            return InteractionPromptInfo.Ok("interact_surface_enter", "지상으로 나가기");

        return IsClosedForToday()
            ? InteractionPromptInfo.Blocked("interact_dungeon_night", "날이 어두워져 들어갈 수 없다")
            : InteractionPromptInfo.Ok("interact_dungeon_enter", "지하로 들어가기");
    }

    void Start()
    {
        // 게임이 시작될 때는 버튼을 숨겨둡니다.
        if (buttonPrompt != null)
        {
            buttonPrompt.SetActive(false);
        }
    }

    void Update()
    {
        // 설정 오버레이는 E를 '선택' 키로 쓴다 — 열려 있는 동안 씬 전환으로 새지 않게 차단.
        if (SettingsOverlayUI.IsOpen) return;

        // 플레이어가 범위 안에 있고, 키보드 E를 눌렀을 때
        if (isPlayerInRange && InteractionKeys.InteractPressed)
        {
            LoadNextScene();
        }
    }

    // 플레이어가 콜라이더 영역 안으로 들어왔을 때 실행
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 들어온 오브젝트의 태그가 "Player"인지 확인
        if (collision.CompareTag("Player"))
        {
            isPlayerInRange = true; // 범위 안에 들어왔다고 표시
            if (buttonPrompt != null)
            {
                buttonPrompt.SetActive(true); // E 버튼 보이게 하기
            }
        }
    }

    // 플레이어가 콜라이더 영역 밖으로 나갔을 때 실행
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            isPlayerInRange = false; // 범위에서 벗어남
            if (buttonPrompt != null)
            {
                buttonPrompt.SetActive(false); // E 버튼 숨기기
            }
        }
    }

    // 씬 전환 함수
    private void LoadNextScene()
    {
        // 저녁에는 지하로 내려갈 수 없다 (문구와 실제 동작을 일치시킨다)
        if (IsClosedForToday())
        {
            if (NotificationUI.Instance != null)
                NotificationUI.Instance.ShowNotification(
                    CodeUI.L("interact_dungeon_night", "날이 어두워져 들어갈 수 없다"));
            Debug.Log("[SceneTransitionTrigger] 저녁이라 지하로 내려갈 수 없습니다.");
            return;
        }

        // 씬 전환 직전 세이브 처리
        if (GameManager.Instance != null && GameManager.Instance.saveManager != null)
        {
            if (!IsGoingUnderground())
            {
                // 지하→지상: 인벤토리를 창고에 병합 후 저장
                Debug.Log("[SceneTransitionTrigger] 지상/정산 씬으로 이동 전 인벤토리를 창고에 병합 후 저장합니다.");
                GameManager.Instance.saveManager.MergeInventoriesToWarehouse();
            }
            else
            {
                // 지상→지하: 지상 데이터 확정 저장
                Debug.Log("[SceneTransitionTrigger] 지하 씬으로 이동 전 지상 데이터를 확정 저장합니다.");
                GameManager.Instance.saveManager.PrepareUndergroundEntry();
            }
        }

        // 설정한 이름의 씬으로 이동합니다.
        SceneLoader.LoadScene(sceneToLoad);
    }
}