using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class Enemy : MonoBehaviour, IPointerEnterHandler
{
    [Header("Enemy Settings")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private GameObject targetIndicator;

    [Header("Jump Settings")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float jumpForce = 7f;
    [SerializeField] private int maxJumps = 2;
    [SerializeField] private float jumpCooldown = 1f;
    [SerializeField] private float doubleJumpDelay = 0.25f;
    [SerializeField] private float wallCheckDistance = 0.6f;
    [SerializeField] private float groundCheckDistance = 0.6f;

    private int jumpsLeft;
    private float lastJumpTime;
    private float nextJumpTime;


    private bool isTarget = false;
    public bool IsDead { get; private set; }
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private PlayerController playerController;

    private void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        playerController = FindFirstObjectByType<PlayerController>();
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        Movement();

        if (IsDead) return;

        if (PlayerCombat.Instance.IsInMode && !isTarget)
        {
            if (targetIndicator != null)
            {
                targetIndicator.SetActive(true);
            }
        }
        else
        {
            if (targetIndicator != null)
            {
                targetIndicator.SetActive(false);
            }
        }

        Flip();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (PlayerCombat.Instance.IsInMode && !isTarget)
        {
            spriteRenderer.color = Color.purple;
            PlayerCombat.Instance.TargetSetup(transform);
            isTarget = true;

            if (targetIndicator != null)
            {
                targetIndicator.SetActive(false);
            }
        }
    }
    public void OnDeath()
    {
        if (IsDead) return;
        IsDead = true;

        Destroy(gameObject);
    }

    private void Movement()
    {
        if (playerController == null || rb == null) return;

        Vector2 dir = (playerController.transform.position - transform.position).normalized;


        if (jumpsLeft == -1)
        {
            // STATE: Wall Sticking -> Freeze all velocity to stick to the wall
            rb.linearVelocity = Vector2.zero;
        }
        else if (jumpsLeft == -2)
        {
            // STATE: Diving Attack -> Aggressively add force towards the player for a homing effect
            rb.AddForce(dir * moveSpeed * 2f);
        }
        else
        {
            // STATE: Normal Movement -> Move horizontally only
            rb.AddForce(new Vector2(dir.x * moveSpeed, 0f));

            // Clamp horizontal speed for normal movement
            Vector2 velocity = rb.linearVelocity;
            velocity.x = Mathf.Clamp(velocity.x, -moveSpeed, moveSpeed);
            rb.linearVelocity = velocity;
        }

        HandleJump(dir);
    }

    private void HandleJump(Vector2 dir)
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return;

        Bounds bounds = col.bounds;

        bool grounded = Physics2D.BoxCast(
            bounds.center,
            new Vector2(bounds.size.x * 0.8f, 0.05f),
            0f,
            Vector2.down,
            groundCheckDistance,
            groundLayer
        );

        // Reset all states and restore jumps when touching the ground
        if (grounded && Time.time > lastJumpTime + 0.1f)
        {
            jumpsLeft = maxJumps;
        }

        Vector2 faceDirection = new Vector2(Mathf.Sign(dir.x), 0);
        bool isTouchingWall = Physics2D.Raycast(
            bounds.center,
            faceDirection,
            bounds.extents.x + wallCheckDistance,
            groundLayer
        );

        // =================================================================
        // --- STATE MACHINE HANDLING ---
        // =================================================================

        // 1. STICKING STATE (Waiting for 2 seconds)
        if (jumpsLeft == -1)
        {
            // Check if 2 seconds have passed (we stored the release time in nextJumpTime)
            if (Time.time >= nextJumpTime)
            {
                // Transition to DIVING STATE
                jumpsLeft = -2;

                // Swoop down towards the player with high velocity!
                Vector2 diveDir = (playerController.transform.position - transform.position).normalized;
                rb.linearVelocity = diveDir * jumpForce * 1.5f;
            }
            return; // Skip normal jump logic while sticking
        }

        // 2. DIVING STATE
        if (jumpsLeft == -2)
        {
            // Continue falling and homing in on the player until hitting the ground
            return;
        }

        // =================================================================
        // --- NORMAL BEHAVIORS ---
        // =================================================================

        // TRIGGER WALL STICK
        // If mid-air, touching a wall, and has jumps left, attach to the wall!
        if (!grounded && isTouchingWall && jumpsLeft > 0)
        {
            // Add a small delay (0.3s) to prevent instant sticking immediately after a ground jump
            if (Time.time >= lastJumpTime + 0.3f)
            {
                jumpsLeft = -1; // Set State to Sticking
                nextJumpTime = Time.time + 2f; // Store the 2-second delay target in nextJumpTime
                return;
            }
        }

        // NORMAL GROUND JUMP
        if (grounded && Time.time >= nextJumpTime && jumpsLeft > 0)
        {
            Jump();
            nextJumpTime = Time.time + jumpCooldown;
            return;
        }

        // NORMAL DOUBLE JUMP
        if (!grounded && jumpsLeft > 0 && Time.time >= lastJumpTime + doubleJumpDelay)
        {
            // Only double jump if not touching a wall (prioritizes wall sticking)
            if (!isTouchingWall)
            {
                Jump();
            }
        }
    }

    private void Jump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);

        jumpsLeft--;
        lastJumpTime = Time.time;
    }

    private void Flip()
    {
        if (rb.linearVelocityX < 0f)
        {
            spriteRenderer.flipX = true;
        }
        else if (rb.linearVelocityX > 0f)
        {
            spriteRenderer.flipX = false;
        }
    }
}