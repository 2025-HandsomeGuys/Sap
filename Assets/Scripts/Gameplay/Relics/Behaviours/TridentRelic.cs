using System;
using System.Collections;
using UnityEngine;

namespace Relic
{
    // 삼지창(패시브): 삽 지형 파기를 창날 3줄기 자동 3연타로 대체.
    // Digger.DigAt의 TryOverrideTerrainDig 훅을 잡아 좌(+20°)→중(0°)→우(−20°) 순서로
    // 0.12초 간격 캡슐 스탬프(ctx.CarveTerrain = ExplodeTerrain 경로, IndestructibleMask 자동 존중).
    // radius(=effectiveRadius)에 차징비율·MiningRange·타 유물 배율이 이미 반영돼 있어
    // 폭·길이를 그대로 곱하면 풀차징일수록 길고 굵게 + 도박꾼 안경 등과 자동 합성된다.
    [Serializable]
    public class TridentRelic : RelicBehaviour
    {
        [SerializeField] private float   spreadAngleDeg      = 20f;
        [SerializeField] private float   stabInterval        = 0.12f;
        [SerializeField] private float[] prongRadiusPerLevel = { 0.30f, 0.36f, 0.42f };
        [SerializeField] private float[] prongLengthPerLevel = { 2.0f, 2.4f, 2.8f };
        [SerializeField] private float   stampStepRatio      = 0.7f;   // 스탬프 간격 = 폭 × 이 값
        [SerializeField] private float   originOffset        = 0.3f;   // 몸 근처 시작 오프셋

        private Coroutine _stabRoutine;

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override bool TryOverrideTerrainDig(Vector2 digPos, Vector2 dir, float radius, int toolIndex)
        {
            if (toolIndex != 1) return false;                 // 삽 전용
            if (radius <= 0f) return false;
            if (ctx == null || ctx.runner == null || ctx.player == null) return false;

            // 연속 파기로 이전 3연타가 아직 돌면 끊고 새로 시작(스윙 쿨 0.5s > 3연타 0.24s라 보통 안 겹침).
            if (_stabRoutine != null) ctx.runner.StopCoroutine(_stabRoutine);
            _stabRoutine = ctx.runner.StartCoroutine(StabSequence(dir.normalized, radius));
            return true;
        }

        private IEnumerator StabSequence(Vector2 aimDir, float scale)
        {
            float w   = Lv(prongRadiusPerLevel) * scale;
            float len = Lv(prongLengthPerLevel) * scale;

            // 좌(+spread, 반시계=위) → 중 → 우. 조준 방향은 스윙 시점에 고정, 원점은 타격 시점 플레이어 위치.
            float[] angles = { spreadAngleDeg, 0f, -spreadAngleDeg };

            for (int i = 0; i < angles.Length; i++)
            {
                if (i > 0) yield return new WaitForSeconds(stabInterval);
                if (ctx == null || ctx.player == null) yield break;

                Vector2 dir    = Rotate(aimDir, angles[i]);
                Vector2 origin = (Vector2)ctx.player.position + dir * originOffset;
                Vector2 tip    = origin + dir * len;
                CarveCapsule(origin, tip, w);

                // 타격 연출: 찌르기마다 Mine 애니메이션 재트리거 + 쉐이크 + 줄기 끝 파티클.
                var anim = ctx.mining != null ? ctx.mining.playerAnimator : null;
                if (anim != null)
                {
                    anim.ResetTrigger("Mine");
                    anim.SetTrigger("Mine");
                }
                CameraShakeManager.Instance?.ShakeOnDig(1);
                TerrainParticleManager.Instance?.SpawnImpact(tip, Color.white, 1);
            }
            _stabRoutine = null;
        }

        // a→b 선분을 따라 원을 겹쳐 스탬프 = 가늘고 긴 창날 슬롯 (PlasmaCutter CarveCapsule 방식).
        private void CarveCapsule(Vector2 a, Vector2 b, float radius)
        {
            int n = StampCount(Vector2.Distance(a, b), radius, stampStepRatio);
            for (int i = 0; i <= n; i++)
            {
                Vector2 p = n == 0 ? a : Vector2.Lerp(a, b, i / (float)n);
                ctx.CarveTerrain(p, radius);
            }
        }

        public override void OnUnequip()
        {
            if (_stabRoutine != null && ctx != null && ctx.runner != null)
                ctx.runner.StopCoroutine(_stabRoutine);
            _stabRoutine = null;
        }

        // ── 순수 기하 헬퍼 (EditMode 테스트 대상) ──

        // 2D 벡터를 deg도 반시계 회전.
        public static Vector2 Rotate(Vector2 v, float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // 길이 dist를 간격 radius×stepRatio(최소 0.15)로 나눈 분할 수. 스탬프는 n+1개(양 끝 포함).
        public static int StampCount(float dist, float radius, float stepRatio)
        {
            if (dist <= 0f) return 0;
            float step = Mathf.Max(0.15f, radius * stepRatio);
            return Mathf.CeilToInt(dist / step);
        }
    }
}
