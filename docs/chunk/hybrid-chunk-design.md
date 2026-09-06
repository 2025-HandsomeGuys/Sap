# Hybrid Chunk 설계 문서
@tags: special-chunk, hybrid, indestructible, IndestructibleOverlay, IChunkInitializer, terrain-chunk, design

> 파기 가능 지형(TerrainChunk)과 파괴 불가 영역(IndestructibleOverlay)이
> 한 청크 안에 공존하는 시스템.
>
> 상태: **설계 완전 확정 — 구현 가능**
> 작성일: 2026-03-15

---

## 1. 요구사항 정리

| 항목 | 결정 |
|---|---|
| 파괴 불가 영역 정의 방법 | 에디터에서 스프라이트로 직접 그림 (자유 형태) |
| 여러 파괴 불가 영역 | 가능 — 자식 오브젝트 여러 개 배치 |
| 시각적 구분 | 별도 스프라이트 사용 |
| 영구성 | 항구적 파괴 불가 |
| 콜라이더 | PolygonCollider2D 필요 (직접 그림) |
| 적용 범위 | 특수 청크에만 |
| 데이터 방식 | 픽셀 배열 아님 — 에디터 배치 오브젝트 기반 |
| 피드백 | 필요 (애니메이션은 추후 추가) |
| 경계 처리 | 아트에서 해결 (아래 섹션 4 참조) |

---

## 2. 아키텍처 설계

### 2.1 프리팹 계층 구조

```
HybridChunkRoot (GameObject)
├── TerrainChunk                       ← 파기 가능 픽셀 담당 (기존)
├── SpriteCavityInitializer            ← 전체 공동 픽셀 설정 (기존)
├── IndestructibleOverlay_A (자식)     ← 파괴 불가 영역 1 [신규]
│   ├── SpriteRenderer                 ← 시각적으로 구분되는 스프라이트
│   ├── PolygonCollider2D              ← 직접 그린 폴리곤
│   └── IndestructibleOverlayInit      ← IChunkInitializer + IIndestructibleHit [신규]
└── IndestructibleOverlay_B (자식)     ← 파괴 불가 영역 2 (있으면 추가)
    └── ...
```

> 자식 오브젝트 수는 제한 없음. 에디터에서 직접 추가/배치.

---

### 2.2 초기화 흐름 (IChunkInitializer 실행 순서)

```
SpecialChunkManager.SpawnSpecialChunkIfPossible()
  ↓
GetComponentsInChildren<IChunkInitializer>()  ← [변경] GetComponents → GetComponentsInChildren
  ↓ (계층 구조 순서: 루트 먼저 → 자식 순)
[1] SpriteCavityInitializer.Initialize()
    → sourceSprite 투명 픽셀 → ChunkData.BasePixels에 air 기록
    → 전체 공동 형태 설정

[2] IndestructibleOverlayInit.Initialize()  (자식마다 각각 실행)
    → 부모 TerrainChunk.GetData().BasePixels 접근
    → 자신의 스프라이트 범위에 해당하는 픽셀을 air로 교체
    → chunk.isTextureDirty = true, chunk.isDirty = true
```

**초기화 순서 보장:**
`GetComponentsInChildren`은 계층 깊이 순(부모 → 자식)으로 반환하므로
루트의 `SpriteCavityInitializer`가 자식의 `IndestructibleOverlayInit`보다 항상 먼저 실행됨. ✅

---

### 2.3 픽셀 좌표 변환 (IndestructibleOverlayInit 핵심 로직)

파괴 불가 영역 아래의 TerrainChunk 픽셀을 제거하는 계산.
`DiggableRock.selfRegister` 로직과 동일한 방식.

```
청크 픽셀 좌표 = 로컬 위치 × chunk.PPU
               - 스프라이트 pivot 오프셋

예) TerrainChunk PPU = 100
    IndestructibleOverlay 로컬 위치 = (3.0, 5.0)
    스프라이트 pivot = (50px, 0px)
    스프라이트 크기 = (200px, 300px)

    픽셀 좌표 x = 3.0 × 100 - 50 = 250
    픽셀 좌표 y = 5.0 × 100 - 0  = 500
    범위: (250, 500) ~ (450, 800)
    → 이 범위의 불투명 픽셀을 air로 교체
```

**중요 전제:** IndestructibleOverlay 스프라이트의 PPU는
TerrainChunk PPU(100)와 동일해야 1:1 픽셀 매핑이 성립.
(섹션 8 미결 질문 Q1 참조)

---

### 2.4 파기 시도 감지 및 피드백

#### 현재 Digger.DigAt() 흐름 분석

```
DigAt(position)
  ├─ IDirtDiggable 체크 (Dirt 레이어)
  ├─ CanDigRock=true이면: OverlapCircleAll → IDiggable 체크
  └─ 위 조건 미해당 → mapManager.ModifyTerrain() (TerrainChunk 픽셀 파기)
```

**문제점:**
- 삽(CanDigRock=false)으로 파괴 불가 벽을 치면 → `ModifyTerrain` 호출 → 픽셀 없음 → 아무 반응 없음 (피드백 불가)
- 곡괭이(CanDigRock=true)로 치면 → IDiggable 탐색 → IDiggable 없음 → `ModifyTerrain` → 픽셀 없음 → 피드백 불가

#### 해결 방안: 신규 인터페이스 + Digger 체크 추가

**신규:** `IIndestructibleHit` 인터페이스

```csharp
public interface IIndestructibleHit
{
    void OnHitAttempt(Vector2 worldPos, int toolIndex);
}
```

**Digger.DigAt() 변경:**
`ModifyTerrain` 호출 직전에 `IIndestructibleHit` 체크 추가.

```
DigAt(position)
  ├─ IDirtDiggable 체크
  ├─ CanDigRock=true이면: IDiggable 체크
  ├─ [신규] OverlapCircleAll → IIndestructibleHit 체크
  │   → 감지 시: OnHitAttempt() 호출 후 return (ModifyTerrain 스킵)
  └─ mapManager.ModifyTerrain()
```

**IndestructibleOverlayInit이 IIndestructibleHit 구현:**

```csharp
public void OnHitAttempt(Vector2 worldPos, int toolIndex)
{
    // 사운드 재생 (AudioSource 컴포넌트)
    // VFX 재생 (파티클 또는 스파크)
    // 애니메이션 트리거 (추후 추가)
    Debug.Log("[IndestructibleOverlay] 타격 시도 — 파괴 불가");
}
```

---

### 2.5 렌더링 레이어

```
Order  오브젝트
  1    IndestructibleOverlay 스프라이트 ← 지형 위에 렌더링 (가려지지 않음)
  0    TerrainChunk 지형 텍스처
 -1    DiggableRock 등
```

---

## 3. 필요한 코드 변경 목록

### 3.1 SpecialChunkManager.cs (라인 246) — 1줄 수정

```csharp
// 변경 전
foreach (var initializer in anchorObj.GetComponents<IChunkInitializer>())

// 변경 후
foreach (var initializer in anchorObj.GetComponentsInChildren<IChunkInitializer>())
```

**영향:** 기존 루트 IChunkInitializer 동작 그대로 유지. 자식 오브젝트 IChunkInitializer 추가 실행.

---

### 3.2 Digger.cs (DigAt 메서드) — ~5줄 추가

`mapManager.ModifyTerrain()` 호출 직전에 IIndestructibleHit 체크 삽입.

```csharp
// [신규] 파괴 불가 영역 충돌 체크
Collider2D[] indestructibleHits = Physics2D.OverlapCircleAll(actualHitPos, effectiveRadius);
foreach (var hit in indestructibleHits)
{
    if (hit.TryGetComponent(out IIndestructibleHit indestructible))
    {
        indestructible.OnHitAttempt(actualHitPos, toolIndex);
        return; // ModifyTerrain 스킵
    }
}

mapManager.ModifyTerrain(actualHitPos, effectiveRadius, toolIndex);
```

---

### 3.3 신규 파일 목록

| 파일 | 위치 | 역할 |
|---|---|---|
| `IIndestructibleHit.cs` | `SpecialChunks/Interfaces/` | 파괴 불가 타격 감지 인터페이스 |
| `IndestructibleOverlayInit.cs` | `SpecialChunks/Behaviours/` | IChunkInitializer + IIndestructibleHit 구현 |

---

## 4. 경계 처리 (아트 기반 권장)

다른 게임들의 사례:

| 게임 | 방식 |
|---|---|
| Terraria | 파괴 불가 블록(던전 벽돌 등)은 완전히 다른 텍스처. 경계는 블록 자체 테두리 픽셀로 처리 |
| Minecraft | Bedrock은 전혀 다른 텍스처. 경계 블렌딩 없음 |
| Hollow Knight | 파괴 불가 지형은 완전히 다른 아트 스타일. 경계는 자연스러운 단절 |
| Cave Story | 보스방 벽은 다른 타일셋. 경계 이음새 없음 |

**공통 패턴:** "다른 재질이다"를 시각적으로 명확히 보여줌. 경계 블렌딩은 하지 않음.

**이 프로젝트 권장 방식 (코드 변경 불필요):**

```
[IndestructibleOverlay 스프라이트 아트 가이드]

1. TerrainChunk 지형 텍스처와 명확히 다른 색/패턴 사용
   예) 회색 금속 질감, 어두운 돌 패턴 등

2. 스프라이트 외곽에 1~2px 짙은 테두리(아웃라인) 포함
   → 이 테두리가 TerrainChunk 픽셀과의 자연스러운 구분선 역할
   → TerrainChunk 픽셀이 제거된 air 영역과 맞닿을 때 깔끔하게 보임

3. 스프라이트 import 설정
   - Filter Mode: Point (no filter)
   - Compression: None
   - PPU: TerrainChunk와 동일 (Q1 참조)
```

경계 처리를 코드로 할 필요 없음. 아트에서 해결하는 게 성능상으로도 옳음.

---

## 5. 구현 순서

1. **`IIndestructibleHit.cs`** 인터페이스 신규 작성
2. **`IndestructibleOverlayInit.cs`** 신규 작성
   - `IChunkInitializer.Initialize()`: 픽셀 제거 로직
   - `IIndestructibleHit.OnHitAttempt()`: 피드백 (사운드/VFX placeholder)
3. **`SpecialChunkManager.cs`** 라인 246: `GetComponents` → `GetComponentsInChildren`
4. **`Digger.cs`** `DigAt()`: `IIndestructibleHit` 체크 삽입
5. 테스트용 특수 청크 프리팹 제작
6. SpecialChunkManager Pool 등록 후 인게임 확인

---

## 6. 파일 위치 요약

```
Assets/Scripts/Gameplay/Terrain/Tiles/
├── SpecialChunks/
│   ├── Interfaces/
│   │   └── IIndestructibleHit.cs          [신규]
│   └── Behaviours/
│       └── IndestructibleOverlayInit.cs   [신규]
├── SpecialChunkManager.cs                 [수정: 라인 246]
└── Digger.cs                              [수정: DigAt()]
```

---

## 7. CLAUDE.md 업데이트 예정 (구현 완료 후)

- 섹션 5-A에 Hybrid Chunk 패턴 추가
- `IndestructibleOverlayInit` 사용 기준
- `GetComponentsInChildren` 변경 이유 및 주의사항
- `IIndestructibleHit` 인터페이스 설명

---

## 8. 확정 사항 전체 요약

| 항목 | 결정 |
|---|---|
| IndestructibleOverlay PPU | **100** (TerrainChunk와 동일) |
| 피드백 발생 도구 범위 | **모든 도구** (삽 포함) |
| 라이팅 차단 | **필요** → 설계 변경 (섹션 9 참조) |

---

## 9. 라이팅 차단 — 아키텍처 변경

### 9.1 왜 픽셀을 air로 제거하면 안 되는가

이전 설계(픽셀 제거 방식)의 문제:
```
IndestructibleOverlayInit → BasePixels[idx] = air(alpha=0)
BFS 라이팅 → air = 빛 통과 → 벽 너머가 밝아짐 ❌
```

라이팅을 차단하려면 **픽셀을 불투명(opaque)으로 유지**해야 한다.
대신 파기 시스템이 해당 픽셀을 건드리지 않도록 별도 마스크가 필요하다.

### 9.2 핵심 변경: ChunkData에 IndestructibleMask 추가

`ChunkData`에 기존 `PixelInfo`와 동일한 패턴으로 `IndestructibleMask` 추가.

```csharp
// ChunkData.cs 추가
/// <summary>파괴 불가 픽셀 마스크. 0=파기가능, 1=파괴불가</summary>
public NativeArray<byte> IndestructibleMask;
```

- 생성자: `new NativeArray<byte>(totalPixels, Allocator.Persistent)` — 0으로 초기화
- `Dispose()`: 기존 배열과 동일하게 `if (IndestructibleMask.IsCreated) IndestructibleMask.Dispose()` 추가

**메모리 영향:** byte × 1000 × 1000 = ~1MB/청크. `PixelInfo`(기존, 동일 크기)와 동일 수준.
모든 청크에 할당되지만 대부분 0. 허용 범위 내.

### 9.3 IndestructibleOverlayInit 동작 변경

픽셀 제거 대신 마스크 설정:

```
[이전] BasePixels[idx] = air(alpha=0)   ← 라이팅 뚫림
[변경] IndestructibleMask[idx] = 1      ← 픽셀 불투명 유지 → 빛 차단
       BasePixels[idx] = 오버레이 스프라이트 픽셀 색상
       (overlay sprite에서 색상 복사 → TerrainChunk 텍스처에 올바른 색 표시)
```

**IndestructibleOverlayInit 처리 순서:**
1. 부모 `TerrainChunk.GetData()` 접근
2. 자신의 SpriteRenderer.sprite 픽셀 읽기 (Read/Write Enabled 필요)
3. 스프라이트 불투명 픽셀 → 청크 픽셀 좌표 변환 (PPU=100 기준)
4. `IndestructibleMask[idx] = 1` 설정
5. `BasePixels[idx] = spritePixel` (색상 복사 — 기술적으로 덮임이지만 BFS용 색상 일관성)
6. `chunk.isTextureDirty = true`, `chunk.isDirty = true`

### 9.4 TerrainModifier 변경

파기 시 마스크 확인:

```csharp
// TerrainModifier에서 픽셀 제거 로직 내
if (_data.IndestructibleMask.IsCreated && _data.IndestructibleMask[idx] != 0)
    continue; // 파괴 불가 → 제거 스킵
```

### 9.5 이중 콜라이더 문제 — 미결 질문 (섹션 10 참조)

픽셀이 불투명으로 유지되면:
- `TerrainCollider`가 해당 픽셀을 solid로 인식 → TerrainChunk PolygonCollider2D에 포함
- `IndestructibleOverlay`도 별도 PolygonCollider2D 보유

→ **같은 공간에 두 개의 콜라이더 중복** (섹션 10 참조)

---

## 10. 수정된 코드 변경 목록

| 파일 | 변경 내용 |
|---|---|
| `ChunkData.cs` | `NativeArray<byte> IndestructibleMask` 추가, 생성자/Dispose 수정 |
| `TerrainModifier.cs` | 픽셀 제거 시 `IndestructibleMask` 체크 추가 |
| `SpecialChunkManager.cs` | `GetComponents` → `GetComponentsInChildren` (라인 246) |
| `Digger.cs` | `IIndestructibleHit` 체크 추가 (ModifyTerrain 직전) |
| `TerrainCollider.cs` | 미결 질문 Q에 따라 변경 여부 결정 (섹션 11 참조) |
| `IIndestructibleHit.cs` | 신규 인터페이스 |
| `IndestructibleOverlayInit.cs` | 신규: IChunkInitializer + IIndestructibleHit |

---

## 11. 이중 콜라이더 — 확정: B (TerrainCollider에서 제외)

**결정:** TerrainCollider가 solid 판정 시 `IndestructibleMask` 체크.
파괴 불가 픽셀은 TerrainChunk PolygonCollider2D에서 제외.
IndestructibleOverlay의 수동 Polygon이 해당 영역의 유일한 충돌 담당.

**이유:**
- 픽셀 기반 자동 생성 경계선 vs 수동 폴리곤 경계선이 미세하게 어긋나 jitter 발생 가능
- 사용자가 직접 그린 폴리곤이 충돌을 담당해야 설계 의도와 일치
- DiggableRock과 동일한 패턴 (암석 자리 픽셀 제거 → 암석 콜라이더만 담당)

**TerrainCollider.cs 변경 (solid 판정 부분):**
```csharp
// 기존
bool isSolid = pixel.a > 0;
// 변경
bool isSolid = pixel.a > 0 && (maskPtr[idx] == 0);
```

---

## 12. 구현 주의사항 (에지 케이스)

| 상황 | 처리 방법 |
|---|---|
| 오버레이 스프라이트가 청크 경계를 넘는 경우 | 픽셀 좌표 0~999 범위로 clamp |
| SpriteCavityInitializer가 오버레이 영역을 air로 먼저 제거한 경우 | IndestructibleOverlayInit이 항상 색상 복원 + 마스크 설정 (덮어쓰기로 처리) |
| 여러 IndestructibleOverlay 자식의 픽셀이 겹치는 경우 | 후순위 Init이 덮어씀. 두 영역 모두 indestructible이므로 무해 |
| TerrainCarver.ClearHole() 호출 시 | 현재는 마스크 미체크 (엘리베이터·암석 노출용). 파괴불가 영역과 교차 가능성 낮음 — 추후 필요 시 추가 |
| 특수 청크 오브젝트 풀 미사용 확인 | 특수 청크는 Instantiate/Destroy (ChunkPool 미사용) → IndestructibleMask 오염 없음 ✅ |
