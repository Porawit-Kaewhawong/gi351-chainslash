using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class LegendarySamurai : EnemyBase
{
    public enum BossActionState
    {
        Idle,
        Moving,
        SlashDash,
        Stomp,
        MultipleTarget,
        Shuriken360,
        Staggered,
        Defeated
    }

    [Header("Boss General Stats")]
    [SerializeField] private int bossMaxHealth = 5;
    [SerializeField] private float bossPatrolSpeed = 3.0f;
    [SerializeField] private float timeBetweenAttacks = 2.0f;
    [SerializeField] private AudioClip bossRoarSFX;
    [SerializeField] private AudioClip bossDefeatSFX;
    [SerializeField] private GameObject bossDefeatVFX;

    [Header("Pattern 1: Slash-Dash Settings")]
    [SerializeField] private float slashDashSpeed = 22f;
    [SerializeField] private float slashDashDuration = 0.45f;
    [SerializeField] private float slashDashWindup = 0.6f;
    [SerializeField] private float slashDashRecovery = 0.5f;
    [SerializeField] private float slashDashDamage = 25f;
    [SerializeField] private Color slashDashTelegraphColor = Color.red;
    [SerializeField] private AudioClip slashDashSFX;
    [SerializeField] private GameObject slashDashVFX;

    [Header("Pattern 2: Stomp Settings")]
    [SerializeField] private float stompJumpForce = 22f;
    [SerializeField] private float stompAirHangDuration = 0.45f;
    [SerializeField] private float stompSlamSpeed = 28f;
    [SerializeField] private float stompRadius = 5.5f;
    [SerializeField] private float stompDamage = 28f;
    [SerializeField] private float stompKnockback = 12f;
    [SerializeField] private float stompShakeDuration = 0.45f;
    [SerializeField] private float stompRecovery = 0.8f;
    [SerializeField] private AudioClip stompImpactSFX;
    [SerializeField] private GameObject stompShockwaveVFX;

    [Header("Pattern 3: Multiple Target Settings")]
    [SerializeField] private int targetPointCount = 5;
    [SerializeField] private float targetPointOrbitRadius = 2.4f;
    [SerializeField] private float targetPointOrbitSpeed = 40f;
    [SerializeField] private float multipleTargetTimeout = 7.0f;
    [SerializeField] private float staggeredDuration = 2.5f;
    [SerializeField] private GameObject targetPointPrefab;
    [SerializeField] private AudioClip barrierBreakSFX;
    [SerializeField] private AudioClip barrierReflectSFX;
    [SerializeField] private GameObject shieldBarrierVFX;
    [Tooltip("How many full rounds of slashing all target points the player must complete before the boss takes damage. E.g. 10 means the player must slash all 5 points 10 times.")]
    [SerializeField] private int requiredSlashRounds = 1;
    [SerializeField] private float roundResetDelay = 0.35f;
    [SerializeField] private AudioClip roundCompleteSFX;

    [Header("Pattern 4: 360 Shuriken Settings")]
    [SerializeField] private GameObject shurikenPrefab;
    [SerializeField] private int shurikenCount360 = 12;
    [SerializeField] private float shurikenSpeed360 = 9f;
    [SerializeField] private float shurikenDamage360 = 10f;
    [SerializeField] private float shurikenWindup = 0.5f;
    [SerializeField] private AudioClip shurikenBurstSFX;

    [Header("Boss Events")]
    public UnityEvent<int, int> onBossHealthChanged;
    public UnityEvent onBossDefeated;

    public int CurrentBossHealth => currentHealth;
    public int MaxBossHealth => bossMaxHealth;
    public BossActionState CurrentBossState => bossState;

    private int currentHealth;
    private BossActionState bossState = BossActionState.Idle;
    private bool isMultipleTargetActive;
    private int slashedPointsInCurrentPhase;
    private int completedSlashRounds;
    private float currentOrbitAngle;
    private bool isDashingSlash;

    private List<BossTargetPoint> targetPoints = new List<BossTargetPoint>();
    private Coroutine aiLoopCoroutine;
    private Coroutine currentActionCoroutine;
    private Coroutine flashCoroutine;

    protected override void Awake()
    {
        base.Awake();

        // The Legendary Samurai is a heavy, immovable boss
        isHeavyEnemy = true;
        moveSpeed = bossPatrolSpeed;
        bossMaxHealth = Mathf.Max(1, bossMaxHealth);
        currentHealth = bossMaxHealth;
    }

    protected override void Start()
    {
        base.Start();

        SpawnOrInitializeTargetPoints();

        if (bossRoarSFX != null && playerController != null)
        {
            playerController.PlaySFX(bossRoarSFX);
        }

        onBossHealthChanged?.Invoke(currentHealth, bossMaxHealth);

        aiLoopCoroutine = StartCoroutine(BossAILoop());
    }

    private void SpawnOrInitializeTargetPoints()
    {
        targetPoints.Clear();

        for (int i = 0; i < targetPointCount; i++)
        {
            BossTargetPoint point;

            if (targetPointPrefab != null)
            {
                GameObject obj = Instantiate(targetPointPrefab, transform.position, Quaternion.identity);
                point = obj.GetComponent<BossTargetPoint>();
                if (point == null) point = obj.AddComponent<BossTargetPoint>();
            }
            else
            {
                GameObject obj = new GameObject($"BossTargetPoint_{i}");
                obj.layer = gameObject.layer;
                obj.tag = gameObject.tag;

                CircleCollider2D circleCol = obj.AddComponent<CircleCollider2D>();
                circleCol.radius = 0.45f;
                circleCol.isTrigger = true;

                Rigidbody2D kinematicRb = obj.AddComponent<Rigidbody2D>();
                kinematicRb.bodyType = RigidbodyType2D.Kinematic;
                kinematicRb.gravityScale = 0f;

                SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
                sr.sprite = BossTargetPoint.GetOrCreateTargetPointSprite();
                sr.sortingOrder = 7;

                point = obj.AddComponent<BossTargetPoint>();
            }

            point.Initialize(this, i);
            point.DeactivatePoint();
            targetPoints.Add(point);
        }
    }

    protected override void Update()
    {
        if (IsDead) return;

        // If health reaches zero at any point, defeat the boss and trigger death immediately!
        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        base.Update();

        // Smoothly orbit active target points around the boss
        if (isMultipleTargetActive && targetPoints.Count > 0)
        {
            UpdateTargetPointsOrbit();
        }
    }

    private void UpdateTargetPointsOrbit()
    {
        currentOrbitAngle += targetPointOrbitSpeed * Time.deltaTime;

        for (int i = 0; i < targetPoints.Count; i++)
        {
            if (targetPoints[i] == null || targetPoints[i].IsSlashed) continue;

            float angle = currentOrbitAngle + i * (360f / targetPointCount);
            float rad = angle * Mathf.Deg2Rad;
            Vector2 offset = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * targetPointOrbitRadius;

            targetPoints[i].transform.position = (Vector2)transform.position + offset;
        }
    }

    #region Boss AI Loop & Attack Patterns

    private IEnumerator BossAILoop()
    {
        yield return new WaitForSeconds(1.0f);

        int attackPatternIndex = 0;

        while (!IsDead && currentHealth > 0)
        {
            // Patrol / Chase towards player between attacks
            bossState = BossActionState.Moving;
            float idleTimer = 0f;

            while (idleTimer < timeBetweenAttacks && !IsDead && currentHealth > 0)
            {
                idleTimer += Time.deltaTime;
                yield return null;
            }

            if (IsDead || currentHealth <= 0) yield break;

            // Cycle through the 4 attack patterns:
            // 0: Slash-Dash
            // 1: Stomp
            // 2: 360 Shuriken
            // 3: Multiple Target (Vulnerable Phase)
            switch (attackPatternIndex % 4)
            {
                case 0:
                    currentActionCoroutine = StartCoroutine(SlashDashRoutine());
                    break;
                case 1:
                    currentActionCoroutine = StartCoroutine(StompRoutine());
                    break;
                case 2:
                    currentActionCoroutine = StartCoroutine(Shuriken360Routine());
                    break;
                case 3:
                    currentActionCoroutine = StartCoroutine(MultipleTargetRoutine());
                    break;
            }

            attackPatternIndex++;

            if (currentActionCoroutine != null)
            {
                yield return currentActionCoroutine;
                currentActionCoroutine = null;
            }

            yield return null;
        }
    }

    #region Pattern 1: Slash-Dash

    private IEnumerator SlashDashRoutine()
    {
        bossState = BossActionState.SlashDash;
        rb.linearVelocity = Vector2.zero;

        // Telegraph charge
        if (spriteRenderer != null) spriteRenderer.color = slashDashTelegraphColor;

        if (playerController != null && spriteRenderer != null)
        {
            float dirX = playerController.transform.position.x - transform.position.x;
            if (Mathf.Abs(dirX) > 0.1f)
            {
                spriteRenderer.flipX = dirX < 0f;
            }
        }

        yield return new WaitForSeconds(slashDashWindup);

        if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;
        if (IsDead || currentHealth <= 0) yield break;

        // Dash toward player
        Vector2 dashDir = playerController != null
            ? ((Vector2)playerController.transform.position - (Vector2)transform.position).normalized
            : (spriteRenderer != null && spriteRenderer.flipX ? Vector2.left : Vector2.right);

        if (slashDashSFX != null && playerController != null)
        {
            playerController.PlaySFX(slashDashSFX);
        }

        if (slashDashVFX != null)
        {
            Instantiate(slashDashVFX, transform.position, Quaternion.identity);
        }

        isDashingSlash = true;
        float dashTimer = 0f;

        while (dashTimer < slashDashDuration && !IsDead && currentHealth > 0)
        {
            dashTimer += Time.deltaTime;
            rb.linearVelocity = dashDir * slashDashSpeed;
            yield return null;
        }

        isDashingSlash = false;
        rb.linearVelocity = Vector2.zero;

        yield return new WaitForSeconds(slashDashRecovery);
        if (!IsDead && currentHealth > 0) bossState = BossActionState.Idle;
    }

    #endregion

    #region Pattern 2: Stomp

    private IEnumerator StompRoutine()
    {
        bossState = BossActionState.Stomp;
        rb.linearVelocity = Vector2.zero;

        // 1. Jump high into the air
        rb.linearVelocity = new Vector2(0f, stompJumpForce);

        float ascendTimer = 0f;
        while (ascendTimer < 0.55f && !IsDead && currentHealth > 0)
        {
            ascendTimer += Time.deltaTime;
            // Track towards player X position while ascending
            if (playerController != null)
            {
                float targetX = playerController.transform.position.x;
                float newX = Mathf.MoveTowards(transform.position.x, targetX, 5.5f * Time.deltaTime);
                transform.position = new Vector3(newX, transform.position.y, transform.position.z);
            }
            yield return null;
        }

        // 2. Hang at peak with warning flash
        float origGrav = rb.gravityScale;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;

        if (spriteRenderer != null) spriteRenderer.color = Color.red;

        yield return new WaitForSeconds(stompAirHangDuration);

        if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;
        if (IsDead || currentHealth <= 0) { rb.gravityScale = origGrav; yield break; }

        // 3. Slam straight down
        rb.gravityScale = origGrav;
        rb.linearVelocity = new Vector2(0f, -stompSlamSpeed);

        bool hasHitGround = false;
        float timeout = 2.0f;
        float elapsed = 0f;

        while (!hasHitGround && elapsed < timeout && !IsDead && currentHealth > 0)
        {
            elapsed += Time.deltaTime;
            Bounds bounds = col.bounds;
            bool groundCheck = Physics2D.BoxCast(bounds.center, new Vector2(bounds.size.x * 0.85f, 0.1f), 0f, Vector2.down, 0.35f, groundLayer);

            if (groundCheck && rb.linearVelocityY <= 0.1f)
            {
                hasHitGround = true;
            }
            yield return null;
        }

        // 4. Ground impact & area shockwave
        if (!IsDead && currentHealth > 0)
        {
            ExecuteStompImpact();
        }

        // 5. Recovery on ground
        yield return new WaitForSeconds(stompRecovery);
        if (!IsDead && currentHealth > 0) bossState = BossActionState.Idle;
    }

    private void ExecuteStompImpact()
    {
        rb.linearVelocity = Vector2.zero;

        if (playerController != null)
        {
            playerController.TriggerCameraShake(stompShakeDuration);
            if (stompImpactSFX != null)
            {
                playerController.PlaySFX(stompImpactSFX);
            }
        }

        if (stompShockwaveVFX != null)
        {
            Instantiate(stompShockwaveVFX, transform.position, Quaternion.identity);
        }

        // Deal surrounding area damage
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, stompRadius);
        foreach (Collider2D hit in hitColliders)
        {
            if (hit.gameObject == gameObject) continue;

            if (hit.TryGetComponent<PlayerController>(out PlayerController player) ||
                (hit.GetComponentInParent<PlayerController>() is PlayerController p && (player = p) != null))
            {
                player.TakeDamage(stompDamage);

                if (hit.TryGetComponent<Rigidbody2D>(out Rigidbody2D prb))
                {
                    Vector2 pushDir = ((Vector2)player.transform.position - (Vector2)transform.position).normalized;
                    if (pushDir == Vector2.zero) pushDir = Vector2.up;
                    pushDir.y = Mathf.Max(pushDir.y, 0.45f);
                    prb.AddForce(pushDir.normalized * stompKnockback, ForceMode2D.Impulse);
                }
            }
        }
    }

    #endregion

    #region Pattern 3: Multiple Target

    private IEnumerator MultipleTargetRoutine()
    {
        bossState = BossActionState.MultipleTarget;
        isMultipleTargetActive = true;
        slashedPointsInCurrentPhase = 0;
        completedSlashRounds = 0;
        rb.linearVelocity = Vector2.zero;

        if (shieldBarrierVFX != null) shieldBarrierVFX.SetActive(true);
        if (spriteRenderer != null) spriteRenderer.color = new Color(0.7f, 0.4f, 1f, 1f);

        ActivateAllTargetPoints();

        // Compute a generous timeout: base timeout * number of rounds required
        float totalTimeout = multipleTargetTimeout * Mathf.Max(1, requiredSlashRounds);

        float phaseTimer = 0f;
        while (phaseTimer < totalTimeout && isMultipleTargetActive && !IsDead && currentHealth > 0)
        {
            phaseTimer += Time.deltaTime;
            yield return null;
        }

        // If time expired without completing all required slash rounds
        if (isMultipleTargetActive && !IsDead && currentHealth > 0)
        {
            isMultipleTargetActive = false;
            DeactivateAllTargetPoints();
            if (shieldBarrierVFX != null) shieldBarrierVFX.SetActive(false);
            if (barrierReflectSFX != null && playerController != null)
            {
                playerController.PlaySFX(barrierReflectSFX);
            }
            if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;
        }

        yield return new WaitForSeconds(1.0f);
        if (!IsDead && currentHealth > 0) bossState = BossActionState.Idle;
    }

    private void ActivateAllTargetPoints()
    {
        currentOrbitAngle = 0f;

        for (int i = 0; i < targetPoints.Count; i++)
        {
            if (targetPoints[i] == null) continue;

            float angle = i * (360f / targetPointCount);
            float rad = angle * Mathf.Deg2Rad;
            Vector2 offset = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * targetPointOrbitRadius;

            targetPoints[i].ActivatePoint((Vector2)transform.position + offset);
        }
    }

    private void DeactivateAllTargetPoints()
    {
        for (int i = 0; i < targetPoints.Count; i++)
        {
            if (targetPoints[i] != null)
            {
                targetPoints[i].DeactivatePoint();
            }
        }
    }

    public void OnTargetPointSlashed(BossTargetPoint point, Vector2 slashImpulse)
    {
        if (!isMultipleTargetActive || IsDead) return;

        slashedPointsInCurrentPhase++;

        // When all 5 target points in this round are slashed by the player's sword
        if (slashedPointsInCurrentPhase >= targetPointCount)
        {
            completedSlashRounds++;

            // Check if the player has completed all required rounds
            if (completedSlashRounds >= requiredSlashRounds)
            {
                // All rounds completed - boss barrier shatters & boss takes damage!
                isMultipleTargetActive = false;
                DeactivateAllTargetPoints();
                if (shieldBarrierVFX != null) shieldBarrierVFX.SetActive(false);

                TakeBossDamage(1, slashImpulse);

                if (currentHealth > 0 && !IsDead)
                {
                    if (currentActionCoroutine != null) StopCoroutine(currentActionCoroutine);
                    currentActionCoroutine = StartCoroutine(StaggeredRoutine());
                }
            }
            else
            {
                // Round completed but more rounds needed - reactivate all target points for the next round
                slashedPointsInCurrentPhase = 0;

                // Play round-complete feedback
                if (roundCompleteSFX != null && playerController != null)
                {
                    playerController.PlaySFX(roundCompleteSFX);
                }
                if (playerController != null)
                {
                    playerController.TriggerCameraShake(0.15f);
                }

                // Brief flash to indicate progress, then reactivate points
                StartCoroutine(ReactivatePointsAfterDelay());
            }
        }
    }

    private IEnumerator ReactivatePointsAfterDelay()
    {
        // Quick visual pulse on the boss to show a round was completed
        if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.white;
        }

        yield return new WaitForSeconds(roundResetDelay);

        if (IsDead || !isMultipleTargetActive) yield break;

        if (spriteRenderer != null)
        {
            spriteRenderer.color = new Color(0.7f, 0.4f, 1f, 1f);
        }

        // Reactivate all target points for the next round
        ActivateAllTargetPoints();
    }

    private IEnumerator StaggeredRoutine()
    {
        bossState = BossActionState.Staggered;
        rb.linearVelocity = Vector2.zero;

        if (barrierBreakSFX != null && playerController != null)
        {
            playerController.PlaySFX(barrierBreakSFX);
        }

        if (playerController != null)
        {
            playerController.TriggerCameraShake(0.35f);
        }

        float staggerTimer = 0f;
        while (staggerTimer < staggeredDuration && !IsDead && currentHealth > 0)
        {
            staggerTimer += Time.deltaTime;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = (Mathf.Sin(staggerTimer * 22f) > 0) ? Color.white : new Color(1f, 0.85f, 0.25f, 1f);
            }
            yield return null;
        }

        if (spriteRenderer != null && !isTarget)
        {
            spriteRenderer.color = originalColor;
        }

        if (!IsDead && currentHealth > 0) bossState = BossActionState.Idle;
    }

    #endregion

    #region Pattern 4: 360 Shuriken

    private IEnumerator Shuriken360Routine()
    {
        bossState = BossActionState.Shuriken360;
        rb.linearVelocity = Vector2.zero;

        // Windup charge
        if (spriteRenderer != null) spriteRenderer.color = Color.yellow;
        yield return new WaitForSeconds(shurikenWindup);
        if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;
        if (IsDead || currentHealth <= 0) yield break;

        // Fire 1st burst of 360 shurikens
        if (shurikenBurstSFX != null && playerController != null)
        {
            playerController.PlaySFX(shurikenBurstSFX);
        }

        Fire360Burst(shurikenCount360, 0f);

        // Fire 2nd spiral wave offset by angle
        yield return new WaitForSeconds(0.22f);
        if (!IsDead && currentHealth > 0)
        {
            Fire360Burst(shurikenCount360, (360f / shurikenCount360) * 0.5f);
        }

        yield return new WaitForSeconds(0.75f);
        if (!IsDead && currentHealth > 0) bossState = BossActionState.Idle;
    }

    private void Fire360Burst(int count, float angleOffset)
    {
        float angleStep = 360f / count;

        for (int i = 0; i < count; i++)
        {
            float angle = i * angleStep + angleOffset;
            float rad = angle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector2 spawnPos = (Vector2)transform.position + dir * 0.75f;

            if (shurikenPrefab != null)
            {
                GameObject proj = Instantiate(shurikenPrefab, spawnPos, Quaternion.identity);
                if (proj.TryGetComponent<Shuriken>(out Shuriken shurikenComp))
                {
                    shurikenComp.Launch(dir, shurikenSpeed360, shurikenDamage360);
                }
            }
            else
            {
                Shuriken.CreateDefaultShuriken(spawnPos, dir, shurikenSpeed360, shurikenDamage360, groundLayer);
            }
        }
    }

    #endregion

    #endregion

    #region Boss Damage & Defeat

    public void TakeDamage(float amount)
    {
        TakeBossDamage(Mathf.Max(1, Mathf.RoundToInt(amount)));
    }

    public void TakeDamage(int amount)
    {
        TakeBossDamage(Mathf.Max(1, amount));
    }

    public void TakeBossDamage(int amount, Vector2 deathImpulse = default)
    {
        if (IsDead) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        onBossHealthChanged?.Invoke(currentHealth, bossMaxHealth);

        // Immediately trigger boss defeat and death when health reaches zero!
        if (currentHealth <= 0)
        {
            Die(deathImpulse);
        }
    }

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        if (IsDead) return;

        // If health has already reached zero, defeat the boss immediately!
        if (currentHealth <= 0)
        {
            Die(deathImpulse);
            return;
        }

        // If the player slashes the boss during Staggered state or while barrier is down, deal damage
        if (bossState == BossActionState.Staggered || !isMultipleTargetActive)
        {
            TakeBossDamage(1, deathImpulse);
            return;
        }

        // Direct slashes to the boss body while Multiple Target barrier is active deflect off the barrier
        if (barrierReflectSFX != null && playerController != null)
        {
            playerController.PlaySFX(barrierReflectSFX);
        }

        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(DeflectFlashRoutine());
    }

    private IEnumerator DeflectFlashRoutine()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.cyan;
        }
        yield return new WaitForSeconds(0.1f);
        if (spriteRenderer != null && !isTarget)
        {
            spriteRenderer.color = originalColor;
        }
    }

    public void Die(Vector2 deathImpulse = default)
    {
        if (IsDead) return;
        IsDead = true;
        currentHealth = 0;
        bossState = BossActionState.Defeated;

        // Stop all active AI, movement, and attack routines immediately so they cannot override death
        StopAllCoroutines();

        // Disable all colliders so boss cannot hurt player or receive further hits
        Collider2D[] allColliders = GetComponentsInChildren<Collider2D>();
        foreach (var c in allColliders)
        {
            if (c != null) c.enabled = false;
        }

        // Immediately destroy all target point GameObjects so GameManager does not count them
        DestroyAllTargetPoints();

        // Unregister from GameManager so wave/boss clear registers immediately
        if (GameManager.Instance != null)
        {
            GameManager.Instance.UnregisterEnemy(gameObject);
        }

        onBossDefeated?.Invoke();
        onBossHealthChanged?.Invoke(0, bossMaxHealth);

        StartCoroutine(DefeatVisualSequence(deathImpulse));
    }

    private IEnumerator DefeatVisualSequence(Vector2 deathImpulse)
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            if (deathImpulse != default)
            {
                rb.AddForce(deathImpulse * 12f, ForceMode2D.Impulse);
            }
        }

        PlayerController targetPlayer = playerController != null ? playerController : PlayerController.Instance;
        if (targetPlayer != null)
        {
            if (bossDefeatSFX != null)
            {
                targetPlayer.PlaySFX(bossDefeatSFX);
            }
            targetPlayer.TriggerCameraShake(0.8f);
        }

        // Dramatic flashing defeat sequence using unscaledDeltaTime so slow-motion/hitstop never freezes it
        float elapsed = 0f;
        float duration = 1.0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = (Mathf.Sin(elapsed * 25f) > 0) ? Color.red : Color.white;
            }
            yield return null;
        }

        if (bossDefeatVFX != null)
        {
            Instantiate(bossDefeatVFX, transform.position, Quaternion.identity);
        }

        Destroy(gameObject);
    }

    private void DestroyAllTargetPoints()
    {
        for (int i = 0; i < targetPoints.Count; i++)
        {
            if (targetPoints[i] != null && targetPoints[i].gameObject != null)
            {
                Destroy(targetPoints[i].gameObject);
            }
        }
        targetPoints.Clear();
    }

    private void OnDestroy()
    {
        DestroyAllTargetPoints();
    }

    #endregion

    #region Movement & Contact Overrides

    protected override void Movement()
    {
        // Only run base ground movement when in Moving state and alive
        if (IsDead || currentHealth <= 0 || bossState != BossActionState.Moving) return;

        base.Movement();
    }

    protected override void HandlePlayerContact(Collision2D collision)
    {
        if (IsDead || currentHealth <= 0) return;

        // If dashing in SlashDash mode, deal slash dash damage
        if (isDashingSlash)
        {
            if (collision.gameObject.TryGetComponent<PlayerController>(out PlayerController player))
            {
                player.TakeDamage(slashDashDamage);
                if (collision.gameObject.TryGetComponent<Rigidbody2D>(out Rigidbody2D playerRb))
                {
                    Vector2 pushDir = (player.transform.position - transform.position).normalized;
                    if (pushDir == Vector2.zero) pushDir = Vector2.up;
                    playerRb.AddForce(pushDir * stompKnockback, ForceMode2D.Impulse);
                }
                return;
            }
        }

        base.HandlePlayerContact(collision);
    }

    #endregion
}

