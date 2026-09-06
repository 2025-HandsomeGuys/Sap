using UnityEngine;
using TMPro;

/// <summary>
/// SRP: 플레이어 주변의 IInteractable 오브젝트를 탐지하고 상호작용 명령만 수행한다.
/// OCP: IInteractable을 구현한 어떤 오브젝트와도 동작한다.
///
/// 키는 <see cref="InteractionKeys.Interact"/>(F) 하나다. 대상이 여러 가지를 할 수 있으면
/// (침대의 수면/낮잠, NPC의 대화·상점·강화) 오브젝트 위에 목록이 뜨고 <b>마우스 휠</b>로 고른다 —
/// 목록을 그리는 쪽과 실행하는 쪽이 같은 인덱스를 보도록 선택 상태는 대상 오브젝트가 갖는다
/// (<see cref="WorldInteractable.SelectedOption"/>).
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [Header("의존성")]
    public InventoryUI inventoryUI;

    [Header("상호작용 설정")]
    public float interactionRadius = 2f;
    public TextMeshProUGUI interactionPromptText;

    private float _checkTimer;
    private const float CHECK_INTERVAL = 0.2f;
    private readonly Collider2D[] _colliderCache = new Collider2D[16];

    private float _holdInteractTimer;
    private const float HOLD_INTERACT_INTERVAL = 0.25f;

    private PlayerInputHandler _inputHandler;
    private IInteractable _closestTarget;

    // 현재 타깃의 안내 문구 제공자. 타깃이 바뀔 때만 다시 만든다(매 프레임 GetComponents 회피).
    private InteractionPromptSource _targetPrompt;
    private string _appliedPromptText;
    private bool _languageSubscribed;

    private void Start()
    {
        _inputHandler = GetComponent<PlayerInputHandler>();
        if (_inputHandler != null)
            _inputHandler.OnInteractPressed += TryInteract;
        else
            Debug.LogWarning("[PlayerInteractor] PlayerInputHandler가 없습니다. 같은 GameObject에 추가해주세요.");

        if (inventoryUI == null)
            inventoryUI = FindFirstObjectByType<InventoryUI>();

        if (interactionPromptText != null)
        {
            interactionPromptText.gameObject.SetActive(false);
            interactionPromptText.raycastTarget = false; // [추가] UI 텍스트가 클릭을 가로막지 않도록 함
        }

        TrySubscribeLanguage();
    }

    private void OnDestroy()
    {
        if (_inputHandler != null)
            _inputHandler.OnInteractPressed -= TryInteract;

        if (_languageSubscribed && LanguageManager.Instance != null)
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
    }

    private void Update()
    {
        TrySubscribeLanguage(); // LanguageManager가 나중에 살아나는 씬 대비

        if (IsAnyUIOpen())
        {
            SetTarget(null);
            _holdInteractTimer = 0f;
            UpdatePrompt();
            return;
        }

        if (_inputHandler == null && InteractionKeys.InteractPressed)
            TryInteract();

        HandleOptionScroll();

        // 상호작용 키 꾹 누르기: PickupableItem(광물)에 한해 연속 줍기
        if (InteractionKeys.InteractHeld)
        {
            if (InteractionKeys.InteractPressed)
                _holdInteractTimer = 0f;

            if (_closestTarget is PickupableItem)
            {
                _holdInteractTimer += Time.deltaTime;
                if (_holdInteractTimer >= HOLD_INTERACT_INTERVAL)
                {
                    _holdInteractTimer = 0f;
                    TryInteract();
                }
            }
        }
        else
        {
            _holdInteractTimer = 0f;
        }

        _checkTimer += Time.deltaTime;
        if (_checkTimer >= CHECK_INTERVAL)
        {
            _checkTimer = 0f;
            SetTarget(FindClosestInteractable());
        }

        // 시간대 등 조건이 바뀌면 문구·색이 즉시 따라오도록 매 프레임 갱신한다.
        // (실제 TMP 반영은 문자열/색이 달라졌을 때만 → 재배치 비용 없음)
        UpdatePrompt();
    }

    /// <summary>휠로 대상의 선택지를 옮긴다(선택지가 둘 이상일 때만).
    /// 휠 위 = 위 항목. 목록 UI가 같은 인덱스를 읽으므로 보이는 대로 바뀐다.</summary>
    private void HandleOptionScroll()
    {
        if (!(_closestTarget is WorldInteractable target)) return;
        if (target.OptionCount <= 1) return;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Approximately(scroll, 0f)) return;

        target.CycleOption(scroll > 0f ? -1 : +1);
    }

    private void TryInteract()
    {
        if (IsAnyUIOpen()) return;
        if (_closestTarget == null)
        {
            Debug.Log($"[PlayerInteractor] {InteractionKeys.Interact} 키 눌림 — 근처에 IInteractable 없음.");
            return;
        }

        _closestTarget.Interact(gameObject);

        // 상호작용 후 즉시 재탐색 (아이템이 사라졌을 수 있음)
        SetTarget(FindClosestInteractable());
    }

    /// <summary>
    /// 타깃을 교체하면서 IHighlightable 하이라이트를 켜고 끈다.
    /// </summary>
    private void SetTarget(IInteractable newTarget)
    {
        if (_closestTarget == newTarget) return;

        // 이전 타깃 하이라이트 OFF
        (_closestTarget as IHighlightable)?.SetHighlighted(false);

        _closestTarget = newTarget;

        // 새 타깃 하이라이트 ON
        (_closestTarget as IHighlightable)?.SetHighlighted(true);

        // 안내 문구 제공자 캐시 교체 (IInteractionPrompt → IInteractable 순으로 해석된다)
        var comp = _closestTarget as Component;
        _targetPrompt = comp != null ? new InteractionPromptSource(comp.gameObject) : null;

        UpdatePrompt();
    }

    private IInteractable FindClosestInteractable()
    {
        IInteractable best = null;
        int bestPriority = int.MinValue;
        float bestDistSqr = float.MaxValue;
        int bestValue = 0;
        bool bestHasValue = false;

        ContactFilter2D filter = ContactFilter2D.noFilter;
        Collider2D myCol = GetComponent<Collider2D>();
        if (myCol == null) return null;

        int hitCount = myCol.Overlap(filter, _colliderCache);

        for (int i = 0; i < hitCount; i++)
        {
            // Collider와 스크립트가 분리된 경우(부모-자식)를 위해 InParent까지 탐색
            IInteractable interactable = _colliderCache[i].GetComponentInParent<IInteractable>();
            if (interactable == null) continue;
            if (!interactable.CanInteract) continue; // 아직 땅에 박힌 광물 등 — 후보에서 제외

            // 청크 입구(IMapEntrance)는 '상호작용 가능한 위치'에 실제로 도달한 이 시점에 지도 마커로 발견 처리.
            // IMapEntranceToggle을 함께 구현했다면(통합 컴포넌트) 지금 입구인지 물어본 뒤 등록한다.
            if (interactable is IMapEntrance && interactable is Component entranceComp)
            {
                var toggle = interactable as IMapEntranceToggle;
                if (toggle == null || toggle.IsMapEntranceNow)
                    MapMarkerRegistry.Discover(entranceComp.transform.position, MapMarkerKind.ChunkEntrance);
            }

            // 엘리베이터(IMapElevator)도 같은 규칙 — 청크 로드가 아니라 '상호작용 가능 위치'에 도달했을 때만
            // 지도에 남는다. 층 이동으로 순간이동하면 목적지 엘리베이터 위에 착지하므로, 이용한 층도 이 경로로 발견된다.
            if (interactable is IMapElevator && interactable is Component elevatorComp)
            {
                var elevToggle = interactable as IMapElevatorToggle;
                if (elevToggle == null || elevToggle.IsMapElevatorNow)
                {
                    MapMarkerRegistry.Discover(elevatorComp.transform.position, MapMarkerKind.Elevator);

                    // 정류장 해금도 같은 시점이다 — "직접 가본 층만 이동 가능"(ElevatorStopUnlockStore).
                    // ElevatorController가 자체 거리 체크로 하면 기준이 어긋난다(NotifyReached 주석 참고).
                    // IElevatorStop으로 받는 이유: 엘리베이터 구현이 두 갈래다
                    // (구 ElevatorController / 통합 WorldInteractable). 한쪽 타입만 보면 조용히 안 걸린다.
                    if (interactable is IElevatorStop elevatorStop)
                        elevatorStop.NotifyReached();
                }
            }

            int priority = interactable.InteractionPriority;
            float distSqr = (_colliderCache[i].transform.position - transform.position).sqrMagnitude;

            // 광물은 "비싼 것부터" 주워지도록 정렬값을 받아온다(가격표가 없으면 false → 거리 기준).
            int value = 0;
            bool hasValue = interactable is PickupableItem pickup && pickup.TryGetPickupValue(out value);

            // 우선순위가 높으면 무조건 선택.
            // 같은 우선순위일 때: 둘 다 값이 매겨진 광물이면 비싼 것 → 같은 값이면 가까운 것,
            // 그 외(아이템 드랍·NPC 등 섞인 경우)는 기존대로 거리만 본다.
            bool isBetter;
            if (best == null || priority > bestPriority)
                isBetter = true;
            else if (priority < bestPriority)
                isBetter = false;
            else if (hasValue && bestHasValue)
                isBetter = value > bestValue || (value == bestValue && distSqr < bestDistSqr);
            else
                isBetter = distSqr < bestDistSqr;

            if (isBetter)
            {
                bestPriority = priority;
                bestDistSqr = distSqr;
                bestValue = value;
                bestHasValue = hasValue;
                best = interactable;
            }
        }

        return best;
    }

    /// <summary>
    /// 화면 HUD 안내 문구 갱신. 문구는 Localization 키로 해석하고,
    /// 상호작용 가능 여부에 따라 색을 바꾼다(오브젝트 위 InteractionPromptLabel과 같은 규칙).
    /// </summary>
    private void UpdatePrompt()
    {
        if (interactionPromptText == null) return;

        InteractionPromptInfo info = default;

        // 선택지가 여럿인 대상은 '지금 고른 것'을 HUD에도 그대로 보여준다
        // (오브젝트 위 목록과 화면 하단 문구가 다른 것을 가리키면 안 된다).
        bool hasPrompt;
        if (_closestTarget is WorldInteractable multi && multi.OptionCount > 0)
        {
            info = multi.GetOptionPrompt(multi.SelectedOption);
            hasPrompt = info.HasText;
        }
        else
        {
            hasPrompt = _targetPrompt != null && _targetPrompt.TryGet(out info);
        }
        if (!hasPrompt)
        {
            if (interactionPromptText.gameObject.activeSelf)
                interactionPromptText.gameObject.SetActive(false);
            _appliedPromptText = null;
            return;
        }

        if (!interactionPromptText.gameObject.activeSelf)
            interactionPromptText.gameObject.SetActive(true);

        string resolved = InteractionPrompts.Resolve(info);
        if (resolved != _appliedPromptText)
        {
            _appliedPromptText = resolved;
            interactionPromptText.text = resolved;
        }

        Color color = InteractionPrompts.ColorOf(info.Available);
        if (interactionPromptText.color != color)
            interactionPromptText.color = color;
    }

    private void TrySubscribeLanguage()
    {
        if (_languageSubscribed || LanguageManager.Instance == null) return;
        LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
        _languageSubscribed = true;
    }

    private void OnLanguageChanged(LanguageType language)
    {
        _appliedPromptText = null; // 다음 UpdatePrompt에서 새 언어로 다시 채운다

        if (interactionPromptText == null) return;
        TMP_FontAsset font = LanguageManager.Instance != null ? LanguageManager.Instance.GetCurrentFont() : null;
        if (font != null) interactionPromptText.font = font;
    }

    private bool IsAnyUIOpen()
    {
        return UIStateManager.Instance != null &&
               UIStateManager.Instance.CurrentState != UIState.None;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}
