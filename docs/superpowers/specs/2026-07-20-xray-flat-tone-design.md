# XRay 3톤 단색화 설계

작성일: 2026-07-20

## 배경

현재 엑스레이 유물은 "톤 다운된 원본"에 가깝다.

- `XRayController`가 투시 대상 `SpriteRenderer`의 `color`에 하이라이트 색을 **섞는다**(Lerp `_highlightBlend`).
- 풀스크린 `Custom/XRay`가 휘도를 살린 채 청록 틴트 + 감광만 입힌다.

결과적으로 지형·배경·광물 모두 원래 텍스처의 명암과 색이 남아, X-ray 특유의 "실루엣만 읽히는" 인상이 나오지 않는다.

## 목표

화면을 **3톤 단색**으로 재구성한다.

| 톤 | 대상 | 상대 밝기 |
|---|---|---|
| 밝음 | 광물·특수블록·낙하함정·구르는 바위 | 가장 밝게 |
| 중간 | 안 판 지형(청크) | 중간 |
| 어두움 | 배경(판 굴 너머로 보이는 배경 타일) | 가장 어둡게 |

3톤인 이유: 2톤(오브젝트 / 나머지)은 판 굴과 안 판 땅이 구분되지 않아 터널 형태를 읽을 수 없다. 3톤이면 터널 실루엣이 유지되면서 광물만 확실히 튄다.

## 접근 방식

**머티리얼 스왑.** XRay 발동 중 대상 `SpriteRenderer`의 `sharedMaterial`을 전용 플랫 머티리얼로 교체한다.

검토했으나 채택하지 않은 대안:

- **렌더링 레이어 마스크 + 풀스크린 3색 매핑** — 정석이지만 렌더 패스가 하나 늘고 URP 세팅 작업이 붙는다. 결과물이 머티리얼 스왑과 거의 같아 과하다.
- **풀스크린에서 휘도 구간 나누기** — 셰이더만 고치면 되지만, 흙 색과 배경 색의 휘도가 겹치면 경계가 뭉개져 3톤이 안정적으로 나오지 않는다.

머티리얼 스왑이 유리한 이유: 지형 텍스처의 **알파가 곧 "안 판 땅" 실루엣**이라, 판 굴은 자동으로 뚫려 뒤 배경 톤이 보인다. 3톤 분리를 별도 마스크 없이 얻는다.

## 컴포넌트

### 1. `Assets/Shaders/XRayFlat.shader` (신규)

`SpriteRenderer`에 물릴 수 있는 언릿 스프라이트 셰이더.

- `_MainTex`의 **알파만** 사용해 실루엣을 만들고, RGB는 `_FlatColor`로 출력한다.
- 전역 `_XRayAmount`(기존 프로퍼티 재사용)로 `원본 RGB ↔ _FlatColor`를 lerp 한다. 페이드가 그대로 유지된다.
- 언릿이므로 2D Light 영향을 받지 않는다. 조명 밝기에 따라 톤이 흔들리지 않고 균일한 X-ray 톤이 나온다.
- 알파 블렌딩, `_RendererColor`, 버텍스 컬러 등 `Sprites/Default` 규약을 유지한다. 배경 타일이 `SpriteDrawMode.Tiled`를 쓰므로 이걸 깨뜨리면 안 된다.
- `MaterialPropertyBlock`으로 색을 주입하는 렌더러가 있을 수 있다. 플랫 셰이더는 해당 프로퍼티를 무시하도록 이름을 맞춘다.

### 2. 공유 머티리얼 3개 (신규 에셋)

`sharedMaterial` 스왑이므로 렌더러당 머티리얼 인스턴스가 생기지 않는다.

| 머티리얼 | 대상 |
|---|---|
| `XRayFlat_Terrain` | 지형 청크 |
| `XRayFlat_Background` | 배경 타일 |
| `XRayFlat_Object` | 광물·특수블록·함정·구르는 바위 |

색은 하드코딩하지 않는다. `XRayRelic` 인스펙터의 필드를 `XRayController`가 런타임에 각 머티리얼의 `_FlatColor`로 세팅한다. 3톤 밸런싱을 에디터에서 바로 조정하기 위함이다.

### 3. `XRayController` 확장

**Entry 구조체**에 원본 `sharedMaterial` 필드를 추가한다. 기존에 보관하던 정렬레이어·order·color·enabled와 함께 `RestoreAll()`에서 복원한다.

**수집 대상을 3분류로 확장한다.**

- **지형** — `InfinityMapManager.GetAllActiveChunks()`로 로드된 청크 전체. 화면 컬링하지 않는다(카메라 이동 시 화면 가장자리에서 원본 색이 번쩍이는 것을 막는다). `FindObjectsByType`를 쓰지 않으므로 비용이 낮다.
  - 지형 테두리 렌더러(`SpriteTerrainBorder` / `SpriteBorderBaker`)가 별도 `SpriteRenderer`라면 지형 톤으로 같이 스왑한다. 누락하면 단색 땅 위에 원본 색 테두리만 남아 지저분해진다.
- **배경** — `BackgroundManager`에 `activeTiles` 읽기 전용 접근자를 추가해 순회한다. 역시 컬링하지 않는다.
- **오브젝트** — 기존 `AddTargets<T>` 5종(`MineralItemController`, `DiggableBlockBase`, `FallingHazardBase`, `RollingRockEntity`, `InteractableBlockBase`)을 그대로 사용한다.
  - **`sr.color` Lerp(`_highlightBlend`)는 제거한다.** 단색은 이제 머티리얼이 담당한다.
  - 정렬순서를 지형 위로 올리는 처리와 `sr.enabled = true`(흙 속에서 꺼져 있던 렌더러 강제 표시)는 유지한다.

**리스캔 주기를 분리한다.** 지형·배경은 새로 로드된 직후 원본 색으로 남는 시간을 줄이기 위해 0.15초 주기로, 오브젝트는 기존 0.4초 주기로 스캔한다.

**종료 경로는 기존 `ForceOff()`를 그대로 쓴다.** 머티리얼도 여기서 복원되므로 유물 해제·`OnDisable`·`OnDestroy` 시 지형이 단색으로 굳는 사고가 나지 않는다.

**페이드 아웃 처리 순서:** 언릿 전환 때문에 `_XRayAmount = 0`이어도 조명이 있던 픽셀은 원본과 완전히 일치하지 않는다. 따라서 페이드 아웃이 **완료된 시점에** 머티리얼을 원복해 마지막에 톡 튀는 것을 막는다.

### 4. `Custom/XRay` (기존 풀스크린) 역할 축소

현재 로직(휘도 그레이스케일 → 청록 틴트 → 감광)을 그대로 두면 3톤을 다시 뭉개 2톤처럼 만든다.

- 그레이스케일 + 틴트 합성을 **제거**한다.
- **전체 감광 + 가장자리 비네트**만 남긴다. 톤 결정은 전적으로 머티리얼이 담당하고, 풀스크린은 "X-ray 모드다"라는 분위기만 담당한다.
- `XRayRendererFeature`(`_XRayAmount <= 0`이면 패스 스킵)는 수정하지 않는다.

## 데이터 흐름

```
XRayRelic (인스펙터: 3톤 색)
  └─ XRayController.Begin()
       ├─ 3개 공유 머티리얼에 _FlatColor 주입
       ├─ Shader.SetGlobalFloat(_XRayAmount, 0→1 페이드)
       │    ├─ XRayFlat.shader     : 원본 RGB ↔ _FlatColor lerp
       │    └─ XRayRendererFeature : 감광 + 비네트 패스
       ├─ PlayerVisionOverlay.SetDarknessOverride(0)   [기존]
       └─ Scan()
            ├─ 지형   (0.15s) → XRayFlat_Terrain
            ├─ 배경   (0.15s) → XRayFlat_Background
            └─ 오브젝트(0.4s) → XRayFlat_Object + 정렬순서 상승 + enabled 강제
  └─ End() / ForceOff()
       └─ 페이드 아웃 완료 후 RestoreAll() — 머티리얼·정렬·색·enabled 원복
```

## 범위 밖

- 오브젝트 종류별(광물 / 위험물 / 특수블록) 색 세분화. 3톤으로 먼저 확인한다.
- 플레이어·파티클·VFX의 단색화. 원본 색으로 남긴다. 단색 지형 위에서 플레이어 위치가 오히려 명확해진다.
- UI 요소.

## 검증

Unity Test Runner 실행은 사람이 직접 수행한다(프로젝트 규약). 플레이 모드에서 확인할 항목:

1. 유물 발동 시 지형·배경·광물이 각각 지정한 3톤으로 보인다.
2. 판 굴 실루엣이 배경 톤으로 뚫려 보인다.
3. 흙 속에 묻힌 광물이 지형 위로 떠올라 밝은 톤으로 보인다.
4. 페이드 인/아웃이 부드럽고, 종료 시점에 색이 튀지 않는다.
5. 발동 중 새로 로드된 청크가 즉시(0.15초 이내) 단색으로 편입된다.
6. 발동 중 씬 전환·유물 해제 시 지형이 단색으로 굳지 않는다.
7. 배경 타일의 `Tiled` drawMode가 깨지지 않는다.
