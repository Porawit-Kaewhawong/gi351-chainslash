using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// Chain & Slash themed dimensional Space-Tear portal.
/// Can be triggered by Chain-Slashing through it (ISlashable), Grapple-Hooking straight into it,
/// or walking/dashing into it. Executes a pixel-art dimensional screen cut & split transition
/// into the next stage, followed by a Space-Tear emergence burst on arrival.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class PortalNextLevel : MonoBehaviour, IPointerEnterHandler, ISlashable
{
    #region Types

    private enum TransitionTriggerType
    {
        WalkIn,
        Slash,
        Grapple
    }

    #endregion

    #region Serialized Fields

    [Header("Next Level Settings")]
    [SerializeField] private float teleportDelay = 0.03f;

    [Header("Portal Audio Settings")]
    [Tooltip("SwordSlashSFX played at Phase A when the dimensional katana cut strikes.")]
    [SerializeField] private AudioClip slashCutSFX;
    [Tooltip("EnteringPortalSFX played in sequence at Phase B when the screen splits open into the void.")]
    [SerializeField] private AudioClip portalEnterSFX;
    [Tooltip("Metallic chain latch SFX when grapple hits or chains snap.")]
    [SerializeField] private AudioClip chainSnapSFX;
    [SerializeField] [Range(0f, 1f)] private float portalSFXVolume = 1f;

    [Header("Katana Space-Tear Visuals")]
    [SerializeField] private bool useMonochromeSpaceTear = true;
    [SerializeField] private bool animateSpawnScale = true;
    [SerializeField] private float spawnAnimationDuration = 0.28f;
    [SerializeField] private bool animateIdlePulse = true;
    [SerializeField] private float idlePulseSpeed = 3.5f;
    [SerializeField] private float idlePulseAmount = 0.045f;
    [SerializeField] private float targetJitterStrength = 0.04f;

    [Header("Dimensional Screen Slice Transition")]
    [SerializeField] private int virtualPixelHeight = 180;
    [SerializeField] private float zoomPunchDuration = 0.22f;
    [SerializeField] private float zoomInOrthoSize = 2.6f;
    [SerializeField] private float katanaCutDuration = 0.09f;
    [SerializeField] private float screenSplitDuration = 0.30f;
    [SerializeField] private float blackScreenHoldDuration = 0.06f;
    [SerializeField] private float nextStageFadeOutDuration = 0.24f;
    [SerializeField] private float modeExitGracePeriod = 0.35f;

    #endregion

    #region Private State & Shared Pixel Caches

    private static readonly int IsRunningHash = Animator.StringToHash("isRunning");
    private static readonly int IsDashHash = Animator.StringToHash("isDash");

    private static Sprite[] cachedRiftFrames;
    private static Sprite cachedChainLinkLight;
    private static Sprite cachedChainLinkDark;

    private bool isTeleporting;
    private bool spawnAnimFinished;
    private bool isTarget;
    private bool wasInMode;
    private bool hasLastMousePos;
    private Vector2 lastMousePos;

    private Vector3 targetScale = Vector3.one;
    private Vector3 restingVisualCompensation = Vector3.one;
    private Vector3 restingTearLocalPos = Vector3.zero;
    private Transform cachedTransform;
    private Transform visualRoot;
    private Transform tearTransform;
    private SpriteRenderer tearRenderer;
    private PixelSpriteEffect tearPixelEffect;
    private readonly List<SpriteRenderer> chainAnchorRenderers = new List<SpriteRenderer>();
    private readonly List<Transform> ambientSparkTransforms = new List<Transform>();
    private readonly List<SpriteRenderer> ambientSparkRenderers = new List<SpriteRenderer>();
    private readonly float[] sparkPhases = new float[5];
    private readonly float[] sparkOffsetsX = new float[5];

    private Collider2D portalCollider;
    private AudioSource portalAudioSource;
    private Camera cachedCamera;
    private Coroutine fallbackResetTargetCoroutine;

    public bool CanInteractWithPortal => !isTeleporting && enabled && gameObject.activeInHierarchy;
    public bool IsTeleporting => isTeleporting;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        cachedTransform = transform;
        targetScale = cachedTransform.localScale;
        if (targetScale.sqrMagnitude < 0.0001f)
        {
            targetScale = Vector3.one;
        }

        Vector3 restingLossy = cachedTransform.lossyScale;
        restingVisualCompensation = new Vector3(
            Mathf.Abs(restingLossy.x) > 0.01f ? 1f / Mathf.Abs(restingLossy.x) : 1f,
            Mathf.Abs(restingLossy.y) > 0.01f ? 1f / Mathf.Abs(restingLossy.y) : 1f,
            1f
        );

        portalCollider = GetComponent<Collider2D>();
        if (portalCollider != null)
        {
            portalCollider.isTrigger = true;
        }

        portalAudioSource = GetComponent<AudioSource>();
        if (portalAudioSource == null)
        {
            portalAudioSource = gameObject.AddComponent<AudioSource>();
            portalAudioSource.playOnAwake = false;
            portalAudioSource.spatialBlend = 0f;
        }

        if (useMonochromeSpaceTear)
        {
            SetupSpaceTearVisuals();
        }
    }

    private void OnEnable()
    {
        if (isTeleporting) return;

        if (animateSpawnScale)
        {
            spawnAnimFinished = false;
            StartCoroutine(SpawnScaleRoutine());
        }
        else
        {
            spawnAnimFinished = true;
            cachedTransform.localScale = targetScale;
        }
    }

    private void Update()
    {
        if (useMonochromeSpaceTear && tearRenderer != null && tearRenderer.enabled)
        {
            UpdateSpaceTearAnimation();
        }

        if (isTeleporting) return;

        if (spawnAnimFinished && animateIdlePulse)
        {
            float steppedWave = Mathf.Round(Mathf.Sin(Time.unscaledTime * idlePulseSpeed) * 2f) * 0.5f;
            float pulse = 1f + steppedWave * idlePulseAmount;
            cachedTransform.localScale = targetScale * pulse;
        }

        PlayerController player = PlayerController.Instance;
        bool inMode = player != null && player.IsInMode;
        bool inGrappleFocus = player != null && player.IsFocusing;

        if (inMode && !isTarget)
        {
            CheckMouseSelection();
        }

        if (inMode && isTarget)
        {
            ApplyTargetFeedback(true);
        }
        else if (inGrappleFocus && !isTarget)
        {
            UpdateFocusHoverFeedback(player);
        }
        else if (!inMode && !inGrappleFocus && !isTarget)
        {
            ResetTargetVisuals();
        }

        if (wasInMode && !inMode && isTarget)
        {
            if (fallbackResetTargetCoroutine != null)
            {
                StopCoroutine(fallbackResetTargetCoroutine);
            }
            fallbackResetTargetCoroutine = StartCoroutine(ResetTargetAfterModeExitGraceRoutine());
        }

        wasInMode = inMode;
    }

    #endregion

    #region Public Entry Points (Slash / Grapple / Walk-In)

    public Vector2 GetGrappleAnchorPosition()
    {
        if (portalCollider != null)
        {
            return portalCollider.bounds.center;
        }
        return cachedTransform != null ? (Vector2)cachedTransform.position : (Vector2)transform.position;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        TrySelectAsSlashTarget();
    }

    public void OnSlashed(Vector2 slashVector)
    {
        if (!CanInteractWithPortal) return;

        if (fallbackResetTargetCoroutine != null)
        {
            StopCoroutine(fallbackResetTargetCoroutine);
            fallbackResetTargetCoroutine = null;
        }

        PlayerController player = PlayerController.Instance != null
            ? PlayerController.Instance
            : FindFirstObjectByType<PlayerController>();

        Vector2 cutDir = slashVector.sqrMagnitude > 0.01f
            ? slashVector.normalized
            : new Vector2(1f, 0.36f).normalized;

        StartPortalTransition(player, TransitionTriggerType.Slash, cutDir);
    }

    public void OnGrappleLatched()
    {
        if (isTeleporting) return;

        PlayClip(chainSnapSFX != null ? chainSnapSFX : GetFallbackGrappleSFX(), 0.9f);
        if (tearPixelEffect != null)
        {
            tearPixelEffect.SetOutline(true, Color.white, 2f, PixelSpriteEffect.OutlineMode.Both);
            tearPixelEffect.Flash(0.09f, Color.white);
        }
        PortalPixelSparkVFX.SpawnBurst(GetGrappleAnchorPosition(), 8, 2.8f, Color.white);
    }

    public void TriggerFromGrapple(PlayerController player, Vector2 approachDir)
    {
        if (!CanInteractWithPortal) return;

        Vector2 cutDir = approachDir.sqrMagnitude > 0.01f
            ? approachDir.normalized
            : new Vector2(1f, 0.36f).normalized;

        StartPortalTransition(player, TransitionTriggerType.Grapple, cutDir);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryTriggerFromContact(other.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryTriggerFromContact(collision.gameObject);
    }

    private void TryTriggerFromContact(GameObject hitObject)
    {
        if (!CanInteractWithPortal || hitObject == null) return;

        PlayerController player = hitObject.GetComponentInParent<PlayerController>();
        if (player == null && !hitObject.CompareTag("Player")) return;

        if (player == null)
        {
            player = PlayerController.Instance != null
                ? PlayerController.Instance
                : FindFirstObjectByType<PlayerController>();
        }

        if (player != null && player.IsDead) return;

        Vector2 cutDir = new Vector2(1f, 0.36f).normalized;
        if (player != null)
        {
            Rigidbody2D prb = player.GetComponent<Rigidbody2D>();
            if (prb != null && prb.linearVelocity.sqrMagnitude > 0.25f)
            {
                cutDir = prb.linearVelocity.normalized;
            }
            else if (player.MainSpriteRenderer != null && player.MainSpriteRenderer.flipX)
            {
                cutDir = new Vector2(-1f, 0.36f).normalized;
            }
        }

        StartPortalTransition(player, TransitionTriggerType.WalkIn, cutDir);
    }

    #endregion

    #region Katana Space-Tear Visuals & Pixel Procedural Sprites

    private void SetupSpaceTearVisuals()
    {
        // Hide any legacy blue oval SpriteRenderer cleanly while preserving prefab hierarchy
        SpriteRenderer[] existingRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < existingRenderers.Length; i++)
        {
            if (existingRenderers[i] != null)
            {
                existingRenderers[i].enabled = false;
            }
        }

        GameObject rootObj = new GameObject("SpaceTearVisualRoot");
        rootObj.transform.SetParent(cachedTransform, false);
        rootObj.transform.localPosition = Vector3.zero;
        rootObj.transform.localRotation = Quaternion.identity;
        rootObj.transform.localScale = restingVisualCompensation;
        visualRoot = rootObj.transform;

        // 1. Main Katana Space-Tear sprite
        GameObject tearObj = new GameObject("KatanaSpaceTear");
        tearObj.transform.SetParent(visualRoot, false);
        tearObj.transform.localPosition = Vector3.zero;
        tearObj.transform.localRotation = Quaternion.identity;
        tearObj.transform.localScale = Vector3.one;
        tearTransform = tearObj.transform;
        restingTearLocalPos = Vector3.zero;

        Sprite[] frames = GetOrCreateRiftFrames();
        tearRenderer = tearObj.AddComponent<SpriteRenderer>();
        tearRenderer.sprite = frames[0];
        tearRenderer.sortingOrder = 6;
        tearRenderer.color = Color.white;

        tearPixelEffect = tearObj.AddComponent<PixelSpriteEffect>();

        // 2. Top & bottom pixel binding chains framing the rift
        EnsureChainAnchorVisuals();

        // 3. Ambient rising white pixel sparks
        Sprite spark1px = EnemyBase.GetSharedPixelBlockSprite(1);
        Sprite spark2px = EnemyBase.GetSharedPixelBlockSprite(2);
        for (int i = 0; i < sparkPhases.Length; i++)
        {
            sparkPhases[i] = i / (float)sparkPhases.Length;
            sparkOffsetsX[i] = Random.Range(-0.42f, 0.42f);

            GameObject sparkObj = new GameObject("RiftEmber_" + i);
            sparkObj.transform.SetParent(visualRoot, false);
            SpriteRenderer sr = sparkObj.AddComponent<SpriteRenderer>();
            sr.sprite = (i % 2 == 0) ? spark2px : spark1px;
            sr.sortingOrder = 7;
            sr.color = Color.white;
            ambientSparkTransforms.Add(sparkObj.transform);
            ambientSparkRenderers.Add(sr);
        }
    }

    private void EnsureChainAnchorVisuals()
    {
        chainAnchorRenderers.Clear();
        Sprite lightLink = GetOrCreateChainLinkSprite(true);
        Sprite darkLink = GetOrCreateChainLinkSprite(false);

        // Two short pixel chain strands at the top & bottom tips pinning the tear
        int[] yEndpoints = { 16, -16 };
        for (int endIdx = 0; endIdx < yEndpoints.Length; endIdx++)
        {
            int baseY = yEndpoints[endIdx];
            int dirY = baseY > 0 ? 1 : -1;
            for (int link = 0; link < 4; link++)
            {
                GameObject linkObj = new GameObject("RiftChain_" + endIdx + "_" + link);
                linkObj.transform.SetParent(visualRoot, false);
                Vector3 pos = new Vector3((link % 2 == 0 ? 0.0625f : -0.0625f), (baseY + dirY * link * 3) / 16f, 0f);
                linkObj.transform.localPosition = EnemyBase.SnapToPixelGrid(pos, 16f);

                SpriteRenderer sr = linkObj.AddComponent<SpriteRenderer>();
                sr.sprite = (link % 2 == 0) ? lightLink : darkLink;
                sr.sortingOrder = 7;
                sr.color = Color.white;
                chainAnchorRenderers.Add(sr);
            }
        }
    }

    private void UpdateSpaceTearAnimation()
    {
        if (tearRenderer == null) return;

        Sprite[] frames = GetOrCreateRiftFrames();
        int frameIdx = Mathf.FloorToInt(Time.unscaledTime * 9f) % frames.Length;
        tearRenderer.sprite = frames[frameIdx];

        float dt = Time.unscaledDeltaTime > 0f ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0.016f;
        for (int i = 0; i < ambientSparkTransforms.Count; i++)
        {
            if (ambientSparkTransforms[i] == null) continue;
            sparkPhases[i] += dt * (0.65f + (i % 3) * 0.22f);
            if (sparkPhases[i] >= 1f)
            {
                sparkPhases[i] -= 1f;
                sparkOffsetsX[i] = Random.Range(-0.45f, 0.45f);
            }

            float p = sparkPhases[i];
            float y = Mathf.Lerp(-0.95f, 1.25f, p);
            float x = sparkOffsetsX[i] + Mathf.Sin(p * Mathf.PI * 3f + i) * 0.10f;
            ambientSparkTransforms[i].localPosition = EnemyBase.SnapToPixelGrid(new Vector3(x, y, 0f), 16f);

            Color c = Color.white;
            c.a = p > 0.65f ? 1f - EnemyBase.QuantizeStep((p - 0.65f) / 0.35f, 3) : 1f;
            ambientSparkRenderers[i].color = c;
        }
    }

    private void ShatterBindingChains(Vector2 blastDir)
    {
        for (int i = 0; i < chainAnchorRenderers.Count; i++)
        {
            SpriteRenderer sr = chainAnchorRenderers[i];
            if (sr == null || !sr.gameObject.activeSelf) continue;

            Vector3 worldPos = sr.transform.position;
            Sprite spr = sr.sprite;
            sr.gameObject.SetActive(false);

            GameObject shard = new GameObject("RiftChainShard");
            shard.transform.position = EnemyBase.SnapToPixelGrid(worldPos, 16f);
            SpriteRenderer shardSr = shard.AddComponent<SpriteRenderer>();
            shardSr.sprite = spr;
            shardSr.sortingOrder = 8;
            shardSr.color = Color.white;

            Vector2 outDir = ((Vector2)(worldPos - cachedTransform.position)).normalized;
            if (outDir.sqrMagnitude < 0.01f) outDir = Random.insideUnitCircle.normalized;
            Vector2 vel = (outDir * Random.Range(5f, 9f)) + (blastDir * 2.5f) + Vector2.up * Random.Range(2f, 5f);

            PortalChainShardVFX vfx = shard.AddComponent<PortalChainShardVFX>();
            vfx.Initialize(shardSr, vel);
        }
    }

    private static Sprite[] GetOrCreateRiftFrames()
    {
        if (cachedRiftFrames != null && cachedRiftFrames.Length == 3 && cachedRiftFrames[0] != null)
        {
            return cachedRiftFrames;
        }

        const int width = 24;
        const int height = 38;
        cachedRiftFrames = new Sprite[3];
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color voidCore = new Color(0.03f, 0.03f, 0.06f, 1f);
        Color innerCrack = new Color(0.85f, 0.88f, 0.96f, 1f);

        for (int f = 0; f < 3; f++)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] px = new Color[width * height];
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            for (int y = 0; y < height; y++)
            {
                float ny = (y / (float)(height - 1)) * 2f - 1f; // -1..1
                float absY = Mathf.Abs(ny);
                if (absY > 0.98f) continue;

                // Diamond/katana belly envelope wider near middle, needle-sharp at ends
                float halfSpan = Mathf.Pow(1f - absY, 0.72f) * 6.2f;
                int jag = (((y + f * 3) % 7) == 0 ? 1 : 0) - (((y + f * 5) % 9) == 0 ? 1 : 0);
                float centerX = 11.5f + Mathf.Round(Mathf.Sin(ny * 3.1f + f * 1.4f) * 0.9f) + jag * 0.5f;

                int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - halfSpan));
                int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(centerX + halfSpan));

                for (int x = minX; x <= maxX; x++)
                {
                    bool isBorder = (x == minX || x == maxX || y <= 2 || y >= height - 3);
                    if (isBorder)
                    {
                        px[y * width + x] = Color.white;
                    }
                    else
                    {
                        // Shimmering internal pixel fracture line
                        bool isInnerFracture = ((y + f * 2) % 6 == 0) && Mathf.Abs(x - centerX) <= halfSpan * 0.55f;
                        px[y * width + x] = isInnerFracture ? innerCrack : voidCore;
                    }
                }

                // Occasional crisp white pixel lightning spur sticking 1-2px off the rim
                if ((y + f * 4) % 8 == 0 && halfSpan > 2.2f)
                {
                    int side = ((y / 8) % 2 == 0) ? (maxX + 1) : (minX - 1);
                    if (side >= 0 && side < width)
                    {
                        px[y * width + side] = Color.white;
                    }
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            cachedRiftFrames[f] = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 16f);
        }

        return cachedRiftFrames;
    }

    private static Sprite GetOrCreateChainLinkSprite(bool lightVariant)
    {
        if (lightVariant && cachedChainLinkLight != null) return cachedChainLinkLight;
        if (!lightVariant && cachedChainLinkDark != null) return cachedChainLinkDark;

        const int w = 5;
        const int h = 5;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color rim = lightVariant ? Color.white : new Color(0.68f, 0.72f, 0.80f, 1f);
        Color dark = new Color(0.10f, 0.11f, 0.16f, 1f);
        Color[] px = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool corner = (x == 0 || x == w - 1) && (y == 0 || y == h - 1);
                bool centerHole = (x == 2 && y == 2);
                if (corner) px[y * w + x] = clear;
                else if (centerHole) px[y * w + x] = dark;
                else if (x == 0 || x == w - 1 || y == 0 || y == h - 1) px[y * w + x] = rim;
                else px[y * w + x] = dark;
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        Sprite spr = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);

        if (lightVariant) cachedChainLinkLight = spr;
        else cachedChainLinkDark = spr;
        return spr;
    }

    #endregion

    #region Chain Slash Selection & Target Feedback

    private Camera GetActiveCamera()
    {
        if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
        {
            cachedCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        }
        return cachedCamera;
    }

    private void CheckMouseSelection()
    {
        if (isTarget || portalCollider == null || Mouse.current == null) return;

        Camera cam = GetActiveCamera();
        if (cam == null) return;

        Vector3 screenPos = Mouse.current.position.ReadValue();
        screenPos.z = -cam.transform.position.z;
        Vector2 currentMouse = cam.ScreenToWorldPoint(screenPos);

        Bounds expandedBounds = portalCollider.bounds;
        expandedBounds.Expand(0.22f);

        bool isHit = expandedBounds.Contains(currentMouse)
            || (hasLastMousePos && LineIntersectsBounds(expandedBounds, lastMousePos, currentMouse));

        lastMousePos = currentMouse;
        hasLastMousePos = true;

        if (isHit)
        {
            TrySelectAsSlashTarget();
        }
    }

    private static bool LineIntersectsBounds(Bounds bounds, Vector2 a, Vector2 b)
    {
        Vector2 min = bounds.min;
        Vector2 max = bounds.max;
        if (bounds.Contains(a) || bounds.Contains(b)) return true;

        return SegmentsIntersect(a, b, new Vector2(min.x, min.y), new Vector2(max.x, min.y))
            || SegmentsIntersect(a, b, new Vector2(max.x, min.y), new Vector2(max.x, max.y))
            || SegmentsIntersect(a, b, new Vector2(max.x, max.y), new Vector2(min.x, max.y))
            || SegmentsIntersect(a, b, new Vector2(min.x, max.y), new Vector2(min.x, min.y));
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        Vector2 r = p2 - p1;
        Vector2 s = p4 - p3;
        float rxs = r.x * s.y - r.y * s.x;
        if (Mathf.Abs(rxs) < 0.00001f) return false;
        Vector2 qp = p3 - p1;
        float t = (qp.x * s.y - qp.y * s.x) / rxs;
        float u = (qp.x * r.y - qp.y * r.x) / rxs;
        return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
    }

    private void TrySelectAsSlashTarget()
    {
        if (!CanInteractWithPortal || isTarget) return;

        PlayerController player = PlayerController.Instance;
        if (player == null || !player.IsInMode) return;

        isTarget = true;
        player.TargetSetup(transform);

        if (tearPixelEffect != null)
        {
            tearPixelEffect.SetOutline(true, Color.white, 2f, PixelSpriteEffect.OutlineMode.Both);
            tearPixelEffect.Flash(0.08f, Color.white);
        }
    }

    private void ApplyTargetFeedback(bool strongJitter)
    {
        if (tearTransform != null && strongJitter)
        {
            Vector2 jitter = Random.insideUnitCircle * targetJitterStrength;
            tearTransform.localPosition = EnemyBase.SnapToPixelGrid(restingTearLocalPos + (Vector3)jitter, 16f);
        }

        if (tearPixelEffect != null && !tearPixelEffect.OutlineEnabled)
        {
            tearPixelEffect.SetOutline(true, Color.white, 2f, PixelSpriteEffect.OutlineMode.Both);
        }
    }

    private void UpdateFocusHoverFeedback(PlayerController player)
    {
        if (Mouse.current == null || tearPixelEffect == null) return;
        Camera cam = GetActiveCamera();
        if (cam == null) return;

        Vector3 screenPos = Mouse.current.position.ReadValue();
        screenPos.z = -cam.transform.position.z;
        Vector2 mouseWorld = cam.ScreenToWorldPoint(screenPos);

        Vector2 portalCenter = GetGrappleAnchorPosition();
        Vector2 toPortal = portalCenter - (Vector2)player.transform.position;
        Vector2 toMouse = mouseWorld - (Vector2)player.transform.position;
        bool aimedNear = toPortal.sqrMagnitude <= 144f
            && toMouse.sqrMagnitude > 0.01f
            && Vector2.Dot(toPortal.normalized, toMouse.normalized) >= 0.90f;

        tearPixelEffect.SetOutline(aimedNear, Color.white, aimedNear ? 1.5f : 0f, PixelSpriteEffect.OutlineMode.Outer);
    }

    private void ResetTargetVisuals()
    {
        if (tearTransform != null && tearTransform.localPosition != restingTearLocalPos)
        {
            tearTransform.localPosition = restingTearLocalPos;
        }
        if (tearPixelEffect != null && tearPixelEffect.OutlineEnabled)
        {
            tearPixelEffect.SetOutline(false);
        }
    }

    private IEnumerator ResetTargetAfterModeExitGraceRoutine()
    {
        yield return new WaitForSecondsRealtime(modeExitGracePeriod);
        if (!isTeleporting)
        {
            isTarget = false;
            hasLastMousePos = false;
            ResetTargetVisuals();
        }
        fallbackResetTargetCoroutine = null;
    }

    #endregion

    #region Core Transition Sequence

    private void StartPortalTransition(PlayerController player, TransitionTriggerType triggerType, Vector2 rawDirection)
    {
        if (isTeleporting) return;

        isTeleporting = true;
        isTarget = false;
        if (portalCollider != null)
        {
            portalCollider.enabled = false;
        }

        // Persist across the scene load so we drive both closing and next-stage opening pixel slices
        cachedTransform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);

        Vector2 cutDir = NormalizeDramaticCutDirection(rawDirection);
        StartCoroutine(DimensionalSliceTransitionRoutine(player, triggerType, cutDir));
    }

    private static Vector2 NormalizeDramaticCutDirection(Vector2 rawDir)
    {
        Vector2 dir = rawDir.sqrMagnitude > 0.001f ? rawDir.normalized : new Vector2(1f, 0.36f).normalized;
        // Avoid purely vertical or purely horizontal flat cuts so the diagonal split always looks razor sharp
        if (Mathf.Abs(dir.y) < 0.28f)
        {
            dir.y = dir.y >= 0f ? 0.36f : -0.36f;
        }
        if (Mathf.Abs(dir.x) < 0.35f)
        {
            dir.x = dir.x >= 0f ? 0.72f : -0.72f;
        }
        return dir.normalized;
    }

    private IEnumerator DimensionalSliceTransitionRoutine(PlayerController player, TransitionTriggerType triggerType, Vector2 cutDir)
    {
        if (teleportDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(teleportDelay);
        }

        // 1. Lock player state & shatter Space-Tear binding chains
        if (player != null)
        {
            player.SetPortalTransitionState(true);
            player.FaceWorldDirection(GetGrappleAnchorPosition().x - player.transform.position.x);
            player.SetPlayerAnimState(
                triggerType == TransitionTriggerType.Grapple
                    ? PlayerController.PlayerAnimState.ReleaseGrapplingHook
                    : PlayerController.PlayerAnimState.Dash,
                true
            );

            Rigidbody2D playerRb = player.GetComponent<Rigidbody2D>();
            if (playerRb != null)
            {
                playerRb.linearVelocity = Vector2.zero;
                playerRb.gravityScale = 0f;
            }
        }

        if (tearPixelEffect != null)
        {
            tearPixelEffect.SetOutline(true, Color.white, 2f, PixelSpriteEffect.OutlineMode.Both);
            tearPixelEffect.Flash(0.10f, Color.white);
        }

        if (useMonochromeSpaceTear)
        {
            ShatterBindingChains(cutDir);
            PortalPixelSparkVFX.SpawnBurst(GetGrappleAnchorPosition(), 14, 4.5f, Color.white);
        }

        // 2. Camera punch toward the Space-Tear
        Camera mainCam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        CinemachineCamera cinemachineCam = FindFirstObjectByType<CinemachineCamera>();

        float startOrtho = mainCam != null && mainCam.orthographic
            ? mainCam.orthographicSize
            : (cinemachineCam != null ? cinemachineCam.Lens.OrthographicSize : 6.25f);

        if (cinemachineCam != null)
        {
            if (cinemachineCam.TryGetComponent<CinemachineGroupFraming>(out var groupFraming))
            {
                groupFraming.enabled = false;
            }
            if (cinemachineCam.TryGetComponent<CinemachinePositionComposer>(out var composer))
            {
                composer.TargetOffset = Vector3.zero;
                composer.Damping = new Vector3(0.08f, 0.08f, 0.08f);
            }
            cinemachineCam.Target.TrackingTarget = cachedTransform;
        }

        float punchElapsed = 0f;
        float punchDuration = Mathf.Max(0.08f, zoomPunchDuration);
        float zoomTarget = Mathf.Max(1.6f, Mathf.Min(startOrtho * 0.65f, zoomInOrthoSize));

        while (punchElapsed < punchDuration)
        {
            punchElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(punchElapsed / punchDuration);
            float ease = 1f - (1f - t) * (1f - t);

            float currentOrtho = Mathf.Lerp(startOrtho, zoomTarget, ease);
            if (cinemachineCam != null)
            {
                LensSettings lens = cinemachineCam.Lens;
                lens.OrthographicSize = currentOrtho;
                cinemachineCam.Lens = lens;
            }
            if (mainCam != null)
            {
                mainCam.orthographicSize = currentOrtho;
            }

            if (visualRoot != null)
            {
                float widen = Mathf.Lerp(1f, 1.35f, ease);
                visualRoot.localScale = new Vector3(
                    restingVisualCompensation.x * widen,
                    restingVisualCompensation.y * Mathf.Lerp(1f, 1.15f, ease),
                    1f
                );
            }

            yield return null;
        }

        // Wait one frame before camera capture so zoomed frame is crisp
        yield return null;

        Vector2 portalViewport = mainCam != null
            ? (Vector2)mainCam.WorldToViewportPoint(GetGrappleAnchorPosition())
            : new Vector2(0.5f, 0.5f);

        AudioClip swordSlash = slashCutSFX != null ? slashCutSFX : (player != null ? player.SlashSFX : null);
        AudioMixerGroup mixer = portalAudioSource != null ? portalAudioSource.outputAudioMixerGroup : null;

        HideWorldPortalRenderers();

        PixelScreenSliceRunner.StartTransition(new PixelScreenSliceRunner.Config
        {
            CaptureCamera = mainCam,
            OptionalOverlayCanvasToCapture = null,
            CutOriginViewport = portalViewport,
            CutDirection = cutDir,
            VirtualPixelHeight = virtualPixelHeight,
            KatanaCutDuration = katanaCutDuration,
            ScreenSplitDuration = screenSplitDuration,
            BlackHoldDuration = blackScreenHoldDuration,
            BlackFadeOutDuration = nextStageFadeOutDuration,
            SlashSFX = swordSlash,
            PortalSplitSFX = portalEnterSFX,
            SFXVolume = portalSFXVolume,
            OutputMixerGroup = mixer,
            LoadTargetSceneAction = LoadNextBuildScene
        });

        Destroy(gameObject);
    }

    private static void LoadNextBuildScene()
    {
        int currentBuildIndex = SceneManager.GetActiveScene().buildIndex;
        int nextBuildIndex = currentBuildIndex + 1;
        if (nextBuildIndex >= 0 && nextBuildIndex < SceneManager.sceneCountInBuildSettings)
        {
            SceneManager.LoadScene(nextBuildIndex);
        }
        else
        {
            Debug.Log("PortalNextLevel: Reached final scene in Build Profile, wrapping to index 0.");
            SceneManager.LoadScene(0);
        }
    }

    #endregion

    #region Helpers & Audio

    private IEnumerator SpawnScaleRoutine()
    {
        cachedTransform.localScale = Vector3.zero;
        float elapsed = 0f;
        float dur = Mathf.Max(0.05f, spawnAnimationDuration);

        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float scaleFactor = 1f + 0.16f * Mathf.Sin(t * Mathf.PI) * (1f - t);
            cachedTransform.localScale = targetScale * Mathf.Lerp(0f, scaleFactor, t);
            yield return null;
        }

        cachedTransform.localScale = targetScale;
        spawnAnimFinished = true;
    }

    private void HideWorldPortalRenderers()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].enabled = false;
        }
    }

    private void PlayClip(AudioClip clip, float volume)
    {
        if (clip == null) return;

        if (portalAudioSource == null)
        {
            portalAudioSource = gameObject.AddComponent<AudioSource>();
            portalAudioSource.playOnAwake = false;
            portalAudioSource.spatialBlend = 0f;
        }

        PlayerController player = PlayerController.Instance;
        if (player != null)
        {
            AudioSource playerAudio = player.GetComponent<AudioSource>();
            if (playerAudio != null && portalAudioSource.outputAudioMixerGroup == null)
            {
                portalAudioSource.outputAudioMixerGroup = playerAudio.outputAudioMixerGroup;
            }
        }

        portalAudioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private static AudioClip GetFallbackGrappleSFX()
    {
        return PlayerController.Instance != null ? PlayerController.Instance.GrappleHitSFX : null;
    }

    #endregion
}

/// <summary>
/// Shared persistent runner for the pixel-art Dimensional Screen Slice scene transition.
/// Used by both MainMenu (Start button) and PortalNextLevel so scene cuts, transition SFX cues,
/// and next-stage Space-Tear emergence bursts share a single cohesive pipeline.
/// </summary>
public sealed class PixelScreenSliceRunner : MonoBehaviour
{
    public struct Config
    {
        public Camera CaptureCamera;
        public Canvas OptionalOverlayCanvasToCapture;
        public Vector2 CutOriginViewport;
        public Vector2 CutDirection;
        public int VirtualPixelHeight;
        public float KatanaCutDuration;
        public float ScreenSplitDuration;
        public float BlackHoldDuration;
        public float BlackFadeOutDuration;
        public AudioClip SlashSFX;
        public AudioClip PortalSplitSFX;
        public float SFXVolume;
        public AudioMixerGroup OutputMixerGroup;
        public Action LoadTargetSceneAction;
    }

    private AudioSource audioSource;
    private CanvasGroup overlayCanvasGroup;
    private readonly List<Texture2D> runtimeTextures = new List<Texture2D>();

    public static void StartTransition(Config cfg)
    {
        GameObject runnerObj = new GameObject("PixelScreenSliceRunner");
        DontDestroyOnLoad(runnerObj);
        PixelScreenSliceRunner runner = runnerObj.AddComponent<PixelScreenSliceRunner>();
        runner.Begin(cfg);
    }

    private void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
    }

    private void OnDestroy()
    {
        CleanupRuntimeTextures();
        if (overlayCanvasGroup != null)
        {
            Destroy(overlayCanvasGroup.gameObject);
            overlayCanvasGroup = null;
        }
    }

    private void Begin(Config cfg)
    {
        if (cfg.OutputMixerGroup != null && audioSource != null)
        {
            audioSource.outputAudioMixerGroup = cfg.OutputMixerGroup;
        }
        StartCoroutine(ExecuteTransitionRoutine(cfg));
    }

    private IEnumerator ExecuteTransitionRoutine(Config cfg)
    {
        Camera cam = cfg.CaptureCamera != null ? cfg.CaptureCamera : (Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>());

        int virtH = Mathf.Clamp(cfg.VirtualPixelHeight > 0 ? cfg.VirtualPixelHeight : 180, 120, 270);
        float aspect = cam != null ? cam.aspect : ((float)Screen.width / Mathf.Max(1, Screen.height));
        int virtW = Mathf.Clamp(Mathf.RoundToInt(virtH * aspect), 200, 480);

        Vector2 cutDir = NormalizeDiagonalDirection(cfg.CutDirection);
        Vector2 originVp = new Vector2(
            Mathf.Clamp(cfg.CutOriginViewport.x, 0.25f, 0.75f),
            Mathf.Clamp(cfg.CutOriginViewport.y, 0.25f, 0.75f)
        );
        Vector2 cutOriginPx = new Vector2(originVp.x * virtW, originVp.y * virtH);

        Color32[] capturedPixels = CaptureVirtualPixelFrame(cam, cfg.OptionalOverlayCanvasToCapture, virtW, virtH);
        Color32[] activeSourcePixels = capturedPixels ?? CreateSolidColorArray(virtW * virtH, new Color32(0, 0, 0, 255));

        BuildPixelSplitTextures(
            activeSourcePixels, virtW, virtH, cutOriginPx, cutDir,
            out Texture2D topHalfTex, out Texture2D botHalfTex, out Texture2D cutBeamTex
        );

        BuildOverlayCanvas(
            out Image blackBackdrop,
            out RawImage topHalfImg, out RectTransform topHalfRect,
            out RawImage botHalfImg, out RectTransform botHalfRect,
            out RawImage cutBeamImg,
            out RawImage flashImg
        );

        blackBackdrop.color = Color.black;
        topHalfImg.texture = topHalfTex;
        botHalfImg.texture = botHalfTex;
        cutBeamImg.texture = cutBeamTex;
        topHalfRect.anchoredPosition = Vector2.zero;
        botHalfRect.anchoredPosition = Vector2.zero;

        float baseVolume = Mathf.Clamp01(cfg.SFXVolume > 0f ? cfg.SFXVolume : 1f);

        // Phase A: Crisp White Pixel Katana Cut sweeps across the viewport with SwordSlashSFX
        if (cfg.SlashSFX != null && audioSource != null)
        {
            audioSource.PlayOneShot(cfg.SlashSFX, baseVolume);
        }

        float cutTime = 0f;
        float cutDur = Mathf.Max(0.05f, cfg.KatanaCutDuration > 0f ? cfg.KatanaCutDuration : 0.09f);
        int facingStepSign = cutDir.x >= 0f ? 1 : -1;

        while (cutTime < cutDur)
        {
            cutTime += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(cutTime / cutDur);

            float steppedReveal = EnemyBase.QuantizeStep(t, 6);
            Rect uv;
            if (facingStepSign > 0)
            {
                uv = new Rect(0f, 0f, steppedReveal, 1f);
                cutBeamImg.rectTransform.anchorMin = Vector2.zero;
                cutBeamImg.rectTransform.anchorMax = new Vector2(steppedReveal, 1f);
            }
            else
            {
                uv = new Rect(1f - steppedReveal, 0f, steppedReveal, 1f);
                cutBeamImg.rectTransform.anchorMin = new Vector2(1f - steppedReveal, 0f);
                cutBeamImg.rectTransform.anchorMax = Vector2.one;
            }
            cutBeamImg.rectTransform.offsetMin = Vector2.zero;
            cutBeamImg.rectTransform.offsetMax = Vector2.zero;
            cutBeamImg.uvRect = uv;
            cutBeamImg.color = Color.white;

            if (t >= 0.8f)
            {
                flashImg.color = new Color(1f, 1f, 1f, 0.35f);
            }
            yield return null;
        }

        flashImg.color = Color.clear;
        cutBeamImg.color = Color.clear;

        // Phase B: Screen splits along the diagonal cut line into Pitch Black with EnteringPortalSFX
        if (cfg.PortalSplitSFX != null && audioSource != null)
        {
            audioSource.PlayOneShot(cfg.PortalSplitSFX, baseVolume);
        }

        Vector2 normal = new Vector2(-cutDir.y, cutDir.x);
        float screenDiag = Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height);
        float maxSlideAlong = Mathf.Max(420f, screenDiag * 0.48f);
        float maxGapNormal = Mathf.Max(220f, screenDiag * 0.30f);

        float splitTime = 0f;
        float splitDur = Mathf.Max(0.12f, cfg.ScreenSplitDuration > 0f ? cfg.ScreenSplitDuration : 0.30f);

        while (splitTime < splitDur)
        {
            splitTime += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(splitTime / splitDur);
            float ease = t * t * (2.2f - 1.2f * t);

            float pxStepX = Screen.width / (float)virtW;
            float pxStepY = Screen.height / (float)virtH;

            Vector2 rawOffset = (cutDir * (maxSlideAlong * ease)) + (normal * (maxGapNormal * ease));
            Vector2 snappedOffset = new Vector2(
                Mathf.Round(rawOffset.x / Mathf.Max(1f, pxStepX)) * pxStepX,
                Mathf.Round(rawOffset.y / Mathf.Max(1f, pxStepY)) * pxStepY
            );

            topHalfRect.anchoredPosition = snappedOffset;
            botHalfRect.anchoredPosition = -snappedOffset;

            float tint = 1f - EnemyBase.QuantizeStep(t * 0.9f, 4);
            Color halfTint = new Color(tint, tint, tint, 1f);
            topHalfImg.color = halfTint;
            botHalfImg.color = halfTint;

            yield return null;
        }

        topHalfImg.color = Color.clear;
        botHalfImg.color = Color.clear;

        if (cfg.BlackHoldDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(cfg.BlackHoldDuration);
        }

        // Load next scene
        Time.timeScale = 1f;
        ChainEnemy.isExploding = false;
        cfg.LoadTargetSceneAction?.Invoke();

        // Wait 2 frames so PlayerController.ReviveAndReset() & GameManager & CinemachineCamera sync
        yield return null;
        yield return null;

        CinemachineCamera newSceneCinemachine = FindFirstObjectByType<CinemachineCamera>();
        if (newSceneCinemachine != null)
        {
            newSceneCinemachine.PreviousStateIsValid = false;
        }

        CleanupRuntimeTextures();
        topHalfImg.color = Color.clear;
        botHalfImg.color = Color.clear;
        cutBeamImg.color = Color.clear;
        flashImg.color = Color.clear;

        if (overlayCanvasGroup != null)
        {
            overlayCanvasGroup.blocksRaycasts = false;
        }
        blackBackdrop.raycastTarget = false;
        blackBackdrop.color = Color.black;

        // Spawn silent arrival Space-Tear + dust behind player in gameplay stages (no extra SFX)
        PlayerController nextPlayer = PlayerController.Instance != null
            ? PlayerController.Instance
            : FindFirstObjectByType<PlayerController>();

        bool isGameplayStage = nextPlayer != null && FindFirstObjectByType<MainMenu>() == null;
        if (isGameplayStage)
        {
            int burstDir = cutDir.x >= 0f ? 1 : -1;
            PortalArrivalTearVFX.Spawn(nextPlayer.transform.position);
            nextPlayer.TriggerStageArrivalBurst(burstDir, playSfx: false);
        }

        // Phase C: Fade cleanly out of black to reveal the new stage (single slash per transition)
        float fadeTime = 0f;
        float fadeDur = Mathf.Max(0.08f, cfg.BlackFadeOutDuration > 0f ? cfg.BlackFadeOutDuration : 0.24f);

        while (fadeTime < fadeDur)
        {
            fadeTime += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeTime / fadeDur);
            blackBackdrop.color = new Color(0f, 0f, 0f, 1f - t);
            yield return null;
        }

        blackBackdrop.color = Color.clear;
        if (overlayCanvasGroup != null)
        {
            Destroy(overlayCanvasGroup.gameObject);
            overlayCanvasGroup = null;
        }

        float audioWait = 0f;
        while (audioSource != null && audioSource.isPlaying && audioWait < 2.5f)
        {
            audioWait += Time.unscaledDeltaTime;
            yield return null;
        }

        Destroy(gameObject);
    }

    private static Vector2 NormalizeDiagonalDirection(Vector2 rawDir)
    {
        Vector2 dir = rawDir.sqrMagnitude > 0.001f ? rawDir.normalized : new Vector2(1f, 0.36f).normalized;
        if (Mathf.Abs(dir.y) < 0.28f) dir.y = dir.y >= 0f ? 0.36f : -0.36f;
        if (Mathf.Abs(dir.x) < 0.35f) dir.x = dir.x >= 0f ? 0.72f : -0.72f;
        return dir.normalized;
    }

    private static Color32[] CaptureVirtualPixelFrame(Camera cam, Canvas overlayCanvas, int virtW, int virtH)
    {
        if (cam == null) return null;

        RenderTexture rt = new RenderTexture(virtW, virtH, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Texture2D tex = new Texture2D(virtW, virtH, TextureFormat.RGBA32, false, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        RenderTexture prevTarget = cam.targetTexture;
        RenderTexture prevActive = RenderTexture.active;

        bool swappedCanvas = overlayCanvas != null && overlayCanvas.renderMode == RenderMode.ScreenSpaceOverlay;
        RenderMode prevCanvasMode = swappedCanvas ? overlayCanvas.renderMode : RenderMode.ScreenSpaceOverlay;
        Camera prevCanvasCam = swappedCanvas ? overlayCanvas.worldCamera : null;
        float prevPlaneDist = swappedCanvas ? overlayCanvas.planeDistance : 100f;

        try
        {
            if (swappedCanvas)
            {
                overlayCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                overlayCanvas.worldCamera = cam;
                overlayCanvas.planeDistance = 5f;
                Canvas.ForceUpdateCanvases();
            }

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, virtW, virtH), 0, 0, false);
            tex.Apply(false, false);
            return tex.GetPixels32();
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (swappedCanvas && overlayCanvas != null)
            {
                overlayCanvas.renderMode = prevCanvasMode;
                overlayCanvas.worldCamera = prevCanvasCam;
                overlayCanvas.planeDistance = prevPlaneDist;
                Canvas.ForceUpdateCanvases();
            }

            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            rt.Release();
            Destroy(rt);
            Destroy(tex);
        }
    }

    private void BuildPixelSplitTextures(
        Color32[] sourcePixels,
        int width,
        int height,
        Vector2 originPx,
        Vector2 cutDir,
        out Texture2D topTex,
        out Texture2D botTex,
        out Texture2D cutTex
    )
    {
        int count = width * height;
        Color32[] topPx = new Color32[count];
        Color32[] botPx = new Color32[count];
        Color32[] cutPx = new Color32[count];

        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 white = new Color32(255, 255, 255, 255);
        Color32 paleWhite = new Color32(230, 236, 245, 255);

        Vector2 normal = new Vector2(-cutDir.y, cutDir.x);

        for (int y = 0; y < height; y++)
        {
            float dy = y - originPx.y;
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                float dx = x - originPx.x;

                float along = dx * cutDir.x + dy * cutDir.y;
                float jag = Mathf.Round(Mathf.Sin(along * 0.22f)) * 0.65f;
                float dist = dx * normal.x + dy * normal.y + jag;

                Color32 src = sourcePixels[idx];
                src.a = 255;

                float absDist = Mathf.Abs(dist);
                if (dist >= 0f)
                {
                    topPx[idx] = dist < 1.35f ? white : src;
                    botPx[idx] = clear;
                }
                else
                {
                    topPx[idx] = clear;
                    botPx[idx] = absDist < 1.35f ? white : src;
                }

                if (absDist < 1.25f) cutPx[idx] = white;
                else if (absDist < 2.25f && ((x + y) % 2 == 0)) cutPx[idx] = paleWhite;
                else cutPx[idx] = clear;
            }
        }

        topTex = CreatePointTexture(width, height, topPx);
        botTex = CreatePointTexture(width, height, botPx);
        cutTex = CreatePointTexture(width, height, cutPx);
    }

    private Texture2D CreatePointTexture(int width, int height, Color32[] pixels)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        runtimeTextures.Add(tex);
        return tex;
    }

    private static Color32[] CreateSolidColorArray(int length, Color32 color)
    {
        Color32[] arr = new Color32[length];
        for (int i = 0; i < length; i++) arr[i] = color;
        return arr;
    }

    private void CleanupRuntimeTextures()
    {
        for (int i = 0; i < runtimeTextures.Count; i++)
        {
            if (runtimeTextures[i] != null) Destroy(runtimeTextures[i]);
        }
        runtimeTextures.Clear();
    }

    private void BuildOverlayCanvas(
        out Image blackBackdrop,
        out RawImage topHalfImg, out RectTransform topHalfRect,
        out RawImage botHalfImg, out RectTransform botHalfRect,
        out RawImage cutBeamImg,
        out RawImage flashImg
    )
    {
        GameObject canvasObj = new GameObject("PixelPortalSliceCanvas");
        canvasObj.transform.SetParent(null, false);
        DontDestroyOnLoad(canvasObj);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        overlayCanvasGroup = canvasObj.AddComponent<CanvasGroup>();
        overlayCanvasGroup.alpha = 1f;
        overlayCanvasGroup.blocksRaycasts = true;
        overlayCanvasGroup.interactable = false;

        blackBackdrop = CreateStretchChild<Image>(canvasObj.transform, "VoidBlackBackdrop");
        blackBackdrop.raycastTarget = true;

        topHalfImg = CreateStretchChild<RawImage>(canvasObj.transform, "SlicedTopHalf");
        topHalfImg.raycastTarget = false;
        topHalfRect = topHalfImg.rectTransform;

        botHalfImg = CreateStretchChild<RawImage>(canvasObj.transform, "SlicedBottomHalf");
        botHalfImg.raycastTarget = false;
        botHalfRect = botHalfImg.rectTransform;

        cutBeamImg = CreateStretchChild<RawImage>(canvasObj.transform, "KatanaPixelCutBeam");
        cutBeamImg.raycastTarget = false;
        cutBeamImg.color = Color.clear;

        flashImg = CreateStretchChild<RawImage>(canvasObj.transform, "KatanaFlash");
        flashImg.raycastTarget = false;
        flashImg.color = Color.clear;
    }

    private static T CreateStretchChild<T>(Transform parent, string objName) where T : Graphic
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return obj.AddComponent<T>();
    }
}

/// <summary>
/// Outward-flying pixel chain link shard spawned when the Space-Tear's binding chains snap apart.
/// </summary>
public class PortalChainShardVFX : MonoBehaviour
{
    private SpriteRenderer sr;
    private Vector2 velocity;
    private Vector3 rawPos;
    private float elapsed;
    private const float Duration = 0.36f;

    public void Initialize(SpriteRenderer renderer, Vector2 initialVelocity)
    {
        sr = renderer;
        velocity = initialVelocity;
        rawPos = transform.position;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime > 0f ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0.016f;
        elapsed += dt;
        velocity += Vector2.down * (24f * dt);
        rawPos += (Vector3)(velocity * dt);
        transform.position = EnemyBase.SnapToPixelGrid(rawPos, 16f);

        if (sr != null)
        {
            float alpha = 1f - EnemyBase.QuantizeStep(elapsed / Duration, 4);
            Color c = sr.color;
            c.a = alpha;
            sr.color = c;
        }

        if (elapsed >= Duration)
        {
            Destroy(gameObject);
        }
    }
}

/// <summary>
/// Crisp pixel-art square spark burst for portal latch, cut, and emergence.
/// </summary>
public class PortalPixelSparkVFX : MonoBehaviour
{
    private Transform[] sparkTransforms;
    private SpriteRenderer[] sparkRenderers;
    private Vector2[] positions;
    private Vector2[] velocities;
    private Color baseColor;
    private float elapsed;
    private const float Duration = 0.28f;

    public static void SpawnBurst(Vector3 center, int count, float speed, Color tint)
    {
        GameObject root = new GameObject("PortalPixelSparkBurst");
        root.transform.position = EnemyBase.SnapToPixelGrid(center, 16f);
        PortalPixelSparkVFX vfx = root.AddComponent<PortalPixelSparkVFX>();
        vfx.Init(count, speed, tint);
    }

    private void Init(int count, float speed, Color tint)
    {
        baseColor = tint;
        sparkTransforms = new Transform[count];
        sparkRenderers = new SpriteRenderer[count];
        positions = new Vector2[count];
        velocities = new Vector2[count];

        Sprite s1 = EnemyBase.GetSharedPixelBlockSprite(1);
        Sprite s2 = EnemyBase.GetSharedPixelBlockSprite(2);

        for (int i = 0; i < count; i++)
        {
            float angle = (Mathf.PI * 2f * i / count) + Random.Range(-0.25f, 0.25f);
            float spd = speed * Random.Range(0.55f, 1.35f);
            velocities[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spd;
            positions[i] = Vector2.zero;

            GameObject p = new GameObject("Spark_" + i);
            p.transform.SetParent(transform, false);
            SpriteRenderer sr = p.AddComponent<SpriteRenderer>();
            sr.sprite = (i % 2 == 0) ? s2 : s1;
            sr.sortingOrder = 18;
            sr.color = tint;

            sparkTransforms[i] = p.transform;
            sparkRenderers[i] = sr;
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime > 0f ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0.016f;
        elapsed += dt;
        float t = Mathf.Clamp01(elapsed / Duration);
        float alpha = baseColor.a * (1f - EnemyBase.QuantizeStep(t, 4));

        for (int i = 0; i < sparkTransforms.Length; i++)
        {
            if (sparkTransforms[i] == null) continue;
            positions[i] += velocities[i] * dt;
            velocities[i] *= (1f - 5.5f * dt);
            sparkTransforms[i].localPosition = EnemyBase.SnapToPixelGrid(positions[i], 16f);

            Color c = baseColor;
            c.a = alpha;
            sparkRenderers[i].color = c;
        }

        if (elapsed >= Duration)
        {
            Destroy(gameObject);
        }
    }
}

/// <summary>
/// Brief white-edged pixel Space-Tear that opens behind the player upon arriving in the next stage and snaps shut.
/// </summary>
public class PortalArrivalTearVFX : MonoBehaviour
{
    private SpriteRenderer sr;
    private float elapsed;
    private const float Duration = 0.28f;
    private static Sprite cachedArrivalSprite;

    public static void Spawn(Vector3 worldPos)
    {
        GameObject obj = new GameObject("StageArrivalSpaceTear");
        obj.transform.position = EnemyBase.SnapToPixelGrid(worldPos + new Vector3(0f, 0.1f, 0f), 16f);
        PortalArrivalTearVFX vfx = obj.AddComponent<PortalArrivalTearVFX>();
        vfx.Initialize();
    }

    private void Initialize()
    {
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateArrivalSprite();
        sr.sortingOrder = 4; // Behind player's 5
        sr.color = Color.white;
        PortalPixelSparkVFX.SpawnBurst(transform.position, 10, 3.8f, Color.white);
    }

    private static Sprite GetOrCreateArrivalSprite()
    {
        if (cachedArrivalSprite != null) return cachedArrivalSprite;

        const int w = 20;
        const int h = 34;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color voidCore = new Color(0.05f, 0.05f, 0.09f, 1f);
        Color[] px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = clear;

        for (int y = 0; y < h; y++)
        {
            float ny = (y / (float)(h - 1)) * 2f - 1f;
            float span = (1f - Mathf.Abs(ny)) * 4.5f;
            float cx = 9.5f + Mathf.Round(Mathf.Sin(ny * 2.8f) * 0.8f);
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - span));
            int maxX = Mathf.Min(w - 1, Mathf.CeilToInt(cx + span));
            for (int x = minX; x <= maxX; x++)
            {
                bool rim = (x == minX || x == maxX || y <= 1 || y >= h - 2);
                px[y * w + x] = rim ? Color.white : voidCore;
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        cachedArrivalSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
        return cachedArrivalSprite;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        elapsed += dt;
        float t = Mathf.Clamp01(elapsed / Duration);

        // Stepped squeeze horizontally until the tear snaps shut into a thin vertical line
        float widthScale = 1f - EnemyBase.QuantizeStep(t, 4);
        transform.localScale = new Vector3(Mathf.Max(0.15f, widthScale), 1f + t * 0.15f, 1f);

        if (sr != null)
        {
            Color c = Color.white;
            c.a = t > 0.6f ? 1f - EnemyBase.QuantizeStep((t - 0.6f) / 0.4f, 3) : 1f;
            sr.color = c;
        }

        if (elapsed >= Duration)
        {
            Destroy(gameObject);
        }
    }
}
