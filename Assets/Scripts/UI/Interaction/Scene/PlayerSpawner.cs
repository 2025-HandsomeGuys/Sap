using System.Collections;
using UnityEngine;

/// <summary>
/// 스폰 청크가 로드된 후 플레이어를 정상 물리 상태로 전환한다.
/// 플레이어는 씬에 활성 상태로 배치되어야 하며, Rigidbody2D는 isKinematic=true로 설정되어 있어야 한다.
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private GameObject playerObj;
    [SerializeField] private Vector2    spawnPosition;
    [SerializeField] private Vector2Int spawnChunkCoord;
    [SerializeField] private float      timeout = 10f;

    private void Start() => StartCoroutine(SpawnRoutine());

    private IEnumerator SpawnRoutine()
    {
        if (playerObj == null)
        {
            Debug.LogError("[PlayerSpawner] playerObj가 비어 있습니다.");
            yield break;
        }

        // [던전 인스턴스 시스템] 던전에서 복귀하는 경우 저장된 좌표를 사용
        Vector2 targetSpawnPosition = spawnPosition;
        Vector2Int targetChunkCoord = spawnChunkCoord;

        // 지상 엘리베이터 입구로 진입한 경우, 스폰 위치는 목표 엘리베이터 청크의 중앙이다.
        // 월드 좌표는 청크 크기가 필요하므로 InfinityMapManager 대기 후 계산한다(아래).
        bool useElevatorEntry = false;
        Vector2Int elevatorEntryCoord = default;

        if (GameManager.Instance != null && GameManager.Instance.saveManager != null)
        {
            var pData = GameManager.Instance.saveManager.playerData;
            if (pData != null && pData.spawnAtElevator)
            {
                useElevatorEntry = true;
                elevatorEntryCoord = new Vector2Int(pData.elevatorEntryXChunk, pData.elevatorEntryYChunk);
                targetChunkCoord = elevatorEntryCoord;

                pData.spawnAtElevator = false;   // 1회성 — 다음 진입엔 기본 스폰으로 복귀
                // 전체 Save()가 아니라 플래그만 파일에 덧쓴다. 이 코루틴은 지하 씬 활성화 직후,
                // 즉 SaveManager.Load()보다 먼저 돌기 때문에 지금의 PlayerStat은 아직
                // 프리팹 기본값 상태다 — 전체 Save는 세이브의 스탯·골드를 그 값으로 덮어쓴다.
                GameManager.Instance.saveManager.PersistSpawnFlagsOnly();
                Debug.Log($"[PlayerSpawner] 엘리베이터 입구 스폰 오버라이드: 청크 {elevatorEntryCoord}");
            }
            else if (pData != null && pData.isReturningFromDungeon)
            {
                targetSpawnPosition = pData.preDungeonPosition;
                // 좌표를 기준으로 청크 코디네이트도 다시 계산 (InfinityMapManager의 청크 크기가 10유닛이므로 10으로 나눔)
                // 실제 청크 크기에 따라 상수값이 달라질 수 있습니다. Sap-UVCS는 1000px / 100PPU = 10 단위.
                targetChunkCoord = new Vector2Int(
                    Mathf.FloorToInt(targetSpawnPosition.x / 10f),
                    Mathf.FloorToInt(targetSpawnPosition.y / 10f)
                );

                pData.isReturningFromDungeon = false;
                GameManager.Instance.saveManager.PersistSpawnFlagsOnly();   // 위와 같은 이유로 전체 Save 금지
                Debug.Log($"[PlayerSpawner] 던전 복귀 좌표로 스폰 위치 오버라이드: {targetSpawnPosition}, 청크: {targetChunkCoord}");
            }
        }

        // 1. 스폰 위치로 이동 후 활성화 (kinematic 상태라 물리 영향 없음)
        playerObj.transform.position = targetSpawnPosition;
        playerObj.SetActive(true);

        var rb = playerObj.GetComponentInChildren<Rigidbody2D>();
        if (rb != null) rb.bodyType = RigidbodyType2D.Kinematic;

        // 2. InfinityMapManager 초기화 대기
        float waited = 0f;
        while (InfinityMapManager.Instance == null)
        {
            waited += Time.deltaTime;
            if (waited > timeout)
            {
                Debug.LogError("[PlayerSpawner] InfinityMapManager가 초기화되지 않았습니다.");
                yield break;
            }
            yield return null;
        }
        var mgr = InfinityMapManager.Instance;

        // 2-B. 엘리베이터 입구 진입이면 목표 정류장 엘리베이터 위치로 착지 좌표를 잡는다.
        //      elevatorEntryCoord는 ElevatorEntryUI가 고른 정류장의 X(ElevatorStopLayout)를
        //      이미 넣어 둔 실제 정류장 청크 좌표다 — 여기서 다시 계산하면 안 된다.
        //      월드 좌표는 청크 크기가 필요해서 InfinityMapManager 대기 뒤인 여기서 계산한다.
        //      (계산식은 ElevatorManager.CalculateElevatorPosition과 같다. 지상→지하 씬 전환
        //       직후라 ElevatorManager가 아직 없을 수 있어 직접 계산한다.)
        if (useElevatorEntry)
        {
            targetSpawnPosition = new Vector2(
                elevatorEntryCoord.x * mgr.chunkWidthWorld  + ElevatorSpawner.ChunkLocalPosition.x,
                elevatorEntryCoord.y * mgr.chunkHeightWorld + ElevatorSpawner.ChunkLocalPosition.y
            );
            targetChunkCoord = elevatorEntryCoord;
            playerObj.transform.position = targetSpawnPosition;
        }

        // 3. 스폰 청크 로드 대기
        waited = 0f;
        while (!mgr.HasChunk(targetChunkCoord))
        {
            waited += Time.deltaTime;
            if (waited > timeout)
            {
                Debug.LogError($"[PlayerSpawner] {timeout}초 초과 — 청크 {targetChunkCoord}가 로드되지 않았습니다.");
                yield break;
            }
            yield return null;
        }

        // 3-B. 엘리베이터 입구 진입이면 착지 방을 페인팅한다(지하 층이동 TeleportRoutine과 동일).
        //      안 하면 생지형 속 엘리베이터 중앙에 스폰돼 물리로 밀려나 지형에 박힌다.
        //      콜라이더 재생성(아래 yield)보다 먼저여야 새 픽셀 기준으로 콜라이더가 만들어진다.
        if (useElevatorEntry && ElevatorManager.Instance != null && ElevatorManager.Instance.roomPainter != null)
        {
            int layerIndex = ElevatorManager.Instance.GetLayerIndexByDepth(elevatorEntryCoord.y);
            if (layerIndex >= 0)
            {
                ElevatorManager.Instance.roomPainter.PaintLandingRoom(targetChunkCoord.x, layerIndex);
                Debug.Log($"[PlayerSpawner] 착지 방 페인팅: 청크 X:{targetChunkCoord.x}, Layer:{layerIndex}");
            }
        }

        // 4. 콜라이더 갱신 대기 후 물리 활성화
        yield return null;
        if (rb != null) rb.bodyType = RigidbodyType2D.Dynamic;
        Debug.Log($"[PlayerSpawner] 스폰 완료: {targetSpawnPosition}");
     
    }
}
