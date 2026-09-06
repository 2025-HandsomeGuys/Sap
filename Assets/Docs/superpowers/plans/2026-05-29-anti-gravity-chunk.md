# Anti-Gravity Chunk System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 플레이어와 청크 내 모든 Rigidbody2D 오브젝트에 반중력을 적용하는 특수 청크 시스템 구현

**Architecture:** `AntiGravityHandler`(Player 부착)가 중력 전환·스프라이트 flip·점프 방향을 담당하고, `AntiGravityZone`(특수 청크 자식)이 트리거 감지 후 Handler 호출 및 비플레이어 Rigidbody2D 조작을 담당한다. `PlayerController`는 점프 방향 1줄만 수정.

**Tech Stack:** Unity 2D, C#, Rigidbody2D.linearVelocity (Unity 6 API), Coroutine

---

## 파일 구조

| 파일 | 작업 |
|------|------|
| `Assets/Scripts/UI/Player/AntiGravityHandler.cs` | 신규 생성 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/AntiGravityZone.cs` | 신규 생성 |
| `Assets/Scripts/UI/Player/PlayerController.cs` | 수정 (3곳, 각 1줄) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunkManager.cs` | enum 값 1개 추가 |

---

## Task 1: AntiGravityHandler — 플레이어 중력 전환 컴포넌트

**Files:**
- Create: `Assets/Scripts/UI/Player/AntiGravityHandler.cs`

- [ ] **Step 1: 파일 생성**

```csharp
// @tags: player, anti-gravity, gravity, handler
using System.Collections;
using UnityEngine;

/// <summary>
/// 반중력 구역 진입 시 플레이어에게 적용되는 중력 전환·스프라이트 flip 처리.
/// AntiGravityZone이 Activate/Deactivate를 호출한다.
/// PlayerController는 IsActive만 읽어 점프 방향을 결정한다.
/// </summary>
public class AntiGravityHandler : MonoBehaviour
{
    [SerializeField] private float antiGravityMultiplier = 0.7f;
    [SerializeField] private float transitionDuration = 0.25f;

    public bool IsActive { get; private set; }

    private Rigidbody2D _rb;
    private float _defaultGravity;
    private Coroutine _transition;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _defaultGravity = _rb.gravityScale;
    }

    /// <summary>반중력 존 진입 시 호출. Y속도 감쇄 후 중력 반전 + 스프라이트 flip.</summary>
    public void Activate()
    {
        if (IsActive) return;
        StopTransition();
        _transition = StartCoroutine(TransitionIn());
    }

    /// <summary>반중력 존 이탈 또는 존 언로드 시 호출. 즉시 원복.</summary>
    public void Deactivate()
    {
        StopTransition();
        _rb.gravityScale = _defaultGravity;
        Vector3 scale = transform.localScale;
        scale.y = Mathf.Abs(scale.y);
        transform.localScale = scale;
        IsActive = false;
    }

    private IEnumerator TransitionIn()
    {
        float elapsed = 0f;
        float startVelY = _rb.linearVelocity.y;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / transitionDuration;
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, Mathf.Lerp(startVelY, 0f, t));
            yield return null;
        }

        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, 0f);
        _rb.gravityScale = _defaultGravity * -antiGravityMultiplier;

        Vector3 scale = transform.localScale;
        scale.y = -Mathf.Abs(scale.y);
        transform.localScale = scale;

        IsActive = true;
        _transition = null;
    }

    private void StopTransition()
    {
        if (_transition == null) return;
        StopCoroutine(_transition);
        _transition = null;
    }
}
```

- [ ] **Step 2: UVCS에 체크인**

  Unity 에디터에서 UVCS(Plastic SCM) > Check In > `AntiGravityHandler.cs`
  커밋 메시지: `feat: add AntiGravityHandler for player gravity transition`

---

## Task 2: AntiGravityZone — 청크 트리거 존 컴포넌트

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/AntiGravityZone.cs`

- [ ] **Step 1: 파일 생성**

```csharp
// @tags: zone, anti-gravity, trigger, special-chunk, chunk, rigidbody
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 반중력 청크 트리거 존.
/// - 플레이어: AntiGravityHandler.Activate/Deactivate 호출
/// - 비플레이어 Rigidbody2D: gravityScale 직접 반전
/// IZoneEffect 대신 직접 OnTriggerEnter2D 사용 — 모든 Rigidbody2D 처리 필요.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class AntiGravityZone : MonoBehaviour
{
    [SerializeField] private float antiGravityMultiplier = 0.7f;

    private AntiGravityHandler _playerHandler;
    private readonly Dictionary<Rigidbody2D, float> _savedGravityScales = new();

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (!col.isTrigger)
        {
            Debug.LogWarning("[AntiGravityZone] Collider2D가 Trigger가 아닙니다. 자동으로 isTrigger = true 설정.");
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var handler = other.GetComponent<AntiGravityHandler>();
        if (handler != null)
        {
            _playerHandler = handler;
            handler.Activate();
            return;
        }

        var rb = other.GetComponent<Rigidbody2D>();
        if (rb == null) return;

        _savedGravityScales[rb] = rb.gravityScale;
        rb.gravityScale = -Mathf.Abs(rb.gravityScale) * antiGravityMultiplier;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var handler = other.GetComponent<AntiGravityHandler>();
        if (handler != null)
        {
            _playerHandler = null;
            handler.Deactivate();
            return;
        }

        var rb = other.GetComponent<Rigidbody2D>();
        if (rb == null) return;

        if (_savedGravityScales.TryGetValue(rb, out float saved))
        {
            rb.gravityScale = saved;
            _savedGravityScales.Remove(rb);
        }
    }

    private void OnDisable()
    {
        // 청크 언로드 시 OnTriggerExit2D가 발생하지 않으므로 수동 원복
        if (_playerHandler != null)
        {
            _playerHandler.Deactivate();
            _playerHandler = null;
        }

        foreach (var kvp in _savedGravityScales)
        {
            if (kvp.Key != null)
                kvp.Key.gravityScale = kvp.Value;
        }
        _savedGravityScales.Clear();
    }
}
```

- [ ] **Step 2: UVCS에 체크인**

  Unity 에디터에서 UVCS > Check In > `AntiGravityZone.cs`
  커밋 메시지: `feat: add AntiGravityZone trigger for all Rigidbody2D`

---

## Task 3: PlayerController — 점프 방향 최소 수정

**Files:**
- Modify: `Assets/Scripts/UI/Player/PlayerController.cs`

변경 위치 3곳:

1. **필드 선언 영역** (line ~85, `windVelocity` 근처)
2. **Start()** (line ~100)
3. **Jump()** (line ~461)

- [ ] **Step 1: 필드 추가**

  `Assets/Scripts/UI/Player/PlayerController.cs` 의 `windVelocity` 선언 바로 아래에 추가:

  ```csharp
  // 환경 효과 (WindZone / IceFloorZone이 설정)
  [HideInInspector] public Vector2 windVelocity = Vector2.zero;
  [HideInInspector] public bool isOnIce = false;
  private AntiGravityHandler _antiGravityHandler;  // ← 추가
  ```

- [ ] **Step 2: Start()에 캐싱 추가**

  `Start()` 내 `defaultGravity = rb.gravityScale;` 바로 아래에 추가:

  ```csharp
  defaultGravity = rb.gravityScale;
  _antiGravityHandler = GetComponent<AntiGravityHandler>();  // ← 추가
  ```

- [ ] **Step 3: Jump() 점프 방향 수정**

  기존:
  ```csharp
  void Jump()
  {
      isJumping = true;
      rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
      rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
  }
  ```

  변경 후:
  ```csharp
  void Jump()
  {
      isJumping = true;
      rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
      float jumpDir = (_antiGravityHandler != null && _antiGravityHandler.IsActive) ? -1f : 1f;
      rb.AddForce(Vector2.up * jumpForce * jumpDir, ForceMode2D.Impulse);
  }
  ```

- [ ] **Step 4: UVCS에 체크인**

  Unity 에디터에서 UVCS > Check In > `PlayerController.cs`
  커밋 메시지: `feat: add anti-gravity jump direction support to PlayerController`

---

## Task 4: SpecialChunkType enum 추가

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunkManager.cs`

- [ ] **Step 1: enum에 AntiGravity 추가**

  `SpecialChunkType` enum의 마지막 항목 아래에 추가:

  ```csharp
  Mine,             // 광산 공동 — 파기 가능 지형 + 파기 불가 구조물 (패턴 C)
  AntiGravity,      // 반중력 공동 — 진입 시 중력 반전  // ← 추가
  ```

- [ ] **Step 2: UVCS에 체크인**

  커밋 메시지: `feat: add AntiGravity to SpecialChunkType enum`

---

## Task 5: 프리팹 및 에디터 설정

> **주의:** CLAUDE.md 규칙 — 프리팹은 반드시 Project 창에서 더블클릭(Prefab Edit 모드)하여 편집. 씬 인스턴스에서 드래그 후 Apply 금지.

- [ ] **Step 1: AntiGravityChunk 프리팹 생성**

  1. Project 창에서 기존 TerrainChunk 프리팹 복제 → 이름: `AntiGravityChunk`
  2. 프리팹 더블클릭(Prefab Edit 모드 진입)
  3. 자식 빈 GameObject 추가: 이름 `AntiGravityTrigger`
     - `local position = (5, 5, 0)` (청크 중앙, 청크 1칸 = 10유닛)
     - `BoxCollider2D` 추가, `isTrigger = true`, `Size = (10, 10)` (청크 전체 커버)
     - `AntiGravityZone` 컴포넌트 추가
     - `antiGravityMultiplier = 0.7`, `transitionDuration = 0.25` 확인
  4. Prefab Edit 모드 저장 후 닫기

- [ ] **Step 2: Player 프리팹에 AntiGravityHandler 추가**

  1. Project 창에서 Player 프리팹 더블클릭
  2. `AntiGravityHandler` 컴포넌트 추가
  3. `antiGravityMultiplier = 0.7`, `transitionDuration = 0.25` 확인
  4. 저장

- [ ] **Step 3: SpecialChunkManager에 AntiGravityChunk 등록**

  씬의 `SpecialChunkManager` Inspector에서:
  1. `Pools` 리스트에서 테스트할 레이어(예: Stone)의 pool 선택
  2. `Chunks` 리스트에 항목 추가:
     - `Prefab`: AntiGravityChunk
     - `Spawn Chance`: 5.0 (테스트용)
     - `Chunk Type`: AntiGravity
     - `Min/Max Depth`: 0 (제한 없음)
     - `Chunk Size X/Y`: 1

---

## Task 6: 수동 검증

> Unity Test Runner 실행은 사람이 직접 수행 (CLAUDE.md 규칙).

- [ ] **Step 1: 기본 반중력 동작 확인**

  Play Mode 진입 후 반중력 청크 구역 진입:
  - [ ] 플레이어가 천천히 떠오르는지 (Y속도 감쇄 → 위로 이동)
  - [ ] 스프라이트가 상하 반전되는지
  - [ ] 좌우 이동이 정상 동작하는지

- [ ] **Step 2: 점프 확인**

  반중력 구역에서 천장에 붙은 상태:
  - [ ] 점프 키 → 아래로 튀어오르는지
  - [ ] 천장 착지 후 다시 점프 가능한지

- [ ] **Step 3: 이탈 후 원복 확인**

  구역 이탈:
  - [ ] 스프라이트 flip 원복
  - [ ] 중력 정상화 (자연스럽게 낙하)
  - [ ] 점프 방향 정상화

- [ ] **Step 4: 비플레이어 오브젝트 확인**

  반중력 구역 내에서 지형 파기:
  - [ ] 파진 광물이 위로 떠오르는지
  - [ ] 바위 조각(RockFragment)이 위로 날아가는지
  - [ ] 구역 이탈 후 일반 중력으로 복귀하는지

- [ ] **Step 5: 청크 언로드 시 원복 확인**

  반중력 구역 안에서 멀리 이동하여 청크 언로드 발생:
  - [ ] `AntiGravityZone.OnDisable()` 호출 → 플레이어 중력 정상화
  - [ ] 비플레이어 오브젝트 gravityScale 원복

- [ ] **Step 6: 벽타기 확인**

  반중력 구역 내 벽에서:
  - [ ] 벽타기 진입/이탈이 정상 동작하는지
  - [ ] 벽타기 중 입력 방향이 뒤집히지 않는지
