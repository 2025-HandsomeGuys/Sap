## Task 3 실행 보고

### 상태: DONE

### 수정 파일
`Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs` 1개 파일만 수정.

### 수정 위치

1. **상수 블록 (18~23행)** — `SHOVEL_TOOL_INDEX`, `ROCK_DIG_RADIUS_RATIO` 2개 추가. 브리프 Step 1 코드 그대로.
2. **`Dig` 메서드 bounds 계산 (177~189행, 원본 176~180행에 해당)** — `frontScale` 지역 변수 제거하고 `useShovelMask` 분기로 `maxRadiusPx` 계산. 브리프 Step 2 코드 그대로.
3. **`ProcessDigPixels` 메서드 전체 (325~≈410행, 원본 316~389행에 해당)** — 마스크/타원 분기 추가. 브리프 Step 3 코드 그대로.

### 브리프 코드를 그대로 썼는가
그대로 썼다. 변경한 부분 없음.

- `useMask` 계산은 브리프 그대로 `bool useMask = (toolIndex == SHOVEL_TOOL_INDEX) && ShovelDigMask.TryGetSampler(radiusPx, out var maskSampler);` 형태를 유지했다. 사전 검토 결과 이 패턴은 C# 확정 대입 규칙상 문제없다: `out` 매개변수는 메서드가 정상 반환하면(참/거짓 무관) 호출 직후 항상 확정 대입 상태가 되므로(패턴매칭 `is T x`의 조건부 대입과 다름), `maskSampler`는 `TryGetSampler`가 실제로 호출됐을 때(=`useMask`가 true로 평가되려면 왼쪽 `&&`가 true여서 오른쪽이 반드시 평가됨) 이미 대입된 상태다. `if (useMask) { ... maskSampler.Contains(...) ... }` 블록 안에서만 읽으므로 컴파일러가 이를 허용한다. 실제로 로컬 변수 미사용/미대입 에러 소지 없음을 확인했다 — 별도 선언 방식으로 바꾸지 않았다.
- 원본 마지막의 `//Debug.Log(...)` 주석 줄은 브리프의 교체 코드에도 없었으므로(브리프 Step 3 코드 마지막이 `return pixelChanged;` 바로 위에서 끝남) 그대로 제거된 상태로 두었다. 이는 브리프 코드를 그대로 옮긴 결과이지 임의 삭제가 아니다.

### 자기 점검

| 항목 | 결과 |
|---|---|
| `Explode` 메서드 손대지 않음 | 확인. 236~308행 원본 그대로(마스크/상수 참조 없음) |
| `CheckFloatingIslandsInArea`/`RemoveNarrowProtrusions`/`ErodeEdges`/`RemovePixel`/`IsAdjacentToIndestructible` 손대지 않음 | 확인. 전부 원본 그대로 |
| `Dig` 시그니처 유지 | 확인. 파라미터 목록 변경 없음 — `TerrainChunk.Dig` 등 호출부 재컴파일 불필요 |
| `ProcessDigPixels` 시그니처 유지 | 확인. 파라미터 목록 변경 없음(private이라 외부 호출부 없음) |
| 타원 분기 판정이 원본과 의미상 동일 | 확인. `currentScale`/`localXScaled`/`distSqr` 계산, `ROCK_DIG_THRESHOLD` 비교(`toolIndex == 1` → `toolIndex == SHOVEL_TOOL_INDEX`로 상수화, 값은 동일) 전부 원본과 동일. 감싸기만 `if (distSqr <= sqrRadius) { ... }` → `if (distSqr > sqrRadius) continue;` 로 반전(브리프 명시대로 의도된 것, 동작 동일) |
| 불괴 픽셀 보호 3줄이 분기 앞에 공통으로 남아있음 | 확인. `IndestructibleMask` 체크 + `HasIndestructiblePixels`&&`IsAdjacentToIndestructible` 체크가 마스크/타원 분기 진입(`if (useMask)`) 이전, 루프 최상단에 위치 |
| 파티클 콜백·`OnPixelDestroyed` 이벤트가 두 분기 모두에서 발생 | 확인. 픽셀 제거 코드(`debrisColor` 저장 → `BasePixels`/`PixelInfo` 초기화 → `particleCallback`/`OnPixelDestroyed`)가 `if/else` 블록 밖, 공통 코드로 한 번만 존재 — 두 분기가 `continue`로 조기 탈출하지 않으면 반드시 이 공통 코드에 도달 |
| 상수 2개가 상수 블록에 있음 | 확인. `SHOVEL_TOOL_INDEX = 1`, `ROCK_DIG_RADIUS_RATIO = 0.2f` |
| C# 문법 오류 없음 | 육안 검토로 중괄호/세미콜론/타입 일치 확인. 실제 컴파일 확인은 사람이 Unity에서 수행(Step 4 — 실행 도구 미호출) |

### 우려사항
없음. Unity 콘솔 컴파일 확인, EditMode 테스트 전체 실행, 마스크 미설정 상태 플레이 확인(Step 4)은 사람이 직접 수행해야 함.

---

## 추가 수정 (코디네이터 리뷰 반영, CS0165)

### 지적 내용
`bool useMask = A && ShovelDigMask.TryGetSampler(radiusPx, out var maskSampler);` 패턴은
ECMA-334 확정 대입 규칙상 `maskSampler`가 "definitely assigned **when true**"일 뿐이고,
이 조건부 대입 상태는 `useMask`라는 일반 `bool` 지역변수에 저장되는 순간 사라진다.
컴파일러는 `useMask`의 런타임 값과 `maskSampler`의 대입 여부 상관관계를 추적하지 않으므로
`if (useMask) { maskSampler.Contains(...) }` 안에서도 `maskSampler`는 미대입 취급 → CS0165.
최초 작업 시 "out 매개변수는 메서드 반환 시 항상 확정 대입된다"고 판단한 것은
**단축 평가로 메서드 자체가 호출되지 않는 경우**(`toolIndex != SHOVEL_TOOL_INDEX`)를 놓친 오판이었다.

### 적용한 수정
`TerrainModifier.cs` `ProcessDigPixels` 내부, 333~339행(수정 전 334~335행에 해당):

```csharp
        // 마스크 분기. 샘플러를 못 얻으면(마스크 없음·반경 0) 아래 타원 판정으로 그대로 떨어진다.
        // 단축 평가로 TryGetSampler가 호출되지 않는 경로가 있어(삽이 아닌 도구),
        // 확정 대입을 위해 default로 먼저 초기화한다. 이 값은 useMask가 false일 때만 남고
        // 그 경우 Contains는 호출되지 않는다.
        ShovelDigMask.Sampler maskSampler = default;
        bool useMask = (toolIndex == SHOVEL_TOOL_INDEX)
                       && ShovelDigMask.TryGetSampler(radiusPx, out maskSampler);
```

`ShovelDigMask.Sampler maskSampler = default;` 선언을 `useMask` 계산 앞에 추가하고,
`out var maskSampler` → `out maskSampler`(기존 선언 변수에 대입하는 일반 `out` 인자)로 변경했다.
피연산자 순서(`(toolIndex == SHOVEL_TOOL_INDEX) && TryGetSampler(...)`)는 그대로 유지 —
단축 평가가 유지되므로 삽이 아닌 도구에서는 여전히 `TryGetSampler`가 호출되지 않는다.
그 외(타원 분기·bounds 계산·상수·다른 메서드)는 손대지 않았다.

### 확정 대입 재확인
- `maskSampler`는 이제 `ProcessDigPixels` 진입 직후 `= default`로 무조건 대입된다.
- `useMask`가 true인 경로: `&&` 왼쪽이 true여서 오른쪽 `TryGetSampler(radiusPx, out maskSampler)`가
  실제로 호출되고, `out maskSampler`가 그 결과값(실제 샘플러)을 재대입한다.
- `useMask`가 false인 경로(왼쪽이 false, 또는 `TryGetSampler`가 false 반환): `maskSampler`는
  진입 시의 `default` 값을 유지 — 여전히 "대입된" 상태이며, 이 경로에서는 `if (useMask)` 블록에
  들어가지 않으므로 `maskSampler.Contains(...)`(373행)가 애초에 실행되지 않는다.
- 즉 373행에 도달하는 모든 실행 경로에서 `maskSampler`는 컴파일러 관점에서도, 값 관점에서도
  확정 대입 상태다. CS0165 해소를 확인했다(코드 재검토 기준 — 실제 컴파일 확인은 사람이 Unity에서 수행).
