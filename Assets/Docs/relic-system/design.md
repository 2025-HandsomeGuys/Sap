# 유물(Relic) 시스템 설계

작성일: 2026-07-15
상태: 설계 확정, 구현 전

Unity 2D 지형 파기 게임의 유물 시스템. 플레이어가 땅속에서 유물을 발견해 수집하고,
2개 슬롯(확장 가능)에 장착해 능력을 얻는다. 액티브 유물은 키로 발동한다.

---

## 1. 확정된 설계 결정

| 항목 | 결정 |
|------|------|
| 소유·장착 모델 | **로드아웃형** — 여러 유물 수집, 동시 장착은 슬롯 제한 |
| 슬롯 수 | 기본 **2개**, 확장 가능(값으로 조절) |
| 슬롯 타입 제약 | **무관** — P/A 아무거나 장착 |
| 액티브 발동 | 슬롯 순서대로 **Q/E** 키. UI 차단 가드 적용 |
| 레벨 | Lv1~3 |
| 강화 재화 | **상점에서 골드 + 재료** 병행 |
| 획득 | **땅속 발견**(월드 스폰). 강화만 상점 |
| 동작 프레임워크 | **접근 A**(폴리모픽 behavior + 중앙 Manager) |
| 훅 전달 방식 | **하이브리드** — 흔한 훅은 가상 메서드, 특이 메커니즘은 구독형 |

---

## 2. 아키텍처 — 5개 기둥

```
RelicManager (플레이어 부착 MonoBehaviour, 오케스트레이터)
 ├─ RelicInventory      — 소유 유물 + 레벨 (저장 대상)
 ├─ loadout[slotCount]  — 장착 슬롯 (기본 2)
 ├─ RelicStatProvider   — PlayerStat에 1개만 등록, 스탯형 유물이 얹음
 ├─ 훅 중계             — Update/점프/착지/dig/액티브키를 behavior에 전달
 ├─ 액티브 상태기계     — 슬롯별 Ready→Active→Cooldown
 └─ 스폰물 추적         — 유물별 스폰 오브젝트 수명 관리

RelicSO (에셋)          — 유물 정의: id/이름/아이콘/타입/강화비용 + [SerializeReference] behaviour(틀)
RelicBehaviour (C# 폴리모픽) — 유물별 고유 로직 + 레벨별 게임 파라미터. Clone되어 런타임 사용
RelicContext           — behavior가 플레이어/월드에 접근하는 유일 창구
```

### 2.1 데이터 모델 — `RelicSO`

`RelicSO`는 **경제·메타 데이터만** 소유한다. 게임 파라미터(쿨타임·지속·범위 등)는
전부 behavior가 레벨별로 소유한다(단일 소스, off-by-one 방지).

```csharp
[CreateAssetMenu(menuName = "Game Data/Relic SO")]
public class RelicSO : ScriptableObject
{
    public RelicID id;
    public string  displayNameKey;    // 로컬라이제이션 키
    public string  descriptionKey;
    public Sprite  icon;
    public RelicType type;            // Passive / Active
    public int maxLevel = 3;

    // 경제 데이터만 (Lv1→2, Lv2→3 강화 비용). length == maxLevel-1
    public RelicUpgradeCost[] upgradeCosts;

    // 유물별 고유 로직 + 게임 파라미터. 폴리모픽 직렬화. 런타임엔 Clone해서 사용.
    [SerializeReference] public RelicBehaviour behaviour;
}

[System.Serializable]
public struct RelicUpgradeCost
{
    public int    gold;
    public ItemID material;
    public int    materialCount;
}

public enum RelicType { Passive, Active }
```

`RelicID`는 신규 enum. `RelicDatabase`(ScriptableObject 싱글턴, `ItemDatabase` 패턴)에
전체 `RelicSO`를 등록하고 `GetRelicByID(RelicID)`로 조회한다.

> 참고: 기존 `EquipmentType.Relic` 슬롯/`WoodRelic`/`SilverRelic`은 **장비 시스템의 유물 슬롯**이며
> 본 시스템과 별개다. 본 유물 시스템은 독립 로드아웃을 쓴다. 기존 장비 유물 슬롯은
> 건드리지 않는다(혼선 방지를 위해 추후 정리 여부는 미결로 남김 — §8).
>
> **로드아웃 칸 수는 0에서 시작한다**(`RelicManager.BaseSlotCount = 0`, 2026-09-05).
> 칸은 업그레이드 트리의 `RelicSlotUp` 노드로만 열린다 — 현재 `RelicSlot_T0_01`,
> `RelicSlot_T1_02` 두 개라 최대 2칸(`RelicManager.MaxSlotCount`). 노드를 사기 전에는
> 유물을 얻어도 **보유만 되고 장착은 안 된다**(의도). 가방·창고 UI는 아직 안 산 칸을
> '잠김'으로 그린다. 세이브(`RelicSaveData.slotCount`)는 참고값이고 **원본은 업그레이드**라,
> 노드가 없으면 로드 직후 `SyncUpgradeSlotCount`가 칸을 도로 0으로 줄인다
> (줄일 때는 밀려나는 칸을 먼저 `UnequipSlot`으로 내려 스폰물을 회수한다).

### 2.2 동작 프레임워크 — `RelicBehaviour` (하이브리드 훅)

베이스는 **얇게** 유지한다. 자주 쓰는 소수 훅만 가상 메서드로 두고,
드물고 고유한 메커니즘(비트/시야/차징점프 등)은 `OnEquip`에서 `ctx`의 이벤트를 **구독**한다.

```csharp
[System.Serializable]
public abstract class RelicBehaviour
{
    protected RelicContext ctx;
    protected int level;              // 현재 레벨(1~3)

    // ── 생명주기 ──
    public virtual void OnEquip(RelicContext c, int lv) { ctx = c; level = lv; }
    public virtual void OnUnequip() { }          // 구독 해제·스폰물 정리 요청은 여기서
    public virtual void OnLevelChanged(int lv) { level = lv; }

    // ── 코어 훅 (자주 쓰임, 가상 no-op) ──
    public virtual void OnUpdate() { }
    public virtual void ModifyDigParameters(ref DigParameters p) { } // 파기범위/랜덤 등

    // ── 액티브 생명주기 (액티브 유물만 override) ──
    public virtual float GetCooldown()  => 0f;   // 레벨별 값 반환
    public virtual float GetDuration()  => 0f;   // 0이면 즉발, >0이면 지속형
    public virtual void OnActivate() { }          // 발동 순간
    public virtual void OnActiveUpdate(float t) { } // 지속 중 매 프레임(0~duration)
    public virtual void OnActiveEnd() { }         // 지속 종료(쿨타임 진입 직전)

    // ── 복제 (정의/런타임 상태 분리) ──
    // 기본은 얕은 복사(값형 상태에 충분). 참조 상태 있으면 override.
    public virtual RelicBehaviour Clone() => (RelicBehaviour)MemberwiseClone();
}
```

**특이 메커니즘의 구독형 처리 예** (하이브리드의 "얇은" 쪽):

```csharp
// 비둘기 깃털 — 점프 이벤트만 필요 → 구독
public override void OnEquip(RelicContext c, int lv) {
    base.OnEquip(c, lv);
    c.controller.JumpRequested += OnJump;
    c.controller.Landed        += OnLand;
}
public override void OnUnequip() {
    ctx.controller.JumpRequested -= OnJump;
    ctx.controller.Landed        -= OnLand;
}
```

### 2.3 문맥 객체 — `RelicContext`

behavior를 순수 로직으로 유지하면서 코루틴·스폰·이벤트에 접근하게 하는 창구.

```csharp
public class RelicContext
{
    public Transform        player;
    public PlayerStat       stat;
    public IPlayerController controller;   // JumpRequested/Landed 이벤트 노출
    public PlayerMining     mining;        // BeforeDig 등 이벤트 노출
    public ToolController   tools;
    public RelicStatProvider statProvider; // 스탯형 유물이 modifier 등록
    public RelicManager     runner;        // 코루틴 대행 + 스폰 등록

    // 스폰물은 Manager가 소유·추적 → 해제 시 자동 정리
    public GameObject Spawn(RelicID owner, GameObject prefab, Vector3 pos);
    public void Despawn(RelicID owner);
}
```

### 2.4 오케스트레이터 — `RelicManager`

플레이어에 부착된 MonoBehaviour. 책임:

1. **로드아웃 관리** — `Equip(slot, relicId)` 시 `RelicDatabase`에서 SO 조회 →
   `so.behaviour.Clone()` → `OnEquip(ctx, level)`. `Unequip(slot)` 시 `OnUnequip` +
   스폰물 정리 + (액티브면) 상태기계 리셋.
2. **훅 중계** — `Update()`에서 각 behavior `OnUpdate()`. `ModifyDigParameters`는
   dig 파이프라인에서 슬롯 순서대로 순차 적용(multiplicative).
3. **액티브 상태기계** — 슬롯별 `Ready → Active(duration) → Cooldown → Ready`(§4).
4. **스폰물 추적** — `Dictionary<RelicID, List<GameObject>>`. `Despawn`/해제 시 파괴.
5. **RelicStatProvider 소유** — `PlayerStat.RegisterProvider`로 1회 등록.

### 2.5 스탯형 유물 경로 — `RelicStatProvider`

`BuffStatProvider` 패턴을 재사용한 신규 `IStatProvider`. `RelicManager`가 1개 소유·등록.
스탯형 behavior가 `OnEquip`/`OnLevelChanged`에서 modifier를 얹고 `OnUnequip`에서 제거.

```csharp
// 스탯형 유물 예시 (기존 StatType에 매핑되는 경우 신규 스탯 불필요)
public override void OnEquip(RelicContext c, int lv) {
    base.OnEquip(c, lv);
    c.statProvider.Set(idKey, StatType.MoveSpeed, ModifierType.Percent, speedMult[lv-1]);
}
public override void OnLevelChanged(int lv) { level = lv; OnEquip(ctx, lv); }
public override void OnUnequip() => ctx.statProvider.Clear(idKey);
```

---

## 3. 저장 / 영속

`PlayerData.relicSave`(신규)에 저장. `SaveManager`에 `coinSave`/`stockSave`와 동일 패턴으로 연동.

```csharp
[System.Serializable]
public class RelicSaveData
{
    public List<OwnedRelic> owned;      // { RelicID id, int level }
    public List<RelicID>    loadout;    // 슬롯 순서(빈 슬롯은 None)
    public int slotCount = 2;           // 확장 시 증가
}
```

- 소유·레벨의 **단일 소스 = RelicInventory**(RelicSaveData.owned 로드). Manager는 여기서 읽음.
- 상점 강화 → `RelicInventory` 레벨 증가 → 장착 중이면 `OnLevelChanged` 전파 + 저장.

---

## 4. 액티브 유물 생명주기 (상태기계)

즉발형(duration=0)과 지속형(duration>0)을 하나의 상태기계로 처리한다.

```
Ready ──[키 입력 & 쿨타임 준비]──▶ OnActivate()
                                     │
                    duration == 0 ───┼─── duration > 0
                                     │           │
                                     ▼           ▼
                                  Cooldown    Active ──[매 프레임]── OnActiveUpdate(t)
                                     │           │
                                     │      [t >= duration]
                                     │           ▼
                                     │       OnActiveEnd()
                                     │           │
                                     ▼           ▼
                                  Cooldown(GetCooldown()) ──[경과]──▶ Ready
```

- 지속형은 `Active` 상태에서 이속 감소 등 부수효과 가능(플라즈마 커터 "사용중 이속↓").
- 퀵슬롯 UI는 `Active`(지속 게이지)와 `Cooldown`(방사형)을 구분 표시.

---

## 5. 액티브 스킬 입력 + 퀵슬롯 UI

- **입력**: 유물 발동 입력은 **신규 `RelicInputHandler`**(또는 RelicManager 내부)에서 처리.
  슬롯0=**Q**, 슬롯1=**R**. (기존 `PlayerInputHandler`가 E를 상호작용에 이미 쓰므로 E 회피.)
  기존 UI 차단 가드(`UIStateManager.CurrentState != None`이면 무시) 동일 적용.
- **쿨타임·발동**: `RelicManager`가 수신 → 해당 슬롯이 액티브 & `Ready`면 상태기계 진입.
- **퀵슬롯 UI**: 슬롯 수만큼 아이콘. 각 아이콘에 키 힌트 + 상태 표시.
  - 패시브: 상시 켜짐 표시(쿨타임 없음)
  - 액티브: `Ready`/`Active`(지속 게이지)/`Cooldown`(방사형 + 남은 초)

---

## 6. 상점 강화 UI

기존 Shop 시스템 확장. "유물 강화" 패널:
- 소유 유물 목록 + 현재 레벨 표시
- 다음 레벨 비용(`RelicSO.upgradeCosts[level-1]`: 골드 + 재료) 표시
- 구매 시: 골드·재료 차감 → `RelicInventory` 레벨++ → (장착 중이면) `OnLevelChanged` → 저장
- 최대 레벨이면 "MAX" 표시

---

## 7. 게임 측에 신규로 심어야 하는 통합 지점

프레임워크가 동작하려면 기존 코드에 **최소한의 이벤트 발행/후처리 훅**을 심어야 한다.

| # | 위치 | 내용 |
|---|------|------|
| 1 | `IPlayerController` / `PlayerController` | `JumpRequested`(ref로 허용 강제 가능), `Landed(fallSpeed)` 이벤트 발행 |
| 2 | `IMiningStrategy.GetDigParameters` 소비처(`PlayerMining`) | 결과 `DigParameters`에 `RelicManager.ApplyDigModifiers(ref)` 후처리 1줄 |
| 3 | `PlayerInputHandler` | `OnRelicSlotPressed(int)` 이벤트 + Q/E 폴링 + UI 가드 |
| 4 | `StatType` enum | 유물용 신규 능력치 추가(기존 스탯으로 커버 안 되는 것만) |
| 5 | `PlayerData` / `SaveManager` | `relicSave` 필드 + 로드/세이브 연동 |
| 6 | 월드 스폰 | 땅속 유물 발견 스폰(특수 청크 또는 드랍) → 획득 시 `RelicInventory.Grant(id)` |

> §7-6(월드 스폰)은 별도 레이어다. 슬라이스에서는 **디버그 지급**(치트키/에디터 버튼)으로
> 대체하고, 실제 월드 스폰 파이프라인은 후속 작업으로 분리한다.

---

## 8. 미결 / 후속 과제

- **기존 장비 유물 슬롯**(`EquipmentType.Relic`, WoodRelic/SilverRelic)과의 관계 정리 —
  혼선 소지. 본 시스템 안착 후 통합/제거 여부 결정.
- **월드 스폰 파이프라인** — 어떤 청크에서 어떤 확률로 어떤 유물이 나오는지(밸런스·기획 필요).
- **훅 목록 점진 확장** — 슬라이스 이후 유물별로 필요한 코어 훅/구독 이벤트를 추가.
- **슬롯 확장 트리거** — 슬롯 수를 무엇으로 늘리나(업그레이드? 진행?). 슬라이스에선 고정 2.

---

## 9. 수직 슬라이스 (1차 구현 범위)

프레임워크 + 3종으로 세 behavior 카테고리를 모두 검증한다.
**패시브-스탯 예시는 기존 StatType에 매핑되는 것으로 선정**(신규 메커니즘 도입 회피).

| 유물 | 카테고리 | 검증 대상 | 매핑 |
|------|----------|-----------|------|
| **테스트 패시브**(기존 `StatType.MiningRange` 등에 % modifier) | 패시브·스탯 | `RelicStatProvider` 경로 | 기존 `StatType` |
| 비둘기 깃털(이단 점프) | 패시브·동작 | 점프/착지 구독 이벤트(§7-1) | 신규 이벤트 |
| 자석(아이템 흡인) | 액티브·즉발 | Q/E 입력 + 쿨타임 상태기계 + 퀵슬롯 UI + 코루틴 | 신규 전부 |

> tier1 명명 유물 중 **기존 StatType에 순수 매핑되는 패시브는 없다**(거미줄 장갑은 벽타기
> 메커니즘 자체가 필요 등). 따라서 슬라이스의 스탯형 검증은 기존 `StatType`(예: `MiningRange`)에
> % modifier를 얹는 **테스트용 패시브 유물**로 수행한다 — `RelicStatProvider` 경로만 격리 검증.

슬라이스 획득은 디버그 지급으로 대체(§7-6). 검증 후 나머지 유물·월드 스폰·밸런스로 확장.

---

## 10. 테스트 (EditMode 우선)

Unity 테스트 실행은 사람이 수행한다(Claude는 작성만).

- `RelicBehaviour.Clone()` — 원본 SO behavior와 런타임 인스턴스 상태 격리 검증
- 액티브 상태기계 — 즉발/지속 전이, 쿨타임 경과, 지속 중 재발동 차단
- `RelicStatProvider` — 장착/레벨업/해제 시 `PlayerStat` 최종값 반영
- 로드아웃 — 장착/교체/해제, 저장·로드 왕복(RelicSaveData), 칸 0개(업그레이드 전) 상태
- dig 파라미터 다중 유물 순차 적용 순서(multiplicative)
