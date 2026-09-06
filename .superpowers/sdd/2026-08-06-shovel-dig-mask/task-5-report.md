# Task 5 보고서 — 청크 선택 반경 (마스크 경계 잘림 수정)

## 상태: DONE

## 파일별 수정 위치

### 1. `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ShovelDigMask.cs`
- 179~190행(기존 `BoundsRadiusPx`)을 교체: `ExtentMultiplier` static 프로퍼티 신규 추가 + `BoundsRadiusPx`가 그것을 곱하는 형태로 축소.
- `PixelsPerMaskPixel` private 메서드는 `TryGetSampler`(228행)에서 계속 쓰이므로 삭제하지 않음. `BoundsRadiusPx`만 더는 그것을 쓰지 않게 됨(브리프 코드 그대로).

### 2. `Assets/Scripts/UI/Player/Strategies/SapStrategy.cs` — 핵심 수정
- 271행 부근: `Collider2D[] hits = ...` 줄 앞뒤에 주석 추가, 그 아래 `terrainSearchRadius` / `terrainHits` 계산 삽입(277~280행).
- 286~314행: 돌 처리 `foreach (var hitCollider in hits)` 루프. 기존 단일 루프에서 지형 처리 블록만 빼낸 것 — 돌 처리 본문(스태미나 계산, `isPuzzle`, `damage`, `diggable.Dig`, `hitAnyRock`)은 한 글자도 바꾸지 않고 그대로 옮김.
- 316~327행: 지형 처리 `foreach (var hitCollider in terrainHits)` 루프. `chunk.Dig(digCenter, radius, p.ToolIndex)` — 인자는 `radius` 그대로(`terrainSearchRadius` 아님).
- `hitAnyRock`/`hitAnyTerrain` 선언(282~284행)은 두 루프보다 위. 328행 이후 스태미나 비용 블록은 미변경.

### 3. `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs:1025~1029`
- `ModifyTerrain(...)` 내 `float maxScale = 2.0f;`를 `(toolIndex == 1) ? Mathf.Max(2.0f, ShovelDigMask.ExtentMultiplier) : 2.0f`로 교체(방어적, 삽이 아닌 도구는 영향 0).

### 4. `Assets/Scripts/Gameplay/Terrain/Tiles/StaticChunkTerrainManager.cs:101~105`
- 동일 패턴으로 `maxScale` 교체.

### 5. `Assets/Tests/EditMode/ShovelDigMaskTests.cs`
- 파일 끝(마지막 `}` 직전)에 브리프의 테스트 5개를 그대로 추가:
  `ExtentMultiplier_비활성이면_0`, `ExtentMultiplier_정사각마스크_배율1`,
  `ExtentMultiplier_세로로_긴_마스크는_2를_넘는다`, `ExtentMultiplier_배율에_비례한다`,
  `BoundsRadiusPx는_ExtentMultiplier와_일관된다`.

## 브리프와 달라진 점
없음. 5개 파일 전부 브리프에 제시된 코드를 그대로 사용했다. 로직·주석·변수명 변경 없음.

## 자기 점검 결과

| 항목 | 결과 |
|---|---|
| `ExtentMultiplier` 비활성 시 0 반환 | 확인 (`if (!IsActive) return 0f;`) |
| `BoundsRadiusPx(r) == r * ExtentMultiplier` 성립 | 확인 (`return radiusPx * ExtentMultiplier;`) |
| `PixelsPerMaskPixel`이 `TryGetSampler`에서도 쓰이는지 | 확인 — 228행에서 사용 중, 삭제하지 않음 |
| `SapStrategy` 돌 처리 블록 내용 무변경 | 확인 — 스태미나 비용, `damage` 계산, `diggable.Dig`, `hitAnyRock` 문구 그대로, 위치만 이동 |
| `chunk.Dig` 인자가 `radius`인지 | 확인 — `terrainSearchRadius`가 아니라 `radius` 사용 |
| `hitAnyRock`/`hitAnyTerrain` 선언이 두 루프 위 | 확인 (282~284행, 루프는 286행부터) |
| 두 루프 아래 스태미나 비용 블록 무변경 | 확인 — 329행 이후 원본 그대로 |
| 매니저 2곳 모두 수정 + `toolIndex == 1` 가드 | 확인 — `InfinityMapManager.cs`, `StaticChunkTerrainManager.cs` 둘 다 적용 |
| `Digger.cs` 미수정 | 확인 — 열람만 하고 수정하지 않음 |
| `TerrainModifier.cs`, `PlayerMining.cs` 미수정 | 확인 — 열람하지 않았고 수정도 안 함 |
| 마스크 비활성 시 `terrainSearchRadius == radius` → `terrainHits`가 `hits`와 동일 배열 | 확인 — `ExtentMultiplier`가 0이므로 `Mathf.Max(1f, 0f) = 1f` → `terrainSearchRadius == radius` → 삼항의 `> radius` 조건이 false → `hits` 재사용, 추가 쿼리 없음 |
| C# 문법 오류 | 육안 검토상 없음(중괄호 짝, 세미콜론, 타입 일치 확인). 실제 컴파일은 Unity 콘솔에서 사람이 확인 필요 |

## 우려사항
없음.
