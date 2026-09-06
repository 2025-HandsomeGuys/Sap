using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class PlayerColorManager : MonoBehaviour
{
    // 지하 씬 이름 (SaveManager와 동일 관례). 이 씬에서는 낮/밤 시간 틴트를 적용하지 않고
    // 항상 기본색(morningColor)으로 초기화한다.
    private const string UndergroundSceneName = "DemoUnderground";

    [Header("시간대별 플레이어 색상 설정")]
    public Color morningColor = Color.white;       // 오전 (기본 원래 색상)
    public Color afternoonColor = new Color(0.6f, 0.6f, 0.6f, 1f); // 오후 (원하는 어두운 색상)

    [Header("시간대 변경 시 부드러운 전환 시간")]
    public float transitionDuration = 1.0f;

    private SpriteRenderer[] allRenderers;
    private Coroutine colorCoroutine;

    private void Awake()
    {
        // 하위의 모든 SpriteRenderer 캐싱
        allRenderers = GetComponentsInChildren<SpriteRenderer>(true);
    }

    private void Start()
    {
        // [지하 진입 시 색 초기화] 지하 씬에서는 시간대 틴트를 무시하고 항상 기본색으로 시작한다.
        // DayCycleManager는 전역 싱글톤이라 지상에서 오후(어두운 색)로 바뀐 상태가 지하까지
        // 따라오므로, 여기서 이벤트 구독도 하지 않아 지하에 있는 동안 시간이 바뀌어도 색이 유지된다.
        //
        // [주의] GetActiveScene()이 아니라 이 오브젝트가 소속된 씬(gameObject.scene)을 검사할 것.
        // LoadingSceneController가 DemoUnderground를 Additive 로드하면 씬 활성화(Start 실행) 시점에는
        // 아직 액티브 씬이 "LoadingScene"이라(SetActiveScene은 그 뒤 Step 5) 가드가 뚫리고
        // 오후 틴트가 지하까지 따라오는 버그가 있었다.
        if (gameObject.scene.name == UndergroundSceneName)
        {
            SetAllColorsInstant(morningColor);
            return;
        }

        // DayCycleManager 싱글톤이 존재할 때만 작동
        if (DayCycleManager.Instance != null)
        {
            // 1. [씬 전환 대응] 새로운 씬에 진입했을 때, 현재 매니저 시간에 맞게 플레이어 색상 즉시 초기화
            UpdatePlayerColor(DayCycleManager.Instance.CurrentTime, instant: true);

            // 2. [실시간 변경 대응] 앞으로 게임 진행 중 시간이 바뀔 때마다 감지하도록 이벤트 구독
            DayCycleManager.Instance.OnDateTimeChanged += HandleDateTimeChanged;
        }
    }

    private void OnDestroy()
    {
        // 플레이어 오브젝트가 파괴되거나 씬이 끊길 때 이벤트 구독 해제 (메모리 누수 방지)
        if (DayCycleManager.Instance != null)
        {
            DayCycleManager.Instance.OnDateTimeChanged -= HandleDateTimeChanged;
        }
    }

    // 매니저에서 시간이 바뀌었다고 신호를 주면 실행되는 함수
    private void HandleDateTimeChanged(int day, TimeOfDay time)
    {
        // 게임 도중 오전/오후가 바뀔 때는 자연스럽게(instant: false) 변하도록 처리
        UpdatePlayerColor(time, instant: false);
    }

    // 실제 색상 변경 방식을 결정하는 로직
    private void UpdatePlayerColor(TimeOfDay currentTime, bool instant)
    {
        // 오후면 afternoonColor, 오전이면 morningColor 선택
        Color targetColor = (currentTime == TimeOfDay.Afternoon) ? afternoonColor : morningColor;

        if (instant)
        {
            // 즉시 변경해야 할 때 (씬 로드 시)
            SetAllColorsInstant(targetColor);
        }
        else
        {
            // 부드럽게 변경해야 할 때 (게임 플레이 중 시간 전환 시)
            if (colorCoroutine != null)
            {
                StopCoroutine(colorCoroutine);
            }
            colorCoroutine = StartCoroutine(ColorTransitionRoutine(targetColor));
        }
    }

    // 모든 렌더러의 색상을 즉시 바꾸는 함수
    private void SetAllColorsInstant(Color targetColor)
    {
        if (allRenderers == null) return;

        foreach (SpriteRenderer renderer in allRenderers)
        {
            if (renderer != null) renderer.color = targetColor;
        }
    }

    // 모든 렌더러의 색상을 부드럽게 바꾸는 코루틴
    private IEnumerator ColorTransitionRoutine(Color targetColor)
    {
        if (allRenderers == null || allRenderers.Length == 0) yield break;

        Color startColor = allRenderers[0].color;
        float elapsedTime = 0f;

        while (elapsedTime < transitionDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / transitionDuration;
            Color currentColor = Color.Lerp(startColor, targetColor, t);

            foreach (SpriteRenderer renderer in allRenderers)
            {
                if (renderer != null) renderer.color = currentColor;
            }
            yield return null;
        }

        SetAllColorsInstant(targetColor);
    }
    public void SetAllColors(Color targetColor)
    {
        if (colorCoroutine != null)
        {
            StopCoroutine(colorCoroutine);
        }
        colorCoroutine = StartCoroutine(ColorTransitionRoutine(targetColor));
    }

    /// <summary>
    /// 영역 밖으로 나갔을 때, 현재 시간대(오전/오후)에 맞는 원래 색으로 스르륵 복구합니다.
    /// </summary>
    public void RestoreTimeColor()
    {
        if (DayCycleManager.Instance != null)
        {
            // 현재 시간에 맞는 색상으로 부드럽게(instant: false) 전환
            UpdatePlayerColor(DayCycleManager.Instance.CurrentTime, instant: false);
        }
    }
}