using System.Collections.Generic; // 추가된 부분
using UnityEngine;
using static Constants;

public class DiggingController : MonoBehaviour
{
    [Header("Digging Settings")]
    public float digRadius = 1.0f;
    public float digOffset = 0.5f;
    public float digCooldown = 0.2f; // 땅 파기 쿨타임

    private Vector2 currentDigDirection = Vector2.right;
    private float nextDigTime = 0f; // 다음 땅 파기 가능 시간

    void Update()
    {
        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        if ((mousePosition - (Vector2)transform.position).sqrMagnitude > 0.01f)
        {
            currentDigDirection = (mousePosition - (Vector2)transform.position).normalized;
        }

        // GetMouseButton으로 변경하고, 쿨타임 확인
        if (Input.GetMouseButton(0) && Time.time >= nextDigTime)
        {
            nextDigTime = Time.time + digCooldown; // 다음 파기 시간 설정
            Dig();
        }
    }

    void Dig()
    {
        Vector2 digCenter = (Vector2)transform.position + (currentDigDirection * digOffset);

        HashSet<Vector3Int> cellsToDig = new HashSet<Vector3Int>();

        // 타일 크기를 기반으로 스캔 정밀도 결정하여 더 정확한 모양 생성
        float scanStep = WorldManager.Instance.groundTilemap.cellSize.x / 2f;
        if (scanStep <= 0) scanStep = 0.1f; // cellSize가 0일 경우를 대비한 안전장치

        // digRadius 내의 모든 점을 확인하여 해당하는 셀을 찾음
        for (float x = -digRadius; x <= digRadius; x += scanStep)
        {
            for (float y = -digRadius; y <= digRadius; y += scanStep)
            {
                if (x * x + y * y <= digRadius * digRadius)
                {
                    Vector2 checkPos = digCenter + new Vector2(x, y);
                    cellsToDig.Add(WorldManager.Instance.WorldToCell(checkPos));
                }
            }
        }

        foreach (Vector3Int cellPos in cellsToDig)
        {
            // 셀의 중앙 월드 좌표를 가져와서 TileDug에 전달
            Vector3 cellWorldCenter = WorldManager.Instance.groundTilemap.GetCellCenterWorld(cellPos);
            WorldManager.Instance.TileDug(cellWorldCenter);
        }
    }

    void OnDrawGizmosSelected()
    {
        Vector2 digCenter = (Vector2)transform.position + (currentDigDirection * digOffset);
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(digCenter, digRadius);
    }
}