# 버그 분석: 시야 범위 내 돌 사라짐 현상
@tags: bug, rock, DiggableRock, disappear, chunk-reload, SpawnedRocks, reveal

## 증상 요약

- 노출된(활성) 상태의 돌이 카메라 이동 후 사라짐
- 사라진 돌 인접 지형을 파야만 다시 나타남
- 콘솔 에러/경고 없음, 모든 청크에서 발생

---

## 근본 원인: 청크 재사용 시 고아 암석 발생

### 흐름 추적

#### 1단계: 정상 노출 상태
```
DiggableRock._spriteRenderer.enabled = true
DiggableRock._polyCollider.enabled   = true
TerrainChunk._spawnedRocks 에 등록됨
```

#### 2단계: 카메라 이동 → 청크 언로드
```
InfinityMapManager.UnloadDistantChunks()
  → SaveChunkDataToMemory()   ← 지형 픽셀(구멍 포함) 저장
  → ChunkPool.Return(chunk)
      → chunk.gameObject.SetActive(false)
          ← 자식 오브젝트(암석)도 함께 비활성화
```
`OnEnable()`은 SetActive(false)에서 호출되지 않음 → 아직 숨겨지지 않음.

#### 3단계: 카메라 복귀 → 청크 재사용

**`Reuse_Step1_Prepare()` (TerrainChunk.cs:454):**
```csharp
_generatedRockBounds.Clear();
_spawnedRocks.Clear();   // ← 리스트만 비움! 암석 GameObject는 여전히 자식으로 존재!
```
저장된 픽셀 데이터(구멍 포함) 로드.

**`Reuse_Step2_Finalize()` (TerrainChunk.cs:516):**
```csharp
gameObject.SetActive(true);  // ← 모든 자식에게 OnEnable() 호출!
```

**`DiggableRock.OnEnable()` (DiggableRock.cs:99):**
```csharp
if (!preExposed)
{
    _polyCollider.enabled   = false;  // ← 이전에 노출된 암석도 강제로 숨겨짐!
    _spriteRenderer.enabled = false;
}
```

#### 4단계: RockDecorator 재실행

```
ChunkGenerationPipeline → RockDecorator.Decorate()
  → RockSpawner.IsValidPlacement(..., checkExistingData: true)
      → pixelType == 2 (암석 마커) → return true  ← 무조건 통과!
  → SpawnRockObject() → 새 암석 생성, _spawnedRocks에 등록
      (새 암석 = 숨겨진 상태로 시작)
```

이 시점의 청크 상태:
```
자식 오브젝트:
  ├── [OLD ROCK] 숨김 (renderer/collider=false), _spawnedRocks에 없음 → 고아
  └── [NEW ROCK] 숨김 (renderer/collider=false), _spawnedRocks에 등록됨
지형 텍스처: 구멍이 복원된 상태 (저장된 픽셀로 덮임)
```

#### 5단계: 인접 파기 → RevealInTerrain

```
TerrainChunk.Dig()
  → _spawnedRocks에서 NEW ROCK 발견
  → rock.RevealInTerrain()
      → renderer.enabled = true, collider.enabled = true
      → TerrainCarver.ClearHole() 다시 실행
```
돌이 다시 보임 → 사용자가 "재등장"으로 인식.

---

## 문제 코드 위치

| 파일 | 라인 | 문제 |
|------|------|------|
| `TerrainChunk.cs` | 454~458 | `_spawnedRocks.Clear()` 전에 암석을 풀에 반납하지 않음 |
| `DiggableRock.cs` | 110~116 | `OnEnable()`이 이전 노출 상태를 고려하지 않고 무조건 숨김 |

---

## 수정 방법

### 방법 A: `Reuse_Step1_Prepare()`에서 암석 풀 반납 (권장)

**`TerrainChunk.cs`, `Reuse_Step1_Prepare()` 내부:**
```csharp
// 기존 코드 (454~457라인):
// _generatedRockBounds.Clear();
// _spawnedRocks.Clear();

// 수정:
// 재사용 전에 기존 암석을 풀에 반납 (SetParent(null)로 자식에서 분리)
for (int i = _spawnedRocks.Count - 1; i >= 0; i--)
{
    if (_spawnedRocks[i] != null)
        RockSpawner.ReturnToPool(_spawnedRocks[i].gameObject);
}
_generatedRockBounds.Clear();
_spawnedRocks.Clear();
```

이렇게 하면:
- 기존 암석이 `RockSpawner` 풀로 반납 (`SetActive(false)` + `SetParent(null)`)
- 청크가 `SetActive(true)`가 돼도 고아 암석 없음
- `RockDecorator`가 새 암석을 깨끗하게 스폰

### 방법 B: `DiggableRock`에 노출 상태 플래그 추가 (보완책)

```csharp
private bool _isRevealed = false;

public void RevealInTerrain()
{
    _isRevealed = true;
    // ... 기존 로직
}

private void OnEnable()
{
    if (!preExposed && !_isRevealed)
    {
        _polyCollider.enabled   = false;
        _spriteRenderer.enabled = false;
    }
    // _isRevealed = true인 경우는 이미 노출 상태 유지
}

private void OnDisable()
{
    _isRevealed = false;  // 풀 반납 시 상태 초기화
}
```

방법 B 단독으로는 **고아 암석 문제**가 남아 있음 (지형 재로드 후 중복 암석 존재).
**방법 A + B 병행 적용 권장.**

---

## 추가 고려사항

- 청크 재사용 시 `DirtPatch`도 동일한 고아 문제가 발생할 수 있음
  → `RockSpawner.ReturnToPool()`이 내부적으로 `associatedDirtPatch`도 반납하므로 방법 A로 함께 해결됨
- `selfRegister=true`인 프리팹 암석은 청크 언로드 시 함께 파괴되므로 해당 없음

---

## 현재 조치 상태 (2026-03-17 기준)

**✅ 방법 A + 방법 B 모두 적용 완료.**

### 방법 A 적용 — `TerrainChunk.cs` (Reuse_Step1_Prepare, 454~462라인)

```csharp
// 청크 재사용 시 바위 목록 초기화
// [Fix] 기존 암석을 리스트에서 제거하기 전에 풀에 반납 → 고아 암석 방지
for (int ri = _spawnedRocks.Count - 1; ri >= 0; ri--)
{
    if (_spawnedRocks[ri] != null)
        RockSpawner.ReturnToPool(_spawnedRocks[ri].gameObject);
}
_generatedRockBounds.Clear();
_spawnedRocks.Clear();
```

- `_spawnedRocks.Clear()` 호출 전에 루프를 돌며 각 암석을 `RockSpawner.ReturnToPool()`에 넘김
- 고아 암석 GameObject가 청크 자식으로 남지 않으므로 `SetActive(true)` 시 재활성화되는 문제 없음

### 방법 B 적용 — `DiggableRock.cs`

`_isRevealed` 필드 및 관련 로직이 모두 적용되어 있음:

| 위치 | 내용 |
|------|------|
| 필드 선언 (24라인) | `private bool _isRevealed = false;` |
| `OnEnable()` (114라인) | `if (!preExposed && !_isRevealed)` 조건으로 이미 노출된 암석은 숨기지 않음 |
| `OnDisable()` (126라인) | `_isRevealed = false;` — 풀 반납 시 상태 초기화 |
| `RevealInTerrain()` (311라인) | `_isRevealed = true;` 설정 |
| `Reveal()` (225라인) | `_isRevealed = true;` 설정 |

### 추가 적용 — `DiggableRock.Start()` 자동 노출 (Fix 1)

재로드 후 노출됐어야 할 암석이 숨겨진 채로 남는 문제를 추가로 수정:

```csharp
// DiggableRock.Start() 끝에 추가
if (!preExposed)
    RevealInTerrain(); // BasePixels 확인 후 노출 여부 자동 결정
```

- `RevealInTerrain()` 내부의 `CountExposedRockPixels()`가 실제 픽셀을 확인해 게이팅
- 노출 불필요한 암석은 그대로 숨겨진 상태 유지

**⚠️ 부작용**: 이 수정이 `ClearHole()`을 자동 호출해 `BasePixels`를 변경하므로,
재로드 시 `GetRockLayout()`의 Ground Check 결과가 달라져 암석 종류/위치가 바뀌는 현상 발생.
→ `BugAnalysis_RockTypeChange.md`의 Fix D(암석 배치 저장/복원 시스템)로 최종 해결됨.

### 결론

모든 수정이 코드베이스에 반영되어 있으며, 근본 원인(고아 암석 + `OnEnable` 강제 숨김 + 재로드 후 미노출)이 해소된 상태입니다.
암석 종류/위치 변경 버그는 `BugAnalysis_RockTypeChange.md` 참조.

