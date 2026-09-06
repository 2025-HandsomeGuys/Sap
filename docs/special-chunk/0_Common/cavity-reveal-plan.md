# 특수 서브청크 공동(Cavity) 노출 — "돌처럼 파면 드러나기" 설계/구현 계획

@tags: special-chunk, cavity, reveal, diggable, SpriteCavityInitializer, DiggableRock, carve, plan

> 작성일: 2026-07-17
> 대상: 땅에 배치되는 단일 특수청크(TerrainChunk + SpriteCavityInitializer)의 공동을
> 처음엔 숨겼다가 주변을 파서 공동 경계가 air와 맞닿으면 열리게(carve) 만들려는 작업자

---

## 1. 목표

현재 특수 서브청크(예: 산화된 공동, 광산 공동 등)는 스폰 즉시 `SpriteCavityInitializer`가
스프라이트 픽셀을 주입하여 **공동이 처음부터 뻥 뚫려 보인다.**

원하는 동작:

1. 스폰 시엔 **주변 지형과 동일한 흙/돌로 꽉 채워** 평범한 땅처럼 보이게 한다(공동 숨김).
2. 플레이어가 파서 **공동(구멍) 영역의 경계가 air와 맞닿으면**, `DiggableRock`이
   드러나듯 그 순간 공동을 carve해서 열고 특수청크 콘텐츠(공동·구조물)를 노출한다.

> **범위 한정:** 현재 지형에 배치되는 특수청크는 **단일 청크(1×1)** 형태만 대상으로 한다.
> 멀티청크(LargeStatic 하이브리드)는 이 계획의 범위 밖.

---

## 2. 기존 메커니즘 대조

| | DiggableRock (돌) | 특수 서브청크 공동 (목표) |
|---|---|---|
| 실체 | 일반 지형으로 찬 청크 **안의 작은 오브젝트** | **청크 그 자체** (TerrainChunk) |
| 숨김 방식 | Renderer·Collider off, 마스크 영역은 청크 지형이 그대로 채움 | 청크 전체를 solid 지형으로 채워야 함(우리가 직접) |
| 노출 판정 | 마스크 픽셀 중 **같은 청크**의 지형이 air인 픽셀 수 (`CountExposedRockPixels`) | 공동(투명) 마스크 픽셀이 **air와 인접**한 수 |
| 노출 실행 | `RevealInTerrain()` → `TerrainCarver.ClearHole` + Renderer/Collider on | `RevealCavity()` → 공동 픽셀 air 처리 + 특수 비주얼 on |
| 트리거 | `TerrainChunk.Dig/Explode`의 인접 판정 + 재로드 시 `RevealExposedRocks` | 동일 패턴 재사용 |

핵심 차이: 돌은 "마스크 = solid로 드러날 영역", 공동은 "마스크 = air로 뚫릴 영역"으로 **반대**다.
돌은 ClearHole로 마스크만큼 파내고 자기 스프라이트를 보여주지만,
공동은 **평소엔 solid로 메워두고** 노출 시 마스크만큼 air로 파낸다.

---

## 3. 설계

### 3-1. 관련 파일

| 파일 | 역할 | 변경 |
|------|------|------|
| `Assets/Scripts/.../SpecialChunks/Behaviours/SpriteCavityInitializer.cs` | 스프라이트 픽셀 주입 | **분기 추가**: 지연 노출 모드 |
| `Assets/Scripts/.../Chunk/TerrainChunk.cs` | Dig/Explode·재로드 훅 | **훅 추가**: cavity reveal 트리거 |
| `Assets/Scripts/.../Generation/TerrainCarver.cs` | carve/clear | **재사용** (변경 없음 예상) |
| (신규) `.../SpecialChunks/Behaviours/CavityRevealController.cs` | 노출 상태·판정·carve | **신규** (선택 A) |

### 3-2. 초기화 — 묻힘 상태 만들기

`SpriteCavityInitializer`에 `[SerializeField] bool hiddenUntilExposed` 플래그를 추가한다.
false면 기존 동작(즉시 공동 오픈) 그대로 → **하위 호환 유지**.

`hiddenUntilExposed == true`일 때 `Initialize()` 동작:

1. 스프라이트를 읽어 **공동 마스크**를 산출·보관한다.
   - 투명 픽셀(a < 10) = 공동(나중에 air로 뚫릴 영역).
   - 불투명 픽셀 = 특수 지형(벽 등).
   - 마스크는 `Color32[]`(또는 `bool[]`)로 컴포넌트에 캐시.
2. 청크 `BasePixels`를 **주변 지층 지형 픽셀로 채운다**(묻힘 상태) — **[확정]**:
   - 소스: `TileDataManager.Instance.GetGroundPixels(chunk의 TileType)` — 지층별
     1000×1000 지형 픽셀 배열. 일반 청크가 채우는 것과 동일한 소스이므로
     **주변 지층과 픽셀 단위로 완전히 일치**한다.
   - 스프라이트가 청크와 1:1(1000×1000)이므로 인덱스가 그대로 매핑된다.
   - 적용 범위: **청크 전체**(투명·불투명 위치 모두)를 groundPixels로 덮는다.
     스프라이트를 아예 무시하고 평범한 지층으로 채워야 특수 지형(불투명 부분)도
     안 보이고 완전히 묻힌 상태가 된다.
3. 특수 콘텐츠 비주얼(자식 `SpriteRenderer`, 구조물)을 **비활성화**한다.
   `IndestructibleOverlayInit`은 노출 전까지 마스크 기록을 미루거나, 기록은 하되
   Renderer만 끈다(구조물이 벽 속에 미리 있어도 안 보이므로 무방).
4. `isTextureDirty = isDirty = true`로 비주얼·콜라이더 갱신 예약.

### 3-3. 노출 판정 — 공동이 air에 맞닿았나

`DiggableRock.CountExposedRockPixels()`의 대응 메서드:

```
int CountExposedCavityPixels():
    exposed = 0
    for 각 공동(투명) 마스크 픽셀 (px, py):
        # 청크 좌표 (특수청크는 스프라이트가 청크 전체이므로 pivot/offset 없음, 1:1)
        # 4방향(또는 8방향) 이웃 중 하나라도 air면 노출로 카운트
        if 이웃픽셀 중 하나라도 air:
            exposed++
    return exposed
```

- "이웃픽셀 air" 판정은 청크 경계를 넘어갈 수 있으므로 `TerrainChunk`의
  기존 이웃 청크 air 헬퍼(`GetPixelAlpha` + 경계 인접 판정, [TerrainChunk.cs:470-494])를 사용.
- 임계값 `minExposedPixels`(기본 예: 10~15)를 넘으면 노출.
- 돌과 달리 "공동 픽셀 자체가 air인지"가 아니라 **"공동 픽셀이 파인 air와 인접했는지"**
  로 판정한다. 묻힘 상태에선 공동 픽셀이 solid로 메워져 있기 때문.

### 3-4. 노출 실행 — RevealCavity()

```
void RevealCavity():
    if 이미 노출됨: return
    if CountExposedCavityPixels() < minExposedPixels: return

    # 1. 원래 스프라이트 주입을 통째로 다시 실행한다.
    #    묻힘 상태에선 청크 전체가 groundPixels(주변 지층)로 덮여 있으므로,
    #    불투명 위치는 스프라이트 고유색(특수 지형)으로 되돌리고
    #    투명(공동) 위치는 air로 뚫는다 = 기존 SpriteCavityInitializer 주입 로직 그대로.
    #    (ClearHole은 air로 뚫는 것만 하므로, 불투명 자리를 특수색으로 되돌리려면
    #     전체 재주입이 필요. 따라서 SpriteCavityInitializer의 주입 코드를
    #     공용 메서드로 빼서 초기화(묻힘)와 노출이 함께 쓰도록 한다.)

    # 2. 특수 비주얼/구조물 활성화 (자식 SpriteRenderer on,
    #    IndestructibleOverlay 마스크가 미기록이면 이때 기록)

    # 3. 노출 플래그 set, isTextureDirty/isDirty 마킹 → 비주얼·콜라이더 갱신
    isRevealed = true
```

### 3-5. 트리거 통합

돌과 동일 패턴으로 `TerrainChunk`에 훅을 단다.

- **자기 청크 Dig/Explode**: 묻힘 상태 특수청크는 공동이 청크 내부 solid로 감싸여 있어
  플레이어가 이 청크 픽셀을 파야 공동에 도달 → **이 청크의 `Dig()`가 발화**한다.
  `_spawnedRocks` 인접 판정 블록([TerrainChunk.cs:670-690]) 옆에
  `_cavityRevealer?.OnChunkDug(digResult)` 훅 한 줄 추가.
- **이웃 청크 경계 노출**: **생략 확정.** 대상 공동 스프라이트는 항상 solid 여백으로
  둘러싸여 있어(투명 영역이 가장자리에 안 닿음), 플레이어가 반드시 이 청크를 파야
  공동에 도달한다 → 이 청크 `Dig()`만으로 충분. (§6-2)
- **청크 재로드**: `RevealExposedRocks()`와 같은 자리(ChunkGenerationPipeline
  `ExecutePhase2_FinalizeAndDecorate`, [ChunkGenerationPipeline.cs:133,160])에서
  `RevealExposedCavity()`도 호출. 저장된 파진 상태가 복원된 뒤 재판정.

### 3-6. 저장/복원

- 특수청크는 언로드 시 `modifiedPixels`/`pixelInfo`를 저장하고 재로드 시
  `SpecialChunkFactory` → `RestoreSavedPixels`로 복원한다([SpecialChunkFactory.cs:37-38]).
- **노출 완료 후**엔 공동이 air로 저장되므로 재로드 시 그대로 복원된다.
- **노출 전(묻힘)** 상태 저장: `SpriteCavityInitializer`가 매 로드 시 solid로 다시 채우므로,
  파다 만 중간 상태(일부만 판 흙)는 `modifiedPixels`로 저장·복원되고,
  재로드 후 `RevealExposedCavity()` 재판정으로 노출 여부가 결정된다.
- **주의**: §CLAUDE.md 아키텍처 제약 #11 — 호출 순서
  `Initialize()` → `ApplyBorderDataOnly()` → `RestoreSavedPixels()` 유지.
  `hiddenUntilExposed`의 solid 채움은 `Initialize()` 단계에서 수행되어야
  이후 `RestoreSavedPixels()`가 파진 상태로 덮어쓸 수 있다.

---

## 4. 구현 선택지

### 선택 A — 신규 `CavityRevealController` 컴포넌트 (권장)
- `SpriteCavityInitializer`는 마스크 산출·solid 채움까지만 하고,
  노출 상태/판정/carve는 별도 컴포넌트가 담당(SRP).
- `TerrainChunk`는 `DiggableRock`처럼 이 컴포넌트를 리스트/참조로 알고 Dig 시 호출.
- 장점: 책임 분리, 테스트 용이(SpecialChunkFootprintTests 스타일 EditMode 테스트 추가 가능).

### 선택 B — `SpriteCavityInitializer` 내부에 통합
- 한 컴포넌트가 초기화+노출을 모두 담당. 파일 수 최소.
- 단점: SRP 위반, 초기화 컴포넌트가 런타임 상태를 갖게 됨.

→ **권장: 선택 A.**

---

## 5. 작업 순서 (구현 시)

1. `SpriteCavityInitializer`에 `hiddenUntilExposed` 플래그 + solid 채움 분기 + 공동 마스크 노출 API.
2. (선택 A) `CavityRevealController` 신규: `CountExposedCavityPixels`, `RevealCavity`,
   `OnChunkDug`, `RevealIfExposed`.
3. `TerrainChunk.Dig()`/`Explode()`에 cavity reveal 훅 추가 (rock 블록 옆).
4. `TerrainChunk`에 `RevealExposedCavity()` 추가 + `ChunkGenerationPipeline`에서 호출
   (`RevealExposedRocks` 옆).
5. 특수 콘텐츠 비주얼/구조물 on/off 처리 (`IndestructibleOverlayInit` 노출 지연 연동).
6. EditMode 테스트: 공동 마스크 노출 카운트, 임계값 게이팅, carve 후 air 확인.
7. 프리팹 설정: 대상 특수청크 프리팹의 `SpriteCavityInitializer.hiddenUntilExposed = true`.

---

## 6. 결정 필요 / 리스크

### 6-1. 묻힘 채움 색 소스 — **[확정: 주변 지층색, 청크 전체]**
묻힘 시 청크 전체를 `TileDataManager.GetGroundPixels(TileType)`로 덮는다(§3-2).
노출 시엔 스프라이트를 통째로 재주입(불투명→특수색, 투명→air)한다(§3-4).
초기화(묻힘)와 노출이 스프라이트 주입 로직을 공유하도록 공용 메서드로 추출.

### 6-2. 경계 공동 노출 트리거 — **[확정: 생략]**
대상 공동 스프라이트는 항상 solid 여백으로 둘러싸여 있어 투명 영역이 가장자리에 닿지 않는다.
따라서 플레이어는 반드시 이 청크를 파야 공동에 도달 → 이 청크 `Dig()`만으로 충분.
이웃 청크 재판정 훅은 **구현하지 않는다.**

### 6-3. 특수청크 스프라이트=청크 크기 가정 — **[확정: 1:1 유지]**
스프라이트는 1000×1000px로 청크와 1:1. 좌표 수식은 offset/pivot 없이 직접 매핑.
크기 불일치 프리팹은 경고 처리(기존 `SpriteCavityInitializer`와 동일).

---

## 7. 구현 완료 (2026-07-17)

계획의 **선택 A**를 채택하되, `SpriteCavityInitializer`는 **무수정**으로 두고
신규 컴포넌트 `CavityRevealController`가 스냅샷 방식으로 모든 것을 처리하도록 구현했다.
프리팹에 이 컴포넌트를 추가하면 "묻혔다가 파면 드러나는" 모드가 켜진다(플래그 불필요).

### 변경 파일
- **신규** `Assets/Scripts/.../SpecialChunks/Behaviours/CavityRevealController.cs`
  - `IChunkInitializer` (InitializationOrder = **100**, 다른 초기화 이후 실행).
  - `Initialize()`: 이전 초기화가 주입한 **노출 상태를 스냅샷**(BasePixels/PixelInfo/
    IndestructibleMask) → 공동 마스크·경계 산출 → 청크 전체를 `GetGroundPixels`로 **묻기** →
    자식 렌더러/콜라이더 숨김 → `TerrainChunk.RegisterCavityReveal(this)`.
  - `RevealIfExposed()`: 공동 경계 픽셀이 air(이웃 청크 포함, `IsTransparent`)에 맞닿은 수가
    `minExposedPixels` 이상이면 `Reveal()`.
  - `Reveal()`: **공동 픽셀만 air로 carve**(벽=주변 지형은 절대 건드리지 않음) + 자식 비주얼 복원 +
    전체 비주얼/콜라이더 갱신. **재로드된 노출 상태(alreadyOpen)면 carve 생략**.

> **[중요] 벽을 복원하지 않는 이유**: 초기 설계는 노출 시 청크 전체를 스냅샷 복원(벽=특수색,
> 공동=air)했으나, 플레이어가 이미 파둔 벽(터널)이 노출 순간 다시 solid로 채워지며
> 플레이어를 밀어내(공동 안으로 빠지는) 충돌 버그가 발생했다. 따라서 **공동만 carve**하고
> 벽은 묻힘 때 채운 주변 지형 그대로 둔다(= 돌 구멍이 열리듯 가운데만 뚫림).
- `Chunk/TerrainChunk.cs`: `_cavityReveal` 필드 + `RegisterCavityReveal()` +
  `RevealExposedCavity()` 추가. `Dig()`/`Explode()`의 rock 노출 블록 옆에
  `_cavityReveal?.RevealIfExposed()` 훅 추가.
- `Generation/Pipeline/ChunkGenerationPipeline.cs`: `RevealExposedRocks()` 호출 2곳 옆에
  `RevealExposedCavity()` 추가(재로드 노출 복원).

### 재로드 정합성 (스냅샷 방식)
`ChunkDataProvider` 1-B 경로에서 재로드 시 `SpawnSpecialChunkIfPossible`가 초기화를 다시 실행
→ 컨트롤러가 새로 스냅샷·묻기 → `SpecialChunkFactory.RestoreSavedPixels`가 저장된 상태로 덮어씀
→ `RevealExposedCavity()`가 재판정. 저장이 노출 상태면 `alreadyOpen`으로 픽셀 보존.

### 프리팹 설정 (작업자용)
대상 단일 특수청크 프리팹(TerrainChunk + SpriteCavityInitializer 보유)에:
1. `CavityRevealController` 컴포넌트를 **루트(TerrainChunk와 같은 오브젝트)** 에 추가.
2. `minExposedPixels` 조정(기본 12) — 값이 클수록 더 많이 파야 열림.
3. 이 컴포넌트가 없으면 기존처럼 스폰 즉시 공동이 열린 상태로 동작(하위 호환).

> 전제: 스프라이트 1000×1000, 공동(투명)은 solid 여백으로 둘러싸여 가장자리 미접촉.

---

## 8. 참고 코드 위치

- `DiggableRock.RevealInTerrain / CountExposedRockPixels` — 노출 로직 원형
  `Assets/Scripts/.../Decoration/DiggableRock.cs:289-356`
- `TerrainChunk.Dig` 내 rock 인접 판정 — 트리거 삽입 위치
  `Assets/Scripts/.../Chunk/TerrainChunk.cs:670-690` (Explode: 782-799)
- `TerrainChunk.RevealExposedRocks` — 재로드 노출 원형
  `Assets/Scripts/.../Chunk/TerrainChunk.cs:596-603`
- `ChunkGenerationPipeline` — 재로드 노출 호출 지점
  `Assets/Scripts/.../Generation/Pipeline/ChunkGenerationPipeline.cs:133,160`
- `TerrainCarver.ClearHole` — carve 재사용
  `Assets/Scripts/.../Generation/TerrainCarver.cs:111-178`
- 이웃 청크 air 판정 — `TerrainChunk.cs:470-494`
- 저장/복원 경로 — `SpecialChunkFactory.cs:30-39`, `TerrainChunk.RestoreSavedPixels:1160-1170`
- 관련 문서: `docs/gameplay/rock-reveal-fix.md`,
  `docs/special-chunk/0_Common/large-static-hybrid-diggable.md`
