using System;
using System.Collections;
using UnityEngine;

namespace Relic
{
    // 폭발 드릴(패시브): 드릴 대쉬가 끝나는 지점에서 폭발(지형 파괴). 자동 발동, 쿨타임 10초.
    // 레벨업으로 폭발 반경 증가. PlayerMining.DrillDashEnded 이벤트 구독(하이브리드 훅).
    [Serializable]
    public class DashBombRelic : RelicBehaviour
    {
        [SerializeField] private float[] radiusPerLevel = { 2f, 2.8f, 3.6f };
        [SerializeField] private float cooldown = 10f;
        [SerializeField] private Color ringColor = new Color(1f, 0.5f, 0.1f, 0.9f);
        [SerializeField] private float ringDuration = 0.3f;
        [SerializeField] private float ringWidth = 0.18f;

        private float _nextBoom;
        private bool _subscribed;

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            if (!_subscribed && ctx?.mining != null)
            {
                ctx.mining.DrillDashEnded += OnDashEnd;
                _subscribed = true;
            }
        }

        public override void OnUnequip()
        {
            if (_subscribed && ctx?.mining != null)
                ctx.mining.DrillDashEnded -= OnDashEnd;
            _subscribed = false;
        }

        private void OnDashEnd(Vector2 pos)
        {
            if (Time.time < _nextBoom) return;
            _nextBoom = Time.time + cooldown;

            float r = Lv(radiusPerLevel);
            ctx.CarveTerrain(pos, r);
            if (ctx.runner != null) ctx.runner.StartCoroutine(BoomRing(pos, r));
        }

        private IEnumerator BoomRing(Vector2 center, float maxR)
        {
            var go = new GameObject("RelicDashBoomRing");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            const int seg = 40;
            lr.positionCount = seg;
            lr.widthMultiplier = ringWidth;
            lr.numCapVertices = 4;
            var mat = new Material(Shader.Find("Sprites/Default"));
            lr.material = mat;
            lr.sortingOrder = 116;

            float t = 0f;
            while (t < ringDuration)
            {
                float k = t / ringDuration;
                float r = Mathf.Lerp(0.2f, maxR, k);
                Color col = ringColor; col.a = ringColor.a * (1f - k);
                lr.startColor = col; lr.endColor = col;
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
