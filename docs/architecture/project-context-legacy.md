# Sap-UVCS 프로젝트 컨텍스트
@tags: architecture, overview, project-context, legacy, InfinityMapManager, layer, chunk-lifecycle, border-sync

> 이 파일은 AI 어시스턴트가 프로젝트를 빠르게 이해하기 위한 컨텍스트 문서입니다.
> 마지막 업데이트: 2026-03-15

## 프로젝트 개요

**장르:** 2D 샌드박스 생존 게임 (Terraria 스타일)
**엔진:** Unity 2D
**특징:** 무한 맵 생성, 픽셀 단위 파기, 광물/암석 시스템, 엘리베이터 이동, 특수 청크(함정/던전) 시스템

### 게임 비주얼 스타일 (중요 — 프리팹/에셋 제작 시 반드시 준수)

> **이 게임은 Terraria와 동일한 2D 횡스크롤 픽셀아트 스타일입니다.**
> 특수 청크 프리팹, UI, 이펙트 등 모든 비주얼 에셋을 만들 때 아래 기준을 따르십시오.

| 항목 | 기준 |
|------|------|
| 시점 | 2D 횡스크롤 (사이드뷰) |
| 아트 스타일 | 픽셀아트 (저해상도 스프라이트, Terraria 수준) |
| 카메라 | 2D 직교(Orthographic) |
| 지형 표현 | 픽셀 단위 텍스처 (1000×1000px 청크를 Texture2D로 렌더링) |
| 오브젝트 | 스프라이트 기반 (SpriteRenderer), 3D 메시 사용 안 함 |
| 조명 | 2D BFS 라이팅 (3D 라이트 사용 안 함) |
| 레이어 구성 | 지형(0) / 암석(-1, 지형 뒤) / 장식물(2, 지형 앞) |

**특수 청크 프리팹 제작 시 주의:**
- 모든 시각 요소는 픽셀아트 스프라이트로 제작
- 3D 오브젝트, 파티클 시스템의 3D 메시 사용 금지
- SortingLayer와 sortingOrder를 반드시 명시 (위 레이어 구성 기준)
- 스프라이트 Import 설정: `Filter Mode = Point (no filter)`, `Compression = None` (픽셀아트 선명도 유지)

---

## 프로젝트 구조

```
Assets/Scripts/
├─ Gameplay/Terrain/Tiles/          # 지형 핵심 시스템
│  ├─ Chunk/                        # 청크 컴포넌트 (분리된 아키텍처)
│  │  ├─ TerrainChunk.cs            # 파사드 (200줄, 구: 2000줄 모놀리식)
│  │  ├─ TerrainModifier.cs         # 파기 알고리즘 (순수 C# 클래스)
│  │  ├─ TerrainVisualizer.cs       # 텍스처/경계 렌더링
│  │  ├─ TerrainCollider.cs         # PolygonCollider2D 관리
│  │  ├─ TerrainLightingCalculator.cs  # 라이팅 BFS
│  │  ├─ ChunkJobScheduler.cs       # Unity Jobs 오케스트레이션
│  │  └─ ChunkData.cs               # 픽셀/색상 데이터 홀더
│  ├─ Core/                         # 인프라 서브시스템
│  │  ├─ GridCoordinateSystem.cs    # 월드 좌표 매핑 (1000px = 10 유닛)
│  │  ├─ ChunkPool.cs               # 청크 오브젝트 풀
│  │  ├─ ActiveChunkRegistry.cs     # O(1) 청크 조회 레지스트리
│  │  ├─ ChunkLoadingRunner.cs      # 비동기 배치 청크 로딩
│  │  ├─ WorldPersistenceSystem.cs  # 저장/로드 (바이너리 포맷)
│  │  ├─ ChunkCoords.cs             # 좌표 변환 유틸
│  │  ├─ ChunkInitializationData.cs # 청크 생성 블루프린트
│  │  └─ WorldInputController.cs    # 입력 라우팅 허브
│  ├─ Generation/Pipeline/          # 청크 생성 파이프라인
│  │  ├─ ChunkGenerationPipeline.cs # 다단계 실행 (Spawn→Decorate→Finalize)
│  │  ├─ ChunkSpawner.cs            # 청크 인스턴스화 팩토리
│  │  ├─ ChunkDataProvider.cs       # 생성 컨텍스트 수집
│  │  ├─ ChunkGenerationContext.cs  # 생성 단계 I/O 데이터 홀더
│  │  ├─ DecorationContext.cs       # 데코레이션 정보 전달
│  │  ├─ StandardChunkFactory.cs    # 일반 청크 팩토리
│  │  └─ SpecialChunkFactory.cs     # 특수 청크 팩토리
│  ├─ Decoration/                   # 데코레이션 시스템
│  │  ├─ TerrainDecorator.cs        # 암석/광물/엘리베이터 생성 파사드
│  │  ├─ RockDecorator.cs           # 청크별 암석 데코레이터
│  │  ├─ RockSpawner.cs             # 암석 오브젝트 풀링 및 스폰
│  │  ├─ RockLayoutCalculator.cs    # 암석 배치 레이아웃 계산 (시드 기반)
│  │  ├─ DiggableRock.cs            # 암석 컴포넌트 (HP, 크랙 비주얼, 파괴)
│  │  ├─ MineralDecorator.cs        # 광물 배치 데코레이터
│  │  └─ ElevatorDecorator.cs       # 엘리베이터 배치 데코레이터
│  ├─ Damage/                       # 피해/위험 지대 시스템
│  │  ├─ DamageZone.cs              # 지속 스태미나 감소 지대 (용암, 산성 등)
│  │  ├─ DamageZoneGroup.cs         # 복수 지대 그룹
│  │  └─ LavaSinkBehavior.cs        # 용암 싱크 트랩 전용 동작
│  ├─ Events/                       # 타일 이벤트 시스템
│  │  ├─ TileEventDispatcher.cs     # 싱글톤 이벤트 브로드캐스트 (SOLID)
│  │  ├─ ITileDestroyListener.cs    # 픽셀 파괴 반응 인터페이스
│  │  ├─ ExplosiveMineralReactor.cs # 광물 파괴 시 폭발 반응
│  │  └─ IcicleSpawner.cs           # 타일 파괴 시 고드름 생성
│  ├─ SpecialChunks/                # 특수 청크 시스템 (30+ 파일)
│  │  ├─ Core/
│  │  │  ├─ SpecialChunkFootprint.cs   # 멀티청크 영역 데이터
│  │  │  ├─ SpecialChunkSelector.cs    # 시드 기반 결정론적 선택
│  │  │  └─ SubChunkRegistry.cs        # 멀티청크 발자국 추적
│  │  ├─ Entities/                     # 트랩 엔티티
│  │  │  ├─ RollingRockEntity.cs
│  │  │  ├─ SnowmanEntity.cs
│  │  │  ├─ CableEntity.cs
│  │  │  ├─ TrashWallEntity.cs
│  │  │  ├─ CrystalBlockEntity.cs
│  │  │  └─ IcicleHazard.cs
│  │  ├─ Traps/                        # 트랩 메카닉
│  │  │  ├─ CollapseFloor.cs           # 바닥 붕괴 오케스트레이터
│  │  │  ├─ DelayedBlast.cs            # 메탄 가스광석 2초 지연 폭발
│  │  │  ├─ RollingRockTrap.cs         # 굴러오는 바위
│  │  │  ├─ StalactiteTrap.cs          # 낙하 종유석
│  │  │  └─ ScrapExplosion.cs          # 압축 쓰레기 벽 폭발
│  │  ├─ Effects/                      # 트랩 VFX/오디오
│  │  │  ├─ VFXCollapseEffect.cs
│  │  │  ├─ AudioCollapseEffect.cs
│  │  │  ├─ TrashWallVFX.cs
│  │  │  ├─ CrystalBlockVFX.cs
│  │  │  └─ DamageStagedVisuals.cs
│  │  ├─ Behaviours/                   # 동작 컴포넌트
│  │  │  ├─ PixelFloorCollapser.cs
│  │  │  ├─ CrystalBlockDropper.cs
│  │  │  ├─ TrashWallDropper.cs
│  │  │  └─ VibrationManager.cs
│  │  ├─ Interfaces/                   # 계약 인터페이스
│  │  │  ├─ IFloorCollapser.cs
│  │  │  ├─ ICollapseEffect.cs
│  │  │  ├─ IDamageStageable.cs
│  │  │  ├─ ILootDropper.cs
│  │  │  ├─ IVibrationReceiver.cs
│  │  │  └─ IChunkInitializer.cs
│  │  ├─ Zones/
│  │  │  └─ OxidizedZone.cs           # 산화 환경 효과
│  │  └─ Data/
│  │     └─ LootTable.cs
│  ├─ SpecialChunkManager.cs          # 특수 청크 파사드
│  ├─ InfinityMapManager.cs           # 청크 시스템 메인 (파사드)
│  ├─ InfinityMapManager.Data.cs      # 저장/로드
│  ├─ TerrainCarver.cs                # 픽셀 제거 (ClearHole)
│  ├─ TerrainBlender.cs               # 지층 경계 픽셀 혼합
│  ├─ MineralGenerator.cs             # 광물 생성
│  └─ Digger.cs                       # 파기 로직 (레거시 유지)
├─ Elevator/                          # 엘리베이터 시스템
│  ├─ ElevatorManager.cs              # 전역 관리 (싱글톤)
│  ├─ ElevatorController.cs           # 개별 엘리베이터
│  └─ ElevatorData.cs                 # 데이터 구조
├─ Player/Strategies/                 # 채굴 전략 패턴
│  ├─ IMiningStrategy.cs              # 전략 인터페이스
│  ├─ SapStrategy.cs                  # 삽 전략
│  ├─ PickaxeStrategy.cs              # 곡괭이 전략
│  ├─ DrillStrategy.cs                # 드릴 전략 (배터리+대시)
│  └─ EmptyStrategy.cs                # 빈 손 전략
├─ Zones/                             # 버프/환경 효과 지대
│  ├─ BuffZone.cs
│  ├─ IZoneEffect.cs
│  └─ ZoneEffectTrigger.cs
├─ UI/                                # UI 시스템
│  ├─ ElevatorUI.cs
│  ├─ StaminaBarUI.cs
│  ├─ DrillBatteryUI.cs               # 드릴 배터리 표시
│  └─ ...
├─ Mineral/                           # 광물 시스템
└─ World/                             # 월드 데이터
   ├─ TileDataManager.cs              # 타일 타입 관리
   ├─ TileDataModels.cs               # JSON 데이터 모델
   └─ Enums.cs                        # 타일/광물 enum (SpecialChunkType 포함)
```

---

## 핵심 시스템

### 1. 맵 생성 시스템 (InfinityMapManager)

**아키텍처 (파사드 패턴으로 리팩터링됨):**
```
InfinityMapManager (파사드)
├─ GridCoordinateSystem  - 좌표 매핑
├─ ChunkPool             - 오브젝트 풀
├─ ActiveChunkRegistry   - O(1) 청크 조회
├─ WorldPersistenceSystem - 저장/로드
├─ ChunkLoadingRunner    - 비동기 배치 로딩
├─ ChunkSpawner          - 청크 인스턴스화
└─ ChunkDataProvider     - 생성 컨텍스트 수집
```

**청크 생성 파이프라인:**
- Phase 1: Spawn (청크 오브젝트 생성)
- Phase 2: Decorate (암석/광물/엘리베이터/특수청크 배치)
- Phase 3: Finalize (경계 렌더링, 조명)

**주요 설정:**
- `enableMemoryCache`: 플레이 세션 동안 청크 유지 (true로 설정)
- `enableDiskSave`: 게임 종료 시 디스크 저장 (false = 테스트 모드)
- `worldSeed`: 고정 시드로 결정론적 생성
- `enableLayerBlending` (true): 지층 픽셀 혼합 활성화
- `blendingRatio` (0.4): 혼합 영역 비율

**GPU 최적화:**
- `texture.Apply()` 호출 간격 제한: 0.05초 (최대 20fps)
- 더티 청크 추적 + RectInt 컬링으로 불필요한 업데이트 제거
- LateUpdate 2라운드: 1차=초기 비주얼, 2차=경계 동기화

**저장 시스템:**
- F5: 수동 저장
- F6: `enableDiskSave` 토글
- 파일 위치: `Application.persistentDataPath/worldData.bin`
- 데이터 형식: `ChunkSaveData` (modifiedPixels, pixelInfo, hasChanges)

---

### 2. 4개 층 시스템

**층 구조 (Y 청크 좌표):**
| Layer | TileType | Y | 이름 | maxStaminaReduction |
|-------|----------|---|------|---|
| 0 | Dirt | 0 | 땅 | 0.1 |
| 1 | Ice | -20 | 얼음땅 | 0.5 |
| 2 | MagmaRock | -40 | 용암땅 | 1.0 |
| 3 | MeteoriteRock | -60 | 우주 | 2.0 |

**광물 depth 범위 (청크 Y 절댓값 기준):**
| 층 | depth 범위 | 주요 광물 |
|---|---|---|
| 땅 | 0-19 | ScrapMetal/GarbageBag/PETBottle/CondensedGas(0-9), Copper/Iron/Lead/Coal(5-14), Silver Rare(10-19) |
| 얼음땅 | 20-39 | Silver/SporeCrystal/Sulfur/Magnetite/Gold(20-29), Fossil/Mithril/Snowflake/Gold(30-39), Sapphire Rare(34-39) |
| 용암땅 | 40-59 | Ruby/Emerald/Topaz(40-49), Obsidian/LavaStone/Quartz(50-59), Diamond Rare(45-59) |
| 우주 | 60-79 | MeteoriteIron/Vibranium/StarFragment/Gravitonium(60-74), Uranium Rare(70-79) |

**설정 파일:** `tileData.json`
- 각 지층 타일 속성, 광물 스폰 규칙 정의
- `terrainDepth`: 80 (층당 20청크)
- `elevatorConfig`: 엘리베이터 생성 간격 (X축 5칸마다)

**지층 경계 (Pixel-Level Blending):**
- `TileDataManager` Inspector:
  - `layerNoiseScale` (0.08): 청크 단위 파동 빈도
  - `layerNoiseAmplitude` (2.5): 청크 단위 파동 높이
- 경계 청크에서 두 타일 타입을 픽셀 단위로 혼합
- Perlin Noise로 자연스러운 물결 패턴

---

### 3. 엘리베이터 시스템

**특징:**
- X % 5 == 0 좌표에 엘리베이터 자동 생성
- 각 층의 startDepth 위치에 배치
- 같은 X 좌표의 엘리베이터끼리 연결

**사용법:**
- 엘리베이터 근처에서 E 키 → UI 열림
- 모든 7개 층 버튼 표시 (로드 안 된 층은 "(미로드)" 표시)
- 버튼 클릭으로 층 이동
- ESC 또는 닫기 버튼으로 UI 닫기

**구현 세부사항:**
- `ElevatorManager`: 싱글톤, 층 정보 관리, 텔레포트 처리
- `ElevatorController`: 개별 엘리베이터, 플레이어 감지, 상호작용
- `ElevatorUI`: UI 패널, 동적 버튼 생성
- 청크 풀링과 호환 (청크 언로드 시 엘리베이터도 파괴)
- 저장된 청크 재로드 시 엘리베이터 재생성 (픽셀은 건드리지 않음)
- 신규: `ElevatorDecorator`를 통해 생성 파이프라인에 통합

---

### 4. 암석 채굴 시스템

#### 동작 흐름
1. **스폰**: 암석이 지형 픽셀 뒤에 묻혀 생성 (`sortingOrder = -1`, 콜라이더 비활성)
2. **노출**: 인접 지형이 파지면 `TerrainChunk.Dig()` → `DiggableRock.RevealInTerrain()` 호출
   - `TerrainCarver.ClearHole()`로 암석 모양대로 지형 픽셀 제거
   - `PolygonCollider2D` 활성화 → 곡괭이로 캘 수 있는 상태
3. **채굴**: 곡괭이(toolIndex=2)만 데미지 가능. HP 소진 시 파괴 + 광물 드롭

#### HP 단계별 크랙 비주얼
- HP 67%~100%: 정상 스프라이트 (`normal`)
- HP 34%~66%: 균열 1단계 (`crack1`)
- HP 0%~33%: 균열 2단계 (`crack2`)
- 스프라이트 교체 시 `PolygonCollider2D` 경로 저장/복원 (Unity 자동 갱신 방지)

#### 주요 컴포넌트
| 파일 | 역할 |
|------|------|
| `TileVisualSettings.cs` | `RockSpriteSet` 구조체: `normal`, `crack1`, `crack2` 스프라이트 세트 |
| `RockLayoutCalculator.cs` | 시드 기반 배치 위치 계산 |
| `RockSpawner.cs` | 오브젝트 풀링, 스폰, `sortingOrder=-1` 설정 |
| `DiggableRock.cs` | HP 관리, 크랙 비주얼, `RevealInTerrain()`, 파괴 시 광물 드롭 |
| `TerrainChunk.cs` | `SpawnedRocks` 목록 관리, Dig 시 인접 암석 노출 트리거 |

#### SortingOrder 정의
| 오브젝트 | sortingOrder |
|----------|-------------|
| 암석 (DiggableRock) | -1 (지형 뒤) |
| 지형 텍스처 | 0 |
| DirtPatch (미사용) | 2 (지형 앞) |

---

### 5. 특수 청크 시스템 (Special Chunks)

**개념:**
- 확률적으로 선택되는 절차적 던전/함정 방
- 멀티청크 구조 지원 (최대 4000px 너비, 복수 청크 슬롯 점유)
- 시드 기반 결정론적 배치 (`SpecialChunkSelector`)

**특수 청크 타입 (SpecialChunkType enum):**
| 타입 | 설명 |
|------|------|
| ScrapExplosion | 압축 쓰레기 벽 + 파편 파티클 |
| DelayedBlast | 메탄 가스광석 수확 후 2초 지연 폭발 |
| CollapseFloor | 플레이어 접촉 0.2초 후 바닥 붕괴 |
| GuideLine | 구리 케이블 + 파괴 시 스파크 이펙트 |
| Pitfall | 나무 함정문 붕괴 |
| DropSpike | 천장에서 낙하하는 종유석 (Ice 층) |

**멀티청크 시스템:**
- `SubChunkRegistry`: 앵커 청크가 서브청크 그리드 위치 추적
- `PrependSubChunkAnchors`: 로딩 큐에서 앵커 청크를 서브청크보다 먼저 로드
- `IsFootprintOutOfRange`: 발자국 기반 언로드 범위 체크

**아키텍처 패턴:**
- `IChunkInitializer`: 각 특수 청크 타입의 초기화 계약
- `IMultiChunkPart`: 멀티청크 파트 인터페이스
- `IDamageStageable`: HP 단계별 비주얼 변화 인터페이스

---

### 5-A. 청크 컴포넌트 선택 기준 (중요)

특수 청크 프리팹 루트에 붙이는 IChunk 구현체를 잘못 고르면 파기 불가 / Phase 처리 오류가 발생한다.
**프리팹을 만들기 전에 아래 표를 반드시 확인할 것.**

| 컴포넌트 | 파일 | 파기 가능 | 픽셀 데이터 | 언제 사용하나 |
|---|---|:---:|:---:|---|
| `TerrainChunk` | `Chunk/TerrainChunk.cs` | ✅ | ✅ ChunkData | 플레이어가 픽셀을 파낼 수 있는 모든 청크 (일반 지형 + 공동이 있는 특수 청크) |
| `LargeStaticTerrainChunk` | `Chunk/LargeStaticTerrainChunk.cs` | ❌ | ❌ 없음 | 스프라이트를 **그대로** 표시하는 완전 정적 구조물. 파기 불가. 크기 자유 (chunkGridWidth/Height) |

#### TerrainChunk — 특수 청크에서의 사용

특수 청크에 공동(빈 공간)이 필요하지만 벽은 파낼 수 있어야 할 때 `TerrainChunk` + `SpriteCavityInitializer`를 조합한다.

```
특수청크 루트 GameObject
├─ TerrainChunk             ← IChunk 구현, 픽셀 파기 가능
├─ SpriteCavityInitializer  ← IChunkInitializer: 스프라이트 투명 픽셀 → 공동 변환
│    sourceSprite: 공동 모양 이미지 (투명 = 빈 공간, 불투명 = 지형)
└─ 자식 DiggableRock 등
```

- `SpriteCavityInitializer`는 `Initialize()` 시 스프라이트 픽셀을 읽어 `ChunkData.BasePixels`에 직접 기록한다.
- 투명 픽셀(alpha < 10) → 공기(air), 불투명 픽셀 → 해당 색상 지형
- **스프라이트 Import 설정에서 `Read/Write Enabled` 체크 필수** (픽셀 읽기에 필요)
- Phase 2에서 `IChunkInitializer`가 감지되면 랜덤 데코레이션을 스킵한다 (`ChunkGenerationPipeline.cs:126`)

#### LargeStaticTerrainChunk — 사용 사례

- 배경 대형 구조물 (파괴 불가 벽, 장식 오브젝트)
- 스프라이트 이미지 그대로 보여주면 충분한 경우
- 크기가 1000px을 초과하는 멀티청크 구조물 (chunkGridWidth > 1)
- **공동 벽을 플레이어가 파낼 수 없어도 되는 경우에만 허용**

#### SpriteCavityInitializer — 사용 조건

| 항목 | 요건 |
|---|---|
| 루트 컴포넌트 | `TerrainChunk` 필수 (`[RequireComponent]`) |
| 스프라이트 크기 | 청크 픽셀 크기(1000×1000)에 맞춰야 함. 크기 다르면 경고 후 범위 내만 복사 |
| Import 설정 | `Read/Write Enabled = true`, `Filter Mode = Point`, `Compression = None` |
| 투명 = 공동 | alpha < 10인 픽셀이 빈 공간(공기)이 됨 |

#### 실수 패턴 (하지 말 것)

| 실수 | 결과 |
|---|---|
| 공동이 있는 특수 청크에 `LargeStaticTerrainChunk` 사용 | 벽 픽셀을 파낼 수 없어 공동 진입 불가 |
| `TerrainChunk` + `SpriteCavityInitializer` 없이 공동 표현 | Phase 1에서 일반 지형으로 픽셀이 채워져 공동 사라짐 |
| `SpriteCavityInitializer.sourceSprite` 미할당 | 에러 로그 후 공동 생성 실패 |
| 스프라이트 `Read/Write Enabled = false` | "Texture is not readable" 에러 후 공동 생성 실패 |

---

### 6. 타일 이벤트 시스템 (TileEventDispatcher)

**개념:**
- SOLID 원칙 기반 이벤트 브로드캐스트 (SRP, OCP, DIP)
- 픽셀 파괴 시 등록된 리스너에게 알림

**사용법:**
```csharp
// 리스너 등록
TileEventDispatcher.Instance.Register(listener);

// 이벤트 발생 (TerrainModifier에서 호출)
TileEventDispatcher.Instance.Dispatch(worldPos, tileType);
```

**기본 리스너:**
- `ExplosiveMineralReactor`: 폭발성 광물 파괴 시 폭발 트리거
- `IcicleSpawner`: 특정 타일 파괴 시 고드름 엔티티 생성

---

### 7. 채굴 전략 시스템 (Strategy Pattern)

**toolIndex 정의:**
| 인덱스 | 도구 | 전략 클래스 |
|--------|------|------------|
| 0 | 삽 | SapStrategy |
| 1 | 도끼 | (별도 전략) |
| 2 | 곡괭이 | PickaxeStrategy |
| 3 | 드릴 | DrillStrategy |

**DrillStrategy (신규):**
- 배터리 기반 메카닉 (`currentBattery` 사용 시 감소)
- LMB 홀드 시 마우스 방향으로 대시 이동
- UI: `DrillBatteryUI.cs`
- 설정: `maxBattery`, `dashSpeed`

**PlayerMining.cs:**
- 전략 컨텍스트 객체 (전략 교체 조정)
- `MouseDigInputHandler`를 통해 입력 수신

---

### 8. 데미지/위험 지대 시스템

**DamageZone.cs:**
- 지속적인 스태미나 감소 지대
- 설정: 데미지 간격, 데미지량
- 적용 예: 용암 지대, 산성 지대

**LavaSinkBehavior.cs:**
- 용암 싱크 트랩 전용 동작
- 플레이어 접촉 시 서서히 빠져듦

---

## 중요한 설정값

**InfinityMapManager:**
- viewDistance: 1 (플레이어 주변 1칸 청크만 로드)
- chunkWidth/Height: 1000 픽셀
- PPU: 10 (Pixels Per Unit)
- enableMemoryCache: true (항상 켜두기)
- enableDiskSave: false (테스트 시 끄기)
- TEXTURE_UPDATE_INTERVAL: 0.05초 (GPU 업로드 쓰로틀링)

**ElevatorManager:**
- elevatorSpawnInterval: 5
- layers: 4개 (땅/얼음땅/용암땅/우주, InitializeLayers)

**청크 크기:**
- 픽셀: 1000x1000
- 월드: 10 x 10 유닛 (PPU=10 기준)

---

## 알려진 이슈 & 해결됨

### 해결된 문제들

1. ✅ **암석 HP가 3.5에서 멈춤**
   - 원인: `SpriteRenderer.sprite` 교체 시 `PolygonCollider2D` 자동 재생성 → 이후 레이캐스트가 암석을 못 찾음
   - 해결: `UpdateCrackVisual()`에서 스프라이트 교체 전후로 콜라이더 경로 저장/복원

2. ✅ **암석 HP 로그가 아예 안 나옴**
   - 원인: `Debug.Log`가 `UpdateCrackVisual()` 호출 뒤에 있어 예외 발생 시 도달 불가
   - 해결: 로그를 `UpdateCrackVisual()` 호출 앞으로 이동

3. ✅ **DirtPatch가 삽으로 상호작용 안 됨**
   - 원인: `SapStrategy.FireMining()`에 `IDirtDiggable` 검사가 없었음
   - 해결: `digCenter` + `mousePos` 두 지점에서 `OverlapCircleAll` 후 `IDirtDiggable` 감지 추가

4. ✅ **DirtPatch가 지형 뒤에 렌더링됨**
   - 원인: DirtPatch `sortingOrder` 기본값 0
   - 해결: `SpawnDirtPatch()`에서 `sortingOrder = 2` 명시 설정

5. ✅ **엘리베이터 청크가 저장 안 됨**
   - 원인: `GenerateElevator`에서 `hasBeenModified = true` 누락
   - 해결: 픽셀 제거 후 플래그 설정 추가

6. ✅ **저장된 청크 로드 시 엘리베이터 공간 막힘**
   - 원인: 엘리베이터 생성이 `!isModified` 블록 안에 있음
   - 해결: 엘리베이터 생성 밖으로 이동, `skipPixelClear` 플래그 추가

7. ✅ **UI 버튼 클릭 안됨**
   - 원인: `Time.timeScale = 0` 사용
   - 해결: `IsUIOpen` static 플래그로 변경

8. ✅ **UI에 버튼 1개만 표시**
   - 원인: 로드된 엘리베이터만 표시
   - 해결: 모든 레이어 정보로 버튼 생성, "(미로드)" 표시 추가

9. ✅ **렌더링 병목 (texture.Apply() 과호출)**
   - 원인: 매 파기마다 1000×1000 텍스처 GPU 업로드
   - 해결: `TEXTURE_UPDATE_INTERVAL = 0.05s` 쓰로틀링 + 더티 청크 추적

---

## 주의사항

- **UI 작업 시** `Time.timeScale` 사용 금지 → `IsUIOpen` 플래그 사용
- **저장 시스템 수정 시** `enableMemoryCache` 항상 true 유지
- **엘리베이터 작업 시** 청크 풀링 고려 필수
- **암석 스프라이트 교체 시** `PolygonCollider2D` 경로 저장/복원 패턴 유지
- **암석 노출 로직 수정 시** `RevealInTerrain()` 중복 호출 방지 (콜라이더 enabled 체크)
- **특수 청크 작업 시** 멀티청크 발자국(SubChunkRegistry) 고려 필수
- **특수 청크 제작 시** 지형·오브젝트 배치는 반드시 에디터 프리팹으로 완성. 런타임 `ClearHole()`이나 `Instantiate()`로 지형/오브젝트를 동적 생성하지 않는다. `IChunkInitializer`는 이미 배치된 오브젝트의 파라미터 설정 및 시스템 등록(AddSpawnedRock 등)만 담당한다.
- **TerrainChunk 수정 시** 분리된 컴포넌트(_modifier, _visualizer, _colliderManager) 활용
- **새 타일 파괴 반응 추가 시** `ITileDestroyListener` 구현 후 `TileEventDispatcher`에 등록

---

## 코드 스타일 & 컨벤션

- Debug.Log 포맷: `[ClassName] 메시지`
- 한글 주석 사용
- 청크 좌표: (xChunk, yChunk) - 월드가 아닌 청크 단위
- 월드 좌표: (worldX, worldY) - 유닛 단위
- toolIndex: 0=삽, 1=도끼, 2=곡괭이, 3=드릴
- 설계 원칙: SOLID (특히 SRP, OCP, DIP) — 특수 청크, 이벤트, 파이프라인 시스템에서 강하게 적용

---

## 아키텍처 패턴 요약

| 패턴 | 적용 위치 |
|------|-----------|
| Composition | TerrainChunk → Modifier/Visualizer/Collider/Scheduler |
| Strategy | IMiningStrategy (Sap/Pickaxe/Drill/Empty) |
| Factory | IChunkFactory (Standard/Special) |
| Facade | InfinityMapManager, TerrainDecorator, SpecialChunkManager |
| Observer | TileEventDispatcher + ITileDestroyListener |
| Object Pool | ChunkPool, RockSpawner |
| Registry | ActiveChunkRegistry (O(1) 조회) |
| Pipeline | ChunkGenerationPipeline (Spawn→Decorate→Finalize) |

---

**마지막 작업자:** AI Assistant (Claude)
**다음 작업 제안:** 특수 청크 Prefab Unity Editor 설정 및 테스트, 드릴 배터리 시스템 밸런싱

## Task Master AI Instructions
**Import Task Master's development workflow commands and guidelines, treat as if import is in the main CLAUDE.md file.**
@./.taskmaster/CLAUDE.md
