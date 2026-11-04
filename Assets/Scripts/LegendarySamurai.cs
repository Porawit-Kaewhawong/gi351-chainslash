using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class LegendarySamurai : EnemyBase
{
    #region Enums & Constants

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

    public enum IaiPatternType
    {
        Ichimonji,       // Phase 1: Horizontal Draw (Ichimonji)
        KesaGiri,        // Phase 2: Diagonal Crescent Slash (Kesa-Giri)
        Jumonji,         // Phase 3: Cross Slash (Jumonji)
        Zantetsu,        // Phase 4: Z-Cut Slash (Zantetsu-ken)
        GorinStar        // Phase 5: Star Flurry / Five Rings (Gorin-no-sho)
    }

    private const float MIN_HIT_DAMAGE_INTERVAL = 0.20f;
    private const float MIN_WEAK_POINT_SPACING_SQR = 0.85f * 0.85f;

    private static readonly float[] GorinStarAngles = { 90f, 306f, 162f, 18f, 234f };
    private static readonly WaitForSeconds WaitInitialDelay = new WaitForSeconds(1.0f);
    private static readonly WaitForSeconds WaitDeflectReset = new WaitForSeconds(0.2f);
    private static readonly WaitForSeconds WaitHitFlash = new WaitForSeconds(0.12f);
    private static readonly WaitForSeconds WaitWindSlashActive = new WaitForSeconds(0.16f);
    private static readonly WaitForSeconds WaitMinionWaveInitial = new WaitForSeconds(0.35f);
    private static readonly WaitForSeconds WaitMinionSpawnInterval = new WaitForSeconds(0.12f);
    private static readonly WaitForSeconds WaitAfterImageFadeStep = new WaitForSeconds(0.055f);

    private static Shader cachedDefaultSpriteShader;

    #endregion

    #region Serialized Fields

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

    [Header("Sequential Iai Cutting Patterns")]
    [Tooltip("Enable samurai sequential Iai cutting patterns (Ichimonji, Kesa-Giri, Jumonji, Zantetsu, Gorin-Star)")]
    [SerializeField] private bool useSequentialIaiPatterns = true;
    [Tooltip("Spread multiplier for the Iai cutting points")]
    [SerializeField] private float iaiPatternSpread = 1.0f;
    [Tooltip("Custom font for weak point order numbers")]
    [SerializeField] private TMP_FontAsset weakPointFontAsset;
    [Tooltip("Font size of the sequential order number inside each weak point circle")]
    [SerializeField] private float weakPointNumberFontSize = 2.0f;
    [Tooltip("Audio clip played when each sequential weak point is selected")]
    [SerializeField] private AudioClip pointSelectChimeSFX;

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

    #endregion

    #region Public Properties

    public int CurrentBossHealth => currentHits;
    public int MaxBossHealth => bossMaxHits;
    public int CurrentPhase => GetCurrentPhase();
    public BossActionState CurrentBossState => bossState;
    public bool WasHitLandedInCurrentSlash => hitDeductedInCurrentSlash;
    public BossAnimState CurrentAnimationState => currentAnimState;
    public int CurrentAnimationFrame => currentAnimFrame;
    public bool UseSequentialIaiPatterns => useSequentialIaiPatterns;
    public int CurrentSequenceIndex => currentSequenceIndex;

    public float BossMasterVolume
    {
        get => bossMasterVolume;
        set => SetBossMasterVolume(value);
    }

    #endregion

    #region Private State

    private int currentHits;
    private BossActionState bossState = BossActionState.Idle;
    private bool areWeakPointsActive = false;
    private int slashedCirclesCount = 0;
    private int currentSequenceIndex = 0;
    private float modeExitTimer = 0f;
    private bool isDashingSlash = false;
    private bool hitDeductedInCurrentSlash = false;
    private bool isStompSlamming = false;
    private bool stompHasHitGround = false;
    private float lastHitDamageTime = -10f;

    private readonly List<GameObject> activeSpawnedMinions = new List<GameObject>();
    private readonly List<BossAttackPattern> availablePatternsBuffer = new List<BossAttackPattern>(5);
    private readonly List<Vector2> randomWeakPointOffsetsBuffer = new List<Vector2>(8);

    // Sprite Animation State
    private BossAnimState currentAnimState = BossAnimState.Idle;
    private int currentAnimFrame = 0;
    private float animTimer = 0f;
    private bool isPlayingActionAnim = false;
    private bool actionAnimLoop = false;
    private Sprite defaultBaseSprite;

    // Weak Points & AI Coroutines
    private readonly List<BossTargetPoint> targetPoints = new List<BossTargetPoint>();
    private Transform weakPointContainer;
    private Coroutine aiLoopCoroutine;
    private Coroutine currentActionCoroutine;
    private Coroutine flashCoroutine;
    private BossAttackPattern lastAttackPattern = (BossAttackPattern)(-1);

    // Audio & BGM State
    private AudioSource bossAudioSource;
    private AudioSource bgmSourceA;
    private AudioSource bgmSourceB;
    private bool isSourceAPlaying = false;
    private bool isPhase2BGMActive = false;
    private Coroutine bgmCrossfadeCoroutine;

    #endregion

    #region Unity Lifecycle

    protected override void Awake()
    {
        base.Awake();

        // Disable Animator if present so code sprite animation has full control
        if (TryGetComponent<Animator>(out Animator anim))
        {
            anim.enabled = false;
        }

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
        if (weakPointFontAsset == null)
        {
            weakPointFontAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/PixelifySans-VariableFont_wght SDF.asset");
        }

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

    private void OnDestroy()
    {
        DestroyAllTargetPoints();
    }

    protected override void OnCollisionEnter2D(Collision2D collision)
    {
        base.OnCollisionEnter2D(collision);
        CheckStompGroundContacts(collision);
    }

    protected override void OnCollisionStay2D(Collision2D collision)
    {
        CheckStompGroundContacts(collision);
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
        availablePatternsBuffer.Clear();
        availablePatternsBuffer.Add(BossAttackPattern.DashSlash); // Always available (Phase 1+)

        if (phase >= 2) availablePatternsBuffer.Add(BossAttackPattern.Stomp);       // Unlocked at 80%
        if (phase >= 3) availablePatternsBuffer.Add(BossAttackPattern.Shuriken360); // Unlocked at 60%
        if (phase >= 4) availablePatternsBuffer.Add(BossAttackPattern.WindSlash);   // Unlocked at 40%
        if (phase >= 5) availablePatternsBuffer.Add(BossAttackPattern.HiddenStab);  // Unlocked at 20%

        return availablePatternsBuffer;
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

    #region AI State Machine & Patrol

    protected override void Movement()
    {
        if (IsDead || currentHits <= 0 || bossState != BossActionState.Moving) return;
        base.Movement();
    }

    private IEnumerator BossAILoop()
    {
        yield return WaitInitialDelay;

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

    #region Attack Patterns

    // -------------------------------------------------------------------------
    // Pattern 1: Dash-Slash (Phase 1 / 100%)
    // -------------------------------------------------------------------------

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

            Bounds baseBounds = col != null ? col.bounds : new Bounds(transform.position, Vector3.one * 1.6f);

            // Damage player if they are in the way during the dash
            if (!hasHitPlayerInDash && playerController != null)
            {
                Bounds playerCheckBounds = baseBounds;
                playerCheckBounds.Expand(0.5f);
                if (playerCheckBounds.Contains(playerController.transform.position))
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
            Bounds dashBounds = baseBounds;
            dashBounds.Expand(0.6f);
            Collider2D[] hitMinions = Physics2D.OverlapBoxAll(dashBounds.center, dashBounds.size, 0f);
            for (int mIdx = 0; mIdx < hitMinions.Length; mIdx++)
            {
                Collider2D hitMinion = hitMinions[mIdx];
                if (hitMinion == null || hitMinion.gameObject == gameObject) continue;

                if (hitMinion.TryGetComponent<ChainEnemy>(out ChainEnemy suicideBomb) && !suicideBomb.IsDead)
                {
                    suicideBomb.OnDeath(); // Detonates on impact!
                }
                else if (hitMinion.TryGetComponent<EnemyBase>(out EnemyBase minion) && !minion.IsDead && !(minion is BossTargetPoint))
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
        Vector2 end = origin + direction * range;

        if (playerController != null)
        {
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

                SpawnHitEffect(playerPos);
            }
        }

        // Boss Collateral: Straight slash cuts through any minions in the line!
        Collider2D[] slashHits = Physics2D.OverlapCircleAll(origin + direction * (range * 0.5f), range * 0.6f);
        float maxCollateralDist = width * 0.75f;
        for (int i = 0; i < slashHits.Length; i++)
        {
            Collider2D hitCol = slashHits[i];
            if (hitCol == null || hitCol.gameObject == gameObject) continue;

            Vector2 hitPos = hitCol.transform.position;
            float dist = DistancePointToLineSegment(hitPos, origin, end);
            Vector2 toHit = hitPos - origin;
            float lineProj = Vector2.Dot(toHit, direction);

            if (dist <= maxCollateralDist && lineProj >= -0.2f && lineProj <= range + 0.5f)
            {
                if (hitCol.TryGetComponent<ChainEnemy>(out ChainEnemy bomb) && !bomb.IsDead)
                {
                    bomb.OnDeath(direction);
                }
                else if (hitCol.TryGetComponent<EnemyBase>(out EnemyBase eb) && !eb.IsDead && !(eb is BossTargetPoint))
                {
                    eb.OnDeath(direction);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Pattern 2: Stomp (Phase 2 / 80%)
    // -------------------------------------------------------------------------

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
                Vector3 pos = transform.position;
                float targetX = playerController.transform.position.x;
                pos.x = Mathf.MoveTowards(pos.x, targetX, 7.5f * speedMult * Time.deltaTime);
                transform.position = pos;
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
        for (int i = 0; i < hitColliders.Length; i++)
        {
            Collider2D hit = hitColliders[i];
            if (hit == null || hit.gameObject == gameObject) continue;

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

    // -------------------------------------------------------------------------
    // Pattern 3: 360 Shuriken (Phase 3 / 60%)
    // -------------------------------------------------------------------------

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
        if (count <= 0) return;

        float angleStep = 360f / count;
        float speed = shurikenSpeed360 * GetSpeedMultiplier();
        Vector2 origin = transform.position;

        for (int i = 0; i < count; i++)
        {
            float rad = (i * angleStep + angleOffset) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector2 spawnPos = origin + dir * 0.85f;

            if (shurikenPrefab != null)
            {
                GameObject proj = Instantiate(shurikenPrefab, spawnPos, Quaternion.identity);
                if (proj.TryGetComponent<Shuriken>(out Shuriken shurikenComp))
                {
                    shurikenComp.Launch(dir, speed, shurikenDamage360);
                }
            }
            else
            {
                Shuriken.CreateDefaultShuriken(spawnPos, dir, speed, shurikenDamage360, groundLayer);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Pattern 4: Wind Slash (Phase 4 / 40%)
    // -------------------------------------------------------------------------

    private static AnimationCurve cachedTaperedSlashCurve;

    private static AnimationCurve GetTaperedSlashWidthCurve()
    {
        if (cachedTaperedSlashCurve != null) return cachedTaperedSlashCurve;
        cachedTaperedSlashCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.0f),
            new Keyframe(0.2f, 0.65f),
            new Keyframe(0.5f, 1.0f),
            new Keyframe(0.8f, 0.65f),
            new Keyframe(1.0f, 0.0f)
        );
        return cachedTaperedSlashCurve;
    }

    private IEnumerator WindSlashRoutine()
    {
        bossState = BossActionState.WindSlash;
        float speedMult = GetSpeedMultiplier();

        rb.linearVelocity = Vector2.zero;
        PlayActionAnimation(BossAnimState.WindSlash, loop: false);

        // Aura windup charge
        if (spriteRenderer != null) spriteRenderer.color = new Color(0.2f, 1f, 0.7f, 1f);
        FacePlayer();

        Vector2 playerCenter = playerController != null
            ? (Vector2)playerController.transform.position
            : (Vector2)transform.position + Vector2.right * 4f;
        int lineCount = Mathf.Clamp(windSlashLineCount + (GetCurrentPhase() >= 5 ? 1 : 0), 3, 5);

        List<GameObject> lineObjects = new List<GameObject>(lineCount * 3 + 16);
        List<LineRenderer> outerLineRenderers = new List<LineRenderer>(lineCount);
        List<LineRenderer> coreLineRenderers = new List<LineRenderer>(lineCount);
        List<KeyValuePair<Vector2, Vector2>> lineSegments = new List<KeyValuePair<Vector2, Vector2>>(lineCount);
        List<Vector2> lineDirections = new List<Vector2>(lineCount);
        List<Vector2> lineCenters = new List<Vector2>(lineCount);

        const float lineLength = 24f;
        Shader lineShader = GetDefaultSpriteShader();
        Material sharedLineMat = lineShader != null ? new Material(lineShader) : null;
        AnimationCurve taperedCurve = GetTaperedSlashWidthCurve();

        Color vibrantWindColor = new Color(windSlashTelegraphColor.r, windSlashTelegraphColor.g, windSlashTelegraphColor.b, 0.88f);
        Color snapGlintColor = Color.Lerp(vibrantWindColor, Color.white, 0.75f);
        snapGlintColor.a = 0.95f;

        // 1. Generate Dual-Layer Tapered Katana Slash Lines (Outer Wind Aura + Inner Hot Core)
        for (int i = 0; i < lineCount; i++)
        {
            float angle = -35f + (i * (70f / Mathf.Max(1, lineCount - 1))) + Random.Range(-10f, 10f);
            float rad = angle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

            Vector2 centerPoint = playerCenter + Random.insideUnitCircle * 0.6f;
            Vector2 start = centerPoint - dir * (lineLength * 0.5f);
            Vector2 end = centerPoint + dir * (lineLength * 0.5f);

            lineSegments.Add(new KeyValuePair<Vector2, Vector2>(start, end));
            lineDirections.Add(dir);
            lineCenters.Add(centerPoint);

            // Outer Wind Aura LineRenderer (5 points for tapered curve)
            GameObject outerObj = new GameObject($"WindSlashOuter_{i}");
            LineRenderer outerLr = outerObj.AddComponent<LineRenderer>();
            outerLr.positionCount = 5;
            outerLr.widthCurve = taperedCurve;
            outerLr.widthMultiplier = 0.06f;
            if (sharedLineMat != null) outerLr.sharedMaterial = sharedLineMat;
            outerLr.startColor = windSlashTelegraphColor;
            outerLr.endColor = windSlashTelegraphColor;
            outerLr.sortingOrder = 14;

            // Inner Core LineRenderer
            GameObject coreObj = new GameObject($"WindSlashCore_{i}");
            LineRenderer coreLr = coreObj.AddComponent<LineRenderer>();
            coreLr.positionCount = 5;
            coreLr.widthCurve = taperedCurve;
            coreLr.widthMultiplier = 0.02f;
            if (sharedLineMat != null) coreLr.sharedMaterial = sharedLineMat;
            coreLr.startColor = new Color(1f, 1f, 1f, 0.65f);
            coreLr.endColor = new Color(1f, 1f, 1f, 0.65f);
            coreLr.sortingOrder = 15;

            SetSubdividedLinePositions(outerLr, start, start);
            SetSubdividedLinePositions(coreLr, start, start);

            lineObjects.Add(outerObj);
            lineObjects.Add(coreObj);
            outerLineRenderers.Add(outerLr);
            coreLineRenderers.Add(coreLr);
        }

        // 2. Spawn Swirling Pixel Wind-Leaves / Vacuum Particles during Telegraph
        const int windParticleCount = 12;
        List<Transform> windParticles = new List<Transform>(windParticleCount);
        List<SpriteRenderer> windParticleRenderers = new List<SpriteRenderer>(windParticleCount);
        List<Vector2> windParticleTargets = new List<Vector2>(windParticleCount);
        List<Vector2> windParticleStartOffsets = new List<Vector2>(windParticleCount);

        Sprite sparkSprite = BossStraightSlashVFX.GetOrCreateSparkSprite();
        Sprite streakSprite = BossStraightSlashVFX.GetOrCreateStreakBarSprite();

        for (int i = 0; i < windParticleCount; i++)
        {
            GameObject pObj = new GameObject($"WindVacuumParticle_{i}");
            Transform pTrans = pObj.transform;
            Vector2 targetHub = (i < lineCount * 2)
                ? lineCenters[i % lineCount] + Random.insideUnitCircle * 1.4f
                : (Vector2)transform.position + new Vector2(0f, 0.65f);
            Vector2 startOffset = Random.insideUnitCircle.normalized * Random.Range(2.2f, 4.2f);

            pTrans.position = targetHub + startOffset;
            float pAngle = Mathf.Atan2(-startOffset.y, -startOffset.x) * Mathf.Rad2Deg;
            pTrans.rotation = Quaternion.Euler(0f, 0f, pAngle);
            pTrans.localScale = (i % 3 == 0) ? new Vector3(0.9f, 0.45f, 1f) : new Vector3(1.1f, 1.1f, 1f);

            SpriteRenderer pSr = pObj.AddComponent<SpriteRenderer>();
            pSr.sprite = (i % 3 == 0) ? streakSprite : sparkSprite;
            pSr.color = (i % 2 == 0) ? vibrantWindColor : Color.Lerp(vibrantWindColor, Color.white, 0.5f);
            pSr.sortingOrder = 16;

            lineObjects.Add(pObj);
            windParticles.Add(pTrans);
            windParticleRenderers.Add(pSr);
            windParticleTargets.Add(targetHub);
            windParticleStartOffsets.Add(startOffset);
        }

        // 3. "Sheathe & Draw" Telegraph: Laser Trace -> Vacuum Swirl -> Snap-Glint Lock-On
        float telegraphDuration = windSlashTelegraphTime / speedMult;
        float snapWindow = Mathf.Min(0.11f, telegraphDuration * 0.28f);
        bool snapGlintTriggered = false;
        float t = 0f;

        while (t < telegraphDuration && !IsDead && currentHits > 0)
        {
            float dt = Time.deltaTime;
            t += dt;
            float normT = Mathf.Clamp01(t / telegraphDuration);

            // Fast initial laser draw-in across the first 45% of telegraph
            float drawProgress = Mathf.Clamp01(normT / 0.45f);
            bool inSnapWindow = (telegraphDuration - t) <= snapWindow;

            if (inSnapWindow && !snapGlintTriggered)
            {
                snapGlintTriggered = true;
                SpawnWindKatanaGlint(lineObjects, vibrantWindColor);
            }

            float outerWidth = inSnapWindow
                ? 0.03f
                : 0.09f + Mathf.PingPong(t * 10f, 0.10f);
            float coreWidth = inSnapWindow ? 0.02f : outerWidth * 0.4f;
            Color currentOuterColor = inSnapWindow ? snapGlintColor : windSlashTelegraphColor;

            for (int i = 0; i < lineCount; i++)
            {
                KeyValuePair<Vector2, Vector2> seg = lineSegments[i];
                Vector2 curEnd = Vector2.Lerp(seg.Key, seg.Value, drawProgress);

                LineRenderer outerLr = outerLineRenderers[i];
                if (outerLr != null)
                {
                    SetSubdividedLinePositions(outerLr, seg.Key, curEnd);
                    outerLr.widthMultiplier = outerWidth;
                    outerLr.startColor = currentOuterColor;
                    outerLr.endColor = currentOuterColor;
                }

                LineRenderer coreLr = coreLineRenderers[i];
                if (coreLr != null)
                {
                    SetSubdividedLinePositions(coreLr, seg.Key, curEnd);
                    coreLr.widthMultiplier = coreWidth;
                }
            }

            // Animate swirling vacuum wind particles sucking inward toward the slash lines
            float swirlEase = normT * normT;
            for (int i = 0; i < windParticles.Count; i++)
            {
                Transform pTrans = windParticles[i];
                if (pTrans == null) continue;

                Vector2 startOff = windParticleStartOffsets[i];
                float spinRad = normT * 4.5f * ((i % 2 == 0) ? 1f : -1f);
                float cos = Mathf.Cos(spinRad);
                float sin = Mathf.Sin(spinRad);
                Vector2 rotatedOff = new Vector2(startOff.x * cos - startOff.y * sin, startOff.x * sin + startOff.y * cos);
                Vector2 curPos = windParticleTargets[i] + rotatedOff * (1f - swirlEase * 0.88f);

                pTrans.position = curPos;
                pTrans.Rotate(0f, 0f, 360f * dt);
            }

            yield return null;
        }

        // Hide vacuum particles on cut ignition
        for (int i = 0; i < windParticleRenderers.Count; i++)
        {
            if (windParticleRenderers[i] != null) windParticleRenderers[i].enabled = false;
        }

        if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;
        if (IsDead || currentHits <= 0)
        {
            StopActionAnimation();
            CleanupLineObjects(lineObjects);
            if (sharedLineMat != null) Destroy(sharedLineMat);
            yield break;
        }

        // 4. Staggered "Anime Multi-Cut" Cascade (1 -> 2 -> 3 -> 4) with Procedural Blades & Crescent Wind Waves!
        AudioClip sfx = windSlashSFX != null ? windSlashSFX : slashDashSFX;
        bool playerHit = false;
        float cascadeInterval = 0.045f / Mathf.Max(1f, speedMult * 0.75f);
        WaitForSeconds cascadeWait = new WaitForSeconds(cascadeInterval);

        for (int i = 0; i < lineCount; i++)
        {
            if (IsDead || currentHits <= 0) break;

            // Escalating pitch per cut in the cascade
            float cutPitch = 1.15f + (i * 0.12f);
            PlayBossSFX(sfx, cutPitch, windSlashVolume);

            if (playerController != null)
            {
                playerController.TriggerCameraShake(i == lineCount - 1 ? 0.35f : 0.18f);
            }

            KeyValuePair<Vector2, Vector2> seg = lineSegments[i];
            Vector2 dir = lineDirections[i];
            Vector2 center = lineCenters[i];

            // Expand Dual-Layer Tapered Katana Line to full blazing width
            LineRenderer outerLr = outerLineRenderers[i];
            if (outerLr != null)
            {
                SetSubdividedLinePositions(outerLr, seg.Key, seg.Value);
                outerLr.widthMultiplier = 0.72f;
                outerLr.startColor = vibrantWindColor;
                outerLr.endColor = vibrantWindColor;
            }

            LineRenderer coreLr = coreLineRenderers[i];
            if (coreLr != null)
            {
                SetSubdividedLinePositions(coreLr, seg.Key, seg.Value);
                coreLr.widthMultiplier = 0.28f;
                coreLr.startColor = windSlashActiveColor;
                coreLr.endColor = windSlashActiveColor;
            }

            // Spawn Emerald/White Tapered Pixel Blade Beam + Flying Wind Sparks at the intersection zone
            const float beamSpan = 9.5f;
            Vector3 beamStart = (Vector3)(center - dir * (beamSpan * 0.5f));
            BossStraightSlashVFX.SpawnWindSlash(beamStart, dir, beamSpan, vibrantWindColor, windSlashActiveColor);

            // Spawn Traveling Pixel Crescent Wind Wave racing along the slash cut
            Vector3 waveStart = (Vector3)(center - dir * 5.5f);
            BossSlashDashVFX.SpawnTravelingWindCrescent(waveStart, dir, 28f * speedMult, 0.38f, vibrantWindColor, windSlashActiveColor);

            // Spawn Drifting Air-Scar After-Ripples parallel to the cut
            SpawnAirScarAfterRipples(lineObjects, center, dir, vibrantWindColor);

            // Check player hit along this cut
            if (!playerHit && playerController != null)
            {
                Vector2 curPlayerPos = playerController.transform.position;
                if (DistancePointToSegment(curPlayerPos, seg.Key, seg.Value) <= 0.75f)
                {
                    playerHit = true;
                    playerController.TakeDamage(windSlashDamage);
                }
            }

            if (i < lineCount - 1)
            {
                yield return cascadeWait;
            }
        }

        // 5. Smooth Dual-Layer Katana Slice-Shut & Dissolve
        const float sliceShutDuration = 0.20f;
        float dissolveTimer = 0f;
        while (dissolveTimer < sliceShutDuration)
        {
            dissolveTimer += Time.deltaTime;
            float fade = 1f - Mathf.Clamp01(dissolveTimer / sliceShutDuration);
            float outerW = 0.72f * fade * fade;
            float coreW = 0.28f * fade;

            Color fadedOuter = vibrantWindColor;
            fadedOuter.a = vibrantWindColor.a * fade;
            Color fadedCore = windSlashActiveColor;
            fadedCore.a = fade;

            for (int i = 0; i < lineCount; i++)
            {
                LineRenderer outerLr = outerLineRenderers[i];
                if (outerLr != null)
                {
                    outerLr.widthMultiplier = outerW;
                    outerLr.startColor = fadedOuter;
                    outerLr.endColor = fadedOuter;
                }

                LineRenderer coreLr = coreLineRenderers[i];
                if (coreLr != null)
                {
                    coreLr.widthMultiplier = coreW;
                    coreLr.startColor = fadedCore;
                    coreLr.endColor = fadedCore;
                }
            }

            yield return null;
        }

        CleanupLineObjects(lineObjects);
        if (sharedLineMat != null) Destroy(sharedLineMat);

        yield return new WaitForSeconds(0.5f / speedMult);
        StopActionAnimation();
        if (!IsDead && currentHits > 0) bossState = BossActionState.Idle;
    }

    private static void SetSubdividedLinePositions(LineRenderer lr, Vector2 start, Vector2 end)
    {
        if (lr == null) return;
        int count = lr.positionCount;
        if (count < 2) return;

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);
            lr.SetPosition(i, Vector2.Lerp(start, end, t));
        }
    }

    private void SpawnWindKatanaGlint(List<GameObject> trackedObjects, Color windColor)
    {
        Vector3 glintPos = transform.position + new Vector3(spriteRenderer != null && spriteRenderer.flipX ? -0.55f : 0.55f, 0.65f, 0f);
        Sprite barSprite = BossStraightSlashVFX.GetOrCreateSparkSprite();

        for (int i = 0; i < 2; i++)
        {
            GameObject cross = new GameObject($"WindKatanaGlint_{i}");
            cross.transform.position = glintPos;
            cross.transform.rotation = Quaternion.Euler(0f, 0f, i * 90f + 45f);
            cross.transform.localScale = new Vector3(3.2f, 0.45f, 1f);

            SpriteRenderer sr = cross.AddComponent<SpriteRenderer>();
            sr.sprite = barSprite;
            sr.color = i == 0 ? Color.white : Color.Lerp(windColor, Color.white, 0.5f);
            sr.sortingOrder = 20;

            trackedObjects.Add(cross);
        }
    }

    private void SpawnAirScarAfterRipples(List<GameObject> trackedObjects, Vector2 center, Vector2 dir, Color windColor)
    {
        Vector2 perp = new Vector2(-dir.y, dir.x);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Sprite streakSprite = BossStraightSlashVFX.GetOrCreateStreakBarSprite();

        for (int side = -1; side <= 1; side += 2)
        {
            GameObject rippleObj = new GameObject("WindAirScarRipple");
            rippleObj.transform.position = center + perp * (side * 0.42f);
            rippleObj.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            rippleObj.transform.localScale = new Vector3(2.8f, 0.65f, 1f);

            SpriteRenderer sr = rippleObj.AddComponent<SpriteRenderer>();
            sr.sprite = streakSprite;
            Color c = Color.Lerp(windColor, Color.white, 0.35f);
            c.a = 0.75f;
            sr.color = c;
            sr.sortingOrder = 16;

            trackedObjects.Add(rippleObj);
        }
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

    // -------------------------------------------------------------------------
    // Pattern 5: Hidden Stab (Phase 5 / 20%)
    // -------------------------------------------------------------------------

    private IEnumerator HiddenStabRoutine()
    {
        bossState = BossActionState.HiddenStab;
        float speedMult = GetSpeedMultiplier();

        rb.linearVelocity = Vector2.zero;

        // 1. Vanish into smoke / stealth
        AudioClip vSfx = slashDashSFX != null ? slashDashSFX : bossRoarSFX;
        PlayBossSFX(vSfx, 1.35f, hiddenStabVolume * 0.85f);

        SpawnHitEffect(transform.position);

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

    #region Sequential Iai & Weak Points

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
                if (!obj.TryGetComponent<BossTargetPoint>(out point))
                {
                    point = obj.AddComponent<BossTargetPoint>();
                }
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
        currentSequenceIndex = 0;
        hitDeductedInCurrentSlash = false;
        areWeakPointsActive = true;
        modeExitTimer = 0f;

        Vector3 centerPos = weakPointContainer != null
            ? weakPointContainer.position
            : transform.position + new Vector3(0f, 0.65f, 0f);

        if (useSequentialIaiPatterns)
        {
            IaiPatternType pattern = GetPatternForPhase(GetCurrentPhase());
            List<Vector2> offsets = GetIaiPatternOffsets(pattern, targetPoints.Count);

            for (int i = 0; i < targetPoints.Count; i++)
            {
                if (targetPoints[i] == null) continue;

                Vector2 offset = i < offsets.Count ? offsets[i] : Vector2.zero;
                Vector3 worldPos = centerPos + (Vector3)offset;

                targetPoints[i].SetSequenceIndex(i, targetPoints.Count, weakPointFontAsset, weakPointNumberFontSize);
                targetPoints[i].SetSequenceState(i == 0 ? TargetPointSequenceState.ActiveNext : TargetPointSequenceState.Locked);
                targetPoints[i].ActivatePoint(worldPos);
            }
        }
        else
        {
            randomWeakPointOffsetsBuffer.Clear();

            for (int i = 0; i < targetPoints.Count; i++)
            {
                if (targetPoints[i] == null) continue;

                Vector2 offset = Vector2.zero;
                bool foundValid = false;

                // Pick a fully random angle and distance, ensuring circles do not stack directly on top of each other
                for (int attempt = 0; attempt < 15; attempt++)
                {
                    float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    float dist = Random.Range(weakPointRadiusMin, weakPointRadiusMax);
                    offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;

                    bool tooClose = false;
                    for (int j = 0; j < randomWeakPointOffsetsBuffer.Count; j++)
                    {
                        if ((offset - randomWeakPointOffsetsBuffer[j]).sqrMagnitude < MIN_WEAK_POINT_SPACING_SQR)
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
                    float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    float dist = Random.Range(weakPointRadiusMin, weakPointRadiusMax);
                    offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
                }

                randomWeakPointOffsetsBuffer.Add(offset);
                Vector3 worldPos = centerPos + (Vector3)offset;
                targetPoints[i].SetSequenceIndex(i, targetPoints.Count, weakPointFontAsset, weakPointNumberFontSize);
                targetPoints[i].SetSequenceState(TargetPointSequenceState.ActiveNext);
                targetPoints[i].ActivatePoint(worldPos);
            }
        }
    }

    private void ResetWeakPoints()
    {
        slashedCirclesCount = 0;
        currentSequenceIndex = 0;
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

    public bool IsPointNextInSequence(BossTargetPoint point)
    {
        if (!useSequentialIaiPatterns) return true;
        if (point == null) return false;
        return point.PointIndex == currentSequenceIndex;
    }

    public void OnTargetPointSelected(BossTargetPoint point)
    {
        if (point == null) return;

        // Play sequential pitch chime SFX if assigned
        if (pointSelectChimeSFX != null)
        {
            float pitch = 0.95f + Mathf.Clamp(point.PointIndex, 0, 8) * 0.12f;
            PlayBossSFX(pointSelectChimeSFX, pitch, 1.15f);
        }

        currentSequenceIndex = point.PointIndex + 1;

        if (useSequentialIaiPatterns && currentSequenceIndex < targetPoints.Count)
        {
            if (targetPoints[currentSequenceIndex] != null)
            {
                targetPoints[currentSequenceIndex].SetSequenceState(TargetPointSequenceState.ActiveNext);
            }
        }
    }

    private IaiPatternType GetPatternForPhase(int phase)
    {
        switch (phase)
        {
            case 1: return IaiPatternType.Ichimonji;
            case 2: return IaiPatternType.KesaGiri;
            case 3: return IaiPatternType.Jumonji;
            case 4: return IaiPatternType.Zantetsu;
            case 5:
            default:
                return IaiPatternType.GorinStar;
        }
    }

    private List<Vector2> GetIaiPatternOffsets(IaiPatternType pattern, int count)
    {
        List<Vector2> offsets = new List<Vector2>(Mathf.Max(5, count));

        switch (pattern)
        {
            case IaiPatternType.Ichimonji:
                // Pure horizontal slash across the samurai's torso
                // 5 points from left to right with subtle blade curve
                offsets.Add(new Vector2(-1.9f, 0.08f));
                offsets.Add(new Vector2(-0.95f, -0.02f));
                offsets.Add(new Vector2(0.0f, -0.06f));
                offsets.Add(new Vector2(0.95f, -0.02f));
                offsets.Add(new Vector2(1.9f, 0.08f));
                break;

            case IaiPatternType.KesaGiri:
                // Diagonal monk's robe crescent slash from shoulder across to hip
                offsets.Add(new Vector2(-1.45f, 1.45f));
                offsets.Add(new Vector2(-0.75f, 0.75f));
                offsets.Add(new Vector2(0.0f, 0.05f));
                offsets.Add(new Vector2(0.75f, -0.65f));
                offsets.Add(new Vector2(1.45f, -1.35f));
                break;

            case IaiPatternType.Jumonji:
                // Cross Slash: First horizontal cut (0->1->2), then top-down vertical strike (3->4)
                offsets.Add(new Vector2(-1.5f, 0.05f));
                offsets.Add(new Vector2(0.0f, 0.0f));
                offsets.Add(new Vector2(1.5f, -0.05f));
                offsets.Add(new Vector2(0.0f, 1.45f));
                offsets.Add(new Vector2(0.0f, -1.45f));
                break;

            case IaiPatternType.Zantetsu:
                // Zantetsu Z-Slash: Top cut -> diagonal cut -> bottom cut
                offsets.Add(new Vector2(-1.45f, 1.35f));
                offsets.Add(new Vector2(1.45f, 1.35f));
                offsets.Add(new Vector2(0.0f, 0.0f));
                offsets.Add(new Vector2(-1.45f, -1.35f));
                offsets.Add(new Vector2(1.45f, -1.35f));
                break;

            case IaiPatternType.GorinStar:
            default:
                // Five Rings / Pentagram Star Flurry (single continuous stroke)
                // Angles: 90, 306, 162, 18, 234 degrees
                const float starRadius = 1.6f;
                for (int i = 0; i < GorinStarAngles.Length; i++)
                {
                    float rad = GorinStarAngles[i] * Mathf.Deg2Rad;
                    offsets.Add(new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * starRadius);
                }
                break;
        }

        // Apply spread multiplier and boss facing orientation (flipX)
        bool isFacingFlipped = spriteRenderer != null && spriteRenderer.flipX;
        for (int i = 0; i < offsets.Count; i++)
        {
            Vector2 p = offsets[i] * iaiPatternSpread;
            if (isFacingFlipped)
            {
                p.x = -p.x;
            }
            offsets[i] = p;
        }

        // Safety fallback if count differs from 5
        while (offsets.Count < count)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            offsets.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(weakPointRadiusMin, weakPointRadiusMax));
        }

        return offsets;
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

    private bool AreAllCirclesSelected()
    {
        if (targetPoints.Count < requiredWeakPoints) return false;

        for (int i = 0; i < targetPoints.Count; i++)
        {
            BossTargetPoint pt = targetPoints[i];
            if (pt == null || (!pt.IsTarget && !pt.IsSlashed))
            {
                return false;
            }
        }
        return true;
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

    #endregion

    #region Combat & Damage

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
        yield return WaitDeflectReset;
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
        SpawnHitEffect(transform.position);

        ResetWeakPoints();
    }

    private void TriggerHitFlash(Color? flashColor = null)
    {
        Color targetColor = flashColor ?? Color.white;
        if (pixelEffect != null)
        {
            pixelEffect.Flash(0.12f, targetColor);
            return;
        }

        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(HitFlashRoutine(targetColor));
    }

    private IEnumerator HitFlashRoutine(Color color)
    {
        if (spriteRenderer != null) spriteRenderer.color = color;
        yield return WaitHitFlash;
        if (spriteRenderer != null && !isTarget) spriteRenderer.color = originalColor;
    }

    protected override void HandlePlayerContact(Collision2D collision)
    {
        if (IsDead || currentHits <= 0) return;

        if (isDashingSlash && collision.gameObject.TryGetComponent<PlayerController>(out PlayerController player))
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

        base.HandlePlayerContact(collision);
    }

    protected override bool HandleThrownImpact(Collision2D collision) => false;

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
        for (int i = 0; i < allColliders.Length; i++)
        {
            if (allColliders[i] != null) allColliders[i].enabled = false;
        }

        DestroyAllTargetPoints();

        // Clean up or defeat all active minions spawned by the boss so the arena is cleared cleanly
        for (int i = 0; i < activeSpawnedMinions.Count; i++)
        {
            GameObject minionObj = activeSpawnedMinions[i];
            if (minionObj != null)
            {
                if (minionObj.TryGetComponent<EnemyBase>(out EnemyBase eb) && !eb.IsDead)
                {
                    eb.OnDeath();
                }
                else
                {
                    Destroy(minionObj);
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
        const float duration = 1.0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = (Mathf.Sin(elapsed * 25f) > 0f) ? Color.red : Color.white;
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

    #endregion

    #region Minion Spawner

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
        yield return WaitMinionWaveInitial;

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
                spawnPoint = hit.collider != null
                    ? hit.point + Vector2.up * 0.5f
                    : (Vector2)transform.position + new Vector2(dist, 0f);
            }

            GameObject spawnedMinion = Instantiate(prefab, spawnPoint, Quaternion.identity);
            activeSpawnedMinions.Add(spawnedMinion);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.RegisterEnemy(spawnedMinion);
            }

            SpawnHitEffect(spawnPoint);

            yield return WaitMinionSpawnInterval;
        }
    }

    #endregion

    #region Sprite Animation

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
            Shader shader = GetDefaultSpriteShader();
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
        float interval = Mathf.Max(0.01f, afterImageInterval);
        WaitForSeconds waitInterval = new WaitForSeconds(interval);

        while (timer < duration && isDashingSlash && !IsDead && currentHits > 0)
        {
            SpawnSpriteAfterImage();
            yield return waitInterval;
            timer += interval;
        }
    }

    private void SpawnSpriteAfterImage()
    {
        SpriteRenderer sourceSr = spriteRenderer != null ? spriteRenderer : GetComponentInChildren<SpriteRenderer>();
        if (sourceSr == null || sourceSr.sprite == null) return;

        GameObject ghost = new GameObject("BossAfterImage");
        Transform sourceTransform = sourceSr.transform;
        ghost.transform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
        ghost.transform.localScale = sourceTransform.lossyScale;

        SpriteRenderer ghostSr = ghost.AddComponent<SpriteRenderer>();
        ghostSr.sprite = sourceSr.sprite;
        ghostSr.flipX = sourceSr.flipX;
        ghostSr.flipY = sourceSr.flipY;
        ghostSr.sortingOrder = Mathf.Max(0, sourceSr.sortingOrder - 1);
        ghostSr.sharedMaterial = sourceSr.sharedMaterial;
        ghostSr.color = new Color(dashTrailColor.r, dashTrailColor.g, dashTrailColor.b, 0.65f);

        StartCoroutine(FadeAndDestroyAfterImage(ghost, ghostSr));
    }

    private IEnumerator FadeAndDestroyAfterImage(GameObject ghost, SpriteRenderer sr)
    {
        const float lifetime = 0.22f;
        float elapsed = 0f;
        Color initialColor = sr != null ? sr.color : dashTrailColor;

        while (elapsed < lifetime)
        {
            elapsed += 0.055f;
            yield return WaitAfterImageFadeStep;

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

    #region Audio & BGM

    private void SetupBossAudio()
    {
        if (!TryGetComponent<AudioSource>(out bossAudioSource))
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

        bgmSourceA = CreateBGMSource("BossBGM_SourceA");
        bgmSourceB = CreateBGMSource("BossBGM_SourceB");
    }

    private AudioSource CreateBGMSource(string sourceName)
    {
        GameObject bgmObj = new GameObject(sourceName);
        bgmObj.transform.SetParent(transform, false);
        AudioSource src = bgmObj.AddComponent<AudioSource>();
        src.loop = loopBGM;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.priority = 16;
        src.volume = 0f;
        return src;
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
        float safeDuration = Mathf.Max(0.01f, duration);

        while (timer < safeDuration)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / safeDuration);

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
        float safeDuration = Mathf.Max(0.01f, duration);
        while (timer < safeDuration && src != null)
        {
            timer += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(0f, targetVol, timer / safeDuration);
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
        float safeDuration = Mathf.Max(0.01f, duration);
        while (timer < safeDuration && src != null)
        {
            timer += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(startVol, 0f, timer / safeDuration);
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

    #region Helpers & Gizmos

    private static Shader GetDefaultSpriteShader()
    {
        if (cachedDefaultSpriteShader == null)
        {
            cachedDefaultSpriteShader = Shader.Find("Sprites/Default");
            if (cachedDefaultSpriteShader == null)
            {
                cachedDefaultSpriteShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            }
            if (cachedDefaultSpriteShader == null)
            {
                cachedDefaultSpriteShader = Shader.Find("Hidden/Internal-Colored");
            }
        }
        return cachedDefaultSpriteShader;
    }

    private void SpawnHitEffect(Vector3 position)
    {
        if (hitVFX != null)
        {
            Instantiate(hitVFX, position, Quaternion.identity);
        }
        else
        {
            BossHitVFX.Spawn(position);
        }
    }

    private void CheckStompGroundContacts(Collision2D collision)
    {
        if (!isStompSlamming) return;

        int contactCount = collision.contactCount;
        for (int i = 0; i < contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.35f)
            {
                stompHasHitGround = true;
                break;
            }
        }
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

    private float DistancePointToLineSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        return DistancePointToSegment(point, start, end);
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
}
