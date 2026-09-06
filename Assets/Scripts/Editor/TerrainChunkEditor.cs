using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

// 이 스크립트는 'TerrainChunk' 컴포넌트의 인스펙터를 확장합니다.
[CustomEditor(typeof(TerrainChunk))]
public class TerrainChunkEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 1. 기존 인스펙터 UI를 그대로 그립니다.
        DrawDefaultInspector();

        TerrainChunk chunk = (TerrainChunk)target;

        // 2. 구분선 추가
        EditorGUILayout.Space(20);
        EditorGUILayout.LabelField("Static Chunk Tools", EditorStyles.boldLabel);

        // 3. 버튼 생성
        if (GUILayout.Button("Generate Static Collider (From Image)", GUILayout.Height(40)))
        {
            GenerateStaticCollider(chunk);
        }
    }

    private void GenerateStaticCollider(TerrainChunk chunk)
    {
        // Undo 등록 (Ctrl+Z 가능하게)
        Undo.RecordObject(chunk.gameObject, "Generate Static Collider");

        // 1. 소스 이미지 찾기 (Predesigned > SpriteRenderer 순서)
        // Note: TerrainChunk에 predesignedDesign 필드가 없다면 SpriteRenderer를 우선 사용하도록 합니다.
        // 사용자가 제공한 코드에 predesignedDesign이 있지만 현재 TerrainChunk.cs에는 PredefinedShape가 있는지 확인 필요.
        // 현재 TerrainChunk.cs에는 'isStaticSpecialChunk'를 사용하며 텍스처 필드는 제거했으므로 SpriteRenderer를 메인으로 씁니다.
        
        Texture2D sourceTexture = null;
        Sprite sourceSprite = null;

        var sr = chunk.GetComponent<SpriteRenderer>();
        if (sr != null) sourceSprite = sr.sprite;
        if (sourceSprite != null) sourceTexture = sourceSprite.texture;

        if (sourceTexture == null)
        {
            EditorUtility.DisplayDialog("Error", "텍스처를 찾을 수 없습니다.\nSpriteRenderer에 이미지를 할당하세요.", "OK");
            return;
        }

        // 2. 콜라이더 컴포넌트 가져오기 (없으면 생성)
        PolygonCollider2D polyCollider = chunk.GetComponent<PolygonCollider2D>();
        if (polyCollider == null)
        {
            polyCollider = Undo.AddComponent<PolygonCollider2D>(chunk.gameObject);
        }

        // 3. 텍스처 경로 가져오기 (TextureImporter 활용)
        string assetPath = AssetDatabase.GetAssetPath(sourceTexture);
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (importer == null)
        {
            // 런타임에 생성된 텍스처 등 에셋이 아닌 경우 처리 곤란
            EditorUtility.DisplayDialog("Error", "텍스처 임포터를 가져올 수 없습니다. 원본 에셋이 아닌 런타임 텍스처인가요?", "OK");
            return;
        }

        // 4. 설정을 잠시 변경하여 물리 모양 데이터 생성
        bool originalReadable = importer.isReadable;
        
        // Sprite로 설정되어 있어야 Physics Shape를 따올 수 있습니다.
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
        }

        // 물리 모양 생성 옵션 켜기
        var settings = importer.GetPlatformTextureSettings("Default");
        importer.isReadable = true; // 픽셀 읽기 허용
        
        // 변경사항 적용 (재임포트)
        importer.SaveAndReimport();

        try
        {
            // ★ 핵심: Unity 내부 기능을 이용해 스프라이트의 물리 모양(Physics Shape)을 가져옴
            // 만약 sourceSprite와 TextureImporter가 처리한 Sprite가 다를 수 있음(Multiple Sprite 모드 등)
            // 여기서는 Single Sprite라고 가정하거나, 해당 Sprite Asset을 다시 로드해야 함
            
            // Re-load the sprite from the asset path to ensure we get the updated physics shape
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            Sprite updatedSprite = null;
            
            foreach (var asset in assets)
            {
                if (asset is Sprite s && s.name == sourceSprite.name)
                {
                    updatedSprite = s;
                    break;
                }
            }
            
            if (updatedSprite == null)
            {
                // Fallback: If not finding by name (e.g. name changed or Single mode), just take the first sprite
                updatedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            }

            if (updatedSprite != null)
            {
                // 스프라이트의 물리 모양 데이터 추출
                int pathCount = updatedSprite.GetPhysicsShapeCount();
                List<Vector2> pathPoints = new List<Vector2>();
                polyCollider.pathCount = pathCount;

                for (int i = 0; i < pathCount; i++)
                {
                    updatedSprite.GetPhysicsShape(i, pathPoints);
                    
                    // PPU scaling is usually handled by the sprite itself in usage, but SetPath takes local coords.
                    // PhysicsShape returns local coords relative to sprite pivot?
                    // According to docs, GetPhysicsShape returns points in the sprite's coordinate system.
                    // If the PolygonCollider2D is on the same object as the SpriteRenderer, they should align if scale is 1.
                    
                    polyCollider.SetPath(i, pathPoints.ToArray());
                }

                Debug.Log($"[TerrainChunkEditor] 콜라이더 생성 완료! (Paths: {pathCount})");
            }
            else
            {
                Debug.LogError("Could not load updated sprite asset.");
            }
            
            // 프리팹 변경사항 저장 알림
            EditorUtility.SetDirty(chunk);
            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
            }
        }
        finally
        {
            // 5. 설정 복구 (원한다면)
            // 보통은 Sprite 설정이 유지되는 게 좋으므로 그대로 둡니다.
            /*
            importer.isReadable = originalReadable;
            importer.SaveAndReimport();
            */
        }
    }
}
