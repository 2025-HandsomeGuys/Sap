using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using static Constants;

public class DiggingController : MonoBehaviour
{
    [Header("Dependencies")]
    public InventoryUI inventoryUI; // Assign in inspector

    [Header("Digging Settings")]
    public float digRadius = 1.0f;
    public float digOffset = 0.5f;
    public float digCooldown = 0.2f;

    private Vector2 currentDigDirection = Vector2.right;
    private float nextDigTime = 0f;
    private bool isDigging = false;

    void Start()
    {
        if (inventoryUI == null)
        {
            Debug.LogError("InventoryUI is not assigned in the DiggingController inspector!", this);
        }
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        isDigging = context.ReadValueAsButton();
    }

    void Update()
    {
        if (inventoryUI != null && inventoryUI.IsOpen())
        {
            return;
        }

        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        if ((mousePosition - (Vector2)transform.position).sqrMagnitude > 0.01f)
        {
            currentDigDirection = (mousePosition - (Vector2)transform.position).normalized;
        }

        if (isDigging && Time.time >= nextDigTime)
        {
            nextDigTime = Time.time + digCooldown;
            Dig();
        }
    }

    void Dig()
    {
        //SoundManager.Instance.PlaySound("Dig"); // Play digging sound

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