# 스프라이트 테두리 원샷 베이크 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development 또는 superpowers:executing-plans 로 task 단위 구현. 스텝은 체크박스(`- [ ]`)로 추적.

**Goal:** SpriteRenderer + PolygonCollider2D 정적 오브젝트의 스프라이트 알파를 모양으로 삼아, 지형과 동일한 테두리 타일 + rim을 구운 텍스처로 `SpriteRenderer.sprite`를 교체한다.

**Architecture:** 기존 지형 테두리 파이프라인(`ChunkData` + `TerrainVisualizer` + `ChunkJobScheduler` + `TerrainVisualJob`)을 원샷 베이크로 재사용한다. 스프라이트 RGBA를 `BasePixels`로 넣어 알파가 거리장을 결정하고, 가장자리에 테두리·rim이 덧그려진 텍스처를 구워 반환한다.

**Tech Stack:** Unity 2D, C#, Unity.Collections(NativeArray), Burst Jobs, NUnit(EditMode).

## Global Constraints

- **버전 관리: UVCS** — git 명령 금지. "커밋" 스텝은 사람이 UVCS로 수행하는 체크포인트다.
- **Unity 테스트는 사람이 실행** — Claude는 테스트 파일 작성만. `.Run(n)` + `Allocator.Temp` 패턴(`TerrainVisualJobRimTests.cs` 참고). 테스트 통과를 게이트로 요구하지 않는다.
- **rim은 지형과 동일** — `TerrainChunk` static(`RimThicknessPx`/`RimColor`) 그대로 사용, 오브젝트별 커스터마이즈 없음.
- **더티 플래그 직접 세팅 금지** — 해당 없음(베이크는 `TerrainVisualizer` 경유).
- 설계 문서: `Assets/Docs/sprite-terrain-border/design.md`

## File Structure

| 파일 | 종류 | 책임 |
|------|------|------|
| `Assets/Scripts/_Core/Data/ChunkData.cs` | 수정 | Primary `BorderData` 세팅 public 헬퍼 `SetBorderData` 추가 |
| `Assets/Scripts/Render/World/SpriteBorderBaker.cs` | 생성 | 순수 static 베이크 로직. 스프라이트 → 테두리 구운 새 Sprite |
| `Assets/Scripts/Render/World/SpriteTerrainBorder.cs` | 생성 | MonoBehaviour. 오브젝트 부착, `Start`에서 1회 베이크 |
| `Assets/Tests/EditMode/ChunkDataBorderDataTests.cs` | 생성 | `SetBorderData` 단위 테스트 |
| `Assets/Tests/EditMode/SpriteBorderBakerTests.cs` | 생성 | 베이커 픽셀 레벨 통합 테스트 |

---

### Task 1: `ChunkData.SetBorderData` 헬퍼

`BorderData`/`BorderWidth`/`BorderHeight`는 public 필드지만 초기값이 0-length NativeArray다. `SetSecondaryBorderData`와 대칭인 안전한 세팅 메서드를 추가한다.

**Files:**
- Modify: `Assets/Scripts/_Core/Data/ChunkData.cs` (기존 `SetSecondaryBorderData` 아래)
- Test: `Assets/Tests/EditMode/ChunkDataBorderDataTests.cs`

**Interfaces:**
- Produces: `public void ChunkData.SetBorderData(Color32[] sourcePixels, int width, int height)`

- [ ] **Step 1: 실패 테스트 작성**

`Assets/Tests/EditMode/ChunkDataBorderDataTests.cs` 생성:

```csharp
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

public class ChunkDataBorderDataTests
{
    [Test]
    public void SetBorderData_CopiesPixelsAndDimensions()
    {
        var data = new ChunkData(4, 4);
        var border = new Color32[]
        {
            new Color32(10, 0, 0, 255), new Color32(20, 0, 0, 255),
            new Color32(30, 0, 0, 255), new Color32(40, 0, 0, 255),
        };

        data.SetBorderData(border, 2, 2);

        Assert.AreEqual(4, data.BorderData.Length);
        Assert.AreEqual(2, data.BorderWidth);
        Assert.AreEqual(2, data.BorderHeight);
        Assert.AreEqual(30, data.BorderData[2].r);

        data.Dispose();
    }

    [Test]
    public void SetBorderData_SecondCall_ResizesWithoutLeak()
    {
        var data = new ChunkData(4, 4);
        data.SetBorderData(new Color32[4], 2, 2);
        data.SetBorderData(new Color32[9], 3, 3);   // 크기 변경

        Assert.AreEqual(9, data.BorderData.Length);
        Assert.AreEqual(3, data.BorderWidth);

        data.Dispose();
    }
}
```

- [ ] **Step 2: 테스트 실패 확인 (사람이 Test Runner 실행, 게이트 아님)**

기대: `SetBorderData` 미정의로 컴파일 실패.

- [ ] **Step 3: 구현 추가**

`ChunkData.cs`의 `SetSecondaryBorderData` 메서드 바로 아래에 추가:

```csharp
    /// <summary>
    /// Primary BorderData 버퍼를 managed 배열에서 세팅/리사이즈한다.
    /// SetSecondaryBorderData 와 대칭. 초기 0-length 버퍼를 안전하게 교체한다.
    /// </summary>
    public void SetBorderData(Color32[] sourcePixels, int width, int height)
    {
        if (sourcePixels == null || sourcePixels.Length == 0)
        {
            Debug.LogWarning("[ChunkData] SetBorderData: sourcePixels is null or empty.");
            return;
        }

        BorderWidth = width;
        BorderHeight = height;

        if (BorderData.Length != sourcePixels.Length)
        {
            if (BorderData.IsCreated) BorderData.Dispose();
            BorderData = new NativeArray<Color32>(sourcePixels, Allocator.Persistent);
        }
        else
        {
            BorderData.CopyFrom(sourcePixels);
        }
    }
```

- [ ] **Step 4: 테스트 통과 확인 (사람이 실행, 게이트 아님)**

기대: 두 테스트 PASS.

- [ ] **Step 5: 체크포인트** — 사람이 UVCS로 커밋 (`feat: ChunkData.SetBorderData 헬퍼 추가`).

---

### Task 2: `SpriteBorderBaker` 순수 베이크 로직

스프라이트 알파를 모양으로 삼아 테두리를 구운 새 Sprite를 반환하는 static 유틸. 정적 오브젝트 전용 메인스레드 원샷.

**Files:**
- Create: `Assets/Scripts/Render/World/SpriteBorderBaker.cs`
- Test: `Assets/Tests/EditMode/SpriteBorderBakerTests.cs`

**Interfaces:**
- Consumes: `ChunkData.SetBorderData` (Task 1), `TerrainVisualizer(ChunkData, Texture2D, SpriteRenderer)`, `TerrainVisualizer.UpdateVisualsFull(int,int)`, `TerrainVisualizer.ApplyTextureSync()`
- Produces:
  - `struct SpriteBorderBaker.Settings { public float textureThickness; public int borderPixelsPerUnit; }`
  - `static Sprite SpriteBorderBaker.Bake(Sprite src, Texture2D borderTex, Settings s)`

- [ ] **Step 1: 실패 테스트 작성**

`Assets/Tests/EditMode/SpriteBorderBakerTests.cs` 생성. 24×24 텍스처 중앙에 12×12 솔리드 사각(투명 여백 6px), 단색 테두리 아틀라스로 베이크 후 픽셀 검증:

```csharp
using NUnit.Framework;
using UnityEngine;

public class SpriteBorderBakerTests
{
    // 중앙 정사각형 + 투명 여백을 가진 읽기 가능한 스프라이트 생성
    private static Sprite MakeSquareSprite(int size, int margin, Color32 fill)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        var clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool solid = x >= margin && x < size - margin &&
                         y >= margin && y < size - margin;
            px[y * size + x] = solid ? fill : clear;
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // 단색 테두리 아틀라스 (모든 픽셀 동일 색)
    private static Texture2D MakeBorderTex(int w, int h, Color32 c)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private static Color32 PixelAt(Sprite s, int x, int y)
    {
        return s.texture.GetPixels32()[y * s.texture.width + x];
    }

    [Test]
    public void Bake_AirStaysTransparent()
    {
        var src = MakeSquareSprite(24, 6, new Color32(100, 120, 140, 255));
        var border = MakeBorderTex(8, 8, new Color32(0, 0, 255, 255));

        var baked = SpriteBorderBaker.Bake(src, border,
            new SpriteBorderBaker.Settings { textureThickness = 2f, borderPixelsPerUnit = 100 });

        Assert.AreEqual(0, PixelAt(baked, 0, 0).a, "여백(공기) 픽셀은 투명이어야 한다");
    }

    [Test]
    public void Bake_InteriorKeepsOriginalColor()
    {
        var fill = new Color32(100, 120, 140, 255);
        var src = MakeSquareSprite(24, 6, fill);
        var border = MakeBorderTex(8, 8, new Color32(0, 0, 255, 255));

        var baked = SpriteBorderBaker.Bake(src, border,
            new SpriteBorderBaker.Settings { textureThickness = 2f, borderPixelsPerUnit = 100 });

        Color32 c = PixelAt(baked, 12, 12);   // 정사각형 정중앙 = 가장자리에서 가장 먼 곳
        Assert.AreEqual(fill.r, c.r);
        Assert.AreEqual(fill.g, c.g);
        Assert.AreEqual(fill.b, c.b);
        Assert.AreEqual(255, c.a);
    }

    [Test]
    public void Bake_EdgeGetsBorderColor()
    {
        var fill = new Color32(100, 120, 140, 255);
        var src = MakeSquareSprite(24, 6, fill);
        var borderColor = new Color32(0, 0, 255, 255);
        var border = MakeBorderTex(8, 8, borderColor);

        var baked = SpriteBorderBaker.Bake(src, border,
            new SpriteBorderBaker.Settings { textureThickness = 2f, borderPixelsPerUnit = 100 });

        // 정사각형 최상단 솔리드 행(y = size-margin-1 = 17)의 중앙 픽셀은 공기와 인접 → 원래색이 아니어야 한다
        Color32 c = PixelAt(baked, 12, 17);
        bool changed = c.r != fill.r || c.g != fill.g || c.b != fill.b;
        Assert.IsTrue(changed, "가장자리 솔리드 픽셀은 테두리/rim 이 덧그려져 원래색과 달라야 한다");
        Assert.AreEqual(255, c.a);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인 (사람 실행, 게이트 아님)**

기대: `SpriteBorderBaker` 미정의로 컴파일 실패.

- [ ] **Step 3: 구현 작성**

`Assets/Scripts/Render/World/SpriteBorderBaker.cs` 생성:

```csharp
using UnityEngine;

/// <summary>
/// 스프라이트 알파를 모양으로 삼아, 지형과 동일한 테두리 타일 + rim 을 구운 새 Sprite 를 반환한다.
/// 정적 오브젝트 전용 — 메인스레드 원샷 동기 실행 후 즉시 리소스 해제.
/// 기존 지형 파이프라인(ChunkData + TerrainVisualizer + TerrainVisualJob)을 재사용한다.
/// 설계: Assets/Docs/sprite-terrain-border/design.md
/// </summary>
public static class SpriteBorderBaker
{
    public struct Settings
    {
        public float textureThickness;   // 테두리 두께(유닛). 지형과 동일하게 기본 4
        public int borderPixelsPerUnit;  // 두께→픽셀 변환 PPU. 지형=100
    }

    /// <summary>
    /// src 의 스프라이트 rect 알파를 모양으로 테두리를 구운 새 Sprite 반환.
    /// src.texture 는 Read/Write Enabled 여야 한다. 실패 시 src 를 그대로 반환.
    /// </summary>
    public static Sprite Bake(Sprite src, Texture2D borderTex, Settings s)
    {
        if (src == null || borderTex == null) return src;

        Rect r = src.textureRect;
        int w = Mathf.RoundToInt(r.width);
        int h = Mathf.RoundToInt(r.height);
        if (w <= 0 || h <= 0) return src;

        // 1. 스프라이트 rect 픽셀 추출 (아틀라스 서브렉트 대응). Read/Write 필요.
        Color[] block = src.texture.GetPixels((int)r.x, (int)r.y, w, h);
        var pixels = new Color32[block.Length];
        for (int i = 0; i < block.Length; i++) pixels[i] = block[i];

        // 2. ChunkData 구성 — 알파가 점유, PixelInfo 는 알파에서 자동 생성
        var data = new ChunkData(w, h);
        data.LoadPixelData(pixels);
        data.LoadPixelInfo(null);

        // 3. 테두리 아틀라스
        data.SetBorderData(borderTex.GetPixels32(), borderTex.width, borderTex.height);

        // 4. 출력 텍스처
        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = src.texture.filterMode,
            wrapMode = TextureWrapMode.Clamp,
        };

        // 5~6. 파이프라인 원샷 (Downsample→Init→Chamfer→Upsample→VisualJob→Apply)
        //      rim 은 TerrainChunk static 을 그대로 사용. SpriteRenderer 인자는 job 경로에서 미사용 → null.
        var vis = new TerrainVisualizer(data, outTex, null);
        vis.TextureThickness = s.textureThickness;
        vis.PixelsPerUnit = s.borderPixelsPerUnit;
        vis.UpdateVisualsFull(0, 0);
        vis.ApplyTextureSync();

        // 7. 새 Sprite — 원본 월드 크기 유지를 위해 src 의 pivot/PPU 사용
        Vector2 pivotNorm = new Vector2(src.pivot.x / w, src.pivot.y / h);
        Sprite baked = Sprite.Create(outTex, new Rect(0, 0, w, h), pivotNorm, src.pixelsPerUnit);

        // 8. 정리
        vis.Dispose();
        data.Dispose();
        return baked;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인 (사람 실행, 게이트 아님)**

기대: 3개 테스트 PASS. `TerrainVisualizer` 생성자에 null SpriteRenderer 전달 시 예외가 나면(구현이 저장 외에 역참조하면) 대신 `outTex` 대상 임시 SpriteRenderer 없이 처리 — 실제 코드상 생성자는 저장만 하므로 null 안전(확인: `TerrainVisualizer.cs:28-34`).

- [ ] **Step 5: 체크포인트** — 사람이 UVCS로 커밋 (`feat: SpriteBorderBaker 원샷 베이커 추가`).

---

### Task 3: `SpriteTerrainBorder` MonoBehaviour

오브젝트에 부착해 `Start`에서 자기 스프라이트를 베이크·교체한다.

**Files:**
- Create: `Assets/Scripts/Render/World/SpriteTerrainBorder.cs`

**Interfaces:**
- Consumes: `SpriteBorderBaker.Bake`, `SpriteBorderBaker.Settings` (Task 2)
- Produces: `MonoBehaviour SpriteTerrainBorder` (씬/프리팹 부착용, 순수 로직 없음 → 자동화 테스트 없음. 수동 검증)

- [ ] **Step 1: 구현 작성**

`Assets/Scripts/Render/World/SpriteTerrainBorder.cs` 생성:

```csharp
using UnityEngine;

/// <summary>
/// 정적 오브젝트에 부착. Start 에서 자기 SpriteRenderer 의 스프라이트를
/// 지형과 동일한 테두리로 구워 교체한다. (SpriteBorderBaker 재사용)
///
/// 사용 조건:
/// - 스프라이트 텍스처 import 에서 Read/Write Enabled 체크
/// - 스프라이트 아트가 텍스처 가장자리에 닿지 않도록 투명 여백 확보
///   (BoundarySync 미사용 → 닿으면 그 변은 테두리 없이 잘림)
/// - 정적 오브젝트 전용 (모양이 변하면 매번 재베이크 필요 — 현 범위 밖)
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteTerrainBorder : MonoBehaviour
{
    [Tooltip("지형과 동일한 테두리 아틀라스 텍스처")]
    [SerializeField] private Texture2D borderTexture;

    [Tooltip("테두리 두께(유닛). 지형 기본값 4")]
    [SerializeField] private float textureThickness = 4f;

    [Tooltip("두께→픽셀 변환 PPU. 지형 기본값 100")]
    [SerializeField] private int borderPixelsPerUnit = 100;

    private void Start()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null || borderTexture == null)
        {
            Debug.LogWarning($"[SpriteTerrainBorder] {name}: sprite 또는 borderTexture 미설정 — 베이크 스킵.");
            return;
        }

        var settings = new SpriteBorderBaker.Settings
        {
            textureThickness = textureThickness,
            borderPixelsPerUnit = borderPixelsPerUnit,
        };

        Sprite baked = SpriteBorderBaker.Bake(sr.sprite, borderTexture, settings);
        if (baked != null) sr.sprite = baked;
    }
}
```

- [ ] **Step 2: 수동 검증 (사람)**

1. 테스트 씬에 SpriteRenderer + PolygonCollider2D 오브젝트 배치, 투명 여백 있는 스프라이트 지정
2. 스프라이트 텍스처 import: Read/Write Enabled 체크
3. `SpriteTerrainBorder` 부착, `borderTexture`에 지형 테두리 아틀라스 지정
4. Play → 오브젝트 가장자리에 지형과 동일한 테두리 + rim 이 나타나는지 확인
5. PolygonCollider2D 모양은 변하지 않음(테두리는 안쪽으로만 그려짐) 확인

- [ ] **Step 3: 체크포인트** — 사람이 UVCS로 커밋 (`feat: SpriteTerrainBorder 컴포넌트 추가`).

---

## Self-Review

**Spec coverage:**
- 스프라이트 알파 모양 → Task 2 (`LoadPixelData` + `LoadPixelInfo(null)`) ✅
- 지형 동일 테두리 타일 → Task 2 (`SetBorderData` + `UpdateVisualsFull`) ✅
- rim 지형 동일 → Task 2 (TerrainChunk static 그대로) ✅
- 정적 원샷 + Dispose → Task 2 (Bake 말미 Dispose) ✅
- 콜라이더 수정 불필요 → Task 3 수동 검증 5 ✅
- Read/Write·투명여백 제약 → Task 3 주석·수동 검증 ✅
- `SetBorderData` 헬퍼 부재 → Task 1 ✅

**Placeholder scan:** 모든 코드 스텝에 실제 코드 포함, TBD 없음 ✅

**Type consistency:** `Settings { textureThickness(float), borderPixelsPerUnit(int) }`, `Bake(Sprite, Texture2D, Settings)`, `SetBorderData(Color32[], int, int)` — Task 1/2/3 전체 일치 ✅
