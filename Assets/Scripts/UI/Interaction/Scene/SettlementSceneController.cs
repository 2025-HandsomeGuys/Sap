using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class SettlementSceneController : MonoBehaviour
{
    [Header("UI Component Settings")]
    [Tooltip("진행 상태를 표시할 텍스트 컴포넌트 (선택)")]
    [SerializeField] private TextMeshProUGUI loadingText;

    [Header("Settlement Settings")]
    [SerializeField] private SettlementUI settlementUI;

    [Header("코드 생성 정산 UI")]
    [Tooltip("체크하면 프리팹 SettlementUI 대신 코드 생성 SettlementOverlayUI를 쓴다")]
    [SerializeField] private bool useCodeBuiltSettlementUI = false;
    [Tooltip("코드 정산 UI의 예상 수익 계산용(없으면 수익 줄 숨김)")]
    [SerializeField] private MineralPriceDatabase priceDatabase;

    private void Start()
    {
        LoadingData.IsLoading = true;
        LoadingData.IsReady = true;

        var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            if (c.gameObject.scene.name == "SettlementScene")
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                
                var layers = SortingLayer.layers;
                if (layers != null && layers.Length > 0)
                    c.sortingLayerID = layers[layers.Length - 1].id;
                
                c.overrideSorting = true;
                c.sortingOrder = 32767;
            }
        }

        if (string.IsNullOrEmpty(LoadingData.NextSceneName))
        {
            Debug.LogError("[SettlementSceneController] 대상 씬 이름이 없습니다. LoadingData.NextSceneName을 확인하세요.");
            return;
        }

        // 정산 데이터 표시 (코드 오버레이 또는 프리팹)
        if (SettlementManager.Instance != null && SettlementManager.Instance.IsDataPending)
        {
            SettlementData data = SettlementManager.Instance.GetSettlementData();
            if (useCodeBuiltSettlementUI)
                SettlementOverlayUI.Instance.Show(data, priceDatabase);
            else if (settlementUI != null)
                settlementUI.Show(data);
        }
        else if (SettlementManager.Instance != null)
        {
            Debug.LogWarning("[SettlementSceneController] 보존된 정산 데이터가 없습니다.");
        }

        StartCoroutine(LoadSceneSequence());
    }

    private IEnumerator LoadSceneSequence()
    {
        string nextScene = LoadingData.NextSceneName;

        AsyncOperation op = SceneManager.LoadSceneAsync(nextScene, LoadSceneMode.Additive);
        if (op == null) yield break;

        op.allowSceneActivation = false;

        // 에셋 스트리밍 진행
        while (op.progress < 0.9f)
        {
            yield return null;
            float targetProgress = op.progress / 0.9f;

            if (loadingText != null)
            {
                loadingText.text = $"Loading... {(targetProgress * 100):F0}%";
            }
        }

        if (loadingText != null) loadingText.text = "Loading Complete!";

        // 연출 완료 및 사용자 확인 대기
        if (useCodeBuiltSettlementUI)
        {
            while (SettlementOverlayUI.IsOpen && !SettlementOverlayUI.Instance.IsConfirmed)
                yield return null;
            SettlementOverlayUI.Instance.Hide(); // 지상 씬으로 넘어가기 전에 오버레이를 닫는다
        }
        else if (settlementUI != null)
        {
            while (!settlementUI.IsConfirmed)
            {
                yield return null;
            }
        }

        // B씬 활성화 및 중복 컴포넌트 정리
        var es = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (es != null && es.gameObject.scene.name == "SettlementScene") 
        {
            es.gameObject.SetActive(false);
        }

        op.allowSceneActivation = true;
        yield return new WaitUntil(() => op.isDone);

        DisableDuplicateComponents();

        // Active Scene 변경
        Scene bScene = SceneManager.GetSceneByName(nextScene);
        if (bScene.IsValid())
        {
            SceneManager.SetActiveScene(bScene);
            
            if (GameManager.Instance != null && GameManager.Instance.saveManager != null)
            {
                GameManager.Instance.saveManager.Load();
            }
        }

        // IsReady 대기
        while (!LoadingData.IsReady)
        {
            yield return null;
        }

        // 정산 데이터 지우기
        if (SettlementManager.Instance != null)
        {
            SettlementManager.Instance.ClearPendingData();
        }

        // 씬 전환
        SceneManager.UnloadSceneAsync("SettlementScene");
    }

    private void DisableDuplicateComponents()
    {
        var eventSystems = FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None);
        if (eventSystems.Length > 1)
        {
            foreach (var es in eventSystems)
            {
                if (es.gameObject.scene.name == "SettlementScene")
                {
                    es.gameObject.SetActive(false);
                    break;
                }
            }
        }

        var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        if (listeners.Length > 1)
        {
            foreach (var al in listeners)
            {
                if (al.gameObject.scene.name == "SettlementScene")
                {
                    al.enabled = false;
                    break;
                }
            }
        }
    }

    private void OnDestroy()
    {
        LoadingData.IsLoading = false;
    }
}
