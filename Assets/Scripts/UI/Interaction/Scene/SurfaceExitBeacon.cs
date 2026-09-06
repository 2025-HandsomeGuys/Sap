using UnityEngine;

/// <summary>
/// @tags: interaction, exit, surface, elevator, beam, entrance
///
/// 빛기둥 아래 같은 "지상 복귀 지점"에서 F키로 탐험을 종료한다.
/// 엘리베이터(ElevatorController)와 조작·우선순위를 맞춘 형제 컴포넌트다.
///
/// 확인창을 직접 만들지 않는다 — CodeConfirmPopup은 오버레이가 캔버스와 함께 들고 있는
/// 클래스라 월드 오브젝트에서 쓸 수 없다. 대신 이미 public인
/// <see cref="ExploreExitController.RequestExitToSurface"/>를 부르면
/// ExploreExitOverlayUI 탐색 → 구 ConfirmationPrompt 폴백 → 정산·씬 전환까지 전부 따라온다.
///
/// 빛기둥 연출(SurfaceLightShaft)과는 서로를 모른다. 따로 붙이고 따로 뗄 수 있다.
/// 설계: Assets/Docs/surface-light-shaft.md
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class SurfaceExitBeacon : MonoBehaviour, IInteractable, IInteractionPrompt
{
    [Tooltip("비우면 씬에서 한 번 찾아 캐시한다. 씬에 ExploreExitController가 하나뿐이면 비워둬도 된다.")]
    [SerializeField] private ExploreExitController exitController;

    // IInteractable — 문구는 IInteractionPrompt 쪽이 실제로 쓰인다(언어 전환 대응).
    public string InteractionPrompt => "지상으로 나가기";

    // 엘리베이터와 같은 값. 발밑에 광물(0)이 굴러와도 이쪽이 먼저 잡힌다.
    public int InteractionPriority => 10;

    public InteractionPromptInfo GetInteractionPrompt()
        => InteractionPromptInfo.Ok("interact_surface_enter", "지상으로 나가기");

    /// <summary>PlayerInteractor가 F 키 입력 시 호출한다.</summary>
    public void Interact(GameObject interactor)
    {
        ExploreExitController controller = Resolve();
        if (controller == null)
        {
            Debug.LogError("[SurfaceExitBeacon] 씬에 ExploreExitController가 없습니다 — 지상으로 나갈 수 없습니다.");
            return;
        }

        // 확인창 표시부터 씬 전환까지 전부 컨트롤러가 맡는다.
        // 취소하면 아무 일도 일어나지 않고, 다시 E를 누르면 또 뜬다
        // (천장 ExitTrigger의 _promptedInZone 재진입 제한은 트리거 전용이라 여기 안 걸린다).
        controller.RequestExitToSurface();
    }

    private ExploreExitController Resolve()
    {
        if (exitController == null)
            exitController = FindFirstObjectByType<ExploreExitController>(FindObjectsInactive.Include);

        return exitController;
    }

    // PlayerInteractor는 플레이어 콜라이더의 Overlap으로 후보를 찾는다 →
    // 겹칠 수 있는 트리거 콜라이더가 반드시 있어야 하고, 그 반경이 곧 상호작용 범위다.
    private void Reset()
    {
        var col = GetComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius    = 2f;
    }

    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<CircleCollider2D>();
        if (col == null) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position + (Vector3)col.offset, col.radius);
    }
}
