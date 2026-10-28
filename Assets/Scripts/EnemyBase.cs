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
    [Tooltip("Maximum vertical height of a step or stair that can be walked up automatically without jumping.")]
    [SerializeField] protected float maxStepHeight = 0.6f;
    [Tooltip("Forward distance to check ahead for upcoming stair steps.")]
    [SerializeField] protected float stepCheckDistance = 0.22f;

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
    [SerializeField] protected bool showTelegraphColor = false;
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
    public bool IsTarget => isTarget;
    public State CurrentState => currentState;

    protected State currentState = State.Normal;
    protected int jumpsLeft;
    protected float lastJumpTime;
    protected float nextJumpTime;
    protected float lastAttackTime;
    protected float grappleStunTimer;
    protected bool wasKickedByPlayer;
    protected bool isTarget = false;
    protected int wallRunDirection = 1;
    protected float wallRunStartTime;
    protected float defaultGravityScale = 1f;

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
        if (rb != null)
        {
            defaultGravityScale = rb.gravityScale;
        }
        Collider2D[] colliders = GetComponents<Collider2D>();
        foreach (var c in colliders)
        {
            if (c != null && !c.isTrigger)
            {
                col = c;
                break;
            }
        }
        if (col == null) col = GetComponent<Collider2D>();
        jumpsLeft = maxJumps;

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
                if (rb != null)
                {
                    rb.gravityScale = 0f;
                    float climbSpeed = Mathf.Max(speed * 1.6f, 4.2f);
                    rb.linearVelocity = new Vector2(wallRunDirection * 1.5f, climbSpeed);
                }
                break;

            case State.Telegraphing:
                if (rb != null)
                {
                    rb.gravityScale = 0f;
                    rb.linearVelocity = Vector2.zero;
                }
                break;

            case State.Diving:
                if (rb != null)
                {
                    rb.gravityScale = defaultGravityScale;
                    rb.AddForce(dir * speed * 2.5f);
                }
                break;

            case State.Normal:
            case State.Dodging:
                if (rb != null)
                {
                    rb.gravityScale = defaultGravityScale;
                    rb.AddForce(new Vector2(dir.x * speed, 0f));
                    Vector2 velocity = rb.linearVelocity;
                    velocity.x = Mathf.Clamp(velocity.x, -speed * 1.5f, speed * 1.5f);
                    rb.linearVelocity = velocity;

                    if (Mathf.Abs(dir.x) > 0.05f)
                    {
                        HandleStepClimb(dir.x);
                    }
                }
                break;
        }

        HandleJump(dir);
    }

    protected virtual void HandleJump(Vector2 dir)
    {
        if (col == null) return;
        Bounds bounds = col.bounds;

        // Cast from the bottom of the collider so collider height (1.3) never swallows groundCheckDistance
        Vector2 groundBoxOrigin = new Vector2(bounds.center.x, bounds.min.y + 0.06f);
        Vector2 groundBoxSize = new Vector2(bounds.size.x * 0.75f, 0.08f);
        bool grounded = Physics2D.BoxCast(
            groundBoxOrigin,
            groundBoxSize,
            0f,
            Vector2.down,
            0.16f,
            groundLayer
        );

        int desiredWallDir = Mathf.Abs(dir.x) > 0.01f ? (dir.x > 0f ? 1 : -1) : wallRunDirection;
        bool isTouchingWall = IsTouchingWall(bounds, desiredWallDir);

        if (currentState == State.WallSticking)
        {
            bool stillOnWall = IsTouchingWall(bounds, wallRunDirection);
            bool hitCeiling = Physics2D.BoxCast(
                new Vector2(bounds.center.x, bounds.max.y - 0.05f),
                new Vector2(bounds.size.x * 0.6f, 0.08f),
                0f,
                Vector2.up,
                0.2f,
                groundLayer
            );

            // If the enemy ran all the way up past the top of the wall/ledge, vault onto the platform!
            if (!stillOnWall && Time.time > wallRunStartTime + 0.1f)
            {
                currentState = State.Normal;
                if (rb != null)
                {
                    rb.gravityScale = defaultGravityScale;
                    rb.linearVelocity = new Vector2(wallRunDirection * CurrentMoveSpeed * 1.6f, jumpForce * 0.45f);
                }
                lastJumpTime = Time.time;
                nextJumpTime = Time.time + jumpCooldown;
                return;
            }

            // If the wall-run timer finished or the enemy hit a ceiling, telegraph and dive at the player
            if (Time.time >= nextJumpTime || hitCeiling)
            {
                StartCoroutine(TelegraphAndDive());
            }
            return;
        }

        if (currentState == State.Telegraphing || currentState == State.Diving)
        {
            if (currentState == State.Diving && grounded && Time.time > lastJumpTime + 0.15f)
            {
                currentState = State.Normal;
                jumpsLeft = maxJumps;
            }
            return;
        }

        if (grounded && Time.time > lastJumpTime + 0.1f)
        {
            jumpsLeft = maxJumps;
            if (currentState != State.Normal && currentState != State.Dodging)
            {
                currentState = State.Normal;
            }

            // If player is on a lower floor and we are on a drop-through platform, drop down!
            Transform playerTarget = playerController != null ? playerController.transform : (PlayerController.Instance != null ? PlayerController.Instance.transform : null);
            if (playerTarget != null && playerTarget.position.y < transform.position.y - 1.2f && Time.time > lastJumpTime + 0.35f)
            {
                if (TryDropThroughPlatform())
                {
                    lastJumpTime = Time.time;
                    return;
                }
            }
        }

        // Start wall-running when contacting a wall in the direction of the player
        if (isTouchingWall && Time.time >= wallRunStartTime + 0.35f)
        {
            wallRunDirection = desiredWallDir;
            wallRunStartTime = Time.time;
            currentState = State.WallSticking;
            nextJumpTime = Time.time + 1.1f;
            if (rb != null)
            {
                rb.gravityScale = 0f;
                float climbSpeed = Mathf.Max(CurrentMoveSpeed * 1.6f, 4.2f);
                rb.linearVelocity = new Vector2(wallRunDirection * 1.5f, climbSpeed);
            }
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

    public virtual bool TryDropThroughPlatform()
    {
        if (col == null) return false;
        Vector2 checkPos = new Vector2(col.bounds.center.x, col.bounds.min.y - 0.05f);
        Collider2D[] hits = Physics2D.OverlapCircleAll(checkPos, 0.25f, groundLayer);
        foreach (Collider2D hit in hits)
        {
            if (hit == null || hit.isTrigger) continue;
            Platformer platform = hit.GetComponentInParent<Platformer>();
            if (platform != null)
            {
                platform.DropThrough(gameObject);
                return true;
            }
        }
        return false;
    }

    protected bool IsTouchingWall(Bounds bounds, int horizontalDir)
    {
        if (horizontalDir == 0) horizontalDir = 1;
        Vector2 castDir = new Vector2(Mathf.Sign(horizontalDir), 0f);
        Vector2 castSize = new Vector2(0.08f, bounds.size.y * 0.65f);
        float castDist = bounds.extents.x + Mathf.Max(0.15f, wallCheckDistance * 0.5f);

        return Physics2D.BoxCast(bounds.center, castSize, 0f, castDir, castDist, groundLayer);
    }

    /// <summary>
    /// Smoothly steps up low obstacles, curbs, and stairs without requiring the enemy to jump.
    /// Also handles downward stair snapping when descending stairs so pursuit remains continuous.
    /// </summary>
    protected virtual void HandleStepClimb(float moveDirX)
    {
        if (col == null || rb == null) return;
        if (Mathf.Abs(moveDirX) < 0.05f) return;
        if (rb.linearVelocityY > 0.25f) return; // Don't step-climb during upward jumps/dives

        Bounds bounds = col.bounds;
        float halfWidth = bounds.extents.x;
        float currentFootY = bounds.min.y;
        float dirX = Mathf.Sign(moveDirX);

        // 1. Check for a low vertical step / obstacle at foot level
        float checkDist = halfWidth + stepCheckDistance;
        Vector2 footOrigin = new Vector2(bounds.center.x, currentFootY + 0.05f);
        RaycastHit2D footHit = Physics2D.Raycast(footOrigin, Vector2.right * dirX, checkDist, groundLayer);

        if (footHit.collider != null && !footHit.collider.isTrigger && !Physics2D.GetIgnoreCollision(col, footHit.collider))
        {
            if (footHit.normal.y < 0.7f)
            {
                float distFromFront = Mathf.Abs(footHit.point.x - (bounds.center.x + dirX * halfWidth));
                if (distFromFront <= 0.14f)
                {
                    // 2. Check if the space above maxStepHeight is clear (ensures it's a step/stair, not a full wall)
                    Vector2 waistOrigin = new Vector2(bounds.center.x, currentFootY + maxStepHeight + 0.06f);
                    RaycastHit2D waistHit = Physics2D.Raycast(waistOrigin, Vector2.right * dirX, checkDist, groundLayer);

                    if (waistHit.collider == null || waistHit.collider.isTrigger || Physics2D.GetIgnoreCollision(col, waistHit.collider))
                    {
                        // 3. Probe downward onto the step to find its walkable surface height
                        float probeX = footHit.point.x + (dirX * 0.15f);
                        Vector2 probeOrigin = new Vector2(probeX, currentFootY + maxStepHeight + 0.08f);
                        RaycastHit2D surfaceHit = Physics2D.Raycast(probeOrigin, Vector2.down, maxStepHeight + 0.12f, groundLayer);

                        if (surfaceHit.collider != null && !surfaceHit.collider.isTrigger && !Physics2D.GetIgnoreCollision(col, surfaceHit.collider))
                        {
                            if (surfaceHit.normal.y >= 0.55f)
                            {
                                float stepHeight = surfaceHit.point.y - currentFootY;
                                if (stepHeight > 0.01f && stepHeight <= maxStepHeight)
                                {
                                    // 4. Verify clearance for the enemy's body above the step
                                    Vector2 targetCenter = (Vector2)bounds.center + new Vector2(dirX * 0.04f, stepHeight + 0.02f);
                                    Vector2 bodySize = new Vector2(bounds.size.x * 0.85f, bounds.size.y * 0.85f);
                                    Collider2D overlap = Physics2D.OverlapBox(targetCenter, bodySize, 0f, groundLayer);

                                    if (overlap == null || overlap == col || overlap.isTrigger || Physics2D.GetIgnoreCollision(col, overlap))
                                    {
                                        // 5. Smoothly step up onto the stair!
                                        rb.position += new Vector2(dirX * 0.04f, stepHeight + 0.02f);
                                        if (rb.linearVelocityY < 0f)
                                        {
                                            rb.linearVelocity = new Vector2(rb.linearVelocityX, 0f);
                                        }
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // 6. Step Down Snapping
        if (rb.linearVelocityY <= 0.05f)
        {
            Vector2 downProbeOrigin = new Vector2(bounds.center.x + (dirX * 0.12f), currentFootY + 0.08f);
            RaycastHit2D downHit = Physics2D.Raycast(downProbeOrigin, Vector2.down, maxStepHeight + 0.15f, groundLayer);

            if (downHit.collider != null && !downHit.collider.isTrigger && !Physics2D.GetIgnoreCollision(col, downHit.collider))
            {
                if (downHit.normal.y >= 0.55f)
                {
                    float dropDist = currentFootY - downHit.point.y;
                    if (dropDist > 0.04f && dropDist <= maxStepHeight)
                    {
                        rb.position -= new Vector2(0f, dropDist - 0.01f);
                        rb.linearVelocity = new Vector2(rb.linearVelocityX, 0f);
                    }
                }
            }
        }
    }

    protected virtual IEnumerator TelegraphAndDive()
    {
        currentState = State.Telegraphing;
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
        }

        if (showTelegraphColor && spriteRenderer != null && !isTarget)
        {
            spriteRenderer.color = telegraphColor;
        }

        yield return new WaitForSeconds(windUpDuration);

        if (spriteRenderer != null && !isTarget)
        {
            spriteRenderer.color = originalColor;
        }

        if (rb != null)
        {
            rb.gravityScale = defaultGravityScale;
        }

        if (IsGrappleStunned || IsDead) yield break;

        currentState = State.Diving;
        lastJumpTime = Time.time;
        if (playerController != null && rb != null)
        {
            Vector2 diveDir = (playerController.transform.position - transform.position).normalized;
            if (diveDir.sqrMagnitude < 0.01f) diveDir = new Vector2(-wallRunDirection, 0.5f).normalized;
            rb.linearVelocity = diveDir * jumpForce * 1.5f;

            if (diveDir.y < -0.2f)
            {
                TryDropThroughPlatform();
            }
        }
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

        if (currentState == State.WallSticking || currentState == State.Telegraphing)
        {
            // Rotate only the child SpriteRenderer (not the root Collider2D) so feet press against the wall and the ninja runs upward
            transform.rotation = Quaternion.identity;
            spriteRenderer.flipX = wallRunDirection < 0;
            spriteRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, wallRunDirection * 90f);
        }
        else
        {
            transform.rotation = Quaternion.identity;
            spriteRenderer.transform.localRotation = Quaternion.identity;

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

        if (rb != null)
        {
            rb.gravityScale = defaultGravityScale;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.transform.localRotation = Quaternion.identity;
            if (!isTarget)
            {
                spriteRenderer.color = originalColor;
            }
        }

        rb.linearVelocity = pullForce;
    }

    public virtual void OnGrappleKicked(Vector2 kickImpulse)
    {
        currentState = State.Normal;
        grappleStunTimer = grappleStunDuration * 2f;
        wasKickedByPlayer = true;

        if (rb != null)
        {
            rb.gravityScale = defaultGravityScale;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.transform.localRotation = Quaternion.identity;
            if (!isTarget)
            {
                spriteRenderer.color = originalColor;
            }
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