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

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        polyCollider = GetComponent<PolygonCollider2D>();

        if (textureThickness <= solidThickness + 0.01f) textureThickness = solidThickness + 0.1f;

        Texture2D original = sr.sprite.texture;
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;

        if (original.width == width && original.height == height)
            texture.SetPixels32(original.GetPixels32());
        else
            texture.SetPixels32(new Color32[width * height]);

        texture.Apply();
        pixelData = texture.GetPixels32();
        solidColor32 = (Color32)solidBorderColor;

        if (borderTexture != null && borderTexture.isReadable)
        {
            borderPixels = borderTexture.GetPixels32();
            borderW = borderTexture.width;
            borderH = borderTexture.height;
            isTextureLoaded = true;
        }

        // 초기화
        UpdateBordersInArea(0, 0, width, height, (player != null) ? (Vector2)player.position : Vector2.zero, 1.0f, 0.0f, 1.0f);
        ApplyTexture();

        sr.sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PPU);
        UpdateCollider();
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

    void ApplyTexture()
    {
        texture.SetPixels32(pixelData);
        texture.Apply(false);
    }

    // ==================================================================================
    //  Dig 함수
    // ==================================================================================
    public void Dig(Vector2 mouseWorldPos, float radius)
    {
        if (player == null) return;

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
        Destroy(polyCollider);
        polyCollider = gameObject.AddComponent<PolygonCollider2D>();
    }
}