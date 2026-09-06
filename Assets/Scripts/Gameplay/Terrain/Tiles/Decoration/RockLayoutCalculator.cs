// @tags: decoration, rock, generation, layout, utility
using UnityEngine;
using System.Collections.Generic;

public static class RockLayoutCalculator
{
    // [Constants] Moved from TerrainDecorator
    private const int HASH_X = 73856093;
    private const int HASH_Y = 19349663;

    // [Refactoring] Independent Safe Zone Generation (Radius Based) - Quadrant Division
    public static List<TerrainDecorator.RockData> GetRockLayout(
        Vector2Int coord,
        int width,
        int height,
        List<GameObject> rockPrefabs,
        int targetCount,
        int worldSeed,
        float spacingBuffer,
        float borderPadding, // [New] Injected dynamic padding based on chunk texture thickness
        System.Func<int, int, bool> isGround, // [New] Ground Check Callback
        List<Rect> preOccupiedAreas = null)
    {
        List<TerrainDecorator.RockData> results = new List<TerrainDecorator.RockData>();
        if (rockPrefabs == null || rockPrefabs.Count == 0) return results;

        // 1. 결정론적 난수 생성기 초기화
        // [Fix] 타입 prng / 위치 prng 분리 → 위치 배치 실패로 타입 시퀀스가 어긋나는 버그 방지
        // 위치 배치가 실패해도 typePrng는 슬롯당 1번만 소비 → 재로드 후에도 타입 안정적
        int baseSeed = (coord.x * HASH_X) ^ (coord.y * HASH_Y) ^ worldSeed;
        System.Random typePrng = new System.Random(baseSeed);
        System.Random posPrng  = new System.Random(baseSeed ^ 0x5F3759DF);

        // 2. 청크를 4개의 Quadrant로 분할
        float halfWidth  = width  * 0.5f;
        float halfHeight = height * 0.5f;

        Rect[] quadrants = new Rect[4]
        {
            new Rect(0,         0,          halfWidth, halfHeight), // 좌하
            new Rect(halfWidth, 0,          halfWidth, halfHeight), // 우하
            new Rect(0,         halfHeight, halfWidth, halfHeight), // 좌상
            new Rect(halfWidth, halfHeight, halfWidth, halfHeight)  // 우상
        };

        // 3. 각 Quadrant에 균등하게 돌 분배
        int rocksPerQuadrant = targetCount / 4;
        int remainingRocks   = targetCount % 4;

        for (int q = 0; q < 4; q++)
        {
            Rect quadrant      = quadrants[q];
            int  quadrantTarget = rocksPerQuadrant + (q < remainingRocks ? 1 : 0);

            int failedAttempts = 0;

            // [Fix] 슬롯(배치할 암석 수)을 외부 루프로, 위치 재시도를 내부 루프로 분리
            // typePrng는 슬롯당 1번만 호출 → 위치 실패 횟수와 무관하게 타입 시퀀스 고정
            for (int slot = 0; slot < quadrantTarget; slot++)
            {
                // 타입 선택 — 이 슬롯에서 위치 시도가 몇 번 실패해도 타입은 고정
                int typeIdx = typePrng.Next(rockPrefabs.Count);
                GameObject selectedPrefab = rockPrefabs[typeIdx];
                Sprite selectedSprite = TerrainDecorator.GetNormalSprite(selectedPrefab);
                if (selectedSprite == null) continue;

                Vector2 pivot = selectedSprite.pivot;
                Vector2 offset = selectedSprite.textureRectOffset;
                float w = selectedSprite.textureRect.width;
                float h = selectedSprite.textureRect.height;

                // [Fix] 변환된 footprint 전체가 solid 지형에 들어가는지 검증하기 위해 마스크 로드.
                // 텍스처가 readable 하지 않으면 null → 중심 1픽셀 체크로 폴백.
                Color32[] shapeMask = null;
                int smW = 0, smH = 0;
                if (selectedSprite.texture != null && selectedSprite.texture.isReadable)
                {
                    shapeMask = RockSpawner.GetOrCreateMask(selectedSprite);
                    RockSpawner.TryGetMaskSize(selectedSprite, out smW, out smH);
                }

                // [Fix] 회전·스케일을 위치보다 먼저 결정 (posPrng로 결정론적)
                // → spawn 단계의 Random.Range 대신 이 값을 fixedAngle/fixedScale로 전달해
                //   layout이 계산한 경계 마진과 실제 렌더 크기가 항상 일치하게 만든다.
                // 슬롯당 1번만 결정 → 위치 재시도 횟수와 무관하게 크기 고정.
                float angle = -100f + (float)posPrng.NextDouble() * 200f; // [-100, 100]
                float scale = 1.0f; // 크기 랜덤 제거 — 원본 크기 고정 (회전만 랜덤)

                // 회전·스케일이 적용된 실제 외곽(피벗 기준 부호 있는 좌표) 계산
                GetTransformedExtents(w, h, pivot, offset, angle, scale,
                    out float exMinX, out float exMaxX, out float exMinY, out float exMaxY);

                float radius = Mathf.Max(exMaxX - exMinX, exMaxY - exMinY) * 0.5f;

                // Safe Zone: 변환된 외곽이 (Quadrant 내부 + 테두리 패딩) 안에 들어오도록
                // 앵커(피벗=actualCenter)가 가질 수 있는 범위를 역산한다.
                float minCX = quadrant.x    + borderPadding - exMinX;
                float maxCX = quadrant.xMax - borderPadding - exMaxX;
                float minCY = quadrant.y    + borderPadding - exMinY;
                float maxCY = quadrant.yMax - borderPadding - exMaxY;

                if (minCX >= maxCX || minCY >= maxCY) continue;

                // 위치 재시도 (posPrng만 소비 → typePrng 불변)
                int maxPosAttempts = 10;
                bool placed = false;

                for (int attempt = 0; attempt < maxPosAttempts; attempt++)
                {
                    float cx = minCX + (float)posPrng.NextDouble() * (maxCX - minCX);
                    float cy = minCY + (float)posPrng.NextDouble() * (maxCY - minCY);

                    // 원본 w×h bounds(bottom-left) — 저장/복원·픽셀 검증과의 호환 유지
                    Rect alignedBounds = new Rect(
                        Mathf.Round(cx - pivot.x),
                        Mathf.Round(cy - pivot.y),
                        w, h);

                    Vector2 actualCenter = new Vector2(
                        alignedBounds.x + pivot.x,
                        alignedBounds.y + pivot.y);

                    // Collision Check (Radial) — 변환된 반경 기준
                    if (CollisionChecker.IsCollidingRadial(actualCenter, radius, results, spacingBuffer))
                    {
                        failedAttempts++;
                        continue;
                    }

                    // Ground Check — [Fix] 변환된(확대·회전) 마스크 footprint 전체가 solid 지형 안에
                    // 들어가는지 검증. 일부라도 비어있는(air) 공간과 겹치면 노출 시 그쪽으로 돌이
                    // 튀어나와 보이므로 거부한다. 마스크 없으면 중심 1픽셀로 폴백.
                    if (isGround != null)
                    {
                        bool grounded = (shapeMask != null)
                            ? IsFootprintGrounded(shapeMask, smW, smH, pivot, offset, angle, scale,
                                                  actualCenter.x, actualCenter.y, isGround)
                            : isGround((int)actualCenter.x, (int)actualCenter.y);
                        if (!grounded)
                        {
                            failedAttempts++;
                            continue;
                        }
                    }

                    // Collision Check (PreOccupied)
                    if (preOccupiedAreas != null &&
                        CollisionChecker.IsCollidingWithRects(alignedBounds, preOccupiedAreas, spacingBuffer))
                        continue;

                    // Valid!
                    TerrainDecorator.RockData data = new TerrainDecorator.RockData();
                    data.center      = actualCenter;
                    data.radius      = radius;
                    data.prefab      = selectedPrefab;
                    data.sprite      = selectedSprite;
                    data.bounds        = alignedBounds;
                    data.pivotX        = (int)pivot.x;
                    data.pivotY        = (int)pivot.y;
                    data.spriteSetIndex = typeIdx;  // [Save] 복원 시 동일 종류 재현용
                    data.angle         = angle;     // [Fix] spawn에 전달할 확정 회전
                    data.scale         = scale;     // [Fix] spawn에 전달할 확정 스케일

                    results.Add(data);
                    placed = true;
                    break;
                }

                if (!placed)
                    failedAttempts++;
            }

            // if (failedAttempts > 0)
            //     Debug.LogWarning($"[RockLayoutCalculator] Quadrant {q}: Failed {failedAttempts} placement attempts");
        }

        return results;
    }

    // [Helper] 원본 w×h 스프라이트를 angle(도, CCW) + 균등 scale로 변환했을 때의
    // AABB 범위를 피벗 기준 부호 있는 좌표로 반환한다.
    // RockSpawner.TransformMask의 코너 변환 수식과 동일 → spawn 결과와 정확히 일치.
    // (public: 굴 벽 전용 배치 패스가 여기 없이 같은 수식을 다시 쓰면 두 배치가 어긋난다)
    public static void GetTransformedExtents(
        float w, float h, Vector2 pivot, Vector2 offset, float angleDeg, float scale,
        out float minX, out float maxX, out float minY, out float maxY)
    {
        float rad  = angleDeg * Mathf.Deg2Rad;
        float cosA = Mathf.Cos(rad);
        float sinA = Mathf.Sin(rad);

        float[] dx = { offset.x - pivot.x, offset.x + w - pivot.x, offset.x - pivot.x,    offset.x + w - pivot.x };
        float[] dy = { offset.y - pivot.y, offset.y - pivot.y,    offset.y + h - pivot.y, offset.y + h - pivot.y };

        minX = float.MaxValue; maxX = float.MinValue;
        minY = float.MaxValue; maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float tx = (dx[i] * cosA - dy[i] * sinA) * scale;
            float ty = (dx[i] * sinA + dy[i] * cosA) * scale;
            if (tx < minX) minX = tx; if (tx > maxX) maxX = tx;
            if (ty < minY) minY = ty; if (ty > maxY) maxY = ty;
        }
    }

    // [Helper] 변환된(angle·scale 적용) 마스크의 불투명 픽셀들이 모두 solid 지형 위에 있는지 검사.
    // 원본 마스크 픽셀(mx,my)을 피벗 기준 회전·스케일해 앵커(actualCenter) 기준 지형 좌표로 매핑한다.
    // 매핑 수식은 RockSpawner.TransformMask / DiggableRock.CountExposedRockPixels와 동일.
    // 비용 제한을 위해 step 간격으로 샘플링한다 (청크 생성 시 1회성 연산).
    // (public: 굴 벽 전용 배치 패스도 같은 검사를 써야 "판 자리에 겹쳐 놓기"가 양쪽에서 막힌다)
    public static bool IsFootprintGrounded(
        Color32[] mask, int mw, int mh, Vector2 pivot, Vector2 offset,
        float angleDeg, float scale, float anchorX, float anchorY,
        System.Func<int, int, bool> isGround)
    {
        if (isGround == null || mask == null || mw <= 0 || mh <= 0) return true;

        float rad  = angleDeg * Mathf.Deg2Rad;
        float cosA = Mathf.Cos(rad);
        float sinA = Mathf.Sin(rad);

        int step = Mathf.Max(2, Mathf.RoundToInt(Mathf.Min(mw, mh) / 12f));

        for (int my = 0; my < mh; my += step)
        {
            for (int mx = 0; mx < mw; mx += step)
            {
                if (mask[my * mw + mx].a <= 10) continue; // 모양 밖(투명) 픽셀은 무시

                float dx = mx + offset.x - pivot.x;
                float dy = my + offset.y - pivot.y;
                float tx = (dx * cosA - dy * sinA) * scale;
                float ty = (dx * sinA + dy * cosA) * scale;

                int px = Mathf.RoundToInt(anchorX + tx);
                int py = Mathf.RoundToInt(anchorY + ty);
                if (!isGround(px, py)) return false;
            }
        }
        return true;
    }

    // [Helper] 결정론적 난수 생성기 반환
    private static System.Random GetDeterministicRandom(Vector2Int coord, int worldSeed)
    {
        int seed = (coord.x * HASH_X) ^ (coord.y * HASH_Y) ^ worldSeed;
        return new System.Random(seed);
    }
}
