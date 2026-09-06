// @tags: interaction, behaviour, elevator, scene, tunnel, travel, save
using UnityEngine;
using UnityEngine.UI; // 페이드 아웃용 UI를 만들기 위해 추가
using System.Collections;

/// <summary>
/// 엘리베이터 — 층 이동 UI를 연다. 기존 <c>ElevatorController</c>의 상호작용 부분과 같다.
///
/// <b>등록은 여전히 ElevatorController 담당</b>이다. <c>ElevatorSpawner</c>가 청크 생성 중
/// 프리팹을 심고 x·y·layer를 채운 뒤 <c>ElevatorManager</c>에 등록하는데, 매니저가
/// <c>ElevatorController</c> 타입으로 목록을 들고 있기 때문이다.
/// 그래서 여기서는 같은 오브젝트의 ElevatorController가 있으면 그 좌표를 그대로 읽고,
/// 없으면(직접 배치한 엘리베이터) 인스펙터 값을 쓴다.
/// </summary>
public sealed class ElevatorBehaviour : InteractionBehaviour
{
    private ElevatorController _controller;

    public override int DefaultPriority => 10;

    public override void OnStart()
    {
        _controller = Owner.GetComponent<ElevatorController>();
        // 지도 마커는 '로드'가 아니라 근접(상호작용 가능 위치) 시점에 찍는다.
        // WorldInteractable이 IMapElevator를 구현하므로 PlayerInteractor가 근접 시 발견 처리한다.
    }

    public override InteractionPromptInfo GetPrompt()
        => InteractionPromptInfo.Ok("interact_elevator", "엘리베이터");

    public override void Interact(GameObject interactor)
    {
        if (ElevatorManager.Instance == null)
        {
            Debug.LogWarning("[WorldInteractable/Elevator] ElevatorManager가 씬에 없습니다.");
            return;
        }

        int x = _controller != null ? _controller.xChunkPosition : Owner.ElevatorXChunk;
        int layer = _controller != null ? _controller.layerIndex : Owner.ElevatorLayerIndex;

        // E를 눌렀다면 확실히 가본 것이다 — 근접 훅(PlayerInteractor)이 놓쳐도 여기서 해금된다.
        Owner.NotifyReached();

        ElevatorManager.Instance.OpenElevatorUI(x, layer);
    }
}

/// <summary>
/// 땅굴 입구 — 씬을 통째로 갈아 지상 ↔ 지하를 오간다. 기존 <c>SceneTransitionTrigger</c>와 같다.
/// </summary>
public sealed class TunnelEntranceBehaviour : InteractionBehaviour
{
    private static readonly string[] SurfaceScenes = { "UpgroundScene", "DemoUpground", "SettlementScene" };

    private bool IsGoingUnderground()
    {
        string target = Owner.SceneToLoad;
        for (int i = 0; i < SurfaceScenes.Length; i++)
        {
            if (target == SurfaceScenes[i]) return false;
        }
        return true;
    }

    private bool IsClosedForToday() => Owner.BlockAtNight && IsGoingUnderground() && IsEvening();

    public override bool IsAvailable => !IsClosedForToday();

    public override InteractionPromptInfo GetPrompt()
    {
        if (!IsGoingUnderground())
            return InteractionPromptInfo.Ok("interact_surface_enter", "지상으로 나가기");

        return IsClosedForToday()
            ? InteractionPromptInfo.Blocked("interact_dungeon_night", "날이 어두워져 들어갈 수 없다")
            : InteractionPromptInfo.Ok("interact_dungeon_enter", "지하로 들어가기");
    }

    public override void Interact(GameObject interactor)
    {
        // =========================================================
        // 공중 상호작용 차단 (점프/낙하 중인지 확인)
        // =========================================================
        Rigidbody2D rbCheck = interactor.GetComponent<Rigidbody2D>();

        if (rbCheck != null && Mathf.Abs(rbCheck.linearVelocity.y) > 0.01f)
        {
            Debug.Log("[WorldInteractable/TunnelEntrance] 공중에 떠 있는 상태에서는 들어갈 수 없습니다.");
            return;
        }
        // =========================================================

        if (IsClosedForToday())
        {
            if (Sap.UI.Notification.NotificationUI.Instance != null)
                Sap.UI.Notification.NotificationUI.Instance.ShowNotification(
                    CodeUI.L("interact_dungeon_night", "날이 어두워져 들어갈 수 없다"));
            Debug.Log("[WorldInteractable/TunnelEntrance] 저녁이라 지하로 내려갈 수 없습니다.");
            return;
        }

        if (string.IsNullOrEmpty(Owner.SceneToLoad))
        {
            Debug.LogWarning($"[WorldInteractable/TunnelEntrance] {Owner.name}: 이동할 씬 이름이 비어 있습니다.");
            return;
        }

        Owner.StartCoroutine(TransitionAndLoadRoutine(interactor));
    }

    private bool isInteracting = false;

    private IEnumerator TransitionAndLoadRoutine(GameObject interactor)
    {
        if (isInteracting) yield break; // 이미 상호작용 중이면 중단
        isInteracting = true;           // 상호작용 시작 상태로 변경

        // 1. 조작 비활성화 및 물리 정지
        var playerMove = interactor.GetComponent("PlayerController") as MonoBehaviour;
        if (playerMove != null) playerMove.enabled = false;

        Rigidbody2D rb = interactor.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        // 2. 어긋남 방지
        Vector3 targetPos = new Vector3(-3.61f, -1.165f, interactor.transform.position.z);
        while (Mathf.Abs(interactor.transform.position.x - targetPos.x) > 0.01f)
        {
            interactor.transform.position = Vector3.MoveTowards(interactor.transform.position, targetPos, 5f * Time.deltaTime);
            yield return null;
        }
        interactor.transform.position = targetPos;

        // =========================================================
        // 3. 애니메이션 트리거 정리 및 실행 (JumpBlend 난입 방지)
        // =========================================================
        Animator anim = interactor.GetComponent<Animator>();
        if (anim != null)
        {
            // Rebind()를 쓰지 않고, 현재 애니메이터에 쌓여있는 모든 '트리거(Trigger)'만 찾아 지웁니다.
            // (bool이나 float 값은 그대로 유지되므로 바닥 판정이 풀리지 않음)
            foreach (AnimatorControllerParameter param in anim.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Trigger)
                {
                    anim.ResetTrigger(param.name);
                }
            }

            // 깔끔해진 상태에서 원하는 구멍 들어가기 트리거 발동
            anim.SetTrigger("EnterHole");
        }
        // =========================================================

        // 4. 애니메이션 대기
        yield return new WaitForSeconds(1.5f);

        // =========================================================
        // 5. 페이드 아웃 연출 (화면 까매짐)
        // =========================================================
        float fadeDuration = 0.5f; // 페이드 아웃에 걸리는 시간 (0.5초)

        GameObject fadeObj = new GameObject("FadeOutCanvas");
        Canvas canvas = fadeObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        Image fadeImage = fadeObj.AddComponent<Image>();
        fadeImage.color = new Color(0, 0, 0, 0);

        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float alpha = Mathf.Clamp01(timer / fadeDuration);
            fadeImage.color = new Color(0, 0, 0, alpha);
            yield return null;
        }
        fadeImage.color = new Color(0, 0, 0, 1);

        // 6. 상태 복구
        if (playerMove != null) playerMove.enabled = true;
        if (rb != null) rb.bodyType = RigidbodyType2D.Dynamic;

        // 7. 세이브 처리
        var saveManager = GameManager.Instance != null ? GameManager.Instance.saveManager : null;
        if (saveManager != null)
        {
            if (!IsGoingUnderground())
            {
                // 지상 복귀 — 어느 입구로 내려갔었는지를 지상 씬으로 넘긴다(저장 전에 필드를 비운다).
                SurfaceReturnRouter.HandOff();
                saveManager.MergeInventoriesToWarehouse();
            }
            else
            {
                // 구멍으로 내려간다 — PrepareUndergroundEntry의 Save()에 실리도록 먼저 찍는다.
                SurfaceReturnRouter.Stamp(SurfaceReturnPoint.Hole);
                saveManager.PrepareUndergroundEntry();
            }
        }

        // 8. 씬 로드
        SceneLoader.LoadScene(Owner.SceneToLoad);
    }
}
/// <summary>
/// 지상 출구 — 지하의 빛기둥 아래 같은 "지상 복귀 지점"에서 탐험을 종료한다.
/// 구 <see cref="SurfaceExitBeacon"/>과 같은 일을 하되, 근접 목록·느낌표·문구 라벨을
/// 다른 상호작용 오브젝트와 같은 방식으로 띄우기 위해 통합 컴포넌트 쪽으로 옮겼다.
///
/// 확인창을 직접 만들지 않는다 — CodeConfirmPopup은 오버레이가 캔버스와 함께 들고 있는
/// 클래스라 월드 오브젝트에서 쓸 수 없다. <see cref="ExploreExitController.RequestExitToSurface"/>를
/// 부르면 ExploreExitOverlayUI 탐색 → 구 ConfirmationPrompt 폴백 → 정산·씬 전환까지 따라온다.
/// </summary>
public sealed class SurfaceExitBehaviour : InteractionBehaviour
{
    private ExploreExitController _controller;

    // 엘리베이터와 같은 값. 발밑에 광물(0)이 굴러와도 이쪽이 먼저 잡힌다.
    public override int DefaultPriority => 10;

    public override void OnStart()
    {
        // 씬 매니저들이 깨어난 뒤라 여기서 찾는 것이 안전하다.
        _controller = Object.FindFirstObjectByType<ExploreExitController>(FindObjectsInactive.Include);
    }

    public override InteractionPromptInfo GetPrompt()
        => InteractionPromptInfo.Ok("interact_surface_enter", "지상으로 나가기");

    public override void Interact(GameObject interactor)
    {
        if (_controller == null)
            _controller = Object.FindFirstObjectByType<ExploreExitController>(FindObjectsInactive.Include);

        if (_controller == null)
        {
            Debug.LogError("[WorldInteractable/SurfaceExit] 씬에 ExploreExitController가 없습니다 — 지상으로 나갈 수 없습니다.");
            return;
        }

        // 확인창 표시부터 씬 전환까지 전부 컨트롤러가 맡는다.
        // 취소하면 아무 일도 일어나지 않고, 다시 F를 누르면 또 뜬다.
        _controller.RequestExitToSurface();
    }
}
