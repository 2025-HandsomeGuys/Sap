# 삽 파기 모양 마스크 이미지화 — 설계

작성일: 2026-08-06

## 배경

삽(`SapStrategy`, toolIndex=1)의 파기 구멍 모양은 현재 `TerrainModifier.ProcessDigPixels`의
**회전 타원** 수식으로 결정된다. 마우스 방향으로 회전하고, 전방(`localX >= 0`)만
`VerticalScale`(기본 1.5)배 늘어난 타원이다. 반지름은 차징 비율에 비례한다.

이 모양을 PNG 이미지(마스크)로 정의할 수 있게 한다. 삽 전용이며, 크기 배율을
인스펙터에서 조절할 수 있어야 한다.

### 기존에 있던 것과 왜 안 쓰는가

`TerrainCarver.ClearHole`(`Assets/Scripts/Gameplay/Terrain/Tiles/Generation/TerrainCarver.cs:111`)이
"마스크 이미지의 불투명 픽셀 위치를 지형에서 지우는" 코드를 이미 갖고 있다.
그러나 호출부가 `DiggableRock`(돌 파괴 시 지형에 새겨진 돌 픽셀 제거) 두 곳뿐이고,
삽 파기에 재사용하기엔 아래가 전부 빠져 있다.

| 삽에 필요한 것 | `ClearHole` 현황 |
|---|---|
| 마우스 방향 회전 + 차징 스케일 | 없음. 축정렬 그대로 스탬프 |
| 여러 청크 걸침 | 단일 청크 전용, 경계에서 잘림 |
| `IndestructibleMask` 보호 | 무시하고 지움 |
| 돌기 제거·침식·공중섬 제거 | 없음 |
| dirty 처리 | `isTextureDirty`/`isDirty` 직접 세팅 + Visualizer 직접 호출 (CLAUDE.md §3·§5 위반) |
| 실제 파였는지 bool 반환 | 없음 (스태미나 비용 판정 불가) |

따라서 `ClearHole`은 건드리지 않고, 위 항목이 이미 다 갖춰진 `ProcessDigPixels`에
마스크 분기를 추가한다.

---

## 1. 설정 위치 — `PlayerMining` 인스펙터

`Assets/Scripts/UI/Player/PlayerMining.cs`에 필드 2개를 추가한다.

```csharp
[Header("삽 파기 모양 (비우면 기존 타원)")]
[Tooltip("삽이 파는 구멍 모양. Read/Write Enabled 필수. 삽이 +X(오른쪽)를 향하는 그림으로 그릴 것")]
public Texture2D shovelDigMask;

[Tooltip("마스크 크기 배율. 1 = 마스크 가로폭이 파기 지름과 같음")]
public float shovelDigMaskScale = 1f;
```

`Start()`와 `OnValidate()`에서 `TerrainModifier.SetShovelDigMask(shovelDigMask, shovelDigMaskScale)`을
호출한다. `OnValidate`를 거는 이유는 플레이 중 인스펙터로 배율을 만지면 즉시 반영되게 하기 위함이다
(이미지 모양을 눈으로 맞춰보는 게 이 기능의 주 용도다).

---

## 2. 전달 경로 — static 상태

마스크가 필요한 지점은 `TerrainModifier.ProcessDigPixels`이고, 거기까지 가려면
`SapStrategy.PerformSapDig` → `TerrainChunk.Dig` → `TerrainModifier.Dig` 2단계의
시그니처를 뚫어야 한다. `TerrainChunk.Dig`는 드릴·곡괭이·`StaticChunkTerrainManager` 등
여러 곳에서 호출되므로 시그니처를 건드리지 않는다.

**`TerrainModifier`의 static 상태로 둔다.** `s_colliderUpdateInterval`·`s_maxIslandSize`와
같은 패턴이다(CLAUDE.md §4). 마스크는 전역 1개이므로 청크마다 인스턴스 사본을 가질 이유도 없다.

```csharp
// TerrainModifier
private static bool[] s_shovelMaskBits;   // 알파 > 10 인 픽셀 = true. null = 타원 폴백
private static int    s_shovelMaskW, s_shovelMaskH;
private static float  s_shovelMaskScale = 1f;

// 로그 중복 억제용 — 마지막으로 로그를 찍은 조합 (§3-1)
private static int    s_lastLoggedTexId;
private static float  s_lastLoggedScale;
private static bool   s_lastLoggedOk;

public static void SetShovelDigMask(Texture2D tex, float scale) { ... }

/// <summary>파기 시점 진단용(§3-2). 현재 마스크 경로가 살아 있는지.</summary>
public static bool HasShovelDigMask => s_shovelMaskBits != null;
```

### 베이크

`SetShovelDigMask` 호출 시 텍스처를 `bool[]`로 **1회** 변환한다. 판정 임계값은
알파 > 10 — `TerrainCarver.ALPHA_THRESHOLD`와 같은 값을 쓴다.
픽셀 루프 안에서 `GetPixel`을 부르지 않는다.

---

## 3. 판정 — 타원 분기를 남기고 마스크 분기를 추가

`ProcessDigPixels`의 기존 타원 코드는 **손대지 않는다.** 옆에 분기를 하나 더 둔다.

```
if (마스크 사용 가능)  → 마스크 샘플링 판정
else                   → 기존 타원 판정 (원본 그대로)
```

### 마스크 사용 가능 조건

하나라도 실패하면 자동으로 타원으로 떨어진다.

1. `toolIndex == 1` (삽) — 파기 시점에 판정
2. 베이크 성공 (`s_shovelMaskBits != null`) — 세팅 시점에 확정

베이크 성공 조건은 §3-1에서 다룬다.

### 3-1. 실패 사유 로그 — 세팅 시점에만 찍는다

**로그는 `SetShovelDigMask` 안에서만 찍는다. 파기 루프에서는 절대 찍지 않는다.**
삽질 1회당 로그 1줄이면 도배되고, 실패 사유는 전부 세팅 시점에 판정 가능하다.

`SetShovelDigMask(tex, scale)`는 아래 순서로 검사하고, **실패한 첫 항목의 사유를 남긴 뒤**
`s_shovelMaskBits = null`로 두고 반환한다(= 타원 폴백).

| # | 조건 | 실패 시 로그 | 레벨 |
|---|---|---|---|
| 1 | `tex != null` | `[ShovelDigMask] 마스크 미설정 → 기존 타원 모양 사용` | `Log` |
| 2 | `scale > 0` | `[ShovelDigMask] '{tex.name}' 배율이 {scale} → 타원 폴백. shovelDigMaskScale은 0보다 커야 함` | `LogWarning` |
| 3 | `tex.isReadable` | `[ShovelDigMask] '{tex.name}' Read/Write Enabled가 꺼져 있음 → 타원 폴백. Import Settings에서 켤 것` | `LogError` |
| 4 | 불투명 픽셀 ≥ 1 | `[ShovelDigMask] '{tex.name}' {w}x{h}에 알파>10 픽셀이 하나도 없음 → 타원 폴백` | `LogWarning` |

성공 시에도 1줄 남긴다 — 이미지가 실제로 먹었는지 눈으로 확인할 수단이 필요하다.

```
[ShovelDigMask] '{tex.name}' {w}x{h} 적용. 불투명 {n}px ({퍼센트}%), 배율 {scale}
```

**중복 억제**: `OnValidate`는 인스펙터를 만질 때마다 불린다. 마지막으로 로그를 찍은
`(텍스처 instanceID, scale, 결과)` 조합을 static으로 들고 있다가, 같은 조합이면 로그를 건너뛴다.
조합이 바뀌면(다른 이미지로 교체, 배율 변경, Read/Write를 켬) 다시 찍힌다.

### 3-2. 파기 시점 진단 — 기존 `LogDigs` 플래그에 얹는다

"삽인데 왜 이미지 모양이 안 나오지"를 확인할 수단은 이미 있는 디버그 경로에 붙인다.
`SapStrategy.PerformSapDig`의 `MiningStaminaTuning.LogDigs` 블록(F8 패널에서 토글)에
한 항목을 더한다.

```
shape=mask   또는   shape=ellipse(마스크 미적용)
```

기본값이 off이므로 평상시 도배되지 않는다.

### 수식

`ProcessDigPixels`에 이미 회전 좌표 `localX`/`localY`가 계산돼 있다(`cos = Cos(-angle)`).
그 뒤만 분기한다.

```
pxPerMaskPx = (radiusPx * 2 / maskW) * maskScale
inv         = 1 / pxPerMaskPx                      // 루프 밖에서 미리 계산, 나눗셈 제거

u = localX * inv + maskW * 0.5
v = localY * inv + maskH * 0.5

if (u < 0 || u >= maskW || v < 0 || v >= maskH) continue
if (!maskBits[(int)v * maskW + (int)u]) continue
```

- **기준 크기**: 마스크 가로폭 = 파기 지름. 마스크에 꽉 찬 원을 넣으면 기존 원형과 같은 크기가 나온다.
- **차징 연동**: `radiusPx`가 차징 비율을 이미 담고 있어 자동으로 스케일된다.
- **방향**: 마스크는 **삽이 +X(오른쪽)를 향하는 그림**으로 그린다. `cos`/`sin`이 마우스 방향으로 회전시킨다.
- **`VerticalScale` 미적용**: 마스크 경로에서는 전방 1.5배 늘림을 걸지 않는다.
  모양은 이미지가 정의한다. 둘 다 걸면 이중으로 늘어난다. (타원 분기는 기존대로 유지)
- **중심 정렬**: 마스크 중심이 `digCenter`(= `playerPos + dir * radius`)에 온다.
  구멍을 앞/뒤로 밀고 싶으면 마스크 이미지 안에서 그림 위치를 옮긴다 — 별도 오프셋 파라미터는 두지 않는다.

---

## 4. 같이 손봐야 하는 것

### 4-1. bounds 계산

`TerrainModifier.Dig`의 현재 margin은 타원 기준이다.

```csharp
int maxRadiusPx = Mathf.CeilToInt(radiusPx * Mathf.Max(1f, VerticalScale));
int margin      = maxRadiusPx + DIG_MARGIN_EXTRA;
```

마스크가 세로로 길거나 회전하면 이 범위를 벗어나 **마스크 일부가 잘린다.**
마스크 경로에서는 아래로 계산한다.

```csharp
// 회전 대각 여유 √2 ≈ 1.415
int maxRadiusPx = Mathf.CeilToInt(pxPerMaskPx * Mathf.Max(maskW, maskH) * 0.5f * 1.415f);
int margin      = maxRadiusPx + DIG_MARGIN_EXTRA;
```

이 값은 순회 범위일 뿐이고 실제 제거는 마스크 판정이 결정하므로, 넉넉해도 결과가 틀리지 않는다.
반대로 모자라면 잘린다. 여유 쪽으로 잡는다.

### 4-2. 돌 픽셀 판정 유지

삽은 지금 돌(`pixelType == 2`)을 파기 중심 근처에서만 판다.

```csharp
if (pixelType == 2 && toolIndex == 1)
    if (distSqr > sqrRadius * ROCK_DIG_THRESHOLD) continue;   // 0.04
```

마스크 경로에는 `distSqr`가 없다. 중심으로부터의 거리로 같은 규칙을 유지한다
(`sqrt(0.04) = 0.2`이므로 반경의 20%).

```csharp
float rockLimit = radiusPx * 0.2f;
if (localX * localX + localY * localY > rockLimit * rockLimit) continue;
```

---

## 5. 회귀 안전성

- 마스크 미설정이 기본값 → **기존 동작과 완전히 동일.**
- 곡괭이·드릴·폭발(`Explode`)·`StaticChunkTerrainManager` 경로는 `toolIndex != 1`이거나
  아예 다른 함수라 영향 없음.
- 불괴 픽셀 보호(`IndestructibleMask`, `IsAdjacentToIndestructible`)·돌기 제거·침식·
  공중섬 제거·파티클·`OnPixelDestroyed` 이벤트·스태미나 비용·다중 청크 처리는
  전부 `ProcessDigPixels` 바깥에 있어 그대로 탄다.
- `DigResult.RadiusPx`는 기존 의미(`radius * PPU`) 그대로 둔다. 소비처를 건드리지 않기 위함.

---

## 6. 범위 밖 — 하지 않는 것

**`DigRangePreview`는 건드리지 않는다.** 차징 중 표시되는 예고 이미지
(`Assets/Scripts/UI/Player/DigRangePreview.cs`)는 별도 인스펙터 스프라이트(`indicatorImage`)다.
마스크와 다른 그림이면 예고와 실제 구멍이 어긋나 보인다.

테스트 시에는 같은 이미지를 `indicatorImage`에도 수동으로 꽂는다.
자동 동기화가 필요해지면 별건으로 다룬다 — 프리뷰는 UI 캔버스 좌표계·회전 보간·
`fixedPreviewRadius` 같은 자체 로직을 갖고 있어 마스크 스케일과 1:1로 맞추는 게
단순 참조 공유로 끝나지 않는다.

---

## 7. 테스트

Unity Test Runner 실행은 사람이 직접 한다(CLAUDE.md). 아래는 작성할 EditMode 테스트다.

`TerrainModifier`의 마스크 좌표 변환은 순수 계산이므로 분리해서 테스트 가능하게 둔다.

- 마스크 미설정 → 타원 분기를 타는지 (파인 픽셀 집합이 기존과 동일)
- 꽉 찬 정사각 마스크 + scale 1 → 파기 지름과 마스크 가로폭이 일치
- scale 2 → 파인 영역 가로폭이 2배
- 회전: 세로로 긴 마스크를 90° 방향으로 쏘면 가로로 긴 구멍
- 범위 밖 `u`/`v` 클램프 없이 `continue` 되는지 (배열 밖 접근 없음)
- 전부 투명한 마스크 → 타원 폴백

수동 확인:
- 텍스처 Read/Write 끈 상태로 넣기 → `LogError`에 사유가 찍히고 타원으로 파짐
- 배율 0 입력 → `LogWarning` + 타원
- 전부 투명한 PNG → `LogWarning` + 타원
- 정상 이미지 → 적용 로그 1줄(크기·불투명 픽셀 수·배율)
- `OnValidate` 도배 안 되는지 — 인스펙터에서 같은 값 반복해 만져도 로그가 1번만
- 플레이 중 `shovelDigMaskScale` 슬라이더 조절 → 로그 갱신 + 다음 파기부터 즉시 반영
- 청크 경계를 걸쳐 파기 → 마스크가 잘리지 않고 이어짐
- 불괴 픽셀(오버레이) 위에 파기 → 보호됨
