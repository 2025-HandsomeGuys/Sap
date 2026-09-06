# 곡괭이 (PickaxeStrategy) 수정 필요 사항
@tags: pickaxe, PickaxeStrategy, todo, dig, stamina

> 작성일: 2026-03-13
> 파일: `Assets/Scripts/UI/Player/Strategies/PickaxeStrategy.cs`

---

## 완료

- [x] `isShovel` 중복 조건 제거 — `CanDigTerrain`이 이미 false이므로 불필요한 `!isShovel` 체크 삭제

---

## 남은 문제

### 1. radius를 흙 경도로 계산 중

**위치:** `PerformDig()` 113번째 줄 근처

```csharp
DigParameters p = GetDigParameters(1.0f, TileType.Dirt);  // ← 잘못된 TileType
```

`GetDigParameters()` 내부에서 `hardnessFactor = MiningPower / tileData.hardness`로 radius를 계산하는데,
곡괭이는 바위만 쓰는 도구인데 **흙(Dirt) 경도** 기준으로 반경을 결정하고 있음.

**수정 방향:**
- 바위 경도 기준으로 호출: `GetDigParameters(1.0f, TileType.HardStone)`
- 또는 플레이어가 현재 있는 깊이의 타일 타입을 동적으로 넘기기
  (Digger처럼 `yChunk` 기반으로 `TileDataManager.GetTileTypeAtDepth()` 활용)

---

### 2. `GetDigParameters()`의 switch가 PickaxeStrategy 전용이 아님

**위치:** `GetDigParameters()` 194번째 줄 근처

```csharp
switch (currentToolIndex)
{
    case 0: canDigTerrain = true;  canDigRock = false; ...  // 빈 손
    case 1: canDigTerrain = true;  canDigRock = false; ...  // 삽/도끼
    case 2: canDigTerrain = false; canDigRock = true;  ...  // 곡괭이 ← 여기만 씀
    default: ...
}
```

`PickaxeStrategy`는 항상 toolIndex=2로만 실행되므로 switch 자체가 불필요한 분기.
case 2 로직만 남기고 단순화 가능.

**수정 방향:**
```csharp
// switch 제거, 곡괭이 고정값 사용
bool canDigTerrain = false;
bool canDigRock = true;
float toolEfficiency = 1.0f;
```

---

### 3. 스태미나 비용이 항상 HardStone 기준으로 고정

**위치:** `PerformDig()` 135번째 줄 근처

```csharp
var data = TileDataManager.Instance.GetData(TileType.HardStone);
```

깊이에 따라 더 단단한 층(CoolStone, Ice, HotStone 등)에서도 동일한 스태미나가 소모됨.

**수정 방향:**
- 플레이어 현재 깊이의 타일 타입으로 스태미나 비용 결정
- 또는 `IDiggable`(DiggableRock)에서 자체 스태미나 비용을 반환하도록 인터페이스 확장

---

## 참고: 현재 toolIndex 정의

| 인덱스 | 도구 |
|--------|------|
| 0 | 삽 (SapStrategy) |
| 1 | 도끼 |
| 2 | 곡괭이 (PickaxeStrategy) |
| 3 | 드릴 (DrillStrategy) |
