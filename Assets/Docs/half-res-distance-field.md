# Half-Resolution Distance Field — 설계 문서

지형 테두리용 거리장(distance field)의 **계산 해상도만** 절반으로 낮춰, 청크 로드 시 발생하는
chamfer 스파이크를 줄이기 위한 설계. 게임 화면·지형·파기 해상도는 그대로 유지한다.

> 상태: **구현 완료(컴파일·시각 검증 통과).** 성능 재측정만 남음.
>
> ### 구현 결과 요약 (실제 방식)
> - **Option X 채택**: 검증된 Init/Chamfer/BoundarySync **내부 코드는 미변경**, half 인자(DistanceFieldHalf +
>   BasePixelsHalf + PixelInfoHalf + halfWidth/height + ext÷2)만 먹여 재사용. 새 잡(`DownsampleMaskJob`,
>   `UpsampleDistanceJob`)만 추가. VisualUpdateJob도 미변경(full 버퍼 그대로 읽음).
> - **다운샘플**: full BasePixels/PixelInfo → half. "2×2 중 하나라도 air면 air", indestructible 비트 OR.
>   `ScheduleInitLighting`(Init용) + `ScheduleChamferPasses`(chamfer 직전, skipInit stale 방지) **양쪽에서** 스케줄.
> - **업샘플**: half DF → full DF ×2, vis rect(full 좌표)만. `ScheduleChamferPasses`에서 bwd 후 스케줄, VisualJob이 의존.
> - **의존성**: over-depend(넉넉히)로 Unity 잡 안전 에러 방지. 청크 내 파이프라인은 직렬이라 병렬성 손실 없음.
> - **BoundarySync SYNC_DEPTH** 50→25(half), **이웃 캡처**(`TerrainLightingCalculator`) → DistanceFieldHalf.
> - 변경 파일: `ChunkData.cs`, `TerrainJobs.cs`, `ChunkJobScheduler.cs`, `TerrainLightingCalculator.cs`.

---

## 1. 배경 / 동기

드릴로 새 영역을 파고 내려갈 때 **50ms대 프레임 스파이크**가 반복 발생.

프로파일러 근거 (`Digger.DigRoutine → EnsureJobsCompleted → CompleteAllJobs`):
```
ChamferForwardPassJob   ~8ms   ┐ 풀 청크(1000×1000) 거리변환 (단일 스레드 sweep)
ChamferBackwardPassJob  ~9ms   ┘ = ~17ms 실제 컴퓨트
Idle (WaitForJobGroupID) ~27ms  이웃 청크 거리장 의존성 대기(캐스케이드)
```

### 왜 풀 청크 chamfer가 파기와 레이스하나 (근본 원인)
- 청크는 **Phase 2**(`ExecutePhase2_DecoratorSteps` → `Reuse_Step2_Finalize`)에서 활성화 = diggable
- 초기 풀 청크 거리장은 **Phase 2.5**(`FinishVisualsAfterInit`)에서 스케줄, **Phase 3**에서 완료
- 로더는 그 사이 프레임을 yield(time-slice) → **빠른 드릴이 그 틈에 청크를 팜**
- 파기(BasePixels 쓰기)와 chamfer(BasePixels 읽기)는 **데이터 해저드** → 파기의 `EnsureJobsCompleted`가
  아직 도는 풀 chamfer를 동기 완료 = 스파이크
- 추가로 `ChunkGenerationPipeline.ExecutePhase3_UpdateLighting`가 신규 청크를 **full rect로 MarkChunkDirty**
  → dirty 파이프라인도 풀 chamfer 유발

viewDistance=1(리드타임 0)이라 새 청크마다 이 레이스가 상시 발생.

### 왜 preload/게이팅이 아니라 "계산을 싸게"인가
- **viewDistance 확대**: 메모리·로드 스파이크 증가로 기각 (의도적으로 1로 낮춰둔 값)
- **diggable 게이팅**(chamfer 완료 전 파기 금지): 프레임 히칭 → 입력 히칭으로 바뀔 뿐, 드릴이 벽에 걸린 듯한
  반응성 저하로 더 나쁠 수 있어 기각
- → 남은 답: **없앨 수 없는 초기 거리장 계산 자체를 4배 싸게** 만들어 레이스가 나도 스파이크가 작게

---

## 2. 안전성 확인 (선행 조사 결과)

`distanceField`의 **최종 소비자는 테두리 텍스처 렌더(VisualUpdateJob) 하나뿐**. 게임플레이(충돌·실제
조명·시야·안개) 커플링 0. 실제 조명/시야 시스템(`GlobalLightingManager`, `PlayerVisionOverlay`,
`FieldOfView`, `FlashlightController`)은 distanceField를 **읽지 않음**.

| 파일 | distanceField 역할 |
|------|------|
| `TerrainJobs.VisualUpdateJob` | **읽음** — 테두리 텍스처 렌더 (유일한 최종 소비자) |
| `TerrainJobs.InitBFSJob / ChamferForward/Backward / BoundarySyncJob` | 계산/씀 (생산 파이프라인) |
| `TerrainLightingCalculator` | 이웃 경계 동기화 (이름만 "Lighting", 실제는 거리장 경계 sync) |
| `TerrainChunk.GetNeighborDistance` | 이웃 경계 거리 조회 (경계 sync용) |

**결론**: 거리장을 half-res로 낮춰도 **테두리 텍스처 정밀도만** 영향. 테두리 두께가 50px
(`textureThickness 4.0 × PPU 100`, texPx 클램프 상한 50)라 **2px granularity 오차는 미미**할 것.

---

## 3. 아키텍처 결정: 업샘플 방식 (B)

| | (A) 순수 half-res | **(B) 업샘플 (채택)** |
|---|---|---|
| VisualUpdateJob | half 버퍼 직접 샘플(×2)로 **수정 필요** | **수정 없음** (full 버퍼 그대로 읽음) |
| 리스크 | 큼 (VisualJob의 이웃 읽기 로직 = 대각 보정·법선 gradient까지 half 샘플링으로 고쳐야) | 낮음 |
| 메모리 | 최소 (full 버퍼 제거) | full 버퍼 유지 (+~0.5MB/청크) |
| 추가 비용 | 없음 | UpsampleJob ~1.5ms |

**(B) 채택 이유**: VisualUpdateJob의 이웃 거리 읽기(대각 보정 lines 96–110, 표면 법선 gradient
lines 126–134)를 안 건드려서 시각 회귀 리스크가 낮다. 시각 품질 트레이드오프(2px granularity)는
두 방식 동일. 순 효과: chamfer 17ms→~4ms 절약 − 업샘플 1.5ms = **~11ms 절감**.

향후 안정화되면 (A)로 최적화 여지 남김(메모리·업샘플 잡 절약).

### 데이터 흐름
```
BasePixels(1000²) ──[다운샘플]──> half 거리장 계산(500²) ──[UpsampleJob ×2]──> full 거리장(1000²) ──> VisualUpdateJob(변경 없음)
   (파기가 쓰는 원본)              Init/Chamfer/BoundarySync            단순 복사+스케일             테두리 렌더
```

---

## 4. 핵심 매핑

- **차원**: 청크 W=H=1000(짝수) → half HW=HH=500. 정렬 깔끔(경계 seam 위험 낮음).
- **인덱스**: full 픽셀 (x,y) → half 셀 (hx,hy) = (x/2, y/2). `HalfToIndex(hx,hy)=hy*HalfWidth+hx`.
- **단위 스케일 (핵심)**: half 1 스텝 = full 2 픽셀. chamfer는 ortho +5 / diag +7 (5/7 메트릭).
  따라서 **full 거리값 = half 거리값 × 2**. UpsampleJob에서 ×2 적용.
  → 기존 임계값 `textureThicknessPx × 5`(=250)와 border UV `dist/5`를 **그대로 재사용**.
- **다운샘플 (Init)**: half 셀(2×2 full 블록)이 **하나라도 air(alpha 0)면 air-seed(거리 0)**.
  보수적으로 잡아 테두리 누락 방지. (IndestructibleMask도 동일하게 OR 다운샘플)
- **상수 절반**: `EXT_MARGIN 50→25`, BoundarySync `SYNC_DEPTH 50→25`.
  dirty rect(full-res, `_lastExt`)는 half 잡에 넘길 때 `÷2` (내림/올림 여유 1셀).

---

## 5. ChunkData 통합 지점

`Assets/Scripts/_Core/Data/ChunkData.cs`:
- **필드 추가**: `public NativeArray<ushort> DistanceFieldHalf;` (기존 `DistanceField` 유지 = full, 업샘플 대상)
- **차원 프로퍼티**: `HalfWidth => Width/2`, `HalfHeight => Height/2`, `HalfToIndex(hx,hy)`
- **생성자(line ~107)**: `DistanceFieldHalf = new NativeArray<ushort>(HalfWidth*HalfHeight, Allocator.Persistent);`
- **Dispose(line ~205)**: `if (DistanceFieldHalf.IsCreated) DistanceFieldHalf.Dispose();`
- 주의: Width/Height는 `private set`(생성자 전용). 홀수 대비 방어 불필요(청크 1000 고정 짝수)지만
  `HalfWidth = (Width+1)/2` 로 방어해두면 안전.

---

## 6. 단계별 구현 (검증하며 진행)

| Phase | 작업 | 검증 |
|-------|------|------|
| **1** | ChunkData half 버퍼 추가 → InitBFS/Chamfer(fwd/bwd)를 half 격자로 계산 → **UpsampleJob 추가**(half→full ×2) → 스케줄러가 half 버퍼로 잡 실행 후 업샘플 | 컴파일 + `TerrainChunk.DebugDistanceField=true`로 거리장 등고선이 정상(계단만 굵어짐)인지 |
| **2** | BoundarySync를 half로 (이웃 half 엣지 읽기, SYNC_DEPTH 25). `TerrainLightingCalculator.CaptureNeighbor`가 이웃 **half** 버퍼 캡처, `GetNeighborDistance`에 half 변형 | **청크 경계 seam 없는지** (최대 리스크) |
| **3** | dirty rect ÷2 배선 정리, `_lastExt` half 변환, EXT_MARGIN 25 반영 | 드릴 재측정 — chamfer 17→~4ms, Idle 대폭 감소 확인 |

**Phase 2(경계)가 최대 리스크.** 이웃 half 격자 정렬이 어긋나면 청크 경계에 seam. 청크 1000=짝수라
500 정렬은 깨끗하지만, 엣지 인덱스 매핑(내 hx=0 ↔ 이웃 hx=HalfWidth-1)을 정확히 맞춰야 함.

---

## 7. 리스크 & 검증 체크리스트

- [ ] 컴파일 (NativeArray 안전 검증 통과 — half 버퍼 writer/reader 의존성)
- [ ] 테두리 두께·모양 미세 변화 **육안 확인** (50px라 거의 안 보일 것)
- [ ] **청크 경계 seam** 집중 확인 (Phase 2)
- [ ] 드릴 연속 파기 시 chamfer 스파이크 감소 재측정
- [ ] 파진 구멍 테두리가 정상 갱신되는지 (dirty rect ÷2 매핑 오류 시 잔상)
- [ ] `DebugDistanceField` 시각화로 거리장 자체 검증
- 게임플레이 영향: **없음** (거리장은 테두리 전용, §2에서 확인)

---

## 8. 관련 파일

| 파일 | 관련 |
|------|------|
| `_Core/Data/ChunkData.cs` | half 버퍼 추가 (§5) |
| `_Core/Managers/TerrainJobs.cs` | InitBFSJob / ChamferForward/Backward / BoundarySyncJob / VisualUpdateJob / **신규 UpsampleJob** |
| `Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs` | 잡 스케줄·의존성 배선, `_lastExt`(EXT_MARGIN) |
| `Gameplay/Terrain/Tiles/Chunk/TerrainLightingCalculator.cs` | 이웃 경계 sync (half 버퍼 캡처) |
| `Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` | `GetNeighborDistance`(half 변형) |

---

## 9. 향후 최적화 (순수 half-res, 선택)

안정화 후 (B)→(A)로: VisualUpdateJob이 half 버퍼를 직접 샘플(index ÷2, value ×2)하면
full 버퍼(+0.5MB)와 UpsampleJob(~1.5ms)을 제거 가능. 단 VisualUpdateJob의 이웃 읽기 로직
(대각 보정·법선 gradient)을 half 샘플링으로 정확히 옮겨야 하므로 시각 검증 부담 큼 → 후순위.

---

## 관련 배경 문서
- `collider-visual-decoupling.md` — 콜라이더/비주얼 분리, ApplyTexture 타이밍
- (본 문서) chamfer 거리장의 의미: 각 흙 픽셀이 가장자리(air)에서 얼마나 안쪽인지를 5/7 정수 근사
  2패스 sweep으로 계산 → 테두리 텍스처 결정
