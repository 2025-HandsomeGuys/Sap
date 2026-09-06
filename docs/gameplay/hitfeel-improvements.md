# 타격감(Hit Feel) 개선 계획
@tags: hitfeel, feedback, improvements, plan, particle, VFX

> 작성일: 2026-03-20
> 관련 파일: `TerrainParticleManager.cs`, `Digger.cs`, `TerrainChunk.cs`

---

## 현황 분석

### 현재 파티클 동작
- `TerrainParticleManager.SpawnDebris()`에서 방향 계산:
  `dir = worldPos - digCenter` → **픽셀에서 바깥쪽으로만** 100% 방출
- 캐릭터 쪽으로 튀어오는 역방향 파티클 없음

### 누락된 타격감 장치
- 카메라 쉐이크 없음 (CameraShake 관련 컴포넌트 전무)
- 파기 중심 임팩트 파티클 없음
- 화면 충격 플래시 없음

---

## 개선 항목

### 1. 파티클 역방향 튐 (Back-scatter)

**목표:** 파티클의 약 30%를 플레이어(캐릭터) 쪽으로 역방향 방출

**수정 파일:**
- `TerrainParticleManager.cs` — `SpawnDebris()`에 `playerPos` 파라미터 추가
- `TerrainChunk.cs` — `SpawnDebrisParticle()` 호출 시 playerPos 전달
- `Digger.cs` — digCenter 경로로 playerPos 전달

**로직:**
```csharp
// SpawnDebris(Vector2 worldPos, Color32 color, Vector2 digCenter, Vector2 playerPos)
float backRatio = 0.3f;  // 전체 burstCount 중 30%
Vector2 outDir  = (worldPos - digCenter).normalized;   // 기존 방향 (바깥)
Vector2 backDir = (playerPos - worldPos).normalized;   // 역방향 (캐릭터 쪽)

for (int i = 0; i < burstCount; i++)
{
    bool isBack = (i < burstCount * backRatio);
    Vector2 baseDir = isBack ? backDir : outDir;
    // 역방향은 위쪽 바이어스 강하게 (+1.0~2.0), 속도 약간 느리게
}
```

**난이도:** 낮음 | **체감 효과:** 즉각적

---

### 2. 카메라 쉐이크 (Camera Shake)

**목표:** 파기 성공 시 도구별 강도의 카메라 진동 추가

**새 파일:** `Assets/Scripts/Camera/CameraShakeManager.cs`

```csharp
// 싱글톤, 카메라 GameObject에 부착
public class CameraShakeManager : MonoBehaviour
{
    public static CameraShakeManager Instance;

    public void ShakeOnDig(int toolIndex) { ... }
    public void Shake(float duration, float magnitude) { ... }
    // 내부: 코루틴으로 position offset → Lerp 감쇠 복원
}
```

**도구별 강도:**
| 도구 | toolIndex | 지속 시간 | 강도 |
|------|-----------|----------|------|
| 삽 | 1 | 0.08s | 0.04 |
| 곡괭이 | 2 | 0.12s | 0.07 |
| 드릴 | 3 | 0.05s (연속) | 0.02 |

**호출 위치:** `Digger.TryDig()` 또는 `TerrainChunk.Dig()` 성공 직후

**난이도:** 낮음 | **체감 효과:** 극적

---

### 3. 임팩트 파티클 (Impact Burst)

**목표:** 파기 중심(`digPosition`)에서 순간적인 스파크 파티클 대량 방출

**수정 파일:** `TerrainParticleManager.cs` — 새 메서드 추가

```csharp
// 파기 중심에서 전방향 스파크 burst
public void SpawnImpact(Vector2 digCenter, Color32 avgColor, int toolIndex)
{
    int count = toolIndex switch
    {
        1 => 8,   // 삽
        2 => 15,  // 곡괭이
        3 => 5,   // 드릴 (연속이라 작게)
        _ => 6
    };
    // 전방향 + 역방향 혼합 방출
    // 크기: 기존 debris보다 크게 (0.05~0.12)
    // 속도: 빠르게 (5~14 m/s), 수명 짧게 (0.15~0.4s)
}
```

**호출 위치:** `Digger.DigAt()` → `mapManager.ModifyTerrain()` 직전

**난이도:** 낮음 | **체감 효과:** 시각적 화려함

---

### 4. 화면 충격 플래시 (Hit Flash UI)

**목표:** 파기 시 화면 가장자리가 순간 밝아지는 비네팅 플래시

> ⚠️ **드릴 제외:** 드릴은 0.2초마다 자동 파기 → 초당 5회 플래시로 화면이 사실상 계속 밝은 상태가 됨.
> 시각적 피로 + 불쾌감이 타격감보다 크므로 **삽·곡괭이 전용**으로 제한.
> 드릴의 피드백은 카메라 쉐이크(약하게)로만 대체.

**적용 도구:**
| 도구 | 플래시 여부 | peakAlpha | 비고 |
|------|------------|-----------|------|
| 삽 | ✅ | 0.15 | 풀차지일수록 강하게 (chargeRatio 비례) |
| 곡괭이 | ✅ | 0.10 | 콤보 3단계에서 살짝 강하게 |
| 드릴 | ❌ | — | 연속 파기 → 카메라 쉐이크로 대체 |

**새 파일:** `Assets/Scripts/UI/HitFlashUI.cs`

```csharp
// Canvas (ScreenSpace-Overlay) + FullScreen Image (알파=0) 에 부착
public class HitFlashUI : MonoBehaviour
{
    public static HitFlashUI Instance;
    [SerializeField] private CanvasGroup _canvasGroup;

    public void Flash(float peakAlpha = 0.15f, float duration = 0.08f)
    {
        StartCoroutine(DoFlash(peakAlpha, duration));
    }

    IEnumerator DoFlash(float peak, float duration)
    {
        // 0 → peak → 0 페이드
        float half = duration * 0.5f;
        for (float t = 0; t < half; t += Time.unscaledDeltaTime)
        {
            _canvasGroup.alpha = Mathf.Lerp(0, peak, t / half);
            yield return null;
        }
        for (float t = 0; t < half; t += Time.unscaledDeltaTime)
        {
            _canvasGroup.alpha = Mathf.Lerp(peak, 0, t / half);
            yield return null;
        }
        _canvasGroup.alpha = 0;
    }
}
```

> `unscaledDeltaTime` 사용 → 정상 동작 보장

**난이도:** 낮음 | **체감 효과:** 보조 피드백

---

### 5. 파기 사운드 피치 변조 (선택)

**목표:** 차지 수준/콤보 단계에 따라 히트 사운드 피치 변조

```csharp
// 삽 풀차지 시 피치 높게
audioSource.pitch = Mathf.Lerp(0.9f, 1.3f, chargeRatio);

// 곡괭이 콤보 단계별 피치
float[] comboPitch = { 1.0f, 1.1f, 1.3f };
audioSource.pitch = comboPitch[_comboStep - 1];
```

**난이도:** 중간 (오디오 시스템 연동 필요) | **체감 효과:** 청각적 다양성

---

## 구현 우선순위

> 히트스톱은 삽·곡괭이처럼 반복 사용하는 도구에서 리듬을 끊는 부작용이 크다고 판단하여 제외.
> 추후 보스 몬스터 등 특수 타격 이벤트가 생길 때 별도 검토.

| 순위 | 항목 | 효과 | 난이도 | 상태 |
|------|------|------|--------|------|
| 1 | 파티클 역방향 튐 | ⭐⭐⭐ 즉각적 체감 | 낮음 | ✅ 구현 완료 |
| 2 | 카메라 쉐이크 | ⭐⭐⭐ 극적 향상 | 낮음 | ✅ 구현 완료 |
| 3 | 임팩트 파티클 | ⭐⭐ 시각적 화려함 | 낮음 | ✅ 구현 완료 |
| 4 | 화면 플래시 | ⭐⭐ 보조 피드백 | 낮음 | ✅ 구현 완료 |
| 5 | 사운드 피치 변조 | ⭐ 청각적 다양성 | 중간 | 미구현 |

---

## 관련 파일 경로

```
수정된 파일:
├── Assets/Scripts/Gameplay/Terrain/Tiles/TerrainParticleManager.cs  (항목 1, 3)
├── Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs      (항목 1)
├── Assets/Scripts/Gameplay/Terrain/Tiles/Digger.cs                  (항목 2, 3)
├── Assets/Scripts/UI/Player/Strategies/SapStrategy.cs               (항목 4)
└── Assets/Scripts/UI/Player/Strategies/PickaxeStrategy.cs           (항목 4)

신규 생성:
├── Assets/Scripts/Camera/CameraShakeManager.cs                      (항목 2)
└── Assets/Scripts/UI/HitFlashUI.cs                                  (항목 4)
```

---

## Unity 에디터 설정 (필수)

코드 구현은 완료됨. 아래 에디터 작업을 완료해야 실제로 동작함.

### 1. CameraShakeManager — 메인 카메라에 부착

1. Hierarchy에서 **Main Camera** 선택
2. Inspector → **Add Component** → `CameraShakeManager` 추가

> 별도 설정값 없음. 부착만 하면 됨.

---

### 2. HitFlashUI — 화면 플래시 오브젝트 생성

1. Hierarchy에서 **우클릭 → UI → Canvas** 생성
   - 이름: `HitFlashCanvas`
   - `Canvas` 컴포넌트 설정:
     - Render Mode: **Screen Space - Overlay**
     - Sort Order: **200** (다른 UI보다 위에 렌더링)
2. `HitFlashCanvas`에 **Add Component** → `HitFlashUI` 추가
   - `[RequireComponent]`로 `CanvasGroup`이 자동 추가됨
3. `HitFlashCanvas`의 자식으로 **우클릭 → UI → Image** 생성
   - 이름: `FlashImage`
   - `Rect Transform`: Anchor = **Stretch/Stretch**, Left/Right/Top/Bottom = **0**
   - `Image` 컴포넌트:
     - Color: **흰색 (R:255, G:255, B:255, A:255)**
     - `Raycast Target`: **체크 해제** (클릭 이벤트 통과)

> `CanvasGroup`의 `Blocks Raycasts`와 `Interactable`은 코드에서 자동으로 false 처리됨.

---

### 3. TerrainParticleManager — impactSystem 연결

`impactSystem` 필드는 파기 중심 스파크 전용 ParticleSystem.
**연결하지 않으면 기존 `debrisSystem`을 그대로 재사용**하므로 필수는 아님.

별도 임팩트 파티클이 필요한 경우:

1. TerrainParticleManager가 붙어 있는 GameObject 선택
2. 자식으로 **ParticleSystem** 추가
   - 이름: `ImpactParticleSystem`
   - `Renderer` → Sorting Layer: 지형과 같은 레이어
   - `Renderer` → Order in Layer: **3** (지형 앞)
3. Inspector에서 `TerrainParticleManager`의 **Impact System** 필드에 드래그 연결

---

## 주의사항

- 카메라 쉐이크는 `transform.localPosition` 기준으로 동작 → 카메라가 다른 오브젝트의 자식이면 부모 좌표 기준으로 쉐이크됨 (의도된 동작)
- 드릴은 연속 파기 특성상 쉐이크 강도 최소화 (magnitude 0.02), 플래시 없음
