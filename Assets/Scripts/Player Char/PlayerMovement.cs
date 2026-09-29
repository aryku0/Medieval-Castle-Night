using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [Range(1, 15)]
    [SerializeField] private float speed = 5f;

    public bool facingRight = true;

    private Vector2 movementInput;

    [Header("Jumping")]
    [Range(1, 20)]
    [SerializeField] private float jumpForce = 8f;

    [Tooltip("Read-only: updated every physics step from floor contacts.")]
    [SerializeField] private bool grounded;

    private bool jumpPressed;

    private Rigidbody2D rb;
    private ContactFilter2D groundFilter;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // Only count contacts whose surface faces upward (floors, not walls or ceilings).
        groundFilter = new ContactFilter2D();
        groundFilter.useTriggers = false;
        groundFilter.SetNormalAngle(45f, 135f);
    }

    public void OnMove(InputValue value)
    {
        movementInput = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        if (value.isPressed)
        {
            jumpPressed = true;
        }
    }

    void FixedUpdate()
    {
        // Re-check every step, so walking off a ledge clears grounded.
        grounded = rb.IsTouching(groundFilter);

        // Horizontal movement
        rb.linearVelocity = new Vector2(
            movementInput.x * speed,
            rb.linearVelocity.y
        );

        // Jump
        if (jumpPressed && grounded)
        {
            rb.AddForce(
                Vector2.up * jumpForce,
                ForceMode2D.Impulse
            );

            grounded = false;
        }

        jumpPressed = false;

        // Remember which direction the player is facing.
        // We are NOT visually flipping the temporary capsule.
        if (movementInput.x > 0)
        {
            facingRight = true;
        }
        else if (movementInput.x < 0)
        {
            facingRight = false;
        }
    }
}
