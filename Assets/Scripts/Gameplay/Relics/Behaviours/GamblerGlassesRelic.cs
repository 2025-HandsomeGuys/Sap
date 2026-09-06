using System;
using UnityEngine;

namespace Relic
{
    // 도박꾼의 안경(패시브): 파기 범위 배율이 도박으로 정해진다.
    //  · 배율은 범위 추첨이 아니라 **동전 던지기** — lowRoll(0.5배) 또는 highRoll(2배) 둘 중 하나만 나온다.
    //  · 배율 확정 시점 = "파기 스윙"(RelicBehaviour.OnDigSwing 훅). 삽·곡괭이 등 모든 채굴 스윙마다
    //    이번 파기에 쓸 배율이 뽑혀 고정된다. 매 프레임 재추첨하던 방식을 폐기 —
    //    한 번의 파기 동안 위치 계산·실제 파기가 같은 배율을 공유한다.
    //    (개별 이벤트 구독 대신 통합 훅 사용: PlayerMining.DigSwing → RelicManager → OnDigSwing.)
    //  · 잭팟(jackpotChance 확률)이 뜨면 흑섬 상태 진입: blackFlashDuration(10초) 동안 모든 파기가
    //    즉시 highRoll로 강제 고정된다. 흑섬 중 차징 시작은 재추첨하지 않고 highRoll을 유지한다.
    //  · 드릴·삽은 데미지=effectiveRadius라 반경과 함께 데미지도 도박화(곡괭이 데미지는 스탯 고정 — 후속).
    //  · 레벨업으로 highRoll이 뜰 확률(highChance)·잭팟 확률이 오른다. 배율 값 자체는 0.5/2.0 고정.
    [Serializable]
    public class GamblerGlassesRelic : RelicBehaviour
    {
        [SerializeField] private float[] lowRollPerLevel     = { 0.5f, 0.5f, 0.5f };   // 꽝 배율(고정)
        [SerializeField] private float[] highRollPerLevel    = { 2.0f, 2.0f, 2.0f };   // 대박 배율(고정)
        [SerializeField] private float[] highChancePerLevel  = { 0.5f, 0.55f, 0.6f };  // highRoll이 뜰 확률(레벨↑ = 덜 망함)
        [SerializeField] private float[] jackpotChancePerLevel = { 0.02f, 0.03f, 0.05f }; // 스윙당 잭팟 확률
        [SerializeField] private float   blackFlashDuration  = 10f;                    // 흑섬 지속(초)

        private float _lockedRoll = 1f;   // 마지막 스윙에 확정된 배율
        private float _blackFlashUntil;   // 흑섬 종료 시각(Time.time). 0 = 비활성

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];
        private bool  BlackFlashActive => Time.time < _blackFlashUntil;

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);

            // 초기 배율(첫 스윙 전) — 밋밋한 1.0 대신 한 번 던져 둔다.
            _lockedRoll = FlipCoin();
        }

        // 동전 던지기: highChance 확률로 highRoll, 아니면 lowRoll. 중간값은 나오지 않는다.
        private float FlipCoin()
            => UnityEngine.Random.value < Lv(highChancePerLevel) ? Lv(highRollPerLevel) : Lv(lowRollPerLevel);

        // 모든 채굴 스윙(삽·곡괭이)마다 이번 파기 배율을 확정한다. (통합 훅 — 개별 구독 없음)
        public override void OnDigSwing()
        {
            // 흑섬 중엔 재추첨 없이 highRoll 유지.
            if (BlackFlashActive)
            {
                _lockedRoll = Lv(highRollPerLevel);
                return;
            }

            if (UnityEngine.Random.value < Lv(jackpotChancePerLevel))
            {
                // 잭팟 → 흑섬 10초: 즉시 highRoll 고정.
                _blackFlashUntil = Time.time + blackFlashDuration;
                _lockedRoll = Lv(highRollPerLevel);
                OnBlackFlashStart();
            }
            else
            {
                _lockedRoll = FlipCoin();
            }
        }

        public override void ModifyDigParameters(ref DigParameters p)
        {
            // 흑섬 중이면 도구·시점 무관 항상 highRoll.
            float roll = BlackFlashActive ? Lv(highRollPerLevel) : _lockedRoll;
            p.RadiusMultiplier *= roll;   // 범위 도박화
            p.DamageMultiplier *= roll;   // 데미지 도박화(곡괭이 돌 데미지 등 고정 데미지에 적용)
        }

        // 흑섬 진입 연출: 검은 섬광 오버레이(코드 생성, 씬 세팅 불필요) + 카메라 흔들림.
        private void OnBlackFlashStart()
        {
            BlackFlashOverlay.Play(blackFlashDuration);
            CameraShakeManager.Instance?.Shake(0.3f, 0.06f);
            Debug.Log($"[Gambler] 잭팟! 흑섬 {blackFlashDuration}초 — 최대 배율 고정");
        }
    }
}
