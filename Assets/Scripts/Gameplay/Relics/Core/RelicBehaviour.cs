using System;

namespace Relic
{
    [Serializable]
    public abstract class RelicBehaviour
    {
        protected RelicContext ctx;
        protected int level = 1;              // 현재 레벨(1..max)

        // ── 생명주기 ──
        public virtual void OnEquip(RelicContext c, int lv) { ctx = c; level = lv; }
        public virtual void OnUnequip() { }
        public virtual void OnLevelChanged(int lv) { level = lv; }

        // ── 코어 훅 (가상 no-op, 필요한 유물만 override) ──
        public virtual void OnUpdate() { }
        public virtual void ModifyDigParameters(ref DigParameters p) { }
        // 파기 스윙(삽·곡괭이 등 도구가 채굴 동작을 실행하는 순간) 1회 통지.
        // 도구별 이벤트를 개별 구독하지 말고 이 훅을 override할 것(허브: PlayerMining.DigSwing → RelicManager relay).
        public virtual void OnDigSwing() { }
        // 지형 파기 실행 오버라이드(삼지창류). true 반환 시 Digger의 기본 원형 파기·연출을 유물이 대체한다.
        // digPos=기본 파기 중심, dir=플레이어→파기점 방향(정규화), radius=effectiveRadius(차징·스탯·타 유물 배율 반영), toolIndex=도구.
        // 스태미나 소모·불괴 체크는 Digger가 훅 호출 전에 이미 끝냈다.
        public virtual bool TryOverrideTerrainDig(UnityEngine.Vector2 digPos, UnityEngine.Vector2 dir, float radius, int toolIndex) => false;
        public virtual void OnLanded() { }
        public virtual bool TryConsumeAirJump() => false;   // 이단점프류
        public virtual bool AllowGroundJump() => true;      // false면 일반 지면 점프 억제(차징 점프류)
        // 다른 슬롯 유물의 쿨타임 배율(모래시계류 패시브). 1=영향 없음.
        // RelicManager가 발동 시점에 발동 슬롯 제외 전 슬롯의 곱을 GetCooldown()에 반영한다.
        public virtual float GetCooldownScale() => 1f;

        // ── 액티브 생명주기 (액티브 유물만 override) ──
        // 발동 전 게이트. false면 상태기계를 소모하지 않고 발동을 막는다(조건부 액티브용).
        public virtual bool CanActivate() => true;
        public virtual float GetCooldown() => 0f;
        public virtual float GetDuration() => 0f;
        public virtual void OnActivate() { }
        public virtual void OnActiveUpdate(float elapsed) { }
        public virtual void OnActiveEnd() { }

        // true면 지속 중 재입력 시 조기 종료(토글). 예: 반중력 장치.
        public virtual bool IsToggle => false;

        // true면 RelicManager가 공통 발동음(SfxKeys.RelicActivate)을 내지 않는다.
        // 전용 효과음이 있는 유물(번개=천둥, 제트팩=분사 루프, 탐지=핑)이 override한다.
        // 주의: 귀환석(OneWayPortalRelic)은 override하지 않는다 — 설치는 공통음,
        //       귀환(OnActiveEnd)만 전용음이라 토글 분기에서 자연히 갈린다.
        public virtual bool HasOwnActivationSfx => false;

        // ── 복제: 정의(SO)/런타임 상태 분리. 기본 얕은 복사(값형 상태에 충분). ──
        public virtual RelicBehaviour Clone() => (RelicBehaviour)MemberwiseClone();

        // 테스트 접근용 (런타임 상태 격리 검증)
        public int Level => level;
    }
}
