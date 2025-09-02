using UnityEngine;

public class WallClimbingState : PlayerBaseState
{
    public override void EnterState(PlayerController player)
    {
        // Set gravity to 0 to stick to the wall
        player.RB.gravityScale = 0f;
        player.RB.linearVelocity = Vector2.zero; // Stop any falling momentum
    }

    public override void UpdateState(PlayerController player)
    {
        // --- Check for exit conditions ---
        bool outOfStamina = player.PlayerStats.currentStamina <= 0;
        bool stoppedHoldingShift = !Input.GetKey(KeyCode.LeftShift);

        // If player lets go of shift, runs out of stamina, or is no longer in a wall zone
        if (stoppedHoldingShift || outOfStamina || !player.isInsideWallZone)
        {
            player.TransitionToState(player.JumpingState); // Transition to air state
            return;
        }

        // --- Handle Stamina Drain ---
        if (player.moveInput != 0 || player.verticalInput != 0)
        {
            player.PlayerStats.UseStamina(player.PlayerStats.staminaCostPerSecond * Time.deltaTime);
        }
    }

    public override void FixedUpdateState(PlayerController player)
    {
        // Apply wall climbing movement
        float verticalVelocity = player.verticalInput * player.PlayerStats.wallClimbingSpeed;
        player.RB.linearVelocity = new Vector2(player.moveInput * player.PlayerStats.moveSpeed, verticalVelocity);
    }

    public override void OnTriggerEnter2D(PlayerController player, Collider2D other)
    {
        // This state doesn't need to detect entering a wall, as you must already be in one.
    }

    public override void OnTriggerExit2D(PlayerController player, Collider2D other)
    { 
        // If we exit the wall trigger, update the flag and the state will handle the transition in Update()
        if (other.CompareTag("Wall"))
        {
            player.isInsideWallZone = false;
        }
    }
}