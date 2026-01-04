using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 마우스 호버 시 툴팁을 표시하는 트리거 컴포넌트
/// </summary>
public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("툴팁 정보")]
    public string tooltipTitle = "";
    public string tooltipContent = "";

    [Header("동적 정보 제공자")]
    public MonoBehaviour tooltipProvider; // ITooltipProvider를 구현한 컴포넌트

    private ITooltipProvider provider;
    private bool isHovering = false;
    private float hideDelay = 0.05f; // 숨기기 전 딜레이 (더 짧게)
    private Coroutine hideCoroutine; // UnityEngine.Coroutine

    void Start()
    {
        // ITooltipProvider 인터페이스를 구현한 컴포넌트 찾기
        if (tooltipProvider != null)
        {
            provider = tooltipProvider as ITooltipProvider;
        }
        else
        {
            provider = GetComponent<ITooltipProvider>();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        
        // 이전 숨기기 코루틴이 실행 중이면 취소
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }

        string title = tooltipTitle;
        string content = tooltipContent;

        // 동적 제공자가 있으면 우선 사용 (매번 최신 정보 가져오기)
        if (provider != null)
        {
            title = provider.GetTooltipTitle();
            content = provider.GetTooltipContent();
        }

        if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(content))
        {
            // 즉시 표시 (다른 슬롯에서 이동한 경우를 위해)
            TooltipManager.Instance.ShowTooltip(title, content);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        
        // 이전 숨기기 코루틴이 실행 중이면 취소
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
        }
        
        // 약간의 딜레이를 두어 깜빡임 방지
        hideCoroutine = StartCoroutine(HideTooltipDelayed());
    }

    private System.Collections.IEnumerator HideTooltipDelayed()
    {
        yield return new WaitForSecondsRealtime(hideDelay);
        // 여전히 호버 중이 아니면 숨기기
        if (!isHovering)
        {
            TooltipManager.Instance.HideTooltip();
        }
        hideCoroutine = null;
    }

    void OnDisable()
    {
        // 비활성화될 때도 툴팁 숨기기
        TooltipManager.Instance?.HideTooltip();
    }
}

