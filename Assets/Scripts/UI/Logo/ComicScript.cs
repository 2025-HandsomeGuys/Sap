using UnityEngine;
using UnityEngine.UI; // UI 이미지를 다루기 위해 추가
using UnityEngine.SceneManagement; // 씬 이동을 위해 추가

[RequireComponent(typeof(Animator))]
public class ComicScript : MonoBehaviour
{
    [Header("설정 (Settings)")]
    [Tooltip("애니메이터에 설정해둔 Trigger 파라미터 이름")]
    public string triggerName = "PlayReveal";

    [Header("클릭 스킵 설정 (Click Skip Settings)")]
    [Tooltip("애니메이션 재생 중 클릭 시 실행할 트리거 파라미터 이름")]
    public string clickTriggerName = "Click";

    [Header("씬 이동 설정 (Scene Transition)")]
    [Tooltip("스킵 완료 시 이동할 다음 씬의 이름 (인스펙터에서 입력)")]
    public string nextSceneName = "TutorialScene";

    [Tooltip("스킵하기 위해 마우스를 누르고 있어야 하는 시간 (초)")]
    public float holdTimeToSkip = 1.5f;

    [Tooltip("(선택) 꾹 누를 때 차오르는 UI 이미지 (Image Type이 Filled여야 합니다)")]
    public Image skipProgressBar;

    [Header("테스트 트리거 (Test Trigger)")]
    [Tooltip("체크하면 애니메이션이 실행됩니다. (인스펙터 테스트용)")]
    public bool startTrigger = false;

    [Header("UI 대상 설정 (UI Targets)")]
    [Tooltip("애니메이션 이벤트 호출 시 즉시 숨길 UI 이미지")]
    public Image imageToHide;

    private Animator anim;

    // 스킵 관련 내부 변수
    private float currentHoldTime = 0f;
    private bool isSkipping = false;

    // 씬을 넘나들어도 유지되는 최초 실행 판정용 변수 (다른 씬에서 돌아왔을 때 인트로 스킵용)
    private static bool s_hasPlayedIntro = false;

    void Awake()
    {
        // 부모 오브젝트의 애니메이터 컴포넌트를 가져옵니다.
        anim = GetComponent<Animator>();

        // 인트로와 상관없이 메뉴 상호작용은 항상 허용
        GameManager.MenuInteractable = true;

        // 시작 시 프로그레스 바가 있다면 0으로 초기화
        if (skipProgressBar != null)
        {
            skipProgressBar.fillAmount = 0f;
        }
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
        // 이미 씬 이동 중이라면 Update 로직 무시
        if (isSkipping) return;

        // 인스펙터에서 startTrigger를 체크하면 실행
        if (startTrigger)
        {
            startTrigger = false; // 연속 실행 방지를 위해 바로 체크박스 해제
            PlayAnimation();
        }

        // 1. 단일 클릭 처리 (기존 로직: 클릭 시 다음 컷으로 애니메이션 넘김)
        if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
        {
            if (anim != null)
            {
                anim.SetTrigger(clickTriggerName);
            }
        }

        // 2. 꾹 누르기 처리 (좌클릭 또는 스페이스바 기준)
        if (Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space))
        {
            currentHoldTime += Time.deltaTime;

            // 프로그레스 바 UI가 연결되어 있다면 게이지 채우기
            if (skipProgressBar != null)
            {
                skipProgressBar.fillAmount = currentHoldTime / holdTimeToSkip;
            }

            // 누른 시간이 설정한 시간을 넘겼을 때 씬 이동
            if (currentHoldTime >= holdTimeToSkip)
            {
                isSkipping = true;
                LoadNextScene();
            }
        }
        else
        {
            // 마우스를 떼면 누적 시간과 UI 게이지 초기화
            if (currentHoldTime > 0f)
            {
                currentHoldTime = 0f;
                if (skipProgressBar != null)
                {
                    skipProgressBar.fillAmount = 0f;
                }
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

    // 다음 씬으로 이동하는 함수
    private void LoadNextScene()
    {
        Debug.Log($"[ComicScript] 컷씬 스킵! {nextSceneName} 씬으로 이동합니다.");

        // 주의: 프로젝트에서 기존에 SceneLoader를 사용하고 계셨다면 아래 주석을 풀고 교체하세요!
        // SceneLoader.LoadScene(nextSceneName);
        SceneManager.LoadScene(nextSceneName);
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

    // (추가) 애니메이션이 끝났을 때 타임라인 끝에 달아줄 함수
    public void OnComicFinished()
    {
        if (!isSkipping)
        {
            isSkipping = true;
            LoadNextScene();
        }
    }
}