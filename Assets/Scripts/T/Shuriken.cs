using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Shuriken : MonoBehaviour
{
    #region Enums & Constants

    private const float MaxAutoAimDistSq = 20f * 20f;
    private const float MinAutoAimDistSq = 0.1f * 0.1f;

    #endregion

    #region Serialized Fields

    [Header("Shuriken Properties")]
    [SerializeField] private float speed = 11f;
    [SerializeField] private float damage = 12f;
    [SerializeField] private float spinSpeed = 720f;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private AudioClip hitSFX;

    [Header("Trail Settings")]
    [SerializeField] private bool enableTrail = true;
    [SerializeField] private float trailTime = 0.22f;
    [SerializeField] private float trailStartWidth = 0.35f;
    [SerializeField] private Color trailColor = new Color(0.85f, 0.95f, 1f, 0.85f);

    [Header("Friendly Fire / Death Ricochet")]
    [SerializeField] private bool canDamageEnemies = false;

    [Header("Reflect Settings")]
    [SerializeField] private GameObject parryVFXPrefab;
    [SerializeField] private AudioClip reflectSFX;
    [SerializeField] private Color reflectColor = new Color(0.2f, 0.95f, 1f, 1f);
    [SerializeField] private float reflectSpeedMultiplier = 1.5f;
    [SerializeField] private float reflectDamageMultiplier = 2.5f;
    [SerializeField] private float reflectRadius = 0.85f;
    [SerializeField] private float parryMeterRefund = 15f;
    [SerializeField] private float parryHitstopDuration = 0.045f;

    #endregion

    #region Public Properties

    public bool IsReflected => isReflected;

    #endregion

    #region Protected & Private State

    private Vector2 moveDirection = Vector2.right;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private TrailRenderer trailRenderer;
    private GameObject sourceEnemy;
    private float spawnTime;
    private bool hasHit = false;
    private bool isReflected = false;
    private Vector2 lastMouseWorld;
    private bool hasLastMousePos = false;
    private Vector3 baseScale = Vector3.one;
    private Coroutine punchScaleCoroutine;
    private Camera cachedCamera;

    private static Material sharedTrailMaterial;
    private static Sprite cachedSprite;
    private static GameObject cachedParryVFXPrefab;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        EnsureParryVFXPrefab();

        baseScale = transform.localScale;
        if (baseScale == Vector3.zero) baseScale = Vector3.one;

        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        spriteRenderer = GetComponent<SpriteRenderer>();

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        SetupTrail();
    }

    private void Start()
    {
        spawnTime = Time.time;
    }

    private void Update()
    {
        // Check mouse swipe reflection during Chain Slash mode
        CheckMouseReflect();

        // Continuous spin rotation
        transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);

        // Backup movement if no Rigidbody2D or non-dynamic
        if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic)
        {
            transform.position += (Vector3)(moveDirection * (speed * Time.deltaTime));
        }

        // Lifetime expiry
        if (Time.time >= spawnTime + lifetime && !hasHit)
        {
            Destroy(gameObject);
        }
    }

    private void FixedUpdate()
    {
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = moveDirection * speed;
        }
    }

    #endregion

    #region Movement & Combat

    public void SetCanDamageEnemies(bool enable, GameObject source = null)
    {
        canDamageEnemies = enable;
        sourceEnemy = source;

        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && enable && !isReflected)
        {
            spriteRenderer.color = new Color(1f, 0.65f, 0.25f, 1f);
        }

        SetupTrail();
    }

    public void Launch(Vector2 direction, float customSpeed = -1f, float customDamage = -1f)
    {
        moveDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        if (customSpeed > 0f) speed = customSpeed;
        if (customDamage > 0f) damage = customDamage;

        if (rb != null)
        {
            rb.linearVelocity = moveDirection * speed;
        }
    }

    private void CheckMouseReflect()
    {
        if (hasHit || isReflected) return;

        PlayerController player = PlayerController.Instance;
        if (player == null || !player.IsInMode)
        {
            hasLastMousePos = false;
            return;
        }

        if (cachedCamera == null) cachedCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        Camera cam = cachedCamera;
        if (cam == null || UnityEngine.InputSystem.Mouse.current == null) return;

        Vector3 screenPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        screenPos.z = -cam.transform.position.z;
        Vector2 currentMouse = cam.ScreenToWorldPoint(screenPos);

        Vector2 shurikenPos = transform.position;
        bool isHit = false;
        Vector2 swipeVector = Vector2.zero;
        float reflectRadiusSq = reflectRadius * reflectRadius;

        // Direct proximity check
        if ((shurikenPos - currentMouse).sqrMagnitude <= reflectRadiusSq)
        {
            isHit = true;
            if (hasLastMousePos)
            {
                swipeVector = currentMouse - lastMouseWorld;
            }
        }
        // Continuous line sweep check so fast mouse flicks are never missed
        else if (hasLastMousePos)
        {
            if (DistancePointToSegmentSq(shurikenPos, lastMouseWorld, currentMouse) <= reflectRadiusSq)
            {
                isHit = true;
                swipeVector = currentMouse - lastMouseWorld;
            }
        }

        lastMouseWorld = currentMouse;
        hasLastMousePos = true;

        if (isHit)
        {
            ReflectWithMouse(swipeVector);
        }
    }

    public void ReflectWithMouse(Vector2 swipeVector)
    {
        if (isReflected || hasHit) return;

        isReflected = true;
        canDamageEnemies = true;

        // Determine reflection direction:
        // 1. If player made a quick swipe with the mouse, reflect along the swipe direction
        // 2. Otherwise aim back towards the source enemy that threw it
        // 3. Otherwise reverse current flight direction
        Vector2 reflectDir;
        if (swipeVector.sqrMagnitude > 0.015f)
        {
            reflectDir = swipeVector.normalized;
        }
        else if (sourceEnemy != null)
        {
            reflectDir = ((Vector2)sourceEnemy.transform.position - (Vector2)transform.position).normalized;
        }
        else
        {
            reflectDir = -moveDirection;
        }

        // Auto-assist: gently guide towards the best enemy within a forward cone (~45 deg)
        reflectDir = FindBestEnemyDirection(reflectDir);

        // Clear source enemy so the reflected projectile can kill the enemy that originally threw it
        sourceEnemy = null;

        // Boost speed, damage, and spin
        speed = Mathf.Max(speed * reflectSpeedMultiplier, 16f);
        damage *= reflectDamageMultiplier;
        spinSpeed = -spinSpeed * 1.5f;
        spawnTime = Time.time; // Refresh lifetime

        // Re-launch along new trajectory
        Launch(reflectDir, speed, damage);

        // Visual feedback
        ApplyReflectVisuals();

        // Audio feedback
        PlayReflectAudio();

        // Punch scale effect
        if (punchScaleCoroutine != null) StopCoroutine(punchScaleCoroutine);
        punchScaleCoroutine = StartCoroutine(PunchScaleRoutine());

        // Player parry animation, meter refund, micro-hitstop & camera micro-shake
        if (PlayerController.Instance != null)
        {
            PlayerController.Instance.OnParryTriggered();
            PlayerController.Instance.RefundMeter(parryMeterRefund);
            PlayerController.Instance.TriggerBriefHitstop(parryHitstopDuration);
            PlayerController.Instance.TriggerCameraShake(0.08f);
        }
    }

    private Vector2 FindBestEnemyDirection(Vector2 defaultDir)
    {
        var activeList = (GameManager.Instance != null) ? GameManager.Instance.ActiveEnemies : null;
        int activeCount = (activeList != null && activeList.Count > 0) ? activeList.Count : 0;
        EnemyBase[] allEnemies = (activeCount == 0) ? FindObjectsByType<EnemyBase>(FindObjectsSortMode.None) : null;
        int totalCandidates = (activeCount > 0) ? activeCount : (allEnemies != null ? allEnemies.Length : 0);
        EnemyBase bestTarget = null;
        float bestScore = -1f;

        Vector2 currentPos = transform.position;

        for (int i = 0; i < totalCandidates; i++)
        {
            EnemyBase enemy = (activeCount > 0)
                ? (activeList[i] != null ? activeList[i].GetComponent<EnemyBase>() : null)
                : allEnemies[i];

            if (enemy == null || enemy.IsDead || enemy is ArmoredNinjaWeakPoint) continue;

            Vector2 toEnemy = (Vector2)enemy.transform.position - currentPos;
            float distSq = toEnemy.sqrMagnitude;
            if (distSq < MinAutoAimDistSq || distSq > MaxAutoAimDistSq) continue;

            float dist = Mathf.Sqrt(distSq);
            Vector2 dirToEnemy = toEnemy / dist;
            float dot = Vector2.Dot(defaultDir, dirToEnemy);

            // Enemy must be generally in the direction of the reflection (> 40 degrees cone)
            if (dot > 0.65f)
            {
                float score = dot / Mathf.Max(1f, dist * 0.4f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = enemy;
                }
            }
        }

        if (bestTarget != null)
        {
            return ((Vector2)bestTarget.transform.position - currentPos).normalized;
        }

        return defaultDir;
    }

    #endregion

    #region Collision & Physics

    private void OnMouseEnter()
    {
        if (PlayerController.Instance != null && PlayerController.Instance.IsInMode)
        {
            ReflectWithMouse(Vector2.zero);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleHit(other.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HandleHit(collision.gameObject);
    }

    private void HandleHit(GameObject other)
    {
        if (hasHit || other == null) return;

        // Reflected shuriken ignores player completely (cannot hurt the player)
        if (isReflected && (other.GetComponentInParent<PlayerController>() != null || other.CompareTag("Player")))
        {
            return;
        }

        // Check Armored Ninja hit
        ArmoredNinja armoredNinja = other.GetComponentInParent<ArmoredNinja>();
        if (armoredNinja != null || other.GetComponentInParent<ArmoredNinjaWeakPoint>() != null)
        {
            hasHit = true;
            if (armoredNinja != null)
            {
                if (isReflected || canDamageEnemies)
                {
                    armoredNinja.TakeShurikenDamage(moveDirection);
                }
                else
                {
                    armoredNinja.DeflectProjectile(transform.position);
                }
            }
            PlayHitFeedback(transform.position);
            Destroy(gameObject);
            return;
        }

        // Check Legendary Samurai boss hit
        LegendarySamurai samuraiBoss = other.GetComponentInParent<LegendarySamurai>();
        if (samuraiBoss != null || other.GetComponentInParent<BossTargetPoint>() != null)
        {
            if (samuraiBoss != null && (isReflected || canDamageEnemies))
            {
                hasHit = true;
                samuraiBoss.TakeShurikenHit(moveDirection);
                PlayHitFeedback(transform.position);
                Destroy(gameObject);
                return;
            }
        }

        // Check if this projectile is configured to damage other enemies (e.g. ThrowerNinja death shurikens or reflected shurikens)
        if (canDamageEnemies || isReflected)
        {
            // Ignore the source enemy that spawned it (unless reflected by player!)
            if (!isReflected && sourceEnemy != null && (other == sourceEnemy || other.transform.IsChildOf(sourceEnemy.transform)))
            {
                return;
            }

            EnemyBase hitEnemy = other.GetComponentInParent<EnemyBase>();
            if (hitEnemy != null && !hitEnemy.IsDead && (isReflected || sourceEnemy == null || hitEnemy.gameObject != sourceEnemy))
            {
                hasHit = true;
                if (hitEnemy is LegendarySamurai bossTarget)
                {
                    bossTarget.TakeShurikenHit(moveDirection);
                }
                else
                {
                    hitEnemy.OnDeath(moveDirection);
                }
                PlayHitFeedback(transform.position);
                Destroy(gameObject);
                return;
            }
        }
        else
        {
            // Normal unreflected shurikens ignore other enemies
            if (other.GetComponentInParent<EnemyBase>() != null) return;
        }

        // Player hit (only unreflected shurikens damage the player)
        if (!isReflected)
        {
            if (other.TryGetComponent<PlayerController>(out PlayerController player) ||
                (other.GetComponentInParent<PlayerController>() is PlayerController parentPlayer && (player = parentPlayer) != null))
            {
                hasHit = true;
                player.TakeDamage(damage);
                PlayHitFeedback(transform.position);
                Destroy(gameObject);
                return;
            }
        }

        // Ground / Wall hit
        if (groundLayer != 0 && ((1 << other.layer) & groundLayer) != 0)
        {
            hasHit = true;
            PlayHitFeedback(transform.position);
            Destroy(gameObject);
            return;
        }
    }

    #endregion

    #region Visuals, Audio & Helpers

    public void SetupTrail()
    {
        if (!enableTrail) return;

        if (trailRenderer == null && !TryGetComponent<TrailRenderer>(out trailRenderer))
        {
            trailRenderer = gameObject.AddComponent<TrailRenderer>();
        }

        trailRenderer.time = trailTime;
        trailRenderer.startWidth = trailStartWidth;
        trailRenderer.endWidth = 0f;
        trailRenderer.minVertexDistance = 0.04f;
        trailRenderer.autodestruct = false;

        if (trailRenderer.material == null || trailRenderer.material.shader.name == "Standard")
        {
            Shader spriteShader = Shader.Find("Sprites/Default");
            if (spriteShader != null)
            {
                if (sharedTrailMaterial == null) sharedTrailMaterial = new Material(spriteShader);
                trailRenderer.material = sharedTrailMaterial;
            }
        }

        Color headColor;
        Color tailColor;

        if (isReflected)
        {
            headColor = reflectColor;
            tailColor = new Color(reflectColor.r * 0.4f, reflectColor.g * 0.8f, 1f, 0f);
        }
        else if (canDamageEnemies)
        {
            headColor = new Color(1f, 0.5f, 0.1f, 0.95f);
            tailColor = new Color(1f, 0.15f, 0.05f, 0f);
        }
        else
        {
            headColor = trailColor;
            tailColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
        }

        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(headColor, 0f), new GradientColorKey(tailColor, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(headColor.a, 0f), new GradientAlphaKey(0f, 1f) }
        );
        trailRenderer.colorGradient = grad;

        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            trailRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            trailRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;
        }
    }

    private void ApplyReflectVisuals()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.color = reflectColor;
        }

        if (trailRenderer != null)
        {
            Gradient grad = new Gradient();
            Color headColor = reflectColor;
            Color tailColor = new Color(reflectColor.r * 0.4f, reflectColor.g * 0.8f, 1f, 0f);

            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(headColor, 0f), new GradientColorKey(tailColor, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );

            trailRenderer.colorGradient = grad;
            trailRenderer.startWidth = trailStartWidth * 1.4f;
        }

        if (hitEffectPrefab != null)
        {
            Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
        }

        SpawnParryVFX();
    }

    private void EnsureParryVFXPrefab()
    {
        if (parryVFXPrefab != null)
        {
            cachedParryVFXPrefab = parryVFXPrefab;
            return;
        }

        if (cachedParryVFXPrefab != null)
        {
            parryVFXPrefab = cachedParryVFXPrefab;
            return;
        }

#if UNITY_EDITOR
        cachedParryVFXPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/ParryVFX.prefab");
        parryVFXPrefab = cachedParryVFXPrefab;
#endif
    }

    private void SpawnParryVFX()
    {
        ParryImpactBurstVFX.Spawn(transform.position, moveDirection, reflectColor);

        EnsureParryVFXPrefab();
        if (parryVFXPrefab == null) return;

        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        Quaternion rot = Quaternion.Euler(0f, 0f, angle);
        GameObject vfx = Instantiate(parryVFXPrefab, transform.position, rot);

        SpriteRenderer vfxSr = vfx.GetComponent<SpriteRenderer>();
        if (vfxSr != null && vfxSr.sortingOrder < 15)
        {
            vfxSr.sortingOrder = 15;
        }

        float clipDuration = 0.34f;
        Animator anim = vfx.GetComponent<Animator>();
        if (anim != null)
        {
            anim.updateMode = AnimatorUpdateMode.UnscaledTime;
            if (anim.runtimeAnimatorController != null && anim.runtimeAnimatorController.animationClips.Length > 0)
            {
                AnimationClip clip = anim.runtimeAnimatorController.animationClips[0];
                if (clip != null && clip.length > 0.05f)
                {
                    clipDuration = clip.length;
                }
            }
        }

        ParryVFXCleanup cleanup = vfx.AddComponent<ParryVFXCleanup>();
        cleanup.Initialize(clipDuration);
    }

    private sealed class ParryImpactBurstVFX : MonoBehaviour
    {
        private SpriteRenderer ringSpriteRenderer;
        private Transform[] sparkTransforms;
        private SpriteRenderer[] sparkRenderers;
        private Vector2[] sparkPositions;
        private Vector2[] sparkVelocities;
        private Color ringColor;
        private float elapsed;
        private const float TotalDuration = 0.24f;
        private static Sprite[] cachedPixelRingFrames;

        public static void Spawn(Vector3 position, Vector2 reflectDir, Color tint)
        {
            GameObject root = new GameObject("ParryImpactBurstVFX");
            root.transform.position = EnemyBase.SnapToPixelGrid(position);
            ParryImpactBurstVFX vfx = root.AddComponent<ParryImpactBurstVFX>();
            vfx.Initialize(reflectDir, tint);
        }

        private static Sprite[] GetOrCreatePixelRingFrames()
        {
            if (cachedPixelRingFrames != null && cachedPixelRingFrames.Length == 4 && cachedPixelRingFrames[0] != null)
            {
                return cachedPixelRingFrames;
            }

            const int size = 24;
            const float center = 11.5f;
            float[] radii = { 3.2f, 6.2f, 8.8f, 10.6f };
            cachedPixelRingFrames = new Sprite[4];
            Color clear = new Color(0f, 0f, 0f, 0f);

            for (int f = 0; f < 4; f++)
            {
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                Color[] px = new Color[size * size];
                float r = radii[f];
                float thickness = (f < 2) ? 1.15f : 0.75f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                        bool onRing = Mathf.Abs(dist - r) <= thickness;
                        // Frame 3 breaks into a dashed 8-bit pixel ring
                        if (onRing && f == 3 && ((x + y) % 2 == 0))
                        {
                            onRing = false;
                        }
                        px[y * size + x] = onRing ? Color.white : clear;
                    }
                }

                tex.SetPixels(px);
                tex.Apply();
                cachedPixelRingFrames[f] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
            }

            return cachedPixelRingFrames;
        }

        private void Initialize(Vector2 reflectDir, Color tint)
        {
            ringColor = tint;
            if (reflectDir.sqrMagnitude < 0.001f) reflectDir = Vector2.right;
            else reflectDir.Normalize();

            Sprite[] ringFrames = GetOrCreatePixelRingFrames();
            ringSpriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            ringSpriteRenderer.sprite = ringFrames[0];
            ringSpriteRenderer.sortingOrder = 16;
            ringSpriteRenderer.color = ringColor;

            int sparkCount = 8;
            sparkTransforms = new Transform[sparkCount];
            sparkRenderers = new SpriteRenderer[sparkCount];
            sparkPositions = new Vector2[sparkCount];
            sparkVelocities = new Vector2[sparkCount];

            float baseAngle = Mathf.Atan2(reflectDir.y, reflectDir.x) * Mathf.Rad2Deg;

            for (int i = 0; i < sparkCount; i++)
            {
                GameObject spark = new GameObject("PixelParrySpark");
                spark.transform.SetParent(transform, false);
                spark.transform.localScale = Vector3.one;
                spark.transform.localRotation = Quaternion.identity;

                float spread = Random.Range(-50f, 50f);
                float rad = (baseAngle + spread) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                sparkVelocities[i] = dir * Random.Range(4.8f, 10.5f);

                SpriteRenderer sr = spark.AddComponent<SpriteRenderer>();
                sr.sprite = EnemyBase.GetSharedPixelBlockSprite((i % 2 == 0) ? 2 : 1);
                sr.sortingOrder = 17;
                sr.color = (i % 2 == 0) ? new Color(0.85f, 1f, 1f, 1f) : new Color(1f, 0.92f, 0.35f, 1f);

                sparkTransforms[i] = spark.transform;
                sparkRenderers[i] = sr;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime > 0f ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : Time.deltaTime;
            elapsed += dt;
            float t = Mathf.Clamp01(elapsed / TotalDuration);

            if (ringSpriteRenderer != null)
            {
                Sprite[] ringFrames = GetOrCreatePixelRingFrames();
                int frameIdx = Mathf.Clamp(Mathf.FloorToInt(t * 4f), 0, 3);
                ringSpriteRenderer.sprite = ringFrames[frameIdx];
                Color rc = ringColor;
                rc.a = 1f - EnemyBase.QuantizeStep(t, 4);
                ringSpriteRenderer.color = rc;
            }

            float quantAlpha = 1f - EnemyBase.QuantizeStep(t, 4);
            if (sparkTransforms != null)
            {
                for (int i = 0; i < sparkTransforms.Length; i++)
                {
                    if (sparkTransforms[i] == null) continue;
                    sparkPositions[i] += sparkVelocities[i] * dt;
                    sparkVelocities[i] *= (1f - 8f * dt);
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
    }

    private sealed class ParryVFXCleanup : MonoBehaviour
    {
        private float remainingUnscaledTime = 0.34f;

        public void Initialize(float duration)
        {
            remainingUnscaledTime = Mathf.Max(0.05f, duration);
        }

        private void Update()
        {
            remainingUnscaledTime -= Time.unscaledDeltaTime;
            if (remainingUnscaledTime <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }

    private void PlayReflectAudio()
    {
        AudioClip clipToPlay = reflectSFX != null ? reflectSFX : hitSFX;

        if (PlayerController.Instance != null && clipToPlay != null)
        {
            PlayerController.Instance.PlayPitchedSFX(clipToPlay, Random.Range(1.15f, 1.35f), 1f);
        }
    }

    private IEnumerator PunchScaleRoutine()
    {
        float elapsed = 0f;
        const float duration = 0.15f;
        Vector3 targetScale = baseScale * 1.5f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            transform.localScale = Vector3.Lerp(targetScale, baseScale, t);
            yield return null;
        }

        transform.localScale = baseScale;
        punchScaleCoroutine = null;
    }

    private static float DistancePointToSegmentSq(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 0.0001f) return (point - a).sqrMagnitude;

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lenSq);
        Vector2 projection = a + t * ab;
        return (point - projection).sqrMagnitude;
    }

    private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        return Mathf.Sqrt(DistancePointToSegmentSq(point, a, b));
    }

    private void PlayHitFeedback(Vector3 position)
    {
        if (hitEffectPrefab != null)
        {
            Instantiate(hitEffectPrefab, position, Quaternion.identity);
        }

        if (hitSFX != null && PlayerController.Instance != null)
        {
            PlayerController.Instance.PlaySFX(hitSFX);
        }
    }

    public static Sprite GetOrCreateShurikenSprite()
    {
        if (cachedSprite != null) return cachedSprite;

        const int size = 16;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0);
        Color steel = new Color(0.85f, 0.88f, 0.95f, 1f);
        Color darkSteel = new Color(0.45f, 0.48f, 0.55f, 1f);
        Color core = new Color(0.2f, 0.2f, 0.25f, 1f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, clear);
            }
        }

        const int center = size / 2;

        // Central hub
        for (int y = center - 1; y <= center; y++)
        {
            for (int x = center - 1; x <= center; x++)
            {
                tex.SetPixel(x, y, core);
            }
        }

        // 4 sharp blade tips
        for (int i = 1; i <= 6; i++)
        {
            // Up blade
            tex.SetPixel(center - 1, center + i - 1, steel);
            tex.SetPixel(center, center + i - 1, darkSteel);

            // Down blade
            tex.SetPixel(center - 1, center - 1 - i, steel);
            tex.SetPixel(center, center - 1 - i, darkSteel);

            // Right blade
            tex.SetPixel(center + i, center - 1, steel);
            tex.SetPixel(center + i, center, darkSteel);

            // Left blade
            tex.SetPixel(center - 1 - i, center, steel);
            tex.SetPixel(center - 1 - i, center - 1, darkSteel);
        }

        tex.Apply();
        cachedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        return cachedSprite;
    }

    public static GameObject CreateDefaultShuriken(Vector2 position, Vector2 direction, float speed, float damage, LayerMask groundLayer)
    {
        GameObject obj = new GameObject("Shuriken");
        obj.transform.position = position;

        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateShurikenSprite();
        sr.color = Color.white;
        sr.sortingOrder = 5;

        CircleCollider2D col = obj.AddComponent<CircleCollider2D>();
        col.radius = 0.35f;
        col.isTrigger = true;

        Rigidbody2D rb = obj.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        Shuriken shuriken = obj.AddComponent<Shuriken>();
        shuriken.groundLayer = groundLayer;
        shuriken.Launch(direction, speed, damage);

        return obj;
    }

    #endregion
}
