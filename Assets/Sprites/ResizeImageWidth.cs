using UnityEngine;

[ExecuteAlways] // 에디터에서도 실시간 반영
[RequireComponent(typeof(SpriteRenderer))] // 이 스크립트를 넣으면 SpriteRenderer가 자동으로 필수 요구됨
public class ResizeSpriteWidth : MonoBehaviour
{
    [Header("가로 크기(Scale X) 조절")]
    [Range(0f, 10f)] // 2.2 기준에 맞춰 0~10으로 범위 설정
    [SerializeField] private float width = 2.2f;

    private SpriteRenderer spriteRenderer;

    private void OnValidate()
    {
        ApplyWidth();
    }

    private void ApplyWidth()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        // 스프라이트 이미지가 비어있으면 작동하지 않음
        if (spriteRenderer == null || spriteRenderer.sprite == null) return;

        // 현재 스케일 확인 (변화가 없으면 연산 건너뜀)
        float currentScaleX = transform.localScale.x;
        if (Mathf.Approximately(currentScaleX, width)) return;

        // 1. 스프라이트 정보 가져오기
        // pivot.x는 픽셀 단위이므로 rect.width로 나누어 0(왼쪽)~1(오른쪽) 사이의 비율로 변환합니다.
        float normalizedPivotX = spriteRenderer.sprite.pivot.x / spriteRenderer.sprite.rect.width;

        // Transform 스케일이 적용되지 않은 스프라이트 원본 가로 길이
        float baseWidth = spriteRenderer.sprite.bounds.size.x;

        // 피벗 기준 오른쪽 끝단까지의 비율 (중앙 피벗이면 0.5, 오른쪽 피벗이면 0)
        float rightEdgeRatio = 1f - normalizedPivotX;

        // 스케일 변화량 계산
        float deltaScale = width - currentScaleX;

        // 2. 가로 스케일(Scale X) 적용
        Vector3 newScale = transform.localScale;
        newScale.x = width;
        transform.localScale = newScale;

        // 3. 위치(Position) 보정
        // 오른쪽 끝을 고정하기 위해, 늘어난 스케일만큼 X 좌표를 왼쪽(-)으로 밀어줍니다.
        Vector3 newPosition = transform.localPosition;
        newPosition.x -= deltaScale * baseWidth * rightEdgeRatio;
        transform.localPosition = newPosition;
    }
}