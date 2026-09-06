## Task 4 완료 보고

### 수정 위치

**`Assets/Scripts/UI/Player/PlayerMining.cs`**
- 23~32행: `wallClimbToolIndex` 바로 아래에 `[Header("삽 파기 모양 (비우면 기존 타원)")]` + `shovelDigMask`(Texture2D) + `shovelDigMaskScale`(float, 기본 1f) 필드 추가.
- 165~166행: `Start()` 안 `_emptyStrategy = new EmptyStrategy();` 바로 아래에 `ShovelDigMask.Set(shovelDigMask, shovelDigMaskScale);` 호출 추가.
- 176~184행: `Start()`의 닫는 `}` 바로 뒤에 `OnValidate()` 메서드 신규 추가, 동일하게 `ShovelDigMask.Set(shovelDigMask, shovelDigMaskScale);` 호출.

**`Assets/Scripts/UI/Player/Strategies/SapStrategy.cs`**
- 254~264행: `PerformSapDig()` 안의 `if (MiningStaminaTuning.LogDigs) { ... }` 블록을 교체. `shape` 지역변수(`p.ToolIndex == MiningStaminaTuning.Shovel && ShovelDigMask.IsActive` → `"mask"` : `"ellipse"`)를 추가하고 `Debug.Log`에 `shape={shape}` 항목을 삽입. 기존 항목(charging/attacking/chargeTimer/ratio/canDig/tool/radius)은 전부 유지.

### 브리프와의 차이
없음. 브리프에 실린 코드를 그대로 옮겼다. 실제 파일 줄 번호가 브리프 명시값(16-23, 142-155, 254-259)과 약간 달랐으나(23→wallClimbToolIndex, Start 시작 151행, `_emptyStrategy` 163행, LogDigs 블록 254행 시작 자체는 일치) 이는 사전 조사 시점과의 자연스러운 오프셋 차이이며, 브리프가 지정한 **앵커 텍스트**(`wallClimbToolIndex` 바로 아래 / `_emptyStrategy = new EmptyStrategy();` 바로 아래 / `Start()` 닫는 `}` 바로 뒤 / 기존 LogDigs 블록 전체)를 기준으로 정확히 그 위치에 삽입했으므로 실질적으로 동일하다.

### 자기 점검 결과
- `PlayerMining`에 `shovelDigMask`(public Texture2D), `shovelDigMaskScale`(public float, 기본값 1f) 필드 추가됨 — 확인 (23~32행).
- `Start()` 안에서 `ShovelDigMask.Set(shovelDigMask, shovelDigMaskScale)` 호출, 위치는 `_emptyStrategy = new EmptyStrategy();` 바로 아래 — 확인 (165~166행).
- `OnValidate()` 신규 추가, 동일 호출 포함 — 확인 (176~184행). 기존 `OnValidate`가 없었음을 사전 확인한 대로 새로 만듦.
- `SapStrategy`의 `LogDigs` 블록에 `shape=` 항목 포함, 기존 항목(charging/attacking/chargeTimer/ratio/canDig/tool/radius) 전부 유지 — 확인 (260~263행 로그 문자열에 7개 기존 항목 + shape 모두 존재).
- `PerformSapDig`의 나머지 로직(사운드 재생, `p.CanDig` 판정, 파기 루프, 스태미나 비용 지불부)은 라인 236~253, 261행 이후 전혀 손대지 않음 — 확인 (diff 범위가 LogDigs 블록 내부로 한정됨).
- `PlayerMining`의 다른 필드·메서드는 건드리지 않음 — 확인 (필드 삽입 1곳, `Start()` 내부 1줄 삽입, `OnValidate` 메서드 신규 삽입 외 변경 없음).
- C# 문법: `ShovelDigMask`가 네임스페이스 없는 `public static class`이므로 `using` 추가 없이 그대로 참조 가능함을 확인. `MiningStaminaTuning.Shovel`은 `public const int Shovel = 1;`로 `p.ToolIndex`(int)와 비교 가능. 중괄호·세미콜론·삼항 연산자 구문 육안 검토 결과 이상 없음.

### 우려사항
없음. Task 1~3 산출물(`ShovelDigMask.Set`/`IsActive`, `TerrainModifier`)은 전혀 수정하지 않았고, 마스크 미설정 시 기본 동작(기존 타원)은 `ShovelDigMask.Set(null, ...)` 호출 경로가 Task 1~3에서 이미 보장하므로 이번 변경으로 영향받지 않는다.
