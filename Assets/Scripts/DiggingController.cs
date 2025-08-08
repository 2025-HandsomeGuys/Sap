
using UnityEngine;
using UnityEngine.Tilemaps;

public class DiggingController : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;
    public Tilemap groundTilemap;

    [Header("Digging Settings")]
    public int digRadius = 1; // 클릭한 곳 주변으로 파는 전체 범위
    public int guaranteedDigRadius = 0; // 이 반지름 내의 타일은 무조건 파짐 (0이면 무조건 파지는 영역 없음)
    [Range(0f, 1f)]
    public float digRandomness = 0.8f; // 중심에서 타일을 팔 최대 확률 (guaranteedDigRadius 바깥 영역부터 적용)
    [Range(0f, 1f)]
    public float minDigProbability = 0.1f; // 가장자리에서 타일을 팔 최소 확률

    void Update()
    {
        // Check for left mouse button click
        if (Input.GetMouseButtonDown(0))
        {
            Dig();
        }
    }

    void Dig()
    {
        if (mainCamera == null || groundTilemap == null)
        {
            Debug.LogError("References not set in DiggingController!");
            return;
        }

        // 마우스 위치 찾기
        Vector2 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);

        // world 위치를 tilemap의 cell 위치로 변환
        Vector3Int clickedCellPosition = groundTilemap.WorldToCell(mouseWorldPos);

        // 원을 포함할만큼 충분히 큰 사각형을 순회
        for (int x = -digRadius; x <= digRadius; x++)
        {
            for (int y = -digRadius; y <= digRadius; y++)
            {
                Vector3Int currentCell = new Vector3Int(clickedCellPosition.x + x, clickedCellPosition.y + y, 0);

                // 클릭한 셀 중심에서 현재 셀 중심까지의 거리를 계산
                Vector2 distanceVector = new Vector2(x, y);
                float distance = distanceVector.magnitude;

                if (distance <= digRadius) // 전체 파기 범위 내에 있는 경우
                {
                    if (distance <= guaranteedDigRadius) // 무조건 파지는 영역인 경우
                    {                        // 무조건 파기
                        TileBase tile = groundTilemap.GetTile(currentCell);
                        if (tile != null)
                        { groundTilemap.SetTile(currentCell, null);
                        }
                    }
                    else // 확률적으로 파지는 영역인 경우
                    {
                        // 확률 변화가 시작되는 지점부터 digRadius까지의 거리를 기준으로 정규화
                        // normalizedDistance는 guaranteedDigRadius에서 digRadius까지 0에서 1로 변화
                        float normalizedDistance = (distance - guaranteedDigRadius) / (digRadius - guaranteedDigRadius);
                        // Mathf.Clamp01을 사용하여 0과 1 사이로 값을 제한 (나누기 0 방지 및 범위 보장)
                        normalizedDistance = Mathf.Clamp01(normalizedDistance);

                        // 거리에 따른 파기 확률 계산 (guaranteedDigRadius 바깥부터 확률 감소)
                        float actualDigProbability = Mathf.Lerp(digRandomness, minDigProbability, normalizedDistance);

                        // 실제 파기 확률과 Random.value를 비교하여 타일을 팔지 결정
                        if (Random.value < actualDigProbability){
                            TileBase tile = groundTilemap.GetTile(currentCell);
                            if (tile != null) groundTilemap.SetTile(currentCell, null);
                        }
                    }
                }
            }
        }
    }
}
