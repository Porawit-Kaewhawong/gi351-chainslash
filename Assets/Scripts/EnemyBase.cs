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

    [Header("Grapple Interaction Settings")]
    [SerializeField] protected bool isHeavyEnemy = false;
    [SerializeField] protected float grappleStunDuration = 0.45f;
    [SerializeField] protected bool dieOnHighSpeedWallSlam = true;
    [SerializeField] protected float wallSlamSpeedThreshold = 11f;

    [Header("UI & Visual Indicators")]
    [SerializeField] protected GameObject targetIndicator;
    [SerializeField] protected Color targetableOutlineColor = new Color(1f, 0.95f, 0.35f, 1f);
    [SerializeField] protected float targetableOutlineThickness = 0.055f;
    [SerializeField] protected float targetJitterStrength = 0.03f;

    public bool IsDead { get; protected set; }
    public bool IsHeavyEnemy => isHeavyEnemy;
    public bool IsGrappleStunned => grappleStunTimer > 0f;
    public State CurrentState => currentState;

    protected State currentState = State.Normal;
    protected int jumpsLeft;
    protected float lastJumpTime;
    protected float nextJumpTime;
    protected float lastAttackTime;
    protected float grappleStunTimer;
    protected bool wasKickedByPlayer;
    protected bool isTarget = false;

    protected Rigidbody2D rb;
    protected Collider2D col;
    protected SpriteRenderer spriteRenderer;
    protected PlayerController playerController;
    protected Color originalColor = Color.white;
    private Material originalSpriteMaterial;
    private Vector3 originalSpriteLocalPos;

    private GameObject outlineContainer;
    private SpriteRenderer[] outlineRenderers;
    private static Material sharedOutlineMaterial;
    private static readonly Vector2[] OutlineDirections = new Vector2[]
    {
        new Vector2( 1f,  0f),
        new Vector2(-1f,  0f),
        new Vector2( 0f,  1f),
        new Vector2( 0f, -1f),
        new Vector2( 0.707f,  0.707f),
        new Vector2(-0.707f,  0.707f),
        new Vector2( 0.707f, -0.707f),
        new Vector2(-0.707f, -0.707f)
    };

    protected virtual float CurrentMoveSpeed => moveSpeed;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        if (targetIndicator != null)
        {
            if (targetIndicator.TryGetComponent<SpriteRenderer>(out SpriteRenderer indicatorSR))
            {
                targetableOutlineColor = indicatorSR.color;
            }
            targetIndicator.SetActive(false);
        }

        // Find the main enemy SpriteRenderer (excluding any legacy targetIndicator object)
        SpriteRenderer[] allRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        foreach (SpriteRenderer sr in allRenderers)
        {
            if (targetIndicator != null && (sr.gameObject == targetIndicator || sr.transform.IsChildOf(targetIndicator.transform)))
            {
                continue;
            }
            spriteRenderer = sr;
            break;
        }

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
            originalSpriteMaterial = spriteRenderer.sharedMaterial;
            originalSpriteLocalPos = spriteRenderer.transform.localPosition;
            CreateSpriteOutlineIndicator();
        }

        if (telegraphColor.a <= 0f)
        {
            telegraphColor = Color.red;
        }
    }

    private void CreateSpriteOutlineIndicator()
    {
        if (spriteRenderer == null) return;

        if (sharedOutlineMaterial == null)
        {
            Shader solidShader = Shader.Find("GUI/Text Shader");
            if (solidShader != null)
            {
                sharedOutlineMaterial = new Material(solidShader);
            }
        }

        outlineContainer = new GameObject("SpriteOutlineIndicator");
        outlineContainer.transform.SetParent(spriteRenderer.transform, false);
        outlineContainer.transform.localPosition = Vector3.zero;
        outlineContainer.transform.localRotation = Quaternion.identity;
        outlineContainer.transform.localScale = Vector3.one;

        outlineRenderers = new SpriteRenderer[OutlineDirections.Length];
        for (int i = 0; i < OutlineDirections.Length; i++)
        {
            GameObject partObj = new GameObject($"Outline_{i}");
            partObj.transform.SetParent(outlineContainer.transform, false);
            partObj.transform.localPosition = (Vector3)(OutlineDirections[i] * targetableOutlineThickness);
            partObj.transform.localRotation = Quaternion.identity;
            partObj.transform.localScale = Vector3.one;

            SpriteRenderer sr = partObj.AddComponent<SpriteRenderer>();
            if (sharedOutlineMaterial != null)
            {
                sr.sharedMaterial = sharedOutlineMaterial;
            }
            else
            {
                sr.sharedMaterial = spriteRenderer.sharedMaterial;
            }

            sr.sprite = spriteRenderer.sprite;
            sr.color = targetableOutlineColor;
            sr.sortingLayerID = spriteRenderer.sortingLayerID;
            sr.sortingOrder = spriteRenderer.sortingOrder - 1;
            outlineRenderers[i] = sr;
        }

        outlineContainer.SetActive(false);
    }

    protected virtual void Start()
    {
        playerController = PlayerController.Instance != null ? PlayerController.Instance : FindFirstObjectByType<PlayerController>();
    }

    protected virtual void Update()
    {
        if (IsDead) return;

        if (grappleStunTimer > 0f)
        {
            grappleStunTimer -= Time.deltaTime;
            if (grappleStunTimer <= 0f)
            {
                wasKickedByPlayer = false;
            }
        }

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
        if (IsDead) return;

        if (HandleThrownImpact(collision))
        {
            return;
        }

        HandlePlayerContact(collision);
    }

    protected virtual void OnCollisionStay2D(Collision2D collision)
    {
        HandlePlayerContact(collision);
    }

    protected virtual bool HandleThrownImpact(Collision2D collision)
    {
        if (!IsGrappleStunned || !wasKickedByPlayer) return false;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < wallSlamSpeedThreshold) return false;

        // Domino impact into another enemy
        if (collision.gameObject.TryGetComponent<EnemyBase>(out EnemyBase otherEnemy) && !otherEnemy.IsDead)
        {
            Vector2 dominoDir = (otherEnemy.transform.position - transform.position).normalized;
            otherEnemy.OnDeath(dominoDir);
            OnDeath(-dominoDir);
            return true;
        }

        // High-speed wall/ground slam after being Grapple-Kicked
        if (dieOnHighSpeedWallSlam && ((1 << collision.gameObject.layer) & groundLayer) != 0)
        {
            Vector2 bounceNormal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector2.up;
            OnDeath(bounceNormal);
            return true;
        }

        return false;
    }

    protected virtual void HandlePlayerContact(Collision2D collision)
    {
        if (IsDead) return;

        if (collision.gameObject.TryGetComponent<PlayerController>(out PlayerController player))
        {
            // Check if the player is executing a high-speed Grapple Swing Kick / Momentum Bash
            if (player.TryExecuteGrappleKick(this))
            {
                return;
            }

            // Do not deal contact damage while stunned from being yanked/kicked or while player has immunity frames
            if (IsGrappleStunned || player.IsImmune) return;

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
            PlayerController.Instance.TargetSetup(transform);
            isTarget = true;

            if (spriteRenderer != null)
            {
                if (sharedOutlineMaterial != null)
                {
                    spriteRenderer.sharedMaterial = sharedOutlineMaterial;
                }
                spriteRenderer.color = targetableOutlineColor;
            }

            if (outlineContainer != null)
            {
                outlineContainer.SetActive(false);
            }
        }
    }

    public abstract void OnDeath(Vector2 deathImpulse = default);

    protected virtual void Movement()
    {
        if (playerController == null || IsGrappleStunned) return;

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

        if (spriteRenderer != null && !isTarget)
        {
            spriteRenderer.color = telegraphColor;
        }

        yield return new WaitForSeconds(windUpDuration);

        if (spriteRenderer != null && !isTarget)
        {
            spriteRenderer.color = originalColor;
        }

        if (IsGrappleStunned) yield break;

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

    protected virtual void LateUpdate()
    {
        if (IsDead || outlineContainer == null || !outlineContainer.activeSelf || spriteRenderer == null || outlineRenderers == null)
        {
            return;
        }

        for (int i = 0; i < outlineRenderers.Length; i++)
        {
            SpriteRenderer sr = outlineRenderers[i];
            if (sr == null) continue;

            sr.sprite = spriteRenderer.sprite;
            sr.flipX = spriteRenderer.flipX;
            sr.flipY = spriteRenderer.flipY;
            sr.color = targetableOutlineColor;
            sr.sortingLayerID = spriteRenderer.sortingLayerID;
            sr.sortingOrder = spriteRenderer.sortingOrder - 1;
            sr.transform.localPosition = (Vector3)(OutlineDirections[i] * targetableOutlineThickness);
        }
    }

    protected virtual void UpdateTargetIndicator()
    {
        bool inCombatMode = PlayerController.Instance != null && PlayerController.Instance.IsInMode;

        if (!inCombatMode && isTarget)
        {
            isTarget = false;
            if (spriteRenderer != null)
            {
                if (originalSpriteMaterial != null)
                {
                    spriteRenderer.sharedMaterial = originalSpriteMaterial;
                }
                spriteRenderer.color = originalColor;
                spriteRenderer.transform.localPosition = originalSpriteLocalPos;
            }
        }
        else if (inCombatMode && isTarget && spriteRenderer != null)
        {
            // Keep locked-on enemies as a full-body glowing silhouette in their targetIndicator color + micro-jitter
            if (sharedOutlineMaterial != null && spriteRenderer.sharedMaterial != sharedOutlineMaterial)
            {
                spriteRenderer.sharedMaterial = sharedOutlineMaterial;
            }
            spriteRenderer.color = targetableOutlineColor;

            Vector2 jitterOffset = Random.insideUnitCircle * targetJitterStrength;
            spriteRenderer.transform.localPosition = originalSpriteLocalPos + new Vector3(jitterOffset.x, jitterOffset.y, 0f);
        }

        bool showOutline = inCombatMode && !isTarget;
        if (outlineContainer != null && outlineContainer.activeSelf != showOutline)
        {
            outlineContainer.SetActive(showOutline);
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
        currentState = State.Normal;
        grappleStunTimer = grappleStunDuration * 1.5f;
        wasKickedByPlayer = false;

        if (spriteRenderer != null && !isTarget)
        {
            spriteRenderer.color = originalColor;
        }

        rb.linearVelocity = pullForce;
    }

    public virtual void OnGrappleKicked(Vector2 kickImpulse)
    {
        currentState = State.Normal;
        grappleStunTimer = grappleStunDuration * 2f;
        wasKickedByPlayer = true;

        if (spriteRenderer != null && !isTarget)
        {
            spriteRenderer.color = originalColor;
        }

        rb.linearVelocity = kickImpulse;
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
            if (player.TryExecuteGrappleKick(this))
            {
                return;
            }

            if (IsGrappleStunned) return;

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