// @tags: bug-report, qa, cave, chunk-boundary, seam, diagnostics
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BugReport
{
    /// <summary>
    /// F12 리포트에 **청크 경계 진단**을 얹는다. 씬 배치 불필요 — ExtraContext 한 줄 구독.
    ///
    /// 굴(cave)이 들어오면서 청크 경계 문제가 두 종류로 나타났고, 스크린샷만으로는 둘을 못 가른다:
    ///   1) 지오메트리 절단 — 한쪽은 공기인데 반대쪽은 지형 → 굴이 수직 벽으로 끊긴다
    ///   2) 렌더링 이음매  — 픽셀은 이어졌는데 거리장이 안 맞아 암반 무늬가 갈린다
    /// 이 둘은 고치는 곳이 완전히 다르므로(카빙 vs BoundarySync) 리포트에서 바로 갈라줘야 한다.
    ///
    /// 그리고 chunkScreenX 는 **스크린샷에서 청크 경계가 몇 픽셀인지**를 그대로 적는다.
    /// 이게 없으면 스크린샷을 보고 경계 위치를 역산해야 하는데, 실제로 그러다 UI·플레이어
    /// 스프라이트를 경계로 오인하는 일이 반복됐다.
    ///
    /// 읽는 법: 값이 정상이면 각 항목이 "이상 없음"으로 나온다. 이상 없음이 아닌 줄만 보면 된다.
    /// </summary>
    public static class CaveSeamDiagnostics
    {
        /// <summary>경계 스캔에서 이 픽셀 수 미만의 불일치 구간은 노이즈로 보고 안 적는다.</summary>
        /// <summary>C# 문자열 이스케이프 대신 상수를 쓴다 — 스크립트로 이 파일을 편집할 때
        /// 역슬래시가 실제 줄바꿈으로 바뀌어 문자열이 깨지는 사고가 있었다.</summary>
        private static readonly string NL = ((char)10).ToString();   // "\n" 대신 — 스크립트 편집 시 역슬래시가 깨진다

        private const int MinRunPx = 8;

        /// <summary>거리장 차이가 이 값을 넘으면 무늬가 눈에 띄게 갈린다(5/px 스케일, 50px 밴드 = 250).</summary>
        private const int DistMismatchThreshold = 40;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            BugReportCollector.ExtraContext -= Fill;   // 씬 재진입 중복 구독 방지
            BugReportCollector.ExtraContext += Fill;
        }

        private static void Fill(Dictionary<string, string> bag)
        {
            var map = InfinityMapManager.Instance;
            if (map == null) return;

            var coords = new List<Vector2Int>();
            foreach (var c in map.GetActiveChunkCoords()) coords.Add(c);
            if (coords.Count == 0) return;

            bag["caveConfig"]   = BuildConfig(coords[0]);
            bag["chunkScreenX"] = BuildScreenGuide(coords);
            bag["chunkKinds"]   = BuildKinds(map, coords);
            bag["chunkSeams"]   = BuildSeams(map, coords);
        }

        // ────────────────────────────────────────────────────────────────────
        //  0. 지금 적용 중인 값 — 튜닝 중에는 "그때 무슨 값이었나"가 매번 문제가 된다
        // ────────────────────────────────────────────────────────────────────
        private static string BuildConfig(Vector2Int sample)
        {
            var tdm = TileDataManager.Instance;
            if (tdm == null) return "TileDataManager 없음";

            TileType layer = tdm.GetTileTypeAtPosition(sample.x, sample.y);
            var c = tdm.GetCaveSettings(layer);

            var sb = new StringBuilder();
            sb.Append($"층={layer} rimThicknessPx={TerrainChunk.RimThicknessPx}");
            sb.Append($"{NL}    cave enabled={c.enabled} blobChance={c.blobChance:0.##}");
            sb.Append($" linkChance={c.linkChance:0.##} maxLinked={c.maxLinkedChunks} keepLargest={c.keepLargestOnly}");
            sb.Append($"{NL}    반경={c.blobRadiusMinPx:0}~{c.blobRadiusMaxPx:0}px");
            sb.Append($" 길이={c.blobLengthMinPx:0}~{c.blobLengthMaxPx:0}px");
            sb.Append($" 여백={c.edgeMarginPx:0}px 침식={c.minRadiusPx:0}px");
            return sb.ToString();
        }

        // ────────────────────────────────────────────────────────────────────
        //  1. 스크린샷에서 청크 경계가 어디인지
        // ────────────────────────────────────────────────────────────────────
        private static string BuildScreenGuide(List<Vector2Int> coords)
        {
            var cam = Camera.main;
            if (cam == null) return "카메라 없음";

            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (var c in coords)
            {
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.y > maxY) maxY = c.y;
            }

            var sb = new StringBuilder();
            sb.Append("스크린샷 좌표계(좌상단 0,0) 기준 청크 경계 위치");

            for (int cx = minX; cx <= maxX + 1; cx++)
            {
                float worldX = cx * ChunkCoords.WorldSize;
                float sx = cam.WorldToScreenPoint(new Vector3(worldX, cam.transform.position.y, 0f)).x;
                sb.Append($"\n    세로선 월드x={worldX,6:0.#} → 화면x={sx,7:0}");
                if (sx < 0 || sx > Screen.width) sb.Append("  (화면 밖)");
            }

            for (int cy = minY; cy <= maxY + 1; cy++)
            {
                float worldY = cy * ChunkCoords.WorldSize;
                float sy = cam.WorldToScreenPoint(new Vector3(cam.transform.position.x, worldY, 0f)).y;
                float imgY = Screen.height - sy;   // 스크린샷은 위가 0
                sb.Append($"\n    가로선 월드y={worldY,6:0.#} → 화면y={imgY,7:0}");
                if (imgY < 0 || imgY > Screen.height) sb.Append("  (화면 밖)");
            }

            return sb.ToString();
        }

        // ────────────────────────────────────────────────────────────────────
        //  2. 청크별 종류 — 특수청크는 굴을 안 판다(이웃이 통로를 뻗으면 벽으로 끝난다)
        // ────────────────────────────────────────────────────────────────────
        private static string BuildKinds(InfinityMapManager map, List<Vector2Int> coords)
        {
            coords.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));

            var sb = new StringBuilder();
            sb.Append("좌표    종류        공기%  좌끝공기  우끝공기  파인적");
            foreach (var coord in coords)
            {
                var chunk = map.GetChunk(coord);
                if (chunk == null) { sb.Append($"\n    {coord.x},{coord.y}  (TerrainChunk 아님)"); continue; }

                var data = chunk.GetData();
                if (data == null || !data.BasePixels.IsCreated) { sb.Append($"\n    {coord.x},{coord.y}  (픽셀 없음)"); continue; }

                chunk.EnsureJobsCompleted();

                // 특수청크는 프리팹 픽셀을 쓰므로 굴을 안 판다 — 이웃이 통로를 뻗으면 벽으로 끝난다.
                var init = chunk.GetComponent<IChunkInitializer>();
                string kind = init != null ? $"특수({init.GetType().Name})" : "일반";

                int w = data.Width, h = data.Height;
                int air = 0, total = 0;
                for (int y = 0; y < h; y += 16)
                    for (int x = 0; x < w; x += 16)
                    { total++; if (data.BasePixels[data.ToIndex(x, y)].a == 0) air++; }

                int leftAir = 0, rightAir = 0;
                for (int y = 0; y < h; y++)
                {
                    if (data.BasePixels[data.ToIndex(0, y)].a == 0) leftAir++;
                    if (data.BasePixels[data.ToIndex(w - 1, y)].a == 0) rightAir++;
                }

                sb.Append($"\n    {coord.x},{coord.y,-4} {kind,-12} {(total > 0 ? air * 100f / total : 0f),5:0.0}  "
                          + $"{leftAir,6}px  {rightAir,6}px  {(chunk.hasBeenModified ? "O" : "-")}");
            }
            return sb.ToString();
        }

        // ────────────────────────────────────────────────────────────────────
        //  3. 경계 스캔 — 절단(지오메트리)과 무늬 갈림(거리장)을 갈라서 보고
        // ────────────────────────────────────────────────────────────────────
        private static string BuildSeams(InfinityMapManager map, List<Vector2Int> coords)
        {
            var set = new HashSet<Vector2Int>(coords);
            var sb = new StringBuilder();
            bool any = false;

            foreach (var a in coords)
            {
                var bCoord = new Vector2Int(a.x + 1, a.y);
                if (!set.Contains(bCoord)) continue;

                var ca = map.GetChunk(a);
                var cb = map.GetChunk(bCoord);
                if (ca == null || cb == null) continue;

                var da = ca.GetData();
                var db = cb.GetData();
                if (da == null || db == null) continue;
                if (!da.BasePixels.IsCreated || !db.BasePixels.IsCreated) continue;

                ca.EnsureJobsCompleted();
                cb.EnsureJobsCompleted();

                int h = Mathf.Min(da.Height, db.Height);
                int wa = da.Width;

                // 절단 = 한쪽만 공기. 굴이 경계에서 벽으로 끝나는 자국이다.
                var cutAB = new List<Vector2Int>();   // A공기 / B지형
                var cutBA = new List<Vector2Int>();   // B공기 / A지형
                CollectRuns(da, db, wa, h, true,  cutAB);
                CollectRuns(da, db, wa, h, false, cutBA);

                // 거리장 차이 = 픽셀은 이어졌는데 무늬가 갈리는 원인
                int worstDist = 0, worstY = -1, worstA = 0, worstB = 0;
                if (da.DistanceField.IsCreated && db.DistanceField.IsCreated)
                {
                    for (int y = 0; y < h; y++)
                    {
                        if (da.BasePixels[da.ToIndex(wa - 1, y)].a == 0) continue;  // 공기끼리는 의미 없음
                        if (db.BasePixels[db.ToIndex(0, y)].a == 0) continue;
                        int va = da.DistanceField[da.ToIndex(wa - 1, y)];
                        int vb = db.DistanceField[db.ToIndex(0, y)];
                        int diff = Mathf.Abs(va - vb);
                        if (diff > worstDist) { worstDist = diff; worstY = y; worstA = va; worstB = vb; }
                    }
                }

                bool bad = cutAB.Count > 0 || cutBA.Count > 0 || worstDist > DistMismatchThreshold;
                if (!bad) continue;

                any = true;
                float worldX = bCoord.x * ChunkCoords.WorldSize;
                sb.Append($"\n  경계 월드x={worldX:0.#}  ({a.x},{a.y}) | ({bCoord.x},{bCoord.y})");
                AppendRuns(sb, "절단 A공기/B지형", cutAB);
                AppendRuns(sb, "절단 B공기/A지형", cutBA);
                if (worstDist > DistMismatchThreshold)
                    sb.Append($"\n      무늬갈림: 거리장 최대차 {worstDist} (A={worstA} B={worstB} @localY={worstY})");
            }

            return any
                ? "이상 있는 세로 경계만 나열" + sb
                : "세로 경계 이상 없음 (절단·무늬갈림 모두 없음)";
        }

        /// <summary>경계 한 줄을 훑어 "한쪽만 공기"인 연속 구간을 모은다.</summary>
        private static void CollectRuns(ChunkData da, ChunkData db, int wa, int h, bool aAirSide,
                                        List<Vector2Int> runs)
        {
            int start = -1;
            for (int y = 0; y <= h; y++)
            {
                bool hit = false;
                if (y < h)
                {
                    bool aAir = da.BasePixels[da.ToIndex(wa - 1, y)].a == 0;
                    bool bAir = db.BasePixels[db.ToIndex(0, y)].a == 0;
                    hit = aAirSide ? (aAir && !bAir) : (bAir && !aAir);
                }

                if (hit) { if (start < 0) start = y; }
                else if (start >= 0)
                {
                    if (y - start >= MinRunPx) runs.Add(new Vector2Int(start, y - 1));
                    start = -1;
                }
            }
        }

        private static void AppendRuns(StringBuilder sb, string label, List<Vector2Int> runs)
        {
            if (runs.Count == 0) return;
            sb.Append($"\n      {label}: ");
            int shown = Mathf.Min(runs.Count, 4);
            for (int i = 0; i < shown; i++)
                sb.Append($"localY {runs[i].x}~{runs[i].y}({runs[i].y - runs[i].x + 1}px) ");
            if (runs.Count > shown) sb.Append($"외 {runs.Count - shown}건");
        }
    }
}
#endif
