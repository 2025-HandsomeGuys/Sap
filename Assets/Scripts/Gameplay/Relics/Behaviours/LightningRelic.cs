using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Relic
{
    // 번개(액티브·즉발): 주변 광물들을 최근접 순으로 연쇄 연결해 그 경로 지형을 파괴한다.
    // 번개 비주얼(LineRenderer)이 경로를 따라 번쩍인 뒤 사라진다. 쿨타임 3분(CSV).
    [Serializable]
    public class LightningRelic : RelicBehaviour
    {
        [SerializeField] private float[] rangePerLevel = { 6f, 8f, 10f };   // 광물 탐지 반경
        [SerializeField] private float cooldown = 180f;                     // 3분
        // 연쇄 최대 광물 수. SerializeField로 두면 RelicSO 에셋의 옛 값(12)이 이겨서 안 줄어들므로 const.
        private const int maxTargets = 5;

        // ── 모양 파라미터: const로 고정 ──
        // RelicSO 에셋이 이 behaviour를 [SerializeReference]로 직렬화하므로, SerializeField로 두면
        // 에셋에 저장된 옛 값이 코드값을 덮어쓴다. 코드가 유일 기준이 되도록 const로 못박는다.

        // 파기 반경을 볼트 폭에서 파생 → 파이는 자국이 보이는 번개선 굵기와 일치(PlasmaCutterRelic과 동일 방식).
        // 0.5면 자국 폭 = 볼트 폭 정확히 일치. 0.55면 살짝 여유.
        private const float CarveToBoltRatio = 0.55f;
        private float CarveRadius  => boltWidth * CarveToBoltRatio;
        // 간격은 반경보다 작아야 채널이 끊기지 않는다(플라즈마의 캡슐 스탬프 간격과 동일 비율).
        private float CarveSpacing => Mathf.Max(0.05f, CarveRadius * 0.7f);

        // 번개 지그재그(프랙탈 중점변위) 설정 — 값이 클수록 더 날카롭게 꺾인다.
        private const float jaggedAmplitude = 1.4f;    // 첫 단계 최대 수직 변위(units) 상한
        private const float jaggedAmpPerLength = 0.18f;// 구간 길이 대비 변위 비율(짧은 구간에서 과하게 튀지 않게)
        private const int   jaggedSubdivisions = 5;    // 세분화 단계(구간당 2^n 분할)
        private const float jaggedRoughness = 0.55f;   // 단계별 진폭 감쇠(0.5=반씩 줄어듦, 클수록 잔주름 많음)
        [SerializeField] private float sweepSpeed = 60f;                    // 번개 전파 속도(units/sec). 순차 파괴·비주얼 리딩 엣지 공용
        [SerializeField] private Color boltColor = new Color(0.6f, 0.85f, 1f, 0.95f);
        // 파기 반경이 여기서 파생되므로 에셋 옛 값에 흔들리지 않게 const로 고정.
        private const float boltWidth = 0.3f;
        [SerializeField] private float boltDuration = 0.4f;

        private LayerMask _mineralMask;
        private readonly Collider2D[] _buffer = new Collider2D[64];
        // OverlapCircleNonAlloc 대체. useTriggers는 구 API와 동일하게 전역 설정을 따라간다
        // (ContactFilter2D 기본값은 false라 그냥 두면 트리거 콜라이더 광물이 안 잡힌다).
        private ContactFilter2D _filter;

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override float GetDuration() => 0f;      // 즉발
        public override float GetCooldown() => cooldown;
        public override bool HasOwnActivationSfx => true; // 천둥소리(SfxKeys.RelicLightning)

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            _mineralMask = LayerMask.GetMask("Mineral");
            if (_mineralMask == 0) _mineralMask = ~0;

            _filter = new ContactFilter2D { useTriggers = Physics2D.queriesHitTriggers };
            _filter.SetLayerMask(_mineralMask);
        }

        public override void OnActivate()
        {
            if (ctx?.player == null) return;
            Vector2 origin = ctx.player.position;
            float range = Lv(rangePerLevel);

            // 1) 주변 광물 위치 수집
            int count = Physics2D.OverlapCircle(origin, range, _filter, _buffer);
            var minerals = new List<Vector2>();
            for (int i = 0; i < count && minerals.Count < maxTargets; i++)
            {
                if (_buffer[i] == null) continue;
                minerals.Add(_buffer[i].transform.position);
            }
            if (minerals.Count == 0) return;

            // 타겟이 확정된 뒤에만 천둥이 친다(광물이 없으면 아무 일도 안 일어나므로)
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SfxKeys.RelicLightning);

            // 2) 최근접 이웃으로 체인 경로 구성 (플레이어 → 가장 가까운 광물 → ...)
            var path = new List<Vector2> { origin };
            Vector2 cur = origin;
            while (minerals.Count > 0)
            {
                int best = 0;
                float bestSqr = float.MaxValue;
                for (int i = 0; i < minerals.Count; i++)
                {
                    float d = ((Vector2)minerals[i] - cur).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; best = i; }
                }
                cur = minerals[best];
                path.Add(cur);
                minerals.RemoveAt(best);
            }

            // 3) 코너 경로를 번개처럼 지그재그화(프랙탈 중점변위) — 이후 파괴·비주얼 모두 이 경로 사용
            var bolt = BuildJaggedPath(path);

            // 4) 번개가 경로를 훑으며 지나간 자리를 순차 파괴 + 비주얼 동기 진행
            if (ctx.runner != null)
                ctx.runner.StartCoroutine(StrikeSequential(bolt));
            else
            {
                // 코루틴 대행자가 없으면 폴백: 경로 전체 즉시 파괴
                for (int i = 0; i < bolt.Count - 1; i++)
                    CarveSegment(bolt[i], bolt[i + 1]);
            }
        }

        // 코너 폴리라인의 각 구간을 재귀 중점변위로 지그재그화해 번개 형태의 폴리라인을 만든다.
        private List<Vector2> BuildJaggedPath(List<Vector2> corners)
        {
            var result = new List<Vector2>();
            for (int c = 0; c < corners.Count - 1; c++)
            {
                var seg = new List<Vector2> { corners[c], corners[c + 1] };
                // 구간이 짧으면 진폭도 줄여 지그재그가 제자리를 맴돌지 않게 한다.
                float segLen = Vector2.Distance(corners[c], corners[c + 1]);
                Subdivide(seg, jaggedSubdivisions, Mathf.Min(jaggedAmplitude, segLen * jaggedAmpPerLength));
                // 첫 구간만 시작점 포함(이후 구간은 앞 구간 끝점과 겹치므로 1부터)
                for (int i = (c == 0 ? 0 : 1); i < seg.Count; i++)
                    result.Add(seg[i]);
            }
            return result;
        }

        // 중점변위: 매 단계 각 구간 중점을 수직 방향으로 무작위 이동, 단계마다 진폭 감쇠.
        private void Subdivide(List<Vector2> pts, int levels, float amplitude)
        {
            float amp = amplitude;
            for (int lvl = 0; lvl < levels; lvl++)
            {
                var next = new List<Vector2>(pts.Count * 2);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector2 a = pts[i];
                    Vector2 b = pts[i + 1];
                    Vector2 dir = b - a;
                    Vector2 perp = new Vector2(-dir.y, dir.x).normalized;
                    Vector2 mid = (a + b) * 0.5f + perp * UnityEngine.Random.Range(-amp, amp);
                    next.Add(a);
                    next.Add(mid);
                }
                next.Add(pts[pts.Count - 1]);
                pts.Clear();
                pts.AddRange(next);
                amp *= jaggedRoughness; // 세부 단계일수록 잔주름으로
            }
        }

        private void CarveSegment(Vector2 a, Vector2 b)
        {
            float dist = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / CarveSpacing));
            for (int i = 0; i <= steps; i++)
                ctx.CarveTerrain(Vector2.Lerp(a, b, i / (float)steps), CarveRadius);
        }

        // 번개가 경로를 따라 전파: 리딩 엣지가 지나간 지점을 순차 파괴하고 LineRenderer를 그만큼 뻗는다.
        private IEnumerator StrikeSequential(List<Vector2> path)
        {
            // 코너별 누적 길이(경로 호장) 계산
            int corners = path.Count;
            var cumLen = new float[corners];
            for (int i = 1; i < corners; i++)
                cumLen[i] = cumLen[i - 1] + Vector2.Distance(path[i - 1], path[i]);
            float totalLen = cumLen[corners - 1];

            // CarveSpacing 간격의 파괴 지점을 누적 길이로 미리 나열
            float spacing = CarveSpacing;
            var carveAt = new List<float>();
            for (float d = 0f; d < totalLen; d += spacing) carveAt.Add(d);
            carveAt.Add(totalLen);
            int nextCarve = 0;

            // LineRenderer 셋업 (처음엔 시작점만 보이게)
            var go = new GameObject("RelicLightningBolt");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = boltWidth;
            lr.numCapVertices = 2;
            var mat = new Material(Shader.Find("Sprites/Default"));
            lr.material = mat;
            lr.sortingOrder = 125;
            lr.startColor = lr.endColor = boltColor;

            // 리딩 엣지 전진: 도달한 지점까지 순차 파괴 + 비주얼 갱신
            float lead = 0f;
            while (lead < totalLen)
            {
                lead += sweepSpeed * Time.deltaTime;
                float clamped = Mathf.Min(lead, totalLen);
                while (nextCarve < carveAt.Count && carveAt[nextCarve] <= clamped)
                    ctx.CarveTerrain(PointAtLength(path, cumLen, carveAt[nextCarve++]), CarveRadius);
                DrawBolt(lr, path, cumLen, clamped);
                yield return null;
            }
            // 스윕이 끝에 도달 — 남은 파괴 마무리 + 전체 경로 표시
            while (nextCarve < carveAt.Count)
                ctx.CarveTerrain(PointAtLength(path, cumLen, carveAt[nextCarve++]), CarveRadius);
            DrawBolt(lr, path, cumLen, totalLen);

            // 완성된 볼트 페이드아웃
            float t = 0f;
            while (t < boltDuration)
            {
                float k = t / boltDuration;
                Color c = boltColor; c.a = boltColor.a * (1f - k);
                lr.startColor = c; lr.endColor = c;
                t += Time.deltaTime;
                yield return null;
            }

            UnityEngine.Object.Destroy(mat);
            UnityEngine.Object.Destroy(go);
        }

        // 경로 코너들을 호장 L까지 그리고, 그 지점의 리딩 엣지 1점을 끝에 붙인다.
        private void DrawBolt(LineRenderer lr, List<Vector2> path, float[] cumLen, float L)
        {
            int n = 1; // path[0]은 항상 포함
            while (n < path.Count && cumLen[n] <= L) n++;
            if (n >= path.Count)
            {
                lr.positionCount = path.Count;
                for (int i = 0; i < path.Count; i++) lr.SetPosition(i, path[i]);
                return;
            }
            lr.positionCount = n + 1;
            for (int i = 0; i < n; i++) lr.SetPosition(i, path[i]);
            lr.SetPosition(n, PointAtLength(path, cumLen, L)); // 진행 중인 리딩 엣지
        }

        // 경로(코너 폴리라인) 위에서 호장 L에 해당하는 좌표.
        private static Vector2 PointAtLength(List<Vector2> path, float[] cumLen, float L)
        {
            if (L <= 0f) return path[0];
            float total = cumLen[cumLen.Length - 1];
            if (L >= total) return path[path.Count - 1];
            for (int i = 1; i < path.Count; i++)
            {
                if (cumLen[i] >= L)
                {
                    float segLen = cumLen[i] - cumLen[i - 1];
                    float t = segLen > 0f ? (L - cumLen[i - 1]) / segLen : 0f;
                    return Vector2.Lerp(path[i - 1], path[i], t);
                }
            }
            return path[path.Count - 1];
        }
    }
}
