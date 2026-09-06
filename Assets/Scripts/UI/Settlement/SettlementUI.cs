using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 정산 결과를 화면에 표시하는 UI 컴포넌트.
/// </summary>
public class SettlementUI : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject contentPanel;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Text References")]
    [SerializeField] private TextMeshProUGUI depthText;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI mineralTotalText;
    [SerializeField] private TextMeshProUGUI profitText;
    [SerializeField] private TextMeshProUGUI continueText; // 추가된 확인 안내 텍스트

    [Header("Mineral List")]
    [SerializeField] private RectTransform mineralListParent;
    [SerializeField] private GameObject mineralItemPrefab; // 아이콘 + 이름 + 수량이 포함된 프리팹

    [Header("Settings")]
    [SerializeField] private float fadeInDuration = 0.5f;
    
    [Header("Sequence Settings")]
    [SerializeField] private float totalMineralDisplayTime = 1f;
    [SerializeField] private float elementDelay = 0.5f;
    [SerializeField] private MineralPriceDatabase priceDatabase; // 수익 계산용

    public bool IsSequenceComplete { get; private set; }
    public bool IsConfirmed { get; private set; } // 추가된 확인 여부

    private List<GameObject> _createdMineralItems = new List<GameObject>();
    private Coroutine _sequenceCoroutine;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (contentPanel != null) contentPanel.SetActive(false);
    }

    public void Show(SettlementData data)
    {
        Debug.Log("[SettlementUI] Show() 시작");
        
        IsSequenceComplete = false;
        
        // 데이터 설정
        if (depthText != null) depthText.text = $"{data.maxDepth:F1}m";
        
        int minutes = Mathf.FloorToInt(data.timeUnderground / 60);
        int seconds = Mathf.FloorToInt(data.timeUnderground % 60);
        if (timeText != null) timeText.text = $"{minutes:00}:{seconds:00}";

        // 광물 리스트 생성 및 수익 계산
        ClearMineralList();
        int totalCount = 0;
        int totalProfit = 0;

        foreach (var pair in data.minedMinerals)
        {
            CreateMineralItem(pair.Key, pair.Value);
            totalCount += pair.Value;
            
            if (priceDatabase != null)
            {
                totalProfit += priceDatabase.GetPrice(pair.Key) * pair.Value;
            }
        }
        
        if (mineralTotalText != null) mineralTotalText.text = $"총 {totalCount}개 획득";
        if (profitText != null) profitText.text = $"{totalProfit} G";
        
        if (continueText != null)
        {
            if (LanguageManager.Instance != null)
                continueText.text = LanguageManager.Instance.L("ui_settlement_continue");
            else
                continueText.text = "Press any key to continue";
        }

        // 초기 상태: 모두 활성화하되 투명하게 설정 (레이아웃 뒤틀림 방지)
        SetAlpha(mineralTotalText?.gameObject, 0);
        SetAlpha(depthText?.gameObject, 0);
        SetAlpha(timeText?.gameObject, 0);
        SetAlpha(profitText?.gameObject, 0);
        SetAlpha(continueText?.gameObject, 0);
        
        foreach (var item in _createdMineralItems)
        {
            item.SetActive(true);
            SetAlpha(item, 0);
        }

        IsConfirmed = false;

        // 애니메이션 효과와 함께 표시
        if (_sequenceCoroutine != null) StopCoroutine(_sequenceCoroutine);
        _sequenceCoroutine = StartCoroutine(ShowSequence());
    }

    private void ClearMineralList()
    {
        _createdMineralItems.Clear();
        foreach (Transform child in mineralListParent)
        {
            Destroy(child.gameObject);
        }
    }

    private void CreateMineralItem(MineralID id, int count)
    {
        if (mineralItemPrefab == null) return;
        
        GameObject itemObj = Instantiate(mineralItemPrefab, mineralListParent);
        SettlementMineralItem itemScript = itemObj.GetComponent<SettlementMineralItem>();
        
        if (itemScript != null)
        {
            itemScript.SetData(id, count);
        }
        else
        {
            // 백업: 스크립트가 없을 경우 자식의 TMP 텍스트라도 업데이트 시도
            var text = itemObj.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null) text.text = $"{id}: {count}개";
        }
        
        _createdMineralItems.Add(itemObj);
    }

    private IEnumerator ShowSequence()
    {
        contentPanel.SetActive(true);
        canvasGroup.alpha = 0;
        
        // 1. 전체 패널 페이드인
        float elapsed = 0;
        while (elapsed < fadeInDuration)
        {
            if (Input.anyKeyDown) { SkipSequence(); yield break; }
            
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeInDuration);
            yield return null;
        }
        canvasGroup.alpha = 1;

        // 2. 총 캔 광물 및 리스트 표시 (순서 1)
        if (mineralTotalText != null)
        {
            if (Input.anyKeyDown) { SkipSequence(); yield break; }
            SetAlpha(mineralTotalText.gameObject, 1);
            yield return WaitWithSkip(elementDelay);
        }

        if (_createdMineralItems.Count > 0)
        {
            float mineralWaitTime = totalMineralDisplayTime / _createdMineralItems.Count;
            foreach (var item in _createdMineralItems)
            {
                if (Input.anyKeyDown) { SkipSequence(); yield break; }
                SetAlpha(item, 1);
                
                float wTimer = 0;
                while (wTimer < mineralWaitTime)
                {
                    if (Input.anyKeyDown) { SkipSequence(); yield break; }
                    wTimer += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            yield return WaitWithSkip(elementDelay);
        }

        // 3. 캔 최대 깊이 (순서 2)
        if (depthText != null)
        {
            if (Input.anyKeyDown) { SkipSequence(); yield break; }
            SetAlpha(depthText.gameObject, 1);
            yield return WaitWithSkip(elementDelay);
        }

        // 4. 지하에 있었던 시간 (순서 3)
        if (timeText != null)
        {
            if (Input.anyKeyDown) { SkipSequence(); yield break; }
            SetAlpha(timeText.gameObject, 1);
            yield return WaitWithSkip(elementDelay);
        }

        // 5. 예상 수익 (순서 4)
        if (profitText != null)
        {
            if (Input.anyKeyDown) { SkipSequence(); yield break; }
            SetAlpha(profitText.gameObject, 1);
            yield return WaitWithSkip(elementDelay);
        }

        IsSequenceComplete = true;

        // 6. 확인 대기
        yield return WaitForConfirmation();
    }

    private IEnumerator WaitForConfirmation()
    {
        if (continueText != null) SetAlpha(continueText.gameObject, 1);
        
        // 이전 프레임의 입력이 남아있을 수 있으므로 한 프레임 대기
        yield return null;

        while (!IsConfirmed)
        {
            if (Input.anyKeyDown)
            {
                IsConfirmed = true;
            }
            yield return null;
        }
    }

    private IEnumerator WaitWithSkip(float duration)
    {
        float timer = 0;
        while (timer < duration)
        {
            if (Input.anyKeyDown)
            {
                SkipSequence();
                yield break;
            }
            timer += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void SkipSequence()
    {
        if (_sequenceCoroutine != null)
        {
            StopCoroutine(_sequenceCoroutine);
        }

        canvasGroup.alpha = 1;
        
        SetAlpha(mineralTotalText?.gameObject, 1);
        SetAlpha(depthText?.gameObject, 1);
        SetAlpha(timeText?.gameObject, 1);
        SetAlpha(profitText?.gameObject, 1);
        
        foreach (var item in _createdMineralItems) SetAlpha(item, 1);

        IsSequenceComplete = true;
        
        // 스킵 시에도 확인 대기 시작
        _sequenceCoroutine = StartCoroutine(WaitForConfirmation());
    }

    private void SetAlpha(GameObject obj, float alpha)
    {
        if (obj == null) return;

        // CanvasGroup이 있으면 우선 사용
        CanvasGroup cg = obj.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = alpha;
            return;
        }

        // 없으면 텍스트와 이미지 각각 처리
        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        if (text != null)
        {
            Color c = text.color;
            c.a = alpha;
            text.color = c;
        }

        Image img = obj.GetComponent<Image>();
        if (img != null)
        {
            Color c = img.color;
            c.a = alpha;
            img.color = c;
        }

        // 자식 요소들도 처리 (예: 아이콘 + 텍스트 형태의 광물 아이템)
        foreach (Transform child in obj.transform)
        {
            SetAlpha(child.gameObject, alpha);
        }
    }
}
