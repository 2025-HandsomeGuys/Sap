# 삼지창 (Trident) 유물 설계 — 2026-07-23

RelicID `Trident = 4028` · 패시브 · 강화 Lv1~3 (4027은 병렬 작업 OneWayPortal이 선점)

## 컨셉

장착 중 **삽 지형 파기**가 원형 1방 대신 **창날 3줄기 자동 3연타**로 바뀐다.
차징 1번 → 발사하면 좌(−20°) → 중(0°) → 우(+20°) 순서로 0.12초 간격 3연속 찌르기.
각 줄기는 가늘고 긴 캡슐 슬롯(폭 0.30~0.42 ≪ 기본 삽 반경 1.0) — "폭은 좁지만 깊게 찌른다".

## 신규 훅 — 지형 파기 오버라이드 (프레임워크 확장)

삽 지형 파기의 유일한 실행 지점은 `Digger.DigAt()`의 `mapManager.ModifyTerrain(...)` 호출이다.
여기에 오버라이드 훅 1개를 신설한다:

```
RelicBehaviour.TryOverrideTerrainDig(Vector2 digPos, Vector2 dir, float radius, int toolIndex) → bool
RelicManager.TryOverrideTerrainDig(...)   — 슬롯 순회, 첫 true에서 중단(첫 소유자 승리)
PlayerMining.TryOverrideTerrainDig(...)   — Digger→RelicManager relay (GetCurrentDigParameters와 동일 패턴)
```

`Digger.DigAt()`의 불괴(IIndestructibleHit) 체크 통과 직후, `ModifyTerrain` 직전에 호출:

- true 반환 → 기본 원형 파기·카메라쉐이크·파티클을 **건너뜀** (연출은 유물이 타격마다 직접 수행)
- 삽 MaxStamina 감소(`AddDiggingReduction`)는 **Digger에 남김** — 스윙 1회당 1회 부과 유지
- 스태미나 소모(PayCost)·차징 게이트·불괴 체크는 기존 흐름이 훅 호출 전에 이미 완료 → 유물은 공짜로 이용

호출 순서 보장: `PayCost` → 불괴 체크 → **훅** → (미오버라이드 시) 기본 파기.

## TridentRelic 동작

`Assets/Scripts/Gameplay/Relics/Behaviours/TridentRelic.cs` 신규.

- `TryOverrideTerrainDig`: `toolIndex != 1`(삽 아님)이면 false. 삽이면 조준 방향 `dir`을 **스윙 시점에 고정**하고 `ctx.runner` 코루틴 시작, true 반환.
- 코루틴(3연타): 타격 i = 0,1,2 (좌→중→우), 간격 `stabInterval`(0.12s)
  1. 방향 `dir_i` = dir을 {−spread, 0, +spread}° 회전
  2. 원점 = **현재 플레이어 위치** + dir_i × 0.3 (몸 근처에서 시작)
  3. 캡슐 스탬프: 원점부터 길이 L까지 반경 w 원을 `step = w × 0.7` 간격으로 `ctx.CarveTerrain` (PlasmaCutter CarveCapsule 방식) — IndestructibleMask 자동 존중
  4. Mine 애니메이션 재트리거(`ctx.mining.playerAnimator`) + `CameraShakeManager.ShakeOnDig(1)` + `TerrainParticleManager.SpawnImpact`(줄기 끝점)
- 스케일: `w = prongRadiusPerLevel[lv] × radius`, `L = prongLengthPerLevel[lv] × radius`
  (`radius` = Digger가 넘겨준 effectiveRadius — 차징비율·MiningRange·ToolRange·타 유물 배율(도박꾼 등)이 이미 반영된 값. 풀차징일수록 길고 굵게, 타 유물과 자동 합성)
- `OnUnequip`: 진행 중 코루틴 중단.

### 파라미터 (SerializeField)

| 필드 | 값 |
|---|---|
| `spreadAngleDeg` | 20 |
| `stabInterval` | 0.12 |
| `prongRadiusPerLevel` | { 0.30, 0.36, 0.42 } |
| `prongLengthPerLevel` | { 2.0, 2.4, 2.8 } |
| `stampStepRatio` | 0.7 |
| `originOffset` | 0.3 |

## 범위 밖 (의도적 미적용)

- **돌(DiggableRock)**: 삽은 원래 돌 못 팜 — SapStrategy의 OverlapCircle 돌 타격 경로는 그대로 둠.
- **곡괭이·드릴**: toolIndex 체크로 제외. 곡괭이 지형 파기는 PickaxeStrategy 내부 경로라 훅 자체가 안 탐.
- **ToolSwap 동시 장착**: 스왑 시 삽은 CanDigTerrain=false → Digger가 훅 전에 조기 리턴 → 삼지창 무효. 알려진 한계로 수용(추후 필요 시 확장).
- **mp3 리듬 모드**: Digger 경로 동일(Down 트리거) → 자동 호환. 3연타 간격 0.36s는 비트 간격보다 짧아 충돌 없음.

## 등록·테스트

- `RelicID.cs`: `Trident = 4028` 추가
- `RelicSliceAssetGenerator.cs`: `CreateRelic(RelicID.Trident, "삼지창", RelicType.Passive, new TridentRelic())` + db 리스트 → 사용자가 `Tools > Relic > Generate Slice Assets` 재실행
- `RelicDebugGranter.cs`: `KeyCode.T` 바인딩 (F/키패드 대부분 선점, T=Trident 연상)
- Unity 컴파일·인게임 검증은 사용자가 수행

## 수정 파일 요약

| 파일 | 변경 |
|---|---|
| `Relics/Behaviours/TridentRelic.cs` | 신규 |
| `Relics/Core/RelicBehaviour.cs` | `TryOverrideTerrainDig` 가상 훅 추가 |
| `Relics/Core/RelicManager.cs` | 슬롯 집계 `TryOverrideTerrainDig` 추가 |
| `UI/Player/PlayerMining.cs` | relay 메서드 추가 |
| `Gameplay/Terrain/Tiles/Digger.cs` | `DigAt`에 훅 호출 1지점 |
| `Editor/RelicSliceAssetGenerator.cs` | 생성기 등록 (공유 파일 — 기존 줄 보존) |
| `Relics/Debug/RelicDebugGranter.cs` | KeyCode.T 바인딩 (공유 파일 — 기존 줄 보존) |
| `Relics/Data/RelicID.cs` | 4028 추가 (공유 파일 — 기존 줄 보존) |
