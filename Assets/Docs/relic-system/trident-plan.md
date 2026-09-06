# 삼지창(Trident) 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 장착 중 삽 지형 파기를 "창날 3줄기 자동 3연타"로 대체하는 패시브 유물 (RelicID 4028 — 실행 중 4027을 병렬 작업 OneWayPortal이 선점해 조정).

**Architecture:** `Digger.DigAt()`의 `ModifyTerrain` 직전에 유물 오버라이드 훅 1개 신설(`TryOverrideTerrainDig`, RelicBehaviour→RelicManager→PlayerMining relay). TridentRelic이 훅을 잡아 `ctx.runner` 코루틴으로 좌/중/우 0.12초 간격 캡슐 스탬프 3연타(`ctx.CarveTerrain`). 설계: `Assets/Docs/relic-system/trident-design.md`.

**Tech Stack:** Unity C#, 기존 Relic 프레임워크, NUnit EditMode 테스트.

## Global Constraints

- **UVCS 프로젝트** — git 명령 금지. 체크인은 사용자가 직접.
- **Unity 테스트 실행은 사람이 수행** — Claude는 테스트 파일 작성만, 실행 게이트 아님.
- 공유 파일 3개(`RelicID.cs`, `RelicSliceAssetGenerator.cs`, `RelicDebugGranter.cs`)는 기존 줄 보존, 한 줄 추가만.
- 더티 플래그·지형 수정은 기존 경로(`ExplodeTerrain`) 경유 — 직접 플래그 세팅 금지.
- 구현 후 사용자가 Unity에서: 컴파일 확인 → `Tools > Relic > Generate Slice Assets` 재실행(SerializeReference 신규 필드) → T키 인게임 검증.

---

### Task 1: 프레임워크 훅 — TryOverrideTerrainDig

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Core/RelicBehaviour.cs` (훅 가상 메서드)
- Modify: `Assets/Scripts/Gameplay/Relics/Core/RelicManager.cs` (슬롯 집계 — `ApplyDigModifiers` 근처)
- Modify: `Assets/Scripts/UI/Player/PlayerMining.cs` (relay — `GetCurrentDigParameters` 근처)
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Digger.cs` (`DigAt` 호출 1지점)

**Interfaces:**
- Produces: `RelicBehaviour.TryOverrideTerrainDig(Vector2 digPos, Vector2 dir, float radius, int toolIndex) → bool` (가상, 기본 false) — Task 2가 override.
- Produces: `PlayerMining.TryOverrideTerrainDig(...)` / `RelicManager.TryOverrideTerrainDig(...)` 동일 시그니처.

- [ ] **Step 1: RelicBehaviour에 가상 훅 추가**

`OnDigSwing()` 선언 아래에 추가:

```csharp
// 지형 파기 실행 오버라이드(삼지창류). true 반환 시 Digger의 기본 원형 파기·연출을 유물이 대체한다.
// digPos=기본 파기 중심, dir=플레이어→파기점 방향(정규화), radius=effectiveRadius(차징·스탯·타 유물 배율 반영), toolIndex=도구.
// 스태미나 소모·불괴 체크는 Digger가 훅 호출 전에 이미 끝냈다.
public virtual bool TryOverrideTerrainDig(UnityEngine.Vector2 digPos, UnityEngine.Vector2 dir, float radius, int toolIndex) => false;
```

(RelicBehaviour.cs는 `using System;`만 있으므로 `UnityEngine.` 풀네임 사용 — 기존 파일에 UnityEngine using 추가하지 않음.)

- [ ] **Step 2: RelicManager에 슬롯 집계 추가**

`ApplyDigModifiers` 메서드 아래에 추가:

```csharp
// 지형 파기 오버라이드 집계: 첫 true(첫 소유자 승리)에서 중단. Digger → PlayerMining → 여기.
public bool TryOverrideTerrainDig(UnityEngine.Vector2 digPos, UnityEngine.Vector2 dir, float radius, int toolIndex)
{
    for (int i = 0; i < _slotBehaviour.Length; i++)
        if (_slotBehaviour[i] != null && _slotBehaviour[i].TryOverrideTerrainDig(digPos, dir, radius, toolIndex))
            return true;
    return false;
}
```

- [ ] **Step 3: PlayerMining에 relay 추가**

`GetCurrentDigParameters` 메서드 아래에 추가:

```csharp
// 지형 파기 오버라이드 relay: Digger → RelicManager. 유물이 true면 기본 원형 파기를 대체한다.
public bool TryOverrideTerrainDig(Vector2 digPos, Vector2 dir, float radius, int toolIndex)
{
    return _relicManager != null && _relicManager.TryOverrideTerrainDig(digPos, dir, radius, toolIndex);
}
```

- [ ] **Step 4: Digger.DigAt에 훅 호출 삽입**

`DigAt()`의 불괴(IIndestructibleHit) 체크 foreach 루프 **직후**, `mapManager.ModifyTerrain(actualHitPos, effectiveRadius, toolIndex);` **직전**에 삽입:

```csharp
// 유물 지형 파기 오버라이드(삼지창 등): true면 기본 원형 파기·쉐이크·파티클을 유물이 대체한다.
// 삽 MaxStamina 감소는 '스윙 1회당 1회' 규칙 유지를 위해 여기 남긴다.
if (_playerMining != null && _playerMining.TryOverrideTerrainDig(actualHitPos, digDirection, effectiveRadius, toolIndex))
{
    if (toolIndex == 1)
        _staminaManager?.AddDiggingReduction(shovelReductionPerRadius * effectiveRadius);
    return;
}
```

주의: 이 시점의 `digDirection`은 플레이어→파기점 방향(정규화)으로 이미 계산돼 있음(Digger.cs의 CircleCastAll용 변수 재사용). PayCost(스태미나)는 이미 위에서 통과됨.

- [ ] **Step 5: 컴파일 확인 요청**

사용자에게 Unity 컴파일 확인 요청(게이트 아님, 다음 Task 진행 가능).

---

### Task 2: TridentRelic behaviour + 기하 헬퍼 테스트

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Behaviours/TridentRelic.cs`
- Test: `Assets/Tests/EditMode/TridentRelicTests.cs`

**Interfaces:**
- Consumes: Task 1의 `RelicBehaviour.TryOverrideTerrainDig` 가상 훅, `ctx.runner`(RelicManager, 코루틴 대행), `ctx.CarveTerrain(Vector2, float)`, `ctx.mining.playerAnimator`(public Animator), `ctx.player`(Transform).
- Produces: `Relic.TridentRelic` 클래스, 순수 정적 헬퍼 `TridentRelic.Rotate(Vector2 v, float deg)` / `TridentRelic.StampCount(float dist, float radius, float stepRatio)`.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/TridentRelicTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Relic;

// 삼지창 순수 기하 로직 검증 (코루틴·지형 파괴는 인게임 검증 영역).
public class TridentRelicTests
{
    [Test]
    public void Rotate_PreservesMagnitude()
    {
        Vector2 v = new Vector2(1.7f, -0.4f);
        Vector2 r = TridentRelic.Rotate(v, 20f);
        Assert.AreEqual(v.magnitude, r.magnitude, 1e-4f);
    }

    [Test]
    public void Rotate_PlusMinus_SymmetricAroundAim()
    {
        Vector2 aim = Vector2.right;
        Vector2 left  = TridentRelic.Rotate(aim, 20f);
        Vector2 right = TridentRelic.Rotate(aim, -20f);
        // 조준축 대칭: y 부호 반대, x 동일
        Assert.AreEqual(left.x, right.x, 1e-4f);
        Assert.AreEqual(left.y, -right.y, 1e-4f);
        // +20° 회전은 반시계(위쪽 = 좌 갈래)
        Assert.Greater(left.y, 0f);
    }

    [Test]
    public void Rotate_Zero_ReturnsSameDirection()
    {
        Vector2 v = new Vector2(0.6f, 0.8f);
        Vector2 r = TridentRelic.Rotate(v, 0f);
        Assert.AreEqual(v.x, r.x, 1e-5f);
        Assert.AreEqual(v.y, r.y, 1e-5f);
    }

    [Test]
    public void StampCount_CoversLength_WithStepSpacing()
    {
        // 길이 2.0, 반경 0.3, step비 0.7 → step=0.21, n=Ceil(2/0.21)=10 → 스탬프 11개(양 끝 포함)
        int n = TridentRelic.StampCount(2.0f, 0.3f, 0.7f);
        Assert.AreEqual(10, n);
        // 간격이 반경×비율 이하 → 캡슐에 구멍 없음
        Assert.LessOrEqual(2.0f / n, 0.3f * 0.7f + 1e-4f);
    }

    [Test]
    public void StampCount_ZeroDistance_IsZero()
    {
        Assert.AreEqual(0, TridentRelic.StampCount(0f, 0.3f, 0.7f));
    }

    [Test]
    public void DefaultBehaviour_IgnoresNonShovel()
    {
        var relic = new TridentRelic();
        // ctx 미주입 상태: 삽이 아니면 false, 삽이라도 ctx 없으면 false(안전)
        Assert.IsFalse(relic.TryOverrideTerrainDig(Vector2.zero, Vector2.right, 1f, 2)); // 곡괭이
        Assert.IsFalse(relic.TryOverrideTerrainDig(Vector2.zero, Vector2.right, 1f, 3)); // 드릴
        Assert.IsFalse(relic.TryOverrideTerrainDig(Vector2.zero, Vector2.right, 1f, 1)); // 삽 + ctx null
    }
}
```

- [ ] **Step 2: 컴파일 실패 확인 (TridentRelic 미존재)** — 작성 직후엔 클래스가 없어 컴파일 에러가 정상. 바로 Step 3으로.

- [ ] **Step 3: TridentRelic 구현**

`Assets/Scripts/Gameplay/Relics/Behaviours/TridentRelic.cs`:

```csharp
using System;
using System.Collections;
using UnityEngine;

namespace Relic
{
    // 삼지창(패시브): 삽 지형 파기를 창날 3줄기 자동 3연타로 대체.
    // Digger.DigAt의 TryOverrideTerrainDig 훅을 잡아 좌(+20°)→중(0°)→우(−20°) 순서로
    // 0.12초 간격 캡슐 스탬프(ctx.CarveTerrain = ExplodeTerrain 경로, IndestructibleMask 자동 존중).
    // radius(=effectiveRadius)에 차징비율·MiningRange·타 유물 배율이 이미 반영돼 있어
    // 폭·길이를 그대로 곱하면 풀차징일수록 길고 굵게 + 도박꾼 안경 등과 자동 합성된다.
    [Serializable]
    public class TridentRelic : RelicBehaviour
    {
        [SerializeField] private float   spreadAngleDeg      = 20f;
        [SerializeField] private float   stabInterval        = 0.12f;
        [SerializeField] private float[] prongRadiusPerLevel = { 0.30f, 0.36f, 0.42f };
        [SerializeField] private float[] prongLengthPerLevel = { 2.0f, 2.4f, 2.8f };
        [SerializeField] private float   stampStepRatio      = 0.7f;   // 스탬프 간격 = 폭 × 이 값
        [SerializeField] private float   originOffset        = 0.3f;   // 몸 근처 시작 오프셋

        private Coroutine _stabRoutine;

        private float Lv(float[] a) => a[Mathf.Clamp(level - 1, 0, a.Length - 1)];

        public override bool TryOverrideTerrainDig(Vector2 digPos, Vector2 dir, float radius, int toolIndex)
        {
            if (toolIndex != 1) return false;                 // 삽 전용
            if (radius <= 0f) return false;
            if (ctx == null || ctx.runner == null || ctx.player == null) return false;

            // 연속 파기로 이전 3연타가 아직 돌면 끊고 새로 시작(스윙 쿨 0.5s > 3연타 0.24s라 보통 안 겹침).
            if (_stabRoutine != null) ctx.runner.StopCoroutine(_stabRoutine);
            _stabRoutine = ctx.runner.StartCoroutine(StabSequence(dir.normalized, radius));
            return true;
        }

        private IEnumerator StabSequence(Vector2 aimDir, float scale)
        {
            float w   = Lv(prongRadiusPerLevel) * scale;
            float len = Lv(prongLengthPerLevel) * scale;

            // 좌(+spread, 반시계=위) → 중 → 우. 조준 방향은 스윙 시점에 고정, 원점은 타격 시점 플레이어 위치.
            float[] angles = { spreadAngleDeg, 0f, -spreadAngleDeg };

            for (int i = 0; i < angles.Length; i++)
            {
                if (i > 0) yield return new WaitForSeconds(stabInterval);
                if (ctx == null || ctx.player == null) yield break;

                Vector2 dir    = Rotate(aimDir, angles[i]);
                Vector2 origin = (Vector2)ctx.player.position + dir * originOffset;
                Vector2 tip    = origin + dir * len;
                CarveCapsule(origin, tip, w);

                // 타격 연출: 찌르기마다 Mine 애니메이션 재트리거 + 쉐이크 + 줄기 끝 파티클.
                var anim = ctx.mining != null ? ctx.mining.playerAnimator : null;
                if (anim != null)
                {
                    anim.ResetTrigger("Mine");
                    anim.SetTrigger("Mine");
                }
                CameraShakeManager.Instance?.ShakeOnDig(1);
                TerrainParticleManager.Instance?.SpawnImpact(tip, Color.white, 1);
            }
            _stabRoutine = null;
        }

        // a→b 선분을 따라 원을 겹쳐 스탬프 = 가늘고 긴 창날 슬롯 (PlasmaCutter CarveCapsule 방식).
        private void CarveCapsule(Vector2 a, Vector2 b, float radius)
        {
            int n = StampCount(Vector2.Distance(a, b), radius, stampStepRatio);
            for (int i = 0; i <= n; i++)
            {
                Vector2 p = n == 0 ? a : Vector2.Lerp(a, b, i / (float)n);
                ctx.CarveTerrain(p, radius);
            }
        }

        public override void OnUnequip()
        {
            if (_stabRoutine != null && ctx != null && ctx.runner != null)
                ctx.runner.StopCoroutine(_stabRoutine);
            _stabRoutine = null;
        }

        // ── 순수 기하 헬퍼 (EditMode 테스트 대상) ──

        // 2D 벡터를 deg도 반시계 회전.
        public static Vector2 Rotate(Vector2 v, float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // 길이 dist를 간격 radius×stepRatio(최소 0.15)로 나눈 분할 수. 스탬프는 n+1개(양 끝 포함).
        public static int StampCount(float dist, float radius, float stepRatio)
        {
            if (dist <= 0f) return 0;
            float step = Mathf.Max(0.15f, radius * stepRatio);
            return Mathf.CeilToInt(dist / step);
        }
    }
}
```

- [ ] **Step 4: 사용자에게 EditMode 테스트 실행 요청 (게이트 아님)** — `TridentRelicTests` 6건 통과 기대.

---

### Task 3: 등록 — RelicID·생성기·디버그 키

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Data/RelicID.cs`
- Modify: `Assets/Scripts/Editor/RelicSliceAssetGenerator.cs`
- Modify: `Assets/Scripts/Gameplay/Relics/Debug/RelicDebugGranter.cs`

**Interfaces:**
- Consumes: Task 2의 `Relic.TridentRelic`.
- Produces: `RelicID.Trident = 4028`, 슬라이스 에셋 `Trident.asset`, 디버그 키 `T`.

- [ ] **Step 1: RelicID 추가**

`Hourglass = 4026,` 줄 아래에:

```csharp
        Trident       = 4028,   // 삼지창 (패시브: 삽 지형 파기 → 창날 3줄기 자동 3연타, 폭 좁고 길게)
```

`// 이후 유물은 4028+로 추가` 주석은 `4029+`로 갱신. (4027은 OneWayPortal 선점)

- [ ] **Step 2: 생성기 등록**

`RelicSliceAssetGenerator.Generate()`의 `hourglass` 줄 아래에:

```csharp
        var trident = CreateRelic(RelicID.Trident, "삼지창", RelicType.Passive, new TridentRelic()); // 삽 파기 → 창날 3줄기 3연타
```

`db.allRelics` 리스트 끝에 `trident` 추가 (기존 항목 보존).

- [ ] **Step 3: 디버그 키 추가**

`RelicDebugGranter.DefaultBindings()` 배열 끝(`Hourglass` 줄 아래)에:

```csharp
            new Binding { key = KeyCode.T,              relic = RelicID.Trident,        slot = 0 }, // 삼지창 (T=Trident)
```

- [ ] **Step 4: 사용자 Unity 작업 안내**

1. 컴파일 확인
2. `Tools > Relic > Generate Slice Assets` 재실행 (신규 에셋 + SerializeReference 필드 직렬화)
3. 인게임: T키 장착(재입력=Lv+) → 삽 풀차징 파기 → 좌/중/우 3연타 확인
4. 검증 포인트: 차징 미달 시 미발동(기존 게이트), 불괴 지형에서 안 파임, 도박꾼 안경 동시 장착 시 갈래 크기 변동, 곡괭이/드릴 영향 없음
5. UVCS 체크인

---

## Self-Review 결과

- **Spec coverage:** 설계 문서의 훅 신설(Task 1)·3연타 동작/파라미터(Task 2)·등록(Task 3) 전부 매핑. "범위 밖" 항목(돌·곡괭이·ToolSwap·mp3)은 코드 변경 불필요 — toolIndex 체크와 기존 경로 유지로 충족.
- **Placeholder scan:** 통과 (모든 스텝에 실제 코드 포함).
- **Type consistency:** `TryOverrideTerrainDig(Vector2, Vector2, float, int) → bool` 4개 파일 동일. `Rotate`/`StampCount` 시그니처 테스트-구현 일치.
