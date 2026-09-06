using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 스왑 시 중앙에 장착된 무기 이미지를 보여주고, 
/// 스왑 애니메이션(오른쪽으로 사라지고 새 무기가 나타나는 연출)을 처리하는 UI 클래스
/// </summary>
public class WeaponSwapUI : MonoBehaviour
{
    [Header("참조 컴포넌트")]
    [Tooltip("이벤트를 발생시킬 ToolController রেফার런스")]
    public ToolController toolController;
    
    [Header("UI 요소")]
    [Tooltip("UI 전용 무기 아이콘 목록. uiSpriteStartIndex번 도구부터 순서대로 대응된다.\n" +
             "맨손을 빼고 삽·곡괭이·드릴만 보여주려면 이 배열에 [삽, 곡괭이, 드릴]을 넣고 uiSpriteStartIndex=1로 둔다.")]
    public Sprite[] uiWeaponSprites;

    [Tooltip("uiWeaponSprites[0]이 대응하는 ToolController 도구 인덱스.\n" +
             "맨손(0)을 UI에서 제외하려면 1(=삽부터)로 둔다. 범위를 벗어난 도구(예: 맨손)는 아이콘이 숨겨진다.")]
    public int uiSpriteStartIndex = 1;

    [Tooltip("현재 보여지는 무기 이미지")]
    public Image currentWeaponImage;
    [Tooltip("애니메이션 연출 시 나타날 새 무기 이미지 (평소엔 비활성화 또는 교대로 사용)")]
    public Image nextWeaponImage;

    [Header("애니메이션 설정")]
    [Tooltip("슬라이드 애니메이션 진행 시간")]
    public float animationDuration = 0.3f;
    [Tooltip("오른쪽으로 사라질 때의 X축 이동 거리")]
    public float slideOffset = 100f;

    // 코루틴 캐싱용
    private Coroutine swapCoroutine;

    // 레이아웃 기본 위치 기억용 (원래 중앙 위치)
    private Vector2 defaultAnchoredPos;

    void Start()
    {
        // 초기화 설정
        if (toolController != null)
        {
            toolController.OnToolSwapped += HandleToolSwapped;
            
            // 초기 무기 이미지 설정
            InitWeaponImage();
        }

        if (currentWeaponImage != null)
        {
            defaultAnchoredPos = currentWeaponImage.rectTransform.anchoredPosition;
        }

        if (nextWeaponImage != null)
        {
            nextWeaponImage.gameObject.SetActive(false); // 보조 이미지는 평소에 숨김
        }
    }

    void OnDestroy()
    {
        if (toolController != null)
        {
            toolController.OnToolSwapped -= HandleToolSwapped;
        }
    }

    private void InitWeaponImage()
    {
        if (currentWeaponImage != null && toolController != null)
        {
            Sprite initialSprite = GetUISprite(toolController.currentToolIndex);
            if (initialSprite != null)
            {
                currentWeaponImage.sprite = initialSprite;
                currentWeaponImage.enabled = true;
            }
            else
            {
                currentWeaponImage.enabled = false;
            }
        }
    }

    private Sprite GetUISprite(int index)
    {
        if (uiWeaponSprites == null) return null;

        // ToolController 도구 인덱스 → uiWeaponSprites 배열 인덱스로 오프셋 변환.
        // uiSpriteStartIndex보다 작은 도구(맨손 등)는 대응 아이콘이 없어 숨긴다(null).
        int uiIndex = index - uiSpriteStartIndex;
        if (uiIndex >= 0 && uiIndex < uiWeaponSprites.Length)
        {
            return uiWeaponSprites[uiIndex];
        }
        return null;
    }

    /// <summary>
    /// ToolController에서 무기 스왑 이벤트 발생 시 호출되는 콜백
    /// </summary>
    /// <param name="previousIndex">이전에 장착했던 도구 인덱스</param>
    /// <param name="newIndex">새로 장착된 도구 인덱스</param>
    private void HandleToolSwapped(int previousIndex, int newIndex)
    {
        if (toolController == null) return;

        // // [추가] 사용자가 무기 순서를 맞출 수 있도록 콘솔에 인덱스 출력
        // Debug.Log($"[WeaponSwapUI] 현재 변경된 무기 인덱스 (uiWeaponSprites의 {previousIndex}번째에 넣으세요): {newIndex}");

        Sprite newSprite = GetUISprite(newIndex);
        
        // 애니메이션 워크플로우 실행
        if (swapCoroutine != null)
        {
            StopCoroutine(swapCoroutine);
        }
        
        swapCoroutine = StartCoroutine(WeaponSwapAnimationWorkflow(newSprite));
    }

    /// <summary>
    /// [애니메이션 워크플로우]
    /// 1. 기존 무기는 오른쪽(+slideOffset)으로 이동하며 나타나지 않게(알파 0 또는 비활성화) 처리
    /// 2. 새 무기는 오른쪽(기존 무기가 들어간 곳)에서 시작하여 왼쪽 방향으로 중앙에 나타남
    /// 3. 애니메이션 완료 후 currentWeaponImage를 갱신하고 nextWeaponImage를 숨김
    /// (이 코루틴은 추후 DOTween 등으로 교체 용이하게 작성됨)
    /// </summary>
    private IEnumerator WeaponSwapAnimationWorkflow(Sprite newSprite)
    {
        if (currentWeaponImage == null || nextWeaponImage == null)
        {
            // UI 에러 시 즉시 교체 폴백
            if (currentWeaponImage != null) 
            {
                currentWeaponImage.sprite = newSprite;
                currentWeaponImage.enabled = (newSprite != null);
            }
            yield break;
        }

        // --- 1. 설정 단계 ---
        // nextWeaponImage에 새 스프라이트 설정
        nextWeaponImage.sprite = newSprite;
        nextWeaponImage.enabled = (newSprite != null);
        nextWeaponImage.gameObject.SetActive(true);

        RectTransform currentRect = currentWeaponImage.rectTransform;
        RectTransform nextRect = nextWeaponImage.rectTransform;

        // 시작 상태 설정
        currentRect.anchoredPosition = defaultAnchoredPos;

        // 새 무기는 오른쪽 위치(기존 무기가 들어간 곳)에서 다시 등장하여 중앙으로 들어옴
        nextRect.anchoredPosition = defaultAnchoredPos + new Vector2(slideOffset, 0);

        // --- 2. 애니메이션 진행 (루프) ---
        float elapsed = 0f;
        while (elapsed < animationDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / animationDuration;
            // 부드러운 감속 이징 효과 (Ease Out)
            float easeT = 1f - Mathf.Pow(1f - t, 3f); 

            // 현재 무기: 중앙 -> 오른쪽으로 이동
            currentRect.anchoredPosition = Vector2.Lerp(defaultAnchoredPos, defaultAnchoredPos + new Vector2(slideOffset, 0), easeT);

            // 새 무기: 오른쪽 -> 왼쪽 방향으로 중앙에 당겨져 들어옴
            nextRect.anchoredPosition = Vector2.Lerp(defaultAnchoredPos + new Vector2(slideOffset, 0), defaultAnchoredPos, easeT);

            yield return null;
        }

        // --- 3. 완료 및 정리 단계 ---
        // 최종 교체
        currentWeaponImage.sprite = newSprite;
        currentWeaponImage.enabled = (newSprite != null);
        
        // 위치 초기화
        currentRect.anchoredPosition = defaultAnchoredPos;
        
        nextWeaponImage.gameObject.SetActive(false);

        swapCoroutine = null;
    }
}
