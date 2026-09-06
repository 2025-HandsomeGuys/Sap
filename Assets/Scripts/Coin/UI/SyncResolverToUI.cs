using UnityEngine;
using UnityEngine.UI; // UI Image를 제어하기 위해 필수

public class SyncResolverToUI : MonoBehaviour
{
    [Header("원본 (image_d72469.png 의 오브젝트)")]
    public SpriteRenderer sourceRenderer;

    [Header("적용할 UI (캔버스의 Image)")]
    public Image targetUIImage;

    void Update()
    {
        // 원본에 Sprite가 할당되어 있다면
        if (sourceRenderer != null && sourceRenderer.sprite != null)
        {
            // Sprite Resolver에 의해 변경된 현재 스프라이트를 UI Image에 실시간으로 덮어씌웁니다.
            targetUIImage.sprite = sourceRenderer.sprite;
        }
    }
}