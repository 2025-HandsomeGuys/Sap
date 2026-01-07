# 전장의 안개 (Fog of War) 설정 가이드

## 개요
이 프로젝트에는 주인공 주변을 원형으로 볼 수 있게 하는 전장의 안개 시스템이 구현되어 있습니다. 추가로 마우스 방향을 향한 삼각형 손전등 효과도 지원합니다.

## 파일 구조
- `Assets/Shader/FogOfWar.shader` - 전장의 안개 셰이더
- `Assets/Shader/FogMaterial.mat` - 셰이더를 사용하는 머티리얼
- `Assets/Scripts/FogOfWarController.cs` - 플레이어 위치를 셰이더에 전달하는 컨트롤러
- `Assets/Scripts/FogOfWarFeature.cs` - URP 렌더링 기능
- `Assets/Scripts/FogOfWar/FieldOfView.cs` - 물리 기반 시야 메쉬 생성 (Shadow Casting)
- `Assets/Shader/StencilMask.shader` - 스텐실 마스크용 쉐이더

## 설정 방법

### 1. Renderer2D에 FogOfWarFeature 추가
1. Unity 에디터에서 `Assets/Settings/Renderer2D.asset` 파일을 선택합니다.
2. Inspector 창에서 "Add Renderer Feature" 버튼을 클릭합니다.
3. 드롭다운 메뉴에서 "Fog Of War Feature"를 선택합니다.
4. Fog Of War Feature의 "Material" 필드에 `Assets/Shader/FogMaterial.mat`을 할당합니다.
   - 또는 비워두면 자동으로 찾습니다.

### 2. 카메라에 FogOfWarController 추가
1. 씬의 Main Camera를 선택합니다.
2. Inspector 창에서 "Add Component" 버튼을 클릭합니다.
3. "Fog Of War Controller"를 검색하여 추가합니다.
4. 다음 필드를 설정합니다:
   - **Player Transform**: 플레이어 오브젝트의 Transform (비워두면 자동으로 찾습니다)
   - **Fog Of War Material**: `Assets/Shader/FogMaterial.mat` (비워두면 자동으로 찾습니다)

#### Tile Spotlight 설정 (원형 시야)
- **Spotlight Radius**: 플레이어 주변 원형 시야의 반지름 (기본값: 5)
- **Spotlight Softness**: 원형 시야 가장자리의 부드러움 (기본값: 1)

#### Flashlight 설정 (손전등 효과)
- **Enable Flashlight**: 손전등 효과 활성화 여부 (기본값: true)
- **Disable Circular Spotlight**: 원형 spotlight 비활성화 (손전등만 보기, 디버그용)
- **Flashlight Angle**: 손전등 각도 (도 단위, 기본값: 60)
- **Flashlight Distance**: 손전등 거리 (월드 단위, 기본값: 15)
- **Flashlight Brightness**: 손전등 밝기 (0-1, 기본값: 1.0)
- **Flashlight Softness**: 손전등 가장자리 부드러움 (월드 단위, 기본값: 2)

### 3. 머티리얼 설정 조정 (선택사항)
`Assets/Shader/FogMaterial.mat`을 선택하여 다음 속성을 조정할 수 있습니다:
- **Fog Color**: 안개의 색상 (기본값: 검은색)
- **Radius**: 시야 반경 (0~0.5, 기본값: 0.3) - 참고: 이 값은 FogOfWarController의 Spotlight Radius로 덮어씌워집니다
- **Softness**: 안개 가장자리의 부드러움 (기본값: 0.15) - 참고: 이 값은 FogOfWarController의 Spotlight Softness로 덮어씌워집니다

> **참고**: 원형 spotlight와 손전등 설정은 `FogOfWarController`의 Inspector에서 조정하는 것이 더 편리합니다. 머티리얼의 값은 기본값으로만 사용됩니다.

### 4. Shadow Casting (물리 기반 시야) 설정
벽 뒤에 그림자가 생기는 리얼한 시야를 구현하려면 다음 설정을 추가합니다.

1. **머티리얼 생성**: `Assets/Shader/StencilMask.shader`를 사용하는 머티리얼(`LightMaskMaterial`)을 생성합니다.
2. **플레이어에 FieldOfView 추가**:
   - 플레이어 오브젝트에 `FieldOfView.cs` 스크립트를 추가합니다.
   - `Mesh Renderer`의 Material 슬롯에 방금 만든 `LightMaskMaterial`을 할당합니다.
3. **레이어 설정**:
   - 벽(장애물) 타일맵의 레이어를 지정합니다 (예: `Wall`).
   - `FieldOfView` 컴포넌트의 `Obstacle Mask`에서 해당 레이어를 체크합니다.
4. **결과**: `FieldOfView`가 생성한 메쉬 영역은 스텐실 버퍼에 기록되어, `FogOfWar.shader`가 해당 영역을 그리지 않고 구멍을 뚫게 됩니다.

## 작동 원리
1. **데이터 전달**: `FogOfWarController`가 매 프레임 다음 데이터를 셰이더에 전달합니다:
   - 플레이어의 화면 좌표 (`_PlayerScreenPos`)
   - 플레이어에서 마우스로의 방향 벡터 (`_PlayerWorldDir`)
   - 원형 spotlight 설정 (`_SpotlightRadius`, `_SpotlightSoftness`)
   - 손전등 설정 (`_FlashlightAngle`, `_FlashlightDistance`, `_FlashlightBrightness`, `_FlashlightSoftness`)

2. **물리 기반 마스킹 (Shadow Casting)**: 
   - `FieldOfView` 스크립트가 `Physics2D.Raycast`를 통해 장애물을 감지하고 동적 메쉬를 만듭니다.
   - 이 메쉬는 `StencilMask.shader`를 통해 스텐실 버퍼에 "값 1"을 기록합니다.

3. **최종 렌더링**:
   - `FogOfWarFeature`가 화면 전체에 안개를 덮을 때, 스텐실 값이 1인 곳(빛이 닿는 곳)은 건너뛰고 나머지 영역만 검게 칠합니다.
   - 셰이더 내부 로직에 의해 두 가지 시야 효과가 결합됩니다:
     - **원형 Spotlight**: 플레이어 주변 고정된 반지름 내의 원형 시야
     - **삼각형 Flashlight**: 마우스 방향을 향한 삼각형 콘 형태의 손전등 시야
   - 두 효과는 `max(spotlight, flashlight)`로 결합되어 더 넓은 시야를 제공합니다.

## 문제 해결
- **안개가 보이지 않는 경우**:
  - Renderer2D에 FogOfWarFeature가 추가되었는지 확인
  - 카메라에 FogOfWarController가 추가되었는지 확인
  - FogMaterial이 올바르게 할당되었는지 확인

- **플레이어를 찾을 수 없다는 오류**:
  - 씬에 PlayerController 컴포넌트가 있는지 확인
  - 또는 FogOfWarController의 Player Transform 필드에 직접 할당

- **머티리얼을 찾을 수 없다는 오류**:
  - Assets/Shader/FogMaterial.mat 파일이 존재하는지 확인
  - 또는 FogOfWarController의 Fog Of War Material 필드에 직접 할당

- **Material ... doesn't have a color property '_PlayerScreenPos' 오류**:
  - 셰이더 파일(`Assets/Shader/FogOfWar.shader`)의 `Properties` 블록에 `_PlayerScreenPos`가 선언되어 있는지 확인
  - 스크립트가 값을 쓰기 전에 머티리얼에 해당 속성이 정의되어 있어야 함

