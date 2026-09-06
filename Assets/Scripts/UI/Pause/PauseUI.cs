using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PauseUI : MonoBehaviour
{
    [Header("버튼 참조")]
    public Button resumeButton;
    public Button settingsButton;
    public Button emergencyEscapeButton;
    public Button mainMenuButton;
    public Button quitButton;

    [Header("긴급탈출 팝업")]
    public GameObject emergencyEscapePopup;
    public Button emergencyYesButton;
    public Button emergencyNoButton;

    [Header("확인 팝업 (메인화면 경고 등)")]
    public ConfirmationPrompt confirmationPrompt;

    [Header("Localization Keys")]
    public string mainMenuWarningTitleKey = "ui_pause_mainmenu_title";
    public string mainMenuWarningMsgKey = "ui_pause_mainmenu_msg";

    [Header("테스트용 씬 설정")]
    [Tooltip("지하 씬 이름 (이 씬에서만 긴급탈출 버튼 활성화)")]
    public string targetUndergroundScene = "DemoUnderground";
    
    [Tooltip("긴급탈출 시 돌아갈 지상 씬 이름")]
    public string targetUpgroundScene = "DemoUpground";

    private void Awake()
    {
        if (resumeButton != null) resumeButton.onClick.AddListener(ResumeGame);
        if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
        if (emergencyEscapeButton != null) emergencyEscapeButton.onClick.AddListener(ShowEmergencyPopup);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ShowMainMenuWarning);
        if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
        
        if (emergencyYesButton != null) emergencyYesButton.onClick.AddListener(ExecuteEmergencyEscape);
        if (emergencyNoButton != null) emergencyNoButton.onClick.AddListener(HideEmergencyPopup);
    }

    private void OnEnable()
    {
        Time.timeScale = 0f;
        
        // 현재 씬이 설정된 지하 씬(targetUndergroundScene)일 때만 긴급탈출 버튼 활성화
        string sceneName = SceneManager.GetActiveScene().name;
        if (emergencyEscapeButton != null)
        {
            emergencyEscapeButton.gameObject.SetActive(sceneName == targetUndergroundScene);
        }

        if (emergencyEscapePopup != null)
        {
            emergencyEscapePopup.SetActive(false);
        }

        if (confirmationPrompt != null)
        {
            confirmationPrompt.gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        // OnDisable 시에는 무조건 timeScale을 원상복구
        Time.timeScale = 1f;
    }

    private void ResumeGame()
    {
        if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.SetState(UIState.None);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void OpenSettings()
    {
        // 설정은 현재 화면 위 오버레이로 뜬다 — 일시정지(timeScale=0)는 그대로 유지되고,
        // SettingsOverlayUI가 닫힐 때 이전 timeScale을 복원한다.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OpenSettings();
        }
    }

    private void ShowEmergencyPopup()
    {
        if (emergencyEscapePopup != null)
        {
            emergencyEscapePopup.SetActive(true);
        }
    }

    private void HideEmergencyPopup()
    {
        if (emergencyEscapePopup != null)
        {
            emergencyEscapePopup.SetActive(false);
        }
    }

    private void ExecuteEmergencyEscape()
    {
        if (emergencyEscapePopup != null)
        {
            emergencyEscapePopup.SetActive(false);
        }

        // 긴급 탈출 대기 상태 플래그 활성화 (지상 씬 진입 시 페널티 적용을 위함)
        LoadingData.IsEmergencyEscapePending = true;

        Time.timeScale = 1f;

        // 탈출해서 올라가도 내려갈 때 쓴 입구 앞에 나온다(아래 저장보다 먼저 비워야 파일에 실린다).
        SurfaceReturnRouter.HandOff();

        // 방법B: 씬 전환 직전에 인벤토리를 창고에 병합 후 저장 (광물 유실 방지)
        if (GameManager.Instance?.saveManager != null)
            GameManager.Instance.saveManager.MergeInventoriesToWarehouse();

        // 사망과 같은 게임오버 연출(HUD 페이드 → 암전 → Die 모션)을 한 번 재생한 뒤
        // 로딩씬 → 지상(정산창) 순으로 넘긴다. 신형 PauseOverlayUI.ExecuteEmergencyEscape와 동일 경로 —
        // 구형 EmergencyEscapeSequenceUI(흔들림·경보만, Die 모션 없음)를 쓰면 탈출에서 죽는 모션이 안 나온다.
        // 프리팹 일시정지 패널은 미리 숨겨 연출 뒤로 클릭이 새지 않게 한다.
        string upground = targetUpgroundScene;
        gameObject.SetActive(false);
        GameOverSequenceUI.Play(GameOverReason.EmergencyEscape, () => SceneLoader.LoadScene(upground));
    }

    private void ShowMainMenuWarning()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName == targetUndergroundScene)
        {
            // 지하에서는 경고 팝업 표시
            if (confirmationPrompt != null)
            {
                string title = LanguageManager.Instance?.L(mainMenuWarningTitleKey) ?? "메인 화면으로 이동";
                string msg = LanguageManager.Instance?.L(mainMenuWarningMsgKey) ?? "지하에서의 진행 내용은 저장되지 않습니다.\n정말 메인 화면으로 이동하시겠습니까?";

                confirmationPrompt.Show(
                    title,
                    msg,
                    () => { Time.timeScale = 1f; GameManager.Instance.OpenMainMenu(); },
                    null
                );
            }
            else
            {
                // 팝업이 없으면 바로 이동
                Time.timeScale = 1f;
                GameManager.Instance.OpenMainMenu();
            }
        }
        else
        {
            // 지상에서는 즉시 저장 후 이동
            Time.timeScale = 1f;
            GameManager.Instance.OpenMainMenu();
        }
    }

    private void QuitGame()
    {
        // 지하에서는 저장하지 않음 (GameManager.OnApplicationQuit에서도 동일 처리)
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
