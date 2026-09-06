# 유물 추가 가이드 (새 대화용 단일 진입점)

이 문서 하나만 읽으면 유물 1종을 프레임워크에 붙일 수 있다. 새 대화에서 이 파일을 먼저 읽게 하라.

관련 문서: `design.md`(전체 설계), `implementation-plan.md`(프레임워크 구현). 기획 원본: `docs/유물/아이디어명세서_ - 유물 기획.csv`.

---

## 0. 프레임워크 요약 (이미 완성됨)

- 유물 = `RelicBehaviour`(namespace `Relic`) 파생 클래스 1개. `[SerializeReference]`로 `RelicSO`에 직렬화, 런타임 `Clone()`.
- 중앙 `RelicManager`(플레이어 부착)가 장착·훅중계·액티브 상태기계·스폰물 수명 총괄.
- 코드 위치: `Assets/Scripts/Gameplay/Relics/{Data,Core,Behaviours,UI,Debug}/`.

**`RelicBehaviour` 오버라이드 가능 훅** (필요한 것만):
```
OnEquip(ctx, lv) / OnUnequip() / OnLevelChanged(lv)   // 생명주기
OnUpdate()                                            // 매 프레임 (패시브 상시효과)
ModifyDigParameters(ref DigParameters p)              // 파기 범위/대상 후처리 (choke: GetCurrentDigParameters)
OnDigSwing()                                          // 파기 스윙 1회 (삽·곡괭이 등, 도구무관 통합 훅)
OnLanded()                                            // 착지 순간
TryConsumeAirJump() -> bool                           // 공중 추가 점프 허용
GetDuration() / GetCooldown()                         // 액티브 지속/쿨 (레벨별)
OnActivate() / OnActiveUpdate(t) / OnActiveEnd()      // 액티브 생명주기
Clone()                                               // 참조형 상태 있을 때만
```

**`RelicContext ctx`로 접근 가능한 것**: `ctx.player`(Transform), `ctx.stat`(PlayerStat), `ctx.mining`(PlayerMining), `ctx.tools`(ToolController), `ctx.statProvider`(스탯 modifier), `ctx.runner`(코루틴·스폰 대행 = RelicManager).

**재사용 가능한 기존 API** (검증됨):
- 스탯: `ctx.statProvider.Set(key, StatType, ModifierType, value)` / `Clear(key)` — 패시브 스탯형
- 무적: `ctx.stat.StartInvincibility(float duration)`
- 배터리 회복: `ctx.mining.RefillBattery(float amount)`
- 액티브 상태기계: `GetDuration()>0`이면 Active(지속)→Cooldown, `==0`이면 즉발→Cooldown
- 스폰물: `ctx.runner.Spawn(RelicID, prefab, pos)` / `ctx.runner.Despawn(RelicID)` (해제 시 자동 정리)

---

## 1. 유물 1종 추가 — 4단계

**Step 1. RelicID** — 이미 `Data/RelicID.cs`에 예약돼 있음(아래 표). 예약값 그대로 사용. 없으면 4013+ 추가.

**Step 2. behaviour 클래스** — `Assets/Scripts/Gameplay/Relics/Behaviours/<이름>Relic.cs` 신규 생성.
필요한 훅만 override. 레벨별 파라미터는 `[SerializeField] float[] xxxPerLevel`. 예시는 §3.

**Step 3. 생성기 등록** — `Assets/Scripts/Editor/RelicSliceAssetGenerator.cs`의 `Generate()`에
`CreateRelic(RelicID.X, "이름", RelicType.Passive/Active, new XRelic())` 한 줄 + `db.allRelics` 리스트에 추가.
→ 사용자가 Unity에서 `Tools > Relic > Generate Slice Assets` 재실행하면 에셋+DB 갱신.

**Step 4. 디버그 테스트** — `Assets/Scripts/Gameplay/Relics/Debug/RelicDebugGranter.cs`에
빈 F키로 grant+equip 추가(아래 "사용 중 키" 피할 것). 사용자가 인게임에서 검증.

> **변수는 하나**: 그 유물이 기존에 없는 게임 이벤트/동작을 필요로 하면(§2의 "필요 훅"), 게임 코드에
> 훅을 먼저 심어야 한다. 아래 표에 유물별로 명시.

---

## 2. 남은 유물 — RelicID·타입·필요 훅·난이도

| RelicID | 유물 | 타입 | 재사용/필요 훅 | 난이도 |
|---------|------|------|----------------|--------|
| `SpiderGlove`(4005) | 거미줄 장갑 | 패시브 | **기존 `StatRelicBehaviour` 재사용** (statType=`WallClimbSpeed`). 코드 0줄, 데이터만 | ⭐ |
| `Generator`(4006) | 발전기 | 액티브 | `ctx.mining.RefillBattery()` 기존 API | ⭐ |
| `GamblerGlasses`(4007) | 도박꾼의 안경 | 패시브 | `ModifyDigParameters` 훅(존재) — RadiusMultiplier 무작위. 데미지 무작위는 별도 훅 필요할 수 있음 | ⭐⭐ |
| `Anvil`(4008) | 모루 | 패시브 | `OnLanded`(존재) + **지형 파괴 API 신규 필요**(§2-A) | ⭐⭐ |
| `Steroid`(4009) | 스테로이드 | 액티브 | **지형 파괴 API 신규 필요**(§2-A) — 삽공격 시 원형 파기 | ⭐⭐ |
| `PlasmaCutter`(4010) | 플라즈마 커터 | 액티브 | 레이저 레이캐스트 + **지형 파괴 API**(§2-A) + 빔 비주얼 | ⭐⭐⭐ |
| `DrillDrone`(4011) | 굴착 드론 | 패시브 | `ctx.runner.Spawn`(존재) + 드론 프리팹/자율 파기 로직 | ⭐⭐⭐ |
| `JunkSpring`(4012) | 고물 스프링 | 패시브 | **점프키 홀드-차징 입력 훅 신규 필요** + 점프력 오버라이드 | ⭐⭐⭐ |

### 2-A. 공유 선행 작업 — "지형 파괴 API"
모루·스테로이드·플라즈마는 모두 **임의 좌표/반경의 지형을 파는 기능**이 필요하다. 이건 아직 유물이 못 쓴다.
**한 대화에서 먼저** `RelicContext`(또는 RelicManager)에 지형 파괴 헬퍼를 추가하고(예:
`ctx.CarveTerrain(Vector2 worldPos, float radius)`), 내부에서 기존 파기 경로(`Digger`/
`InfinityMapManager.ModifyTerrain`, `PlayerMining.GetCurrentDigParameters` 소비처)를 호출하게 만들어라.
그 뒤 세 유물은 이 헬퍼만 부르면 된다. → **이 선행 작업을 먼저 처리**하고 세 유물을 이어서.

---

## 3. behaviour 예시 (복붙 출발점)

**패시브 스탯 (거미줄 장갑) — 신규 클래스도 불필요, 데이터만:**
생성기에서 `new StatRelicBehaviour()`를 쓰되 인스펙터/생성기에서 statType=`WallClimbSpeed` 지정하면 끝.
(StatRelicBehaviour는 keyId·statType·valuePerLevel 필드 보유. 생성기에서 만들려면 StatRelicBehaviour에
public 세터를 추가하거나, 에셋 생성 후 인스펙터에서 값만 조정.)

**액티브 (발전기) — 기존 API 재사용:**
```csharp
using System;
using UnityEngine;
namespace Relic
{
    [Serializable]
    public class GeneratorRelic : RelicBehaviour
    {
        [SerializeField] private float[] amountPerLevel   = { 2f, 3f, 4f };
        [SerializeField] private float[] cooldownPerLevel = { 20f, 16f, 12f };
        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];
        public override float GetCooldown() => Lv(cooldownPerLevel);   // 즉발(Duration=0)
        public override void OnActivate() => ctx?.mining?.RefillBattery(Lv(amountPerLevel));
    }
}
```

**패시브 dig 후처리 (도박꾼 안경) — 기존 훅:**
```csharp
public override void ModifyDigParameters(ref DigParameters p)
{
    // 주의: Random 직접 호출은 워크플로/테스트에서 지양. 프레임당 시드나 프레임카운트 기반 권장.
    float roll = Mathf.Lerp(0.1f, 2.0f, Mathf.PingPong(Time.time, 1f)); // 예시(교체)
    p.RadiusMultiplier *= roll;
}
```

---

## 4. 사용 중인 디버그 키 (피할 것)
- **게임**: F4(드릴 무한배터리 치트)
- **ColliderDebugger**: F9, F10
- **RelicDebugGranter**: F6(지급/장착), F7(Magnet Lv+), F8(스탯유물), F11(무적 장착), F12(무적 Lv+)
- → 새 유물 테스트는 **F1~F3, F5** 등 빈 키 사용.

---

## 5. 새 대화 시작 프롬프트 (복붙용)

> `Assets/Docs/relic-system/adding-a-relic.md`를 먼저 읽어줘. 그 가이드대로 **<유물이름>**(RelicID `<예약값>`) 유물을
> 구현해줘. behaviour 클래스 신규 작성 + 생성기 등록 + 디버그 키(빈 F키) 추가까지. 게임 코드 훅이 필요하면
> 최소 침습으로 심고, 어떤 파일을 수정했는지 알려줘. Unity 컴파일/테스트/에셋 셋업은 내가 직접 한다.

**병렬 작업 주의:** behaviour 클래스 파일은 유물마다 독립이라 충돌 없음. 하지만 **공유 파일 3개**
(`RelicID.cs`, `RelicSliceAssetGenerator.cs`, `RelicDebugGranter.cs`)는 모든 유물이 건드린다.
- RelicID는 위 표로 **미리 예약**돼 있어 충돌 없음.
- 생성기·디버그granter는 **한 유물씩 끝내고 UVCS 체크인 후 다음**을 권장(동시 편집 시 수동 병합 필요).
- §2-A 지형 파괴 API는 여러 유물의 공통 선행이므로 **가장 먼저 한 대화에서** 처리.
