using System.Collections.Generic;
using UnityEngine;

// [저장 시스템 1] 청크의 정보를 담을 클래스 (나중에 광물 리스트 등도 여기에 추가)
public class ChunkData
{
    public Color32[] modifiedPixels; // 변형된 지형 데이터
    public bool hasChanges;          // 변경사항이 있는지 여부 (최적화용)
    // public List<MineralData> minerals; // 나중에 광물 위치 저장 시 사용
}

public class InfinityMapManager : MonoBehaviour
{
    [Header("설정")]
    public GameObject chunkPrefab;
    public Transform player;
    public int viewDistance = 1;

    // 내부 변수
    private float chunkWidthWorld;
    private float chunkHeightWorld;
    private Vector2Int lastChunkCoord = new Vector2Int(99999, 99999);

    // 생성된 청크 관리 & 재활용 풀
    private Dictionary<Vector2Int, TerrainChunk> activeChunks = new Dictionary<Vector2Int, TerrainChunk>();
    private Queue<TerrainChunk> chunkPool = new Queue<TerrainChunk>();

    // [저장 시스템 2] 좌표별 지형 데이터를 영구(게임 켜져있는 동안) 저장하는 딕셔너리
    private Dictionary<Vector2Int, ChunkData> worldData = new Dictionary<Vector2Int, ChunkData>();

    // 원본 픽셀 데이터 캐싱
    private Color32[] cachedSourcePixels;
    private int sourceWidth, sourceHeight;

    public float reachOffset = 1.0f;

    // 싱글톤
    public static InfinityMapManager Instance;
    void Awake() { Instance = this; }

    void Start()
    {
        TerrainChunk temp = chunkPrefab.GetComponent<TerrainChunk>();
        chunkWidthWorld = temp.width / temp.PPU;
        chunkHeightWorld = temp.height / temp.PPU;

        Texture2D sourceTex = temp.GetComponent<SpriteRenderer>().sprite.texture;
        if (sourceTex.width == temp.width && sourceTex.height == temp.height)
        {
            cachedSourcePixels = sourceTex.GetPixels32();
            sourceWidth = temp.width;
            sourceHeight = temp.height;
        }
        else
        {
            Debug.LogError("프리팹 텍스처 크기 오류");
        }

        UpdateChunks();
    }

    void Update()
    {
        if (!player) return;

        int x = Mathf.RoundToInt(player.position.x / chunkWidthWorld);
        int y = Mathf.RoundToInt(player.position.y / chunkHeightWorld);
        Vector2Int currentChunkCoord = new Vector2Int(x, y);

        if (currentChunkCoord != lastChunkCoord)
        {
            lastChunkCoord = currentChunkCoord;
            UpdateChunks();
        }
    }

    void UpdateChunks()
    {
        // 1. 화면 밖으로 나간 청크 제거 및 [데이터 저장]
        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var kvp in activeChunks)
        {
            if (Mathf.Abs(kvp.Key.x - lastChunkCoord.x) > viewDistance ||
                Mathf.Abs(kvp.Key.y - lastChunkCoord.y) > viewDistance)
            {
                // [저장 시스템 3] 청크를 끄기 전에 현재 상태를 백업
                SaveChunkData(kvp.Key, kvp.Value);

                ReturnChunkToPool(kvp.Value);
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var key in toRemove)
        {
            activeChunks.Remove(key);
        }

        // 2. 새로운 청크 생성 및 [데이터 로드]
        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int y = -viewDistance; y <= viewDistance; y++)
            {
                Vector2Int targetCoord = lastChunkCoord + new Vector2Int(x, y);

                if (!activeChunks.ContainsKey(targetCoord))
                {
                    SpawnChunk(targetCoord);
                }
            }
        }
    }

    // [저장 시스템 4] 청크 데이터 저장 함수
    void SaveChunkData(Vector2Int coord, TerrainChunk chunk)
    {
        // 이미 저장된 데이터가 있는지 확인
        if (!worldData.ContainsKey(coord))
        {
            worldData.Add(coord, new ChunkData());
        }

        ChunkData data = worldData[coord];

        // 메모리 할당: 픽셀 데이터의 '복사본'을 만들어야 함 (참조만 복사하면 풀링된 청크가 덮어씌워질 때 망가짐)
        if (data.modifiedPixels == null || data.modifiedPixels.Length != chunk.pixelData.Length)
        {
            data.modifiedPixels = new Color32[chunk.pixelData.Length];
        }

        // 현재 청크의 픽셀 상태를 worldData에 복사
        System.Array.Copy(chunk.pixelData, data.modifiedPixels, chunk.pixelData.Length);
        data.hasChanges = true;

        // *팁: 나중에 광물 위치 정보도 여기서 chunk.minerals 리스트를 가져와서 data.minerals에 저장하면 됨
    }

    void SpawnChunk(Vector2Int coord)
    {
        TerrainChunk chunk;
        Vector3 spawnPos = new Vector3(coord.x * chunkWidthWorld, coord.y * chunkHeightWorld, 0);

        if (chunkPool.Count > 0)
        {
            chunk = chunkPool.Dequeue();
            chunk.transform.position = spawnPos;
            chunk.gameObject.SetActive(true);
        }
        else
        {
            GameObject newObj = Instantiate(chunkPrefab, spawnPos, Quaternion.identity);
            newObj.transform.parent = transform;
            chunk = newObj.GetComponent<TerrainChunk>();
            chunk.FirstTimeInit(sourceWidth, sourceHeight);
        }

        chunk.name = $"Terrain_{coord.x}_{coord.y}";

        bool isModified = false;
        Color32[] pixelsToUse = cachedSourcePixels;

        if (worldData.ContainsKey(coord) && worldData[coord].hasChanges)
        {
            // 저장된 데이터가 있으면 그것을 사용
            pixelsToUse = worldData[coord].modifiedPixels;
            isModified = true; // "이거 수정된 땅이야!" 라고 표시
        }

        // [핵심 변경점] Reuse 호출 시 isModified 플래그 전달
        chunk.Reuse(player, pixelsToUse, isModified);

        activeChunks.Add(coord, chunk);
    }

    public void ModifyTerrain(Vector2 mouseWorldPos, float radius)
    {
        if (player == null) return;
        Vector2 playerPos = player.position;
        Vector2 direction = (mouseWorldPos - playerPos).normalized;
        Vector2 actualHitPos = playerPos + (direction * reachOffset);

        float maxScale = 2.0f;
        float searchRadius = radius * maxScale + 1.0f;

        float minX = actualHitPos.x - searchRadius;
        float maxX = actualHitPos.x + searchRadius;
        float minY = actualHitPos.y - searchRadius;
        float maxY = actualHitPos.y + searchRadius;

        int minChunkX = Mathf.RoundToInt(minX / chunkWidthWorld);
        int maxChunkX = Mathf.RoundToInt(maxX / chunkWidthWorld);
        int minChunkY = Mathf.RoundToInt(minY / chunkHeightWorld);
        int maxChunkY = Mathf.RoundToInt(maxY / chunkHeightWorld);

        for (int x = minChunkX; x <= maxChunkX; x++)
        {
            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                Vector2Int chunkCoord = new Vector2Int(x, y);
                if (activeChunks.ContainsKey(chunkCoord))
                {
                    activeChunks[chunkCoord].Dig(mouseWorldPos, radius);
                }
            }
        }
    }

    void ReturnChunkToPool(TerrainChunk chunk)
    {
        chunk.gameObject.SetActive(false);
        chunkPool.Enqueue(chunk);
    }

    public bool IsWorldPositionEmpty(Vector2 worldPos)
    {
        int chunkX = Mathf.RoundToInt(worldPos.x / chunkWidthWorld);
        int chunkY = Mathf.RoundToInt(worldPos.y / chunkHeightWorld);
        Vector2Int targetCoord = new Vector2Int(chunkX, chunkY);

        if (activeChunks.ContainsKey(targetCoord))
        {
            TerrainChunk targetChunk = activeChunks[targetCoord];
            Vector2 localPos = worldPos - (Vector2)targetChunk.transform.position;
            return targetChunk.IsPixelEmptyLocal(localPos);
        }
        return false;
    }

    public TerrainChunk GetChunk(Vector2Int coord)
    {
        if (activeChunks.ContainsKey(coord)) return activeChunks[coord];
        return null;
    }
}