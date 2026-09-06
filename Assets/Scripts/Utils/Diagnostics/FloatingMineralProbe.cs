// @tags: diagnostics, qa, mineral, physics, settle, bug-report
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameDiagnostics
{
    /// <summary>
    /// "광물이 공중에 멈춰 있다" 버그 전용 F12 스냅샷.
    ///
    /// 이 버그가 사후 조사가 안 되는 이유: 멈춘 광물의 인스펙터만 보면
    /// 매설(Kinematic)과 안착(Settle→Kinematic)이 완전히 똑같아 보인다.
    /// 둘은 깨어나는 조건이 서로 달라서(매설=0.5초 지지 코루틴, 안착=NotifyTerrainDug 이벤트만)
    /// 어느 쪽인지 모르면 원인 후보가 갈라지지도 않는다.
    ///
    /// 그래서 F12 시점에 플레이어 주변 광물의 <b>내부 상태</b>를 통째로 찍는다:
    ///   embedded/settled · bodyType · simulated · awake · 부모(청크 자식인지 루트인지)
    ///   · 발밑 픽셀 간격 · 발밑 콜라이더 간격 · MineralLifetime 유무
    ///
    /// 픽셀 간격과 콜라이더 간격을 <b>나란히</b> 찍는 게 핵심이다 —
    /// "픽셀은 비었는데 콜라이더가 남아 받치고 있었다"와 "둘 다 비었는데 안 깨어난다"는
    /// 완전히 다른 버그이고, 하나만 봐서는 구분이 안 된다.
    ///
    /// 씬 배치 불필요 — RuntimeInitializeOnLoadMethod로 자동 생성된다.
    /// </summary>
    public class FloatingMineralProbe : MonoBehaviour
    {
        /// <summary>전체 on/off.</summary>
        public static bool Enabled = true;

        /// <summary>플레이어 기준 이 반경 안의 광물만 찍는다.</summary>
        public static float ScanRadius = 3f;

        /// <summary>발밑 픽셀이 이만큼(월드 단위) 비어 있으면 "떠 있음"으로 표시한다.</summary>
        private const float FLOAT_GAP = 0.03f;

        /// <summary>발밑 탐침 최대 거리. 이 이상은 "바닥 없음"으로 본다.</summary>
        private const float PROBE_MAX = 1.5f;
        private const float PROBE_STEP = 0.01f; // 1px (PPU 100)

        private static readonly Collider2D[] s_hits = new Collider2D[64];
        private static readonly RaycastHit2D[] s_castHits = new RaycastHit2D[4];
        private static ContactFilter2D s_filter;
        private static bool s_filterReady;

        private static FloatingMineralProbe s_instance;
        private readonly StringBuilder _sb = new StringBuilder(2048);
        private Transform _player;
        private float _nextPlayerLookup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            AutoCreate();
            SceneManager.sceneLoaded -= OnSceneLoadedEnsure;
            SceneManager.sceneLoaded += OnSceneLoadedEnsure;
        }

        private static void OnSceneLoadedEnsure(Scene scene, LoadSceneMode mode) => AutoCreate();

        /// <summary>
        /// ⚠ 자가복구: GameManager.OpenMainMenu의 DestroyPersistentObjects()가
        /// DontDestroyOnLoad 씬의 루트를 전부 파괴해 이 오브젝트도 같이 죽는다.
        /// [RuntimeInitializeOnLoadMethod]는 세션당 한 번만 돌아 재생성되지 않으므로,
        /// 메인메뉴를 다녀오면 F12 리포트에서 광물 스냅샷이 빠졌다. static 이벤트 구독은 파괴와
        /// 무관하게 살아남으므로 씬 로드마다 되살린다(SoundManager와 같은 패턴).
        /// </summary>
        private static void AutoCreate()
        {
            if (s_instance != null) return;
            var go = new GameObject("[FloatingMineralProbe]");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<FloatingMineralProbe>();
        }

        private void OnEnable()  => BugReport.BugReportCollector.ExtraContext += OnCollectExtraContext;
        private void OnDisable() => BugReport.BugReportCollector.ExtraContext -= OnCollectExtraContext;

        private void OnCollectExtraContext(Dictionary<string, string> bag)
        {
            if (!Enabled) { bag["mineralProbe"] = "off"; return; }
            if (!ResolvePlayer()) { bag["mineralProbe"] = "플레이어 못 찾음"; return; }

            int floating = BuildDump(out string dump);
            bag["mineralProbe"] = $"주변 {ScanRadius:F0}u 스캔 — 떠 있는 광물 {floating}개";

            // 상세는 로그로. report.json에 넣으면 통째로 이스케이프되어 못 읽는다.
            // Collect()가 log.txt 스냅샷보다 먼저 도므로 이 로그도 리포트에 실린다.
            Debug.Log(dump);
        }

        /// <returns>떠 있는 것으로 판정된 광물 수</returns>
        private int BuildDump(out string dump)
        {
            if (!s_filterReady)
            {
                int mineralLayer = LayerMask.NameToLayer("Mineral");
                s_filter = new ContactFilter2D
                {
                    useTriggers  = true,               // 매설 광물은 trigger 콜라이더다 — 빼면 절반이 안 잡힌다
                    useLayerMask = mineralLayer >= 0,
                    layerMask    = mineralLayer >= 0 ? (1 << mineralLayer) : ~0
                };
                s_filterReady = true;
            }

            _sb.Clear();
            _sb.Append("[MineralProbe] ===== F12 =====\n");
            _sb.Append($"  플레이어 {(Vector2)_player.position} 기준 {ScanRadius:F1}u 안의 광물\n");

            int count = Physics2D.OverlapCircle(_player.position, ScanRadius, s_filter, s_hits);
            if (count >= s_hits.Length)
                _sb.Append($"  ⚠ 버퍼({s_hits.Length}) 가득 참 — 일부 누락됨\n");

            int floating = 0;
            for (int i = 0; i < count; i++)
            {
                Collider2D c = s_hits[i];
                if (c == null) continue;
                if (!c.TryGetComponent<MineralItemController>(out var m)) continue;

                float pixelGap = ProbePixelGapBelow(c.transform.position);
                // gap < 0 = 1.5u 안에 바닥이 아예 없음 = 가장 심하게 떠 있는 경우다.
                // 이걸 [ok]로 흘리면 정작 제일 중요한 놈이 리포트에서 빠진다(2026-09-01 리포트에서 실제로 그랬다).
                bool isFloating = pixelGap < 0f || pixelGap > FLOAT_GAP;
                if (isFloating) floating++;

                var rb = c.attachedRigidbody;
                string parent = c.transform.parent == null
                    ? "root"
                    : (c.transform.parent.GetComponent<TerrainChunk>() != null ? "chunk자식" : c.transform.parent.name);

                _sb.Append(isFloating ? "  [뜸] " : "  [ok] ").Append(c.name).Append('\n');
                _sb.Append("      ").Append(m.DescribeExposure()).Append('\n');
                _sb.Append($"      rb: body={(rb != null ? rb.bodyType.ToString() : "없음")} " +
                           $"simulated={(rb != null && rb.simulated)} awake={(rb != null && rb.IsAwake())} " +
                           $"vel={(rb != null ? rb.linearVelocity : Vector2.zero)} grav={(rb != null ? rb.gravityScale : 0f):F2}\n");
                _sb.Append($"      col: trigger={c.isTrigger} enabled={c.enabled} layer={LayerMask.LayerToName(c.gameObject.layer)}\n");
                _sb.Append($"      부모={parent} lifetime={(c.GetComponent<MineralLifetime>() != null ? "있음" : "없음")}\n");
                _sb.Append($"      발밑: 픽셀 {Fmt(pixelGap)} / 콜라이더 {Fmt(ProbeColliderGapBelow(c))}\n");
            }

            if (count == 0) _sb.Append("  (없음)\n");
            _sb.Append("[MineralProbe] ================================");
            dump = _sb.ToString();
            return floating;
        }

        private static string Fmt(float gap)
            => gap < 0f ? "바닥없음(>1.5u)" : $"{gap:F3}u({gap * 100f:F0}px)";

        /// <summary>지형 <b>픽셀</b> 기준 아래쪽 첫 solid까지 거리. 못 찾으면 -1.</summary>
        private static float ProbePixelGapBelow(Vector2 origin)
        {
            var map = InfinityMapManager.Instance;
            if (map == null) return -1f;

            for (float d = 0f; d <= PROBE_MAX; d += PROBE_STEP)
            {
                if (!map.IsWorldPositionEmpty(origin + Vector2.down * d))
                    return d;
            }
            return -1f;
        }

        /// <summary>
        /// 광물을 떠받칠 수 있는 <b>콜라이더</b>까지의 아래쪽 거리. 못 찾으면 -1.
        ///
        /// Raycast를 쓰면 안 된다 — 시작점이 어떤 트리거 안이면 queriesStartInColliders 때문에
        /// 거리 0으로 즉시 맞는다. 2026-09-01 리포트에서 매설·공중 광물이 전부 "콜라이더 0px"으로
        /// 찍혀 이 칸이 통째로 쓸모없어졌던 게 그 이유다.
        /// Collider2D.Cast는 자기 자신을 제외하고 실제 모양으로 쓸어서 그 함정이 없다.
        /// </summary>
        private static float ProbeColliderGapBelow(Collider2D self)
        {
            var filter = new ContactFilter2D
            {
                useTriggers  = false,
                useLayerMask = true,
                layerMask    = Physics2D.GetLayerCollisionMask(self.gameObject.layer)
            };

            int n = self.Cast(Vector2.down, filter, s_castHits, PROBE_MAX);
            if (n <= 0) return -1f;

            float nearest = float.MaxValue;
            for (int i = 0; i < n; i++)
                if (s_castHits[i].distance < nearest) nearest = s_castHits[i].distance;
            return nearest;
        }

        private bool ResolvePlayer()
        {
            if (_player != null) return true;
            if (Time.unscaledTime < _nextPlayerLookup) return false;
            _nextPlayerLookup = Time.unscaledTime + 1f;

            var stat = FindFirstObjectByType<PlayerStat>();
            if (stat == null) return false;
            _player = stat.transform;
            return true;
        }
    }
}
#endif
