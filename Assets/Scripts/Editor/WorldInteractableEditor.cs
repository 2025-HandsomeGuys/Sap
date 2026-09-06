// @tags: editor, inspector, interaction, world, kind, custom-editor
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="WorldInteractable"/> 전용 인스펙터.
/// 종류를 고르면 <b>그 종류에 필요한 항목만</b> 보여준다 —
/// 통합 컴포넌트라 필드가 다 노출되면 뭘 채워야 할지 알 수 없기 때문.
/// 필드에 붙은 [Header]가 그대로 구분선 역할을 하므로 여기서 제목을 따로 그리지 않는다.
/// </summary>
[CustomEditor(typeof(WorldInteractable))]
[CanEditMultipleObjects]
public class WorldInteractableEditor : Editor
{
    /// <summary>종류별로 보여줄 필드. 여기 없는 필드는 그 종류에서 숨는다.</summary>
    private static readonly Dictionary<WorldInteractableKind, string[]> KindFields =
        new Dictionary<WorldInteractableKind, string[]>
        {
            { WorldInteractableKind.ChunkEntrance,  new[] { "chunkPrefab", "exploredColor" } },
            { WorldInteractableKind.ChunkExit,      new[] { "clearRewardGold" } },
            { WorldInteractableKind.Elevator,       new[] { "elevatorXChunk", "elevatorLayerIndex" } },
            { WorldInteractableKind.TunnelEntrance, new[] { "sceneToLoad", "blockAtNight" } },
            { WorldInteractableKind.SubQuestBoard,  new string[0] },
            { WorldInteractableKind.TruckNpc,       new[] { "npcName", "dialogueData", "mainQuestData",
                                                            "isMainQuestNpc", "canOpenShop",
                                                            "popupOffset" } },
            { WorldInteractableKind.Bed,            new string[0] },
            { WorldInteractableKind.Wardrobe,       new string[0] },
            { WorldInteractableKind.MarketTerminal, new[] { "marketSceneName" } },
            { WorldInteractableKind.ElevatorEntrance, new string[0] },
            { WorldInteractableKind.Workbench,        new string[0] },
            { WorldInteractableKind.SurfaceExit,      new string[0] },
        };

    /// <summary>종류 설명 + 원래 이 일을 하던 컴포넌트(교체 대상 확인용).</summary>
    private static readonly Dictionary<WorldInteractableKind, string> KindHelp =
        new Dictionary<WorldInteractableKind, string>
        {
            { WorldInteractableKind.ChunkEntrance,
              "지하에서 청크(던전) 안으로 들어간다. 씬 전환이 아니라 오버레이 방식.\n" +
              "이미 탐험한 입구는 자동으로 잠기고 회색이 된다.\n기존: DungeonDoorChunk" },
            { WorldInteractableKind.ChunkExit,
              "청크(던전)에서 지하로 돌아온다. 이 입구를 '탐험 완료'로 찍고 저장한다.\n기존: DungeonExitInteractable" },
            { WorldInteractableKind.Elevator,
              "층 이동 UI를 연다. 좌표·매니저 등록은 ElevatorSpawner가 심는 ElevatorController가 계속 담당하므로,\n" +
              "생성되는 엘리베이터라면 아래 좌표는 비워둬도 된다.\n기존: ElevatorController(상호작용 부분)" },
            { WorldInteractableKind.TunnelEntrance,
              "씬을 통째로 갈아 지상 ↔ 지하를 오간다. 목적지 이름으로 방향을 판단해 저장 처리까지 갈라준다.\n" +
              "기존: SceneTransitionTrigger / DungeonEntranceInteractable" },
            { WorldInteractableKind.SubQuestBoard,
              "서브퀘스트 게시판 UI를 연다. 설정할 것 없음." },
            { WorldInteractableKind.TruckNpc,
              "머리 위 팝업(상점·업그레이드·퀘스트·대화)을 띄우고, 고른 항목의 UI로 이어진다.\n" +
              "업그레이드(연구 트리)는 작업대(Workbench)로 옮겨졌다.\n" +
              "팝업은 useCodeBuiltPopupUI가 켜져 있으면 코드 생성 오버레이, 아니면 씬의 NpcPopup 프리팹이 그린다." },
            { WorldInteractableKind.Bed,
              "저녁에만 잘 수 있다. 자면 하루 정산 연출 후 다음 날 아침이 된다.\n기존: BedInteractable" },
            { WorldInteractableKind.Wardrobe,
              "지상 창고(창고 + 가방 통합 화면)를 연다. Tab 키와 같은 경로라 ESC/Tab으로 닫힌다.\n" +
              "UIStateManager의 useCodeBuiltWarehouseUI가 켜져 있어야 한다.\n기존: 없음(Tab 키로만 열렸음)" },
            { WorldInteractableKind.MarketTerminal,
              "마켓 씬을 Additive로 얹는다. 진입 동안 게임 시간이 멈춘다.\n" +
              "해금: 업그레이드 노드 Facility_Computer_T0" },
            { WorldInteractableKind.ElevatorEntrance,
              "지상에서 지하 엘리베이터 정류장을 골라 내려간다.\n" +
              "해금: 업그레이드가 아니라 '지하에서 엘리베이터를 직접 찾아간 적 있음'.\n" +
              "→ 아래 '엘리베이터 발견 필요'를 켜고 unlockNodeId는 비워둔다." },
            { WorldInteractableKind.Workbench,
              "장비·유물 강화 화면과 업그레이드(연구 트리)를 연다.\n" +
              "해금: 튜토리얼 완료. → 아래 '튜토리얼 완료 필요'를 켠다." },
            { WorldInteractableKind.SurfaceExit,
              "지하에서 지상으로 나가는 통로(빛기둥 아래). 확인창 → 정산 → 지상 씬 전환.\n" +
              "씬에 ExploreExitController가 하나 있어야 한다. 설정할 것 없음.\n기존: SurfaceExitBeacon" },
        };

    private static readonly string[] CommonFields =
    {
        "promptKeyOverride", "promptTextOverride",
        "priorityOverride", "selfHandleInput", "interactKey",
        "spriteRenderer", "tintOnNearby", "idleColor", "nearbyColor",

        // 해금 게이트 — 여기 빠져 있으면 인스펙터에 아예 안 보인다.
        // 실제로 그래서 모든 씬의 unlockNodeId가 빈 채였고, 단말기가 안 잠겼다.
        "unlockNodeId", "requireElevatorDiscovered", "requireTutorialCompleted", "lockedColor",

        "onInteract",

        // 피드백(느낌표·문구 라벨·강조) — WorldInteractable.Feedback.cs
        "showIndicator", "indicatorAlwaysVisible",
        "indicatorSprite", "indicatorFill", "indicatorOutline",
        "indicatorScale", "indicatorOffset", "indicatorSortingOffset",
        "showPromptLabel", "promptLabelOnInteractOnly", "promptLabelFlashDuration",
        "labelFontSize", "labelScale", "labelOffset",
        "labelBackdrop", "labelBackdropSprite", "labelBackdropSpriteColor",
        "labelBackdropPixelsPerUnitMultiplier", "labelBackdropPadding",
        "labelSortingOffset",
        "promptSprite", "promptSpriteAlt", "promptSpriteFrameInterval",
        "promptSpriteBlocked", "promptSpriteScale", "promptSpriteOffset",
        "highlightScaleOnNearby", "hoverScaleMultiplier", "hoverScaleSpeed", "outlineObject",
        "feedbackRadius", "feedbackTriggerGrace", "hideFeedbackWhileUIOpen",
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty kindProp = serializedObject.FindProperty("kind");
        EditorGUILayout.PropertyField(kindProp);

        var kind = (WorldInteractableKind)kindProp.enumValueIndex;

        if (!kindProp.hasMultipleDifferentValues && KindHelp.TryGetValue(kind, out string help))
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox(help, MessageType.Info);
        }

        // 종류별 항목 (필드에 붙은 [Header]가 제목을 대신한다)
        if (!kindProp.hasMultipleDifferentValues && KindFields.TryGetValue(kind, out string[] fields))
        {
            for (int i = 0; i < fields.Length; i++)
                DrawField(fields[i]);
        }

        // 공통 항목
        for (int i = 0; i < CommonFields.Length; i++)
            DrawField(CommonFields[i]);

        if (!kindProp.hasMultipleDifferentValues)
            DrawSetupWarnings(kind);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawField(string name)
    {
        SerializedProperty prop = serializedObject.FindProperty(name);
        if (prop != null) EditorGUILayout.PropertyField(prop, true);
    }

    /// <summary>
    /// 해금 게이트가 하나도 안 켜진 시설에 안내를 띄운다.
    /// 잠겨야 할 시설이 안 잠긴 것은 플레이해도 티가 안 난다(그냥 열려 있을 뿐이라
    /// 로그도 경고도 없다) — 실제로 전 씬의 unlockNodeId가 빈 채로 방치됐던 이유다.
    /// </summary>
    private void WarnIfNoGate(string hint)
    {
        bool hasGate =
            !string.IsNullOrEmpty(serializedObject.FindProperty("unlockNodeId").stringValue) ||
            serializedObject.FindProperty("requireElevatorDiscovered").boolValue ||
            serializedObject.FindProperty("requireTutorialCompleted").boolValue;

        if (!hasGate)
            EditorGUILayout.HelpBox("해금 조건이 비어 있어 처음부터 사용할 수 있습니다.\n" + hint,
                                    MessageType.Info);
    }

    /// <summary>플레이 눌러보기 전에 걸러낼 수 있는 세팅 실수들.</summary>
    private void DrawSetupWarnings(WorldInteractableKind kind)
    {
        var self = (WorldInteractable)target;

        EditorGUILayout.Space(4);

        if (self.GetComponent<Collider2D>() == null)
        {
            EditorGUILayout.HelpBox(
                "Collider2D가 없습니다. 플레이어 콜라이더와 겹쳐야 탐지되므로 Collider2D(Is Trigger 권장)를 추가하세요.",
                MessageType.Warning);
        }

        switch (kind)
        {
            case WorldInteractableKind.ChunkEntrance:
                if (serializedObject.FindProperty("chunkPrefab").objectReferenceValue == null)
                    EditorGUILayout.HelpBox("청크 프리팹이 비어 있어 들어갈 수 없습니다.", MessageType.Warning);
                break;

            case WorldInteractableKind.TunnelEntrance:
                if (string.IsNullOrEmpty(serializedObject.FindProperty("sceneToLoad").stringValue))
                    EditorGUILayout.HelpBox("이동할 씬 이름이 비어 있습니다.", MessageType.Warning);
                break;

            case WorldInteractableKind.MarketTerminal:
                if (string.IsNullOrEmpty(serializedObject.FindProperty("marketSceneName").stringValue))
                    EditorGUILayout.HelpBox("마켓 씬 이름이 비어 있습니다.", MessageType.Warning);
                WarnIfNoGate("단말기는 업그레이드 노드 Facility_Computer_T0로 열립니다.");
                break;

            case WorldInteractableKind.SubQuestBoard:
                WarnIfNoGate("게시판은 업그레이드 노드 Facility_Board_T0로 열립니다.");
                break;

            case WorldInteractableKind.ElevatorEntrance:
                WarnIfNoGate("지상 엘리베이터 입구는 '엘리베이터 발견 필요'로 열립니다(노드 아님).");
                break;

            case WorldInteractableKind.Workbench:
                WarnIfNoGate("작업대는 '튜토리얼 완료 필요'로 열립니다(노드 아님).");
                break;
        }

        // 같은 오브젝트에 옛 컴포넌트가 남아 있으면 E가 두 번 처리된다.
        var legacy = FindLegacyComponent(self);
        if (legacy != null)
        {
            EditorGUILayout.HelpBox(
                $"같은 오브젝트에 기존 컴포넌트 '{legacy.GetType().Name}'가 남아 있습니다. " +
                "둘 다 켜져 있으면 상호작용이 두 번 실행될 수 있으니, 확인 후 기존 컴포넌트를 비활성화하세요.",
                MessageType.Warning);
        }
    }

    /// <summary>이 오브젝트에 남아 있는 구 상호작용 컴포넌트(있으면).</summary>
    private static Component FindLegacyComponent(WorldInteractable self)
    {
        Component c;
        if ((c = self.GetComponent<BedInteractable>()) != null) return c;
        if ((c = self.GetComponent<SceneTransitionTrigger>()) != null) return c;
        if ((c = self.GetComponent<DungeonEntranceInteractable>()) != null) return c;
        if ((c = self.GetComponent<DungeonDoorChunk>()) != null) return c;
        if ((c = self.GetComponent<DungeonExitInteractable>()) != null) return c;
        if ((c = self.GetComponent<SurfaceExitBeacon>()) != null) return c;
        return null;
    }
}
