using UnityEngine;

public class JumpingState : PlayerBaseState
{
    public override void EnterState(PlayerController player)
    {
        // Set gravity back to normal in case we are transitioning from wall climbing
        player.RB.gravityScale = player.originalGravityScale;

        // This check ensures jump force is applied only when transitioning from a state that requests it.
        if (Input.GetKey(KeyCode.Space) && player.isGrounded) // Check isGrounded to prevent double jumps
        {
            player.RB.linearVelocity = new Vector2(player.RB.linearVelocity.x, 0f);
            player.RB.AddForce(new Vector2(0f, player.PlayerStats.jumpForce), ForceMode2D.Impulse);
            player.Anim.SetTrigger("jump");
        }
    }

    public override void UpdateState(PlayerController player)
    {
        player.Anim.SetBool("isjumping", !player.isGrounded);

        // Allow flipping mid-air
        if (player.moveInput > 0)
        {
            player.SpriteRenderer.flipX = true;
        }
        else if (player.moveInput < 0)
        {
            player.SpriteRenderer.flipX = false;
        }

        // Check for wall climb transition while in air
        if (player.isInsideWallZone && Input.GetKey(KeyCode.LeftShift) && player.PlayerStats.currentStamina > 0)
        {
            player.TransitionToState(player.WallClimbingState);
        }
    }

    public override void FixedUpdateState(PlayerController player)
    {
        // Ground check
        player.isGrounded = Physics2D.OverlapCircle(player.groundCheck.position, player.groundCheckRadius, player.groundLayer);

        // Transition to GroundedState if landed
        if (player.isGrounded)
        {
            player.TransitionToState(player.GroundedState);
            return;
        }

        // Handle air movement
        float currentMoveSpeed = player.PlayerStats.moveSpeed;
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
