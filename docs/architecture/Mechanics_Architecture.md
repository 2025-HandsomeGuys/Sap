# Sap-Sap-UVCS: 1~4층 기믹 아키텍처 및 파일 구조 설계 (SOLID 원칙 기반)
@tags: architecture, mechanics, zone-effect, stamina, IZoneEffect, layer, SOLID, IChunkDecorator

새로운 지하 층(1층~4층)의 다양한 기믹을 유연하고 확장 가능하게 구현하기 위한 클래스 구조 및 파일럿 설계입니다. 기존 코드(`DamageZone.cs`, `Digger.cs`, `TerrainModifier.cs`, `PlayerStat.cs`, `IChunkDecorator` 등)를 최대한 재활용하고 **단일 책임 원칙(SRP)**과 **개방-폐쇄 원칙(OCP)**을 준수합니다.

---

## 📁 1. Zone Effect System (영역 기반 버프/디버프)
`DamageZone.cs`가 현재 스태미나 틱 데미지에 하드코딩되어 있습니다. 이를 범용적인 인터페이스로 확장하여 1층(산화된 공동), 2층(잊혀진 가로등), 3층(용암)에 모두 대처합니다.

**[파일 구조]**
*   `Assets/Scripts/Gameplay/Zones/`
    *   `IZoneEffect.cs` (New): 영역 효과 적용/해제 인터페이스
    *   `DamageZone.cs` (Refactor): `IZoneEffect` 구현. 스태미나/HP 즉시 또는 틱 감소 처리.
    *   `BuffZone.cs` (New): `IZoneEffect` 구현. 진입 시 `BuffStatProvider`를 통해 버프/디버프 부여, 진출 시 제거. (가로등의 '추위 면역', 산화된 공동의 '스태미나 최대치 감소' 등)
    *   `ZoneEffectTrigger.cs` (New): `Collider2D` Trigger 이벤트(`OnTriggerEnter2D`, `OnTriggerExit2D`)를 받아 부착된 모든 `IZoneEffect` 컴포넌트를 실행하는 델리게이터 역할 (SRP).

**[사용 예시]**
산화된 공동(1층) 바닥: 빈 GameObject + `BoxCollider2D` + `ZoneEffectTrigger` + `DamageZone`
산화된 공동 벽 타기: 벽면 구역에 `ZoneEffectTrigger` + `BuffZone` (벽에 매달릴 때만 발동되도록 조건 추가)

---

## 📁 2. Tile Event System (타일 파괴 연쇄 반응)
`Digger.cs`가 타일을 파괴할 때 (`InfinityMapManager.ModifyTerrain` 호출) 지형 타일(Pixel)이 사라지는 시점을 캐치하여 이벤트를 발생시킵니다.

**[파일 구조]**
*   `Assets/Scripts/Tiles/Events/`
    *   `TileEventDispatcher.cs` (New): `TerrainModifier`의 Pixel 파괴 이벤트를 구독하여 해당 위치와 `TileType`을 브로드캐스팅합니다.
    *   `ITileDestroyListener.cs` (New): 타일 파괴 시 발동할 로직의 인터페이스.
    *   `SinkholeReactor.cs` (New): `ITileDestroyListener` 구현. 싱크홀 타일 파괴 시 주변 타일 검색(`Physics2D` 또는 배열 검색) 후 붕괴 코루틴 실행.
    *   `ExplosiveMineralReactor.cs` (New): 3층 폭발 광물 파괴 시 4초 대기 후 파편(`ProjectileHazard`) 스폰.
    *   `IcicleSpawner.cs` (New): 2층 얼음 수정 파괴 시 천장에서 고드름(`FallingHazard`) 스폰.

**[장점]**
`Digger.cs` 나 `TerrainModifier.cs` 내부에 싱크홀이나 고드름 관련 로직을 전혀 추가할 필요가 없습니다. (OCP 준수)

---

## 📁 3. Hazard Entity (피격 및 함정 오브젝트)
용암 파편(3층), 얼음 고드름(2층) 등 동적으로 생성되어 플레이어에게 피해를 주는 투사체/물리를 제어합니다.

**[파일 구조]**
*   `Assets/Scripts/Gameplay/Hazards/`
    *   `IHazard.cs` (New): 데미지 처리 인터페이스
    *   `ProjectileHazard.cs` (New): 시작 시 `Rigidbody2D.AddForce`로 곡사 비행하며, `OnCollisionEnter2D` 시 튕기거나(벽) `PlayerStat`을 깎고 폭발(플레이어). (폭발 광물 파편)
    *   `FallingHazard.cs` (New): 시작 시 중력 적용, 낙하 중 플레이어와 충돌 시 데미지 및 둔화 디버프(`BuffStatProvider`) 부여 후 파괴. (얼음 고드름)

---

## 📁 4. Modular Chunk Decorator (구조물 생성)
기존 `RockDecorator.cs` 나 `IChunkDecorator` 구조를 상속받아 방이나 큰 덩어리를 생성합니다.

**[파일 구조]**
*   `Assets/Scripts/Tiles/Decoration/Decorators/`
    *   `MineshaftDecorator.cs` (New): 1층 갱도. 사전에 셋팅된 프리팹(방 형태의 Tilemap/장애물 묶음) 배열에서 무작위 선택하여 청크 좌표에 덮어쓰기. 지정된 연결 슬롯 부위에 다음 방을 확률적으로 이어 붙임.
    *   `DinosaurBoneDecorator.cs` (New): 2층 거대 공룡 뼈. 거대한 화석 스프라이트와 매우 단단한 타일셋 덩어리를 생성.
    *   `ConstellationGateDecorator.cs` (New): 4층 우주 관문. 잠긴 문과 퍼즐 스위치를 포함한 방 생성.

---

## 📁 5. Custom Diggable Entities (특수 채굴 오브젝트)
일반 지형 지우기(`TerrainModifier.cs`)가 아니라 오브젝트 자체를 캐는 로직(눈사람, 케이블)입니다. 기존 `Digger.cs`의 `IDiggable` 탐지 로직을 그대로 활용합니다.

**[파일 구조]**
*   `Assets/Scripts/Gameplay/Entities/`
    *   `SnowmanEntity.cs` (New): `IDiggable` 구현. 채굴될 때 지형을 지우는 대신 내부 HP 변수를 차감. % 구간마다 대사 이벤트(`Action`) 핑, HP 0 달성 시 `설화` 아이템 스폰.
    *   `CableEntity.cs` (New): `IDiggable` 구현. 캘 때 무적 처리(또는 지형은 깎이되 잔해 역할)하며, 특수 청크 타겟 위치 방향으로 전기 파티클 이펙트를 날려 보냄.
    *   `ConstellationGate.cs` (New): `IDiggable` 구현. 정상적인 퍼즐 해금이 아닌 곡괭이로 파괴 시 내부 보상을 지우는 이벤트 발생.

**[OCP/SRP 효과]**
`Digger.cs`는 그저 광선을 쏘아 `IDiggable.Dig()` 만 호출하면 되므로 수정이 불필요합니다. 각 Entity가 자신의 특수 행동(전기 이펙트, 대사 출력 등)을 스스로 책임집니다.

---

## 🚀 구현 우선순위 제안 (Next Steps)
1.  **Core Interface 추가**: `IZoneEffect`, `ITileDestroyListener`, `IHazard` 인터페이스 정의 및 `DamageZone` 리팩토링 구현.
2.  **Event System 연동**: `TerrainModifier`에 액션 델리게이트를 추가하고 `TileEventDispatcher` 연결.
3.  **1층 기믹 실제 구현**: `SinkholeReactor` 및 `MineshaftDecorator` 우선 구현 및 테스트.
