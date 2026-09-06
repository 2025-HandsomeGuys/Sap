# Rock Break VFX — 설계 문서
@tags: rock, VFX, DiggableRock, RockBreakVFX, RockFragment, design, spec

## 개요
DiggableRock이 HP 0으로 파괴될 때 조각(fragment)들이 튀어나와 지형에 착지한 뒤 페이드아웃되는 시각 효과.

---

## 컴포넌트 구조

```
DiggableRock (기존)
  └─ RockBreakVFX (새 컴포넌트, 프리팹에 Add Component)

RockFragment (새 MonoBehaviour, 런타임 생성)
```

---

## RockBreakVFX.cs

**위치**: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockBreakVFX.cs`

**Inspector 설정**
```csharp
public Sprite[] fragmentSprites;          // 2~4개 generic stone fragment
[Range(2, 6)] public int fragmentCount = 4;
[Range(0f, 5f)] public float launchSpeedMin = 2f;
[Range(0f, 10f)] public float launchSpeedMax = 6f;
public float fragmentLifetime = 4f;       // 총 수명 (페이드 포함)
public float fadeDuration = 0.5f;         // 수명 끝 페이드아웃 시간
public string terrainLayerName = "Terrain";
```

**역할**
- `Play(Vector3 position)` 호출 시 fragmentCount 개수만큼 RockFragment 생성
- 각 조각: 랜덤 sprite 선택, 방사형 + 위쪽 편향 초기 속도 부여
- 발사 각도: `360 / fragmentCount * i + Random.Range(-30, 30)`, y 성분 +0.5f 편향

---

## RockFragment.cs

**위치**: `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockFragment.cs`

**초기화**
```csharp
void Init(Sprite sprite, Vector2 velocity, float lifetime, float fadeDuration)
```

**구성 요소**
- `SpriteRenderer` — 랜덤 sprite 표시
- `Rigidbody2D` — gravityScale=1, drag=0.5, Continuous collision detection
- `CircleCollider2D` — radius=0.08f

**수명 Coroutine**
1. `lifetime - fadeDuration` 초 대기
2. `fadeDuration` 동안 alpha 0으로 선형 페이드
3. `Destroy(gameObject)`

---

## DiggableRock.cs 변경

`DestroyRock()` 첫 줄에 1줄 추가:
```csharp
GetComponent<RockBreakVFX>()?.Play(transform.position);
```
기존 흐름(지형 픽셀 제거 → 파티클 → 광물 드롭 → Destroy) 완전 유지.

---

## 물리 설정

| 항목 | 값 |
|------|----|
| gravityScale | 1f |
| drag | 0.5f |
| Collision Detection | Continuous |
| CircleCollider radius | 0.08f |
| Fragment Layer | `RockFragment` |
| `RockFragment ↔ Terrain` | 충돌 ON |
| `RockFragment ↔ Player` | 충돌 OFF |
| `RockFragment ↔ RockFragment` | 충돌 OFF |

---

## Unity 에디터 수동 작업

1. `"RockFragment"` Layer 추가 (Project Settings → Tags & Layers)
2. Physics 2D Matrix 설정
3. DiggableRock 프리팹에 `RockBreakVFX` 컴포넌트 Add
4. `fragmentSprites` 배열에 sprite 연결

---

## 파일 변경 요약

| 파일 | 변경 |
|------|------|
| `RockBreakVFX.cs` | 신규 생성 |
| `RockFragment.cs` | 신규 생성 |
| `DiggableRock.cs` | 1줄 추가 |
