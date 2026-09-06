// @tags: interaction, behaviour, strategy, base, factory, unified
using UnityEngine;

/// <summary>
/// <see cref="WorldInteractable"/>이 종류별로 갈아끼우는 동작 한 개(전략 패턴).
/// MonoBehaviour가 아닌 순수 C# 객체라, 종류를 바꿔도 컴포넌트를 붙였다 뗐다 할 필요가 없다.
///
/// 구현할 것은 보통 둘뿐이다 — 무슨 문구를 띄울지(<see cref="GetPrompt"/>),
/// E를 눌렀을 때 뭘 할지(<see cref="Interact"/>). 나머지는 필요할 때만 override 한다.
/// </summary>
public abstract class InteractionBehaviour
{
    /// <summary>나를 들고 있는 컴포넌트. 인스펙터 설정·Transform·코루틴은 전부 여기서 얻는다.</summary>
    protected WorldInteractable Owner { get; private set; }

    protected Transform Transform => Owner.transform;

    public void Bind(WorldInteractable owner)
    {
        Owner = owner;
        OnBind();
    }

    /// <summary>Owner가 연결된 직후. 참조 캐시 등에 쓴다(씬 오브젝트 탐색은 OnStart 권장).</summary>
    protected virtual void OnBind() { }

    /// <summary>Owner의 Start 시점. 씬의 다른 매니저가 깨어난 뒤라 참조를 찾기 안전하다.</summary>
    public virtual void OnStart() { }

    /// <summary>Owner의 Update 시점. 대부분의 동작은 아무것도 하지 않는다.</summary>
    public virtual void Tick() { }

    /// <summary>Owner가 꺼지거나 파괴될 때. 이벤트 구독·timeScale 같은 전역 상태를 되돌린다.</summary>
    public virtual void OnDisabled() { }

    /// <summary>겹쳤을 때의 선택 우선순위 기본값. 엘리베이터만 10, 나머지는 5.</summary>
    public virtual int DefaultPriority => 5;

    /// <summary>지금 상호작용이 가능한가. false면 느낌표가 뜨지 않고 문구가 붉게 표시된다.</summary>
    public virtual bool IsAvailable => true;

    /// <summary>
    /// 지금 '실제로 할 일'이 있는가 — 느낌표를 띄울지 판단하는 기준.
    /// (예: 게시판에 수락 가능한 서브퀘스트, 침대에서 잘 수 있는 저녁, 오늘 코인 판이 남음, 상점에 팔 광물)
    /// 기본은 <see cref="IsAvailable"/> — 할 일 개념이 없는 이동용 오브젝트(엘리베이터·청크문 등)는
    /// 상호작용 가능 = 할 일 있음으로 취급해 기존 동작을 유지한다.
    /// 매 프레임 불릴 수 있으니 무거운 조회는 캐시할 것.
    /// </summary>
    public virtual bool HasPendingTask => IsAvailable;

    /// <summary>스프라이트 색을 상태로 강제해야 할 때(예: 탐험 완료 청크문의 회색). 보통 null.</summary>
    public virtual Color? OverrideTint() => null;

    /// <summary>
    /// 느낌표를 <b>거리 제한 없이 상시</b>(할 일 있을 때) 띄우고 싶은가. 기본은 근접 시에만.
    /// 인스펙터의 <c>indicatorAlwaysVisible</c>와 OR로 합쳐진다 — 종류별로 멀리서도 눈에 띄게
    /// 해야 하는 오브젝트(침대처럼)가 프리팹 설정 없이 코드로 켠다.
    /// </summary>
    public virtual bool IndicatorAlwaysVisible => false;

    /// <summary>
    /// 문구 라벨을 <b>근접 시 항상</b> 띄우도록 코드로 강제한다(기본 false).
    /// 켜면 프리팹의 <c>showPromptLabel</c>/<c>promptLabelOnInteractOnly</c> 인스펙터 설정과 무관하게
    /// 라벨이 만들어지고 근접하면 문구가 뜬다 — 침대처럼 "다가가면 무엇을 할지 보여야 하는" 종류용.
    /// </summary>
    public virtual bool ForcePromptLabelOnApproach => false;

    /// <summary>
    /// 근접하면 느낌표를 <b>감춘다</b>(기본 false). <see cref="IndicatorAlwaysVisible"/>와 함께 쓰면
    /// "멀리선 느낌표로 유도 → 가까이 가면 느낌표 대신 문구 라벨" 패턴이 된다(침대용).
    /// </summary>
    public virtual bool IndicatorHidesWhenNear => false;

    /// <summary>근접 시 띄울 문구. 매 프레임 불리므로 할당 없이 가볍게 구현할 것.</summary>
    public abstract InteractionPromptInfo GetPrompt();

    /// <summary>상호작용 키를 눌렀을 때의 실제 동작.</summary>
    public abstract void Interact(GameObject interactor);

    // ─────────────────────── 여러 선택지 (근접 목록) ───────────────────────
    // 근접하면 이 오브젝트로 '지금 할 수 있는 일'이 전부 목록으로 뜨고, 휠로 고른 뒤 F로 실행한다.
    // 기본은 선택지 1개 = 기존 GetPrompt/Interact 그대로라, 대부분의 동작은 아무것도 안 고쳐도 된다.
    // 여러 개인 종류(침대의 수면/낮잠, NPC의 대화/상점/강화)만 아래 셋을 override 한다.
    //
    // ⚠ OptionCount는 매 프레임 불린다 — 무거운 조회는 캐시할 것.
    // ⚠ 개수와 인덱스 순서는 같은 프레임 안에서 일관돼야 한다(목록을 그리고 나서 실행할 때
    //    인덱스가 가리키는 항목이 달라지면 엉뚱한 게 실행된다).

    /// <summary>지금 띄울 선택지 개수. 0이면 근접해도 아무것도 뜨지 않는다.</summary>
    public virtual int OptionCount => GetPrompt().HasText ? 1 : 0;

    /// <summary>index번째 선택지의 문구.</summary>
    public virtual InteractionPromptInfo GetOption(int index) => GetPrompt();

    /// <summary>index번째 선택지를 실행한다.</summary>
    public virtual void InteractOption(int index, GameObject interactor) => Interact(interactor);

    // ─────────────────────────── 공용 도우미 ───────────────────────────

    /// <summary>다른 UI가 떠 있으면 새 UI를 열지 않는다(기존 모든 상호작용의 공통 규칙).</summary>
    protected static bool IsAnyUIOpen()
        => UIStateManager.Instance != null && UIStateManager.Instance.CurrentState != UIState.None;

    /// <summary>저녁(Afternoon)인가. 침대·지하 진입 판정에 쓴다.</summary>
    protected static bool IsEvening()
        => DayCycleManager.Instance != null &&
           DayCycleManager.Instance.CurrentTime == TimeOfDay.Afternoon;

    // ─────────────────────────── 팩토리 ───────────────────────────

    /// <summary>종류 → 동작. 새 종류를 추가하면 여기에 한 줄만 더하면 된다.</summary>
    public static InteractionBehaviour Create(WorldInteractableKind kind)
    {
        switch (kind)
        {
            case WorldInteractableKind.ChunkEntrance:  return new ChunkEntranceBehaviour();
            case WorldInteractableKind.ChunkExit:      return new ChunkExitBehaviour();
            case WorldInteractableKind.Elevator:       return new ElevatorBehaviour();
            case WorldInteractableKind.TunnelEntrance: return new TunnelEntranceBehaviour();
            case WorldInteractableKind.ElevatorEntrance: return new SurfaceElevatorEntranceBehaviour();
            case WorldInteractableKind.SubQuestBoard:  return new SubQuestBoardBehaviour();
            case WorldInteractableKind.TruckNpc:       return new TruckNpcBehaviour();
            case WorldInteractableKind.Bed:            return new BedBehaviour();
            case WorldInteractableKind.Wardrobe:       return new WardrobeBehaviour();
            case WorldInteractableKind.MarketTerminal: return new MarketTerminalBehaviour();
            case WorldInteractableKind.Workbench:      return new WorkbenchBehaviour();
            case WorldInteractableKind.SurfaceExit:    return new SurfaceExitBehaviour();
            default:
                Debug.LogError($"[WorldInteractable] 처리되지 않은 종류: {kind}");
                return new NullInteractionBehaviour();
        }
    }
}

/// <summary>알 수 없는 종류일 때의 안전한 빈 동작 — 게임을 멈추지 않는다.</summary>
public sealed class NullInteractionBehaviour : InteractionBehaviour
{
    public override InteractionPromptInfo GetPrompt() => InteractionPromptInfo.None;
    public override void Interact(GameObject interactor) { }
}
