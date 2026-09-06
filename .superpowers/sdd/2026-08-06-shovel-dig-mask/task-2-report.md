# Task 2 완료 보고: Texture2D 베이크와 실패 사유 로그

## 상태
DONE

## 수정한 파일

### 1. `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ShovelDigMask.cs`
- `Clear()` 본문 끝에 로그 억제 상태 리셋 3줄 추가 (`s_hasLogged = false` / `s_lastLoggedTexId = 0` / `s_lastLoggedOk = false`).
- `Clear()` 바로 아래에 다음을 추가:
  - 로그 억제용 private static 필드 4개(`s_lastLoggedTexId`, `s_lastLoggedScale`, `s_lastLoggedOk`, `s_hasLogged`)
  - `public static int LogEmitCount { get; private set; }`
  - `public static void Set(Texture2D tex, float scale)` — 검사 순서: null → scale≤0 → !isReadable → 불투명 픽셀 0개 → 성공(SetBits 호출 + 성공 로그)
  - `private static void LogOnce(...)` — (texId, scale, ok) 조합이 직전과 같으면 스킵, 다르면 `LogEmitCount++` 후 레벨별 Debug.Log/LogWarning/LogError 호출

### 2. `Assets/Tests/EditMode/ShovelDigMaskTests.cs`
- 파일 상단 `using`을 4줄로 교체: `NUnit.Framework`, `UnityEngine`, `UnityEngine.TestTools`, `System.Text.RegularExpressions` 추가.
- 마지막 `}` 직전에 브리프의 테스트 블록 그대로 추가: `MakeTex` 헬퍼 + 테스트 10개
  (`Set_정상텍스처_활성화`, `Set_null이면_비활성`, `Set_배율0이면_비활성`, `Set_배율음수면_비활성`,
  `Set_전부투명하면_비활성`, `Set_알파가_임계값_이하면_투명취급`, `Set_알파가_임계값_초과면_불투명취급`,
  `Set_같은조합_반복호출시_로그가_한번만`, `Set_조합이_바뀌면_로그가_다시_찍힌다`, `Set_배율이_바뀌면_다시_적용된다`)
- Task 1의 기존 테스트 12개는 그대로 유지, 아무것도 지우거나 고치지 않음.

## 브리프와 달라진 점
없음. 브리프 코드를 그대로 옮겨 적었다.

## 자기 점검 결과
- `Set`의 검사 순서 = 브리프대로 null → scale≤0 → !isReadable → 불투명 0개 → 성공. 확인.
- 4개 실패 분기 전부 `Clear()` 호출 다음에 `LogOnce()` 호출. 확인 (코드 88~91, 96~100, 105~109, 127~131행).
- `LogEmitCount`는 `LogOnce`에서 억제 조건(`s_hasLogged && 텍스처ID·scale·ok 모두 동일`)에 걸리면 조기 `return`하여 증가하지 않고, 새 조합일 때만 `LogEmitCount++` 실행. 확인.
- `Clear()` 끝에 억제 상태 리셋 3줄 추가됨. 확인 (52~55행).
- Task 1의 테스트 12개(`Clear_상태_비활성` ~ `반경0이면_샘플러를_못_얻는다`) 그대로 파일에 남아 있음. 확인.
- 테스트 파일 using 4줄로 갱신됨. 확인.
- C# 문법: 중괄호 짝, 세미콜론, 시그니처 모두 육안 검토 완료. 문제 없음 (컴파일은 사람이 Unity에서 확인).

## 다음 Task를 위해 알아야 할 것
- `ShovelDigMask.Set(Texture2D, float)`과 `LogEmitCount`가 이제 공개 API로 존재. Task 3(아마 인스펙터 필드·OnValidate 연동으로 추정)이 이 `Set`을 호출하면 된다.
- `Set`은 실패 시 항상 `Clear()`를 호출하므로 실패해도 안전하게 타원 폴백 상태로 떨어진다(半 상태 없음).
- 로그 억제는 (texture InstanceID, scale, 성공여부) 3-tuple 키다. 텍스처 내용(픽셀)이 바뀌어도 같은 Texture2D 인스턴스·같은 scale이면 같은 로그로 억제된다 — 텍스처를 에디터에서 다시 임포트하면 보통 새 인스턴스가 되므로 실사용에서는 문제 없을 것으로 보이나, 만약 텍스처 픽셀만 런타임에 바뀌는 시나리오가 생기면 이 억제 키로는 못 잡는다는 점 참고.

## 우려사항
없음.
