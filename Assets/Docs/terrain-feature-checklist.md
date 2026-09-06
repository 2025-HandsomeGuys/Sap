# 지형 신기능 도입 체크리스트

지형·청크 관련 새 기능(특수청크, 링크 피스, 새 오버레이 등) 구현 시 반드시 확인할 항목.

---

## 1. 로딩 파이프라인

- [ ] `IsSubChunkCoord` — 새 좌표가 차단 대상인지 역산 가능한가
- [ ] `PrependSubChunkAnchors` — 앵커 우선 스폰 처리가 되는가
- [ ] `ActiveChunkRegistry` 등록/해제 정합성
- [ ] `ChunkPool` 반환 — 언로드 시 풀로 정상 반납되는가

## 2. 데이터 저장·복원

- [ ] `wasNormalChunk` 플래그가 올바르게 설정되는가
- [ ] `hasChanges` + `RestoreSavedPixels` 호출 경로가 연결되는가
- [ ] `GetAndClearSavedRocks` — 바위 복원 경로가 닿는가

## 3. 시각·물리

- [ ] `UpdateBoundaryLighting` — 인접 청크 경계 갱신이 트리거되는가
- [ ] `ProcessDirtyChunksAsync` 2라운드 파이프라인이 정상 진입하는가
- [ ] `TryUpdateCollider` — 콜라이더가 올바르게 생성되는가
- [ ] `IndestructibleMask` 인접 보호(`IsAdjacentToIndestructible`) — 청크 경계 넘어서도 작동하는가

## 4. 콘텐츠 생성

- [ ] `IChunkInitializer` 유무 → Phase 2 데코레이터 스킵 여부가 의도적인가
- [ ] `RockDecorator` / `MineralDecorator` — 생성 필요한가, 불필요한가
- [ ] `RevealExposedRocks` — 노출 바위 처리가 되는가

## 5. 독점 구역 (Exclusion Zone)

- [ ] `SpecialChunkSelector._exclusionBuffer` — 새 청크가 다른 특수청크 스폰을 올바르게 차단하는가
- [ ] `minChunkSpacing` 제약이 새 청크 footprint에도 적용되는가

## 6. 광물·아이템 생명주기

- [ ] 언로드 시 `DetachMineralsToWorld` / `ReturnToPool` 분기가 올바른가
- [ ] `MineralLifetime` 타이머가 붙어야 하는 경우 누락 없는가
- [ ] `MineralDug` 태그 상태 전이가 정상인가

## 7. 파기 상호작용

- [ ] `TerrainModifier` — 플레이어 파기 가능/불가능 여부가 의도적인가
- [ ] 파기 후 `MarkDirty()` → `MarkChunkDirty()` 체인이 연결되는가
- [ ] 파기 불가 픽셀이 있다면 `IndestructibleMask`로 세팅됐는가
