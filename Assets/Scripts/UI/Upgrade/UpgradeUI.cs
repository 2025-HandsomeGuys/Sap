using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 업그레이드 UI 메인 클래스입니다.
/// 트리 구조를 시각화하고 슬롯들을 생성/배치합니다.
/// </summary>
public class UpgradeUI : MonoBehaviour
{
    [Header("Preferences")]
    public UpgradeTreeSO upgradeTree; // 표시할 트리 데이터
    public GameObject slotPrefab;
    public GameObject linePrefab; // UILineRenderer 컴포넌트가 달린 이미지 프리팹
    public GameObject tierPrefab; // 계층 배경/오버레이 프리팹
    public Transform contentArea; // Scroll View의 Content


    // 생성된 슬롯 관리 (NodeID -> SlotUI)
    private Dictionary<string, UpgradeSlotUI> _slotMap = new Dictionary<string, UpgradeSlotUI>();
    
    // 생성된 계층 UI 관리
    private List<UpgradeTierUI> _tierUIs = new List<UpgradeTierUI>();

    // 생성된 연결선 관리
    private List<OrthogonalUILineRenderer> _lineRenderers = new List<OrthogonalUILineRenderer>();
    private Dictionary<string, List<OrthogonalUILineRenderer>> _nodeToLines = new Dictionary<string, List<OrthogonalUILineRenderer>>();

    private float _globalCenterY = 0f; // 전체 노드의 Y축 중심 오프셋 캐싱

    void Start()
    {
        // 초기화
        if (UpgradeManager.Instance != null)
        {
            UpgradeManager.Instance.OnUpgradeStateChanged += RefreshUI;
        }


        CreateTreeUI();
    }


    public void CloseUpgrade()
    {
        if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.CloseAll();
        }
    }


    void OnDestroy()
    {
        // Instance가 이미 파괴되었거나 앱 종료 중이면 null이 반환되어 생성이 방지됨
        if (UpgradeManager.Instance != null)
        {
            UpgradeManager.Instance.OnUpgradeStateChanged -= RefreshUI;
        }
    }

    public void RefreshUI()
    {
        // 계층 상태 업데이트 (오버레이 표시 여부)
        foreach (var tierUI in _tierUIs)
        {
            if (tierUI != null)
                tierUI.UpdateState();
        }

        // 슬롯 상태 업데이트
        foreach (var kvp in _slotMap)
        {
            if (kvp.Value != null)
                kvp.Value.UpdateState();
        }
        
        // 연결선 색상 업데이트
        UpdateLineColors();
    }

    private void UpdateLineColors()
    {
        foreach (var kvp in _nodeToLines)
        {
            string parentNodeId = kvp.Key;
            bool isUnlocked = UpgradeManager.Instance.IsNodeUnlocked(parentNodeId);
            
            foreach (var line in kvp.Value)
            {
                if (line != null)
                {
                    line.SetUnlocked(isUnlocked);
                }
            }
        }
    }

    private void CreateTreeUI()
    {
        // 기존 자식 제거
        foreach (Transform child in contentArea)
        {
            Destroy(child.gameObject);
        }
        _slotMap.Clear();
        _lineRenderers.Clear();
        _nodeToLines.Clear();
        _tierUIs.Clear();

        if (upgradeTree == null || upgradeTree.allNodes == null) return;

        // --- 전체 노드의 Y축 중심점(globalCenterY) 연산 ---
        float globalMinY = float.MaxValue;
        float globalMaxY = float.MinValue;
        foreach (var node in upgradeTree.allNodes)
        {
            globalMinY = Mathf.Min(globalMinY, node.uiPosition.y);
            globalMaxY = Mathf.Max(globalMaxY, node.uiPosition.y);
        }
        _globalCenterY = (globalMinY + globalMaxY) / 2f;

        // --- 레이어 구분을 위한 컨테이너 생성 ---
        // 1. 배경 레이어 (가장 뒤)
        GameObject bgLayer = new GameObject("BackgroundLayer", typeof(RectTransform));
        bgLayer.transform.SetParent(contentArea, false);
        SetFullStretch(bgLayer.GetComponent<RectTransform>());

        // 2. 연결선 레이어
        GameObject lineLayer = new GameObject("LineLayer", typeof(RectTransform));
        lineLayer.transform.SetParent(contentArea, false);
        SetFullStretch(lineLayer.GetComponent<RectTransform>());

        // 3. 노드 레이어
        GameObject nodeLayer = new GameObject("NodeLayer", typeof(RectTransform));
        nodeLayer.transform.SetParent(contentArea, false);
        SetFullStretch(nodeLayer.GetComponent<RectTransform>());

        // 4. 오버레이 레이어 (가장 앞)
        GameObject overlayLayer = new GameObject("OverlayLayer", typeof(RectTransform));
        overlayLayer.transform.SetParent(contentArea, false);
        SetFullStretch(overlayLayer.GetComponent<RectTransform>());

        // --- 계층(Tier) UI 생성 ---
        // 트리에 존재하는 모든 TierIndex 수집
        HashSet<int> tierIndices = new HashSet<int>();
        foreach (var node in upgradeTree.allNodes)
        {
            tierIndices.Add(node.tier);
        }

        Dictionary<int, UpgradeTierUI> tierMap = new Dictionary<int, UpgradeTierUI>();

        foreach (int tIndex in tierIndices)
        {
            if (tierPrefab != null)
            {
                GameObject tierObj = Instantiate(tierPrefab, contentArea);
                UpgradeTierUI tierUI = tierObj.GetComponent<UpgradeTierUI>();
                
                if (tierUI != null)
                {
                    TierInfo info = upgradeTree.GetTierInfo(tIndex);
                    tierUI.Init(tIndex, info);
                    
                    // 해당 계층 노드들의 Y 범위를 계산하여 배경/오버레이 크기 결정
                    float minY = float.MaxValue;
                    float maxY = float.MinValue;
                    foreach (var node in upgradeTree.allNodes)
                    {
                        if (node.tier == tIndex)
                        {
                            minY = Mathf.Min(minY, node.uiPosition.y);
                            maxY = Mathf.Max(maxY, node.uiPosition.y);
                        }
                    }
                    
                    // 여백 적용 (노드 크기 고려)
                    float padding = 150f;
                    float height = (maxY - minY) + padding * 2;
                    float centerY = (minY + maxY) / 2f;
                    float adjustedCenterY = centerY - _globalCenterY; // 평행이동 오프셋 보정

                    // 배경과 오버레이를 각각 레이어 컨테이너로 이동시키고 위치/크기 설정
                    if (tierUI.backgroundRect != null)
                    {
                        tierUI.backgroundRect.SetParent(bgLayer.transform, false);
                        SetupTierRect(tierUI.backgroundRect, adjustedCenterY, height);
                    }
                    
                    if (tierUI.lockedOverlay != null)
                    {
                        RectTransform overlayRect = tierUI.lockedOverlay.GetComponent<RectTransform>();
                        overlayRect.SetParent(overlayLayer.transform, false);
                        SetupTierRect(overlayRect, adjustedCenterY, height);
                    }

                    _tierUIs.Add(tierUI);
                    tierMap.Add(tIndex, tierUI);
                }
            }
        }

        // 1. 모든 노드 슬롯 생성 (Y축 평행이동 오프셋 보정)
        foreach (var node in upgradeTree.allNodes)
        {
            GameObject slotObj = Instantiate(slotPrefab, nodeLayer.transform);
            UpgradeSlotUI slotUI = slotObj.GetComponent<UpgradeSlotUI>();
            
            RectTransform rect = slotObj.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchoredPosition = new Vector2(node.uiPosition.x, node.uiPosition.y - _globalCenterY);
            }

            slotUI.Init(node, this);
            _slotMap.Add(node.nodeId, slotUI);
        }

        // 2. 연결 선 그리기
        foreach (var node in upgradeTree.allNodes)
        {
            if (node.parentNodes == null) continue;

            foreach (var parent in node.parentNodes)
            {
                if (_slotMap.ContainsKey(parent.nodeId) && _slotMap.ContainsKey(node.nodeId))
                {
                    CreateConnection(parent, node, lineLayer.transform);
                }
            }
        }

        // 3. Content Area 크기 동적 조절 (자식 노드들의 Y 범위를 감싸도록 확장)
        if (contentArea != null && upgradeTree.allNodes.Count > 0)
        {
            // 위아래 여백을 넉넉히 제공 (상단, 하단 각각 200px 패딩)
            float padding = 200f;
            float totalHeight = (globalMaxY - globalMinY) + padding * 2f;

            RectTransform contentRect = contentArea.GetComponent<RectTransform>();
            if (contentRect != null)
            {
                contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, totalHeight);
                
                // 첫 진입 시 스크롤 위치가 Tier 0(가장 아랫부분)에 머무르도록 초기 뷰포트 설정 지원
                ScrollRect scrollRect = GetComponentInChildren<ScrollRect>();
                if (scrollRect != null)
                {
                    // Y가 -200부터 1500으로 자라나므로 아래(낮은 Y)가 Tier 0입니다.
                    // 유니티 ScrollRect의 verticalNormalizedPosition은 0이 가장 아래(낮은 Y)입니다.
                    scrollRect.verticalNormalizedPosition = 0f;
                }
            }
        }
    }

    private void SetFullStretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void CreateConnection(UpgradeNodeSO parent, UpgradeNodeSO child, Transform parentTransform)
    {
        GameObject lineObj = Instantiate(linePrefab, parentTransform);

        // 기존 UILineRenderer 대신 OrthogonalUILineRenderer 사용
        OrthogonalUILineRenderer orthogonalLineRenderer = lineObj.GetComponent<OrthogonalUILineRenderer>();
        if (orthogonalLineRenderer == null)
        {
            // 만약 프리팹에 OrthogonalUILineRenderer가 없으면 추가
            orthogonalLineRenderer = lineObj.AddComponent<OrthogonalUILineRenderer>();
        }

        if (orthogonalLineRenderer != null)
        {
            // 노드 위치 가져오기 (Y축 평행이동 오프셋 보정)
            Vector2 startPos = new Vector2(parent.uiPosition.x, parent.uiPosition.y - _globalCenterY);
            Vector2 endPos = new Vector2(child.uiPosition.x, child.uiPosition.y - _globalCenterY);
            
            // 부모 노드가 해금되었는지 확인
            bool isParentUnlocked = UpgradeManager.Instance.IsNodeUnlocked(parent.nodeId);
            
            // 직각 선 그리기
            orthogonalLineRenderer.DrawOrthogonalLine(startPos, endPos, isParentUnlocked);
            
            // 연결선 관리 리스트에 추가
            _lineRenderers.Add(orthogonalLineRenderer);
            
            // 부모 노드와 연결선 매핑
            if (!_nodeToLines.ContainsKey(parent.nodeId))
            {
                _nodeToLines[parent.nodeId] = new List<OrthogonalUILineRenderer>();
            }
            _nodeToLines[parent.nodeId].Add(orthogonalLineRenderer);
        }
        
        // (선택사항) 기존 UILineRenderer도 사용 가능하도록 폴백
        UILineRenderer lineRenderer = lineObj.GetComponent<UILineRenderer>();
        if (lineRenderer != null && orthogonalLineRenderer == null)
        {
            Vector2 startPos = new Vector2(parent.uiPosition.x, parent.uiPosition.y - _globalCenterY);
            Vector2 endPos = new Vector2(child.uiPosition.x, child.uiPosition.y - _globalCenterY);
            lineRenderer.DrawLine(startPos, endPos);
        }
    }

    private void SetupTierRect(RectTransform rt, float centerY, float height)
    {
        // 가로로 꽉 차게, 세로는 계산된 중앙값과 높이 적용
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        
        // 가로 스트레치를 위해 Left/Right를 0으로 (sizeDelta.x는 0, anchoredPosition.x도 0)
        rt.sizeDelta = new Vector2(0, height);
        rt.anchoredPosition = new Vector2(0, centerY);
        
        // offsetMin/Max 초기화하여 가로 스트레치 보장
        rt.offsetMin = new Vector2(0, rt.offsetMin.y);
        rt.offsetMax = new Vector2(0, rt.offsetMax.y);
    }
}
