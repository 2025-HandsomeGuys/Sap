using System;
using UnityEngine;

namespace Relic
{
    // 고물 스프링(패시브): 점프키를 꾹 누르면 지면에서 충전(캐릭터가 압축)되고, 떼는 순간
    // 충전량에 비례한 슈퍼 점프로 솟구친다. 레벨업으로 최대 도약 속도(=최대 충전 정도)가 커진다.
    //
    // 충전 중에는 일반 지면 점프를 억제(AllowGroundJump=false)해 GetButtonDown 즉발 점프와 충돌하지 않는다.
    // 실제 발사는 PlayerController.SuperJump(속도) — 상승 리미터를 일시 상향해 슈퍼 점프가 깎이지 않게 한다.
    // 단, 전역 maxFallSpeed(±30) 상한이 있어 도약 속도는 그 이하로만 유효(기본값이 그 안에 들어감).
    [Serializable]
    public class JunkSpringRelic : RelicBehaviour
    {
        [SerializeField] private float   maxChargeTime      = 1.0f;  // 완충까지 홀드 시간(초)
        [SerializeField] private float   minJumpVelocity    = 12f;   // 탭(무충전) 도약 속도 ≈ 일반 점프
        [SerializeField] private float[] maxJumpVelPerLevel = { 22f, 26f, 29f }; // 완충 도약 속도(레벨별, <30)
        [SerializeField] private bool    enableSquish       = true;  // 압축 비주얼
        [SerializeField] private float   fullSquishY        = 0.6f;  // 완충 시 Y 스케일 배율

        private float     _charge;
        private Transform _visual;                     // 압축 대상(스프라이트 자식)
        private Vector3   _visualBaseScale = Vector3.one;

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            ResolveVisual();
        }

        public override bool AllowGroundJump() => false; // 일반 지면 점프 억제 → 차징 점프가 대신 처리

        public override void OnUpdate()
        {
            var pc = ctx?.controller;
            if (pc == null) return;

            // 플레이어 입력과 동일하게 UI 열림 중엔 무시
            bool uiBlocked = UIStateManager.Instance != null && UIStateManager.Instance.CurrentState != UIState.None;
            bool grounded  = pc.IsGrounded;

            // 아주 빠른 탭도 도약되도록 누른 프레임에 최소 충전 시드
            if (!uiBlocked && grounded && Input.GetButtonDown("Jump"))
                _charge = Mathf.Max(_charge, 0.001f);

            if (!uiBlocked && grounded && Input.GetButton("Jump"))
            {
                _charge = Mathf.Min(_charge + Time.deltaTime, maxChargeTime);
                ApplySquish(_charge / maxChargeTime);
            }
            else if (!grounded && _charge > 0f)
            {
                _charge = 0f; ResetSquish(); // 충전 중 지면 이탈 시 취소
            }

            if (Input.GetButtonUp("Jump"))
            {
                if (grounded && _charge > 0f && !uiBlocked)
                {
                    float t = _charge / maxChargeTime;
                    float vel = Mathf.Lerp(minJumpVelocity, Lv(maxJumpVelPerLevel), t);
                    pc.SuperJump(vel);
                }
                _charge = 0f;
                ResetSquish();
            }
        }

        public override void OnUnequip() => ResetSquish();

        private void ApplySquish(float t)
        {
            if (!enableSquish || _visual == null) return;
            float y = Mathf.Lerp(1f, fullSquishY, t);
            _visual.localScale = new Vector3(_visualBaseScale.x, _visualBaseScale.y * y, _visualBaseScale.z);
        }

        private void ResetSquish()
        {
            if (_visual != null) _visual.localScale = _visualBaseScale;
        }

        private void ResolveVisual()
        {
            if (ctx?.player == null) return;
            // 루트를 스케일하면 콜라이더까지 찌그러지므로 스프라이트 자식만 압축.
            var root = ctx.player.root;
            var sr = root != null ? root.GetComponentInChildren<SpriteRenderer>() : null;
            if (sr != null && sr.transform != root)
            {
                _visual = sr.transform;
                _visualBaseScale = _visual.localScale;
            }
            else _visual = null;
        }
    }
}
