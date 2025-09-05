using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class WorldManager : MonoBehaviour
{
    #region Singleton
    public static WorldManager Instance;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }
    #endregion

    [Header("References")]
    public Transform playerTransform;
    public WorldGenerator worldGenerator;
    public Tilemap groundTilemap;

    [Header("Settings")]
    public int viewDistanceInChunks = 1;

    public class ChunkData
    {
        public TileType[,] tileStates;
        public Vector2Int chunkCoord;
        public List<GameObject> spawnedItems;

        public ChunkData(Vector2Int coord, int chunkSize)
        {
            chunkCoord = coord;
            tileStates = new TileType[chunkSize, chunkSize];
            spawnedItems = new List<GameObject>();
        }
    }

    private Vector2Int currentPlayerChunkCoord;
    private HashSet<Vector2Int> generatedChunks = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> loadingChunks = new HashSet<Vector2Int>();
    private Dictionary<Vector2Int, ChunkData> chunkDataMap = new Dictionary<Vector2Int, ChunkData>();

    void Start()
    {
        if (playerTransform == null || worldGenerator == null || groundTilemap == null)
        {
            Debug.LogError("Player Transform, World Generator, or Ground Tilemap not assigned in WorldManager!");
            this.enabled = false;
            return;
        }

        UpdateChunks();
    }

    void Update()
    {
        Vector2Int playerChunk = GetChunkCoordFromPosition(playerTransform.position);
        if (playerChunk != currentPlayerChunkCoord)
        {
            currentPlayerChunkCoord = playerChunk;
            UpdateChunks();
        }
    }

    public Vector3Int WorldToCell(Vector3 worldPos)
    {
        return groundTilemap.WorldToCell(worldPos);
    }

    Vector2Int GetChunkCoordFromPosition(Vector3 position)
    {
        Vector3Int cellPos = groundTilemap.WorldToCell(position);
        int x = Mathf.FloorToInt((float)cellPos.x / WorldGenerator.chunkSize);
        int y = Mathf.FloorToInt((float)cellPos.y / WorldGenerator.chunkSize);
        return new Vector2Int(x, y);
    }

    void UpdateChunks()
    {
        currentPlayerChunkCoord = GetChunkCoordFromPosition(playerTransform.position);

        // 새 청크 로드
        for (int xOffset = -viewDistanceInChunks; xOffset <= viewDistanceInChunks; xOffset++)
        {
            for (int yOffset = -viewDistanceInChunks; yOffset <= viewDistanceInChunks; yOffset++)
            {
                Vector2Int chunkToGenerate = new Vector2Int(currentPlayerChunkCoord.x + xOffset, currentPlayerChunkCoord.y + yOffset);

                if (!generatedChunks.Contains(chunkToGenerate) && !loadingChunks.Contains(chunkToGenerate))
                {
                    loadingChunks.Add(chunkToGenerate);

                    if (!chunkDataMap.TryGetValue(chunkToGenerate, out ChunkData chunkData))
                    {
                        chunkData = new ChunkData(chunkToGenerate, WorldGenerator.chunkSize);
                        worldGenerator.InitializeChunkData(chunkData);
                        chunkDataMap.Add(chunkToGenerate, chunkData);
                    }

                    StartCoroutine(GenerateChunkCoroutineWrapper(chunkToGenerate, chunkData));
                }
            }
        }

        // 범위 밖 청크 언로드
        List<Vector2Int> chunksToUnload = new List<Vector2Int>();
        foreach (Vector2Int chunkCoord in generatedChunks)
        {
            int xDiff = Mathf.Abs(chunkCoord.x - currentPlayerChunkCoord.x);
            int yDiff = Mathf.Abs(chunkCoord.y - currentPlayerChunkCoord.y);

            if (xDiff > viewDistanceInChunks + 1 || yDiff > viewDistanceInChunks + 1)
            {
                chunksToUnload.Add(chunkCoord);
            }
        }

        foreach (Vector2Int chunkCoord in chunksToUnload)
        {
            UnloadChunk(chunkCoord);
        }
    }

    IEnumerator GenerateChunkCoroutineWrapper(Vector2Int chunkCoord, ChunkData chunkData)
    {
        // 청크 생성 분산 → 한 프레임에 몰리지 않음
        yield return StartCoroutine(worldGenerator.GenerateChunk(chunkCoord, chunkData, groundTilemap));
        yield return null;

        loadingChunks.Remove(chunkCoord);
        generatedChunks.Add(chunkCoord);
    }

    void UnloadChunk(Vector2Int chunkCoord)
    {
        if (generatedChunks.Remove(chunkCoord))
        {
            if (chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
            {
                // 오브젝트 반환
                foreach (GameObject itemObject in chunkData.spawnedItems)
                {
                    Mineable mineable = itemObject.GetComponent<Mineable>();
                    if (mineable != null && mineable.itemData != null)
                    {
                        ObjectPooler.Instance.ReturnToPool(mineable.itemData.poolType, itemObject);
                    }
                    else
                    {
                        Destroy(itemObject);
                    }
                }
                chunkData.spawnedItems.Clear();

                // 타일 제거 최적화
                int startX = chunkCoord.x * WorldGenerator.chunkSize;
                int startY = chunkCoord.y * WorldGenerator.chunkSize;

                BoundsInt bounds = new BoundsInt(
                    startX, startY, 0,
                    WorldGenerator.chunkSize, WorldGenerator.chunkSize, 1
                );
                groundTilemap.SetTilesBlock(bounds, new TileBase[WorldGenerator.chunkSize * WorldGenerator.chunkSize]);
            }

            // 메모리 절약: 멀리 벗어난 청크 데이터 제거
            chunkDataMap.Remove(chunkCoord);
        }
    }

    public void TileDug(Vector3 worldPosition)
    {
        Vector3Int cellPosition = groundTilemap.WorldToCell(worldPosition);
        Vector2Int chunkCoord = GetChunkCoordFromPosition(worldPosition);

        if (chunkDataMap.TryGetValue(chunkCoord, out ChunkData chunkData))
        {
            int localX = cellPosition.x - (chunkCoord.x * WorldGenerator.chunkSize);
            int localY = cellPosition.y - (chunkCoord.y * WorldGenerator.chunkSize);

            if (localX >= 0 && localX < WorldGenerator.chunkSize && localY >= 0 && localY < WorldGenerator.chunkSize)
            {
                if (chunkData.tileStates[localX, localY] != TileType.Empty)
                {
                    chunkData.tileStates[localX, localY] = TileType.Empty;
                    groundTilemap.SetTile(cellPosition, null);
                }
            }
        }
    }
}
