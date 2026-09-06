using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Relic
{
    // 탐지파동(액티브·즉발): 발동 시 반경 내 특수청크를 결정론 예측해 DetectedChunkStore에 기록한다.
    // 미니맵·전체지도가 Store를 구독해 마커를 그린다. 파동 VFX는 OnActivate에서 함께 재생(Task 5).
    [Serializable]
    public class DetectionPulseRelic : RelicBehaviour
    {
        [SerializeField] private int[] radiusPerLevel = { 4, 5, 6 }; // 청크 좌표 체비쇼프 반경
        [SerializeField] private float cooldown = 60f;

        // 소나 확산 속도(유닛/초). 고정 시간이 아니라 고정 속도라서
        // 레벨이 올라 반경이 커져도 링이 퍼지는 체감 속도가 같다.
        [SerializeField] private float pulseSpeed = 6f;
        [SerializeField] private Color ringColor = new Color(0.5f, 0.85f, 1f, 0.85f);
        [SerializeField] private float ringWidth = 0.18f;
        [SerializeField] private Color pingColor = new Color(1f, 0.9f, 0.4f, 1f);

        // ── 재사용 버퍼 (GC 절약) ──────────────────────────────────────────────
        // RelicManager가 슬롯마다 Clone()으로 별도 인스턴스를 들고 있어 슬롯 간 충돌은 없다.
        // ⚠ _pings 이하는 PulseVFX 코루틴이 프레임을 넘겨 읽는다. 새 발동이 버퍼를 덮어쓰기 전에
        //   OnActivate의 _vfxRoutine 가드가 이전 연출을 정리한다 — 가드를 지우면 진행 중인
        //   코루틴이 다음 발동의 데이터를 읽는다.
        private readonly List<(Vector2Int coord, SpecialChunkType type)> _buffer
            = new List<(Vector2Int, SpecialChunkType)>();
        private readonly List<Vector2> _pings = new List<Vector2>();
        private readonly List<Vector2Int> _pingCoords = new List<Vector2Int>();
        private readonly List<GameObject> _pingObjs = new List<GameObject>();
        private readonly List<float> _pingDist = new List<float>();
        private readonly List<bool> _pingFired = new List<bool>();

        // 진행 중인 연출. 링 오브젝트도 CleanupVFX가 회수해야 해서 코루틴 지역변수에서 필드로 뺐다.
        private Coroutine _vfxRoutine;
        private GameObject _ringGo;

        // 링·핑 공용 머티리얼. 색은 LineRenderer의 startColor/endColor(정점 컬러)로 주고
        // 머티리얼 프로퍼티는 아무도 안 건드리므로 인스턴스를 나눌 이유가 없다.
        // 예전엔 링 1개 + 핑 n개마다 new Material(Shader.Find(...))을 돌렸다.
        // ⚠ sharedMaterial로 대입할 것 — Renderer.material은 대입해도 인스턴스 사본이 생긴다.
        // 씬 언로드로 파괴되면 아래 null 체크가 다시 만든다(파괴된 UnityEngine.Object는 == null).
        private static Material s_lineMat;

        private static Material LineMaterial()
        {
            if (s_lineMat == null) s_lineMat = new Material(Shader.Find("Sprites/Default"));
            return s_lineMat;
        }

        private int Radius() => radiusPerLevel[Mathf.Clamp(level - 1, 0, radiusPerLevel.Length - 1)];

        public override float GetDuration() => 0f;          // 즉발 → 바로 쿨타임
        public override float GetCooldown() => cooldown;
        public override bool HasOwnActivationSfx => true;   // DetectionPingSfx의 핑

        public override void OnActivate()
        {
            if (ctx?.player == null) return;
            var scm = SpecialChunkManager.Instance;
            if (scm == null) return;

            Vector2Int origin = ChunkCoords.ToChunk(ctx.player.position);
            int radius = Radius();

            scm.PredictAnchorsInRadius(origin, radius, _buffer);

            var imm = InfinityMapManager.Instance;
            foreach (var hit in _buffer)
            {
                bool visited = imm != null && imm.IsChunkVisited(hit.coord);
                DetectedChunkStore.Instance.Report(hit.coord, hit.type, visited);
            }

            // 파동 VFX (월드 링 + 발견 청크 핑). 순수 연출.
            if (ctx.runner != null)
            {
                // 평소엔 쿨타임(60s)이 연출 길이(~10s)보다 훨씬 길어 겹치지 않는다.
                // 쿨타임 감소 유물이 붙으면 겹칠 수 있으므로 버퍼를 덮어쓰기 전에 이전 연출을 끝낸다.
                // StopCoroutine만 하면 링·핑 오브젝트와 머티리얼이 그대로 남아 CleanupVFX가 따라붙어야 한다.
                if (_vfxRoutine != null)
                {
                    ctx.runner.StopCoroutine(_vfxRoutine);
                    CleanupVFX();
                }

                float worldRadius = radius * ChunkCoords.WorldSize;
                _pings.Clear();
                _pingCoords.Clear();
                foreach (var hit in _buffer)
                {
                    Vector2 c = ChunkCenterWorld(hit.coord);
                    _pings.Add(c);
                    _pingCoords.Add(hit.coord);
                    worldRadius = Mathf.Max(worldRadius, Vector2.Distance(ctx.player.position, c));
                }
                _vfxRoutine = ctx.runner.StartCoroutine(PulseVFX(ctx.player.position, worldRadius));
            }
        }

        private static Vector2 ChunkCenterWorld(Vector2Int coord)
        {
            Vector3 w = ChunkCoords.ToWorld(coord);
            float half = ChunkCoords.WorldSize * 0.5f;
            return new Vector2(w.x + half, w.y + half);
        }

        // 링·핑으로 만든 오브젝트 회수 + 연출 상태 리셋.
        // 정상 종료와 중도 취소(StopCoroutine) 양쪽에서 호출된다.
        // 머티리얼은 s_lineMat 공유라 여기서 파괴하지 않는다.
        private void CleanupVFX()
        {
            for (int i = 0; i < _pingObjs.Count; i++)
            {
                if (_pingObjs[i] != null) UnityEngine.Object.Destroy(_pingObjs[i]);
            }
            _pingObjs.Clear();
            _pingDist.Clear();
            _pingFired.Clear();

            if (_ringGo != null) { UnityEngine.Object.Destroy(_ringGo); _ringGo = null; }

            _vfxRoutine = null;
        }

        private IEnumerator PulseVFX(Vector2 center, float maxR)
        {
            // 고정 속도 → 반경에 비례한 지속시간. 하한은 maxR이 비정상적으로 작을 때의 방어.
            float duration = Mathf.Max(0.3f, maxR / Mathf.Max(1f, pulseSpeed));

            // 파동마다 순번 리셋 — 안 하면 두 번째 발동부터 항상 최고음에서 시작한다.
            DetectionPingSfx.BeginSequence();

            // 확장 링
            _ringGo = new GameObject("RelicDetectionRing");
            var lr = _ringGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            const int seg = 64;
            lr.positionCount = seg;
            lr.widthMultiplier = ringWidth;
            lr.numCapVertices = 4;
            lr.sharedMaterial = LineMaterial();
            lr.sortingOrder = 115;

            // 핑 상태: 각 발견 청크의 플레이어 거리 + 발사 여부
            _pingObjs.Clear();
            _pingDist.Clear();
            _pingFired.Clear();
            foreach (var p in _pings)
            {
                _pingDist.Add(Vector2.Distance(center, p));
                _pingFired.Add(false);
                _pingObjs.Add(null);
            }

            float t = 0f;
            while (t < duration)
            {
                float k = t / duration;
                float r = Mathf.Lerp(0.2f, maxR, k);
                Color c = ringColor; c.a = ringColor.a * (1f - k);
                lr.startColor = c; lr.endColor = c;
                for (int i = 0; i < seg; i++)
                {
                    float a = (i / (float)seg) * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * r, center.y + Mathf.Sin(a) * r, 0f));
                }

                // 링 반경이 핑 거리를 지나면 핑 팝
                for (int i = 0; i < _pings.Count; i++)
                {
                    if (_pingFired[i] || r < _pingDist[i]) continue;
                    _pingFired[i] = true;

                    // 링이 이 청크를 훑은 순간 = 순차 알림 트리거.
                    // 소리는 relic이 직접 낸다(미니맵이 꺼져 있어도 들려야 하므로).
                    DetectionPingSfx.PlayNext();
                    DetectedChunkStore.Instance.RaisePinged(_pingCoords[i]);

                    var go = new GameObject("RelicDetectionPing");
                    go.transform.position = _pings[i];
                    var plr = go.AddComponent<LineRenderer>();
                    plr.useWorldSpace = true; plr.loop = true;
                    plr.positionCount = 20; plr.widthMultiplier = 0.12f; plr.numCapVertices = 4;
                    plr.sharedMaterial = LineMaterial(); plr.sortingOrder = 116;
                    plr.startColor = plr.endColor = pingColor;
                    for (int s = 0; s < 20; s++)
                    {
                        float aa = (s / 20f) * Mathf.PI * 2f;
                        plr.SetPosition(s, _pings[i] + new Vector2(Mathf.Cos(aa), Mathf.Sin(aa)) * 0.6f);
                    }
                    _pingObjs[i] = go;
                }

                t += Time.deltaTime;
                yield return null;
            }

            // 핑 짧게 페이드 후 정리
            float ft = 0f;
            while (ft < 0.35f)
            {
                float a = 1f - ft / 0.35f;
                for (int i = 0; i < _pingObjs.Count; i++)
                {
                    if (_pingObjs[i] == null) continue;
                    var plr = _pingObjs[i].GetComponent<LineRenderer>();
                    Color pc = pingColor; pc.a = a;
                    plr.startColor = plr.endColor = pc;
                }
                ft += Time.deltaTime;
                yield return null;
            }

            CleanupVFX();
        }
    }
}
