# GameManager & SaveManager 구조 및 씬 전환 처리 가이드
@tags: GameManager, SaveManager, scene-transition, guide, system

## 개요
이 문서는 `GameManager`와 `SaveManager`의 역할 분담과 씬 전환 시 발생하는 참조 갱신 구조에 대해 설명합니다.  
이번 변경은 씬 전환 시 `GameManager`가 `SaveManager`를 찾지 못했다고 에러 로그를 출력하는 문제를 해결하고, 이를 정상적인 갱신 과정으로 처리하도록 개선한 내용을 포함합니다.

## 기존 구조 요약

### GameManager (Persistent Singleton)
- `DontDestroyOnLoad`로 설정되어 게임 내내 **파괴되지 않고 유지**됩니다.
- 씬 로드 이벤트(`OnGeneralSceneLoaded`)를 감지하여 게임 흐름을 제어합니다.
- `saveManager` 변수를 통해 데이터 저장/로드를 요청합니다.

### SaveManager (Scene Object)
- 각 씬(GameScene, UpgroundScene 등)에 배치된 **일반 오브젝트**입니다.
- 씬이 전환되면 기존 `SaveManager`는 **파괴**되고, 새 씬의 `SaveManager`가 **생성**됩니다.
- `Awake` 및 `Load` 시점에 해당 씬의 인벤토리, 플레이어 등을 찾아 연결합니다.

## 문제 원인 분석 (참조 갱신 이슈)
기존 코드에서는 `GameManager`가 씬이 로드되었을 때 `saveManager` 변수가 `null`(또는 파괴된 객체)인 경우, **에러(`LogError`)를 출력**한 뒤 복구를 시도했습니다.

1. **상황**: 지하(Underground) -> 지상(Upground) 이동
2. **현상**: 
   - 지하 씬의 `SaveManager`가 파괴됨.
   - `GameManager`는 여전히 파괴된 `SaveManager`를 참조 중.
   - 씬 로드 콜백 실행 시, `saveManager`가 유효하지 않으므로 `else` 분기로 진입.
   - **"saveManager가 할당되지 않았습니다!"** 에러 로그 출력.
3. **결론**: 이는 구조상 **필연적으로 발생하는 참조 갱신 과정**이며, 오류가 아닙니다.

## 변경 사항 요약

### GameManager.cs 수정
- `OnGeneralSceneLoaded` 메서드에서 `saveManager` 참조가 없거나 파괴된 경우를 **정상적인 흐름**으로 간주합니다.
- `LogError` 대신 `Log`를 사용하여 "새 씬에서 갱신을 시도함"을 알립니다.
- 갱신 후 즉시 `Load()`를 호출하여 데이터 연결 및 통합 로직(창고 등)이 실행되도록 합니다.

## 전체 구조 설명 (데이터 흐름)

1. **씬 로드 시작**: Unity가 새 씬(예: UpgroundScene)을 로드.
2. **SaveManager 생성**: 새 씬의 `SaveManager.Awake()` 실행 (인벤토리 등 참조 찾기).
3. **GameManager 감지**: `OnGeneralSceneLoaded` 콜백 호출.
4. **참조 갱신**:
   - `GameManager`가 기존 `saveManager`가 파괴되었음을 확인.
   - `FindFirstObjectByType<SaveManager>()`로 새 씬의 인스턴스 검색 및 연결.
5. **데이터 로드**: `saveManager.Load()` 호출 -> 인벤토리/창고 데이터 복원 및 통합.

## 적용 방법

### 수정된 파일
- `Assets/Scripts/MainManagers/GameManager.cs`

### 확인 사항
- 각 씬(지하, 지상)에 `SaveManager` 프리팹이 배치되어 있어야 합니다.
- `GameManager`는 최초 진입 씬(GameScene 등)에만 있으면 되며, 이후 자동으로 유지됩니다.

## 확장 및 주의사항
- **SaveManager의 배치**: 모든 게임플레이 씬에는 `SaveManager`가 존재해야 정상적으로 데이터가 로드됩니다.
- **실행 순서**: `GameManager`의 씬 로드 콜백은 `Awake`/`OnEnable` 이후에 실행되므로, `SaveManager`가 이미 초기화된 상태에서 `Load()`가 호출되어 안전합니다.
