# Project Structure: Fog of War System

이 문서는 현재 프로젝트의 Fog of War(전장의 안개) 시스템 구조를 설명합니다.

## 1. Class Diagram (클래스 관계)

```mermaid
classDiagram
    %% Unity Built-in Classes
    class MonoBehaviour {
        <<Unity>>
    }
    class ScriptableRendererFeature {
        <<URP>>
    }
    class ScriptableRenderPass {
        <<URP>>
    }

    %% Custom Classes
    class FogOfWarController {
        +Transform playerTransform
        +Material fogOfWarMaterial
        +float worldYLimit
        +float sightAngle
        +float sightDistance
        -Camera mainCamera
        +Start()
        +Update()
        note: 1. 플레이어/마우스 위치 추적<br/>2. Y제한 및 시야 파라미터 계산<br/>3. 쉐이더(Material)에 데이터 전달
    }

    class FogOfWarFeature {
        +FogOfWarSettings settings
        -FogOfWarPass fogOfWarPass
        +Create()
        +AddRenderPasses()
        note: 렌더링 파이프라인에<br/>Pass를 끼워 넣는 관리자
    }

    class FogOfWarPass {
        -Material material
        +Execute()
        note: 실제로 화면을 그리는 작업(Blit)<br/>수행
    }

    %% Relationships
    FogOfWarController --|> MonoBehaviour : 상속
    FogOfWarFeature --|> ScriptableRendererFeature : 상속
    FogOfWarPass --|> ScriptableRenderPass : 상속

    FogOfWarFeature *-- FogOfWarPass : 1. 생성 및 보유
    
    FogOfWarController ..> Material : 2. 데이터 쓰기 (_PlayerScreenPos, _PlayerDir, _SightAngle, _SightDistance 등)
    FogOfWarPass ..> Material : 3. 데이터 읽기 및 렌더링
```

---

## 2. Sequence Diagram (실행 흐름)

```mermaid
sequenceDiagram
    participant GameLoop as Unity Engine Loop
    participant Controller as FogOfWarController
    participant Material as Shared Material
    participant Feature as FogOfWarFeature
    participant Pass as FogOfWarPass

    Note over GameLoop, Pass: 초기화 단계 (Start)
    GameLoop->>Controller: Start()
    Controller->>Controller: Player & Material 찾기
    GameLoop->>Feature: Create()
    Feature->>Pass: new FogOfWarPass(Material)

    Note over GameLoop, Pass: 매 프레임 반복 (Update & Render)
    
    rect rgb(200, 220, 255)
        note right of GameLoop: 1. 로직 업데이트 (Logic)
        GameLoop->>Controller: Update()
        Controller->>Controller: 1. 플레이어 & 마우스 화면 좌표 계산<br/>2. 마우스 방향 벡터(lookDir) 추출<br/>3. Y제한선 화면 좌표 변환
        Controller->>Material: SetVector("_PlayerScreenPos")
        Controller->>Material: SetVector("_PlayerDir")
        Controller->>Material: SetFloat("_SightAngle", "_SightDistance", "_FogYLimit")
    end

    rect rgb(255, 220, 200)
        note right of GameLoop: 2. 렌더링 파이프라인 (Render)
        GameLoop->>Feature: AddRenderPasses()
        
        alt 씬 이름 == "khbScene 1" AND !SceneView
            Feature->>GameLoop: EnqueuePass(fogOfWarPass)
        else 그 외
            Feature->>Feature: Skip
        end

        opt Pass가 등록된 경우
            GameLoop->>Pass: Execute()
            Pass->>Material: 시야 데이터 읽기
            Pass->>GameLoop: Blit(Source -> Temp -> Dest)
            note right of Pass: 쉐이더: 1. Y제한 확인<br/>2. 원형 + 부채꼴 시야 결합<br/>3. 최종 밝기 적용
        end
    end
```

## 3. 주요 렌더링 로직 (FogOfWar.shader)
안개 가시성(`visibility`)은 두 가지 시야의 합집합(MAX)으로 결정됩니다:

1.  **Base Circle Visibility (기본 원형)**
    -   플레이어 중심의 고정된 반지름(`_Radius`) 내를 밝힘.
    -   `visibilityA = 1.0 - smoothstep(_Radius, _Radius + _Softness, dist)`

2.  **Flashlight Cone Visibility (손전등 부채꼴)**
    -   마우스 방향(`_PlayerDir`)과 픽셀 방향의 내적(Dot Product)을 이용.
    -   설정된 각도(`_SightAngle`)와 사거리(`_SightDistance`) 내를 밝힘.
    -   `visibilityB = coneDistVisibility * angleVisibility`

3.  **Final Result**
    -   `visibility = max(visibilityA, visibilityB)`
    -   `Brightness = lerp(1.0 - _FogDarkness, 1.0, visibility)`
    -   Y좌표가 `_FogYLimit`보다 높으면 안개를 적용하지 않음.

---
## 확인 방법
1. VS Code에서 이 파일을 엽니다.
2. `Ctrl + Shift + V`를 누르거나 우측 상단의 **Open Preview** 버튼을 클릭합니다.
