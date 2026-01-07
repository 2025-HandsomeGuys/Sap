using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(PolygonCollider2D))]
public class TerrainChunk : MonoBehaviour
{
    private SpriteRenderer sr;
    private PolygonCollider2D polyCollider;
    private Texture2D texture;
    private Color32[] pixelData;

    [Header("기본 설정")]
    public int width = 1000;
    public int height = 1000;
    public float PPU = 100f;

    [Header("단독 씬용 자동 초기화")]
    [Tooltip("WorldManager 없이 단독으로 사용하는 씬이라면 체크해서 Start 시 자동 초기화합니다.")]
    public bool autoInitializeOnStart = false;

    [Header("플레이어 설정")]
    public Transform player;
    public float reachOffset = 1.0f;

    [Header("땅파기 모양 설정")]
    public float verticalScale = 1.5f;

    private bool isDirty = false;
    private float updateTimer = 0f;
    private float updateInterval = 0.1f;

    [Header("테두리 설정")]
    public Color solidBorderColor = new Color(0, 0, 0, 1);
    public float solidThickness = 0.05f;
    public float textureThickness = 0.3f;

    [Tooltip("텍스처 반복 빈도 (0.5 ~ 1.0 추천)")]
    public float textureTiling = 0.8f;

    public Texture2D borderTexture;

    // 내부 변수
    private Color32 solidColor32;
    private Color32[] borderPixels;
    private int borderW, borderH;
    private bool isTextureLoaded = false;

    // WorldGenerator 통합 변수
    private Vector2Int chunkCoord;      // 청크 좌표
    private float cellSize;              // 월드 단위 (0.3125f)
    private bool isInitialized = false;  // 초기화 상태

    // TileType → Color 매핑
    private static Color GetColorForTileType(TileType tileType)
    {
        return tileType switch
        {
            TileType.Dirt => new Color(0.6f, 0.4f, 0.2f, 1f),           // 갈색
            TileType.HardStone => new Color(0.5f, 0.5f, 0.5f, 1f),      // 회색
            TileType.CoolStone => new Color(0.4f, 0.5f, 0.6f, 1f),       // 청회색
            TileType.Ice => new Color(0.7f, 0.9f, 1f, 1f),              // 하늘색
            TileType.HotStone => new Color(1f, 0.6f, 0.2f, 1f),          // 주황색
            TileType.MagmaRock => new Color(0.8f, 0.2f, 0.2f, 1f),      // 빨간색
            TileType.MeteoriteRock => new Color(0.6f, 0.3f, 0.8f, 1f),  // 보라색
            TileType.Bedrock => new Color(0.1f, 0.1f, 0.1f, 1f),        // 검은색
            TileType.Empty => new Color(0, 0, 0, 0),                     // 투명
            _ => new Color(0.5f, 0.5f, 0.5f, 1f)                        // 기본 회색
        };
    }

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        polyCollider = GetComponent<PolygonCollider2D>();

        // WorldManager 없이 사용하는 씬에서는 간단한 자동 초기화를 사용할 수 있다.
        if (!isInitialized && autoInitializeOnStart)
        {
            SimpleAutoInitialize();
        }
    }

    void Update()
    {
        if (isDirty)
        {
            updateTimer += Time.deltaTime;
            if (updateTimer > updateInterval)
            {
                UpdateCollider();
                isDirty = false;
                updateTimer = 0f;
            }
        }
    }

    /// <summary>
    /// ChunkData를 기반으로 TerrainChunk를 초기화합니다.
    /// </summary>
    /// <param name="chunkData">WorldManager의 ChunkData</param>
    /// <param name="chunkCoord">청크 좌표</param>
    /// <param name="cellSize">월드 단위 (기본값 0.3125f)</param>
    public void Initialize(WorldManager.ChunkData chunkData, Vector2Int chunkCoord, float cellSize)
    {
        if (isInitialized)
        {
            Debug.LogWarning($"TerrainChunk at {chunkCoord} is already initialized!");
            return;
        }

        this.chunkCoord = chunkCoord;
        this.cellSize = cellSize;

        // PPU는 100f로 고정 (계산하지 않음)
        // 월드 크기 = width / PPU = 1000 / 100 = 10 유니티 단위
        // 청크 월드 크기 = chunkSize * cellSize = 32 * 0.3125 = 10 유니티 단위
        // → 1:1 매핑 완료

        // Transform 위치 설정 (청크들이 겹치지 않도록)
        // WorldManager의 GetChunkCoordFromPosition과 일치하도록 설정
        // WorldManager는 groundTilemap.WorldToCell을 사용하므로, 
        // TerrainChunk의 위치도 동일한 방식으로 계산해야 함
        float chunkWorldSize = WorldGenerator.chunkSize * cellSize; // 10 유니티 단위
        // 청크의 중심점을 기준으로 위치 설정 (스프라이트의 pivot이 0.5, 0.5이므로)
        float worldX = chunkCoord.x * chunkWorldSize + (chunkWorldSize * 0.5f);
        float worldY = chunkCoord.y * chunkWorldSize + (chunkWorldSize * 0.5f);
        transform.position = new Vector3(worldX, worldY, 0);
        
        Debug.Log($"TerrainChunk position calculated: chunkCoord={chunkCoord}, chunkWorldSize={chunkWorldSize}, position={transform.position}");

        // 컴포넌트 가져오기
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        if (polyCollider == null) polyCollider = GetComponent<PolygonCollider2D>();

        // 테두리 두께 검증
        if (textureThickness <= solidThickness + 0.01f) textureThickness = solidThickness + 0.1f;

        // 텍스처 생성
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;

        // ChunkData 기반으로 텍스처 생성
        GenerateTextureFromChunkData(chunkData);

        texture.Apply();
        pixelData = texture.GetPixels32();
        solidColor32 = (Color32)solidBorderColor;

        // 테두리 텍스처 로드
        if (borderTexture != null && borderTexture.isReadable)
        {
            borderPixels = borderTexture.GetPixels32();
            borderW = borderTexture.width;
            borderH = borderTexture.height;
            isTextureLoaded = true;
        }

        // 초기 테두리 생성 (전체 영역)
        UpdateBordersInArea(0, 0, width, height, Vector2.zero, 1.0f, 0.0f, 1.0f);
        ApplyTexture();

        // 스프라이트 생성
        sr.sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PPU);
        
        // Collider 생성 (Digger가 Physics2D.OverlapCircleAll로 찾을 수 있도록)
        UpdateCollider();

        // 플레이어 자동 찾기 (없는 경우)
        if (player == null)
        {
            if (WorldManager.Instance != null && WorldManager.Instance.playerTransformPublic != null)
            {
                player = WorldManager.Instance.playerTransformPublic;
            }
            else
            {
                Player3Controller playerController = FindFirstObjectByType<Player3Controller>();
                if (playerController != null)
                {
                    player = playerController.transform;
                }
            }
        }

        isInitialized = true;
        
        Debug.Log($"TerrainChunk at {chunkCoord} initialized successfully. Position: {transform.position}, Collider: {(polyCollider != null ? "Created" : "NULL")}");
    }

    /// <summary>
    /// WorldManager/WorldGenerator 없이 단독 씬에서 사용할 수 있는 간단한 자동 초기화.
    /// 전체를 하나의 타일 타입(기본 Dirt)으로 채운 뒤 텍스처/콜라이더를 세팅한다.
    /// </summary>
    private void SimpleAutoInitialize()
    {
        // 이미 다른 곳에서 초기화했다면 패스
        if (isInitialized)
            return;

        // 기본 컴포넌트 확보
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        if (polyCollider == null) polyCollider = GetComponent<PolygonCollider2D>();

        // 텍스처 생성 및 전체를 Dirt 색으로 채우기
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;

        Color32 baseColor = (Color32)GetColorForTileType(TileType.Dirt);
        Color32[] pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = baseColor;
        }
        texture.SetPixels32(pixels);
        texture.Apply();

        pixelData = texture.GetPixels32();
        solidColor32 = (Color32)solidBorderColor;

        // 테두리 텍스처 준비
        if (borderTexture != null && borderTexture.isReadable)
        {
            borderPixels = borderTexture.GetPixels32();
            borderW = borderTexture.width;
            borderH = borderTexture.height;
            isTextureLoaded = true;
        }

        // 전체 영역 테두리 적용
        UpdateBordersInArea(0, 0, width, height, Vector2.zero, 1.0f, 0.0f, 1.0f);
        ApplyTexture();

        // 스프라이트 및 콜라이더 세팅
        sr.sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PPU);
        UpdateCollider();

        isInitialized = true;
        Debug.Log("TerrainChunk: SimpleAutoInitialize completed (standalone mode).");
    }

    /// <summary>
    /// ChunkData의 terrainLayer를 기반으로 텍스처를 생성합니다.
    /// </summary>
    private void GenerateTextureFromChunkData(WorldManager.ChunkData chunkData)
    {
        Color32[] pixels = new Color32[width * height];

        // 청크 데이터 크기 (32 x 32)
        // 텍스처 크기 (1000 x 1000)로 스케일링
        float scaleX = (float)width / WorldGenerator.chunkSize;
        float scaleY = (float)height / WorldGenerator.chunkSize;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // 텍스처 좌표를 청크 데이터 좌표로 변환
                int chunkX = Mathf.FloorToInt(x / scaleX);
                int chunkY = Mathf.FloorToInt(y / scaleY);

                // 범위 체크
                if (chunkX >= 0 && chunkX < WorldGenerator.chunkSize && chunkY >= 0 && chunkY < WorldGenerator.chunkSize)
                {
                    TileType tileType = chunkData.terrainLayer[chunkX, chunkY];
                    Color color = GetColorForTileType(tileType);
                    pixels[y * width + x] = (Color32)color;
                }
                else
                {
                    // 범위 밖은 투명
                    pixels[y * width + x] = new Color32(0, 0, 0, 0);
                }
            }
        }

        texture.SetPixels32(pixels);
    }

    void ApplyTexture()
    {
        if (texture == null || pixelData == null) return;
        texture.SetPixels32(pixelData);
        texture.Apply(false);
    }

    // ==================================================================================
    //  Dig 함수
    // ==================================================================================
    public void Dig(Vector2 mouseWorldPos, float radius)
    {
        if (!isInitialized)
        {
            Debug.LogWarning($"TerrainChunk at {chunkCoord}: Not initialized! Call Initialize() first.");
            return;
        }
        
        // 디버그 로그 추가
        Debug.Log($"TerrainChunk.Dig() called at {mouseWorldPos} with radius {radius}");

        // 플레이어 자동 찾기
        if (player == null)
        {
            if (WorldManager.Instance != null && WorldManager.Instance.playerTransformPublic != null)
            {
                player = WorldManager.Instance.playerTransformPublic;
            }
            else
            {
                Player3Controller playerController = FindFirstObjectByType<Player3Controller>();
                if (playerController != null)
                {
                    player = playerController.transform;
                }
                else
                {
                    Debug.LogWarning("TerrainChunk: Player Transform is not assigned and cannot be found!");
                    return;
                }
            }
        }

        Vector2 playerPos = player.position;
        Vector2 direction = (mouseWorldPos - playerPos).normalized;
        float angle = Mathf.Atan2(direction.y, direction.x);

        float worldWidth = width / PPU;
        float worldHeight = height / PPU;

        Vector2 playerLocalPos = transform.InverseTransformPoint(playerPos);
        int playerPx = Mathf.FloorToInt((playerLocalPos.x + (worldWidth * 0.5f)) * PPU);
        int playerPy = Mathf.FloorToInt((playerLocalPos.y + (worldHeight * 0.5f)) * PPU);

        float reachOffsetPx = reachOffset * PPU;
        int r_holePx = Mathf.FloorToInt(radius * PPU);

        float cos = Mathf.Cos(-angle);
        float sin = Mathf.Sin(-angle);
        float frontScale = Mathf.Max(1f, verticalScale);

        int centerPx = playerPx + Mathf.FloorToInt(direction.x * reachOffsetPx);
        int centerPy = playerPy + Mathf.FloorToInt(direction.y * reachOffsetPx);

        int maxRadiusPx = Mathf.CeilToInt(r_holePx * frontScale);
        int margin = maxRadiusPx + 5;

        int minX = Mathf.Clamp(centerPx - margin, 0, width);
        int maxX = Mathf.Clamp(centerPx + margin, 0, width);
        int minY = Mathf.Clamp(centerPy - margin, 0, height);
        int maxY = Mathf.Clamp(centerPy + margin, 0, height);

        float sqrHolePx = r_holePx * r_holePx;
        bool pixelChanged = false;

        // 1. 구멍 뚫기
        for (int y = minY; y < maxY; y++)
        {
            float dy = y - playerPy;
            for (int x = minX; x < maxX; x++)
            {
                int index = y * width + x;
                if (pixelData[index].a == 0) continue;

                float dx = x - playerPx;
                float localX = dx * cos - dy * sin;
                float localY = dx * sin + dy * cos;
                localX -= reachOffsetPx;

                float currentScale = (localX >= 0) ? verticalScale : 1.0f;
                float localX_scaled = localX / currentScale;
                float distSqr = (localX_scaled * localX_scaled) + (localY * localY);

                if (distSqr <= sqrHolePx)
                {
                    pixelData[index] = new Color32(0, 0, 0, 0);
                    pixelChanged = true;
                }
            }
        }

        if (pixelChanged)
        {
            int updateMargin = Mathf.CeilToInt(textureThickness * PPU) + 20;

            int bMinX = Mathf.Clamp(minX - updateMargin, 0, width);
            int bMaxX = Mathf.Clamp(maxX + updateMargin, 0, width);
            int bMinY = Mathf.Clamp(minY - updateMargin, 0, height);
            int bMaxY = Mathf.Clamp(maxY + updateMargin, 0, height);

            // [중요] 늘어짐 보정을 위해 verticalScale 전달
            UpdateBordersInArea(bMinX, bMinY, bMaxX, bMaxY, new Vector2(centerPx, centerPy), cos, sin, verticalScale, true);

            ApplyTexture();
            isDirty = true;
        }
    }

    // ==================================================================================
    //  테두리 업데이트 (늘어짐 보정 로직 강화)
    // ==================================================================================
    public void UpdateBordersInArea(int minX, int minY, int maxX, int maxY, Vector2 pivotPos,
                                    float cos = 1f, float sin = 0f, float scaleCorrection = 1f,
                                    bool isPixelSpace = false)
    {
        if (!isInitialized) return;

        int solidPx = Mathf.CeilToInt(solidThickness * PPU);
        int texPx = Mathf.CeilToInt(textureThickness * PPU);
        float thicknessDelta = Mathf.Max(1f, texPx - solidPx);

        int pX = (int)pivotPos.x;
        int pY = (int)pivotPos.y;
        if (!isPixelSpace)
        {
            Vector2 localPos = transform.InverseTransformPoint(pivotPos);
            pX = Mathf.FloorToInt((localPos.x + (width / PPU * 0.5f)) * PPU);
            pY = Mathf.FloorToInt((localPos.y + (height / PPU * 0.5f)) * PPU);
        }

        for (int y = minY; y < maxY; y++)
        {
            int yIndex = y * width;
            for (int x = minX; x < maxX; x++)
            {
                int index = yIndex + x;
                if (pixelData[index].a == 0) continue;

                int distToAir = GetDistanceToNearestAir(x, y, texPx);

                // 1. 단색 테두리
                if (distToAir <= solidPx)
                {
                    pixelData[index] = solidColor32;
                }
                // 2. 이미지 테두리
                else if (distToAir <= texPx && isTextureLoaded)
                {
                    float dx = x - pX;
                    float dy = y - pY;

                    // 회전된 로컬 좌표 (앞/뒤 구분용)
                    float localX = dx * cos - dy * sin;
                    float localY = dx * sin + dy * cos;

                    // [늘어짐 해결 핵심]
                    // 앞쪽(localX > 0)일 때는 Y좌표에 스케일을 곱해서 '각도를 빠르게' 만듭니다.
                    // 이렇게 하면 타원이 길어진 만큼 텍스처 좌표도 압축되어 늘어짐이 사라집니다.
                    float angleY = localY;
                    if (localX > 0) angleY *= scaleCorrection;

                    // 보정된 각도 계산
                    float correctedAngle = Mathf.Atan2(angleY, localX);
                    float normalizedAngle = (correctedAngle + Mathf.PI) / (2 * Mathf.PI);

                    // U좌표: 보정된 각도 * 반지름 * 타일링
                    // (반지름은 일정한 값을 곱해주면 텍스처 크기가 일정해집니다)
                    // 여기서는 '텍스처 두께'를 기준 반지름으로 삼아 균일하게 만듭니다.
                    float baseCircumference = texPx * 20f; // 임의의 기준 원둘레
                    int u = Mathf.FloorToInt(normalizedAngle * baseCircumference * textureTiling) % borderW;
                    if (u < 0) u += borderW;

                    // V좌표: (1 - 거리) -> 거꾸로 매핑 (공기와 가까울수록 상단)
                    float normalizedDist = (float)(distToAir - solidPx) / thicknessDelta;
                    int v = Mathf.RoundToInt((1f - normalizedDist) * (borderH - 1));
                    v = Mathf.Clamp(v, 0, borderH - 1);

                    Color32 col = borderPixels[v * borderW + u];
                    if (col.a > 20) pixelData[index] = col;
                }
            }
        }
    }

    private int GetDistanceToNearestAir(int cx, int cy, int maxCheck)
    {
        if (IsTransparent(cx + 1, cy) || IsTransparent(cx - 1, cy) ||
            IsTransparent(cx, cy + 1) || IsTransparent(cx, cy - 1)) return 1;

        for (int r = 2; r <= maxCheck; r++)
        {
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (IsTransparent(x, cy + r)) return r;
                if (IsTransparent(x, cy - r)) return r;
            }
            for (int y = cy - r + 1; y <= cy + r - 1; y++)
            {
                if (IsTransparent(cx + r, y)) return r;
                if (IsTransparent(cx - r, y)) return r;
            }
        }
        return maxCheck + 1;
    }

    private bool IsTransparent(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return false;
        return pixelData[y * width + x].a == 0;
    }

    void UpdateCollider()
    {
        // 기존 Collider 제거
        if (polyCollider != null)
        {
            Destroy(polyCollider);
            polyCollider = null;
        }
        
        // 새 Collider 생성
        polyCollider = gameObject.AddComponent<PolygonCollider2D>();
        
        if (polyCollider == null)
        {
            Debug.LogError($"TerrainChunk: Failed to create PolygonCollider2D at {chunkCoord}");
        }
    }

    // ==================================================================================
    //  좌표 변환 유틸리티
    // ==================================================================================
    
    /// <summary>
    /// 월드 좌표를 청크 내부 픽셀 좌표로 변환합니다.
    /// </summary>
    public Vector2Int WorldToChunkPixel(Vector2 worldPos)
    {
        if (!isInitialized) return Vector2Int.zero;

        Vector2 localPos = transform.InverseTransformPoint(worldPos);
        float worldWidth = width / PPU;
        float worldHeight = height / PPU;
        
        int pixelX = Mathf.FloorToInt((localPos.x + (worldWidth * 0.5f)) * PPU);
        int pixelY = Mathf.FloorToInt((localPos.y + (worldHeight * 0.5f)) * PPU);
        
        return new Vector2Int(pixelX, pixelY);
    }

    /// <summary>
    /// 청크 내부 픽셀 좌표를 월드 좌표로 변환합니다.
    /// </summary>
    public Vector2 ChunkPixelToWorld(int pixelX, int pixelY)
    {
        if (!isInitialized) return Vector2.zero;

        float worldWidth = width / PPU;
        float worldHeight = height / PPU;
        
        float localX = (pixelX / PPU) - (worldWidth * 0.5f);
        float localY = (pixelY / PPU) - (worldHeight * 0.5f);
        
        return transform.TransformPoint(new Vector3(localX, localY, 0));
    }
}
