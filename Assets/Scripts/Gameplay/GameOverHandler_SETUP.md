# GameOverHandler 설정 가이드

> **씬 세팅은 더 이상 필요 없습니다.**
> 연출·페이드 캔버스는 전부 코드로 생성되고(`GameOverSequenceUI`),
> `GameOverHandler`는 `DemoUnderground` 로드 시 플레이어(`PlayerStat`)에 자동으로 붙습니다.
>
> 전체 설계는 **`Assets/Docs/game-over-sequence.md`** 를 보세요.

---

## 그래도 손볼 게 있다면

### 다른 지하 씬을 추가했을 때

자동 부착 대상 씬 목록에 이름만 넣으면 됩니다.

```csharp
GameOverHandler.AutoAttachScenes = new[] { "DemoUnderground", "새_지하씬_이름" };
```

### 값을 조정하고 싶을 때

Player 오브젝트에 **Add Component → GameOverHandler** 로 직접 붙이면
자동 부착은 건너뛰고 인스펙터 값이 쓰입니다.

| 필드 | 기본값 | 설명 |
|------|--------|------|
| Player Stat | 비움 | 비워두면 Awake에서 자동 탐색 |
| Mineral Inventory | 비움 | 비워두면 `InventoryUI` → `FindFirstObjectByType` 순으로 자동 탐색 |
| Mineral Burst VFX | 비움 | 사망 순간 재생할 파티클. **없어도 됩니다** |
| Burst Duration | 1.0 | 가방을 비울 때까지 대기(초). HUD가 사라진 뒤 수치가 0이 되게 함 |
| Target Scene | `DemoUpground` | 사망 후 돌아갈 지상 씬. Build Settings 등록 필요 |

---

## 미네랄 버스트 파티클 (선택)

붙이면 사망 순간 광물이 터져 나가는 연출이 추가됩니다. 비워두면 그냥 생략됩니다.

1. Player 자식으로 **Effects → Particle System** 생성 (Name: `MineralBurstVFX`)
2. 설정:

| 항목 | 값 |
|------|----|
| Duration | 1.0 |
| Looping | 체크 해제 |
| Start Lifetime | 0.5 ~ 1.0 |
| Start Speed | 3 ~ 8 |
| Start Size | 0.1 ~ 0.3 |
| Start Color | 노란색~흰색 그라디언트 (광물 느낌) |
| Play On Awake | **체크 해제** |
| Stop Action | None |
| Emission > Rate over Time | 0 |
| Emission > Bursts | Count 1, Particles 20~30, Time 0 |
| Shape | Circle / Sphere, Radius 0.3 |

3. `GameOverHandler`의 **Mineral Burst VFX** 필드에 연결

---

## 사운드 (선택)

`SoundDataSO`에 아래 이름으로 클립을 등록하면 연출 시작 순간 재생됩니다. 없으면 무음입니다.

| SFX 이름 | 재생 시점 |
|----------|-----------|
| `game_over` | 사망 |
| `emergency_escape` | 긴급 탈출 |
