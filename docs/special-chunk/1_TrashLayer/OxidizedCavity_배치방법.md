# OxidizedCavity 특수 청크 배치 방법
@tags: special-chunk, oxidized-cavity, setup, guide, placement, prefab

## 개요

OxidizedCavity는 **4청크 너비** 멀티 청크 특수 구역이다.
`SpecialChunkManager`의 `pools` 리스트에 anchor + subChunks 방식으로 등록한다.

---

## 사용 프리팹

| 역할 | 프리팹 경로 | 설명 |
|------|------------|------|
| Anchor (Start) | `Prefabs/World/SpecialChunks/OxidizedCavityChunk.prefab` | 앵커 청크. TerrainChunk + ZoneTrigger 포함 |
| Middle | `Prefabs/World/SpecialChunks/Trash/OxidizedCavityChunk_Sub.prefab` | 중간 청크 (2개 사용) |
| End | `Sprites/UI/ground/OxidizedCavityChunk_SubEnd.prefab` | 마지막 청크 |
| 레이아웃 참고 | `Prefabs/World/SpecialChunks/OxidizedCavityChunk1.prefab` | 에디터 시각 확인용 (등록 X) |

> **주의:** `OxidizedCavityChunk1.prefab`은 위치 확인용 참고 프리팹이다.
> SpecialChunkManager에는 **절대 등록하지 않는다** (루트에 TerrainChunk 없음).

---

## Inspector 등록 방법

Unity Editor에서 `SpecialChunkManager` 오브젝트를 선택한다.

### 1. `pools` 리스트에서 대상 레이어 풀 선택

`targetLayer`가 `CoolStone` (또는 OxidizedCavity가 등장할 레이어)인 풀을 찾는다.
없으면 `+` 버튼으로 새 `SpecialChunkPool` 추가 후 `targetLayer` 설정.

### 2. `chunks` 리스트에 새 항목 추가

`chunks` 리스트의 `+` 버튼 클릭 → 아래와 같이 설정:

```
SpecialChunkDef
├─ prefab         : OxidizedCavityChunk       ← Anchor 프리팹 드래그
├─ spawnChance    : 원하는 확률 (예: 20%)
├─ chunkType      : DropSpike
├─ minDepth       : 0  (제한 없으면 0)
├─ maxDepth       : 0  (제한 없으면 0)
└─ subChunks      : (크기 3)
    [0] offset: (1, 0)  prefab: OxidizedCavityChunk_Sub
    [1] offset: (2, 0)  prefab: OxidizedCavityChunk_Sub
    [2] offset: (3, 0)  prefab: OxidizedCavityChunk_SubEnd
```

---

## 청크 배치 좌표 구조

앵커가 청크 좌표 `(cx, cy)`에 배치될 때 서브 청크 위치:

```
cx-0   cx+1   cx+2   cx+3
  ┌──────┬──────┬──────┬──────┐
  │ Start│ Mid1 │ Mid2 │  End │   ← 각 청크 10×10 world units
  └──────┴──────┴──────┴──────┘
  Anchor  sub0   sub1   sub2
```

세계 좌표 = `청크 좌표 × 10`
예) cx=5이면 Start=x50, Mid1=x60, Mid2=x70, End=x80

---

## 동작 조건 체크리스트

- [ ] `OxidizedCavityChunk.prefab` 루트에 `TerrainChunk` 컴포넌트 있음
- [ ] `OxidizedCavityChunk_Sub.prefab` 루트에 `TerrainChunk` 컴포넌트 있음
- [ ] `OxidizedCavityChunk_SubEnd.prefab` 루트에 `TerrainChunk` 컴포넌트 있음
- [ ] 각 프리팹의 `_isStaticSpecialChunk = true` 설정 확인
- [ ] `ZoneTrigger` 자식 오브젝트가 Anchor 프리팹에만 있으면 충분 (Sub에는 없어도 됨)
- [ ] `playerLayer` 비트마스크가 플레이어 레이어와 일치하는지 확인 (현재 `2048` = Layer 11)

---

## 주의사항

- `subChunks`의 `offset`은 **청크 단위**다 (world unit이 아님).
- 앵커 스폰 실패 시 (TerrainChunk 없음 등) 전체 멀티 청크가 취소된다.
- 블렌드 경계 청크에는 특수 청크가 생성되지 않는다 (`SpecialChunkManager` 내부 규칙).
- Sub 청크도 `isStaticSpecialChunk = true`로 자동 설정되므로 별도 설정 불필요.
