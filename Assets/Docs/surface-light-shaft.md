# 지상 빛기둥 + E키 복귀 설계

구멍 입구 착지 청크(`ImageChunkOverrider`가 칠하는 청크)에 **위에서 새어드는 햇빛 기둥**을 세우고,
그 아래에서 **E키로 지상에 복귀**할 수 있게 한다.

---

## 1. 왜 이 구조인가

### 1-A. Light2D를 못 쓴다

이 프로젝트의 지하 어둠은 URP Light2D가 아니라
`PlayerVisionOverlay`가 만드는 **월드 캔버스 어둠막**(`sortingOrder 999`, Default 레이어)이 화면을 덮는 방식이다.
Light2D를 넣어도 어둠막이 그 위에 깔리므로 빛이 보이지 않는다.

→ 빛기둥은 **어둠막보다 높은 `sortingOrder`를 가진 스프라이트**여야 한다. 기본값 `1000`.
   결과적으로 "멀리서도 저기 빛이 샌다"가 보이는 **랜드마크**가 된다(끄고 싶으면 인스펙터에서 낮추면 됨).

### 1-B. 스프라이트는 런타임 생성

에셋을 만들지 않고 `Texture2D`를 코드로 굽는다. 프로젝트의 UI·오버레이 관례와 같다
(`SettingsOverlayUI`, `UndergroundMinimap` 등 — "인스펙터에 스프라이트를 꽂으면 그걸 쓰고, 비우면 코드 생성").

### 1-C. 청크 자식이 아니라 씬 오브젝트

입구 청크는 풀에서 언로드/재로드된다. 자식으로 붙이면
`ImageChunkOverrider`의 재페인팅과 파이프라인 Phase 2의 자식 정리 루프에 휘말린다.
기존 `ExitTrigger`와 동일하게 **씬에 고정 배치**하면 이 문제가 통째로 사라진다.

### 1-D. 확인창은 기존 경로 재사용

`CodeConfirmPopup`은 단독 MonoBehaviour가 아니라 오버레이가 캔버스와 함께 들고 있는 클래스라
월드 오브젝트에서 직접 못 쓴다. 대신 이미 public인 `ExploreExitController.RequestExitToSurface()`를 부른다.

```
E → SurfaceExitBeacon.Interact()
      → ExploreExitController.RequestExitToSurface()
           → ExploreExitOverlayUI.Show(확인, 취소)      ← 씬에 있으면 우선
           → (없으면) ConfirmationPrompt 프리팹 폴백
      → 확인 → ConfirmExit() → StopTracking·오후 설정·스태미나 복구
                              → 창고 병합 → DemoUpground 로드
```

팝업 스타일·언어 전환·구 프리팹 폴백이 전부 공짜로 따라온다.

---

## 2. 구성

| 파일 | 역할 |
|---|---|
| `Assets/Shaders/SpriteAdditive.shader` | 스프라이트용 가산 합성(`Blend One One`). 알파를 밝기로 쓴다 |
| `Assets/Scripts/Render/World/SurfaceLightShaft.cs` | 빛기둥 비주얼 전담. 게임플레이 의존 0 |
| `Assets/Scripts/UI/Interaction/Scene/SurfaceExitBeacon.cs` | `IInteractable` — E → `RequestExitToSurface()` |

두 컴포넌트는 **서로를 모른다.** 빛기둥만 장식으로 쓰거나, 빛기둥 없이 E 지점만 두는 것도 된다.

### 2-A. `SurfaceLightShaft`

**기준점 = 빛이 닿는 바닥.** 트랜스폼 위치에 바닥 웅덩이가 생기고 기둥은 위로 뻗는다.
(레벨 배치할 때 "여기가 빛 웅덩이이고 여기서 E를 누른다"가 바로 보이게 하기 위함)

굽는 텍스처 2~3장:

- **기둥** `64×128` — 사다리꼴을 알파에 직접 인코딩한다. 행마다 반폭을 `bottomWidth→topWidth`로 보간해
  그 밖은 알파 0. 세로 밝기는 위(구멍)가 최대, 아래로 `bottomFade`까지 감쇠.
  맨 위 6%는 살짝 죽여 천장에서 잘린 직선이 안 보이게 한다.
- **바닥 웅덩이** `64×32` — 타원 방사 그라데이션 (`showFloorPool`)
- **먼지 알갱이** `16×16` — 원형 그라데이션 (`dustCount > 0`일 때만)

애니메이션은 `Update` 한 곳:
- 알파 사인 펄스(`pulseAmount`, `pulseSpeed`) — 기둥과 웅덩이에 함께 적용
- 먼지 알갱이 하강 + 좌우 흔들림. 바닥에 닿으면 꼭대기로 되돌린다(풀 없이 재사용, 할당 0)

`Sprite.Create`는 **PPU=1**로 만든다 → 스프라이트 월드 크기가 텍셀 수와 같아지고,
`localScale`이 그대로 월드 크기 비율이 된다(`GameOverSequenceUI`와 같은 방식).

**셰이더 해석 순서**: 인스펙터 슬롯 → `Shader.Find("Custom/SpriteAdditive")` → `Sprites/Default`.
`MineralPickupGlow`가 `Custom/SpriteOutline`을 다루는 방식과 같다.
빌드에서 가산 합성을 보장하려면 인스펙터 슬롯에 꽂거나
Project Settings > Graphics > Always Included Shaders에 넣는다. 못 찾아도 알파 블렌딩으로 곱게 degrade한다.

### 2-B. `SurfaceExitBeacon`

- `IInteractable` + `IInteractionPrompt`
- `InteractionPriority = 10` — 엘리베이터와 같은 값. 광물(0)보다 먼저 잡힌다
- 문구 키 `interact_surface_enter` ("E - 지상으로 나가기") — **이미 CSV에 있다**. 추가 작업 없음
- `RequireComponent(CircleCollider2D)`. `Reset()`이 `isTrigger=true`, `radius=2`를 잡아준다
  (`PlayerInteractor`는 플레이어 콜라이더의 `Overlap`으로 탐지하므로 겹치는 콜라이더가 필수)
- `exitController`가 비어 있으면 `FindFirstObjectByType`으로 1회 해석 후 캐시

---

## 3. 씬 세팅 (DemoUnderground)

1. 빈 GameObject 생성 — 이름 예: `SurfaceExitBeam`
2. `SurfaceLightShaft` + `SurfaceExitBeacon` 추가 (콜라이더는 자동 부착)
3. 위치를 **기존 `ExitTrigger`(local y 0.482, 20×2 박스) 아래**, 빛이 바닥에 닿을 지점에 둔다
4. `height`를 천장 구멍까지 닿게 조절

기존 `ExitTrigger`는 **건드리지 않는다.** 빛기둥이 그 아래라 서로 안 겹치고,
`ExploreExitController`의 `_promptedInZone` 플래그는 트리거 전용이라 E키 경로와 간섭하지 않는다.
(둘 다 `_isExiting` 가드를 공유하므로 중복 씬 로드도 안 난다)

---

## 4. 안 한 것

- **지도 마커 등록** — 입구 마커는 이미 `MapMarkerRegistry`/`IMapEntrance`가 다루는 영역이고 이번 요구와 무관
- **전용 효과음** — 새 `SfxKeys` 없이 간다. 확인창 UI 사운드는 오버레이가 이미 낸다
- **`ExitTrigger` 제거** — 자동 팝업은 그대로 유지(사용자 결정)
- **`ImageChunkOverrider` 수정** — 빛기둥이 청크 자식이 아니므로 손댈 이유가 없다

---

## 5. 검증 항목

- [ ] 빛기둥이 어둠막 위로 보인다 (멀리서도)
- [ ] 먼지가 기둥 폭 안에서만 떨어지고 바닥에서 되돌아간다
- [ ] 빛 아래 서면 "E - 지상으로 나가기" 문구가 뜬다
- [ ] E → 확인창 → 확인 시 정산 후 `DemoUpground` 진입
- [ ] 확인창에서 취소 후 다시 E를 누르면 또 뜬다 (트리거와 달리 재진입 제한 없음)
- [ ] 같은 자리에 광물이 떨어져 있어도 빛기둥이 먼저 잡힌다 (우선순위 10)
- [ ] 청크가 언로드→재로드돼도 빛기둥이 사라지지 않는다
