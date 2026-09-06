using UnityEngine;

[System.Serializable]
public class SpriteSwapInfo
{
    public SpriteRenderer renderer; // 이미지를 바꿀 대상 렌더러
    public Sprite morningSprite;    // 오전일 때의 기본 이미지
    public Sprite afternoonSprite;  // 오후일 때 바뀔 이미지
}

public class SceneTimeVisualizer : MonoBehaviour
{
    [Header("Afternoon Settings (오후 상태 설정)")]
    [Tooltip("오후가 되면 켜고, 오전에는 꺼질 라이트 오브젝트들")]
    [SerializeField] private GameObject[] afternoonLights;

    [Tooltip("오후에 이미지를 교체할 오브젝트들의 정보")]
    [SerializeField] private SpriteSwapInfo[] spriteSwaps;

    private void Start()
    {
        // 1. 다른 씬에서 이 씬으로 넘어왔을 때, 현재 매니저 시간에 맞게 비주얼 초기화
        if (DayCycleManager.Instance != null)
        {
            UpdateVisuals(DayCycleManager.Instance.CurrentTime);

            // 2. 앞으로 게임 진행 중 시간이 바뀔 때마다 감지할 수 있도록 이벤트 구독
            DayCycleManager.Instance.OnDateTimeChanged += HandleDateTimeChanged;
        }
    }

    private void OnDestroy()
    {
        // 3. 씬이 끝나서 이 오브젝트가 파괴될 때 이벤트 구독 해제 (메모리 누수 방지)
        if (DayCycleManager.Instance != null)
        {
            DayCycleManager.Instance.OnDateTimeChanged -= HandleDateTimeChanged;
        }
    }

    // 매니저에서 시간이 바뀌었다고 신호를 주면 실행되는 함수
    private void HandleDateTimeChanged(int day, TimeOfDay time)
    {
        UpdateVisuals(time);
    }

    // 실제 라이트와 스프라이트를 켜고 끄는 로직
    private void UpdateVisuals(TimeOfDay currentTime)
    {
        bool isAfternoon = (currentTime == TimeOfDay.Afternoon);

        // 1. 라이트 온/오프
        if (afternoonLights != null)
        {
            foreach (GameObject lightObj in afternoonLights)
            {
                if (lightObj != null) lightObj.SetActive(isAfternoon);
            }
        }

        // 2. 스프라이트 교체
        if (spriteSwaps != null)
        {
            foreach (SpriteSwapInfo swap in spriteSwaps)
            {
                if (swap != null && swap.renderer != null)
                {
                    swap.renderer.sprite = isAfternoon ? swap.afternoonSprite : swap.morningSprite;
                }
            }
        }
    }
}