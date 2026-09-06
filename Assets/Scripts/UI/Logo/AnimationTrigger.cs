using UnityEngine;
using UnityEngine.UI; // UI 이미지를 다루기 위해 추가

[RequireComponent(typeof(Animator))]
public class AnimationTrigger : MonoBehaviour
{
    [Header("설정 (Settings)")]
    [Tooltip("애니메이터에 설정해둔 Trigger 파라미터 이름")]
    public string triggerName = "PlayReveal";

    [Header("클릭 스킵 설정 (Click Skip Settings)")]
    [Tooltip("애니메이션 재생 중 클릭 시 실행할 트리거 파라미터 이름")]
    public string clickTriggerName = "Click";

    [Header("테스트 트리거 (Test Trigger)")]
    [Tooltip("체크하면 애니메이션이 실행됩니다. (인스펙터 테스트용)")]
    public bool startTrigger = false;

    [Header("UI 대상 설정 (UI Targets)")]
    [Tooltip("애니메이션 이벤트 호출 시 즉시 숨길 UI 이미지")]
    public Image imageToHide;

    private Animator anim;

    // 씬을 넘나들어도 유지되는 최초 실행 판정용 변수 (다른 씬에서 돌아왔을 때 인트로 스킵용)
    private static bool s_hasPlayedIntro = false;

    void Awake()
    {
        // 부모 오브젝트의 애니메이터 컴포넌트를 가져옵니다.
        anim = GetComponent<Animator>();

        // 인트로와 상관없이 메뉴 상호작용은 항상 허용
        GameManager.MenuInteractable = true;
    }

    void Start()
    {
        // 이 씬에서 처음 시작할 때만 PlayReveal 실행 (다른 씬에서 넘어온 경우 실행 안 함)
        if (!s_hasPlayedIntro)
        {
            s_hasPlayedIntro = true;
            PlayAnimation();
        }
        else
        {
            // 다른 씬에서 메인메뉴로 돌아온 경우 인트로 스킵에 따른 UI 상태 정리
            if (imageToHide != null)
            {
                Color c = imageToHide.color;
                c.a = 0f;
                imageToHide.color = c;
            }
        }
    }

    void Update()
    {
        // 인스펙터에서 startTrigger를 체크하면 실행
        if (startTrigger)
        {
            startTrigger = false; // 연속 실행 방지를 위해 바로 체크박스 해제
            PlayAnimation();
        }

        // 애니메이션 재생 중 마우스나 키보드 입력이 들어오면 Click 트리거 실행 (필요에 따라 유지 또는 제거 가능)
        if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
        {
            if (anim != null)
            {
                anim.SetTrigger(clickTriggerName);
            }
        }
    }

    // 외부 스크립트나 UI 버튼 등에서 호출할 수 있는 함수
    public void PlayAnimation()
    {
        if (anim != null)
        {
            anim.SetTrigger(triggerName);
        }
    }

    // ==========================================
    // 애니메이션 이벤트(Animation Event) 호출용 함수들
    // ==========================================

    // 특정 오브젝트 즉시 안 보이게 하기
    public void HideImageInstant()
    {
        if (imageToHide != null)
        {
            Color c = imageToHide.color;
            c.a = 0f;
            imageToHide.color = c;
        }
    }
}