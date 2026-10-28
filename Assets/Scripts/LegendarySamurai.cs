using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class LegendarySamurai : EnemyBase
{
    public enum BossActionState
    {
        Idle,
        Moving,
        DashSlash,
        SlashDash = DashSlash,
        Stomp,
        Shuriken360,
        WindSlash,
        HiddenStab,
        Staggered,
        Defeated
    }

    public enum BossAttackPattern
    {
        DashSlash,
        SlashDash = DashSlash,
        Stomp,
        Shuriken360,
        WindSlash,
        HiddenStab
    }

    public enum BossAnimState
    {
        Idle,
        Walk,
        Jump,
        DashSlash,
        Stomp,
        Shuriken360,
        HiddenStab,
        WindSlash
    }

    [Header("Boss Health & Weak Points")]
    [Tooltip("Number of hits before death (Default: 20 hits)")]
    [SerializeField] private int bossMaxHits = 20;
    [Tooltip("Number of circles required to land a single hit (Default: 5)")]
    [SerializeField] private int requiredWeakPoints = 5;
    [SerializeField] private GameObject weakPointPrefab;
    [SerializeField] private float weakPointRadiusMin = 1.8f;
    [SerializeField] private float weakPointRadiusMax = 3.2f;
    [SerializeField] private AudioClip hitDamageSFX;
    [SerializeField] private AudioClip deflectSFX;
    [SerializeField] private GameObject hitVFX;

    [Header("Boss General Stats")]
    [SerializeField] private float bossPatrolSpeed = 3.2f;
    [SerializeField] private float maxTimeBetweenAttacks = 2.0f;
    [SerializeField] private float minTimeBetweenAttacks = 0.7f;
    [SerializeField] private AudioClip bossRoarSFX;
    [SerializeField] private AudioClip bossDefeatSFX;
    [SerializeField] private GameObject bossDefeatVFX;

    [Header("Boss Audio & Volume Sliders")]
    [Tooltip("Master volume slider for all boss SFX (0 = Mute, 1 = 100%, 2 = 200%, 3 = 300%). Drag this in the Inspector to boost boss sound loudness!")]
    [Range(0f, 3f)] [SerializeField] private float bossMasterVolume = 1.8f;
    [Tooltip("Volume multiplier for Slash-Dash attack sound.")]
    [Range(0f, 2f)] [SerializeField] private float slashDashVolume = 1.2f;
    [Tooltip("Volume multiplier for Stomp ground impact sound.")]
    [Range(0f, 2f)] [SerializeField] private float stompVolume = 1.3f;
    [Tooltip("Volume multiplier for 360 Shuriken burst sound.")]
    [Range(0f, 2f)] [SerializeField] private float shurikenBurstVolume = 1.2f;
    [Tooltip("Volume multiplier for Wind Slash blade wave sound.")]
    [Range(0f, 2f)] [SerializeField] private float windSlashVolume = 1.2f;
    [Tooltip("Volume multiplier for Hidden Stab ambush sound.")]
    [Range(0f, 2f)] [SerializeField] private float hiddenStabVolume = 1.3f;
    [Tooltip("Volume multiplier for hit damage & deflect sounds.")]
    [Range(0f, 2f)] [SerializeField] private float hitDeflectVolume = 1.2f;
    [Tooltip("Volume multiplier for Boss Roar sound.")]
    [Range(0f, 2f)] [SerializeField] private float roarVolume = 1.4f;
    [Tooltip("Volume multiplier for Boss Defeat explosion sound.")]
    [Range(0f, 2f)] [SerializeField] private float defeatVolume = 1.5f;

    [Header("Optional Runtime UI Volume Controls")]
    [Tooltip("Optional in-game UI Slider to control boss volume at runtime from the screen/UI.")]
    [SerializeField] private UnityEngine.UI.Slider bossVolumeSlider;
    [Tooltip("If enabled, renders an on-screen volume slider during the boss fight for live testing (toggle with F8).")]
    [SerializeField] private bool showOnScreenSlider = false;

    [Header("Boss Background Music (BGM)")]
    [Tooltip("BGM clip played from 100% health down to 50% health.")]
    [SerializeField] private AudioClip phase1BGM;
    [Tooltip("BGM clip played when health reaches 50% and below.")]
    [SerializeField] private AudioClip phase2BGM;
    [Tooltip("Master volume for Boss BGM.")]
    [Range(0f, 2f)] [SerializeField] private float bossBGMVolume = 0.85f;
    [Tooltip("Crossfade blend duration between Phase 1 and Phase 2 BGM in seconds.")]
    [SerializeField] private float bgmCrossfadeDuration = 1.5f;
    [Tooltip("Whether the boss background music tracks loop continuously.")]
    [SerializeField] private bool loopBGM = true;

    [Header("Visual Dynamic Boss Healthbar")]
    [Tooltip("Reference to custom BossHealthBarUI. If left empty, automatically searches the scene.")]
    [SerializeField] private BossHealthBarUI bossHealthBarUI;

    [Header("Direct Boss Health Bar Elements (Optional Shortcut)")]
    [Tooltip("Direct reference to Title Text if you prefer wiring directly on the boss.")]
    [SerializeField] private TMP_Text directTitleText;
    [Tooltip("Direct reference to Phrase Text if you prefer wiring directly on the boss.")]
    [SerializeField] private TMP_Text directPhraseText;
    [Tooltip("Direct reference to Background Bar if you prefer wiring directly on the boss.")]
    [SerializeField] private GameObject directBackgroundBar;
    [Tooltip("Direct reference to Fill Bar Image if you prefer wiring directly on the boss.")]
    [SerializeField] private Image directFillBar;

    [Header("Pattern 1: Dash-Slash (Phase 1 / 100%)")]
    [Tooltip("Movement speed during the dash phase.")]
    [SerializeField] private float slashDashSpeed = 24f;
    [Tooltip("Duration of the dash toward the player.")]
    [SerializeField] private float slashDashDuration = 0.40f;
    [Tooltip("Windup telegraph duration before the dash starts.")]
    [SerializeField] private float slashDashWindup = 0.50f;
    [Tooltip("Recovery duration after completing the straight-line slash.")]
    [SerializeField] private float slashDashRecovery = 0.45f;
    [Tooltip("Damage dealt if the player is in the way during the dash.")]
    [SerializeField] private float slashDashDamage = 20f;
    [Tooltip("Brief pause after dash before unleashing the straight-line slash.")]
    [SerializeField] private float postDashSlashDelay = 0.12f;
    [Tooltip("Damage dealt by the straight-line slash after dash.")]
    [SerializeField] private float straightSlashDamage = 26f;
    [Tooltip("Reach / length of the straight-line slash in world units.")]
    [SerializeField] private float straightSlashRange = 6.0f;
    [Tooltip("Width of the straight-line slash hitbox in world units.")]
    [SerializeField] private float straightSlashWidth = 1.6f;
    [SerializeField] private Color slashDashTelegraphColor = Color.red;
    [Tooltip("Audio clip played during the dash phase.")]
    [SerializeField] private AudioClip slashDashSFX;
    [Tooltip("Audio clip played for the straight-line slash cut.")]
    [SerializeField] private AudioClip straightSlashSFX;
    [Tooltip("Straight-line slash VFX prefab. If empty, automatically spawns procedural BossStraightSlashVFX.")]
    [SerializeField] private GameObject straightSlashVFX;
    [Tooltip("Optional VFX spawned during dash movement.")]
    [SerializeField] private GameObject slashDashVFX;
    [SerializeField] private TrailRenderer dashTrailRenderer;
    [SerializeField] private Color dashTrailColor = new Color(1f, 0.15f, 0.25f, 0.85f);
    [SerializeField] private float afterImageInterval = 0.045f;

    [Header("Pattern 2: Stomp (Phase 2 / 80%)")]
    [SerializeField] private float stompJumpForce = 22f;
    [SerializeField] private float stompAirHangDuration = 0.45f;
    [SerializeField] private float stompSlamSpeed = 28f;
    [SerializeField] private float stompRadius = 5.5f;
    [SerializeField] private float stompDamage = 28f;
    [SerializeField] private float stompKnockback = 12f;
    [SerializeField] private float stompShakeDuration = 0.45f;
    [SerializeField] private float stompRecovery = 0.7f;
    [SerializeField] private AudioClip stompImpactSFX;
    [SerializeField] private GameObject stompShockwaveVFX;

    [Header("Pattern 3: 360 Shuriken (Phase 3 / 60%)")]
    [SerializeField] private GameObject shurikenPrefab;
    [SerializeField] private int shurikenCount360 = 14;
    [SerializeField] private float shurikenSpeed360 = 9f;
    [SerializeField] private float shurikenDamage360 = 10f;
    [SerializeField] private float shurikenWindup = 0.45f;
    [SerializeField] private AudioClip shurikenBurstSFX;

    [Header("Pattern 4: Wind Slash (Phase 4 / 40%)")]
    [SerializeField] private int windSlashLineCount = 3;
    [SerializeField] private float windSlashTelegraphTime = 0.45f;
    [SerializeField] private float windSlashDamage = 26f;
    [SerializeField] private Color windSlashTelegraphColor = new Color(0.2f, 1f, 0.6f, 0.5f);
    [SerializeField] private Color windSlashActiveColor = Color.white;
    [SerializeField] private AudioClip windSlashSFX;

    [Header("Pattern 5: Hidden Stab (Phase 5 / 20%)")]
    [SerializeField] private float hiddenStabVanishDuration = 0.38f;
    [SerializeField] private float hiddenStabWindup = 0.28f;
    [SerializeField] private float hiddenStabSpeed = 30f;
    [SerializeField] private float hiddenStabDamage = 32f;
    [SerializeField] private Color hiddenStabGlintColor = Color.red;
    [SerializeField] private AudioClip hiddenStabSFX;

    [Header("Phrase-Based Minion Spawner")]
    [Tooltip("List of enemy prefabs that can be randomly chosen to spawn when a phrase passes.")]
    [SerializeField] private List<GameObject> minionPrefabs = new List<GameObject>();
    [Tooltip("Self-set amount of minions to spawn for each phrase. Index 0 = Phrase 1 (0), Index 1 = Phrase 2 (2), Index 2 = Phrase 3 (3), Index 3 = Phrase 4 (4), Index 4 = Phrase 5 (5).")]
    [SerializeField] private int[] minionsPerPhrase = new int[] { 0, 2, 3, 4, 5 };
    [Tooltip("Audio clip played when minions are summoned upon entering a new phrase.")]
    [SerializeField] private AudioClip minionSummonSFX;

    [Header("Boss Sprite Animations (No Animator)")]
    [Tooltip("Idle / Standing animation sprite frames.")]
    [SerializeField] private List<Sprite> idleSprites = new List<Sprite>();
    [SerializeField] private float idleFPS = 8f;

    [Tooltip("Walk / Patrol animation sprite frames.")]
    [SerializeField] private List<Sprite> walkSprites = new List<Sprite>();
    [SerializeField] private float walkFPS = 8f;

    [Tooltip("Jump / Airborne animation sprite frames.")]
    [SerializeField] private List<Sprite> jumpSprites = new List<Sprite>();
    [SerializeField] private float jumpFPS = 8f;

    [Tooltip("Dash-Slash attack animation sprite frames.")]
    [SerializeField] private List<Sprite> dashSlashSprites = new List<Sprite>();
    [SerializeField] private float dashSlashFPS = 12f;

    [Tooltip("Stomp attack animation sprite frames.")]
    [SerializeField] private List<Sprite> stompSprites = new List<Sprite>();
    [SerializeField] private float stompFPS = 10f;

    [Tooltip("360 Shuriken burst attack animation sprite frames.")]
    [SerializeField] private List<Sprite> shuriken360Sprites = new List<Sprite>();
    [SerializeField] private float shuriken360FPS = 12f;

    [Tooltip("Hidden Stab ambush attack animation sprite frames.")]
    [SerializeField] private List<Sprite> hiddenStabSprites = new List<Sprite>();
    [SerializeField] private float hiddenStabFPS = 14f;

    [Tooltip("Wind Slash blade wave attack animation sprite frames (Optional).")]
    [SerializeField] private List<Sprite> windSlashSprites = new List<Sprite>();
    [SerializeField] private float windSlashFPS = 12f;

    [Header("Boss Events")]
    public UnityEvent<int, int> onBossHealthChanged;
    public UnityEvent onBossDefeated;

    public int CurrentBossHealth => currentHits;
    public int MaxBossHealth => bossMaxHits;
    public int CurrentPhase => GetCurrentPhase();
    public BossActionState CurrentBossState => bossState;
    public bool WasHitLandedInCurrentSlash => hitDeductedInCurrentSlash;
    public BossAnimState CurrentAnimationState => currentAnimState;
    public int CurrentAnimationFrame => currentAnimFrame;

    private int currentHits;
    private BossActionState bossState = BossActionState.Idle;
    private bool areWeakPointsActive = false;
    private int slashedCirclesCount = 0;
    private float modeExitTimer = 0f;
    private bool isDashingSlash = false;
    private bool hitDeductedInCurrentSlash = false;
    private bool isStompSlamming = false;
    private bool stompHasHitGround = false;
    private float lastHitDamageTime = -10f;
    private const float MIN_HIT_DAMAGE_INTERVAL = 0.20f;
    private readonly List<GameObject> activeSpawnedMinions = new List<GameObject>();

    // Sprite Animation State
    private BossAnimState currentAnimState = BossAnimState.Idle;
    private int currentAnimFrame = 0;
    private float animTimer = 0f;
    private bool isPlayingActionAnim = false;
    private bool actionAnimLoop = false;
    private Sprite defaultBaseSprite;

    private readonly List<BossTargetPoint> targetPoints = new List<BossTargetPoint>();
    private Transform weakPointContainer;
    private Coroutine aiLoopCoroutine;
    private Coroutine currentActionCoroutine;
    private Coroutine flashCoroutine;
    private BossAttackPattern lastAttackPattern = (BossAttackPattern)(-1);

    protected override void Awake()
    {
        base.Awake();

        // Disable Animator if present so code sprite animation has full control
        Animator anim = GetComponent<Animator>();
        if (anim != null) anim.enabled = false;

        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            defaultBaseSprite = spriteRenderer.sprite;
        }

        isHeavyEnemy = true;
        dieOnHighSpeedWallSlam = false;
        moveSpeed = bossPatrolSpeed;
        bossMaxHits = Mathf.Max(1, bossMaxHits);
        currentHits = bossMaxHits;

        SetupDashTrail();
        SetupBossAudio();
        SetupBossBGM();

#if UNITY_EDITOR
        if (minionPrefabs == null || minionPrefabs.Count == 0)
        {
            minionPrefabs = new List<GameObject>();
            string[] defaultEnemyPaths = new string[]
            {
                "Assets/Prefabs/Enemies/SuicideNinja.prefab",
                "Assets/Prefabs/Enemies/Ninja.prefab",
                "Assets/Prefabs/Enemies/ThrowerNinja.prefab",
                "Assets/Prefabs/Enemies/ArmoredNinja.prefab"
            };
            foreach (string path in defaultEnemyPaths)
            {
                GameObject p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (p != null) minionPrefabs.Add(p);
            }
        }
#endif
    }

    protected override void Start()
    {
        base.Start();

        CreateWeakPointContainer();

        PlayBossSFX(bossRoarSFX, 1f, roarVolume);
        StartBossBGM();

        if (bossHealthBarUI == null)
        {
            bossHealthBarUI = FindFirstObjectByType<BossHealthBarUI>();
        }

        // If direct elements are assigned on LegendarySamurai, wire them
        if (bossHealthBarUI == null && (directTitleText != null || directPhraseText != null || directBackgroundBar != null || directFillBar != null))
        {
            bossHealthBarUI = gameObject.AddComponent<BossHealthBarUI>();
            bossHealthBarUI.AssignDirectElements(directTitleText, directPhraseText, directBackgroundBar, directFillBar);
        }

        if (bossHealthBarUI != null)
        {
            bossHealthBarUI.Initialize(this);
        }

        onBossHealthChanged?.Invoke(currentHits, bossMaxHits);

        aiLoopCoroutine = StartCoroutine(BossAILoop());
    }

    private void CreateWeakPointContainer()
    {
        targetPoints.Clear();

        GameObject containerObj = new GameObject("BossWeakPointContainer");
        weakPointContainer = containerObj.transform;
        weakPointContainer.SetParent(transform, false);
        weakPointContainer.localPosition = new Vector3(0f, 0.65f, 0f);

        for (int i = 0; i < requiredWeakPoints; i++)
        {
            BossTargetPoint point;

            if (weakPointPrefab != null)
            {
                GameObject obj = Instantiate(weakPointPrefab, weakPointContainer);
                point = obj.GetComponent<BossTargetPoint>();
                if (point == null) point = obj.AddComponent<BossTargetPoint>();
            }
            else
            {
                GameObject obj = new GameObject($"BossTargetPoint_{i}");
                obj.transform.SetParent(weakPointContainer, false);
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

    #region Phase & Difficulty Scaling

    /// <summary>
    /// Phase 1: 100% - 80% (hits 20 to 17)
    /// Phase 2:  80% - 60% (hits 16 to 13)
    /// Phase 3:  60% - 40% (hits 12 to 9)
    /// Phase 4:  40% - 20% (hits 8 to 5)
    /// Phase 5:  20% -  0% (hits 4 to 1)
    /// </summary>
    public int GetCurrentPhase()
    {
        float hpPercent = (float)currentHits / bossMaxHits;
        if (hpPercent > 0.80f) return 1;
        if (hpPercent > 0.60f) return 2;
        if (hpPercent > 0.40f) return 3;
        if (hpPercent > 0.20f) return 4;
        return 5;
    }

    /// <summary>
    /// Returns all available attack patterns for the boss's current health.
    /// The lower the health, the more patterns are unlocked!
    /// </summary>
    private List<BossAttackPattern> GetAvailablePatterns()
    {
        int phase = GetCurrentPhase();
        List<BossAttackPattern> patterns = new List<BossAttackPattern>
        {
            BossAttackPattern.DashSlash // Always available (Phase 1+)
        };

        if (phase >= 2) patterns.Add(BossAttackPattern.Stomp);       // Unlocked at 80%
        if (phase >= 3) patterns.Add(BossAttackPattern.Shuriken360); // Unlocked at 60%
        if (phase >= 4) patterns.Add(BossAttackPattern.WindSlash);   // Unlocked at 40%
        if (phase >= 5) patterns.Add(BossAttackPattern.HiddenStab);  // Unlocked at 20%

        return patterns;
    }

    /// <summary>
    /// As health goes lower, attack patterns and windups execute faster!
    /// (1.0x speed at full HP -> 1.85x speed at critical HP).
    /// </summary>
    private float GetSpeedMultiplier()
    {
        float hpRatio = Mathf.Clamp01((float)currentHits / bossMaxHits);
        return Mathf.Lerp(1.85f, 1.0f, hpRatio);
    }

    private float GetAttackCooldown()
    {
        float hpRatio = Mathf.Clamp01((float)currentHits / bossMaxHits);
        return Mathf.Lerp(minTimeBetweenAttacks, maxTimeBetweenAttacks, hpRatio);
    }

    #endregion

    #region Boss AI Loop

    private IEnumerator BossAILoop()
    {
        yield return new WaitForSeconds(1.0f);

        while (!IsDead && currentHits > 0)
        {
            // Moving / Positioning between attacks
            bossState = BossActionState.Moving;
            float cooldown = GetAttackCooldown();
            float idleTimer = 0f;

            while (idleTimer < cooldown && !IsDead && currentHits > 0)
            {
                idleTimer += Time.deltaTime;
                yield return null;
            }

            if (IsDead || currentHits <= 0) yield break;

            // Pick an attack from the unlocked pool, avoiding immediate repetition if possible
            List<BossAttackPattern> availablePatterns = GetAvailablePatterns();
            BossAttackPattern chosenPattern = availablePatterns[Random.Range(0, availablePatterns.Count)];

            if (availablePatterns.Count > 1 && chosenPattern == lastAttackPattern)
            {
                chosenPattern = availablePatterns[Random.Range(0, availablePatterns.Count)];
            }
            lastAttackPattern = chosenPattern;

            // Execute the chosen attack
            switch (chosenPattern)
            {
                case BossAttackPattern.DashSlash:
                    currentActionCoroutine = StartCoroutine(DashSlashRoutine());
                    break;
                case BossAttackPattern.Stomp:
                    currentActionCoroutine = StartCoroutine(StompRoutine());
                    break;
                case BossAttackPattern.Shuriken360:
                    currentActionCoroutine = StartCoroutine(Shuriken360Routine());
                    break;
                case BossAttackPattern.WindSlash:
                    currentActionCoroutine = StartCoroutine(WindSlashRoutine());
                    break;
                case BossAttackPattern.HiddenStab:
                    currentActionCoroutine = StartCoroutine(HiddenStabRoutine());
                    break;
            }

            if (currentActionCoroutine != null)
            {
                yield return currentActionCoroutine;
                currentActionCoroutine = null;
            }

            yield return null;
        }
    }

    #endregion

    #region Pattern 1: Dash-Slash (Phase 1 / 100%)

    private IEnumerator DashSlashRoutine()
    {
        bossState = BossActionState.DashSlash;
        float speedMult = GetSpeedMultiplier();

        rb.linearVelocity = Vector2.zero;
        PlayActionAnimation(BossAnimState.DashSlash, loop: false);

        // 1. Telegraph charge
        if (spriteRenderer != null) spriteRenderer.color = slashDashTelegraphColor;
        FacePlayer();

        yield return new WaitForSeconds(slashDashWindup / speedMult);

        if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;
        if (IsDead || currentHits <= 0)
        {
            StopActionAnimation();
            yield break;
        }

        // 2. Dash toward player
        Vector2 dashDir = GetDirectionToPlayer();
        FacePlayer();

        PlayBossSFX(slashDashSFX, Random.Range(0.95f, 1.15f), slashDashVolume);

        Vector3 spawnPos = transform.position + new Vector3(0f, 0.55f, 0f);
        if (slashDashVFX != null)
        {
            float dashAngle = Mathf.Atan2(dashDir.y, dashDir.x) * Mathf.Rad2Deg;
            GameObject vfx = Instantiate(slashDashVFX, spawnPos, Quaternion.Euler(0f, 0f, dashAngle));
            if (vfx.TryGetComponent<BossSlashDashVFX>(out BossSlashDashVFX comp))
            {
                comp.SetFollowTarget(transform, new Vector3(0.45f, 0.55f, 0f));
            }
        }

        isDashingSlash = true;
        bool hasHitPlayerInDash = false;

        if (dashTrailRenderer != null)
        {
            dashTrailRenderer.Clear();
            dashTrailRenderer.emitting = true;
        }

        float timer = 0f;
        float duration = slashDashDuration / speedMult;
        float activeSpeed = slashDashSpeed * speedMult;
        Coroutine afterImageCoroutine = StartCoroutine(DashAfterImageRoutine(duration));

        while (timer < duration && !IsDead && currentHits > 0)
        {
            timer += Time.deltaTime;
            rb.linearVelocity = dashDir * activeSpeed;

            // Damage player if they are in the way during the dash
            if (!hasHitPlayerInDash && playerController != null)
            {
                Collider2D col = GetComponent<Collider2D>();
                Bounds b = col != null ? col.bounds : new Bounds(transform.position, Vector3.one * 1.6f);
                b.Expand(0.5f);
                if (b.Contains(playerController.transform.position))
                {
                    hasHitPlayerInDash = true;
                    playerController.TakeDamage(slashDashDamage);
                    if (playerController.TryGetComponent<Rigidbody2D>(out Rigidbody2D prb))
                    {
                        prb.AddForce(dashDir * 12f, ForceMode2D.Impulse);
                    }
                    PlayBossSFX(hitDamageSFX, 1f, hitDeflectVolume);
                }
            }

            // Boss Collateral: If boss dashes into Suicide Ninjas or other minions, trigger them!
            Collider2D bossCol = GetComponent<Collider2D>();
            Bounds dashBounds = bossCol != null ? bossCol.bounds : new Bounds(transform.position, Vector3.one * 1.6f);
            dashBounds.Expand(0.6f);
            Collider2D[] hitMinions = Physics2D.OverlapBoxAll(dashBounds.center, dashBounds.size, 0f);
            for (int mIdx = 0; mIdx < hitMinions.Length; mIdx++)
            {
                if (hitMinions[mIdx] == null || hitMinions[mIdx].gameObject == gameObject) continue;
                if (hitMinions[mIdx].TryGetComponent<ChainEnemy>(out ChainEnemy suicideBomb) && !suicideBomb.IsDead)
                {
                    suicideBomb.OnDeath(); // Detonates on impact!
                }
                else if (hitMinions[mIdx].TryGetComponent<EnemyBase>(out EnemyBase minion) && !minion.IsDead && !(minion is BossTargetPoint))
                {
                    minion.OnDeath(dashDir);
                }
            }

            yield return null;
        }

        isDashingSlash = false;
        if (dashTrailRenderer != null)
        {
            dashTrailRenderer.emitting = false;
        }
        if (afterImageCoroutine != null)
        {
            StopCoroutine(afterImageCoroutine);
        }
        rb.linearVelocity = Vector2.zero;

        if (IsDead || currentHits <= 0) yield break;

        // 3. Post-dash brief pause & aim directly at player
        FacePlayer();
        yield return new WaitForSeconds(postDashSlashDelay / speedMult);
        if (IsDead || currentHits <= 0) yield break;

        // 4. Slash one time in a straight line at player direction!
        Vector2 slashDir = GetDirectionToPlayer();
        FacePlayer();

        Vector3 slashOrigin = transform.position + new Vector3(0f, 0.55f, 0f);
        float slashAngle = Mathf.Atan2(slashDir.y, slashDir.x) * Mathf.Rad2Deg;

        AudioClip sfx = straightSlashSFX != null ? straightSlashSFX : slashDashSFX;
        PlayBossSFX(sfx, Random.Range(1.15f, 1.35f), slashDashVolume);

        // Spawn straight line slash VFX
        if (straightSlashVFX != null)
        {
            GameObject vfx = Instantiate(straightSlashVFX, slashOrigin, Quaternion.Euler(0f, 0f, slashAngle));
            if (vfx.TryGetComponent<BossStraightSlashVFX>(out BossStraightSlashVFX comp))
            {
                comp.SetSlashLength(straightSlashRange);
            }
        }
        else
        {
            BossStraightSlashVFX.Spawn(slashOrigin, slashDir, straightSlashRange);
        }

        // Damage player if in straight line
        CheckStraightSlashHit(slashOrigin, slashDir, straightSlashRange, straightSlashWidth, straightSlashDamage);

        // 5. Recovery
        yield return new WaitForSeconds(slashDashRecovery / speedMult);
        StopActionAnimation();
        if (!IsDead && currentHits > 0) bossState = BossActionState.Idle;
    }

    private IEnumerator SlashDashRoutine()
    {
        return DashSlashRoutine();
    }

    private void CheckStraightSlashHit(Vector2 origin, Vector2 direction, float range, float width, float damage)
    {
        if (playerController == null) return;

        Vector2 end = origin + direction * range;
        Vector2 playerPos = playerController.transform.position;

        float distToLine = DistancePointToLineSegment(playerPos, origin, end);
        Vector2 toPlayer = playerPos - origin;
        float proj = Vector2.Dot(toPlayer, direction);

        if (distToLine <= width * 0.5f && proj >= -0.2f && proj <= range + 0.5f)
        {
            playerController.TakeDamage(damage);
            if (playerController.TryGetComponent<Rigidbody2D>(out Rigidbody2D playerRb))
            {
                Vector2 knockbackDir = (direction + Vector2.up * 0.35f).normalized;
                playerRb.AddForce(knockbackDir * 14f, ForceMode2D.Impulse);
            }

            if (hitVFX != null)
            {
                Instantiate(hitVFX, playerPos, Quaternion.identity);
            }
            else
            {
                BossHitVFX.Spawn(playerPos);
            }
        }

        // Boss Collateral: Straight slash cuts through any minions in the line!
        Collider2D[] slashHits = Physics2D.OverlapCircleAll(origin + direction * (range * 0.5f), range * 0.6f);
        for (int i = 0; i < slashHits.Length; i++)
        {
            if (slashHits[i] == null || slashHits[i].gameObject == gameObject) continue;

            Vector2 hitPos = slashHits[i].transform.position;
            float dist = DistancePointToLineSegment(hitPos, origin, end);
            Vector2 toHit = hitPos - origin;
            float lineProj = Vector2.Dot(toHit, direction);

            if (dist <= width * 0.75f && lineProj >= -0.2f && lineProj <= range + 0.5f)
            {
                if (slashHits[i].TryGetComponent<ChainEnemy>(out ChainEnemy bomb) && !bomb.IsDead)
                {
                    bomb.OnDeath(direction);
                }
                else if (slashHits[i].TryGetComponent<EnemyBase>(out EnemyBase eb) && !eb.IsDead && !(eb is BossTargetPoint))
                {
                    eb.OnDeath(direction);
                }
            }
        }
    }

    private float DistancePointToLineSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lenSq = segment.sqrMagnitude;
        if (lenSq < 0.0001f) return Vector2.Distance(point, start);
        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lenSq);
        Vector2 projection = start + t * segment;
        return Vector2.Distance(point, projection);
    }

    #endregion

    #region Pattern 2: Stomp (Phase 2 / 80%)

    private IEnumerator StompRoutine()
    {
        bossState = BossActionState.Stomp;
        float speedMult = GetSpeedMultiplier();

        rb.linearVelocity = Vector2.zero;
        PlayActionAnimation(BossAnimState.Stomp, loop: false);

        // 1. Jump high into the air
        rb.linearVelocity = new Vector2(0f, stompJumpForce * speedMult);

        float ascendTimer = 0f;
        float ascendDuration = 0.5f / speedMult;
        while (ascendTimer < ascendDuration && !IsDead && currentHits > 0)
        {
            ascendTimer += Time.deltaTime;
            if (playerController != null)
            {
                float targetX = playerController.transform.position.x;
                float newX = Mathf.MoveTowards(transform.position.x, targetX, 7.5f * speedMult * Time.deltaTime);
                transform.position = new Vector3(newX, transform.position.y, transform.position.z);
            }
            yield return null;
        }

        // 2. Apex hover with warning flash
        float origGrav = rb.gravityScale;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;

        if (spriteRenderer != null) spriteRenderer.color = Color.red;
        yield return new WaitForSeconds(stompAirHangDuration / speedMult);
        if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;

        if (IsDead || currentHits <= 0)
        {
            rb.gravityScale = origGrav;
            yield break;
        }

        // 3. Slam straight down
        rb.gravityScale = origGrav;
        rb.linearVelocity = new Vector2(0f, -stompSlamSpeed * speedMult);

        isStompSlamming = true;
        stompHasHitGround = false;

        float timeout = 0.85f;
        float elapsed = 0f;

        while (!stompHasHitGround && elapsed < timeout && !IsDead && currentHits > 0)
        {
            elapsed += Time.deltaTime;

            Bounds bounds = col != null ? col.bounds : new Bounds(transform.position, Vector3.one * 1.5f);
            // Probe from the bottom edge of the collider down towards the ground
            Vector2 footOrigin = new Vector2(bounds.center.x, bounds.min.y + 0.12f);
            Vector2 boxSize = new Vector2(bounds.size.x * 0.80f, 0.10f);
            bool groundCheck = Physics2D.BoxCast(footOrigin, boxSize, 0f, Vector2.down, 0.35f, groundLayer);

            // Ground detected via foot cast, OR vertical velocity halted by solid collision after starting descent
            if (groundCheck || (elapsed > 0.05f && Mathf.Abs(rb.linearVelocityY) <= 0.25f))
            {
                stompHasHitGround = true;
                break;
            }
            yield return null;
        }

        isStompSlamming = false;

        // 4. Ground impact & area shockwave
        if (!IsDead && currentHits > 0)
        {
            ExecuteStompImpact();
        }

        yield return new WaitForSeconds(stompRecovery / speedMult);
        StopActionAnimation();
        if (!IsDead && currentHits > 0) bossState = BossActionState.Idle;
    }

    private void ExecuteStompImpact()
    {
        rb.linearVelocity = Vector2.zero;

        if (playerController != null)
        {
            playerController.TriggerCameraShake(stompShakeDuration);
        }

        PlayBossSFX(stompImpactSFX, 1f, stompVolume);

        if (stompShockwaveVFX != null)
        {
            Instantiate(stompShockwaveVFX, transform.position, Quaternion.identity);
        }
        else
        {
            BossStompShockwaveVFX.Spawn(transform.position);
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
            else if (hit.TryGetComponent<ChainEnemy>(out ChainEnemy suicideNinja) && !suicideNinja.IsDead)
            {
                // Seismic stomp shockwave detonates bomb ninjas!
                suicideNinja.OnDeath(Vector2.up);
            }
            else if (hit.TryGetComponent<EnemyBase>(out EnemyBase eb) && !eb.IsDead && !(eb is BossTargetPoint))
            {
                eb.OnDeath(Vector2.up);
            }
        }
    }

    #endregion

    #region Pattern 3: 360 Shuriken (Phase 3 / 60%)

    private IEnumerator Shuriken360Routine()
    {
        bossState = BossActionState.Shuriken360;
        float speedMult = GetSpeedMultiplier();

        rb.linearVelocity = Vector2.zero;
        PlayActionAnimation(BossAnimState.Shuriken360, loop: false);

        if (spriteRenderer != null) spriteRenderer.color = Color.yellow;
        yield return new WaitForSeconds(shurikenWindup / speedMult);
        if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;

        if (IsDead || currentHits <= 0)
        {
            StopActionAnimation();
            yield break;
        }

        PlayBossSFX(shurikenBurstSFX, Random.Range(1f, 1.2f), shurikenBurstVolume);

        Fire360Burst(shurikenCount360, 0f);

        // If at Phase 4 or 5, fire a secondary offset wave for bullet-hell intensity
        if (GetCurrentPhase() >= 4)
        {
            yield return new WaitForSeconds(0.18f / speedMult);
            if (!IsDead && currentHits > 0)
            {
                Fire360Burst(shurikenCount360, (360f / shurikenCount360) * 0.5f);
            }
        }

        yield return new WaitForSeconds(0.6f / speedMult);
        StopActionAnimation();
        if (!IsDead && currentHits > 0) bossState = BossActionState.Idle;
    }

    private void Fire360Burst(int count, float angleOffset)
    {
        float angleStep = 360f / count;

        for (int i = 0; i < count; i++)
        {
            float angle = i * angleStep + angleOffset;
            float rad = angle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector2 spawnPos = (Vector2)transform.position + dir * 0.85f;

            if (shurikenPrefab != null)
            {
                GameObject proj = Instantiate(shurikenPrefab, spawnPos, Quaternion.identity);
                if (proj.TryGetComponent<Shuriken>(out Shuriken shurikenComp))
                {
                    shurikenComp.Launch(dir, shurikenSpeed360 * GetSpeedMultiplier(), shurikenDamage360);
                }
            }
            else
            {
                Shuriken.CreateDefaultShuriken(spawnPos, dir, shurikenSpeed360 * GetSpeedMultiplier(), shurikenDamage360, groundLayer);
            }
        }
    }

    #endregion

    #region Pattern 4: Wind Slash (Phase 4 / 40%)

    private IEnumerator WindSlashRoutine()
    {
        bossState = BossActionState.WindSlash;
        float speedMult = GetSpeedMultiplier();

        rb.linearVelocity = Vector2.zero;
        PlayActionAnimation(BossAnimState.WindSlash, loop: false);

        // Aura windup charge
        if (spriteRenderer != null) spriteRenderer.color = new Color(0.2f, 1f, 0.7f, 1f);
        FacePlayer();

        Vector2 playerCenter = playerController != null ? (Vector2)playerController.transform.position : (Vector2)transform.position + Vector2.right * 4f;
        int lineCount = Mathf.Clamp(windSlashLineCount + (GetCurrentPhase() >= 5 ? 1 : 0), 3, 5);

        List<GameObject> lineObjects = new List<GameObject>();
        List<KeyValuePair<Vector2, Vector2>> lineSegments = new List<KeyValuePair<Vector2, Vector2>>();

        float lineLength = 24f;

        // Generate multiple straight slash lines intersecting the player's zone
        for (int i = 0; i < lineCount; i++)
        {
            float angle = -35f + (i * (70f / Mathf.Max(1, lineCount - 1))) + Random.Range(-10f, 10f);
            float rad = angle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

            Vector2 centerPoint = playerCenter + Random.insideUnitCircle * 0.6f;
            Vector2 start = centerPoint - dir * (lineLength * 0.5f);
            Vector2 end = centerPoint + dir * (lineLength * 0.5f);

            lineSegments.Add(new KeyValuePair<Vector2, Vector2>(start, end));

            // Create telegraph line
            GameObject lObj = new GameObject($"WindSlashTelegraph_{i}");
            LineRenderer lr = lObj.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = windSlashTelegraphColor;
            lr.endColor = windSlashTelegraphColor;
            lr.sortingOrder = 9;

            lineObjects.Add(lObj);
        }

        // Telegraph wait
        float telegraphDuration = windSlashTelegraphTime / speedMult;
        float t = 0f;
        while (t < telegraphDuration && !IsDead && currentHits > 0)
        {
            t += Time.deltaTime;
            float pulseWidth = 0.05f + Mathf.PingPong(t * 8f, 0.08f);
            for (int i = 0; i < lineObjects.Count; i++)
            {
                if (lineObjects[i] != null && lineObjects[i].TryGetComponent<LineRenderer>(out LineRenderer lr))
                {
                    lr.startWidth = pulseWidth;
                    lr.endWidth = pulseWidth;
                }
            }
            yield return null;
        }

        if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;
        if (IsDead || currentHits <= 0)
        {
            StopActionAnimation();
            CleanupLineObjects(lineObjects);
            yield break;
        }

        // Execute violent slash flash across all lines!
        AudioClip sfx = windSlashSFX != null ? windSlashSFX : slashDashSFX;
        PlayBossSFX(sfx, Random.Range(1.2f, 1.4f), windSlashVolume);

        if (playerController != null)
        {
            playerController.TriggerCameraShake(0.35f);
        }

        // Flash lines thick and check player hit
        bool playerHit = false;
        Vector2 curPlayerPos = playerController != null ? (Vector2)playerController.transform.position : Vector2.zero;

        for (int i = 0; i < lineObjects.Count; i++)
        {
            if (lineObjects[i] != null && lineObjects[i].TryGetComponent<LineRenderer>(out LineRenderer lr))
            {
                lr.startWidth = 0.45f;
                lr.endWidth = 0.45f;
                lr.startColor = windSlashActiveColor;
                lr.endColor = windSlashActiveColor;
            }

            if (!playerHit && playerController != null)
            {
                var seg = lineSegments[i];
                if (DistancePointToSegment(curPlayerPos, seg.Key, seg.Value) <= 0.75f)
                {
                    playerHit = true;
                    playerController.TakeDamage(windSlashDamage);
                }
            }
        }

        yield return new WaitForSeconds(0.16f);
        CleanupLineObjects(lineObjects);

        yield return new WaitForSeconds(0.5f / speedMult);
        StopActionAnimation();
        if (!IsDead && currentHits > 0) bossState = BossActionState.Idle;
    }

    private void CleanupLineObjects(List<GameObject> lineObjects)
    {
        for (int i = 0; i < lineObjects.Count; i++)
        {
            if (lineObjects[i] != null)
            {
                Destroy(lineObjects[i]);
            }
        }
        lineObjects.Clear();
    }

    #endregion

    #region Pattern 5: Hidden Stab (Phase 5 / 20%)

    private IEnumerator HiddenStabRoutine()
    {
        bossState = BossActionState.HiddenStab;
        float speedMult = GetSpeedMultiplier();

        rb.linearVelocity = Vector2.zero;

        // 1. Vanish into smoke / stealth
        AudioClip vSfx = slashDashSFX != null ? slashDashSFX : bossRoarSFX;
        PlayBossSFX(vSfx, 1.35f, hiddenStabVolume * 0.85f);

        if (hitVFX != null)
        {
            Instantiate(hitVFX, transform.position, Quaternion.identity);
        }
        else
        {
            BossHitVFX.Spawn(transform.position);
        }

        Color origColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
        if (spriteRenderer != null) spriteRenderer.color = Color.clear;
        if (col != null) col.enabled = false;

        yield return new WaitForSeconds(hiddenStabVanishDuration / speedMult);

        if (IsDead || currentHits <= 0)
        {
            RestoreVisibility(origColor);
            yield break;
        }

        // 2. Teleport directly behind the player
        PlayerController player = playerController != null ? playerController : PlayerController.Instance;
        if (player != null)
        {
            float playerFacing = 1f;
            SpriteRenderer pSr = player.GetComponentInChildren<SpriteRenderer>();
            if (pSr != null && pSr.flipX) playerFacing = -1f;
            else if (player.InputValue.x < -0.1f) playerFacing = -1f;
            else if (player.InputValue.x > 0.1f) playerFacing = 1f;

            Vector3 behindPos = player.transform.position - new Vector3(playerFacing * 2.6f, 0f, 0f);
            transform.position = behindPos;
            FacePlayer();
        }

        // 3. Become visible with a red eye glint / ominous warning
        RestoreVisibility(hiddenStabGlintColor);
        PlayActionAnimation(BossAnimState.HiddenStab, loop: false);

        AudioClip cueSfx = hiddenStabSFX != null ? hiddenStabSFX : shurikenBurstSFX;
        PlayBossSFX(cueSfx, 1.45f, hiddenStabVolume);

        // Brief split-second reaction window
        yield return new WaitForSeconds(hiddenStabWindup / speedMult);

        if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;
        if (IsDead || currentHits <= 0)
        {
            StopActionAnimation();
            yield break;
        }

        // 4. Dash Thrust through the player!
        Vector2 dashDir = GetDirectionToPlayer();
        PlayBossSFX(slashDashSFX, 1.25f, hiddenStabVolume);

        Vector3 spawnPos = transform.position + new Vector3(0f, 0.55f, 0f);
        if (slashDashVFX != null)
        {
            float dashAngle = Mathf.Atan2(dashDir.y, dashDir.x) * Mathf.Rad2Deg;
            GameObject vfx = Instantiate(slashDashVFX, spawnPos, Quaternion.Euler(0f, 0f, dashAngle));
            if (vfx.TryGetComponent<BossSlashDashVFX>(out BossSlashDashVFX comp))
            {
                comp.SetFollowTarget(transform, new Vector3(0.45f, 0.55f, 0f));
            }
        }
        else
        {
            BossSlashDashVFX.Spawn(spawnPos, dashDir, transform);
        }

        isDashingSlash = true;
        if (dashTrailRenderer != null)
        {
            dashTrailRenderer.Clear();
            dashTrailRenderer.emitting = true;
        }

        float timer = 0f;
        float duration = (slashDashDuration * 0.85f) / speedMult;
        float activeSpeed = hiddenStabSpeed * speedMult;
        Coroutine afterImageCoroutine = StartCoroutine(DashAfterImageRoutine(duration));

        while (timer < duration && !IsDead && currentHits > 0)
        {
            timer += Time.deltaTime;
            rb.linearVelocity = dashDir * activeSpeed;
            yield return null;
        }

        isDashingSlash = false;
        if (dashTrailRenderer != null)
        {
            dashTrailRenderer.emitting = false;
        }
        if (afterImageCoroutine != null)
        {
            StopCoroutine(afterImageCoroutine);
        }
        rb.linearVelocity = Vector2.zero;

        yield return new WaitForSeconds(0.45f / speedMult);
        StopActionAnimation();
        if (!IsDead && currentHits > 0) bossState = BossActionState.Idle;
    }

    private void RestoreVisibility(Color targetColor)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = targetColor;
        }
        if (col != null)
        {
            col.enabled = true;
        }
    }

    #endregion

    #region Weak Points & 5-Circle Selection System

    public override void TrySelectTarget()
    {
        if (IsDead || currentHits <= 0) return;

        bool wasTarget = isTarget;
        base.TrySelectTarget();

        // When the boss is targeted in Chain Slash mode, display the 5 random circles around itself
        if (!wasTarget && isTarget)
        {
            ShowWeakPointCircles();
        }
    }

    private void ShowWeakPointCircles()
    {
        slashedCirclesCount = 0;
        hitDeductedInCurrentSlash = false;
        areWeakPointsActive = true;
        modeExitTimer = 0f;

        Vector3 centerPos = weakPointContainer != null
            ? weakPointContainer.position
            : transform.position + new Vector3(0f, 0.65f, 0f);

        List<Vector2> placedOffsets = new List<Vector2>();

        for (int i = 0; i < targetPoints.Count; i++)
        {
            if (targetPoints[i] == null) continue;

            Vector2 offset = Vector2.zero;
            bool foundValid = false;

            // Pick a fully random angle and distance, ensuring circles do not stack directly on top of each other
            for (int attempt = 0; attempt < 15; attempt++)
            {
                float angle = Random.Range(0f, 360f);
                float dist = Random.Range(weakPointRadiusMin, weakPointRadiusMax);
                offset = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * dist;

                bool tooClose = false;
                for (int j = 0; j < placedOffsets.Count; j++)
                {
                    if (Vector2.Distance(offset, placedOffsets[j]) < 0.85f)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    foundValid = true;
                    break;
                }
            }

            if (!foundValid)
            {
                float angle = Random.Range(0f, 360f);
                float dist = Random.Range(weakPointRadiusMin, weakPointRadiusMax);
                offset = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * dist;
            }

            placedOffsets.Add(offset);
            Vector3 worldPos = centerPos + (Vector3)offset;
            targetPoints[i].ActivatePoint(worldPos);
        }
    }

    private void ResetWeakPoints()
    {
        slashedCirclesCount = 0;
        hitDeductedInCurrentSlash = false;
        areWeakPointsActive = false;
        isTarget = false;

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
        if (IsDead || currentHits <= 0) return;

        slashedCirclesCount++;
        TriggerHitFlash();

        // If all 5 circles were slashed, land 1 hit on the boss!
        if (slashedCirclesCount >= requiredWeakPoints)
        {
            TakeBossHit(1, slashImpulse);
        }
    }

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        if (IsDead || currentHits <= 0) return;

        // If player managed to select all 5 circles, land 1 hit!
        if (AreAllCirclesSelected() || slashedCirclesCount >= requiredWeakPoints)
        {
            TakeBossHit(1, deathImpulse);
            return;
        }

        // If not all 5 circles were selected, deflect and take 0 damage!
        HandleDeflectedHit();
    }

    private bool AreAllCirclesSelected()
    {
        if (targetPoints.Count < requiredWeakPoints) return false;

        for (int i = 0; i < targetPoints.Count; i++)
        {
            if (targetPoints[i] == null || (!targetPoints[i].IsTarget && !targetPoints[i].IsSlashed))
            {
                return false;
            }
        }
        return true;
    }

    private void HandleDeflectedHit()
    {
        if (hitDeductedInCurrentSlash) return;

        AudioClip dSfx = deflectSFX != null ? deflectSFX : hitDamageSFX;
        PlayBossSFX(dSfx, Random.Range(0.95f, 1.05f), hitDeflectVolume);

        TriggerHitFlash(Color.cyan);
        StartCoroutine(ResetAfterDeflectRoutine());
    }

    private IEnumerator ResetAfterDeflectRoutine()
    {
        yield return new WaitForSeconds(0.2f);
        ResetWeakPoints();
    }

    /// <summary>
    /// Damages the boss when caught in an explosion (e.g. from a ChainEnemy / Suicide Ninja detonating).
    /// </summary>
    public void TakeExplosionHit(Vector3 explosionPos, float damage = 35f)
    {
        if (IsDead || currentHits <= 0) return;
        if (Time.time - lastHitDamageTime < MIN_HIT_DAMAGE_INTERVAL) return;
        lastHitDamageTime = Time.time;

        hitDeductedInCurrentSlash = false; // Allow explosion damage
        Vector2 knockback = ((Vector2)transform.position - (Vector2)explosionPos).normalized * 4.5f;
        TakeBossHit(1, knockback);
    }

    /// <summary>
    /// Damages the boss when struck by a projectile (e.g. a shuriken reflected by the player).
    /// </summary>
    public void TakeShurikenHit(Vector2 hitDirection)
    {
        if (IsDead || currentHits <= 0) return;
        if (Time.time - lastHitDamageTime < MIN_HIT_DAMAGE_INTERVAL) return;
        lastHitDamageTime = Time.time;

        hitDeductedInCurrentSlash = false; // Allow shuriken damage
        TakeBossHit(1, hitDirection * 3.5f);
    }

    public void TakeBossHit(int hitCount = 1, Vector2 deathImpulse = default)
    {
        if (IsDead || currentHits <= 0) return;
        if (hitDeductedInCurrentSlash) return;
        hitDeductedInCurrentSlash = true;

        int oldPhase = GetCurrentPhase();
        currentHits = Mathf.Max(0, currentHits - hitCount);
        onBossHealthChanged?.Invoke(currentHits, bossMaxHits);

        // Crossfade to Phase 2 BGM when at or below 50% health!
        float healthRatio = (float)currentHits / bossMaxHits;
        if (healthRatio <= 0.5f && !isPhase2BGMActive)
        {
            TransitionToPhase2BGM();
        }

        // Defeat when all 20 hits are landed!
        if (currentHits <= 0)
        {
            Die(deathImpulse);
            return;
        }

        int newPhase = GetCurrentPhase();
        if (newPhase > oldPhase)
        {
            // Ominous roar cue upon entering a new phase with faster attacks & new patterns
            PlayBossSFX(bossRoarSFX, 1.15f, roarVolume);

            // Spawn phrase-based minion wave!
            SpawnMinionWaveForPhase(newPhase);
        }

        TriggerHitFlash(Color.white);
        PlayBossSFX(hitDamageSFX, Random.Range(0.95f, 1.05f), hitDeflectVolume);

        if (hitVFX != null)
        {
            Instantiate(hitVFX, transform.position, Quaternion.identity);
        }
        else
        {
            BossHitVFX.Spawn(transform.position);
        }

        ResetWeakPoints();
    }

    private void TriggerHitFlash(Color? flashColor = null)
    {
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(HitFlashRoutine(flashColor ?? Color.white));
    }

    private IEnumerator HitFlashRoutine(Color color)
    {
        if (spriteRenderer != null) spriteRenderer.color = color;
        yield return new WaitForSeconds(0.12f);
        if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;
    }

    public void Die(Vector2 deathImpulse = default)
    {
        if (IsDead) return;
        IsDead = true;
        currentHits = 0;
        bossState = BossActionState.Defeated;

        StopAllCoroutines();
        StopActionAnimation();
        FadeOutBossBGM(1.5f);
        isDashingSlash = false;
        if (defaultBaseSprite != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = defaultBaseSprite;
        }
        if (dashTrailRenderer != null) dashTrailRenderer.emitting = false;

        Collider2D[] allColliders = GetComponentsInChildren<Collider2D>();
        foreach (var c in allColliders)
        {
            if (c != null) c.enabled = false;
        }

        DestroyAllTargetPoints();

        // Clean up or defeat all active minions spawned by the boss so the arena is cleared cleanly
        for (int i = 0; i < activeSpawnedMinions.Count; i++)
        {
            if (activeSpawnedMinions[i] != null)
            {
                if (activeSpawnedMinions[i].TryGetComponent<EnemyBase>(out EnemyBase eb) && !eb.IsDead)
                {
                    eb.OnDeath();
                }
                else
                {
                    Destroy(activeSpawnedMinions[i]);
                }
            }
        }
        activeSpawnedMinions.Clear();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.UnregisterEnemy(gameObject);
        }

        onBossDefeated?.Invoke();
        onBossHealthChanged?.Invoke(0, bossMaxHits);

        StartCoroutine(DefeatVisualSequence(deathImpulse));
    }

    /// <summary>
    /// Spawns a randomized wave of minions when entering a new phrase based on minionsPerPhrase.
    /// </summary>
    public void SpawnMinionWaveForPhase(int phase)
    {
        if (minionPrefabs == null || minionPrefabs.Count == 0) return;

        int phaseIndex = phase - 1;
        int countToSpawn = 0;
        if (minionsPerPhrase != null && phaseIndex >= 0 && phaseIndex < minionsPerPhrase.Length)
        {
            countToSpawn = minionsPerPhrase[phaseIndex];
        }

        if (countToSpawn <= 0) return;

        StartCoroutine(SpawnMinionWaveRoutine(countToSpawn));
    }

    private IEnumerator SpawnMinionWaveRoutine(int count)
    {
        yield return new WaitForSeconds(0.35f);

        AudioClip sfx = minionSummonSFX != null ? minionSummonSFX : bossRoarSFX;
        PlayBossSFX(sfx, 1.1f, roarVolume);

        for (int i = 0; i < count; i++)
        {
            if (IsDead || currentHits <= 0) yield break;

            GameObject prefab = minionPrefabs[Random.Range(0, minionPrefabs.Count)];
            if (prefab == null) continue;

            Vector2 spawnPoint;
            if (GameManager.Instance != null && GameManager.Instance.TryGetValidSpawnPoint(out Vector2 validPoint, prefab))
            {
                spawnPoint = validPoint;
            }
            else
            {
                float sign = (i % 2 == 0) ? 1f : -1f;
                float dist = Random.Range(4.5f, 10f) * sign;
                Vector2 origin = (Vector2)transform.position + new Vector2(dist, 4f);
                RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, 12f, groundLayer);
                if (hit.collider != null)
                {
                    spawnPoint = hit.point + Vector2.up * 0.5f;
                }
                else
                {
                    spawnPoint = (Vector2)transform.position + new Vector2(dist, 0f);
                }
            }

            GameObject spawnedMinion = Instantiate(prefab, spawnPoint, Quaternion.identity);
            activeSpawnedMinions.Add(spawnedMinion);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.RegisterEnemy(spawnedMinion);
            }

            if (hitVFX != null)
            {
                Instantiate(hitVFX, spawnPoint, Quaternion.identity);
            }
            else
            {
                BossHitVFX.Spawn(spawnPoint);
            }

            yield return new WaitForSeconds(0.12f);
        }
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

        PlayBossSFX(bossDefeatSFX, 1f, defeatVolume);

        PlayerController targetPlayer = playerController != null ? playerController : PlayerController.Instance;
        if (targetPlayer != null)
        {
            targetPlayer.TriggerCameraShake(0.85f);
        }

        // Dramatic flashing defeat sequence
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
        else
        {
            BossDefeatVFX.Spawn(transform.position);
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

    #region Helper Methods & Updates

    protected override void Update()
    {
        if (IsDead) return;

        if (currentHits <= 0)
        {
            Die();
            return;
        }

        base.Update();

        UpdateSpriteAnimation(Time.deltaTime);

        // Reset circles if the player leaves Chain Slash mode without slashing
        if (areWeakPointsActive && (PlayerController.Instance == null || !PlayerController.Instance.IsInMode))
        {
            modeExitTimer += Time.unscaledDeltaTime;
            if (modeExitTimer > 0.45f)
            {
                ResetWeakPoints();
            }
        }
        else
        {
            modeExitTimer = 0f;
        }

#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f8Key.wasPressedThisFrame)
        {
            showOnScreenSlider = !showOnScreenSlider;
        }
#endif
    }

    #region Sprite Animation Engine (No Animator)

    public void PlayActionAnimation(BossAnimState state, bool loop = false)
    {
        currentAnimState = state;
        currentAnimFrame = 0;
        animTimer = 0f;
        isPlayingActionAnim = true;
        actionAnimLoop = loop;

        List<Sprite> frames = GetFramesForState(state);
        if (frames != null && frames.Count > 0 && frames[0] != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = frames[0];
        }
    }

    public void StopActionAnimation()
    {
        isPlayingActionAnim = false;
        actionAnimLoop = false;
    }

    private bool IsBossGrounded()
    {
        if (col == null) return true;
        Bounds bounds = col.bounds;
        Vector2 groundBoxOrigin = new Vector2(bounds.center.x, bounds.min.y + 0.06f);
        Vector2 groundBoxSize = new Vector2(bounds.size.x * 0.75f, 0.08f);
        return Physics2D.BoxCast(
            groundBoxOrigin,
            groundBoxSize,
            0f,
            Vector2.down,
            0.18f,
            groundLayer
        );
    }

    private List<Sprite> GetFramesForState(BossAnimState state)
    {
        switch (state)
        {
            case BossAnimState.Idle:
                return idleSprites;
            case BossAnimState.Walk:
                return (walkSprites != null && walkSprites.Count > 0) ? walkSprites : idleSprites;
            case BossAnimState.Jump:
                return (jumpSprites != null && jumpSprites.Count > 0) ? jumpSprites : idleSprites;
            case BossAnimState.DashSlash:
                return (dashSlashSprites != null && dashSlashSprites.Count > 0) ? dashSlashSprites : idleSprites;
            case BossAnimState.Stomp:
                return (stompSprites != null && stompSprites.Count > 0) ? stompSprites : (jumpSprites != null && jumpSprites.Count > 0 ? jumpSprites : idleSprites);
            case BossAnimState.Shuriken360:
                return (shuriken360Sprites != null && shuriken360Sprites.Count > 0) ? shuriken360Sprites : idleSprites;
            case BossAnimState.HiddenStab:
                return (hiddenStabSprites != null && hiddenStabSprites.Count > 0) ? hiddenStabSprites : (dashSlashSprites != null && dashSlashSprites.Count > 0 ? dashSlashSprites : idleSprites);
            case BossAnimState.WindSlash:
                return (windSlashSprites != null && windSlashSprites.Count > 0) ? windSlashSprites : (dashSlashSprites != null && dashSlashSprites.Count > 0 ? dashSlashSprites : idleSprites);
            default:
                return idleSprites;
        }
    }

    private float GetFPSForState(BossAnimState state)
    {
        switch (state)
        {
            case BossAnimState.Idle: return idleFPS;
            case BossAnimState.Walk: return walkFPS;
            case BossAnimState.Jump: return jumpFPS;
            case BossAnimState.DashSlash: return dashSlashFPS;
            case BossAnimState.Stomp: return stompFPS;
            case BossAnimState.Shuriken360: return shuriken360FPS;
            case BossAnimState.HiddenStab: return hiddenStabFPS;
            case BossAnimState.WindSlash: return windSlashFPS;
            default: return 8f;
        }
    }

    private void UpdateSpriteAnimation(float dt)
    {
        if (IsDead || spriteRenderer == null) return;

        // If not playing a dedicated action animation, automatically track Idle, Walk, and Jump
        if (!isPlayingActionAnim)
        {
            BossAnimState targetState;
            if (!IsBossGrounded())
            {
                targetState = BossAnimState.Jump;
            }
            else if (bossState == BossActionState.Moving && Mathf.Abs(rb.linearVelocityX) > 0.15f)
            {
                targetState = BossAnimState.Walk;
            }
            else
            {
                targetState = BossAnimState.Idle;
            }

            if (targetState != currentAnimState)
            {
                currentAnimState = targetState;
                currentAnimFrame = 0;
                animTimer = 0f;
            }
        }

        List<Sprite> frames = GetFramesForState(currentAnimState);
        if (frames == null || frames.Count == 0)
        {
            if (defaultBaseSprite != null && spriteRenderer.sprite != defaultBaseSprite)
            {
                spriteRenderer.sprite = defaultBaseSprite;
            }
            return;
        }

        float fps = GetFPSForState(currentAnimState);
        if (isPlayingActionAnim)
        {
            fps *= GetSpeedMultiplier();
        }

        if (fps <= 0.01f) fps = 8f;
        float frameDuration = 1f / fps;

        animTimer += dt;
        if (animTimer >= frameDuration)
        {
            int framesToAdvance = Mathf.FloorToInt(animTimer / frameDuration);
            animTimer %= frameDuration;

            currentAnimFrame += framesToAdvance;

            if (currentAnimFrame >= frames.Count)
            {
                if (isPlayingActionAnim && !actionAnimLoop)
                {
                    // Hold on the final frame of action animation until routine concludes
                    currentAnimFrame = frames.Count - 1;
                }
                else
                {
                    currentAnimFrame %= frames.Count;
                }
            }
        }

        if (currentAnimFrame >= 0 && currentAnimFrame < frames.Count)
        {
            Sprite targetSprite = frames[currentAnimFrame];
            if (targetSprite != null && spriteRenderer.sprite != targetSprite)
            {
                spriteRenderer.sprite = targetSprite;
            }
        }
    }

    #endregion

    protected override void Movement()
    {
        if (IsDead || currentHits <= 0 || bossState != BossActionState.Moving) return;
        base.Movement();
    }

    protected override void OnCollisionEnter2D(Collision2D collision)
    {
        base.OnCollisionEnter2D(collision);

        if (isStompSlamming)
        {
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y > 0.35f)
                {
                    stompHasHitGround = true;
                    break;
                }
            }
        }
    }

    protected override void OnCollisionStay2D(Collision2D collision)
    {
        if (isStompSlamming)
        {
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y > 0.35f)
                {
                    stompHasHitGround = true;
                    break;
                }
            }
        }
    }

    protected override void HandlePlayerContact(Collision2D collision)
    {
        if (IsDead || currentHits <= 0) return;

        if (isDashingSlash)
        {
            if (collision.gameObject.TryGetComponent<PlayerController>(out PlayerController player))
            {
                float dmg = (bossState == BossActionState.HiddenStab) ? hiddenStabDamage : slashDashDamage;
                player.TakeDamage(dmg);
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

    protected override bool HandleThrownImpact(Collision2D collision) => false;

    private void FacePlayer()
    {
        if (playerController != null && spriteRenderer != null)
        {
            float dirX = playerController.transform.position.x - transform.position.x;
            if (Mathf.Abs(dirX) > 0.1f)
            {
                spriteRenderer.flipX = dirX < 0f;
            }
        }
    }

    private Vector2 GetDirectionToPlayer()
    {
        if (playerController != null)
        {
            Vector2 diff = (Vector2)playerController.transform.position - (Vector2)transform.position;
            if (diff.sqrMagnitude > 0.01f) return diff.normalized;
        }
        return (spriteRenderer != null && spriteRenderer.flipX) ? Vector2.left : Vector2.right;
    }

    private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 0.0001f) return Vector2.Distance(point, a);

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lenSq);
        Vector2 projection = a + t * ab;
        return Vector2.Distance(point, projection);
    }

    #endregion

    #region Dash Trail & Ghost After-Images

    private void SetupDashTrail()
    {
        if (dashTrailRenderer == null)
        {
            dashTrailRenderer = GetComponentInChildren<TrailRenderer>();
        }

        if (dashTrailRenderer == null)
        {
            GameObject trailObj = new GameObject("BossDashTrail");
            trailObj.transform.SetParent(transform, false);
            trailObj.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            dashTrailRenderer = trailObj.AddComponent<TrailRenderer>();
        }

        dashTrailRenderer.time = 0.22f;
        dashTrailRenderer.startWidth = 0.85f;
        dashTrailRenderer.endWidth = 0.05f;
        dashTrailRenderer.autodestruct = false;
        dashTrailRenderer.emitting = false;

        // Custom fiery/crimson gradient matching pixel arcade palette
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.2f, 0.25f), 0f),
                new GradientColorKey(new Color(1f, 0.6f, 0.15f), 0.65f),
                new GradientColorKey(new Color(0.4f, 0f, 0.05f), 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.85f, 0f),
                new GradientAlphaKey(0.5f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        dashTrailRenderer.colorGradient = grad;

        if (dashTrailRenderer.material == null || dashTrailRenderer.material.name.Contains("Default-Material"))
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader != null)
            {
                dashTrailRenderer.material = new Material(shader);
            }
        }

        if (spriteRenderer != null)
        {
            dashTrailRenderer.sortingOrder = Mathf.Max(0, spriteRenderer.sortingOrder - 1);
        }
    }

    private IEnumerator DashAfterImageRoutine(float duration)
    {
        float timer = 0f;
        while (timer < duration && isDashingSlash && !IsDead && currentHits > 0)
        {
            SpawnSpriteAfterImage();
            yield return new WaitForSeconds(afterImageInterval);
            timer += afterImageInterval;
        }
    }

    private void SpawnSpriteAfterImage()
    {
        SpriteRenderer sourceSr = spriteRenderer != null ? spriteRenderer : GetComponentInChildren<SpriteRenderer>();
        if (sourceSr == null || sourceSr.sprite == null) return;

        GameObject ghost = new GameObject("BossAfterImage");
        ghost.transform.position = sourceSr.transform.position;
        ghost.transform.rotation = sourceSr.transform.rotation;
        ghost.transform.localScale = sourceSr.transform.lossyScale;

        SpriteRenderer ghostSr = ghost.AddComponent<SpriteRenderer>();
        ghostSr.sprite = sourceSr.sprite;
        ghostSr.flipX = sourceSr.flipX;
        ghostSr.flipY = sourceSr.flipY;
        ghostSr.sortingOrder = Mathf.Max(0, sourceSr.sortingOrder - 1);
        ghostSr.material = sourceSr.material;

        ghostSr.color = new Color(dashTrailColor.r, dashTrailColor.g, dashTrailColor.b, 0.65f);

        StartCoroutine(FadeAndDestroyAfterImage(ghost, ghostSr));
    }

    private IEnumerator FadeAndDestroyAfterImage(GameObject ghost, SpriteRenderer sr)
    {
        float lifetime = 0.22f;
        float elapsed = 0f;
        Color initialColor = sr != null ? sr.color : dashTrailColor;

        while (elapsed < lifetime)
        {
            elapsed += 0.055f;
            yield return new WaitForSeconds(0.055f);

            if (sr == null || ghost == null) yield break;

            float alphaNorm = 1f - (elapsed / lifetime);
            float steppedAlpha = Mathf.Floor(alphaNorm * 4f) / 4f * initialColor.a;
            sr.color = new Color(initialColor.r, initialColor.g, initialColor.b, steppedAlpha);
        }

        if (ghost != null)
        {
            Destroy(ghost);
        }
    }

    #endregion

    #region Boss Audio & Volume Sliders

    private AudioSource bossAudioSource;

    private void SetupBossAudio()
    {
        bossAudioSource = GetComponent<AudioSource>();
        if (bossAudioSource == null)
        {
            bossAudioSource = gameObject.AddComponent<AudioSource>();
        }

        bossAudioSource.playOnAwake = false;
        bossAudioSource.spatialBlend = 0f; // 2D direct sound for clear arcade punch
        bossAudioSource.priority = 32;     // High priority: never culled when multiple sounds play
        bossAudioSource.volume = 1f;

        if (bossVolumeSlider != null)
        {
            bossVolumeSlider.minValue = 0f;
            bossVolumeSlider.maxValue = 3f;
            bossVolumeSlider.value = bossMasterVolume;
            bossVolumeSlider.onValueChanged.RemoveListener(SetBossMasterVolume);
            bossVolumeSlider.onValueChanged.AddListener(SetBossMasterVolume);
        }
    }

    public void SetBossMasterVolume(float value)
    {
        bossMasterVolume = Mathf.Clamp(value, 0f, 3f);
        if (bossVolumeSlider != null && Mathf.Abs(bossVolumeSlider.value - bossMasterVolume) > 0.01f)
        {
            bossVolumeSlider.value = bossMasterVolume;
        }
    }

    public float BossMasterVolume
    {
        get => bossMasterVolume;
        set => SetBossMasterVolume(value);
    }

    public void PlayBossSFX(AudioClip clip, float pitch = 1f, float patternVolume = 1f)
    {
        if (clip == null) return;

        float finalVolume = bossMasterVolume * patternVolume;
        if (finalVolume <= 0.001f) return;

        if (bossAudioSource == null) SetupBossAudio();

        if (bossAudioSource != null)
        {
            bossAudioSource.pitch = pitch;
            bossAudioSource.PlayOneShot(clip, finalVolume);
        }
        else if (playerController != null)
        {
            playerController.PlayPitchedSFX(clip, pitch, finalVolume);
        }
    }

    private void OnGUI()
    {
        if (!showOnScreenSlider || IsDead || currentHits <= 0) return;

        GUILayout.BeginArea(new Rect(15, Screen.height - 95, 230, 80), GUI.skin.box);
        GUILayout.Label($"<b>Boss Master Volume:</b> {bossMasterVolume:F1}x");
        float newVol = GUILayout.HorizontalSlider(bossMasterVolume, 0f, 3f);
        if (Mathf.Abs(newVol - bossMasterVolume) > 0.01f)
        {
            SetBossMasterVolume(newVol);
        }
        GUILayout.EndArea();
    }

    #endregion

    #region Boss Background Music (BGM) Engine

    private AudioSource bgmSourceA;
    private AudioSource bgmSourceB;
    private bool isSourceAPlaying = false;
    private bool isPhase2BGMActive = false;
    private Coroutine bgmCrossfadeCoroutine;

    private void SetupBossBGM()
    {
#if UNITY_EDITOR
        if (phase1BGM == null)
        {
            phase1BGM = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/BossBGM1.mp3");
        }
        if (phase2BGM == null)
        {
            phase2BGM = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/BossBGM2.mp3");
        }
#endif

        GameObject bgmObjA = new GameObject("BossBGM_SourceA");
        bgmObjA.transform.SetParent(transform, false);
        bgmSourceA = bgmObjA.AddComponent<AudioSource>();
        bgmSourceA.loop = loopBGM;
        bgmSourceA.playOnAwake = false;
        bgmSourceA.spatialBlend = 0f;
        bgmSourceA.priority = 16;
        bgmSourceA.volume = 0f;

        GameObject bgmObjB = new GameObject("BossBGM_SourceB");
        bgmObjB.transform.SetParent(transform, false);
        bgmSourceB = bgmObjB.AddComponent<AudioSource>();
        bgmSourceB.loop = loopBGM;
        bgmSourceB.playOnAwake = false;
        bgmSourceB.spatialBlend = 0f;
        bgmSourceB.priority = 16;
        bgmSourceB.volume = 0f;
    }

    private void StartBossBGM()
    {
        if (phase1BGM == null || bgmSourceA == null) return;

        bgmSourceA.clip = phase1BGM;
        bgmSourceA.loop = loopBGM;
        bgmSourceA.volume = 0f;
        bgmSourceA.Play();
        isSourceAPlaying = true;
        isPhase2BGMActive = false;

        StartCoroutine(FadeInSource(bgmSourceA, bossBGMVolume, 1.2f));
    }

    public void TransitionToPhase2BGM()
    {
        if (isPhase2BGMActive || phase2BGM == null) return;
        isPhase2BGMActive = true;

        if (bgmCrossfadeCoroutine != null) StopCoroutine(bgmCrossfadeCoroutine);
        bgmCrossfadeCoroutine = StartCoroutine(CrossfadeRoutine(phase2BGM, bgmCrossfadeDuration));
    }

    private IEnumerator CrossfadeRoutine(AudioClip newClip, float duration)
    {
        AudioSource currentSource = isSourceAPlaying ? bgmSourceA : bgmSourceB;
        AudioSource newSource = isSourceAPlaying ? bgmSourceB : bgmSourceA;

        if (newSource != null)
        {
            newSource.clip = newClip;
            newSource.loop = loopBGM;
            newSource.volume = 0f;
            newSource.Play();
        }

        float timer = 0f;
        float startVol = currentSource != null ? currentSource.volume : bossBGMVolume;

        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / duration);

            if (currentSource != null) currentSource.volume = Mathf.Lerp(startVol, 0f, t);
            if (newSource != null) newSource.volume = Mathf.Lerp(0f, bossBGMVolume, t);

            yield return null;
        }

        if (currentSource != null)
        {
            currentSource.volume = 0f;
            currentSource.Stop();
        }
        if (newSource != null)
        {
            newSource.volume = bossBGMVolume;
        }

        isSourceAPlaying = !isSourceAPlaying;
        bgmCrossfadeCoroutine = null;
    }

    private IEnumerator FadeInSource(AudioSource src, float targetVol, float duration)
    {
        float timer = 0f;
        while (timer < duration && src != null)
        {
            timer += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(0f, targetVol, timer / duration);
            yield return null;
        }
        if (src != null) src.volume = targetVol;
    }

    private void FadeOutBossBGM(float duration)
    {
        if (bgmCrossfadeCoroutine != null) StopCoroutine(bgmCrossfadeCoroutine);

        if (bgmSourceA != null)
        {
            bgmSourceA.transform.SetParent(null);
            StartCoroutine(FadeOutAndDestroySource(bgmSourceA, duration));
        }
        if (bgmSourceB != null)
        {
            bgmSourceB.transform.SetParent(null);
            StartCoroutine(FadeOutAndDestroySource(bgmSourceB, duration));
        }
    }

    private IEnumerator FadeOutAndDestroySource(AudioSource src, float duration)
    {
        if (src == null || !src.isPlaying)
        {
            if (src != null) Destroy(src.gameObject);
            yield break;
        }

        float startVol = src.volume;
        float timer = 0f;
        while (timer < duration && src != null)
        {
            timer += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(startVol, 0f, timer / duration);
            yield return null;
        }

        if (src != null)
        {
            src.volume = 0f;
            src.Stop();
            Destroy(src.gameObject);
        }
    }

    #endregion
}
