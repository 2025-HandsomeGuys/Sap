// @tags: diagnostics, qa, terrain, collider, chunk-seam, penetration, bug-report
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameDiagnostics
{
    /// <summary>
    /// "흙은 그대로인데 몸이 청크 사이로 통과한다" 버그 전용 감시기.
    ///
    /// 이 버그는 재현이 안 되고 사후 증거도 남지 않는다 — 지형은 세이브에 안 남고,
    /// 콜라이더는 매 갱신마다 새로 그려져 사고 당시 모양이 사라진다.
    /// 그래서 "터진 뒤에 조사"가 불가능하다. 상시로 얇게 관찰해 두는 수밖에 없다.
    ///
    /// 관찰 대상 4가지:
    ///   1. EMBEDDED — 플레이어 중심 픽셀이 solid (= 지형에 파묻힘)
    ///   2. FALLTHRU — 한 스텝의 발 스윕이 solid 슬랩을 지나 그 아래 빈 공간으로 빠져나옴 (착지 관통)
    ///   3. TUNNEL   — 한 물리 스텝에 청크 경계선을 넘어 크게 순간이동
    ///   4. NOCOVER  — 지형은 solid인데 그 청크 콜라이더가 그 점을 안 덮음 (증상의 직접 원인)
    ///   4. TRACE    — TerrainCollider가 윤곽을 못 닫았다고 스스로 신고 (원인의 원인)
    ///
    /// 잡히면 Debug.Log로 상세 덤프를 찍는다. 로그는 BugReportLogBuffer가 물고 있으므로
    /// F12 리포트의 log.txt에 그대로 실린다. F12를 눌렀을 땐 사고가 없어도
    /// 직전 몇 초 궤적과 지금 이음매 상태를 같이 싣는다.
    ///
    /// 씬 배치 불필요 — RuntimeInitializeOnLoadMethod로 자동 생성된다.
    /// 설계: Assets/Docs/qa/terrain-seam-watchdog.md
    /// </summary>
    public class TerrainSeamWatchdog : MonoBehaviour
    {
        // ========================================================================================================
        //  튜닝
        // ========================================================================================================

        /// <summary>전체 on/off. 콘솔 `seam off`.</summary>
        public static bool Enabled = true;
        /// <summary>NOCOVER 격자 탐침 on/off. 부하가 느껴지면 끈다.</summary>
        public static bool ProbeEnabled = true;
        /// <summary>
        /// 사고 시 F12 리포트를 자동으로 띄울지. 세션당 AutoCaptureLimit번까지.
        ///
        /// 기본 off — 탐지기는 휴리스틱이라 오탐이 남는데(특히 엘리베이터 지형),
        /// 오탐 한 번의 대가가 "플레이 중 화면을 뺏고 게임을 멈추는 것"이라 너무 비싸다.
        /// 사고는 어차피 Debug.LogError 덤프로 남고, F12를 누르면 그 덤프가 리포트에 실린다.
        /// 켜려면 콘솔 `seam auto`.
        /// </summary>
        public static bool AutoCapture = false;

        private const int   AutoCaptureLimit = 3;
        private const float ProbeInterval    = 0.2f;   // NOCOVER 탐침 주기(초)
        private const float DumpCooldown     = 1.5f;   // 같은 사고가 연속으로 도배되는 것 방지(초)

        /// <summary>한 물리 스텝 이동량이 이보다 크고 경계선을 넘었으면 TUNNEL.</summary>
        private const float TunnelStepDistance = 0.5f;

        /// <summary>
        /// 물리 적분이 한 스텝에 낼 수 있는 이동량 여유배수 + 여유값(유닛).
        /// Rigidbody2D는 한 스텝에 최대 vel*dt만큼 움직인다 — 이보다 크게 튀었으면
        /// 물리가 아니라 코드가 좌표를 대입한 것(엘리베이터·스폰·던전 출입)이다.
        /// </summary>
        private const float ScriptedMoveFactor = 2f;
        private const float ScriptedMoveSlack  = 0.25f;

        /// <summary>텔레포트 직후 이 시간 동안은 탐지를 쉰다(착지 보호 시퀀스가 끝날 때까지).</summary>
        private const float SuppressAfterTeleport = 1.0f;

        /// <summary>탐침 격자: 중심에서 ±ProbeExtent 범위를 ProbeStep 간격으로.</summary>
        private const float ProbeExtent = 0.5f;
        private const float ProbeStep   = 0.05f;

        /// <summary>
        /// "확실히 지형 안쪽"으로 볼 여유(픽셀)에 더하는 안전 여유.
        /// 콜라이더 윤곽은 픽셀 '중심'을 잇고 거기에 RDP 단순화가 얹히므로,
        /// 실제 여유는 `DeepMarginPx`가 단순화 허용오차에서 매 프레임 계산한다.
        /// </summary>
        private const int DeepMarginSafetyPx = 2;

        /// <summary>월드 유닛 → 픽셀. 청크 1칸(10u)이 1000px.</summary>
        private const float PixelsPerUnit = 100f;

        /// <summary>
        /// "확실히 지형 안쪽"으로 볼 여유(픽셀).
        ///
        /// ⚠ 이 값을 상수로 박으면 안 된다. 콜라이더 윤곽은 RDP로 단순화되고
        ///    그 허용오차는 worldSettings.json(`chunk.colliderSimplifyTolerance`)이 정한다.
        ///    현재 설정값 0.05u = 5px — 코드 기본값(0.005u=0.5px)의 10배다.
        ///    3px로 박아두면 "지형 안쪽 3px인데 콜라이더가 없다"가 삽질할 때마다 참이 되어
        ///    (막 판 벽은 들쭉날쭉해서 단순화가 크게 깎는다) NOCOVER 오탐이 쏟아지고,
        ///    AutoCapture가 버그 리포트 창을 저절로 띄운다. 2026-08-22 실측 확인.
        /// </summary>
        private static int DeepMarginPx =>
            Mathf.CeilToInt(TerrainCollider.SimplifyTolerance * PixelsPerUnit) + DeepMarginSafetyPx;

        /// <summary>청크 가장자리에서 이 픽셀 안쪽은 NOCOVER 판정에서 뺀다(이음매 틈은 별도 계측).</summary>
        private const int EdgeExemptPx = 3;

        /// <summary>NOCOVER로 보고할 최소 미덮음 탐침 점 수. 한두 점은 윤곽 단순화의 정상 오차다.</summary>
        private const int NoCoverMinPoints = 8;

        // ── FALLTHRU (착지 관통) ─────────────────────────────────────────────
        // EMBEDDED와 TUNNEL은 이 사고를 못 잡는다.
        //   EMBEDDED : 슬랩을 뚫고 '아래 빈 공간'으로 나오면 중심 픽셀이 solid가 아니다.
        //   TUNNEL   : 청크 경계를 넘어야 하는데, 자기가 판 굴 바닥은 청크 한복판이다.
        // 그래서 한 물리 스텝의 이동선(스윕)을 픽셀 단위로 훑어 직접 본다.

        /// <summary>스윕 샘플 간격(월드 단위). 0.01u = 1픽셀.</summary>
        private const float SweepStepU = 0.01f;

        /// <summary>지나친 solid 픽셀이 이보다 적으면 무시 — 표면 1픽셀 스침은 상시로 있다.</summary>
        private const int FallThruMinSolidPx = 2;

        /// <summary>
        /// solid 구간을 지난 뒤 이만큼 빈 공간이 이어져야 '뚫고 나온 것'이다.
        /// 정상 착지는 solid 안에서 끝난다 — 콜라이더 윤곽이 픽셀보다 최대
        /// <see cref="DeepMarginPx"/>만큼 안쪽이라 발이 원래 조금 잠긴다.
        /// </summary>
        private const int FallThruMinClearPx = 3;

        /// <summary>스윕 샘플 상한(억제를 빠져나온 텔레포트 방어).</summary>
        private const int SweepMaxSamples = 512;

        private const int RingCapacity = 256;

        // ========================================================================================================
        //  상태
        // ========================================================================================================

        private struct Sample
        {
            public float time;
            public int frame;
            public Vector2 pos;
            public Vector2 vel;
            public Vector2Int chunk;
            public bool grounded;
            public bool solidAtCenter;
        }

        private static TerrainSeamWatchdog s_instance;

        private readonly Sample[] _ring = new Sample[RingCapacity];
        private int _ringCount;
        private int _ringHead;

        private Transform _player;
        private Rigidbody2D _playerRb;
        private PlayerController _playerCtrl;
        private float _nextPlayerLookup;

        private Vector2 _prevPos;
        private bool _hasPrevPos;
        private int _embeddedStreak;

        // 낙하 높이·체공 시간 — "조금만 위에서 떨어져도"를 수치로 확인하기 위한 것.
        private float _lastGroundedY;
        private float _lastGroundedTime;
        private bool _hasGroundedY;

        private float _suppressUntil;

        private float _nextProbeTime;
        private float _lastDumpTime = -999f;
        private int _autoCaptureCount;

        private string _lastEventSummary = "(없음)";
        private int _eventCount;

        // TerrainCollider.OnAnomaly는 물리 스텝 밖에서도 오므로 큐에 담아 다음 프레임에 덤프한다.
        private readonly Queue<string> _pendingColliderAnomalies = new Queue<string>();

        private readonly StringBuilder _sb = new StringBuilder(4096);

        // ========================================================================================================
        //  부트스트랩
        // ========================================================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureExists();
            SceneManager.sceneLoaded -= OnSceneLoadedEnsure;
            SceneManager.sceneLoaded += OnSceneLoadedEnsure;

            // 명령 등록은 여기 한 번만 — 정적 레지스트리라 파괴와 무관하게 살아남는다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
            RegisterCommands();
#endif
        }

        private static void OnSceneLoadedEnsure(Scene scene, LoadSceneMode mode) => EnsureExists();

        /// <summary>
        /// ⚠ 자가복구: GameManager.OpenMainMenu의 DestroyPersistentObjects()가
        /// DontDestroyOnLoad 씬의 루트를 전부 파괴해 이 오브젝트도 같이 죽는다.
        /// [RuntimeInitializeOnLoadMethod]는 세션당 한 번만 돌아 재생성되지 않으므로,
        /// 메인메뉴를 다녀오면 이음매 감시가 꺼져 있었다. static 이벤트 구독은 파괴와
        /// 무관하게 살아남으므로 씬 로드마다 되살린다(SoundManager와 같은 패턴).
        /// </summary>
        private static void EnsureExists()
        {
            if (s_instance != null) return;

            var go = new GameObject("[TerrainSeamWatchdog]");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<TerrainSeamWatchdog>();
        }

        private void OnEnable()
        {
            TerrainCollider.OnAnomaly += OnColliderAnomaly;
            BugReport.BugReportCollector.ExtraContext += OnCollectExtraContext;
        }

        private void OnDisable()
        {
            TerrainCollider.OnAnomaly -= OnColliderAnomaly;
            BugReport.BugReportCollector.ExtraContext -= OnCollectExtraContext;
        }

        // ========================================================================================================
        //  메인 루프
        // ========================================================================================================

        private void FixedUpdate()
        {
            if (!Enabled) return;
            if (!ResolvePlayer()) return;

            // ── 0. 탐지를 쉬어야 하는 구간.
            //    (a) Rigidbody가 Dynamic이 아니면 물리가 좌표를 쥐고 있지 않다 —
            //        엘리베이터·스폰의 착지 보호(Kinematic 고정 → 청크 로드 대기)가 여기다.
            //        이때 플레이어는 아직 로드 안 된 지형 속에 있는 게 정상이라 EMBEDDED가 무조건 뜬다.
            //    (b) 명시적 억제 창(SuppressFor) — 던전 출입처럼 Dynamic인 채로 좌표만 대입하는 경로.
            //    쉬는 동안 궤적 추적을 끊어야 복귀 첫 스텝이 TUNNEL로 오인되지 않는다.
            bool scriptedControl = _playerRb != null && _playerRb.bodyType != RigidbodyType2D.Dynamic;
            if (scriptedControl || Time.time < _suppressUntil)
            {
                _hasPrevPos = false;
                _embeddedStreak = 0;
                _hasGroundedY = false;
                return;
            }

            Vector2 pos = _player.position;
            Vector2 vel = _playerRb != null ? _playerRb.linearVelocity : Vector2.zero;
            Vector2Int chunkCoord = ChunkCoords.ToChunk(pos);

            bool solidHere = IsSolidAt(pos);

            bool groundedNow = _playerCtrl != null && _playerCtrl.IsGrounded;
            if (groundedNow)
            {
                _lastGroundedY = pos.y;
                _lastGroundedTime = Time.time;
                _hasGroundedY = true;
            }

            Push(new Sample
            {
                time = Time.time,
                frame = Time.frameCount,
                pos = pos,
                vel = vel,
                chunk = chunkCoord,
                grounded = groundedNow,
                solidAtCenter = solidHere,
            });

            // ── 1. EMBEDDED: 지형 안에 파묻힘.
            //    파기 직후 1스텝은 정상적으로 겹칠 수 있어 2스텝 연속일 때만 사고로 본다.
            if (solidHere)
            {
                _embeddedStreak++;
                if (_embeddedStreak == 2) Report("EMBEDDED", "플레이어 중심 픽셀이 solid — 지형에 파묻힘", true);
            }
            else
            {
                _embeddedStreak = 0;
            }

            if (_hasPrevPos)
            {
                Vector2 delta = pos - _prevPos;

                // ── 2. FALLTHRU: 발 스윕이 슬랩을 지나 아래 빈 공간으로 빠져나옴.
                CheckFallThrough(_prevPos, pos, vel);

                // ── 3. TUNNEL: 한 스텝에 경계선을 넘어 크게 이동.
                if (delta.sqrMagnitude > TunnelStepDistance * TunnelStepDistance &&
                    CrossedChunkBoundary(_prevPos, pos))
                {
                    // 속도로 설명되지 않는 이동은 물리 관통이 아니라 코드의 좌표 대입이다.
                    // 이 게이트가 없으면 엘리베이터·스폰·던전 출입 때마다 사고로 잡혀
                    // 버그 리포트 창이 저절로 뜬다.
                    if (IsScriptedMove(delta.magnitude, vel.magnitude, Time.fixedDeltaTime))
                    {
                        _prevPos = pos;
                        _hasPrevPos = true;
                        _embeddedStreak = 0;
                        _suppressUntil = Time.time + SuppressAfterTeleport;
                        return;
                    }

                    Report("TUNNEL",
                        $"한 물리 스텝에 {delta.magnitude:F2}u 이동하며 청크 경계선 통과 " +
                        $"({_prevPos.x:F2},{_prevPos.y:F2}) → ({pos.x:F2},{pos.y:F2})", true);
                }
            }
            _prevPos = pos;
            _hasPrevPos = true;

            // ── 4. NOCOVER: 지형은 있는데 콜라이더가 없는 지점.
            if (ProbeEnabled && Time.time >= _nextProbeTime)
            {
                _nextProbeTime = Time.time + ProbeInterval;
                ProbeCoverage(pos);
            }
        }

        /// <summary>
        /// 코드가 플레이어를 순간이동시킬 때 호출 — 그 동안 탐지를 쉰다.
        /// Kinematic으로 묶는 경로는 자동으로 걸러지므로, Dynamic인 채로 좌표만
        /// 대입하는 경로(던전 출입 등)에서만 필요하다.
        /// </summary>
        public static void SuppressFor(float seconds = SuppressAfterTeleport)
        {
            if (s_instance == null) return;
            float until = Time.time + Mathf.Max(0f, seconds);
            if (until > s_instance._suppressUntil) s_instance._suppressUntil = until;
            s_instance._hasPrevPos = false;
            s_instance._embeddedStreak = 0;
        }

        /// <summary>
        /// 한 스텝 이동량이 물리 적분으로 설명되지 않으면(=코드가 좌표를 대입했으면) true.
        /// 순수 함수 — EditMode 테스트 대상.
        /// </summary>
        public static bool IsScriptedMove(float stepDistance, float speed, float fixedDeltaTime)
        {
            float physicsMax = speed * fixedDeltaTime * ScriptedMoveFactor + ScriptedMoveSlack;
            return stepDistance > physicsMax;
        }

        private void Update()
        {
            while (_pendingColliderAnomalies.Count > 0)
                Report("TRACE", _pendingColliderAnomalies.Dequeue(), false);
        }

        private void OnColliderAnomaly(TerrainCollider source, string reason)
        {
            if (!Enabled) return;
            if (_pendingColliderAnomalies.Count < 8) _pendingColliderAnomalies.Enqueue(reason);
        }

        // ========================================================================================================
        //  탐지기
        // ========================================================================================================

        /// <summary>
        /// 플레이어 주변 격자에서 "확실히 지형 안쪽인데 콜라이더가 안 덮는" 점을 찾는다.
        /// 청크 가장자리 EdgeExemptPx 이내는 뺀다 — 거기 1픽셀 틈은 구조적으로 항상 있고
        /// (콜라이더 윤곽이 픽셀 중심을 잇는다) 그건 경보가 아니라 AppendSeamScan이 실측으로 남긴다.
        /// </summary>
        private void ProbeCoverage(Vector2 center)
        {
            var map = InfinityMapManager.Instance;
            if (map == null) return;

            int uncovered = 0;
            Vector2 firstHit = Vector2.zero;
            TerrainChunk firstChunk = null;
            int firstPaths = 0;

            for (float dy = -ProbeExtent; dy <= ProbeExtent; dy += ProbeStep)
            {
                for (float dx = -ProbeExtent; dx <= ProbeExtent; dx += ProbeStep)
                {
                    Vector2 p = new Vector2(center.x + dx, center.y + dy);
                    if (!IsDeepSolid(p)) continue;

                    var chunk = map.GetChunk(ChunkCoords.ToChunk(p));
                    if (chunk == null) continue;

                    var poly = chunk.GetComponent<PolygonCollider2D>();
                    if (poly == null || !poly.enabled) continue;
                    if (poly.OverlapPoint(p)) continue;

                    if (uncovered == 0)
                    {
                        firstHit = p;
                        firstChunk = chunk;
                        firstPaths = poly.pathCount;
                    }
                    uncovered++;
                }
            }

            // 점 하나로는 사고라고 못 한다. 윤곽 단순화·와인딩 때문에 경계 근처엔
            // 한두 점씩 늘 어긋나는 자리가 생긴다. 몸이 빠질 정도의 구멍이면
            // 탐침 격자(간격 5px)에 여러 점이 한꺼번에 걸린다.
            if (uncovered < NoCoverMinPoints) return;

            Report("NOCOVER",
                $"({firstHit.x:F3},{firstHit.y:F3}) 등 {uncovered}점이 지형 안쪽 {DeepMarginPx}px인데 " +
                $"청크({firstChunk.ChunkX},{firstChunk.ChunkY}) 콜라이더가 덮지 않음 " +
                $"(paths={firstPaths})", true);
        }

        /// <summary>
        /// 한 물리 스텝의 '발' 이동선을 1픽셀 간격으로 훑어, solid 슬랩을 지나
        /// 그 아래 빈 공간으로 빠져나왔는지 본다. 빠져나왔으면 착지 관통이다.
        ///
        /// 정상 착지와의 구분: 정상 착지는 스윕이 solid 안에서 끝난다(콜라이더 윤곽이
        /// 지형 픽셀보다 최대 <see cref="DeepMarginPx"/>만큼 안쪽이라 발이 원래 조금 잠긴다).
        /// 슬랩을 지난 뒤 <see cref="FallThruMinClearPx"/>픽셀 이상 빈 공간이 이어질 때만 사고로 본다.
        ///
        /// 덤프의 핵심은 마지막 한 줄이다 — 같은 구간에 Linecast를 쏴서
        /// '콜라이더가 있었는데 뚫은 것'과 '애초에 콜라이더가 없던 것'을 갈라준다.
        /// 이 둘은 고치는 곳이 완전히 다르다(물리 설정 vs 콜라이더 생성).
        /// </summary>
        private void CheckFallThrough(Vector2 prev, Vector2 cur, Vector2 vel)
        {
            Vector2 delta = cur - prev;
            if (delta.y >= 0f) return;                                              // 내려가는 스텝만
            if (IsScriptedMove(delta.magnitude, vel.magnitude, Time.fixedDeltaTime)) return;

            // 몸 중심이 아니라 발 밑을 본다 — 슬랩을 먼저 만나는 건 발이다.
            float footOffset = _playerCtrl != null && _playerCtrl.groundCollider != null
                ? _playerCtrl.groundCollider.bounds.min.y - cur.y
                : 0f;
            Vector2 a = new Vector2(prev.x, prev.y + footOffset);
            Vector2 b = new Vector2(cur.x, cur.y + footOffset);

            int steps = Mathf.CeilToInt((b - a).magnitude / SweepStepU);
            if (steps < FallThruMinSolidPx + FallThruMinClearPx) return;
            if (steps > SweepMaxSamples) steps = SweepMaxSamples;

            int solidCount = 0;
            int trailingClear = 0;
            Vector2 firstSolid = Vector2.zero, lastSolid = Vector2.zero;

            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                if (IsSolidAt(p))
                {
                    if (solidCount == 0) firstSolid = p;
                    lastSolid = p;
                    solidCount++;
                    trailingClear = 0;
                }
                else if (solidCount > 0)
                {
                    trailingClear++;
                }
            }

            if (solidCount < FallThruMinSolidPx || trailingClear < FallThruMinClearPx) return;

            // 슬랩을 '세로로' 지났어야 한다. 이 조건이 없으면 난간 모서리를 스치며
            // 걸어 내려가는 스텝(가로 이동이 지배적)이 전부 사고로 잡힌다.
            if (firstSolid.y - lastSolid.y < FallThruMinSolidPx * SweepStepU) return;

            // ── 범인 가르기 ──────────────────────────────────────────────────
            int mask = _playerCtrl != null ? _playerCtrl.groundLayer.value : Physics2D.AllLayers;
            RaycastHit2D hit = Physics2D.Linecast(a, b, mask);

            Vector2 mid = (firstSolid + lastSolid) * 0.5f;
            var map = InfinityMapManager.Instance;
            var chunk = map != null ? map.GetChunk(ChunkCoords.ToChunk(mid)) : null;
            var poly = chunk != null ? chunk.GetComponent<PolygonCollider2D>() : null;
            var diag = chunk != null ? chunk.ColliderManager : null;

            bool covered = poly != null && poly.enabled && poly.OverlapPoint(mid);
            float age = diag != null && diag.LastUpdateTime >= 0f ? Time.time - diag.LastUpdateTime : -1f;
            float slabPx = Mathf.Abs(firstSolid.y - lastSolid.y) * PixelsPerUnit;
            float fallHeight = _hasGroundedY ? _lastGroundedY - cur.y : -1f;
            float airTime = _hasGroundedY ? Time.time - _lastGroundedTime : -1f;

            string verdict = hit.collider != null
                ? "콜라이더는 있었는데 통과 → 물리 관통(플레이어 CCD가 Discrete / 낙하속도가 스텝당 몸길이 초과)"
                : covered
                    ? "Linecast는 못 맞췄는데 OverlapPoint는 덮음 → 스윕 판정 실패(레이어 마스크 또는 broad-phase 확인)"
                    : "그 자리에 콜라이더가 없음 → 얇은 슬랩이 윤곽 단순화로 사라진 것";

            Report("FALLTHRU",
                $"{verdict}\n" +
                $"    발 스윕 ({a.x:F3},{a.y:F3}) → ({b.x:F3},{b.y:F3})  " +
                $"스텝이동 {delta.magnitude:F3}u  vy={vel.y:F2}  " +
                $"낙하높이 {fallHeight:F2}u  체공 {airTime:F2}s  dt={Time.fixedDeltaTime:F4}\n" +
                $"    지나친 solid {solidCount}px (두께 {slabPx:F0}px, y {firstSolid.y:F3} → {lastSolid.y:F3}), " +
                $"통과 후 빈공간 {trailingClear}px\n" +
                $"    Linecast={(hit.collider != null ? hit.collider.name : "없음")}  OverlapPoint={covered}  " +
                $"paths={(poly != null ? poly.pathCount : -1)}  colDirty={(chunk != null && chunk.isDirty)}  " +
                $"갱신후={age:F2}s  실패윤곽={(diag != null ? diag.LastFailedTraces : -1)}  " +
                $"단순화={TerrainCollider.SimplifyTolerance * PixelsPerUnit:F1}px",
                true);
        }

        private static bool CrossedChunkBoundary(Vector2 a, Vector2 b)
        {
            Vector2Int ca = ChunkCoords.ToChunk(a);
            Vector2Int cb = ChunkCoords.ToChunk(b);
            return ca != cb;
        }

        // ========================================================================================================
        //  지형 조회
        // ========================================================================================================

        /// <summary>월드 좌표 → 그 좌표가 속한 청크의 픽셀 좌표.</summary>
        private static Vector2Int WorldToPixel(Vector2 world, Vector2Int chunkCoord)
        {
            float localX = world.x - chunkCoord.x * ChunkCoords.WorldSize;
            float localY = world.y - chunkCoord.y * ChunkCoords.WorldSize;
            // PPU는 청크 1칸(=ChunkCoords.WorldSize 10u)이 1000px이라는 데서 나온다.
            // 청크 프리팹의 pixelsPerUnit이 바뀌면 여기도 같이 바뀌어야 한다.
            return new Vector2Int(
                Mathf.FloorToInt(localX * PixelsPerUnit),
                Mathf.FloorToInt(localY * PixelsPerUnit));
        }

        private static bool IsSolidAt(Vector2 world)
        {
            var map = InfinityMapManager.Instance;
            if (map == null) return false;
            Vector2Int cc = ChunkCoords.ToChunk(world);
            var chunk = map.GetChunk(cc);
            if (chunk == null) return false;

            // 불괴 오버레이 청크는 BasePixels가 solid여도 지형 콜라이더가 안 만들어지는 게 정상이라
            // EMBEDDED 판정에서 뺀다(IsDeepSolid와 같은 이유).
            if (chunk.Data != null && chunk.Data.HasIndestructiblePixels) return false;

            Vector2Int px = WorldToPixel(world, cc);
            return chunk.GetPixelAlpha(px.x, px.y) != 0;
        }

        /// <summary>
        /// 표면에서 DeepMarginPx만큼 안쪽이고 청크 가장자리에서도 EdgeExemptPx 이상 떨어진 solid 점인가.
        /// 여기서 true면 "콜라이더가 반드시 덮어야 하는 점"이다.
        /// </summary>
        private static bool IsDeepSolid(Vector2 world)
        {
            var map = InfinityMapManager.Instance;
            if (map == null) return false;

            Vector2Int cc = ChunkCoords.ToChunk(world);
            var chunk = map.GetChunk(cc);
            if (chunk == null) return false;

            // 불괴 오버레이 픽셀은 '설계상' 지형 콜라이더에서 빠진다(오버레이 자체 콜라이더가 담당).
            // 그런 청크를 탐침하면 전부 오탐이 되므로 통째로 뺀다.
            if (chunk.Data != null && chunk.Data.HasIndestructiblePixels) return false;

            int margin = DeepMarginPx;

            Vector2Int px = WorldToPixel(world, cc);
            // 가장자리 제외도 같은 여유를 쓴다 — 단순화가 커지면 이음매 근처 오탐도 같이 커진다.
            int edgeExempt = Mathf.Max(EdgeExemptPx, margin);
            if (px.x < edgeExempt || px.y < edgeExempt ||
                px.x >= chunk.width - edgeExempt || px.y >= chunk.height - edgeExempt) return false;

            // 자기 자신 + 8방향 margin 지점이 모두 solid여야 '안쪽'이다.
            // 대각선을 빼면 오목한 모서리(막 판 벽에 널렸다)가 '안쪽'으로 통과해 오탐이 된다.
            if (chunk.GetPixelAlpha(px.x, px.y) == 0) return false;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if (chunk.GetPixelAlpha(px.x + dx * margin, px.y + dy * margin) == 0) return false;
            }
            return true;
        }

        // ========================================================================================================
        //  리포팅
        // ========================================================================================================

        private void Report(string kind, string detail, bool severe)
        {
            _eventCount++;
            _lastEventSummary = $"{kind} @t={Time.time:F2} {detail}";

            if (Time.time - _lastDumpTime < DumpCooldown) return;
            _lastDumpTime = Time.time;

            Debug.LogError(BuildDump(kind, detail));

            if (severe && AutoCapture && _autoCaptureCount < AutoCaptureLimit)
            {
                _autoCaptureCount++;
                BugReport.BugReportSystem.Capture();
            }
        }

        /// <summary>F12를 눌렀을 때 — 사고가 없어도 현재 이음매 상태와 궤적을 리포트에 남긴다.</summary>
        private void OnCollectExtraContext(Dictionary<string, string> bag)
        {
            bag["seamWatchdog"] = Enabled ? $"on (사고 {_eventCount}건)" : "off";
            bag["seamWatchdogLastEvent"] = _lastEventSummary;

            // 상세는 로그로 — report.json에 넣으면 통째로 이스케이프되어 못 읽는다.
            // Collect()는 log.txt 스냅샷보다 먼저 돌기 때문에 이 로그도 리포트에 실린다.
            Debug.Log(BuildDump("F12", "사용자 리포트 시점 스냅샷"));
        }

        private string BuildDump(string kind, string detail)
        {
            _sb.Clear();
            _sb.Append("[SeamWatchdog] ===== ").Append(kind).Append(" =====\n");
            _sb.Append("  ").Append(detail).Append('\n');
            _sb.Append($"  frame={Time.frameCount} time={Time.time:F2} timeScale={Time.timeScale:F2}\n");

            if (!ResolvePlayer())
            {
                _sb.Append("  플레이어를 찾지 못함\n");
                return _sb.ToString();
            }

            Vector2 pos = _player.position;
            Vector2Int cc = ChunkCoords.ToChunk(pos);
            Vector2Int px = WorldToPixel(pos, cc);

            AppendPlayerBlock(pos, cc, px);
            AppendChunkTable(cc, pos);
            AppendTerrainVsColliderMap(pos);
            AppendSeamScan(pos, cc, px);
            AppendTrajectory();

            _sb.Append("[SeamWatchdog] ================================\n");
            return _sb.ToString();
        }

        private void AppendPlayerBlock(Vector2 pos, Vector2Int cc, Vector2Int px)
        {
            var own = InfinityMapManager.Instance != null ? InfinityMapManager.Instance.GetChunk(cc) : null;
            int w = own != null ? own.width : 1000;
            int h = own != null ? own.height : 1000;

            _sb.Append("  -- 플레이어 --\n");
            _sb.Append($"  worldPos=({pos.x:F3},{pos.y:F3})  chunk=({cc.x},{cc.y})  localPx=({px.x},{px.y})\n");
            _sb.Append($"  청크 가장자리까지: 좌={px.x}px 우={w - 1 - px.x}px 하={px.y}px 상={h - 1 - px.y}px\n");
            _sb.Append($"  중심픽셀 solid={IsSolidAt(pos)}  grounded={(_playerCtrl != null ? _playerCtrl.IsGrounded.ToString() : "?")}\n");

            if (_playerRb != null)
            {
                _sb.Append($"  rb: vel=({_playerRb.linearVelocity.x:F2},{_playerRb.linearVelocity.y:F2}) " +
                           $"body={_playerRb.bodyType} ccd={_playerRb.collisionDetectionMode} " +
                           $"interp={_playerRb.interpolation} simulated={_playerRb.simulated} " +
                           $"sleep={_playerRb.IsSleeping()}\n");
            }

            var cols = _player.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                _sb.Append($"  col[{i}] {c.GetType().Name} on '{c.gameObject.name}' " +
                           $"enabled={c.enabled} trigger={c.isTrigger} bounds={c.bounds}\n");
            }
        }

        private void AppendChunkTable(Vector2Int center, Vector2 playerPos)
        {
            _sb.Append("  -- 주변 3x3 청크 콜라이더 상태 --\n");
            _sb.Append("  coord    | loaded active colEn paths verts colDirty  갱신후(s) 실패윤곽 덮음\n");

            var map = InfinityMapManager.Instance;
            for (int dy = 1; dy >= -1; dy--)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var coord = new Vector2Int(center.x + dx, center.y + dy);
                    var chunk = map != null ? map.GetChunk(coord) : null;

                    if (chunk == null)
                    {
                        _sb.Append($"  {coord.x,3},{coord.y,-3} | -      (로드 안 됨)\n");
                        continue;
                    }

                    var poly = chunk.GetComponent<PolygonCollider2D>();
                    var diag = chunk.ColliderManager;

                    int verts = 0;
                    if (poly != null)
                        for (int p = 0; p < poly.pathCount; p++) verts += poly.GetPath(p).Length;

                    string age = diag != null && diag.LastUpdateTime >= 0f
                        ? (Time.time - diag.LastUpdateTime).ToString("F2") : "?";

                    _sb.Append($"  {coord.x,3},{coord.y,-3} | O      " +
                               $"{chunk.gameObject.activeSelf,-6} " +
                               $"{(poly != null && poly.enabled),-5} " +
                               $"{(poly != null ? poly.pathCount : -1),-5} " +
                               $"{verts,-5} " +
                               $"{chunk.isDirty,-9} " +
                               $"{age,-9} " +
                               $"{(diag != null ? diag.LastFailedTraces : -1),-8} " +
                               $"{(poly != null && poly.OverlapPoint(playerPos))}\n");
                }
            }
        }

        /// <summary>
        /// 플레이어 주변을 한 장에 겹쳐 그린다 — 지형과 콜라이더가 어긋나는 자리('!')가 곧 통과 지점이다.
        /// 격자가 어느 청크에 걸쳐 있는지는 바로 위 '청크 가장자리까지' 픽셀 거리로 읽는다.
        /// </summary>
        private void AppendTerrainVsColliderMap(Vector2 pos)
        {
            const int Half = 10;          // 21x21
            const float Step = 0.05f;     // 5px 간격 → 세로 ±0.5u

            _sb.Append($"  -- 주변 지형 vs 콜라이더 (간격 {Step * 100f:F0}px, 중앙=플레이어) --\n");
            _sb.Append("     '#'=지형(콜라이더 있음) '.'=공기 '+'=공기인데 콜라이더 안 " +
                       "'!'=지형인데 콜라이더 없음 'P'=플레이어 '?'=청크 미로드\n");

            var map = InfinityMapManager.Instance;
            if (map == null) { _sb.Append("     (맵 매니저 없음)\n"); return; }

            for (int gy = Half; gy >= -Half; gy--)
            {
                _sb.Append("     ");
                for (int gx = -Half; gx <= Half; gx++)
                {
                    Vector2 p = new Vector2(pos.x + gx * Step, pos.y + gy * Step);

                    if (gx == 0 && gy == 0) { _sb.Append('P'); continue; }

                    var chunk = map.GetChunk(ChunkCoords.ToChunk(p));
                    if (chunk == null) { _sb.Append('?'); continue; }

                    Vector2Int lp = WorldToPixel(p, ChunkCoords.ToChunk(p));
                    bool solid = chunk.GetPixelAlpha(lp.x, lp.y) != 0;

                    var poly = chunk.GetComponent<PolygonCollider2D>();
                    bool covered = poly != null && poly.enabled && poly.OverlapPoint(p);

                    if (solid && !covered) _sb.Append('!');
                    else if (solid)        _sb.Append('#');
                    else if (covered)      _sb.Append('+');
                    else                   _sb.Append('.');
                }
                _sb.Append('\n');
            }
        }

        /// <summary>
        /// 가장 가까운 청크 경계를 가로질러 1픽셀씩 훑으며 지형/콜라이더를 잰다.
        /// "이음매 틈이 실제로 몇 픽셀인가"를 숫자로 남기는 것이 목적.
        /// </summary>
        private void AppendSeamScan(Vector2 pos, Vector2Int cc, Vector2Int px)
        {
            // 경계에서 60px(0.6u) 안쪽일 때만 — 그보다 멀면 이음매가 통과 원인일 수 없다.
            var own = InfinityMapManager.Instance != null ? InfinityMapManager.Instance.GetChunk(cc) : null;
            int w = own != null ? own.width : 1000;
            int h = own != null ? own.height : 1000;

            bool nearVertical   = px.x < 60 || px.x > w - 61;
            bool nearHorizontal = px.y < 60 || px.y > h - 61;
            if (!nearVertical && !nearHorizontal)
            {
                _sb.Append("  -- 이음매 스캔: 경계에서 멀어 생략 --\n");
                return;
            }

            var map = InfinityMapManager.Instance;
            if (map == null) return;

            if (nearVertical)
            {
                float seamX = (px.x < w / 2 ? cc.x : cc.x + 1) * ChunkCoords.WorldSize;
                _sb.Append($"  -- 세로 이음매 x={seamX:F2} 가로 스캔 (플레이어 y={pos.y:F3}, 1px 간격, ±20px) --\n");
                ScanLine(map, seamX, pos.y, true);
            }
            if (nearHorizontal)
            {
                float seamY = (px.y < h / 2 ? cc.y : cc.y + 1) * ChunkCoords.WorldSize;
                _sb.Append($"  -- 가로 이음매 y={seamY:F2} 세로 스캔 (플레이어 x={pos.x:F3}, 1px 간격, ±20px) --\n");
                ScanLine(map, seamY, pos.x, false);
            }
        }

        private void ScanLine(InfinityMapManager map, float seamCoord, float otherCoord, bool horizontalScan)
        {
            _sb.Append("     지형: ");
            AppendScanRow(map, seamCoord, otherCoord, horizontalScan, wantCollider: false);
            _sb.Append("     콜라: ");
            AppendScanRow(map, seamCoord, otherCoord, horizontalScan, wantCollider: true);
            _sb.Append("            (좌=이음매-20px, 우=이음매+20px)\n");
        }

        private void AppendScanRow(InfinityMapManager map, float seamCoord, float otherCoord,
                                   bool horizontalScan, bool wantCollider)
        {
            for (int i = -20; i <= 20; i++)
            {
                // 픽셀 '중심'을 찍어야 콜라이더 윤곽(픽셀 중심을 잇는 선)과 같은 기준이 된다.
                float c = seamCoord + (i + 0.5f) * 0.01f;
                Vector2 p = horizontalScan ? new Vector2(c, otherCoord) : new Vector2(otherCoord, c);

                var chunk = map.GetChunk(ChunkCoords.ToChunk(p));
                if (chunk == null) { _sb.Append('?'); continue; }

                if (wantCollider)
                {
                    var poly = chunk.GetComponent<PolygonCollider2D>();
                    _sb.Append(poly != null && poly.enabled && poly.OverlapPoint(p) ? '+' : '.');
                }
                else
                {
                    Vector2Int lp = WorldToPixel(p, ChunkCoords.ToChunk(p));
                    _sb.Append(chunk.GetPixelAlpha(lp.x, lp.y) != 0 ? '#' : '.');
                }
            }
            _sb.Append('\n');
        }

        private void AppendTrajectory()
        {
            int show = Mathf.Min(_ringCount, 40);
            _sb.Append($"  -- 직전 {show}스텝 궤적 (오래된 것부터) --\n");

            for (int i = show - 1; i >= 0; i--)
            {
                int idx = (_ringHead - 1 - i + RingCapacity * 2) % RingCapacity;
                var s = _ring[idx];
                _sb.Append($"   t={s.time:F3} f={s.frame} pos=({s.pos.x:F3},{s.pos.y:F3}) " +
                           $"vel=({s.vel.x:F2},{s.vel.y:F2}) chunk=({s.chunk.x},{s.chunk.y}) " +
                           $"grounded={s.grounded} solid={s.solidAtCenter}\n");
            }
        }

        // ========================================================================================================
        //  헬퍼
        // ========================================================================================================

        private void Push(Sample s)
        {
            _ring[_ringHead] = s;
            _ringHead = (_ringHead + 1) % RingCapacity;
            if (_ringCount < RingCapacity) _ringCount++;
        }

        /// <summary>플레이어 참조 확보. 씬 전환·리스폰으로 끊기므로 주기적으로 다시 찾는다.</summary>
        private bool ResolvePlayer()
        {
            if (_player != null) return true;
            if (Time.unscaledTime < _nextPlayerLookup) return false;
            _nextPlayerLookup = Time.unscaledTime + 1f;

            var stat = FindFirstObjectByType<PlayerStat>();
            if (stat == null) return false;

            _player = stat.transform;
            _playerRb = _player.GetComponent<Rigidbody2D>();
            _playerCtrl = _player.GetComponent<PlayerController>();
            _hasPrevPos = false;
            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
        private static void RegisterCommands()
        {
            DebugTools.DebugCommandRegistry.Register("seam", "seam [on|off|probe|auto|dump]",
                "청크 이음매 통과 감시기. 인자 없으면 현황, dump=지금 상태 덤프", SeamCommand, "이음매");
        }

        private static string SeamCommand(string[] args)
        {
            if (s_instance == null) return "감시기가 없습니다.";

            if (args.Length == 0)
                return $"seam: {(Enabled ? "on" : "off")} / probe={(ProbeEnabled ? "on" : "off")} / " +
                       $"auto={(AutoCapture ? "on" : "off")} / 사고 {s_instance._eventCount}건\n" +
                       $"마지막: {s_instance._lastEventSummary}";

            switch (args[0].ToLowerInvariant())
            {
                case "on":  Enabled = true;  return "감시기 on";
                case "off": Enabled = false; return "감시기 off";
                case "probe":
                    ProbeEnabled = !ProbeEnabled;
                    return $"NOCOVER 탐침 {(ProbeEnabled ? "on" : "off")}";
                case "auto":
                    AutoCapture = !AutoCapture;
                    return $"사고 시 자동 리포트 {(AutoCapture ? "on" : "off")}";
                case "dump":
                    Debug.Log(s_instance.BuildDump("MANUAL", "콘솔 요청"));
                    return "콘솔/로그에 덤프했습니다.";
                default:
                    return "사용법: seam [on|off|probe|auto|dump]";
            }
        }
#endif
    }
}
#endif
