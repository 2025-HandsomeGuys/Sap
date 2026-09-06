// @tags: relic, drop, table, weight, tier, layer, pure, testable
using System;
using System.Collections.Generic;
using Relic.Data;

namespace Relic.Drop
{
    /// <summary>
    /// "이 지층에서 어떤 유물이 나오는가"의 순수 추첨 로직. Unity API를 쓰지 않아 EditMode 테스트로 검증한다.
    ///
    /// 규칙 두 가지가 전부다.
    ///  1) <b>이미 가진 유물은 후보에서 뺀다.</b> 유물은 수량 개념이 없어 중복 드롭이 곧 꽝이다.
    ///  2) <b>후보가 하나도 없는 티어는 가중치에서 통째로 뺀다.</b> 안 그러면 그 티어를 다 모은 뒤부터
    ///     "티어는 뽑혔는데 줄 게 없다"가 반복돼 체감 드롭률이 설정값보다 낮아진다.
    ///
    /// 지층별 가중치는 해당 지층의 티어가 가장 크고 아래 티어도 0이 아니다 —
    /// 얕은 층에서 놓친 유물을 깊은 층에서 회수할 수 있게 한 의도된 겹침이다.
    /// </summary>
    public static class RelicDropTable
    {
        /// <summary>
        /// 지층 <paramref name="tileType"/>에서 유물 1종을 뽑는다.
        /// </summary>
        /// <param name="settings">relicDropSettings.json</param>
        /// <param name="tileType">tileData.json의 tileType 문자열</param>
        /// <param name="isOwned">이미 보유 중인지 판정. null이면 전부 미보유로 본다</param>
        /// <param name="rng">난수원. 테스트에서 시드를 고정해 결과를 재현한다</param>
        /// <param name="picked">뽑힌 유물. 실패 시 <see cref="RelicID.None"/></param>
        /// <returns>뽑았으면 true</returns>
        public static bool TryPick(RelicDropSettingsData settings, string tileType,
                                   Func<RelicID, bool> isOwned, Random rng, out RelicID picked)
        {
            picked = RelicID.None;
            if (settings == null || !settings.enabled || rng == null) return false;

            int[] weights = settings.ResolveTierWeights(tileType);
            if (weights == null || weights.Length == 0) return false;

            // 티어별 후보(미보유) 수집. 빈 티어는 가중치 0으로 눌러 추첨에서 제외한다.
            var pools = new List<RelicID>[weights.Length];
            long total = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0) continue;

                pools[i] = CollectAvailable(settings, tier: i + 1, isOwned);
                if (pools[i].Count == 0) continue;

                total += weights[i];
            }
            if (total <= 0) return false;

            long roll = (long)(rng.NextDouble() * total);
            if (roll >= total) roll = total - 1;   // NextDouble()이 1.0을 돌려주는 구현 방어

            long cumulative = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0 || pools[i] == null || pools[i].Count == 0) continue;

                cumulative += weights[i];
                if (roll >= cumulative) continue;

                var pool = pools[i];
                picked = pool[rng.Next(pool.Count)];
                return true;
            }

            return false;   // 누적 계산이 어긋나는 경우는 없지만, 조용히 None을 주느니 false로 드러낸다
        }

        /// <summary>티어 <paramref name="tier"/>에서 아직 안 가진 유물 목록.</summary>
        public static List<RelicID> CollectAvailable(RelicDropSettingsData settings, int tier,
                                                     Func<RelicID, bool> isOwned)
        {
            var result = new List<RelicID>();
            if (settings == null) return result;

            string[] names = settings.ResolveTierPool(tier);
            for (int i = 0; i < names.Length; i++)
            {
                if (!TryParseRelic(names[i], out var id)) continue;
                if (isOwned != null && isOwned(id)) continue;
                if (result.Contains(id)) continue;      // JSON 중복 기재 방어
                result.Add(id);
            }
            return result;
        }

        /// <summary>
        /// RelicID 이름 파싱. 오타는 조용히 삼키지 않고 false로 돌려준다 —
        /// 호출부가 경고를 찍어 JSON 오타가 화면에 드러나게 한다.
        /// </summary>
        public static bool TryParseRelic(string name, out RelicID id)
        {
            id = RelicID.None;
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (!Enum.TryParse(name.Trim(), ignoreCase: false, out RelicID parsed)) return false;
            if (parsed == RelicID.None) return false;
            id = parsed;
            return true;
        }

        /// <summary>JSON에 적힌 유물 이름 중 enum에 없는 것들. 로드 검증·테스트용.</summary>
        public static List<string> FindUnknownRelicNames(RelicDropSettingsData settings)
        {
            var bad = new List<string>();
            if (settings == null) return bad;
            foreach (string n in settings.AllRelicNames())
                if (!TryParseRelic(n, out _)) bad.Add(n);
            return bad;
        }
    }
}
