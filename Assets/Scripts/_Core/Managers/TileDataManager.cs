// @tags: tile, data, manager, singleton, json, terrain, layer
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class TileDataManager : MonoBehaviour
{
    #region Singleton
    public static TileDataManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        { 
            Destroy(gameObject);
            return;
        }
        Instance = this;
        LoadTileData();
    }
    #endregion

    [Header("Settings")]
    [Tooltip("Path to the JSON file relative to the StreamingAssets folder.")]
    public string tileDataJsonPath = "tileData.json";

    /// <summary>tileData.json의 globalMineralDensity. 광물 스폰 밀도 전역 배율.</summary>
    public float GlobalMineralDensity { get; private set; } = 1.0f;

    /// <summary>tileData.json의 logMineralSpawn. 청크별 광물 요청/배치 개수 콘솔 출력(튜닝용).</summary>
    public bool LogMineralSpawn { get; private set; } = false;

    /// <summary>tileData.json의 mineralScatterJitter. 광물 배치 지터(0~1).</summary>
    public float MineralScatterJitter { get; private set; } = 1.0f;

    private Dictionary<TileType, TileDataJson> _tileDataDict = new Dictionary<TileType, TileDataJson>();
    private List<TileDataJson> _sortedTileData = new List<TileDataJson>();

    // 블렌딩 프로필
    private Dictionary<string, BlendingProfileData> _blendingProfiles = new Dictionary<string, BlendingProfileData>();
    private BlendingProfileData _defaultBlendingProfile;

    // [Step 2] Resource Caching
    private Dictionary<TileType, Color32[]> _groundPixelCache = new Dictionary<TileType, Color32[]>();
    private Dictionary<TileType, Color32[]> _borderPixelCache = new Dictionary<TileType, Color32[]>();
    private Dictionary<TileType, Vector2Int> _borderDimensionsCache = new Dictionary<TileType, Vector2Int>();
    
    public int sourceWidth { get; private set; }
    public int sourceHeight { get; private set; }
    private bool _isResourcesInitialized = false;
    
    // [New] Terrain width boundary (horizontal chunk limit)
    public int terrainWidth { get; private set; } = 50;
    
    // [New] Terrain depth boundary (vertical chunk limit)
    public int terrainDepth { get; private set; } = 36;

    /// <summary>
    /// 엘리베이터 정류장 깊이 배치 설정(tileData.json의 elevatorConfig).
    /// 로드 전이거나 JSON에 항목이 없으면 null — 소비처(ElevatorLayerCatalog)가 기본값으로 폴백한다.
    /// </summary>
    public ElevatorConfig ElevatorSettings { get; private set; }

    // [Step 2] Initialize Resources
    public void InitializeResources(List<TileVisualSettings.TileVisualData> visualSettings, int fallbackWidth, int fallbackHeight)
    {
        if (_isResourcesInitialized) return;
        
        // Determine source dimensions
        if (visualSettings != null && visualSettings.Count > 0 && visualSettings[0].mainTexture != null)
        {
            sourceWidth = visualSettings[0].mainTexture.width;
            sourceHeight = visualSettings[0].mainTexture.height;
            //Debug.Log($"[TileDataManager] Using texture dimensions: {sourceWidth}x{sourceHeight}");
        }
        else
        {
            sourceWidth = fallbackWidth;
            sourceHeight = fallbackHeight;
            Debug.LogWarning($"[TileDataManager] Using fallback dimensions: {sourceWidth}x{sourceHeight}");
        }

        CacheTextures(visualSettings);
        _isResourcesInitialized = true;
    }

    private void CacheTextures(List<TileVisualSettings.TileVisualData> visualSettings)
    {
        if (visualSettings == null) return;

        foreach (var visual in visualSettings)
        {
            if (visual.mainTexture == null) continue;

            if (!visual.mainTexture.isReadable)
            {
                Debug.LogError($"[TileDataManager] Texture '{visual.mainTexture.name}' is not Read/Write Enabled!");
                continue;
            }

            // Cache ground pixels
            if (visual.mainTexture.width == sourceWidth && visual.mainTexture.height == sourceHeight)
            {
                _groundPixelCache[visual.tileType] = visual.mainTexture.GetPixels32();
            }
            else
            {
                Debug.LogError($"Texture size mismatch for {visual.tileType}. Expected {sourceWidth}x{sourceHeight}");
            }

            // Cache border pixels
            if (visual.borderTexture != null)
            {
                if (visual.borderTexture.isReadable)
                {
                    _borderPixelCache[visual.tileType] = visual.borderTexture.GetPixels32();
                    Vector2Int dims = new Vector2Int(visual.borderTexture.width, visual.borderTexture.height);
                    _borderDimensionsCache[visual.tileType] = dims;
                    //Debug.Log($"[BORDER_DIM_DEBUG] Cached border dimensions for {visual.tileType}: {dims.x}x{dims.y}");
                }
                else
                {
                    Debug.LogError($"[TileDataManager] Border Texture '{visual.borderTexture.name}' is not Read/Write Enabled!");
                }
            }
        }
        //Debug.Log("[TileDataManager] Texture Caching Complete.");
    }

    public Color32[] GetGroundPixels(TileType type)
    {
        if (_groundPixelCache.ContainsKey(type)) return _groundPixelCache[type];
        return null;
    }

    public Color32[] GetBorderPixels(TileType type)
    {
        if (_borderPixelCache.ContainsKey(type)) return _borderPixelCache[type];
        return null;
    }

    public Vector2Int GetBorderDimensions(TileType type)
    {
        if (_borderDimensionsCache.ContainsKey(type))
        {
            Vector2Int dims = _borderDimensionsCache[type];
            //Debug.Log($"[BORDER_DIM_DEBUG] GetBorderDimensions({type}): {dims.x}x{dims.y}");
            return dims;
        }
        Debug.LogWarning($"[BORDER_DIM_DEBUG] GetBorderDimensions({type}): NOT FOUND in cache! Returning Vector2Int.zero");
        return Vector2Int.zero;
    }

    private void LoadTileData()
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, tileDataJsonPath);

        if (!File.Exists(filePath))
        {
            Debug.LogError($"Tile data file not found at: {filePath}");
            return;
        }

        try
        {
            string dataAsJson = File.ReadAllText(filePath);
            TileDatabaseJson database = JsonUtility.FromJson<TileDatabaseJson>(dataAsJson);

            // [New] Load terrain width
            terrainWidth = database.terrainWidth;
            //Debug.Log($"[TileDataManager] Terrain Width: {terrainWidth} chunks (X range: -{terrainWidth} to +{terrainWidth})");
            
            // [New] Load terrain depth
            terrainDepth = database.terrainDepth;

            // 엘리베이터 정류장 깊이 배치. 없으면 null로 두고 카탈로그가 기본값을 쓴다.
            ElevatorSettings = database.elevatorConfig;
            //Debug.Log($"[TileDataManager] Terrain Depth: {terrainDepth} chunks (Y range: 0 to -{terrainDepth})");

            // 광물 스폰 전역 밀도 배율. 음수·NaN 방지.
            // 0은 "광물 전부 끄기"로 유효하므로 허용한다.
            GlobalMineralDensity = (database.globalMineralDensity >= 0f) ? database.globalMineralDensity : 1.0f;
            LogMineralSpawn = database.logMineralSpawn;
            MineralScatterJitter = Mathf.Clamp01(database.mineralScatterJitter);

            // 블렌딩 프로필 로드
            _blendingProfiles.Clear();
            if (database.blendingProfiles != null)
            {
                foreach (var profile in database.blendingProfiles)
                    _blendingProfiles[profile.name] = profile;
            }
            _defaultBlendingProfile =
                (!string.IsNullOrEmpty(database.defaultBlendingProfileName) &&
                 _blendingProfiles.TryGetValue(database.defaultBlendingProfileName, out var def))
                ? def
                : BuiltinDefaultProfile();

            _tileDataDict.Clear();
            _sortedTileData.Clear();
            foreach (var tileData in database.tiles)
            {
                if (Enum.TryParse(tileData.tileType, true, out TileType typeEnum))
                {
                    if (!_tileDataDict.ContainsKey(typeEnum))
                    {
                        _tileDataDict.Add(typeEnum, tileData);
                        _sortedTileData.Add(tileData);
                    }
                    else
                    {
                        Debug.LogWarning($"Duplicate TileType found in JSON data: {tileData.tileType}");
                    }
                }
                else
                {
                    Debug.LogWarning($"Failed to parse TileType from JSON: {tileData.tileType}");
                }
            }
            
            // Sort by startDepth ASCENDING (Deepest first in coordinate system: -1000, -500, 0)
            _sortedTileData.Sort((a, b) => a.startDepth.CompareTo(b.startDepth));

            ValidateMineralRules(database.tiles);
            
            // Debug.Log($"[TileDataManager] Successfully loaded {_sortedTileData.Count} tiles.");
            // foreach(var t in _sortedTileData)
            // {
            //     Debug.Log($"[TileDataManager] Loaded Tile: {t.tileType}, Depth: {t.startDepth}");
            // }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading tile data: {e.Message}");
        }
    }

    /// <summary>
    /// 광물 규칙의 필수 필드 누락을 로드 시 1회 리포트한다.
    /// 런타임에 매 청크마다 경고를 뿜으면 로그가 범람하므로 여기서만 검사한다.
    /// 누락된 rule은 스폰 0개로 조용히 넘어가기 때문에 이 로그가 유일한 단서다.
    /// 배경: Assets/Docs/mineral-density-redesign.md §6-3
    /// </summary>
    private static void ValidateMineralRules(List<TileDataJson> tiles)
    {
        if (tiles == null) return;

        var problems = new List<string>();
        foreach (var tile in tiles)
        {
            if (tile?.minerals == null) continue;
            foreach (var rule in tile.minerals)
            {
                if (rule == null) { problems.Add($"{tile.tileType}: null 규칙 항목"); continue; }

                if (!MineralDensity.HasValidRange(rule.perChunk))
                    problems.Add($"{tile.tileType}/{rule.mineralType}: perChunk 누락 또는 길이<2 → 청크 스폰 0개");

                if (rule.rockDropCount == null || rule.rockDropCount.Length < 2)
                    problems.Add($"{tile.tileType}/{rule.mineralType}: rockDropCount 누락 → 돌 드랍 개수가 폴백값(1)으로 처리됨");
            }
        }

        if (problems.Count > 0)
            Debug.LogWarning($"[TileDataManager] 광물 규칙 {problems.Count}건 확인 필요:\n - " + string.Join("\n - ", problems));
    }

    public TileDataJson GetData(TileType tileType)
    {
        _tileDataDict.TryGetValue(tileType, out TileDataJson data);
        return data;
    }

    /// <summary>
    /// 층별 절차적 굴 설정. 층 데이터가 없거나 caveEnabled=false 면 None(비활성)을 돌려준다.
    /// seedSalt 로 TileType ID 를 넣어 층마다 다른 노이즈 도메인을 쓰게 한다.
    /// </summary>
    public CaveCarveSettings GetCaveSettings(TileType tileType)
    {
        var data = GetData(tileType);
        if (data == null || !data.caveEnabled) return CaveCarveSettings.None;

        return new CaveCarveSettings
        {
            enabled         = true,
            scale           = data.caveScale,
            threshold       = data.caveThreshold,
            detail          = data.caveDetail,
            regionScale     = data.caveRegionScale,
            regionThreshold = data.caveRegionThreshold,
            regionBand      = data.caveRegionBand,
            regionFalloff   = data.caveRegionFalloff,
            edgeMarginPx    = data.caveEdgeMarginPx,
            keepLargestOnly = data.caveKeepLargestOnly,
            rockCount       = data.caveRockCount,

            useBlob         = data.caveUseBlob,
            blobChance      = data.caveBlobChance,
            blobLengthMinPx = data.caveBlobLengthMinPx,
            blobLengthMaxPx = data.caveBlobLengthMaxPx,
            blobRadiusMinPx = data.caveBlobRadiusMinPx,
            blobRadiusMaxPx = data.caveBlobRadiusMaxPx,
            linkChance      = data.caveLinkChance,
            linkRadiusFactor = data.caveLinkRadiusFactor,
            maxLinkedChunks = data.caveMaxLinkedChunks,
            blobWaveAmpMinPx = data.caveBlobWaveAmpMinPx,
            blobWaveAmpMaxPx = data.caveBlobWaveAmpMaxPx,
            blobWaveLenMinPx = data.caveBlobWaveLenMinPx,
            blobWaveLenMaxPx = data.caveBlobWaveLenMaxPx,
            blobMinAspect    = data.caveBlobMinAspect,
            blobWaveMaxSlope = data.caveBlobWaveMaxSlope,
            blobTiltMaxDeg   = data.caveBlobTiltMaxDeg,
            blobBulgeAmp     = data.caveBlobBulgeAmp,
            blobBulgeLenPx   = data.caveBlobBulgeLenPx,
            blobWallAmpPx   = data.caveBlobWallAmpPx,
            blobWallScale   = data.caveBlobWallScale,
            blobEndRoomGain  = data.caveBlobEndRoomGain,
            blobEndRoomLenPx = data.caveBlobEndRoomLenPx,
            hiddenUntilExposed = data.caveHiddenUntilExposed,
            aspect          = data.caveAspect,
            minRadiusPx     = data.caveMinRadiusPx,
            roughness       = data.caveRoughness,
            roughFreq1      = data.caveRoughFreq1,
            roughFreq2      = data.caveRoughFreq2,
            seedSalt        = (int)tileType,
        };
    }

    /// <summary>
    /// 층 데이터를 얕은→깊은 순서로 반환한다 (Dirt → ... → 최하층).
    /// 내부 _sortedTileData는 startDepth 오름차순(깊은→얕은)이므로 역순으로 복사해 돌려준다.
    /// 도깨비 가마솥 승급 사다리(MineralUpgradeLadder) 구축에 사용.
    /// </summary>
    public List<TileDataJson> GetTilesShallowToDeep()
    {
        var list = new List<TileDataJson>(_sortedTileData);
        list.Reverse();
        return list;
    }

    public TileType GetTileTypeAtDepth(int y)
    {
        // Debug.Log($"Checking Depth: {y}"); 
        
        foreach (var tile in _sortedTileData)
        {
            // Debug.Log($"[TileDataManager] Comparing: Is {tile.startDepth} >= {y}? ({tile.startDepth >= y}) for {tile.tileType}");
            if (tile.startDepth >= y)
            {
                if (Enum.TryParse(tile.tileType, true, out TileType typeEnum))
                {
                    // Debug.Log($"[TileDataManager] Match Found! Depth {y} -> {typeEnum} (StartDepth: {tile.startDepth})");
                    return typeEnum;
                }
            }
        }

        // Debug.Log($"[TileDataManager] No match found for depth {y}, returning default Dirt");
        return TileType.Dirt; 
    }

    // ============================================================================================================
    //  블렌딩 프로필 API
    // ============================================================================================================

    private static BlendingProfileData BuiltinDefaultProfile() => new BlendingProfileData
    {
        name             = "__builtin_default",
        blendHeightRatio = 0.4f
    };

    /// <summary>이름으로 블렌딩 프로필 반환. 없으면 전역 기본값.</summary>
    public BlendingProfileData GetBlendingProfile(string name)
    {
        if (string.IsNullOrEmpty(name)) return _defaultBlendingProfile;
        return _blendingProfiles.TryGetValue(name, out var p) ? p : _defaultBlendingProfile;
    }

    /// <summary>층 타입의 하단 경계에 적용할 블렌딩 프로필 반환.</summary>
    public BlendingProfileData GetBlendingProfileForLayer(TileType tileType)
    {
        var data = GetData(tileType);
        return GetBlendingProfile(data?.blendingProfileName);
    }

    /// <summary>
    /// 파기 지점 worldY 기반으로 스태미나 감소값을 반환한다.
    /// 블렌딩 구역 안이면 두 층 maxStaminaReduction 을 선형 보간한다.
    /// </summary>
    /// <param name="worldY">파기 지점의 월드 Y (유닛)</param>
    /// <param name="chunkHeightWorld">청크 높이 (유닛)</param>
    public float GetStaminaReductionAtWorldY(float worldY, float chunkHeightWorld)
    {
        int   chunkY = Mathf.FloorToInt(worldY / chunkHeightWorld);
        float subY   = (worldY - chunkY * chunkHeightWorld) / chunkHeightWorld; // 0=하단, 1=상단

        TileType currentType = GetTileTypeAtDepth(chunkY);
        TileType typeBelow   = GetTileTypeAtDepth(chunkY - 1);

        // 같은 층이면 일반 조회
        if (currentType == typeBelow)
            return GetData(currentType)?.maxStaminaReduction ?? 0f;

        // 블렌딩 구역 판정: 청크 하단 blendHeightRatio 이내
        BlendingProfileData profile = GetBlendingProfileForLayer(currentType);
        if (subY < profile.blendHeightRatio)
        {
            // t=0: 하단 끝(아래 층 저항), t=1: 블렌딩 구역 상단(현재 층 저항)
            float t            = subY / profile.blendHeightRatio;
            float staminaAbove = GetData(currentType)?.maxStaminaReduction ?? 0f;
            float staminaBelow = GetData(typeBelow)?.maxStaminaReduction   ?? 0f;
            return Mathf.Lerp(staminaBelow, staminaAbove, t);
        }

        return GetData(currentType)?.maxStaminaReduction ?? 0f;
    }

    // ============================================================================================================

    /// <summary>
    /// X, Y 좌표를 고려하여 타일 타입 결정 (수평 경계, 노이즈 없음)
    /// </summary>
    public TileType GetTileTypeAtPosition(int xChunk, int yChunk)
    {
        return GetTileTypeAtDepth(yChunk);
    }
}
