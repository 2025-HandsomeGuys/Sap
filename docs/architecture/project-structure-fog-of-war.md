# Project Structure: Fog of War System
@tags: fog-of-war, architecture, project-structure, FieldOfView, PlayerVisionOverlay

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
        note: 매 프레임 플레이어 위치를 계산해<br/>쉐이더(Material)에 전달하는 역할<br/>(쉐이더에 _PlayerScreenPos 속성 필수)
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

---

# Player System Structure

이 섹션은 플레이어의 조작, 상태 관리, 데이터 영속성 구조를 설명합니다.

## 1. Class Diagram (플레이어 구조)

```mermaid
classDiagram
    %% Core Interfaces & Base Classes
    class MonoBehaviour { <<Unity>> }
    class ScriptableObject { <<Unity>> }
    class IPlayerController { 
        <<Interface>> 
        +IsWallClimbing
    }

    %% Main Controllers
    class Player3Controller {
        +PlayerStatsController playerStats
        +Rigidbody2D playerRigidbody
        -Animator anim
        +CurrentMod : string
        +Update()
        +ModWalking()
        +ModClimbing()
        note: 현재 메인 플레이어 컨트롤러<br/>걷기/등반 모드 전환 및 이동 관리
    }

    class PlayerStatsController {
        +float currentStamina
        +float maxStamina
        +float moveSpeed
        +float jumpForce
        +float wallClimbingSpeed
        +UseStamina()
        +ToData() : PlayerData
        +FromData(PlayerData)
    }

    %% Data Structures
    class PlayerSO {
        <<ScriptableObject>>
        +float maxStamina
        +float moveSpeed
        +float jumpForce
    }

    class PlayerData {
        <<DTO>>
        +float currentStamina
        +int gold
        +PlayerData(PlayerSO)
    }

    %% Relationships
    Player3Controller --|> MonoBehaviour
    Player3Controller ..|> IPlayerController
    Player3Controller --> PlayerStatsController : 참조 (필수)

    PlayerStatsController --|> MonoBehaviour
    PlayerStatsController ..> PlayerData : 변환 (Save/Load)
    PlayerStatsController ..> PlayerSO : 초기값 참조
```

## 2. Movement Mode Logic (플레이어 행동 모드)

플레이어의 움직임은 **CurrentMod** 변수와 **Shift** 키를 통한 모드 전환을 통해 관리됩니다.

```mermaid
stateDiagram-v2
    [*] --> WalkingMod

    state WalkingMod {
        [*] --> Idle
        Idle --> Moving : Horizontal Input != 0
        Moving --> Idle : Horizontal Input == 0
        Idle --> Jumping : Space Bar
        Moving --> Jumping : Space Bar
        Jumping --> Idle : IsGrounded (OnTriggerStay2D)
    }

    WalkingMod --> ClimbingMod : Left Shift (Toggle)
    ClimbingMod --> WalkingMod : Left Shift (Toggle)

    state ClimbingMod {
        [*] --> ClimbIdle
        ClimbIdle --> ClimbMoving : Input != 0
        ClimbMoving --> ClimbIdle : Input == 0
        ClimbMoving --> UseStamina : While Moving
    }
```

## 확인 방법
1. VS Code에서 이 파일을 엽니다.
2. `Ctrl + Shift + V`를 누르거나 우측 상단의 **Open Preview** 버튼을 클릭합니다.