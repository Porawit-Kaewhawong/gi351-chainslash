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
    public static PlayerController Instance { get; set; }

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
    [SerializeField] private AudioClip grappleReleaseSFX;
    [SerializeField] private AudioClip grappleHitSFX;
    [SerializeField] private GameObject grappleImpactPrefab;
    [SerializeField] private GameObject grappleTargetIndicatorPrefab; // Optional custom target indicator prefab
    [SerializeField] private float grappleIndicatorOrbitRadius = 1.8f;  // Orbit distance outside the player

    [Header("Audio Settings")]
    [SerializeField] private int maxSoundPlaying = 3;

    // Movement state
    private bool doubleJump;
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
    public bool IsDead => isDead;
    public void SetPendingSpawnReset(bool value) => pendingSpawnReset = value;

    // Health state
    public float CurrentHealth { get; private set; }
    public bool IsImmune => Time.unscaledTime < immunityEndTime || isDashing;
    private float immunityEndTime;

    // Combat state
    public bool IsInMode { get; private set; }
    public bool IsGrappling { get; private set; }
    public Vector2 InputValue => inputValue;
    private bool isThrowingHook = false;
    private bool isFocusing = false;
    private float currentMeter;
    private float defaultFixedDeltaTime;
    private List<Vector3> drawnLinePoints = new List<Vector3>();

    // Grapple state
    private Vector2 grappleAnchorPoint;
    private Transform grappleTargetTransform;
    private Vector2 currentHookTip;
    private GameObject insGrappleTargetIndicator;

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
    private static Sprite cachedSolidWhiteSprite;

    // Instantiated Objects
    private GameObject insLineRendererObj;
    private LineRenderer insLineRendererCom;
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
    private Animator animator;
    private SpringJoint2D springJoint;
    private LineRenderer grappleLine;
    private Camera mainCamera;
    private CinemachineCamera cinemachineCamera;
    private CinemachineBasicMultiChannelPerlin perlin;

    private int soundPlaying;
    private float originalAudioSourceVolume = 1f;
    private float originalPitchedVolume = 1f;
    private float originalShimmerVolume = 1f;
    private List<CanvasGroup> meterCanvasGroups = new List<CanvasGroup>();
    private List<CanvasGroup> healthCanvasGroups = new List<CanvasGroup>();

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
        foreach (var c in colliders)
        {
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
            if (!spriteRenderer.TryGetComponent<PixelSpriteEffect>(out pixelEffect))
            {
                pixelEffect = spriteRenderer.gameObject.AddComponent<PixelSpriteEffect>();
            }
        }
        animator = GetComponentInChildren<Animator>();
        springJoint = GetComponent<SpringJoint2D>();
        grappleLine = GetComponent<LineRenderer>();

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
    }

    public void SetSpawnPositionForNewScene(Vector3 spawnPos)
    {
        initialSpawnPosition = spawnPos;
        pendingSpawnReset = true;
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
        foreach (var c in colliders)
        {
            if (c != null) c.enabled = true;
        }

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var sr in renderers)
        {
            if (sr == null) continue;
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

        if (animator != null)
        {
            animator.SetBool("isInMode", false);
            animator.SetBool("isDash", false);
            animator.SetBool("isJump", false);
            animator.SetBool("isRunning", false);
        }

        DetachGrappling();
        isFocusing = false;
        IsInMode = false;
        isDashing = false;
        canDash = true;
        isWallSliding = false;
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
        float[] xOffsets = new float[] { 0f, 1.5f, -1.5f, 3f, -3f, 4.5f, -4.5f, 6f, -6f, 8f, -8f };
        float[] yProbes = new float[] { 0.5f, 2f, -1f, 4f, 6.5f };
        Vector3[] searchCenters = new Vector3[] { desiredPos, Vector3.zero };

        foreach (Vector3 center in searchCenters)
        {
            foreach (float xOffset in xOffsets)
            {
                foreach (float yProbe in yProbes)
                {
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

            perlin = cinemachineCamera.GetComponent<CinemachineBasicMultiChannelPerlin>();
            if (perlin != null)
            {
                orgNoiseSettings = perlin.NoiseProfile;
                orgAmplitudeGain = perlin.AmplitudeGain;
                orgFrequencyGain = perlin.FrequencyGain;
            }
        }
    }

    private Camera GetActiveCamera()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        }
        return mainCamera;
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
            return;
        }

        RecordDrawnLinePoints();
        UpdateMouseTrail();
        UpdateGrappleIndicator();

        if (wallJumpLockCounter > 0f)
        {
            wallJumpLockCounter -= Time.deltaTime;
        }

        if (isDashing) return;

        bool grounded = IsGrounded();
        if (grounded)
        {
            coyoteTimeCounter = coyoteTime;
            doubleJump = true;
            isWallSliding = false;
            if (rb.linearVelocityY <= 0.25f && animator != null)
            {
                animator.SetBool("isJump", false);
            }
        }
        else
        {
            coyoteTimeCounter -= Time.deltaTime;
        }

        UpdateWallSlideState(grounded);
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

                // Preserve high-speed airborne momentum from grapple releases, dashes, or wall kicks
                if (!IsGrounded() && Mathf.Abs(currentSpeedX) > moveSpeed && Mathf.Sign(inputValue.x) == Mathf.Sign(currentSpeedX) && Mathf.Abs(inputValue.x) > 0.05f)
                {
                    float decayedX = Mathf.MoveTowards(currentSpeedX, targetSpeedX, airMomentumDecay * Time.fixedDeltaTime);
                    rb.linearVelocity = new Vector2(decayedX, rb.linearVelocityY);
                }
                else if (!IsGrounded() && Mathf.Abs(currentSpeedX) > moveSpeed && Mathf.Abs(inputValue.x) <= 0.05f)
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

                if (animator != null)
                {
                    animator.SetBool("isRunning", Mathf.Abs(rb.linearVelocityX) > 0.05f);
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

    #region Health System Logic

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
        foreach (CanvasGroup cg in healthCanvasGroups)
        {
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

        if (activeHealthRegenCoroutine != null) StopCoroutine(activeHealthRegenCoroutine);
        activeHealthRegenCoroutine = StartCoroutine(HealthRegenRoutine());
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
        float sparkInterval = 0.22f;

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

            yield return new WaitForSeconds(healthRegenTickInterval);
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

    private static Sprite cachedPixelHealCrossSprite;
    private static Sprite GetOrCreatePixelHealCrossSprite()
    {
        if (cachedPixelHealCrossSprite != null) return cachedPixelHealCrossSprite;

        int size = 5;
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
        float duration = 0.50f;
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
            float pulseDuration = 0.15f;
            while (pulseTimer < pulseDuration)
            {
                pulseTimer += Time.deltaTime;
                float scale = Mathf.Lerp(1f, 1.15f, Mathf.Sin((pulseTimer / pulseDuration) * Mathf.PI));

                foreach (CanvasGroup cg in healthCanvasGroups)
                {
                    if (cg != null) cg.transform.localScale = Vector3.one * scale;
                }
                yield return null;
            }

            foreach (CanvasGroup cg in healthCanvasGroups)
            {
                if (cg != null) cg.transform.localScale = Vector3.one;
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

        if (animator != null)
        {
            animator.SetBool("isInMode", false);
            animator.SetBool("isDash", false);
            animator.SetBool("isJump", false);
            animator.SetBool("isRunning", false);
        }

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

    #region Movement & Jump Logic

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
                rb.linearVelocity = boostDir * launchSpeed;
                doubleJump = true;
                jumpBufferCounter = 0f;
                coyoteTimeCounter = 0f;
                PlayPitchedSFX(jumpSFX, 1.08f, 1f);
                if (animator != null) animator.SetBool("isJump", true);
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
                coyoteTimeCounter = 0f;
                jumpBufferCounter = 0f;
                PlayPitchedSFX(jumpSFX, Random.Range(0.96f, 1.04f), 1f);
                if (animator != null) animator.SetBool("isJump", true);
                return;
            }

            if (enableWallMechanics && isWallSliding)
            {
                int kickDir = -wallDirection;
                rb.linearVelocity = new Vector2(kickDir * wallJumpForce.x, wallJumpForce.y);
                wallJumpLockCounter = wallJumpInputLockTime;
                isWallSliding = false;
                doubleJump = true;
                jumpBufferCounter = 0f;
                PlayPitchedSFX(jumpSFX, 1.06f, 1f);

                if (spriteRenderer != null)
                {
                    spriteRenderer.flipX = kickDir < 0;
                }
                if (animator != null) animator.SetBool("isJump", true);
                return;
            }

            if (jumpAction.WasPressedThisFrame() && doubleJump)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocityX, jumpHeight);
                doubleJump = false;
                jumpBufferCounter = 0f;
                AudioClip clipToPlay = doubleJumpSFX != null ? doubleJumpSFX : jumpSFX;
                float pitchToPlay = doubleJumpSFX != null ? Random.Range(0.96f, 1.05f) : 1.15f;
                PlayPitchedSFX(clipToPlay, pitchToPlay, 1f);
                if (animator != null) animator.SetBool("isJump", true);
                return;
            }
        }

        if (jumpAction.WasReleasedThisFrame() && rb.linearVelocityY > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocityX, rb.linearVelocityY * 0.5f);
            if (animator != null) animator.SetBool("isJump", true);
        }
    }

    public bool TryDropThroughPlatform()
    {
        Vector2 checkPos = groundCheck != null ? (Vector2)groundCheck.position : (Vector2)transform.position;
        Collider2D[] hits = Physics2D.OverlapCircleAll(checkPos, 0.35f, groundLayer);
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
        if (animator != null) animator.SetBool("isDash", true);

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

        if (animator != null) animator.SetBool("isDash", false);

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

    #region Chain Slash Mode Logic

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
        if (animator != null) animator.SetBool("isInMode", true);
        FadeModeDimOverlay(modeDimAlpha);

        Time.timeScale = timeSpeed;
        Time.fixedDeltaTime = defaultFixedDeltaTime * Time.timeScale;

        drawnLinePoints.Clear();
        Vector3 startMousePos = GetMouseWorldPosition();
        drawnLinePoints.Add(startMousePos);

        if (lineRendererObject != null)
        {
            insLineRendererObj = Instantiate(lineRendererObject);
            insLineRendererCom = insLineRendererObj.GetComponent<LineRenderer>();
            if (insLineRendererCom != null)
            {
                insLineRendererCom.positionCount = 1;
                insLineRendererCom.SetPosition(0, startMousePos);
                if (drawGradient.colorKeys.Length > 0) insLineRendererCom.colorGradient = drawGradient;
            }
        }

        if (mouseTrailPrefab != null && GetActiveCamera() != null && Mouse.current != null)
        {
            insMouseTrail = Instantiate(mouseTrailPrefab, startMousePos, Quaternion.identity);
        }

        ShowMeterBar();

        if (activeMeterCoroutine != null) StopCoroutine(activeMeterCoroutine);
        activeMeterCoroutine = StartCoroutine(MeterDegeneration(1f, 0.005f));
    }

    private void ExitChainSlashMode()
    {
        IsInMode = false;
        if (animator != null) animator.SetBool("isInMode", false);
        FadeModeDimOverlay(0f);

        if (targetGroup != null && targetGroup.Targets.Count > 1)
        {
            targetGroup.Targets[1].Object = null;
        }

        if (activeMeterCoroutine != null) StopCoroutine(activeMeterCoroutine);

        activeMeterCoroutine = StartCoroutine(MeterRegenerate());

        if (insLineRendererObj != null)
        {
            LineRendererManager lineRendererManager = insLineRendererObj.GetComponent<LineRendererManager>();
            if (lineRendererManager != null && insLineRendererCom != null)
            {
                lineRendererManager.activeCoroutine = StartCoroutine(AnimateKillLine(insLineRendererCom));
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

        if (drawnLinePoints.Count == 0 || Vector3.Distance(drawnLinePoints[drawnLinePoints.Count - 1], currentMousePos) >= minPointDistance)
        {
            drawnLinePoints.Add(currentMousePos);
            insLineRendererCom.positionCount = drawnLinePoints.Count;
            insLineRendererCom.SetPositions(drawnLinePoints.ToArray());
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

        LineRendererManager lineRendererManager = insLineRendererObj.GetComponent<LineRendererManager>();
        if (lineRendererManager == null) return;

        if (!lineRendererManager.targets.Contains(enemyPos.gameObject))
        {
            lineRendererManager.targets.Add(enemyPos.gameObject);
            currentMeter = Mathf.Clamp(currentMeter - (maxMeter / 40f), 0f, maxMeter);
            if (modeMeterImage != null) modeMeterImage.fillAmount = currentMeter / maxMeter;

            int comboIndex = lineRendererManager.targets.Count - 1;
            PlayJuicyTargetSelectSFX(comboIndex);
            StartCoroutine(PunchTargetScaleRoutine(enemyPos));
        }
    }

    private IEnumerator PunchTargetScaleRoutine(Transform targetTransform)
    {
        if (targetTransform == null) yield break;

        Vector3 baseScale = targetTransform.localScale;
        float duration = 0.08f;
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
        List<Vector3> drawPoints = new List<Vector3>();

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

        if (hitstopDuration > 0f)
        {
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(hitstopDuration);
            Time.timeScale = 1f;
        }

        Vector2 slashVector = (origPoints[origPoints.Length - 1] - origPoints[0]).normalized;
        HashSet<EnemyBase> primaryEnemies = new HashSet<EnemyBase>();
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
        bool anyEnemyDiedOrHit = false;
        foreach (EnemyBase primaryEnemy in primaryEnemies)
        {
            if (primaryEnemy == null)
            {
                anyEnemyDiedOrHit = true;
                break;
            }

            if (primaryEnemy is ArmoredNinja armoredNinja)
            {
                if (armoredNinja.IsDead || armoredNinja.IsDefeated)
                {
                    anyEnemyDiedOrHit = true;
                    break;
                }
            }
            else if (primaryEnemy is LegendarySamurai boss)
            {
                if (boss.IsDead || boss.WasHitLandedInCurrentSlash)
                {
                    anyEnemyDiedOrHit = true;
                    break;
                }
            }
            else
            {
                // Normal enemy (Enemy, Ninja, ThrowerNinja, SuicideNinja, ChainEnemy)
                if (primaryEnemy.IsDead && aliveBeforeSlash.Contains(primaryEnemy))
                {
                    anyEnemyDiedOrHit = true;
                    break;
                }
            }
        }

        if (anyEnemyDiedOrHit)
        {
            PlaySFX(postSlashSFX);
        }

        if (flashGradient.colorKeys.Length > 0) lineRenderer.colorGradient = flashGradient;

        float orgWidth = lineRenderer.widthMultiplier;
        float flashDuration = 0.18f;
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
        List<Vector3> currentPoints = new List<Vector3>();

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
                lineRenderer.SetPositions(currentPoints.ToArray());
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

    #endregion

    #region Dynamic Grappling Hook System

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

        ConsumeMeter(maxMeter / 10f);

        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = defaultFixedDeltaTime;

        if (Mouse.current == null || GetActiveCamera() == null || grappleLine == null)
        {
            isThrowingHook = false;
            yield break;
        }

        Vector2 mouseWorldPos = GetMouseWorldPosition();
        Vector2 fireOrigin = transform.position;
        Vector2 dir = (mouseWorldPos - fireOrigin).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        RaycastHit2D rayHit = FindGrappleHit(fireOrigin, dir, out EnemyBase hitEnemy);
        grappleAnchorPoint = rayHit.collider != null ? rayHit.point : fireOrigin + (dir * maxGrappleRange);

        grappleLine.positionCount = ropeSegments;
        grappleLine.enabled = true;

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
            currentHookTip = Vector2.Lerp(fireOrigin, grappleAnchorPoint, progress);

            RenderProceduralRope(transform.position, currentHookTip, progress, true);
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
                float maxReelDuration = 0.65f;
                float reelTimer = 0f;
                float kickDistance = 1.55f;

                while (IsGrappling && reelTimer < maxReelDuration && hitEnemy != null && !hitEnemy.IsDead)
                {
                    reelTimer += Time.deltaTime;
                    Vector2 enemyCenter = GetEnemyGrapplePoint(hitEnemy);
                    Vector2 toPlayer = (Vector2)transform.position - enemyCenter;
                    float distToPlayer = toPlayer.magnitude;

                    // 2. Once the enemy is pulled right up close to the player, kick them off far!
                    if (distToPlayer <= kickDistance)
                    {
                        ForceExecuteGrappleKick(hitEnemy, dir);
                        yield break;
                    }

                    if (enemyRb != null)
                    {
                        Vector2 reelDir = toPlayer.normalized;
                        enemyRb.linearVelocity = reelDir * pullForce;
                    }

                    RenderProceduralRope(transform.position, enemyCenter, 1f, false);
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
                springJoint.connectedAnchor = grappleAnchorPoint;
                springJoint.distance = Vector2.Distance(transform.position, grappleAnchorPoint);
            }

            while (IsGrappling && springJoint != null && springJoint.enabled)
            {
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

                RenderProceduralRope(transform.position, grappleAnchorPoint, 1f, false);
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
        Collider2D col = enemy.GetComponent<Collider2D>();
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

        foreach (RaycastHit2D candidate in allHits)
        {
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
        bool isSwingingFast = IsGrappling && rb.linearVelocity.magnitude >= grappleKickSpeedThreshold;

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
        StartCoroutine(GrappleKickHitstopAndShakeRoutine());

        // Bounce player into an aerial vault, refresh mobility, and reduce mode meter by 1 target's cost
        float recoilX = -Mathf.Sign(kickDir.x) * moveSpeed * 0.45f;
        rb.linearVelocity = new Vector2(recoilX, grappleKickBounceForce);
        doubleJump = true;
        canDash = true;

        ConsumeMeter(maxMeter / 10f);
    }

    private IEnumerator GrappleKickHitstopAndShakeRoutine()
    {
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

    private void RenderProceduralRope(Vector2 startPos, Vector2 endPos, float launchProgress, bool isThrowing)
    {
        if (grappleLine == null || ropeSegments < 2) return;

        grappleLine.positionCount = ropeSegments;
        Vector2 dir = (endPos - startPos);
        Vector2 perpendicular = new Vector2(-dir.y, dir.x).normalized;

        for (int i = 0; i < ropeSegments; i++)
        {
            float t = (float)i / (ropeSegments - 1);
            Vector2 basePos = Vector2.Lerp(startPos, endPos, t);

            if (isThrowing)
            {
                float wave = Mathf.Sin(t * Mathf.PI * waveFrequency) * waveAmplitude * (1f - launchProgress);
                basePos += perpendicular * wave;
            }
            else
            {
                float sag = Mathf.Sin(t * Mathf.PI) * 0.15f;
                basePos -= Vector2.up * sag;
            }

            grappleLine.SetPosition(i, basePos);
        }
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
        if (grappleLine != null) grappleLine.enabled = false;
        if (springJoint != null) springJoint.enabled = false;
    }

    #endregion

    #region Dynamic UI Auto-Hide & Visuals

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
        foreach (CanvasGroup cg in meterCanvasGroups)
        {
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
            float pulseDuration = 0.15f;
            while (pulseTimer < pulseDuration)
            {
                pulseTimer += Time.deltaTime;
                float scale = Mathf.Lerp(1f, 1.15f, Mathf.Sin((pulseTimer / pulseDuration) * Mathf.PI));

                foreach (CanvasGroup cg in meterCanvasGroups)
                {
                    if (cg != null) cg.transform.localScale = Vector3.one * scale;
                }
                yield return null;
            }

            foreach (CanvasGroup cg in meterCanvasGroups)
            {
                if (cg != null) cg.transform.localScale = Vector3.one;
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
        yield return new WaitForSeconds(currentMeter == 0 ? 1f : 0.5f);

        while (currentMeter < maxMeter)
        {
            currentMeter = Mathf.Clamp(currentMeter + 1f, 0f, maxMeter);
            if (modeMeterImage != null) modeMeterImage.fillAmount = currentMeter / maxMeter;
            yield return new WaitForSeconds(0.03f);
        }

        HideMeterBarWithPulse();
        activeMeterCoroutine = null;
    }

    private IEnumerator MeterDegeneration(float amount, float interval)
    {
        while (currentMeter > 0f)
        {
            currentMeter = Mathf.Clamp(currentMeter - amount, 0f, maxMeter);
            if (modeMeterImage != null) modeMeterImage.fillAmount = currentMeter / maxMeter;
            yield return new WaitForSeconds(interval);
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
        yield return new WaitForSecondsRealtime(0.022f);
        if (shimmerAudioSource != null && targetSelectSFX != null)
        {
            shimmerAudioSource.pitch = Mathf.Min(shimmerPitch, 2.4f);
            shimmerAudioSource.PlayOneShot(targetSelectSFX, 0.35f);
        }
    }

    #endregion
}