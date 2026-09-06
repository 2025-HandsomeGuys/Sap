# 엑스레이(X-Ray) 유물 설계 — RelicID 4025

`adding-a-relic.md` 가이드 기반. 액티브·지속형. **3초간 화면이 흑백 반전(X-ray)되고, 화면 내 흙 너머의
숨은 광물·특수청크·함정이 투시된다. 쿨타임 2분.**

## 1. 정체성

| 항목 | 결정 |
|------|------|
| 타입 | **액티브·지속형**(Duration>0). Q/R 발동 |
| 지속 | Lv1 **3초**(레벨 3/4/5) |
| 쿨타임 | **120초 고정** |
| 화면 효과 | **흑백 네거티브(반전)** — URP 렌더러 피처 + 셰이더 |
| 투시 대상 | **광물 · 특수청크/블록 · 함정/위험** |
| 투시 방식 | 화면 내 대상 SpriteRenderer **정렬순서 상향(지형 위) + 하이라이트 틴트** |

## 2. 아키텍처

```
XRayRelic (액티브, dur 3/4/5s, cd 120s)
  ├─ OnEquip     → XRayController 생성(코드, 프리팹 불필요)
  ├─ OnActivate  → controller.Begin()   (투시 스캔 + _XRayAmount 목표 1)
  ├─ OnActiveEnd → controller.End()      (원복 + _XRayAmount 목표 0)
  └─ OnUnequip   → controller.ForceOff() + 파괴

XRayController (MonoBehaviour)
  ├─ Update: _XRayAmount 페이드(MoveTowards) → Shader.SetGlobalFloat
  ├─ 발동 중 0.4s마다 재스캔(새 로드 청크 편입)
  └─ 투시: 카메라 뷰 내 대상 SpriteRenderer 정렬 상향 + 틴트, 종료 시 원복

XRayRendererFeature (+ Custom/XRay 셰이더)
  └─ _XRayAmount>0일 때만 카메라 컬러를 흑백반전 합성(풀스크린 blit)
```

## 3. 화면 흑백 반전 — URP 렌더러 피처 + 셰이더

프로젝트의 `PixelateLightRendererFeature`와 동일 패턴.

- **`Assets/Shaders/XRay.shader`** (`Custom/XRay`): `_BlitTexture` 샘플 → 휘도(luma) 계산 →
  `(1-luma)` 흑백 네거티브 → 전역 `_XRayAmount`로 원본↔반전 lerp. URP Blitter 풀스크린 규약.
- **`Assets/Scripts/Render/XRayRendererFeature.cs`**:
  - RenderGraph 경로(URP17 기본): `activeColorTexture` → 임시 텍스처로 material blit → `cameraColor` 스왑.
  - Legacy(Compatibility) 경로: `Blitter.BlitCameraTexture` 왕복.
  - `AddRenderPasses`에서 `_XRayAmount<=0`이면 패스 스킵(미발동 시 0 비용), Game 카메라만.
  - Injection Point = **After Rendering Transparents**(월드에만 적용, ScreenSpace-Overlay HUD는 원색 유지).

> **사용자 Unity 작업(1회)**: URP Renderer 에셋의 Renderer Features에 **XRayRendererFeature 추가**
> (Pixelate와 같은 방식). 카메라 Post Processing 여부와 무관(피처가 직접 blit).

## 4. 투시(흙 너머 보기)

광물·특수블록·함정은 **실제 스프라이트 GameObject**로 존재하되 지형 SpriteRenderer(order 0)의
드로우 순서에 가려 안 보인다. 발동 중 이들을 **지형 위로 끌어올려** 투시한다.

- 정렬 기준 1회 해석: `TerrainChunk`의 SpriteRenderer에서 `sortingLayerID`/`sortingOrder`를 읽어
  **같은 레이어 + order+300**을 투시 정렬값으로 사용(폴백 Default/300).
- 카메라 뷰(+여유 3유닛) 내 대상만 편입. 각 SpriteRenderer의 원래
  (layerId, order, color, enabled)를 저장 → 정렬 상향 + 하이라이트 틴트 + `enabled=true`.
- 종료(End)·해제(ForceOff) 시 전부 **원복**.
- 발동 중 0.4s마다 재스캔 → 새로 로드된 청크의 오브젝트도 편입(중복은 `HashSet`으로 방지).

**대상 컴포넌트**(FindObjectsByType, 유니온):
`MineralItemController`(광물) · `DiggableBlockBase`(크리스탈 등) · `FallingHazardBase`(고드름 등) ·
`RollingRockEntity`(구르는 바위) · `InteractableBlockBase`(상호작용 블록).

## 5. 레벨 파라미터

```csharp
durationPerLevel = { 3f, 4f, 5f };   // Lv1=3초(기획)
cooldown         = 120f;             // 2분(기획)
highlightColor   = (0.4, 1, 0.9);    // 투시 틴트
fadeSpeed        = 6f;               // _XRayAmount 페이드
cameraPadding    = 3f;               // 화면 밖 여유(유닛)
```

## 6. 수정·신규 파일

**신규**
- `Assets/Shaders/XRay.shader` (`Custom/XRay`)
- `Assets/Scripts/Render/XRayRendererFeature.cs`
- `Assets/Scripts/Gameplay/Relics/Behaviours/XRayRelic.cs`
- `Assets/Scripts/Gameplay/Relics/Behaviours/XRayController.cs`

**공유(순차 편집)**
- `Data/RelicID.cs` — `XRay = 4025`
- `Editor/RelicSliceAssetGenerator.cs` — 등록 1줄 + `db.allRelics`
- `Gameplay/Relics/Debug/RelicDebugGranter.cs` — `KeypadPeriod` grant+equip, slot 1

**게임 코드 훅**: 없음(기존 액티브 상태기계 + SpriteRenderer 조작만).

## 7. 엣지 케이스

| 케이스 | 처리 |
|--------|------|
| 미발동 | `_XRayAmount=0` → 렌더 피처 패스 스킵(0 비용) |
| 발동 중 새 청크 로드 | 0.4s 재스캔으로 편입 |
| 렌더러가 꺼진 지하 오브젝트 | `enabled=true` 강제 표시, 원복 시 복구 |
| 대상 파괴(채굴 등) | `sr==null` 가드로 원복 스킵 |
| 유물 해제/파괴 | `ForceOff` — 즉시 원복 + `_XRayAmount=0` |
| HUD 가독성 | 피처 주입 After Transparents → Overlay UI 원색 유지 |
| 지속 종료 | 상태기계 activeEnded → `End()` → 페이드아웃 + 원복 → 쿨타임 |

## 8. 검증 (사용자 인게임)

1. **URP Renderer에 XRayRendererFeature 추가**(1회) + `Tools > Relic > Generate Slice Assets`.
2. `KeypadPeriod`로 슬롯1 장착 → Q/R 발동.
3. 3초간 화면 흑백 반전 + 흙 속 광물/크리스탈/고드름 등이 하이라이트로 드러나는지 확인.
4. 종료 시 화면·정렬 정상 복귀, 쿨타임 2분 확인.
5. 레벨↑ → 지속 3→4→5초 체감.

## 9. 리스크

- **URP RenderGraph API 버전차**: `AddBlitPass`/`BlitMaterialParameters`/`CreateRenderGraphTexture`는
  URP 17.3 기준. 컴파일 이슈 시 알려주면 조정.
- **정렬 레이어 가정**: 지형과 같은 레이어+order로 올리는 전제. 특수 셰이더/스텐실로 그려지는
  일부 블록은 정렬만으로 안 드러날 수 있음(그 경우 하이라이트 오버레이 방식으로 보강 가능).
- **성능**: 5개 타입 FindObjectsByType를 0.4s마다 — 2분 쿨 액티브라 무시 가능.
