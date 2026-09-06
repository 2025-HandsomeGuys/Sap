// @tags: decoration, terrain, rock, generation, chunk, data-container
using UnityEngine;
using System.Collections.Generic;

public static class TerrainDecorator
{
    // RockLayoutCalculator 와 같은 해시 상수 — 굴 벽 패스 시드를 청크 좌표에서 뽑는 데 쓴다.
    private const int HASH_X = 73856093;
    private const int HASH_Y = 19349663;

    // [Refactoring] Lightweight struct for rock layout simulation
    // Kept here as it is the data contract between components
    public struct RockData
    {
        public Rect bounds;         // Compatibility: Position and size (Bottom-Left)
        public Vector2 center;      // [New] Center position for radial check
        public float radius;        // [New] Radius for distance check
        public GameObject prefab;   // 스폰할 돌 프리팹 (균열 스프라이트·티어·조각을 자체 보유)
        public Sprite sprite;       // 프리팹의 정상 상태 스프라이트 — 레이아웃·마스크 계산용 캐시
        public int pivotX;          // Integer pivot X
        public int pivotY;          // Integer pivot Y
        public int spriteSetIndex;  // [Save] rockPrefabs[] 인덱스 (저장/복원용)
        public float angle;         // [Fix] 배치 단계에서 결정된 Z 회전(도) — spawn에 그대로 전달
        public float scale;         // [Fix] 배치 단계에서 결정된 균등 스케일 — spawn에 그대로 전달
    }

    /// <summary>
    /// 돌 프리팹의 "정상 상태" 스프라이트를 꺼낸다.
    /// 레이아웃 계산(피벗·크기·마스크)과 복원이 인스턴스화 전에 이 값을 필요로 한다.
    /// 프리팹 root의 SpriteRenderer가 단일 진실 소스다 — DamageStagedVisuals.stages[0]이
    /// 비어 있으면 Awake에서 이 스프라이트로 채워지므로 둘은 항상 일치한다.
    /// </summary>
    public static Sprite GetNormalSprite(GameObject prefab)
    {
        if (prefab == null) return null;
        SpriteRenderer sr = prefab.GetComponent<SpriteRenderer>();
        return sr != null ? sr.sprite : null;
    }

    // [Facade] Generate Rocks using sub-components
    public static void GenerateRocks(TerrainChunk chunk, List<GameObject> rockPrefabs, int minCount, int maxCount, Vector2Int coord, int worldSeed, float spacingBuffer, float borderPadding = 5f, bool checkExistingData = false, List<Rect> preOccupiedAreas = null, TileType tileType = TileType.HardStone)
    {
        // [DEBUG] 호출 추적
        //Debug.Log($"[GenerateRocks] Called for chunk {coord}, checkExistingData={checkExistingData}");

        // 1. 유효성 검사
        if (rockPrefabs == null || rockPrefabs.Count == 0 || chunk == null) return;

        // Ground Check — 일반 배치와 굴 벽 패스가 **같은 판정**을 써야 한다.
        // (여기서 갈리면 한쪽만 이미 판 자리에 돌을 놓는다)
        var chunkData = chunk.GetData();
        System.Func<int, int, bool> isGround = (x, y) =>
        {
            if ((uint)x >= (uint)chunk.width || (uint)y >= (uint)chunk.height) return false;
            int idx = y * chunk.width + x;
            if (idx < 0 || idx >= chunkData.BasePixels.Length) return false;
            return chunkData.BasePixels[idx].a > 0;
        };

        // 2. Safe Layout 계산 (RockLayoutCalculator)
        List<RockData> rocksToSpawn = RockLayoutCalculator.GetRockLayout(
            coord,
            chunk.width,
            chunk.height,
            rockPrefabs,
            4, // Target count fixed to 4
            worldSeed,
            spacingBuffer,
            borderPadding,
            isGround,
            preOccupiedAreas
        );
        
        //Debug.Log($"[GenerateRocks] Layout calculated. Candidates: {rocksToSpawn.Count}");

        // 3. 실제 생성 (RockSpawner)
        var occupied = new List<Rect>();
        if (preOccupiedAreas != null) occupied.AddRange(preOccupiedAreas);

        foreach (var rock in rocksToSpawn)
        {
            if (!TrySpawnRock(chunk, rock, tileType, checkExistingData)) continue;
            occupied.Add(rock.bounds);
        }

        // 4. 굴 벽 전용 패스 — 일반 배치만으로는 굴 안이 휑하다(아래 메서드 주석 참고).
        GenerateCaveEdgeRocks(chunk, rockPrefabs, coord, worldSeed, spacingBuffer,
                              borderPadding, checkExistingData, occupied, tileType, isGround);
    }

    /// <summary>
    /// 레이아웃 한 건을 실제로 스폰한다. 일반 배치와 굴 벽 패스가 같은 절차를 써야 해서 뽑았다.
    /// </summary>
    private static bool TrySpawnRock(TerrainChunk chunk, RockData rock, TileType tileType, bool checkExistingData)
    {
        // 지형 데이터 검증 (배치 가능 여부 확인)
        if (!RockSpawner.IsValidPlacement(chunk, rock, checkExistingData)) return false;

        // 오브젝트 생성 (Visual)
        // [Fix] 배치 단계에서 결정된 회전·스케일을 그대로 전달 → layout 마진과 실제 렌더 크기 일치
        GameObject rockObj = RockSpawner.SpawnRockObject(chunk, rock, tileType, rock.angle, rock.scale);
        if (rockObj == null) return false; // 프리팹이 비었거나 필수 컴포넌트 누락 — 스포너가 경고를 남긴다

        // 청크 픽셀 범위 설정 및 SpawnedRocks 등록
        DiggableRock dr = rockObj.GetComponent<DiggableRock>();
        dr.PixelBoundsInChunk = new RectInt(
            (int)rock.bounds.x, (int)rock.bounds.y,
            (int)rock.bounds.width, (int)rock.bounds.height);
        chunk.AddSpawnedRock(dr);

        // [New] Cache bounds for neighbor lookup
        chunk.AddRockBound(rock.bounds);
        return true;
    }

    /// <summary>
    /// 굴 가장자리에 바위를 따로 깐다.
    ///
    /// 왜 필요한가 — 일반 배치는 청크당 4개 고정이고 자리를 균등하게 뿌린다. 굴은 청크 면적의
    /// 15% 남짓이라 확률적으로 굴에 걸치는 바위가 0~1개뿐이고, 실제로 "2.5청크를 팠는데 굴 안
    /// 바위가 한 개"라는 리포트가 나왔다(2026-09-04).
    ///
    /// (2026-09-04 2차) 처음 구현은 "레이아웃을 20개 뽑아 굴에 닿는 것만 고른다"는 **기각 표집**이었는데,
    /// F12 리포트(2026-09-04_183828)의 placementProbe 가 그 방식이 왜 안 되는지 그대로 찍었다:
    /// 청크 표본 225개 중 "굴에 접하면서 배치가 허용되는" 자리는 10개(4.4%)뿐이다.
    /// 후보 20개 * 4.4% = 기대 0.9개 &lt; 목표 3개 — 뽑기에 맡기면 대부분의 청크에서 0개가 나온다.
    /// 그래서 청크 전체에 뿌리고 거르는 대신 **굴 벽을 직접 찾아가서** 그 자리에만 놓는다.
    ///
    /// 방식 — 굴 판정을 성긴 격자(CaveScanStep)로 한 번 훑어 "굴이 아니면서 이웃 칸이 굴인"
    /// 벽 칸을 모으고, 그중 필요한 수만큼 골라 굴 쪽으로 바짝 붙여 놓는다.
    /// 격자 캐시가 핵심이다 — IsFutureCaveAt 한 번이 8방향 침식 탭(IsCaveEroded)이라 비싸서,
    /// 이웃 판정 때문에 같은 칸을 두 번 묻기 시작하면 비용이 4배가 된다.
    ///
    /// 앵커 규칙(중심은 흙, 3탭 이상 굴이면 거부)은 RockSpawner.IsValidPlacement 가 그대로 적용한다
    /// → 굴이 뚫려도 바위는 벽·바닥·천장에 물린 채 튀어나온다. 공중에 뜨는 경우가 없다.
    ///
    /// ⚠ 이미 저장된 청크는 RestoreRocks 경로라 이 패스가 돌지 않는다 — 기존 세이브에서 이미
    ///   방문한 청크에는 굴 바위가 생기지 않는다(저장된 배치가 단일 진실 소스이므로 의도된 동작).
    /// </summary>
    private static void GenerateCaveEdgeRocks(TerrainChunk chunk, List<GameObject> rockPrefabs,
                                              Vector2Int coord, int worldSeed, float spacingBuffer,
                                              float borderPadding, bool checkExistingData,
                                              List<Rect> occupied, TileType tileType,
                                              System.Func<int, int, bool> isGround)
    {
        if (chunk == null || !chunk.HasHiddenCave) return;

        int want = chunk.CaveRockCount;
        if (want <= 0) return;

        List<CaveWallSpot> wall = CollectCaveWall(chunk, borderPadding);
        if (wall.Count == 0) return;

        // 일반 배치와 다른 시퀀스 — 같은 자리를 다시 뽑지 않게
        var prng = new System.Random(((coord.x * HASH_X) ^ (coord.y * HASH_Y) ^ worldSeed) ^ 0x2C1B3A5D);
        ShuffleInPlace(wall, prng);

        int placed = 0;
        for (int i = 0; i < wall.Count && placed < want; i++)
        {
            int typeIdx = prng.Next(rockPrefabs.Count);
            GameObject prefab = rockPrefabs[typeIdx];
            Sprite sprite = GetNormalSprite(prefab);
            if (sprite == null) continue;

            float angle = -100f + (float)prng.NextDouble() * 200f;
            const float scale = 1f;

            // 격자 간격만큼 떨어진 채로 놓으면 굴이 열려도 바위가 흙 속에 묻혀 안 드러난다.
            // 굴 바로 앞 픽셀까지 밀어붙인다.
            Vector2Int anchor = HugCaveWall(chunk, wall[i], isGround);

            if (!TryBuildRockAt(chunk, prefab, typeIdx, sprite, anchor, angle, scale,
                                borderPadding, spacingBuffer, occupied, isGround, out RockData rock))
                continue;

            if (!TrySpawnRock(chunk, rock, tileType, checkExistingData)) continue;

            occupied.Add(rock.bounds);
            placed++;
        }
    }

    /// <summary>굴 벽 후보 한 칸. dir 는 "굴이 있는 쪽"이다(바위를 그쪽으로 붙인다).</summary>
    public struct CaveWallSpot
    {
        public int x, y;
        public int dirX, dirY;
    }

    /// <summary>굴 판정 격자 간격(px). 바위 반경(~50px)보다 촘촘하게 훑을 이유가 없다.</summary>
    private const int CaveScanStep = 64;

    /// <summary>벽에 붙일 때의 이동 단위(px).</summary>
    private const int CaveHugStep = 8;

    /// <summary>
    /// 굴 벽(= 굴이 아니면서 이웃 칸이 굴인 자리)을 성긴 격자로 모은다.
    ///
    /// 굴 판정은 칸마다 **한 번만** 한다. IsFutureCaveAt 은 내부적으로 8방향 침식 탭이라
    /// 한 번이 최대 27회 noise 다 — 이웃을 볼 때마다 다시 물으면 비용이 4배가 된다.
    /// 그래서 1패스에서 격자를 캐시하고 2패스는 배열만 읽는다.
    /// </summary>
    // (public: F12 진단(CaveRockDiagnostics)이 "이 청크에서 굴 벽을 몇 칸 찾았나"를
    //  같은 함수로 물어봐야 리포트와 실제 배치가 어긋나지 않는다)
    public static List<CaveWallSpot> CollectCaveWall(TerrainChunk chunk, float borderPadding)
    {
        var result = new List<CaveWallSpot>();

        int margin = Mathf.CeilToInt(borderPadding);
        int nx = (chunk.width  - 2 * margin) / CaveScanStep + 1;
        int ny = (chunk.height - 2 * margin) / CaveScanStep + 1;
        if (nx < 3 || ny < 3) return result;

        bool[] isCave = new bool[nx * ny];
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
                isCave[j * nx + i] = chunk.IsFutureCaveAt(margin + i * CaveScanStep,
                                                          margin + j * CaveScanStep);

        for (int j = 1; j < ny - 1; j++)
        {
            for (int i = 1; i < nx - 1; i++)
            {
                int c = j * nx + i;
                if (isCave[c]) continue;   // 굴 한복판 = 놓으면 공중에 뜬다

                int dirX = 0, dirY = 0;
                if      (isCave[c + 1])  dirX =  1;
                else if (isCave[c - 1])  dirX = -1;
                else if (isCave[c + nx]) dirY =  1;
                else if (isCave[c - nx]) dirY = -1;
                else continue;

                result.Add(new CaveWallSpot
                {
                    x = margin + i * CaveScanStep,
                    y = margin + j * CaveScanStep,
                    dirX = dirX,
                    dirY = dirY
                });
            }
        }

        return result;
    }

    /// <summary>굴이 시작되기 직전 픽셀까지 앵커를 밀어붙인다. 흙이 아닌 칸(이미 판 자리)에서도 멈춘다.</summary>
    private static Vector2Int HugCaveWall(TerrainChunk chunk, CaveWallSpot spot,
                                          System.Func<int, int, bool> isGround)
    {
        var best = new Vector2Int(spot.x, spot.y);
        for (int s = CaveHugStep; s < CaveScanStep; s += CaveHugStep)
        {
            int px = spot.x + spot.dirX * s;
            int py = spot.y + spot.dirY * s;
            if (chunk.IsFutureCaveAt(px, py)) break;
            if (isGround != null && !isGround(px, py)) break;
            best = new Vector2Int(px, py);
        }
        return best;
    }

    /// <summary>
    /// 앵커 좌표 하나로 RockData 를 조립한다. 경계·간격·footprint 검사는 일반 배치
    /// (RockLayoutCalculator)와 **같은 함수**를 쓴다 — 굴 패스만 다른 기준을 쓰면 두 배치가 어긋난다.
    /// </summary>
    private static bool TryBuildRockAt(TerrainChunk chunk, GameObject prefab, int typeIdx, Sprite sprite,
                                       Vector2Int anchor, float angle, float scale,
                                       float borderPadding, float spacingBuffer, List<Rect> occupied,
                                       System.Func<int, int, bool> isGround, out RockData rock)
    {
        rock = default;

        Vector2 pivot  = sprite.pivot;
        Vector2 offset = sprite.textureRectOffset;
        float w = sprite.textureRect.width;
        float h = sprite.textureRect.height;

        RockLayoutCalculator.GetTransformedExtents(w, h, pivot, offset, angle, scale,
            out float exMinX, out float exMaxX, out float exMinY, out float exMaxY);

        // 청크 밖으로 삐져나오면 버린다 — 이음매에서 잘려 보인다
        if (anchor.x + exMinX < borderPadding || anchor.x + exMaxX > chunk.width  - borderPadding) return false;
        if (anchor.y + exMinY < borderPadding || anchor.y + exMaxY > chunk.height - borderPadding) return false;

        Rect bounds = new Rect(Mathf.Round(anchor.x - pivot.x), Mathf.Round(anchor.y - pivot.y), w, h);
        Vector2 center = new Vector2(bounds.x + pivot.x, bounds.y + pivot.y);

        if (CollisionChecker.IsCollidingWithRects(bounds, occupied, spacingBuffer)) return false;

        // footprint 전체가 흙인지. 굴은 아직 안 뚫려 흙이라 통과하고, 이미 판 자리만 걸러진다.
        if (isGround != null && sprite.texture != null && sprite.texture.isReadable)
        {
            Color32[] mask = RockSpawner.GetOrCreateMask(sprite);
            RockSpawner.TryGetMaskSize(sprite, out int mw, out int mh);
            if (mask != null && !RockLayoutCalculator.IsFootprintGrounded(
                    mask, mw, mh, pivot, offset, angle, scale, center.x, center.y, isGround))
                return false;
        }

        rock = new RockData
        {
            bounds         = bounds,
            center         = center,
            radius         = Mathf.Max(exMaxX - exMinX, exMaxY - exMinY) * 0.5f,
            prefab         = prefab,
            sprite         = sprite,
            pivotX         = (int)pivot.x,
            pivotY         = (int)pivot.y,
            spriteSetIndex = typeIdx,
            angle          = angle,
            scale          = scale
        };
        return true;
    }

    /// <summary>Fisher-Yates. 청크 시드 prng 로 돌리므로 재로드해도 같은 순서가 나온다.</summary>
    private static void ShuffleInPlace<T>(List<T> list, System.Random prng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = prng.Next(i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    /// <summary>
    /// 저장된 암석 배치 데이터로 암석을 복원합니다.
    /// GetRockLayout()을 호출하지 않으므로 BasePixels 상태와 무관하게 항상 동일한 위치에 복원됩니다.
    /// </summary>
    public static void RestoreRocks(TerrainChunk chunk, RockSaveEntry[] savedRocks,
        List<GameObject> rockPrefabs, TileType tileType)
    {
        if (savedRocks == null || rockPrefabs == null || chunk == null) return;

        foreach (var entry in savedRocks)
        {
            if (entry == null) continue;
            if (entry.spriteSetIndex < 0 || entry.spriteSetIndex >= rockPrefabs.Count) continue;

            GameObject prefab = rockPrefabs[entry.spriteSetIndex];
            Sprite normal = GetNormalSprite(prefab);
            if (normal == null) continue;

            int pivotX = (int)normal.pivot.x;
            int pivotY = (int)normal.pivot.y;

            RockData rock = new RockData();
            rock.bounds       = new Rect(entry.boundsX, entry.boundsY, entry.boundsW, entry.boundsH);
            rock.center       = new Vector2(entry.boundsX + pivotX, entry.boundsY + pivotY);
            rock.radius       = Mathf.Max(entry.boundsW, entry.boundsH) * 0.5f;
            rock.prefab         = prefab;
            rock.sprite         = normal;
            rock.pivotX         = pivotX;
            rock.pivotY         = pivotY;
            rock.spriteSetIndex = entry.spriteSetIndex;

            float savedScale = entry.scale == 0f ? 1f : entry.scale; // 0 = 구버전 데이터
            GameObject rockObj = RockSpawner.SpawnRockObject(chunk, rock, tileType, entry.angle, savedScale);
            if (rockObj == null) continue;

            DiggableRock dr = rockObj.GetComponent<DiggableRock>();
            dr.PixelBoundsInChunk = new RectInt(entry.boundsX, entry.boundsY, entry.boundsW, entry.boundsH);
            chunk.AddSpawnedRock(dr);
            chunk.AddRockBound(rock.bounds);

            // 저장된 HP·회복타이머·노출 상태 복원
            dr.RestoreState(entry.savedHp, entry.lastDamageTime, entry.isRevealed);
        }
    }

    // [Facade] Generate Elevator using ElevatorSpawner
    // Returns: Rect? representing the occupied quadrant (for rock generation exclusion)
    public static Rect? GenerateElevator(TerrainChunk chunk, int xChunk, int yChunk, int layerIndex, int worldSeed, bool skipPixelClear)
    {
        return ElevatorSpawner.GenerateElevator(chunk, xChunk, yChunk, layerIndex, worldSeed, skipPixelClear);
    }
}
