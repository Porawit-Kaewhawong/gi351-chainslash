using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody2D), typeof(AudioSource))]
public class PlayerController : MonoBehaviour
{
    public static PlayerController Instance { get; private set; }

    [Header("Mouse Cursor Settings")]
    [SerializeField] private Texture2D cursorTexture;
    [SerializeField] private CursorMode cursorMode = CursorMode.Auto;

    [Header("Player Health System Settings")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float healthRegenDelay = 5f;            // Delay in seconds after taking a hit
    [SerializeField] private float healthRegenTickInterval = 0.05f;    // Regeneration speed (+1 per tick)
    [SerializeField] private Image healthBarImage;                  // Fill Image for Health
    [SerializeField] private GameObject healthBackgroundObject;     // Background / Frame for Health UI
    [SerializeField] private float healthFadeDuration = 0.25f;

    [Header("Player Movement Settings")]
    [SerializeField] private float moveSpeed = 7.5f;
    [SerializeField] private float jumpHeight = 15f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;

    [Header("Player Dashing Settings")]
    [SerializeField] private float dashingPower = 15f;
    [SerializeField] private float dashingTime = 0.2f;
    [SerializeField] private float dashingCooldown = 1f;

    [Header("Chain Slash Mode Settings")]
    [SerializeField] private float maxMeter = 100f;
    [SerializeField] private float timeSpeed = 0.25f;
    [SerializeField] private float minPointDistance = 0.1f;
    [SerializeField] private float slashDrawDuration = 0.18f;
    [SerializeField] private float slashDisappearDuration = 0.3f;
    [SerializeField] private AudioClip targetSelectSFX;
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

    [Header("Grappling Visual & Polish")]
    [SerializeField] private int ropeSegments = 20;
    [SerializeField] private float waveFrequency = 12f;
    [SerializeField] private float waveAmplitude = 0.6f;
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

    // Health state
    public float CurrentHealth { get; private set; }

    // Combat state
    public bool IsInMode { get; private set; }
    public bool IsGrappling { get; private set; }
    private bool isFocusing = false;
    private float currentMeter;
    private float defaultFixedDeltaTime;
    private List<Vector3> drawnLinePoints = new List<Vector3>();

    // Grapple state
    private Vector2 grappleAnchorPoint;
    private Vector2 currentHookTip;
    private GameObject insGrappleTargetIndicator;

    // Cinemachine Noise caching
    private float orgAmplitudeGain;
    private NoiseSettings orgNoiseSettings;

    // Active Coroutines
    private Coroutine activeMeterCoroutine;
    private Coroutine activeMeterFadeCoroutine;
    private Coroutine activeHealthRegenCoroutine;
    private Coroutine activeHealthFadeCoroutine;
    private Coroutine activeGrapplingCoroutine;

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
    private AudioSource audioSource;
    private TrailRenderer trailRenderer;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private SpringJoint2D springJoint;
    private LineRenderer grappleLine;
    private CinemachineCamera cinemachineCamera;
    private CinemachineBasicMultiChannelPerlin perlin;

    private int soundPlaying;
    private List<CanvasGroup> meterCanvasGroups = new List<CanvasGroup>();
    private List<CanvasGroup> healthCanvasGroups = new List<CanvasGroup>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        rb = GetComponent<Rigidbody2D>();
        audioSource = GetComponent<AudioSource>();
        trailRenderer = GetComponent<TrailRenderer>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        animator = GetComponentInChildren<Animator>();
        springJoint = GetComponent<SpringJoint2D>();
        grappleLine = GetComponent<LineRenderer>();

        cinemachineCamera = FindAnyObjectByType<CinemachineCamera>();
        if (cinemachineCamera != null)
        {
            perlin = cinemachineCamera.GetComponent<CinemachineBasicMultiChannelPerlin>();
            if (perlin != null)
            {
                orgNoiseSettings = perlin.NoiseProfile;
                orgAmplitudeGain = perlin.AmplitudeGain;
            }
        }

        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        dashAction = InputSystem.actions.FindAction("Dash");
        attackAction = InputSystem.actions.FindAction("Attack");
        grappleAction = InputSystem.actions.FindAction("Grapple");
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
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.isPressed)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            ChainEnemy.isExploding = false;
        }

        RecordDrawnLinePoints();
        UpdateMouseTrail();
        UpdateGrappleIndicator();

        if (isDashing) return;

        if (IsGrounded())
        {
            doubleJump = true;
            if (rb.linearVelocityY <= 0.25f && animator != null)
            {
                animator.SetBool("isJump", false);
            }
        }

        HandleJumpInput();

        if (dashAction != null && dashAction.triggered && canDash)
        {
            StartCoroutine(DashRoutine());
        }

        HandleCombatInput();
        HandleGrappleInput();
        Flip();
    }

    private void FixedUpdate()
    {
        if (isDashing) return;

        if (moveAction != null)
        {
            inputValue = moveAction.ReadValue<Vector2>();

            if (IsGrappling)
            {
                Vector2 ropeDir = (grappleAnchorPoint - (Vector2)transform.position).normalized;
                Vector2 perpendicular = new Vector2(-ropeDir.y, ropeDir.x);
                rb.AddForce(perpendicular * inputValue.x * swingForce, ForceMode2D.Force);
            }
            else
            {
                rb.linearVelocity = new Vector2(inputValue.x * moveSpeed, rb.linearVelocityY);

                if (animator != null)
                {
                    animator.SetBool("isRunning", Mathf.Abs(rb.linearVelocityX) > 0.05f);
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

            healthBarImage.fillAmount = CurrentHealth / maxHealth;
        }

        SetHealthAlpha(0f); // Hidden when health is full
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
        if (CurrentHealth <= 0f) return;

        CurrentHealth = Mathf.Clamp(CurrentHealth - amount, 0f, maxHealth);

        if (healthBarImage != null)
        {
            healthBarImage.fillAmount = CurrentHealth / maxHealth;
        }

        ShowHealthBar();

        if (CurrentHealth <= 0f)
        {
            Die();
            return;
        }

        if (activeHealthRegenCoroutine != null) StopCoroutine(activeHealthRegenCoroutine);
        activeHealthRegenCoroutine = StartCoroutine(HealthRegenRoutine());
    }

    private IEnumerator HealthRegenRoutine()
    {
        yield return new WaitForSeconds(healthRegenDelay);

        while (CurrentHealth < maxHealth)
        {
            CurrentHealth = Mathf.Clamp(CurrentHealth + 1f, 0f, maxHealth);

            if (healthBarImage != null)
            {
                healthBarImage.fillAmount = CurrentHealth / maxHealth;
            }

            yield return new WaitForSeconds(healthRegenTickInterval);
        }

        HideHealthBarWithPulse();
        activeHealthRegenCoroutine = null;
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
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        ChainEnemy.isExploding = false;
    }

    #endregion

    #region Movement & Jump Logic

    private void HandleJumpInput()
    {
        if (jumpAction == null) return;

        if (jumpAction.WasPressedThisFrame())
        {
            if (IsGrappling)
            {
                Vector2 boostDir = (rb.linearVelocity.normalized + Vector2.up * 0.5f).normalized;
                DetachGrappling();
                rb.linearVelocity = boostDir * jumpHeight * releaseBoost;
                if (animator != null) animator.SetBool("isJump", true);
                return;
            }

            if (IsGrounded())
            {
                rb.linearVelocity = new Vector2(rb.linearVelocityX, jumpHeight);
                if (animator != null) animator.SetBool("isJump", true);
            }
            else if (doubleJump)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocityX, jumpHeight);
                doubleJump = false;
                if (animator != null) animator.SetBool("isJump", true);
            }
        }

        if (jumpAction.WasReleasedThisFrame() && rb.linearVelocityY > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocityX, rb.linearVelocityY * 0.5f);
            if (animator != null) animator.SetBool("isJump", true);
        }
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

        if (animator != null) animator.SetBool("isDash", true);

        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0f;

        int dashDirection = spriteRenderer.flipX ? -1 : 1;
        rb.linearVelocity = new Vector2(dashDirection * dashingPower, 0f);

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
        if (inputValue.x < 0f) spriteRenderer.flipX = true;
        else if (inputValue.x > 0f) spriteRenderer.flipX = false;
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

        if (mouseTrailPrefab != null && Camera.main != null && Mouse.current != null)
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
        if (Camera.main == null || Mouse.current == null) return Vector3.zero;

        Vector3 mouseScreenPos = Mouse.current.position.ReadValue();
        mouseScreenPos.z = -Camera.main.transform.position.z;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);
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
            currentMeter -= maxMeter / 40f;
            PlaySFX(targetSelectSFX);
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
                lineRenderer.SetPositions(drawPoints.ToArray());
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

        for (int i = 0; i < lineRendererManager.targets.Count; i++)
        {
            GameObject targetObj = lineRendererManager.targets[i];
            if (targetObj == null) continue;

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

        PlaySFX(postSlashSFX);

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

        if (grappleAction.IsPressed() && currentMeter <= 0)
        {
            GrapplingHook();
            return;
        }

        if (grappleAction.IsPressed())
        {
            if (IsInMode) return;
            if (!IsGrappling && !isFocusing) GrapplingFocus();
        }
        else
        {
            if (!IsGrappling && isFocusing) GrapplingHook();
        }
    }

    private void GrapplingHook()
    {
        if (activeGrapplingCoroutine != null) StopCoroutine(activeGrapplingCoroutine);
        activeGrapplingCoroutine = StartCoroutine(AttachGrappleRoutine());
    }

    private IEnumerator AttachGrappleRoutine()
    {
        IsGrappling = true;
        isFocusing = false;

        if (activeMeterCoroutine != null) StopCoroutine(activeMeterCoroutine);
        activeMeterCoroutine = StartCoroutine(MeterRegenerate());

        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = defaultFixedDeltaTime;

        if (Mouse.current == null || Camera.main == null || grappleLine == null) yield break;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(mousePos);
        Vector2 fireOrigin = transform.position;
        Vector2 dir = (mouseWorldPos - fireOrigin).normalized;

        RaycastHit2D rayHit = Physics2D.Raycast(fireOrigin, dir, maxGrappleRange, grappleLayer);
        grappleAnchorPoint = rayHit.collider != null ? rayHit.point : fireOrigin + (dir * maxGrappleRange);

        grappleLine.positionCount = ropeSegments;
        grappleLine.enabled = true;

        float distance = Vector2.Distance(fireOrigin, grappleAnchorPoint);
        float duration = distance / grappleSpeed;
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / duration);
            currentHookTip = Vector2.Lerp(fireOrigin, grappleAnchorPoint, progress);

            RenderProceduralRope(transform.position, currentHookTip, progress, true);
            yield return null;
        }

        if (rayHit.collider != null)
        {
            PlaySFX(grappleHitSFX);

            if (rayHit.collider.TryGetComponent<EnemyBase>(out EnemyBase enemy))
            {
                Vector2 pullDir = ((Vector2)transform.position - rayHit.point).normalized;
                enemy.OnGrappled(pullDir * pullForce * 1.5f);
            }

            if (grappleImpactPrefab != null)
            {
                Instantiate(grappleImpactPrefab, grappleAnchorPoint, Quaternion.identity);
            }

            if (springJoint != null)
            {
                springJoint.enabled = true;
                springJoint.connectedAnchor = grappleAnchorPoint;
                springJoint.distance = Vector2.Distance(transform.position, grappleAnchorPoint);
            }

            while (IsGrappling && springJoint != null && springJoint.enabled)
            {
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

    public IEnumerator ShakingTrigger(float duration)
    {
        if (perlin != null)
        {
            perlin.NoiseProfile = noiseSettings;
            perlin.AmplitudeGain = 10f;

            yield return new WaitForSeconds(duration);

            perlin.NoiseProfile = orgNoiseSettings;
            perlin.AmplitudeGain = orgAmplitudeGain;
        }
    }

    public void PlaySFX(AudioClip audioClip)
    {
        if (audioClip != null && audioSource != null && soundPlaying < maxSoundPlaying)
        {
            audioSource.PlayOneShot(audioClip);
            soundPlaying++;
        }
    }

    #endregion
}