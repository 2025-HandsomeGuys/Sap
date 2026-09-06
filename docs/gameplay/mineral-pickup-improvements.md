# 광물 획득 UX 개선
@tags: mineral, pickup, UX, improvements, MineralLifetime, MineralItemController

> 작성일: 2026-03-29
> 관련 파일: `PlayerInteractor.cs`, `PickupableItem.cs`, `Assets/Prefabs/Particles/MineralPickupFX.prefab`

---

## 개선 항목

### 1. E 홀드 — 광물 연속 줍기

**목표:** E 키를 꾹 누르면 주변 광물을 0.25초 간격으로 자동으로 계속 줍는다.

**수정 파일:** `Assets/Scripts/UI/Interaction/PlayerInteractor.cs`

**추가된 필드:**
```csharp
private float _holdInteractTimer;
private const float HOLD_INTERACT_INTERVAL = 0.25f;
```

**로직 (Update 내):**
```csharp
if (Input.GetKey(KeyCode.E))
{
    if (Input.GetKeyDown(KeyCode.E))
        _holdInteractTimer = 0f;

    if (_closestTarget is PickupableItem)
    {
        _holdInteractTimer += Time.deltaTime;
        if (_holdInteractTimer >= HOLD_INTERACT_INTERVAL)
        {
            _holdInteractTimer = 0f;
            TryInteract();
        }
    }
}
else
{
    _holdInteractTimer = 0f;
}
```

**설계 포인트:**
- `PickupableItem`인 경우에만 연속 발동 — 엘리베이터 등 다른 `IInteractable`은 영향 없음
- `TryInteract()` 내부에서 `SetTarget(FindClosestInteractable())`를 호출하므로 광물 제거 후 즉시 다음 타겟으로 전환됨
- UI가 열리면 타이머 초기화

---

### 2. 광물 획득 이펙트 — 파티클 버스트 + 카메라 미세 진동

**목표:** 광물을 줍는 순간 파티클이 터지고 카메라가 미세하게 흔들린다.

**수정 파일:** `Assets/Scripts/UI/Items/PickupableItem.cs`

**추가된 필드:**
```csharp
[Header("획득 이펙트")]
[SerializeField] private GameObject _pickupParticlePrefab;
[SerializeField] private float _particleLifetime = 1f;
```

**추가된 메서드:**
```csharp
private void PlayPickupEffect()
{
    if (_pickupParticlePrefab != null)
    {
        GameObject fx = Instantiate(_pickupParticlePrefab, transform.position, Quaternion.identity);
        Destroy(fx, _particleLifetime);
    }
    CameraShakeManager.Instance?.Shake(0.06f, 0.015f);
}
```

**호출 위치:** `Interact()` → 인벤토리 추가 성공(`added == true`) 직후, `RemoveFromWorld()` 전

**카메라 진동 강도:** duration 0.06s, magnitude 0.015 (드릴보다 약한 수준)

---

### 3. MineralPickupFX 파티클 프리팹

**경로:** `Assets/Prefabs/Particles/MineralPickupFX.prefab`

**생성 방법:** Unity 메뉴 → `Tools > Sap-Sap > Create Mineral Pickup FX`
- 스크립트 위치: `Assets/Editor/MineralPickupFXCreator.cs`

**파티클 설정:**

| 항목 | 값 |
|------|-----|
| Duration | 0.3s |
| Loop | false |
| Burst | 8개 (단발) |
| 수명 | 0.25 ~ 0.45s (랜덤) |
| 속도 | 1.5 ~ 3.5 (랜덤) |
| 크기 | 0.07 ~ 0.14 (랜덤) |
| 색상 | 노란빛(1, 0.95, 0.45) → 흰빛 |
| 알파 | 1.0 → 0 (80% 시점부터 페이드) |
| 중력 | 0.6 (포물선 낙하) |
| 시뮬레이션 | World Space |
| 종료 동작 | Destroy (자동 소멸) |
| Sorting Order | 10 |

> **픽셀 아트 주의사항:** scale 애니메이션(C 옵션) 사용 시 확대 시 깨져 보일 수 있어 적용하지 않음.
> 파티클 크기는 `startSize` 랜덤 범위로만 조절.

---

## Unity 에디터 설정 (필수)

광물 프리팹마다 `PickupableItem` 컴포넌트의 **Pickup Particle Prefab** 슬롯에 `MineralPickupFX` 프리팹을 연결해야 파티클이 재생됨.

1. Project 창에서 광물 프리팹 선택 (다중 선택 가능)
2. Inspector → `PickupableItem` → **Pickup Particle Prefab** 슬롯에 `MineralPickupFX` 드래그
3. **Particle Lifetime**은 기본값 `1f` (파티클 자체 `stopAction = Destroy`로 자동 소멸하므로 별도 조정 불필요)

> `_pickupParticlePrefab`이 null이면 파티클 없이 카메라 진동만 발동됨.

---

## 관련 파일 경로

```
수정된 파일:
├── Assets/Scripts/UI/Interaction/PlayerInteractor.cs   (홀드 연속 줍기)
└── Assets/Scripts/UI/Items/PickupableItem.cs           (획득 이펙트)

신규 생성:
├── Assets/Prefabs/Particles/MineralPickupFX.prefab     (파티클 프리팹)
└── Assets/Editor/MineralPickupFXCreator.cs             (프리팹 생성 에디터 툴)
```
