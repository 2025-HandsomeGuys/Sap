// @tags: interaction, behaviour, bed, wardrobe, market, npc, board, settlement
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sap.UI.Notification;

/// <summary>
/// 서브퀘스트 게시판 — 게시판 UI를 연다. 기존 <c>SubQuestBoardInteraction</c>과 같다.
/// 코드 생성 오버레이를 쓰는 씬(<c>UIStateManager.useCodeBuiltSubQuestUI</c>)이면
/// 프리팹 참조 없이 <c>UIState.SubQuestBoard</c>로 열고, 아니면 씬의 SubQuestBoardUI를 찾아 연다.
/// </summary>
public sealed class SubQuestBoardBehaviour : InteractionBehaviour
{
    private SubQuestBoardUI _boardUI;

    // 수락 가능한 서브퀘스트가 있는지 — 매 프레임 순회하지 않도록 0.5초 캐시.
    private float _nextQuestCheck;
    private bool _cachedHasQuest;

    private static bool UsesCodeUI()
        => UIStateManager.Instance != null && UIStateManager.Instance.useCodeBuiltSubQuestUI;

    /// <summary>느낌표: 슬롯 여유가 있고 지금 수락 가능한(Available) 서브퀘스트가 하나라도 있을 때.</summary>
    public override bool HasPendingTask
    {
        get
        {
            if (Time.unscaledTime >= _nextQuestCheck)
            {
                _nextQuestCheck = Time.unscaledTime + 0.5f;
                _cachedHasQuest = HasAcceptableSubQuest();
            }
            return _cachedHasQuest;
        }
    }

    private static bool HasAcceptableSubQuest()
    {
        var qm = QuestManager.Instance;
        if (qm == null || qm.subQuests == null) return false;
        if (!qm.CanAcceptSubQuest()) return false; // 슬롯이 가득 차면 더 받을 수 없다

        foreach (var quest in qm.subQuests)
        {
            if (quest == null || quest.questType != QuestType.Sub) continue;
            if (qm.GetQuestStatus(quest.questID) == QuestStatus.Available) return true;
        }
        return false;
    }

    public override void OnStart()
    {
        if (!UsesCodeUI())
        {
            _boardUI = Object.FindFirstObjectByType<SubQuestBoardUI>();
            if (_boardUI == null)
                Debug.LogWarning($"[WorldInteractable/SubQuestBoard] {Owner.name}: SubQuestBoardUI를 찾을 수 없습니다. " +
                                 "UIStateManager의 useCodeBuiltSubQuestUI를 켜거나 프리팹을 배치하세요.");
        }
    }

    // 침대와 같은 근접 피드백 3종 — 멀리선 느낌표(할 일 있을 때)로 유도, 가까이 가면 느낌표 대신 키 아이콘.
    public override bool IndicatorAlwaysVisible => true;
    public override bool IndicatorHidesWhenNear => true;
    public override bool ForcePromptLabelOnApproach => true;

    public override InteractionPromptInfo GetPrompt()
        => InteractionPromptInfo.Ok("interact_board", "게시판 확인");

    public override void Interact(GameObject interactor)
    {
        if (UsesCodeUI())
        {
            UIStateManager.Instance.SetState(UIState.SubQuestBoard, Transform);
            return;
        }

        if (_boardUI == null) _boardUI = Object.FindFirstObjectByType<SubQuestBoardUI>();
        if (_boardUI == null)
        {
            Debug.LogError($"[WorldInteractable/SubQuestBoard] {Owner.name}: 열 게시판 UI가 없습니다.");
            return;
        }

        _boardUI.OpenSubQuestBoard(Transform);
    }
}

/// <summary>
/// 트럭 NPC — 근접하면 <b>할 수 있는 일이 목록으로</b> 뜬다(대화 / 퀘스트 / 상점).
/// 업그레이드(연구 트리)는 여기가 아니라 <see cref="WorkbenchBehaviour"/>(작업대)에서 연다.
/// 휠로 고르고 F로 실행하면 곧장 그 UI가 열린다.
///
/// 무엇을 보여줄지는 <see cref="INpcPopupSource"/>(=이 컴포넌트의 인스펙터 값)가 정한다.
/// 구 머리 위 팝업(<see cref="NpcPopup"/> / <see cref="NpcPopupOverlayUI"/>)은 더 이상 이 경로에서
/// 띄우지 않는다 — 다른 상호작용 오브젝트와 조작을 하나로 맞추기 위해서다.
/// (팝업 클래스 자체는 대화 종료 후 복귀 등 다른 경로가 아직 참조하므로 남겨둔다)
/// </summary>
public sealed class TruckNpcBehaviour : InteractionBehaviour
{
    // 이름은 {0}으로 넘겨 언어별 어순에 맞춘다. 매 프레임 호출되므로 배열을 캐시한다.
    private object[] _promptArgs;

    // 할 일 판정(팔 광물·메인퀘스트) — 창고/퀘스트 조회를 0.5초 캐시.
    private ShopManager _shop;
    private float _nextCheck;
    private bool _cachedPending;

    /// <summary>
    /// 느낌표가 뜨는 경우(하나라도 해당):
    ///  • 상점 NPC이고 가방에 팔 광물이 있음
    ///  • 메인퀘스트 NPC이고 새로 받을(대화) 메인퀘스트가 있거나, 완료(제출) 가능한 메인퀘스트가 있음
    /// 상점도 메인퀘스트도 아닌 순수 대화 NPC는 기존대로 근접 시 항상 표시.
    /// </summary>
    public override bool HasPendingTask
    {
        get
        {
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.5f;
                _cachedPending = ComputePending();
            }
            return _cachedPending;
        }
    }

    private bool ComputePending()
    {
        if (Owner.CanOpenShop && HasMineralsToSell()) return true;
        if (Owner.IsMainQuestNpc && HasMainQuestBusiness()) return true;

        // 상점·메인퀘스트 어느 쪽도 아니면(순수 대화) 근접 시 항상 표시.
        if (!Owner.CanOpenShop && !Owner.IsMainQuestNpc) return base.HasPendingTask;

        return false;
    }

    /// <summary>
    /// 트럭 상점은 <b>창고(WarehouseManager)</b>의 광물을 판다(가방이 아님).
    /// 가격이 있는(팔리는) 광물이 창고에 하나라도 있으면 true.
    /// 가격 DB(ShopManager)를 못 찾으면 광물 보유만으로 판단한다.
    /// </summary>
    private bool HasMineralsToSell()
    {
        var wh = WarehouseManager.Instance;
        if (wh == null || wh.AllSlots == null) return false;

        if (_shop == null) _shop = Object.FindFirstObjectByType<ShopManager>();
        var priceDb = _shop != null ? _shop.priceDatabase : null;

        var slots = wh.AllSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.quantity <= 0) continue;
            if (!(slot.item is MineralSO mineral)) continue;

            // 가격 DB가 있으면 팔리는(가격 > 0) 광물만, 없으면 광물 보유만으로 판단.
            if (priceDb == null || priceDb.GetPrice(mineral.mineralID) > 0) return true;
        }
        return false;
    }

    /// <summary>메인퀘스트 관련 '할 일': 수락 가능(대화) 또는 제출 가능(완료).</summary>
    private bool HasMainQuestBusiness()
    {
        var qm = QuestManager.Instance;
        if (qm == null) return false;

        QuestSO quest = Owner.MainQuest;
        
        if (Owner.IsMainQuestNpc)
        {
            QuestSO activeQuest = qm.GetActiveMainQuest();
            if (activeQuest != null) quest = activeQuest;
        }

        if (quest != null)
        {
            QuestStatus status = qm.GetQuestStatus(quest.questID);
            if (status == QuestStatus.Available) return true;                  // 새로 받을 퀘스트(대화)
            if (status == QuestStatus.Accepted && CanCompleteQuest(qm, quest)) return true; // 제출 가능
        }

        return false;
    }

    private static bool CanCompleteQuest(QuestManager qm, QuestSO quest)
    {
        if (quest == null) return false;
        // 요구 광물이 없으면 즉시 완료 가능. 있으면 창고가 있어야 충족 여부를 확인할 수 있다.
        if (quest.requirements == null || quest.requirements.Count == 0) return true;
        if (WarehouseManager.Instance == null) return false;
        return qm.CheckRequirements(quest);
    }

    // 침대와 같은 근접 피드백 3종 — 멀리선 느낌표(팔 광물·메인퀘스트가 있을 때), 근접하면 키 아이콘.
    public override bool IndicatorAlwaysVisible => true;
    public override bool IndicatorHidesWhenNear => true;
    public override bool ForcePromptLabelOnApproach => true;

    // ── 선택지 ──
    // 예전엔 F 한 번 = 머리 위 팝업(NpcPopup)이었고, 거기서 다시 골라야 했다.
    // 이제는 근접만 해도 할 수 있는 일이 목록으로 뜨고 휠로 골라 F로 바로 실행한다 —
    // 클릭 한 단계가 줄고, 다른 상호작용 오브젝트와 조작이 같아진다.
    // 순서(대화 → 퀘스트 → 상점)는 팝업 버튼 순서를 그대로 따른다.

    private enum NpcAction { Talk, Quest, Shop }

    // 매 프레임 배열을 만들지 않도록 한 번 만들어 재사용한다.
    private readonly NpcAction[] _actionBuffer = new NpcAction[3];
    private int _actionCount;
    private int _actionsFrame = -1;

    /// <summary>지금 가능한 항목을 순서대로 모은다(프레임당 1회).</summary>
    private void RebuildActions()
    {
        if (_actionsFrame == Time.frameCount) return;
        _actionsFrame = Time.frameCount;

        _actionCount = 0;
        if (Owner.Dialogue != null || Owner.MainQuest != null) _actionBuffer[_actionCount++] = NpcAction.Talk;
        if (Owner.MainQuest != null || Owner.IsMainQuestNpc) _actionBuffer[_actionCount++] = NpcAction.Quest;
        if (Owner.CanOpenShop) _actionBuffer[_actionCount++] = NpcAction.Shop;
    }

    public override int OptionCount
    {
        get { RebuildActions(); return _actionCount; }
    }

    public override InteractionPromptInfo GetOption(int index)
    {
        RebuildActions();
        if (index < 0 || index >= _actionCount) return InteractionPromptInfo.None;

        switch (_actionBuffer[index])
        {
            case NpcAction.Talk:
                if (_promptArgs == null || (string)_promptArgs[0] != Owner.NpcName)
                    _promptArgs = new object[] { Owner.NpcName };
                return InteractionPromptInfo.Ok("interact_npc_talk", "{0}와 대화", _promptArgs);
            case NpcAction.Quest:   return InteractionPromptInfo.Ok("interact_npc_quest", "퀘스트");
            default:                return InteractionPromptInfo.Ok("interact_npc_shop", "상점");
        }
    }

    public override void InteractOption(int index, GameObject interactor)
    {
        RebuildActions();
        if (index < 0 || index >= _actionCount) return;

        switch (_actionBuffer[index])
        {
            case NpcAction.Talk:    OpenTalk(); return;
            case NpcAction.Quest:   OpenQuest(); return;
            default:                OpenState(UIState.Shop); return;
        }
    }

    public override InteractionPromptInfo GetPrompt()
        => OptionCount > 0 ? GetOption(0) : InteractionPromptInfo.None;

    public override void Interact(GameObject interactor) => InteractOption(0, interactor);

    private void OpenState(UIState state)
    {
        if (UIStateManager.Instance == null)
        {
            Debug.LogError("[WorldInteractable/TruckNpc] UIStateManager가 없습니다.");
            return;
        }
        UIStateManager.Instance.SetState(state, Transform);
    }

    /// <summary>퀘스트 — '제출 가능' 모드로 연다(구 NpcPopup의 퀘스트 버튼과 같은 규칙).</summary>
    private void OpenQuest()
    {
        if (UIStateManager.Instance != null && UIStateManager.Instance.useCodeBuiltQuestUI)
        {
            QuestOverlayUI.RequestSubmission();
            UIStateManager.Instance.SetState(UIState.Quest, Transform);
            return;
        }

        QuestPanelUI questPanel = Object.FindFirstObjectByType<QuestPanelUI>();
        if (questPanel != null) { questPanel.OpenPanel(true, Transform); return; }

        if (UIStateManager.Instance != null)
        {
            UIStateManager.Instance.SetState(UIState.Quest, Transform);
            var panel = Object.FindFirstObjectByType<QuestPanelUI>();
            if (panel != null) panel.RefreshLog(true);
        }
    }

    /// <summary>대화 — 진행 중인 메인 퀘스트가 있으면 그 대사, 없으면 일반 대화(구 NpcPopup과 동일).</summary>
    private void OpenTalk()
    {
        QuestDialogueUI dialogueUI = Owner.DialogueUI;
        if (dialogueUI == null) dialogueUI = Object.FindFirstObjectByType<QuestDialogueUI>(FindObjectsInactive.Include);
        if (dialogueUI == null)
        {
            Debug.LogError("[WorldInteractable/TruckNpc] QuestDialogueUI를 찾을 수 없습니다!");
            return;
        }

        QuestSO questToTalk = Owner.MainQuest;
        
        if (QuestManager.Instance != null && Owner.IsMainQuestNpc)
        {
            QuestSO activeQuest = QuestManager.Instance.GetActiveMainQuest();
            if (activeQuest != null)
            {
                questToTalk = activeQuest;
            }
        }

        if (questToTalk != null && QuestManager.Instance != null)
        {
            QuestStatus status = QuestManager.Instance.GetQuestStatus(questToTalk.questID);
            if (status == QuestStatus.Available || status == QuestStatus.Accepted)
            {
                dialogueUI.ShowQuestDialogue(questToTalk, Transform);
                return;
            }
        }

        if (Owner.Dialogue != null) dialogueUI.ShowDialogue(Owner.Dialogue, Transform);
        else Debug.LogWarning("[WorldInteractable/TruckNpc] 출력할 대화 데이터가 없습니다.");
    }
}

/// <summary>
/// 침대 — <b>수면</b>(밤에만, 하루 마무리+정산 연출)과 <b>낮잠</b>(횟수 있으면 언제나, 주식 1틱만)을 처리한다.
/// 실제 시퀀스는 <see cref="SleepSequence"/>(수면)·<see cref="NapSequence"/>(낮잠)가 갖고 있고,
/// 낮잠 횟수는 <see cref="NapManager"/>(업그레이드 <see cref="UpgradeEffectType.NapCount"/>)가 관리한다.
/// 둘 다 가능할 때(밤 + 낮잠 횟수 남음)만 <see cref="BedRestChoiceOverlayUI"/> 선택 팝업을 띄운다.
/// 설계: Assets/Docs/nap-system.md
/// (구 <c>BedInteractable</c> MonoBehaviour에도 같은 로직이 있지만, 씬의 침대는 이 전략을 쓴다)
/// </summary>
public sealed class BedBehaviour : InteractionBehaviour
{
    private bool _busy; // 수면/낮잠 시퀀스 진행 중 재진입 방지

    private static bool CanNap => NapManager.Instance != null && NapManager.Instance.CanNap;
    private static bool AnyRestPlaying => SleepSequence.IsPlaying || NapSequence.IsPlaying;

    /// <summary>낮잠(횟수 있으면 언제나) 또는 수면(밤에만) 중 하나라도 가능하면 상호작용 가능.</summary>
    public override bool IsAvailable => !_busy && !AnyRestPlaying && (IsEvening() || CanNap);

    // HasPendingTask는 오버라이드하지 않는다 → 기본값(IsAvailable)을 그대로 쓴다.
    // 이래야 낮에 낮잠이 가능할 때도 근접 시 느낌표·문구가 뜬다(밤 수면과 같은 근접 피드백).
    // 낮잠 횟수를 다 쓰면 낮엔 IsAvailable=false가 되어 느낌표도 자동으로 사라진다.

    /// <summary>느낌표는 멀리서도 상시로(잘 수 있을 때만) — 침대 위치를 한눈에 찾게.</summary>
    public override bool IndicatorAlwaysVisible => true;

    /// <summary>가까이 가면 느낌표는 사라지고 문구 라벨이 대신 뜬다.</summary>
    public override bool IndicatorHidesWhenNear => true;

    /// <summary>근접하면 프리팹 설정과 무관하게 문구("낮잠 자기"·"수면/낮잠")가 뜨게 강제한다.</summary>
    public override bool ForcePromptLabelOnApproach => true;

    // ── 선택지 ──
    // 지금 할 수 있는 것을 그대로 목록으로 보여준다(낮잠 / 하루 마무리). 둘 다 되면 두 줄이 뜨고
    // 휠로 고른 뒤 F로 실행한다 — 예전처럼 별도 선택 팝업(BedRestChoiceOverlayUI)을 띄우지 않는다.
    // 순서는 낮잠 → 수면으로 고정한다(가벼운 것부터. 인덱스가 흔들리면 엉뚱한 게 실행된다).

    public override int OptionCount
    {
        get
        {
            if (_busy || AnyRestPlaying) return 0;
            int n = 0;
            if (CanNap) n++;
            if (IsEvening()) n++;
            return n;
        }
    }

    /// <summary>index → 실제 항목. true면 낮잠, false면 수면.</summary>
    private bool IsNapOption(int index)
    {
        bool canNap = CanNap;
        if (canNap && index == 0) return true;   // 낮잠이 있으면 0번은 항상 낮잠
        return false;                             // 나머지는 수면
    }

    public override InteractionPromptInfo GetOption(int index)
    {
        if (_busy || AnyRestPlaying) return InteractionPromptInfo.None;

        return IsNapOption(index)
            ? InteractionPromptInfo.Ok("interact_bed_nap", "낮잠 자기")
            : InteractionPromptInfo.Ok("interact_bed_sleep", "하루를 마무리 하기");
    }

    public override void InteractOption(int index, GameObject interactor)
    {
        if (_busy || AnyRestPlaying) return;

        if (DayCycleManager.Instance == null)
        {
            Debug.LogError("[WorldInteractable/Bed] DayCycleManager가 씬에 없습니다. 시간을 전환할 수 없습니다.");
            if (NotificationUI.Instance != null)
                NotificationUI.Instance.ShowNotification("시스템 오류: 시간을 전환할 수 없습니다.");
            return;
        }

        if (IsNapOption(index))
        {
            if (CanNap) Owner.StartCoroutine(RunNap());
            else if (NotificationUI.Instance != null)
                NotificationUI.Instance.ShowNotification(CodeUI.L("interact_bed_daytime", "낮에는 잘 수 없습니다."));
            return;
        }

        if (IsEvening()) { Owner.StartCoroutine(RunSleep()); return; }

        if (NotificationUI.Instance != null)
            NotificationUI.Instance.ShowNotification(CodeUI.L("interact_bed_daytime", "낮에는 잘 수 없습니다."));
    }

    /// <summary>HUD 한 줄 표시용(첫 선택지). 목록은 GetOption이 그린다.</summary>
    public override InteractionPromptInfo GetPrompt()
        => OptionCount > 0 ? GetOption(0) : InteractionPromptInfo.None;

    /// <summary>선택지 경로로만 실행한다 — 목록에서 고른 것과 실제 실행이 어긋나지 않게.</summary>
    public override void Interact(GameObject interactor) => InteractOption(0, interactor);

    private IEnumerator RunSleep()
    {
        _busy = true;
        yield return SleepSequence.Run();
        _busy = false;
    }

    private IEnumerator RunNap()
    {
        _busy = true;
        yield return NapSequence.Run();
        _busy = false;
    }
}

/// <summary>
/// 옷장 — 지상의 창고. 코드 생성 창고 오버레이(창고 + 가방 통합 화면)를 연다.
/// 여는 경로는 Tab 키와 같은 <c>UIState.Inventory</c>라, 닫기(ESC/Tab)·상태 동기화가 그대로 따라온다.
/// </summary>
public sealed class WardrobeBehaviour : InteractionBehaviour
{
    public override InteractionPromptInfo GetPrompt()
        => InteractionPromptInfo.Ok("interact_wardrobe", "옷장");

    public override void Interact(GameObject interactor)
    {
        if (UIStateManager.Instance == null)
        {
            Debug.LogError("[WorldInteractable/Wardrobe] UIStateManager가 없습니다.");
            return;
        }

        if (!UIStateManager.Instance.useCodeBuiltWarehouseUI)
        {
            // 창고가 아니라 기존 인벤토리 패널이 열린다. 지상 씬이면 이 체크를 켜는 게 맞다.
            Debug.LogWarning("[WorldInteractable/Wardrobe] UIStateManager의 useCodeBuiltWarehouseUI가 꺼져 있어 " +
                             "창고 대신 인벤토리가 열립니다. 지상 씬이면 체크하세요.");
        }

        UIStateManager.Instance.SetState(UIState.Inventory, Transform);
    }
}

/// <summary>
/// 작업대 — 장비·유물 강화 화면과 <b>업그레이드(연구 트리)</b>를 연다.
/// 근접하면 두 항목이 목록으로 뜨고 휠로 골라 F로 실행한다(트럭 NPC와 같은 조작).
/// 옷장과 같은 방식으로 <c>UIState.Workbench</c>/<c>UIState.Upgrade</c>를 통해 열어
/// 닫기(ESC/Tab)·상태 동기화를 그대로 얻는다.
///
/// 업그레이드는 원래 트럭 NPC(<see cref="TruckNpcBehaviour"/>)에 있었지만,
/// '강화하는 곳'을 작업대 하나로 모으려고 여기로 옮겼다.
/// </summary>
public sealed class WorkbenchBehaviour : InteractionBehaviour
{
    // 침대와 같은 근접 피드백 3종 — 근접하면 느낌표 대신 키 아이콘.
    // ⚠ 작업대는 '할 일 있음' 판정이 따로 없어(HasPendingTask = IsAvailable = 항상 true)
    //    IndicatorAlwaysVisible을 켜면 느낌표가 상시로 떠 있게 된다 → 근접 시에만 유지한다.
    //    (근접하면 IndicatorHidesWhenNear로 숨으므로 실질적으로 키 아이콘만 뜬다)
    public override bool IndicatorHidesWhenNear => true;
    public override bool ForcePromptLabelOnApproach => true;

    public override int OptionCount => 2;

    public override InteractionPromptInfo GetOption(int index)
    {
        switch (index)
        {
            case 0:  return InteractionPromptInfo.Ok("interact_workbench", "작업대 (강화)");
            case 1:  return InteractionPromptInfo.Ok("interact_workbench_upgrade", "업그레이드");
            default: return InteractionPromptInfo.None;
        }
    }

    public override void InteractOption(int index, GameObject interactor)
    {
        if (UIStateManager.Instance == null)
        {
            Debug.LogError("[WorldInteractable/Workbench] UIStateManager가 없습니다.");
            return;
        }
        UIStateManager.Instance.SetState(index == 1 ? UIState.Upgrade : UIState.Workbench, Transform);
    }

    public override InteractionPromptInfo GetPrompt() => GetOption(0);

    public override void Interact(GameObject interactor) => InteractOption(0, interactor);
}

/// <summary>
/// PC(거래소 단말기) — 마켓 씬을 Additive로 얹는다. 기존 <c>MarketTerminalInteractable</c>과 같다.
///
/// 진입 동안 <c>UIState.Market</c>으로 정착지 쪽 입력·핫키를 막는다.
/// 닫기는 마켓 씬 안의 MarketSceneController가 씬을 언로드하며 처리하고,
/// 그 언로드 콜백에서 UIState를 되돌린다.
///
/// <para>게임 시간은 <b>멈추지 않는다</b>. 예전엔 <c>timeScale = 0</c>으로 세웠는데,
/// 점프 중에 단말기를 열면 공중에 그대로 얼어붙었다가 마켓을 닫는 순간 이어서 떨어져 어색했다.
/// 시간을 흘려보내면 마켓을 보는 동안 착지가 끝나 있다.
/// 날짜·주식 틱은 실시간이 아니라 수면/복귀 이벤트로만 넘어가므로 시간이 흘러도 진행되지 않는다.
/// 마켓 UI 자체는 원래부터 unscaled로 동작해 영향이 없다.</para>
/// </summary>
public sealed class MarketTerminalBehaviour : InteractionBehaviour
{
    private bool _open;

    // 코인 하루 판수 상한. 마켓 씬 밖이라 CoinTableSO를 읽을 수 없어 설계 기본값(5)을 쓴다.
    private const int CoinDailyRoundLimit = 5;

    /// <summary>
    /// 느낌표: 오늘 코인 베팅 기회가 남아 있을 때. 마켓 씬이 로드돼야 매니저가 생기므로
    /// PC 앞(정착지)에서는 영속 세이브(PlayerData.coinSave)로 판단한다.
    /// 주식은 일일 제한이 없어(상시 거래) 코인의 '오늘 판 남음'을 기회 기준으로 삼는다.
    /// </summary>
    public override bool HasPendingTask => CoinChanceRemainsToday();

    // 침대와 같은 근접 피드백 3종 — 멀리선 느낌표(오늘 코인 판이 남았을 때), 근접하면 키 아이콘.
    public override bool IndicatorAlwaysVisible => true;
    public override bool IndicatorHidesWhenNear => true;
    public override bool ForcePromptLabelOnApproach => true;

    private static bool CoinChanceRemainsToday()
    {
        var sm = SaveManager.Instance;
        var save = (sm != null && sm.playerData != null) ? sm.playerData.coinSave : null;
        if (save == null || !save.hasData) return true; // 아직 한 판도 안 함 → 기회 있음

        int today = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 0;
        if (save.lockedDay != today) return true;       // 오늘 아직 시작 안 함 → 기회 있음

        return save.roundsToday < CoinDailyRoundLimit;
    }

    public override InteractionPromptInfo GetPrompt()
        => InteractionPromptInfo.Ok("interact_market", "거래소 단말기");

    public override void Interact(GameObject interactor)
    {
        if (_open) return;

        string sceneName = Owner.MarketSceneName;
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning($"[WorldInteractable/Market] {Owner.name}: 마켓 씬 이름이 비어 있습니다.");
            return;
        }

        // 이미 로드돼 있으면 중복 로드 방지.
        Scene existing = SceneManager.GetSceneByName(sceneName);
        if (existing.IsValid() && existing.isLoaded) return;

        SceneManager.sceneUnloaded += OnSceneUnloaded;
        var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (op == null)
        {
            // 빌드 세팅에 씬이 없으면 null이 온다 → 부작용 없이 중단.
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Debug.LogError($"[WorldInteractable/Market] '{sceneName}'을(를) 로드할 수 없습니다. Build Settings를 확인하세요.");
            return;
        }

        _open = true;
        if (UIStateManager.Instance != null)
            UIStateManager.Instance.SetState(UIState.Market, Transform);
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (scene.name != Owner.MarketSceneName) return;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        Restore();
    }

    /// <summary>단말기가 비활성/파괴돼도 UIState·구독이 새지 않도록 방어.</summary>
    public override void OnDisabled()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        if (_open) Restore();
    }

    private void Restore()
    {
        if (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState == UIState.Market)
            UIStateManager.Instance.SetState(UIState.None);

        _open = false;
    }
}
