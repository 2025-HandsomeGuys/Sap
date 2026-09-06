# 동상 비네트 오버레이 구현 설계
@tags: frostbite, overlay, design, UI, rendering, cold, stamina

## 개요

동상(`frostbite`)이 쌓일수록 화면 가장자리에 서리/어둠 이미지가 점점 짙어지는 시각적 압박 효과.
HitFlash처럼 순간적으로 반짝이는 게 아니라, 누적 수치에 따라 알파값이 부드럽게 유지된다.

---

## 구성 요소

### 1. 비네트 이미지 (Sprite)

화면 가장자리는 불투명, 중앙은 투명한 방사형 그라디언트 이미지가 필요하다.

**제작 방법 (두 가지 중 선택)**

**A) 직접 제작**
- Photoshop/Aseprite 등에서 512×512 이상 PNG 생성
- 가장자리 → 중앙 방향으로 불투명(흰색 or 파란빛) → 완전 투명 그라디언트
- 서리 느낌을 원하면 가장자리에 결정 느낌의 텍스처 추가

**B) Unity Radial Gradient 셰이더 사용**
별도 이미지 없이 UI/Default 머티리얼에 셰이더를 적용해도 가능하나, 단순하게 이미지로 처리하는 것이 작업량이 적다.

Import 설정:
- Texture Type: `Sprite (2D and UI)`
- Alpha Is Transparency: `true`

---

### 2. Prefab 구성

```
[FrostbiteOverlay] (Canvas)
  - Render Mode: Screen Space - Overlay
  - Sort Order: HitFlashUI보다 낮게 (예: HitFlash=10, FrostbiteOverlay=5)
  CanvasGroup (컴포넌트)
  FrostbiteOverlayUI (컴포넌트)
  |
  └─ [Image] (비네트 이미지)
       - RectTransform: 앵커/오프셋 전체 화면 꽉 채우기
         (Anchor: stretch-stretch, Left/Right/Top/Bottom = 0)
       - Image.Color: 흰색 또는 연한 파란색 (#B0D8FF 등)
       - Raycast Target: OFF
```

---

### 3. FrostbiteOverlayUI 스크립트

파일: `Assets/Scripts/UI/FrostbiteOverlayUI.cs`

---

## 연동 흐름

```
[PlayerZoneChecker]
  매 초마다 AddFrostbite() 호출
       |
       v
[StaminaManager]
  frostbite 수치 누적
  PlayerStat.MarkDirty() → MaxStamina 감소 반영
       |
       v
[FrostbiteOverlayUI.Update()]
  staminaManager.Frostbite 읽기
  → frostbite / originalMaxStamina 비율 계산
  → CanvasGroup.alpha를 부드럽게 보간
```

---

## Inspector 설정 체크리스트

1. `[FrostbiteOverlay]` GameObject 생성, Canvas 컴포넌트 설정
   - Render Mode: `Screen Space - Overlay`
   - Sort Order: `5` (HitFlashUI보다 낮게)
2. CanvasGroup 컴포넌트 추가
3. FrostbiteOverlayUI 컴포넌트 추가
   - `Stamina Manager` 슬롯에 플레이어의 StaminaManager 연결 (또는 비워두면 자동 탐색)
   - `Max Alpha`: `0.75` (취향에 따라 조절)
   - `Lerp Speed`: `3`
4. 자식 Image 오브젝트 생성
   - 비네트 Sprite 할당
   - RectTransform stretch-stretch, 모든 오프셋 0
   - Raycast Target: OFF

---

## 수치 튜닝 가이드

| 파라미터 | 낮음 | 높음 | 권장 |
|---------|------|------|------|
| `maxAlpha` | 효과 약함, 덜 압박 | 화면 많이 가려짐 | 0.6~0.8 |
| `lerpSpeed` | 느리게 변화, 부드러움 | 즉각 반응 | 2~4 |

`checkInterval`(현재 1초)마다 `maxStaminaReduction`씩 쌓이므로:
- Ice 지층 (0.5/초): MaxStamina 100 기준 → 200초 만에 최대 알파 도달
- 빠른 압박을 원한다면 `maxStaminaReduction` 값을 높이거나 `maxAlpha`를 올림

---

## 참고: 회복 시 자연스러운 페이드아웃

`frostbite`가 `RecoverStatus()`로 감소하면 `FrostbiteOverlayUI`의 `_targetAlpha`도 함께 낮아지고,
`lerpSpeed`에 따라 부드럽게 페이드아웃된다. 별도 처리 불필요.
