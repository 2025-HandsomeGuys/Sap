using System;
using UnityEngine;

namespace Relic
{
    // 반중력 장치(액티브): 발동 시 duration 동안 중력 반전(위로 떨어짐). 기존 AntiGravityHandler 재사용.
    // Lv1 5초 / Lv2·3 7초 (CSV). 지속 종료 시 원래 중력 복귀.
    [Serializable]
    public class GravityFlipRelic : RelicBehaviour
    {
        [SerializeField] private float[] durationPerLevel = { 5f, 7f, 7f };
        [SerializeField] private float[] cooldownPerLevel = { 20f, 18f, 15f };

        private AntiGravityHandler _antiGrav;

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override float GetDuration() => Lv(durationPerLevel);
        public override float GetCooldown() => Lv(cooldownPerLevel);

        // 지속 중 Q 재입력 시 즉시 중력 복귀(토글 off).
        public override bool IsToggle => true;

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            Resolve();
        }

        // 전용 지속음이 있으므로 공통 발동음(SfxKeys.RelicActivate)은 내지 않는다.
        public override bool HasOwnActivationSfx => true;

        // 반중력 지속 루프의 핸들. 클립 길이와 무관하게 효과가 켜진 동안만 정확히 울린다
        // (원샷으로 틀면 효과가 끝난 뒤에도 소리가 남거나 먼저 끊긴다).
        private const string GravityLoopHandle = "gravity";

        public override void OnActivate()
        {
            Resolve();
            _antiGrav?.SetPhase(GravityPhase.Inverted);

            if (SoundManager.Instance != null)
                SoundManager.Instance.Loop(GravityLoopHandle, SfxKeys.RelicGravity);
        }

        public override void OnActiveEnd()
        {
            _antiGrav?.ExitZone();
            StopGravityLoop();
        }

        public override void OnUnequip()
        {
            _antiGrav?.ExitZone();
            StopGravityLoop();
        }

        private static void StopGravityLoop()
        {
            if (SoundManager.Instance != null)
                SoundManager.Instance.StopLoop(GravityLoopHandle, 0.2f);
        }

        private void Resolve()
        {
            if (_antiGrav != null || ctx?.player == null) return;
            _antiGrav = ctx.player.GetComponentInParent<AntiGravityHandler>();
            if (_antiGrav == null) _antiGrav = UnityEngine.Object.FindFirstObjectByType<AntiGravityHandler>();
        }
    }
}
