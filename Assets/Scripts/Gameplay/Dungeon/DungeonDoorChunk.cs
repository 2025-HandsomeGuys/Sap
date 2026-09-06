// @tags: dungeon, door, special-chunk, interactable, scene, entry
using UnityEngine;

/// <summary>
/// 던전 문 1×1 특수청크. 플레이어가 F키로 상호작용하면 던전 씬으로 진입한다.
/// 인스턴스 ID = 자기 청크 좌표 (시드 결정론 → 안정적). DokkaebiCauldron과 동일한
/// InteractableBlockBase + IChunkInitializer 패턴.
/// 진입: 좌표를 DungeonStateStore.CurrentInstance로 세팅 → SaveManager.PrepareDungeonEntry(복귀위치 기록)
///       → SceneLoader.LoadScene(dungeonSceneName). 복귀는 기존 PlayerSpawner가 처리.
/// </summary>
public class DungeonDoorChunk : InteractableBlockBase, IChunkInitializer, IMapEntrance
{
    [Header("Dungeon Door")]
    [Tooltip("이 문이 여는 던전 지오메트리 프리팹 (DungeonEntryPoint 포함)")]
    [SerializeField] private GameObject dungeonPrefab;

    [Tooltip("탐험 완료(재입장 불가) 시 스프라이트 틴트 색")]
    [SerializeField] private Color usedColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    private Vector2Int _coord;
    private bool _initialized;

    public int InitializationOrder => 0;

    public void Initialize(Transform parent)
    {
        var chunk = GetComponentInParent<IChunk>();
        _coord = chunk != null ? chunk.Coord : Vector2Int.zero;
        _initialized = true;
    }

    protected override void Start()
    {
        base.Start();
        // 특수청크 스폰 경로를 안 탄 직접 배치(테스트 등) 폴백 초기화.
        if (!_initialized) Initialize(transform.parent);
    }

    // 탐험 완료 상태를 실시간 반영. 오버레이 방식이라 이탈 후 문이 재스폰되지 않으므로
    // 매 프레임 IsUsed를 조회해 프롬프트·색을 갱신한다.
    public override string InteractionPrompt =>
        DungeonStateStore.IsUsed(_coord) ? "이미 탐험한 던전" : promptText;

    /// <summary>탐험 완료면 "이미 탐험한 던전"을 불가(붉은) 색으로 띄운다.</summary>
    public override InteractionPromptInfo GetInteractionPrompt()
        => DungeonStateStore.IsUsed(_coord)
            ? InteractionPromptInfo.Blocked("interact_dungeon_used", "이미 탐험한 던전")
            : InteractionPromptInfo.Ok(promptKey, promptText);

    protected override void UpdateVisuals()
    {
        if (DungeonStateStore.IsUsed(_coord))
        {
            if (spriteRenderer != null) spriteRenderer.color = usedColor;
            return; // 회색 유지 — 근접 하이라이트로 덮어쓰지 않음
        }
        base.UpdateVisuals();
    }

    protected override void HandleInteraction(GameObject interactor)
    {
        if (DungeonOverlayController.Instance.InDungeon) return; // 이미 던전 안이면 무시
        if (DungeonStateStore.IsUsed(_coord))
        {
            Debug.Log("[DungeonDoor] 이미 탐험한 던전 — 재입장 불가.");
            return;
        }
        if (dungeonPrefab == null)
        {
            Debug.LogWarning("[DungeonDoor] dungeonPrefab이 설정되지 않았습니다.");
            return;
        }

        // 오버레이 진입: 지하씬은 유지, 던전 프리팹을 먼 offset에 생성하고 플레이어를 텔레포트한다.
        // 인스턴스 좌표(_coord)로 던전 rock/보상의 저장 상태를 구분한다.
        DungeonOverlayController.Instance.EnterDungeon(dungeonPrefab, _coord);
    }
}
