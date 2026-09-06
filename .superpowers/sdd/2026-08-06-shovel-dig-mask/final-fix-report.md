# 최종 리뷰 수정 보고서 (2026-08-06)

## 상태: 완료

## 수정한 파일
1. `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ShovelDigMask.cs` (Important — 실측 MaxExtent로 bounds 조이기)
2. `Assets/Tests/EditMode/ShovelDigMaskTests.cs` (기대값 갱신 6건 + 신규 테스트 1건)
3. `Assets/Scripts/UI/Player/Strategies/SapStrategy.cs` (Minor 2건: 불필요 쿼리 제거, 미사용 지역변수 제거)

---

## 수정 1: 실측 MaxExtent (Important)

- `s_maxExtentMaskPx` static 필드 추가.
- `SetBits()`에서 `MeasureMaxExtent(bits, w, h)`로 실측해 세팅.
- `MeasureMaxExtent`: 불투명 비트만 순회(`if (!bits[row+u]) continue;`), 픽셀 중심(`(u+0.5f)-cx`, `(v+0.5f)-cy`)까지 거리 제곱을 누적, 마지막에 `Mathf.Sqrt(maxSqr)`와 반대각선(`sqrt(w²+h²)/2`) 중 작은 값으로 클램프.
- `Clear()`에 `s_maxExtentMaskPx = 0f;` 추가 (로그 억제용 `s_hasLogged`/`s_lastLoggedTexId`/`s_lastLoggedOk` 리셋 3줄은 그대로 유지).
- `ExtentMultiplier`를 `s_scale * 2f * s_maxExtentMaskPx / s_width`로 변경(반대각선 sqrt(w²+h²) 기반 계산 제거). XML 주석도 실측 근거로 갱신.
- `BoundsRadiusPx`는 지시대로 **손대지 않음** — `radiusPx * ExtentMultiplier`를 그대로 반환하므로 자동으로 새 값을 씀.

## 테스트 기대값 계산 결과

| 테스트 | 조건 | 계산 | 새 기대값 |
|---|---|---|---|
| `순회반경은_실측_최대반경` (구 `순회반경은_회전_외접반경`) | 100×100, scale1, r=50 | `sqrt(49.5²+49.5²)=70.0036` → `50*(1*2*70.0036/100)` | **70.0036** (기존 70.71) |
| `순회반경은_세로로_긴_마스크를_안_자른다` | 20×100, scale1, r=10 | `sqrt(9.5²+49.5²)=50.4034` → `10*(1*2*50.4034/20)` | **50.4034** (기존 50.99) |
| `순회반경은_배율에_비례한다` | 100×100, scale2, r=50 | `50*(2*2*70.0036/100)` | **140.0071** (기존 141.42) |
| `ExtentMultiplier_정사각마스크_배율1` | 100×100, scale1 | `1*2*70.0036/100` | **1.40007** (기존 1.4142) |
| `ExtentMultiplier_세로로_긴_마스크는_2를_넘는다` | 20×100, scale1 | `1*2*50.4034/20` | **5.04034** (기존 5.099), `Assert.Greater(...,2f)` 유지 |
| `ExtentMultiplier_배율에_비례한다` | 100×100, scale3 | `3*2*70.0036/100` | **4.20022** (기존 4.2426) |
| `BoundsRadiusPx는_ExtentMultiplier와_일관된다` | — | 관계만 검증 | 수정 없음 |

허용 오차는 원래 값 그대로 유지(`BoundsRadiusPx` 계열 0.05, `ExtentMultiplier` 계열 0.001) — 위 계산값이 모두 오차 범위 안에 들어간다.

## 신규 테스트

`ExtentMultiplier는_여백을_실측으로_잘라낸다` 추가: 100×100 중 중앙 2×2(u,v ∈ {49,50})만 불투명. 4개 픽셀 모두 중심에서 `sqrt(0.5²+0.5²)=0.7071` 거리로 동일 → `ExtentMultiplier = 1*2*0.7071/100 = 0.01414`, 오차 0.0005로 검증. 반대각선(70.7)이 아니라 실측값이 나오는지 확인하는 회귀 고정 테스트.

테스트 총 개수: **28개** (`grep -c "\[Test\]"` 확인 완료 — 기존 27 + 신규 1).

---

## 수정 2 (Minor): `SapStrategy.cs` — 불필요 물리 쿼리 제거

`terrainHits` 산출 조건에 `p.CanDigTerrain &&`를 추가. `p.CanDigTerrain == false`면 아래 지형 루프(`if (chunk != null && p.CanDigTerrain)`)가 어차피 아무것도 안 하므로 넓힌 `OverlapCircleAll`을 쏘지 않는다.

## 수정 3 (Minor): `SapStrategy.cs` — 미사용 지역변수 제거

`PerformSapDig` 내부 277행 부근의 `int currentToolIndex = (_context.toolController != null) ? _context.toolController.currentToolIndex : 0;` 한 줄 삭제. 삭제 전 해당 메서드 전체를 검색해 사용처가 없음을 확인(파기 로직은 전부 `p.ToolIndex`를 사용). 파일 내 다른 메서드(413행 부근)에도 동일 이름의 지역변수가 있으나 그건 별개 메서드(`ToolCapabilities.Resolve` 호출부)에서 실제로 사용 중이라 손대지 않았다.

---

## 자기 점검

- `MeasureMaxExtent`가 불투명 픽셀만 보고, 픽셀 중심(+0.5)으로 재는가 → 예
- 반대각선 클램프가 있는가 → 예 (`Mathf.Min(Mathf.Sqrt(maxSqr), halfDiagonal)`)
- `Clear()`에 `s_maxExtentMaskPx = 0f;`가 들어갔고 기존 리셋(로그 억제 3줄 포함)이 그대로인가 → 예
- 고친 테스트 기대값 6개를 직접 계산해서 넣었는가 → 예 (표 참조)
- 새 테스트 1개가 추가됐는가 → 예
- 기존 테스트가 지워지지 않았는가 (총 28개) → 예, `grep -c` 확인
- `SapStrategy` 수정 2·3이 적용됐고 다른 로직은 안 바뀌었는가 → 예
- `TerrainModifier.cs`/`PlayerMining.cs`/`InfinityMapManager.cs`/`StaticChunkTerrainManager.cs`를 안 건드렸는가 → 예, 위 3개 파일 외 손대지 않음

## 우려사항

없음. `BoundsRadiusPx`의 XML 주석은 여전히 "외접원(반대각선)" 표현을 쓰고 있으나(203행 근처), 지시에서 이 메서드 본문은 손대지 말라 했고 주석 수정도 지시 범위(수정1의 5개 지점)에 포함되지 않아 그대로 두었다. 동작에는 영향 없음.
