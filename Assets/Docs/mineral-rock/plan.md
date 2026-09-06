# 발광 광물돌 (Mineral Rock) 구현 플랜

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (권장) 또는 superpowers:executing-plans 로 task 단위 구현. 스텝은 체크박스(`- [ ]`)로 추적.

**Goal:** 특정 광물 전용 + 어두운 동굴에서 파티클로 자체발광하는 채굴 가능한 돌을, 기존 `DiggableRock` 재사용 + 컴포넌트 합성으로 만든다.

**Architecture:** `DiggableRock`에 `IRockDropOverride` 훅 1곳만 추가하고, 광물돌 고유 동작은 신규 `MineralRock`(정체성·확정드롭·JSON 적용) + `MineralRockGlow`(항상발광 스파클) 컴포넌트로 합성한다. HP·드롭 개수는 `specialChunkSettings.json`에서 광물별 조정.

**Tech Stack:** Unity 2D (URP), C#, NUnit EditMode, JsonUtility, 특수청크 시스템.

## Global Constraints

- **테스트 실행 금지:** Claude는 테스트 파일을 **작성만** 한다. `mcp__mcp-unity__run_tests` 등 Unity 테스트 실행 도구를 호출하지 않는다. 실행은 사람이 Unity Test Runner에서 직접 한다. (CLAUDE.md)
- **git 명령 금지:** 이 프로젝트는 UVCS를 쓴다. `git commit/diff/log` 등을 호출하지 않는다. 각 task 끝의 "체크포인트"는 **컴파일 통과 확인** 후 사용자가 UVCS로 체크인하는 지점이다.
- **EditMode 테스트 패턴:** MonoBehaviour 없이 순수 C# 로직만 NUnit `[Test]`로 검증한다. Instantiate/싱글톤/파티클 의존 코드는 단위 테스트하지 않고 수동 플레이로 검증한다. (`Assets/Tests/EditMode/` 참고)
- **테스트 어셈블리:** `Assets/Tests/EditMode/EditModeTests.asmdef` 가 `GameScripts`를 참조. 신규 테스트는 이 폴더에 둔다.
- **파일 검색:** 코드 위치 탐색 시 `@tags:` 패턴 우선 사용.
- **프리팹 배치 제약(#8/#9):** 특수청크 자식은 Prefab Edit 모드에서 배치. 광물돌은 `preExposed=true`, `minExposedPixels=0`, Rigidbody2D 없음.

---

## 파일 구조

**신규 코드**
- `Assets/Scripts/_Core/Interfaces/IRockDropOverride.cs` — 드롭 오버라이드 인터페이스
- `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralDropHelper.cs` — 광물 인스턴스화 공용 static 헬퍼
- `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralRock.cs` — 정체성 + 확정드롭 + JSON 적용
- `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralRockGlow.cs` — 항상발광 스파클 구동

**수정 코드**
- `Assets/Scripts/_Core/Data/SpecialChunkSettingsData.cs` — `MineralRockSection`/`MineralRockEntry`/`ResolvedMineralRock` 추가
- `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/DiggableRock.cs` — DestroyRock 드롭 훅 + TryDropMineral을 헬퍼로 위임
- `Assets/StreamingAssets/specialChunkSettings.json` — `mineralRock` 섹션 추가

**신규 테스트**
- `Assets/Tests/EditMode/MineralRockSettingsTests.cs` — `MineralRockSection.Resolve()` 검증

**신규 에셋(사람이 에디터에서)**
- Additive 파티클 머티리얼, 광물돌 프리팹, `Assets/Docs/mineral-rock/prefab-setup.md`

---

## Task 1: JSON 설정 — MineralRockSection (TDD)

가장 순수하게 단위 테스트 가능한 부분을 먼저 한다. HP·드롭 개수의 광물별 조정 + default 폴백 로직.

**Files:**
- Modify: `Assets/Scripts/_Core/Data/SpecialChunkSettingsData.cs` (클래스 추가, 파일 끝 `PhysicsSection` 다음)
- Modify: `Assets/StreamingAssets/specialChunkSettings.json` (`mineralRock` 섹션 추가)
- Test: `Assets/Tests/EditMode/MineralRockSettingsTests.cs`

**Interfaces:**
- Produces:
  - `SpecialChunkSettingsData.MineralRockSection` — 필드 `float defaultMaxHp; int defaultMinDrop; int defaultMaxDrop; List<MineralRockEntry> overrides;`
  - `ResolvedMineralRock Resolve(string mineralID)` — 항상 non-null, 매칭 없으면 default값
  - `struct ResolvedMineralRock { public float maxHp; public int minDrop; public int maxDrop; }`
  - `class MineralRockEntry { public string mineralID; public float maxHp; public int minDrop; public int maxDrop; }`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/MineralRockSettingsTests.cs`:

```csharp
using NUnit.Framework;
using System.Collections.Generic;

/// <summary>
/// MineralRockSection.Resolve() — 광물별 오버라이드 적중 / 미적중(default 폴백) 검증.
/// 순수 로직만 테스트 (MonoBehaviour 없음).
/// </summary>
public class MineralRockSettingsTests
{
    private SpecialChunkSettingsData.MineralRockSection MakeSection()
    {
        return new SpecialChunkSettingsData.MineralRockSection
        {
            defaultMaxHp   = 8f,
            defaultMinDrop = 2,
            defaultMaxDrop = 4,
            overrides = new List<SpecialChunkSettingsData.MineralRockEntry>
            {
                new SpecialChunkSettingsData.MineralRockEntry
                    { mineralID = "Copper", maxHp = 6f, minDrop = 2, maxDrop = 4 },
                new SpecialChunkSettingsData.MineralRockEntry
                    { mineralID = "Iron",   maxHp = 10f, minDrop = 1, maxDrop = 3 },
            }
        };
    }

    [Test]
    public void Resolve_KnownMineral_ReturnsOverride()
    {
        var r = MakeSection().Resolve("Iron");
        Assert.AreEqual(10f, r.maxHp);
        Assert.AreEqual(1, r.minDrop);
        Assert.AreEqual(3, r.maxDrop);
    }

    [Test]
    public void Resolve_UnknownMineral_ReturnsDefaults()
    {
        var r = MakeSection().Resolve("Gold");
        Assert.AreEqual(8f, r.maxHp);
        Assert.AreEqual(2, r.minDrop);
        Assert.AreEqual(4, r.maxDrop);
    }

    [Test]
    public void Resolve_EmptyId_ReturnsDefaults()
    {
        var r = MakeSection().Resolve("");
        Assert.AreEqual(8f, r.maxHp);
    }

    [Test]
    public void Resolve_CachesDictionary_SecondCallSameResult()
    {
        var section = MakeSection();
        var first  = section.Resolve("Copper");
        var second = section.Resolve("Copper");
        Assert.AreEqual(first.maxHp, second.maxHp);
        Assert.AreEqual(6f, second.maxHp);
    }
}
```

- [ ] **Step 2: 테스트가 실패(컴파일 에러)하는지 확인 — 사람이 실행**

Unity Test Runner(EditMode) 실행. 기대: `MineralRockSection`/`Resolve` 미정의로 **컴파일 실패**.

- [ ] **Step 3: 최소 구현 — SpecialChunkSettingsData.cs에 클래스 추가**

`PhysicsSection` 클래스 닫는 `}` 다음, `SpecialChunkSettingsData` 클래스 내부 끝에 추가:

```csharp
    // ─── Mineral Rock (발광 광물돌) ──────────────────────────────
    public MineralRockSection mineralRock = new MineralRockSection();

    [System.Serializable]
    public class MineralRockSection
    {
        public float defaultMaxHp   = 8f;
        public int   defaultMinDrop = 2;
        public int   defaultMaxDrop = 4;
        public List<MineralRockEntry> overrides = new List<MineralRockEntry>();

        // spawnChances와 동일 패턴: List → Dictionary 1회 변환
        private Dictionary<string, MineralRockEntry> _dict;

        public ResolvedMineralRock Resolve(string mineralID)
        {
            if (_dict == null)
            {
                _dict = new Dictionary<string, MineralRockEntry>(overrides.Count);
                foreach (var e in overrides)
                    if (!string.IsNullOrEmpty(e.mineralID))
                        _dict[e.mineralID] = e;
            }

            if (!string.IsNullOrEmpty(mineralID) && _dict.TryGetValue(mineralID, out var entry))
                return new ResolvedMineralRock
                    { maxHp = entry.maxHp, minDrop = entry.minDrop, maxDrop = entry.maxDrop };

            return new ResolvedMineralRock
                { maxHp = defaultMaxHp, minDrop = defaultMinDrop, maxDrop = defaultMaxDrop };
        }
    }

    [System.Serializable]
    public class MineralRockEntry
    {
        public string mineralID;
        public float  maxHp;
        public int    minDrop;
        public int    maxDrop;
    }

    public struct ResolvedMineralRock
    {
        public float maxHp;
        public int   minDrop;
        public int   maxDrop;
    }
```

- [ ] **Step 4: 테스트 통과 확인 — 사람이 실행**

Unity Test Runner(EditMode). 기대: `MineralRockSettingsTests` 4개 **PASS**.

- [ ] **Step 5: JSON에 mineralRock 섹션 추가**

`Assets/StreamingAssets/specialChunkSettings.json` 의 `"physics"` 블록 다음(최상위), 닫는 `}` 직전에 추가 (앞 항목 끝에 콤마 추가 주의):

```json
  "mineralRock": {
    "defaultMaxHp": 8.0,
    "defaultMinDrop": 2,
    "defaultMaxDrop": 4,
    "overrides": [
      { "mineralID": "Copper", "maxHp": 6.0,  "minDrop": 2, "maxDrop": 4 },
      { "mineralID": "Iron",   "maxHp": 10.0, "minDrop": 1, "maxDrop": 3 }
    ]
  }
```

- [ ] **Step 6: 체크포인트**

컴파일 통과 + EditMode 테스트 PASS 확인 후 사용자가 UVCS 체크인.

---

## Task 2: 드롭 로직 추출 + 오버라이드 훅 (DiggableRock 재사용 기반)

광물 인스턴스화를 공용 헬퍼로 추출(DRY)하고, `DiggableRock`에 드롭 오버라이드 훅을 심는다.
Instantiate·싱글톤 의존이라 단위 테스트 불가 → 컴파일 + 기존 드롭 동작 수동 검증.

**Files:**
- Create: `Assets/Scripts/_Core/Interfaces/IRockDropOverride.cs`
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralDropHelper.cs`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/DiggableRock.cs` (DestroyRock ~350행, TryDropMineral ~419-459행)

**Interfaces:**
- Produces:
  - `interface IRockDropOverride { bool TryDropOnDestroy(Vector3 rockCenter); }`
  - `static class MineralDropHelper` — `void Drop(MineralSO so, Vector3 worldPos)`, `void Drop(MineralID id, Vector3 worldPos)`
- Consumes: `MineralSO`(필드 `mineralPrefab`), `MineralDatabase.Instance.GetMineralByID(MineralID)`, `MineralLifetime`, `MineralID` (모두 기존)

- [ ] **Step 1: IRockDropOverride 인터페이스 생성**

`Assets/Scripts/_Core/Interfaces/IRockDropOverride.cs`:

```csharp
using UnityEngine;

/// <summary>
/// DiggableRock의 기본 랜덤 드롭을 대체하고 싶은 컴포넌트가 구현한다.
/// DiggableRock.DestroyRock()이 파괴 시 이 인터페이스를 조회한다.
/// </summary>
public interface IRockDropOverride
{
    /// <summary>드롭을 처리했으면 true. true면 DiggableRock의 기본 드롭(DropRareMinerals)을 건너뛴다.</summary>
    bool TryDropOnDestroy(Vector3 rockCenter);
}
```

- [ ] **Step 2: MineralDropHelper 생성**

`Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralDropHelper.cs`:

```csharp
// @tags: mineral, drop, helper, spawn
using UnityEngine;

/// <summary>
/// 월드에 광물 픽업 오브젝트를 1개 생성하고 MineralLifetime(자동소멸)을 부착하는 공용 헬퍼.
/// DiggableRock의 랜덤 드롭과 MineralRock의 확정 드롭이 공유한다 (DRY).
/// </summary>
public static class MineralDropHelper
{
    /// <summary>MineralSO 직접 드롭.</summary>
    public static void Drop(MineralSO so, Vector3 worldPos)
    {
        if (so == null) { Debug.LogWarning("[MineralDropHelper] MineralSO 가 null"); return; }
        if (so.mineralPrefab == null) { Debug.LogWarning($"[MineralDropHelper] mineralPrefab 없음: {so.name}"); return; }

        GameObject spawned = Object.Instantiate(so.mineralPrefab, worldPos, Quaternion.identity);
        if (spawned != null)
            spawned.AddComponent<MineralLifetime>();
    }

    /// <summary>MineralID로 MineralDatabase에서 조회 후 드롭.</summary>
    public static void Drop(MineralID id, Vector3 worldPos)
    {
        if (MineralDatabase.Instance == null) { Debug.LogWarning("[MineralDropHelper] MineralDatabase.Instance 가 null"); return; }
        Drop(MineralDatabase.Instance.GetMineralByID(id), worldPos);
    }
}
```

- [ ] **Step 3: DiggableRock.TryDropMineral을 헬퍼로 위임**

`DiggableRock.cs`의 `TryDropMineral`(419-459행) 본문을 교체. id 파싱 후 dropPos 계산까지는 유지하고, 인스턴스화/생명타이머 부분만 헬퍼로 위임:

```csharp
    private void TryDropMineral(string typeName)
    {
        if (!System.Enum.TryParse(typeName, true, out MineralID id))
        {
            Debug.LogWarning($"  [TryDrop] MineralID 파싱 실패: {typeName}");
            return;
        }

        // 스프라이트의 실제 시각적 중심에서 드롭 (pivot 위치와 무관)
        Vector3 rockCenter = _spriteRenderer != null
            ? _spriteRenderer.bounds.center
            : new Vector3(transform.position.x, transform.position.y, 0f);

        Vector3 dropPos = new Vector3(
            rockCenter.x + Random.Range(-0.05f, 0.05f),
            rockCenter.y,
            -1f
        );

        MineralDropHelper.Drop(id, dropPos);
    }
```

- [ ] **Step 4: DiggableRock.DestroyRock에 드롭 오버라이드 훅 추가**

`DestroyRock()`(322-354행)에서 `DropRareMinerals();` 한 줄(352행)을 다음으로 교체:

```csharp
        // 드롭 오버라이드(광물돌 등)가 있으면 위임, 없으면 기존 랜덤 드롭
        var dropOverride = GetComponent<IRockDropOverride>();
        if (dropOverride == null || !dropOverride.TryDropOnDestroy(_spriteRenderer.bounds.center))
            DropRareMinerals();
```

- [ ] **Step 5: 컴파일 + 기존 동작 회귀 확인 — 사람이 수동 검증**

Unity 컴파일 에러 없음 확인. 기존 일반 바위를 곡괭이/드릴로 파괴 → 종전과 동일하게 랜덤 희귀 광물 드롭되는지 플레이 확인 (오버라이드 미부착 시 경로 불변).

- [ ] **Step 6: 체크포인트**

컴파일 통과 + 회귀 확인 후 UVCS 체크인.

---

## Task 3: MineralRock 컴포넌트 (정체성 + 확정드롭 + JSON 적용)

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralRock.cs`

**Interfaces:**
- Consumes: `IRockDropOverride`(Task2), `MineralDropHelper.Drop(MineralSO, Vector3)`(Task2), `SpecialChunkSettingsData.MineralRockSection.Resolve(string)`(Task1), `SpecialChunkSettingsLoader.Instance.Settings`(기존), `DiggableRock`(필드 `public float MaxHp`, 기존), `MineralSO`(필드 `mineralID`)
- Produces: `MineralRock` 컴포넌트 (DiggableRock와 같은 GameObject에 부착)

- [ ] **Step 1: MineralRock 작성**

`Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralRock.cs`:

```csharp
// @tags: rock, mineral, drop, ore, decoration, special-chunk
using UnityEngine;

/// <summary>
/// "광물돌"의 정체성 컴포넌트. DiggableRock과 같은 GameObject에 부착한다.
///  - 이 돌이 어떤 광물(ore)인지 보유
///  - 파괴 시 그 광물을 확정 개수 드롭 (IRockDropOverride)
///  - specialChunkSettings.json의 mineralRock 설정으로 HP·드롭 개수를 광물별 주입
/// 발광색은 MineralRockGlow가 따로 보유한다 (SRP).
/// Loader(-150) 다음, DiggableRock.OnEnable(_currentHp=MaxHp) 전에 MaxHp를 세팅하기 위해 -140.
/// </summary>
[DefaultExecutionOrder(-140)]
[RequireComponent(typeof(DiggableRock))]
public class MineralRock : MonoBehaviour, IRockDropOverride
{
    [Header("정체성")]
    [Tooltip("이 광물돌이 드롭하는 광물")]
    [SerializeField] private MineralSO ore;

    private int _minDrop = 2;
    private int _maxDrop = 4;

    private void Awake()
    {
        var section = (SpecialChunkSettingsLoader.Instance != null)
            ? SpecialChunkSettingsLoader.Instance.Settings.mineralRock
            : new SpecialChunkSettingsData.MineralRockSection();

        string id = ore != null ? ore.mineralID.ToString() : "";
        var cfg = section.Resolve(id);

        _minDrop = cfg.minDrop;
        _maxDrop = cfg.maxDrop;

        // DiggableRock.OnEnable()이 이후 _currentHp = MaxHp 로 초기화 → 순서 안전
        var rock = GetComponent<DiggableRock>();
        if (rock != null) rock.MaxHp = cfg.maxHp;
    }

    // IRockDropOverride — DiggableRock.DestroyRock()에서 호출
    public bool TryDropOnDestroy(Vector3 rockCenter)
    {
        if (ore == null) return false; // 광물 미지정 → 기본 드롭으로 폴백

        int count = Mathf.Max(0, Random.Range(_minDrop, _maxDrop + 1));
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = new Vector3(
                rockCenter.x + Random.Range(-0.05f, 0.05f),
                rockCenter.y,
                -1f);
            MineralDropHelper.Drop(ore, pos);
        }
        return true;
    }
}
```

- [ ] **Step 2: 컴파일 확인 — 사람이 검증**

Unity 컴파일 에러 없음 확인. (단위 테스트는 Instantiate/싱글톤 의존이라 생략 — Task 5 수동 플레이로 검증)

- [ ] **Step 3: 체크포인트**

컴파일 통과 후 UVCS 체크인.

---

## Task 4: MineralRockGlow 컴포넌트 (항상발광 스파클 + 노출 동기화)

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralRockGlow.cs`

**Interfaces:**
- Consumes: `SpriteRenderer.enabled`(같은 GameObject, DiggableRock이 토글), `UnityEngine.ParticleSystem`
- Produces: `MineralRockGlow` 컴포넌트 (DiggableRock와 같은 GameObject, 자식 ParticleSystem 참조)

- [ ] **Step 1: MineralRockGlow 작성**

`Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/MineralRockGlow.cs`:

```csharp
// @tags: rock, mineral, glow, vfx, particle, decoration
using UnityEngine;

/// <summary>
/// 광물돌의 항상발광 스파클을 구동한다. DiggableRock과 같은 GameObject에 부착.
/// Additive/Unlit 파티클이라 자체발광 → 어두운 동굴에서 자연스럽게 도드라진다 (광량 감지 없음).
/// 돌이 아직 땅에 묻혀(미노출) 있는 동안에는 스파클을 끈다.
/// 발광색은 이 컴포넌트가 직접 보유한다 (각 광물돌 프리팹에서 설정).
/// </summary>
public class MineralRockGlow : MonoBehaviour
{
    [Header("발광")]
    [Tooltip("스파클 파티클 시스템 (자식 오브젝트). 프리팹에서 Additive/Unlit 머티리얼로 설정")]
    [SerializeField] private ParticleSystem sparkleSystem;

    [Tooltip("이 광물돌의 발광색 (광물별로 직접 설정)")]
    [SerializeField] private Color glowColor = new Color(0.4f, 0.9f, 1f, 1f);

    private SpriteRenderer _sr;
    private bool _emitting;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        if (sparkleSystem != null)
        {
            var main = sparkleSystem.main;
            main.startColor = glowColor;
        }
        ApplyEmission(ShouldEmit());
    }

    private void Update()
    {
        bool want = ShouldEmit();
        if (want != _emitting)
            ApplyEmission(want);
    }

    // 돌이 실제로 보일 때(렌더러 활성)만 발광. preExposed/파서노출/복원 모두 일관 처리.
    private bool ShouldEmit() => _sr == null || _sr.enabled;

    private void ApplyEmission(bool on)
    {
        _emitting = on;
        if (sparkleSystem == null) return;

        if (on) sparkleSystem.Play();
        else    sparkleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void OnDisable()
    {
        // 풀 재사용/비활성 시 깔끔히 정리
        _emitting = false;
        if (sparkleSystem != null)
            sparkleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
```

- [ ] **Step 2: 컴파일 확인 — 사람이 검증**

Unity 컴파일 에러 없음 확인.

- [ ] **Step 3: 체크포인트**

컴파일 통과 후 UVCS 체크인.

---

## Task 5: 에셋·프리팹 셋업 + 수동 검증 (사람이 에디터에서)

코드로 만들 수 없는 부분(머티리얼·파티클·프리팹 배치)을 가이드 문서로 정리하고, 사람이 셋업·검증한다.

**Files:**
- Create: `Assets/Docs/mineral-rock/prefab-setup.md`
- 사람이 에디터에서: Additive 파티클 머티리얼, 광물돌 프리팹, 특수청크 배치

- [ ] **Step 1: prefab-setup.md 작성**

`Assets/Docs/mineral-rock/prefab-setup.md` 에 아래 절차를 기록:

1. **Additive 파티클 머티리얼 생성**
   - `Assets/Material/`에 `MineralSparkleAdditive.mat` 생성
   - Shader: `Universal Render Pipeline/Particles/Unlit`, Surface Type: Transparent, Blending Mode: **Additive**
   - 작고 부드러운 점/별 스프라이트 텍스처 할당 (없으면 기본 원형)

2. **광물돌 프리팹 생성** (`Assets/Prefabs/` 등)
   - 루트 GameObject: `SpriteRenderer`(광물돌 스프라이트, sortingLayer `ground`, order -1) + `PolygonCollider2D`
   - 컴포넌트 부착: `DamageStagedVisuals`, `RockBreakVFX`, `DiggableRock`, `MineralRock`, `MineralRockGlow`
   - `DiggableRock`: **`preExposed = true`, `minExposedPixels = 0`**, Rigidbody2D **없음** (제약 #9)
   - `MineralRock.ore`: 해당 광물 `MineralSO` 할당
   - 자식 `__Sparkle` GameObject + `ParticleSystem`:
     - Renderer Material = `MineralSparkleAdditive`
     - Emission rate ≈ 5/s, Shape = Sprite(또는 Box, 돌 바운즈)
     - Start Size 0.04~0.1, Start Lifetime 0.4~0.8
     - Color over Lifetime: alpha 0→1→0, Size over Lifetime: 0→1→0 (트윙클)
     - Play On Awake 체크 (컴포넌트가 노출 시 제어)
   - `MineralRockGlow.sparkleSystem` ← 자식 ParticleSystem, `glowColor` ← 광물색

3. **특수청크에 배치** (제약 #8)
   - Project 창에서 특수청크 프리팹을 **더블클릭(Prefab Edit 모드)** 후 광물돌을 자식으로 배치
   - local position 유효 범위 X(0~10), Y(0~10) 확인

- [ ] **Step 2: JSON 값 확인**

`specialChunkSettings.json`의 `mineralRock.overrides`에 배치한 광물 ID가 있는지 확인 (없으면 default 적용). 필요 시 항목 추가.

- [ ] **Step 3: 수동 플레이 검증**

- 어두운 동굴에서 광물돌이 파티클로 발광하며 도드라지는지
- 곡괭이/드릴로 채굴 시 HP대로 파괴되고 지정 광물이 `[minDrop, maxDrop]` 개수만큼 드롭되는지
- 묻혀있는 동안 발광 OFF → 인접 파기로 노출되면 발광 ON 되는지 (preExposed=false 케이스)
- `specialChunkSettings.json`에서 maxHp/minDrop/maxDrop 변경 후 재실행 시 반영되는지

- [ ] **Step 4: 체크포인트**

검증 완료 후 UVCS 체크인.

---

## Self-Review 결과

- **Spec 커버리지:** 발광(Task4) / 특정광물 확정드롭(Task3) / DiggableRock 재사용 훅(Task2) / JSON HP·드롭조정(Task1) / 프리팹 배치·제약(Task5) / MineralSO 무수정(설계대로 어디에도 수정 없음) — 모두 task 매핑됨.
- **Placeholder:** 없음. 코드 스텝은 전체 코드 포함.
- **타입 일관성:** `Resolve`/`ResolvedMineralRock`/`MineralRockEntry`/`MineralRockSection`(Task1) ↔ Task3 사용 일치. `IRockDropOverride.TryDropOnDestroy`(Task2) ↔ Task3 구현 일치. `MineralDropHelper.Drop`(Task2) ↔ Task3 호출 일치. `DiggableRock.IsRevealed`/`MaxHp` 기존 멤버 확인됨.
- **프로젝트 제약 반영:** 테스트 실행/ git 미사용을 Global Constraints + 각 스텝에 반영.
