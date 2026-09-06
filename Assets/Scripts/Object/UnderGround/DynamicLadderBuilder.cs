using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class DynamicLadderBuilder : MonoBehaviour
{
    [Header("Sprite Parts (스프라이트 구성 요소)")]
    public Sprite topCapSprite;
    public Sprite midSectionSprite;
    public Sprite bottomCapSprite;

    [Header("Settings (설정)")]
    [Tooltip("중앙 섹션(Mid Section)의 반복 횟수입니다.")]
    [Min(0)]
    public int lengthSetting = 2;

    [Tooltip("맨 위/맨 아래 마감 부분과 중앙 부분 사이의 겹침 정도(간격)를 조절합니다.")]
    public float capToMidAdjustment = 0f;

    [Tooltip("중앙 부분과 중앙 부분 사이의 겹침 정도(간격)를 조절합니다.")]
    public float midToMidAdjustment = 0f;

    [Header("Sorting")]
    public int sortingOrder = 0;

    private const string LADDER_PART_TAG = "__GeneratedLadderPart__";

    private void OnValidate()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= SafeBuild;
        EditorApplication.delayCall += SafeBuild;
#else
        SafeBuild();
#endif
    }

    private void SafeBuild()
    {
        if (this == null) return;

        if (topCapSprite == null || midSectionSprite == null || bottomCapSprite == null) return;
        BuildLadder();
    }

    private void BuildLadder()
    {
        ClearLadder();

        // 각 스프라이트의 실제 월드 단위 높이 계산
        float topH = topCapSprite.rect.height / topCapSprite.pixelsPerUnit;
        float midH = midSectionSprite.rect.height / midSectionSprite.pixelsPerUnit;
        float botH = bottomCapSprite.rect.height / bottomCapSprite.pixelsPerUnit;

        float currentY = 0f;

        // 1. Top Cap 생성
        CreatePart("TopCap", topCapSprite, new Vector3(0, currentY, 0));

        // 2. Mid Sections 생성
        for (int i = 0; i < lengthSetting; i++)
        {
            float prevHeight = (i == 0) ? topH : midH;
            float currentAdjustment = (i == 0) ? capToMidAdjustment : midToMidAdjustment;

            currentY -= (prevHeight / 2f) + (midH / 2f) - currentAdjustment;

            CreatePart($"MidSection_{i}", midSectionSprite, new Vector3(0, currentY, 0));
        }

        // 3. Bottom Cap 생성
        float lastPrevHeight = (lengthSetting == 0) ? topH : midH;
        currentY -= (lastPrevHeight / 2f) + (botH / 2f) - capToMidAdjustment;

        CreatePart("BottomCap", bottomCapSprite, new Vector3(0, currentY, 0));

        // ★ 4. 콜라이더 업데이트 호출
        UpdateCollider(currentY, topH, botH);
    }

    private GameObject CreatePart(string name, Sprite sprite, Vector3 position)
    {
        GameObject part = new GameObject(name);
        part.transform.SetParent(this.transform);
        part.transform.localPosition = position;

        SpriteRenderer renderer = part.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;

        part.name = $"{part.name}_{LADDER_PART_TAG}";
        return part;
    }

    // ★ 새롭게 추가된 콜라이더 업데이트 메서드
    private void UpdateCollider(float finalY, float topH, float botH)
    {
        // 부모 오브젝트에 BoxCollider2D가 있는지 확인하고 없으면 추가
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col == null)
        {
            col = gameObject.AddComponent<BoxCollider2D>();
        }

        // 트리거 모드 활성화
        col.isTrigger = true;

        // 중앙 섹션의 너비를 가져와서 콜라이더의 가로 크기로 사용
        float midW = midSectionSprite.rect.width / midSectionSprite.pixelsPerUnit;

        // 사다리의 맨 위쪽 끝과 맨 아래쪽 끝의 Y 좌표 계산 (스프라이트 피벗이 Center라고 가정)
        float topEdgeY = topH / 2f;
        float bottomEdgeY = finalY - (botH / 2f);

        // 콜라이더의 전체 세로 길이와 중심점(Offset) 계산
        float sizeY = topEdgeY - bottomEdgeY;
        float centerOffsetY = (topEdgeY + bottomEdgeY) / 2f;

        // 계산된 값 적용
        col.size = new Vector2(midW, sizeY);
        col.offset = new Vector2(0f, centerOffsetY);
    }

    private void ClearLadder()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (child.name.Contains(LADDER_PART_TAG))
            {
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }
    }
}