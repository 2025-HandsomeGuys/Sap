// @tags: debug, console, qa, cheat, relic, drop, unlock, pity, tier
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using System;
using System.Collections.Generic;
using System.Text;
using Relic;
using Relic.Data;
using Relic.Drop;
using UnityEngine;

namespace DebugTools
{
    /// <summary>
    /// 유물 치트 (F9 콘솔의 <c>relic</c>).
    ///
    /// 유물이 상점에서 빠지고 탐험 드롭으로 옮겨가면서, QA가 특정 유물을 손에 넣는 방법이
    /// "될 때까지 돌 캐기"밖에 없어졌다. 씬에 붙는 <see cref="RelicDebugGranter"/>는
    /// 인스펙터에 미리 등록한 유물만, 그 컴포넌트가 있는 씬에서만 된다.
    ///
    /// <c>drop</c>·<c>where</c>는 지급이 아니라 <b>드롭 시스템 자체</b>를 보는 명령이다 —
    /// 지금 서 있는 곳이 어느 지층으로 해석되는지, 그 지층 표에서 무엇이 뽑히는지 확인한다.
    ///
    /// 설계: <c>Assets/Docs/relic-exploration-drop.md</c>
    /// </summary>
    internal static class RelicDebugCommands
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            DebugCommandRegistry.Register("relic",
                "relic [이름 [레벨] | all | off <이름> | clear | drop [이름] | where | pity [reset]]",
                "유물 지급/회수/드롭 확인. 인자 없으면 보유 현황 + 티어 목록. 예: relic magnet / relic drop / relic off xray",
                Relic_, "유물");
        }

        private static string Relic_(string[] args)
        {
            var mgr = RelicManager.EnsureInScene();
            if (mgr == null) return "RelicManager 없음 (플레이어가 있는 씬에서 실행할 것)";

            if (args.Length == 0) return ListAll(mgr);

            switch (args[0].ToLowerInvariant())
            {
                case "all":   return GrantAll(mgr);
                case "clear": return ClearAll(mgr);
                case "where": return Where();
                case "pity":  return Pity(mgr, args);
                case "drop":  return Drop(args.Length > 1 ? args[1] : null);
                case "off":
                    return args.Length < 2
                        ? "회수할 유물 이름이 필요하다. 예: relic off xray"
                        : Revoke(mgr, args[1]);
            }

            return Grant(mgr, args[0], args.Length > 1 ? args[1] : null);
        }

        // ================================================================
        //  지급 / 회수
        // ================================================================

        private static string Grant(RelicManager mgr, string name, string levelArg)
        {
            if (!TryResolve(name, out var id, out string err)) return err;

            bool wasOwned = mgr.Inventory.IsOwned(id);
            mgr.GrantAndAutoEquip(id);

            var so = RelicDatabase.Instance != null ? RelicDatabase.Instance.GetRelicByID(id) : null;
            if (!wasOwned && so != null) AcquisitionNotifier.NotifyRelic(so);

            string levelNote = string.Empty;
            if (!string.IsNullOrEmpty(levelArg) && int.TryParse(levelArg, out int wanted))
                levelNote = RaiseLevel(mgr, id, so, wanted);

            return $"{(wasOwned ? "이미 보유" : "지급")}: {id} (Lv{mgr.Inventory.GetLevel(id)}){levelNote}";
        }

        /// <summary>목표 레벨까지 올린다. 내리는 건 지원하지 않는다 — TryUpgrade가 단방향이다.</summary>
        private static string RaiseLevel(RelicManager mgr, RelicID id, RelicSO so, int wanted)
        {
            int max = so != null ? so.maxLevel : 3;
            wanted = Mathf.Clamp(wanted, 1, max);

            int guard = 0;
            while (mgr.Inventory.GetLevel(id) < wanted && guard++ < 32)
                if (!mgr.Inventory.TryUpgrade(id, max)) break;

            mgr.RefreshLevel(id);

            int now = mgr.Inventory.GetLevel(id);
            return now == wanted ? $" → Lv{now}" : $" → Lv{now} (Lv{wanted} 실패, 최대 Lv{max})";
        }

        private static string GrantAll(RelicManager mgr)
        {
            int granted = 0;
            foreach (var id in AllRelics())
            {
                if (mgr.Inventory.IsOwned(id)) continue;
                mgr.GrantAndAutoEquip(id);
                granted++;
            }
            return granted == 0 ? "이미 전부 보유 중" : $"{granted}종 지급 (보유 {mgr.Inventory.Owned.Count}종)";
        }

        private static string Revoke(RelicManager mgr, string name)
        {
            if (!TryResolve(name, out var id, out string err)) return err;

            return mgr.RevokeAndUnequip(id)
                ? $"회수: {id} (도감 발견 기록은 남는다)"
                : $"보유하고 있지 않음: {id}";
        }

        private static string ClearAll(RelicManager mgr)
        {
            // Owned를 돌면서 지우면 컬렉션이 바뀐다 — 먼저 복사한다.
            var ids = new List<RelicID>(mgr.Inventory.Owned.Keys);
            int n = 0;
            foreach (var id in ids)
                if (mgr.RevokeAndUnequip(id)) n++;

            return n == 0 ? "보유한 유물 없음" : $"{n}종 회수";
        }

        // ================================================================
        //  드롭 시스템 확인
        // ================================================================

        /// <summary>
        /// 유물을 발밑에 떨어뜨린다. 이름을 주면 그 유물을, 안 주면 <b>지금 지층의 드롭표로 추첨</b>한다.
        /// 후자는 실제 드롭과 같은 경로를 타므로 "이 층에서 뭐가 나오나"를 그대로 확인할 수 있다.
        /// </summary>
        private static string Drop(string name)
        {
            var player = ResolvePlayer();
            if (player == null) return "플레이어를 찾지 못했다";

            Vector3 pos = player.position + new Vector3(0f, 0.6f, 0f);
            string layer = RelicDropRoller.CurrentTileType(pos);

            if (string.IsNullOrEmpty(name))
            {
                if (!RelicDropRoller.ForceDrop(pos, out var picked))
                    return $"{layer}: 뽑을 유물이 없다 (이 지층 후보를 전부 보유했거나 가중치가 0)";
                return $"{layer} 드롭표에서 추첨 → {picked} (발밑에 떨어짐)";
            }

            if (!TryResolve(name, out var id, out string err)) return err;

            int order = RelicDropSettingsLoader.Settings.rock.sortingOrder;
            return WorldRelicPickup.Create(id, pos, order) != null
                ? $"{id} 를 발밑에 떨어뜨렸다"
                : $"{id} 픽업 생성 실패 (RelicDatabase 등록 확인)";
        }

        /// <summary>지금 서 있는 지층과 그 지층의 티어 가중치·남은 후보 수.</summary>
        private static string Where()
        {
            var player = ResolvePlayer();
            if (player == null) return "플레이어를 찾지 못했다";

            var s = RelicDropSettingsLoader.Settings;
            string layer = RelicDropRoller.CurrentTileType(player.position);
            int[] w = s.ResolveTierWeights(layer);

            if (w == null) return $"현재 지층: {layer} — 드롭 설정에 없는 지층이라 유물이 안 나온다";

            var mgr = RelicManager.EnsureInScene();
            Func<RelicID, bool> owned = null;
            if (mgr != null) owned = mgr.Inventory.IsOwned;

            // 실효 확률은 '후보가 남은 티어'만 놓고 다시 정규화해야 실제 추첨과 맞는다
            // (RelicDropTable이 빈 티어를 가중치에서 빼기 때문).
            int total = 0;
            var left = new int[w.Length];
            for (int i = 0; i < w.Length; i++)
            {
                left[i] = RelicDropTable.CollectAvailable(s, i + 1, owned).Count;
                if (w[i] > 0 && left[i] > 0) total += w[i];
            }

            var sb = new StringBuilder($"현재 지층: <color=#F5C63F>{layer}</color>");
            for (int i = 0; i < w.Length; i++)
            {
                string pct = (w[i] > 0 && left[i] > 0 && total > 0) ? $"{100f * w[i] / total:0.#}%" : "—";
                sb.Append($"\n  {i + 1}티어  가중치 {w[i],4}  미보유 {left[i],2}종  실효 {pct}");
            }
            sb.Append($"\n  돌 완파 {s.rock.baseChance:P2} (광물돌 ×{s.rock.mineralRockMultiplier}) / 던전 상자 {s.dungeonChest.chance:P0}");
            return sb.ToString();
        }

        private static string Pity(RelicManager mgr, string[] args)
        {
            var s = RelicDropSettingsLoader.Settings;

            if (args.Length > 1 && args[1].Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                mgr.Inventory.RockDropPity = 0;
                mgr.Inventory.ChestDropPity = 0;
                return "피티 카운터 초기화";
            }

            return $"피티 — 돌 {mgr.Inventory.RockDropPity}/{s.rock.pityRocks}, " +
                   $"상자 {mgr.Inventory.ChestDropPity}/{s.dungeonChest.pityChests}\n" +
                   "  임계치에 닿으면 다음 판정이 확정. 'relic pity reset'으로 초기화.";
        }

        // ================================================================
        //  현황 출력
        // ================================================================

        private static string ListAll(RelicManager mgr)
        {
            var s = RelicDropSettingsLoader.Settings;
            var sb = new StringBuilder($"보유 <color=#D87FC0>{mgr.Inventory.Owned.Count}</color>종");

            for (int tier = 1; tier <= Mathf.Max(1, s.HighestTier()); tier++)
            {
                string[] names = s.ResolveTierPool(tier);
                if (names.Length == 0) continue;

                sb.Append($"\n<color=#F5C63F>{tier}티어</color> ({names.Length}종)");
                foreach (string n in names)
                {
                    if (!RelicDropTable.TryParseRelic(n, out var id))
                    {
                        sb.Append($"\n  ⚠ {n} (RelicID에 없는 이름)");
                        continue;
                    }
                    int lv = mgr.Inventory.GetLevel(id);
                    sb.Append(lv > 0 ? $"\n  <color=#7FD8A0>■</color> {n} Lv{lv}" : $"\n  □ {n}");
                }
            }

            var extras = new List<string>();
            foreach (var id in AllRelics())
                if (!InAnyTier(s, id)) extras.Add(id.ToString());

            sb.Append("\n드롭 표에 없는 유물: ");
            sb.Append(extras.Count == 0 ? "없음" : string.Join(", ", extras));
            sb.Append("\nrelic <이름> 지급 / relic where 현재 지층 / relic drop 추첨 드롭");
            return sb.ToString();
        }

        // ================================================================
        //  이름 해석
        // ================================================================

        /// <summary>
        /// enum 이름을 대소문자 무시로 찾고, 없으면 앞부분이 일치하는 것 하나를 받아준다
        /// (relic gambler → GamblerGlasses). 후보가 여럿이면 골라 쓰라고 되돌린다.
        /// </summary>
        private static bool TryResolve(string name, out RelicID id, out string error)
        {
            id = RelicID.None;
            error = null;

            if (Enum.TryParse(name, ignoreCase: true, out RelicID exact) && exact != RelicID.None)
            {
                id = exact;
                return true;
            }

            var hits = new List<RelicID>();
            foreach (var candidate in AllRelics())
                if (candidate.ToString().StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    hits.Add(candidate);

            if (hits.Count == 1) { id = hits[0]; return true; }
            if (hits.Count > 1)
            {
                error = $"'{name}' 후보가 여럿: {string.Join(", ", hits)}";
                return false;
            }

            error = $"그런 유물 없음: {name}  (relic 만 치면 목록)";
            return false;
        }

        private static IEnumerable<RelicID> AllRelics()
        {
            foreach (RelicID id in Enum.GetValues(typeof(RelicID)))
                if (id != RelicID.None) yield return id;
        }

        private static bool InAnyTier(RelicDropSettingsData s, RelicID id)
        {
            foreach (string n in s.AllRelicNames())
                if (RelicDropTable.TryParseRelic(n, out var parsed) && parsed == id) return true;
            return false;
        }

        private static Transform s_player;

        private static Transform ResolvePlayer()
        {
            if (s_player != null) return s_player;
            var pc = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            s_player = pc != null ? pc.transform : null;
            return s_player;
        }
    }
}
#endif
