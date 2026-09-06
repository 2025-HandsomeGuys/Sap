# 특수 청크 프리팹 보존 조건
@tags: special-chunk, prefab, preservation, IChunkInitializer, restore, lifecycle

> 특수 청크가 "프리팹 상태 그대로" 씬에 배치되는 조건과 원리를 정리한 문서.

---

## 핵심 규칙

**`IChunkInitializer` 인터페이스를 구현하는 컴포넌트가 루트 GameObject에 하나라도 있으면 프리팹이 보존된다.**

```
CompressedTrashWallChunk (루트)
├─ TerrainChunk
├─ SpriteCavityInitializer  ← IChunkInitializer 구현체 → 이것 때문에 보존됨
└─ CompressedTrashBag (자식)
```

---

## 보존이란 무엇인가

| 항목 | 보존 O | 보존 X (기존 일반 청크) |
|------|--------|----------------------|
| 자식 GameObject | 유지 | `Destroy()` (ROCK_/MINERAL_ 제외) |
| 자식 컴포넌트 설정 | 유지 | 재생성 |
| 랜덤 암석/광물 | 추가 안 됨 | 추가됨 |
| 엘리베이터 | 추가 안 됨 | 조건부 추가 |

---

## 내부 동작 흐름

```
[Phase 1] ChunkDataProvider.GetContext()
          → SpecialChunkManager.SpawnSpecialChunkIfPossible()
          → Instantiate(prefab)  ← 프리팹 그대로 생성
          → IChunkInitializer.Initialize() 호출  ← 픽셀 주입 등 초기화
          → SetActive(false)  (TerrainChunk 기반일 때만)

[Phase 2] ChunkGenerationPipeline.ExecutePhase2_FinalizeAndDecorate()
          → chunk.GetComponent<IChunkInitializer>() != null ?
              YES → DecorateChunk_Phase2 스킵  ← 자식 보존, 데코레이션 없음
              NO  → DecorateChunk_Phase2 실행 (일반 청크)

[Phase 3] UpdateBoundaryLighting 실행
```

---

## 보존 조건 체크리스트

- [ ] 루트 GameObject에 `IChunkInitializer`를 구현한 컴포넌트가 있다
- [ ] 해당 컴포넌트가 `MonoBehaviour`이다 (`GetComponent`로 탐색되어야 함)
- [ ] `SpecialChunkManager`의 Pool에 이 프리팹이 등록되어 있다
- [ ] `SpecialChunkType`이 `None`이 아니다

---

## IChunkInitializer 구현체 종류

| 컴포넌트 | 역할 | 자식 보존 | 픽셀 주입 |
|---------|------|----------|----------|
| `SpriteCavityInitializer` | 스프라이트 픽셀을 TerrainChunk에 주입, 투명 픽셀 = 공동 | ✅ | ✅ |
| *(기타 추가 예정)* | | | |

> `IChunkInitializer` 없이 `LargeStaticTerrainChunk`만 사용하는 경우도 보존된다.
> `LargeStaticTerrainChunk`는 파이프라인이 Phase 2를 별도로 스킵하기 때문이다.

---

## LargeStaticTerrainChunk vs TerrainChunk + SpriteCavityInitializer

| | `LargeStaticTerrainChunk` | `TerrainChunk` + `SpriteCavityInitializer` |
|---|---|---|
| 비주얼 | 스프라이트 그대로 | 스프라이트 픽셀을 TerrainChunk에 복사 |
| 파기 가능 | ❌ | ✅ |
| DiggableRock 자식 | `selfRegister` 불가 (부모 TerrainChunk 없음) | `selfRegister = true` 사용 가능 |
| 용도 | 파기 불필요한 장식용 특수 청크 | 공동 + 파기 가능한 특수 청크 |

---

## 주의사항

- `SpriteCavityInitializer`의 `sourceSprite` 텍스처는 **Read/Write Enabled** 필수
- 자식 이름에 `ROCK_` 또는 `MINERAL_` 접두사가 붙으면 풀로 반납 시도됨 → **사용 금지**
- `IChunkInitializer.Initialize()`는 `SetActive(false)` 이전에 호출되므로, 초기화 시 GameObject가 활성 상태임을 보장
- 보존된 청크에는 랜덤 암석/광물/엘리베이터가 **일절 추가되지 않음**. 필요하다면 IChunkInitializer 내에서 직접 처리해야 함
