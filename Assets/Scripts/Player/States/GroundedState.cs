using UnityEngine;

public class GroundedState : PlayerBaseState
{
    public override void EnterState(PlayerController player)
    {
        // Ensure gravity is normal when grounded
        player.RB.gravityScale = player.originalGravityScale;
    }

    public override void UpdateState(PlayerController player)
    {
        // Handle animations and flipping
        // This one line handles BOTH moving and standing still.
        // If moveInput is not 0, isMoving becomes true.
        // If moveInput is 0, isMoving becomes false.
        player.Anim.SetBool("ismoving", player.moveInput != 0);

        // This part just handles which way the character is facing.
        if (player.moveInput > 0)
        {
            player.SpriteRenderer.flipX = true;
        }
        else if (player.moveInput < 0)
        {
            player.SpriteRenderer.flipX = false;
        }

        // Check for jump transition
        if (Input.GetKeyDown(KeyCode.Space))
        {
            player.TransitionToState(player.JumpingState);
            return; // Exit early to prevent other transitions
        }

        // Check for wall climb transition
        if (player.isInsideWallZone && Input.GetKey(KeyCode.LeftShift) && player.PlayerStats.currentStamina > 0)
        {
            player.TransitionToState(player.WallClimbingState);
        }
    }

    public override void FixedUpdateState(PlayerController player)
    {
        // Ground check
        player.isGrounded = Physics2D.OverlapCircle(player.groundCheck.position, player.groundCheckRadius, player.groundLayer);
        player.Anim.SetBool("isjumping", !player.isGrounded);

        // If player walks off a ledge, transition to jumping/falling state
        if (!player.isGrounded)
        {
            player.TransitionToState(player.JumpingState);
            return;
        }

        // Handle movement
        float currentMoveSpeed = player.PlayerStats.moveSpeed;
        if (player.playerInventory != null && player.playerInventory.IsEncumbered)
        {
            currentMoveSpeed *= player.PlayerStats.encumberedSpeedMultiplier;
        }
        player.RB.linearVelocity = new Vector2(player.moveInput * currentMoveSpeed, player.RB.linearVelocity.y);
    }

    public override void OnTriggerEnter2D(PlayerController player, Collider2D other)
    {
        if (other.CompareTag("Wall"))
        {
            player.isInsideWallZone = true;
        }
    }

    public override void OnTriggerExit2D(PlayerController player, Collider2D other)
    {
        if (other.CompareTag("Wall"))
        {
            player.isInsideWallZone = false;
        }
    }
}
