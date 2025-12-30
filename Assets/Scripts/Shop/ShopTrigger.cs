using UnityEngine;
using UnityEngine.EventSystems;

public class ShopTrigger : MonoBehaviour, IPointerClickHandler
{
    [Header("참조")]
    public ShopUI shopUI;
    
    [Header("클릭 방식 선택")]
    [Tooltip("UI 방식: Button 또는 Image 컴포넌트 필요\n월드 방식: Collider2D 필요")]
    public bool useUIComponent = true; // true면 UI 컴포넌트 사용, false면 Collider2D 사용

    void Start()
    {
        // 자동 참조
        if (shopUI == null)
            shopUI = FindFirstObjectByType<ShopUI>();
        
        // UI 방식인 경우 Image 컴포넌트 확인
        if (useUIComponent)
        {
            if (GetComponent<UnityEngine.UI.Image>() == null && GetComponent<UnityEngine.UI.Button>() == null)
            {
                Debug.LogWarning($"[ShopTrigger] {gameObject.name}: UI 컴포넌트(Image 또는 Button)가 없습니다. Image 컴포넌트를 추가하세요.");
            }
        }
        else
        {
            // 월드 방식인 경우 Collider2D 확인
            if (GetComponent<Collider2D>() == null)
            {
                Debug.LogWarning($"[ShopTrigger] {gameObject.name}: Collider2D가 없습니다. Collider2D를 추가하세요.");
            }
        }
    }

    // UI 방식: IPointerClickHandler 사용
    public void OnPointerClick(PointerEventData eventData)
    {
        if (useUIComponent && shopUI != null && !shopUI.IsOpen())
        {
            shopUI.OpenShop();
        }
    }

    // 월드 방식: OnMouseDown 사용 (Collider2D 필요)
    void OnMouseDown()
    {
        if (!useUIComponent && shopUI != null && !shopUI.IsOpen())
        {
            shopUI.OpenShop();
        }
    }

    // 또는 Collider2D를 사용하는 경우 (선택사항)
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            // 플레이어가 근처에 오면 힌트 표시 (선택사항)
            // Debug.Log("상점에 접근했습니다. 클릭하여 열 수 있습니다.");
        }
    }
}

