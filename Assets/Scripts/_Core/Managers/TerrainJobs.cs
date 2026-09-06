// @tags: terrain, burst, jobs, rendering, chunk, native-array, visual, collider
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Burst-compiled job definitions for terrain chunk processing.
/// These are separated from MonoBehaviour to allow pure job scheduling.
/// MIGRATED FROM: TerrainChunk.Jobs.cs
/// </summary>
public static class TerrainJobs
{
    // ============================================================================================================
    //  TERRAIN VISUAL JOB (Parallel) - 베이스 지형 + 테두리 + rim 렌더
    //  (구 VisualUpdateJob — rim 필드 추가 시 Burst 스테일 커널을 피하려고 개명했다.
    //   배경: Burst 는 잡 struct 의 필드가 바뀌어도 옛 커널을 재사용할 수 있고 재시작으로 안 풀린다.
    //   InitBFSJob → InitDistanceFieldJob 개명과 같은 이유.)
    // ============================================================================================================

    [BurstCompile]
    public struct TerrainVisualJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> baseData;
        [ReadOnly] [NativeDisableParallelForRestriction] public NativeArray<ushort> distanceField;
        [ReadOnly] public NativeArray<Color32> borderData;
        
        // [Added] Secondary Border Support
        [ReadOnly] public NativeArray<byte> pixelInfo;
        [ReadOnly] public NativeArray<Color32> secondaryBorderData;
        public byte secondaryTileId;
        public int secondaryBorderWidth;
        public int secondaryBorderHeight;

        [NativeDisableParallelForRestriction]
        [WriteOnly] public NativeArray<Color32> outputTexture;

        public int width;
        public int height;

        // Settings
        public int textureThicknessPx; // In pixels (textureThickness * PPU)

        // Border UV Mapping Data
        public int borderWidth;
        public int borderHeight;

        // Global UV Mapping
        public int chunkOffsetX;
        public int chunkOffsetY;

        // Debug: DistanceField 등고선 시각화. true 이면 솔리드 픽셀을 거리 기반 회색조 밴드로 출력.
        public bool debugDistanceField;

        // [rim] 공기·파괴불가 픽셀과 맞닿는 최외곽 단색 라인. 0 이면 비활성.
        // half-res 거리장으로는 얇은 라인을 균일하게 그릴 수 없어 full-res baseData 를 직접 스캔한다.
        // 파생 상수는 ChunkJobScheduler 가 계산해 넣는다. 설계: Assets/Docs/terrain-rim-outline.md
        public int     rimThicknessPx;       // t
        public int     rimR2;                // t² — 원판 판정용
        public int     rimGateDist;          // (t+2)*10 — full-res 스캔 게이트
        public int     rimEdgeFallbackDist;  // t*5 — 청크 가장자리 폴백 임계값
        public Color32 rimColor;

        // [rect-local] dispatch 도메인 (full 좌표). Schedule(rectWidth * rectHeight).
        // 이 rect == 방금 거리장이 갱신된 범위(_lastExt) == 텍스처가 바뀔 수 있는 정확한 영역.
        // rect 밖 픽셀은 dispatch되지 않으므로 GPU 텍스처의 이전 값이 그대로 유지된다.
        public int rectMinX, rectMinY, rectWidth;

        [Unity.Burst.BurstDiscard]
        private static void LogBorderInvalid(int bw, int bh, int len) =>
            UnityEngine.Debug.LogWarning($"[TerrainVisualJob] Border data invalid: width={bw}, height={bh}, length={len}");

        /// <summary>
        /// 반경 rimThicknessPx 유클리드 원판 안에 공기가 있으면 true.
        ///
        /// 원판(dx²+dy² ≤ t²)이어야 경사와 무관하게 수직 두께가 t 로 유지된다.
        /// 5×5 정사각형(체비셰프)으로 바꾸면 45° 경사에서 두께가 2.8px 로 벌어진다.
        ///
        /// rim 소스는 공기뿐이다. chamfer 는 `pixelInfo & 128` 도 거리 0 시드로 쓰지만
        /// 그 비트는 파괴 불가가 아니라 TerrainCarver 가 찍는 **카빙 오브젝트 실루엣 마커**다
        /// (파괴 불가는 별개 배열인 IndestructibleMask). rim 이 그걸 소스로 쓰면 땅속에 완전히
        /// 묻힌 바위마다 단색 라인이 생겨 "공기와 맞닿는 곳"이라는 정의가 깨진다.
        /// 그 경계는 종전대로 borderTexture 가 그린다.
        ///
        /// 호출 시점에 OOB 는 불가능하다 — Execute 의 isEdge 분기가 가장자리를 이미 걸러낸다.
        /// </summary>
        private bool IsRimDisc(int px, int py)
        {
            int t = rimThicknessPx;
            for (int dy = -t; dy <= t; dy++)
            for (int dx = -t; dx <= t; dx++)
            {
                if (dx * dx + dy * dy > rimR2) continue;

                int ni = (py + dy) * width + (px + dx);
                if (baseData[ni].a == 0) return true;   // 공기
            }
            return false;
        }

        public void Execute(int i)
        {
            // [rect-local] i는 rect 로컬 인덱스. early-return 가드 불필요 — dispatch 도메인이 곧 rect다.
            int px = rectMinX + i % rectWidth;
            int py = rectMinY + i / rectWidth;
            int index = py * width + px;

            Color32 baseCol = baseData[index];

            // 1. Transparent pixels (Air) are always transparent
            if (baseCol.a == 0)
            {
                outputTexture[index] = baseCol;
                return;
            }

            // Debug: 등고선 시각화 — 25 dist 단위로 밝기 교대 (Case A/B 판별용)
            if (debugDistanceField)
            {
                ushort d = distanceField[index];
                int band = (d / 25) % 2;
                byte g = band == 0 ? (byte)80 : (byte)200;
                outputTexture[index] = new Color32(g, g, g, 255);
                return;
            }

            // [rim] 최외곽 라인 — borderTexture 보다 우선한다.
            // 게이트: 업샘플 dist 는 실제 거리를 최대 +7 까지만 과대평가하고 rim 후보의 실제
            // 거리는 최대 5t 이므로, rim 픽셀의 dist 는 5t+7 이하다. (t+2)*10 이면 충분히 안전.
            // 설계·유도: Assets/Docs/terrain-rim-outline.md §3-4
            if (rimThicknessPx > 0)
            {
                ushort rawDist = distanceField[index];
                if (rawDist <= rimGateDist)
                {
                    int t = rimThicknessPx;
                    bool isEdge = px < t || px >= width - t || py < t || py >= height - t;

                    // 가장자리는 원판이 청크 밖으로 나가므로 BoundarySync 된 거리장으로 폴백 (§4)
                    bool isRim = isEdge ? (rawDist <= rimEdgeFallbackDist)
                                        : IsRimDisc(px, py);

                    if (isRim)
                    {
                        outputTexture[index] = rimColor;
                        return;
                    }
                }
            }

            // 2. Logic: Draw borders based on distance from air
            // Distance is in 5/7 metric (approx 5 units per pixel).
            // So we need to scale thickness checks: thickness * 5.

            ushort dist = distanceField[index];

            // [Fix] 오목 모서리 보정: 대각선 단축 경로로 dist가 과소평가된 픽셀을
            // 직교 이웃 전파 거리로 끌어올려 뾰족한 텍스처 돌출 방지.
            // dist가 직교 이웃의 최소 (dist+5) 보다 작다면 대각선 단축이 원인이므로
            // 더 큰 값(= 직교 경로 거리)으로 교체한다.
            {
                int x0 = px;
                int y0 = py;
                int bestOrtho = int.MaxValue;
                if (x0 > 0)        { int n = (int)distanceField[index - 1]     + 5; if (n < bestOrtho) bestOrtho = n; }
                if (x0 < width-1)  { int n = (int)distanceField[index + 1]     + 5; if (n < bestOrtho) bestOrtho = n; }
                if (y0 > 0)        { int n = (int)distanceField[index - width]  + 5; if (n < bestOrtho) bestOrtho = n; }
                if (y0 < height-1) { int n = (int)distanceField[index + width]  + 5; if (n < bestOrtho) bestOrtho = n; }
                if (bestOrtho != int.MaxValue && bestOrtho > (int)dist)
                    dist = (ushort)math.min(bestOrtho, 255);
            }

            // Texture Border — Biplanar UV
            if (dist <= textureThicknessPx * 5)
            {
                if (borderWidth > 0 && borderHeight > 0 && borderData.Length > 0)
                {
                    int x = px;
                    int y = py;
                    int v = math.min((int)(dist / 5), borderHeight - 1);

                    int globalX = chunkOffsetX + x;
                    int globalY = chunkOffsetY + y;
                    int uX = globalX % borderWidth;  if (uX < 0) uX += borderWidth;
                    int uY = globalY % borderWidth;  if (uY < 0) uY += borderWidth;

                    // DistanceField 기울기로 표면 방향 추정 (보정 전 raw 값 사용)
                    int raw = (int)distanceField[index];
                    int dR = (x < width  - 1) ? (int)distanceField[index + 1]     : raw;
                    int dL = (x > 0)          ? (int)distanceField[index - 1]     : raw;
                    int dU = (y < height - 1) ? (int)distanceField[index + width] : raw;
                    int dD = (y > 0)          ? (int)distanceField[index - width] : raw;
                    int absGx = dR - dL; if (absGx < 0) absGx = -absGx;
                    int absGy = dU - dD; if (absGy < 0) absGy = -absGy;
                    int totalG = absGx + absGy;

                    // 타일 선택 (Primary / Secondary)
                    NativeArray<Color32> targetBorderData = borderData;
                    int targetBWidth = borderWidth;

                    if (pixelInfo.IsCreated && pixelInfo.Length > 0)
                    {
                        byte pid = (byte)(pixelInfo[index] & 127);
                        if (secondaryBorderData.IsCreated && secondaryBorderData.Length > 0 && pid == secondaryTileId)
                        {
                            targetBorderData = secondaryBorderData;
                            if (secondaryBorderWidth != borderWidth || secondaryBorderHeight != borderHeight)
                            {
                                v    = math.min((int)(dist / 5), secondaryBorderHeight - 1);
                                uX   = globalX % secondaryBorderWidth; if (uX < 0) uX += secondaryBorderWidth;
                                uY   = globalY % secondaryBorderWidth; if (uY < 0) uY += secondaryBorderWidth;
                                targetBWidth = secondaryBorderWidth;
                            }
                        }
                    }

                    // Biplanar 샘플: X방향(수평 표면용)과 Y방향(수직 표면용)
                    int idxX = v * targetBWidth + uX;
                    int idxY = v * targetBWidth + uY;
                    Color32 colX = (idxX < targetBorderData.Length) ? targetBorderData[idxX] : new Color32(0,0,0,0);
                    Color32 colY = (idxY < targetBorderData.Length) ? targetBorderData[idxY] : new Color32(0,0,0,0);

                    Color32 result;
                    if (totalG > 0)
                    {
                        // |gy| 클수록 수평 표면 → X 기반 UV 비중 높임
                        // |gx| 클수록 수직 표면 → Y 기반 UV 비중 높임
                        int wx = absGy;
                        int wy = absGx;
                        result = new Color32(
                            (byte)((colX.r * wx + colY.r * wy) / totalG),
                            (byte)((colX.g * wx + colY.g * wy) / totalG),
                            (byte)((colX.b * wx + colY.b * wy) / totalG),
                            (byte)((colX.a * wx + colY.a * wy) / totalG));
                    }
                    else
                    {
                        result = colX; // 기울기 없으면 X 기반 (기존 동작)
                    }

                    if (result.a > 0) { outputTexture[index] = result; return; }
                }
                else
                {
                    if (i == 0)
                    {
                        int bLen = borderData.Length;
                        LogBorderInvalid(borderWidth, borderHeight, bLen);
                    }
                }
            }

            // 3. Main Terrain (No Fog/Shading)
            // Just output the base color
            outputTexture[index] = baseCol;
        }
    }

    // ============================================================================================================
    //  CARVE CAVE JOB - 생성 시점에 지형에 굴을 뚫는다.
    //
    //  이 잡은 BasePixels 에 **쓰는** 첫 생성단계 잡이다. Reuse_Step1_Prepare 의
    //  LoadPixelData/FillPixelInfo 직후, ScheduleInitArea 직전에 스케줄된다 — 그래야
    //  거리장·테두리·콜라이더·돌·광물이 전부 "원래 그런 지형"으로 보고 알아서 따라온다.
    //
    //  노이즈 도메인은 청크 내부 좌표가 아니라 **월드 픽셀 좌표**다.
    //    - snoise 는 순수 함수라 언로드→재로드 해도 모양이 동일하다 → 세이브 불필요.
    //    - 좌표가 연속이라 이웃 청크와 무늬가 이어지긴 하지만, 굴이 실제로 청크를 넘어가지는
    //      않는다(edgeMarginPx 참고).
    //
    //  ⚠ 굴은 청크 밖으로 안 나간다 — edgeMarginPx 안쪽에서 통로를 닫는다.
    //    월드 좌표가 연속이어도 청크 경계에서 굴이 직선으로 잘려 보이는 경우가 있었다:
    //      1. 층(TileType)마다 seedSalt 가 달라 seedOffset(도메인 오프셋)이 통째로 점프한다.
    //         층 경계 청크는 위아래 무늬가 애초에 못 이어진다.
    //      2. 저장 복원 청크는 카빙을 건너뛴다(플레이어가 메운 자리를 도로 파면 안 되므로).
    //         한쪽만 카빙되면 그 경계가 직선으로 남는다.
    //    둘 다 "이어 붙이기"로는 못 고친다. 그래서 아예 청크 안에서 시작하고 끝내게 했다.
    //    청크가 1000×1000px 이라 굴 하나가 들어가고도 남는다.
    //
    //  ridged 노이즈를 쓰는 이유: snoise 를 그대로 임계화하면 둥근 물방울 구멍이 되고,
    //  영교차 근처만 남기면(1 - |v|) 가늘고 길게 이어지는 굴 모양이 된다.
    //  ridge 는 [0,1] 로 정규화되어 threshold 하나가 굴의 폭을 직접 제어한다(1에 가까울수록 좁다).
    //
    //  ridge 만으로는 부족하다 — 영교차선은 끊기지 않는 등고선이라 threshold 를 어떻게 잡아도
    //  지형 전체가 균일한 그물망이 된다(굵은 협곡 아니면 촘촘한 미로, 그 사이가 없다).
    //  그래서 저주파 region 노이즈로 **굴이 생길 수 있는 지대**를 먼저 정하고 그 안에서만 판다.
    //
    //  이 게이트는 원래 하드 컷이었다. 부드럽게 감쇠시키면 지대 경계에서 통로 폭이 0 으로 수렴해
    //  플레이어가 못 지나가는 1~2px 짜리 실금이 잔뜩 생겼기 때문이다. 대신 굴이 지대 경계선을 따라
    //  **직선으로 뚝 잘린** 막다른 길로 끝났다.
    //  지금은 regionBand/regionFalloff 로 경계 안쪽만 서서히 조인다(taper) — 얇아진 끝자락은
    //  minRadiusPx 8방향 침식이 통째로 지우므로 실금이 남지 않고, 굴 끝이 둥글게 닫힌다.
    //  ⚠ 그래서 regionBand 를 키우려면 minRadiusPx 가 살아 있어야 한다(0 이면 실금이 그대로 남는다).
    //  regionBand = 0 이면 예전 하드 컷 그대로다.
    //
    //  aspect 는 노이즈 Y 도메인만 늘려 굴을 가로로 눕힌다. 등방 노이즈를 그대로 쓰면 수직 굴이
    //  절반이라 "갈라진 틈"처럼 보이지 동굴로 안 읽힌다. 지형을 파고 내려가는 게임이라
    //  수직 통로는 플레이어가 직접 뚫고, 가로 통로가 이동 경로로 쓸모 있다.
    //
    //  roughness 는 굴 벽을 각지게 만드는 고주파 옥타브 2개다. 저주파 ridge 만 쓰면 경계가
    //  매끈한 등고선이라 "동굴"이 아니라 유기적인 덩어리로 읽힌다(게임 화면에서 확인).
    //  주파수를 너무 높이면(11배 이상) 잔물결이 되어 지저분하고 튜브를 끊으므로 4~9배가 적당하다.
    //  도메인 워프도 시도했는데 파편만 잔뜩 생겨서 버렸다.
    //
    //  minRadiusPx 는 8방향 침식이다 — 반경 안이 전부 굴일 때만 굴로 남긴다.
    //  폭이 minRadiusPx*2 아래로 얇아지는 곳이 통째로 사라지므로, 굴 끝이 뾰족하게 길게 빠지는 걸
    //  막는 값이기도 하다(= "이만큼 안 되면 땅으로 채운다"). 침식은 통로 벽도 반경만큼 깎으므로
    //  threshold 를 같이 낮춰 두께를 되찾아야 한다. 이 둘은 반드시 함께 조정할 것.
    //
    //  굴 모양은 두 가지 모드가 있다 (tileData.json 의 caveUseBlob).
    //
    //   [blob] 청크마다 **캡슐 하나**를 놓는다. 중심점을 하나 찍고 가로로 늘린 뒤 중심선을
    //     사인파로 흔들고 벽에 노이즈 요철을 준다. 끝은 반원 캡이라 뾰족하게 빠지지 않는다.
    //     길이·두께·빈도를 직접 지정하므로 결과가 예측 가능하고, 굴이 하나뿐이라 외톨이 구멍이
    //     구조적으로 안 생긴다.
    //
    //     좌우 이웃과는 **경계 해시**로 잇는다(caveLinkChance). 두 청크가 공유하는 세로 경계를
    //     키로 난수를 뽑으므로, 이웃 청크가 로드돼 있든 말든 양쪽이 같은 Y 지점을 골라 통로를
    //     뻗는다 → 경계에서 정확히 맞물린다. 이웃 데이터를 읽지 않는 게 핵심이다.
    //     세로(층 경계)로는 안 잇는다 — 층마다 seedSalt 가 달라 도메인이 점프하고, 저장 복원
    //     청크는 카빙을 건너뛰기 때문이다.
    //
    //   [noise] 아래 ridged 노이즈 방식. 무늬는 유기적이지만 임계값 방식이라 등고선이 작게
    //     닫히는 자리에서 자잘한 조각 굴이 계속 생긴다. 롤백용으로 남겨 둔다.
    //
    //  카빙은 잡 3개짜리 체인이다:
    //     CaveGridMaskJob  — 4px 격자로 굴 배치를 훑어 저해상도 마스크(침식 포함)
    //     CaveKeepBiggestJob — 그 격자에서 최대 연결 성분만 남김(외톨이 구멍 제거)
    //     CaveCarvePixelsJob — 남은 성분 안에서만 실제 픽셀을 판다
    //  판정식(CaveCapsuleGeometry.IsCaveAt)은 세 잡이 공유한다. 복사해 두 벌로 만들면 한쪽만 고쳤을 때
    //  마스크와 실제 굴이 어긋나 통로가 잘린다.
    //
    //  ⚠ 이 struct 들(CaveCapsuleGeometry 포함)에 필드를 추가하면 struct 이름을 바꿀 것.
    //     Burst 는 필드가 바뀐 잡의 예전 커널을 재사용할 수 있고 에디터 재시작으로도 안 풀린다
    //     (이 파일 상단 TerrainVisualJob · InitDistanceFieldJob 개명 경위 참고).
    // ============================================================================================================

    /// <summary>
    /// 굴 판정식 한 벌. 카빙 잡과 연결성 마스크 잡이 **같은 함수**를 봐야 해서 struct 로 뽑았다.
    /// (두 벌로 복사하면 한쪽만 고쳤을 때 마스크와 실제 굴이 어긋나 통로가 잘린다.)
    /// </summary>
    public struct CaveCapsuleGeometry
    {
        public float scale;
        public float threshold;
        public float detail;
        public float invRidgeMax;

        public float regionScale;
        public float regionThreshold;
        public float regionBand;
        public float regionFalloff;

        public float aspect;
        public float roughness;
        public float roughFreq1;
        public float roughFreq2;

        /// <summary>청크 좌하단의 월드 픽셀 좌표(+ 층별 도메인 오프셋).</summary>
        public float2 chunkOrigin;
        public int    width;
        public int    height;

        /// <summary>청크 가장자리에서 이 거리(px) 안쪽부터 통로를 닫는다. 0 이면 가둠 없음.</summary>
        public float edgeMarginPx;

        // ─── [blob] 캡슐 한 개 모드 ────────────────────────────────────────────────
        // 좌표는 전부 **청크 로컬 픽셀**이다. 배치는 스케줄러가 청크 좌표 해시로 뽑아 넣는다.

        /// <summary>1 이면 캡슐 모드. 0 이면 아래 ridged 노이즈 모드.</summary>
        public byte useBlob;

        /// <summary>0 이면 이 청크엔 굴이 없다(캡슐 모드 전용).</summary>
        public byte blobExists;

        public float2 blobCenter;      // 캡슐 중심(청크 로컬 px)
        public float  blobHalfLength;  // 가로 반길이. 0 이면 원형 굴
        public float  blobRadius;      // 통로 반두께. 실제 폭 = 2*(blobRadius - minRadiusPx)
        public float  blobWaveAmp;     // 중심선 흔들림 진폭(px). 청크마다 다르다
        public float  blobWaveFreq;    // 라디안/px. 파장도 청크마다 다르다
        public float  blobWavePhase;

        /// <summary>축 방향 단위벡터. 굴을 기울여 수평 일변도를 깬다(cos, sin).</summary>
        public float  blobCos;
        public float  blobSin;

        /// <summary>길이 방향 두께 변조. 0 이면 굵기가 일정한 관.</summary>
        public float  blobBulgeAmp;
        public float  blobBulgeScale;   // 1/변조 파장(px)
        public float  blobBulgeSeed;    // 청크마다 변조 무늬를 다르게

        /// <summary>변조로 얇아져도 이 반두께는 보장한다. 플레이어가 못 지나가는 목을 막는다.</summary>
        public float  blobMinRadius;
        public float  blobWallAmp;     // 벽 요철 진폭(px). 0 이면 매끈한 캡슐
        public float  blobWallScale;   // 벽 요철 노이즈 주파수

        /// <summary>
        /// [끝방] 굴 끝 blobEndRoomLenPx 구간에서 반경을 최대 (1+gain) 배로 부풀린다.
        ///
        /// 안 부풀리면 두께 변조가 끝에서 얇아진 뒤 침식이 그 자락을 잘라, 굴이 **뾰족한 쐐기**로
        /// 끝난다. 쐐기의 두 옆면이 거의 직선이라 "굴이 일직선으로 뚝 끝난다"로 보인다.
        /// 끝을 둥근 방으로 만들면 동굴의 막다른 공동처럼 읽힌다.
        ///
        /// ⚠ ComputeCapsule 의 rMax 가 이 배율을 반영해야 한다. 안 그러면 부푼 끝이 배치 여백을
        ///   넘어 청크 경계에 닿고, 경계에서 수직으로 잘린 벽이 다시 생긴다.
        /// </summary>
        public float  blobEndRoomGain;
        public float  blobEndRoomLenPx;

        // ─── 좌우 이웃 연결 통로 ───────────────────────────────────────────────
        // 경계 해시로 정한 Y 지점에서 본 굴의 끝까지 잇는 선분이다. 양 끝점은 스케줄러가 넣는다.
        // 통로 끝을 청크 밖으로 살짝 빼두므로(A 점) 경계 픽셀에서 통로가 얇아지지 않는다.
        public byte   hasLinkLeft;
        public byte   hasLinkRight;
        public float2 linkLeftA,  linkLeftB;
        public float2 linkRightA, linkRightB;
        /// <summary>
        /// 좌·우 통로의 반두께를 **따로** 둔다. 하나로 합쳐 max() 를 쓰면 그 값이 "그 청크의 다른 쪽
        /// 통로"에 좌우되므로, 같은 경계를 그리는 두 청크가 서로 다른 두께를 써서 이음매에 단차가 생긴다
        /// (활성 경계의 37% 에서 발생, 지름 기준 최대 50px). 경계마다 값이 하나여야 양쪽이 일치한다.
        /// </summary>
        public float  linkRadiusLeft;
        public float  linkRadiusRight;

        /// <summary>
        /// 월드 픽셀 좌표 한 점이 굴인지. 침식이 이 함수를 이웃 좌표로 여러 번 부른다.
        ///
        /// 청크가 아니라 월드 좌표만 보므로 청크 경계 너머를 물어도 정답이 나온다 —
        /// 침식이 청크 가장자리에서 어긋나지 않는 이유다.
        /// </summary>
        public bool IsCaveAt(float2 world)
        {
            if (useBlob != 0) return IsBlobAt(world);

            // aspect 는 Y 도메인만 누른다 -> 같은 노이즈가 가로로 늘어난 모양이 된다.
            float2 skewed = new float2(world.x, world.y * aspect);

            // 굴 지대 밖이면 즉시 탈락. ridge 계산보다 먼저 걸러 노이즈 2회를 아낀다.
            //
            // (811, -233) 은 region 노이즈를 굴 노이즈와 다른 도메인으로 밀어내는 상수다.
            // 안 밀면 두 노이즈의 특징이 같은 자리에 정렬돼 지대가 굴 그물을 그대로 따라간다.
            // Burst 에서 static readonly 필드 접근은 버전을 타므로 리터럴로 박아 둔다.
            float region = noise.snoise(skewed * regionScale + new float2(811f, -233f));
            if (region <= regionThreshold) return false;

            // [taper] 지대 경계 안쪽 regionBand 구간에서만 threshold 를 끌어올려 통로를 서서히 조인다.
            // 굴 끝이 직선이 아니라 둥글게 닫히는 이유다. 조여서 minRadiusPx*2 아래로 얇아진 끝자락은
            // 8방향 침식이 통째로 지우므로 예전에 이 감쇠를 뺐던 이유(실금)는 재발하지 않는다.
            float raise = 0f;
            if (regionBand > 0f)
            {
                float edge = math.saturate((region - regionThreshold) / regionBand); // 0 = 경계, 1 = 지대 안쪽
                raise = regionFalloff * (1f - edge);
            }

            // [청크 가둠] 청크 가장자리 edgeMarginPx 안쪽에서 통로를 닫는다.
            // 가장자리에서 더하는 양이 (1 - threshold) 라 ridge 최댓값 1 로도 못 넘는다 = 확실히 닫힘.
            // 침식 탭이 청크 밖 좌표를 물으면 d < 0 이 되어 그대로 닫힌 것으로 판정된다 ->
            // 굴 픽셀이 경계에 닿는 일 자체가 없다.
            if (edgeMarginPx > 0f)
            {
                float2 local = world - chunkOrigin;
                float d = math.min(math.min(local.x, width - local.x),
                                   math.min(local.y, height - local.y));
                float e = math.saturate(d / edgeMarginPx);
                // 두 taper 는 합치지 않고 강한 쪽을 쓴다 — 합치면 지대 경계 + 청크 경계가 겹치는
                // 모서리에서만 과하게 닫혀 굴 끝 모양이 불규칙해진다.
                raise = math.max(raise, (1f - threshold) * (1f - e));
            }

            float localThreshold = threshold + raise;

            float2 p = skewed * scale;
            float  v = noise.snoise(p) + noise.snoise(p * 2.17f) * detail;

            // 벽 요철. invRidgeMax 가 이 진폭까지 포함해 정규화하므로 threshold 의 의미는 유지된다.
            if (roughness > 0f)
            {
                v += noise.snoise(p * roughFreq1) * roughness;
                v += noise.snoise(p * roughFreq2) * roughness * 0.5f;
            }

            return 1f - math.abs(v) * invRidgeMax > localThreshold;
        }

        /// <summary>
        /// [blob] 캡슐 한 개 판정. 중심선은 가로로 늘린 선분이고 거기에 사인파 흔들림이 얹힌다.
        ///
        /// 끝을 선분 밖 거리(over)로 재기 때문에 양 끝이 반원 캡이 된다 — 뾰족하게 길게 빠지는
        /// 자락이 구조적으로 안 생긴다. 벽 요철은 반지름에 노이즈를 더해서 낸다(모양은 캡슐 유지).
        ///
        /// 요철 노이즈는 월드 좌표로 뽑는다. 로컬 좌표로 뽑으면 모든 청크의 요철 무늬가 똑같아진다.
        /// </summary>
        private bool IsBlobAt(float2 world)
        {
            if (blobExists == 0) return false;

            float2 local = world - chunkOrigin;

            // 벽 요철은 **월드 좌표**로 뽑는다. 로컬로 뽑으면 모든 청크의 요철이 똑같아지고,
            // 무엇보다 경계를 사이에 둔 두 청크가 서로 다른 값을 써서 연결 통로가 어긋난다.
            float wall = (blobWallAmp > 0f)
                ? blobWallAmp * noise.snoise(world * blobWallScale + new float2(37f, 91f))
                : 0f;

            // 축 기준 좌표로 옮긴다. dx = 축 방향, dy = 축에 수직.
            float2 rel = local - blobCenter;
            float dx = rel.x * blobCos + rel.y * blobSin;
            float dyRaw = -rel.x * blobSin + rel.y * blobCos;

            float dy = dyRaw - blobWaveAmp * math.sin(dx * blobWaveFreq + blobWavePhase);

            float over = math.max(0f, math.abs(dx) - blobHalfLength);   // 선분 밖으로 나간 만큼
            float dist = math.sqrt(dy * dy + over * over);

            // 길이 방향 두께 변조 — 넓은 방과 좁은 목이 번갈아 생긴다.
            // 축 좌표(dx)로만 뽑으므로 통로를 따라 굵기가 부드럽게 변한다.
            float r = blobRadius;
            if (blobBulgeAmp > 0f)
            {
                float bulge = 1f + blobBulgeAmp
                            * noise.snoise(new float2(dx * blobBulgeScale, blobBulgeSeed));
                r = math.max(blobMinRadius, blobRadius * bulge);
            }

            // [끝방] 끝으로 갈수록 반경을 키운다. t=0 안쪽, t=1 굴 끝.
            if (blobEndRoomGain > 0f && blobEndRoomLenPx > 0f)
            {
                float t = math.saturate(
                    (math.abs(dx) - (blobHalfLength - blobEndRoomLenPx)) / blobEndRoomLenPx);
                r *= 1f + blobEndRoomGain * t;
            }

            if (dist < r + wall) return true;

            if (hasLinkLeft != 0 && DistToSegment(local, linkLeftA, linkLeftB) < linkRadiusLeft + wall)
                return true;

            if (hasLinkRight != 0 && DistToSegment(local, linkRightA, linkRightB) < linkRadiusRight + wall)
                return true;

            return false;
        }

        /// <summary>점에서 선분까지 거리. 연결 통로를 캡슐(둥근 끝)로 만드는 데 쓴다.</summary>
        private static float DistToSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float2 ap = p - a;
            float t = math.saturate(math.dot(ap, ab) / math.max(1e-6f, math.dot(ab, ab)));
            return math.length(ap - ab * t);
        }

        /// <summary>
        /// 침식까지 적용한 굴 판정 — 반경 minRadiusPx 원판이 통째로 굴 안에 들어갈 때만 true.
        /// 폭이 minRadiusPx*2 아래로 얇아지는 곳(굴 끝의 뾰족한 자락)은 여기서 탈락한다.
        ///
        /// 카빙 잡과 저해상도 마스크 잡이 **둘 다** 이걸 써야 한다. 마스크만 침식 없이 보면
        /// 실제로는 지워질 얇은 목을 "연결됨"으로 오판해, 그 목으로만 이어진 외톨이 구멍이 살아남는다.
        /// </summary>
        public bool IsCaveEroded(float2 world, float minRadiusPx)
        {
            if (!IsCaveAt(world)) return false;
            if (minRadiusPx <= 0f) return true;

            float r = minRadiusPx;
            float d = r * 0.7071f;   // 대각선 탭 — 8방향을 원에 가깝게 만든다

            return IsCaveAt(world + new float2( r, 0f))
                && IsCaveAt(world + new float2(-r, 0f))
                && IsCaveAt(world + new float2(0f,  r))
                && IsCaveAt(world + new float2(0f, -r))
                && IsCaveAt(world + new float2( d,  d))
                && IsCaveAt(world + new float2( d, -d))
                && IsCaveAt(world + new float2(-d,  d))
                && IsCaveAt(world + new float2(-d, -d));
        }
    }

    // ============================================================================================================
    //  CAVE COARSE MASK JOB - 굴 배치를 cellPx 격자로 한 번 훑어 저해상도 마스크를 만든다.
    //  다음 잡(CaveKeepBiggestJob)이 이 격자 위에서 연결 성분을 세고, 카빙 잡이 그 결과를 참조한다.
    //
    //  왜 격자인가: 연결 판정은 본질적으로 직렬(BFS)이라 100만 픽셀을 그대로 돌면 비싸다.
    //  침식(minRadiusPx=8)이 폭 16px 미만 통로를 이미 지우므로, 4px 격자면 살아남은 통로는
    //  최소 4셀 폭이라 연결이 끊기지 않는다.
    // ============================================================================================================

    [BurstCompile]
    public struct CaveGridMaskJob : IJobParallelFor
    {
        [WriteOnly] public NativeArray<byte> cells;
        public CaveCapsuleGeometry field;
        public int cellsX;
        public int cellPx;

        /// <summary>카빙 잡과 **같은 값**을 넣을 것. 다르면 마스크와 실제 굴이 어긋난다.</summary>
        public float minRadiusPx;

        public void Execute(int c)
        {
            int cx = c % cellsX;
            int cy = c / cellsX;
            float2 world = field.chunkOrigin
                         + new float2(cx * cellPx + cellPx * 0.5f, cy * cellPx + cellPx * 0.5f);
            cells[c] = field.IsCaveEroded(world, minRadiusPx) ? (byte)1 : (byte)0;
        }
    }

    // ============================================================================================================
    //  CAVE KEEP LARGEST JOB - 저해상도 마스크에서 **가장 큰 연결 성분만** 남긴다.
    //  주변과 이어지지 않은 외톨이 구멍을 없앤다.
    //
    //  굴이 청크 안에 갇혀 있어야(edgeMarginPx > 0) 이 판정이 정확하다 — 청크를 넘어가는 굴이면
    //  이웃 청크에서 이어지는 통로를 "외톨이"로 오판해 지워 버린다. 둘은 세트다.
    //
    //  마지막에 성분을 dilate 셀만큼 팽창시킨다. 셀 중심만 샘플링하므로 통로 가장자리 셀은
    //  solid 로 잡히는데, 팽창을 안 하면 카빙 잡이 그 셀의 픽셀을 건너뛰어 통로 벽이 셀 격자
    //  모양으로 깎인다(4px 계단).
    // ============================================================================================================

    [BurstCompile]
    public struct CaveKeepBiggestJob : IJob
    {
        /// <summary>입력: 1 = 굴. 출력: 남긴 성분만 1, 나머지 0.</summary>
        public NativeArray<byte> cells;

        /// <summary>BFS 스택 겸 팽창용 스크래치. 길이는 cells 와 같아야 한다.</summary>
        public NativeArray<int> stack;

        public int cellsX;
        public int cellsY;
        public int dilate;

        /// <summary>0 이면 아무것도 안 하고 통과(= 외톨이 구멍 제거 끄기).</summary>
        public byte enabled;

        public void Execute()
        {
            if (enabled == 0) return;

            int n = cells.Length;

            // 1패스: 모든 성분을 훑어 최대 성분의 시작점을 찾는다. 방문 표시는 2.
            int bestStart = -1;
            int bestSize = 0;

            for (int s = 0; s < n; s++)
            {
                if (cells[s] != 1) continue;

                int size = 0;
                int sp = 0;
                stack[sp++] = s;
                cells[s] = 2;

                while (sp > 0)
                {
                    int c = stack[--sp];
                    size++;
                    int cx = c % cellsX;
                    int cy = c / cellsX;

                    if (cx > 0          && cells[c - 1] == 1)      { cells[c - 1] = 2;      stack[sp++] = c - 1; }
                    if (cx < cellsX - 1 && cells[c + 1] == 1)      { cells[c + 1] = 2;      stack[sp++] = c + 1; }
                    if (cy > 0          && cells[c - cellsX] == 1) { cells[c - cellsX] = 2; stack[sp++] = c - cellsX; }
                    if (cy < cellsY - 1 && cells[c + cellsX] == 1) { cells[c + cellsX] = 2; stack[sp++] = c + cellsX; }
                }

                if (size > bestSize) { bestSize = size; bestStart = s; }
            }

            if (bestStart < 0)
            {
                for (int i = 0; i < n; i++) cells[i] = 0;
                return;
            }

            // 2패스: 최대 성분만 3 으로 다시 칠한다(2 = 굴이지만 버릴 성분).
            {
                int sp = 0;
                stack[sp++] = bestStart;
                cells[bestStart] = 3;

                while (sp > 0)
                {
                    int c = stack[--sp];
                    int cx = c % cellsX;
                    int cy = c / cellsX;

                    if (cx > 0          && cells[c - 1] == 2)      { cells[c - 1] = 3;      stack[sp++] = c - 1; }
                    if (cx < cellsX - 1 && cells[c + 1] == 2)      { cells[c + 1] = 3;      stack[sp++] = c + 1; }
                    if (cy > 0          && cells[c - cellsX] == 2) { cells[c - cellsX] = 3; stack[sp++] = c - cellsX; }
                    if (cy < cellsY - 1 && cells[c + cellsX] == 2) { cells[c + cellsX] = 3; stack[sp++] = c + cellsX; }
                }
            }

            for (int i = 0; i < n; i++) cells[i] = (byte)(cells[i] == 3 ? 1 : 0);

            // 3패스: 셀 격자 계단을 막는 팽창.
            for (int d = 0; d < dilate; d++)
            {
                for (int i = 0; i < n; i++) stack[i] = cells[i];

                for (int c = 0; c < n; c++)
                {
                    if (stack[c] != 0) continue;
                    int cx = c % cellsX;
                    int cy = c / cellsX;

                    bool near = (cx > 0          && stack[c - 1] == 1)
                             || (cx < cellsX - 1 && stack[c + 1] == 1)
                             || (cy > 0          && stack[c - cellsX] == 1)
                             || (cy < cellsY - 1 && stack[c + cellsX] == 1);

                    if (near) cells[c] = 1;
                }
            }
        }
    }

    [BurstCompile]
    public struct CaveCarvePixelsJob : IJobParallelFor
    {
        // 읽기·쓰기 둘 다 하므로 [ReadOnly] 금지.
        // 접근이 전부 인덱스 i 에만 일어나므로 [NativeDisableParallelForRestriction] 는 불필요.
        public NativeArray<Color32> baseData;
        public NativeArray<byte>    pixelInfo;

        /// <summary>CaveKeepBiggestJob 이 남긴 성분(1). 0 인 셀의 픽셀은 파지 않는다.</summary>
        [ReadOnly] public NativeArray<byte> keptCells;

        public CaveCapsuleGeometry field;
        public int width;

        public int cellsX;
        public int cellPx;

        /// <summary>8방향 침식 반경(px). 이보다 얇은 조각이 사라진다. 0이면 비활성.</summary>
        public float minRadiusPx;

        public void Execute(int i)
        {
            // 이미 공기면 할 일 없음(이미 파진 청크는 저장 복원 경로라 여기 안 온다).
            if (baseData[i].a == 0) return;

            int x = i % width;
            int y = i / width;

            // [외톨이 제거] 이 픽셀이 속한 셀이 '남긴 성분'이 아니면 파지 않는다.
            if (keptCells[(y / cellPx) * cellsX + (x / cellPx)] == 0) return;

            float2 world = field.chunkOrigin + new float2(x, y);

            // 8방향 침식 포함. 후보 픽셀(전체의 몇 %)에만 탭이 도는 데다 대부분 region 게이트에서
            // 바로 탈락하므로, 탭이 8개여도 픽셀당 평균 비용은 1회 노이즈 수준이다.
            if (!field.IsCaveEroded(world, minRadiusPx)) return;

            baseData[i]  = default;   // (0,0,0,0) = 공기
            pixelInfo[i] = 0;         // TerrainCarver.ClearHole 과 동일 처리
        }
    }

    // ============================================================================================================
    //  DOWNSAMPLE MASK JOB - full BasePixels/PixelInfo → half (2×2 다운샘플).
    //  "2×2 중 하나라도 air면 air", indestructible 비트(&128)는 OR. Init/Chamfer가 half 격자에서 읽는다.
    //  half 좌표 ext rect 넓이만큼 스케줄(rect-local dispatch). (배경: Assets/Docs/half-res-distance-field.md)
    // ============================================================================================================

    [BurstCompile]
    public struct DownsampleMaskJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> baseData;
        [ReadOnly] public NativeArray<byte> pixelInfo;

        // [rect-local] job index != array index → 안전 검증 해제 필수
        [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<Color32> baseHalf;
        [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<byte> pixelInfoHalf;

        public int fullWidth, fullHeight;
        public int halfWidth;

        // [rect-local] dispatch 도메인 (half 좌표). Schedule(rectWidth * rectHeight).
        public int rectMinX, rectMinY, rectWidth;

        public void Execute(int i)
        {
            int hx = rectMinX + i % rectWidth;
            int hy = rectMinY + i / rectWidth;
            int hIndex = hy * halfWidth + hx;

            int x0 = hx << 1; // hx*2
            int y0 = hy << 1; // hy*2

            bool anyAir = false;
            bool anyIndestr = false;
            bool hasInfo = pixelInfo.Length > 0;

            for (int dy = 0; dy < 2; dy++)
            {
                int fy = y0 + dy;
                if (fy >= fullHeight) continue;
                int rowBase = fy * fullWidth;
                for (int dx = 0; dx < 2; dx++)
                {
                    int fx = x0 + dx;
                    if (fx >= fullWidth) continue;
                    int fi = rowBase + fx;
                    if (baseData[fi].a == 0) anyAir = true;
                    if (hasInfo && (pixelInfo[fi] & 128) != 0) anyIndestr = true;
                }
            }

            // 하나라도 air면 air(alpha 0), 아니면 solid(alpha 255). RGB는 미사용(Init/Chamfer는 alpha만 체크).
            baseHalf[hIndex] = anyAir ? new Color32(0, 0, 0, 0) : new Color32(0, 0, 0, 255);
            // Init/Chamfer는 pixelInfo의 &128(indestructible)만 읽으므로 그 비트만 전달.
            pixelInfoHalf[hIndex] = (byte)(anyIndestr ? 128 : 0);
        }
    }

    // ============================================================================================================
    //  UPSAMPLE DISTANCE JOB - half-res 거리장(DistanceFieldHalf) → full 거리장(DistanceField) ×2 펼침.
    //  half 1스텝 = full 2픽셀이므로 값 ×2. TerrainVisualJob은 종전대로 full 버퍼를 읽는다(변경 없음).
    //  Chamfer 완료 후 스케줄. 변경 영역(vis rect)만 업샘플. (배경: Assets/Docs/half-res-distance-field.md)
    // ============================================================================================================

    [BurstCompile]
    public struct UpsampleDistanceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ushort> halfField;

        // [rect-local] job index != array index → 안전 검증 해제 필수
        [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<ushort> fullField;

        public int fullWidth;
        public int halfWidth;
        public int maxDist;

        // [rect-local] dispatch 도메인 (full 좌표). Schedule(rectWidth * rectHeight).
        public int rectMinX, rectMinY, rectWidth;

        public void Execute(int i)
        {
            int x = rectMinX + i % rectWidth;
            int y = rectMinY + i / rectWidth;
            int index = y * fullWidth + x;

            int hx = x >> 1; // x / 2
            int hy = y >> 1; // y / 2
            int d = halfField[hy * halfWidth + hx] * 2; // half 1스텝 = full 2픽셀 → ×2
            fullField[index] = (ushort)(d < maxDist ? d : maxDist);
        }
    }

    // ============================================================================================================
    //  CHAMFER FORWARD PASS JOB - 좌상→우하 스캔라인 (Chamfer 5-7 2패스 거리 변환)
    //  BFS 대체: 순차 메모리 접근으로 캐시 효율 대폭 향상, 큐 오버헤드 없음
    // ============================================================================================================

    [BurstCompile]
    public struct ChamferForwardPassJob : IJob
    {
        public NativeArray<ushort> distanceField;
        [ReadOnly] public NativeArray<Color32> baseData;
        [ReadOnly] public NativeArray<byte> pixelInfo;
        public int width, height, maxDist;
        public int extMinX, extMinY, extMaxX, extMaxY;

        public void Execute()
        {
            for (int y = extMinY; y < extMaxY; y++)
            for (int x = extMinX; x < extMaxX; x++)
            {
                int idx = y * width + x;

                // 에어 또는 경계 마커 → 거리 0 (소스)
                if (baseData[idx].a == 0 || (pixelInfo.Length > 0 && (pixelInfo[idx] & 128) != 0))
                {
                    distanceField[idx] = 0;
                    continue;
                }

                // 이미 처리된 이웃에서 최단 거리 전파
                // 청크 전체 경계 사용(ext 범위 아님): 확장 영역 밖 픽셀의 기존 값도 활용
                int best = distanceField[idx];
                int nb;
                if (x > 0)              { nb = distanceField[idx - 1]          + 5; if (nb < best) best = nb; }
                if (y > 0)              { nb = distanceField[idx - width]       + 5; if (nb < best) best = nb; }
                if (x > 0 && y > 0)     { nb = distanceField[idx - width - 1]  + 7; if (nb < best) best = nb; }
                if (x < width-1 && y>0) { nb = distanceField[idx - width + 1]  + 7; if (nb < best) best = nb; }

                distanceField[idx] = (ushort)(best < maxDist ? best : maxDist);
            }
        }
    }

    // ============================================================================================================
    //  CHAMFER BACKWARD PASS JOB - 우하→좌상 스캔라인
    //  ForwardPass 이후 실행. 두 패스 합산 = BFS와 동일한 Chamfer 5-7 거리 필드
    // ============================================================================================================

    [BurstCompile]
    public struct ChamferBackwardPassJob : IJob
    {
        public NativeArray<ushort> distanceField;
        [ReadOnly] public NativeArray<Color32> baseData;
        [ReadOnly] public NativeArray<byte> pixelInfo;
        public int width, height, maxDist;
        public int extMinX, extMinY, extMaxX, extMaxY;

        public void Execute()
        {
            for (int y = extMaxY - 1; y >= extMinY; y--)
            for (int x = extMaxX - 1; x >= extMinX; x--)
            {
                int idx = y * width + x;

                if (baseData[idx].a == 0 || (pixelInfo.Length > 0 && (pixelInfo[idx] & 128) != 0))
                {
                    distanceField[idx] = 0;
                    continue;
                }

                int best = distanceField[idx];
                int nb;
                if (x < width-1)              { nb = distanceField[idx + 1]         + 5; if (nb < best) best = nb; }
                if (y < height-1)             { nb = distanceField[idx + width]      + 5; if (nb < best) best = nb; }
                if (x < width-1 && y<height-1){ nb = distanceField[idx + width + 1] + 7; if (nb < best) best = nb; }
                if (x > 0 && y < height-1)    { nb = distanceField[idx + width - 1] + 7; if (nb < best) best = nb; }

                distanceField[idx] = (ushort)(best < maxDist ? best : maxDist);
            }
        }
    }

    // ============================================================================================================
    //  BOUNDARY SYNC JOB - Init 완료 후 이웃 청크 거리값을 자기 distanceField 경계에 기록
    //  (Burst化 전: TerrainLightingCalculator.SyncBoundaryDistanceWithNeighbors 메인 스레드 처리 → 31ms idle 원인)
    //  의존성: 자기 _initJobHandle + 각 이웃 _lightingJobHandle
    //  안전: 이웃 distanceField는 [ReadOnly], 자기 distanceField만 쓰므로 RW 충돌 없음
    // ============================================================================================================

    [BurstCompile]
    public struct BoundarySyncJob : IJob
    {
        public NativeArray<ushort> distanceField;

        [ReadOnly] public NativeArray<ushort> leftField;
        [ReadOnly] public NativeArray<ushort> rightField;
        [ReadOnly] public NativeArray<ushort> topField;
        [ReadOnly] public NativeArray<ushort> bottomField;
        [ReadOnly] public NativeArray<ushort> topLeftField;
        [ReadOnly] public NativeArray<ushort> topRightField;
        [ReadOnly] public NativeArray<ushort> bottomLeftField;
        [ReadOnly] public NativeArray<ushort> bottomRightField;

        public int width;
        public int height;

        public bool hasLeft, hasRight, hasTop, hasBottom;
        public bool hasTopLeft, hasTopRight, hasBottomLeft, hasBottomRight;
        public bool isSkyAbove; // cy==0 이고 위쪽 이웃 없음 → 거리 BOUNDARY_COST
        public int maxDist;

        // [Opt] 경계 동기화 범위 — dirty span(±margin). 경계 전체가 아닌 "파낸 구간"만 sync.
        // 좌/우 엣지는 [syncMinY,syncMaxY), 상/하 엣지는 [syncMinX,syncMaxX) 만 순회.
        // 파낸 구간 밖 경계는 base 픽셀이 안 변했으므로 이전 sync 값이 유효(재계산 불필요).
        // 스케줄러가 [0,width]/[0,height]로 클램프해서 넘김.
        public int syncMinX, syncMaxX, syncMinY, syncMaxY;

        public void Execute()
        {
            const ushort BOUNDARY_COST = 5;
            const ushort CORNER_COST = 7;
            // 이웃 거리 필드를 경계 1열만 전달하면 구멍 형태를 잃어
            // 경계선을 따라 직선 스트라이프 border가 생기는 버그 발생.
            // SYNC_DEPTH 픽셀 깊이까지 전파해 실제 구멍 형태를 보존한다.
            const int SYNC_DEPTH = 25; // [half-res] 25 half-cells = 50 full px (종전 full-res 깊이와 동일)

            // 1. Left edge: my (0..SYNC_DEPTH-1, y) ← neighbor's edge (width-1, y)
            // y±1 인접 행도 대각선 비용(+2)으로 체크 → 대각선 경계 통과 비용 7 정확 반영.
            if (hasLeft)
            {
                for (int y = syncMinY; y < syncMaxY; y++)
                {
                    int rowBase  = y * width;
                    int edgeSame  = leftField[rowBase + (width - 1)];
                    int edgeAbove = (y < height - 1) ? leftField[(y + 1) * width + (width - 1)] + 2 : maxDist;
                    int edgeBelow = (y > 0)          ? leftField[(y - 1) * width + (width - 1)] + 2 : maxDist;
                    int bestEdge  = edgeSame;
                    if (edgeAbove < bestEdge) bestEdge = edgeAbove;
                    if (edgeBelow < bestEdge) bestEdge = edgeBelow;
                    for (int k = 0; k < SYNC_DEPTH; k++)
                    {
                        int candidate = bestEdge + BOUNDARY_COST + k * 5;
                        if (candidate > maxDist) candidate = maxDist;
                        if (candidate < distanceField[rowBase + k])
                            distanceField[rowBase + k] = (ushort)candidate;
                    }
                }
            }

            // 2. Right edge: my (width-1..width-SYNC_DEPTH, y) ← neighbor's edge (0, y)
            if (hasRight)
            {
                for (int y = syncMinY; y < syncMaxY; y++)
                {
                    int rowBase   = y * width;
                    int edgeSame  = rightField[rowBase];
                    int edgeAbove = (y < height - 1) ? rightField[(y + 1) * width] + 2 : maxDist;
                    int edgeBelow = (y > 0)          ? rightField[(y - 1) * width] + 2 : maxDist;
                    int bestEdge  = edgeSame;
                    if (edgeAbove < bestEdge) bestEdge = edgeAbove;
                    if (edgeBelow < bestEdge) bestEdge = edgeBelow;
                    for (int k = 0; k < SYNC_DEPTH; k++)
                    {
                        int candidate = bestEdge + BOUNDARY_COST + k * 5;
                        if (candidate > maxDist) candidate = maxDist;
                        if (candidate < distanceField[rowBase + (width - 1 - k)])
                            distanceField[rowBase + (width - 1 - k)] = (ushort)candidate;
                    }
                }
            }

            // 3. Bottom edge: my (x, 0..SYNC_DEPTH-1) ← neighbor's edge (x, height-1)
            if (hasBottom)
            {
                int neighborEdgeRowBase = (height - 1) * width;
                for (int k = 0; k < SYNC_DEPTH; k++)
                {
                    int myRowBase = k * width;
                    int baseCost  = BOUNDARY_COST + k * 5;
                    for (int x = syncMinX; x < syncMaxX; x++)
                    {
                        int edgeSame  = bottomField[neighborEdgeRowBase + x];
                        int edgeLeft  = (x > 0)       ? bottomField[neighborEdgeRowBase + x - 1] + 2 : maxDist;
                        int edgeRight = (x < width-1) ? bottomField[neighborEdgeRowBase + x + 1] + 2 : maxDist;
                        int bestEdge  = edgeSame;
                        if (edgeLeft  < bestEdge) bestEdge = edgeLeft;
                        if (edgeRight < bestEdge) bestEdge = edgeRight;
                        int candidate = bestEdge + baseCost;
                        if (candidate > maxDist) candidate = maxDist;
                        if (candidate < distanceField[myRowBase + x])
                            distanceField[myRowBase + x] = (ushort)candidate;
                    }
                }
            }

            // 4. Top edge: my (x, height-1..height-SYNC_DEPTH) ← neighbor's edge (x, 0)
            if (hasTop)
            {
                for (int k = 0; k < SYNC_DEPTH; k++)
                {
                    int myRowBase = (height - 1 - k) * width;
                    int baseCost  = BOUNDARY_COST + k * 5;
                    for (int x = syncMinX; x < syncMaxX; x++)
                    {
                        int edgeSame  = topField[x];
                        int edgeLeft  = (x > 0)       ? topField[x - 1] + 2 : maxDist;
                        int edgeRight = (x < width-1) ? topField[x + 1] + 2 : maxDist;
                        int bestEdge  = edgeSame;
                        if (edgeLeft  < bestEdge) bestEdge = edgeLeft;
                        if (edgeRight < bestEdge) bestEdge = edgeRight;
                        int candidate = bestEdge + baseCost;
                        if (candidate > maxDist) candidate = maxDist;
                        if (candidate < distanceField[myRowBase + x])
                            distanceField[myRowBase + x] = (ushort)candidate;
                    }
                }
            }
            else if (isSkyAbove)
            {
                // Y=0 가장 위 청크: 위가 하늘(에어) → 거리 BOUNDARY_COST
                int myRowOffset = (height - 1) * width;
                for (int x = syncMinX; x < syncMaxX; x++)
                {
                    int myIdx = myRowOffset + x;
                    if (BOUNDARY_COST < distanceField[myIdx])
                        distanceField[myIdx] = BOUNDARY_COST;
                }
            }

            // 5. Corners (cost 7)
            // BottomLeft: my (0,0) ← neighbor's (width-1, height-1)
            if (hasBottomLeft)
            {
                int myIdx = 0;
                int candidate = bottomLeftField[(height - 1) * width + (width - 1)] + CORNER_COST;
                if (candidate > maxDist) candidate = maxDist;
                if (candidate < distanceField[myIdx])
                    distanceField[myIdx] = (ushort)candidate;
            }
            // BottomRight: my (width-1, 0) ← neighbor's (0, height-1)
            if (hasBottomRight)
            {
                int myIdx = width - 1;
                int candidate = bottomRightField[(height - 1) * width] + CORNER_COST;
                if (candidate > maxDist) candidate = maxDist;
                if (candidate < distanceField[myIdx])
                    distanceField[myIdx] = (ushort)candidate;
            }
            // TopLeft: my (0, height-1) ← neighbor's (width-1, 0)
            if (hasTopLeft)
            {
                int myIdx = (height - 1) * width;
                int candidate = topLeftField[width - 1] + CORNER_COST;
                if (candidate > maxDist) candidate = maxDist;
                if (candidate < distanceField[myIdx])
                    distanceField[myIdx] = (ushort)candidate;
            }
            // TopRight: my (width-1, height-1) ← neighbor's (0, 0)
            if (hasTopRight)
            {
                int myIdx = (height - 1) * width + (width - 1);
                int candidate = topRightField[0] + CORNER_COST;
                if (candidate > maxDist) candidate = maxDist;
                if (candidate < distanceField[myIdx])
                    distanceField[myIdx] = (ushort)candidate;
            }
        }
    }

    // ============================================================================================================
    //  INIT DISTANCE FIELD JOB — dirty rect의 거리장을 리셋하고 ext 영역의 시드(에어·indestructible)를 보정.
    //  (구 InitBFSJob. BFS가 아니라 seed 리셋이므로 개명 — Chamfer 2패스가 실제 거리를 채운다.)
    // ============================================================================================================

    [BurstCompile]
    public struct InitDistanceFieldJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> baseData;
        [ReadOnly] public NativeArray<byte> pixelInfo;

        // [rect-local] job index != array index → 안전 검증 해제 필수
        [NativeDisableParallelForRestriction] public NativeArray<ushort> distanceField;

        public int maxDist;

        // Dirty rect: 실제 변경된 영역 (maxDist로 리셋할 대상)
        public int dirtyMinX, dirtyMinY, dirtyMaxX, dirtyMaxY;

        // [rect-local] dispatch 도메인 = extended rect. Schedule(extWidth * extHeight).
        // ext = dirty ± EXT_MARGIN. 2패스가 이 범위 밖 픽셀의 기존 값을 읽어 전파한다.
        public int extMinX, extMinY, extWidth;

        public int width;
        public int height;

        public void Execute(int i)
        {
            int x = extMinX + i % extWidth;
            int y = extMinY + i / extWidth;
            int index = y * width + x;

            bool isSeed = baseData[index].a == 0
                || (pixelInfo.Length > 0 && (pixelInfo[index] & 128) != 0);

            bool inDirty = x >= dirtyMinX && x < dirtyMaxX && y >= dirtyMinY && y < dirtyMaxY;

            if (inDirty)
            {
                // Dirty 픽셀: 에어→0, 솔리드→maxDist (2패스가 다시 채움)
                distanceField[index] = isSeed ? (ushort)0 : (ushort)maxDist;
            }
            else if (isSeed)
            {
                // Extended 영역 (Dirty 바깥): 에어는 0 보장, 솔리드는 기존 값 유지
                // 2패스는 이 기존 값을 읽어 Dirty 안으로 전파한다
                distanceField[index] = 0;
            }
        }
    }
}
