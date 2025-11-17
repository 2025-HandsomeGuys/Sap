# 전장의 안개 (Fog of War) 설정 가이드

## 개요
이 프로젝트에는 주인공 주변을 원형으로 볼 수 있게 하는 전장의 안개 시스템이 구현되어 있습니다.

## 파일 구조
- `Assets/Shader/FogOfWar.shader` - 전장의 안개 셰이더
- `Assets/Shader/FogMaterial.mat` - 셰이더를 사용하는 머티리얼
- `Assets/Scripts/FogOfWarController.cs` - 플레이어 위치를 셰이더에 전달하는 컨트롤러
- `Assets/Scripts/FogOfWarFeature.cs` - URP 렌더링 기능

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

### 3. 머티리얼 설정 조정 (선택사항)
`Assets/Shader/FogMaterial.mat`을 선택하여 다음 속성을 조정할 수 있습니다:
- **Fog Color**: 안개의 색상 (기본값: 검은색)
- **Radius**: 시야 반경 (0~0.5, 기본값: 0.3)
- **Softness**: 안개 가장자리의 부드러움 (기본값: 0.15)

## 작동 원리
1. `FogOfWarController`가 매 프레임 플레이어의 위치를 화면 좌표로 변환하여 셰이더에 전달합니다.
2. `FogOfWarFeature`가 URP 렌더링 파이프라인에 통합되어 화면 전체에 안개 효과를 적용합니다.
3. 셰이더가 플레이어 위치를 중심으로 원형 마스크를 생성하여 안개를 렌더링합니다.

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

