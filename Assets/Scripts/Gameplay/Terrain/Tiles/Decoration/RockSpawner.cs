// @tags: decoration, rock, spawn, pool, generation, chunk
using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;

public static class RockSpawner
{
    // [최적화 2차] 바위 종류(이름)별로 오브젝트 풀 분리
    private static Dictionary<int, Queue<GameObject>> _rockPools = new Dictionary<int, Queue<GameObject>>();

    // 스프라이트당 1회만 추출한 픽셀 마스크 캐시
    private static readonly Dictionary<int, Color32[]> _maskCache = new Dictionary<int, Color32[]>();
    private static readonly Dictionary<int, (int w, int h)> _maskSizeCache = new Dictionary<int, (int, int)>();

    public static Color32[] GetOrCreateMask(Sprite sprite)
    {
        int key = sprite.GetInstanceID();
        if (_maskCache.TryGetValue(key, out Color32[] cached)) return cached;

        Rect r = sprite.textureRect;
        int texWidth = sprite.texture.width;
        NativeArray<Color32> native = sprite.texture.GetPixelData<Color32>(0);
        int rx = (int)r.x, ry = (int)r.y;
        int w = Mathf.Min((int)r.width,  texWidth - rx);
        int h = Mathf.Min((int)r.height, native.Length / texWidth - ry);
        Color32[] mask = new Color32[w * h];
        for (int my = 0; my < h; my++)
        {
            int texRow = (ry + my) * texWidth + rx;
            int maskRow = my * w;
            for (int mx = 0; mx < w; mx++)
                mask[maskRow + mx] = native[texRow + mx];
        }

        _maskCache[key] = mask;
        _maskSizeCache[key] = (w, h);
        return mask;
    }

    // [Fix] layout 단계에서 변환된 footprint 검증을 위해 마스크 크기 노출.
    // GetOrCreateMask()로 캐시가 채워진 뒤에만 유효.
    public static bool TryGetMaskSize(Sprite sprite, out int w, out int h)
    {
        if (sprite != null && _maskSizeCache.TryGetValue(sprite.GetInstanceID(), out var s))
        {
            w = s.w; h = s.h;
            return true;
        }
        w = 0; h = 0;
        return false;
    }

    public static void ReturnToPool(GameObject rock)
    {
        if (rock == null) return;

        rock.SetActive(false);
        rock.transform.SetParent(null);

        // [Fix] DiggableRock.poolKey에 캐시된 프리팹 인스턴스 ID 사용
        // 이름 기반이던 이전 방식은 충돌로 다른 타일타입의 바위와 뒤섞이는 심각한 버그가 있었다.
        DiggableRock drComp = rock.GetComponent<DiggableRock>();
        int poolKey = (drComp != null && drComp.poolKey != 0)
            ? drComp.poolKey
            : 0;

        if (!_rockPools.ContainsKey(poolKey))
        {
            _rockPools[poolKey] = new Queue<GameObject>();
        }
        _rockPools[poolKey].Enqueue(rock);
    }

    public static bool IsValidPlacement(TerrainChunk chunk, TerrainDecorator.RockData rock, bool checkExistingData)
    {
        int startX = (int)rock.bounds.x;
        int startY = (int)rock.bounds.y;
        int width = chunk.width;
        int centerX = startX + rock.pivotX;
        int centerY = startY + rock.pivotY;
        int centerIdx = centerY * width + centerX;

        if (centerIdx < 0 || centerIdx >= chunk.GetData().BasePixels.Length) return false;

        byte pixelType = 0;
        if (chunk.GetData().PixelInfo.IsCreated) 
            pixelType = chunk.GetData().PixelInfo[centerIdx];

        if (checkExistingData)
        {
            if (pixelType == 3) return false;
            if (pixelType == 2) return true;
            if (chunk.GetData().BasePixels[centerIdx].a == 0) return false;
        }
        else
        {
            if (pixelType == 2) return true;
            if (chunk.GetData().BasePixels[centerIdx].a == 0) return false;
        }

        // [굴] 굴 안에 돌이 있는 건 좋지만 **떠 있으면 안 된다.** 돌은 광물과 달리 떨어지지도
        // 않아서, 굴 한복판에 놓이면 공중에 그대로 박제된다.
        //
        // 규칙은 앵커 기준이다 — 중심 픽셀이 흙이면 굴이 뚫려도 그 자리는 지형으로 남으므로
        // 돌은 반드시 어딘가에 물려 있다. 몸통이 굴 쪽으로 튀어나오는 건 오히려 원하는 그림이라
        // (바닥에 얹힌 바위 / 벽에서 튀어나온 바위 / 천장에 붙은 바위) 그대로 허용한다.
        //
        // 다만 4방향 탭 중 3개 이상이 굴이면 실제로 물린 데가 실낱 같은 목뿐이라 떠 보인다.
        // 그건 거른다. 탭 거리는 footprint 의 절반(= 반지름의 절반)이라 돌 크기를 따라간다.
        if (chunk.IsFutureCaveAt(centerX, centerY)) return false;

        int quarterW = Mathf.Max(1, (int)rock.bounds.width  / 4);
        int quarterH = Mathf.Max(1, (int)rock.bounds.height / 4);
        if (chunk.CountFutureCaveAround(centerX, centerY, quarterW, quarterH) >= 3) return false;

        return true;
    }

    public static GameObject SpawnRockObject(TerrainChunk chunk, TerrainDecorator.RockData rock, TileType tileType = TileType.HardStone, float? fixedAngle = null, float? fixedScale = null)
    {
        if (rock.prefab == null || rock.sprite == null)
        {
            Debug.LogWarning("[RockSpawner] 돌 프리팹 또는 정상 스프라이트가 비어 있습니다. TileVisualSettings의 rockPrefabs를 확인하세요.");
            return null;
        }

        int startX = (int)rock.bounds.x;
        int startY = (int)rock.bounds.y;

        // 풀 키는 프리팹 단위다. 스프라이트가 아니라 프리팹이 오브젝트의 정체성이므로
        // 같은 스프라이트를 쓰는 다른 프리팹(HP·컴포넌트 구성이 다른)이 뒤섞이지 않는다.
        int poolKey = rock.prefab.GetInstanceID();
        GameObject rockObj = null;

        if (_rockPools.TryGetValue(poolKey, out Queue<GameObject> pool))
        {
            while (pool.Count > 0)
            {
                GameObject pooledObj = pool.Dequeue();
                if (pooledObj != null)
                {
                    rockObj = pooledObj;
                    break;
                }
            }
        }

        if (rockObj != null)
        {
            rockObj.transform.SetParent(chunk.transform, false);
            rockObj.SetActive(true);
            // 균열 스프라이트 복구는 DiggableRock.OnEnable이 ratio=1로 브로드캐스트하면서
            // DamageStagedVisuals가 처리한다 — 여기서 손댈 필요 없다.
        }
        else
        {
            rockObj = Object.Instantiate(rock.prefab, chunk.transform, false);
        }

        DiggableRock dr = rockObj.GetComponent<DiggableRock>();
        if (dr == null)
        {
            Debug.LogWarning($"[RockSpawner] 프리팹 '{rock.prefab.name}'에 DiggableRock이 없습니다. " +
                             "SpriteRenderer·PolygonCollider2D·DiggableRock은 반드시 프리팹 root의 같은 GameObject에 있어야 합니다 " +
                             "(파기 호출부가 콜라이더의 GameObject에서 GetComponent<IDiggable>로 찾기 때문).");
            Object.Destroy(rockObj);
            return null;
        }

        // 신규/풀 재사용 공통 레이어 설정
        rockObj.layer = LayerMask.NameToLayer("Ground"); // 플레이어 groundLayer 감지용
        SpriteRenderer sr = rockObj.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            // "ground"라는 정렬 레이어는 프로젝트에 존재하지 않는다(Default/Fog/BackGround/Objects/
            // player/Mineral/Map/LoadingScreen). 잘못된 이름은 조용히 무시되어 실제로는 Default였다 —
            // 지형·광물과 같은 레이어여야 order 비교가 성립하므로 Default를 명시한다.
            sr.sortingLayerID = SortingLayer.NameToID("Default");
            sr.sortingOrder = -1; // 지형 텍스처(sortingOrder=0) 뒤 + 플레이어(-49~-30) 앞. 파진 구멍으로만 노출
        }

        // 이름 규칙 통일 (반납 시 사용)
        rockObj.name = $"ROCK_{rock.sprite.name}_{startX}_{startY}";

        Vector2 pivotOffset = rock.sprite.pivot;
        float ppu = chunk.PPU;
        float targetPixelX = startX + pivotOffset.x;
        float targetPixelY = startY + pivotOffset.y;
        float localX = targetPixelX / ppu;
        float localY = targetPixelY / ppu;

        float randomAngle = fixedAngle ?? Random.Range(-100f, 100f);
        // 크기 랜덤은 제거됐다 — 돌의 크기는 프리팹의 대/중/소가 표현한다(RockBreakVFX.tier).
        // fixedScale은 RockLayoutCalculator가 확정한 값(1.0)이거나 구버전 세이브에 남은 스케일이다.
        float randomScale = fixedScale ?? 1f;

        Vector3    basePos   = new Vector3(localX, localY, 0);
        Quaternion baseRot   = Quaternion.Euler(0, 0, randomAngle);
        Vector3    baseScale = new Vector3(randomScale, randomScale, 1f);

        rockObj.transform.localPosition = basePos;
        rockObj.transform.localRotation = baseRot;
        rockObj.transform.localScale    = baseScale;

        dr.AssignChunk(chunk);         // [Fix] 풀 재사용 시 stale _chunk 갱신 (구멍-돌 위치 불일치 방지)
        dr.tileType = tileType;

        // [필수] 배치값을 transform과 별개로 명시 주입한다.
        // 히트 애니메이션(RockHitAnimator)이 transform을 흔드는 동안에도
        // 지형 픽셀 좌표·세이브가 흔들림 오프셋을 읽지 않게 하는 단일 진실 소스다.
        dr.SetPlacement(new Vector2(localX, localY), randomAngle);

        dr.ApplySizeScale(randomScale); // 시각 스케일 주입 + HP 리필. HP·드롭은 티어(RockTierStats)가 결정

        RockBreakVFX vfx = rockObj.GetComponent<RockBreakVFX>();
        if (vfx != null) vfx.Setup(tileType);

        // 히트 연출도 같은 배치값 위에 얹힌다. 풀에서 재사용된 오브젝트에 남아 있던
        // 이전 흔들림은 SetBase()가 정리한다.
        RockHitAnimator hitAnim = rockObj.GetComponent<RockHitAnimator>();
        if (hitAnim != null) hitAnim.SetBase(basePos, baseRot, baseScale);

        dr.poolKey = poolKey;          // [Fix] 풀 반납 키 캐시
        dr.spriteSetIndex = rock.spriteSetIndex; // [Save] 언로드 시 저장용

        // 스프라이트당 1회 추출된 마스크 주입 후 회전·스케일에 맞게 변환.
        // [Fix] 마스크 캐시는 스프라이트 키다 — poolKey(프리팹 ID)로 조회하면 KeyNotFound가 난다.
        Color32[] mask = GetOrCreateMask(rock.sprite);
        TryGetMaskSize(rock.sprite, out int mw, out int mh);
        var (tMask, tW, tH, tPivot) = TransformMask(mask, mw, mh, rock.sprite.pivot, rock.sprite.textureRectOffset, randomAngle, randomScale);
        dr.SetMask(tMask, tW, tH, tPivot);

        return rockObj;
    }

    // 원본 마스크를 주어진 회전(degree, CCW)과 스케일에 맞게 변환한 새 마스크를 반환한다.
    // newPivot은 변환된 마스크 안에서 원본 pivot이 위치하는 좌표다.
    private static (Color32[] newMask, int newW, int newH, Vector2 newPivot) TransformMask(
        Color32[] mask, int mw, int mh, Vector2 pivot, Vector2 offset, float angleDeg, float scale)
    {
        float rad  = angleDeg * Mathf.Deg2Rad;
        float cosA = Mathf.Cos(rad);
        float sinA = Mathf.Sin(rad);

        // 원본 4 코너를 pivot 기준 상대좌표로 변환한 뒤 rotate+scale → bounding box 계산
        float[] dx = { offset.x - pivot.x, offset.x + mw - pivot.x, offset.x - pivot.x,    offset.x + mw - pivot.x };
        float[] dy = { offset.y - pivot.y, offset.y - pivot.y,      offset.y + mh - pivot.y, offset.y + mh - pivot.y };
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float tx = (dx[i] * cosA - dy[i] * sinA) * scale;
            float ty = (dx[i] * sinA + dy[i] * cosA) * scale;
            if (tx < minX) minX = tx; if (tx > maxX) maxX = tx;
            if (ty < minY) minY = ty; if (ty > maxY) maxY = ty;
        }

        int newW = Mathf.CeilToInt(maxX - minX) + 1;
        int newH = Mathf.CeilToInt(maxY - minY) + 1;
        Vector2 newPivot = new Vector2(-minX, -minY);

        float invScale = 1f / scale;
        Color32[] newMask = new Color32[newW * newH];
        for (int ny = 0; ny < newH; ny++)
        {
            for (int nx = 0; nx < newW; nx++)
            {
                // 출력 픽셀 → pivot 기준 좌표 → 역회전 → 역스케일 → 원본 좌표
                float tx = (nx - newPivot.x) * invScale;
                float ty = (ny - newPivot.y) * invScale;
                int oxi = Mathf.RoundToInt(tx * cosA + ty * sinA + pivot.x - offset.x);
                int oyi = Mathf.RoundToInt(-tx * sinA + ty * cosA + pivot.y - offset.y);
                if ((uint)oxi < (uint)mw && (uint)oyi < (uint)mh)
                    newMask[ny * newW + nx] = mask[oyi * mw + oxi];
            }
        }
        return (newMask, newW, newH, newPivot);
    }
}