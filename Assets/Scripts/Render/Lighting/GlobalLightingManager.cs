using UnityEngine;

/// <summary>
/// 글로벌 조명 관리자 - URP 17 호환 버전
///
/// [변경 이유]
/// OnRenderImage()는 URP에서 동작하지 않음 (Legacy 렌더러 전용).
/// URP에서 후처리는 ScriptableRendererFeature를 사용해야 함.
/// → PixelateLightRendererFeature (Renderer2D.asset에서 추가) 로 대체.
///
/// 이 클래스는 싱글톤 참조 유지 목적으로만 존재.
/// 실제 조명 픽셀화는 PixelateLightRendererFeature에서 처리.
/// </summary>
public class GlobalLightingManager : MonoBehaviour
{
    public static GlobalLightingManager Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }
}
