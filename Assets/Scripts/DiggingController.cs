using System.Collections.Generic;
using UnityEngine;
using static Constants;

public class DiggingController : MonoBehaviour
{
    [Header("Digging Settings")]
    public float digRadius = 1.0f;
    public float digOffset = 0.5f;
    public float digCooldown = 0.2f;

    private Vector2 currentDigDirection = Vector2.right;
    private float nextDigTime = 0f;
    private PlayerController playerController; // PlayerController 참조 변수 추가

    void Start()
    {
        // 같은 게임 오브젝트에 있는 PlayerController 컴포넌트를 자동으로 찾아옴
        playerController = GetComponent<PlayerController>();
    }

    void Update()
    {
        // 인벤토리가 열려있으면 아무것도 하지 않음
        if (playerController != null && playerController.IsInventoryOpen())
        {
            return;
        }

        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        if ((mousePosition - (Vector2)transform.position).sqrMagnitude > 0.01f)
        {
            currentDigDirection = (mousePosition - (Vector2)transform.position).normalized;
        }

        if (Input.GetMouseButton(0) && Time.time >= nextDigTime)
        {
            nextDigTime = Time.time + digCooldown;
            Dig();
        }
    }

    void Dig()
    {
        Vector2 digCenter = (Vector2)transform.position + (currentDigDirection * digOffset);

        HashSet<Vector3Int> cellsToDig = new HashSet<Vector3Int>();

        float scanStep = WorldManager.Instance.groundTilemap.cellSize.x / 2f;
        if (scanStep <= 0) scanStep = 0.1f;

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
