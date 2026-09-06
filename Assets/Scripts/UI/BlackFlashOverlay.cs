using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 흑섬(도박꾼의 안경 잭팟) 화면 오버레이.
/// 진입 순간 검은 섬광 → 지속 동안 은은한 다크+골드 비네트 펄스 → 페이드아웃.
///
/// 씬 세팅이 필요 없다(유물 링과 동일한 "코드 생성, 프리팹 불필요" 철학).
/// Canvas·Image·비네트 텍스처를 최초 호출 시 런타임 생성한다.
/// 사용: BlackFlashOverlay.Play(durationSeconds)
/// </summary>
public class BlackFlashOverlay : MonoBehaviour
{
    private static BlackFlashOverlay _instance;

    private CanvasGroup _group;
    private Image _vignette;   // 가장자리 어둡고 중앙은 투명(시야 확보)
    private Image _flash;      // 진입 순간 전체 검은 섬광
    private Coroutine _co;

    // 연출 파라미터
    private const float OnsetTime   = 0.22f;  // 검은 섬광 진입
    private const float FadeOutTime = 0.8f;   // 종료 페이드
    private const float VignetteSustainAlpha = 0.9f;
    private static readonly Color DarkBase = new Color(0.02f, 0.02f, 0.04f, 1f); // 흑
    private static readonly Color GoldTint = new Color(0.55f, 0.42f, 0.12f, 1f); // 잭팟 골드

    /// <summary>흑섬 오버레이를 duration초 동안 재생한다.</summary>
    public static void Play(float duration)
    {
        Ensure();
        if (_instance != null) _instance.PlayInternal(duration);
    }

    private static void Ensure()
    {
        if (_instance != null) return; // 파괴된 인스턴스는 Unity의 == 오버로드로 null 판정
        var go = new GameObject("BlackFlashOverlay");
        _instance = go.AddComponent<BlackFlashOverlay>();
        _instance.Build();
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // 게임 위, 대부분 UI 위(입력은 통과)

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        _vignette = CreateFullScreenImage("Vignette", BuildVignetteSprite());
        _vignette.color = new Color(DarkBase.r, DarkBase.g, DarkBase.b, 0f);

        _flash = CreateFullScreenImage("Flash", BuildSolidSprite());
        _flash.color = new Color(0f, 0f, 0f, 0f);

        gameObject.SetActive(true);
    }

    private Image CreateFullScreenImage(string name, Sprite sprite)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    private void PlayInternal(float duration)
    {
        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(DoPlay(duration));
    }

    private IEnumerator DoPlay(float duration)
    {
        _group.alpha = 1f;
        float sustain = Mathf.Max(0f, duration - OnsetTime - FadeOutTime);

        // ── 진입: 검은 섬광(빠르게 확 어두워졌다 반쯤 풀림) + 비네트 상승 ──
        for (float t = 0f; t < OnsetTime; t += Time.deltaTime)
        {
            float k = t / OnsetTime;
            // 섬광: 앞 25%에서 0→0.85, 이후 0.85→0.12로 풀림
            float flashA = k < 0.25f ? Mathf.Lerp(0f, 0.85f, k / 0.25f)
                                     : Mathf.Lerp(0.85f, 0.12f, (k - 0.25f) / 0.75f);
            SetFlashAlpha(flashA);
            SetVignette(Mathf.Lerp(0f, VignetteSustainAlpha, k), 0f);
            yield return null;
        }
        SetFlashAlpha(0.12f);

        // ── 지속: 다크↔골드 비네트 펄스 + 잔여 섬광 소거 ──
        float elapsed = 0f;
        while (elapsed < sustain)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(elapsed * 3.2f);           // 0~1
            float vA = Mathf.Lerp(0.72f, VignetteSustainAlpha, pulse);
            SetVignette(vA, pulse * 0.6f);                                    // 골드 혼합량
            SetFlashAlpha(Mathf.Lerp(0.12f, 0f, Mathf.Clamp01(elapsed / 0.5f)));
            elapsed += Time.deltaTime;
            yield return null;
        }

        // ── 종료: 전체 페이드아웃 ──
        float startV = _vignette.color.a;
        for (float t = 0f; t < FadeOutTime; t += Time.deltaTime)
        {
            float k = t / FadeOutTime;
            SetVignette(Mathf.Lerp(startV, 0f, k), 0.3f * (1f - k));
            SetFlashAlpha(0f);
            yield return null;
        }

        SetVignette(0f, 0f);
        SetFlashAlpha(0f);
        _group.alpha = 0f;
        _co = null;
    }

    private void SetFlashAlpha(float a)
    {
        var c = _flash.color; c.a = a; _flash.color = c;
    }

    // 비네트 색 = 다크 기반에 골드를 goldMix만큼 섞고 알파 적용.
    private void SetVignette(float alpha, float goldMix)
    {
        Color rgb = Color.Lerp(DarkBase, GoldTint, Mathf.Clamp01(goldMix));
        _vignette.color = new Color(rgb.r, rgb.g, rgb.b, alpha);
    }

    // 중앙 투명 → 가장자리 불투명 방사형 비네트 스프라이트(런타임 생성).
    private static Sprite BuildVignetteSprite()
    {
        const int size = 256;
        const float inner = 0.45f; // 이 반경(정규화)까지는 완전 투명
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        Vector2 c = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxD = c.magnitude;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / maxD; // 0(중앙)~1(모서리)
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, 1f, d));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite BuildSolidSprite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
    }
}
