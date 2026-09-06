// @tags: bug-report, qa, state, collector
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BugReport
{
    /// <summary>
    /// 기존 싱글톤에서 상태를 읽어 BugReportData를 만든다.
    ///
    /// ⚠ 모든 항목을 개별 try-catch로 감싼다. 매니저 하나가 null이거나 예외를 던져도
    ///   그 필드만 &lt;unavailable&gt;로 남고 리포트는 저장돼야 한다 —
    ///   버그 리포트가 버그 때문에 실패하면 안 된다.
    /// </summary>
    public static class BugReportCollector
    {
        /// <summary>
        /// 확장 지점. 새 시스템이 자기 값을 리포트에 넣고 싶으면 한 줄 구독하면 된다.
        /// 인터페이스 구현도, 이 파일 수정도 필요 없다.
        /// </summary>
        public static event Action<Dictionary<string, string>> ExtraContext;

        public static BugReportData Collect()
        {
            var data = new BugReportData();
            CollectMeta(data.meta);
            CollectPlayer(data.player);
            CollectWorld(data.world);
            Try(() =>
            {
                if (UIStateManager.Instance != null)
                    data.ui = UIStateManager.Instance.CurrentState.ToString();
            });
            CollectExtra(data.extra);
            return data;
        }

        /// <summary>
        /// 세이브 스냅샷. report.json 안에 끼워 넣으면 통째로 이스케이프되어
        /// 사람이 읽을 수 없게 되므로 별도 파일로 뺀다.
        /// </summary>
        public static string CollectSaveJson()
        {
            try
            {
                var pd = SaveManager.Instance != null ? SaveManager.Instance.playerData : null;
                return pd != null ? JsonUtility.ToJson(pd, true) : "{}";
            }
            catch (Exception e)
            {
                return "{ \"error\": \"" + e.Message.Replace("\"", "'") + "\" }";
            }
        }

        private static void CollectMeta(BugReportMeta m)
        {
            m.localTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            m.appVersion = Application.version;
            m.unityVersion = Application.unityVersion;
            m.platform = Application.platform.ToString();
            m.resolution = $"{Screen.width}x{Screen.height} (fullscreen={Screen.fullScreen})";
            m.realtimeSinceStartup = Time.realtimeSinceStartup;
            m.totalMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
            m.sessionErrorCount = BugReportLogBuffer.ErrorCount;
            m.sessionExceptionCount = BugReportLogBuffer.ExceptionCount;
            Try(() => m.sceneName = SceneManager.GetActiveScene().name);
        }

        private static void CollectPlayer(BugReportPlayer p)
        {
            Transform player = null;

            Try(() =>
            {
                var stat = UnityEngine.Object.FindFirstObjectByType<PlayerStat>();
                if (stat == null) return;
                player = stat.transform;
                p.gold = stat.Gold.ToString();
                p.stamina = stat.CurrentStamina.ToString("F1");
                p.miningLevel = stat.MiningLevel.ToString();
            });

            Try(() =>
            {
                if (player == null) return;
                Vector3 pos = player.position;
                p.worldPos = $"{pos.x:F2}, {pos.y:F2}";
                // 좌표 변환은 반드시 ChunkCoords를 통한다 (단일 진실 공급원).
                Vector2Int c = ChunkCoords.ToChunk(pos);
                p.chunkCoord = $"{c.x}, {c.y}";
            });

            Try(() =>
            {
                if (player == null) return;
                var rb = player.GetComponent<Rigidbody2D>();
                if (rb != null)
                    p.velocity = $"{rb.linearVelocity.x:F2}, {rb.linearVelocity.y:F2}";
            });

            Try(() =>
            {
                var enc = UnityEngine.Object.FindFirstObjectByType<EncumbranceController>();
                if (enc != null)
                    p.weight = $"{enc.TotalWeight:F1} / 임계 {enc.EncumbranceThreshold:F1}";
            });
        }

        private static void CollectWorld(BugReportWorld w)
        {
            Try(() => w.inDungeon = DungeonOverlayController.IsInDungeon.ToString());

            Try(() =>
            {
                var dc = UnityEngine.Object.FindFirstObjectByType<DayCycleManager>();
                if (dc == null) return;
                w.day = dc.CurrentDay.ToString();
                w.timeOfDay = dc.CurrentTime.ToString();
            });

            Try(() =>
            {
                var stat = UnityEngine.Object.FindFirstObjectByType<PlayerStat>();
                if (stat == null || TileDataManager.Instance == null) return;
                // FootstepPlayer.CurrentLayer()와 같은 방식 — 깊이로 층을 얻는다.
                int chunkY = ChunkCoords.ToChunk(stat.transform.position).y;
                w.layer = TileDataManager.Instance.GetTileTypeAtDepth(chunkY).ToString();
            });

            Try(() =>
            {
                var map = InfinityMapManager.Instance;
                if (map == null) return;
                foreach (var coord in map.GetActiveChunkCoords())
                    w.loadedChunks.Add($"{coord.x},{coord.y}");
                w.loadedChunkCount = w.loadedChunks.Count;
            });
        }

        private static void CollectExtra(List<string> into)
        {
            var handler = ExtraContext;
            if (handler == null) return;

            // 구독자가 예외를 던져도 리포트를 깨뜨리지 못하게 한다.
            Try(() =>
            {
                var bag = new Dictionary<string, string>();
                handler(bag);
                foreach (var kv in bag) into.Add($"{kv.Key}={kv.Value}");
            });
        }

        /// <summary>한 항목의 실패가 리포트 전체를 막지 않게 하는 래퍼.</summary>
        private static void Try(Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogWarning($"[BugReport] 수집 실패: {e.Message}"); }
        }
    }
}
#endif
