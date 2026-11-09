using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
// Refactored cleanly
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody2D), typeof(AudioSource))]
public class PlayerController : MonoBehaviour
{
    #region Singleton & Constants

    public static PlayerController Instance { get; set; }

    public enum PlayerAnimState
    {
        Idle = 0,
        Walk = 1,
        Dash = 2,
        Jump = 3,
        DoubleJump = 4,
        Parry = 5,
        InModeSlashingStance = 6,
        ReleaseGrapplingHook = 7,
        Death = 8,
        Kick = 9
    }

    private static readonly float[] SafeSpawnXOffsets = { 0f, 1.5f, -1.5f, 3f, -3f, 4.5f, -4.5f, 6f, -6f, 8f, -8f };
    private static readonly float[] SafeSpawnYProbes = { 0.5f, 2f, -1f, 4f, 6.5f };

    private static readonly WaitForSeconds WaitHalfSecond = new WaitForSeconds(0.5f);
    private static readonly WaitForSeconds WaitOneSecond = new WaitForSeconds(1f);
    private static readonly WaitForSeconds WaitMeterRegenTick = new WaitForSeconds(0.03f);
    private static readonly WaitForSeconds WaitMeterDegenTick = new WaitForSeconds(0.005f);
    private static readonly WaitForSecondsRealtime WaitShimmerEcho = new WaitForSecondsRealtime(0.022f);

    private static Sprite cachedSolidWhiteSprite;
    private static Sprite cachedPixelHealCrossSprite;

    /// <summary>
    /// Explicitly destroys the current persistent player instance (e.g. on death or returning to Main Menu)
    /// so newly loaded scenes instantiate and bind their own fresh PlayerController and HUD UI.
    /// </summary>
    public static void DestroyCurrentInstance()
    {
        if (Instance != null)
        {
            GameObject go = Instance.gameObject;
            Instance = null;
            if (go != null)
            {
                go.SetActive(false);
                Destroy(go);
            }
        }
    }

    public static void EnforceSinglePlayerInstance()
    {
        PlayerController[] allPlayers = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (allPlayers.Length <= 1) return;

        // Prefer an alive player over a dead one
        PlayerController mainPlayer = (Instance != null && !Instance.isDead) ? Instance : null;
        if (mainPlayer == null)
        {
            for (int i = 0; i < allPlayers.Length; i++)
            {
                if (allPlayers[i] != null && !allPlayers[i].isDead)
                {
                    mainPlayer = allPlayers[i];
                    break;
                }
            }
        }
        if (mainPlayer == null && allPlayers.Length > 0)
        {
            mainPlayer = allPlayers[0];
        }
        Instance = mainPlayer;

        for (int i = 0; i < allPlayers.Length; i++)
        {
            PlayerController p = allPlayers[i];
            if (p != null && p != mainPlayer)
            {
                p.gameObject.SetActive(false);
                Destroy(p.gameObject);
            }
        }
    }

    #endregion

    #region Serialized Fields

    [Header("Mouse Cursor Settings")]
    [SerializeField] private Texture2D cursorTexture;
    [SerializeField] private CursorMode cursorMode = CursorMode.Auto;

    [Header("Player Health System Settings")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float healthRegenDelay = 4f;            // Delay in seconds after taking a hit
    [SerializeField] private float healthRegenTickInterval = 0.05f;    // Regeneration speed (+1 per tick)
    [SerializeField] private Image healthBarImage;                  // Fill Image for Health
    [SerializeField] private GameObject healthBackgroundObject;     // Background / Frame for Health UI
    [SerializeField] private float healthFadeDuration = 0.25f;
    [SerializeField] private float immunityDuration = 0.8f;
    [SerializeField] private Color damageFlashColor = new Color(1f, 0.2f, 0.2f, 1f);
    [SerializeField] private float damageFlashDuration = 0.25f;
    [SerializeField] private float damageShakeDuration = 0.15f;
    [SerializeField] private AudioClip takeDamageSFX;

    [Header("Player Healing Indicator Settings")]
    [Tooltip("Optional custom UI healing indicator (e.g. green plus badge or icon). Automatically created if left empty.")]
    [SerializeField] private GameObject healIndicatorObject;
    [Tooltip("Whether to show the healing plus badge beside the health bar.")]
    [SerializeField] private bool showBadgeOnBar = true;
    [Tooltip("Scale multiplier for the healing badge indicator.")]
    [SerializeField] private float healBadgeScale = 1.0f;
    [Tooltip("Audio clip played when health regeneration begins.")]
    [SerializeField] private AudioClip healStartSFX;
    [Tooltip("Color glow for the health bar while actively healing.")]
    [SerializeField] private Color healingGlowColor = new Color(0.25f, 1.0f, 0.55f, 1.0f);
    [Tooltip("Whether to spawn floating pixel healing plus particles around the player while healing.")]
    [SerializeField] private bool showFloatingHealSparks = true;
    [Tooltip("Scale multiplier for floating healing spark particles.")]
    [SerializeField] private float healSparkScale = 1.0f;

    public enum HealGestureType
    {
        None = 0,
        Plus = 1,
        Heart = 2
    }

    [Header("Draw-to-Heal Gesture Settings (Heart & Plus)")]
    [Tooltip("If enabled, passive regeneration is disabled and the player heals by drawing a Plus (+) or Heart (♡) in Chain Slash mode.")]
    [SerializeField] private bool enableDrawToHeal = true;
    [Tooltip("HP restored when drawing a Plus (+) symbol in Chain Slash mode.")]
    [SerializeField] private float plusHealAmount = 25f;
    [Tooltip("HP restored when drawing a Heart (♡) symbol in Chain Slash mode.")]
    [SerializeField] private float heartHealAmount = 50f;
    [Tooltip("Chain Slash meter consumed when completing a Plus (+) heal gesture.")]
    [SerializeField] private float plusHealMeterCost = 15f;
    [Tooltip("Chain Slash meter consumed when completing a Heart (♡) heal gesture.")]
    [SerializeField] private float heartHealMeterCost = 28f;
    [Tooltip("Minimum world-space width/height required for a drawn Heart or Plus gesture.")]
    [SerializeField] private float minHealGestureWorldSize = 0.7f;
    [Tooltip("Line glow color when a valid Plus (+) heal gesture is recognized.")]
    [SerializeField] private Color plusGestureGlowColor = new Color(0.25f, 1.0f, 0.55f, 1.0f);
    [Tooltip("Line glow color when a valid Heart (♡) heal gesture is recognized.")]
    [SerializeField] private Color heartGestureGlowColor = new Color(1.0f, 0.38f, 0.68f, 1.0f);

    [Header("Player Movement Settings")]
    [SerializeField] private float moveSpeed = 7.5f;
    [SerializeField] private float jumpHeight = 15f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float jumpBufferTime = 0.12f;
    [SerializeField] private float airMomentumDecay = 8f;
    [SerializeField] private float idleBobInterval = 0.5f;
    [SerializeField] private float idleSquashScaleY = 0.93f;
    [SerializeField] private List<AudioClip> walkSFXList = new List<AudioClip>();
    [SerializeField] private float walkStepInterval = 0.28f;
    [SerializeField] private AudioClip jumpSFX;
    [SerializeField] private AudioClip doubleJumpSFX;

    [Header("Stair & Step Handling")]
    [Tooltip("Maximum vertical height of a step or stair that can be walked up automatically without jumping.")]
    [SerializeField] private float maxStepHeight = 0.6f;
    [Tooltip("Forward distance to check ahead for upcoming stair steps.")]
    [SerializeField] private float stepCheckDistance = 0.22f;

    [Header("Wall Slide & Wall Jump Settings")]
    [SerializeField] private bool enableWallMechanics = true;
    [SerializeField] private float wallCheckDistance = 0.15f;
    [SerializeField] private float wallSlideSpeed = 2.5f;
    [SerializeField] private Vector2 wallJumpForce = new Vector2(11f, 14.5f);
    [SerializeField] private float wallJumpInputLockTime = 0.15f;

    [Header("Player Dashing Settings")]
    [SerializeField] private float dashingPower = 15f;
    [SerializeField] private float dashingTime = 0.2f;
    [SerializeField] private float dashingCooldown = 1f;
    [SerializeField] private AudioClip dashSFX;

    [Header("Chain Slash Mode Settings")]
    [SerializeField] private float maxMeter = 100f;
    [SerializeField] private float timeSpeed = 0.25f;
    [SerializeField] private float modeDimAlpha = 0.55f;
    [SerializeField] private float modeDimFadeDuration = 0.15f;
    [SerializeField] private float minPointDistance = 0.1f;
    [SerializeField] private float slashDrawDuration = 0.18f;
    [SerializeField] private float slashDisappearDuration = 0.3f;
    [SerializeField] private AudioClip targetSelectSFX;
    [SerializeField] private float targetSelectBasePitch = 0.95f;
    [SerializeField] private float targetSelectPitchStep = 0.1f;
    [SerializeField] private float targetSelectMaxPitch = 1.85f;
    [SerializeField] private AudioClip postSlashSFX;
    [SerializeField] private GameObject lineRendererObject;
    [SerializeField] private GameObject mouseTrailPrefab;
    [SerializeField] private CinemachineTargetGroup targetGroup;

    [Header("Chain Slash Meter UI Settings")]
    [SerializeField] private Image modeMeterImage;                 // Fill Image for Meter
    [SerializeField] private GameObject modeMeterBarObject;          // Background / Frame for Meter UI
    [SerializeField] private float meterFadeDuration = 0.25f;
    [SerializeField] private NoiseSettings noiseSettings;

    [Header("Slash Visuals & Juice")]
    [SerializeField] private float hitstopDuration = 0.05f;
    [SerializeField] private float jitterStrength = 0.1f;
    [SerializeField] private GameObject slashImpactPrefab;
    [SerializeField] private GameObject slashPathParticlePrefab;
    [SerializeField] private Gradient drawGradient;
    [SerializeField] private Gradient flashGradient;
    [SerializeField] private Gradient dissolveGradient;

    [Header("Grappling Hook Settings")]
    [SerializeField] private float maxGrappleRange = 12f;
    [SerializeField] private float grappleSpeed = 35f;
    [SerializeField] private float pullForce = 25f;
    [SerializeField] private float swingForce = 15f;
    [SerializeField] private float releaseBoost = 1.3f;
    [SerializeField] private float minDistance = 0.75f;
    [SerializeField] private LayerMask grappleLayer;

    [Header("Grapple Combat & Kick Settings")]
    [SerializeField] private float grappleKickSpeedThreshold = 11f;
    [SerializeField] private float grappleKickKnockback = 22f;
    [SerializeField] private float grappleKickBounceForce = 12.5f;

    [Header("Grappling Visual & Polish")]
    [SerializeField] private int ropeSegments = 20;
    [SerializeField] private float waveFrequency = 12f;
    [SerializeField] private float waveAmplitude = 0.6f;
    [Tooltip("Horizontal offset applied to the grappling hook origin when attached (moves right when facing right, left when facing left).")]
    [SerializeField] private float grappleAttachedHandOffsetX = 0.32f;
    [Tooltip("Vertical offset ratio relative to the player's height for the grappling hook starting point (> 0.5 places it above half of the player size).")]
    [SerializeField] private float grappleStartHeightMultiplier = 0.55f;
    [SerializeField] private AudioClip grappleReleaseSFX;
    [SerializeField] private AudioClip grappleHitSFX;
    [SerializeField] private GameObject grappleImpactPrefab;
    [SerializeField] private GameObject grappleTargetIndicatorPrefab; // Optional custom target indicator prefab
    [SerializeField] private float grappleIndicatorOrbitRadius = 1.8f;  // Orbit distance outside the player

    [Header("Audio Settings")]
    [SerializeField] private int maxSoundPlaying = 3;

    [Header("Player Sprite Animations (No Animator)")]
    [Tooltip("Idle / Standing animation sprite frames.")]
    [SerializeField] private List<Sprite> idleSprites = new List<Sprite>();
    [SerializeField] private float idleFPS = 4f;

    [Tooltip("Walk / Run animation sprite frames.")]
    [SerializeField] private List<Sprite> walkSprites = new List<Sprite>();
    [SerializeField] private float walkFPS = 10f;

    [Tooltip("Dash animation sprite frames.")]
    [SerializeField] private List<Sprite> dashSprites = new List<Sprite>();
    [SerializeField] private float dashFPS = 12f;

    [Tooltip("Jump animation sprite frames.")]
    [SerializeField] private List<Sprite> jumpSprites = new List<Sprite>();
    [SerializeField] private float jumpFPS = 8f;

    [Tooltip("Double Jump animation sprite frames.")]
    [SerializeField] private List<Sprite> doubleJumpSprites = new List<Sprite>();
    [SerializeField] private float doubleJumpFPS = 10f;

    [Tooltip("Parry animation sprite frames.")]
    [SerializeField] private List<Sprite> parrySprites = new List<Sprite>();
    [SerializeField] private float parryFPS = 12f;
    [SerializeField] private float parryAnimDuration = 0.28f;

    [Tooltip("In Mode Slashing Stance animation sprite frames.")]
    [SerializeField] private List<Sprite> inModeSlashingStanceSprites = new List<Sprite>();
    [SerializeField] private float inModeSlashingStanceFPS = 8f;

    [Tooltip("Release Grappling Hook animation sprite frames.")]
    [SerializeField] private List<Sprite> releaseGrapplingHookSprites = new List<Sprite>();
    [SerializeField] private float releaseGrapplingHookFPS = 12f;
    [SerializeField] private float releaseGrappleAnimDuration = 0.25f;

    [Tooltip("Death animation sprite frames.")]
    [SerializeField] private List<Sprite> deathSprites = new List<Sprite>();
    [SerializeField] private float deathFPS = 6f;

    [Tooltip("Kick animation sprite frames.")]
    [SerializeField] private List<Sprite> kickSprites = new List<Sprite>();
    [SerializeField] private float kickFPS = 12f;
    [SerializeField] private float kickAnimDuration = 0.30f;

    [Header("Juiciness & Game Feel Settings")]
    [Tooltip("Spawns fading pixel silhouette afterimages during dashes, double jumps, kicks, and high-speed grapple flight.")]
    [SerializeField] private bool enableAfterimages = true;
    [SerializeField] private float afterimageInterval = 0.035f;
    [SerializeField] private float afterimageLifetime = 0.22f;
    [SerializeField] private Color afterimageStartColor = new Color(1f, 1f, 1f, 0.65f);
    [SerializeField] private Color afterimageEndColor = new Color(1f, 1f, 1f, 0f);

    [Tooltip("Smoothly zooms the camera in during Chain Slash mode and adds impact zoom-punches on slash execution and kicks.")]
    [SerializeField] private bool enableDynamicCameraZoom = true;
    [SerializeField] private float chainSlashZoomMultiplier = 0.91f;
    [SerializeField] private float cameraZoomSmoothSpeed = 9f;

    [Tooltip("Spawns pixel dust puffs on jump takeoff, landing, wall-sliding, and air-rings on double-jump.")]
    [SerializeField] private bool enableMovementDustVFX = true;

    [Tooltip("Spawns rotating lock-on crosshair reticles and ascending 1x/2x/3x! combo popups on targeted enemies.")]
    [SerializeField] private bool enableTargetReticlesAndComboPopups = true;

    #endregion

    #region Private State

    // Movement state
    private bool doubleJump;
    private bool isDoubleJumping;
    private bool canDash = true;
    private bool isDashing;
    private Vector2 inputValue;
    private float coyoteTimeCounter;
    private float jumpBufferCounter;
    private bool isWallSliding;
    private int wallDirection;
    private float wallJumpLockCounter;
    private float lastGrappleKickTime;
    private float walkStepTimer;
    private int lastWalkSFXIndex = -1;
    private float idleBobTimer;
    private bool isIdleSquashed;
    private Vector3 originalSpriteScale = Vector3.one;
    private Vector3 originalSpriteLocalPos = Vector3.zero;
    private Vector3 initialSpawnPosition;
    private float defaultGravityScale = 2f;
    private bool pendingSpawnReset;
    private bool isDead;

    // Juiciness state
    private float afterimageTimer;
    private bool wasGroundedLastFrame = true;
    private float lastAirborneVelY;
    private float wallSlideDustTimer;
    private float baseCameraOrthoSize = -1f;
    private float currentZoomMultiplier = 1f;
    private float currentZoomPunchOffset = 0f;
    private Coroutine activeZoomPunchCoroutine;
    private Coroutine activeBriefHitstopCoroutine;
    private Coroutine activeLandingSquashCoroutine;
    private readonly List<GameObject> activeTargetReticles = new List<GameObject>();
    private static readonly Dictionary<int, Sprite> cachedComboPopupSprites = new Dictionary<int, Sprite>();

    // Sprite Animation State (No Animator)
    private PlayerAnimState currentAnimState = PlayerAnimState.Idle;
    private int currentAnimFrame = 0;
    private float animTimer = 0f;
    private float parryAnimTimer = 0f;
    private float kickAnimTimer = 0f;
    private float releaseGrappleAnimTimer = 0f;
    private Sprite defaultBaseSprite;

    // Health state
    private float immunityEndTime;

    // Combat state
    private bool isThrowingHook = false;
    private bool isFocusing = false;
    private float currentMeter;
    private float defaultFixedDeltaTime;
    private readonly List<Vector3> drawnLinePoints = new List<Vector3>();
    private HealGestureType currentPreviewedGesture = HealGestureType.None;

    // Grapple state
    private Vector2 grappleAnchorPoint;
    private Transform grappleTargetTransform;
    private Collider2D cachedGrappleEnemyCollider;
    private Vector2 currentHookTip;
    private GameObject insGrappleTargetIndicator;
    private GameObject grapplePixelRopeContainer;
    private readonly List<SpriteRenderer> grapplePixelLinkRenderers = new List<SpriteRenderer>();
    private SpriteRenderer grapplePixelClawRenderer;
    private static Sprite cachedGrappleLinkLightSprite;
    private static Sprite cachedGrappleLinkDarkSprite;
    private static Sprite cachedGrappleClawSprite;

    // Cinemachine Noise caching
    private float orgAmplitudeGain;
    private float orgFrequencyGain;
    private NoiseSettings orgNoiseSettings;
    private float shakeRemainingTime;

    // Active Coroutines
    private Coroutine activeMeterCoroutine;
    private Coroutine activeMeterFadeCoroutine;
    private Coroutine activeHealthRegenCoroutine;
    private Coroutine activeHealthFadeCoroutine;
    private Coroutine activeGrapplingCoroutine;
    private Coroutine activeShakeCoroutine;
    private Coroutine activeDamageFlashCoroutine;
    private Coroutine activeModeDimCoroutine;

    // Damage visual caching
    private Color originalSpriteColor = Color.white;
    private Color originalHealthBarColor = Color.lightGreen;
    private SpriteRenderer modeDimRenderer;

    // Instantiated Objects
    private GameObject insLineRendererObj;
    private LineRenderer insLineRendererCom;
    private LineRendererManager insLineRendererManager;
    private GameObject insMouseTrail;

    // Input Actions
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;
    private InputAction attackAction;
    private InputAction grappleAction;

    // Cached Components
    private Rigidbody2D rb;
    private Collider2D playerCollider;
    private AudioSource audioSource;
    private AudioSource pitchedAudioSource;
    private AudioSource shimmerAudioSource;
    private TrailRenderer trailRenderer;
    private SpriteRenderer spriteRenderer;
    private PixelSpriteEffect pixelEffect;
    private SpringJoint2D springJoint;
    private LineRenderer grappleLine;
    private Camera mainCamera;
    private CinemachineCamera cinemachineCamera;
    private CinemachineBasicMultiChannelPerlin perlin;

    private int soundPlaying;
    private float originalAudioSourceVolume = 1f;
    private float originalPitchedVolume = 1f;
    private float originalShimmerVolume = 1f;
    private readonly List<CanvasGroup> meterCanvasGroups = new List<CanvasGroup>();
    private readonly List<CanvasGroup> healthCanvasGroups = new List<CanvasGroup>();

    #endregion

    #region Public Properties

    public bool IsDead => isDead;
    public float CurrentHealth { get; private set; }
    public bool IsImmune => Time.unscaledTime < immunityEndTime || isDashing;
    public bool IsInMode { get; private set; }
    public bool IsGrappling { get; private set; }
    public Vector2 InputValue => inputValue;

    public void SetPendingSpawnReset(bool value) => pendingSpawnReset = value;

    public void SetSpawnPositionForNewScene(Vector3 spawnPos)
    {
        initialSpawnPosition = spawnPos;
        pendingSpawnReset = true;
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // If the old instance is dead, destroy it completely so this fresh alive player can bind UI
            if (Instance.isDead)
            {
                GameObject oldGo = Instance.gameObject;
                Instance = null;
                if (oldGo != null)
                {
                    oldGo.SetActive(false);
                    Destroy(oldGo);
                }
            }
            else
            {
                Instance.SetSpawnPositionForNewScene(transform.position);
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }
        }
        Instance = this;
        transform.SetParent(null);
        initialSpawnPosition = transform.position;

        EnforceSinglePlayerInstance();

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
                playerCollider = c;
                break;
            }
        }
        if (playerCollider == null) playerCollider = GetComponent<Collider2D>();
        if (playerCollider == null) playerCollider = GetComponentInChildren<Collider2D>();
        audioSource = GetComponent<AudioSource>();

        // Secondary audio sources for pitch-modulated SFX (footsteps, jumps, and combo target locks)
        pitchedAudioSource = gameObject.AddComponent<AudioSource>();
        shimmerAudioSource = gameObject.AddComponent<AudioSource>();
        if (audioSource != null)
        {
            originalAudioSourceVolume = audioSource.volume > 0f ? audioSource.volume : 1f;
            pitchedAudioSource.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
            pitchedAudioSource.spatialBlend = audioSource.spatialBlend;
            pitchedAudioSource.volume = audioSource.volume;
            originalPitchedVolume = pitchedAudioSource.volume;
            pitchedAudioSource.playOnAwake = false;

            shimmerAudioSource.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
            shimmerAudioSource.spatialBlend = audioSource.spatialBlend;
            shimmerAudioSource.volume = audioSource.volume;
            originalShimmerVolume = shimmerAudioSource.volume;
            shimmerAudioSource.playOnAwake = false;
        }

        trailRenderer = GetComponent<TrailRenderer>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalSpriteColor = spriteRenderer.color;
            originalSpriteScale = spriteRenderer.transform.localScale;
            originalSpriteLocalPos = spriteRenderer.transform.localPosition;
            defaultBaseSprite = spriteRenderer.sprite;
            if (!spriteRenderer.TryGetComponent<PixelSpriteEffect>(out pixelEffect))
            {
                pixelEffect = spriteRenderer.gameObject.AddComponent<PixelSpriteEffect>();
            }
        }

        // Disable any Animator component so code-driven sprite animation lists have full control
        Animator[] animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] != null)
            {
                animators[i].enabled = false;
            }
        }

        EnsureDefaultPlayerSprites();

        springJoint = GetComponent<SpringJoint2D>();
        grappleLine = GetComponent<LineRenderer>();
        if (grappleLine != null) grappleLine.enabled = false;

        BindSceneCameraAndTargets();

        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        dashAction = InputSystem.actions.FindAction("Dash");
        attackAction = InputSystem.actions.FindAction("Attack");
        grappleAction = InputSystem.actions.FindAction("Grapple");
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (insGrappleTargetIndicator != null)
        {
            Destroy(insGrappleTargetIndicator);
        }

        if (grapplePixelRopeContainer != null)
        {
            Destroy(grapplePixelRopeContainer);
        }
    }

    private void Start()
    {
        defaultFixedDeltaTime = Time.fixedDeltaTime;
        currentMeter = maxMeter;
        CurrentHealth = maxHealth;

        if (cursorTexture != null)
        {
            Cursor.SetCursor(cursorTexture, Vector2.zero, cursorMode);
        }

        SetupMeterUI();
        SetupHealthUI();
        SetupGrappleIndicator();
        SetupModeDimOverlay();
    }

    private void Update()
    {
        if (isDead)
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            UpdatePlayerSpriteAnimation(false);
            return;
        }

        RecordDrawnLinePoints();
        UpdateMouseTrail();
        UpdateGrappleIndicator();
        UpdateDynamicCameraZoom();

        if (wallJumpLockCounter > 0f)
        {
            wallJumpLockCounter -= Time.deltaTime;
        }

        if (isDashing)
        {
            UpdatePlayerSpriteAnimation(false);
            UpdateAfterimageGhosts();
            return;
        }

        bool grounded = IsGrounded();
        if (grounded)
        {
            if (!wasGroundedLastFrame && lastAirborneVelY < -3.2f)
            {
                OnPlayerLanded(Mathf.Abs(lastAirborneVelY));
            }
            coyoteTimeCounter = coyoteTime;
            doubleJump = true;
            isWallSliding = false;
            if (rb.linearVelocityY <= 0.25f)
            {
                isDoubleJumping = false;
            }
        }
        else
        {
            coyoteTimeCounter -= Time.deltaTime;
            if (rb != null)
            {
                lastAirborneVelY = rb.linearVelocityY;
            }
        }
        wasGroundedLastFrame = grounded;

        UpdateWallSlideState(grounded);
        UpdateWallSlideDust();
        HandleFootsteps(grounded);
        HandleJumpInput();

        if (dashAction != null && dashAction.triggered && canDash)
        {
            StartCoroutine(DashRoutine());
        }

        HandleCombatInput();
        HandleGrappleInput();
        Flip();
        HandleIdleSquashBob(grounded);
        UpdatePlayerSpriteAnimation(grounded);
        UpdateAfterimageGhosts();
    }

    private void FixedUpdate()
    {
        if (isDead || isDashing) return;

        if (moveAction != null)
        {
            inputValue = moveAction.ReadValue<Vector2>();

            if (IsGrappling && springJoint != null && springJoint.enabled)
            {
                Vector2 ropeDir = (grappleAnchorPoint - (Vector2)transform.position).normalized;
                // Right-handed perpendicular so pressing Right (+x) swings Right when hanging below an anchor
                Vector2 perpendicular = new Vector2(ropeDir.y, -ropeDir.x);
                if (perpendicular.x < 0f) perpendicular = -perpendicular;

                Vector2 steerForce = (perpendicular * inputValue.x + inputValue * 0.5f) * swingForce;
                rb.AddForce(steerForce, ForceMode2D.Force);
            }
            else if (wallJumpLockCounter <= 0f)
            {
                float targetSpeedX = inputValue.x * moveSpeed;
                float currentSpeedX = rb.linearVelocityX;
                bool grounded = IsGrounded();

                // Preserve high-speed airborne momentum from grapple releases, dashes, or wall kicks
                if (!grounded && Mathf.Abs(currentSpeedX) > moveSpeed && Mathf.Sign(inputValue.x) == Mathf.Sign(currentSpeedX) && Mathf.Abs(inputValue.x) > 0.05f)
                {
                    float decayedX = Mathf.MoveTowards(currentSpeedX, targetSpeedX, airMomentumDecay * Time.fixedDeltaTime);
                    rb.linearVelocity = new Vector2(decayedX, rb.linearVelocityY);
                }
                else if (!grounded && Mathf.Abs(currentSpeedX) > moveSpeed && Mathf.Abs(inputValue.x) <= 0.05f)
                {
                    float coastX = Mathf.MoveTowards(currentSpeedX, 0f, airMomentumDecay * 0.75f * Time.fixedDeltaTime);
                    rb.linearVelocity = new Vector2(coastX, rb.linearVelocityY);
                }
                else
                {
                    rb.linearVelocity = new Vector2(targetSpeedX, rb.linearVelocityY);
                }

                if (isWallSliding && rb.linearVelocityY < -wallSlideSpeed)
                {
                    rb.linearVelocity = new Vector2(rb.linearVelocityX, -wallSlideSpeed);
                }

                if (Mathf.Abs(inputValue.x) > 0.05f && !isDashing && !IsGrappling)
                {
                    HandleStepClimb(inputValue.x);
                }
            }
        }
    }

    private void LateUpdate()
    {
        soundPlaying = 0;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Instance != this)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        EnforceSinglePlayerInstance();

        // If returning to the Main Menu scene (Build Index 0 or MainMenu component present), clean up persistent Player instance
        if (scene.buildIndex == 0 || FindFirstObjectByType<MainMenu>() != null)
        {
            Time.timeScale = 1f;
            if (defaultFixedDeltaTime > 0f)
            {
                Time.fixedDeltaTime = defaultFixedDeltaTime;
            }
            Destroy(gameObject);
            return;
        }

        ResetStateForLoadedScene();
    }

    #endregion

    #region Health & Regen System

    /// <summary>
    /// Fully revives the player, restores full health & meter, enables colliders,
    /// resets rigidbody physics & animator, restores visuals, and teleports to the safe spawn position.
    /// </summary>
    public void ReviveAndReset()
    {
        isDead = false;
        CurrentHealth = maxHealth;
        currentMeter = maxMeter;
        pendingSpawnReset = true;

        if (rb != null)
        {
            rb.simulated = true;
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.gravityScale = defaultGravityScale;
            rb.linearVelocity = Vector2.zero;
        }

        Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) colliders[i].enabled = true;
        }

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];
            if (sr == null || sr == modeDimRenderer) continue;
            sr.color = originalSpriteColor;
            sr.sortingOrder = 5;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalSpriteColor;
            spriteRenderer.transform.localScale = originalSpriteScale;
            spriteRenderer.transform.localPosition = originalSpriteLocalPos;
            spriteRenderer.transform.localRotation = Quaternion.identity;
            spriteRenderer.sortingOrder = 5;
        }

        isDoubleJumping = false;
        parryAnimTimer = 0f;
        kickAnimTimer = 0f;
        releaseGrappleAnimTimer = 0f;
        SetPlayerAnimState(PlayerAnimState.Idle, true);

        DetachGrappling();
        ClearTargetReticles();
        isFocusing = false;
        IsInMode = false;
        isDashing = false;
        canDash = true;
        isWallSliding = false;
        wasGroundedLastFrame = true;
        lastAirborneVelY = 0f;
        ResetIdleSquashBob();

        if (insMouseTrail != null)
        {
            Destroy(insMouseTrail);
            insMouseTrail = null;
        }

        if (insLineRendererObj != null)
        {
            Destroy(insLineRendererObj);
            insLineRendererObj = null;
            insLineRendererCom = null;
            insLineRendererManager = null;
        }

        if (trailRenderer != null)
        {
            trailRenderer.emitting = false;
            trailRenderer.Clear();
        }

        if (healthBarImage != null)
        {
            healthBarImage.color = originalHealthBarColor;
            healthBarImage.fillAmount = 1f;
        }
        SetHealthAlpha(0f);

        if (modeMeterImage != null)
        {
            modeMeterImage.fillAmount = 1f;
        }
        SetMeterAlpha(0f);
        SetHealIndicatorActive(false);

        // Restore AudioSource volumes so rapid restarts never decay audio
        if (audioSource != null)
        {
            audioSource.volume = originalAudioSourceVolume;
            audioSource.UnPause();
        }
        if (pitchedAudioSource != null)
        {
            pitchedAudioSource.volume = originalPitchedVolume;
            pitchedAudioSource.UnPause();
        }
        if (shimmerAudioSource != null)
        {
            shimmerAudioSource.volume = originalShimmerVolume;
            shimmerAudioSource.UnPause();
        }

        Vector3 targetSpawnPos = GetSceneSpawnPosition();
        transform.position = ResolveSafePlayerPosition(targetSpawnPos);
        Physics2D.SyncTransforms();
        pendingSpawnReset = false;
    }

    private void SetupHealthUI()
    {
        healthCanvasGroups.Clear();

        if (healthBackgroundObject != null)
        {
            CanvasGroup cg = healthBackgroundObject.GetComponent<CanvasGroup>();
            if (cg == null) cg = healthBackgroundObject.AddComponent<CanvasGroup>();
            if (!healthCanvasGroups.Contains(cg)) healthCanvasGroups.Add(cg);
        }

        if (healthBarImage != null)
        {
            CanvasGroup cg = healthBarImage.GetComponent<CanvasGroup>();
            if (cg == null) cg = healthBarImage.gameObject.AddComponent<CanvasGroup>();
            if (!healthCanvasGroups.Contains(cg)) healthCanvasGroups.Add(cg);

            originalHealthBarColor = healthBarImage.color;
            healthBarImage.fillAmount = CurrentHealth / maxHealth;
        }

        // Automatic procedural healing badge indicator if none assigned
        if (healIndicatorObject == null && healthBarImage != null && showBadgeOnBar)
        {
            Transform parent = healthBarImage.transform.parent;
            Transform existing = parent.Find("HealIndicatorBadge");
            if (existing != null)
            {
                healIndicatorObject = existing.gameObject;
            }
            else
            {
                GameObject badge = new GameObject("HealIndicatorBadge");
                RectTransform rt = badge.AddComponent<RectTransform>();
                rt.SetParent(parent, false);

                // Auto-scale to match the health bar height accurately
                float barHeight = healthBarImage.rectTransform.rect.height;
                if (barHeight <= 0.001f) barHeight = healthBarImage.rectTransform.sizeDelta.y;
                if (barHeight <= 0.001f) barHeight = 0.12f;

                float badgeSize = Mathf.Clamp(barHeight * 1.25f * healBadgeScale, 0.06f, 24f);

                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(badgeSize, badgeSize);
                rt.anchoredPosition = new Vector2(-badgeSize * 0.4f, 0f);

                Image img = badge.AddComponent<Image>();
                img.sprite = GetOrCreatePixelHealCrossSprite();
                img.color = healingGlowColor;

                CanvasGroup cg = badge.AddComponent<CanvasGroup>();
                if (!healthCanvasGroups.Contains(cg)) healthCanvasGroups.Add(cg);

                healIndicatorObject = badge;
            }
        }

        SetHealIndicatorActive(false);
        SetHealthAlpha(0f); // Hidden when health is full
    }

    private void SetHealIndicatorActive(bool active)
    {
        if (healIndicatorObject != null)
        {
            healIndicatorObject.SetActive(active);
        }
    }

    private void SetHealthAlpha(float alpha)
    {
        for (int i = 0; i < healthCanvasGroups.Count; i++)
        {
            CanvasGroup cg = healthCanvasGroups[i];
            if (cg != null) cg.alpha = alpha;
        }
    }

    public void TakeDamage(float amount)
    {
        if (isDead || CurrentHealth <= 0f || IsImmune) return;

        immunityEndTime = Time.unscaledTime + immunityDuration;
        CurrentHealth = Mathf.Clamp(CurrentHealth - amount, 0f, maxHealth);

        if (healthBarImage != null)
        {
            healthBarImage.fillAmount = CurrentHealth / maxHealth;
            healthBarImage.color = originalHealthBarColor;
        }

        if (spriteRenderer != null && activeDamageFlashCoroutine == null)
        {
            spriteRenderer.color = originalSpriteColor;
        }

        SetHealIndicatorActive(false);

        PlaySFX(takeDamageSFX);
        TriggerDamageFeedback();
        ShowHealthBar();

        if (CurrentHealth <= 0f)
        {
            Die();
            return;
        }

        if (activeHealthRegenCoroutine != null)
        {
            StopCoroutine(activeHealthRegenCoroutine);
            activeHealthRegenCoroutine = null;
        }

        // Passive healing is disabled when Draw-to-Heal (Heart & Plus) is enabled
        if (!enableDrawToHeal)
        {
            activeHealthRegenCoroutine = StartCoroutine(HealthRegenRoutine());
        }
    }

    /// <summary>
    /// Restores player health when a Plus (+) or Heart (♡) gesture is drawn in Chain Slash mode.
    /// </summary>
    private void TriggerDrawHeal(HealGestureType gestureType)
    {
        if (isDead || CurrentHealth <= 0f || gestureType == HealGestureType.None) return;

        float healAmount = gestureType == HealGestureType.Heart ? heartHealAmount : plusHealAmount;
        float meterCost = gestureType == HealGestureType.Heart ? heartHealMeterCost : plusHealMeterCost;
        Color glowTint = gestureType == HealGestureType.Heart ? heartGestureGlowColor : plusGestureGlowColor;

        currentMeter = Mathf.Clamp(currentMeter - meterCost, 0f, maxMeter);
        if (modeMeterImage != null)
        {
            modeMeterImage.fillAmount = currentMeter / maxMeter;
        }

        if (activeHealthRegenCoroutine != null)
        {
            StopCoroutine(activeHealthRegenCoroutine);
            activeHealthRegenCoroutine = null;
        }

        activeHealthRegenCoroutine = StartCoroutine(DrawHealRoutine(healAmount, glowTint));
    }

    private IEnumerator DrawHealRoutine(float healAmount, Color glowTint)
    {
        ShowHealthBar();

        if (healStartSFX != null)
        {
            PlaySFX(healStartSFX);
        }
        else if (targetSelectSFX != null)
        {
            PlaySFX(targetSelectSFX);
        }

        SetHealIndicatorActive(true);

        // Spawn an immediate burst of pixel healing crosses around the player
        if (showFloatingHealSparks)
        {
            for (int i = 0; i < 6; i++)
            {
                SpawnHealPixelParticle();
            }
        }

        float startHP = CurrentHealth;
        float targetHP = Mathf.Clamp(CurrentHealth + healAmount, 0f, maxHealth);
        const float duration = 0.45f;
        float elapsed = 0f;
        float sparkTimer = 0f;

        while (elapsed < duration && !isDead)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            sparkTimer += dt;

            float t = Mathf.Clamp01(elapsed / duration);
            float easeOut = 1f - (1f - t) * (1f - t);
            CurrentHealth = Mathf.Lerp(startHP, targetHP, easeOut);

            if (healthBarImage != null)
            {
                healthBarImage.fillAmount = CurrentHealth / maxHealth;
                float pulse = Mathf.Sin(t * Mathf.PI);
                healthBarImage.color = Color.Lerp(originalHealthBarColor, glowTint, pulse);
            }

            if (spriteRenderer != null && activeDamageFlashCoroutine == null)
            {
                float pulse = Mathf.Sin(t * Mathf.PI);
                spriteRenderer.color = Color.Lerp(originalSpriteColor, glowTint, pulse * 0.42f);
            }

            if (showFloatingHealSparks && sparkTimer >= 0.10f)
            {
                sparkTimer = 0f;
                SpawnHealPixelParticle();
            }

            yield return null;
        }

        CurrentHealth = targetHP;
        if (healthBarImage != null)
        {
            healthBarImage.fillAmount = CurrentHealth / maxHealth;
            healthBarImage.color = originalHealthBarColor;
        }

        if (spriteRenderer != null && activeDamageFlashCoroutine == null)
        {
            spriteRenderer.color = originalSpriteColor;
        }

        SetHealIndicatorActive(false);

        if (CurrentHealth >= maxHealth - 0.01f)
        {
            HideHealthBarWithPulse();
        }

        activeHealthRegenCoroutine = null;
    }

    private void TriggerDamageFeedback()
    {
        if (pixelEffect != null)
        {
            pixelEffect.Flash(damageFlashDuration, damageFlashColor);
        }

        if (activeDamageFlashCoroutine != null) StopCoroutine(activeDamageFlashCoroutine);
        activeDamageFlashCoroutine = StartCoroutine(DamageFlashRoutine());

        TriggerCameraShake(damageShakeDuration);
    }

    private IEnumerator DamageFlashRoutine()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = damageFlashColor;
        }

        if (healthBarImage != null)
        {
            healthBarImage.color = damageFlashColor;
        }

        yield return new WaitForSecondsRealtime(damageFlashDuration);

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalSpriteColor;
        }

        if (healthBarImage != null)
        {
            healthBarImage.color = originalHealthBarColor;
        }

        activeDamageFlashCoroutine = null;
    }

    private IEnumerator HealthRegenRoutine()
    {
        yield return new WaitForSeconds(healthRegenDelay);

        if (CurrentHealth <= 0f) yield break;

        // Healing has started!
        if (healStartSFX != null)
        {
            PlaySFX(healStartSFX);
        }

        SetHealIndicatorActive(true);

        float sparkSpawnTimer = 0f;
        const float sparkInterval = 0.22f;
        WaitForSeconds tickWait = new WaitForSeconds(healthRegenTickInterval);

        while (CurrentHealth < maxHealth)
        {
            CurrentHealth = Mathf.Clamp(CurrentHealth + 1f, 0f, maxHealth);

            if (healthBarImage != null)
            {
                healthBarImage.fillAmount = CurrentHealth / maxHealth;

                // Pulsing emerald glow on health bar while healing
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
                healthBarImage.color = Color.Lerp(originalHealthBarColor, healingGlowColor, pulse);
            }

            // Subtle healing tint on player sprite
            if (spriteRenderer != null && activeDamageFlashCoroutine == null)
            {
                float spritePulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
                spriteRenderer.color = Color.Lerp(originalSpriteColor, new Color(0.65f, 1.0f, 0.78f, 1.0f), spritePulse * 0.35f);
            }

            // Floating pixel healing plus particles around player
            if (showFloatingHealSparks)
            {
                sparkSpawnTimer += healthRegenTickInterval;
                if (sparkSpawnTimer >= sparkInterval)
                {
                    sparkSpawnTimer = 0f;
                    SpawnHealPixelParticle();
                }
            }

            yield return tickWait;
        }

        // Restore default colors
        if (healthBarImage != null)
        {
            healthBarImage.color = originalHealthBarColor;
        }
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalSpriteColor;
        }

        SetHealIndicatorActive(false);

        HideHealthBarWithPulse();
        activeHealthRegenCoroutine = null;
    }

    private static Sprite GetOrCreatePixelHealCrossSprite()
    {
        if (cachedPixelHealCrossSprite != null) return cachedPixelHealCrossSprite;

        const int size = 5;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0);
        Color green = new Color(0.25f, 1.0f, 0.55f, 1.0f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool isCross = (x == 2) || (y == 2);
                tex.SetPixel(x, y, isCross ? green : clear);
            }
        }
        tex.Apply();
        cachedPixelHealCrossSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 48f);
        return cachedPixelHealCrossSprite;
    }

    private void SpawnHealPixelParticle()
    {
        GameObject spark = new GameObject("HealPixelSpark");
        Vector3 offset = new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(-0.35f, 0.35f), 0f);
        spark.transform.position = transform.position + offset;

        SpriteRenderer sr = spark.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreatePixelHealCrossSprite();
        sr.color = new Color(0.3f, 1.0f, 0.55f, 0.95f);
        sr.sortingOrder = (spriteRenderer != null ? spriteRenderer.sortingOrder : 5) + 1;

        StartCoroutine(HealSparkRoutine(spark, sr));
    }

    private IEnumerator HealSparkRoutine(GameObject spark, SpriteRenderer sr)
    {
        float timer = 0f;
        const float duration = 0.50f;
        float floatSpeed = Random.Range(0.9f, 1.5f);
        Vector3 startScale = Vector3.one * (0.65f * healSparkScale);

        while (timer < duration && spark != null)
        {
            timer += Time.deltaTime;
            float t = timer / duration;

            spark.transform.position += Vector3.up * (floatSpeed * Time.deltaTime);
            spark.transform.localScale = startScale * (1f - (t * 0.35f));

            if (sr != null)
            {
                Color c = sr.color;
                c.a = Mathf.Clamp01(1f - (t * t));
                sr.color = c;
            }

            yield return null;
        }

        if (spark != null) Destroy(spark);
    }

    private void ShowHealthBar()
    {
        if (activeHealthFadeCoroutine != null) StopCoroutine(activeHealthFadeCoroutine);
        activeHealthFadeCoroutine = StartCoroutine(FadeHealthBar(1f, false));
    }

    private void HideHealthBarWithPulse()
    {
        if (activeHealthFadeCoroutine != null) StopCoroutine(activeHealthFadeCoroutine);
        activeHealthFadeCoroutine = StartCoroutine(FadeHealthBar(0f, true));
    }

    private IEnumerator FadeHealthBar(float targetAlpha, bool pulseOnFull)
    {
        if (healthCanvasGroups.Count == 0) yield break;

        if (pulseOnFull)
        {
            float pulseTimer = 0f;
            const float pulseDuration = 0.15f;
            while (pulseTimer < pulseDuration)
            {
                pulseTimer += Time.deltaTime;
                float scale = Mathf.Lerp(1f, 1.15f, Mathf.Sin((pulseTimer / pulseDuration) * Mathf.PI));

                for (int i = 0; i < healthCanvasGroups.Count; i++)
                {
                    if (healthCanvasGroups[i] != null) healthCanvasGroups[i].transform.localScale = Vector3.one * scale;
                }
                yield return null;
            }

            for (int i = 0; i < healthCanvasGroups.Count; i++)
            {
                if (healthCanvasGroups[i] != null) healthCanvasGroups[i].transform.localScale = Vector3.one;
            }
        }

        float startAlpha = healthCanvasGroups.Count > 0 && healthCanvasGroups[0] != null ? healthCanvasGroups[0].alpha : 0f;
        float timer = 0f;

        while (timer < healthFadeDuration)
        {
            timer += Time.deltaTime;
            float currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, timer / healthFadeDuration);
            SetHealthAlpha(currentAlpha);
            yield return null;
        }

        SetHealthAlpha(targetAlpha);
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        if (activeHealthRegenCoroutine != null)
        {
            StopCoroutine(activeHealthRegenCoroutine);
            activeHealthRegenCoroutine = null;
        }

        if (activeDamageFlashCoroutine != null)
        {
            StopCoroutine(activeDamageFlashCoroutine);
            activeDamageFlashCoroutine = null;
        }

        SetHealIndicatorActive(false);

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalSpriteColor;
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        parryAnimTimer = 0f;
        kickAnimTimer = 0f;
        releaseGrappleAnimTimer = 0f;
        SetPlayerAnimState(PlayerAnimState.Death, true);

        DetachGrappling();

        if (insMouseTrail != null)
        {
            insMouseTrail.SetActive(false);
        }

        if (insLineRendererObj != null)
        {
            insLineRendererObj.SetActive(false);
        }

        if (grappleLine != null)
        {
            grappleLine.enabled = false;
        }

        if (insGrappleTargetIndicator != null)
        {
            insGrappleTargetIndicator.SetActive(false);
        }

        if (IsInMode)
        {
            ExitChainSlashMode();
        }

        if (modeDimRenderer != null)
        {
            modeDimRenderer.color = new Color(0f, 0f, 0f, 0f);
        }

        SetHealthAlpha(0f);
        SetMeterAlpha(0f);

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        GameOverPanel.GetOrCreateInstance().ShowGameOver(transform, spriteRenderer);
    }

    #endregion

    #region Movement & Physics

    private void UpdateWallSlideState(bool grounded)
    {
        if (!enableWallMechanics || grounded || IsGrappling || wallJumpLockCounter > 0f)
        {
            isWallSliding = false;
            return;
        }

        int detectedWall = GetTouchingWallDirection();
        if (detectedWall != 0 && rb.linearVelocityY <= 0.1f)
        {
            bool pressingTowardsWall = Mathf.Abs(inputValue.x) > 0.1f && Mathf.Sign(inputValue.x) == detectedWall;
            if (pressingTowardsWall || isWallSliding)
            {
                isWallSliding = true;
                wallDirection = detectedWall;
                doubleJump = true;
                return;
            }
        }

        isWallSliding = false;
    }

    private int GetTouchingWallDirection()
    {
        Bounds bounds = playerCollider != null ? playerCollider.bounds : new Bounds(transform.position, new Vector3(0.8f, 1.2f, 0f));
        Vector2 boxSize = new Vector2(0.05f, bounds.size.y * 0.7f);

        Vector2 rightOrigin = new Vector2(bounds.max.x, bounds.center.y);
        if (Physics2D.OverlapBox(rightOrigin + Vector2.right * (wallCheckDistance * 0.5f), boxSize, 0f, groundLayer))
        {
            return 1;
        }

        Vector2 leftOrigin = new Vector2(bounds.min.x, bounds.center.y);
        if (Physics2D.OverlapBox(leftOrigin + Vector2.left * (wallCheckDistance * 0.5f), boxSize, 0f, groundLayer))
        {
            return -1;
        }

        return 0;
    }

    private void HandleFootsteps(bool grounded)
    {
        if (grounded && !isDashing && !IsGrappling && Mathf.Abs(inputValue.x) > 0.1f && Mathf.Abs(rb.linearVelocityX) > 0.2f)
        {
            walkStepTimer -= Time.deltaTime;
            if (walkStepTimer <= 0f)
            {
                walkStepTimer = walkStepInterval;
                AudioClip stepClip = GetRandomWalkSFX();
                if (stepClip != null)
                {
                    PlayPitchedSFX(stepClip, Random.Range(0.92f, 1.08f), 0.75f);
                }
            }
        }
        else
        {
            walkStepTimer = 0.05f;
        }
    }

    private AudioClip GetRandomWalkSFX()
    {
        if (walkSFXList == null || walkSFXList.Count == 0) return null;
        if (walkSFXList.Count == 1) return walkSFXList[0];

        int index = Random.Range(0, walkSFXList.Count);
        if (index == lastWalkSFXIndex)
        {
            index = (index + 1) % walkSFXList.Count;
        }
        lastWalkSFXIndex = index;
        return walkSFXList[index];
    }

    /// <summary>
    /// Smoothly steps up low obstacles, curbs, and stairs without requiring the player to jump.
    /// Also handles downward stair snapping when descending stairs so running remains continuous.
    /// </summary>
    private void HandleStepClimb(float moveInputX)
    {
        if (playerCollider == null || rb == null) return;
        if (Mathf.Abs(moveInputX) < 0.05f) return;
        if (rb.linearVelocityY > 0.25f) return; // Don't step-climb while actively jumping upward

        Bounds bounds = playerCollider.bounds;
        float halfWidth = bounds.extents.x;
        float currentFootY = bounds.min.y;
        float dirX = Mathf.Sign(moveInputX);

        // 1. Check for a low vertical step / obstacle at foot level
        float checkDist = halfWidth + stepCheckDistance;
        Vector2 footOrigin = new Vector2(bounds.center.x, currentFootY + 0.05f);
        RaycastHit2D footHit = Physics2D.Raycast(footOrigin, Vector2.right * dirX, checkDist, groundLayer);

        if (footHit.collider != null && !footHit.collider.isTrigger && !Physics2D.GetIgnoreCollision(playerCollider, footHit.collider))
        {
            // Only climb if this is a vertical riser / step (not already a flat surface)
            if (footHit.normal.y < 0.7f)
            {
                float distFromFront = Mathf.Abs(footHit.point.x - (bounds.center.x + dirX * halfWidth));
                if (distFromFront <= 0.14f)
                {
                    // 2. Check if the space above maxStepHeight is clear (ensures it's a step/stair, not a full wall)
                    Vector2 waistOrigin = new Vector2(bounds.center.x, currentFootY + maxStepHeight + 0.06f);
                    RaycastHit2D waistHit = Physics2D.Raycast(waistOrigin, Vector2.right * dirX, checkDist, groundLayer);

                    if (waistHit.collider == null || waistHit.collider.isTrigger || Physics2D.GetIgnoreCollision(playerCollider, waistHit.collider))
                    {
                        // 3. Probe downward onto the step to find its exact walkable surface height
                        float probeX = footHit.point.x + (dirX * 0.15f);
                        Vector2 probeOrigin = new Vector2(probeX, currentFootY + maxStepHeight + 0.08f);
                        RaycastHit2D surfaceHit = Physics2D.Raycast(probeOrigin, Vector2.down, maxStepHeight + 0.12f, groundLayer);

                        if (surfaceHit.collider != null && !surfaceHit.collider.isTrigger && !Physics2D.GetIgnoreCollision(playerCollider, surfaceHit.collider))
                        {
                            if (surfaceHit.normal.y >= 0.55f) // Walkable floor on top of step
                            {
                                float stepHeight = surfaceHit.point.y - currentFootY;
                                if (stepHeight > 0.01f && stepHeight <= maxStepHeight)
                                {
                                    // 4. Verify clearance for the player's full body above the step
                                    Vector2 targetCenter = (Vector2)bounds.center + new Vector2(dirX * 0.04f, stepHeight + 0.02f);
                                    Vector2 bodySize = new Vector2(bounds.size.x * 0.85f, bounds.size.y * 0.85f);
                                    Collider2D overlap = Physics2D.OverlapBox(targetCenter, bodySize, 0f, groundLayer);

                                    if (overlap == null || overlap == playerCollider || overlap.isTrigger || Physics2D.GetIgnoreCollision(playerCollider, overlap))
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

        // 6. Step Down Snapping: If grounded and walking down stairs, snap to lower step so movement remains smooth
        if (inputValue.y >= -0.4f && rb.linearVelocityY <= 0.05f)
        {
            Vector2 downProbeOrigin = new Vector2(bounds.center.x + (dirX * 0.12f), currentFootY + 0.08f);
            RaycastHit2D downHit = Physics2D.Raycast(downProbeOrigin, Vector2.down, maxStepHeight + 0.15f, groundLayer);

            if (downHit.collider != null && !downHit.collider.isTrigger && !Physics2D.GetIgnoreCollision(playerCollider, downHit.collider))
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

    private void HandleJumpInput()
    {
        if (jumpAction == null) return;

        if (jumpAction.WasPressedThisFrame())
        {
            jumpBufferCounter = jumpBufferTime;
        }
        else
        {
            jumpBufferCounter -= Time.deltaTime;
        }

        if (jumpBufferCounter > 0f)
        {
            if (IsGrappling)
            {
                Vector2 currentVel = rb.linearVelocity;
                Vector2 ropeDir = (grappleAnchorPoint - (Vector2)transform.position).normalized;
                Vector2 baseDir = currentVel.sqrMagnitude > 0.25f ? currentVel.normalized : ropeDir;

                // Allow player to direct the grapple jump boost using movement keys (WASD / Stick)
                Vector2 boostDir;
                if (inputValue.sqrMagnitude > 0.05f)
                {
                    boostDir = (inputValue.normalized * 0.7f + baseDir * 0.3f + Vector2.up * 0.45f).normalized;
                }
                else
                {
                    boostDir = (baseDir + Vector2.up * 0.55f).normalized;
                }

                float launchSpeed = Mathf.Max(currentVel.magnitude * releaseBoost, jumpHeight * releaseBoost);
                DetachGrappling();
                releaseGrappleAnimTimer = releaseGrappleAnimDuration;
                rb.linearVelocity = boostDir * launchSpeed;
                doubleJump = true;
                isDoubleJumping = false;
                jumpBufferCounter = 0f;
                coyoteTimeCounter = 0f;
                PlayPitchedSFX(jumpSFX, 1.08f, 1f);
                SetPlayerAnimState(PlayerAnimState.ReleaseGrapplingHook, true);
                return;
            }

            if (coyoteTimeCounter > 0f)
            {
                // If holding Down on a one-way platform, drop through instead of jumping upward
                if (inputValue.y < -0.4f && TryDropThroughPlatform())
                {
                    coyoteTimeCounter = 0f;
                    jumpBufferCounter = 0f;
                    return;
                }

                rb.linearVelocity = new Vector2(rb.linearVelocityX, jumpHeight);
                isDoubleJumping = false;
                coyoteTimeCounter = 0f;
                jumpBufferCounter = 0f;
                PlayPitchedSFX(jumpSFX, Random.Range(0.96f, 1.04f), 1f);
                SetPlayerAnimState(PlayerAnimState.Jump, true);
                SpawnTakeoffDust();
                return;
            }

            if (enableWallMechanics && isWallSliding)
            {
                int kickDir = -wallDirection;
                rb.linearVelocity = new Vector2(kickDir * wallJumpForce.x, wallJumpForce.y);
                wallJumpLockCounter = wallJumpInputLockTime;
                isWallSliding = false;
                doubleJump = true;
                isDoubleJumping = false;
                jumpBufferCounter = 0f;
                PlayPitchedSFX(jumpSFX, 1.06f, 1f);

                if (spriteRenderer != null)
                {
                    spriteRenderer.flipX = kickDir < 0;
                }
                SetPlayerAnimState(PlayerAnimState.Jump, true);
                SpawnWallJumpDust(kickDir);
                return;
            }

            if (jumpAction.WasPressedThisFrame() && doubleJump)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocityX, jumpHeight);
                doubleJump = false;
                isDoubleJumping = true;
                jumpBufferCounter = 0f;
                AudioClip clipToPlay = doubleJumpSFX != null ? doubleJumpSFX : jumpSFX;
                float pitchToPlay = doubleJumpSFX != null ? Random.Range(0.96f, 1.05f) : 1.15f;
                PlayPitchedSFX(clipToPlay, pitchToPlay, 1f);
                SetPlayerAnimState(PlayerAnimState.DoubleJump, true);
                SpawnDoubleJumpAirRing();
                return;
            }
        }

        if (jumpAction.WasReleasedThisFrame() && rb.linearVelocityY > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocityX, rb.linearVelocityY * 0.5f);
        }
    }

    public bool TryDropThroughPlatform()
    {
        Vector2 checkPos = groundCheck != null ? (Vector2)groundCheck.position : (Vector2)transform.position;
        Collider2D[] hits = Physics2D.OverlapCircleAll(checkPos, 0.35f, groundLayer);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
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

    private bool IsGrounded()
    {
        if (groundCheck == null) return false;
        return Physics2D.OverlapCircle(groundCheck.position, 0.2f, groundLayer);
    }

    private IEnumerator DashRoutine()
    {
        canDash = false;
        isDashing = true;
        isWallSliding = false;
        ResetIdleSquashBob();

        if (IsGrappling)
        {
            DetachGrappling();
        }

        PlaySFX(dashSFX);
        SetPlayerAnimState(PlayerAnimState.Dash, true);

        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0f;

        int dashDirection = Mathf.Abs(inputValue.x) > 0.1f
            ? (inputValue.x < 0f ? -1 : 1)
            : (spriteRenderer != null && spriteRenderer.flipX ? -1 : 1);

        // Preserve momentum if dashing out of a faster grapple swing
        float currentHorizontalSpeed = Mathf.Sign(rb.linearVelocityX) == dashDirection ? Mathf.Abs(rb.linearVelocityX) : 0f;
        float finalDashSpeed = Mathf.Max(dashingPower, currentHorizontalSpeed);

        rb.linearVelocity = new Vector2(dashDirection * finalDashSpeed, 0f);

        if (trailRenderer != null) trailRenderer.emitting = true;

        yield return new WaitForSeconds(dashingTime);

        if (trailRenderer != null) trailRenderer.emitting = false;
        rb.gravityScale = originalGravity;
        isDashing = false;

        yield return new WaitForSeconds(dashingCooldown);
        canDash = true;
    }

    private void Flip()
    {
        if (wallJumpLockCounter > 0f || spriteRenderer == null) return;

        if (isWallSliding)
        {
            spriteRenderer.flipX = wallDirection > 0;
            return;
        }

        if (inputValue.x < 0f) spriteRenderer.flipX = true;
        else if (inputValue.x > 0f) spriteRenderer.flipX = false;
    }

    private void HandleIdleSquashBob(bool grounded)
    {
        if (spriteRenderer == null) return;

        // If multi-frame idle sprites are assigned, let the sprite frames handle idle breathing without scale squash
        if (idleSprites != null && idleSprites.Count > 1)
        {
            ResetIdleSquashBob();
            return;
        }

        bool isStandingIdle = grounded
            && Mathf.Abs(inputValue.x) <= 0.05f
            && Mathf.Abs(rb.linearVelocityX) <= 0.1f
            && !isDashing
            && !IsGrappling
            && !IsInMode;

        if (!isStandingIdle)
        {
            ResetIdleSquashBob();
            return;
        }

        idleBobTimer += Time.deltaTime;
        if (idleBobTimer >= idleBobInterval)
        {
            idleBobTimer -= idleBobInterval;
            isIdleSquashed = !isIdleSquashed;

            if (isIdleSquashed)
            {
                float newScaleY = originalSpriteScale.y * idleSquashScaleY;
                float deltaScaleY = newScaleY - originalSpriteScale.y;
                float offsetY = spriteRenderer.sprite != null ? (-spriteRenderer.sprite.bounds.min.y * deltaScaleY) : 0f;

                spriteRenderer.transform.localScale = new Vector3(originalSpriteScale.x, newScaleY, originalSpriteScale.z);
                spriteRenderer.transform.localPosition = new Vector3(
                    originalSpriteLocalPos.x,
                    originalSpriteLocalPos.y + offsetY,
                    originalSpriteLocalPos.z
                );
            }
            else
            {
                spriteRenderer.transform.localScale = originalSpriteScale;
                spriteRenderer.transform.localPosition = originalSpriteLocalPos;
            }
        }
    }

    private void ResetIdleSquashBob()
    {
        idleBobTimer = 0f;
        if (isIdleSquashed && spriteRenderer != null)
        {
            isIdleSquashed = false;
            spriteRenderer.transform.localScale = originalSpriteScale;
            spriteRenderer.transform.localPosition = originalSpriteLocalPos;
        }
    }

    #endregion

    #region Chain Slash Mode & Targeting

    private void HandleCombatInput()
    {
        if (attackAction == null) return;

        if (attackAction.IsPressed() && currentMeter <= 0)
        {
            ExitChainSlashMode();
            return;
        }

        if (attackAction.IsPressed())
        {
            if (isFocusing) return;
            if (!IsInMode) EnterChainSlashMode();
        }
        else
        {
            if (IsInMode) ExitChainSlashMode();
        }
    }

    private void EnterChainSlashMode()
    {
        IsInMode = true;
        SetPlayerAnimState(PlayerAnimState.InModeSlashingStance, true);
        FadeModeDimOverlay(modeDimAlpha);

        Time.timeScale = timeSpeed;
        Time.fixedDeltaTime = defaultFixedDeltaTime * Time.timeScale;

        drawnLinePoints.Clear();
        currentPreviewedGesture = HealGestureType.None;
        Vector3 startMousePos = GetMouseWorldPosition();
        drawnLinePoints.Add(startMousePos);

        if (lineRendererObject != null)
        {
            insLineRendererObj = Instantiate(lineRendererObject);
            insLineRendererCom = insLineRendererObj.GetComponent<LineRenderer>();
            insLineRendererManager = insLineRendererObj.GetComponent<LineRendererManager>();
            if (insLineRendererCom != null)
            {
                insLineRendererCom.enabled = true;
                insLineRendererCom.positionCount = 1;
                insLineRendererCom.SetPosition(0, startMousePos);
                if (drawGradient.colorKeys.Length > 0) insLineRendererCom.colorGradient = drawGradient;
            }
            if (insLineRendererManager != null)
            {
                insLineRendererManager.RenderPixelSlashLine();
            }
        }

        if (mouseTrailPrefab != null && GetActiveCamera() != null && Mouse.current != null)
        {
            insMouseTrail = Instantiate(mouseTrailPrefab, startMousePos, Quaternion.identity);
            if (insMouseTrail.TryGetComponent<TrailRenderer>(out TrailRenderer mouseTrailRenderer))
            {
                mouseTrailRenderer.emitting = false;
                mouseTrailRenderer.forceRenderingOff = true;
            }
        }

        ShowMeterBar();

        if (activeMeterCoroutine != null) StopCoroutine(activeMeterCoroutine);
        activeMeterCoroutine = StartCoroutine(MeterDegeneration(1f, 0.005f));
    }

    private void ExitChainSlashMode()
    {
        IsInMode = false;
        FadeModeDimOverlay(0f);
        ClearTargetReticles();

        if (targetGroup != null && targetGroup.Targets.Count > 1)
        {
            targetGroup.Targets[1].Object = null;
        }

        if (activeMeterCoroutine != null) StopCoroutine(activeMeterCoroutine);

        activeMeterCoroutine = StartCoroutine(MeterRegenerate());

        if (insLineRendererObj != null)
        {
            if (insLineRendererManager == null)
            {
                insLineRendererManager = insLineRendererObj.GetComponent<LineRendererManager>();
            }
            if (insLineRendererManager != null && insLineRendererCom != null)
            {
                bool hasCombatTargets = insLineRendererManager.targets != null && insLineRendererManager.targets.Count > 0;
                if (!hasCombatTargets && enableDrawToHeal)
                {
                    HealGestureType gesture = DetectHealGesture(drawnLinePoints, out Bounds gestureBounds);
                    if (gesture != HealGestureType.None)
                    {
                        TriggerDrawHeal(gesture);
                        insLineRendererManager.activeCoroutine = StartCoroutine(AnimateHealGestureLine(insLineRendererCom, gesture, gestureBounds));
                    }
                    else
                    {
                        Destroy(insLineRendererCom.gameObject);
                    }
                }
                else
                {
                    insLineRendererManager.activeCoroutine = StartCoroutine(AnimateKillLine(insLineRendererCom));
                }
            }
        }

        if (insMouseTrail != null)
        {
            if (insMouseTrail.TryGetComponent<TrailRenderer>(out TrailRenderer trail))
            {
                trail.emitting = false;
                Destroy(insMouseTrail, trail.time);
            }
            else
            {
                Destroy(insMouseTrail);
            }
            insMouseTrail = null;
        }

        attackAction.Disable();
        attackAction.Enable();

        insLineRendererCom = null;
        insLineRendererObj = null;
        insLineRendererManager = null;

        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = defaultFixedDeltaTime;
    }

    private void SetupModeDimOverlay()
    {
        if (modeDimRenderer != null) return;

        if (cachedSolidWhiteSprite == null)
        {
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[] { Color.white, Color.white, Color.white, Color.white };
            tex.SetPixels(pixels);
            tex.Apply();
            cachedSolidWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);
        }

        GameObject dimObj = new GameObject("ChainSlashDimOverlay");
        Camera activeCam = GetActiveCamera();
        if (activeCam != null)
        {
            dimObj.transform.SetParent(activeCam.transform, false);
            dimObj.transform.localPosition = new Vector3(0f, 0f, 10f);
        }
        else
        {
            dimObj.transform.SetParent(transform, false);
            dimObj.transform.localPosition = Vector3.zero;
        }
        dimObj.transform.localScale = new Vector3(250f, 250f, 1f);

        modeDimRenderer = dimObj.AddComponent<SpriteRenderer>();
        modeDimRenderer.sprite = cachedSolidWhiteSprite;
        modeDimRenderer.color = new Color(0f, 0f, 0f, 0f);
        // SortingOrder 1 sits above all backgrounds/tilemaps (<= 0) and below enemy outlines (2), enemies (3), and player (5)
        modeDimRenderer.sortingLayerID = spriteRenderer != null ? spriteRenderer.sortingLayerID : 0;
        modeDimRenderer.sortingOrder = 1;
    }

    private void FadeModeDimOverlay(float targetAlpha)
    {
        if (modeDimRenderer == null) return;
        if (activeModeDimCoroutine != null) StopCoroutine(activeModeDimCoroutine);
        activeModeDimCoroutine = StartCoroutine(ModeDimFadeRoutine(targetAlpha));
    }

    private IEnumerator ModeDimFadeRoutine(float targetAlpha)
    {
        float startAlpha = modeDimRenderer.color.a;
        float duration = Mathf.Max(0.01f, modeDimFadeDuration);
        float elapsed = 0f;

        while (elapsed < duration && modeDimRenderer != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            modeDimRenderer.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }

        if (modeDimRenderer != null)
        {
            modeDimRenderer.color = new Color(0f, 0f, 0f, targetAlpha);
        }

        activeModeDimCoroutine = null;
    }

    private void RecordDrawnLinePoints()
    {
        if (!IsInMode || insLineRendererCom == null) return;

        Vector3 currentMousePos = GetMouseWorldPosition();
        float effectiveMinDist = Mathf.Min(minPointDistance, 1f / 16f);
        float minDistSqr = effectiveMinDist * effectiveMinDist;

        if (drawnLinePoints.Count == 0 || (drawnLinePoints[drawnLinePoints.Count - 1] - currentMousePos).sqrMagnitude >= minDistSqr)
        {
            drawnLinePoints.Add(currentMousePos);
            int newCount = drawnLinePoints.Count;
            insLineRendererCom.positionCount = newCount;
            insLineRendererCom.SetPosition(newCount - 1, currentMousePos);

            if (enableDrawToHeal && newCount >= 8)
            {
                bool hasCombatTargets = insLineRendererManager != null
                    && insLineRendererManager.targets != null
                    && insLineRendererManager.targets.Count > 0;

                if (!hasCombatTargets)
                {
                    HealGestureType detected = DetectHealGesture(drawnLinePoints, out _);
                    if (detected != currentPreviewedGesture)
                    {
                        currentPreviewedGesture = detected;
                        ApplyLineGesturePreviewGradient(detected);
                    }
                }
            }
        }
        else if (insLineRendererCom.positionCount > 0)
        {
            // Keep the live tip locked to the current cursor position with zero lag
            insLineRendererCom.SetPosition(insLineRendererCom.positionCount - 1, currentMousePos);
        }

        if (insLineRendererManager != null)
        {
            insLineRendererManager.RenderPixelSlashLine();
        }
    }

    private void ApplyLineGesturePreviewGradient(HealGestureType gestureType)
    {
        if (insLineRendererCom == null) return;

        if (gestureType == HealGestureType.None)
        {
            if (drawGradient != null && drawGradient.colorKeys.Length > 0)
            {
                insLineRendererCom.colorGradient = drawGradient;
            }
            return;
        }

        Color primary = gestureType == HealGestureType.Heart ? heartGestureGlowColor : plusGestureGlowColor;
        Color highlight = Color.Lerp(primary, Color.white, 0.55f);

        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(primary, 0f),
                new GradientColorKey(highlight, 0.5f),
                new GradientColorKey(primary, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(1.0f, 0.5f),
                new GradientAlphaKey(0.95f, 1f)
            }
        );
        insLineRendererCom.colorGradient = g;

        if (showFloatingHealSparks)
        {
            SpawnHealPixelParticle();
        }
    }

    private void UpdateMouseTrail()
    {
        if (IsInMode && insMouseTrail != null)
        {
            insMouseTrail.transform.position = GetMouseWorldPosition();
        }
    }

    private Vector3 GetMouseWorldPosition()
    {
        Camera activeCam = GetActiveCamera();
        if (activeCam == null || Mouse.current == null) return Vector3.zero;

        Vector3 mouseScreenPos = Mouse.current.position.ReadValue();
        mouseScreenPos.z = -activeCam.transform.position.z;
        Vector3 worldPos = activeCam.ScreenToWorldPoint(mouseScreenPos);
        worldPos.z = 0f;
        return worldPos;
    }

    public void TargetSetup(Transform enemyPos)
    {
        if (insLineRendererObj == null) return;

        if (targetGroup != null && targetGroup.Targets.Count > 1)
        {
            targetGroup.Targets[1].Object = enemyPos;
        }

        if (insLineRendererManager == null)
        {
            insLineRendererManager = insLineRendererObj.GetComponent<LineRendererManager>();
        }
        if (insLineRendererManager == null) return;

        if (!insLineRendererManager.targets.Contains(enemyPos.gameObject))
        {
            insLineRendererManager.targets.Add(enemyPos.gameObject);
            if (currentPreviewedGesture != HealGestureType.None)
            {
                currentPreviewedGesture = HealGestureType.None;
                ApplyLineGesturePreviewGradient(HealGestureType.None);
            }

            currentMeter = Mathf.Clamp(currentMeter - (maxMeter / 40f), 0f, maxMeter);
            if (modeMeterImage != null) modeMeterImage.fillAmount = currentMeter / maxMeter;

            int comboIndex = insLineRendererManager.targets.Count - 1;
            PlayJuicyTargetSelectSFX(comboIndex);
            StartCoroutine(PunchTargetScaleRoutine(enemyPos));
            SpawnTargetReticleAndComboPopup(enemyPos, comboIndex + 1);
        }
    }

    private IEnumerator PunchTargetScaleRoutine(Transform targetTransform)
    {
        if (targetTransform == null) yield break;

        Vector3 baseScale = targetTransform.localScale;
        const float duration = 0.08f;
        float elapsed = 0f;

        while (elapsed < duration && targetTransform != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            float scaleFactor = Mathf.Lerp(1f, 1.25f, Mathf.Sin(t * Mathf.PI));
            targetTransform.localScale = baseScale * scaleFactor;
            yield return null;
        }

        if (targetTransform != null)
        {
            targetTransform.localScale = baseScale;
        }
    }

    /// <summary>
    /// Recognizes whether a freehand stroke drawn in Chain Slash mode forms a Plus (+) or a Heart (♡).
    /// </summary>
    private HealGestureType DetectHealGesture(List<Vector3> points, out Bounds bounds)
    {
        bounds = default;
        if (points == null || points.Count < 8) return HealGestureType.None;

        float minX = points[0].x, maxX = points[0].x;
        float minY = points[0].y, maxY = points[0].y;
        float totalLength = 0f;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;

            if (i > 0)
            {
                totalLength += Vector2.Distance(points[i - 1], p);
            }
        }

        float width = maxX - minX;
        float height = maxY - minY;
        float minReq = Mathf.Max(0.35f, minHealGestureWorldSize);

        if (width < minReq || height < minReq) return HealGestureType.None;

        float aspect = width / height;
        if (aspect < 0.35f || aspect > 2.85f) return HealGestureType.None;

        // Reject simple straight or diagonal single strokes
        if (totalLength < (width + height) * 0.85f) return HealGestureType.None;

        Vector3 center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
        bounds = new Bounds(center, new Vector3(width, height, 0.1f));

        // Resample into 32 equidistant normalized points in [0, 1] x [0, 1]
        const int sampleCount = 32;
        Vector2[] normPts = ResampleNormalizedPoints(points, sampleCount, minX, minY, width, height, totalLength);

        float plusScore = EvaluatePlusGestureScore(normPts);
        float heartScore = EvaluateHeartGestureScore(normPts, totalLength, width, height);

        if (plusScore < 1.0f && heartScore < 1.0f)
        {
            return HealGestureType.None;
        }

        return heartScore >= plusScore ? HealGestureType.Heart : HealGestureType.Plus;
    }

    private static Vector2[] ResampleNormalizedPoints(
        List<Vector3> rawPoints,
        int targetCount,
        float minX,
        float minY,
        float width,
        float height,
        float totalLength)
    {
        Vector2[] result = new Vector2[targetCount];
        float invW = width > 0.0001f ? 1f / width : 1f;
        float invH = height > 0.0001f ? 1f / height : 1f;

        if (totalLength <= 0.0001f || rawPoints.Count < 2)
        {
            for (int i = 0; i < targetCount; i++)
            {
                result[i] = new Vector2(0.5f, 0.5f);
            }
            return result;
        }

        float step = totalLength / (targetCount - 1);
        float accumulated = 0f;
        int srcIdx = 0;

        result[0] = new Vector2((rawPoints[0].x - minX) * invW, (rawPoints[0].y - minY) * invH);

        for (int s = 1; s < targetCount - 1; s++)
        {
            float targetDist = s * step;
            while (srcIdx < rawPoints.Count - 2)
            {
                float segLen = Vector2.Distance(rawPoints[srcIdx], rawPoints[srcIdx + 1]);
                if (accumulated + segLen >= targetDist) break;
                accumulated += segLen;
                srcIdx++;
            }

            Vector2 a = rawPoints[srcIdx];
            Vector2 b = rawPoints[srcIdx + 1];
            float seg = Vector2.Distance(a, b);
            float t = seg > 0.0001f ? Mathf.Clamp01((targetDist - accumulated) / seg) : 0f;
            Vector2 pt = Vector2.Lerp(a, b, t);
            result[s] = new Vector2((pt.x - minX) * invW, (pt.y - minY) * invH);
        }

        Vector3 last = rawPoints[rawPoints.Count - 1];
        result[targetCount - 1] = new Vector2((last.x - minX) * invW, (last.y - minY) * invH);
        return result;
    }

    /// <summary>
    /// Evaluates whether normalized points [0..1] form a Plus (+) symbol:
    /// Supports both a 2-stroke intersecting cross, a center-retraced cross, or a 12-corner outline plus.
    /// </summary>
    private static float EvaluatePlusGestureScore(Vector2[] normPts)
    {
        int n = normPts.Length;
        bool reachedTop = false, reachedBottom = false, reachedLeft = false, reachedRight = false;
        bool passedNearCenter = false;
        int inCrossBandCount = 0;
        int inOuterCornerCount = 0;

        float horizTravelInMidY = 0f;
        float vertTravelInMidX = 0f;

        for (int i = 0; i < n; i++)
        {
            Vector2 p = normPts[i];
            if (p.y >= 0.78f && Mathf.Abs(p.x - 0.5f) <= 0.35f) reachedTop = true;
            if (p.y <= 0.22f && Mathf.Abs(p.x - 0.5f) <= 0.35f) reachedBottom = true;
            if (p.x <= 0.22f && Mathf.Abs(p.y - 0.5f) <= 0.35f) reachedLeft = true;
            if (p.x >= 0.78f && Mathf.Abs(p.y - 0.5f) <= 0.35f) reachedRight = true;

            float dxCenter = Mathf.Abs(p.x - 0.5f);
            float dyCenter = Mathf.Abs(p.y - 0.5f);

            if (dxCenter <= 0.26f && dyCenter <= 0.26f)
            {
                passedNearCenter = true;
            }

            if (dxCenter <= 0.28f || dyCenter <= 0.28f)
            {
                inCrossBandCount++;
            }

            if (dxCenter >= 0.32f && dyCenter >= 0.32f)
            {
                inOuterCornerCount++;
            }

            if (i > 0)
            {
                Vector2 prev = normPts[i - 1];
                Vector2 delta = p - prev;
                float adx = Mathf.Abs(delta.x);
                float ady = Mathf.Abs(delta.y);

                if (adx > ady * 1.25f && Mathf.Abs(((p.y + prev.y) * 0.5f) - 0.5f) <= 0.32f)
                {
                    horizTravelInMidY += adx;
                }
                else if (ady > adx * 1.25f && Mathf.Abs(((p.x + prev.x) * 0.5f) - 0.5f) <= 0.32f)
                {
                    vertTravelInMidX += ady;
                }
            }
        }

        if (!reachedTop || !reachedBottom || !reachedLeft || !reachedRight || !passedNearCenter)
        {
            return 0f;
        }

        // Check for perpendicular segment intersection near the center
        bool hasPerpendicularCenterIntersection = false;
        for (int i = 0; i < n - 3; i++)
        {
            Vector2 a1 = normPts[i];
            Vector2 a2 = normPts[i + 1];
            Vector2 dirA = a2 - a1;
            if (dirA.sqrMagnitude < 0.0001f) continue;

            for (int j = i + 3; j < n - 1; j++)
            {
                Vector2 b1 = normPts[j];
                Vector2 b2 = normPts[j + 1];
                Vector2 dirB = b2 - b1;
                if (dirB.sqrMagnitude < 0.0001f) continue;

                if (TryIntersectSegments(a1, a2, b1, b2, out Vector2 hit))
                {
                    float dot = Mathf.Abs(Vector2.Dot(dirA.normalized, dirB.normalized));
                    if (dot <= 0.72f && Mathf.Abs(hit.x - 0.5f) <= 0.34f && Mathf.Abs(hit.y - 0.5f) <= 0.34f)
                    {
                        hasPerpendicularCenterIntersection = true;
                        break;
                    }
                }
            }
            if (hasPerpendicularCenterIntersection) break;
        }

        float crossBandRatio = (float)inCrossBandCount / n;
        float cornerRatio = (float)inOuterCornerCount / n;

        // Case 1: Classic 2-stroke connected '+' with central perpendicular crossing and strong H + V arms
        if (hasPerpendicularCenterIntersection && horizTravelInMidY >= 0.48f && vertTravelInMidX >= 0.48f && cornerRatio <= 0.28f)
        {
            return 1.25f + (horizTravelInMidY + vertTravelInMidX) * 0.25f;
        }

        // Case 2: Retraced '+' or 12-corner outline '+' where points stay tightly within the cross arms and avoid the 4 corners
        if (crossBandRatio >= 0.68f && cornerRatio <= 0.16f && horizTravelInMidY >= 0.42f && vertTravelInMidX >= 0.42f)
        {
            return 1.15f + crossBandRatio * 0.3f;
        }

        return 0f;
    }

    /// <summary>
    /// Evaluates whether normalized points [0..1] form a Heart (♡) symbol:
    /// Checks closed/nearly-closed loop, bottom centered V-tip, wider upper lobes, and top cleft / heart template similarity.
    /// </summary>
    private static float EvaluateHeartGestureScore(Vector2[] normPts, float totalLength, float width, float height)
    {
        int n = normPts.Length;
        if (totalLength < (width + height) * 1.05f) return 0f;

        // 1. Check loop closure (either start/end are close, or the first 25% and last 25% of the stroke come close)
        float minClosureDist = Vector2.Distance(normPts[0], normPts[n - 1]);
        for (int i = 0; i < n / 4; i++)
        {
            for (int j = (n * 3) / 4; j < n; j++)
            {
                float d = Vector2.Distance(normPts[i], normPts[j]);
                if (d < minClosureDist) minClosureDist = d;
            }
        }
        if (minClosureDist > 0.46f) return 0f;

        // 2. Find bottom-most point (V-tip) and upper left/right lobe peaks
        float minY = 1f;
        float bottomTipX = 0.5f;
        float leftLobeMaxY = 0f;
        float rightLobeMaxY = 0f;
        int upperLeftCount = 0;
        int upperRightCount = 0;

        float upperMinX = 1f, upperMaxX = 0f;
        float lowerMinX = 1f, lowerMaxX = 0f;

        for (int i = 0; i < n; i++)
        {
            Vector2 p = normPts[i];
            if (p.y < minY)
            {
                minY = p.y;
                bottomTipX = p.x;
            }

            if (p.x < 0.46f && p.y > 0.52f)
            {
                upperLeftCount++;
                if (p.y > leftLobeMaxY) leftLobeMaxY = p.y;
            }
            if (p.x > 0.54f && p.y > 0.52f)
            {
                upperRightCount++;
                if (p.y > rightLobeMaxY) rightLobeMaxY = p.y;
            }

            if (p.y >= 0.48f)
            {
                if (p.x < upperMinX) upperMinX = p.x;
                if (p.x > upperMaxX) upperMaxX = p.x;
            }
            if (p.y <= 0.25f)
            {
                if (p.x < lowerMinX) lowerMinX = p.x;
                if (p.x > lowerMaxX) lowerMaxX = p.x;
            }
        }

        // Bottom V-tip must be reasonably centered horizontally
        if (bottomTipX < 0.16f || bottomTipX > 0.84f) return 0f;

        // Must have points in both upper-left and upper-right lobes
        if (upperLeftCount < 2 || upperRightCount < 2) return 0f;
        if (leftLobeMaxY < 0.68f || rightLobeMaxY < 0.68f) return 0f;

        // Upper half of a Heart is wider than the bottom V-tip quarter
        float upperSpan = Mathf.Max(0f, upperMaxX - upperMinX);
        float lowerSpan = lowerMaxX >= lowerMinX ? (lowerMaxX - lowerMinX) : 0f;
        if (upperSpan < 0.65f || lowerSpan > upperSpan * 0.82f) return 0f;

        // 3. Check for top-center cleft dip OR start/end anchored at top-cleft / bottom-tip
        float minLobePeak = Mathf.Min(leftLobeMaxY, rightLobeMaxY);
        bool hasTopCleftDip = false;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = normPts[i];
            if (p.x >= 0.30f && p.x <= 0.70f && p.y >= 0.38f && p.y <= minLobePeak - 0.04f)
            {
                hasTopCleftDip = true;
                break;
            }
        }

        Vector2 startPt = normPts[0];
        Vector2 endPt = normPts[n - 1];
        bool anchoredAtTopCleft = (Mathf.Abs(startPt.x - 0.5f) <= 0.26f && startPt.y >= 0.48f)
                               || (Mathf.Abs(endPt.x - 0.5f) <= 0.26f && endPt.y >= 0.48f);
        bool anchoredAtBottomTip = (Mathf.Abs(startPt.x - 0.5f) <= 0.26f && startPt.y <= 0.30f)
                                || (Mathf.Abs(endPt.x - 0.5f) <= 0.26f && endPt.y <= 0.30f);

        float templateDist = ComputeBestHeartTemplateDistance(normPts);

        if ((hasTopCleftDip && (anchoredAtTopCleft || anchoredAtBottomTip || templateDist <= 0.26f))
            || templateDist <= 0.21f)
        {
            return 1.30f + Mathf.Clamp01(0.30f - templateDist);
        }

        return 0f;
    }

    private static float ComputeBestHeartTemplateDistance(Vector2[] normPts)
    {
        int n = normPts.Length;
        // Compare against parametric Heart curve starting from Top Cleft (CW & CCW) and Bottom Tip (CW & CCW)
        float bestDist = float.MaxValue;
        float[] phaseOffsets = { 0f, Mathf.PI }; // 0 = Top Cleft (t=0), PI = Bottom Tip (t=PI)
        int[] directions = { 1, -1 };

        for (int pIdx = 0; pIdx < phaseOffsets.Length; pIdx++)
        {
            for (int dIdx = 0; dIdx < directions.Length; dIdx++)
            {
                float phase = phaseOffsets[pIdx];
                int dir = directions[dIdx];
                float sumDist = 0f;

                for (int i = 0; i < n; i++)
                {
                    float u = (float)i / (n - 1);
                    float t = phase + dir * u * Mathf.PI * 2f;
                    Vector2 heartPt = EvaluateNormalizedHeartPoint(t);
                    sumDist += Vector2.Distance(normPts[i], heartPt);
                }

                float avgDist = sumDist / n;
                if (avgDist < bestDist) bestDist = avgDist;
            }
        }

        return bestDist;
    }

    private static Vector2 EvaluateNormalizedHeartPoint(float t)
    {
        // Classic parametric heart: x = 16 sin^3(t), y = 13 cos(t) - 5 cos(2t) - 2 cos(3t) - cos(4t)
        // Normalized to [0, 1] x [0, 1]
        float sinT = Mathf.Sin(t);
        float rawX = 16f * sinT * sinT * sinT; // [-16, 16]
        float rawY = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t); // approx [-17, 12]

        float nx = (rawX + 16f) / 32f;
        float ny = Mathf.Clamp01((rawY + 17f) / 29f);
        return new Vector2(nx, ny);
    }

    private static bool TryIntersectSegments(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 intersection)
    {
        intersection = Vector2.zero;
        Vector2 r = p2 - p1;
        Vector2 s = p4 - p3;
        float rxs = r.x * s.y - r.y * s.x;
        if (Mathf.Abs(rxs) < 0.00001f) return false;

        Vector2 qp = p3 - p1;
        float t = (qp.x * s.y - qp.y * s.x) / rxs;
        float u = (qp.x * r.y - qp.y * r.x) / rxs;

        if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
        {
            intersection = p1 + t * r;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Smoothly morphs the player's drawn stroke into a crisp glowing Heart (♡) or Plus (+),
    /// pulses it with healing sparks, and fades it out.
    /// </summary>
    private IEnumerator AnimateHealGestureLine(LineRenderer lineRenderer, HealGestureType gestureType, Bounds gestureBounds)
    {
        if (lineRenderer == null) yield break;

        const int symbolPointCount = 40;
        Vector3[] startPts = new Vector3[symbolPointCount];
        Vector3[] targetPts = new Vector3[symbolPointCount];

        int origCount = lineRenderer.positionCount;
        Vector3[] rawOrig = new Vector3[Mathf.Max(1, origCount)];
        if (origCount > 0)
        {
            lineRenderer.GetPositions(rawOrig);
        }

        Vector3 center = gestureBounds.center;
        float radiusX = Mathf.Clamp(gestureBounds.extents.x, 0.55f, 1.85f);
        float radiusY = Mathf.Clamp(gestureBounds.extents.y, 0.55f, 1.85f);
        float uniformRadius = (radiusX + radiusY) * 0.5f;

        for (int i = 0; i < symbolPointCount; i++)
        {
            float u = (float)i / (symbolPointCount - 1);
            int srcIdx = Mathf.Clamp(Mathf.RoundToInt(u * (rawOrig.Length - 1)), 0, rawOrig.Length - 1);
            startPts[i] = rawOrig[srcIdx];

            if (gestureType == HealGestureType.Heart)
            {
                float t = u * Mathf.PI * 2f;
                Vector2 nh = EvaluateNormalizedHeartPoint(t);
                targetPts[i] = center + new Vector3((nh.x - 0.5f) * 2f * uniformRadius, (nh.y - 0.5f) * 2f * uniformRadius, 0f);
            }
            else
            {
                // Clean 12-vertex outline Plus (+) symbol
                targetPts[i] = EvaluatePlusOutlinePoint(u, center, uniformRadius);
            }
        }

        lineRenderer.positionCount = symbolPointCount;
        Color baseColor = gestureType == HealGestureType.Heart ? heartGestureGlowColor : plusGestureGlowColor;
        float baseWidth = lineRenderer.startWidth > 0.01f ? lineRenderer.startWidth : 0.14f;

        // 1. Snap/morph into clean Heart or Plus over 0.15s
        const float morphDuration = 0.15f;
        float timer = 0f;
        while (timer < morphDuration && lineRenderer != null)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / morphDuration);
            float ease = 1f - (1f - t) * (1f - t);

            for (int i = 0; i < symbolPointCount; i++)
            {
                lineRenderer.SetPosition(i, Vector3.Lerp(startPts[i], targetPts[i], ease));
            }

            float w = Mathf.Lerp(baseWidth, baseWidth * 1.45f, Mathf.Sin(t * Mathf.PI));
            lineRenderer.startWidth = w;
            lineRenderer.endWidth = w;
            yield return null;
        }

        // 2. Pulse outward & dissolve over 0.28s
        const float fadeDuration = 0.28f;
        timer = 0f;
        while (timer < fadeDuration && lineRenderer != null)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / fadeDuration);
            float scale = 1f + t * 0.22f;
            float alpha = 1f - t;

            for (int i = 0; i < symbolPointCount; i++)
            {
                Vector3 offset = targetPts[i] - center;
                lineRenderer.SetPosition(i, center + offset * scale);
            }

            Color c = Color.Lerp(baseColor, Color.white, 0.35f * (1f - t));
            c.a = alpha;
            lineRenderer.startColor = c;
            lineRenderer.endColor = c;
            lineRenderer.startWidth = baseWidth * (1.2f * alpha);
            lineRenderer.endWidth = baseWidth * (1.2f * alpha);
            yield return null;
        }

        if (lineRenderer != null)
        {
            Destroy(lineRenderer.gameObject);
        }
    }

    private static Vector3 EvaluatePlusOutlinePoint(float u, Vector3 center, float radius)
    {
        float arm = radius * 0.32f;
        Vector2[] v =
        {
            new Vector2(-arm,  radius),
            new Vector2( arm,  radius),
            new Vector2( arm,  arm),
            new Vector2( radius,  arm),
            new Vector2( radius, -arm),
            new Vector2( arm, -arm),
            new Vector2( arm, -radius),
            new Vector2(-arm, -radius),
            new Vector2(-arm, -arm),
            new Vector2(-radius, -arm),
            new Vector2(-radius,  arm),
            new Vector2(-arm,  arm),
            new Vector2(-arm,  radius)
        };

        float scaled = Mathf.Clamp01(u) * (v.Length - 1);
        int idx = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, v.Length - 2);
        float frac = scaled - idx;
        Vector2 pt = Vector2.Lerp(v[idx], v[idx + 1], frac);
        return center + new Vector3(pt.x, pt.y, 0f);
    }

    #endregion

    #region Combat & Damage

    public IEnumerator AnimateKillLine(LineRenderer lineRenderer)
    {
        if (lineRenderer == null) yield break;

        LineRendererManager lineRendererManager = lineRenderer.GetComponent<LineRendererManager>();
        if (lineRendererManager == null || lineRendererManager.targets.Count == 0)
        {
            Destroy(lineRenderer.gameObject);
            yield break;
        }

        int pointCount = lineRenderer.positionCount;
        if (pointCount < 2)
        {
            Destroy(lineRendererManager.gameObject);
            yield break;
        }

        Vector3[] origPoints = new Vector3[pointCount];
        lineRenderer.GetPositions(origPoints);

        float totalLength = 0f;
        float[] segmentLengths = new float[pointCount - 1];
        for (int i = 0; i < pointCount - 1; i++)
        {
            segmentLengths[i] = Vector3.Distance(origPoints[i], origPoints[i + 1]);
            totalLength += segmentLengths[i];
        }

        lineRenderer.enabled = true;

        if (drawGradient.colorKeys.Length > 0) lineRenderer.colorGradient = drawGradient;

        float drawTimer = 0f;
        List<Vector3> drawPoints = new List<Vector3>(pointCount);

        while (drawTimer < slashDrawDuration)
        {
            drawTimer += Time.deltaTime;
            float linearT = Mathf.Clamp01(drawTimer / slashDrawDuration);

            float progress = linearT == 1f ? 1f : 1f - Mathf.Pow(2f, -10f * linearT);
            float currentDrawDist = progress * totalLength;

            drawPoints.Clear();
            drawPoints.Add(origPoints[0]);

            float accumulated = 0f;
            for (int i = 0; i < pointCount - 1; i++)
            {
                float segLen = segmentLengths[i];
                if (accumulated + segLen <= currentDrawDist)
                {
                    drawPoints.Add(origPoints[i + 1]);
                    accumulated += segLen;
                }
                else
                {
                    float segProgress = segLen > 0f ? (currentDrawDist - accumulated) / segLen : 0f;
                    Vector3 endPt = Vector3.Lerp(origPoints[i], origPoints[i + 1], segProgress);
                    drawPoints.Add(endPt);

                    if (slashPathParticlePrefab != null && Random.value > 0.5f)
                    {
                        Instantiate(slashPathParticlePrefab, endPt, Quaternion.identity);
                    }
                    break;
                }
            }

            if (drawPoints.Count >= 2)
            {
                lineRenderer.positionCount = drawPoints.Count;
                for (int p = 0; p < drawPoints.Count; p++) lineRenderer.SetPosition(p, drawPoints[p]);
            }

            yield return null;
        }

        lineRenderer.positionCount = pointCount;
        lineRenderer.SetPositions(origPoints);

        Vector2 slashVector = (origPoints[origPoints.Length - 1] - origPoints[0]).normalized;
        int totalChainedTargets = lineRendererManager.targets.Count;

        if (hitstopDuration > 0f)
        {
            float scaledHitstop = totalChainedTargets >= 3 ? hitstopDuration * 1.35f : hitstopDuration;
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(scaledHitstop);
            Time.timeScale = 1f;
        }

        HashSet<EnemyBase> primaryEnemies = new HashSet<EnemyBase>();
        bool anyTargetSlashed = false;
        HashSet<EnemyBase> aliveBeforeSlash = new HashSet<EnemyBase>();

        for (int i = 0; i < lineRendererManager.targets.Count; i++)
        {
            GameObject targetObj = lineRendererManager.targets[i];
            if (targetObj == null) continue;

            // Resolve the primary enemy entity (distinguishing weak points from the main body)
            EnemyBase primary = null;
            if (targetObj.TryGetComponent<ArmoredNinjaWeakPoint>(out var anWp) && anWp.OwnerNinja != null)
            {
                primary = anWp.OwnerNinja;
            }
            else if (targetObj.TryGetComponent<BossTargetPoint>(out var bWp) && bWp.OwnerBoss != null)
            {
                primary = bWp.OwnerBoss;
            }
            else
            {
                ArmoredNinja an = targetObj.GetComponentInParent<ArmoredNinja>();
                if (an != null)
                {
                    primary = an;
                }
                else
                {
                    LegendarySamurai ls = targetObj.GetComponentInParent<LegendarySamurai>();
                    if (ls != null)
                    {
                        primary = ls;
                    }
                    else
                    {
                        EnemyBase eb = targetObj.GetComponentInParent<EnemyBase>();
                        if (eb != null && !(eb is ArmoredNinjaWeakPoint) && !(eb is BossTargetPoint))
                        {
                            primary = eb;
                        }
                    }
                }
            }

            if (primary != null)
            {
                primaryEnemies.Add(primary);
                if (!primary.IsDead)
                {
                    aliveBeforeSlash.Add(primary);
                }
            }

            if (slashImpactPrefab != null)
            {
                Instantiate(slashImpactPrefab, targetObj.transform.position, Quaternion.identity);
            }

            // Check slashable props / crates
            ISlashable slashable = targetObj.GetComponentInParent<ISlashable>();
            if (slashable != null && !(slashable is EnemyBase))
            {
                slashable.OnSlashed(slashVector);
                anyTargetSlashed = true;
                continue;
            }

            ChainEnemy chainEnemy = targetObj.GetComponentInParent<ChainEnemy>();
            if (chainEnemy != null)
            {
                if (!chainEnemy.IsDead)
                {
                    chainEnemy.OnDeath(slashVector);
                }
                continue;
            }

            EnemyBase enemy = targetObj.GetComponentInParent<EnemyBase>();
            if (enemy != null)
            {
                if (!enemy.IsDead)
                {
                    enemy.OnDeath(slashVector);
                }
            }
        }

        // Check whether at least one primary enemy actually died or sustained damage
        bool anyEnemyDiedOrHit = anyTargetSlashed;
        foreach (EnemyBase primaryEnemy in primaryEnemies)
        {
            if (primaryEnemy == null)
            {
                anyEnemyDiedOrHit = true;
                continue;
            }

            if (primaryEnemy is ArmoredNinja armoredNinja)
            {
                if (armoredNinja.IsDead || armoredNinja.IsDefeated)
                {
                    armoredNinja.TrySpawnDeathSlice(slashVector);
                    anyEnemyDiedOrHit = true;
                }
            }
            else if (primaryEnemy is LegendarySamurai boss)
            {
                if (boss.IsDead || boss.WasHitLandedInCurrentSlash)
                {
                    anyEnemyDiedOrHit = true;
                }
            }
            else
            {
                // Normal enemy (Enemy, Ninja, ThrowerNinja, SuicideNinja, ChainEnemy)
                if (primaryEnemy.IsDead && aliveBeforeSlash.Contains(primaryEnemy))
                {
                    primaryEnemy.TrySpawnDeathSlice(slashVector);
                    anyEnemyDiedOrHit = true;
                }
            }
        }

        if (anyEnemyDiedOrHit)
        {
            PlaySFX(postSlashSFX);
            float punchIntensity = totalChainedTargets >= 3 ? -0.68f : -0.38f;
            TriggerCameraZoomPunch(punchIntensity, 0.22f);
        }

        if (flashGradient.colorKeys.Length > 0) lineRenderer.colorGradient = flashGradient;

        float orgWidth = lineRenderer.widthMultiplier;
        const float flashDuration = 0.18f;
        float timer = 0f;
        Vector3[] jitteredPoints = new Vector3[pointCount];

        while (timer < flashDuration)
        {
            timer += Time.deltaTime;
            float t = timer / flashDuration;
            float pulseFactor = Mathf.Sin(t * Mathf.PI);

            lineRenderer.widthMultiplier = Mathf.Lerp(orgWidth, orgWidth * 3f, pulseFactor);

            for (int j = 0; j < pointCount; j++)
            {
                Vector3 jitter = (Vector3)Random.insideUnitCircle * jitterStrength * pulseFactor;
                jitteredPoints[j] = origPoints[j] + jitter;
            }
            lineRenderer.SetPositions(jitteredPoints);

            yield return null;
        }

        lineRenderer.SetPositions(origPoints);
        lineRenderer.widthMultiplier = orgWidth;

        if (dissolveGradient.colorKeys.Length > 0) lineRenderer.colorGradient = dissolveGradient;

        float disappearTimer = 0f;
        List<Vector3> currentPoints = drawPoints;

        while (disappearTimer < slashDisappearDuration)
        {
            disappearTimer += Time.deltaTime;
            float linearT = Mathf.Clamp01(disappearTimer / slashDisappearDuration);

            float progress = linearT * linearT;
            float trimDistance = progress * totalLength;

            currentPoints.Clear();
            float accumulated = 0f;
            bool startFound = false;

            for (int i = 0; i < pointCount - 1; i++)
            {
                float segLen = segmentLengths[i];

                if (!startFound)
                {
                    if (accumulated + segLen >= trimDistance)
                    {
                        float segProgress = segLen > 0f ? (trimDistance - accumulated) / segLen : 0f;
                        Vector3 newStartPoint = Vector3.Lerp(origPoints[i], origPoints[i + 1], segProgress);
                        currentPoints.Add(newStartPoint);
                        startFound = true;
                    }
                }
                else
                {
                    currentPoints.Add(origPoints[i]);
                }

                accumulated += segLen;
            }

            if (startFound)
            {
                currentPoints.Add(origPoints[pointCount - 1]);
            }

            if (currentPoints.Count >= 2)
            {
                lineRenderer.positionCount = currentPoints.Count;
                for (int p = 0; p < currentPoints.Count; p++) lineRenderer.SetPosition(p, currentPoints[p]);
            }
            else
            {
                lineRenderer.positionCount = 0;
            }

            yield return null;
        }

        lineRenderer.enabled = false;
        Destroy(lineRendererManager.gameObject);
    }

    private void SetupGrappleIndicator()
    {
        if (insGrappleTargetIndicator != null)
        {
            insGrappleTargetIndicator.SetActive(false);
            return;
        }

        if (grappleTargetIndicatorPrefab != null)
        {
            insGrappleTargetIndicator = Instantiate(grappleTargetIndicatorPrefab, transform.position, Quaternion.identity);
        }
        else
        {
            insGrappleTargetIndicator = new GameObject("GrappleTargetIndicator");
            SpriteRenderer sr = insGrappleTargetIndicator.AddComponent<SpriteRenderer>();

            Texture2D tex = new Texture2D(32, 32);
            Color[] colors = new Color[32 * 32];
            Vector2 center = new Vector2(15.5f, 15.5f);
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 32; y++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if ((dist >= 12f && dist <= 15f) || (dist <= 3f) || (Mathf.Abs(x - 15.5f) < 1f && dist <= 14f) || (Mathf.Abs(y - 15.5f) < 1f && dist <= 14f))
                    {
                        colors[y * 32 + x] = new Color(0f, 0.9f, 1f, 0.9f);
                    }
                    else
                    {
                        colors[y * 32 + x] = Color.clear;
                    }
                }
            }
            tex.SetPixels(colors);
            tex.Apply();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 16f);
            sr.sortingOrder = 10;
        }

        insGrappleTargetIndicator.SetActive(false);
    }

    private void UpdateGrappleIndicator()
    {
        if (insGrappleTargetIndicator == null) return;

        if (isFocusing)
        {
            if (!insGrappleTargetIndicator.activeSelf)
            {
                insGrappleTargetIndicator.SetActive(true);
            }

            Vector3 mouseWorldPos = GetMouseWorldPosition();
            Vector3 dirToMouse = (mouseWorldPos - transform.position).normalized;
            if (dirToMouse == Vector3.zero) dirToMouse = Vector3.right;

            // Position indicator orbiting outside player at set radius
            Vector3 targetPos = transform.position + dirToMouse * grappleIndicatorOrbitRadius;
            insGrappleTargetIndicator.transform.position = targetPos;

            // Align rotation with -90 degree offset for Upward-facing target graphics
            float angle = (Mathf.Atan2(dirToMouse.y, dirToMouse.x) * Mathf.Rad2Deg) - 90f;
            insGrappleTargetIndicator.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
        else
        {
            if (insGrappleTargetIndicator.activeSelf)
            {
                insGrappleTargetIndicator.SetActive(false);
            }
        }
    }

    private void HandleGrappleInput()
    {
        if (grappleAction == null) return;

        if (isFocusing && currentMeter <= 0)
        {
            GrapplingHook();
            return;
        }

        if (grappleAction.IsPressed())
        {
            if (IsInMode) return;
            if (!IsGrappling && !isThrowingHook && !isFocusing && currentMeter >= maxMeter / 10f) GrapplingFocus();
        }
        else
        {
            if (!IsGrappling && !isThrowingHook && isFocusing) GrapplingHook();
        }
    }

    private void GrapplingHook()
    {
        if (activeGrapplingCoroutine != null) StopCoroutine(activeGrapplingCoroutine);
        activeGrapplingCoroutine = StartCoroutine(AttachGrappleRoutine());
    }

    private IEnumerator AttachGrappleRoutine()
    {
        isThrowingHook = true;
        IsGrappling = false;
        isFocusing = false;
        isWallSliding = false;
        grappleTargetTransform = null;
        cachedGrappleEnemyCollider = null;

        ConsumeMeter(maxMeter / 10f);

        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = defaultFixedDeltaTime;

        if (Mouse.current == null || GetActiveCamera() == null || grappleLine == null)
        {
            isThrowingHook = false;
            yield break;
        }

        Vector2 mouseWorldPos = GetMouseWorldPosition();
        Vector2 fireOrigin = GetGrappleRopeStartPosition(false);
        Vector2 dir = (mouseWorldPos - fireOrigin).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        if (spriteRenderer != null && Mathf.Abs(dir.x) > 0.05f)
        {
            spriteRenderer.flipX = dir.x < 0f;
        }

        RaycastHit2D rayHit = FindGrappleHit(fireOrigin, dir, out EnemyBase hitEnemy);
        if (hitEnemy != null)
        {
            cachedGrappleEnemyCollider = hitEnemy.GetComponent<Collider2D>();
        }
        grappleAnchorPoint = rayHit.collider != null ? rayHit.point : fireOrigin + (dir * maxGrappleRange);

        grappleLine.positionCount = ropeSegments;
        grappleLine.enabled = false;
        EnsureGrapplePixelRopeContainer();
        if (grapplePixelRopeContainer != null) grapplePixelRopeContainer.SetActive(true);

        releaseGrappleAnimTimer = releaseGrappleAnimDuration;
        SetPlayerAnimState(PlayerAnimState.ReleaseGrapplingHook, true);

        PlaySFX(grappleReleaseSFX);

        float distance = Vector2.Distance(fireOrigin, grappleAnchorPoint);
        float duration = Mathf.Max(0.02f, distance / grappleSpeed);
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / duration);
            if (hitEnemy != null && !hitEnemy.IsDead)
            {
                grappleAnchorPoint = GetEnemyGrapplePoint(hitEnemy);
            }
            Vector2 currentStartPos = GetGrappleRopeStartPosition(false);
            currentHookTip = Vector2.Lerp(currentStartPos, grappleAnchorPoint, progress);

            RenderProceduralRope(currentStartPos, currentHookTip, progress, true);
            yield return null;
        }

        isThrowingHook = false;

        if (rayHit.collider != null)
        {
            IsGrappling = true;
            PlaySFX(grappleHitSFX);

            if (hitEnemy == null)
            {
                hitEnemy = rayHit.collider.GetComponentInParent<EnemyBase>();
                if (hitEnemy != null)
                {
                    cachedGrappleEnemyCollider = hitEnemy.GetComponent<Collider2D>();
                }
            }

            if (grappleImpactPrefab != null)
            {
                Instantiate(grappleImpactPrefab, grappleAnchorPoint, Quaternion.identity);
            }

            if (hitEnemy != null && !hitEnemy.IsHeavyEnemy)
            {
                // Pause meter regeneration while reeling the enemy in for the kick
                if (activeMeterCoroutine != null)
                {
                    StopCoroutine(activeMeterCoroutine);
                    activeMeterCoroutine = null;
                }

                // 1. Pull the enemy rapidly toward the player along the grapple rope
                hitEnemy.OnGrappled(Vector2.zero);
                doubleJump = true;
                canDash = true;

                Rigidbody2D enemyRb = hitEnemy.GetComponent<Rigidbody2D>();
                const float maxReelDuration = 0.65f;
                float reelTimer = 0f;
                const float kickDistance = 1.55f;
                const float kickDistanceSqr = kickDistance * kickDistance;

                while (IsGrappling && reelTimer < maxReelDuration && hitEnemy != null && !hitEnemy.IsDead)
                {
                    reelTimer += Time.deltaTime;
                    Vector2 enemyCenter = GetEnemyGrapplePoint(hitEnemy);
                    Vector2 toPlayer = (Vector2)transform.position - enemyCenter;
                    float distToPlayerSqr = toPlayer.sqrMagnitude;

                    // 2. Once the enemy is pulled right up close to the player, kick them off far!
                    if (distToPlayerSqr <= kickDistanceSqr)
                    {
                        ForceExecuteGrappleKick(hitEnemy, dir);
                        yield break;
                    }

                    if (enemyRb != null)
                    {
                        Vector2 reelDir = toPlayer.normalized;
                        enemyRb.linearVelocity = reelDir * pullForce;
                    }

                    RenderProceduralRope(GetGrappleRopeStartPosition(true), enemyCenter, 1f, false);
                    yield return null;
                }

                if (IsGrappling && hitEnemy != null && !hitEnemy.IsDead)
                {
                    ForceExecuteGrappleKick(hitEnemy, dir);
                }
                else
                {
                    DetachGrappling();
                    if (!IsInMode && !isFocusing)
                    {
                        if (activeMeterCoroutine != null) StopCoroutine(activeMeterCoroutine);
                        activeMeterCoroutine = StartCoroutine(MeterRegenerate());
                    }
                }
                yield break;
            }

            if (hitEnemy != null)
            {
                grappleTargetTransform = hitEnemy.transform;
            }

            if (springJoint != null)
            {
                springJoint.enabled = true;
                springJoint.anchor = GetGrappleLocalStartOffset(true);
                springJoint.connectedAnchor = grappleAnchorPoint;
                springJoint.distance = Vector2.Distance(GetGrappleRopeStartPosition(true), grappleAnchorPoint);
            }

            while (IsGrappling && springJoint != null && springJoint.enabled)
            {
                springJoint.anchor = GetGrappleLocalStartOffset(true);

                if (grappleTargetTransform != null)
                {
                    if (hitEnemy != null && hitEnemy.IsDead)
                    {
                        DetachGrappling();
                        yield break;
                    }
                    grappleAnchorPoint = hitEnemy != null ? GetEnemyGrapplePoint(hitEnemy) : (Vector2)grappleTargetTransform.position;
                    springJoint.connectedAnchor = grappleAnchorPoint;
                }

                if (springJoint.distance > minDistance)
                {
                    springJoint.distance -= pullForce * Time.deltaTime;
                }

                RenderProceduralRope(GetGrappleRopeStartPosition(true), grappleAnchorPoint, 1f, false);
                yield return null;
            }
        }
        else
        {
            DetachGrappling();
        }
    }

    private Vector2 GetEnemyGrapplePoint(EnemyBase enemy)
    {
        if (enemy == null) return Vector2.zero;
        if (cachedGrappleEnemyCollider != null && cachedGrappleEnemyCollider.gameObject == enemy.gameObject)
        {
            return cachedGrappleEnemyCollider.bounds.center;
        }
        Collider2D col = enemy.GetComponent<Collider2D>();
        cachedGrappleEnemyCollider = col;
        return col != null ? (Vector2)col.bounds.center : (Vector2)enemy.transform.position;
    }

    /// <summary>
    /// Finds the closest valid grapple target along the aim direction, detecting both grappleLayer walls and any EnemyBase colliders.
    /// </summary>
    private RaycastHit2D FindGrappleHit(Vector2 origin, Vector2 dir, out EnemyBase hitEnemy)
    {
        hitEnemy = null;

        // 1. Check environment on grappleLayer
        RaycastHit2D wallHit = Physics2D.Raycast(origin, dir, maxGrappleRange, grappleLayer);
        float maxCheckDist = wallHit.collider != null ? wallHit.distance : maxGrappleRange;

        // 2. Check for enemies along the ray
        RaycastHit2D[] allHits = Physics2D.CircleCastAll(origin, 0.25f, dir, maxCheckDist);
        float closestEnemyDist = float.MaxValue;
        RaycastHit2D bestEnemyHit = default;

        for (int i = 0; i < allHits.Length; i++)
        {
            RaycastHit2D candidate = allHits[i];
            if (candidate.collider == null || candidate.collider.isTrigger) continue;
            if (candidate.collider.gameObject == gameObject || candidate.collider.transform.IsChildOf(transform)) continue;

            EnemyBase enemy = candidate.collider.GetComponentInParent<EnemyBase>();
            if (enemy != null && !enemy.IsDead)
            {
                Vector2 toEnemy = ((Vector2)candidate.collider.bounds.center - origin).normalized;
                if (Vector2.Dot(toEnemy, dir) < 0.25f) continue;

                if (candidate.distance < closestEnemyDist)
                {
                    closestEnemyDist = candidate.distance;
                    bestEnemyHit = candidate;
                    hitEnemy = enemy;
                }
            }
        }

        if (hitEnemy != null)
        {
            return bestEnemyHit;
        }

        return wallHit;
    }

    /// <summary>
    /// Checks if the player is actively swinging/reeling on the grapple hook (or reeling in a grappled enemy) to kick the enemy on contact.
    /// </summary>
    public bool TryExecuteGrappleKick(EnemyBase enemy)
    {
        if (enemy == null || enemy.IsDead) return false;
        if (Time.time < lastGrappleKickTime + 0.15f) return true; // Prevent multi-frame collision re-triggers

        // Allow kick if actively grappling at speed, OR if this enemy is currently being reeled in by the grapple
        bool isBeingReeledIn = IsGrappling && enemy.IsGrappleStunned;
        bool isSwingingFast = IsGrappling && rb.linearVelocity.sqrMagnitude >= grappleKickSpeedThreshold * grappleKickSpeedThreshold;

        if (!isBeingReeledIn && !isSwingingFast) return false;

        Vector2 fallbackDir = ((Vector2)(enemy.transform.position - transform.position)).normalized;
        ForceExecuteGrappleKick(enemy, fallbackDir);
        return true;
    }

    private void ForceExecuteGrappleKick(EnemyBase enemy, Vector2 preferredDir)
    {
        if (enemy == null || enemy.IsDead) return;
        lastGrappleKickTime = Time.time;

        if (IsGrappling)
        {
            DetachGrappling();
        }

        // Determine launch direction: use held movement direction if steering, otherwise launch away along preferredDir
        Vector2 kickDir;
        if (Mathf.Abs(inputValue.x) > 0.1f)
        {
            kickDir = new Vector2(Mathf.Sign(inputValue.x), 0.35f).normalized;
        }
        else if (preferredDir.sqrMagnitude > 0.01f)
        {
            kickDir = (preferredDir.normalized + Vector2.up * 0.3f).normalized;
        }
        else
        {
            float facingDir = spriteRenderer != null && spriteRenderer.flipX ? -1f : 1f;
            kickDir = new Vector2(facingDir, 0.35f).normalized;
        }

        enemy.OnGrappleKicked(kickDir * grappleKickKnockback);

        // Visual & audio impact feedback
        if (grappleImpactPrefab != null)
        {
            Instantiate(grappleImpactPrefab, enemy.transform.position, Quaternion.identity);
        }
        else if (slashImpactPrefab != null)
        {
            Instantiate(slashImpactPrefab, enemy.transform.position, Quaternion.identity);
        }

        PlaySFX(grappleHitSFX != null ? grappleHitSFX : postSlashSFX);
        kickAnimTimer = kickAnimDuration;
        SetPlayerAnimState(PlayerAnimState.Kick, true);
        StartCoroutine(GrappleKickHitstopAndShakeRoutine());

        // Bounce player into an aerial vault, refresh mobility, and reduce mode meter by 1 target's cost
        float recoilX = -Mathf.Sign(kickDir.x) * moveSpeed * 0.45f;
        rb.linearVelocity = new Vector2(recoilX, grappleKickBounceForce);
        doubleJump = true;
        isDoubleJumping = false;
        canDash = true;

        ConsumeMeter(maxMeter / 10f);
    }

    private IEnumerator GrappleKickHitstopAndShakeRoutine()
    {
        TriggerCameraZoomPunch(-0.45f, 0.20f);

        if (hitstopDuration > 0f && !IsInMode && !isFocusing)
        {
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(hitstopDuration * 0.8f);
            if (!IsInMode && !isFocusing)
            {
                Time.timeScale = 1f;
            }
        }

        TriggerCameraShake(0.15f);
    }

    public void ConsumeMeter(float amount)
    {
        if (amount <= 0f) return;

        currentMeter = Mathf.Clamp(currentMeter - amount, 0f, maxMeter);
        if (modeMeterImage != null)
        {
            modeMeterImage.fillAmount = currentMeter / maxMeter;
        }

        ShowMeterBar();
        if (!IsInMode && !isFocusing)
        {
            if (activeMeterCoroutine != null) StopCoroutine(activeMeterCoroutine);
            activeMeterCoroutine = StartCoroutine(MeterRegenerate());
        }
    }

    /// <summary>
    /// Refunds Chain Slash meter (e.g. when parrying a projectile) and pulses the meter bar.
    /// </summary>
    public void RefundMeter(float amount)
    {
        if (amount <= 0f || isDead) return;

        currentMeter = Mathf.Clamp(currentMeter + amount, 0f, maxMeter);
        if (modeMeterImage != null)
        {
            modeMeterImage.fillAmount = currentMeter / maxMeter;
        }

        ShowMeterBar();
    }

    /// <summary>
    /// Triggers a brief unscaled micro-hitstop (e.g. on projectile parry) that safely restores the active timeScale.
    /// </summary>
    public void TriggerBriefHitstop(float duration)
    {
        if (duration <= 0f || isDead) return;
        if (activeBriefHitstopCoroutine != null) StopCoroutine(activeBriefHitstopCoroutine);
        activeBriefHitstopCoroutine = StartCoroutine(BriefHitstopRoutine(duration));
    }

    private IEnumerator BriefHitstopRoutine(float duration)
    {
        Time.timeScale = 0.02f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = (IsInMode || isFocusing) ? timeSpeed : 1f;
        Time.fixedDeltaTime = defaultFixedDeltaTime * Time.timeScale;
        activeBriefHitstopCoroutine = null;
    }

    private float GetPlayerHeight()
    {
        if (playerCollider != null && playerCollider.bounds.size.y > 0.05f)
        {
            return playerCollider.bounds.size.y;
        }
        if (spriteRenderer != null && spriteRenderer.bounds.size.y > 0.05f)
        {
            return spriteRenderer.bounds.size.y;
        }
        return 1.5f;
    }

    private Vector2 GetGrappleLocalStartOffset(bool isAttached)
    {
        float offsetY = GetPlayerHeight() * grappleStartHeightMultiplier;
        float offsetX = 0f;
        if (isAttached)
        {
            float facingSign = (spriteRenderer != null && spriteRenderer.flipX) ? -1f : 1f;
            offsetX = facingSign * grappleAttachedHandOffsetX;
        }
        return new Vector2(offsetX, offsetY);
    }

    private Vector2 GetGrappleRopeStartPosition(bool isAttached)
    {
        return (Vector2)transform.position + GetGrappleLocalStartOffset(isAttached);
    }

    private void RenderProceduralRope(Vector2 startPos, Vector2 endPos, float launchProgress, bool isThrowing)
    {
        if (grappleLine != null && grappleLine.enabled)
        {
            grappleLine.enabled = false;
        }

        int segCount = Mathf.Max(2, ropeSegments);
        EnsureGrapplePixelRopeContainer();
        if (grapplePixelRopeContainer == null) return;
        if (!grapplePixelRopeContainer.activeSelf) grapplePixelRopeContainer.SetActive(true);

        Vector2 dir = (endPos - startPos);
        Vector2 perpendicular = dir.sqrMagnitude > 0.0001f ? new Vector2(-dir.y, dir.x).normalized : Vector2.up;

        // 1. Sample curve control points (including throw wave or hang sag)
        Vector2 prevCurvePt = startPos;
        int activeLinkCount = 0;
        Vector3 lastSnappedPos = new Vector3(float.MinValue, float.MinValue, 0f);
        const float pixelStepWorld = 1f / 16f; // 1 pixel step at 16 PPU

        Sprite lightLink = GetOrCreateGrappleChainLinkSprite(true);
        Sprite darkLink = GetOrCreateGrappleChainLinkSprite(false);
        int sortLayerId = spriteRenderer != null ? spriteRenderer.sortingLayerID : 0;
        int sortOrder = spriteRenderer != null ? spriteRenderer.sortingOrder - 1 : 4;

        for (int i = 0; i < segCount; i++)
        {
            float t = (float)i / (segCount - 1);
            Vector2 curvePt = Vector2.Lerp(startPos, endPos, t);

            if (isThrowing)
            {
                float wave = Mathf.Sin(t * Mathf.PI * waveFrequency) * waveAmplitude * (1f - launchProgress);
                curvePt += perpendicular * wave;
            }
            else
            {
                float sag = Mathf.Sin(t * Mathf.PI) * 0.15f;
                curvePt -= Vector2.up * sag;
            }

            if (i > 0)
            {
                float segDist = Vector2.Distance(prevCurvePt, curvePt);
                int subSteps = Mathf.Max(1, Mathf.CeilToInt(segDist / pixelStepWorld));
                for (int s = 0; s <= subSteps; s++)
                {
                    Vector2 sample = Vector2.Lerp(prevCurvePt, curvePt, (float)s / subSteps);
                    Vector3 snapped = EnemyBase.SnapToPixelGrid(sample, 16f);
                    if ((snapped - lastSnappedPos).sqrMagnitude < 0.0001f)
                    {
                        continue;
                    }

                    lastSnappedPos = snapped;
                    SpriteRenderer linkSr = GetOrCreateGrappleLinkRenderer(activeLinkCount, sortLayerId, sortOrder);
                    linkSr.transform.position = snapped;
                    linkSr.sprite = (activeLinkCount % 2 == 0) ? lightLink : darkLink;
                    if (!linkSr.gameObject.activeSelf) linkSr.gameObject.SetActive(true);
                    activeLinkCount++;
                }
            }

            prevCurvePt = curvePt;
        }

        // Hide unused pooled links
        for (int i = activeLinkCount; i < grapplePixelLinkRenderers.Count; i++)
        {
            if (grapplePixelLinkRenderers[i] != null && grapplePixelLinkRenderers[i].gameObject.activeSelf)
            {
                grapplePixelLinkRenderers[i].gameObject.SetActive(false);
            }
        }

        // Position pixel grappling claw at the tip
        if (grapplePixelClawRenderer != null)
        {
            grapplePixelClawRenderer.sortingLayerID = sortLayerId;
            grapplePixelClawRenderer.sortingOrder = sortOrder + 1;
            grapplePixelClawRenderer.transform.position = EnemyBase.SnapToPixelGrid(endPos, 16f);
            if (!grapplePixelClawRenderer.gameObject.activeSelf)
            {
                grapplePixelClawRenderer.gameObject.SetActive(true);
            }
        }
    }

    private void EnsureGrapplePixelRopeContainer()
    {
        if (grapplePixelRopeContainer != null) return;

        grapplePixelRopeContainer = new GameObject("GrapplePixelRopeContainer");
        GameObject clawObj = new GameObject("GrapplePixelClaw");
        clawObj.transform.SetParent(grapplePixelRopeContainer.transform, false);
        clawObj.transform.localRotation = Quaternion.identity;
        clawObj.transform.localScale = Vector3.one;

        grapplePixelClawRenderer = clawObj.AddComponent<SpriteRenderer>();
        grapplePixelClawRenderer.sprite = GetOrCreateGrappleClawPixelSprite();
    }

    private SpriteRenderer GetOrCreateGrappleLinkRenderer(int index, int sortingLayerId, int sortingOrder)
    {
        while (grapplePixelLinkRenderers.Count <= index)
        {
            GameObject linkObj = new GameObject("GrapplePixelLink_" + grapplePixelLinkRenderers.Count);
            linkObj.transform.SetParent(grapplePixelRopeContainer.transform, false);
            linkObj.transform.localRotation = Quaternion.identity;
            linkObj.transform.localScale = Vector3.one;

            SpriteRenderer sr = linkObj.AddComponent<SpriteRenderer>();
            grapplePixelLinkRenderers.Add(sr);
        }

        SpriteRenderer target = grapplePixelLinkRenderers[index];
        target.sortingLayerID = sortingLayerId;
        target.sortingOrder = sortingOrder;
        return target;
    }

    private static Sprite GetOrCreateGrappleChainLinkSprite(bool isLightLink)
    {
        if (isLightLink && cachedGrappleLinkLightSprite != null) return cachedGrappleLinkLightSprite;
        if (!isLightLink && cachedGrappleLinkDarkSprite != null) return cachedGrappleLinkDarkSprite;

        // 3x3 pixel chain link: 1px metallic core + 1px dark outline at 16 PPU
        const int size = 3;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color outline = new Color(0.08f, 0.09f, 0.14f, 0.95f);
        Color core = isLightLink
            ? new Color(0.92f, 0.96f, 1.0f, 1.0f)
            : new Color(0.55f, 0.64f, 0.76f, 1.0f);

        Color[] px =
        {
            clear,   outline, clear,
            outline, core,    outline,
            clear,   outline, clear
        };

        tex.SetPixels(px);
        tex.Apply();

        Sprite spr = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        if (isLightLink) cachedGrappleLinkLightSprite = spr;
        else cachedGrappleLinkDarkSprite = spr;
        return spr;
    }

    private static Sprite GetOrCreateGrappleClawPixelSprite()
    {
        if (cachedGrappleClawSprite != null) return cachedGrappleClawSprite;

        // 7x7 pixel grappling claw / hook head at 16 PPU
        const int size = 7;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color dark = new Color(0.08f, 0.09f, 0.14f, 1f);
        Color steel = new Color(0.92f, 0.96f, 1f, 1f);

        byte[] mask =
        {
            0b0101010,
            0b1011101,
            0b0111110,
            0b1111111,
            0b0111110,
            0b1011101,
            0b0101010
        };

        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            byte row = mask[size - 1 - y];
            for (int x = 0; x < size; x++)
            {
                bool filled = ((row >> (size - 1 - x)) & 1) != 0;
                if (!filled)
                {
                    px[y * size + x] = clear;
                }
                else
                {
                    bool isCore = (x >= 2 && x <= 4 && y >= 2 && y <= 4) || x == 3 || y == 3;
                    px[y * size + x] = isCore ? steel : dark;
                }
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        cachedGrappleClawSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        return cachedGrappleClawSprite;
    }

    private void GrapplingFocus()
    {
        ShowMeterBar();

        if (activeMeterCoroutine != null) StopCoroutine(activeMeterCoroutine);
        activeMeterCoroutine = StartCoroutine(MeterDegeneration(1f, 0.005f));

        isFocusing = true;
        Time.timeScale = timeSpeed;
        Time.fixedDeltaTime = defaultFixedDeltaTime * Time.timeScale;
    }

    public void DetachGrappling()
    {
        IsGrappling = false;
        isThrowingHook = false;
        grappleTargetTransform = null;
        cachedGrappleEnemyCollider = null;
        if (grappleLine != null) grappleLine.enabled = false;
        if (grapplePixelRopeContainer != null) grapplePixelRopeContainer.SetActive(false);
        if (springJoint != null)
        {
            springJoint.anchor = Vector2.zero;
            springJoint.enabled = false;
        }
    }

    #endregion

    #region Player Sprite Animation (No Animator)

    /// <summary>
    /// Triggered when the player parries/reflects a shuriken or projectile.
    /// </summary>
    public void OnParryTriggered()
    {
        if (isDead) return;
        parryAnimTimer = Mathf.Max(0.10f, parryAnimDuration);
        SetPlayerAnimState(PlayerAnimState.Parry, true);
        TriggerCameraZoomPunch(-0.32f, 0.16f);
    }

    public void SetPlayerAnimState(PlayerAnimState newState, bool forceReset = false)
    {
        if (!forceReset && currentAnimState == newState) return;

        currentAnimState = newState;
        currentAnimFrame = 0;
        animTimer = 0f;

        List<Sprite> frames = GetFramesForPlayerState(newState);
        if (frames != null && frames.Count > 0 && frames[0] != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = frames[0];
        }
    }

    private void UpdatePlayerSpriteAnimation(bool grounded)
    {
        if (spriteRenderer == null) return;

        float unscaledDt = Time.unscaledDeltaTime;
        float scaledDt = Time.deltaTime;

        if (parryAnimTimer > 0f)
        {
            parryAnimTimer -= unscaledDt;
        }
        if (kickAnimTimer > 0f)
        {
            kickAnimTimer -= scaledDt;
        }
        if (releaseGrappleAnimTimer > 0f)
        {
            releaseGrappleAnimTimer -= scaledDt;
        }

        // Determine active animation state by priority
        PlayerAnimState targetState;
        if (isDead)
        {
            targetState = PlayerAnimState.Death;
        }
        else if (parryAnimTimer > 0f)
        {
            targetState = PlayerAnimState.Parry;
        }
        else if (kickAnimTimer > 0f)
        {
            targetState = PlayerAnimState.Kick;
        }
        else if (isDashing)
        {
            targetState = PlayerAnimState.Dash;
        }
        else if (isThrowingHook || IsGrappling || releaseGrappleAnimTimer > 0f)
        {
            targetState = PlayerAnimState.ReleaseGrapplingHook;
        }
        else if (IsInMode)
        {
            targetState = PlayerAnimState.InModeSlashingStance;
        }
        else if (!grounded)
        {
            targetState = isDoubleJumping ? PlayerAnimState.DoubleJump : PlayerAnimState.Jump;
        }
        else if (Mathf.Abs(inputValue.x) > 0.05f && rb != null && Mathf.Abs(rb.linearVelocityX) > 0.05f)
        {
            targetState = PlayerAnimState.Walk;
        }
        else
        {
            targetState = PlayerAnimState.Idle;
        }

        if (targetState != currentAnimState)
        {
            currentAnimState = targetState;
            currentAnimFrame = 0;
            animTimer = 0f;
        }

        List<Sprite> frames = GetFramesForPlayerState(currentAnimState);
        if (frames == null || frames.Count == 0)
        {
            if (defaultBaseSprite != null && spriteRenderer.sprite != defaultBaseSprite)
            {
                spriteRenderer.sprite = defaultBaseSprite;
            }
            return;
        }

        float fps = GetFPSForPlayerState(currentAnimState);
        if (fps <= 0.01f) fps = 8f;
        float frameDuration = 1f / fps;

        // Use unscaled delta time for states that occur during slow-motion or death
        bool useUnscaled = (currentAnimState == PlayerAnimState.Death)
                        || (currentAnimState == PlayerAnimState.Parry)
                        || (currentAnimState == PlayerAnimState.InModeSlashingStance);

        animTimer += useUnscaled ? unscaledDt : scaledDt;

        if (animTimer >= frameDuration)
        {
            int framesToAdvance = Mathf.FloorToInt(animTimer / frameDuration);
            animTimer %= frameDuration;
            currentAnimFrame += framesToAdvance;

            bool shouldLoop = (currentAnimState == PlayerAnimState.Idle)
                           || (currentAnimState == PlayerAnimState.Walk);

            if (currentAnimFrame >= frames.Count)
            {
                if (shouldLoop)
                {
                    currentAnimFrame %= frames.Count;
                }
                else
                {
                    currentAnimFrame = frames.Count - 1;
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

    private List<Sprite> GetFramesForPlayerState(PlayerAnimState state)
    {
        switch (state)
        {
            case PlayerAnimState.Idle:
                return idleSprites;
            case PlayerAnimState.Walk:
                return (walkSprites != null && walkSprites.Count > 0) ? walkSprites : idleSprites;
            case PlayerAnimState.Dash:
                return (dashSprites != null && dashSprites.Count > 0) ? dashSprites : idleSprites;
            case PlayerAnimState.Jump:
                return (jumpSprites != null && jumpSprites.Count > 0) ? jumpSprites : idleSprites;
            case PlayerAnimState.DoubleJump:
                return (doubleJumpSprites != null && doubleJumpSprites.Count > 0)
                    ? doubleJumpSprites
                    : ((jumpSprites != null && jumpSprites.Count > 0) ? jumpSprites : idleSprites);
            case PlayerAnimState.Parry:
                return (parrySprites != null && parrySprites.Count > 0) ? parrySprites : idleSprites;
            case PlayerAnimState.InModeSlashingStance:
                return (inModeSlashingStanceSprites != null && inModeSlashingStanceSprites.Count > 0) ? inModeSlashingStanceSprites : idleSprites;
            case PlayerAnimState.ReleaseGrapplingHook:
                return (releaseGrapplingHookSprites != null && releaseGrapplingHookSprites.Count > 0) ? releaseGrapplingHookSprites : idleSprites;
            case PlayerAnimState.Death:
                return (deathSprites != null && deathSprites.Count > 0) ? deathSprites : idleSprites;
            case PlayerAnimState.Kick:
                return (kickSprites != null && kickSprites.Count > 0) ? kickSprites : idleSprites;
            default:
                return idleSprites;
        }
    }

    private float GetFPSForPlayerState(PlayerAnimState state)
    {
        switch (state)
        {
            case PlayerAnimState.Idle: return idleFPS;
            case PlayerAnimState.Walk: return walkFPS;
            case PlayerAnimState.Dash: return dashFPS;
            case PlayerAnimState.Jump: return jumpFPS;
            case PlayerAnimState.DoubleJump: return doubleJumpFPS;
            case PlayerAnimState.Parry: return parryFPS;
            case PlayerAnimState.InModeSlashingStance: return inModeSlashingStanceFPS;
            case PlayerAnimState.ReleaseGrapplingHook: return releaseGrapplingHookFPS;
            case PlayerAnimState.Death: return deathFPS;
            case PlayerAnimState.Kick: return kickFPS;
            default: return 8f;
        }
    }

    private void EnsureDefaultPlayerSprites()
    {
#if UNITY_EDITOR
        const string folder = "Assets/Textures/Characters/N ewPlayer/";
        TryPopulateSpriteList(ref idleSprites, folder + "stand1.png", folder + "stand2.png");
        TryPopulateSpriteList(ref walkSprites, folder + "Run1.png", folder + "Run2.png", folder + "Run3.png", folder + "Run4.png");
        TryPopulateSpriteList(ref dashSprites, folder + "dash.png");
        TryPopulateSpriteList(ref jumpSprites, folder + "jump1.png", folder + "jump2.png");
        TryPopulateSpriteList(ref doubleJumpSprites, folder + "jumpslash1.png", folder + "jumpslash2.png");
        TryPopulateSpriteList(ref parrySprites, folder + "parry.png");
        TryPopulateSpriteList(ref inModeSlashingStanceSprites, folder + "slash1.png", folder + "slash2.png");
        TryPopulateSpriteList(ref releaseGrapplingHookSprites, folder + "graphook.png");
        TryPopulateSpriteList(ref deathSprites, folder + "dead1.png", folder + "dead2.png");
        TryPopulateSpriteList(ref kickSprites, folder + "kick.png");

        if (idleSprites != null && idleSprites.Count > 0 && idleSprites[0] != null)
        {
            defaultBaseSprite = idleSprites[0];
            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = defaultBaseSprite;
            }
        }
#endif
    }

#if UNITY_EDITOR
    private static void TryPopulateSpriteList(ref List<Sprite> list, params string[] assetPaths)
    {
        if (list == null) list = new List<Sprite>();
        if (list.Count > 0) return;

        for (int i = 0; i < assetPaths.Length; i++)
        {
            Sprite sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetPaths[i]);
            if (sp != null)
            {
                list.Add(sp);
            }
        }
    }
#endif

    #endregion

    #region Audio & VFX

    private void SetupMeterUI()
    {
        meterCanvasGroups.Clear();

        if (modeMeterBarObject != null)
        {
            CanvasGroup cg = modeMeterBarObject.GetComponent<CanvasGroup>();
            if (cg == null) cg = modeMeterBarObject.AddComponent<CanvasGroup>();
            if (!meterCanvasGroups.Contains(cg)) meterCanvasGroups.Add(cg);
        }

        if (modeMeterImage != null)
        {
            CanvasGroup cg = modeMeterImage.GetComponent<CanvasGroup>();
            if (cg == null) cg = modeMeterImage.gameObject.AddComponent<CanvasGroup>();
            if (!meterCanvasGroups.Contains(cg)) meterCanvasGroups.Add(cg);

            modeMeterImage.fillAmount = currentMeter / maxMeter;
        }

        SetMeterAlpha(0f);
    }

    private void SetMeterAlpha(float alpha)
    {
        for (int i = 0; i < meterCanvasGroups.Count; i++)
        {
            CanvasGroup cg = meterCanvasGroups[i];
            if (cg != null) cg.alpha = alpha;
        }
    }

    private void ShowMeterBar()
    {
        if (activeMeterFadeCoroutine != null) StopCoroutine(activeMeterFadeCoroutine);
        activeMeterFadeCoroutine = StartCoroutine(FadeMeterBar(1f, false));
    }

    private void HideMeterBarWithPulse()
    {
        if (activeMeterFadeCoroutine != null) StopCoroutine(activeMeterFadeCoroutine);
        activeMeterFadeCoroutine = StartCoroutine(FadeMeterBar(0f, true));
    }

    private IEnumerator FadeMeterBar(float targetAlpha, bool pulseOnFull)
    {
        if (meterCanvasGroups.Count == 0) yield break;

        if (pulseOnFull)
        {
            float pulseTimer = 0f;
            const float pulseDuration = 0.15f;
            while (pulseTimer < pulseDuration)
            {
                pulseTimer += Time.deltaTime;
                float scale = Mathf.Lerp(1f, 1.15f, Mathf.Sin((pulseTimer / pulseDuration) * Mathf.PI));

                for (int i = 0; i < meterCanvasGroups.Count; i++)
                {
                    if (meterCanvasGroups[i] != null) meterCanvasGroups[i].transform.localScale = Vector3.one * scale;
                }
                yield return null;
            }

            for (int i = 0; i < meterCanvasGroups.Count; i++)
            {
                if (meterCanvasGroups[i] != null) meterCanvasGroups[i].transform.localScale = Vector3.one;
            }
        }

        float startAlpha = meterCanvasGroups.Count > 0 && meterCanvasGroups[0] != null ? meterCanvasGroups[0].alpha : 0f;
        float timer = 0f;

        while (timer < meterFadeDuration)
        {
            timer += Time.deltaTime;
            float currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, timer / meterFadeDuration);
            SetMeterAlpha(currentAlpha);
            yield return null;
        }

        SetMeterAlpha(targetAlpha);
    }

    private IEnumerator MeterRegenerate()
    {
        yield return currentMeter == 0 ? WaitOneSecond : WaitHalfSecond;

        while (currentMeter < maxMeter)
        {
            currentMeter = Mathf.Clamp(currentMeter + 1f, 0f, maxMeter);
            if (modeMeterImage != null) modeMeterImage.fillAmount = currentMeter / maxMeter;
            yield return WaitMeterRegenTick;
        }

        HideMeterBarWithPulse();
        activeMeterCoroutine = null;
    }

    private IEnumerator MeterDegeneration(float amount, float interval)
    {
        WaitForSeconds wait = Mathf.Approximately(interval, 0.005f) ? WaitMeterDegenTick : new WaitForSeconds(interval);
        while (currentMeter > 0f)
        {
            currentMeter = Mathf.Clamp(currentMeter - amount, 0f, maxMeter);
            if (modeMeterImage != null) modeMeterImage.fillAmount = currentMeter / maxMeter;
            yield return wait;
        }

        activeMeterCoroutine = null;
    }

    public void TriggerCameraShake(float duration)
    {
        shakeRemainingTime = Mathf.Max(shakeRemainingTime, duration);
        if (activeShakeCoroutine == null)
        {
            activeShakeCoroutine = StartCoroutine(ShakingTrigger(duration));
        }
    }

    public IEnumerator ShakingTrigger(float duration)
    {
        shakeRemainingTime = Mathf.Max(shakeRemainingTime, duration);

        if (perlin == null && cinemachineCamera != null)
        {
            perlin = cinemachineCamera.GetComponent<CinemachineBasicMultiChannelPerlin>();
        }

        if (perlin != null)
        {
            if (noiseSettings != null)
            {
                perlin.NoiseProfile = noiseSettings;
            }
            perlin.AmplitudeGain = 10f;
            if (perlin.FrequencyGain <= 0.01f)
            {
                perlin.FrequencyGain = 2f;
            }

            while (shakeRemainingTime > 0f)
            {
                shakeRemainingTime -= Time.unscaledDeltaTime;
                yield return null;
            }

            perlin.NoiseProfile = orgNoiseSettings;
            perlin.AmplitudeGain = orgAmplitudeGain;
            perlin.FrequencyGain = orgFrequencyGain;
        }

        shakeRemainingTime = 0f;
        activeShakeCoroutine = null;
    }

    public void PlaySFX(AudioClip audioClip)
    {
        if (audioClip != null && audioSource != null && soundPlaying < maxSoundPlaying)
        {
            audioSource.PlayOneShot(audioClip);
            soundPlaying++;
        }
    }

    public void PlayPitchedSFX(AudioClip audioClip, float pitch = 1f, float volumeScale = 1f)
    {
        if (audioClip == null || soundPlaying >= maxSoundPlaying) return;

        AudioSource src = pitchedAudioSource != null ? pitchedAudioSource : audioSource;
        if (src != null)
        {
            src.pitch = pitch;
            src.PlayOneShot(audioClip, volumeScale);
            soundPlaying++;
        }
    }

    private void PlayJuicyTargetSelectSFX(int comboIndex)
    {
        if (targetSelectSFX == null) return;

        float comboPitch = Mathf.Clamp(
            targetSelectBasePitch + (comboIndex * targetSelectPitchStep),
            0.5f,
            targetSelectMaxPitch
        );

        // Primary lock-on hit with ascending combo pitch and slight volume boost per chain
        float primaryVolume = Mathf.Clamp(0.9f + (comboIndex * 0.05f), 0.9f, 1.2f);
        AudioSource primarySrc = pitchedAudioSource != null ? pitchedAudioSource : audioSource;
        if (primarySrc != null)
        {
            primarySrc.pitch = comboPitch;
            primarySrc.PlayOneShot(targetSelectSFX, primaryVolume);
        }

        // Harmonic metallic shimmer overtone (a musical fifth higher) slightly delayed in real-time
        if (shimmerAudioSource != null)
        {
            StartCoroutine(TargetShimmerEchoRoutine(comboPitch * 1.5f));
        }
    }

    private IEnumerator TargetShimmerEchoRoutine(float shimmerPitch)
    {
        yield return WaitShimmerEcho;
        if (shimmerAudioSource != null && targetSelectSFX != null)
        {
            shimmerAudioSource.pitch = Mathf.Min(shimmerPitch, 2.4f);
            shimmerAudioSource.PlayOneShot(targetSelectSFX, 0.35f);
        }
    }

    #endregion

    #region Helpers

    private void ResetStateForLoadedScene()
    {
        ReviveAndReset();

        if (defaultFixedDeltaTime > 0f)
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = defaultFixedDeltaTime;
        }

        BindSceneCameraAndTargets();
        SetupGrappleIndicator();
        SetupModeDimOverlay();
    }

    private Vector3 GetSceneSpawnPosition()
    {
        GameManager gm = GameManager.Instance != null ? GameManager.Instance : FindFirstObjectByType<GameManager>();
        if (gm != null && gm.PlayerSpawnPoint != null)
        {
            return gm.PlayerSpawnPoint.position;
        }

        if (pendingSpawnReset)
        {
            return initialSpawnPosition;
        }

        GameObject namedSpawn = GameObject.Find("PlayerSpawnPoint");
        if (namedSpawn == null)
        {
            namedSpawn = GameObject.Find("SpawnPoint");
        }
        if (namedSpawn != null)
        {
            return namedSpawn.transform.position;
        }

        return initialSpawnPosition;
    }

    private Vector3 ResolveSafePlayerPosition(Vector3 desiredPos)
    {
        Vector2 boxSize = new Vector2(0.9f, 1.4f);
        float halfHeight = 0.75f;

        if (playerCollider is BoxCollider2D boxCol)
        {
            boxSize = Vector2.Scale(boxCol.size, transform.localScale) * 0.92f;
            halfHeight = (boxCol.size.y * Mathf.Abs(transform.localScale.y)) * 0.5f;
        }
        else if (playerCollider != null && playerCollider.bounds.size.sqrMagnitude > 0.01f)
        {
            boxSize = (Vector2)playerCollider.bounds.size * 0.92f;
            halfHeight = playerCollider.bounds.extents.y;
        }

        // 1. If desiredPos is already free of ground/wall colliders and has ground beneath it, use it directly
        if (Physics2D.OverlapBox(desiredPos, boxSize, 0f, groundLayer) == null && HasGroundBelow(desiredPos))
        {
            return desiredPos;
        }

        // 2. Try stepping straight up out of any embedded floor at desiredPos.x
        for (float upOffset = 0.25f; upOffset <= 8f; upOffset += 0.25f)
        {
            Vector3 liftedPos = desiredPos + Vector3.up * upOffset;
            if (Physics2D.OverlapBox(liftedPos, boxSize, 0f, groundLayer) == null && HasGroundBelow(liftedPos))
            {
                return liftedPos;
            }
        }

        // 3. Scan horizontal offsets around desiredPos (and world origin) from open air to find a clean floor surface
        for (int c = 0; c < 2; c++)
        {
            Vector3 center = c == 0 ? desiredPos : Vector3.zero;
            for (int xIdx = 0; xIdx < SafeSpawnXOffsets.Length; xIdx++)
            {
                float xOffset = SafeSpawnXOffsets[xIdx];
                for (int yIdx = 0; yIdx < SafeSpawnYProbes.Length; yIdx++)
                {
                    float yProbe = SafeSpawnYProbes[yIdx];
                    Vector2 rayOrigin = new Vector2(center.x + xOffset, center.y + yProbe);
                    if (Physics2D.OverlapPoint(rayOrigin, groundLayer) != null)
                    {
                        continue;
                    }

                    RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, 25f, groundLayer);
                    if (hit.collider != null && hit.distance > 0.02f && hit.normal.y >= 0.7f)
                    {
                        Vector2 candidatePos = hit.point + Vector2.up * (halfHeight + 0.1f);
                        if (Physics2D.OverlapBox(candidatePos, boxSize, 0f, groundLayer) == null)
                        {
                            return new Vector3(candidatePos.x, candidatePos.y, desiredPos.z);
                        }
                    }
                }
            }
        }

        return desiredPos;
    }

    private bool HasGroundBelow(Vector3 checkPos)
    {
        RaycastHit2D hit = Physics2D.Raycast(checkPos + Vector3.up * 0.5f, Vector2.down, 25f, groundLayer);
        return hit.collider != null;
    }

    private void BindSceneCameraAndTargets()
    {
        // 1. Automatically detect the Main Camera in the active scene
        mainCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (mainCamera != null)
        {
            if (mainCamera.GetComponent<CinemachineBrain>() == null)
            {
                mainCamera.gameObject.AddComponent<CinemachineBrain>();
            }

            Canvas playerCanvas = GetComponentInChildren<Canvas>(true);
            if (playerCanvas != null && playerCanvas.renderMode == RenderMode.WorldSpace)
            {
                playerCanvas.worldCamera = mainCamera;
            }
        }

        // 2. Automatically detect the CinemachineTargetGroup in the active scene and bind the Player
        if (targetGroup == null)
        {
            targetGroup = FindFirstObjectByType<CinemachineTargetGroup>();
        }

        if (targetGroup != null)
        {
            if (targetGroup.Targets.Count == 0)
            {
                targetGroup.AddMember(transform, 5f, 5f);
            }
            else
            {
                targetGroup.Targets[0].Object = transform;
            }

            if (targetGroup.Targets.Count < 2)
            {
                targetGroup.AddMember(null, 5f, 5f);
            }
            else
            {
                targetGroup.Targets[1].Object = null;
            }
        }

        // 3. Automatically detect CinemachineCamera and assign Target Group as its Tracking Target
        cinemachineCamera = FindFirstObjectByType<CinemachineCamera>();
        if (cinemachineCamera != null)
        {
            cinemachineCamera.Target.TrackingTarget = targetGroup != null ? targetGroup.transform : transform;
            if (cinemachineCamera.Lens.OrthographicSize > 0.5f)
            {
                baseCameraOrthoSize = cinemachineCamera.Lens.OrthographicSize;
            }

            perlin = cinemachineCamera.GetComponent<CinemachineBasicMultiChannelPerlin>();
            if (perlin != null)
            {
                orgNoiseSettings = perlin.NoiseProfile;
                orgAmplitudeGain = perlin.AmplitudeGain;
                orgFrequencyGain = perlin.FrequencyGain;
            }
        }
        else if (mainCamera != null && mainCamera.orthographic && mainCamera.orthographicSize > 0.5f)
        {
            baseCameraOrthoSize = mainCamera.orthographicSize;
        }

        currentZoomMultiplier = 1f;
        currentZoomPunchOffset = 0f;
    }

    private Camera GetActiveCamera()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        }
        return mainCamera;
    }

    #endregion

    #region Juiciness & Game Feel Systems

    // =========================================================================
    // 1. Dash, Double-Jump, Kick & High-Speed Grapple Afterimage Ghosts
    // =========================================================================
    private void UpdateAfterimageGhosts()
    {
        if (!enableAfterimages || isDead || spriteRenderer == null || spriteRenderer.sprite == null) return;

        bool highSpeedFlight = rb != null && rb.linearVelocity.sqrMagnitude > (moveSpeed * 1.38f) * (moveSpeed * 1.38f);
        bool risingDoubleJump = isDoubleJumping && rb != null && rb.linearVelocityY > 1.2f;
        bool shouldSpawn = isDashing || risingDoubleJump || kickAnimTimer > 0f || highSpeedFlight;

        if (!shouldSpawn)
        {
            afterimageTimer = 0f;
            return;
        }

        afterimageTimer -= Time.unscaledDeltaTime;
        if (afterimageTimer <= 0f)
        {
            afterimageTimer = Mathf.Max(0.015f, afterimageInterval);
            PlayerAfterimageGhostVFX.Spawn(spriteRenderer, afterimageLifetime, afterimageStartColor, afterimageEndColor);
        }
    }

    // =========================================================================
    // 5. Dynamic Camera Zoom-In During Chain Slash & Zoom-Punch on Execution
    // =========================================================================
    private void UpdateDynamicCameraZoom()
    {
        if (!enableDynamicCameraZoom) return;

        if (baseCameraOrthoSize <= 0.5f)
        {
            if (cinemachineCamera != null && cinemachineCamera.Lens.OrthographicSize > 0.5f)
            {
                baseCameraOrthoSize = cinemachineCamera.Lens.OrthographicSize;
            }
            else if (mainCamera != null && mainCamera.orthographic && mainCamera.orthographicSize > 0.5f)
            {
                baseCameraOrthoSize = mainCamera.orthographicSize;
            }
            else
            {
                return;
            }
        }

        float targetMult = (IsInMode || isFocusing) ? Mathf.Clamp(chainSlashZoomMultiplier, 0.75f, 1f) : 1f;
        float dt = Time.unscaledDeltaTime > 0f ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0.016f;
        currentZoomMultiplier = Mathf.Lerp(currentZoomMultiplier, targetMult, dt * Mathf.Max(1f, cameraZoomSmoothSpeed));

        float desiredOrtho = Mathf.Max(2.2f, (baseCameraOrthoSize * currentZoomMultiplier) + currentZoomPunchOffset);

        if (cinemachineCamera != null)
        {
            var lens = cinemachineCamera.Lens;
            lens.OrthographicSize = desiredOrtho;
            cinemachineCamera.Lens = lens;
        }
        else if (mainCamera != null && mainCamera.orthographic)
        {
            mainCamera.orthographicSize = desiredOrtho;
        }
    }

    public void TriggerCameraZoomPunch(float orthoDelta, float duration = 0.2f)
    {
        if (!enableDynamicCameraZoom || duration <= 0.01f) return;
        if (activeZoomPunchCoroutine != null) StopCoroutine(activeZoomPunchCoroutine);
        activeZoomPunchCoroutine = StartCoroutine(CameraZoomPunchRoutine(orthoDelta, duration));
    }

    private IEnumerator CameraZoomPunchRoutine(float orthoDelta, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            currentZoomPunchOffset = orthoDelta * Mathf.Sin(t * Mathf.PI) * (1f - t * 0.45f);
            yield return null;
        }
        currentZoomPunchOffset = 0f;
        activeZoomPunchCoroutine = null;
    }

    // =========================================================================
    // 6. Jump, Double-Jump, Wall-Slide & Landing Pixel Dust Puffs + Landing Squash
    // =========================================================================
    private Vector3 GetFeetPosition()
    {
        if (groundCheck != null) return groundCheck.position;
        return transform.position - new Vector3(0f, GetPlayerHeight() * 0.48f, 0f);
    }

    private void SpawnTakeoffDust()
    {
        if (!enableMovementDustVFX) return;
        PixelMovementDustVFX.SpawnDustBurst(GetFeetPosition(), Vector2.up, 5, Color.white);
    }

    private void OnPlayerLanded(float impactSpeed)
    {
        if (enableMovementDustVFX)
        {
            int count = impactSpeed > 11f ? 7 : 4;
            PixelMovementDustVFX.SpawnLandingPuffs(GetFeetPosition(), count, Color.white);
        }

        if (spriteRenderer != null)
        {
            if (activeLandingSquashCoroutine != null) StopCoroutine(activeLandingSquashCoroutine);
            activeLandingSquashCoroutine = StartCoroutine(LandingSquashRoutine(Mathf.Clamp01(impactSpeed / 18f)));
        }
    }

    private IEnumerator LandingSquashRoutine(float intensity)
    {
        const float duration = 0.11f;
        float elapsed = 0f;
        float squashY = Mathf.Lerp(0.91f, 0.80f, intensity);
        float stretchX = Mathf.Lerp(1.06f, 1.16f, intensity);

        while (elapsed < duration && spriteRenderer != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float wave = Mathf.Sin(t * Mathf.PI);

            float sx = originalSpriteScale.x * Mathf.Lerp(1f, stretchX, wave);
            float sy = originalSpriteScale.y * Mathf.Lerp(1f, squashY, wave);
            float deltaY = sy - originalSpriteScale.y;
            float offsetY = spriteRenderer.sprite != null ? (-spriteRenderer.sprite.bounds.min.y * deltaY) : 0f;

            spriteRenderer.transform.localScale = new Vector3(sx, sy, originalSpriteScale.z);
            spriteRenderer.transform.localPosition = new Vector3(
                originalSpriteLocalPos.x,
                originalSpriteLocalPos.y + offsetY,
                originalSpriteLocalPos.z
            );
            yield return null;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.transform.localScale = originalSpriteScale;
            spriteRenderer.transform.localPosition = originalSpriteLocalPos;
        }
        activeLandingSquashCoroutine = null;
    }

    private void SpawnDoubleJumpAirRing()
    {
        if (!enableMovementDustVFX) return;
        PixelMovementDustVFX.SpawnAirRing(GetFeetPosition(), Color.white);
    }

    private void UpdateWallSlideDust()
    {
        if (!enableMovementDustVFX || !isWallSliding)
        {
            wallSlideDustTimer = 0f;
            return;
        }

        wallSlideDustTimer -= Time.deltaTime;
        if (wallSlideDustTimer <= 0f)
        {
            wallSlideDustTimer = 0.075f;
            Vector3 wallPos = transform.position + new Vector3(wallDirection * 0.35f, 0.1f, 0f);
            PixelMovementDustVFX.SpawnDustBurst(wallPos, Vector2.up, 2, Color.white);
        }
    }

    private void SpawnWallJumpDust(int kickDir)
    {
        if (!enableMovementDustVFX) return;
        Vector3 wallPos = transform.position + new Vector3(-kickDir * 0.35f, 0f, 0f);
        Vector2 burstDir = new Vector2(kickDir, 0.5f).normalized;
        PixelMovementDustVFX.SpawnDustBurst(wallPos, burstDir, 5, Color.white);
    }

    // =========================================================================
    // 7. Target Lock-On Crosshair Reticle & Combo Counter Popups
    // =========================================================================
    private void SpawnTargetReticleAndComboPopup(Transform targetTransform, int comboNumber)
    {
        if (!enableTargetReticlesAndComboPopups || targetTransform == null) return;

        GameObject reticle = TargetLockReticleVFX.Spawn(targetTransform, comboNumber);
        if (reticle != null)
        {
            activeTargetReticles.Add(reticle);
        }

        ComboCounterPopupVFX.Spawn(targetTransform.position + Vector3.up * 0.75f, comboNumber, GetOrCreateComboSprite(comboNumber));
    }

    private void ClearTargetReticles()
    {
        for (int i = 0; i < activeTargetReticles.Count; i++)
        {
            if (activeTargetReticles[i] != null)
            {
                Destroy(activeTargetReticles[i]);
            }
        }
        activeTargetReticles.Clear();
    }

    private static Sprite GetOrCreateComboSprite(int comboNumber)
    {
        int clamped = Mathf.Clamp(comboNumber, 1, 99);
        if (cachedComboPopupSprites.TryGetValue(clamped, out Sprite existing) && existing != null)
        {
            return existing;
        }

        string text = clamped >= 3 ? (clamped + "x!") : (clamped + "x");
        int charCount = text.Length;
        const int glyphW = 5;
        const int glyphH = 7;
        const int spacing = 1;
        const int pad = 2;

        int texW = pad * 2 + charCount * glyphW + (charCount - 1) * spacing;
        int texH = pad * 2 + glyphH;

        Texture2D tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[texW * texH];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        bool[,] fillMask = new bool[texW, texH];

        int cursorX = pad;
        for (int c = 0; c < charCount; c++)
        {
            byte[] rows = GetPixelGlyphRows(text[c]);
            for (int ry = 0; ry < glyphH; ry++)
            {
                byte rowBits = rows[glyphH - 1 - ry]; // top-to-bottom -> bottom-to-top
                for (int rx = 0; rx < glyphW; rx++)
                {
                    if (((rowBits >> (glyphW - 1 - rx)) & 1) != 0)
                    {
                        fillMask[cursorX + rx, pad + ry] = true;
                    }
                }
            }
            cursorX += glyphW + spacing;
        }

        Color outlineCol = new Color(0.06f, 0.06f, 0.12f, 1f);
        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                if (fillMask[x, y])
                {
                    pixels[y * texW + x] = Color.white;
                }
                else
                {
                    bool neighborFilled =
                        (x > 0 && fillMask[x - 1, y]) ||
                        (x < texW - 1 && fillMask[x + 1, y]) ||
                        (y > 0 && fillMask[x, y - 1]) ||
                        (y < texH - 1 && fillMask[x, y + 1]);
                    if (neighborFilled)
                    {
                        pixels[y * texW + x] = outlineCol;
                    }
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        Sprite spr = Sprite.Create(tex, new Rect(0, 0, texW, texH), new Vector2(0.5f, 0.5f), 16f);
        cachedComboPopupSprites[clamped] = spr;
        return spr;
    }

    private static byte[] GetPixelGlyphRows(char ch)
    {
        switch (ch)
        {
            case '0': return new byte[] { 0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110 };
            case '1': return new byte[] { 0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 };
            case '2': return new byte[] { 0b01110, 0b10001, 0b00001, 0b00110, 0b01000, 0b10000, 0b11111 };
            case '3': return new byte[] { 0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110 };
            case '4': return new byte[] { 0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010 };
            case '5': return new byte[] { 0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110 };
            case '6': return new byte[] { 0b01110, 0b10000, 0b11110, 0b10001, 0b10001, 0b10001, 0b01110 };
            case '7': return new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000 };
            case '8': return new byte[] { 0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110 };
            case '9': return new byte[] { 0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00001, 0b01110 };
            case 'x': return new byte[] { 0b00000, 0b00000, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001 };
            case '!': return new byte[] { 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00000, 0b00100 };
            default:  return new byte[] { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000 };
        }
    }

    #endregion
}

/// <summary>
/// Fading pixel silhouette afterimage ghost for Player Dash, Double-Jump, Kick, and High-Speed Grapple flight.
/// Uses pixel-grid position snapping and stepped 4-stage retro opacity.
/// </summary>
public class PlayerAfterimageGhostVFX : MonoBehaviour
{
    private SpriteRenderer sr;
    private float duration;
    private float elapsed;
    private Color startColor;
    private Color endColor;

    public static void Spawn(SpriteRenderer source, float lifetime, Color startTint, Color endTint)
    {
        if (source == null || source.sprite == null) return;

        GameObject obj = new GameObject("PlayerAfterimageGhost");
        obj.transform.position = EnemyBase.SnapToPixelGrid(source.transform.position);
        obj.transform.rotation = Quaternion.identity;
        obj.transform.localScale = source.transform.lossyScale;

        float startAlpha = startTint.a > 0.05f ? startTint.a : 0.65f;
        Color whiteStart = new Color(1f, 1f, 1f, startAlpha);
        Color whiteEnd = new Color(1f, 1f, 1f, 0f);

        SpriteRenderer ghostSr = obj.AddComponent<SpriteRenderer>();
        ghostSr.sprite = source.sprite;
        ghostSr.flipX = source.flipX;
        ghostSr.flipY = source.flipY;
        ghostSr.sortingLayerID = source.sortingLayerID;
        ghostSr.sortingOrder = source.sortingOrder - 1;
        ghostSr.color = whiteStart;

        PlayerAfterimageGhostVFX vfx = obj.AddComponent<PlayerAfterimageGhostVFX>();
        vfx.sr = ghostSr;
        vfx.duration = Mathf.Max(0.05f, lifetime);
        vfx.startColor = whiteStart;
        vfx.endColor = whiteEnd;
    }

    private void Update()
    {
        elapsed += Time.unscaledDeltaTime > 0f ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : Time.deltaTime;
        float steppedT = EnemyBase.QuantizeStep(elapsed / duration, 4);
        if (sr != null)
        {
            sr.color = Color.Lerp(startColor, endColor, steppedT);
        }
        if (elapsed >= duration)
        {
            Destroy(gameObject);
        }
    }
}

/// <summary>
/// Frame-stepped 8-bit pixel dust puffs for Jump takeoff, Landing, Wall-Sliding, and Double-Jump air-ring bursts.
/// All sprites use FilterMode.Point, Quaternion.identity, and 16 PPU pixel-grid position snapping.
/// </summary>
public class PixelMovementDustVFX : MonoBehaviour
{
    private Transform[] puffTransforms;
    private SpriteRenderer[] puffRenderers;
    private Vector2[] puffPositions;
    private Vector2[] puffVelocities;
    private SpriteRenderer airRingRenderer;
    private Color baseColor;
    private float elapsed;
    private const float TotalDuration = 0.26f;

    private static Sprite[] cachedDustPuffFrames;
    private static Sprite[] cachedAirRingFrames;

    public static void SpawnDustBurst(Vector3 pos, Vector2 mainDir, int count, Color tint)
    {
        GameObject root = new GameObject("PixelMovementDustVFX");
        root.transform.position = EnemyBase.SnapToPixelGrid(pos);
        PixelMovementDustVFX vfx = root.AddComponent<PixelMovementDustVFX>();
        vfx.InitPuffs(mainDir, count, tint, false);
    }

    public static void SpawnLandingPuffs(Vector3 pos, int count, Color tint)
    {
        GameObject root = new GameObject("PixelLandingDustVFX");
        root.transform.position = EnemyBase.SnapToPixelGrid(pos);
        PixelMovementDustVFX vfx = root.AddComponent<PixelMovementDustVFX>();
        vfx.InitPuffs(Vector2.up, count, tint, true);
    }

    public static void SpawnAirRing(Vector3 pos, Color tint)
    {
        GameObject root = new GameObject("DoubleJumpAirRingVFX");
        root.transform.position = EnemyBase.SnapToPixelGrid(pos);
        PixelMovementDustVFX vfx = root.AddComponent<PixelMovementDustVFX>();
        vfx.InitAirRing(tint);
    }

    private static Sprite[] GetOrCreateDustPuffFrames()
    {
        if (cachedDustPuffFrames != null && cachedDustPuffFrames.Length == 3 && cachedDustPuffFrames[0] != null)
        {
            return cachedDustPuffFrames;
        }

        cachedDustPuffFrames = new Sprite[3];
        const int size = 8;
        Color clear = new Color(0f, 0f, 0f, 0f);

        // 3 frames of 8x8 pixel dust cloud: dense cluster -> hollow pixel puff -> scattered 1px dots
        ushort[][] masks =
        {
            new ushort[]
            {
                0b00000000,
                0b00011000,
                0b00111100,
                0b01111110,
                0b01111110,
                0b00111100,
                0b00011000,
                0b00000000
            },
            new ushort[]
            {
                0b00111000,
                0b01101100,
                0b11000110,
                0b10000010,
                0b11000110,
                0b01101100,
                0b00111000,
                0b00000000
            },
            new ushort[]
            {
                0b01000100,
                0b00000000,
                0b10010010,
                0b00000000,
                0b01000000,
                0b00001010,
                0b00100000,
                0b00000000
            }
        };

        for (int f = 0; f < 3; f++)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                ushort row = masks[f][size - 1 - y];
                for (int x = 0; x < size; x++)
                {
                    bool set = ((row >> (size - 1 - x)) & 1) != 0;
                    px[y * size + x] = set ? Color.white : clear;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            cachedDustPuffFrames[f] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }

        return cachedDustPuffFrames;
    }

    private static Sprite[] GetOrCreateAirRingFrames()
    {
        if (cachedAirRingFrames != null && cachedAirRingFrames.Length == 4 && cachedAirRingFrames[0] != null)
        {
            return cachedAirRingFrames;
        }

        const int w = 24;
        const int h = 8;
        float cx = 11.5f;
        float cy = 3.5f;
        float[] rxList = { 4.5f, 7.2f, 9.5f, 11.0f };
        float[] ryList = { 1.4f, 2.1f, 2.7f, 3.0f };

        cachedAirRingFrames = new Sprite[4];
        Color clear = new Color(0f, 0f, 0f, 0f);

        for (int f = 0; f < 4; f++)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] px = new Color[w * h];
            float rx = rxList[f];
            float ry = ryList[f];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (x - cx) / rx;
                    float ny = (y - cy) / ry;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    bool onOval = Mathf.Abs(d - 1f) <= 0.24f;
                    if (onOval && f == 3 && ((x + y) % 2 == 0))
                    {
                        onOval = false;
                    }
                    px[y * w + x] = onOval ? Color.white : clear;
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            cachedAirRingFrames[f] = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
        }

        return cachedAirRingFrames;
    }

    private void InitPuffs(Vector2 mainDir, int count, Color tint, bool horizontalSpread)
    {
        baseColor = tint;
        puffTransforms = new Transform[count];
        puffRenderers = new SpriteRenderer[count];
        puffPositions = new Vector2[count];
        puffVelocities = new Vector2[count];

        Sprite[] frames = GetOrCreateDustPuffFrames();

        for (int i = 0; i < count; i++)
        {
            GameObject p = new GameObject("PixelDustPuff");
            p.transform.SetParent(transform, false);
            p.transform.localScale = Vector3.one;
            p.transform.localRotation = Quaternion.identity;

            if (horizontalSpread)
            {
                float side = (i % 2 == 0) ? 1f : -1f;
                puffVelocities[i] = new Vector2(side * Random.Range(1.5f, 3.8f), Random.Range(0.4f, 1.4f));
                puffPositions[i] = new Vector2(side * 0.125f, 0f);
            }
            else
            {
                Vector2 dir = (mainDir + Random.insideUnitCircle * 0.65f).normalized;
                puffVelocities[i] = dir * Random.Range(1.2f, 3.0f);
                puffPositions[i] = Vector2.zero;
            }

            p.transform.localPosition = EnemyBase.SnapToPixelGrid(puffPositions[i]);

            SpriteRenderer sr = p.AddComponent<SpriteRenderer>();
            sr.sprite = frames[0];
            sr.flipX = (i % 2 == 1);
            sr.sortingOrder = 6;
            sr.color = tint;

            puffTransforms[i] = p.transform;
            puffRenderers[i] = sr;
        }
    }

    private void InitAirRing(Color tint)
    {
        baseColor = tint;
        Sprite[] ringFrames = GetOrCreateAirRingFrames();
        airRingRenderer = gameObject.AddComponent<SpriteRenderer>();
        airRingRenderer.sprite = ringFrames[0];
        airRingRenderer.sortingOrder = 6;
        airRingRenderer.color = tint;

        InitPuffs(Vector2.down, 4, tint, true);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        elapsed += dt;
        float t = Mathf.Clamp01(elapsed / TotalDuration);

        Sprite[] puffFrames = GetOrCreateDustPuffFrames();
        int puffFrameIdx = Mathf.Clamp(Mathf.FloorToInt(t * 3f), 0, 2);
        float quantAlpha = baseColor.a * (1f - EnemyBase.QuantizeStep(t, 4));

        if (puffTransforms != null)
        {
            for (int i = 0; i < puffTransforms.Length; i++)
            {
                if (puffTransforms[i] == null) continue;
                puffPositions[i] += puffVelocities[i] * dt;
                puffVelocities[i] *= (1f - 6.5f * dt);
                puffTransforms[i].localPosition = EnemyBase.SnapToPixelGrid(puffPositions[i]);
                puffRenderers[i].sprite = puffFrames[puffFrameIdx];
                Color c = baseColor;
                c.a = quantAlpha;
                puffRenderers[i].color = c;
            }
        }

        if (airRingRenderer != null)
        {
            Sprite[] ringFrames = GetOrCreateAirRingFrames();
            int ringIdx = Mathf.Clamp(Mathf.FloorToInt(t * 4f), 0, 3);
            airRingRenderer.sprite = ringFrames[ringIdx];
            Color rc = baseColor;
            rc.a = quantAlpha;
            airRingRenderer.color = rc;
        }

        if (elapsed >= TotalDuration)
        {
            Destroy(gameObject);
        }
    }
}

/// <summary>
/// 2-frame pixel-art lock-on crosshair reticle attached to each targeted enemy during Chain Slash mode.
/// Strictly axis-aligned (no smooth rotation or LineRenderer).
/// </summary>
public class TargetLockReticleVFX : MonoBehaviour
{
    private Transform target;
    private SpriteRenderer sr;
    private float elapsed;
    private static Sprite[] cachedReticleFrames;

    public static GameObject Spawn(Transform targetTransform, int comboNumber)
    {
        if (targetTransform == null) return null;

        GameObject obj = new GameObject("TargetLockReticleVFX");
        obj.transform.position = EnemyBase.SnapToPixelGrid(targetTransform.position);

        TargetLockReticleVFX vfx = obj.AddComponent<TargetLockReticleVFX>();
        vfx.Initialize(targetTransform, comboNumber);
        return obj;
    }

    private static Sprite[] GetOrCreateReticleFrames()
    {
        if (cachedReticleFrames != null && cachedReticleFrames.Length == 2 && cachedReticleFrames[0] != null)
        {
            return cachedReticleFrames;
        }

        const int size = 18;
        cachedReticleFrames = new Sprite[2];
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color darkOutline = new Color(0.06f, 0.06f, 0.12f, 1f);

        for (int f = 0; f < 2; f++)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            bool[,] mask = new bool[size, size];
            int inset = (f == 0) ? 1 : 3;

            // 4 pixel corner brackets
            for (int d = 0; d < 4; d++)
            {
                mask[inset + d, inset] = true;
                mask[inset, inset + d] = true;

                mask[size - 1 - inset - d, inset] = true;
                mask[size - 1 - inset, inset + d] = true;

                mask[inset + d, size - 1 - inset] = true;
                mask[inset, size - 1 - inset - d] = true;

                mask[size - 1 - inset - d, size - 1 - inset] = true;
                mask[size - 1 - inset, size - 1 - inset - d] = true;
            }

            // Center 4-pixel target cross ticks
            mask[8, 6] = true; mask[9, 6] = true;
            mask[8, 11] = true; mask[9, 11] = true;
            mask[6, 8] = true; mask[6, 9] = true;
            mask[11, 8] = true; mask[11, 9] = true;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (mask[x, y])
                    {
                        px[y * size + x] = Color.white;
                    }
                    else
                    {
                        bool nb =
                            (x > 0 && mask[x - 1, y]) ||
                            (x < size - 1 && mask[x + 1, y]) ||
                            (y > 0 && mask[x, y - 1]) ||
                            (y < size - 1 && mask[x, y + 1]);
                        if (nb) px[y * size + x] = darkOutline;
                    }
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            cachedReticleFrames[f] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }

        return cachedReticleFrames;
    }

    private void Initialize(Transform targetTransform, int comboNumber)
    {
        target = targetTransform;
        Sprite[] frames = GetOrCreateReticleFrames();

        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        sr.sortingOrder = 20;
        sr.color = Color.white;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    private void Update()
    {
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        transform.position = EnemyBase.SnapToPixelGrid(target.position);
        float dt = Time.unscaledDeltaTime > 0f ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0.016f;
        elapsed += dt;

        // 2-frame stepped pixel reticle animation at 10 FPS
        Sprite[] frames = GetOrCreateReticleFrames();
        int frameIdx = Mathf.FloorToInt(elapsed * 10f) % 2;
        if (sr != null)
        {
            sr.sprite = frames[frameIdx];
        }
    }
}

/// <summary>
/// Floating pixel-art combo popup ("1x", "2x", "3x!", etc.) with pixel-snapped rising motion and stepped pixel scale.
/// </summary>
public class ComboCounterPopupVFX : MonoBehaviour
{
    private SpriteRenderer sr;
    private Color baseTint;
    private Vector3 rawPos;
    private float elapsed;
    private const float TotalDuration = 0.52f;

    public static void Spawn(Vector3 worldPos, int comboNumber, Sprite comboSprite)
    {
        if (comboSprite == null) return;

        GameObject obj = new GameObject("ComboCounterPopup_" + comboNumber + "x");
        Vector3 snapped = EnemyBase.SnapToPixelGrid(worldPos);
        obj.transform.position = snapped;
        obj.transform.rotation = Quaternion.identity;

        SpriteRenderer renderer = obj.AddComponent<SpriteRenderer>();
        renderer.sprite = comboSprite;
        renderer.sortingOrder = 25;
        renderer.color = Color.white;

        ComboCounterPopupVFX vfx = obj.AddComponent<ComboCounterPopupVFX>();
        vfx.sr = renderer;
        vfx.baseTint = Color.white;
        vfx.rawPos = snapped;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime > 0f ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0.016f;
        elapsed += dt;
        float t = Mathf.Clamp01(elapsed / TotalDuration);

        // Stepped integer pixel scale (2x pop -> 1x crisp pixel size)
        float steppedScale = (elapsed < 0.09f) ? 2f : 1f;
        transform.localScale = Vector3.one * steppedScale;

        rawPos += Vector3.up * (1.15f * dt);
        transform.position = EnemyBase.SnapToPixelGrid(rawPos);

        if (sr != null && t > 0.55f)
        {
            float fadeT = EnemyBase.QuantizeStep((t - 0.55f) / 0.45f, 3);
            Color c = baseTint;
            c.a = 1f - fadeT;
            sr.color = c;
        }

        if (elapsed >= TotalDuration)
        {
            Destroy(gameObject);
        }
    }
}
