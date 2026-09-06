// @tags: map, minimap, worldmap, marker, sprite, icon, elevator, entrance
using UnityEngine;

/// <summary>
/// 지도 마커 아이콘 스프라이트와 팔레트를 코드로 생성해 캐시하는 정적 팩토리.
/// 미니맵(UndergroundMinimap)과 전체지도(WorldMapOverlay)가 공유한다.
/// 스프라이트는 HideAndDontSave로 앱 수명 동안 유지된다(씬 로드에도 파괴되지 않음).
/// </summary>
public static class MapMarkerVisuals
{
    // ── 팔레트 ──
    /// <summary>안 파진 일반 돌을 지도에 채울 색(터널 앰버·지형 암석과 구분되는 톤).</summary>
    public static readonly Color32 RockFill        = new Color32(176, 120, 74, 255);
    /// <summary>광물돌을 지도에 채울 색(청록 발광 톤).</summary>
    public static readonly Color32 MineralRockFill = new Color32(120, 205, 210, 255);
    /// <summary>엘리베이터 마커 색.</summary>
    public static readonly Color   ElevatorColor   = new Color(0.36f, 0.82f, 0.95f, 1f);
    /// <summary>청크 입구 마커 색.</summary>
    public static readonly Color   EntranceColor   = new Color(0.80f, 0.52f, 0.96f, 1f);
    /// <summary>이미 탐험해 재입장 불가한 청크 입구 마커 색(회색).</summary>
    public static readonly Color   EntranceUsedColor = new Color(0.52f, 0.54f, 0.60f, 1f);

    private static Sprite _elevator;
    private static Sprite _entrance;
    private static Sprite _entranceUsed;

    /// <summary>엘리베이터 마커(원형 토큰).</summary>
    public static Sprite Elevator
    {
        get { if (_elevator == null) _elevator = BuildCircle(40, ElevatorColor); return _elevator; }
    }

    /// <summary>청크 입구 마커(위로 향한 삼각 토큰).</summary>
    public static Sprite Entrance
    {
        get { if (_entrance == null) _entrance = BuildTriangle(40, EntranceColor); return _entrance; }
    }

    /// <summary>이미 탐험한(재입장 불가) 청크 입구 마커(회색 삼각 토큰).</summary>
    public static Sprite EntranceUsed
    {
        get { if (_entranceUsed == null) _entranceUsed = BuildTriangle(40, EntranceUsedColor); return _entranceUsed; }
    }

    /// <summary>종류에 맞는 마커 스프라이트.</summary>
    public static Sprite ForKind(MapMarkerKind kind)
        => kind == MapMarkerKind.Elevator ? Elevator : Entrance;

    // ── 스프라이트 빌더 ──
    private static readonly Color32 Outline = new Color32(12, 14, 20, 255);

    private static Sprite BuildCircle(int size, Color fill)
    {
        var px = new Color32[size * size];
        float c = size * 0.5f - 0.5f;
        float rFill = size * 0.30f;   // 밝은 코어 반경
        float rEdge = size * 0.40f;   // 외곽선 바깥 반경
        Color32 hi = Color.Lerp(fill, Color.white, 0.55f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                px[y * size + x] = Compose(r - rEdge, r - rFill, fill, hi, (y - c) / size);
            }
        return ToSprite(size, px);
    }

    private static Sprite BuildTriangle(int size, Color fill)
    {
        var px = new Color32[size * size];
        float c = size * 0.5f - 0.5f;
        // 위로 향한 정삼각형 꼭짓점 (약간 안쪽으로).
        Vector2 top = new Vector2(c, c + size * 0.34f);
        Vector2 bl  = new Vector2(c - size * 0.36f, c - size * 0.28f);
        Vector2 br  = new Vector2(c + size * 0.36f, c - size * 0.28f);
        Color32 hi = Color.Lerp(fill, Color.white, 0.55f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x, y);
                // 각 변에 대한 부호 거리(내부가 양수가 되도록 정렬). 내부 거리 = 세 변 중 최소.
                float d0 = EdgeDist(p, top, br);
                float d1 = EdgeDist(p, br, bl);
                float d2 = EdgeDist(p, bl, top);
                float inside = Mathf.Min(d0, Mathf.Min(d1, d2)); // 내부면 양수, 클수록 깊숙이
                float sdf = -inside;                             // 표준 SDF(내부 음수)
                px[y * size + x] = Compose(sdf, sdf + size * 0.11f, fill, hi, (y - c) / size);
            }
        return ToSprite(size, px);
    }

    /// <summary>삼각형 변(a→b) 왼쪽이 양수인 부호 거리.</summary>
    private static float EdgeDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len = ab.magnitude;
        if (len < 1e-4f) return 0f;
        // (b-a) x (p-a) / |b-a|
        return ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) / len;
    }

    /// <summary>
    /// SDF 값으로 외곽선+채움+상단 하이라이트를 합성한다.
    /// outerSdf ≤ 0 이면 도형 안(외곽선 포함), fillSdf ≤ 0 이면 밝은 채움.
    /// </summary>
    private static Color32 Compose(float outerSdf, float fillSdf, Color fill, Color hi, float topT)
    {
        float alpha = Mathf.Clamp01(0.5f - outerSdf / 1.5f); // 도형 커버리지(AA)
        if (alpha <= 0f) return new Color32(0, 0, 0, 0);
        float innerT = Mathf.Clamp01(0.5f - fillSdf / 1.5f); // 1=채움, 0=외곽선
        Color body = Color.Lerp((Color)Outline, fill, innerT);
        body = Color.Lerp(body, hi, innerT * Mathf.Clamp01(topT + 0.35f) * 0.5f); // 위쪽 살짝 밝게
        return new Color32(
            (byte)(Mathf.Clamp01(body.r) * 255f),
            (byte)(Mathf.Clamp01(body.g) * 255f),
            (byte)(Mathf.Clamp01(body.b) * 255f),
            (byte)(alpha * 255f));
    }

    private static Sprite ToSprite(int size, Color32[] px)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        tex.SetPixels32(px);
        tex.Apply(false);
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
