using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

/// <summary>
/// DemoUnderground 씬 초기 세팅 도구
/// 메뉴: Tools > Setup Demo Underground Scene
/// </summary>
public static class DemoUndergroundSetup
{
    [MenuItem("Tools/Setup Demo Underground Scene")]
    public static void SetupScene()
    {
        CreateConfirmationPromptUI(out ConfirmationPrompt prompt);
        CreateExitTrigger(prompt);
        CreateIndestructibleFloors();

        Debug.Log("[DemoUndergroundSetup] 세팅 완료. 각 오브젝트 위치를 씬에 맞게 조정하세요.");
    }

    // ────────────────────────────────────────────────
    // ConfirmationPrompt UI
    // ────────────────────────────────────────────────
    private static void CreateConfirmationPromptUI(out ConfirmationPrompt promptOut)
    {
        // Canvas
        GameObject canvasGO = new GameObject("ExitConfirmCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create ExitConfirmCanvas");

        // Prompt 패널
        GameObject panelGO = new GameObject("ConfirmationPrompt");
        panelGO.transform.SetParent(canvasGO.transform, false);
        RectTransform panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.sizeDelta = new Vector2(480, 280);
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;

        Image panelImg = panelGO.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.92f);

        promptOut = panelGO.AddComponent<ConfirmationPrompt>();

        // Title
        GameObject titleGO = CreateTMPText(panelGO.transform, "TitleText", "탐험 종료",
            new Vector2(0, 90), new Vector2(440, 50), 28, FontStyles.Bold);

        // Message
        GameObject msgGO = CreateTMPText(panelGO.transform, "MessageText", "탐험을 종료하시겠습니까?",
            new Vector2(0, 20), new Vector2(440, 40), 22, FontStyles.Normal);

        // Confirm 버튼
        GameObject confirmGO = CreateButton(panelGO.transform, "ConfirmButton", "확인",
            new Vector2(-90, -70), new Vector2(160, 50), new Color(0.2f, 0.6f, 0.3f));

        // Cancel 버튼
        GameObject cancelGO = CreateButton(panelGO.transform, "CancelButton", "취소",
            new Vector2(90, -70), new Vector2(160, 50), new Color(0.6f, 0.2f, 0.2f));

        // ConfirmationPrompt 필드 연결 (SerializedObject 사용)
        SerializedObject so = new SerializedObject(promptOut);
        so.FindProperty("titleText").objectReferenceValue = titleGO.GetComponent<TextMeshProUGUI>();
        so.FindProperty("messageText").objectReferenceValue = msgGO.GetComponent<TextMeshProUGUI>();
        so.ApplyModifiedProperties();

        // 버튼 이벤트: OnClickConfirm / OnClickCancel 연결
        Button confirmBtn = confirmGO.GetComponent<Button>();
        Button cancelBtn = cancelGO.GetComponent<Button>();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            confirmBtn.onClick, promptOut.OnClickConfirm);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            cancelBtn.onClick, promptOut.OnClickCancel);

        // 시작 시 숨김
        panelGO.SetActive(false);
    }

    // ────────────────────────────────────────────────
    // ExitTrigger
    // ────────────────────────────────────────────────
    private static void CreateExitTrigger(ConfirmationPrompt prompt)
    {
        GameObject triggerGO = new GameObject("ExitTrigger");
        triggerGO.transform.position = new Vector3(0, 14f, 0); // 플레이어 위치에 맞게 조정 필요
        Undo.RegisterCreatedObjectUndo(triggerGO, "Create ExitTrigger");

        BoxCollider2D col = triggerGO.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(20f, 2f); // 씬 폭에 맞게 조정

        ExploreExitController controller = triggerGO.AddComponent<ExploreExitController>();
        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("targetScene").stringValue = "BackGround_Tree";
        so.FindProperty("confirmationPrompt").objectReferenceValue = prompt;
        so.ApplyModifiedProperties();

        // 에디터에서 보이도록 레이어 색
        triggerGO.layer = LayerMask.NameToLayer("Default");
    }

    // ────────────────────────────────────────────────
    // IndestructibleFloor 블록 3개
    // ────────────────────────────────────────────────
    private static void CreateIndestructibleFloors()
    {
        // 엘리베이터 아래 위치 — 실제 씬에 맞게 인스펙터에서 조정 필요
        Vector3[] positions = {
            new Vector3(-1f, 9f, 0),
            new Vector3(0f,  9f, 0),
            new Vector3(1f,  9f, 0),
        };

        for (int i = 0; i < positions.Length; i++)
        {
            GameObject block = new GameObject($"IndestructibleFloor_{i}");
            block.transform.position = positions[i];
            block.transform.localScale = new Vector3(1f, 1f, 1f);
            Undo.RegisterCreatedObjectUndo(block, "Create IndestructibleFloor");

            // 충돌체 (파괴 불가 = TerrainChunk/IDiggable 없음)
            BoxCollider2D col = block.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;

            // 시각적 표시 (흰색 사각형, 나중에 실제 타일 Sprite로 교체)
            SpriteRenderer sr = block.AddComponent<SpriteRenderer>();
            sr.color = new Color(0.3f, 0.3f, 0.35f);
            sr.sortingLayerName = "Default";
        }
    }

    // ────────────────────────────────────────────────
    // 헬퍼: TMP 텍스트 생성
    // ────────────────────────────────────────────────
    private static GameObject CreateTMPText(Transform parent, string name, string text,
        Vector2 anchoredPos, Vector2 size, float fontSize, FontStyles style)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        return go;
    }

    // ────────────────────────────────────────────────
    // 헬퍼: 버튼 생성
    // ────────────────────────────────────────────────
    private static GameObject CreateButton(Transform parent, string name, string label,
        Vector2 anchoredPos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        Image img = go.AddComponent<Image>();
        img.color = color;

        Button btn = go.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = color * 1.2f;
        cb.pressedColor = color * 0.8f;
        btn.colors = cb;

        // 버튼 텍스트
        GameObject labelGO = new GameObject("Text");
        labelGO.transform.SetParent(go.transform, false);
        RectTransform labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = labelRT.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 20;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return go;
    }
}
