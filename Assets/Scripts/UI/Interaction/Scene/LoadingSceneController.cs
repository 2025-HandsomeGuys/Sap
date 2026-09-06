using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

public class LoadingSceneController : MonoBehaviour
{
    [Header("UI Component Settings")]
    [Tooltip("게임 팁을 표시할 텍스트 컴포넌트 (선택)")]
    [SerializeField] private TextMeshProUGUI tipText;

    [Tooltip("로딩 완료 시 표시할 '아무 키나 누르세요' 컴포넌트 (선택)")]
    [SerializeField] private GameObject anyKeyPrompt;

    [Tooltip("로딩 중 보여줄 메인 일러스트 컴포넌트 (선택)")]
    [SerializeField] private Image mainVisualImage;

    [Tooltip("로딩 중 회전할 스피너/아이콘 컴포넌트 (선택)")]
    [SerializeField] private Image loadingSpinner;

    [Tooltip("로딩 중 보여줄 비디오 출력용 RawImage (선택)")]
    [SerializeField] private RawImage loadingVideoDisplay;

    [Tooltip("로딩 중 재생할 비디오 컴포넌트 (선택)")]
    [SerializeField] private VideoPlayer videoPlayer;

    [Header("Transition Settings")]
    [Tooltip("최소 로딩 대기 시간 (초)")]
    [SerializeField] private float minimumLoadingTime = 0.3f;

    [Tooltip("팁이 변경되는 간격 (초)")]
    [SerializeField] private float tipChangeInterval = 5.0f;

    [Header("Fade Settings")]
    [Tooltip("페이드 인/아웃 연출 시간 (초)")]
    [SerializeField] private float fadeDuration = 0.5f;

    private Coroutine _tipCoroutine;
    private Image _fadeOverlay; // 페이드 효과를 위한 검은색 덮개 이미지

    private void Update()
    {
        // 로딩 스피너 회전 (메인 이미지가 아닌 별도의 아이콘이 회전하도록 함)
        if (loadingSpinner != null)
        {
            loadingSpinner.rectTransform.Rotate(0, 0, -200 * Time.unscaledDeltaTime);
        }

        // 비디오 텍스처 동기화 (Play 직후에는 texture가 null일 수 있으므로 준비되는 대로 연결)
        if (videoPlayer != null && videoPlayer.isPlaying && loadingVideoDisplay != null && loadingVideoDisplay.texture == null)
        {
            loadingVideoDisplay.texture = videoPlayer.texture;
        }
    }

    private void Start()
    {
        // 🌟 [추가됨] 로딩 씬 시작 시 유니티 전체 오디오 음소거
        AudioListener.pause = true;

        LoadingData.IsLoading = true; // 로딩 중 팝업 및 입력 차단 플래그 켜기
        LoadingData.IsReady = true;   // [Fix] 무한 로딩 방지: 기본값을 true로 리셋 (reporter가 있는 씬만 Awake에서 false로 만듦)

        // 랜덤 팁 및 비주얼 설정
        if (tipText != null)
        {
            SetRandomTip();
            _tipCoroutine = StartCoroutine(ChangeTipRoutine());
        }

        SetRandomVisual();

        // "아무 키나 눌러서 계속" 텍스트 미리 설정 및 초기 비활성화 (나중에 나타날 때 방지)
        if (anyKeyPrompt != null)
        {
            anyKeyPrompt.SetActive(false);
            if (LanguageManager.Instance != null)
            {
                var promptText = anyKeyPrompt.GetComponentInChildren<TextMeshProUGUI>(true);
                if (promptText != null)
                {
                    promptText.text = LanguageManager.Instance.L("ui_settlement_continue");
                }
            }
        }

        // 로딩 캔버스가 B씬의 모든 UI를 가리도록 설정
        // (페이드 캔버스가 32767을 쓰게 되므로, 기존 로딩 UI는 32766으로 한 단계 낮춥니다)
        var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            if (c.gameObject.scene.name == "LoadingScene")
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;

                var layers = SortingLayer.layers;
                if (layers != null && layers.Length > 0)
                    c.sortingLayerID = layers[layers.Length - 1].id;

                c.overrideSorting = true;
                c.sortingOrder = 32766; // [수정] 최상단에서 한 칸 아래
            }
        }

        if (string.IsNullOrEmpty(LoadingData.NextSceneName))
        {
            Debug.LogError("[LoadingSceneController] 대상 씬 이름이 없습니다. LoadingData.NextSceneName을 확인하세요.");
            return;
        }

        // ─── 페이드 캔버스 생성 및 페이드 인 시작 ───
        _fadeOverlay = CreateFadeOverlay();
        StartCoroutine(FadeInRoutine());

        StartCoroutine(LoadSceneSequence());
    }

    private IEnumerator LoadSceneSequence()
    {
        string nextScene = LoadingData.NextSceneName;

        // ─── Step 1: B씬 Additive 로드 (allowSceneActivation=false → 90%에서 정지) ──
        AsyncOperation op = SceneManager.LoadSceneAsync(nextScene, LoadSceneMode.Additive);
        if (op == null)
        {
            Debug.LogError($"[LoadingSceneController] 대상 씬('{nextScene}')을 Additive로 로드할 수 없습니다. Build Settings를 확인하세요.");
            yield break;
        }

        op.allowSceneActivation = false;

        float timer = 0f;

        // ─── Step 3: 에셋 스트리밍 (0% → 90%) ────────────────────────────
        while (op.progress < 0.9f)
        {
            yield return null;
            timer += Time.unscaledDeltaTime;
        }

        // ─── Step 4: 씬 활성화 전 LoadingScene의 UI 입력계 끄기 ─────────
        var es = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (es != null && es.gameObject.scene.name == "LoadingScene")
        {
            es.gameObject.SetActive(false);
        }

        // ─── Step 4.5: 씬 활성화 → B씬의 Awake/Start가 실행되며 맵 생성 시작 ─────────
        op.allowSceneActivation = true;
        yield return new WaitUntil(() => op.isDone);

        // ─── Step 4.6: AudioListener 등 나머지 중복 컴포넌트 비활성화 ─────────
        DisableDuplicateComponents();

        // ─── Step 5: B씬을 Active Scene으로 전환 및 데이터 로드 ────────────────────────
        Scene bScene = SceneManager.GetSceneByName(nextScene);
        if (bScene.IsValid())
        {
            SceneManager.SetActiveScene(bScene);

            if (GameManager.Instance != null && GameManager.Instance.saveManager != null)
            {
                Debug.Log($"[LoadingSceneController] {nextScene} 활성화 완료. 데이터 로드(SaveManager.Load)를 사전 수행합니다.");
                GameManager.Instance.saveManager.Load();

                if (nextScene == "DemoUnderground")
                {
                    GameManager.Instance.saveManager.ClearInventoriesForUnderground();
                }
            }
        }

        // ─── Step 6: 최소 대기시간 & IsReady 동시 대기 (병렬) ─────────────────────────
        while (!LoadingData.IsReady || timer < minimumLoadingTime)
        {
            yield return null;
            timer += Time.unscaledDeltaTime;
        }

        // ─── Step 6.5: "아무 키나 누르세요" 대기 (로딩이 너무 빨리 끝나는 것 방지) ─────
        if (anyKeyPrompt != null)
        {
            anyKeyPrompt.SetActive(true);

            // 입력 대기
            yield return new WaitUntil(() => Input.anyKeyDown);
        }

        // =========================================================
        // 🌟 Step 6.8: 다음 씬을 보여주기 전 화면을 부드럽게 까맣게 덮기 (페이드 아웃)
        // =========================================================
        yield return StartCoroutine(FadeOutRoutine());

        // ─── Step 7: LoadingScene 언로드 → 완성된 B씬 노출 (이후 B씬에서 페이드 인) ───────────────
        SceneManager.UnloadSceneAsync("LoadingScene");
    }

    #region Fade Effects

    private Image CreateFadeOverlay()
    {
        GameObject fadeObj = new GameObject("LoadingFadeOverlay");
        // LoadingScene이 언로드될 때 함께 지워지도록 이 컨트롤러의 자식으로 설정
        fadeObj.transform.SetParent(this.transform, false);

        Canvas canvas = fadeObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767; // 모든 UI 중 무조건 최상단 보장
        canvas.overrideSorting = true;

        Image fadeImage = fadeObj.AddComponent<Image>();
        fadeImage.color = new Color(0, 0, 0, 1); // 완전한 검은색으로 시작
        fadeImage.raycastTarget = true; // 페이드 중 클릭 방지

        return fadeImage;
    }

    private IEnumerator FadeInRoutine()
    {
        if (_fadeOverlay == null) yield break;

        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime;
            float alpha = 1f - Mathf.Clamp01(timer / fadeDuration);
            _fadeOverlay.color = new Color(0, 0, 0, alpha);
            yield return null;
        }
        _fadeOverlay.color = new Color(0, 0, 0, 0);
        _fadeOverlay.raycastTarget = false; // 입력 차단 해제 (아무 키나 누르세요 작동을 위해)
    }

    private IEnumerator FadeOutRoutine()
    {
        if (_fadeOverlay == null) yield break;

        _fadeOverlay.raycastTarget = true; // 다시 입력 차단
        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(timer / fadeDuration);
            _fadeOverlay.color = new Color(0, 0, 0, alpha);
            yield return null;
        }
        _fadeOverlay.color = new Color(0, 0, 0, 1); // 완전한 검은색
    }

    #endregion

    private void SetRandomTip()
    {
        if (tipText == null) return;
        if (LanguageManager.Instance == null)
        {
            Debug.LogWarning("[LoadingSceneController] LanguageManager.Instance가 null입니다.");
            return;
        }

        string randomTip = LanguageManager.Instance.GetRandomTextByPrefix("tip_");
        if (!string.IsNullOrEmpty(randomTip))
        {
            tipText.text = randomTip;
            Debug.Log($"[LoadingSceneController] Tip 설정 완료: {randomTip}");
        }
        else
        {
            Debug.LogWarning("[LoadingSceneController] 팁을 찾을 수 없습니다. (prefix: 'tip_')");
        }
    }

    private IEnumerator ChangeTipRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(tipChangeInterval);
            SetRandomTip();
        }
    }

    private void SetRandomVisual()
    {
        if (LanguageManager.Instance == null) return;

        var imgKeys = LanguageManager.Instance.GetKeysByPrefix("visual_img_");
        var vidKeys = LanguageManager.Instance.GetKeysByPrefix("visual_vid_");

        int totalCount = imgKeys.Count + vidKeys.Count;
        if (totalCount == 0) return;

        int randomIndex = Random.Range(0, totalCount);

        if (randomIndex < imgKeys.Count)
        {
            string path = LanguageManager.Instance.L(imgKeys[randomIndex]);
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite != null && mainVisualImage != null)
            {
                mainVisualImage.sprite = sprite;
                mainVisualImage.gameObject.SetActive(true);
                if (loadingVideoDisplay != null) loadingVideoDisplay.gameObject.SetActive(false);
            }
        }
        else
        {
            string path = LanguageManager.Instance.L(vidKeys[randomIndex - imgKeys.Count]);
            VideoClip clip = Resources.Load<VideoClip>(path);
            if (clip != null && videoPlayer != null && loadingVideoDisplay != null)
            {
                videoPlayer.clip = clip;
                videoPlayer.isLooping = true;
                videoPlayer.Play();

                loadingVideoDisplay.texture = videoPlayer.texture;

                loadingVideoDisplay.gameObject.SetActive(true);
                if (mainVisualImage != null) mainVisualImage.gameObject.SetActive(false);
            }
        }
    }

    private void DisableDuplicateComponents()
    {
        var eventSystems = FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None);
        if (eventSystems.Length > 1)
        {
            foreach (var es in eventSystems)
            {
                if (es.gameObject.scene.name == "LoadingScene")
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
                if (al.gameObject.scene.name == "LoadingScene")
                {
                    al.enabled = false;
                    break;
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (_tipCoroutine != null) StopCoroutine(_tipCoroutine);

        LoadingData.IsLoading = false;

        // 🌟 [추가됨] 로딩 씬 종료 시 유니티 전체 오디오 음소거 해제
        AudioListener.pause = false;
    }

    private void TriggerAutoDeposit()
    {
        WarehouseManager warehouse = WarehouseManager.Instance;
        if (warehouse == null) return;

        ItemInventory itemInv = null;
        MineralInventory mineralInv = null;
        EquipmentInventory toolInv = null;

        if (InventoryUI.Instance != null)
        {
            itemInv = InventoryUI.Instance.itemInventory;
            mineralInv = InventoryUI.Instance.mineralInventory;
            toolInv = InventoryUI.Instance.equipmentInventory;
        }

        if (itemInv == null) itemInv = FindFirstObjectByType<ItemInventory>(FindObjectsInactive.Include);
        if (mineralInv == null) mineralInv = FindFirstObjectByType<MineralInventory>(FindObjectsInactive.Include);
        if (toolInv == null) toolInv = FindFirstObjectByType<EquipmentInventory>(FindObjectsInactive.Include);

        if (LoadingData.IsEmergencyEscapePending)
        {
            if (mineralInv != null)
            {
                var items = new System.Collections.Generic.List<MineralSO>();
                foreach (var slot in mineralInv.ReadonlyItems)
                {
                    if (slot.item is MineralSO mineral && slot.quantity > 0)
                    {
                        for (int i = 0; i < slot.quantity; i++) items.Add(mineral);
                    }
                }

                int totalCount = items.Count;
                int dropCount = Mathf.FloorToInt(totalCount * 0.6f);

                for (int i = 0; i < totalCount; i++)
                {
                    int r = Random.Range(i, totalCount);
                    var temp = items[i];
                    items[i] = items[r];
                    items[r] = temp;
                }

                for (int i = 0; i < dropCount; i++)
                {
                    mineralInv.RemoveItem(items[i], 1);
                }
                Debug.Log($"[LoadingSceneController] 긴급 탈출 페널티 적용 완료: 총 {totalCount}개 중 {dropCount}개 삭제");
            }
            LoadingData.IsEmergencyEscapePending = false;
        }

        int beforeCount = mineralInv != null ? mineralInv.ReadonlyItems.Count : 0;
        warehouse.DepositAllFromInventory(itemInv, mineralInv, toolInv);
        int afterCount = mineralInv != null ? mineralInv.ReadonlyItems.Count : 0;

        Debug.Log($"[LoadingSceneController] 인벤토리 자동 창고 입고 완료. (광물 슬롯: {beforeCount} -> {afterCount})");

        if (GameManager.Instance != null && GameManager.Instance.saveManager != null)
        {
            GameManager.Instance.saveManager.Save();
            Debug.Log("[LoadingSceneController] 자동 입고 후 최종 세이브 완료.");
        }
    }
}