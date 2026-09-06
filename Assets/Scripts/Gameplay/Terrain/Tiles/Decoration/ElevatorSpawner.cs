// @tags: decoration, spawn, elevator, chunk, generation
using UnityEngine;

public static class ElevatorSpawner
{
    /// <summary>
    /// 엘리베이터가 청크 안에서 배치되는 로컬 위치(Unity 단위, 청크 좌하단 기준).
    /// <b>스폰 위치의 단일 원천</b> — 여기만 바꾸면 지상→지하 착지(PlayerSpawner)와
    /// 지하 층 이동 착지 추정(ElevatorManager.CalculateElevatorPosition)이 함께 따라온다.
    /// (5,5)는 청크 중앙(청크 1칸 = 10유닛). 값은 X(0~10)·Y(0~10) 범위 안이어야 한다.
    /// </summary>
    public static readonly Vector3 ChunkLocalPosition = new Vector3(5f, 5f, 0f);

    // [신규] 엘리베이터 생성 (2x2 Grid System)
    // Returns: Rect? representing the occupied quadrant (for rock generation exclusion)
    public static Rect? GenerateElevator(TerrainChunk chunk, int xChunk, int yChunk, int layerIndex, int _, bool skipPixelClear)
    {
        // Early validation
        if (ElevatorManager.Instance == null || ElevatorManager.Instance.elevatorPrefab == null)
            return null;

        // [FIX] Ensure jobs are done before clearing pixels
        if (!skipPixelClear) chunk.EnsureJobsCompleted();

        // 1. 청크 로컬 고정 위치 (단일 원천 상수 — ChunkLocalPosition)
        Vector3 elevatorLocalPos = ChunkLocalPosition;

        // 5. Instantiate Elevator at Quadrant Center
        GameObject elevatorObj = UnityEngine.Object.Instantiate(
            ElevatorManager.Instance.elevatorPrefab,
            chunk.transform
        );
        elevatorObj.transform.localPosition = elevatorLocalPos;

        elevatorObj.name = $"Elevator_X{xChunk}_Y{yChunk}_L{layerIndex}";

        // 지하 정렬 대역에 편입 — 플레이어(-49~-30) 뒤, 청크 배경(-100)·특수청크 배경(-99) 앞.
        // 프리팹 기본값(Default/order 1)은 지형(0)보다 앞이라 플레이어를 가린다.
        // 승강로 픽셀은 아래에서 비워지므로 지형(0) 뒤여도 배경 위에 그대로 보인다.
        ApplySortingOrder(elevatorObj);

        // 6. Setup Elevator Controller
        // GetComponentInChildren인 이유: 프리팹(ElevatorPrefab)의 상호작용 컴포넌트는
        // 루트가 아니라 자식 오브젝트("Elevator")에 붙어 있다. GetComponent로 찾으면 null이 되어
        // 좌표·층 주입과 ElevatorManager 등록이 통째로 조용히 건너뛰어진다.
        ElevatorController controller = elevatorObj.GetComponentInChildren<ElevatorController>(true);
        if (controller != null)
        {
            controller.xChunkPosition = xChunk;
            controller.yChunkPosition = yChunk;
            controller.layerIndex = layerIndex;
        }

        // 실제 프리팹(ElevatorPrefab)에는 ElevatorController가 없고 통합 컴포넌트만 붙어 있다.
        // 여기서 주입하지 않으면 모든 엘리베이터가 인스펙터 기본값(x=0, layer=0)으로 남아
        // '현재 층' 판정과 정류장 해금이 전부 층 0으로 뭉개진다.
        var interactable = elevatorObj.GetComponentInChildren<WorldInteractable>(true);
        if (interactable != null)
            interactable.SetElevatorPlacement(xChunk, yChunk, layerIndex);

        // 둘 다 없으면 이 엘리베이터는 좌표를 모르는 채로 선다 — 현재 층은 0으로 뜨고,
        // 매니저 등록도 해금도 안 된다. 조용히 넘어가면 원인 찾기가 아주 오래 걸린다.
        if (controller == null && interactable == null)
            Debug.LogError($"[ElevatorSpawner] {elevatorObj.name}에 ElevatorController도 WorldInteractable도 없다 — " +
                           $"정류장 좌표(X:{xChunk}, Y:{yChunk}, Layer:{layerIndex})를 주입할 곳이 없다.");

        // 7. Clear Terrain Area at Elevator Position (직접 픽셀 클리어 — TerrainCarver.ClearArea는 비활성화됨)
        if (!skipPixelClear)
        {
            int centerX_pixels = (int)(elevatorLocalPos.x * chunk.pixelsPerUnit);
            int centerY_pixels = (int)(elevatorLocalPos.y * chunk.pixelsPerUnit);
            int clearRadius    = (int)(chunk.width * 0.15f);

            var data   = chunk.GetData();
            Color32 air = new Color32(0, 0, 0, 0);

            int minX = Mathf.Max(0, centerX_pixels - clearRadius);
            int maxX = Mathf.Min(chunk.width,  centerX_pixels + clearRadius);
            int minY = Mathf.Max(0, centerY_pixels - clearRadius);
            int maxY = Mathf.Min(chunk.height, centerY_pixels + clearRadius);

            for (int y = minY; y < maxY; y++)
            {
                int rowOffset = y * chunk.width;
                for (int x = minX; x < maxX; x++)
                {
                    int idx = rowOffset + x;
                    data.BasePixels[idx] = air;
                    data.PixelInfo[idx]  = 0;
                }
            }

            data.MarkRenderDirty();
        }

        // 8. Return occupied rect for Rock Exclusion (청크 전체 중앙 기준 고정 영역)
        return new Rect(
            elevatorLocalPos.x * chunk.pixelsPerUnit - chunk.width * 0.25f,
            elevatorLocalPos.y * chunk.pixelsPerUnit - chunk.height * 0.25f,
            chunk.width * 0.5f,
            chunk.height * 0.5f
        );
    }

    /// <summary>
    /// 지하 청크에 배치되는 엘리베이터 스프라이트의 정렬 대역.
    /// 플레이어(-49~-30)보다 뒤, 청크 배경(-100)·특수청크 배경(-99)보다 앞.
    /// 대역 규칙은 <c>PlayerSortingController</c>·<c>Assets/Docs/player-terrain-sorting.md</c> 참고.
    /// </summary>
    public const int SortingOrder = -60;

    /// <summary>정렬 대역의 위쪽 끝. 플레이어(-49~-30) 바로 아래까지만 쓴다.</summary>
    private const int SortingOrderMax = -50;

    // 자식까지 훑는 이유: 프리팹이 나중에 문·표시등 등으로 쪼개져도 대역이 함께 따라오게 하려는 것.
    // 플레이어와 order를 비교하려면 같은 정렬 레이어(Default)여야 한다 — 정렬 레이어가 order보다 항상 우선.
    //
    // [중요] 자식을 전부 같은 order로 눌러버리면 안 된다. 프리팹은 케이블(ElevatorBack) < 하얀 테두리
    // (ElevatorWhite) < 본체(Elevator)를 Objects 레이어 order 100/104/105로 정해 뒀는데, 이걸 한 값으로
    // 뭉개면 같은 레이어·같은 order·같은 z가 되어 그리는 순서가 정해지지 않는다(렌더러 등록 순서에 따라
    // 뒤집힌다). 지하 엘리베이터만 케이블이 본체를 덮던 원인이 이것이다 — 지상 엘리베이터는 씬에 직접
    // 배치돼 이 함수를 타지 않아서 멀쩡했다.
    // → 프리팹이 authoring한 앞뒤 관계를 rank로 뽑아 -60부터 1씩 쌓아 올려 대역만 옮긴다.
    private static void ApplySortingOrder(GameObject elevatorObj)
    {
        var renderers = elevatorObj.GetComponentsInChildren<SpriteRenderer>(true);
        if (renderers.Length == 0) return;

        int defaultLayerId = SortingLayer.NameToID("Default");

        // 프리팹이 정한 앞뒤 = (정렬 레이어 값, order) 오름차순. 레이어가 order보다 우선한다.
        System.Array.Sort(renderers, CompareAuthoredDepth);

        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].sortingLayerID = defaultLayerId;
            renderers[i].sortingOrder   = Mathf.Min(SortingOrder + i, SortingOrderMax);
        }
    }

    /// <summary>프리팹이 authoring해 둔 앞뒤 순서. 뒤에 그려지는 것이 작다.</summary>
    private static int CompareAuthoredDepth(SpriteRenderer a, SpriteRenderer b)
    {
        int byLayer = SortingLayer.GetLayerValueFromID(a.sortingLayerID)
                      .CompareTo(SortingLayer.GetLayerValueFromID(b.sortingLayerID));
        return byLayer != 0 ? byLayer : a.sortingOrder.CompareTo(b.sortingOrder);
    }
}
