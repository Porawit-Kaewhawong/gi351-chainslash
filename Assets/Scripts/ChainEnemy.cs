using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

public class ChainEnemy : MonoBehaviour, IPointerEnterHandler
{
    public static bool isExploding = false;

    [Header("Chain Enemy Settings")]
    [SerializeField] private float moveSpeed = 2.5f;

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


    [SerializeField] private GameObject targetIndicator;
    [SerializeField] private AudioClip explosionSound;
    [SerializeField] private GameObject explosionRadiusPrefab;
    [SerializeField] private float explosionRadius = 5f;


    public bool IsDead { get; private set; }
    private bool isTarget = false;
    private Rigidbody2D rb;
    private GameObject insExplosion;
    private SpriteRenderer spriteRenderer;
    private PlayerController playerController;

    private void Awake()
    {
        explosionRadius = explosionRadius / 2;
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        playerController = FindFirstObjectByType<PlayerController>();
        insExplosion = Instantiate(explosionRadiusPrefab, transform.position, Quaternion.identity);
        insExplosion.SetActive(false);
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        insExplosion.transform.position = transform.position;

        Movement();

        if (IsDead) return;

        if (PlayerCombat.Instance.IsInMode && !isTarget)
        {
            if (targetIndicator != null)
            {
                targetIndicator.SetActive(true);
            }
        }
        else if (!PlayerCombat.Instance.IsInMode && !isTarget)
        {
            if (targetIndicator != null)
            {
                targetIndicator.SetActive(false);
            }
            insExplosion.SetActive(false);
        }
        else if (!PlayerCombat.Instance.IsInMode && isTarget)
        {
            insExplosion.SetActive(false);
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
            insExplosion.SetActive(true);
        }
    }

    public IEnumerator OnDeath()
    {
        if (IsDead) yield break;
        IsDead = true;

        while (isExploding)
        {
            yield return null;
        }

        isExploding = true;

        yield return new WaitForSeconds(0.25f);

        Collider2D[] withinRange = Physics2D.OverlapCircleAll(transform.position, explosionRadius);

        foreach (Collider2D collider in withinRange)
        {
            if (collider == null || collider.gameObject == gameObject) continue;

            if (collider.TryGetComponent<Enemy>(out Enemy enemy))
            {
                if (enemy != null && enemy.IsDead == false)
                {
                    enemy.OnDeath();
                }
            }
        }

        foreach (Collider2D collider in withinRange)
        {
            if (collider == null || collider.gameObject == gameObject) continue;

            if (collider.TryGetComponent<ChainEnemy>(out ChainEnemy chainEnemy))
            {
                if (chainEnemy != null && chainEnemy.IsDead == false)
                {
                    chainEnemy.StartCoroutine(chainEnemy.OnDeath());
                }
            }
        }

        insExplosion.SetActive(true);

        playerController.PlaySFX(explosionSound);

        isExploding = false;

        PlayerCombat.Instance.StartCoroutine(PlayerCombat.Instance.ShakingTrigger(0.25f));

        Destroy(insExplosion, 0.5f);
        Destroy(gameObject);
    }

    private void Movement()
    {
        if (playerController == null || rb == null) return;

        Vector2 dir = (playerController.transform.position - transform.position).normalized;

        // Hello Dev Water! I'm Bob, Mr.T's assistant.
        // We are using 'jumpsLeft' as a State Machine to avoid adding any new variables outside these functions.
        // jumpsLeft > 0 : Normal State (Ground movement & Jumping)
        // jumpsLeft == -1 : Wall Sticking State (Freezes on the wall for 2 seconds)
        // jumpsLeft == -2 : Diving Attack State (Swooping down to kill the player)

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