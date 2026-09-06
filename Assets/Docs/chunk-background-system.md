# 청크 배경 시스템 설계 (2026-07-19)

청크가 로드될 때 그 청크 뒤에 배경 스프라이트를 함께 붙인다. 배경 종류는 깊이(층)에 따라 달라진다.

---

## 1. 배경

기존 `Assets/Scripts/Render/World/BackgroundManager.cs`는 플레이어 주변에 배경 타일을 **독립 격자**로 깔았다. 문제 두 가지:

- **격자 어긋남** — 타일 인덱스는 `floor(pos / tileSize)`로 구하는데 스폰 위치는 `index * tileSize`를 *중심*으로 쓴다. 플레이어 기준으로 반 타일 어긋나 `renderDistance`가 작으면 가장자리에 빈틈이 보인다.
- **지형과 무관한 수명** — 청크 로드/언로드와 별개로 돌아가고, `ceilingLevel` 컷오프를 따로 관리해야 한다.

배경을 청크에 종속시키면 둘 다 사라진다. 청크는 항상 10유닛 고정(`ChunkCoords.WorldSize`)이라 배경 격자가 지형 격자와 정확히 일치하고, 수명은 청크 풀이 알아서 관리한다.

---

## 2. 구성 요소

| 요소 | 위치 | 역할 |
|---|---|---|
| `ChunkBackgroundTableSO` | `Assets/Scripts/_Core/Data/` | `TileType → Sprite[]` 매핑 에셋 |
| `ChunkBackground` | `Assets/Scripts/Render/World/` | 청크 1개의 배경 렌더러. 순수 C# 클래스 |
| `ChunkBackgroundTableCreator` | `Assets/Scripts/Utils/Editor/` | 메뉴에서 .asset 생성 |

`TerrainVisualizer`·`TerrainCollider`를 `TerrainChunk`에서 분리해 둔 기존 패턴을 따른다. `TerrainChunk`는 `ChunkBackground` 인스턴스를 하나 들고 위임만 한다.

### 2.1 `ChunkBackgroundTableSO`

```csharp
[CreateAssetMenu(menuName = "Terrain/Chunk Background Table")]
public class ChunkBackgroundTableSO : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public TileType layer;
        public Sprite[] sprites;
    }

    [SerializeField] private Entry[] entries;

    // OnEnable에서 Dictionary<TileType, Sprite[]>로 캐시
    public Sprite[] GetSprites(TileType layer);
}
```

**필드 4개로 고정하지 않고 엔트리 배열을 쓰는 이유:** `TileType` enum에 구 7층 잔재(`HardStone`/`CoolStone`/`HotStone`)가 아직 남아 있고 정리 예정이다. 엔트리 방식이면 enum이 바뀌어도 SO 구조가 안 깨진다.

현재 `tileData.json` 기준 실사용 층은 4개다.

| 층 | TileType | startDepth (청크 Y) |
|---|---|---|
| 1 | Dirt | 0 |
| 2 | Ice | −20 |
| 3 | MagmaRock | −40 |
| 4 | MeteoriteRock | −60 |

### 2.2 데이터 주입 — static

`ChunkBackground.Table`을 **static**으로 두고 `InfinityMapManager.Awake`에서 1회 주입한다.

`Instantiate`는 non-serialized 필드를 프리팹에서 복사하지 않는다. 전체 인스턴스에 적용할 설정은 static을 쓴다 — `TerrainChunk.s_colliderUpdateInterval`과 같은 패턴(CLAUDE.md 아키텍처 제약 4항).

---

## 3. 생명주기

### `TerrainChunk.Awake`

자식 GameObject 1개를 코드로 생성하고 `SpriteRenderer`를 붙인다.

| 설정 | 값 | 근거 |
|---|---|---|
| `sortingOrder` | −3 | 기존 `BackgroundTile.prefab`과 동일. 지형보다 뒤 |
| `drawMode` | `Tiled` | |
| `size` | 10 × 10 | 청크 1칸 = 10유닛 |
| local position | (5, 5) | 청크 원점이 좌하단, 배경 스프라이트 pivot이 중심 |

청크 스프라이트는 pivot `(0,0)`으로 생성되므로(`TerrainChunk.cs`) 청크 transform 위치 = 좌하단 모서리다. 따라서 배경을 청크 중앙에 놓으려면 local (5, 5)다.

> **에셋 요구사항:** 배경 스프라이트는 **pivot = Center**로 임포트해야 한다. Bottom-Left로 임포트하면 배경이 청크 밖으로 밀린다. 런타임에서 pivot을 보정하지 않는다.

**프리팹을 건드리지 않는다.** 코드 생성이므로 특수 청크 프리팹까지 렌더러가 자동으로 만들어진다. 프리팹마다 자식을 배치하면 CLAUDE.md 8항의 local position 함정(청크 루트 월드 좌표에 따라 값이 틀어짐)에 걸리는데, 그것도 함께 피한다.

### `Reuse_Step2_Finalize` — 스프라이트 할당

1. `TileDataManager.Instance.GetTileTypeAtDepth(ChunkY)` → 층 판정
2. `Table.GetSprites(layer)` → 후보 배열
3. `(ChunkX, ChunkY)` 해시를 시드로 `System.Random` → 스프라이트 결정

**반드시 `Reuse_Step2_Finalize`여야 한다. `Reuse_Step1_Prepare`에 넣으면 안 된다.**

`Reuse_Step1_Prepare`는 `StandardChunkFactory`에서만 호출된다. **특수 청크는 이 메서드를 거치지 않는다**(`SpecialChunkFactory` 코멘트에 명시). 여기에 스프라이트 할당을 넣으면 특수 청크는 렌더러만 생기고 스프라이트가 영영 비어 폴백 경로로 빠진다 — 특수 청크에서만 배경이 사라진다.

반면 `Reuse_Step2_Finalize`는 `ChunkGenerationPipeline`이 `isSpecialPrebuilt` 분기보다 **먼저 무조건** 호출하므로 표준·특수 두 경로가 모두 지나간다.

시드 해시는 기존 `BackgroundManager`의 방식을 그대로 쓴다.

```csharp
int seed = (chunkX * 73856093) ^ (chunkY * 19349663);
```

같은 좌표는 항상 같은 배경이 나오므로, 청크가 언로드 후 재로드돼도 배경이 바뀌지 않는다.

### `ChunkSpawner`의 자식 정리 루프 — 반드시 예외 처리

`ChunkSpawner.DecorateChunk_Phase2_Steps`는 데코레이터를 돌리기 전에 청크 자식을 정리하는데,
`ROCK_`/`MINERAL_` 접두사가 **아닌 자식은 전부 `Destroy`** 한다.

`ChunkGenerationPipeline.ExecutePhase2_DecoratorSteps`에서 이 정리가 `Reuse_Step2_Finalize()` **바로 다음**에
실행되므로, 예외 처리를 안 하면 `Refresh()`가 성공 로그까지 찍고 배경을 켠 직후에 그 자식이 파괴된다.
로그는 정상인데 화면에는 아무것도 없는 상태가 된다.

배경은 `TerrainChunk.Awake`에서 **1회만** 생성되므로 한 번 파괴되면 그 풀 인스턴스는 영영 배경이 없다.
`ChunkBackground.ChildName` 상수로 이름을 맞추고 정리 루프에서 `continue` 한다.

> 특수 청크는 `DecorateMineralsOnly`(파괴 루프 없음)를 타므로 이 버그의 영향을 받지 않았다.
> **특수 청크에서만 배경이 보인다면 이 증상이다.**

### 풀 반환

별도 처리 없음. 배경이 청크의 자식이라 청크가 비활성화되면 같이 꺼진다.

---

## 3.5 벽타기 판정면 — 배경이 겸한다

`PlayerController`의 벽타기는 [`bodyCollider.IsTouchingLayers(wallLayer)`] 한 줄로만 판정하고,
`NewPlayer.prefab`의 `wallLayer`는 **레이어 21 `Wall`** 만 본다(`m_Bits: 2097152`).

구 `BackgroundTile.prefab`이 `m_Layer: 21` + `BoxCollider2D(isTrigger)` 를 들고 있었다.
**즉 벽타기 판정면을 제공하던 것이 배경 타일이었다** — `BackgroundManager`를 끄면 벽타기가 통째로 죽는다.
(코드에서 `Wall` 레이어를 쓰는 곳은 이 플레이어 마스크가 유일하다.)

그래서 배경 자식 GameObject가 이 역할까지 이어받는다.

| 설정 | 값 |
|---|---|
| `GameObject.layer` | `Wall`(21) — 렌더링에는 영향 없음(카메라 컬링 마스크 = Everything) |
| `BoxCollider2D.isTrigger` | `true` — 이동을 막으면 안 된다 |
| 크기 | 청크 10×10, 단 `WallCeilingY` 위는 잘라낸다 |

`WallCeilingY`는 구 `BackgroundManager.ceilingLevel`(씬 값 **9**)을 옮긴 것으로,
지상에서 공중 벽타기가 되는 것을 막는다. `InfinityMapManager`의 `wallClimbCeilingY`에서 주입한다.

**트리거 갱신은 `Refresh()`의 스프라이트 early-return보다 먼저** 해야 한다.
스프라이트가 없는 층에서도 벽타기는 되어야 하기 때문이다.

> 구 방식은 플레이어 주변 `renderDistance:4`(±4유닛)에만 타일을 깔았고,
> 새 방식은 **로드된 모든 청크**에 깔린다. 판정 범위가 넓어지지만 `Wall` 레이어를
> 참조하는 다른 시스템이 없어 부작용은 없다.

---

## 4. 폴백

해당 층 엔트리가 없거나 `sprites` 배열이 비면 `SpriteRenderer.enabled = false`. 배경이 없는 상태가 될 뿐 에러는 나지 않는다.

**현재 층별 지하 배경 스프라이트 에셋은 아직 없다.** `Assets/Sprites/World/BackGround/`는 지상 하늘·구름·산 계열이고, 지하용은 `SpeciaChunk/LegacyTiles/StoneBackground.png` 정도만 있다. 에디터 툴은 빈 슬롯 4개짜리 에셋을 만들어 주고, 스프라이트는 이후에 채운다. 그 전까지는 전 층이 폴백 경로로 들어가 배경이 안 보인다 — 정상 동작이다.

---

## 5. 에디터 툴

`ChunkBackgroundTableCreator` — 메뉴 항목에서 실행하면 `Assets/Data/ChunkBackgroundTable.asset`을 생성하고, `tileData.json`에 정의된 층으로 엔트리를 미리 채운다(스프라이트는 비움). 에셋이 이미 있으면 덮어쓰지 않고 Project 창에서 선택만 한다.

---

## 6. 기존 `BackgroundManager` 처리

**코드는 삭제하지 않되, 지하 씬에서는 오브젝트를 비활성화해야 한다.**

`BackgroundManager`는 주 지하 씬 `Assets/Scenes/Demo/DemoUnderground.unity`에 실제로 배치돼 있다(그 외 `Test_DongJin/CopyDemoUnderground`, `Test_Hanbin/khbScene 1`, `Test_Hanbin/Specialchunk`).

문제는 `BackgroundTile.prefab`의 `sortingOrder`가 −3이고 새 배경도 −3이라는 점이다. 같은 sorting layer에서 order가 같으면 그리기 순서가 불확정이라 두 배경이 겹쳐 깜빡인다.

따라서 지하 씬에서는 `BackgroundManager` 오브젝트를 **비활성화**한다. 삭제가 아니라 비활성화인 이유는 새 배경에 문제가 생겼을 때 즉시 되돌리기 위해서다. 지상 씬에서의 사용 여부를 확인한 뒤, 지하 전용이었다면 그때 코드까지 정리한다.

---

## 7. XRayController 연동

X-ray 유물은 지형·배경·오브젝트를 서로 다른 톤의 머티리얼로 칠해 구분시킨다. `XRayController.ScanWorld`는 청크에 `GetComponentsInChildren<SpriteRenderer>`를 돌리므로, **배경 자식이 지형(Terrain) 톤에 휩쓸린다** — 배경과 지형이 같은 색이 되어 X-ray의 의미가 사라진다.

`SwapRenderers`는 `_tracked`에 이미 있는 렌더러를 건너뛴다(선착순). 이를 이용해 **배경을 먼저** `XRayTone.Background`로 잡은 뒤 청크 전체를 `Terrain`으로 스왑한다. 순서를 바꾸면 안 된다.

`TerrainChunk.BackgroundObject`가 이 목적으로 배경 GameObject를 노출한다.

기존 `BackgroundManager` 분기(`FindFirstObjectByType<BackgroundManager>`)는 그대로 뒀다. 씬에서 비활성화하면 `FindFirstObjectByType`이 비활성 오브젝트를 제외하므로 자연히 no-op이 된다.

---

## 8. 보류 항목

**층 경계 블렌딩** — 층이 바뀌는 청크 줄에서 배경이 칼같이 전환된다. 실제로 보고 어색한지 판단한 뒤 결정한다. 지형 자체는 `TerrainBlender`로 층을 섞고 있어서, 배경만 독자적으로 섞으면 오히려 지형과 어긋날 수 있다.
