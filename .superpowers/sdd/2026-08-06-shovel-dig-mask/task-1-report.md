# Task 1 완료 보고 — ShovelDigMask 코어

## 상태: DONE

## 만든 파일
- `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ShovelDigMask.cs` (신규)
- `Assets/Tests/EditMode/ShovelDigMaskTests.cs` (신규)

기존 파일은 수정하지 않았다.

## 브리프와 달라진 점
없음. 브리프에 제시된 코드를 그대로 옮겨 적었다 — 클래스 전체, 테스트 전체 모두 1:1.
`Mathf.FloorToInt` 캐스팅도 그대로 유지했다(음수 좌표 버그 방지용, 브리프가 강조한 부분).

## 자기 점검 결과
- 파일 내용이 브리프의 코드 블록과 문자 그대로 일치함 (Write 시 그대로 복사).
- `ShovelDigMask`의 public 표면 확인:
  - `public const byte AlphaThreshold` ✓
  - `public static bool IsActive { get; }` ✓
  - `public static void SetBits(bool[] bits, int w, int h, float scale)` ✓
  - `public static void Clear()` ✓
  - `public static float BoundsRadiusPx(float radiusPx)` ✓
  - `public static bool TryGetSampler(float radiusPx, out Sampler sampler)` ✓
  - `public struct Sampler` with `public bool Contains(float localX, float localY)` ✓
- C# 문법 육안 검토: using 문, 네임스페이스 없음(프로젝트 다른 파일들과 동일하게 글로벌 네임스페이스 사용), 중괄호/세미콜론 짝 맞음, `Mathf`/`UnityEngine` 참조 정상. 컴파일 에러 소지 없어 보임.
- `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/` 디렉토리에 `TerrainModifier.cs` 등 기존 파일이 이미 있어 위치가 CLAUDE.md 디렉토리 구조와 일치함을 확인.
- `Assets/Tests/EditMode/`에 기존 EditMode 테스트 다수 존재, `EditModeTests.asmdef` 확인 — 테스트 어셈블리가 게임 스크립트 어셈블리를 참조하는 기존 관례를 따름. `ShovelDigMask.cs`는 별도 asmdef 지정 없이 해당 폴더의 기본 어셈블리(GameScripts.asmdef로 추정)에 편입되므로, 같은 폴더의 `TerrainModifier.cs` 등과 동일하게 테스트 어셈블리에서 접근 가능할 것으로 판단.

## 다음 Task를 위해 알아야 할 것
- Task 2(Texture2D 베이크)는 `ShovelDigMask.SetBits(bool[] bits, int w, int h, float scale)`를 호출하는 진입점을 만들면 된다. 이 시그니처가 계약이다.
- Task 3(지형 코드 연결)은 `ShovelDigMask.IsActive`로 마스크 활성 여부를 체크하고, `TryGetSampler(radiusPx, out sampler)`로 얻은 `Sampler`를 픽셀 루프 밖에서 1회 획득해 루프 안에서 `sampler.Contains(localX, localY)`로 판정해야 한다. `BoundsRadiusPx(radiusPx)`는 순회 반경(외접원)을 얻는 데 쓴다.
- `AlphaThreshold`(byte, 값 10)는 Task 2가 PNG 알파 채널을 bool로 변환할 때 `alpha > AlphaThreshold` 판정에 써야 한다(brief constraints.md와 일치).
- static 상태이므로 마스크가 활성화된 채로 남으면 이후 모든 파기에 영향을 준다 — Task 3에서 마스크를 켜고 끄는 시점(예: 삽 도구 선택/해제)을 명확히 관리해야 한다.

## 우려사항
- 파일 컴파일 여부와 EditMode 테스트 12개 통과 여부는 사람이 Unity에서 직접 확인해야 한다(지시에 따라 실행하지 않음).
- `ShovelDigMask.cs`가 속할 정확한 asmdef를 직접 열어 확인하지는 않았다 — 다만 같은 디렉토리의 다른 파일들과 동일한 규칙을 따르므로 문제가 없을 것으로 판단.
