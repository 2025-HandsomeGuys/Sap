# 특수 청크 자식 오브젝트 좌표 문제 — 원인과 런타임 동작 흐름
@tags: special-chunk, child-position, instantiate, flow, coordinate, prefab, local-position

> 마지막 업데이트: 2026-05-10
> 대상: 특수 청크 프리팹에 자식 오브젝트를 추가하거나 좌표가 틀어지는 원인을 파악하려는 작업자

---

## 핵심 원칙

> **Instantiate는 자식 로컬 좌표를 변경하지 않는다.**
>
> 루트 오브젝트는 지정된 월드 위치에 놓이고, 자식들은 **프리팹에 저장된 로컬 좌표 그대로** 유지된다.
> 자식 월드 위치 = 루트 월드 위치 + 자식 로컬 위치.

---

## 1. Instantiate 호출 흐름

```
SpecialChunkManager.SpawnSpecialChunkIfPossible(coord, ...)
    │
    ├─ anchorPos = ChunkCoords.ToWorld(coord)
    │     = new Vector3(coord.x * 10f, coord.y * 10f, 0f)
    │     예) coord = (3, -4) → anchorPos = (30, -40, 0)
    │
    └─ Instantiate(prefab.gameObject, anchorPos, Quaternion.identity, parent)
          │
          ├─ 루트 GameObject → 월드 위치 (30, -40, 0) 배치
          └─ 자식 GameObject → 프리팹에 저장된 로컬 좌표 그대로 유지
                예) 자식 localPosition = (3, 5, 0)
                    → 자식 월드 = (30+3, -40+5, 0) = (33, -35, 0)  ✅
```

Unity `Instantiate(obj, position, rotation, parent)` 시그니처에서
`position`은 **월드 공간 위치**이다. 자식 로컬 좌표는 건드리지 않는다.

---

## 2. 왜 자식 좌표가 틀어지는가

### 원인: 씬 인스턴스에서 편집 → Apply to Prefab

씬에 스폰된 특수 청크 인스턴스(예: 청크 좌표 (2, -4), 월드 위치 (20, **-41**, 0))에서 직접 자식을 드래그하거나 Position을 수정하면:

```
Unity가 계산하는 로컬 좌표 = child world pos - root world pos
```

예시:
- 자식 오브젝트를 월드 Y = -2 위치에 배치
- 루트(청크)가 월드 Y = -41에 있을 때
- **저장되는 로컬 Y = -2 - (-41) = 39**

이 값이 프리팹에 저장되고, 다음 스폰 시 재현된다.

```
coord = (3, -4) → 루트 월드 Y = -40
자식 로컬 Y = 39 (잘못 저장됨)
→ 자식 월드 Y = -40 + 39 = -1   ← 지표 근처로 날아감  ❌
```

청크 1칸 = 10 유닛이므로 **자식 로컬 좌표 유효 범위는 X(0~10), Y(0~10)** 이다.
로컬 Y = 39는 청크 영역을 완전히 벗어난 잘못된 값이다.

---

## 3. 올바른 편집 방법

### 반드시 Prefab Edit 모드에서 편집

Project 창에서 프리팹을 **더블클릭** → Prefab Edit 모드 진입.

이 모드에서는 루트 오브젝트가 월드 (0, 0, 0)에 있으므로:
```
자식 로컬 좌표 = 자식 월드 좌표 = 루트 피벗(좌하단)에서의 오프셋
```

| 자식 로컬 좌표 | 의미 |
|--------------|------|
| (0, 0) | 청크 좌하단 모서리 |
| (5, 5) | 청크 중앙 |
| (10, 10) | 청크 우상단 모서리 |
| (3, 7) | 청크 내 X 300px, Y 700px 위치 |

### 씬 인스턴스에서 편집하면 안 되는 이유 (요약)

| 작업 방식 | 결과 |
|---------|------|
| Project에서 프리팹 더블클릭 → Prefab Edit 모드 | 로컬 좌표 = 청크 내 오프셋 (0~10 범위) ✅ |
| 씬 인스턴스 선택 → Overrides → Apply to Prefab | 로컬 좌표 = child world - 현재 청크 world (음수 깊이 반영됨) ❌ |
| 씬 인스턴스 계층구조에 자식 드래그 → Apply to Prefab | 위와 동일 ❌ |

---

## 4. 런타임 Instantiate 전체 흐름 (자식 위치 관점)

```
[Prefab Edit 모드에서 올바르게 설정한 경우]

프리팹 저장 상태:
  루트: (0, 0, 0)
  자식A localPos: (3, 5, 0)   ← 청크 내 유효 오프셋
  자식B localPos: (7, 2, 0)

런타임 — 청크 coord = (2, -4) 스폰 시:

  ① anchorPos = ChunkCoords.ToWorld((2, -4)) = (20, -40, 0)
  ② Instantiate(prefab, (20, -40, 0), Quaternion.identity, parent)
      루트 world  = (20, -40, 0)
      자식A world = (20+3, -40+5, 0)  = (23, -35, 0)  ✅ 청크 내부
      자식B world = (20+7, -40+2, 0)  = (27, -42, 0)  ✅ 청크 내부

  ③ IChunkInitializer.Initialize(parent)  ← SpriteCavityInitializer 등 픽셀 초기화
     (자식 위치 변경 없음 — 픽셀 데이터만 조작)

  ④ Phase 2: Reuse_Step2_Finalize() → SetActive(true), UpdateCollider()
     (자식 위치 변경 없음)
```

---

## 5. 자식 DiggableRock 관련 추가 주의사항

CLAUDE.md §9 참고: 프리팹 자식으로 직접 배치된 DiggableRock은
`_mask`가 null → `CountExposedRockPixels()` 항상 0 반환.

반드시 다음 설정 필요:
- `preExposed = true`
- `minExposedPixels = 0`
- `Rigidbody2D` 없어야 함

---

## 6. 체크리스트 (자식 오브젝트 배치 시)

- [ ] Project 창에서 프리팹 더블클릭 → Prefab Edit 모드인지 확인
- [ ] 자식 localPosition X 범위: 0 ~ 10 (청크 1칸 = 10 유닛)
- [ ] 자식 localPosition Y 범위: 0 ~ 10
- [ ] 씬 인스턴스에서 편집한 적 없음 (씬 인스턴스 Overrides가 없어야 함)
- [ ] DiggableRock 자식이면 `preExposed = true`, `Rigidbody2D` 없음

---

## 관련 문서

| 문서 | 내용 |
|------|------|
| `special-chunk-architecture.md` | 특수 청크 패턴 A/B/C/D 및 전체 파이프라인 |
| `compressedtrashwall-spawn-flow.md` | CompressedTrashWall 스폰 전체 흐름 |
| `special-chunk-editor-setup-guide.md` | 에디터에서 프리팹 설정 순서 |
| `special-chunk-prefab-preservation.md` | 자식 보존 조건 (IChunkInitializer) |
