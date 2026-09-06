# XRay 3톤 단색화 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 엑스레이 유물 발동 중 화면을 광물(밝음) / 지형(중간) / 배경(어두움) 3톤 단색으로 재구성한다.

**Architecture:** 대상 `SpriteRenderer`의 `sharedMaterial`을 언릿 플랫 셰이더 머티리얼 3종으로 스왑한다. 지형 텍스처의 알파가 곧 "안 판 땅" 실루엣이므로 판 굴은 자동으로 뚫려 배경 톤이 보인다 — 별도 마스크 없이 3톤이 분리된다. 기존 풀스크린 패스는 톤 합성을 버리고 감광+비네트만 담당한다.

**Tech Stack:** Unity 2D / URP 17 (RenderGraph), HLSL, C#

**설계 문서:** `docs/superpowers/specs/2026-07-20-xray-flat-tone-design.md`

## Global Constraints

- **버전 관리는 UVCS(Unity Version Control)다. `git` 명령을 사용하지 않는다.** 이 플랜의 "커밋" 스텝은 UVCS 체크인을 의미하며, **사람이 직접 수행**한다. 에이전트는 커밋하지 않고 "체크인 시점"만 알린다.
- **Unity Test Runner 실행은 사람이 직접 수행한다.** 에이전트는 테스트 파일을 작성·수정만 하고 `mcp__mcp-unity__run_tests` 등 실행 도구를 호출하지 않는다. 테스트 통과를 다음 태스크의 게이트로 요구하지 않는다.
- 머티리얼 스왑은 반드시 **`sharedMaterial`** 로 한다. `material`을 쓰면 렌더러마다 인스턴스가 생겨 누수된다.
- 전역 셰이더 프로퍼티 이름 `_XRayAmount`는 기존 값을 그대로 재사용한다(`XRayRendererFeature`가 이 값으로 패스 스킵을 판정한다).
- 신규 C# 코드는 기존 `Relic` 네임스페이스와 파일 주석 스타일(한국어 설명 주석)을 따른다.
- 색상 값은 코드에 하드코딩하지 않는다. `XRayRelic`의 `[SerializeField]`에서 주입한다.

---

## File Structure

| 파일 | 상태 | 책임 |
|---|---|---|
| `Assets/Shaders/XRayFlat.shader` | 생성 | 스프라이트 알파만 써서 단색 실루엣 출력. `_XRayAmount`로 원본↔단색 lerp |
| `Assets/Materials/XRay/XRayFlat_Terrain.mat` | 생성 | 지형 톤 머티리얼 |
| `Assets/Materials/XRay/XRayFlat_Background.mat` | 생성 | 배경 톤 머티리얼 |
| `Assets/Materials/XRay/XRayFlat_Object.mat` | 생성 | 오브젝트 톤 머티리얼 |
| `Assets/Scripts/Gameplay/Relics/Behaviours/XRayFlatPalette.cs` | 생성 | 3톤 머티리얼 로드·`_FlatColor` 주입. 순수 로직이라 EditMode 테스트 가능 |
| `Assets/Scripts/Gameplay/Relics/Behaviours/XRayController.cs` | 수정 | 수집 대상 3분류 확장, 머티리얼 스왑/복원, 리스캔 주기 분리 |
| `Assets/Scripts/Gameplay/Relics/Behaviours/XRayRelic.cs` | 수정 | 3톤 색 인스펙터 필드 추가, 컨트롤러에 주입 |
| `Assets/Scripts/Render/World/BackgroundManager.cs` | 수정 | `activeTiles` 읽기 전용 접근자 추가 |
| `Assets/Shaders/XRay.shader` | 수정 | 그레이스케일+틴트 제거, 감광+비네트만 유지 |
| `Assets/Tests/EditMode/XRayFlatPaletteTests.cs` | 생성 | 팔레트 주입 로직 테스트 |

`XRayRendererFeature.cs`는 수정하지 않는다.

---

## Task 1: 플랫 셰이더와 3종 머티리얼

**Files:**
- Create: `Assets/Shaders/XRayFlat.shader`
- Create: `Assets/Materials/XRay/XRayFlat_Terrain.mat`
- Create: `Assets/Materials/XRay/XRayFlat_Background.mat`
- Create: `Assets/Materials/XRay/XRayFlat_Object.mat`

**Interfaces:**
- Consumes: 없음 (첫 태스크)
- Produces: 셰이더 이름 `"Custom/XRayFlat"`. 머티리얼 프로퍼티 `_FlatColor` (Color). 전역 float `_XRayAmount`를 읽는다. 머티리얼 에셋 3개는 `Resources` 폴더가 아닌 경로에 있으므로 Task 3에서 인스펙터 참조로 연결한다.

- [ ] **Step 1: 셰이더 파일 작성**

`Assets/Shaders/XRayFlat.shader`:

```hlsl
// XRay 3톤 단색화 — SpriteRenderer에 물리는 언릿 플랫 셰이더.
// 스프라이트의 알파(실루엣)만 사용하고 RGB는 _FlatColor로 대체한다.
// 전역 _XRayAmount(0..1)로 원본 RGB ↔ _FlatColor를 블렌드하므로 페이드가 그대로 유지된다.
//
// 언릿인 이유: 2D Light 영향을 받으면 조명 밝기에 따라 톤이 흔들려
// "균일한 단색"이라는 X-ray 인상이 깨진다.
Shader "Custom/XRayFlat"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _FlatColor ("Flat Color", Color) = (0.5, 0.5, 0.5, 1)
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha   // 스프라이트 표준 premultiplied 규약

        Pass
        {
            Name "XRayFlatSprite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _FlatColor;
                float4 _Color;
            CBUFFER_END

            // SpriteRenderer.color는 _RendererColor로 들어온다(머티리얼 CBUFFER 밖).
            float4 _RendererColor;
            float  _XRayAmount;

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                o.color = input.color * _Color * _RendererColor;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 col = tex * input.color;

                // _XRayAmount=0 이면 원본과 동일, 1이면 완전 단색.
                half amt = saturate(_XRayAmount);
                col.rgb = lerp(col.rgb, _FlatColor.rgb * col.a, amt);

                col.rgb *= col.a;   // premultiply (Blend One OneMinusSrcAlpha 대응)
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
```

- [ ] **Step 2: 머티리얼 에셋 3개 생성**

Unity 에디터에서 `Assets/Materials/XRay/` 폴더를 만들고, 그 안에 머티리얼 3개를 생성한 뒤 셰이더를 `Custom/XRayFlat`로 지정한다.

- `XRayFlat_Terrain.mat`
- `XRayFlat_Background.mat`
- `XRayFlat_Object.mat`

`_FlatColor` 초기값은 무엇으로 두든 상관없다 — Task 3에서 런타임에 덮어쓴다. 다만 에디터에서 구분하기 쉽게 각각 중간 회색 / 어두운 회색 / 밝은 회색으로 둔다.

- [ ] **Step 3: 셰이더 컴파일 확인**

Unity 에디터에서 각 머티리얼을 선택하고 인스펙터에 **분홍색 에러 머티리얼이나 컴파일 에러가 없는지** 확인한다. 에러가 있으면 인스펙터 하단의 "Compile and show code"로 메시지를 확인한다.

기대: 3개 머티리얼 모두 정상 프리뷰(구체가 `_FlatColor` 색으로 보임), 콘솔에 셰이더 에러 없음.

- [ ] **Step 4: 체크인 시점**

사람이 UVCS로 체크인한다: "feat: XRay 플랫 단색 셰이더 + 3톤 머티리얼 추가"

---

## Task 2: BackgroundManager 접근자

**Files:**
- Modify: `Assets/Scripts/Render/World/BackgroundManager.cs:13`

**Interfaces:**
- Consumes: 없음
- Produces: `public IEnumerable<GameObject> ActiveTiles` — Task 3의 `XRayController`가 배경 타일을 순회할 때 사용한다.

- [ ] **Step 1: 읽기 전용 접근자 추가**

`activeTiles` 필드 선언(13번 줄) 바로 아래에 추가한다:

```csharp
    private Dictionary<Vector2Int, GameObject> activeTiles = new Dictionary<Vector2Int, GameObject>();

    /// <summary>현재 활성화된 배경 타일. XRayController가 단색 머티리얼 스왑 대상으로 순회한다.</summary>
    public IEnumerable<GameObject> ActiveTiles => activeTiles.Values;
```

내부 딕셔너리는 private으로 유지한다. 외부에서 추가·삭제하지 못하게 값만 노출한다.

- [ ] **Step 2: 컴파일 확인**

Unity 에디터로 포커스를 옮겨 컴파일이 끝나기를 기다린다.

기대: 콘솔에 컴파일 에러 없음.

- [ ] **Step 3: 체크인 시점**

사람이 UVCS로 체크인한다: "feat: BackgroundManager.ActiveTiles 읽기 전용 접근자 추가"

---

## Task 3: XRayFlatPalette — 3톤 머티리얼 주입

**Files:**
- Create: `Assets/Scripts/Gameplay/Relics/Behaviours/XRayFlatPalette.cs`
- Test: `Assets/Tests/EditMode/XRayFlatPaletteTests.cs`

**Interfaces:**
- Consumes: Task 1의 `_FlatColor` 프로퍼티를 가진 머티리얼 3종
- Produces:
  - `public enum XRayTone { Terrain, Background, Object }`
  - `public XRayFlatPalette(Material terrain, Material background, Material obj)`
  - `public void Apply(Color terrain, Color background, Color obj)` — 각 머티리얼의 `_FlatColor`를 세팅
  - `public Material Get(XRayTone tone)` — 스왑에 쓸 머티리얼 반환. 해당 머티리얼이 null이면 null 반환
  - `public bool IsComplete` — 3개 머티리얼이 모두 할당되었는지

  Task 4의 `XRayController`가 이 타입을 필드로 보유한다.

이 클래스를 분리하는 이유: `XRayController`는 `MonoBehaviour`라 EditMode 테스트가 번거롭다. 머티리얼 주입/조회는 순수 로직이므로 떼어내면 테스트 가능하고, 컨트롤러는 "무엇을 스왑할지"에만 집중한다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/Tests/EditMode/XRayFlatPaletteTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Relic;

public class XRayFlatPaletteTests
{
    private Material _terrain, _background, _object;
    private Shader _shader;

    [SetUp]
    public void SetUp()
    {
        _shader = Shader.Find("Custom/XRayFlat");
        Assert.IsNotNull(_shader, "Custom/XRayFlat 셰이더를 찾을 수 없다.");
        _terrain = new Material(_shader);
        _background = new Material(_shader);
        _object = new Material(_shader);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_terrain);
        Object.DestroyImmediate(_background);
        Object.DestroyImmediate(_object);
    }

    [Test]
    public void Apply_각_머티리얼에_해당_톤_색을_주입한다()
    {
        var palette = new XRayFlatPalette(_terrain, _background, _object);

        palette.Apply(Color.red, Color.green, Color.blue);

        Assert.AreEqual(Color.red, _terrain.GetColor("_FlatColor"));
        Assert.AreEqual(Color.green, _background.GetColor("_FlatColor"));
        Assert.AreEqual(Color.blue, _object.GetColor("_FlatColor"));
    }

    [Test]
    public void Get_톤에_대응하는_머티리얼을_반환한다()
    {
        var palette = new XRayFlatPalette(_terrain, _background, _object);

        Assert.AreSame(_terrain, palette.Get(XRayTone.Terrain));
        Assert.AreSame(_background, palette.Get(XRayTone.Background));
        Assert.AreSame(_object, palette.Get(XRayTone.Object));
    }

    [Test]
    public void IsComplete_머티리얼이_하나라도_없으면_false()
    {
        var full = new XRayFlatPalette(_terrain, _background, _object);
        var partial = new XRayFlatPalette(_terrain, null, _object);

        Assert.IsTrue(full.IsComplete);
        Assert.IsFalse(partial.IsComplete);
    }

    [Test]
    public void Apply_머티리얼이_없어도_예외를_던지지_않는다()
    {
        var partial = new XRayFlatPalette(null, null, null);

        Assert.DoesNotThrow(() => partial.Apply(Color.red, Color.green, Color.blue));
        Assert.IsNull(partial.Get(XRayTone.Terrain));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

**사람이 직접** Unity Test Runner(EditMode)에서 `XRayFlatPaletteTests`를 실행한다.

기대: 컴파일 에러 — `XRayFlatPalette` / `XRayTone` 타입이 존재하지 않음.

> 프로젝트 규약상 이 확인은 게이트가 아니다. 확인이 어려우면 Step 3으로 진행한다.

- [ ] **Step 3: 최소 구현 작성**

`Assets/Scripts/Gameplay/Relics/Behaviours/XRayFlatPalette.cs`:

```csharp
using UnityEngine;

namespace Relic
{
    /// <summary>XRay 단색화의 3톤 구분.</summary>
    public enum XRayTone
    {
        Terrain,     // 안 판 지형 — 중간 톤
        Background,  // 판 굴 너머 배경 — 가장 어두운 톤
        Object       // 광물·특수블록·함정 — 가장 밝은 톤
    }

    // XRay 3톤 단색화용 공유 머티리얼 묶음.
    // XRayController에서 분리한 이유: 머티리얼 주입·조회는 순수 로직이라
    // MonoBehaviour 밖에 두면 EditMode 테스트가 가능하고, 컨트롤러는
    // "무엇을 스왑할지"에만 집중할 수 있다.
    //
    // 여기 담기는 머티리얼은 sharedMaterial로 스왑되는 '공유' 에셋이다.
    // 인스턴스를 만들지 않으므로 렌더러 수와 무관하게 3개만 존재한다.
    public class XRayFlatPalette
    {
        private static readonly int FlatColorID = Shader.PropertyToID("_FlatColor");

        private readonly Material _terrain;
        private readonly Material _background;
        private readonly Material _object;

        public XRayFlatPalette(Material terrain, Material background, Material obj)
        {
            _terrain = terrain;
            _background = background;
            _object = obj;
        }

        public bool IsComplete => _terrain != null && _background != null && _object != null;

        /// <summary>3톤 색을 각 머티리얼의 _FlatColor에 주입한다. 미할당 머티리얼은 조용히 건너뛴다.</summary>
        public void Apply(Color terrain, Color background, Color obj)
        {
            if (_terrain != null) _terrain.SetColor(FlatColorID, terrain);
            if (_background != null) _background.SetColor(FlatColorID, background);
            if (_object != null) _object.SetColor(FlatColorID, obj);
        }

        /// <summary>스왑에 사용할 공유 머티리얼. 미할당이면 null(호출처가 스왑을 건너뛴다).</summary>
        public Material Get(XRayTone tone)
        {
            switch (tone)
            {
                case XRayTone.Terrain: return _terrain;
                case XRayTone.Background: return _background;
                case XRayTone.Object: return _object;
                default: return null;
            }
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인**

**사람이 직접** Unity Test Runner(EditMode)에서 `XRayFlatPaletteTests`를 실행한다.

기대: 4개 테스트 모두 PASS.

- [ ] **Step 5: 체크인 시점**

사람이 UVCS로 체크인한다: "feat: XRayFlatPalette — 3톤 공유 머티리얼 주입/조회 + EditMode 테스트"

---

## Task 4: XRayController — 머티리얼 스왑과 3분류 수집

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Behaviours/XRayController.cs`

**Interfaces:**
- Consumes:
  - Task 2의 `BackgroundManager.ActiveTiles` (`IEnumerable<GameObject>`)
  - Task 3의 `XRayFlatPalette`, `XRayTone`
  - 기존 `InfinityMapManager.GetAllActiveChunks()` (`IEnumerable<TerrainChunk>`)
- Produces:
  - `public void ConfigureFlat(XRayFlatPalette palette, Color terrain, Color background, Color obj)` — Task 5의 `XRayRelic`이 호출한다.
  - 기존 `Configure` / `ConfigureTone` / `Begin` / `End` / `ForceOff` 시그니처는 변경하지 않는다.

- [ ] **Step 1: Entry에 원본 머티리얼 보관 필드 추가**

`XRayController.cs`의 `Entry` 구조체(44-51번 줄)를 교체한다:

```csharp
        private struct Entry
        {
            public SpriteRenderer sr;
            public int layerId;
            public int order;
            public Color color;
            public bool enabled;
            public Material material;   // 원본 sharedMaterial — 복원용
        }
```

- [ ] **Step 2: 팔레트 필드와 ConfigureFlat 추가**

`ConfigureTone` 메서드(63-68번 줄) 바로 아래에 추가한다:

```csharp
        // 3톤 단색화(설계: xray-flat-tone). 팔레트가 null이거나 불완전하면
        // 머티리얼 스왑을 통째로 건너뛰고 기존 정렬순서 투시만 동작한다.
        private XRayFlatPalette _palette;
        private Color _flatTerrain = new Color(0.22f, 0.30f, 0.34f, 1f);
        private Color _flatBackground = new Color(0.05f, 0.09f, 0.11f, 1f);
        private Color _flatObject = new Color(0.55f, 1f, 0.92f, 1f);

        public void ConfigureFlat(XRayFlatPalette palette, Color terrain, Color background, Color obj)
        {
            _palette = palette;
            _flatTerrain = terrain;
            _flatBackground = background;
            _flatObject = obj;
        }
```

- [ ] **Step 3: 리스캔 타이머를 지형/오브젝트로 분리**

기존 필드 선언(27-28번 줄)을 교체한다:

```csharp
        private float _rescanInterval = 0.4f;
        private float _rescanTimer;
```

교체 후:

```csharp
        // 오브젝트는 FindObjectsByType 비용이 있어 기존 주기를 유지한다.
        private float _rescanInterval = 0.4f;
        private float _rescanTimer;
        // 지형·배경은 매니저 컬렉션 순회라 저렴하다. 새로 로드된 청크가 원본 색으로
        // 남는 시간을 줄이려고 더 짧은 주기로 돈다.
        private float _worldRescanInterval = 0.15f;
        private float _worldRescanTimer;
```

- [ ] **Step 4: Begin에서 팔레트 색 주입 + 초기 스캔**

`Begin()` 메서드(70-79번 줄)를 교체한다:

```csharp
        public void Begin()
        {
            _active = true;
            _amountTarget = 1f;
            _rescanTimer = 0f;
            _worldRescanTimer = 0f;
            ApplyToneGlobals();
            _palette?.Apply(_flatTerrain, _flatBackground, _flatObject);
            Overlay?.SetDarknessOverride(0f); // 어둠막이 투시 대상을 가리지 않게
            ResolveSorting();
            ScanWorld();
            Scan();
        }
```

- [ ] **Step 5: Update에서 두 타이머를 각각 돌리고, 페이드 아웃 완료 시 복원**

`Update()` 메서드(107-121번 줄)를 교체한다:

```csharp
        private void Update()
        {
            _amount = Mathf.MoveTowards(_amount, _amountTarget, _fadeSpeed * Time.deltaTime);
            Shader.SetGlobalFloat(XRayAmountID, _amount);

            if (_active)
            {
                _rescanTimer -= Time.deltaTime;
                if (_rescanTimer <= 0f)
                {
                    _rescanTimer = _rescanInterval;
                    Scan(); // 발동 중 새로 로드된 청크의 오브젝트도 투시에 편입
                }

                _worldRescanTimer -= Time.deltaTime;
                if (_worldRescanTimer <= 0f)
                {
                    _worldRescanTimer = _worldRescanInterval;
                    ScanWorld(); // 새로 로드된 지형·배경도 단색으로 편입
                }
            }
            else if (_entries.Count > 0 && _amount <= 0f)
            {
                // 페이드 아웃이 '완료된 뒤'에 원복한다. 언릿 전환 때문에
                // amount>0 구간에서 머티리얼을 되돌리면 조명 있던 픽셀이 톡 튄다.
                RestoreAll();
            }
        }
```

- [ ] **Step 6: End에서 즉시 복원하지 않도록 변경**

`End()` 메서드(81-87번 줄)를 교체한다:

```csharp
        public void End()
        {
            _active = false;
            _amountTarget = 0f;
            Overlay?.ClearDarknessOverride();
            // RestoreAll은 Update가 페이드 아웃 완료를 확인한 뒤 호출한다(Step 5).
        }
```

`ForceOff()`(97-105번 줄)는 **그대로 둔다.** 즉시 원복이 목적이고 `_amount = 0f`를 직접 넣으므로 안전망 역할이 유지된다.

- [ ] **Step 7: ScanWorld — 지형·배경 수집**

`Scan()` 메서드(147-158번 줄) 바로 아래에 추가한다:

```csharp
        // 지형·배경은 화면 컬링하지 않는다. 카메라가 움직일 때 화면 가장자리에서
        // 원본 색이 번쩍이는 것을 막기 위함이다. FindObjectsByType을 쓰지 않아
        // 매니저 컬렉션 순회 비용만 든다.
        private void ScanWorld()
        {
            if (_palette == null) return;

            var map = InfinityMapManager.Instance;
            if (map != null)
            {
                foreach (var chunk in map.GetAllActiveChunks())
                {
                    if (chunk == null) continue;
                    // GetComponentsInChildren이므로 지형 테두리 렌더러(SpriteTerrainBorder 등
                    // 자식 SpriteRenderer)도 같이 잡힌다. 누락하면 단색 땅 위에
                    // 원본 색 테두리만 남아 지저분해진다.
                    SwapRenderers(chunk.gameObject, XRayTone.Terrain, forceEnable: false);
                }
            }

            var bg = FindFirstObjectByType<BackgroundManager>();
            if (bg != null)
            {
                foreach (var tile in bg.ActiveTiles)
                {
                    if (tile == null) continue;
                    SwapRenderers(tile, XRayTone.Background, forceEnable: false);
                }
            }
        }

        // 한 GameObject 트리의 SpriteRenderer를 지정 톤 머티리얼로 스왑하고
        // 원본 상태를 _entries에 적재한다. 이미 추적 중인 렌더러는 건너뛴다.
        private void SwapRenderers(GameObject root, XRayTone tone, bool forceEnable)
        {
            var mat = _palette?.Get(tone);
            var srs = root.GetComponentsInChildren<SpriteRenderer>(true);

            for (int i = 0; i < srs.Length; i++)
            {
                var sr = srs[i];
                if (sr == null || _tracked.Contains(sr)) continue;

                _tracked.Add(sr);
                _entries.Add(new Entry
                {
                    sr = sr,
                    layerId = sr.sortingLayerID,
                    order = sr.sortingOrder,
                    color = sr.color,
                    enabled = sr.enabled,
                    material = sr.sharedMaterial
                });

                // material이 아니라 sharedMaterial — 렌더러마다 인스턴스가 생기면 누수된다.
                if (mat != null) sr.sharedMaterial = mat;
                if (forceEnable) sr.enabled = true;
            }
        }
```

- [ ] **Step 8: AddTargets를 SwapRenderers 기반으로 정리**

`AddTargets<T>` 메서드(160-193번 줄)를 교체한다. 색 Lerp(`_highlightBlend`)를 제거하는 것이 핵심이다 — 단색은 이제 머티리얼이 담당한다.

```csharp
        private void AddTargets<T>(Bounds view) where T : Component
        {
            var comps = FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;

                Vector3 p = c.transform.position; p.z = view.center.z;
                if (!view.Contains(p)) continue;

                // 흙 속에서 렌더러가 꺼져 있던 경우가 있어 forceEnable.
                SwapRenderers(c.gameObject, XRayTone.Object, forceEnable: true);

                // 지형 위로 끌어올린다. SwapRenderers가 방금 적재한 항목만 손보면 되지만
                // 트리 전체를 다시 도는 편이 단순하고, 이미 _tracked에 있으므로 중복 적재는 없다.
                var srs = c.GetComponentsInChildren<SpriteRenderer>(true);
                for (int j = 0; j < srs.Length; j++)
                {
                    var sr = srs[j];
                    if (sr == null) continue;
                    sr.sortingLayerID = _revealSortingLayerId;
                    sr.sortingOrder = _revealSortingOrder;
                }
            }
        }
```

- [ ] **Step 9: RestoreAll에서 머티리얼도 복원**

`RestoreAll()` 메서드(195-208번 줄)를 교체한다:

```csharp
        private void RestoreAll()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.sr == null) continue;
                e.sr.sortingLayerID = e.layerId;
                e.sr.sortingOrder = e.order;
                e.sr.color = e.color;
                e.sr.enabled = e.enabled;
                e.sr.sharedMaterial = e.material;
            }
            _entries.Clear();
            _tracked.Clear();
        }
```

- [ ] **Step 10: 클래스 상단 주석 갱신**

파일 상단 주석(6-11번 줄)의 2번 항목을 교체한다:

```csharp
    //  2) 화면 내 지형·배경·오브젝트의 SpriteRenderer를 단색 머티리얼 3종으로 스왑(XRayFlatPalette)
    //     → 광물(밝음)/지형(중간)/배경(어두움) 3톤. 지형 알파가 곧 '안 판 땅' 실루엣이라
    //     판 굴은 자동으로 뚫려 배경 톤이 보인다. 오브젝트는 정렬순서도 지형 위로 올린다.
```

- [ ] **Step 11: 컴파일 확인**

Unity 에디터로 포커스를 옮겨 컴파일을 기다린다.

기대: 콘솔에 컴파일 에러 없음. `InfinityMapManager.Instance`가 존재하지 않는다는 에러가 나면, `InfinityMapManager.cs`에서 실제 싱글턴 접근자 이름을 확인해 맞춘다(없으면 `FindFirstObjectByType<InfinityMapManager>()`로 대체).

- [ ] **Step 12: 체크인 시점**

사람이 UVCS로 체크인한다: "feat: XRayController — 지형/배경/오브젝트 3톤 머티리얼 스왑"

---

## Task 5: XRayRelic — 3톤 색 인스펙터 노출

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Behaviours/XRayRelic.cs`

**Interfaces:**
- Consumes: Task 3의 `XRayFlatPalette`, Task 4의 `XRayController.ConfigureFlat`
- Produces: 인스펙터에서 3톤 색과 머티리얼 3종을 지정할 수 있는 유물 설정

- [ ] **Step 1: 3톤 필드 추가**

`XRayRelic.cs`의 "화면 톤" 블록(21-24번 줄) 아래에 추가한다:

```csharp
        [Header("3톤 단색화")]
        [SerializeField] private Material flatTerrainMat;     // XRayFlat_Terrain.mat
        [SerializeField] private Material flatBackgroundMat;  // XRayFlat_Background.mat
        [SerializeField] private Material flatObjectMat;      // XRayFlat_Object.mat
        [SerializeField] private Color flatTerrainColor = new Color(0.22f, 0.30f, 0.34f, 1f);    // 중간
        [SerializeField] private Color flatBackgroundColor = new Color(0.05f, 0.09f, 0.11f, 1f); // 가장 어둡게
        [SerializeField] private Color flatObjectColor = new Color(0.55f, 1f, 0.92f, 1f);        // 가장 밝게
```

- [ ] **Step 2: EnsureController에서 팔레트 주입**

`EnsureController()` 메서드(60-67번 줄)를 교체한다:

```csharp
        private void EnsureController()
        {
            if (_controller != null) return;
            var go = new GameObject("XRayController");
            _controller = go.AddComponent<XRayController>();
            _controller.Configure(highlightColor, highlightBlend, fadeSpeed, cameraPadding);
            _controller.ConfigureTone(toneTint, toneDim, toneHighlightCut);

            // 머티리얼이 하나라도 비어 있으면 팔레트가 불완전하다.
            // 컨트롤러가 스왑을 건너뛰고 기존 정렬순서 투시만 하므로 유물은 계속 동작한다.
            var palette = new XRayFlatPalette(flatTerrainMat, flatBackgroundMat, flatObjectMat);
            if (!palette.IsComplete)
            {
                Debug.LogWarning("[XRayRelic] 3톤 머티리얼이 지정되지 않아 단색화를 건너뜁니다. " +
                                 "유물 에셋 인스펙터에서 XRayFlat_* 머티리얼 3종을 연결하세요.");
            }
            _controller.ConfigureFlat(palette, flatTerrainColor, flatBackgroundColor, flatObjectColor);
        }
```

- [ ] **Step 3: 유물 에셋에 머티리얼 연결**

Unity 에디터에서 XRay 유물 데이터 에셋을 찾아(`RelicID`에서 XRay 항목을 참조하는 SO) 인스펙터의 "3톤 단색화" 항목에 Task 1에서 만든 머티리얼 3개를 드래그해 연결한다.

기대: 세 슬롯이 모두 채워지고, 플레이 시 Step 2의 경고 로그가 뜨지 않는다.

- [ ] **Step 4: 체크인 시점**

사람이 UVCS로 체크인한다: "feat: XRayRelic — 3톤 색·머티리얼 인스펙터 노출"

---

## Task 6: 풀스크린 XRay 셰이더 역할 축소

**Files:**
- Modify: `Assets/Shaders/XRay.shader`

**Interfaces:**
- Consumes: 전역 `_XRayAmount`, `_XRayDim` (기존)
- Produces: 없음. `XRayRendererFeature.cs`는 수정하지 않는다.

기존 로직(휘도 그레이스케일 → 청록 틴트 → 감광)을 그대로 두면 Task 1~5에서 만든 3톤을 다시 뭉개 2톤처럼 만든다. 톤 결정은 전부 머티리얼로 넘기고, 풀스크린은 분위기만 담당한다.

`_XRayTintColor` / `_XRayHighlightCut`은 셰이더에서 더 이상 쓰지 않지만, `XRayController.ApplyToneGlobals()`가 계속 세팅해도 무해하다(전역 프로퍼티는 소비처가 없으면 무시된다). 컨트롤러 쪽은 건드리지 않는다.

- [ ] **Step 1: 셰이더 본문 교체**

`Assets/Shaders/XRay.shader` 전체를 교체한다:

```hlsl
// 엑스레이 유물 — 풀스크린 분위기 패스.
// 전역 _XRayAmount(0..1)로 원본↔효과 블렌드. URP Blitter 풀스크린 규약 사용.
//
// 톤(색) 결정은 이 패스가 하지 않는다. 지형·배경·오브젝트를 각각 단색으로 칠하는 일은
// Custom/XRayFlat 머티리얼 스왑(XRayController)이 담당한다. 여기서 다시 그레이스케일·
// 틴트를 합성하면 애써 분리한 3톤이 뭉개져 2톤처럼 보인다.
// 그래서 이 패스는 전체 감광 + 가장자리 비네트만 남긴다.
Shader "Custom/XRay"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "XRayVignette"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _XRayAmount;   // 0..1 페이드
            float _XRayDim;      // 전체 감광 배수(1 = 감광 없음)

            half4 Frag(Varyings input) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                // 화면 중심에서 멀어질수록 어두워지는 비네트.
                float2 d = input.texcoord - 0.5;
                float  r = saturate(length(d) * 1.6);
                half   vig = 1.0h - smoothstep(0.45h, 1.0h, r) * 0.75h;

                half3 fx = col.rgb * _XRayDim * vig;
                col.rgb = lerp(col.rgb, fx, saturate(_XRayAmount));
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
```

- [ ] **Step 2: 컴파일 확인**

Unity 에디터 콘솔을 확인한다.

기대: 셰이더 컴파일 에러 없음. `XRayRendererFeature`가 `Custom/XRay`를 계속 찾을 수 있으므로 "셰이더를 찾을 수 없습니다" 경고도 없어야 한다.

- [ ] **Step 3: 체크인 시점**

사람이 UVCS로 체크인한다: "refactor: XRay 풀스크린 패스를 감광+비네트로 축소(톤은 머티리얼이 담당)"

---

## Task 7: 플레이 모드 검증과 톤 밸런싱

**Files:**
- Modify (필요 시): XRay 유물 에셋의 3톤 색 값

**Interfaces:**
- Consumes: Task 1~6 전부
- Produces: 없음 (검증 태스크)

- [ ] **Step 1: 검증 항목 실행**

**사람이 직접** 플레이 모드에서 XRay 유물을 발동하고 아래를 확인한다. 설계 문서의 검증 목록과 동일하다.

1. 지형·배경·광물이 각각 지정한 3톤으로 보인다.
2. 판 굴 실루엣이 배경 톤으로 뚫려 보인다.
3. 흙 속에 묻힌 광물이 지형 위로 떠올라 밝은 톤으로 보인다.
4. 페이드 인/아웃이 부드럽고, **종료 시점에 색이 튀지 않는다.**
5. 발동 중 새로 로드된 청크가 즉시(0.15초 이내) 단색으로 편입된다.
6. 발동 중 씬 전환·유물 해제 시 지형이 단색으로 굳지 않는다.
7. 배경 타일의 `Tiled` drawMode가 깨지지 않는다(타일이 늘어나거나 잘리지 않음).

- [ ] **Step 2: 증상별 대처**

| 증상 | 원인 | 대처 |
|---|---|---|
| 배경 타일이 늘어나거나 잘림 | `Tiled` drawMode가 셰이더와 안 맞음 | `XRayFlat.shader`의 Vert에서 `TRANSFORM_TEX` 대신 `input.uv`를 그대로 사용해 본다 |
| 지형이 통짜로 칠해져 굴이 안 보임 | 지형 스프라이트가 불투명(알파 1) | 알파 대신 밝기 문턱으로 실루엣을 뽑아야 한다 — Frag에서 `col.a` 자리에 `step(0.01, luminance)` 적용을 검토 |
| 단색 땅에 원본 색 테두리가 남음 | 테두리 렌더러가 청크 자식이 아님 | 해당 렌더러의 소유 오브젝트를 찾아 `ScanWorld`에서 `Terrain` 톤으로 추가 스왑 |
| 종료 시 색이 톡 튐 | 언릿↔라이팅 전환 차이 | `_flatObject` 등의 색을 원본 평균 밝기에 가깝게 낮춘다 |
| 광물이 배경에 묻힘 | 톤 대비 부족 | 인스펙터에서 `flatObjectColor`를 밝게, `flatBackgroundColor`를 더 어둡게 |

- [ ] **Step 3: 톤 밸런싱**

유물 에셋 인스펙터에서 3색을 조정해 광물 > 지형 > 배경 순으로 밝기 대비가 확실해지도록 맞춘다. 코드 수정 없이 인스펙터 값만 바꾼다.

- [ ] **Step 4: 체크인 시점**

사람이 UVCS로 체크인한다: "chore: XRay 3톤 밸런싱 값 조정"

---

## Self-Review 결과

**스펙 커버리지:** 설계 문서의 컴포넌트 4개(플랫 셰이더 / 머티리얼 3종 / XRayController 확장 / 풀스크린 축소)가 각각 Task 1, 1, 4, 6에 대응한다. `BackgroundManager` 접근자는 Task 2, 인스펙터 색 주입은 Task 5, 검증 항목 7개는 Task 7에 있다. 누락 없음.

**타입 일관성:** `XRayTone` / `XRayFlatPalette.Get` / `Apply` / `IsComplete` / `ConfigureFlat` / `ActiveTiles` / `SwapRenderers`의 이름과 시그니처가 Task 3·4·5에서 일치한다. `_FlatColor` 프로퍼티 이름이 셰이더(Task 1)와 팔레트(Task 3)에서 일치한다.

**알려진 불확실성 2건** (Task 7의 대처표에 반영):
- 지형 스프라이트가 불투명하면 알파 기반 실루엣이 안 나온다. 지형은 파인 픽셀이 투명해지는 구조라 알파가 있을 가능성이 높지만, 플레이 모드에서 확인해야 확정된다.
- `SpriteDrawMode.Tiled`와 커스텀 셰이더의 UV 처리 궁합은 실행해 봐야 안다.

둘 다 코드 구조가 아니라 값·UV 처리 수준의 조정으로 해결되므로 태스크 분해에는 영향이 없다.
