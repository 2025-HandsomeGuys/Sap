// @tags: settings, data-container, dto, json, world, chunk, rendering, performance
/// <summary>
/// worldSettings.json 의 데이터 모델.
/// JsonUtility.FromJson 으로 역직렬화된다.
/// </summary>
[System.Serializable]
public class WorldSettingsData
{
    public WorldSection       world       = new WorldSection();
    public SaveSection        save        = new SaveSection();
    public RenderingSection   rendering   = new RenderingSection();
    public PerformanceSection performance = new PerformanceSection();
    public ChunkSection       chunk       = new ChunkSection();

    [System.Serializable]
    public class WorldSection
    {
        public int seed         = 0;
        public int viewDistance = 1;
    }

    [System.Serializable]
    public class SaveSection
    {
        public bool enableMemoryCache = true;
        public bool enableDiskSave    = false;
    }

    [System.Serializable]
    public class RenderingSection
    {
        public bool  enableLayerBlending  = true;
        public float globalLightFalloff   = 30f;
        public float textureUpdateInterval= 0f;

        /// <summary>
        /// [A/B 안전장치] Round 1에서도 Upsample + Visual 을 돌려 텍스처를 조기 프리뷰한다.
        /// 기본 false — 프리뷰는 Round 2가 즉시 덮어쓰므로 낭비다(설계 §3.4).
        /// 파기 반응성이 이상하게 느껴지면 true 로 켜서 A/B 비교하고, 검증 후 이 필드를 제거한다.
        /// </summary>
        public bool  enableRound1Preview  = false;
    }

    [System.Serializable]
    public class PerformanceSection
    {
        public int chunkSpawningBatchSize = 4;
        public int maxTimePerBatchMs      = 6;
        public int maxTimePerFramePh2Ms   = 5;
    }

    [System.Serializable]
    public class ChunkSection
    {
        public float verticalScale         = 1.5f;
        public bool  useIslandRemoval      = true;

        /// <summary>공중 섬으로 판정할 최대 픽셀 수(면적). 이 값보다 큰 덩어리는 '땅'으로 간주해 남긴다.
        /// 100 PPU 기준 10000 = 1×1 유닛, 40000 = 2×2, 90000 = 3×3. 올릴수록 더 큰 공중 섬까지 무너진다.</summary>
        public int   maxIslandSize         = 90000;
        public float solidThickness        = 1.6f;
        public float textureThickness      = 4.0f;
        public float colliderUpdateInterval= 0.2f;
        // RDP 콜라이더 단순화 허용오차(월드 좌표, PPU 의존). 클수록 정점·물리 프록시 감소.
        public float colliderSimplifyTolerance = 0.005f;

        /// <summary>공기·파괴불가 픽셀과 맞닿는 최외곽 rim 라인 두께(px). 0 이면 비활성.
        /// 설계: Assets/Docs/terrain-rim-outline.md</summary>
        public int rimThicknessPx = 2;

        /// <summary>rim 색(hex).
        /// ⚠ JsonUtility 는 hex 문자열을 Color32 로 역직렬화하지 못하므로 반드시 string 이어야 한다.
        /// 파싱은 InfinityMapManager.ApplyWorldSettings 에서 ColorUtility.TryParseHtmlString 으로 한다.</summary>
        public string rimColor = "#241009";
    }
}
