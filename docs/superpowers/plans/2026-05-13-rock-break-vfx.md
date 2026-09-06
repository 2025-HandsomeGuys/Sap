# Rock Break VFX Implementation Plan
@tags: rock, VFX, DiggableRock, RockBreakVFX, RockFragment, plan, implementation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** DiggableRock 파괴 시 조각(fragment)이 튀어나와 지형에 물리적으로 착지한 뒤 페이드아웃되는 VFX 추가.

**Architecture:** `RockFragment.cs`(물리+수명 처리)와 `RockBreakVFX.cs`(조각 생성 오케스트레이션)를 분리 생성. `DiggableRock.DestroyRock()`에서 `GetComponent<RockBreakVFX>()?.Play()`를 1줄 추가해 기존 파괴 흐름을 보존. 기존 TerrainParticleManager 소형 debris 파티클은 그대로 유지.

**Tech Stack:** Unity 2D, C#, Rigidbody2D, CircleCollider2D, Coroutine

---

## 파일 구조

| 파일 | 작업 | 역할 |
|------|------|------|
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockFragment.cs` | 신규 생성 | 물리 + 수명 타이머 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockBreakVFX.cs` | 신규 생성 | 조각 생성 오케스트레이션 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/DiggableRock.cs` | 1줄 추가 | DestroyRock()에서 VFX 호출 |

---

### Task 1: RockFragment.cs 생성

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockFragment.cs`

- [ ] **Step 1: 파일 생성**

```csharp
// @tags: rock, vfx, fragment, physics, decoration
using System.Collections;
using UnityEngine;

public class RockFragment : MonoBehaviour
{
    private SpriteRenderer _sr;

    public void Init(
        Sprite sprite,
        Vector2 velocity,
        int layer,
        int sortingLayerID,
        int sortingOrder,
        float colliderRadius,
        float lifetime,
        float fadeDuration)
    {
        gameObject.layer = layer;

        _sr = gameObject.AddComponent<SpriteRenderer>();
        _sr.sprite = sprite;
        _sr.sortingLayerID = sortingLayerID;
        _sr.sortingOrder = sortingOrder;

        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 1f;
        rb.drag = 0.5f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.velocity = velocity;

        var col = gameObject.AddComponent<CircleCollider2D>();
        col.radius = colliderRadius;

        StartCoroutine(LifetimeRoutine(lifetime, fadeDuration));
    }

    private IEnumerator LifetimeRoutine(float lifetime, float fadeDuration)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, lifetime - fadeDuration));

        float elapsed = 0f;
        Color c = _sr.color;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            c.a = 1f - Mathf.Clamp01(elapsed / fadeDuration);
            _sr.color = c;
            yield return null;
        }

        Destroy(gameObject);
    }
}
```

- [ ] **Step 2: 컴파일 확인**

Unity Editor Console에 에러 없음을 확인.

- [ ] **Step 3: 커밋**

```bash
git add Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockFragment.cs
git commit -m "feat: add RockFragment - physics fragment with fade lifetime"
```

---

### Task 2: RockBreakVFX.cs 생성

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockBreakVFX.cs`

- [ ] **Step 1: 파일 생성**

```csharp
// @tags: rock, vfx, break, decoration
using UnityEngine;

public class RockBreakVFX : MonoBehaviour
{
    [Header("Fragment 스프라이트")]
    public Sprite[] fragmentSprites;

    [Header("Fragment 수량 및 물리")]
    [Range(2, 6)] public int fragmentCount = 4;
    [Range(0f, 5f)]  public float launchSpeedMin = 2f;
    [Range(0f, 10f)] public float launchSpeedMax = 6f;
    public float fragmentColliderRadius = 0.08f;

    [Header("Fragment 수명")]
    public float fragmentLifetime = 4f;
    public float fadeDuration = 0.5f;

    public void Play(Vector3 position)
    {
        if (fragmentSprites == null || fragmentSprites.Length == 0)
        {
            Debug.LogWarning("[RockBreakVFX] fragmentSprites가 비어 있습니다. 프리팹에서 스프라이트를 연결해주세요.");
            return;
        }

        int fragmentLayer = LayerMask.NameToLayer("RockFragment");
        if (fragmentLayer < 0)
        {
            Debug.LogWarning("[RockBreakVFX] 'RockFragment' 레이어가 없습니다. Project Settings → Tags & Layers에 추가 필요.");
            fragmentLayer = gameObject.layer;
        }

        var sr = GetComponent<SpriteRenderer>();
        int sortingLayerID = sr != null ? sr.sortingLayerID : 0;
        int sortingOrder   = sr != null ? sr.sortingOrder   : 0;

        for (int i = 0; i < fragmentCount; i++)
        {
            float angleDeg = 360f / fragmentCount * i + Random.Range(-30f, 30f);
            float angleRad = angleDeg * Mathf.Deg2Rad;
            float speed    = Random.Range(launchSpeedMin, launchSpeedMax);
            // 위쪽 편향으로 위로 튀는 느낌
            var velocity = new Vector2(Mathf.Cos(angleRad) * speed, Mathf.Sin(angleRad) * speed + 0.5f);

            Sprite sprite = fragmentSprites[Random.Range(0, fragmentSprites.Length)];

            var go = new GameObject("RockFragment");
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            go.AddComponent<RockFragment>().Init(
                sprite, velocity, fragmentLayer,
                sortingLayerID, sortingOrder,
                fragmentColliderRadius, fragmentLifetime, fadeDuration);
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인**

Unity Editor Console에 에러 없음을 확인.

- [ ] **Step 3: 커밋**

```bash
git add Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockBreakVFX.cs
git commit -m "feat: add RockBreakVFX - spawns physics fragments on rock destroy"
```

---

### Task 3: DiggableRock.cs 수정

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/DiggableRock.cs:328`

- [ ] **Step 1: DestroyRock() 첫 줄에 1줄 추가**

`DestroyRock()` 메서드 시작 부분을 아래처럼 수정:

```csharp
private void DestroyRock()
{
    GetComponent<RockBreakVFX>()?.Play(transform.position);

    // 1. 부모 TerrainChunk 찾기
    TerrainChunk chunk = GetComponentInParent<TerrainChunk>();
```

기존 코드 이후는 변경 없음.

- [ ] **Step 2: 컴파일 확인**

Unity Editor Console에 에러 없음을 확인.

- [ ] **Step 3: 커밋**

```bash
git add Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/DiggableRock.cs
git commit -m "feat: trigger RockBreakVFX on rock destroy"
```

---

### Task 4: Unity 에디터 설정

> ⚠️ 이 단계는 코드가 아닌 Unity Editor 수동 작업입니다.

- [ ] **Step 1: RockFragment 레이어 추가**

`Edit → Project Settings → Tags and Layers → Layers`
빈 슬롯에 `RockFragment` 입력 후 저장.

- [ ] **Step 2: Physics 2D 충돌 매트릭스 설정**

`Edit → Project Settings → Physics 2D → Layer Collision Matrix`
- `RockFragment ↔ Terrain` : ✅ ON
- `RockFragment ↔ Player`  : ☐ OFF
- `RockFragment ↔ RockFragment` : ☐ OFF

- [ ] **Step 3: DiggableRock 프리팹에 RockBreakVFX 컴포넌트 추가**

Project 창에서 DiggableRock 프리팹 더블클릭(Prefab Edit 모드 진입).
`Add Component → RockBreakVFX`.

- [ ] **Step 4: Fragment 스프라이트 연결**

Inspector의 `RockBreakVFX → Fragment Sprites` 배열에
준비된 stone fragment 스프라이트 2~4개 드래그하여 연결.

- [ ] **Step 5: 동작 확인**

Play Mode 진입 → 바위 파기 → 파괴 시 조각이 튀어나와 지형에 착지 → 4초 후 페이드아웃 확인.

- [ ] **Step 6: 프리팹 저장 및 커밋**

Prefab Edit 모드에서 Save 후:

```bash
git add Assets/
git commit -m "feat: configure RockBreakVFX prefab with fragment sprites"
```

---

## 동작 체크리스트

- [ ] 바위 파괴 시 조각 `fragmentCount`개가 방사형으로 튀어나옴
- [ ] 조각이 지형 콜라이더와 충돌해 바닥에 착지
- [ ] 착지 후 `fragmentLifetime - fadeDuration`초 뒤 페이드아웃 시작
- [ ] `fadeDuration`초 동안 투명해지며 사라짐
- [ ] 기존 debris 파티클 및 광물 드롭 정상 동작
- [ ] 조각이 플레이어 이동을 방해하지 않음
- [ ] `fragmentSprites` 미연결 시 경고 로그 출력, 게임 크래시 없음
- [ ] `RockFragment` 레이어 미등록 시 경고 로그 출력, 게임 크래시 없음
