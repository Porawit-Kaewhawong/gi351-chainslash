using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Represents the sequential state of a weak point in the Legendary Samurai's Iai cutting pattern.
/// </summary>
public enum TargetPointSequenceState
{
    Locked,       // Dimmed, cannot be selected yet
    ActiveNext,   // Current target to select: bright pulsing glow and animated scale
    Completed     // Successfully selected in the Iai sequence
}

/// <summary>
/// Weak point target node spawned around the Legendary Samurai during Chain Slash mode.
/// Supports sequential Iai cutting order, visual number labels, pulse animation, and fast mouse-swipe detection.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CircleCollider2D))]
public class BossTargetPoint : EnemyBase
{
    #region Constants

    private const float HOVER_RADIUS_SQ = 0.65f * 0.65f;
    private const float SWIPE_SEGMENT_RADIUS_SQ = 0.55f * 0.55f;
    private const float LOCKED_SCALE_MULT = 0.9f;
    private const int SPRITE_SORTING_ORDER = 7;
    private const int LABEL_SORTING_ORDER = 9;
    private const int PROCEDURAL_TEX_SIZE = 24;

    private static readonly Color LockedLabelColor = new Color(0.7f, 0.7f, 0.75f, 0.6f);
    private static readonly Color CompletedLabelColor = new Color(0.2f, 0.95f, 1f, 0.9f);

    #endregion

    #region Serialized Fields

    [Header("Target Point Visuals")]
    [SerializeField] private Color normalColor = new Color(1f, 0.6f, 0.15f, 1f);
    [SerializeField] private Color selectedColor = new Color(0.2f, 0.95f, 1f, 1f);
    [SerializeField] private Color lockedColor = new Color(0.35f, 0.35f, 0.4f, 0.45f);
    [SerializeField] private Color activeNextColor = new Color(1f, 0.88f, 0.25f, 1f);
    [SerializeField] private GameObject slashVFXPrefab;
    [SerializeField] private AudioClip slashSFX;

    [Header("Sequence Number & Pulse Settings")]
    [SerializeField] private float numberFontSize = 2.0f;
    [SerializeField] private float pulseFrequency = 6.5f;
    [SerializeField] private float pulseScaleMin = 0.92f;
    [SerializeField] private float pulseScaleMax = 1.32f;
    [SerializeField] private TMP_FontAsset fontAsset;

    #endregion

    #region Private State

    private LegendarySamurai boss;
    private int pointIndex;
    private bool isSlashed;
    private TargetPointSequenceState sequenceState = TargetPointSequenceState.Locked;

    private TextMeshPro numberText;
    private Camera cachedCamera;
    private Vector3 baseScale = Vector3.one;
    private Vector2 lastMouseWorldPos;
    private bool hasLastMousePos;

    private static Sprite cachedTargetSprite;

    #endregion

    #region Public Properties

    public int PointIndex => pointIndex;
    public bool IsSlashed => isSlashed;
    public LegendarySamurai OwnerBoss => boss;
    public TargetPointSequenceState SequenceState => sequenceState;

    public bool IsSelectable
    {
        get
        {
            if (IsDead || isSlashed || isTarget) return false;
            if (boss != null && boss.UseSequentialIaiPatterns)
            {
                return sequenceState == TargetPointSequenceState.ActiveNext && boss.IsPointNextInSequence(this);
            }
            return true;
        }
    }

    #endregion

    #region Unity Lifecycle

    protected override void Awake()
    {
        col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            if (spriteRenderer.color.a > 0f) normalColor = spriteRenderer.color;
            originalColor = normalColor;
            spriteRenderer.sortingOrder = SPRITE_SORTING_ORDER;
        }

        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
        }

        baseScale = transform.localScale;
        EnsureNumberLabel();
        DeactivatePoint();
    }

    protected override void Update()
    {
        if (IsDead || isSlashed || !gameObject.activeSelf) return;

        // Active target pulse animation
        if (sequenceState == TargetPointSequenceState.ActiveNext)
        {
            float pulse = Mathf.PingPong(Time.time * pulseFrequency, 1f);
            float scaleMult = Mathf.Lerp(pulseScaleMin, pulseScaleMax, pulse);
            transform.localScale = baseScale * scaleMult;
        }

        if (!isTarget && PlayerController.Instance != null && PlayerController.Instance.IsInMode)
        {
            CheckMouseHover();
        }
    }

    #endregion

    #region Initialization & Sequence State

    public void Initialize(LegendarySamurai ownerBoss, int index)
    {
        boss = ownerBoss;
        pointIndex = index;
        EnsureNumberLabel();
        if (numberText != null)
        {
            numberText.text = (pointIndex + 1).ToString();
        }
    }

    public void SetSequenceIndex(int index, int totalCount, TMP_FontAsset customFont = null, float customFontSize = -1f)
    {
        pointIndex = index;
        if (customFont != null) fontAsset = customFont;
        if (customFontSize > 0f) numberFontSize = customFontSize;
        EnsureNumberLabel();
        if (numberText != null)
        {
            numberText.text = (index + 1).ToString();
            numberText.fontSize = numberFontSize;
            if (fontAsset != null && numberText.font != fontAsset)
            {
                numberText.font = fontAsset;
            }
        }
    }

    public void SetSequenceState(TargetPointSequenceState newState)
    {
        sequenceState = newState;
        UpdateVisualsForState();
    }

    public void ActivatePoint(Vector3 worldPos)
    {
        transform.position = worldPos;
        transform.localScale = baseScale;
        isSlashed = false;
        IsDead = false;
        isTarget = false;
        hasLastMousePos = false;

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
        }

        if (numberText != null)
        {
            numberText.gameObject.SetActive(true);
        }

        if (col != null) col.enabled = true;

        gameObject.SetActive(true);
        UpdateVisualsForState();
    }

    public void DeactivatePoint()
    {
        isSlashed = false;
        IsDead = true;
        isTarget = false;
        hasLastMousePos = false;
        transform.localScale = baseScale;

        if (col != null) col.enabled = false;
        if (spriteRenderer != null) spriteRenderer.enabled = false;
        if (numberText != null) numberText.gameObject.SetActive(false);

        gameObject.SetActive(false);
    }

    private void UpdateVisualsForState()
    {
        if (spriteRenderer == null) return;

        switch (sequenceState)
        {
            case TargetPointSequenceState.Locked:
                spriteRenderer.color = lockedColor;
                transform.localScale = baseScale * LOCKED_SCALE_MULT;
                if (numberText != null)
                {
                    numberText.color = LockedLabelColor;
                }
                break;

            case TargetPointSequenceState.ActiveNext:
                spriteRenderer.color = activeNextColor;
                if (numberText != null)
                {
                    numberText.color = Color.white;
                }
                break;

            case TargetPointSequenceState.Completed:
                spriteRenderer.color = selectedColor;
                transform.localScale = baseScale;
                if (numberText != null)
                {
                    numberText.color = CompletedLabelColor;
                }
                break;
        }
    }

    #endregion

    #region Target Selection & Combat

    public override void TrySelectTarget()
    {
        if (!IsSelectable) return;

        if (PlayerController.Instance != null && PlayerController.Instance.IsInMode)
        {
            isTarget = true;
            SetSequenceState(TargetPointSequenceState.Completed);

            if (boss != null)
            {
                boss.OnTargetPointSelected(this);
            }

            PlayerController.Instance.TargetSetup(transform);
        }
    }

    protected override void CheckMouseHover()
    {
        if (isTarget || col == null || !IsSelectable || Mouse.current == null) return;

        Camera cam = GetActiveCamera();
        if (cam == null) return;

        Vector3 screenPos = Mouse.current.position.ReadValue();
        screenPos.z = -cam.transform.position.z;
        Vector2 mouseWorld = cam.ScreenToWorldPoint(screenPos);
        Vector2 currentPos = transform.position;

        bool hit = false;

        // Direct collider overlap or squared radius check
        if (col.OverlapPoint(mouseWorld) || (currentPos - mouseWorld).sqrMagnitude <= HOVER_RADIUS_SQ)
        {
            hit = true;
        }
        // Fast swipe interpolation: Line segment between previous and current mouse position
        else if (hasLastMousePos)
        {
            float segmentDistSq = DistanceSqPointToLineSegment(currentPos, lastMouseWorldPos, mouseWorld);
            if (segmentDistSq <= SWIPE_SEGMENT_RADIUS_SQ)
            {
                hit = true;
            }
        }

        lastMouseWorldPos = mouseWorld;
        hasLastMousePos = true;

        if (hit)
        {
            TrySelectTarget();
        }
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        if (IsSelectable)
        {
            TrySelectTarget();
        }
    }

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        if (isSlashed || !isTarget) return;
        isSlashed = true;
        IsDead = true;

        if (slashVFXPrefab != null) Instantiate(slashVFXPrefab, transform.position, Quaternion.identity);
        if (slashSFX != null && PlayerController.Instance != null) PlayerController.Instance.PlaySFX(slashSFX);

        if (spriteRenderer != null) spriteRenderer.enabled = false;
        if (numberText != null) numberText.gameObject.SetActive(false);
        if (col != null) col.enabled = false;

        gameObject.SetActive(false);

        if (boss != null)
        {
            boss.OnTargetPointSlashed(this, deathImpulse);
        }
    }

    // Lightweight stubs to prevent base enemy pathfinding/jumping
    protected override void Movement() { }
    protected override void HandleJump(Vector2 dir) { }
    protected override void HandlePlayerContact(Collision2D collision) { }
    protected override void HandlePlayerTriggerContact(Collider2D other) { }
    protected override bool HandleThrownImpact(Collision2D collision) => false;
    protected override void FlipAndRotate() { }
    protected override void UpdateTargetIndicator() { }

    #endregion

    #region Helpers & Procedural Sprite

    private Camera GetActiveCamera()
    {
        if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
        {
            return cachedCamera;
        }
        cachedCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        return cachedCamera;
    }

    private void EnsureNumberLabel()
    {
        if (numberText != null)
        {
            numberText.fontSize = numberFontSize;
            return;
        }

        numberText = GetComponentInChildren<TextMeshPro>(true);
        if (numberText != null)
        {
            numberText.fontSize = numberFontSize;
            return;
        }

        GameObject textObj = new GameObject("NumberLabel");
        textObj.transform.SetParent(transform, false);
        textObj.transform.localPosition = Vector3.zero;

        numberText = textObj.AddComponent<TextMeshPro>();
        numberText.rectTransform.sizeDelta = new Vector2(1.5f, 1.5f);
        numberText.text = (pointIndex + 1).ToString();
        numberText.fontSize = numberFontSize;
        numberText.alignment = TextAlignmentOptions.Center;
        numberText.fontStyle = FontStyles.Bold;
        numberText.sortingOrder = LABEL_SORTING_ORDER;

        if (fontAsset != null)
        {
            numberText.font = fontAsset;
        }
        else
        {
#if UNITY_EDITOR
            TMP_FontAsset pixelFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/PixelifySans-VariableFont_wght SDF.asset");
            if (pixelFont != null)
            {
                numberText.font = pixelFont;
                fontAsset = pixelFont;
            }
#endif
        }

        numberText.color = Color.white;
    }

    private static float DistanceSqPointToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 line = lineEnd - lineStart;
        float lineLengthSquared = line.sqrMagnitude;
        if (lineLengthSquared < 0.0001f) return (point - lineStart).sqrMagnitude;

        float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / lineLengthSquared);
        Vector2 projection = lineStart + t * line;
        return (point - projection).sqrMagnitude;
    }

    public static Sprite GetOrCreateTargetPointSprite()
    {
        if (cachedTargetSprite != null) return cachedTargetSprite;

        Texture2D tex = new Texture2D(PROCEDURAL_TEX_SIZE, PROCEDURAL_TEX_SIZE, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color core = new Color(1f, 0.95f, 0.8f, 1f);
        Color border = new Color(0.95f, 0.25f, 0.9f, 1f);

        Vector2 center = new Vector2(PROCEDURAL_TEX_SIZE * 0.5f - 0.5f, PROCEDURAL_TEX_SIZE * 0.5f - 0.5f);
        float radius = PROCEDURAL_TEX_SIZE * 0.45f;
        float coreRadius = radius * 0.45f;
        float radiusSq = radius * radius;
        float coreRadiusSq = coreRadius * coreRadius;

        for (int y = 0; y < PROCEDURAL_TEX_SIZE; y++)
        {
            for (int x = 0; x < PROCEDURAL_TEX_SIZE; x++)
            {
                float distSq = (new Vector2(x, y) - center).sqrMagnitude;
                if (distSq <= coreRadiusSq)
                {
                    tex.SetPixel(x, y, core);
                }
                else if (distSq <= radiusSq)
                {
                    tex.SetPixel(x, y, border);
                }
                else
                {
                    tex.SetPixel(x, y, clear);
                }
            }
        }

        tex.Apply();
        cachedTargetSprite = Sprite.Create(tex, new Rect(0, 0, PROCEDURAL_TEX_SIZE, PROCEDURAL_TEX_SIZE), new Vector2(0.5f, 0.5f), 16f);
        return cachedTargetSprite;
    }

    #endregion
}
