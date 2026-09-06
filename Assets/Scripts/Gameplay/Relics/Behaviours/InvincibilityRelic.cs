using System;
using UnityEngine;

namespace Relic
{
    // 무적(액티브): 발동 시 일정시간 무적. 기존 PlayerStat 무적 로직 재사용.
    // Lv1 기본, Lv2 무적시간 증가, Lv3 쿨타임 감소 (CSV 기획).
    // 무적 지속 동안 플레이어 주변에 링 이펙트를 표시(프리팹/스프라이트 불필요, 코드 생성).
    [Serializable]
    public class InvincibilityRelic : RelicBehaviour
    {
        [SerializeField] private float[] durationPerLevel = { 3f, 5f, 5f };
        [SerializeField] private float[] cooldownPerLevel = { 60f, 60f, 45f };

        // 링 비주얼 파라미터
        private const int   RingSegments = 48;
        private const float RingRadius   = 0.9f;
        private const float RingWidth    = 0.08f;

        private GameObject   _ring;
        private LineRenderer _lr;
        private Material     _ringMat;

        private float Lv(float[] arr) => arr[Mathf.Clamp(level - 1, 0, arr.Length - 1)];

        // 액티브 상태기계: Active(duration) 동안 무적 게이지·링 → 이후 Cooldown.
        public override float GetDuration() => Lv(durationPerLevel);
        public override float GetCooldown() => Lv(cooldownPerLevel);
        public override bool HasOwnActivationSfx => true; // 포스필드음(SfxKeys.RelicInvincible)

        public override void OnActivate()
        {
            // PlayerStat가 자체 타이머로 무적을 유지 (지속시간은 GetDuration과 일치).
            ctx?.stat?.StartInvincibility(GetDuration());

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SfxKeys.RelicInvincible);

            EnsureRing();
            if (_ring != null) _ring.SetActive(true);
        }

        public override void OnActiveUpdate(float elapsed)
        {
            if (_ring == null) return;
            // 회전 + 폭 맥동으로 "켜져 있음"을 분명히.
            _ring.transform.localRotation = Quaternion.Euler(0f, 0f, elapsed * 90f);
            _lr.widthMultiplier = RingWidth * (1f + 0.35f * Mathf.Sin(elapsed * 8f));
        }

        public override void OnActiveEnd()
        {
            if (_ring != null) _ring.SetActive(false);
        }

        public override void OnUnequip()
        {
            if (_ring != null) UnityEngine.Object.Destroy(_ring);
            if (_ringMat != null) UnityEngine.Object.Destroy(_ringMat);
            _ring = null; _lr = null; _ringMat = null;
        }

        private void EnsureRing()
        {
            if (_ring != null || ctx?.player == null) return;

            _ring = new GameObject("RelicInvincibilityRing");
            _ring.transform.SetParent(ctx.player, false);
            _ring.transform.localPosition = Vector3.zero;

            _lr = _ring.AddComponent<LineRenderer>();
            _lr.useWorldSpace = false;
            _lr.loop = true;
            _lr.positionCount = RingSegments;
            _lr.widthMultiplier = RingWidth;
            _lr.numCapVertices = 4;
            _ringMat = new Material(Shader.Find("Sprites/Default"));
            _lr.material = _ringMat;

            var color = new Color(1f, 0.9f, 0.2f, 0.9f); // 밝은 노랑
            _lr.startColor = color;
            _lr.endColor = color;
            _lr.sortingOrder = 100;

            for (int i = 0; i < RingSegments; i++)
            {
                float a = (i / (float)RingSegments) * Mathf.PI * 2f;
                _lr.SetPosition(i, new Vector3(Mathf.Cos(a) * RingRadius, Mathf.Sin(a) * RingRadius, 0f));
            }

            _ring.SetActive(false);
        }
    }
}
