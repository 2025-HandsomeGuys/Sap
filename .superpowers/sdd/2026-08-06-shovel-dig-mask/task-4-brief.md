## Task 4: 인스펙터 배선과 진단 로그

**Files:**
- Modify: `Assets/Scripts/UI/Player/PlayerMining.cs:16-23` (필드 추가), `PlayerMining.cs:142-155` (`Start`)
- Modify: `Assets/Scripts/UI/Player/Strategies/SapStrategy.cs:254-259` (`LogDigs` 진단 줄)

**Interfaces:**
- Consumes: Task 2의 `ShovelDigMask.Set(Texture2D, float)`, Task 1의 `ShovelDigMask.IsActive`
- Produces: `PlayerMining.shovelDigMask`, `PlayerMining.shovelDigMaskScale` (인스펙터 노출)

- [ ] **Step 1: `PlayerMining`에 인스펙터 필드 추가**

`PlayerMining.cs` 23행 `public int wallClimbToolIndex = 0;` **바로 아래**에 추가:

```csharp

    [Header("삽 파기 모양 (비우면 기존 타원)")]
    [Tooltip("삽이 파는 구멍 모양 이미지. Import Settings에서 Read/Write Enabled 필수.\n" +
             "삽이 +X(오른쪽)를 향하는 그림으로 그릴 것 — 마우스 방향으로 회전한다.\n" +
             "알파 10 초과 픽셀이 파인다. 비우면 기존 회전 타원으로 판다.")]
    public Texture2D shovelDigMask;

    [Tooltip("마스크 크기 배율. 1 = 마스크 가로폭이 파기 지름과 같다(꽉 찬 원을 넣으면 기존 원형과 동일).")]
    public float shovelDigMaskScale = 1f;
```

- [ ] **Step 2: `Start`에서 마스크 적용 + `OnValidate` 추가**

`PlayerMining.cs`의 `Start()` 안, 154행 `_emptyStrategy = new EmptyStrategy();` **바로 아래**에 추가:

```csharp

        // 삽 파기 모양 마스크. 실패 사유는 ShovelDigMask.Set이 콘솔에 남긴다.
        ShovelDigMask.Set(shovelDigMask, shovelDigMaskScale);
```

그리고 `Start()` 메서드가 끝나는 `}` **바로 뒤**에 새 메서드를 추가:

```csharp

    /// <summary>
    /// 인스펙터에서 마스크·배율을 만지면 즉시 반영한다. 이미지 모양을 눈으로 맞춰보는 게
    /// 이 기능의 주 용도라, 플레이를 껐다 켜야 반영되면 쓸모가 없다.
    /// 로그 도배는 ShovelDigMask.Set이 (텍스처, 배율, 결과) 조합으로 억제한다.
    /// </summary>
    private void OnValidate()
    {
        ShovelDigMask.Set(shovelDigMask, shovelDigMaskScale);
    }
```

`PlayerMining`에는 현재 `OnValidate`가 없다(확인함). 새로 만드는 게 맞다.

- [ ] **Step 3: `SapStrategy`의 진단 줄에 `shape=` 추가**

`SapStrategy.cs` 254~259행. 아래 기존 블록을

```csharp
        if (MiningStaminaTuning.LogDigs)
        {
            Debug.Log($"[SapDig] f{Time.frameCount} charging={_isCharging} attacking={_isAttacking} " +
                      $"chargeTimer={_currentChargeTimer:F3} ratio={GetChargeRatio():F3} " +
                      $"canDig={p.CanDig} tool={p.ToolIndex} radius={(p.CanDig ? p.RadiusMultiplier : 0f):F4}");
        }
```

이렇게 교체한다 (`shape=` 항목 하나만 추가):

```csharp
        if (MiningStaminaTuning.LogDigs)
        {
            // shape: 마스크가 실제로 먹었는지. "삽인데 왜 이미지 모양이 안 나오지"를 여기서 본다.
            // 기본 off라 평상시엔 안 찍힌다. 실패 사유 자체는 ShovelDigMask.Set이 세팅 시점에 남긴다.
            string shape = (p.ToolIndex == MiningStaminaTuning.Shovel && ShovelDigMask.IsActive)
                ? "mask" : "ellipse";
            Debug.Log($"[SapDig] f{Time.frameCount} charging={_isCharging} attacking={_isAttacking} " +
                      $"chargeTimer={_currentChargeTimer:F3} ratio={GetChargeRatio():F3} " +
                      $"canDig={p.CanDig} tool={p.ToolIndex} shape={shape} " +
                      $"radius={(p.CanDig ? p.RadiusMultiplier : 0f):F4}");
        }
```

- [ ] **Step 4: 수동 확인**

**사람이 직접**, 플레이어 프리팹의 `PlayerMining`에서:

| 확인 | 기대 |
|---|---|
| 마스크 비움 → 삽질 | 지금과 똑같은 타원 구멍 + 콘솔 `마스크 미설정 → 기존 타원` |
| Read/Write 꺼진 PNG 지정 | `LogError`에 이미지 이름 + `Import Settings에서 켤 것` / 타원으로 파짐 |
| `shovelDigMaskScale`에 0 입력 | `LogWarning` 배율 사유 / 타원 |
| 전부 투명한 PNG 지정 | `LogWarning` 알파 사유 / 타원 |
| 정상 PNG 지정 | `Log` 1줄에 크기·불투명 px·%·배율 / 이미지 모양대로 파임 |
| 인스펙터에서 같은 값 반복 클릭 | 로그가 더 안 늘어남 |
| 플레이 중 배율 슬라이더 조절 | 로그 갱신 + 다음 삽질부터 크기 반영 |
| 마우스를 위/왼쪽/아래로 두고 삽질 | 마스크가 그 방향으로 회전 (+X가 파는 방향) |
| 청크 경계를 걸쳐 삽질 | 마스크가 잘리지 않고 이어짐 |
| 불괴 픽셀(오버레이) 위 삽질 | 보호됨 |
| F8 패널에서 `LogDigs` on → 삽질 | `[SapDig] ... shape=mask ...` |

- [ ] **Step 5: 체크인**

UVCS 체크인 — **사람이 직접**. 설명 예시: `feat: 삽 파기 마스크 인스펙터 배선 + SapDig 진단에 shape 추가`

