# 돌 프리팹 전환 + 히트 애니메이션 설계

작성일: 2026-08-17

## 배경

돌 1종의 정의(정상/균열1/균열2 스프라이트 + 크기 티어 + 조각 스프라이트)가
`TileVisualSettings.rockSpriteSets[]`라는 **구조체 리스트**에 들어 있었고,
`RockSpawner`가 런타임에 `new GameObject()` + `AddComponent` 5개로 돌을 조립했다.

바뀐 이유는 하나다: **돌이 맞을 때 흔들리는 애니메이션을 붙이려면 Animator가 필요하고,
Animator는 프리팹에 있어야 한다.**

## 1. 프리팹 구조 — 전부 root 한 GameObject

```
Rock_HardStone_0_Large (프리팹 root)
├─ SpriteRenderer          정상 스프라이트
├─ PolygonCollider2D       SpriteRenderer 모양에서 자동 생성
├─ DamageStagedVisuals     stages[0]=정상 [1]=균열1 [2]=균열2
├─ DiggableRock            baseHp·recoveryDelay·minExposedPixels를 돌마다 다르게
├─ RockBreakVFX            tier + fragmentSets
├─ Animator                히트 흔들림 클립
└─ RockHitAnimator         Animator 구동 + 배치값 합성
```

`TileVisualSettings`는 `List<GameObject> rockPrefabs`만 들고 있다.

### ⚠ 자식으로 쪼개면 안 되는 이유

"Animator가 transform을 덮어쓰니 root(스포너 소유) / Visual 자식(애니메이션)으로
분리하자"는 안을 먼저 검토했고 **기각했다.**

프로젝트 전체가 "**콜라이더와 `IDiggable`은 같은 GameObject**" 규약 위에 서 있다.
파기 호출부가 전부 히트한 콜라이더에서 `GetComponent`를 부른다 —
`GetComponentInParent`가 아니다:

| 호출부 | 코드 |
|---|---|
| `Digger.cs:155` | `hit.TryGetComponent(out IDiggable diggable)` |
| `Digger.cs:239` / `288` | `hit.GetComponent<IDiggable>()` |
| `SapStrategy.cs:301` | `hitCollider.GetComponent<IDiggable>()` |
| `PickaxeStrategy.cs:242` | `hitCollider.GetComponent<IDiggable>()` |
| `PlasmaCutterRelic.cs:109` | `hit.collider.GetComponent<DiggableRock>()` |

`StarResetLever.cs:8`에는 이 규약이 주석으로 명시돼 있다.
추가로 `PolygonCollider2D`는 **같은 GameObject의 SpriteRenderer**에서 모양을 굽기 때문에,
SR을 자식으로 내리면 돌 프리팹마다 콜라이더 폴리곤을 손으로 그려야 한다.

→ 구조는 건드리지 않고, 흔들림 충돌만 코드로 푼다.

## 2. 흔들림 충돌 — 왜 Animator만 붙이면 깨지는가

`RockSpawner`가 돌의 `localPosition` / `localRotation` / `localScale`을 확정해서 넣는다.
회전은 **연출용 랜덤이 아니다** — `RockLayoutCalculator`가 배치 단계에서 결정하고,
마스크 변환(`TransformMask`) · 노출 판정 · 세이브가 전부 이 값에 물려 있다.

Animator는 클립 값을 transform에 **절대값으로** 쓴다.
클립이 Transform을 건드리는 순간 그 배치값이 매 프레임 지워진다.

### 해법: Update 원점복귀 → Animator → LateUpdate 합성

Unity 실행 순서는 `Update` → (Animator 평가) → `LateUpdate`다.

| 단계 | 하는 일 |
|---|---|
| `Update` | transform을 원점(0 / identity / 1)으로 되돌린다 |
| Animator | 클립이 애니메이트하는 속성만 덮어쓴다. 나머지는 원점으로 남는다 |
| `LateUpdate` | 남은 값을 오프셋으로 보고 `base ∘ clip` 합성 |

`PlayerSortingController`가 애니메이션이 덮어쓰는 `sortingOrder`를 LateUpdate에서
재적용하는 것과 같은 패턴이다 (CLAUDE.md §16).

**`Update`의 원점복귀가 핵심이다.** 이게 없으면 클립이 예컨대 `localPosition`만
애니메이트할 때 rotation/scale은 지난 프레임에 우리가 써 넣은 합성 결과를 들고 있고,
그걸 다시 오프셋으로 곱해 **매 프레임 발산한다.** 이 단계 덕분에 클립이
pos/rot/scale 중 무엇을 애니메이트하든 안전하다.

회귀 테스트: `Assets/Tests/EditMode/RockHitAnimatorTests.cs`

### 클립 제작 규칙

- **원점 기준으로 만든다.** position 0, rotation 0, scale 1에서 흔든다.
  돌의 실제 배치(회전 -100~100도 등)는 코드가 얹는다.
- `RockHitAnimator.duration`을 클립 길이에 맞춘다. 이 값은 **하드 타임아웃**도 겸해서,
  클립을 실수로 Loop로 만들어도 컴포넌트가 영원히 켜진 채 남지 않는다.

## 3. 성능 — Animator를 상시로 켜두지 않는다

`viewDistance: 1` → 로드 청크 3×3 = 9개, 청크당 돌 4개 → 화면에 약 36개.

| 상태 | 프레임 비용 |
|---|---|
| 안 맞고 있는 돌 (~35개) | **0** — Animator·`RockHitAnimator` 둘 다 비활성 |
| 맞고 있는 돌 (1개, 빔이면 2~3개) | Update+LateUpdate 각 1회, ~0.15초 |

비활성 MonoBehaviour는 Unity 메시지 리스트에서 아예 빠진다. 조기 return과 달리
native→managed 디스패치 비용조차 내지 않는다.

**진짜 피하려는 건 LateUpdate가 아니라 Animator다.** 활성 Animator는 클립이 단순해도
개당 5~20μs라 36개를 상시로 켜두면 0.2~0.7ms다. LateUpdate 디스패치는 36개를 다 켜도
0.005ms 수준이라 애초에 문제가 아니었다.

`RockHitAnimator`는 `GetComponents<IRockHitReactor>()`로 수집되는데, **비활성 컴포넌트도
수집되고 메서드 직접 호출도 정상 동작**하므로 평소 꺼둬도 히트 경로는 살아 있다.

## 4. ⚠ 동작 변경 — transform이 더 이상 "배치 확정값"이 아니다

이번 작업에서 같이 고친 **실제 버그 2개**. 둘 다 원인이 같다:
transform이 암묵적으로 배치값 역할을 겸하고 있었고, 흔들림이 들어오면 깨진다.

### (1) 파괴 시 구멍이 엉뚱한 곳에 뚫린다

`DiggableRock.DestroyRock()`이 `transform.localPosition`으로 `TerrainCarver.ClearHole`의
픽셀 좌표를 계산했다. 흔들리는 중에 치명타가 들어오면 구멍이 오프셋만큼 어긋나
돌 모양의 지형이 유령처럼 남는다.

`PlasmaCutterRelic`은 **매 프레임** `Dig()`를 호출하므로(빔), 이론적 위험이 아니라
확정적으로 재현된다.

### (2) 흔들림 오프셋이 세이브에 영구히 구워진다

`WorldPersistenceSystem`이 `dr.transform.localEulerAngles.z` / `localScale.x`를
그대로 저장했다. 흔들리는 중에 청크가 언로드되면 그 오차가 세이브에 남아
재로드 때마다 되살아난다.

### 수정

`DiggableRock`이 배치값을 **명시 필드**로 들고, 픽셀 좌표·세이브가 transform 대신
그걸 읽는다.

```csharp
public void SetPlacement(Vector2 anchorLocal, float baseAngleZ)  // RockSpawner가 주입
public float BaseAngleZ { get; }                                 // 세이브가 사용
private Vector2 AnchorLocal { get; }                             // 픽셀 좌표 계산이 사용
```

프리팹·씬에 직접 배치된 돌(`selfRegister`)은 스포너를 안 거치므로
`Start()`에서 자기 transform으로 자동 확정한다.

**앞으로 돌의 지형 좌표를 계산하는 코드를 추가할 때 `transform.localPosition`을 읽지 말 것.**
`AnchorLocal`을 쓴다. 세이브에 회전을 넣을 일이 있으면 `BaseAngleZ`를 쓴다.

## 5. 히트 훅을 `IDamageStageable`로 하지 않은 이유

`IDamageStageable.OnHpRatioChanged`는 스폰·풀 재사용·HP 회복 때도 `ratio=1`로 불린다.
"지금 맞았다"를 구분할 수 없어서, 이걸로 히트 연출을 걸면 **돌이 스폰될 때마다 재생된다.**

그래서 `IRockHitReactor`를 따로 뒀다. `DiggableRock.Dig()`의 타격 경로에서만 불리고,
치명타(HP 0)일 때는 호출되지 않는다 — 그 순간은 `RockBreakVFX`의 파괴 연출이 대신한다.

## 6. 같이 고친 잠복 버그

`RockSpawner`의 마스크 크기 조회가 `_maskSizeCache[poolKey]`였다.
`poolKey`가 우연히 스프라이트 InstanceID였기 때문에 동작하던 코드다.
풀 키가 **프리팹** InstanceID로 바뀌면서 `KeyNotFoundException`이 날 자리라
`TryGetMaskSize(rock.sprite, ...)`로 교체했다.

## 7. 마이그레이션

`Tools/Rock/Migrate RockSpriteSets → Prefabs` (`Assets/Scripts/Editor/RockPrefabMigrator.cs`)

구 `rockSpriteSets` 데이터를 읽어 `Assets/Prefabs/Rocks/`에 프리팹을 굽고
`rockPrefabs`를 채운다. **리스트 순서를 보존한다** —
`RockSaveEntry.spriteSetIndex`가 이 인덱스라 순서가 바뀌면 세이브된 돌의 종류가 뒤바뀐다.
스프라이트가 없어 프리팹 생성이 실패한 자리는 `null`을 넣어 인덱스를 밀지 않는다.

마이그레이션이 끝나면 삭제할 것:
- `TileVisualSettings.RockSpriteSet` 구조체
- `TileVisualData.rockSpriteSets` 필드
- `RockPrefabMigrator.cs`

## 8. 검토했지만 안 한 것

- **Animator 없이 코드로 흔들기** (`AnimationCurve` 기반 컴포넌트).
  Animator/클립 비용이 아예 없고 발산 위험도 없다. 하지만 애니메이션을 클립으로
  만들겠다는 게 이 작업의 전제라 기각.
- **프리팹 대신 ScriptableObject로 돌 정의.** 데이터만 보면 SO가 더 맞지만,
  Animator를 붙일 수 없어서 목적을 달성하지 못한다.
- **`RockPrefabRegistry` 캐시.** `RockLayoutCalculator`가 슬롯당 1회
  `prefab.GetComponent<SpriteRenderer>()`를 부른다. 청크 로드당 4회라 캐시 계층을
  만들 가치가 없다고 판단.

## 9. 2026-09-04 회귀 — 파괴 좌표가 청크 원점으로 튐

증상: "작은 돌을 깼는데 광물이 하나도 안 나온다."

실제로는 나왔다. 청크 원점에서 나왔을 뿐이다.
버그리포트 로그(`MineralLifetime.VerboseLog`)에 `Iron(Clone)`이 **(20.05, -70.00)** =
청크 (2,-7)의 로컬 (0,0)에 두 개 떨어진 뒤 미로드 청크 (2,-8)로 굴러떨어진 기록이 남았다.
바로 앞의 다른 돌은 (27.25,-64.97)에 정상 드롭했다 — **간헐적**이다.

### 원인

`RockHitAnimator`는 `Update()`에서 transform을 원점으로 비우고 `LateUpdate()`에서
base ∘ clip 을 합성한다(§ 위쪽 참고). 그래서 **흔들림이 켜져 있는 프레임의 Update 구간**에는
transform이 `(0,0,0)` 상태로 놓여 있다.

`DiggableRock.Dig()`도 Update 경로에서 불린다. 흔들림이 아직 살아 있는 동안(`duration` 0.15s)
치명타가 들어오면 `DestroyRock()`이 그 원점 transform을 읽는다.
`ClearHole`은 `AnchorLocal`을 써서 무사했지만, **조각 VFX·파괴 효과음·
`NotifyTerrainDug`·광물 드롭·유물 드롭이 전부 `_spriteRenderer.bounds.center`** 를 읽고 있었다.
→ 전부 청크 원점으로. 작은 돌에서 잘 보이는 이유는 연타 몇 번에 죽어서
흔들림이 살아 있는 동안 치명타가 들어올 확률이 높기 때문이다.

### 수정

`IRockHitReactor`에 `OnRockBreaking()`을 추가하고 `DestroyRock()`의 **첫 줄**에서 브로드캐스트한다.
`RockHitAnimator`는 이를 `Stop()`(= 배치 확정값 복원)으로 구현한다.
어차피 사라질 돌이라 흔들림을 끊는 시각적 손해는 없고, bounds를 읽는 소비처
다섯 곳을 한 번에 고친다.

⚠ `DestroyRock()`에 transform·bounds를 읽는 코드를 새로 넣을 때는 **이 브로드캐스트 아래**에 넣을 것.
회귀 테스트: `RockHitAnimatorTests.OnRockBreaking_RestoresBasePlacement_WhenClipHasZeroedTransform`.
