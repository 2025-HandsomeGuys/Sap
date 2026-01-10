using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(PolygonCollider2D))]
public class TerrainChunk : MonoBehaviour
{
    private SpriteRenderer sr;
    private PolygonCollider2D polyCollider;
    private Texture2D texture;
    // pixelData를 외부(ProcessIsland의 다른 청크 접근)에서 읽을 수 있게 public getter 추가 혹은 내부 로직 활용
    public Color32[] pixelData;

    [Header("기본 설정")]
    public int width = 1000;
    public int height = 1000;
    public float PPU = 100f;

    [Header("플레이어 설정")]
    public Transform player;
    public float reachOffset = 1.0f;

    [Header("땅파기 모양 설정")]
    public float verticalScale = 1.5f;

    private bool isDirty = false;
    private float updateTimer = 0f;
    private float updateInterval = 0.1f;

    [Header("부유섬 제거 설정 (렉 최적화)")]
    public int maxIslandSize = 50;
    public bool useIslandRemoval = true;

    [Header("테두리 설정")]
    public Color solidBorderColor = new Color(0, 0, 0, 1);
    public float solidThickness = 0.05f;
    public float textureThickness = 0.3f;
    public float textureTiling = 0.8f;
    public Texture2D borderTexture;

    private Color32 solidColor32;
    private Color32[] borderPixels;
    private int borderW, borderH;
    private bool isTextureLoaded = false;

    private TerrainChunk leftChunk;
    private TerrainChunk rightChunk;
    private TerrainChunk topChunk;
    private TerrainChunk bottomChunk;

    // =========================================================
    // 초기화
    // =========================================================
    public void FirstTimeInit(int w, int h)
    {
        width = w; height = h;
        sr = GetComponent<SpriteRenderer>();
        polyCollider = GetComponent<PolygonCollider2D>();
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        pixelData = new Color32[width * height];
        sr.sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PPU);

        if (borderTexture != null && borderTexture.isReadable)
        {
            borderPixels = borderTexture.GetPixels32();
            borderW = borderTexture.width;
            borderH = borderTexture.height;
            isTextureLoaded = true;
        }
        solidColor32 = (Color32)solidBorderColor;
    }

    public void Reuse(Transform playerTransform, Color32[] sourcePixels, bool hasChanges)
    {
        this.player = playerTransform;
        Array.Copy(sourcePixels, pixelData, sourcePixels.Length);
        texture.SetPixels32(pixelData);
        texture.Apply(false);
        if (hasChanges)
        {
            // 수정된 적이 있는 땅이라면 -> 구멍 모양대로 콜라이더 재생성 (약간의 연산 비용 발생)
            UpdateCollider();
        }
        else
        {
            // 수정된 적 없는 새 땅이라면 -> 그냥 네모난 통짜 콜라이더 (빠름)
            SetFullSquareCollider();
        }

        isDirty = false;
    }

    void SetFullSquareCollider()
    {
        if (polyCollider == null) polyCollider = gameObject.AddComponent<PolygonCollider2D>();
        float w = width / PPU;
        float h = height / PPU;
        polyCollider.pathCount = 1;
        polyCollider.SetPath(0, new Vector2[] { new Vector2(-w / 2, -h / 2), new Vector2(w / 2, -h / 2), new Vector2(w / 2, h / 2), new Vector2(-w / 2, h / 2) });
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

    public void CacheNeighbors()
    {
        if (InfinityMapManager.Instance == null) return;

        float worldW = width / PPU;
        float worldH = height / PPU;
        int chunkX = Mathf.RoundToInt(transform.position.x / worldW);
        int chunkY = Mathf.RoundToInt(transform.position.y / worldH);

        leftChunk = InfinityMapManager.Instance.GetChunk(new Vector2Int(chunkX - 1, chunkY));
        rightChunk = InfinityMapManager.Instance.GetChunk(new Vector2Int(chunkX + 1, chunkY));
        topChunk = InfinityMapManager.Instance.GetChunk(new Vector2Int(chunkX, chunkY + 1));
        bottomChunk = InfinityMapManager.Instance.GetChunk(new Vector2Int(chunkX, chunkY - 1));
    }

    public byte GetPixelAlpha(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return 0;
        return pixelData[y * width + x].a;
    }

    // [Helper] 픽셀 좌표 -> 월드 좌표 변환 함수 (파티클 생성 위치용)
    public Vector2 GetWorldPos(int x, int y)
    {
        // 1. 로컬 좌표 계산 (Pivot 0.5, 0.5 기준)
        float localX = (x / PPU) - (width / PPU * 0.5f);
        float localY = (y / PPU) - (height / PPU * 0.5f);

        // 2. 로컬 -> 월드 변환
        return transform.TransformPoint(new Vector2(localX, localY));
    }

    // ==================================================================================
    //  Dig 함수
    // ==================================================================================
    public void Dig(Vector2 mouseWorldPos, float radius)
    {
        CacheNeighbors();
        if (player == null) return;

        Vector2 playerWorldPos = player.position;
        Vector2 direction = (mouseWorldPos - playerWorldPos).normalized;
        float angle = Mathf.Atan2(direction.y, direction.x);

        float worldWidth = width / PPU;
        float worldHeight = height / PPU;
        Vector2 playerLocalPos = transform.InverseTransformPoint(playerWorldPos);

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

        int rawMinX = centerPx - margin;
        int rawMaxX = centerPx + margin;
        int rawMinY = centerPy - margin;
        int rawMaxY = centerPy + margin;

        int borderMargin = Mathf.CeilToInt(textureThickness * PPU) + 10;
        bool isOverlap = (rawMaxX >= -borderMargin && rawMinX <= width + borderMargin &&
                          rawMaxY >= -borderMargin && rawMinY <= height + borderMargin);

        if (!isOverlap) return;

        int loopMinX = Mathf.Clamp(rawMinX, 0, width);
        int loopMaxX = Mathf.Clamp(rawMaxX, 0, width);
        int loopMinY = Mathf.Clamp(rawMinY, 0, height);
        int loopMaxY = Mathf.Clamp(rawMaxY, 0, height);

        float sqrHolePx = r_holePx * r_holePx;
        bool pixelChanged = false;

        for (int y = loopMinY; y < loopMaxY; y++)
        {
            float dy = y - playerPy;
            for (int x = loopMinX; x < loopMaxX; x++)
            {
                int index = y * width + x;
                if (pixelData[index].a == 0) continue;

                float dx = x - playerPx;
                float localX = dx * cos - dy * sin;
                float localY = dx * sin + dy * cos;
                localX -= reachOffsetPx;

                float currentScale = (localX >= 0) ? verticalScale : 1.0f;
                float localX_scaled = localX / currentScale;

                if ((localX_scaled * localX_scaled) + (localY * localY) <= sqrHolePx)
                {
                    // [파티클 추가 1] 땅을 직접 팔 때
                    // 1. 현재 픽셀 색상 저장
                    Color32 debrisColor = pixelData[index];

                    // 2. 픽셀 지우기
                    pixelData[index] = new Color32(0, 0, 0, 0);
                    pixelChanged = true;

                    // 3. 파티클 매니저 호출 (확률은 매니저 내부 혹은 여기서 처리)
                    // TerrainParticleManager가 있다면 아래 주석 해제
                    if (TerrainParticleManager.Instance != null)
                    {
                        TerrainParticleManager.Instance.SpawnDebris(GetWorldPos(x, y), debrisColor);
                    }
                }
            }
        }

        if (pixelChanged || isOverlap)
        {
            int updateMargin = Mathf.CeilToInt(textureThickness * PPU) + 20;
            int bMinX = Mathf.Clamp(rawMinX - updateMargin, 0, width);
            int bMaxX = Mathf.Clamp(rawMaxX + updateMargin, 0, width);
            int bMinY = Mathf.Clamp(rawMinY - updateMargin, 0, height);
            int bMaxY = Mathf.Clamp(rawMaxY + updateMargin, 0, height);

            UpdateBordersInArea(bMinX, bMinY, bMaxX, bMaxY, new Vector2(centerPx, centerPy), cos, sin, verticalScale, true);

            if (useIslandRemoval && pixelChanged)
            {
                CheckFloatingIslandsInArea(loopMinX - 2, loopMinY - 2, loopMaxX + 2, loopMaxY + 2);
            }

            ApplyTexture();
            if (pixelChanged) isDirty = true;
        }
    }

    // ==================================================================================
    //  부유섬 제거 알고리즘
    // ==================================================================================

    struct PixelNode
    {
        public TerrainChunk chunk;
        public int x, y;
        public PixelNode(TerrainChunk c, int _x, int _y) { chunk = c; x = _x; y = _y; }
    }

    void CheckFloatingIslandsInArea(int minX, int minY, int maxX, int maxY)
    {
        minX = Mathf.Clamp(minX, 0, width);
        maxX = Mathf.Clamp(maxX, 0, width);
        minY = Mathf.Clamp(minY, 0, height);
        maxY = Mathf.Clamp(maxY, 0, height);

        HashSet<TerrainChunk> affectedChunks = new HashSet<TerrainChunk>();
        affectedChunks.Add(this);

        Dictionary<TerrainChunk, HashSet<int>> safeGrounds = new Dictionary<TerrainChunk, HashSet<int>>();

        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                int index = y * width + x;
                if (pixelData[index].a != 0 && !IsVisited(this, index, safeGrounds))
                {
                    ProcessIsland(x, y, affectedChunks, safeGrounds);
                }
            }
        }

        foreach (var c in affectedChunks)
        {
            if (c != this && c.gameObject.activeSelf)
            {
                c.ApplyTexture();
                c.isDirty = true;
            }
        }
    }

    void ProcessIsland(int startX, int startY, HashSet<TerrainChunk> affectedChunks, Dictionary<TerrainChunk, HashSet<int>> safeGrounds)
    {
        Queue<PixelNode> queue = new Queue<PixelNode>();
        List<PixelNode> currentCluster = new List<PixelNode>();
        Dictionary<TerrainChunk, HashSet<int>> localVisited = new Dictionary<TerrainChunk, HashSet<int>>();

        PushNode(this, startX, startY, queue, localVisited, currentCluster);

        int count = 0;
        bool isConnectedToSafeGround = false;

        while (queue.Count > 0)
        {
            PixelNode node = queue.Dequeue();
            count++;

            if (count > maxIslandSize)
            {
                isConnectedToSafeGround = true;
                break;
            }

            if (CheckNeighbor(node.chunk, node.x + 1, node.y, queue, localVisited, safeGrounds, currentCluster) ||
                CheckNeighbor(node.chunk, node.x - 1, node.y, queue, localVisited, safeGrounds, currentCluster) ||
                CheckNeighbor(node.chunk, node.x, node.y + 1, queue, localVisited, safeGrounds, currentCluster) ||
                CheckNeighbor(node.chunk, node.x, node.y - 1, queue, localVisited, safeGrounds, currentCluster))
            {
                isConnectedToSafeGround = true;
                break;
            }
        }

        if (isConnectedToSafeGround)
        {
            foreach (var node in currentCluster)
            {
                if (!safeGrounds.ContainsKey(node.chunk)) safeGrounds.Add(node.chunk, new HashSet<int>());
                safeGrounds[node.chunk].Add(node.y * width + node.x);
            }
        }
        else
        {
            // [파티클 추가 2] 부유섬이 제거될 때
            foreach (var pixel in currentCluster)
            {
                // 삭제될 픽셀의 색상 가져오기 (해당 청크의 pixelData 접근)
                int idx = pixel.y * pixel.chunk.width + pixel.x;
                Color32 color = pixel.chunk.pixelData[idx];

                // 픽셀 삭제
                pixel.chunk.RemovePixel(pixel.x, pixel.y);

                // 파티클 생성
                if (TerrainParticleManager.Instance != null)
                {
                    TerrainParticleManager.Instance.SpawnDebris(pixel.chunk.GetWorldPos(pixel.x, pixel.y), color);
                }

                affectedChunks.Add(pixel.chunk);
            }
        }
    }

    bool CheckNeighbor(TerrainChunk currentChunk, int x, int y, Queue<PixelNode> q,
           Dictionary<TerrainChunk, HashSet<int>> localV,
           Dictionary<TerrainChunk, HashSet<int>> safeV,
           List<PixelNode> cluster)
    {
        if (x >= 0 && x < width && y >= 0 && y < height)
        {
            int index = y * width + x;
            if (currentChunk.pixelData[index].a == 0) return false;
            if (IsVisited(currentChunk, index, safeV)) return true;

            if (!IsVisited(currentChunk, index, localV))
            {
                PushNode(currentChunk, x, y, q, localV, cluster);
            }
        }
        else
        {
            currentChunk.CacheNeighbors();
            TerrainChunk neighbor = null;
            int nx = x, ny = y;

            if (x < 0) { neighbor = currentChunk.leftChunk; nx = width + x; }
            else if (x >= width) { neighbor = currentChunk.rightChunk; nx = x - width; }
            else if (y < 0) { neighbor = currentChunk.bottomChunk; ny = height + y; }
            else if (y >= height) { neighbor = currentChunk.topChunk; ny = y - height; }

            if (neighbor != null && neighbor.gameObject.activeSelf)
            {
                return CheckNeighbor(neighbor, nx, ny, q, localV, safeV, cluster);
            }
        }
        return false;
    }

    bool IsVisited(TerrainChunk c, int index, Dictionary<TerrainChunk, HashSet<int>> v)
    {
        if (!v.ContainsKey(c)) return false;
        return v[c].Contains(index);
    }

    void PushNode(TerrainChunk c, int x, int y, Queue<PixelNode> q, Dictionary<TerrainChunk, HashSet<int>> v, List<PixelNode> cluster)
    {
        int index = y * width + x;
        if (!v.ContainsKey(c)) v.Add(c, new HashSet<int>());
        v[c].Add(index);
        PixelNode newNode = new PixelNode(c, x, y);
        q.Enqueue(newNode);
        cluster.Add(newNode);
    }

    public void RemovePixel(int x, int y)
    {
        if (x >= 0 && x < width && y >= 0 && y < height)
        {
            pixelData[y * width + x] = new Color32(0, 0, 0, 0);
        }
    }

    // ==================================================================================
    //  테두리 및 유틸리티
    // ==================================================================================
    public void UpdateBordersInArea(int minX, int minY, int maxX, int maxY, Vector2 pivotPos,
    float cos = 1f, float sin = 0f, float scaleCorrection = 1f,
    bool isPixelSpace = false)
    {
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

        minX = Mathf.Clamp(minX, 0, width); maxX = Mathf.Clamp(maxX, 0, width);
        minY = Mathf.Clamp(minY, 0, height); maxY = Mathf.Clamp(maxY, 0, height);

        for (int y = minY; y < maxY; y++)
        {
            int yIndex = y * width;
            for (int x = minX; x < maxX; x++)
            {
                int index = yIndex + x;
                if (pixelData[index].a == 0) continue;

                int distToAir = GetDistanceToNearestAir(x, y, texPx);

                if (distToAir <= solidPx)
                {
                    pixelData[index] = solidColor32;
                }
                else if (distToAir <= texPx && isTextureLoaded)
                {
                    float dx = x - pX;
                    float dy = y - pY;
                    float localX = dx * cos - dy * sin;
                    float localY = dx * sin + dy * cos;
                    float angleY = localY;
                    if (localX > 0) angleY *= scaleCorrection;

                    float correctedAngle = Mathf.Atan2(angleY, localX);
                    float normalizedAngle = (correctedAngle + Mathf.PI) / (2 * Mathf.PI);

                    float baseCircumference = texPx * 20f;
                    int u = Mathf.FloorToInt(normalizedAngle * baseCircumference * textureTiling) % borderW;
                    if (u < 0) u += borderW;

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

    public bool IsPixelEmptyLocal(Vector2 localPos)
    {
        float halfW = (width / PPU) * 0.5f;
        float halfH = (height / PPU) * 0.5f;
        int x = Mathf.FloorToInt((localPos.x + halfW) * PPU);
        int y = Mathf.FloorToInt((localPos.y + halfH) * PPU);
        if (x < 0 || x >= width || y < 0 || y >= height) return false;
        return pixelData[y * width + x].a == 0;
    }

    private bool IsTransparent(int x, int y)
    {
        if (x >= 0 && x < width && y >= 0 && y < height)
            return pixelData[y * width + x].a == 0;

        if (x < 0) return leftChunk != null && leftChunk.GetPixelAlpha(width + x, y) == 0;
        if (x >= width) return rightChunk != null && rightChunk.GetPixelAlpha(x - width, y) == 0;
        if (y < 0) return bottomChunk != null && bottomChunk.GetPixelAlpha(x, height + y) == 0;
        if (y >= height) return topChunk != null && topChunk.GetPixelAlpha(x, y - height) == 0;
        return false;
    }

    void UpdateCollider()
    {
        if (polyCollider == null) polyCollider = gameObject.AddComponent<PolygonCollider2D>();
        polyCollider.pathCount = 0;
        var paths = GenerateOutlines();
        polyCollider.pathCount = paths.Count;
        for (int i = 0; i < paths.Count; i++)
        {
            polyCollider.SetPath(i, paths[i]);
        }
    }

    private bool[] cachedVisited;
    private List<Vector2> rawPathPoints = new List<Vector2>(2000);
    private List<Vector2> simplifiedPoints = new List<Vector2>(2000);

    private List<Vector2[]> GenerateOutlines()
    {
        var paths = new List<Vector2[]>();
        if (cachedVisited == null || cachedVisited.Length != width * height)
            cachedVisited = new bool[width * height];
        else
            Array.Clear(cachedVisited, 0, cachedVisited.Length);

        float worldW = width / PPU;
        float worldH = height / PPU;
        float halfW = worldW * 0.5f;
        float halfH = worldH * 0.5f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (pixelData[index].a == 0 || cachedVisited[index]) continue;

                bool isLeftEmpty = (x == 0) || (pixelData[index - 1].a == 0);
                if (isLeftEmpty)
                {
                    rawPathPoints.Clear();
                    int startX = x, startY = y;
                    int curX = x, curY = y;
                    int[] dx = { 0, 1, 1, 1, 0, -1, -1, -1 };
                    int[] dy = { 1, 1, 0, -1, -1, -1, 0, 1 };
                    int enterFrom = 6;
                    int loopCount = 0, maxLoops = width * height;

                    do
                    {
                        cachedVisited[curY * width + curX] = true;
                        float localX = (curX / PPU) - halfW;
                        float localY = (curY / PPU) - halfH;
                        rawPathPoints.Add(new Vector2(localX, localY));

                        int startCheckDir = (enterFrom + 2) % 8;
                        int foundDir = -1;
                        for (int i = 0; i < 8; i++)
                        {
                            int dir = (startCheckDir + i) % 8;
                            int nx = curX + dx[dir];
                            int ny = curY + dy[dir];
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                if (pixelData[ny * width + nx].a != 0)
                                {
                                    curX = nx; curY = ny;
                                    foundDir = dir;
                                    enterFrom = (dir + 4) % 8;
                                    break;
                                }
                            }
                        }
                        if (foundDir == -1) break;
                        loopCount++;
                    }
                    while ((curX != startX || curY != startY) && loopCount < maxLoops);

                    simplifiedPoints.Clear();
                    float tolerance = 0.03f;
                    LineUtility.Simplify(rawPathPoints, tolerance, simplifiedPoints);

                    if (simplifiedPoints.Count > 2)
                    {
                        paths.Add(simplifiedPoints.ToArray());
                    }
                }
            }
        }
        return paths;
    }

    void ApplyTexture()
    {
        texture.SetPixels32(pixelData);
        texture.Apply(false);
    }
}