# 돌 조각 VFX — 크기 티어 믹스 설계

작성일: 2026-07-20

## 목표

돌이 깨질 때 튀는 조각을 **크기 티어(대/중/소)별 풀**로 나눠서 관리하고,
큰 돌은 큰 조각 위주로 나오되 하위 티어 조각이 조금씩 섞이게 한다.

## 현재 구조와 문제

| 위치 | 현재 동작 |
|------|-----------|
| `TileVisualSettings.RockSpriteSet.fragmentSets` | 돌 종류마다 조각 스프라이트 배열(`RockFragmentSet[]`) 보유 |
| `RockSpawner.SpawnRockObject` | `vfx.SetFragmentSets(rock.fragmentSets)` — 그 돌의 세트만 주입 |
| `RockBreakVFX.Play` | 세트 하나를 랜덤 선택 → **세트 안 스프라이트를 전부** 스폰 |
| `RockBreakVFX.Play` | 돌의 `SizeScale`(0.5~1.5)을 모든 조각의 `localScale`에 곱함 |

문제:
- 조각 개수가 배열 길이에 묶여 있어 명시적으로 제어할 수 없다
- 돌 종류를 넘어 조각을 섞을 방법이 없다
- 큰 돌은 조각이 통째로 확대돼 나온다 (원본 픽셀 아트가 뭉개짐)

조각 세트는 **퍼즐 분할이 아니다.** 단순 스프라이트 모음이므로 자유롭게 섞어도 된다.

## 설계

### 1. 데이터

```csharp
public enum RockSizeTier { Small, Medium, Large }
```

**`TileVisualSettings.RockSpriteSet`**
```csharp
public RockSizeTier tier;              // 신규 — 이 돌이 대/중/소 어디인지
public RockFragmentSet[] fragmentSets; // 기존 유지 — 이 돌 고유 조각 스프라이트
```

**`TileVisualSettings.TileVisualData`**
```csharp
public FragmentMixRule[] fragmentMixRules; // 신규 — 티어별 섞기 규칙

[System.Serializable]
public struct FragmentMixRule
{
    public RockSizeTier tier;       // 깨진 돌의 티어
    public int   totalCount;        // 뽑을 조각 기준 개수
    public float weightLarge;       // 대 티어 풀에서 뽑을 가중치
    public float weightMedium;
    public float weightSmall;
    public float countScaleMin;     // SizeScale 최소일 때 개수 배율 (기본 0.7)
    public float countScaleMax;     // SizeScale 최대일 때 개수 배율 (기본 1.3)
}
```

기본값:

| 깨진 돌 티어 | totalCount | 대 | 중 | 소 |
|---|---|---|---|---|
| Large  | 8 | 0.6 | 0.3 | 0.1 |
| Medium | 6 | 0   | 0.7 | 0.3 |
| Small  | 4 | 0   | 0   | 1.0 |

가중치는 합이 1일 필요 없다. 룰렛 선택 시 총합으로 정규화한다.

### 2. 풀 스코프

풀은 **지형(`TileVisualData`) 단위**다. 한 지형의 대/중/소 돌 3종이 가진 조각이
티어 버킷 3개로 묶인다. HardStone 조각과 Ice 조각은 섞이지 않는다.

조각 스프라이트 자체는 지금처럼 돌 종류별로 authoring한다 — 에셋 작업 방식은 안 바뀐다.
`fragmentSets`는 더 이상 "변형 한 벌"이 아니라 단순 인스펙터 그룹핑이며,
런타임에 **전부 평탄화해서** 해당 돌의 티어 버킷에 합쳐진다.

### 3. `RockFragmentPool` (신규)

static 캐시. `TileType`을 키로 티어별 `Sprite[]` 버킷과 `FragmentMixRule`을 보관한다.

- `TileVisualData.rockSpriteSets`를 1회 순회해 빌드하고 캐시. 파괴할 때마다 재순회하지 않는다
- `PickSprite(RockSizeTier brokenTier)` — 규칙의 가중치로 룰렛 → 티어 결정 → 그 버킷에서 스프라이트 1개 랜덤 (중복 허용)
- 폴백: 선택된 버킷이 비어있으면 깨진 돌 자신의 티어 버킷 → 그것도 비면 비어있지 않은 아무 버킷 → 전부 비면 경고 로그 1회 후 스폰 생략
- 규칙이 지정되지 않은 티어는 위 표의 코드 기본값 사용

### 4. `RockBreakVFX.Play(position, scale)` 개편

```
rule  = pool.GetRule(myTier)
t     = InverseLerp(0.5, 1.5, scale)
count = max(1, round(rule.totalCount * Lerp(rule.countScaleMin, rule.countScaleMax, t)))

count번 반복:
    spr = pool.PickSprite(myTier)
    → 기존 물리 스폰 로직 그대로
```

기준 개수 결과 (countScale 0.7~1.3):

| 티어 | scale 0.5 | scale 1.0 | scale 1.5 |
|---|---|---|---|
| Large (8)  | 6 | 8 | 10 |
| Medium (6) | 4 | 6 | 8 |
| Small (4)  | 3 | 4 | 5 |

**조각 크기는 스케일하지 않는다.** 하위 티어 조각은 이미 그에 맞는 스프라이트가 있으므로
항상 원본 크기로 나온다. 돌 크기는 **개수로만** 표현된다.

```csharp
// 제거: go.transform.localScale = new Vector3(scale, scale, 1f);
// 변경: go.transform.position = spawnCenter - (Vector3)spr.bounds.center;  // * scale 제거
```

발사 속도·중력·회전 감쇠·수명·페이드·산란(`scatterX/Y`)·Ghost 레이어 전환은
**전부 기존 코드 그대로**다. 바뀌는 것은 "어떤 스프라이트를 몇 개 뽑느냐"뿐이다.

### 5. 주입 경로

```csharp
// RockSpawner.SpawnRockObject
// 기존: vfx.SetFragmentSets(rock.fragmentSets);
// 변경: vfx.Setup(tileType, rock.tier);
```

- `TerrainDecorator.RockData`: `fragmentSets` 필드 **제거**, `RockSizeTier tier` **추가**
- `RockLayoutCalculator`가 `RockSpriteSet`에서 `RockData`를 채울 때 `tier`를 함께 복사
- `tileType`은 `SpawnRockObject`가 이미 파라미터로 받고 있다
- `RockBreakVFX`는 풀 조회에 필요한 `TileType`·`RockSizeTier`만 들고 있으면 되므로
  스프라이트 배열 참조를 더 이상 보유하지 않는다 (풀 재사용 시 stale 참조 위험 제거)

## 영향 파일

| 파일 | 변경 |
|---|---|
| `_Core/Data/TileVisualSettings.cs` | `RockSizeTier` enum, `RockSpriteSet.tier`, `FragmentMixRule`, `TileVisualData.fragmentMixRules` |
| `Gameplay/Terrain/Tiles/Decoration/RockFragmentPool.cs` | 신규 — 티어 버킷 빌드/캐시/룰렛 |
| `Gameplay/Terrain/Tiles/Decoration/RockBreakVFX.cs` | `Play` 개편, `Setup(tileType, tier)`, 조각 스케일 제거 |
| `Gameplay/Terrain/Tiles/Decoration/RockSpawner.cs` | 주입 호출 변경 |
| `Gameplay/Terrain/Tiles/Decoration/TerrainDecorator.cs` | `RockData.fragmentSets` 제거, `tier` 추가 |
| `Gameplay/Terrain/Tiles/Decoration/RockLayoutCalculator.cs` | `RockData` 생성 시 `tier` 복사 |
| `Gameplay/Terrain/Tiles/Decoration/RockFragmentSet.cs` | 변경 없음 (그룹핑 컨테이너로 유지) |

## 에셋 마이그레이션

`TileVisualSettings` 에셋에서 지형별로:
1. 각 `RockSpriteSet`의 `tier`를 지정 (신규 필드라 기본값 `Small` — **전 항목 확인 필요**)
2. `fragmentMixRules`는 비워두면 코드 기본값으로 동작. 튜닝 시에만 채운다

`tier` 미지정 시 모든 돌이 Small로 취급돼 소형 조각만 4개 나온다. 눈에 띄는 증상이라
누락은 플레이 즉시 드러난다.

## 검증

- 대형 돌 반복 파괴 → 조각에 중·소 티어 스프라이트가 섞여 나오는지 육안 확인
- 소형 돌 파괴 → 소형 조각만 나오는지 확인
- 같은 티어에서 `SizeScale` 최소/최대 돌 비교 → 개수 차이가 보이는지
- 조각이 원본 스프라이트 크기 그대로인지 (확대/축소 없음)
- 얼음 지형 돌에서 돌 조각이 나오지 않는지 (풀 스코프 격리)
- 청크 언로드/재로드 후 파괴 → 풀 재사용 시에도 정상 동작

Unity Test Runner 실행은 사람이 직접 수행한다.

## 하지 않는 것 (YAGNI)

- 조각별 개별 물리 파라미터 (티어마다 다른 발사 속도 등) — 필요해지면 추가
- 티어 4단계 이상 확장
- 조각 오브젝트 풀링 — 현재도 `Destroy` 방식이며 이번 변경으로 개수가 크게 늘지 않는다
