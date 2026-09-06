# SDD ledger — plan: docs/superpowers/plans/2026-08-06-shovel-dig-mask.md

프로젝트 규칙(CLAUDE.md)에 따른 절차 변경:
- UVCS 사용. git worktree·git commit 안 씀. 커밋은 사람이 UVCS에서 직접.
- Unity Test Runner 실행은 사람이 직접. 구현 에이전트는 테스트 작성만 하고 실행 결과를
  DONE 조건으로 요구하지 않는다. 리뷰어도 "테스트 실행 증거 없음"으로 반려하지 않는다.
- 커밋 해시가 없으므로 리뷰는 diff 대신 대상 파일 직접 읽기로 한다.

Task 1: complete (신규 2파일, 리뷰 clean — 사양 ✅ / 품질 승인, Critical·Important 0)
  - Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ShovelDigMask.cs
  - Assets/Tests/EditMode/ShovelDigMaskTests.cs (테스트 12개, 실행은 사람 대기)
Task 2: complete (ShovelDigMask.Set + 로그, 리뷰 clean — 사양 ✅ / 품질 승인, Critical·Important 0)
  - 테스트 10개 추가 → 총 22개 (실행은 사람 대기)
  - minor (deferred): LogOnce의 float 등가 비교 (인스펙터 원값만 들어와 실사용 위험 낮음)
  - minor (deferred): Clear()가 s_lastLoggedScale은 리셋 안 함 (s_hasLogged가 1차 게이트라 무해)
Task 3: fix round 1/5 (1 addressed, 0 open — CS0165 확정 대입: maskSampler = default 추가)
Task 3: complete (TerrainModifier.cs 수정, 재리뷰 clean)
  - 리뷰 Important #3 → 계획에 없던 범위 갭 발견. 사용자 결정 대기 중 (아래 참조)

## 계획 갭 — 사용자 결정 대기
기존 설계 문서 Assets/Docs/shovel-dig-mask.md (2026-07-20, '설계 확정, 구현 전')를
계획 수립 시 놓쳤다. 그 문서 §5가 bounds 보정 4곳을 지시하는데 내 계획엔 1곳만 있다:
  1. TerrainModifier bounds — 있음 (Task 3 완료)
  2. Digger.cs CircleCast 스윕 반경 — 없음
  3. InfinityMapManager.cs maxScale=2.0f — 없음
  4. StaticChunkTerrainManager.cs maxScale=2.0f — 없음
→ 마스크 외접반경 > radius*2 이면 인접 청크 미호출로 경계에서 잘림.
→ 옵션 1: Task 5로 추가 / 옵션 2: Task 4까지 하고 실물 테스트 후 판단
기존 문서 §9(스태미나 비용이 반지름 기준이라 마스크 면적과 불일치)도 내 계획엔 없음 — 원 문서도 범위 밖 처리.
Task 4: complete (PlayerMining 필드2개+Start+OnValidate, SapStrategy shape=, 리뷰 clean — 사양 ✅ / 품질 승인, Critical·Important 0)

계획된 Task 1~4 전부 완료. 최종 전체 리뷰는 청크 경계 결정(위 '계획 갭') 이후로 보류.

Task 5: complete (청크 선택 반경 확장, 리뷰 clean — 사양 ✅ / 품질 승인, Critical·Important 0)
  - 진단 정정: 기존 설계문서·Task3 리뷰어가 지목한 Digger/ModifyTerrain 3곳은 삽이 안 타는 경로였다.
    (Digger.Update:97이 currentTool==1이면 return). 진짜 병목은 SapStrategy의 OverlapCircleAll(radius).
  - ShovelDigMask.ExtentMultiplier 추가, SapStrategy 루프를 돌/지형으로 분리(돌 사거리 불변)
  - 매니저 2곳은 toolIndex==1 가드로 방어적 수정. Digger.cs:324는 도달 불가라 미수정(의도)
  - 테스트 5개 추가 → 총 27개
  - minor (deferred): SapStrategy.cs:282 currentToolIndex 미사용 지역변수 (CS0219, 기존부터 있던 것)
  - minor (deferred): 매니저 2곳 삽 판정에 리터럴 1 (MiningStaminaTuning.Shovel과 이중화)
  - minor (deferred): 마스크 활성 시 스윙당 OverlapCircleAll 1회 추가 (세로5배 마스크면 검색면적 25배)

최종 전체 리뷰: 병합 가능 (Critical 0, Important 2)
  - I-2 (bounds가 반대각선 상한 → 여백까지 순회·침식·섬제거) → 수정 완료.
    실측 MaxExtent(MeasureMaxExtent) 도입. 선행 설계문서 §5가 지시했으나 계획에서 누락됐던 것.
    테스트 기대값 6개 갱신 + 신규 1개 → 총 28개. 재리뷰 clean (수치 전부 재계산 검증)
  - I-1 (마우스 왼쪽일 때 마스크가 180° 회전 → 상하 비대칭 마스크가 뒤집힘) → 사용자 결정 대기
  - minor 수정: SapStrategy terrainHits에 p.CanDigTerrain 가드, 미사용 currentToolIndex 삭제
  - minor (deferred): BoundsRadiusPx XML 주석이 아직 '외접원(반대각선)'이라 실측 방식과 안 맞음
  - minor (deferred): Set()이 같은 조합이어도 GetPixels32+베이크는 매번 재실행 (에디터 한정 비용)
  - minor (deferred): 마스크 Set 경로의 텍스처 행 순서(상하) 테스트 없음 — 가장 흔한 마스크 버그
