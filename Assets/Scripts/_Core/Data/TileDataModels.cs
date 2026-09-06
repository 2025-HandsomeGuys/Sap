// @tags: tile, data-container, dto, json, terrain, blending, generation
using System.Collections.Generic;

// This file contains the C# class representations of our JSON data structure.
// These classes are used by JsonUtility to deserialize the JSON file.

[System.Serializable]
public class BlendingProfileData
{
    public string name;              // 프로필 식별자 (tileData.json 내 고유)
    public float blendHeightRatio;   // 블렌딩 구역 높이 비율 (0~1). 예: 0.4 = 청크 하단 40%
}

/// <summary>
/// 층별 절차적 굴 파라미터. tileData.json 의 tiles[].cave* 필드에서 만들어져
/// TerrainJobs.CarveCaveConnectedJob 으로 그대로 넘어간다.
///
/// 튜닝 손잡이:
///   regionThreshold — 굴이 얼마나 자주 나오나. 올리면 굴 지대가 줄어 지형이 통짜에 가까워진다.
///   threshold       — 통로의 두께. 낮추면 두꺼워진다.
///   scale           — 굴 하나의 크기(굽이의 반경). 낮추면 굴이 커진다.
///   aspect          — 굴이 눕는 정도. 1이면 등방(수직 굴이 절반이라 '갈라진 틈'처럼 보인다).
/// [blob 모드 손잡이] caveUseBlob = true 면 아래 값만 쓴다(scale/threshold/region* 는 무시).
///   blobChance      — 이 청크에 굴이 있을 확률(0~1). 굴의 빈도는 이 값 하나로 정해진다.
///   blobRadius*Px   — 통로 반두께 범위. 실제 보이는 폭 = 2*(반두께 - caveMinRadiusPx).
///   linkChance      — 좌우 이웃 청크와 이어질 확률. 0 이면 청크마다 완전히 고립된 굴.
///   maxLinkedChunks — 한 줄로 이어질 수 있는 청크 수 상한(3 이면 최대 3칸짜리 굴).
///   linkRadiusFactor— 연결 통로 반두께 = 양쪽 굴 중 얇은 쪽 × 이 값. 1 에 가까울수록
///                     통로가 굴과 같은 굵기라 여러 청크가 '하나의 긴 굴'로 읽힌다.
///   blobLength*Px   — 가로 길이 범위. 청크에 안 들어가면 자동으로 줄여 맞춘다.
///   blobWaveAmp*/blobWaveLen*  — 중심선 흔들림 범위. 파장이 짧으면 지그재그, 길면 완만한 곡선.
///   blobMinAspect   — 최소 종횡비(반길이 / 반두께). 청크에 안 들어가 길이가 깎이면 길이를
///                     포기하는 대신 반경을 줄여 길쭉함을 지킨다. 0 이면 보정 없음.
///   blobWaveMaxSlope— 중심선 최대 기울기(2π·amp/λ). 진폭을 파장에 맞춰 깎아 급격한 꺾임을 막는다.
///                     0.5 ≈ 27°, 1.0 ≈ 45°. 0 이면 상한 없음(진폭·파장이 독립 = 지그재그 가능).
///   blobTiltMaxDeg  — 굴 축 기울기 상한(±도). 0 이면 전부 수평.
///   blobBulgeAmp/blobBulgeLenPx — 길이 방향 두께 변조. 넓은 방과 좁은 목을 만든다.
///                     얇아져도 minRadiusPx+35 는 보장하므로 못 지나가는 목은 안 생긴다.
///   blobWallAmpPx/blobWallScale — 벽 요철. 0 이면 매끈한 캡슐.
///
/// [noise 모드 손잡이]
///   keepLargestOnly — 청크 안에서 가장 큰 굴 하나만 남긴다. 외톨이 구멍이 사라진다.
///                     edgeMarginPx(청크 가둠)와 세트다 — 가두지 않으면 이웃으로 이어지는 통로를
///                     외톨이로 오판해 지운다.
///   edgeMarginPx    — 청크 가장자리에서 굴을 닫는 폭(px). 굴이 청크를 넘어가지 못하게 한다.
///                     0 이면 가둠 없음. 청크는 1000×1000px 이다.
///   regionBand      — 굴 끝이 닫히는 길이. 0 이면 지대 경계에서 직선으로 뚝 잘린다.
///   regionFalloff   — 경계에서 얼마나 조이나. (1 - threshold) 이상이면 완전히 닫힌다.
///   minRadiusPx     — 이보다 얇은 조각을 지운다. threshold 와 반드시 함께 조정할 것.
///                     regionBand 로 조여 얇아진 끝자락도 이 침식이 지운다 — 0 으로 끄면 실금이 남는다.
///   roughness       — 벽 요철. 0이면 매끈한 곡선이라 동굴이 아니라 덩어리로 읽힌다.
/// detail 은 굴 벽의 울퉁불퉁함이라 웬만하면 안 건드려도 된다.
///
/// ⚠ 세 값은 서로 물려 있다. 튜닝하다 두 번 헛짚은 기록을 남겨 둔다:
///
///   1. minRadiusPx 를 12~16 으로 올렸더니 굴이 통째로 파편이 됐다. ridged 튜브는 굽이에서
///      얇아지는데 침식이 그 목부터 끊어 긴 굴을 조각으로 쪼갠다. 자잘한 구멍을 지우겠다고
///      침식을 키우면 안 된다 — 8px 근처가 상한이다. 조각의 진짜 원인은 지대 경계가 튜브를
///      토막내는 것이라, regionScale 을 낮춰 지대를 키우는 쪽이 해법이다.
///
///   2. aspect 를 2.0 으로 올려 굴을 눕혔더니 세로 두께가 절반(31px)이 돼 플레이어가 못
///      지나가게 됐다. aspect 를 올리면 threshold 를 같이 낮춰 두께를 되찾아야 한다.
///
/// 기본값은 3×2 청크 렌더를 눈으로 보고 고른 값이다(공기 4~7%, 통로 두께 중앙 60px 안팎).
/// 플레이어 캡슐이 20×50px 이므로 통로 두께가 50px 아래로 내려가면 걸어서 못 지나간다.
/// </summary>
public struct CaveCarveSettings
{
    public bool  enabled;
    public float scale;
    public float threshold;
    public float detail;
    public float regionScale;
    public float regionThreshold;
    public float regionBand;
    public float regionFalloff;
    public float edgeMarginPx;
    public bool  keepLargestOnly;

    /// <summary>굴 벽·바닥·천장에 따로 깔아 줄 바위 수. 0 이면 일반 배치에만 맡긴다.</summary>
    public int   rockCount;

    // ─── [blob] 캡슐 한 개 모드 ───────────────────────────────────────────────
    public bool  useBlob;
    public float blobChance;
    public float blobLengthMinPx;
    public float blobLengthMaxPx;
    public float blobRadiusMinPx;
    public float blobRadiusMaxPx;
    public float linkChance;
    public float linkRadiusFactor;
    public int   maxLinkedChunks;
    public float blobWaveAmpMinPx;
    public float blobWaveAmpMaxPx;
    public float blobWaveLenMinPx;
    public float blobWaveLenMaxPx;
    public float blobMinAspect;
    public float blobWaveMaxSlope;
    public float blobTiltMaxDeg;
    public float blobBulgeAmp;
    public float blobBulgeLenPx;
    public float blobWallAmpPx;
    public float blobWallScale;
    public float blobEndRoomGain;
    public float blobEndRoomLenPx;
    public float aspect;
    public float minRadiusPx;
    public float roughness;
    public float roughFreq1;
    public float roughFreq2;

    /// <summary>층마다 다른 노이즈 도메인을 쓰기 위한 소금값. 층 ID를 넣는다.</summary>
    public int   seedSalt;

    /// <summary>
    /// 좌/우 이웃 청크가 **실제로 굴을 파는 청크인지**. false 면 그쪽으로 연결 통로를 안 뻗는다.
    ///
    /// 특수청크·서브청크·링크피스(던전문 등)는 프리팹 픽셀을 그대로 쓰므로 굴을 안 판다.
    /// 엘리베이터 정류장도 ResolveCaveSettings 가 굴을 끈다. 이걸 모르고 통로를 뻗으면
    /// 받아줄 쪽이 없어 굴이 청크 경계에서 **수직 벽으로 잘린다**(실제로 (3,-3)→(4,-3) DungeonDoor
    /// 에서 발생). LinkAt 의 캡슐 존재 검사는 좌표 기하만 보므로 이 경우를 못 걸러낸다.
    ///
    /// 대칭성: 두 청크가 각자 상대를 조회하므로 경계 하나에 대해 양쪽 판정이 일치한다.
    /// </summary>
    public bool  leftNeighborCarves;
    public bool  rightNeighborCarves;

    /// <summary>
    /// true 면 생성 시엔 굴을 안 판다. 플레이어가 파다가 굴에 닿았을 때 그때 통째로 연다
    /// (돌·특수청크 공동과 같은 방식). 굴은 좌표의 순수 함수라 마스크를 들고 있을 필요 없이
    /// 필요할 때 다시 계산하면 된다 — 그래서 청크당 1MB 짜리 마스크 보관이 없다.
    /// </summary>
    public bool  hiddenUntilExposed;

    public static CaveCarveSettings None => default;   // enabled = false
}

[System.Serializable]
public class TileDataJson
{
    public string tileType; // Using string for enum name to be human-readable in JSON
    public float maxStaminaReduction;
    public int startDepth; // This tile type appears starting from this depth (Y coordinate)

    // [New] Wall Climbing Properties
    public float wallClimbSpeedRatio = 1.0f; // 벽타기 속도 배율 (기본 1배)
    public float wallSlipForce = 0f;         // 벽에서 미끄러지는 힘 (0이면 안 미끄러짐)

    // [New] Mining Properties
    public int tier = 0;          // 요구 채굴 티어 (0: 기본)
    public float hardness = 0f;   // 타일 경도 (MiningPower로 극복해야 함. 0이면 무조건 채굴 가능)

    // [New] 이 층에서 삽 파기 반경에 곱해지는 계수. 1.0 = 감쇠 없음(1지층).
    // 얼어붙은/달궈진 땅이 도구를 방해한다는 표현이며, 플레이어 스탯을 깎는 게 아니라
    // '지형이 거는 감쇠'다 — 그래서 스탯창에도 원래 값과 감쇠 후 값을 나란히 보여준다.
    //
    // 되돌리는 건 범위 업그레이드(MiningRangeUp)뿐이다 — 스탯이 커져서 감쇠를 이겨낸다.
    // (EnvironmentResistance가 이 계수를 상쇄하던 규칙은 2026-08-24에 없앴다.)
    //
    // ⚠ 반경은 파기 비용에도 그대로 곱해지는데 판 양은 면적(반경²)이라,
    //    이 값을 내리면 '면적당' 스태미나 비용은 1/계수 배로 나빠진다.
    //    계수 0.8 = 면적당 비용 1.25배. maxStaminaReduction을 같이 올리면 이중으로 걸린다.
    public float digRadiusModifier = 1.0f;

    // [New] 이 층의 광물 스폰 밀도 배율. 1.0 = 기준. 층 간 밀도 편차 조정용.
    // 배경: Assets/Docs/mineral-density-redesign.md §2-3
    public float mineralDensity = 1.0f;

    // ─── 절차적 굴(cave) ──────────────────────────────────────────────
    // 생성 시점에 지형에 뚫리는 구멍. 시드에서 결정적으로 재생성되므로 세이브에 안 남는다.
    // 값을 바꾸면 다음 청크 로드부터 바로 반영된다(리컴파일 불필요).
    //
    // caveRegionThreshold = 굴의 빈도, caveThreshold = 통로 두께, caveScale = 굴 하나의 크기,
    // caveRegionBand/caveRegionFalloff = 굴 끝이 둥글게 닫히는 정도,
    // caveEdgeMarginPx = 굴을 청크 안에 가두는 여백(청크 경계 직선 컷 방지),
    // caveKeepLargestOnly = 청크당 굴 하나만 남김(외톨이 구멍 제거),
    // caveAspect = 굴이 눕는 정도, caveMinRadiusPx = 이보다 얇은 조각 제거,
    // caveRoughness = 벽 요철(0이면 매끈한 곡선이라 동굴로 안 보인다).
    // 자세한 설명과 주의점은 CaveCarveSettings 주석 참고.
    public bool  caveEnabled         = true;
    public float caveScale           = 0.0009f;
    public float caveThreshold       = 0.880f;
    public float caveDetail          = 0.45f;
    public float caveRegionScale     = 0.00015f;
    public float caveRegionThreshold = 0.10f;   // 층별로 tileData.json 이 덮어쓴다
    public float caveRegionBand      = 0.15f;   // 0 이면 굴 끝이 직선으로 잘린다(옛 동작)
    public float caveRegionFalloff   = 0.12f;   // (1 - caveThreshold) 이상이면 경계에서 완전 폐쇄
    public float caveEdgeMarginPx    = 40f;    // 청크 가장자리 이 폭 안에서 굴을 닫는다. 0 이면 가둠 없음
    public bool  caveKeepLargestOnly = true;    // 청크당 굴 하나만 남긴다(외톨이 구멍 제거)

    // 굴 벽 전용 바위. 일반 배치는 청크당 4개뿐이라 굴(청크 면적의 ~15%)에 확률적으로
    // 0~1개밖에 안 들어간다 — 굴 안이 휑해 보이는 원인이다. 이 값만큼 굴 가장자리에 따로 깐다.
    public int   caveRockCount       = 3;

    // [blob] 캡슐 한 개 모드. false 면 위 ridged 노이즈 방식으로 돌아간다.
    public bool  caveUseBlob         = true;
    public float caveBlobChance      = 0.7f;    // 청크당 굴이 있을 확률
    public float caveBlobLengthMinPx = 600f;
    public float caveBlobLengthMaxPx = 1000f;
    public float caveBlobRadiusMinPx = 68f;     // 침식 15px 를 빼면 실제 폭 86px
    public float caveBlobRadiusMaxPx = 98f;     // 〃 140px
    public float caveLinkChance      = 0.85f;   // 좌우 이웃과 이어질 확률
    public float caveLinkRadiusFactor = 0.85f;  // 연결 통로 굵기 = 얇은 쪽 굴 × 이 값
    public int   caveMaxLinkedChunks = 4;       // 한 줄로 최대 몇 청크까지 이어지나
    public float caveBlobWaveAmpMinPx = 20f;
    public float caveBlobWaveAmpMaxPx = 120f;
    public float caveBlobWaveLenMinPx = 250f;
    public float caveBlobWaveLenMaxPx = 700f;
    public float caveBlobMinAspect    = 2.4f;  // 반길이 ≥ 반두께 × 이 값 (길이 하한을 지킨다)
    public float caveBlobWaveMaxSlope = 0.5f;  // 중심선 최대 기울기 (0.5 ≈ 27°)
    public float caveBlobTiltMaxDeg   = 25f;   // 굴 축을 ±이만큼 기울인다
    public float caveBlobBulgeAmp     = 0.35f; // 길이 방향 두께 변조 (0이면 굵기 일정)
    public float caveBlobBulgeLenPx   = 260f;  // 변조 파장
    public float caveBlobWallAmpPx   = 16f;
    public float caveBlobWallScale   = 0.008f;

    // [끝방] 굴 끝을 둥근 방으로 부풀린다. 0 이면 예전처럼 뾰족한 쐐기로 끝난다.
    // gain 을 올리면 배치 여백(rMax)도 같이 커져 굴이 조금 짧아진다 — 둘은 한 몸이다.
    public float caveBlobEndRoomGain  = 0.35f;
    public float caveBlobEndRoomLenPx = 180f;

    /// <summary>생성 시엔 굴을 묻어두고, 파서 닿았을 때 연다. false 면 처음부터 뚫려 있다.</summary>
    public bool  caveHiddenUntilExposed = true;
    public float caveAspect          = 2.0f;
    public float caveMinRadiusPx     = 8f;
    public float caveRoughness       = 0.25f;
    public float caveRoughFreq1      = 4.3f;
    public float caveRoughFreq2      = 8.5f;

    public List<MineralRuleJson> minerals; // [New] List of minerals that can spawn in this layer

    // [New] 이 층의 하단 경계에 사용할 블렌딩 프로필 이름. null/빈 문자열 → 전역 기본값 사용
    public string blendingProfileName;

    // [New] 층 체류 상태이상. maxStaminaReduction(파기 1회 비용)과 별개의 축이다.
    // 과거에는 PlayerZoneChecker가 maxStaminaReduction을 그대로 동상 누적량으로 썼는데,
    // 그 탓에 1층(Dirt)에서도 추위가 쌓이고 마그마층에서 동상이 쌓였다.
    public string zoneStatusType;        // "None"/"Frostbite"/"Burn"/"Radiation". 비우면 None
    public float  zoneStatusPerSecond;   // 초당 누적량. 0이면 효과 없음
}

[System.Serializable]
public class MineralRuleJson
{
    public string mineralType; // Enum name as string (e.g. "Coal")
    public int minDepth;       // 적용 깊이 범위 (양쪽 시스템 공유)
    public int maxDepth;

    // [Fix] JsonUtility는 enum을 숫자로만 인식 → string으로 받아서 직접 파싱
    // JSON에는 "rarity": "Common" 또는 "Rare" 로 작성
    public string rarity;      // "Common" or "Rare" (양쪽 시스템 공유)

    public bool IsRare => string.Equals(rarity, "Rare", System.StringComparison.OrdinalIgnoreCase);

    // ─── 청크 지형 스폰 전용 (MineralGenerator) ───────────────────────
    // [min, max] 청크당 스폰 개수 범위. 깊이 보정·밀도 배율 적용 전 기준값.
    // 소수 허용 — [0, 1]처럼 1개 미만의 희소 광물을 표현한다.
    public float[] perChunk;

    // ─── 돌 깨기 드랍 전용 (DiggableRock) ─────────────────────────────
    // 드랍 광물 선택 가중치. 깊이에 따라 Lerp(w, 1-w, t)로 뒤집힌다.
    public float rockDropWeight;
    // [min, max] 돌 1개당 드랍 개수 범위.
    public int[] rockDropCount;

    public int RockDropMin => (rockDropCount != null && rockDropCount.Length > 0) ? rockDropCount[0] : 1;
    public int RockDropMax => (rockDropCount != null && rockDropCount.Length > 1) ? rockDropCount[1] : RockDropMin;
}

/// <summary>
/// 한 지층의 정류장 오프셋 덮어쓰기. <see cref="ElevatorConfig.layerStopOffsets"/> 항목.
///
/// JsonUtility가 배열의 배열을 못 읽어서 래퍼 클래스로 감쌌다.
/// 지층을 인덱스가 아니라 이름으로 지정하는 이유: tiles 순서가 바뀌어도 안 어긋난다.
/// </summary>
[System.Serializable]
public class LayerStopOffsetsJson
{
    public string tileType;   // TileDataJson.tileType과 같은 문자열 ("Dirt", "Ice", ...)
    public int[] offsets;     // 그 지층 startDepth 기준 정류장 오프셋 (얕은 → 깊은)
}

/// <summary>
/// 엘리베이터 정류장의 <b>깊이(Y) 배치</b> 설정. X 배치는 ElevatorStopLayout이 따로 담당한다.
///
/// 예전엔 지층 두께를 균등 3등분해서 상·중·하 간격이 6/7/7로 사실상 평평했다.
/// 한 번 장비가 되면 다음 정류장이 공짜로 따라와서 진행 리듬이 죽는다.
/// → 간격을 데이터로 빼서 재컴파일 없이 튜닝할 수 있게 했다.
///
/// (구 spawnInterval·elevatorYOffset은 층당 엘리베이터 1개 체제로 바뀌면서
///  아무도 읽지 않는 죽은 값이 됐다 — 그 자리를 이 설정이 대신한다.)
/// </summary>
[System.Serializable]
public class ElevatorConfig
{
    /// <summary>
    /// 지층 startDepth 기준 정류장 오프셋(양수 = 아래로). 전 지층 공통 기본값.
    /// 기본 {0, 6, 15} = 상층·중층·하층, 간격 6 → 9 → (다음 지층까지) 12.
    ///
    /// 배열 길이가 곧 지층당 정류장 개수다 — 바꾸면 ElevatorRoomPainter.layerRoomImages
    /// 인덱스가 밀리므로 인스펙터도 같이 봐야 한다.
    /// </summary>
    public int[] stopOffsets = { 0, 6, 15 };

    /// <summary>지층별 덮어쓰기. 비어 있으면 <see cref="stopOffsets"/>를 쓴다.</summary>
    public List<LayerStopOffsetsJson> layerStopOffsets;
}

[System.Serializable]
public class LayerBoundaryNoiseConfig
{
    public float noiseScale = 0.08f;     // 파동 빈도 (작을수록 완만)
    public float noiseAmplitude = 2.5f;  // 파동 높이 (청크 단위)
}

[System.Serializable]
public class TileDatabaseJson
{
    public int terrainWidth = 50; // Horizontal chunk boundary (-terrainWidth to +terrainWidth)
    public int terrainDepth = 36; // Vertical chunk depth (0 to -terrainDepth)
    public List<TileDataJson> tiles;

    // [New] 게임 전체 광물 스폰 밀도 배율. 층 배율(mineralDensity)과 곱해진다.
    public float globalMineralDensity = 1.0f;

    // [New] 청크마다 광물 요청/배치 개수를 콘솔에 출력. 밀도 튜닝용 — 평소엔 꺼둘 것.
    // 켜면 청크 로드마다 문자열을 만들므로 GC가 늘어난다.
    public bool logMineralSpawn = false;

    // [New] 광물 배치 지터 0~1. 0 = 격자 정중앙, 1 = 셀 전체(기본).
    // 배경: Assets/Docs/mineral-even-scatter.md §3-4
    public float mineralScatterJitter = 1.0f;

    public ElevatorConfig elevatorConfig; // [New] 엘리베이터 설정
    public LayerBoundaryNoiseConfig layerBoundaryNoise; // 지층 경계 Perlin Noise 설정

    // [New] 블렌딩 프로필 목록 및 전역 기본값
    public List<BlendingProfileData> blendingProfiles;
    public string defaultBlendingProfileName;
}
