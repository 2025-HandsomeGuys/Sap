# 유물(Relic) 시스템 구현 계획 (프레임워크 + 수직 슬라이스)

> **에이전트 작업자용:** 이 계획은 `superpowers:subagent-driven-development` 또는
> `superpowers:executing-plans`로 태스크 단위 실행한다. 스텝은 체크박스(`- [ ]`)로 추적한다.

**목표:** 유물 프레임워크(데이터/behavior/Manager/저장)를 세우고, 대표 유물 3종
(테스트 패시브-스탯 / 비둘기 깃털 이단점프 / 자석 액티브)으로 세 카테고리를 end-to-end 검증한다.

**아키텍처:** 접근 A — 폴리모픽 `RelicBehaviour`(각 유물 = 자기완결 클래스, `[SerializeReference]`로
SO에 직렬화, 런타임엔 Clone) + 중앙 `RelicManager`(플레이어 부착 MonoBehaviour)가 로드아웃·훅중계·
액티브 상태기계·스폰물 수명을 총괄. 스탯형은 `RelicStatProvider`(`IStatProvider`) 경유. 하이브리드 훅:
흔한 훅은 가상 메서드, 특이 메커니즘은 구독. 상세 설계: `Assets/Docs/relic-system/design.md`.

**기술 스택:** Unity(C#), ScriptableObject, `[SerializeReference]` 폴리모픽 직렬화, EditMode 테스트.

## Global Constraints

- **버전관리는 UVCS** — git 명령 금지. 각 태스크 끝 "체크인"은 **사람이 UVCS로 수행**.
- **Unity 테스트 실행은 사람이 수행** — Claude/에이전트는 테스트 **작성만** 한다. "테스트 실행" 스텝은
  Unity Test Runner(EditMode)에서 사람이 돌리고 결과 확인.
- **네임스페이스**: 유물 코드는 `Relic`(데이터는 `Relic.Data`) 네임스페이스. 기존 `Coin.*`/`Stock.*` 컨벤션과 동일.
  전역 네임스페이스 타입(`PlayerStat`, `PlayerController` 등)은 그대로 참조 가능.
- **더티 플래그·스탯**: `PlayerStat.MarkDirty()`는 modifier 변경 시 반드시 호출(`BuffStatProvider` 패턴).
- **입력 UI 가드**: 유물 입력도 `UIStateManager.Instance != null && UIStateManager.Instance.CurrentState != UIState.None`이면 무시.
- **발동키**: 슬롯0=`KeyCode.Q`, 슬롯1=`KeyCode.R` (E는 상호작용에 사용 중이라 회피).
- **파일 배치**: 신규 유물 코드는 `Assets/Scripts/Gameplay/Relics/**`. 테스트는 `Assets/Tests/EditMode/Relics/**`.

---

## 파일 구조 (생성/수정)

**생성 — `Assets/Scripts/Gameplay/Relics/`**
```
Data/
  RelicID.cs           — enum RelicID
  RelicType.cs         — enum RelicType { Passive, Active }
  RelicSO.cs           — RelicSO + RelicUpgradeCost struct
  RelicDatabase.cs     — SO 싱글턴 (ItemDatabase 패턴)
  RelicSaveData.cs     — RelicSaveData + OwnedRelic (저장 DTO)
Core/
  RelicBehaviour.cs    — abstract base + Clone
  RelicContext.cs      — 문맥 객체
  RelicStatProvider.cs — IStatProvider (BuffStatProvider 패턴)
  ActiveRelicState.cs  — 순수 C# 상태기계 (Ready/Active/Cooldown)
  RelicInventory.cs    — 소유/레벨/로드아웃 + save/load 매핑
  RelicManager.cs      — MonoBehaviour 오케스트레이터
  RelicInputHandler.cs — Q/R 입력 → RelicManager
Behaviours/
  StatRelicBehaviour.cs   — 슬라이스: 테스트 패시브-스탯
  DoubleJumpRelic.cs      — 슬라이스: 비둘기 깃털
  MagnetRelic.cs          — 슬라이스: 자석 (액티브)
UI/
  RelicQuickslotUI.cs     — 퀵슬롯 UI
Debug/
  RelicDebugGranter.cs    — 슬라이스 디버그 지급(치트)
```

**수정 (통합 지점)**
```
Assets/Scripts/UI/Player/Stats/StatModifier.cs         — ModifierSource에 Relic 추가
Assets/Scripts/UI/Player/IPlayerController.cs          — Landed 이벤트 + AirJumpQuery
Assets/Scripts/UI/Player/PlayerController.cs           — Landed 발행 + AirJumpQuery + 점프블록 수정
Assets/Scripts/UI/Player/PlayerMining.cs               — GetCurrentDigParameters에 ModifyDigParameters 훅
Assets/Scripts/UI/Player/PlayerData.cs                 — relicSave 필드
Assets/Scripts/_Core/Managers/SaveManager.cs          — relic save/load 분기
```

**테스트 (EditMode) — `Assets/Tests/EditMode/Relics/`**
```
Relics.EditModeTests.asmdef
RelicBehaviourCloneTests.cs
ActiveRelicStateTests.cs
RelicStatProviderTests.cs
RelicInventorySaveTests.cs
```

---

## Task 1: 기반 enum + ModifierSource.Relic

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Data/RelicID.cs`
- Create: `Assets/Scripts/Gameplay/Relics/Data/RelicType.cs`
- Modify: `Assets/Scripts/UI/Player/Stats/StatModifier.cs` (ModifierSource enum)

**Interfaces:**
- Produces: `Relic.Data.RelicID`, `Relic.Data.RelicType`, `ModifierSource.Relic`

- [ ] **Step 1: RelicID enum 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Data/RelicID.cs
namespace Relic.Data
{
    // 유물 ID 컨벤션: 4000번대
    public enum RelicID
    {
        None = 0,
        // 슬라이스 3종
        TestStatRelic = 4001,   // 테스트용 패시브-스탯 (기존 StatType 매핑)
        PigeonFeather = 4002,   // 비둘기 깃털 (이단 점프)
        Magnet        = 4003,   // 자석 (액티브)
        // 이후 유물은 4000번대로 추가
    }
}
```

- [ ] **Step 2: RelicType enum 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Data/RelicType.cs
namespace Relic.Data
{
    public enum RelicType
    {
        Passive,
        Active
    }
}
```

- [ ] **Step 3: ModifierSource에 Relic 추가**

`Assets/Scripts/UI/Player/Stats/StatModifier.cs`의 `ModifierSource` enum을 아래로 교체:

```csharp
public enum ModifierSource
{
    Upgrade,     // 업그레이드 트리
    Equipment,   // 장비
    Buff,        // 버프 (소모 아이템, 환경 등)
    Drill,       // 드릴 시스템
    Inventory,   // 인벤토리 시스템
    Relic,       // 유물 시스템
    Other        // 기타
}
```

- [ ] **Step 4: 컴파일 확인 (사람)**

Unity 에디터로 전환해 컴파일 에러가 없는지 확인. `ModifierSource.Relic`을 쓰는 코드가 아직 없으므로
기존 스위치문 등에서 경고만 없으면 통과.

- [ ] **Step 5: 체크인 (사람, UVCS)**

`RelicID.cs`, `RelicType.cs`, `StatModifier.cs` 체크인. 메시지: "feat(relic): add RelicID/RelicType enums + ModifierSource.Relic".

---

## Task 2: 저장 DTO (RelicSaveData / OwnedRelic)

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Data/RelicSaveData.cs`

**Interfaces:**
- Consumes: `Relic.Data.RelicID` (Task 1)
- Produces: `Relic.Data.RelicSaveData { List<OwnedRelic> owned; List<RelicID> loadout; int slotCount; bool hasData; }`,
  `Relic.Data.OwnedRelic { RelicID id; int level; }`

- [ ] **Step 1: DTO 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Data/RelicSaveData.cs
using System;
using System.Collections.Generic;

namespace Relic.Data
{
    [Serializable]
    public struct OwnedRelic
    {
        public RelicID id;
        public int level;   // 1..maxLevel

        public OwnedRelic(RelicID id, int level)
        {
            this.id = id;
            this.level = level;
        }
    }

    [Serializable]
    public class RelicSaveData
    {
        public bool hasData = false;                       // 세이브 존재 플래그 (Coin/Stock 컨벤션)
        public int slotCount = 2;                          // 확장 가능
        public List<OwnedRelic> owned = new List<OwnedRelic>();
        public List<RelicID> loadout = new List<RelicID>(); // 슬롯 순서. 빈 슬롯 = RelicID.None
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사람)** — Unity 에디터에서 에러 없음 확인.

- [ ] **Step 3: 체크인 (사람, UVCS)** — "feat(relic): add RelicSaveData/OwnedRelic DTO".

---

## Task 3: RelicBehaviour 추상 베이스 + Clone (EditMode 테스트)

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Core/RelicBehaviour.cs`
- Create: `Assets/Scripts/Gameplay/Relics/Core/RelicContext.cs`
- Create: `Assets/Tests/EditMode/Relics/Relics.EditModeTests.asmdef`
- Test: `Assets/Tests/EditMode/Relics/RelicBehaviourCloneTests.cs`

**Interfaces:**
- Consumes: `DigParameters`(전역, `IMiningStrategy.cs`), `RelicContext`
- Produces: `Relic.RelicBehaviour`(abstract, 아래 가상 메서드), `Relic.RelicContext`

- [ ] **Step 1: RelicContext 생성 (일부 참조는 이후 태스크에서 채워짐)**

```csharp
// Assets/Scripts/Gameplay/Relics/Core/RelicContext.cs
using UnityEngine;

namespace Relic
{
    // behavior가 플레이어/월드에 접근하는 유일 창구.
    // 필드는 RelicManager가 셋업 시 채운다(Task 9). Task 3에서는 타입만 확정.
    public class RelicContext
    {
        public Transform      player;
        public PlayerStat     stat;
        public PlayerMining   mining;
        public ToolController tools;
        public RelicStatProvider statProvider; // Task 5에서 타입 확정
        public RelicManager   runner;          // 코루틴 대행 + 스폰 (Task 9)
    }
}
```

> 주: `RelicStatProvider`/`RelicManager` 타입은 Task 5/9에서 생성된다. Task 3 시점에는
> 이 파일이 컴파일되려면 두 타입이 존재해야 하므로, **Task 3~9는 한 번에 컴파일 통과되도록
> 순서대로 진행**하되, 각 파일 생성 직후 컴파일은 뒤 태스크 완료 후에 성립한다. 각 태스크의
> "컴파일 확인"은 **의존 태스크까지 끝난 뒤** 성립함에 유의(중간 컴파일 실패는 정상).

- [ ] **Step 2: RelicBehaviour 추상 베이스 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Core/RelicBehaviour.cs
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
        public virtual void OnLanded() { }
        public virtual bool TryConsumeAirJump() => false;   // 이단점프류

        // ── 액티브 생명주기 (액티브 유물만 override) ──
        public virtual float GetCooldown() => 0f;
        public virtual float GetDuration() => 0f;
        public virtual void OnActivate() { }
        public virtual void OnActiveUpdate(float elapsed) { }
        public virtual void OnActiveEnd() { }

        // ── 복제: 정의(SO)/런타임 상태 분리. 기본 얕은 복사(값형 상태에 충분). ──
        public virtual RelicBehaviour Clone() => (RelicBehaviour)MemberwiseClone();

        // 테스트 접근용 (런타임 상태 격리 검증)
        public int Level => level;
    }
}
```

- [ ] **Step 3: EditMode 테스트 asmdef 생성**

```json
// Assets/Tests/EditMode/Relics/Relics.EditModeTests.asmdef
{
    "name": "Relics.EditModeTests",
    "references": [
        "GameScripts"
    ],
    "includePlatforms": [ "Editor" ],
    "optionalUnityReferences": [ "TestAssemblies" ],
    "autoReferenced": false
}
```

> `references`의 `"GameScripts"`는 게임 코드 asmdef 이름. 실제 이름이 다르면
> `Assets/Scripts/**/*.asmdef`의 `name`으로 교체. (CLAUDE.md 기준 단일 `GameScripts.asmdef`.)

- [ ] **Step 4: 실패 테스트 작성 — Clone 상태 격리**

```csharp
// Assets/Tests/EditMode/Relics/RelicBehaviourCloneTests.cs
using NUnit.Framework;
using Relic;

public class RelicBehaviourCloneTests
{
    // 테스트 전용 더미 behavior (값형 런타임 상태 보유)
    private class DummyRelic : RelicBehaviour
    {
        public int counter;
        public void Bump() => counter++;
    }

    [Test]
    public void Clone_ProducesIndependentInstance()
    {
        var origin = new DummyRelic { counter = 0 };
        var clone = (DummyRelic)origin.Clone();

        clone.Bump();   // 클론 상태만 변경

        Assert.AreEqual(1, clone.counter, "클론 상태가 변경돼야 함");
        Assert.AreEqual(0, origin.counter, "원본(SO 틀) 상태는 불변이어야 함");
        Assert.AreNotSame(origin, clone, "서로 다른 인스턴스여야 함");
    }

    [Test]
    public void TwoClones_AreIndependent()
    {
        var origin = new DummyRelic();
        var a = (DummyRelic)origin.Clone();
        var b = (DummyRelic)origin.Clone();

        a.Bump();

        Assert.AreEqual(1, a.counter);
        Assert.AreEqual(0, b.counter, "동일 유물 2슬롯 장착 시 상태 충돌 없어야 함");
    }
}
```

- [ ] **Step 5: 테스트 실행 (사람, Unity Test Runner EditMode)**

`Relics.EditModeTests` 실행. **의존 타입(RelicStatProvider/RelicManager) 미완이면 Task 9까지 완료 후 실행.**
기대: `Clone_ProducesIndependentInstance`, `TwoClones_AreIndependent` PASS.

- [ ] **Step 6: 체크인 (사람, UVCS)** — "feat(relic): RelicBehaviour base + Clone isolation + context".

---

## Task 4: ActiveRelicState 순수 상태기계 (EditMode 테스트)

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Core/ActiveRelicState.cs`
- Test: `Assets/Tests/EditMode/Relics/ActiveRelicStateTests.cs`

**Interfaces:**
- Produces: `Relic.RelicPhase { Ready, Active, Cooldown }`,
  `Relic.RelicTickResult { bool activeTick; float activeElapsed; bool activeEnded; bool becameReady; }`,
  `Relic.ActiveRelicState`(메서드: `bool TryActivate(float duration, float cooldown)`, `RelicTickResult Tick(float dt)`, `RelicPhase Phase { get; }`, `float Timer { get; }`)

- [ ] **Step 1: 상태기계 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Core/ActiveRelicState.cs
namespace Relic
{
    public enum RelicPhase { Ready, Active, Cooldown }

    public struct RelicTickResult
    {
        public bool  activeTick;    // 이 프레임 Active 지속 중
        public float activeElapsed; // 발동 후 경과(0..duration)
        public bool  activeEnded;   // 이 프레임 Active→Cooldown 전이
        public bool  becameReady;   // 이 프레임 Cooldown→Ready 전이
    }

    // 즉발(duration=0)과 지속형(duration>0)을 하나로 처리.
    // 순수 C# — MonoBehaviour 비의존(테스트 용이).
    public class ActiveRelicState
    {
        public RelicPhase Phase { get; private set; } = RelicPhase.Ready;
        public float Timer => _timer;

        private float _timer;
        private float _duration;
        private float _cooldown;

        // Ready일 때만 발동. duration/cooldown은 발동 시점 값으로 확정.
        public bool TryActivate(float duration, float cooldown)
        {
            if (Phase != RelicPhase.Ready) return false;

            _duration = duration;
            _cooldown = cooldown;

            if (duration > 0f)
            {
                Phase = RelicPhase.Active;
                _timer = 0f;
            }
            else
            {
                Phase = RelicPhase.Cooldown;
                _timer = cooldown;
            }
            return true;
        }

        public RelicTickResult Tick(float dt)
        {
            var r = new RelicTickResult();
            switch (Phase)
            {
                case RelicPhase.Active:
                    _timer += dt;
                    if (_timer >= _duration)
                    {
                        Phase = RelicPhase.Cooldown;
                        _timer = _cooldown;
                        r.activeEnded = true;
                    }
                    else
                    {
                        r.activeTick = true;
                        r.activeElapsed = _timer;
                    }
                    break;

                case RelicPhase.Cooldown:
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        Phase = RelicPhase.Ready;
                        _timer = 0f;
                        r.becameReady = true;
                    }
                    break;
            }
            return r;
        }
    }
}
```

- [ ] **Step 2: 실패 테스트 작성**

```csharp
// Assets/Tests/EditMode/Relics/ActiveRelicStateTests.cs
using NUnit.Framework;
using Relic;

public class ActiveRelicStateTests
{
    [Test]
    public void Instant_GoesStraightToCooldown()
    {
        var s = new ActiveRelicState();
        Assert.IsTrue(s.TryActivate(duration: 0f, cooldown: 2f));
        Assert.AreEqual(RelicPhase.Cooldown, s.Phase);
    }

    [Test]
    public void Cooldown_BlocksReactivation()
    {
        var s = new ActiveRelicState();
        s.TryActivate(0f, 2f);
        Assert.IsFalse(s.TryActivate(0f, 2f), "쿨타임 중 재발동 차단");
    }

    [Test]
    public void Cooldown_ExpiresToReady()
    {
        var s = new ActiveRelicState();
        s.TryActivate(0f, 1f);
        var r = s.Tick(1.0f);
        Assert.IsTrue(r.becameReady);
        Assert.AreEqual(RelicPhase.Ready, s.Phase);
        Assert.IsTrue(s.TryActivate(0f, 1f), "쿨타임 후 재발동 가능");
    }

    [Test]
    public void Duration_TicksThenEndsThenCooldown()
    {
        var s = new ActiveRelicState();
        s.TryActivate(duration: 1f, cooldown: 2f);
        Assert.AreEqual(RelicPhase.Active, s.Phase);

        var mid = s.Tick(0.5f);
        Assert.IsTrue(mid.activeTick);
        Assert.AreEqual(0.5f, mid.activeElapsed, 1e-4f);

        var end = s.Tick(0.5f);
        Assert.IsTrue(end.activeEnded);
        Assert.AreEqual(RelicPhase.Cooldown, s.Phase);
    }
}
```

- [ ] **Step 3: 테스트 실행 (사람, EditMode)** — 4개 PASS 기대 (의존 없음 → 지금 바로 실행 가능).

- [ ] **Step 4: 체크인 (사람, UVCS)** — "feat(relic): ActiveRelicState state machine + tests".

---

## Task 5: RelicStatProvider (EditMode 테스트)

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Core/RelicStatProvider.cs`
- Test: `Assets/Tests/EditMode/Relics/RelicStatProviderTests.cs`

**Interfaces:**
- Consumes: `IStatProvider`, `StatModifier`, `StatType`, `ModifierType`, `ModifierSource.Relic`(전역)
- Produces: `Relic.RelicStatProvider`(메서드: `void Set(string key, StatType, ModifierType, float value)`,
  `void Clear(string key)`, `IReadOnlyList<StatModifier> GetModifiers()`), 생성자는 순수(모노비헤이비어 아님)

> 설계상 `RelicStatProvider`는 `RelicManager`가 소유해 `PlayerStat`에 1개만 등록한다.
> `BuffStatProvider`와 달리 자체 만료 타이머가 없다(유물 스탯은 장착/해제로만 변함). key 기반 upsert.

- [ ] **Step 1: RelicStatProvider 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Core/RelicStatProvider.cs
using System.Collections.Generic;

namespace Relic
{
    // 유물 스탯 modifier 집계. RelicManager가 소유하고 PlayerStat에 등록.
    // MonoBehaviour 아님(순수) — 등록/해제·MarkDirty는 RelicManager가 담당.
    public class RelicStatProvider : IStatProvider
    {
        private readonly Dictionary<string, StatModifier> _byKey = new Dictionary<string, StatModifier>();
        private readonly List<StatModifier> _cache = new List<StatModifier>();
        private bool _dirty = true;

        // 콜백: modifier가 바뀌면 RelicManager가 PlayerStat.MarkDirty() 호출하도록 통지
        public System.Action OnChanged;

        public void Set(string key, StatType statType, ModifierType modType, float value)
        {
            _byKey[key] = new StatModifier(statType, modType, value, ModifierSource.Relic);
            _dirty = true;
            OnChanged?.Invoke();
        }

        public void Clear(string key)
        {
            if (_byKey.Remove(key))
            {
                _dirty = true;
                OnChanged?.Invoke();
            }
        }

        public void ClearAll()
        {
            if (_byKey.Count == 0) return;
            _byKey.Clear();
            _dirty = true;
            OnChanged?.Invoke();
        }

        public IReadOnlyList<StatModifier> GetModifiers()
        {
            if (_dirty)
            {
                _cache.Clear();
                foreach (var kv in _byKey) _cache.Add(kv.Value);
                _dirty = false;
            }
            return _cache;
        }
    }
}
```

- [ ] **Step 2: 실패 테스트 작성**

```csharp
// Assets/Tests/EditMode/Relics/RelicStatProviderTests.cs
using NUnit.Framework;
using Relic;

public class RelicStatProviderTests
{
    [Test]
    public void Set_AddsModifierWithRelicSource()
    {
        var p = new RelicStatProvider();
        p.Set("relic:test", StatType.MiningRange, ModifierType.Percent, 1.2f);

        var mods = p.GetModifiers();
        Assert.AreEqual(1, mods.Count);
        Assert.AreEqual(StatType.MiningRange, mods[0].statType);
        Assert.AreEqual(ModifierSource.Relic, mods[0].source);
        Assert.AreEqual(1.2f, mods[0].value, 1e-4f);
    }

    [Test]
    public void Set_SameKey_UpsertsInsteadOfDuplicating()
    {
        var p = new RelicStatProvider();
        p.Set("relic:test", StatType.MiningRange, ModifierType.Percent, 1.1f); // Lv1
        p.Set("relic:test", StatType.MiningRange, ModifierType.Percent, 1.3f); // Lv2 강화

        var mods = p.GetModifiers();
        Assert.AreEqual(1, mods.Count, "레벨업은 중복이 아니라 갱신");
        Assert.AreEqual(1.3f, mods[0].value, 1e-4f);
    }

    [Test]
    public void Clear_RemovesModifier()
    {
        var p = new RelicStatProvider();
        p.Set("relic:test", StatType.MoveSpeed, ModifierType.Flat, 5f);
        p.Clear("relic:test");
        Assert.AreEqual(0, p.GetModifiers().Count);
    }
}
```

- [ ] **Step 3: 테스트 실행 (사람, EditMode)** — 3개 PASS 기대.

- [ ] **Step 4: 체크인 (사람, UVCS)** — "feat(relic): RelicStatProvider + tests".

---

## Task 6: RelicSO + RelicDatabase

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Data/RelicSO.cs`
- Create: `Assets/Scripts/Gameplay/Relics/Data/RelicDatabase.cs`

**Interfaces:**
- Consumes: `RelicID`, `RelicType`(Task 1), `RelicBehaviour`(Task 3), `ItemID`(전역)
- Produces: `Relic.Data.RelicSO`(필드: `RelicID id; string displayNameKey; string descriptionKey; Sprite icon;
  RelicType type; int maxLevel; RelicUpgradeCost[] upgradeCosts; RelicBehaviour behaviour;`),
  `Relic.Data.RelicUpgradeCost { int gold; ItemID material; int materialCount; }`,
  `Relic.Data.RelicDatabase`(정적 `Instance`, `RelicSO GetRelicByID(RelicID)`)

- [ ] **Step 1: RelicSO 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Data/RelicSO.cs
using System;
using UnityEngine;

namespace Relic.Data
{
    [Serializable]
    public struct RelicUpgradeCost
    {
        public int    gold;
        public ItemID material;      // 전역 ItemID enum
        public int    materialCount;
    }

    [CreateAssetMenu(fileName = "Relic", menuName = "Game Data/Relic SO")]
    public class RelicSO : ScriptableObject
    {
        public RelicID  id;
        public string   displayNameKey;
        public string   descriptionKey;
        public Sprite   icon;
        public RelicType type;
        public int       maxLevel = 3;

        // 경제 데이터만 (Lv1→2, Lv2→3). length == maxLevel-1
        public RelicUpgradeCost[] upgradeCosts;

        // 유물별 고유 로직 + 게임 파라미터. 런타임엔 Clone해서 사용.
        [SerializeReference] public RelicBehaviour behaviour;
    }
}
```

- [ ] **Step 2: RelicDatabase 생성 (ItemDatabase 패턴)**

```csharp
// Assets/Scripts/Gameplay/Relics/Data/RelicDatabase.cs
using System.Collections.Generic;
using UnityEngine;

namespace Relic.Data
{
    [CreateAssetMenu(fileName = "RelicDatabase", menuName = "Database/Relic Database")]
    public class RelicDatabase : ScriptableObject
    {
        public static RelicDatabase Instance { get; private set; }

        public List<RelicSO> allRelics;

        private Dictionary<RelicID, RelicSO> _dict;

        private void OnEnable()
        {
            Instance = this;
            _dict = new Dictionary<RelicID, RelicSO>();
            if (allRelics != null)
            {
                foreach (var r in allRelics)
                {
                    if (r != null && r.id != RelicID.None && !_dict.ContainsKey(r.id))
                        _dict.Add(r.id, r);
                }
            }
        }

        public RelicSO GetRelicByID(RelicID id)
        {
            if (_dict == null) OnEnable();
            _dict.TryGetValue(id, out var so);
            return so;
        }
    }
}
```

- [ ] **Step 3: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 4: 체크인 (사람, UVCS)** — "feat(relic): RelicSO + RelicDatabase".

---

## Task 7: RelicInventory (소유/레벨/로드아웃 + 저장 왕복, EditMode 테스트)

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Core/RelicInventory.cs`
- Test: `Assets/Tests/EditMode/Relics/RelicInventorySaveTests.cs`

**Interfaces:**
- Consumes: `RelicID`(Task 1), `RelicSaveData`/`OwnedRelic`(Task 2)
- Produces: `Relic.RelicInventory`(순수 C#) — 메서드:
  `void Grant(RelicID id)`, `bool IsOwned(RelicID id)`, `int GetLevel(RelicID id)`, `bool TryUpgrade(RelicID id, int maxLevel)`,
  `bool Equip(int slot, RelicID id)`, `void Unequip(int slot)`, `RelicID GetEquipped(int slot)`, `int SlotCount`,
  `RelicSaveData ToSaveData()`, `void LoadFrom(RelicSaveData data)`; 이벤트 `Action OnChanged`

- [ ] **Step 1: RelicInventory 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Core/RelicInventory.cs
using System;
using System.Collections.Generic;
using Relic.Data;

namespace Relic
{
    // 소유 유물·레벨·로드아웃의 단일 소스. 순수 C#(저장 왕복 테스트 용이).
    public class RelicInventory
    {
        public event Action OnChanged;

        private readonly Dictionary<RelicID, int> _owned = new Dictionary<RelicID, int>(); // id -> level
        private RelicID[] _loadout = { RelicID.None, RelicID.None };

        public int SlotCount => _loadout.Length;

        public void Grant(RelicID id)
        {
            if (id == RelicID.None) return;
            if (!_owned.ContainsKey(id))
            {
                _owned[id] = 1;      // 획득 시 Lv1
                OnChanged?.Invoke();
            }
        }

        public bool IsOwned(RelicID id) => _owned.ContainsKey(id);

        public int GetLevel(RelicID id) => _owned.TryGetValue(id, out var lv) ? lv : 0;

        // 상점 강화: 레벨 +1 (최대치 이하일 때만). 재화 차감은 호출측(상점)이 담당.
        public bool TryUpgrade(RelicID id, int maxLevel)
        {
            if (!_owned.TryGetValue(id, out var lv)) return false;
            if (lv >= maxLevel) return false;
            _owned[id] = lv + 1;
            OnChanged?.Invoke();
            return true;
        }

        public bool Equip(int slot, RelicID id)
        {
            if (slot < 0 || slot >= _loadout.Length) return false;
            if (id != RelicID.None && !IsOwned(id)) return false;
            // 같은 유물 중복 장착 방지
            for (int i = 0; i < _loadout.Length; i++)
                if (i != slot && _loadout[i] == id && id != RelicID.None)
                    return false;
            _loadout[slot] = id;
            OnChanged?.Invoke();
            return true;
        }

        public void Unequip(int slot)
        {
            if (slot < 0 || slot >= _loadout.Length) return;
            if (_loadout[slot] == RelicID.None) return;
            _loadout[slot] = RelicID.None;
            OnChanged?.Invoke();
        }

        public RelicID GetEquipped(int slot)
            => (slot >= 0 && slot < _loadout.Length) ? _loadout[slot] : RelicID.None;

        public void SetSlotCount(int count)
        {
            if (count < 1 || count == _loadout.Length) return;
            var next = new RelicID[count];
            for (int i = 0; i < count; i++)
                next[i] = (i < _loadout.Length) ? _loadout[i] : RelicID.None;
            _loadout = next;
            OnChanged?.Invoke();
        }

        // ── 저장 왕복 ──
        public RelicSaveData ToSaveData()
        {
            var data = new RelicSaveData
            {
                hasData = true,
                slotCount = _loadout.Length,
                owned = new List<OwnedRelic>(),
                loadout = new List<RelicID>(_loadout)
            };
            foreach (var kv in _owned)
                data.owned.Add(new OwnedRelic(kv.Key, kv.Value));
            return data;
        }

        public void LoadFrom(RelicSaveData data)
        {
            _owned.Clear();
            if (data == null || !data.hasData)
            {
                _loadout = new[] { RelicID.None, RelicID.None };
                OnChanged?.Invoke();
                return;
            }

            if (data.owned != null)
                foreach (var o in data.owned)
                    if (o.id != RelicID.None) _owned[o.id] = o.level;

            int count = data.slotCount > 0 ? data.slotCount : 2;
            _loadout = new RelicID[count];
            for (int i = 0; i < count; i++)
                _loadout[i] = (data.loadout != null && i < data.loadout.Count) ? data.loadout[i] : RelicID.None;

            OnChanged?.Invoke();
        }
    }
}
```

- [ ] **Step 2: 실패 테스트 작성 (저장 왕복 + 규칙)**

```csharp
// Assets/Tests/EditMode/Relics/RelicInventorySaveTests.cs
using NUnit.Framework;
using Relic;
using Relic.Data;

public class RelicInventorySaveTests
{
    [Test]
    public void Grant_SetsLevel1_AndIsOwned()
    {
        var inv = new RelicInventory();
        inv.Grant(RelicID.Magnet);
        Assert.IsTrue(inv.IsOwned(RelicID.Magnet));
        Assert.AreEqual(1, inv.GetLevel(RelicID.Magnet));
    }

    [Test]
    public void TryUpgrade_RespectsMaxLevel()
    {
        var inv = new RelicInventory();
        inv.Grant(RelicID.Magnet);                 // Lv1
        Assert.IsTrue(inv.TryUpgrade(RelicID.Magnet, 3));  // ->2
        Assert.IsTrue(inv.TryUpgrade(RelicID.Magnet, 3));  // ->3
        Assert.IsFalse(inv.TryUpgrade(RelicID.Magnet, 3)); // max, 실패
        Assert.AreEqual(3, inv.GetLevel(RelicID.Magnet));
    }

    [Test]
    public void Equip_RejectsUnowned_AndDuplicates()
    {
        var inv = new RelicInventory();
        Assert.IsFalse(inv.Equip(0, RelicID.Magnet), "미보유 장착 거부");
        inv.Grant(RelicID.Magnet);
        Assert.IsTrue(inv.Equip(0, RelicID.Magnet));
        Assert.IsFalse(inv.Equip(1, RelicID.Magnet), "같은 유물 중복 장착 거부");
    }

    [Test]
    public void SaveLoad_Roundtrips()
    {
        var inv = new RelicInventory();
        inv.Grant(RelicID.Magnet);
        inv.Grant(RelicID.PigeonFeather);
        inv.TryUpgrade(RelicID.Magnet, 3);   // Lv2
        inv.Equip(0, RelicID.Magnet);
        inv.Equip(1, RelicID.PigeonFeather);

        var data = inv.ToSaveData();

        var restored = new RelicInventory();
        restored.LoadFrom(data);

        Assert.AreEqual(2, restored.GetLevel(RelicID.Magnet));
        Assert.AreEqual(1, restored.GetLevel(RelicID.PigeonFeather));
        Assert.AreEqual(RelicID.Magnet, restored.GetEquipped(0));
        Assert.AreEqual(RelicID.PigeonFeather, restored.GetEquipped(1));
    }
}
```

- [ ] **Step 3: 테스트 실행 (사람, EditMode)** — 4개 PASS 기대.

- [ ] **Step 4: 체크인 (사람, UVCS)** — "feat(relic): RelicInventory + save roundtrip tests".

---

## Task 8: RelicManager (오케스트레이터 MonoBehaviour)

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Core/RelicManager.cs`

**Interfaces:**
- Consumes: `RelicInventory`(Task 7), `RelicDatabase`/`RelicSO`(Task 6), `RelicBehaviour`(Task 3),
  `RelicStatProvider`(Task 5), `ActiveRelicState`(Task 4), `RelicContext`(Task 3),
  `PlayerStat`/`PlayerMining`/`ToolController`(전역)
- Produces: `Relic.RelicManager`(MonoBehaviour). 메서드:
  `void EquipSlot(int slot, RelicID id)`, `void UnequipSlot(int slot)`, `void ActivateSlot(int slot)`,
  `void ApplyDigModifiers(ref DigParameters p)`, `bool TryConsumeAirJump()`, `void NotifyLanded()`,
  `GameObject Spawn(RelicID owner, GameObject prefab, Vector3 pos)`, `void Despawn(RelicID owner)`,
  `RelicInventory Inventory { get; }`, `RelicPhase GetSlotPhase(int slot)`, `float GetSlotTimer(int slot)`

- [ ] **Step 1: RelicManager 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Core/RelicManager.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Relic.Data;

namespace Relic
{
    // 플레이어에 부착. 로드아웃·훅중계·액티브 상태기계·스폰물 수명 총괄.
    public class RelicManager : MonoBehaviour
    {
        public RelicInventory Inventory { get; private set; } = new RelicInventory();

        private RelicContext _ctx;
        private readonly RelicStatProvider _statProvider = new RelicStatProvider();
        private PlayerStat _playerStat;

        // 슬롯별 런타임 behavior(clone) + 액티브 상태
        private RelicBehaviour[] _slotBehaviour;
        private ActiveRelicState[] _slotActive;

        // 유물별 스폰물 추적
        private readonly Dictionary<RelicID, List<GameObject>> _spawned = new Dictionary<RelicID, List<GameObject>>();
        private RelicID[] _spawnOwnerBySlot; // slot -> 현재 장착 유물 id (스폰 소유자 매핑)

        private void Awake()
        {
            _playerStat = GetComponentInParent<PlayerStat>();
            if (_playerStat == null) _playerStat = FindFirstObjectByType<PlayerStat>();

            _ctx = new RelicContext
            {
                player = transform,
                stat   = _playerStat,
                mining = GetComponentInParent<PlayerMining>() ?? FindFirstObjectByType<PlayerMining>(),
                tools  = GetComponentInParent<ToolController>() ?? FindFirstObjectByType<ToolController>(),
                statProvider = _statProvider,
                runner = this
            };

            _statProvider.OnChanged = () => { if (_playerStat != null) _playerStat.MarkDirty(); };
            if (_playerStat != null) _playerStat.RegisterProvider(_statProvider);

            int n = Inventory.SlotCount;
            _slotBehaviour = new RelicBehaviour[n];
            _slotActive = new ActiveRelicState[n];
            _spawnOwnerBySlot = new RelicID[n];
        }

        private void OnDestroy()
        {
            if (_playerStat != null) _playerStat.UnregisterProvider(_statProvider);
        }

        // ── 장착/해제 ──
        public void EquipSlot(int slot, RelicID id)
        {
            if (slot < 0 || slot >= _slotBehaviour.Length) return;
            UnequipSlot(slot); // 기존 것 정리

            if (!Inventory.Equip(slot, id)) return;
            if (id == RelicID.None) return;

            var so = RelicDatabase.Instance != null ? RelicDatabase.Instance.GetRelicByID(id) : null;
            if (so == null || so.behaviour == null)
            {
                Debug.LogWarning($"[RelicManager] RelicSO/behaviour 없음: {id}");
                return;
            }

            var behaviour = so.behaviour.Clone();
            int level = Mathf.Max(1, Inventory.GetLevel(id));
            behaviour.OnEquip(_ctx, level);

            _slotBehaviour[slot] = behaviour;
            _spawnOwnerBySlot[slot] = id;
            _slotActive[slot] = (so.type == RelicType.Active) ? new ActiveRelicState() : null;
        }

        public void UnequipSlot(int slot)
        {
            if (slot < 0 || slot >= _slotBehaviour.Length) return;
            var b = _slotBehaviour[slot];
            if (b != null)
            {
                b.OnUnequip();
                Despawn(_spawnOwnerBySlot[slot]);
            }
            _slotBehaviour[slot] = null;
            _slotActive[slot] = null;
            _spawnOwnerBySlot[slot] = RelicID.None;
            Inventory.Unequip(slot);
        }

        // ── 액티브 발동 (입력에서 호출) ──
        public void ActivateSlot(int slot)
        {
            if (slot < 0 || slot >= _slotBehaviour.Length) return;
            var b = _slotBehaviour[slot];
            var st = _slotActive[slot];
            if (b == null || st == null) return; // 패시브거나 빈 슬롯

            if (st.TryActivate(b.GetDuration(), b.GetCooldown()))
                b.OnActivate();
        }

        // ── 매 프레임 훅 중계 ──
        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _slotBehaviour.Length; i++)
            {
                var b = _slotBehaviour[i];
                if (b == null) continue;

                b.OnUpdate();

                var st = _slotActive[i];
                if (st != null)
                {
                    var r = st.Tick(dt);
                    if (r.activeTick) b.OnActiveUpdate(r.activeElapsed);
                    if (r.activeEnded) b.OnActiveEnd();
                }
            }
        }

        // ── 코어 훅 중계 (통합 지점에서 호출) ──
        public void ApplyDigModifiers(ref DigParameters p)
        {
            for (int i = 0; i < _slotBehaviour.Length; i++)
                _slotBehaviour[i]?.ModifyDigParameters(ref p);
        }

        public bool TryConsumeAirJump()
        {
            for (int i = 0; i < _slotBehaviour.Length; i++)
                if (_slotBehaviour[i] != null && _slotBehaviour[i].TryConsumeAirJump())
                    return true;
            return false;
        }

        public void NotifyLanded()
        {
            for (int i = 0; i < _slotBehaviour.Length; i++)
                _slotBehaviour[i]?.OnLanded();
        }

        // ── 스폰물 수명 ──
        public GameObject Spawn(RelicID owner, GameObject prefab, Vector3 pos)
        {
            var go = Instantiate(prefab, pos, Quaternion.identity);
            if (!_spawned.TryGetValue(owner, out var list))
            {
                list = new List<GameObject>();
                _spawned[owner] = list;
            }
            list.Add(go);
            return go;
        }

        public void Despawn(RelicID owner)
        {
            if (owner == RelicID.None) return;
            if (_spawned.TryGetValue(owner, out var list))
            {
                foreach (var go in list) if (go != null) Destroy(go);
                list.Clear();
                _spawned.Remove(owner);
            }
        }

        // ── UI 조회 ──
        public RelicPhase GetSlotPhase(int slot)
            => (_slotActive != null && slot >= 0 && slot < _slotActive.Length && _slotActive[slot] != null)
               ? _slotActive[slot].Phase : RelicPhase.Ready;

        public float GetSlotTimer(int slot)
            => (_slotActive != null && slot >= 0 && slot < _slotActive.Length && _slotActive[slot] != null)
               ? _slotActive[slot].Timer : 0f;

        // 강화 후 레벨 반영 (상점에서 호출)
        public void RefreshLevel(RelicID id)
        {
            for (int i = 0; i < _slotBehaviour.Length; i++)
                if (_spawnOwnerBySlot[i] == id && _slotBehaviour[i] != null)
                    _slotBehaviour[i].OnLevelChanged(Inventory.GetLevel(id));
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사람)** — Task 3~8 완료 시점에 전체 `Relic.*` 컴파일 성립. 에러 없음 확인.

- [ ] **Step 3: Task 3의 Clone 테스트 등 EditMode 전체 재실행 (사람)** — Task 3~7 테스트 전부 PASS 확인.

- [ ] **Step 4: 체크인 (사람, UVCS)** — "feat(relic): RelicManager orchestrator".

---

## Task 9: 점프/착지 통합 (PlayerController)

**Files:**
- Modify: `Assets/Scripts/UI/Player/IPlayerController.cs`
- Modify: `Assets/Scripts/UI/Player/PlayerController.cs:156-159` (점프 블록), `:241`(OnLanded)

**Interfaces:**
- Produces: `IPlayerController.Landed`(event Action), `IPlayerController.AirJumpQuery`(Func<bool>);
  PlayerController가 점프 시 지면 아니면 `AirJumpQuery`로 이단점프 허용 질의, 착지 시 `Landed` 발행.

- [ ] **Step 1: IPlayerController 확장**

`Assets/Scripts/UI/Player/IPlayerController.cs`를 아래로 교체:

```csharp
using System;
using UnityEngine;

public interface IPlayerController
{
    bool IsWallClimbing { get; }
    void ApplyExternalKnockback(Vector2 velocity, float duration);

    // 유물 훅
    event Action Landed;                 // 착지 순간
    System.Func<bool> AirJumpQuery { get; set; } // true면 공중 추가 점프 허용(소비)
}
```

- [ ] **Step 2: PlayerController에 이벤트/필드 추가**

`PlayerController` 클래스 필드부(`:62` 부근, 다른 필드 선언 근처)에 추가:

```csharp
    public event System.Action Landed;
    public System.Func<bool> AirJumpQuery { get; set; }
```

- [ ] **Step 3: 점프 입력 블록 수정 — 이단점프 허용**

`PlayerController.cs:156-159`의 기존 블록:

```csharp
if (Input.GetButtonDown("Jump") && isGrounded && !isWallClimbing && !isSlidingDown && !isHardLanding)
{
    Jump();
}
```

을 아래로 교체:

```csharp
if (Input.GetButtonDown("Jump"))
{
    if (isGrounded && !isWallClimbing && !isSlidingDown && !isHardLanding)
    {
        Jump();
    }
    else if (!isGrounded && !isWallClimbing && !isSlidingDown && !isHardLanding
             && AirJumpQuery != null && AirJumpQuery.Invoke())
    {
        Jump();   // 유물이 허용한 공중 점프
    }
}
```

- [ ] **Step 4: 착지 이벤트 발행**

`PlayerController.cs:241`의 `OnLanded()` 메서드 본문 **첫 줄**에 추가:

```csharp
    Landed?.Invoke();
```

- [ ] **Step 5: RelicManager 배선 — PlayerController 이벤트 구독**

`RelicManager.Awake()`(Task 8) 끝부분에 아래 추가(필드/구독). 먼저 클래스 상단에 필드:

```csharp
        private IPlayerController _controller;
```

`Awake()` 내 `_ctx` 구성 뒤에 배선 추가:

```csharp
            _controller = GetComponentInParent<PlayerController>();
            if (_controller == null) _controller = FindFirstObjectByType<PlayerController>();
            if (_controller != null)
            {
                _controller.AirJumpQuery = TryConsumeAirJump;
                _controller.Landed += NotifyLanded;
            }
```

그리고 `OnDestroy()`에 해제 추가:

```csharp
            if (_controller != null)
            {
                _controller.Landed -= NotifyLanded;
                if (ReferenceEquals(_controller.AirJumpQuery, (System.Func<bool>)TryConsumeAirJump))
                    _controller.AirJumpQuery = null;
            }
```

> `RelicContext`에 `controller`가 필요하면 여기서 `_ctx.controller = _controller;`도 셋업(선택).
> 슬라이스의 DoubleJumpRelic은 Manager 경유(TryConsumeAirJump/NotifyLanded)로 동작하므로 ctx.controller는 불필요.

- [ ] **Step 6: 컴파일 확인 (사람)** — 에러 없음. (기존 `Jump()`의 `force` 미사용 버그는 이 태스크 범위 밖 — 건드리지 않음.)

- [ ] **Step 7: 체크인 (사람, UVCS)** — "feat(relic): jump/land integration hooks in PlayerController".

---

## Task 10: dig 파라미터 통합 (PlayerMining)

**Files:**
- Modify: `Assets/Scripts/UI/Player/PlayerMining.cs:349-357` (GetCurrentDigParameters)

**Interfaces:**
- Consumes: `RelicManager.ApplyDigModifiers(ref DigParameters)`(Task 8)
- Produces: 모든 dig 소비처(`Digger.cs`)가 경유하는 단일 지점에서 유물 dig 후처리 적용

- [ ] **Step 1: PlayerMining에 RelicManager 참조 추가**

`PlayerMining` 클래스 필드부에 추가:

```csharp
    private Relic.RelicManager _relicManager;
```

`PlayerMining`의 `Start()`(또는 `Awake()`) 말미에 캐싱 추가:

```csharp
    _relicManager = GetComponentInChildren<Relic.RelicManager>();
    if (_relicManager == null) _relicManager = FindFirstObjectByType<Relic.RelicManager>();
```

> 파일 상단에 `using`을 추가하지 않고 `Relic.RelicManager`로 전역 참조한다(네임스페이스 충돌 회피).

- [ ] **Step 2: GetCurrentDigParameters 반환부에 훅 삽입**

`PlayerMining.cs:349-357`를 아래로 교체:

```csharp
public DigParameters GetCurrentDigParameters(float baseRadius, TileType targetTileType)
{
    // UI가 열려있으면 모든 채굴 파라미터 거부
    if (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState != UIState.None)
        return new DigParameters { CanDig = false };

    if (_currentStrategy == null) return new DigParameters { CanDig = false };

    var p = _currentStrategy.GetDigParameters(baseRadius, targetTileType);

    // 유물 dig 후처리 (슬롯 순서대로 순차 적용)
    if (_relicManager != null && p.CanDig)
        _relicManager.ApplyDigModifiers(ref p);

    return p;
}
```

- [ ] **Step 3: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 4: 체크인 (사람, UVCS)** — "feat(relic): dig parameter post-process hook in PlayerMining".

---

## Task 11: 입력 핸들러 (RelicInputHandler, Q/R)

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Core/RelicInputHandler.cs`

**Interfaces:**
- Consumes: `RelicManager.ActivateSlot(int)`(Task 8), `UIStateManager`(전역)
- Produces: Q→slot0, R→slot1 발동. UI 열림 시 무시.

- [ ] **Step 1: RelicInputHandler 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Core/RelicInputHandler.cs
using UnityEngine;

namespace Relic
{
    // 유물 발동 입력 전담. RelicManager와 같은 오브젝트(또는 자식)에 부착.
    [RequireComponent(typeof(RelicManager))]
    public class RelicInputHandler : MonoBehaviour
    {
        [SerializeField] private KeyCode slot0Key = KeyCode.Q;
        [SerializeField] private KeyCode slot1Key = KeyCode.R;

        private RelicManager _manager;

        private void Awake() => _manager = GetComponent<RelicManager>();

        private void Update()
        {
            if (UIStateManager.Instance != null &&
                UIStateManager.Instance.CurrentState != UIState.None)
                return;

            if (Input.GetKeyDown(slot0Key)) _manager.ActivateSlot(0);
            if (Input.GetKeyDown(slot1Key)) _manager.ActivateSlot(1);
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 3: 체크인 (사람, UVCS)** — "feat(relic): RelicInputHandler (Q/R)".

---

## Task 12: 저장 연동 (PlayerData + SaveManager)

**Files:**
- Modify: `Assets/Scripts/UI/Player/PlayerData.cs` (relicSave 필드)
- Modify: `Assets/Scripts/_Core/Managers/SaveManager.cs` (save/load 분기)
- Modify: `Assets/Scripts/Gameplay/Relics/Core/RelicManager.cs` (CaptureSaveData/ApplySaveData)

**Interfaces:**
- Consumes: `RelicSaveData`(Task 2), `RelicInventory.ToSaveData/LoadFrom`(Task 7)
- Produces: `PlayerData.relicSave`; `RelicManager.CaptureSaveData()`/`ApplySaveData(RelicSaveData)`

- [ ] **Step 1: PlayerData에 필드 추가**

`Assets/Scripts/UI/Player/PlayerData.cs`의 `coinSave` 선언(`:53-54` 부근) 아래에 추가:

```csharp
    // Relic System
    [Header("Relic")] public Relic.Data.RelicSaveData relicSave = new Relic.Data.RelicSaveData();
```

- [ ] **Step 2: RelicManager에 Capture/Apply 추가 (CoinGameManager 패턴)**

`RelicManager`(Task 8)에 메서드 추가:

```csharp
        public RelicData.RelicSaveData CaptureSaveData() => Inventory.ToSaveData();

        public void ApplySaveData(RelicData.RelicSaveData data)
        {
            if (data == null || !data.hasData) return;

            Inventory.LoadFrom(data);

            // 슬롯 수 변경 반영
            int n = Inventory.SlotCount;
            if (_slotBehaviour == null || _slotBehaviour.Length != n)
            {
                _slotBehaviour = new RelicBehaviour[n];
                _slotActive = new ActiveRelicState[n];
                _spawnOwnerBySlot = new RelicID[n];
            }

            // 로드된 로드아웃대로 재장착 (behavior clone + OnEquip)
            for (int i = 0; i < n; i++)
            {
                var id = Inventory.GetEquipped(i);
                // Inventory엔 이미 반영됐으므로 behavior만 재구성
                RebuildSlot(i, id);
            }
        }

        // Inventory 상태는 그대로 두고 슬롯 behavior만 재구성 (중복 Equip 방지)
        private void RebuildSlot(int slot, RelicID id)
        {
            var prev = _slotBehaviour[slot];
            if (prev != null) { prev.OnUnequip(); Despawn(_spawnOwnerBySlot[slot]); }
            _slotBehaviour[slot] = null;
            _slotActive[slot] = null;
            _spawnOwnerBySlot[slot] = RelicID.None;

            if (id == RelicID.None) return;
            var so = RelicDatabase.Instance != null ? RelicDatabase.Instance.GetRelicByID(id) : null;
            if (so == null || so.behaviour == null) return;

            var behaviour = so.behaviour.Clone();
            behaviour.OnEquip(_ctx, Mathf.Max(1, Inventory.GetLevel(id)));
            _slotBehaviour[slot] = behaviour;
            _spawnOwnerBySlot[slot] = id;
            _slotActive[slot] = (so.type == RelicType.Active) ? new ActiveRelicState() : null;
        }
```

> `RelicData`는 `Relic.Data` 별칭 대신 완전수식 사용. 파일 상단에 `using RelicData = Relic.Data;` 별칭을
> 추가하거나, `Relic.Data.RelicSaveData`로 직접 표기. (이 계획에선 `Relic.Data.RelicSaveData` 직접 표기 권장 —
> 위 코드의 `RelicData.RelicSaveData`를 `Data.RelicSaveData`로 바꿔라. RelicManager는 이미 `namespace Relic`
> 안이므로 `Data.RelicSaveData`로 접근 가능.)

수정 지침: 위 메서드의 `RelicData.RelicSaveData` → `Data.RelicSaveData`로 표기(같은 `Relic` 네임스페이스 하위).

- [ ] **Step 3: SaveManager 저장 분기 추가**

`SaveManager.cs`의 코인 저장 블록(`:428-437` 부근) **아래**에 추가:

```csharp
            // 유물 시스템 데이터 저장
            var relicMgr = FindFirstObjectByType<Relic.RelicManager>();
            if (relicMgr != null)
            {
                data.relicSave = relicMgr.CaptureSaveData();
            }
            else if (data.relicSave == null)
            {
                data.relicSave = new Relic.Data.RelicSaveData();
            }
```

- [ ] **Step 4: SaveManager 복원 분기 추가**

`SaveManager.cs`의 코인 복원 블록(`:544-548` 부근) **아래**에 추가:

```csharp
            // 유물 시스템 데이터 복원
            var relicMgrLoad = FindFirstObjectByType<Relic.RelicManager>();
            if (relicMgrLoad != null && data.relicSave != null && data.relicSave.hasData)
            {
                relicMgrLoad.ApplySaveData(data.relicSave);
            }
```

- [ ] **Step 5: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 6: 체크인 (사람, UVCS)** — "feat(relic): save/load integration".

---

## Task 13: 슬라이스 유물 ① StatRelicBehaviour (패시브-스탯)

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Behaviours/StatRelicBehaviour.cs`

**Interfaces:**
- Consumes: `RelicBehaviour`(Task 3), `RelicContext.statProvider`(Task 3/5), `StatType`/`ModifierType`(전역), `RelicID`(Task 1)
- Produces: `Relic.StatRelicBehaviour` — 기존 StatType에 레벨별 % modifier를 얹는 패시브

- [ ] **Step 1: StatRelicBehaviour 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Behaviours/StatRelicBehaviour.cs
using System;
using UnityEngine;
using Relic.Data;

namespace Relic
{
    // 슬라이스 검증용 패시브-스탯 유물. 인스펙터에서 statType·레벨별 값 지정.
    [Serializable]
    public class StatRelicBehaviour : RelicBehaviour
    {
        [SerializeField] private RelicID keyId = RelicID.TestStatRelic; // 고유 키(중복 방지)
        [SerializeField] private StatType statType = StatType.MiningRange;
        [SerializeField] private ModifierType modifierType = ModifierType.Percent;
        [SerializeField] private float[] valuePerLevel = { 1.1f, 1.2f, 1.3f };

        private string Key => $"relic:{keyId}:stat";

        private void Apply(int lv)
        {
            int idx = Mathf.Clamp(lv - 1, 0, valuePerLevel.Length - 1);
            ctx.statProvider.Set(Key, statType, modifierType, valuePerLevel[idx]);
        }

        public override void OnEquip(RelicContext c, int lv)
        {
            base.OnEquip(c, lv);
            Apply(lv);
        }

        public override void OnLevelChanged(int lv)
        {
            base.OnLevelChanged(lv);
            Apply(lv);
        }

        public override void OnUnequip()
        {
            ctx?.statProvider?.Clear(Key);
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 3: 체크인 (사람, UVCS)** — "feat(relic): StatRelicBehaviour (passive stat slice)".

---

## Task 14: 슬라이스 유물 ② DoubleJumpRelic (비둘기 깃털)

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Behaviours/DoubleJumpRelic.cs`

**Interfaces:**
- Consumes: `RelicBehaviour`(Task 3) — `TryConsumeAirJump`/`OnLanded` override
- Produces: `Relic.DoubleJumpRelic` — 착지 후 1회 공중 점프 허용(레벨별 추가 점프 수)

- [ ] **Step 1: DoubleJumpRelic 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Behaviours/DoubleJumpRelic.cs
using System;
using UnityEngine;

namespace Relic
{
    // 비둘기 깃털: 공중에서 추가 점프. 레벨별 추가 점프 횟수.
    [Serializable]
    public class DoubleJumpRelic : RelicBehaviour
    {
        [SerializeField] private int[] airJumpsPerLevel = { 1, 1, 2 };

        private int _airJumpsUsed;

        private int MaxAirJumps
        {
            get
            {
                int idx = Mathf.Clamp(level - 1, 0, airJumpsPerLevel.Length - 1);
                return airJumpsPerLevel[idx];
            }
        }

        public override void OnLanded()
        {
            _airJumpsUsed = 0; // 착지 시 리셋
        }

        public override bool TryConsumeAirJump()
        {
            if (_airJumpsUsed >= MaxAirJumps) return false;
            _airJumpsUsed++;
            return true;
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 3: 체크인 (사람, UVCS)** — "feat(relic): DoubleJumpRelic (pigeon feather slice)".

---

## Task 15: 슬라이스 유물 ③ MagnetRelic (액티브)

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Behaviours/MagnetRelic.cs`

**Interfaces:**
- Consumes: `RelicBehaviour`(Task 3), `RelicContext.runner`(코루틴/스폰), `RelicContext.player`
- Produces: `Relic.MagnetRelic` — 발동 시 duration 동안 주변 Rigidbody2D 아이템을 플레이어로 흡인

> 슬라이스 범위: **물리 흡인만** 검증(입력→쿨타임→상태기계→코루틴 전 경로). 아이템 "가치 증가"는
> 아이템 경제 API 확정 후 후속(설계 §8). LayerMask로 흡인 대상 지정.

- [ ] **Step 1: MagnetRelic 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Behaviours/MagnetRelic.cs
using System;
using System.Collections;
using UnityEngine;

namespace Relic
{
    // 자석: 발동 시 duration 동안 반경 내 Rigidbody2D 아이템을 플레이어로 끌어당김.
    [Serializable]
    public class MagnetRelic : RelicBehaviour
    {
        [SerializeField] private float[] radiusPerLevel   = { 4f, 5f, 6f };
        [SerializeField] private float[] durationPerLevel = { 2f, 2.5f, 3f };
        [SerializeField] private float[] cooldownPerLevel = { 8f, 7f, 6f };
        [SerializeField] private float   pullForce = 20f;
        [SerializeField] private LayerMask itemMask = ~0; // 인스펙터에서 아이템 레이어 지정

        private float Lv(float[] arr) => arr[Mathf.Clamp(level - 1, 0, arr.Length - 1)];

        public override float GetDuration() => Lv(durationPerLevel);
        public override float GetCooldown() => Lv(cooldownPerLevel);

        public override void OnActivate()
        {
            if (ctx?.runner != null)
                ctx.runner.StartCoroutine(PullRoutine());
        }

        private IEnumerator PullRoutine()
        {
            float dur = GetDuration();
            float r = Lv(radiusPerLevel);
            float t = 0f;
            var buffer = new Collider2D[32];

            while (t < dur)
            {
                if (ctx?.player == null) yield break;
                Vector2 center = ctx.player.position;

                int count = Physics2D.OverlapCircleNonAlloc(center, r, buffer, itemMask);
                for (int i = 0; i < count; i++)
                {
                    var col = buffer[i];
                    if (col == null) continue;
                    var rb = col.attachedRigidbody;
                    if (rb == null) continue;
                    if (rb.transform == ctx.player) continue;

                    Vector2 dir = (center - (Vector2)rb.position).normalized;
                    rb.AddForce(dir * pullForce, ForceMode2D.Force);
                }

                t += Time.deltaTime;
                yield return null;
            }
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 3: 체크인 (사람, UVCS)** — "feat(relic): MagnetRelic (active slice)".

---

## Task 16: 퀵슬롯 UI (RelicQuickslotUI)

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/UI/RelicQuickslotUI.cs`

**Interfaces:**
- Consumes: `RelicManager`(Task 8) — `GetSlotPhase`/`GetSlotTimer`/`Inventory`, `RelicDatabase`(아이콘)
- Produces: `Relic.RelicQuickslotUI` — 슬롯별 아이콘 + 액티브 쿨타임/지속 표시

- [ ] **Step 1: RelicQuickslotUI 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/UI/RelicQuickslotUI.cs
using UnityEngine;
using UnityEngine.UI;
using Relic.Data;

namespace Relic
{
    // 퀵슬롯 아이콘 + 쿨타임/지속 오버레이. 슬롯당 아이콘 Image + fill Image 필요.
    public class RelicQuickslotUI : MonoBehaviour
    {
        [System.Serializable]
        public class SlotView
        {
            public Image icon;      // 유물 아이콘
            public Image fill;      // radial(쿨타임) / 지속 게이지. type=Filled 권장
            public GameObject readyMark; // 선택: Ready 표시
        }

        [SerializeField] private RelicManager manager;
        [SerializeField] private SlotView[] slots;

        private void Start()
        {
            if (manager == null) manager = FindFirstObjectByType<RelicManager>();
            RefreshIcons();
            if (manager != null) manager.Inventory.OnChanged += RefreshIcons;
        }

        private void OnDestroy()
        {
            if (manager != null) manager.Inventory.OnChanged -= RefreshIcons;
        }

        private void RefreshIcons()
        {
            if (manager == null || slots == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                var id = manager.Inventory.GetEquipped(i);
                var so = (RelicDatabase.Instance != null) ? RelicDatabase.Instance.GetRelicByID(id) : null;
                if (slots[i].icon != null)
                {
                    slots[i].icon.enabled = so != null;
                    if (so != null) slots[i].icon.sprite = so.icon;
                }
            }
        }

        private void Update()
        {
            if (manager == null || slots == null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                var fill = slots[i].fill;
                if (fill == null) continue;

                var phase = manager.GetSlotPhase(i);
                float timer = manager.GetSlotTimer(i);

                switch (phase)
                {
                    case RelicPhase.Cooldown:
                        // timer = 남은 쿨타임. 정규화는 UI 근사(정확 총량 필요시 Manager 확장)
                        fill.fillAmount = Mathf.Clamp01(timer / 10f);
                        fill.enabled = true;
                        break;
                    case RelicPhase.Active:
                        fill.enabled = true;
                        fill.fillAmount = 1f; // 지속 중 강조
                        break;
                    default: // Ready
                        fill.enabled = false;
                        break;
                }

                if (slots[i].readyMark != null)
                    slots[i].readyMark.SetActive(phase == RelicPhase.Ready
                        && manager.Inventory.GetEquipped(i) != RelicID.None);
            }
        }
    }
}
```

> 쿨타임 fillAmount 정규화가 근사(÷10)다. 정확 표시가 필요하면 `RelicManager`에 `GetSlotCooldownTotal(i)`를
> 추가해 분모로 쓰도록 후속 확장(설계 §8 UI 항목).

- [ ] **Step 2: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 3: 체크인 (사람, UVCS)** — "feat(relic): RelicQuickslotUI".

---

## Task 17: 디버그 지급 + 에디터 셋업 + end-to-end 검증

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Debug/RelicDebugGranter.cs`

**Interfaces:**
- Consumes: `RelicManager`(Task 8), `RelicID`(Task 1)
- Produces: 치트키로 슬라이스 3종 지급·장착 → 실제 게임에서 검증

- [ ] **Step 1: RelicDebugGranter 생성**

```csharp
// Assets/Scripts/Gameplay/Relics/Debug/RelicDebugGranter.cs
using UnityEngine;
using Relic.Data;

namespace Relic
{
    // 슬라이스 검증용 치트: F6=3종 지급+장착, F7=Magnet Lv+1.
    public class RelicDebugGranter : MonoBehaviour
    {
        [SerializeField] private RelicManager manager;

        private void Start()
        {
            if (manager == null) manager = FindFirstObjectByType<RelicManager>();
        }

        private void Update()
        {
            if (manager == null) return;

            if (Input.GetKeyDown(KeyCode.F6))
            {
                manager.Inventory.Grant(RelicID.PigeonFeather);
                manager.Inventory.Grant(RelicID.Magnet);
                manager.EquipSlot(0, RelicID.PigeonFeather); // 패시브
                manager.EquipSlot(1, RelicID.Magnet);        // 액티브(R)
                Debug.Log("[Relic] 슬라이스 지급: 슬롯0=비둘기깃털, 슬롯1=자석");
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (manager.Inventory.TryUpgrade(RelicID.Magnet, 3))
                {
                    manager.RefreshLevel(RelicID.Magnet);
                    Debug.Log($"[Relic] Magnet Lv{manager.Inventory.GetLevel(RelicID.Magnet)}");
                }
            }
        }
    }
}
```

- [ ] **Step 2: 에디터 에셋 셋업 (사람)**

Unity 에디터에서:
1. `Game Data/Relic SO`로 유물 3종 에셋 생성:
   - `TestStatRelic`: id=TestStatRelic, type=Passive, behaviour=`StatRelicBehaviour`(statType=MiningRange, valuePerLevel=1.1/1.2/1.3)
   - `PigeonFeather`: id=PigeonFeather, type=Passive, behaviour=`DoubleJumpRelic`(airJumpsPerLevel=1/1/2)
   - `Magnet`: id=Magnet, type=Active, behaviour=`MagnetRelic`(radius/duration/cooldown 기본값, itemMask=아이템 레이어)
   - 각 `upgradeCosts` 2칸(Lv1→2, Lv2→3): gold + material(예: 임의 광물 ItemID) 지정
2. `Database/Relic Database` 에셋 생성 → `allRelics`에 3종 등록.
3. 플레이어 프리팹(또는 씬 플레이어)에 `RelicManager` + `RelicInputHandler` + `RelicDebugGranter` 부착.
4. `RelicQuickslotUI`를 HUD에 배치하고 slots 2개(icon/fill Image) 연결, manager 참조 연결.
5. `MagnetRelic.itemMask`가 실제 월드 아이템(예: MineralDug 오브젝트) 레이어를 포함하는지 확인.

- [ ] **Step 3: end-to-end 검증 (사람, Play 모드)**

1. Play → F6 → 퀵슬롯에 아이콘 2개 표시 확인.
2. **비둘기 깃털(패시브)**: 공중에서 점프키 → 이단 점프 발동. 착지 후 리셋되어 다시 이단 점프 가능.
3. **자석(액티브, R키)**: 근처에 아이템 놓고 R → duration 동안 아이템이 플레이어로 끌려옴. 발동 직후 R
   재입력 → 쿨타임 중 무반응(상태기계 차단). 퀵슬롯 fill이 쿨타임 표시.
4. **테스트 스탯**: 슬롯 교체해 TestStatRelic 장착 → `StatDiagnosticPanel`(있으면)에서 MiningRange에
   Relic 소스 % modifier 확인, 또는 실제 파기 범위 증가 체감.
5. **F7**: Magnet Lv 상승 → 다음 발동 시 반경/지속 증가 체감.
6. **저장/로드**: 저장 → 재시작/로드 → 소유·레벨·로드아웃 유지 확인.

- [ ] **Step 4: EditMode 테스트 전체 재실행 (사람)** — Task 3~7 테스트 전부 PASS 확인.

- [ ] **Step 5: 체크인 (사람, UVCS)** — "feat(relic): debug granter + slice wiring; vertical slice complete".

---

## Self-Review 결과 (계획 작성자 확인)

- **Spec 커버리지**: design.md §2(5기둥) → Task 1~12, §3(저장) → Task 2/12, §4(액티브 상태기계) →
  Task 4/8, §5(입력·퀵슬롯) → Task 11/16, §7(통합 6지점) → Task 1(ModifierSource)/9(점프착지)/10(dig)/
  11(입력)/12(저장), §9(슬라이스 3종) → Task 13/14/15/17. **§6(상점 강화 UI)는 이 슬라이스 계획에서
  디버그 지급(F7)으로 대체** — 정식 상점 UI는 후속 계획으로 분리(design §8 미결과 정합).
- **플레이스홀더 스캔**: 모든 코드 스텝에 완전한 코드 포함. TODO/TBD 없음. (Task 15의 "가치 증가"와
  Task 16의 fill 정규화 근사는 설계 §8 후속으로 **명시적 분리**, 플레이스홀더 아님.)
- **타입 일관성**: `RelicBehaviour`/`RelicContext`/`ActiveRelicState`/`RelicManager`/`RelicInventory`
  시그니처가 태스크 간 일치. `ApplyDigModifiers`/`ModifyDigParameters`/`TryConsumeAirJump`/`NotifyLanded`
  명명 통일. **주의사항**: Task 12의 `RelicData.RelicSaveData` 표기는 `Data.RelicSaveData`로 정정하라고
  Step 2에 명시(같은 `Relic` 네임스페이스 하위 접근).
- **후속 계획(별도)**: 정식 상점 강화 UI, 월드 스폰 파이프라인, 슬롯 확장 트리거, 나머지 유물 다수,
  기존 EquipmentType.Relic 슬롯 정리(design §8).
