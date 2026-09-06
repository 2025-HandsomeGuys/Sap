// @tags: ui, effect, skew, slant, 사선, bar, gauge, drill, battery, mesh
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI Graphic(Image 등)의 정점을 X축으로 기울여 평행사변형(사선) 게이지를 만드는 메시 이펙트.
/// 스프라이트는 직사각형 그대로 두고 렌더링 정점만 기울이므로,
/// 게이지 폭이 줄어들 때 앞쪽 끝도 항상 같은 각도의 사선으로 잘린다.
/// RectTransform의 rect 자체는 직사각형으로 유지된다(레이아웃 계산에 영향 없음).
/// </summary>
[AddComponentMenu("UI/Effects/Skewed Bar Effect")]
[RequireComponent(typeof(Graphic))]
public class SkewedBarEffect : BaseMeshEffect
{
    [Tooltip("위쪽 모서리를 오른쪽(+)/왼쪽(-)으로 미는 픽셀 수. 프레임 스프라이트의 사선 기울기에 맞춘다.")]
    public float skewX = 8f;

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || Mathf.Approximately(skewX, 0f)) return;

        Rect rect = graphic.rectTransform.rect;
        if (rect.height <= 0f) return;

        UIVertex vert = default;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vert, i);
            float t = (vert.position.y - rect.yMin) / rect.height; // 0(아래) ~ 1(위)
            vert.position.x += skewX * t;
            vh.SetUIVertex(vert, i);
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (graphic != null) graphic.SetVerticesDirty();
    }
#endif
}
