# 도구 해금 잠금 시스템 설계 계획
@tags: equipment, unlock, lock, plan, tool, upgrade

## 대상

손, 삽, 곡괭이, 드릴 등 `ToolController`가 관리하는 도구에만 적용.
인벤토리 방어구(머리/옷/신발/유물)는 이 시스템과 무관.

---

## 도구 해금 잠금

### 현재 상태 (As-Is)

- `ToolController`가 `toolSprites[]` 배열을 단순 인덱스 순환(+1/-1)으로 교체
- 해금 조건 없음 — 모든 도구가 처음부터 선택 가능

### 목표 (To-Be)

- 도구별로 `UpgradeNodeSO` 해금 조건을 Inspector에서 지정 가능
- 마우스 휠 스크롤 시 잠긴 도구는 완전히 건너뜀 (선택 목록에 등장하지 않음)
- 조건 없는 도구는 항상 선택 가능 (기존 동작 유지)
- **지하씬에서는 손(index 0)을 선택 불가** — 스크롤 시 건너뜀, 지하 진입 시 손이 선택된 상태라면 자동으로 다음 가용 도구로 전환

---

### Step 1 — 도구별 해금 조건 데이터 추가

**파일:** `Assets/Scripts/UI/Player/Tools/ToolController.cs`

`toolSprites[]`와 인덱스가 1:1 대응하는 해금 조건 배열 추가:

```csharp
[Header("도구 해금 조건")]
[Tooltip("toolSprites와 같은 순서. null이면 항상 해금됨.")]
public UpgradeNodeSO[] toolUnlockNodes;
```

헬퍼 메서드:

```csharp
private bool IsToolUnlocked(int index)
{
    if (toolUnlockNodes == null || index >= toolUnlockNodes.Length) return true;
    var node = toolUnlockNodes[index];
    return node == null ||
           (UpgradeManager.Instance != null &&
            UpgradeManager.Instance.IsNodeUnlocked(node.nodeId));
}
```

---

### Step 2 — 지하씬 손 사용 금지

**파일:** `Assets/Scripts/UI/Player/Tools/ToolController.cs`

#### 2-A. 지하씬 판별

`InventoryUI.IsUndergroundScene()`은 `InventoryUI` 인스턴스가 필요하다.
`ToolController`는 UI 레이어와 분리되어 있으므로 `gameObject.scene.name`으로 판별한다.

```csharp
[Header("씬 설정")]
[Tooltip("지하씬 이름 (InventoryUI.undergroundSceneName과 일치해야 함)")]
public string undergroundSceneName = "UndergroundScene";

private bool IsUndergroundScene()
    => gameObject.scene.name == undergroundSceneName;
```

> `SceneManager.GetActiveScene()`을 쓰면 안 된다. 씬 로딩이 Additive 방식이라 `Start()` 시점에 아직 `SetActiveScene`이 호출되지 않아 LoadingScene 이름을 반환한다. `gameObject.scene`은 SetActiveScene과 무관하게 이 오브젝트가 소속된 씬을 반환한다.

#### 2-B. 선택 가능 여부 통합 메서드

해금 조건과 지하씬 손 금지를 하나로 묶는다:

```csharp
private bool IsToolSelectable(int index)
{
    // 지하씬에서 손(index 0) 금지
    if (IsUndergroundScene() && index == 0) return false;
    // 업그레이드 해금 조건
    return IsToolUnlocked(index);
}
```

`FindNextUnlockedTool()`과 `Start()` 초기화에서 `IsToolUnlocked` 대신 `IsToolSelectable`을 사용한다.

#### 2-C. 지하씬 진입 시 자동 전환

`ToolController`는 `DontDestroyOnLoad`가 아니므로 씬 전환 시 플레이어와 함께 새로 생성된다.
따라서 `sceneLoaded` 이벤트 구독은 불필요하고, `Start()`에서만 처리하면 충분하다.

---

### Step 3 — 스크롤 순환 로직 교체

**파일:** `Assets/Scripts/UI/Player/Tools/ToolController.cs`

현재 `Update()` 스크롤 분기를 아래 메서드 기반으로 교체.
`IsToolUnlocked` 대신 `IsToolSelectable`을 사용해 해금 조건과 지하씬 손 금지를 동시에 반영:

```csharp
// 방향: +1 (위 스크롤) 또는 -1 (아래 스크롤)
private int FindNextSelectableTool(int direction)
{
    int count = toolSprites.Length;
    int next = currentToolIndex;
    for (int i = 0; i < count - 1; i++)
    {
        next = (next + direction + count) % count;
        if (IsToolSelectable(next)) return next;
    }
    return currentToolIndex; // 선택 가능한 도구가 하나뿐이면 유지
}
```

기존 코드:
```csharp
if (scroll > 0f)
{
    currentToolIndex++;
    if (currentToolIndex >= toolSprites.Length) currentToolIndex = 0;
}
else
{
    currentToolIndex--;
    if (currentToolIndex < 0) currentToolIndex = toolSprites.Length - 1;
}
```

변경 후:
```csharp
if (scroll > 0f)
    currentToolIndex = FindNextSelectableTool(+1);
else
    currentToolIndex = FindNextSelectableTool(-1);
```

---

### Step 4 — 초기화 시 선택 불가 도구 처리

`Start()`에서 현재 인덱스가 선택 불가 상태이면 다음 가용 도구로 이동.

**주의:** 자동 전환이 일어난 경우 `WeaponSwapUI`가 `OnToolSwapped` 이벤트를 통해 UI를 갱신한다.
`UpdateToolSprite(-1)` 은 `previousIndex == -1`이어서 이벤트가 발생하지 않으므로,
전환이 실제로 일어난 경우엔 이전 인덱스를 정확히 전달해야 한다:

```csharp
void Start()
{
    int startIndex = currentToolIndex;

    if (!IsToolSelectable(currentToolIndex))
        currentToolIndex = FindNextSelectableTool(+1);

    // 자동 전환이 일어났으면 previousIndex를 실제 값으로 전달해 OnToolSwapped 발생
    UpdateToolSprite(currentToolIndex != startIndex ? startIndex : -1);
}
```

---

### 파일 수정 목록

| 파일 | 변경 내용 |
|------|----------|
| `ToolController.cs` | `toolUnlockNodes[]` 배열 + `undergroundSceneName` 추가, `IsToolUnlocked()` / `IsToolSelectable()` / `FindNextSelectableTool()` 추가, 스크롤 로직 교체, `OnSceneLoaded` 콜백으로 씬 전환 시 자동 전환 처리 |

### 주의 사항

- `toolUnlockNodes`를 비워두거나 null 원소로 두면 기존 동작 그대로 유지 (하위 호환)
- `undergroundSceneName` 기본값은 `"UndergroundScene"` — `InventoryUI.undergroundSceneName`과 동일
- `ToolController`는 DontDestroyOnLoad가 아니므로 씬 전환 시 `Start()`가 항상 새로 호출됨. `sceneLoaded` 이벤트 구독 불필요
- `EquipTool(int index)` (외부 강제 전환 메서드)에 잠금 체크를 넣을지는 호출처 의도에 따라 결정 필요 (현재 범위 외)
- 해금 취소 기능이 생기면, 현재 선택된 도구가 잠기는 케이스 처리 필요 (현재 범위 외)

