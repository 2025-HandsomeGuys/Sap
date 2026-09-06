# Mine 청크 스폰 디버그 기록

## 증상
- 직접 배치: 정적 오브젝트(스프라이트, 오버레이)는 보임 / 파기 가능 영역(TerrainChunk 픽셀)은 안 보임
- 스폰 시스템: 4칸짜리 빈칸만 생성, 정적 오브젝트조차 보이지 않음

## 적용된 Fix 목록
| 파일 | 내용 |
|------|------|
| `SpecialChunkManager.cs:261` | `NeedsDelayedActivation && GetComponent<IChunkInitializer>() == null` 조건 추가 — TC 앵커의 SetActive(false) 방지 |
| `InfinityMapManager.cs` (ModifyTerrain) | 파괴된 TC null 가드 |
| `InfinityMapManager.cs` (IsWorldPositionEmpty) | 파괴된 TC null 가드 |
| `InfinityMapManager.cs` (언로드) | IChunkInitializer 보유 TC → pool 반납 대신 Destroy |

---

## Step 1: 컴파일 확인

**확인 방법**: Unity Editor 하단 Console 탭 열기 → 빨간 에러(컴파일 에러) 없는지 확인

### 체크 항목
- [ ] Console에 컴파일 에러(빨간 CS 에러) 없음
- [ ] `SpecialChunkManager.cs` 261번 라인: `anchorChunk.NeedsDelayedActivation && anchorObj.GetComponent<IChunkInitializer>() == null` 조건 확인

### 결과
- 상태: 통과 (컴파일 에러 없음)

---

## Step 2: 플레이 모드 Hierarchy 확인

**확인 방법**: 게임 실행 후 Mine 청크가 스폰될 위치 근처에서 Hierarchy 탭에서 `Special_X_Y` 오브젝트 찾기

### 체크 항목
- [ ] `Special_X_Y` GameObject가 Hierarchy에 존재
- [ ] 해당 오브젝트의 `activeSelf = true`
- [ ] 자식 오브젝트(DiggableSection_1/2/3)도 활성화 상태

### 결과
- 상태: 미확인

---

## Step 3: 로그로 SetActive 흐름 추적

**확인 방법**: `SpecialChunkManager.cs` 258번 근처에 임시 로그 추가 후 재컴파일

```csharp
// 258번 근처 — 기존 코드 위에 삽입
bool hasInitializer = anchorObj.GetComponent<IChunkInitializer>() != null;
Debug.Log($"[SpecialChunkManager] NeedsDelayedActivation={anchorChunk.NeedsDelayedActivation}, hasInitializer={hasInitializer}");
```

### 예상 정상 로그
```
[SpecialChunkManager] NeedsDelayedActivation=True, hasInitializer=True
```
→ hasInitializer=True 이면 SetActive(false) 건너뜀 (Fix 정상 동작)

### 결과
- 상태: 미확인
- 실제 로그:

---

## Step 4: SubChunkInstances null 확인

**확인 방법**: `ChunkGenerationPipeline.cs` ExecutePhase1 내 SubChunkInstances 처리 블록에 로그 추가

```csharp
Debug.Log($"[Pipeline] SubChunkInstances: {(context.SubChunkInstances == null ? "NULL" : context.SubChunkInstances.Count.ToString())}");
```

### 예상 정상 로그
```
[Pipeline] SubChunkInstances: 3
```
→ NULL이면 자식 TC들의 Reuse_Step2_Finalize()가 호출 안 됨

### 결과
- 상태: 미확인
- 실제 로그:

---

## 결론
- 미작성
