# Stovel 구현된 시스템 목록 (2026-03-21 기준)

## 지형/세계 생성
- 무한 스크롤 맵 (InfinityMapManager, ChunkPool, ActiveChunkRegistry)
- 절차적 지형 생성 파이프라인 (TerrainGenerator, TerrainBlender, TerrainCarver)
- 청크 로딩/언로딩 스케줄러
- 미네랄/암석 배치 (MineralDecorator, RockDecorator, RockSpawner)
- 월드 상태 저장/복구 (WorldPersistenceSystem)

## 특수 청크 (Special Chunks)
- 함정: 붕괴 바닥 (CollapseFloor), 구르는 바위 (RollingRockTrap), 고드름 (StalactiteTrap), 지연 폭발 (DelayedBlast), 파편 폭발 (ScrapExplosion)
- 특수 엔티티: 수정 블록 (CrystalBlockEntity), 쓰레기 벽 (TrashWallEntity), 고드름 위험 (IcicleHazard), 눈사람 (SnowmanEntity)
- 위험 구역: DamageZone, 용암 수렁 (LavaSinkBehavior), 산화 구역 (OxidizedZone)

## 채굴 시스템
- 채굴 핵심 (Digger, PlayerMining)
- 채굴 전략 패턴: 드릴 / 곡괭이 / 삽 (DrillStrategy, PickaxeStrategy, SapStrategy)
- 채굴 범위 미리보기 (DigRangePreview)
- 스태미나 채굴 비용 계산 (StaminaDigCostCalculator)
- 드릴 배터리 UI (DrillBatteryUI)

## 플레이어
- 이동/컨트롤 (PlayerController, PlayerInputHandler)
- 스탯 시스템: StatType, StatModifier, StatBaseValueTable, PlayerStat
- 스탯 프로바이더 (버프/드릴/장비/인벤토리/업그레이드 각각 별도 Provider)
- 스태미나 (StaminaManager, StaminaBarUI)
- 도구 전환 (ToolController, WeaponSwapUI)
- Foot IK (SimpleFootIK)
- 구역 감지 (PlayerZoneChecker)

## 인벤토리
- 미네랄 / 아이템 / 도구 / 장비 인벤토리 (각각 별도 클래스)
- 드래그 앤 드롭 (InventorySlotDragHandler, InventorySlotClickHandler)
- 손에 든 아이템 (HeldItemManager)

## 경제 시스템
- 상점 (ShopManager, ShopUI, ShopDropZone)
- 창고 자동 입고 (WarehouseAutoDepositor)
- 정착지 (SettlementManager, SettlementUI)
- 미네랄 가격 DB (MineralPriceDatabase)

## 업그레이드
- 업그레이드 트리 (UpgradeManager, UpgradeTreeSO, UpgradeNodeSO)
- 업그레이드 효과 (UpgradeEffectSO)
- UI: 티어별 슬롯, 라인 렌더러 연결선

## 퀘스트/NPC
- 퀘스트 (QuestManager, QuestSO, QuestProgressData)
- NPC 대화 (NpcInteraction, DialogueData, QuestDialogueUI)
- 서브퀘스트 보드 (SubQuestBoardController)
- 엘리베이터 (ElevatorController, ElevatorManager)

## 렌더링
- 조명: 플래시라이트 FOV, 글로벌 조명 (GlobalLightingManager)
- 시야 제한 (FieldOfView)
- 픽셀라이즈 렌더러 피처 (PixelateLightRendererFeature)
- 카메라 추적 + 진동 (CameraFollow, CameraShakeManager)
- 패럴랙스 배경 (BackgroundManager, ParallaxLayer)
- 미네랄 픽업 글로우 (MineralPickupGlow)

## 씬/구역 전환
- 포털 (PortalController)
- 씬 전환 트리거 (SceneTransitionTrigger, MapTransition)
- 탐험 종료 (ExploreExitController)
- 인트로 (IntroChanger)

## 버프/존
- 버프 존 (BuffZone, ZoneEffectTrigger)

## 인프라
- 세이브/로드 (SaveManager)
- 사운드 (SoundManager)
- 다국어 지원 (LanguageManager, StringTableSO, LocalizedTextUI)
- 그래픽 설정 (GraphicSettingsManager)
- 툴팁 (TooltipManager + 다수 Provider)
- 알림 (NotificationUI)
- 확인/수량 입력 프롬프트 (ConfirmationPrompt, QuantityPrompt)
