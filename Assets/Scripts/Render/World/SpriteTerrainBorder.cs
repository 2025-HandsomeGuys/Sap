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

    [Tooltip("원본(un-baked) 스프라이트. 비우면 최초 베이크 시 현재 스프라이트를 자동 캡처한다. " +
             "재베이크 시 이 원본을 기준으로 구워 테두리 중첩을 막는다")]
    [SerializeField] private Sprite sourceSprite;

    private void Start()
    {
        BakeNow();
    }

    /// <summary>
    /// 원본 스프라이트를 테두리로 구워 SpriteRenderer 에 반영한다.
    /// 최초 호출 시 현재 스프라이트를 원본(sourceSprite)으로 캡처하므로 반복 호출해도 테두리가 중첩되지 않는다.
    /// 컴포넌트 우클릭 → "테두리 미리 굽기" 로 에디트 모드에서도 미리볼 수 있다.
    /// ⚠ 에디트 모드 결과는 임시 Sprite/Texture 다 — 씬 재오픈·스크립트 리컴파일 시 사라진다(원본은 유지).
    /// </summary>
    [ContextMenu("테두리 미리 굽기")]
    public void BakeNow()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr == null || borderTexture == null)
        {
            Debug.LogWarning($"[SpriteTerrainBorder] {name}: SpriteRenderer 또는 borderTexture 미설정 — 베이크 스킵.");
            return;
        }

        // 최초 1회 원본 캡처 — 재베이크가 이미 구워진 스프라이트를 다시 굽지 않도록 한다.
        if (sourceSprite == null) sourceSprite = sr.sprite;
        if (sourceSprite == null)
        {
            Debug.LogWarning($"[SpriteTerrainBorder] {name}: 원본 스프라이트가 없습니다 — 베이크 스킵.");
            return;
        }

        var settings = new SpriteBorderBaker.Settings
        {
            textureThickness = textureThickness,
            borderPixelsPerUnit = borderPixelsPerUnit,
        };

        Sprite baked = SpriteBorderBaker.Bake(sourceSprite, borderTexture, settings);
        if (baked != null) sr.sprite = baked;
    }

    /// <summary>미리보기를 원본 스프라이트로 되돌린다.</summary>
    [ContextMenu("원본으로 되돌리기")]
    public void RevertToOriginal()
    {
        if (sourceSprite == null) return;
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.sprite = sourceSprite;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 원본 스프라이트를 테두리로 구운 결과를 PNG 파일로 원본 텍스처 옆에 저장한다.
    /// 런타임 교체(<see cref="BakeNow"/>)와 달리 결과가 프로젝트 에셋으로 영속된다
    /// (씬 재오픈·리컴파일에도 유지). 컴포넌트 우클릭 → "테두리 굽고 PNG 복사본 저장".
    /// 파일명은 "<원본>_bordered.png" (충돌 시 자동 넘버링).
    /// </summary>
    [ContextMenu("테두리 굽고 PNG 복사본 저장")]
    public void BakeAndExportPng()
    {
        if (borderTexture == null)
        {
            Debug.LogWarning($"[SpriteTerrainBorder] {name}: borderTexture 미설정 — PNG 내보내기 스킵.");
            return;
        }

        var sr = GetComponent<SpriteRenderer>();
        Sprite src = sourceSprite != null ? sourceSprite : (sr != null ? sr.sprite : null);
        if (src == null || src.texture == null)
        {
            Debug.LogWarning($"[SpriteTerrainBorder] {name}: 원본 스프라이트가 없습니다 — PNG 내보내기 스킵.");
            return;
        }

        string srcPath = UnityEditor.AssetDatabase.GetAssetPath(src.texture);
        if (string.IsNullOrEmpty(srcPath))
        {
            Debug.LogWarning($"[SpriteTerrainBorder] {name}: 원본 텍스처가 프로젝트 에셋이 아니어서 저장 위치를 정할 수 없습니다.");
            return;
        }

        // 베이커는 GetPixels 를 쓰므로 원본/테두리 텍스처가 Read/Write 여야 한다.
        // 꺼져 있으면 임시로 켰다가 완료 후 원복한다.
        var srcState = PushReadable(src.texture);
        var borderState = PushReadable(borderTexture);

        Sprite baked;
        try
        {
            var settings = new SpriteBorderBaker.Settings
            {
                textureThickness = textureThickness,
                borderPixelsPerUnit = borderPixelsPerUnit,
            };
            baked = SpriteBorderBaker.Bake(src, borderTexture, settings);
        }
        finally
        {
            PopReadable(srcState);
            PopReadable(borderState);
        }

        if (baked == null || baked.texture == null)
        {
            Debug.LogWarning($"[SpriteTerrainBorder] {name}: 베이크 실패 — PNG 내보내기 스킵.");
            return;
        }

        byte[] png = baked.texture.EncodeToPNG();
        if (png == null || png.Length == 0)
        {
            Debug.LogWarning($"[SpriteTerrainBorder] {name}: PNG 인코딩 결과가 비었습니다 — 저장 스킵.");
            return;
        }

        string dir = System.IO.Path.GetDirectoryName(srcPath);
        string baseName = System.IO.Path.GetFileNameWithoutExtension(srcPath);
        string outPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{dir}/{baseName}_bordered.png");

        System.IO.File.WriteAllBytes(outPath, png);
        UnityEditor.AssetDatabase.ImportAsset(outPath, UnityEditor.ImportAssetOptions.ForceSynchronousImport);

        // 복사본을 원본과 동일한 Sprite import 설정으로 맞춰 바로 사용 가능하게 한다.
        if (UnityEditor.AssetImporter.GetAtPath(outPath) is UnityEditor.TextureImporter outImporter)
        {
            outImporter.textureType = UnityEditor.TextureImporterType.Sprite;
            outImporter.spritePixelsPerUnit = src.pixelsPerUnit;
            outImporter.filterMode = src.texture.filterMode;
            outImporter.SaveAndReimport();
        }

        Debug.Log($"[SpriteTerrainBorder] {name}: 테두리 PNG 복사본 저장 → {outPath}");
        UnityEditor.Selection.activeObject = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(outPath);
    }

    private readonly struct ReadableState
    {
        public readonly UnityEditor.TextureImporter Importer;
        public readonly bool WasReadable;
        public ReadableState(UnityEditor.TextureImporter importer, bool wasReadable)
        {
            Importer = importer;
            WasReadable = wasReadable;
        }
    }

    /// <summary>텍스처가 읽기 불가면 임시로 Read/Write 를 켠다. 원복용 상태를 반환.</summary>
    private static ReadableState PushReadable(Texture2D tex)
    {
        if (tex == null || tex.isReadable) return default;
        string p = UnityEditor.AssetDatabase.GetAssetPath(tex);
        if (UnityEditor.AssetImporter.GetAtPath(p) is UnityEditor.TextureImporter importer && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
            return new ReadableState(importer, false);
        }
        return default;
    }

    /// <summary>PushReadable 로 변경한 Read/Write 설정을 원래대로 되돌린다.</summary>
    private static void PopReadable(ReadableState state)
    {
        if (state.Importer == null) return;
        state.Importer.isReadable = state.WasReadable;
        state.Importer.SaveAndReimport();
    }
#endif
}
