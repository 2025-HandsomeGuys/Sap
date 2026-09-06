# 특수청크 나침반 (Special Chunk Compass) 설계

작성일: 2026-07-09

## 목적

플레이어에게 가장 가까운 특수청크의 **방향**을 알려준다.
1차 구현은 플레이어 머리 위 화살표. 이후 미니맵 시스템이 같은 탐색 코어를 재사용한다.

## 핵심 원칙

**탐색(어디 있나)** 과 **표시(어떻게 보여주나)** 를 완전히 분리한다.
- 탐색 코어는 뷰를 모른다. 화살표든 미니맵이든 코어를 구독만 한다.
- 탐색 대상은 **실제로 스폰된 특수청크 앵커만**(`SpecialChunkManager.ActiveAnchors`).

### 왜 결정론적 예측을 쓰지 않는가 (설계 변경 이력)

초안은 `SpecialChunkManager.GetSpecialChunk`(= `SpecialChunkSelector.TrySelect`)로
좌표에서 "특수청크가 생길까?"를 **예측**해 로드 없이 먼 곳까지 가리키려 했다.
그러나 이 예측기는 **실제 배치와 무관하게** 임의 좌표를 평가한다:
`GetTileTypeAtDepth`는 매칭 층이 없으면 fallback으로 `Dirt`를 반환하므로
`y=10000`(정상 월드 밖 던전 스테이징 영역)도 Dirt 층으로 취급되어,
Dirt pool에 depth 제한 없는 특수청크가 있으면 **거기 특수청크가 있다고 오탐**한다.
→ 나침반이 하늘(월드 밖)을 가리키는 버그.

그래서 **실제 스폰된 앵커만 추적**으로 전환했다. `SpecialChunkManager`가 스폰 시
`_activeAnchors[coord] = chunkType` 등록, 언로드(`UnregisterSubChunksForAnchor`) 시 제거.
- 장점: 월드 밖 유령 없음. 기믹 종류(`chunkType`)를 알므로 종류 필터도 즉시 가능.
- 한계: **현재 로드된(플레이어 근처) 특수청크만** 대상. 언로드된 먼 특수청크는 안 가리킴
  (특수청크는 근접 시 로드되므로 실사용엔 충분).

## 재사용하는 기존 API

| API | 용도 |
|-----|------|
| `TileDataManager.Instance.GetTileTypeAtPosition(x, y)` | 청크 좌표 → 레이어(TileType) |
| `SpecialChunkManager.Instance.GetSpecialChunk(coord, layer, seed)` | 해당 좌표에 특수청크 앵커 존재 시 프리팹 반환(non-null), 없으면 null |
| `ChunkCoords.ToWorld(coord)` / `ToChunk(worldPos)` | 청크↔월드 좌표 변환 (피벗 좌하단, 1칸=10 units) |
| `InfinityMapManager.Instance.worldSeed` | 월드 시드 |

> 주의: `GetSpecialChunk`는 앵커 좌표에서만 non-null. 서브/링크 피스 좌표는 앵커가 아니므로
> 탐색은 앵커 기준으로만 동작하면 충분하다(가장 가까운 "특수청크 위치" = 앵커).

## 컴포넌트

### 1. 탐색 코어 (view-agnostic, 순수 C#)

```csharp
public struct CompassTarget
{
    public Vector2Int coord;        // 앵커 청크 좌표
    public Vector2 worldPos;        // 앵커 청크 중심 월드 좌표
    public SpecialChunkType type;   // 특수청크 종류 (미니맵/필터 확장용)
}

public interface ISpecialChunkLocator
{
    bool TryFindNearest(Vector2Int origin, out CompassTarget target);
}
```

**`DeterministicRingLocator : ISpecialChunkLocator`**
- 생성자: `DeterministicRingLocator(int maxRadius, Predicate<SpecialChunkType> filter = null)`
- `origin` 청크에서 링(반경 r = 0 → maxRadius) 바깥으로 스캔.
  - 각 좌표 c마다: `layer = GetTileTypeAtPosition(c.x, c.y)` →
    `prefab = GetSpecialChunk(c, layer, seed)`.
  - `prefab != null` 이고 `filter == null || filter(type)` 이면 발견.
- 같은 반경 링 안에서는 origin과의 실제 거리(제곱거리)가 최소인 좌표를 채택 → 가장 가까운 것 보장.
- 첫 발견 링에서 후보를 모아 최소거리 선택 후 반환. 이후 링은 보지 않는다(바깥 링일수록 무조건 멀기 때문).
- 없으면 false.
- `worldPos = ChunkCoords.ToWorld(coord) + (WorldSize/2, WorldSize/2)` (청크 중심).

**특수청크 종류 얻기**: `GetSpecialChunk`는 프리팹만 반환한다.
프리팹의 `SpecialChunkType`를 얻기 위해 `SpecialChunkManager`에 조회 헬퍼를 추가하거나,
1차 구현은 `type = SpecialChunkType.None`으로 두고 확장 시 배선한다.
→ **1차는 프리팹→type 매핑을 `SpecialChunkManager`에 얇은 public 메서드로 추가**
  (`GetSpecialChunkType(coord, layer, seed)`), locator가 이를 채운다. 방향만 쓰면 type은 무시 가능.

**확장점**
- `filter` 파라미터로 "특정 종류만" 지원(지금은 항상 null → 아무거나).
- `maxRadius`는 나침반이 주입(기본 8).

### 2. `SpecialChunkCompass` (MonoBehaviour — 코어 허브)

```csharp
[SerializeField] Transform player;
[SerializeField] float scanInterval = 0.5f;
[SerializeField] int maxRadius = 8;

public CompassTarget? CurrentTarget { get; private set; }
public event Action<CompassTarget?> OnTargetChanged;
```

- `ISpecialChunkLocator`를 생성/보유(`new DeterministicRingLocator(maxRadius)`).
- `scanInterval` 주기로만 스캔(매프레임 X). 타이머 누적 방식.
- 스캔: `origin = ChunkCoords.ToChunk(player.position)` → `locator.TryFindNearest`.
- 결과가 이전과 다르면(coord 비교) `CurrentTarget` 갱신 + `OnTargetChanged` 발생.
- 의존성 준비 안 됨(매니저 null) 시 스캔 skip.
- **미니맵은 이후 이 컴포넌트의 `CurrentTarget`/`OnTargetChanged`만 구독**한다. 표시 로직 없음.

### 3. `HeadCompassArrow` (표시 뷰 — 1차 유일 뷰)

```csharp
[SerializeField] Transform player;
[SerializeField] SpecialChunkCompass compass;
[SerializeField] Transform arrow;      // 회전시킬 스프라이트 루트
[SerializeField] float headOffsetY = 1.2f;
```

- 매 `LateUpdate`:
  - 화살표 루트를 `player.position + (0, headOffsetY)`로 이동(머리 위 따라다님).
  - `compass.CurrentTarget`이 있으면:
    - `dir = (target.worldPos - (Vector2)player.position)` → 각도 계산 →
      `arrow.rotation = Quaternion.Euler(0,0, atan2(dir.y,dir.x)*Rad2Deg - 90)` (스프라이트가 위 방향 기준일 때 -90 보정).
    - 화살표 표시(SetActive true).
  - 없으면 화살표 숨김(SetActive false).
- 거리 텍스트 등은 지금 미구현. 필요 시 `CurrentTarget.worldPos` 거리로 추가(훅만 열어둠).

## 데이터 흐름

```
player 이동
  → SpecialChunkCompass (0.5s마다)
       origin = ToChunk(player.pos)
       DeterministicRingLocator.TryFindNearest(origin)
          링 스캔: GetTileTypeAtPosition → GetSpecialChunk (로드 불필요)
       → CurrentTarget 갱신 + OnTargetChanged
  → HeadCompassArrow (매 프레임)
       머리 위 위치 갱신 + 타겟 방향 회전/표시
  → (이후) 미니맵도 CurrentTarget 구독
```

## 씬 배치

- `SpecialChunkCompass`: 플레이어 또는 매니저 오브젝트에 부착, player 참조 연결.
- `HeadCompassArrow` + 화살표 스프라이트: 월드 스페이스 오브젝트(플레이어 자식 아님 — 회전 독립).
  - 화살표 스프라이트는 "위(+Y)" 방향 기준 에셋 가정, 보정각으로 정렬.

## 비용

- 반경 8 = 최대 17×17 = 289 좌표 평가 × (0.5초마다) → 무시 가능.
- 각 좌표 평가는 순수 계산(딕셔너리/해시 조회), 청크 로드/할당 없음.

## 확장 로드맵 (지금 구현 안 함, 코드만 열어둠)

1. **종류 지정**: `DeterministicRingLocator`에 `filter` 주입 → UI에서 종류 선택.
2. **미니맵 표시**: `SpecialChunkCompass.CurrentTarget` 구독하는 두 번째 뷰 추가.
3. **다중 타겟**: locator에 `FindAllInRadius` 추가(미니맵 다중 마커용).

## 테스트

- EditMode: `DeterministicRingLocator`를 `ISpecialChunkLocator` 목/스텁 없이,
  좌표 평가 델리게이트를 주입 가능하게 만들어 순수 링 스캔 로직(가장 가까운 것 선택, 없을 때 false) 검증.
  → locator가 `SpecialChunkManager`를 직접 참조하지 않고, 좌표→(hasSpecial, type) 조회 델리게이트를 받도록 설계하면 테스트 용이 + 결합도↓.
- 실행 테스트는 사람이 Unity에서 수행(플레이어 이동 시 화살표 회전/숨김 확인).
