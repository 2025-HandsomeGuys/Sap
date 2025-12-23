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
        -Camera mainCamera
        +Start()
        +Update()
        note: 매 프레임 플레이어 위치를 계산해<br/>쉐이더(Material)에 전달하는 역할
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
    
    FogOfWarController ..> Material : 2. 데이터 쓰기 (_PlayerScreenPos)
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
        Controller->>Controller: 플레이어 화면 좌표 계산
        Controller->>Material: SetVector("_PlayerScreenPos")
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
            Pass->>Material: GetVector("_PlayerScreenPos")
            Pass->>GameLoop: Blit(Source -> Temp -> Dest)
        end
    end
```

## 확인 방법
1. VS Code에서 이 파일을 엽니다.
2. `Ctrl + Shift + V`를 누르거나 우측 상단의 **Open Preview** 버튼을 클릭합니다.
