using System;
using System.Collections;
using UnityEngine;

namespace Relic
{
    // 스테로이드(액티브·즉발): 발동 즉시 플레이어 주위를 원형으로 파괴한다.
    // 확산 링 이펙트로 피드백. 레벨업으로 파괴 반경 증가.
    [Serializable]
    public class SteroidRelic : RelicBehaviour
    {
        [SerializeField] private float[] radiusPerLevel   = { 2.5f, 3.2f, 4.0f };
        [SerializeField] private float[] cooldownPerLevel = { 12f, 10f, 8f };

        [SerializeField] private Color ringColor    = new Color(0.6f, 1f, 0.3f, 0.9f);
        [SerializeField] private float ringDuration = 0.3f;
        [SerializeField] private float ringWidth    = 0.15f;

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override float GetDuration() => 0f;              // 즉발 → 바로 쿨타임
        public override float GetCooldown() => Lv(cooldownPerLevel);

        public override void OnActivate()
        {
            if (ctx?.player == null) return;

            float r = Lv(radiusPerLevel);
            Vector2 center = ctx.player.position;

            // 즉시 원형 파괴 (ExplodeTerrain 경로라 IndestructibleMask 자동 존중)
            ctx.CarveTerrain(center, r);

            // 확산 링 피드백
            if (ctx.runner != null)
                ctx.runner.StartCoroutine(ShockwaveRing(center, r));
        }

        private IEnumerator ShockwaveRing(Vector2 center, float maxR)
        {
            var go = new GameObject("RelicSteroidRing");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            const int seg = 48;
            lr.positionCount = seg;
            lr.widthMultiplier = ringWidth;
            lr.numCapVertices = 4;
            var mat = new Material(Shader.Find("Sprites/Default"));
            lr.material = mat;
            lr.sortingOrder = 115;

            float t = 0f;
            while (t < ringDuration)
            {
                float k = t / ringDuration;
                float r = Mathf.Lerp(0.2f, maxR, k);
                Color c = ringColor; c.a = ringColor.a * (1f - k);
                lr.startColor = c; lr.endColor = c;

                for (int i = 0; i < seg; i++)
                {
                    float a = (i / (float)seg) * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * r, center.y + Mathf.Sin(a) * r, 0f));
                }

                t += Time.deltaTime;
                yield return null;
            }

            UnityEngine.Object.Destroy(mat);
            UnityEngine.Object.Destroy(go);
        }
    }
}
