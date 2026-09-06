using UnityEngine;
using Stock.Core;

/// <summary>
/// @tags: exit, surface, settlement, trigger, prompt
/// 탐험 종료 컨트롤러 — 외부 호출(상호작용 버튼 등)을 통해 지상 이동 확인창을 띄우거나 씬 전환을 담당.
/// </summary>
public class ExploreExitController : MonoBehaviour
{
    [Header("씬 설정")]
    [SerializeField] private string targetScene = "DemoUpground";

    [Header("확인창")]
    [SerializeField] private ConfirmationPrompt confirmationPrompt;

    private bool _isExiting = false;

    // 물리 충돌 감지(OnTrigger...) 로직은 모두 삭제되었습니다.

    /// <summary>
    /// 지상 이동 확인 다이얼로그를 띄운다. 취소 시 아무 동작도 하지 않는다.
    /// 이제 플레이어 충돌 시가 아닌, 외부 상호작용(예: F키 입력, 버튼 클릭) 시 호출되어야 합니다.
    /// </summary>
    public void RequestExitToSurface()
    {
        if (_isExiting) return;

        var overlay = ExploreExitOverlayUI.FindInScene();
        if (overlay != null)
        {
            overlay.Show(ConfirmExit, () => { /* 취소 */ });
            return;
        }

        if (confirmationPrompt == null) return;

        confirmationPrompt.Show(
            title: "탐험 종료",
            message: "정말 탐험을 종료하고 지상으로 올라가시겠습니까?",
            confirmCallback: ConfirmExit,
            cancelCallback: null
        );
    }

    private void ConfirmExit()
    {
        if (_isExiting) return;
        _isExiting = true;
        PrepareSettlement();
    }

    /// <summary>
    /// 외부(엘리베이터 UI 등)에서 추가 확인창 없이 바로 탐험 종료를 트리거할 때 호출
    /// </summary>
    public void ExecuteExit()
    {
        if (_isExiting) return;
        _isExiting = true;
        PrepareSettlement();
    }

    private void PrepareSettlement()
    {
        Debug.Log("[ExploreExitController] PrepareSettlement - StopTracking 호출 시도");

        if (StockGameManager.Instance != null && StockGameManager.Instance.IsInitialized)
            StockGameManager.Instance.ProcessNewsFreeTicks(StockGameManager.SkipTicks);

        if (SettlementManager.Instance != null)
        {
            SettlementManager.Instance.StopTracking();
        }
        else
        {
            Debug.LogError("[ExploreExitController] SettlementManager.Instance가 Null입니다!");
        }

        if (DayCycleManager.Instance != null)
        {
            DayCycleManager.Instance.SetAfternoon();
        }

        var staminaManager = FindFirstObjectByType<StaminaManager>();
        staminaManager?.ResetDiggingReduction();
        staminaManager?.RecoverStatus(float.MaxValue, 0f, 0f);
        staminaManager?.RefillStamina();

        MiningStaminaTuning.ResetShovelTripCount();

        MapMarkerRegistry.Reset();
        MapTerrainCache.Clear();

        ExitToSurface();
    }

    private void ExitToSurface()
    {
        Debug.Log($"[ExploreExitController] {targetScene} 씬으로 이동합니다.");

        SurfaceReturnRouter.HandOff();

        if (GameManager.Instance?.saveManager != null)
            GameManager.Instance.saveManager.MergeInventoriesToWarehouse();

        SceneLoader.LoadSettlementScene(targetScene);
    }
}