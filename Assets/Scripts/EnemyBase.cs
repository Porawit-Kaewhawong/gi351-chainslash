using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public abstract class EnemyBase : MonoBehaviour, IPointerEnterHandler, ISlashable
{
    #region Enums & Constants

    public enum State
    {
        Normal,
        WallSticking,
        CeilingRunning,
        Telegraphing,
        Diving,
        Dodging
    }

    protected static readonly ContactFilter2D FlockingFilter = new ContactFilter2D { useTriggers = false };
    protected static readonly Collider2D[] FlockingBuffer = new Collider2D[8];
    private static readonly Collider2D[] PlatformOverlapBuffer = new Collider2D[8];

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

    #endregion

    #region Serialized Fields

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

    [Header("AI & Pathfinding Settings")]
    [Tooltip("Enable intelligent obstacle leaping, gap jumping, and platform pathing.")]
    [SerializeField] protected bool smartPathfinding = true;
    [Tooltip("Distance ahead to look for gaps or pits.")]
    [SerializeField] protected float ledgeCheckDistance = 0.45f;
    [Tooltip("Maximum gap distance the enemy will attempt to jump across.")]
    [SerializeField] protected float maxLeapGapDistance = 3.2f;
    [Tooltip("Horizontal separation radius between adjacent enemies to prevent clustering.")]
    [SerializeField] protected float flockingSeparationRadius = 0.75f;

    [Header("Wall & Ceiling Running Settings")]
    [Tooltip("Enable running along ceilings when reaching the top of a wall or jumping into a ceiling.")]
    [SerializeField] protected bool canCeilingRun = true;
    [Tooltip("Upward cling force applied while ceiling running to keep feet locked against the ceiling.")]
    [SerializeField] protected float ceilingClingForce = 3.5f;
    [Tooltip("Maximum duration the enemy will run along a ceiling before diving at the player.")]
    [SerializeField] protected float maxCeilingRunDuration = 2.4f;
    [Tooltip("Speed multiplier while running on the ceiling.")]
    [SerializeField] protected float ceilingRunSpeedMultiplier = 1.4f;

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

    #endregion

    #region Public Properties

    public bool IsDead { get; protected set; }
    public bool IsHeavyEnemy => isHeavyEnemy;
    public bool IsGrappleStunned => grappleStunTimer > 0f;
    public bool IsTarget => isTarget;
    public State CurrentState => currentState;

    protected virtual float CurrentMoveSpeed => moveSpeed;

    protected static Camera MainCamera
    {
        get
        {
            if (cachedMainCamera == null)
            {
                cachedMainCamera = Camera.main;
            }
            return cachedMainCamera;
        }
    }

    #endregion

    #region Protected & Private State

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
    protected int ceilingRunDirection = 1;
    protected float ceilingRunStartTime;
    protected bool wasCeilingRunning;
    protected float defaultGravityScale = 1f;
    protected float stuckTimer = 0f;

    protected Rigidbody2D rb;
    protected Collider2D col;
    protected SpriteRenderer spriteRenderer;
    protected PixelSpriteEffect pixelEffect;
    protected PlayerController playerController;
    protected Color originalColor = Color.white;

    private Material originalSpriteMaterial;
    private Vector3 originalSpriteLocalPos;
    private GameObject outlineContainer;
    private SpriteRenderer[] outlineRenderers;

    private static Material sharedOutlineMaterial;
    private static Camera cachedMainCamera;

    #endregion

    #region Unity Lifecycle

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            defaultGravityScale = rb.gravityScale;
        }

        Collider2D[] colliders = GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D c = colliders[i];
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
        for (int i = 0; i < allRenderers.Length; i++)
        {
            SpriteRenderer sr = allRenderers[i];
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
            if (!spriteRenderer.TryGetComponent<PixelSpriteEffect>(out pixelEffect))
            {
                pixelEffect = spriteRenderer.gameObject.AddComponent<PixelSpriteEffect>();
            }

            if (pixelEffect != null)
            {
                pixelEffect.OutlineColor = targetableOutlineColor;
                pixelEffect.OutlineThickness = targetableOutlineThickness > 0.5f ? targetableOutlineThickness : 1.2f;
            }
            else
            {
                CreateSpriteOutlineIndicator();
            }
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
        }
    }

    #endregion

    #region Movement & AI

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

            case State.CeilingRunning:
                if (rb != null)
                {
                    rb.gravityScale = 0f;
                    float speedX = speed * ceilingRunSpeedMultiplier;
                    // Upward cling velocity keeps feet locked against the ceiling physics
                    rb.linearVelocity = new Vector2(ceilingRunDirection * speedX, ceilingClingForce);
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
                    rb.AddForce(dir * (speed * 2.5f));
                }
                break;

            case State.Normal:
            case State.Dodging:
                if (rb != null)
                {
                    rb.gravityScale = defaultGravityScale;

                    // Flocking separation from other nearby enemies to prevent sprite clumping
                    float separation = CalculateSeparationOffset();
                    float moveDirX = dir.x + (separation * 0.4f);
                    if (Mathf.Abs(moveDirX) > 0.02f)
                    {
                        moveDirX = Mathf.Sign(moveDirX);
                    }

                    // Stuck detection: trying to move horizontally but blocked
                    if (Mathf.Abs(moveDirX) > 0.1f && Mathf.Abs(rb.linearVelocityX) < 0.15f)
                    {
                        stuckTimer += Time.fixedDeltaTime;
                    }
                    else
                    {
                        stuckTimer = Mathf.Max(0f, stuckTimer - Time.fixedDeltaTime * 2f);
                    }

                    float targetVelX = moveDirX * speed;
                    float newVelX = Mathf.MoveTowards(rb.linearVelocityX, targetVelX, speed * 8f * Time.fixedDeltaTime);
                    rb.linearVelocity = new Vector2(newVelX, rb.linearVelocityY);

                    if (Mathf.Abs(moveDirX) > 0.05f)
                    {
                        HandleStepClimb(moveDirX);
                    }
                }
                break;
        }

        HandleJump(dir);
    }

    protected virtual void HandleJump(Vector2 dir)
    {
        if (col == null || rb == null) return;
        Bounds bounds = col.bounds;

        bool grounded = CheckGrounded(bounds);

        int desiredWallDir = Mathf.Abs(dir.x) > 0.01f ? (dir.x > 0f ? 1 : -1) : wallRunDirection;
        bool isTouchingWall = IsTouchingWall(bounds, desiredWallDir);

        if (currentState == State.WallSticking)
        {
            bool stillOnWall = IsTouchingWall(bounds, wallRunDirection);
            bool hitCeiling = IsTouchingCeiling(bounds);

            // Transition from wall-run to ceiling-run when reaching a ceiling!
            if (hitCeiling && canCeilingRun)
            {
                int roomDir = -wallRunDirection;
                StartCeilingRun(roomDir);
                return;
            }

            // If the enemy ran all the way up past the top of the wall/ledge, vault onto the platform!
            if (!stillOnWall && Time.time > wallRunStartTime + 0.1f)
            {
                currentState = State.Normal;
                rb.gravityScale = defaultGravityScale;
                rb.linearVelocity = new Vector2(wallRunDirection * CurrentMoveSpeed * 1.6f, jumpForce * 0.45f);
                lastJumpTime = Time.time;
                nextJumpTime = Time.time + jumpCooldown;
                return;
            }

            // If the wall-run timer finished or hit a ceiling without ceiling run
            if (Time.time >= nextJumpTime || hitCeiling)
            {
                StartCoroutine(TelegraphAndDive());
            }
            return;
        }

        if (currentState == State.CeilingRunning)
        {
            bool stillOnCeiling = IsTouchingCeiling(bounds);
            bool hitWallAhead = IsTouchingWall(bounds, ceilingRunDirection);

            // Ambush dive when directly overhead of the player!
            bool directlyOverhead = false;
            Transform targetPlayer = playerController != null ? playerController.transform : (PlayerController.Instance != null ? PlayerController.Instance.transform : null);
            if (targetPlayer != null)
            {
                float dx = Mathf.Abs(targetPlayer.position.x - transform.position.x);
                float dy = transform.position.y - targetPlayer.position.y;
                if (dx < 1.35f && dy > 1.0f)
                {
                    directlyOverhead = true;
                }
            }

            // If the ceiling ended, or hit an opposing wall, or timed out, or directly overhead: drop-kick dive!
            if (!stillOnCeiling || hitWallAhead || directlyOverhead || Time.time >= nextJumpTime)
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

        Transform playerTarget = playerController != null ? playerController.transform : (PlayerController.Instance != null ? PlayerController.Instance.transform : null);
        float targetDeltaY = playerTarget != null ? (playerTarget.position.y - transform.position.y) : 0f;
        float targetDeltaX = playerTarget != null ? (playerTarget.position.x - transform.position.x) : 0f;

        if (grounded && Time.time > lastJumpTime + 0.1f)
        {
            jumpsLeft = maxJumps;
            if (currentState != State.Normal && currentState != State.Dodging)
            {
                currentState = State.Normal;
            }

            // If player is on a lower floor and we are on a drop-through platform, drop down!
            if (targetDeltaY < -1.2f && Time.time > lastJumpTime + 0.35f)
            {
                if (TryDropThroughPlatform())
                {
                    lastJumpTime = Time.time;
                    return;
                }
            }
        }

        // Start wall-running when contacting a tall wall in the direction of the player
        if (isTouchingWall && Time.time >= wallRunStartTime + 0.35f)
        {
            wallRunDirection = desiredWallDir;
            wallRunStartTime = Time.time;
            currentState = State.WallSticking;
            nextJumpTime = Time.time + 1.1f;
            rb.gravityScale = 0f;
            float climbSpeed = Mathf.Max(CurrentMoveSpeed * 1.6f, 4.2f);
            rb.linearVelocity = new Vector2(wallRunDirection * 1.5f, climbSpeed);
            return;
        }

        // --- SMART JUMP DECISIONS ---
        if (grounded && Time.time >= nextJumpTime && jumpsLeft > 0)
        {
            bool shouldJump = false;
            float jumpMultiplier = 1f;
            float forwardBoost = 0f;

            if (smartPathfinding)
            {
                // 1. Gap / Pit ahead
                bool atLedge = DetectLedgeOrGap(bounds, dir.x, out bool canLeap, out float gapWidth);
                if (atLedge)
                {
                    if (targetDeltaY >= -0.8f && canLeap)
                    {
                        shouldJump = true;
                        jumpMultiplier = 1.1f;
                        forwardBoost = dir.x * CurrentMoveSpeed * 0.8f;
                    }
                    else if (targetDeltaY < -1.0f)
                    {
                        // Player is below - smoothly run/walk off ledge to drop down without jumping!
                        shouldJump = false;
                    }
                    else if (!canLeap)
                    {
                        // Dead drop or bottomless chasm with no landing
                        shouldJump = false;
                    }
                }

                // 2. Obstacle ahead higher than step-climb height
                if (!shouldJump && DetectObstacleAhead(bounds, dir.x, out bool isTallWall))
                {
                    if (isTallWall)
                    {
                        // Jump towards the wall to latch on
                        shouldJump = true;
                        jumpMultiplier = 0.9f;
                    }
                    else
                    {
                        // Low obstacle/hurdle, jump over it
                        shouldJump = true;
                        jumpMultiplier = 1.0f;
                    }
                }

                // 3. Player is on a higher platform
                if (!shouldJump && targetDeltaY > 0.85f && Mathf.Abs(targetDeltaX) < 4.5f)
                {
                    shouldJump = true;
                    jumpMultiplier = Mathf.Clamp(0.9f + targetDeltaY * 0.12f, 1f, 1.45f);
                }

                // 4. Stuck recovery
                if (!shouldJump && stuckTimer >= 0.35f)
                {
                    shouldJump = true;
                    jumpMultiplier = 1.1f;
                    stuckTimer = 0f;
                }
            }
            else
            {
                // Fallback to legacy jump
                shouldJump = true;
            }

            if (shouldJump)
            {
                Jump(jumpMultiplier);
                if (Mathf.Abs(forwardBoost) > 0.01f)
                {
                    rb.linearVelocity = new Vector2(rb.linearVelocityX + forwardBoost, rb.linearVelocityY);
                }
                nextJumpTime = Time.time + jumpCooldown;
                return;
            }
        }

        // Jump onto ceiling if touching overhead ceiling while airborne
        if (!grounded && canCeilingRun && IsTouchingCeiling(bounds) && Time.time > lastJumpTime + 0.15f)
        {
            int toPlayerDirX = (playerTarget != null && playerTarget.position.x < transform.position.x) ? -1 : 1;
            StartCeilingRun(toPlayerDirX);
            return;
        }

        // --- AIR NAVIGATION & DOUBLE JUMP ---
        if (!grounded && jumpsLeft > 0 && Time.time >= lastJumpTime + doubleJumpDelay && !isTouchingWall)
        {
            // Only double jump if falling or at apex, and still need vertical lift to reach the target/platform
            if (rb.linearVelocityY < 1.2f && (targetDeltaY > 0.3f || isTouchingWall || stuckTimer > 0.2f))
            {
                Jump(0.95f);
            }
        }
    }

    protected virtual bool CheckGrounded(Bounds bounds)
    {
        Vector2 groundBoxOrigin = new Vector2(bounds.center.x, bounds.min.y + 0.06f);
        Vector2 groundBoxSize = new Vector2(bounds.size.x * 0.75f, 0.08f);
        return Physics2D.BoxCast(
            groundBoxOrigin,
            groundBoxSize,
            0f,
            Vector2.down,
            0.16f,
            groundLayer
        );
    }

    protected virtual bool DetectLedgeOrGap(Bounds bounds, float moveDirX, out bool canLeap, out float gapWidth)
    {
        canLeap = false;
        gapWidth = 0f;
        if (Mathf.Abs(moveDirX) < 0.05f) return false;

        float dirX = Mathf.Sign(moveDirX);
        float footY = bounds.min.y;
        float probeX = bounds.center.x + (dirX * (bounds.extents.x + ledgeCheckDistance));

        // 1. Check if ground exists immediately ahead of the front foot
        Vector2 probeOrigin = new Vector2(probeX, footY + 0.1f);
        RaycastHit2D groundAhead = Physics2D.Raycast(probeOrigin, Vector2.down, 1.4f, groundLayer);

        if (groundAhead.collider != null && !groundAhead.collider.isTrigger)
        {
            return false; // Ground is present, not a ledge
        }

        // 2. A ledge or gap is detected! Probe forward to find a walkable surface across the gap
        for (float dist = 0.8f; dist <= maxLeapGapDistance; dist += 0.45f)
        {
            Vector2 leapProbe = new Vector2(probeX + (dirX * dist), footY + 0.35f);
            RaycastHit2D landingHit = Physics2D.Raycast(leapProbe, Vector2.down, 1.9f, groundLayer);
            if (landingHit.collider != null && !landingHit.collider.isTrigger)
            {
                canLeap = true;
                gapWidth = dist;
                return true;
            }
        }

        return true;
    }

    protected virtual bool DetectObstacleAhead(Bounds bounds, float moveDirX, out bool isTallWall)
    {
        isTallWall = false;
        if (Mathf.Abs(moveDirX) < 0.05f) return false;

        float dirX = Mathf.Sign(moveDirX);
        float checkDist = bounds.extents.x + Mathf.Max(0.2f, wallCheckDistance * 0.5f);

        // Check at waist level (above step-climb height)
        Vector2 waistOrigin = new Vector2(bounds.center.x, bounds.min.y + maxStepHeight + 0.15f);
        RaycastHit2D waistHit = Physics2D.Raycast(waistOrigin, Vector2.right * dirX, checkDist, groundLayer);

        if (waistHit.collider == null || waistHit.collider.isTrigger)
        {
            return false;
        }

        // Check at head level to see if it extends into a tall wall
        Vector2 headOrigin = new Vector2(bounds.center.x, bounds.max.y + 0.3f);
        RaycastHit2D headHit = Physics2D.Raycast(headOrigin, Vector2.right * dirX, checkDist, groundLayer);
        isTallWall = headHit.collider != null && !headHit.collider.isTrigger;

        return true;
    }

    protected virtual float CalculateSeparationOffset()
    {
        if (flockingSeparationRadius <= 0f) return 0f;

        int count = Physics2D.OverlapCircle(transform.position, flockingSeparationRadius, FlockingFilter, FlockingBuffer);
        float separation = 0f;

        for (int i = 0; i < count; i++)
        {
            Collider2D otherCol = FlockingBuffer[i];
            if (otherCol == null || otherCol == col || otherCol.isTrigger) continue;

            if (otherCol.TryGetComponent<EnemyBase>(out EnemyBase otherEnemy) && !otherEnemy.IsDead)
            {
                float deltaX = transform.position.x - otherEnemy.transform.position.x;
                if (Mathf.Abs(deltaX) < 0.01f)
                {
                    deltaX = (GetInstanceID() > otherEnemy.GetInstanceID()) ? 0.1f : -0.1f;
                }
                separation += Mathf.Sign(deltaX) * Mathf.Clamp01(1f - (Mathf.Abs(deltaX) / flockingSeparationRadius));
            }
        }

        return Mathf.Clamp(separation, -1f, 1f);
    }

    public virtual bool TryDropThroughPlatform()
    {
        if (col == null) return false;

        Bounds bounds = col.bounds;
        Vector2 checkPos = new Vector2(bounds.center.x, bounds.min.y - 0.05f);
        ContactFilter2D platformFilter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = true,
            layerMask = groundLayer
        };

        int hitCount = Physics2D.OverlapCircle(checkPos, 0.25f, platformFilter, PlatformOverlapBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = PlatformOverlapBuffer[i];
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

    protected virtual void StartCeilingRun(int runDirection)
    {
        currentState = State.CeilingRunning;
        ceilingRunDirection = runDirection != 0 ? runDirection : 1;
        ceilingRunStartTime = Time.time;
        nextJumpTime = Time.time + maxCeilingRunDuration;
        wasCeilingRunning = true;

        if (rb != null)
        {
            rb.gravityScale = 0f;
            float speedX = CurrentMoveSpeed * ceilingRunSpeedMultiplier;
            rb.linearVelocity = new Vector2(ceilingRunDirection * speedX, ceilingClingForce);
        }
    }

    protected bool IsTouchingCeiling(Bounds bounds)
    {
        Vector2 boxOrigin = new Vector2(bounds.center.x, bounds.max.y - 0.04f);
        Vector2 boxSize = new Vector2(bounds.size.x * 0.7f, 0.08f);
        return Physics2D.BoxCast(boxOrigin, boxSize, 0f, Vector2.up, 0.22f, groundLayer);
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
            rb.linearVelocity = diveDir * (jumpForce * 1.5f);

            if (diveDir.y < -0.2f)
            {
                TryDropThroughPlatform();
            }
        }
    }

    protected virtual void Jump()
    {
        Jump(1f);
    }

    protected virtual void Jump(float multiplier)
    {
        if (rb == null) return;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * multiplier);
        jumpsLeft--;
        lastJumpTime = Time.time;
    }

    #endregion

    #region Combat & Targeting

    public virtual void OnPointerEnter(PointerEventData eventData)
    {
        TrySelectTarget();
    }

    protected virtual void CheckMouseHover()
    {
        if (isTarget || PlayerController.Instance == null || !PlayerController.Instance.IsInMode) return;

        Camera cam = MainCamera;
        if (cam != null && Mouse.current != null && col != null)
        {
            Vector2 mouseWorldPos = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
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

            if (pixelEffect != null)
            {
                pixelEffect.SetOutline(true, targetableOutlineColor, 2f, PixelSpriteEffect.OutlineMode.Both);
                pixelEffect.Flash(0.08f, Color.white);
            }
            else if (spriteRenderer != null)
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

    /// <summary>
    /// Triggers an arcade damage or impact flash using the Pixel2D shader.
    /// </summary>
    public virtual void TriggerHitFlash(float duration = 0.12f, Color? flashColor = null)
    {
        if (pixelEffect != null)
        {
            pixelEffect.Flash(duration, flashColor ?? Color.white);
        }
    }

    public abstract void OnDeath(Vector2 deathImpulse = default);
    public virtual void OnSlashed(Vector2 slashVector) => OnDeath(slashVector);

    public virtual void OnGrappled(Vector2 pullForce)
    {
        currentState = State.Normal;
        grappleStunTimer = grappleStunDuration * 1.5f;
        wasKickedByPlayer = false;

        if (rb != null)
        {
            rb.gravityScale = defaultGravityScale;
            rb.linearVelocity = pullForce;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.transform.localRotation = Quaternion.identity;
            if (!isTarget)
            {
                spriteRenderer.color = originalColor;
            }
        }

        TriggerHitFlash(0.12f, Color.white);
    }

    public virtual void OnGrappleKicked(Vector2 kickImpulse)
    {
        currentState = State.Normal;
        grappleStunTimer = grappleStunDuration * 2f;
        wasKickedByPlayer = true;

        if (rb != null)
        {
            rb.gravityScale = defaultGravityScale;
            rb.linearVelocity = kickImpulse;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.transform.localRotation = Quaternion.identity;
            if (!isTarget)
            {
                spriteRenderer.color = originalColor;
            }
        }

        TriggerHitFlash(0.18f, new Color(1f, 0.9f, 0.35f, 1f));
    }

    #endregion

    #region Collision & Physics

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

    // Add these alongside OnCollisionEnter2D inside EnemyBase.cs to support Trigger Colliders:
    protected virtual void OnTriggerEnter2D(Collider2D other)
    {
        HandlePlayerTriggerContact(other);
    }

    protected virtual void OnTriggerStay2D(Collider2D other)
    {
        HandlePlayerTriggerContact(other);
    }

    protected virtual bool HandleThrownImpact(Collision2D collision)
    {
        if (!IsGrappleStunned || !wasKickedByPlayer) return false;

        float impactSpeedSq = collision.relativeVelocity.sqrMagnitude;
        if (impactSpeedSq < wallSlamSpeedThreshold * wallSlamSpeedThreshold) return false;

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

    #endregion

    #region Visuals & Helpers

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
            sr.sharedMaterial = sharedOutlineMaterial != null ? sharedOutlineMaterial : spriteRenderer.sharedMaterial;
            sr.sprite = spriteRenderer.sprite;
            sr.color = targetableOutlineColor;
            sr.sortingLayerID = spriteRenderer.sortingLayerID;
            sr.sortingOrder = spriteRenderer.sortingOrder - 1;
            outlineRenderers[i] = sr;
        }

        outlineContainer.SetActive(false);
    }

    protected virtual void UpdateTargetIndicator()
    {
        bool inCombatMode = PlayerController.Instance != null && PlayerController.Instance.IsInMode;

        if (!inCombatMode && isTarget)
        {
            isTarget = false;
            if (pixelEffect != null)
            {
                pixelEffect.SetOutline(false);
            }
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
            if (pixelEffect != null)
            {
                pixelEffect.SetOutline(true, targetableOutlineColor, 2f, PixelSpriteEffect.OutlineMode.Both);
            }
            else if (sharedOutlineMaterial != null && spriteRenderer.sharedMaterial != sharedOutlineMaterial)
            {
                spriteRenderer.sharedMaterial = sharedOutlineMaterial;
            }
            spriteRenderer.color = targetableOutlineColor;

            Vector2 jitterOffset = Random.insideUnitCircle * targetJitterStrength;
            spriteRenderer.transform.localPosition = originalSpriteLocalPos + new Vector3(jitterOffset.x, jitterOffset.y, 0f);
        }

        bool showOutline = inCombatMode && !isTarget;
        if (pixelEffect != null)
        {
            if (pixelEffect.OutlineEnabled != showOutline)
            {
                pixelEffect.SetOutline(showOutline, targetableOutlineColor, 1.2f, PixelSpriteEffect.OutlineMode.Outer);
            }
        }
        else if (outlineContainer != null && outlineContainer.activeSelf != showOutline)
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
        else if (currentState == State.CeilingRunning)
        {
            transform.rotation = Quaternion.identity;
            spriteRenderer.flipX = ceilingRunDirection < 0;
            spriteRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        }
        else
        {
            transform.rotation = Quaternion.identity;
            spriteRenderer.transform.localRotation = Quaternion.identity;

            if (rb != null)
            {
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
    }

    private bool hasSpawnedDeathSlice;
    private static Sprite cachedPixel1x1Sprite;
    private static Sprite cachedPixel2x2Sprite;
    private static Sprite cachedPixel3x3Sprite;
    private static Sprite[] cachedPixelSlashFrames;

    /// <summary>
    /// Spawns a sliced-in-half sprite split VFX + glowing slash seam & pixel sparks when this enemy dies.
    /// Safe to call multiple times; executes at most once per enemy instance.
    /// </summary>
    public void TrySpawnDeathSlice(Vector2 slashDir = default)
    {
        if (hasSpawnedDeathSlice) return;
        if (this is BossTargetPoint || this is ArmoredNinjaWeakPoint) return;
        if (spriteRenderer == null || spriteRenderer.sprite == null || spriteRenderer.sprite.texture == null) return;

        hasSpawnedDeathSlice = true;

        if (slashDir.sqrMagnitude < 0.001f)
        {
            if (playerController != null)
            {
                slashDir = ((Vector2)transform.position - (Vector2)playerController.transform.position).normalized;
            }
            if (slashDir.sqrMagnitude < 0.001f)
            {
                float angle = Random.Range(20f, 55f) * Mathf.Deg2Rad;
                slashDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
        }
        else
        {
            slashDir.Normalize();
        }

        EnemyDeathSliceVFX.Spawn(spriteRenderer, slashDir, originalColor);
    }

    internal static Vector3 SnapToPixelGrid(Vector3 pos, float ppu = 16f)
    {
        float inv = 1f / Mathf.Max(1f, ppu);
        return new Vector3(
            Mathf.Round(pos.x * ppu) * inv,
            Mathf.Round(pos.y * ppu) * inv,
            pos.z
        );
    }

    internal static float QuantizeStep(float val, int steps = 4)
    {
        return Mathf.Round(Mathf.Clamp01(val) * steps) / steps;
    }

    internal static Sprite GetSharedPixelSquareSprite()
    {
        return GetSharedPixelBlockSprite(2);
    }

    internal static Sprite GetSharedPixelBlockSprite(int sizePx)
    {
        sizePx = Mathf.Clamp(sizePx, 1, 3);
        if (sizePx == 1 && cachedPixel1x1Sprite != null) return cachedPixel1x1Sprite;
        if (sizePx == 2 && cachedPixel2x2Sprite != null) return cachedPixel2x2Sprite;
        if (sizePx == 3 && cachedPixel3x3Sprite != null) return cachedPixel3x3Sprite;

        Texture2D tex = new Texture2D(sizePx, sizePx, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        Color[] cols = new Color[sizePx * sizePx];
        for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
        tex.SetPixels(cols);
        tex.Apply();

        Sprite spr = Sprite.Create(tex, new Rect(0f, 0f, sizePx, sizePx), new Vector2(0.5f, 0.5f), 16f);
        if (sizePx == 1) cachedPixel1x1Sprite = spr;
        else if (sizePx == 2) cachedPixel2x2Sprite = spr;
        else cachedPixel3x3Sprite = spr;
        return spr;
    }

    /// <summary>
    /// Generates a 3-frame 24x24 pixel-art diagonal slash animation (FilterMode.Point, axis-aligned stair-stepped pixels).
    /// </summary>
    internal static Sprite[] GetSharedPixelSlashFrames()
    {
        if (cachedPixelSlashFrames != null && cachedPixelSlashFrames.Length == 3 && cachedPixelSlashFrames[0] != null)
        {
            return cachedPixelSlashFrames;
        }

        const int size = 24;
        cachedPixelSlashFrames = new Sprite[3];
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color coreWhite = Color.white;
        Color edgeCyan = new Color(0.35f, 0.96f, 1f, 1f);

        for (int f = 0; f < 3; f++)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            for (int i = 2; i < size - 2; i++)
            {
                // Frame 2 breaks into dashed pixel segments
                if (f == 2 && (i % 4 == 0 || i % 4 == 1)) continue;

                int x = i;
                int y = (size / 2 - 5) + (i / 2);
                if (x >= 0 && x < size && y >= 0 && y < size)
                {
                    px[y * size + x] = (f == 2) ? edgeCyan : coreWhite;
                    if (f == 0)
                    {
                        if (y + 1 < size) px[(y + 1) * size + x] = edgeCyan;
                        if (y - 1 >= 0) px[(y - 1) * size + x] = edgeCyan;
                    }
                    else if (f == 1 && (i > 5 && i < size - 5))
                    {
                        if (y + 1 < size) px[(y + 1) * size + x] = edgeCyan;
                    }
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            cachedPixelSlashFrames[f] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }

        return cachedPixelSlashFrames;
    }

    #endregion
}

/// <summary>
/// Self-contained pixel-art sliced-in-half enemy death effect: splits the enemy sprite into top and bottom
/// halves that flash white, step apart on the pixel grid (without sub-pixel rotation), and burst into pixel blocks.
/// </summary>
public class EnemyDeathSliceVFX : MonoBehaviour
{
    private SpriteRenderer topRenderer;
    private SpriteRenderer bottomRenderer;
    private SpriteRenderer seamRenderer;
    private Sprite topSprite;
    private Sprite bottomSprite;

    private Vector2 topPos;
    private Vector2 bottomPos;
    private Vector2 topVelocity;
    private Vector2 bottomVelocity;
    private Color baseColor;

    private Transform[] sparkTransforms;
    private SpriteRenderer[] sparkRenderers;
    private Vector2[] sparkPositions;
    private Vector2[] sparkVelocities;

    private float elapsed;
    private const float TotalDuration = 0.42f;
    private const float FlashDuration = 0.065f;

    public static void Spawn(SpriteRenderer source, Vector2 slashDir, Color baseTint)
    {
        if (source == null || source.sprite == null || source.sprite.texture == null) return;

        Sprite orig = source.sprite;
        Rect r = orig.textureRect;
        if (r.width < 2f || r.height < 2f) return;

        GameObject root = new GameObject("EnemyDeathSliceVFX");
        root.transform.position = EnemyBase.SnapToPixelGrid(source.bounds.center);

        EnemyDeathSliceVFX vfx = root.AddComponent<EnemyDeathSliceVFX>();
        vfx.Initialize(source, orig, r, slashDir, baseTint);
    }

    private void Initialize(SpriteRenderer source, Sprite orig, Rect r, Vector2 slashDir, Color baseTint)
    {
        baseColor = baseTint.a > 0.1f ? baseTint : Color.white;

        float halfH = Mathf.Floor(r.height * 0.5f);
        Rect bottomRect = new Rect(r.x, r.y, r.width, halfH);
        Rect topRect = new Rect(r.x, r.y + halfH, r.width, r.height - halfH);

        float ppu = Mathf.Max(1f, orig.pixelsPerUnit);
        topSprite = Sprite.Create(orig.texture, topRect, new Vector2(0.5f, 0f), ppu);
        bottomSprite = Sprite.Create(orig.texture, bottomRect, new Vector2(0.5f, 1f), ppu);

        Vector3 lossyScale = source.transform.lossyScale;

        // Keep Quaternion.identity so pixel art stays strictly axis-aligned (no sub-pixel rotation shearing)
        topRenderer = CreateHalfObject("TopHalf", topSprite, source, lossyScale);
        bottomRenderer = CreateHalfObject("BottomHalf", bottomSprite, source, lossyScale);

        float dirSignX = slashDir.x >= 0f ? 1f : -1f;
        topVelocity = new Vector2(dirSignX * Random.Range(2.2f, 3.4f), Random.Range(1.8f, 2.8f));
        bottomVelocity = new Vector2(-dirSignX * Random.Range(1.0f, 1.8f), Random.Range(-0.8f, -0.2f));

        // 3-frame pixel-art slash seam sprite (axis-aligned, flipped to match slash quadrant)
        GameObject seamObj = new GameObject("PixelSlashSeam");
        seamObj.transform.SetParent(transform, false);
        seamObj.transform.localScale = Vector3.one * 1.5f;

        Sprite[] slashFrames = EnemyBase.GetSharedPixelSlashFrames();
        seamRenderer = seamObj.AddComponent<SpriteRenderer>();
        seamRenderer.sprite = slashFrames[0];
        seamRenderer.flipX = slashDir.x < 0f;
        seamRenderer.flipY = slashDir.y < 0f;
        seamRenderer.sortingLayerID = source.sortingLayerID;
        seamRenderer.sortingOrder = source.sortingOrder + 3;

        // Chunky axis-aligned 1x1 and 2x2 pixel sparks
        int sparkCount = 8;
        sparkTransforms = new Transform[sparkCount];
        sparkRenderers = new SpriteRenderer[sparkCount];
        sparkPositions = new Vector2[sparkCount];
        sparkVelocities = new Vector2[sparkCount];

        for (int i = 0; i < sparkCount; i++)
        {
            GameObject spark = new GameObject("PixelSlashSpark");
            spark.transform.SetParent(transform, false);
            spark.transform.localScale = Vector3.one;
            spark.transform.localRotation = Quaternion.identity;

            float sign = (i % 2 == 0) ? 1f : -1f;
            Vector2 dir = (slashDir * sign + Random.insideUnitCircle * 0.55f).normalized;
            sparkVelocities[i] = dir * Random.Range(4.0f, 8.5f);

            SpriteRenderer sr = spark.AddComponent<SpriteRenderer>();
            sr.sprite = EnemyBase.GetSharedPixelBlockSprite((i % 2 == 0) ? 2 : 1);
            sr.sortingLayerID = source.sortingLayerID;
            sr.sortingOrder = source.sortingOrder + 4;
            sr.color = (i % 3 == 0) ? new Color(0.3f, 0.95f, 1f, 1f) : new Color(1f, 0.25f, 0.35f, 1f);

            sparkTransforms[i] = spark.transform;
            sparkRenderers[i] = sr;
        }
    }

    private SpriteRenderer CreateHalfObject(string objName, Sprite spr, SpriteRenderer source, Vector3 worldScale)
    {
        GameObject halfObj = new GameObject(objName);
        halfObj.transform.SetParent(transform, false);
        halfObj.transform.localPosition = Vector3.zero;
        halfObj.transform.localRotation = Quaternion.identity;
        halfObj.transform.localScale = worldScale;

        SpriteRenderer sr = halfObj.AddComponent<SpriteRenderer>();
        sr.sprite = spr;
        sr.flipX = source.flipX;
        sr.flipY = source.flipY;
        sr.sortingLayerID = source.sortingLayerID;
        sr.sortingOrder = source.sortingOrder + 1;
        sr.color = Color.white;
        return sr;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime > 0f ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : Time.deltaTime;
        elapsed += dt;

        float t = Mathf.Clamp01(elapsed / TotalDuration);

        // Apply gravity & drag, then snap positions to 16 PPU pixel grid
        topVelocity.y -= 9.5f * dt;
        bottomVelocity.y -= 12f * dt;
        topVelocity *= (1f - 3.5f * dt);
        bottomVelocity *= (1f - 3.5f * dt);

        topPos += topVelocity * dt;
        bottomPos += bottomVelocity * dt;

        if (topRenderer != null)
        {
            topRenderer.transform.localPosition = EnemyBase.SnapToPixelGrid(topPos);
        }

        if (bottomRenderer != null)
        {
            bottomRenderer.transform.localPosition = EnemyBase.SnapToPixelGrid(bottomPos);
        }

        // Quantized 4-step retro color & alpha fade
        Color c;
        if (elapsed < FlashDuration)
        {
            c = new Color(0.85f, 1f, 1f, 1f);
        }
        else
        {
            float fadeT = EnemyBase.QuantizeStep((elapsed - FlashDuration) / (TotalDuration - FlashDuration), 4);
            c = Color.Lerp(Color.white, new Color(1f, 0.25f, 0.3f, 0f), fadeT);
            c.r *= baseColor.r;
            c.g *= baseColor.g;
            c.b *= baseColor.b;
            c.a = 1f - fadeT;
        }

        if (topRenderer != null) topRenderer.color = c;
        if (bottomRenderer != null) bottomRenderer.color = c;

        // 3-frame stepped pixel slash seam animation
        if (seamRenderer != null)
        {
            if (elapsed >= 0.15f)
            {
                seamRenderer.enabled = false;
            }
            else
            {
                Sprite[] frames = EnemyBase.GetSharedPixelSlashFrames();
                int frameIdx = Mathf.Clamp(Mathf.FloorToInt((elapsed / 0.15f) * 3f), 0, 2);
                seamRenderer.sprite = frames[frameIdx];
            }
        }

        // Pixel-snapped sparks update
        float quantAlpha = 1f - EnemyBase.QuantizeStep(t, 4);
        if (sparkTransforms != null)
        {
            for (int i = 0; i < sparkTransforms.Length; i++)
            {
                if (sparkTransforms[i] == null) continue;
                sparkPositions[i] += sparkVelocities[i] * dt;
                sparkVelocities[i] *= (1f - 6f * dt);
                sparkTransforms[i].localPosition = EnemyBase.SnapToPixelGrid(sparkPositions[i]);
                Color sc = sparkRenderers[i].color;
                sc.a = quantAlpha;
                sparkRenderers[i].color = sc;
            }
        }

        if (elapsed >= TotalDuration)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (topSprite != null) Destroy(topSprite);
        if (bottomSprite != null) Destroy(bottomSprite);
    }
}
