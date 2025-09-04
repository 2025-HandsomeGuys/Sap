using UnityEngine;

public abstract class PlayerBaseState
{
    public abstract void EnterState(PlayerController player);
    public abstract void UpdateState(PlayerController player);
    public abstract void FixedUpdateState(PlayerController player);
    public abstract void OnTriggerEnter2D(PlayerController player, Collider2D other);
    public abstract void OnTriggerExit2D(PlayerController player, Collider2D other);
}