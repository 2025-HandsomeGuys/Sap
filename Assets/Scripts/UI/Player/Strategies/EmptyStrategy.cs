using UnityEngine;

public class EmptyStrategy : IMiningStrategy
{
    private PlayerMining _context;

    public void Enter(PlayerMining context)
    {
        _context = context;
        _context.ResetRotations();
    }

    public void Exit()
    {
        _context = null;
    }

    public void HandleUpdate()
    {
        // Do nothing or handle basic interactions
    }

    public void HandleFixedUpdate()
    {
        // Do nothing
    }

    public void HandleLateUpdate()
    {
        // Do nothing
    }

    public bool CanSwitchTool()
    {
        return true; // Always allow switch from Empty? Unless climbing lock is handled by Manager.
    }

    public float GetChargeRatio()
    {
        return 0f;
    }

    public bool IsCharging => false;
    public bool IsAttacking => false;

    public DigParameters GetDigParameters(float baseRadius, TileType targetTileType)
    {
        return new DigParameters { CanDig = false };
    }
}
