// @tags: encumbrance, icon, ui, player, indicator, status
using UnityEngine;

/// <summary>
/// 과중 상태일 때 캐릭터 위에 아이콘을 표시하는 컴포넌트.
/// 이 스크립트가 붙은 GameObject 자체를 SetActive로 토글한다.
/// </summary>
public class EncumbranceIconUI : MonoBehaviour
{
    [SerializeField] private EncumbranceController encumbranceController;

    private void Start()
    {
        if (encumbranceController == null)
            encumbranceController = GetComponentInParent<EncumbranceController>();
        if (encumbranceController == null)
            encumbranceController = FindFirstObjectByType<EncumbranceController>();

        if (encumbranceController == null)
        {
            Debug.LogWarning("[EncumbranceIconUI] EncumbranceController를 찾을 수 없습니다.");
            gameObject.SetActive(false);
            return;
        }

        encumbranceController.OnEncumbranceChanged += OnEncumbranceChanged;
        gameObject.SetActive(encumbranceController.IsEncumbered);
    }

    private void OnDestroy()
    {
        if (encumbranceController != null)
            encumbranceController.OnEncumbranceChanged -= OnEncumbranceChanged;
    }

    private void OnEncumbranceChanged(bool isEncumbered)
    {
        gameObject.SetActive(isEncumbered);
    }
}
