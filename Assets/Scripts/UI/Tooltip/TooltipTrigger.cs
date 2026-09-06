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
    // 외부에서 대입 시 provider 캐시를 즉시 갱신 (AddComponent 후 Start() 타이밍 문제 방지)
    [SerializeField] private MonoBehaviour _tooltipProvider;
    public MonoBehaviour tooltipProvider
    {
        get => _tooltipProvider;
        set
        {
            _tooltipProvider = value;
            provider = value as ITooltipProvider;
        }
    }

    private ITooltipProvider provider;

    void Start()
    {
        // tooltipProvider가 외부(코드)에서 이미 설정된 경우 provider는 이미 캐싱되어 있음.
        // Inspector에서 직접 연결된 경우나 누락된 경우만 여기서 보완.
        if (provider == null)
        {
            if (_tooltipProvider != null)
                provider = _tooltipProvider as ITooltipProvider;
            else
                provider = GetComponent<ITooltipProvider>();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
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
            // 지연 표시 로직 사용 (설정된 showDelay만큼 대기)
            // 자신을 owner로 넘겨 매니저가 슬롯 파괴·마우스 이탈을 감시할 수 있게 함
            TooltipManager.Instance.ShowTooltipDelayed(title, content, this);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 툴팁 매니저에게 숨기기 요청 (중앙 집중식 관리)
        TooltipManager.Instance.RequestHide();
    }

    void OnDisable()
    {
        // 비활성화될 때는 즉시 숨김
        TooltipManager.Instance?.HideTooltip();
    }


}

