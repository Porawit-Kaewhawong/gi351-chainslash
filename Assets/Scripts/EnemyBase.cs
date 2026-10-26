using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public abstract class EnemyBase : MonoBehaviour, IPointerEnterHandler
{
    public enum State
    {
        Normal,
        WallSticking,
        Telegraphing,
        Diving,
        Dodging
    }

    [Header("Base Movement Settings")]
    [SerializeField] protected float moveSpeed = 2.5f;

    [Header("Jump Settings")]
    [SerializeField] protected LayerMask groundLayer;
    [SerializeField] protected float jumpForce = 7f;
    [SerializeField] protected int maxJumps = 2;
    [SerializeField] protected float jumpCooldown = 1f;
    [SerializeField] protected float doubleJumpDelay = 0.25f;
    [SerializeField] protected float wallCheckDistance = 0.6f;
    [SerializeField] protected float groundCheckDistance = 0.6f;

    [Header("Telegraph Wind-up Settings")]
    [SerializeField] protected float windUpDuration = 0.4f;
    [SerializeField] protected Color telegraphColor = Color.red;

    [Header("Attack Settings")]
    [SerializeField] protected float contactDamage = 15f;
    [SerializeField] protected float attackCooldown = 1f;
    [SerializeField] protected float knockbackForce = 8f;

    [Header("UI & Visual Indicators")]
    [SerializeField] protected GameObject targetIndicator;

    public bool IsDead { get; protected set; }
    public State CurrentState => currentState;

    protected State currentState = State.Normal;
    protected int jumpsLeft;
    protected float lastJumpTime;
    protected float nextJumpTime;
    protected float lastAttackTime;
    protected bool isTarget = false;

    protected Rigidbody2D rb;
    protected Collider2D col;
    protected SpriteRenderer spriteRenderer;
    protected PlayerController playerController;
    protected Color originalColor = Color.white;

    protected virtual float CurrentMoveSpeed => moveSpeed;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }

        if (telegraphColor.a <= 0f)
        {
            telegraphColor = Color.red;
        }
    }

    protected virtual void Start()
    {
        playerController = PlayerController.Instance != null ? PlayerController.Instance : FindFirstObjectByType<PlayerController>();
    }

    protected virtual void Update()
    {
        if (IsDead) return;

        UpdateTargetIndicator();
        CheckMouseHover();
        FlipAndRotate();
    }

    protected virtual void FixedUpdate()
    {
        if (IsDead) return;

        Movement();
    }

    #region Attack & Collision Logic

    protected virtual void OnCollisionEnter2D(Collision2D collision)
    {
        HandlePlayerContact(collision);
    }

    protected virtual void OnCollisionStay2D(Collision2D collision)
    {
        HandlePlayerContact(collision);
    }

    protected virtual void HandlePlayerContact(Collision2D collision)
    {
        if (IsDead) return;

        if (collision.gameObject.TryGetComponent<PlayerController>(out PlayerController player))
        {
            if (Time.time >= lastAttackTime + attackCooldown)
            {
                lastAttackTime = Time.time;

                // Deal extra damage if in Diving state
                float damageToDeal = (currentState == State.Diving) ? contactDamage * 1.5f : contactDamage;
                player.TakeDamage(damageToDeal);

                // Apply physical knockback push to the player
                if (collision.gameObject.TryGetComponent<Rigidbody2D>(out Rigidbody2D playerRb))
                {
                    Vector2 knockbackDir = (player.transform.position - transform.position).normalized;
                    if (knockbackDir == Vector2.zero) knockbackDir = Vector2.up;
                    playerRb.AddForce(knockbackDir * knockbackForce, ForceMode2D.Impulse);
                }

                // Reset dive state back to normal after landing a hit
                if (currentState == State.Diving)
                {
                    currentState = State.Normal;
                }
            }
        }
    }

    #endregion

    public virtual void OnPointerEnter(PointerEventData eventData)
    {
        TrySelectTarget();
    }

    protected virtual void CheckMouseHover()
    {
        if (isTarget || PlayerController.Instance == null || !PlayerController.Instance.IsInMode) return;

        if (Camera.main != null && Mouse.current != null && col != null)
        {
            Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            if (col.OverlapPoint(mouseWorldPos))
            {
                TrySelectTarget();
            }
        }
    }

    public virtual void TrySelectTarget()
    {
        if (IsDead || isTarget) return;

        if (PlayerController.Instance != null && PlayerController.Instance.IsInMode)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = Color.purple;
            }

            PlayerController.Instance.TargetSetup(transform);
            isTarget = true;

            if (targetIndicator != null)
            {
                targetIndicator.SetActive(false);
            }
        }
    }

    public abstract void OnDeath(Vector2 deathImpulse = default);

    protected virtual void Movement()
    {
        if (playerController == null) return;

        Vector2 diff = playerController.transform.position - transform.position;
        if (diff.sqrMagnitude < 0.0001f) return;

        Vector2 dir = diff.normalized;
        float speed = CurrentMoveSpeed;

        switch (currentState)
        {
            case State.WallSticking:
            case State.Telegraphing:
                rb.linearVelocity = Vector2.zero;
                break;

            case State.Diving:
                rb.AddForce(dir * speed * 2.5f);
                break;

            case State.Normal:
            case State.Dodging:
                rb.AddForce(new Vector2(dir.x * speed, 0f));
                Vector2 velocity = rb.linearVelocity;
                velocity.x = Mathf.Clamp(velocity.x, -speed * 1.5f, speed * 1.5f);
                rb.linearVelocity = velocity;
                break;
        }

        HandleJump(dir);
    }

    protected virtual void HandleJump(Vector2 dir)
    {
        Bounds bounds = col.bounds;

        bool grounded = Physics2D.BoxCast(
            bounds.center,
            new Vector2(bounds.size.x * 0.8f, 0.05f),
            0f,
            Vector2.down,
            groundCheckDistance,
            groundLayer
        );

        if (grounded && Time.time > lastJumpTime + 0.1f && currentState != State.Telegraphing)
        {
            jumpsLeft = maxJumps;
            if (currentState != State.Normal && currentState != State.Dodging)
            {
                currentState = State.Normal;
            }
        }

        Vector2 faceDirection = new Vector2(Mathf.Sign(dir.x), 0);
        bool isTouchingWall = Physics2D.Raycast(
            bounds.center,
            faceDirection,
            bounds.extents.x + wallCheckDistance,
            groundLayer
        );

        if (currentState == State.WallSticking)
        {
            if (Time.time >= nextJumpTime)
            {
                StartCoroutine(TelegraphAndDive());
            }
            return;
        }

        if (currentState == State.Telegraphing || currentState == State.Diving) return;

        if (!grounded && isTouchingWall && jumpsLeft > 0 && Time.time >= lastJumpTime + 0.3f)
        {
            currentState = State.WallSticking;
            nextJumpTime = Time.time + 2f;
            return;
        }

        if (grounded && Time.time >= nextJumpTime && jumpsLeft > 0)
        {
            Jump();
            nextJumpTime = Time.time + jumpCooldown;
            return;
        }

        if (!grounded && jumpsLeft > 0 && Time.time >= lastJumpTime + doubleJumpDelay && !isTouchingWall)
        {
            Jump();
        }
    }

    protected virtual IEnumerator TelegraphAndDive()
    {
        currentState = State.Telegraphing;

        if (spriteRenderer != null)
        {
            spriteRenderer.color = telegraphColor;
        }

        yield return new WaitForSeconds(windUpDuration);

        if (spriteRenderer != null && !isTarget)
        {
            spriteRenderer.color = originalColor;
        }

        currentState = State.Diving;
        Vector2 diveDir = (playerController.transform.position - transform.position).normalized;
        rb.linearVelocity = diveDir * jumpForce * 1.5f;
    }

    protected virtual void Jump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        jumpsLeft--;
        lastJumpTime = Time.time;
    }

    protected virtual void UpdateTargetIndicator()
    {
        bool inCombatMode = PlayerController.Instance != null && PlayerController.Instance.IsInMode;

        if (!inCombatMode && isTarget)
        {
            isTarget = false;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = originalColor;
            }
        }

        if (targetIndicator != null)
        {
            targetIndicator.SetActive(inCombatMode && !isTarget);
        }
    }

    protected virtual void FlipAndRotate()
    {
        if (spriteRenderer == null) return;

        if (currentState == State.WallSticking)
        {
            float wallDir = Mathf.Sign(playerController.transform.position.x - transform.position.x);
            transform.rotation = Quaternion.Euler(0f, 0f, wallDir * -90f);
        }
        else
        {
            transform.rotation = Quaternion.identity;

            if (rb.linearVelocityX < -0.1f)
            {
                spriteRenderer.flipX = true;
            }
            else if (rb.linearVelocityX > 0.1f)
            {
                spriteRenderer.flipX = false;
            }
        }
    }

    public virtual void OnGrappled(Vector2 pullForce)
    {
        rb.AddForce(pullForce, ForceMode2D.Impulse);
    }

    // Add these alongside OnCollisionEnter2D inside EnemyBase.cs to support Trigger Colliders:
    protected virtual void OnTriggerEnter2D(Collider2D other)
    {
        HandlePlayerTriggerContact(other);
    }

    protected virtual void OnTriggerStay2D(Collider2D other)
    {
        HandlePlayerTriggerContact(other);
    }

    protected virtual void HandlePlayerTriggerContact(Collider2D other)
    {
        if (IsDead) return;

        if (other.TryGetComponent<PlayerController>(out PlayerController player))
        {
            if (Time.time >= lastAttackTime + attackCooldown)
            {
                lastAttackTime = Time.time;

                float damageToDeal = (currentState == State.Diving) ? contactDamage * 1.5f : contactDamage;
                player.TakeDamage(damageToDeal);

                if (other.TryGetComponent<Rigidbody2D>(out Rigidbody2D playerRb))
                {
                    Vector2 knockbackDir = (player.transform.position - transform.position).normalized;
                    if (knockbackDir == Vector2.zero) knockbackDir = Vector2.up;
                    playerRb.AddForce(knockbackDir * knockbackForce, ForceMode2D.Impulse);
                }

                if (currentState == State.Diving)
                {
                    currentState = State.Normal;
                }
            }
        }
    }
}