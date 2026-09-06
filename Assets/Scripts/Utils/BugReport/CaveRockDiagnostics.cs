// @tags: bug-report, qa, cave, rock, placement, diagnostics
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BugReport
{
    /// <summary>
    /// F12 리포트에 **굴 속 바위 진단**을 얹는다. 씬 배치 불필요 — ExtraContext 한 줄 구독.
    ///
    /// "굴에 바위가 안 보인다"는 원인이 셋인데 화면만 봐서는 못 가른다:
    ///   1) 애초에 그 청크에 바위가 없다        → 배치 규칙이 다 걸러냈다
    ///   2) 바위는 있는데 굴에서 먼 자리에만 있다 → 규칙이 굴 근처를 과하게 막는다
    ///   3) 굴 옆에 있는데 안 드러났다           → 노출(RevealInTerrain) 훅이 안 돌았다
    ///
    /// 그래서 청크마다 "바위 수 / 노출 수 / 굴에 걸친 수"를 같이 찍는다. 셋의 조합이 곧 원인이다.
    ///   바위 0            → 1번
    ///   바위 n, 걸친 0    → 2번
    ///   걸친 n, 노출 0    → 3번
    ///
    /// 덤으로 굴 벽 전용 배치 패스가 이 청크에서 후보를 몇 칸 찾았는지도 찍는다(placementProbe).
    /// 추정이 아니라 배치가 실제로 쓰는 TerrainDecorator.CollectCaveWall 을 그대로 부른다.
    /// </summary>
    public static class CaveRockDiagnostics
    {
        private static readonly string NL = ((char)10).ToString();   // "\n" 대신 — 스크립트 편집 시 역슬래시가 깨진다

        /// <summary>배치 규칙 재현 표본 간격(px). 촘촘할수록 정확하지만 리포트가 느려진다.</summary>
        private const int ProbeStep = 64;

        /// <summary>표본 바위의 반크기(px). 실제 프리팹 평균에 가깝게. 탭 거리는 이 값의 절반.</summary>
        private const int ProbeHalf = 48;

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

            var chunks = new List<TerrainChunk>();
            foreach (var c in map.GetAllActiveChunks()) chunks.Add(c);
            if (chunks.Count == 0) return;

            bag["caveRocks"]      = BuildRockTable(chunks);
            bag["placementProbe"] = BuildProbe(chunks);
        }

        /// <summary>청크별 바위 현황. 굴에 걸친 바위와 그중 몇이 드러났는지가 핵심이다.</summary>
        private static string BuildRockTable(List<TerrainChunk> chunks)
        {
            var sb = new StringBuilder("좌표    바위  노출  굴에걸침  걸친것중노출  굴상태");

            foreach (var chunk in chunks)
            {
                if (chunk == null) continue;

                int total = 0, revealed = 0, touching = 0, touchingRevealed = 0;

                foreach (var rock in chunk.SpawnedRocks)
                {
                    if (rock == null) continue;
                    total++;
                    if (rock.IsRevealed) revealed++;

                    if (!TouchesCave(chunk, rock)) continue;
                    touching++;
                    if (rock.IsRevealed) touchingRevealed++;
                }

                sb.Append(NL);
                sb.Append(string.Format("    {0},{1}   {2,4}  {3,4}  {4,8}  {5,12}  {6}",
                    chunk.ChunkX, chunk.ChunkY, total, revealed, touching, touchingRevealed,
                    chunk.CaveRevealed ? "열림" : "숨김"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 바위의 노출 판정 영역(RevealBounds)이 굴과 겹치는지.
        ///
        /// 굴이 이미 뚫린 청크는 픽셀이 공기라 IsFutureCave* 가 false 를 돌려주므로,
        /// 열린 굴에서는 **실제 공기 픽셀**을 세서 판정한다. 숨김 상태면 예정 굴 판정을 쓴다.
        /// 둘 다 "이 바위가 굴에 물려 있나"라는 같은 질문이다.
        /// </summary>
        private static bool TouchesCave(TerrainChunk chunk, DiggableRock rock)
        {
            RectInt b = rock.RevealBounds;
            int cx = b.xMin + b.width / 2;
            int cy = b.yMin + b.height / 2;
            int mx = Mathf.Max(1, b.width / 4);
            int my = Mathf.Max(1, b.height / 4);

            if (!chunk.CaveRevealed)
                return chunk.CountFutureCaveAround(cx, cy, mx, my) > 0;

            // 열린 굴: 바위 주변 4방향에 공기가 있으면 굴(또는 판 자리)에 물린 것으로 본다.
            return chunk.IsTransparent(cx + mx, cy)
                || chunk.IsTransparent(cx - mx, cy)
                || chunk.IsTransparent(cx, cy + my)
                || chunk.IsTransparent(cx, cy - my);
        }

        /// <summary>
        /// 굴 벽 전용 배치 패스가 이 청크에서 **몇 칸을 후보로 찾았는지**를 그대로 찍는다.
        ///
        /// 옛 버전은 배치 규칙을 격자로 재현해 "얼마나 걸러지는가"를 쟀는데, 그 수치가
        /// 기각 표집 방식이 왜 실패하는지를 증명한 뒤(2026-09-04: 허용 자리 4.4%)
        /// 패스 자체가 굴 벽을 직접 찾아가는 방식으로 바뀌었다. 그래서 이제는 추정이 아니라
        /// 실제 수집 함수(TerrainDecorator.CollectCaveWall)를 그대로 부른다 — 리포트와 배치가
        /// 다른 코드를 보면 진단이 거짓말을 한다.
        ///
        /// 읽는 법: 굴벽칸 0 인데 굴중심 > 0 이면 수집 격자가 굴을 놓친 것,
        ///          굴벽칸 > 0 인데 caveRocks 의 "굴에걸침" 이 0 이면 배치·스폰 단계에서 막힌 것.
        /// </summary>
        private static string BuildProbe(List<TerrainChunk> chunks)
        {
            var sb = new StringBuilder(
                "좌표    표본  굴중심  굴벽칸   ← 굴 숨김 상태에서만 의미 있음");

            foreach (var chunk in chunks)
            {
                if (chunk == null) continue;

                int samples = 0, centerInCave = 0;

                for (int y = ProbeHalf; y < chunk.height - ProbeHalf; y += ProbeStep)
                {
                    for (int x = ProbeHalf; x < chunk.width - ProbeHalf; x += ProbeStep)
                    {
                        samples++;
                        if (chunk.IsFutureCaveAt(x, y)) centerInCave++;
                    }
                }

                // 배치 패스가 쓰는 것과 같은 borderPadding (RockDecorator 계산식)
                float padding = Mathf.Clamp(chunk.textureThickness * chunk.PPU, 5f, 50f) + 5f;
                int wallSpots = chunk.HasHiddenCave
                    ? TerrainDecorator.CollectCaveWall(chunk, padding).Count
                    : -1;   // -1 = 숨은 굴이 없어 패스가 아예 안 도는 청크

                sb.Append(NL);
                sb.Append(string.Format("    {0},{1}   {2,4}  {3,6}  {4,7}",
                    chunk.ChunkX, chunk.ChunkY, samples, centerInCave, wallSpots));
            }

            return sb.ToString();
        }
    }
}
#endif
