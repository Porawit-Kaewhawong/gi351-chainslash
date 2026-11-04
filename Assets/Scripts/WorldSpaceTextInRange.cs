using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

/// <summary>
/// Displays world-space text (e.g. prompts, signs, tutorial hints, dialogues)
/// when the player or target enters a specified proximity range.
/// Features smooth fade/scale transitions, floating bobbing, typewriter effects, and automatic TextMeshPro setup.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/World Space Text In Range")]
public class WorldSpaceTextInRange : MonoBehaviour
{
    #region Types

    public enum AnimationStyle
    {
        [Tooltip("Smoothly fades alpha and scales with an elastic pop.")]
        FadeAndScale,
        [Tooltip("Smoothly fades alpha only.")]
        FadeOnly,
        [Tooltip("Pops scale only.")]
        ScaleOnly,
        [Tooltip("Instantly toggles visibility.")]
        Instant
    }

    #endregion

    #region Serialized Fields

    [Header("Proximity Settings")]
    [Tooltip("Radius around this object in world units within which text becomes visible.")]
    [SerializeField] private float detectionRange = 3.5f;

    [Tooltip("Center offset for the proximity check relative to this transform.")]
    [SerializeField] private Vector2 rangeOffset = Vector2.zero;

    [Tooltip("Optional custom target Transform to track. If left empty, automatically tracks the Player.")]
    [SerializeField] private Transform targetOverride;

    [Tooltip("Tag used to find the player if no direct reference exists.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("If true, checks line of sight so text won't display through solid obstacles/walls.")]
    [SerializeField] private bool requireLineOfSight = false;

    [Tooltip("Layers considered solid obstacles blocking line of sight.")]
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Text Content")]
    [Tooltip("The TextMeshPro or TextMeshProUGUI component displaying the text.")]
    [SerializeField] private TMP_Text textComponent;

    [TextArea(2, 5)]
    [Tooltip("The text message to display.")]
    [SerializeField] private string message = "Press [E] to Interact";

    [Tooltip("Text color tint.")]
    [SerializeField] private Color textColor = Color.white;

    [Header("Font & Typography")]
    [Tooltip("Custom TMP Font Asset (e.g. PixelifySans SDF). If left empty, retains the default or assigned font.")]
    [SerializeField] private TMP_FontAsset fontAsset;

    [Tooltip("Optional custom material preset for the font (e.g. outline, drop shadow, glow material).")]
    [SerializeField] private Material fontMaterial;

    [Tooltip("Font size in world units.")]
    [SerializeField] private float fontSize = 4.5f;

    [Tooltip("Font style (Normal, Bold, Italic, Underline, etc.).")]
    [SerializeField] private FontStyles fontStyle = FontStyles.Normal;

    [Tooltip("Horizontal and vertical text alignment.")]
    [SerializeField] private TextAlignmentOptions alignment = TextAlignmentOptions.Center;

    [Tooltip("Enable font auto-sizing within min/max bounds.")]
    [SerializeField] private bool enableAutoSizing = false;

    [SerializeField] private float fontSizeMin = 2f;
    [SerializeField] private float fontSizeMax = 6f;

    [Tooltip("Additional character spacing.")]
    [SerializeField] private float characterSpacing = 0f;

    [Tooltip("Additional line spacing.")]
    [SerializeField] private float lineSpacing = 0f;

    [Header("Text Outline (Optional)")]
    [Tooltip("Enable outline on the text.")]
    [SerializeField] private bool enableOutline = false;

    [Tooltip("Outline color.")]
    [SerializeField] private Color outlineColor = Color.black;

    [Range(0f, 1f)]
    [Tooltip("Outline thickness ratio.")]
    [SerializeField] private float outlineWidth = 0.2f;

    [Header("Sorting & Visibility")]
    [Tooltip("Sorting layer for the world-space text renderer.")]
    [SerializeField] private string sortingLayerName = "Default";

    [Tooltip("Sorting order for the text (set higher than tilemaps/props, e.g. 10, so it remains visible).")]
    [SerializeField] private int sortingOrder = 10;

    [Header("Display & Animation")]
    [Tooltip("Animation style used when entering and leaving range.")]
    [SerializeField] private AnimationStyle animationStyle = AnimationStyle.FadeAndScale;

    [Tooltip("Duration of the appearance and disappearance transition in seconds.")]
    [SerializeField] private float transitionDuration = 0.25f;

    [Tooltip("Peak bounce scale multiplier when popping into view.")]
    [SerializeField] private float scaleBounceOvershoot = 1.15f;

    [Tooltip("If true, the text bobs subtly up and down while visible in range.")]
    [SerializeField] private bool enableFloatingBob = true;

    [Tooltip("Height amplitude of the floating bob motion in world units.")]
    [SerializeField] private float bobAmplitude = 0.06f;

    [Tooltip("Frequency/speed of the floating bob motion.")]
    [SerializeField] private float bobSpeed = 2.5f;

    [Header("Typewriter (Optional)")]
    [Tooltip("If true, reveals characters one by one when entering range.")]
    [SerializeField] private bool useTypewriter = false;

    [Tooltip("Characters revealed per second in typewriter mode.")]
    [SerializeField] private float charactersPerSecond = 35f;

    [Header("Optional Visuals & Container")]
    [Tooltip("Optional root container holding text, frames, or icons (deactivated when out of range).")]
    [SerializeField] private GameObject displayRoot;

    [Tooltip("Optional background frame or speech bubble sprite (fades together with text).")]
    [SerializeField] private SpriteRenderer backgroundSprite;

    [Tooltip("Optional CanvasGroup if using a World Space Canvas.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Audio & Events")]
    [Tooltip("Audio clip played when entering range.")]
    [SerializeField] private AudioClip showSFX;

    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 0.8f;

    [Tooltip("Event triggered when the player enters detection range.")]
    public UnityEvent onEnterRange;

    [Tooltip("Event triggered when the player exits detection range.")]
    public UnityEvent onExitRange;

    #endregion

    #region Public Properties

    // Public Status
    public bool IsInRange => isInRange;
    public string CurrentText => textComponent != null ? textComponent.text : message;

    #endregion

    #region Private State

    // Internal State
    private bool isInRange = false;
    private Transform cachedTarget;
    private Transform displayRootTransform;
    private MeshRenderer cachedTextMeshRenderer;
    private Vector3 initialLocalPos;
    private Vector3 initialScale;
    private float initialBgAlpha = 1f;
    private Coroutine activeTransitionCoroutine;
    private Coroutine activeTypewriterCoroutine;

    #endregion

    #region Unity Lifecycle

    private void Reset()
    {
        #if UNITY_EDITOR
        if (fontAsset == null)
        {
            fontAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/PixelifySans-VariableFont_wght SDF.asset");
        }
        #endif

        if (textComponent == null)
        {
            textComponent = GetComponent<TMP_Text>();
            if (textComponent == null) textComponent = GetComponentInChildren<TMP_Text>();
        }

        if (textComponent == null)
        {
            CreateDefaultTextObject();
        }
        else
        {
            ApplyFontSettings();
        }
    }

    private void Awake()
    {
        EnsureTextComponent();

        if (displayRoot == null)
        {
            displayRoot = (textComponent != null && textComponent.gameObject != gameObject)
                ? textComponent.gameObject
                : gameObject;
        }

        if (displayRoot != null)
        {
            displayRootTransform = displayRoot.transform;
            initialLocalPos = displayRootTransform.localPosition;
            initialScale = displayRootTransform.localScale;
            if (initialScale == Vector3.zero) initialScale = Vector3.one;
        }
        else
        {
            initialLocalPos = Vector3.zero;
            initialScale = Vector3.one;
        }

        if (backgroundSprite != null)
        {
            initialBgAlpha = backgroundSprite.color.a;
        }

        ApplySorting();
        ApplyFontSettings();
        ApplyMessageContent(false);

        // Start hidden
        SetDisplayVisuals(0f, 0f);
        SetDisplayActive(false);
    }

    private void Start()
    {
        ResolveTarget();
    }

    private void OnValidate()
    {
        if (detectionRange < 0.1f) detectionRange = 0.1f;
        if (transitionDuration < 0.01f) transitionDuration = 0.01f;
        if (bobSpeed < 0f) bobSpeed = 0f;
        if (fontSize < 0.1f) fontSize = 0.1f;
        if (fontSizeMin < 0.1f) fontSizeMin = 0.1f;
        if (fontSizeMax < fontSizeMin) fontSizeMax = fontSizeMin;

        if (textComponent == null)
        {
            textComponent = GetComponent<TMP_Text>();
            if (textComponent == null) textComponent = GetComponentInChildren<TMP_Text>();
        }

        if (textComponent != null && !Application.isPlaying)
        {
            textComponent.text = message;
            textComponent.color = textColor;
            ApplySorting();
            ApplyFontSettings();
        }
    }

    private void Update()
    {
        Transform target = ResolveTarget();
        if (target == null) return;

        Vector2 checkOrigin = (Vector2)transform.position + rangeOffset;
        Vector2 targetPos = target.position;
        float sqrDistance = (targetPos - checkOrigin).sqrMagnitude;

        bool rangeHit = sqrDistance <= (detectionRange * detectionRange);

        // Line of sight check
        if (rangeHit && requireLineOfSight)
        {
            RaycastHit2D hit = Physics2D.Linecast(checkOrigin, targetPos, obstacleLayer);
            if (hit.collider != null)
            {
                rangeHit = false;
            }
        }

        // Handle proximity state changes
        if (rangeHit && !isInRange)
        {
            EnterRange();
        }
        else if (!rangeHit && isInRange)
        {
            ExitRange();
        }

        // Subtle floating bob motion while visible
        if (isInRange && enableFloatingBob && displayRootTransform != null && activeTransitionCoroutine == null)
        {
            float bobOffset = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
            displayRootTransform.localPosition = new Vector3(
                initialLocalPos.x,
                initialLocalPos.y + bobOffset,
                initialLocalPos.z
            );
        }
    }

    #endregion

    #region Proximity Actions

    private void EnterRange()
    {
        isInRange = true;
        onEnterRange?.Invoke();

        if (showSFX != null)
        {
            AudioSource.PlayClipAtPoint(showSFX, transform.position, sfxVolume);
        }

        SetDisplayActive(true);

        if (activeTransitionCoroutine != null) StopCoroutine(activeTransitionCoroutine);
        activeTransitionCoroutine = StartCoroutine(TransitionRoutine(true));

        if (useTypewriter)
        {
            if (activeTypewriterCoroutine != null) StopCoroutine(activeTypewriterCoroutine);
            activeTypewriterCoroutine = StartCoroutine(TypewriterRoutine());
        }
    }

    private void ExitRange()
    {
        isInRange = false;
        onExitRange?.Invoke();

        if (activeTypewriterCoroutine != null)
        {
            StopCoroutine(activeTypewriterCoroutine);
            activeTypewriterCoroutine = null;
        }

        if (activeTransitionCoroutine != null) StopCoroutine(activeTransitionCoroutine);
        activeTransitionCoroutine = StartCoroutine(TransitionRoutine(false));
    }

    #endregion

    #region Transitions & Animations

    private IEnumerator TransitionRoutine(bool showing)
    {
        float duration = animationStyle == AnimationStyle.Instant ? 0.001f : Mathf.Max(0.01f, transitionDuration);
        float elapsed = 0f;

        float startAlpha = GetCurrentAlpha();
        float targetAlpha = showing ? 1f : 0f;

        float startScaleFactor = GetCurrentScaleFactor();
        float targetScaleFactor = showing ? 1f : 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            float currentAlpha;
            float currentScaleFactor;

            switch (animationStyle)
            {
                case AnimationStyle.FadeOnly:
                    currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                    currentScaleFactor = 1f;
                    break;

                case AnimationStyle.ScaleOnly:
                    currentAlpha = 1f;
                    currentScaleFactor = showing
                        ? EvaluateBounce(startScaleFactor, 1f, t, scaleBounceOvershoot)
                        : Mathf.Lerp(startScaleFactor, 0f, t);
                    break;

                case AnimationStyle.FadeAndScale:
                    currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                    currentScaleFactor = showing
                        ? EvaluateBounce(startScaleFactor, 1f, t, scaleBounceOvershoot)
                        : Mathf.Lerp(startScaleFactor, 0f, t);
                    break;

                case AnimationStyle.Instant:
                default:
                    currentAlpha = targetAlpha;
                    currentScaleFactor = targetScaleFactor;
                    break;
            }

            SetDisplayVisuals(currentAlpha, currentScaleFactor);
            yield return null;
        }

        SetDisplayVisuals(targetAlpha, targetScaleFactor);

        if (!showing)
        {
            SetDisplayActive(false);
            if (displayRootTransform != null)
            {
                displayRootTransform.localPosition = initialLocalPos;
            }
        }

        activeTransitionCoroutine = null;
    }

    private float EvaluateBounce(float from, float to, float t, float overshoot)
    {
        float ease = Mathf.Sin(t * Mathf.PI * 0.5f);
        float bounce = Mathf.Sin(t * Mathf.PI) * (overshoot - 1f);
        return Mathf.Lerp(from, to, ease) + bounce;
    }

    private IEnumerator TypewriterRoutine()
    {
        if (textComponent == null) yield break;

        textComponent.maxVisibleCharacters = 0;
        int totalCharacters = textComponent.textInfo != null && textComponent.textInfo.characterCount > 0
            ? textComponent.textInfo.characterCount
            : message.Length;

        WaitForSeconds stepWait = new WaitForSeconds(1f / Mathf.Max(1f, charactersPerSecond));
        int currentVisible = 0;

        while (currentVisible < totalCharacters)
        {
            currentVisible++;
            textComponent.maxVisibleCharacters = currentVisible;
            yield return stepWait;
        }

        textComponent.maxVisibleCharacters = 9999;
        activeTypewriterCoroutine = null;
    }

    private void SetDisplayVisuals(float alpha, float scaleFactor)
    {
        // 1. Text Alpha
        if (textComponent != null)
        {
            Color c = textComponent.color;
            c.a = alpha;
            textComponent.color = c;

            if (enableOutline)
            {
                Color oc = outlineColor;
                oc.a = alpha * outlineColor.a;
                textComponent.outlineColor = oc;
            }
        }

        // 2. Background Sprite Alpha
        if (backgroundSprite != null)
        {
            Color c = backgroundSprite.color;
            c.a = alpha * initialBgAlpha;
            backgroundSprite.color = c;
        }

        // 3. CanvasGroup Alpha
        if (canvasGroup != null)
        {
            canvasGroup.alpha = alpha;
        }

        // 4. Scale
        if (displayRootTransform == null && displayRoot != null)
        {
            displayRootTransform = displayRoot.transform;
        }

        if (displayRootTransform != null)
        {
            displayRootTransform.localScale = initialScale * scaleFactor;
        }
    }

    private void SetDisplayActive(bool active)
    {
        if (displayRoot != null && displayRoot != gameObject)
        {
            displayRoot.SetActive(active);
        }
        else
        {
            if (textComponent != null) textComponent.enabled = active;
            if (backgroundSprite != null) backgroundSprite.enabled = active;
        }
    }

    private float GetCurrentAlpha()
    {
        if (canvasGroup != null) return canvasGroup.alpha;
        if (textComponent != null) return textComponent.color.a;
        if (backgroundSprite != null) return backgroundSprite.color.a;
        return isInRange ? 1f : 0f;
    }

    private float GetCurrentScaleFactor()
    {
        if (displayRootTransform != null && initialScale.x > 0.0001f)
        {
            return displayRootTransform.localScale.x / initialScale.x;
        }
        return isInRange ? 1f : 0f;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Updates the text displayed by this prompt dynamically at runtime.
    /// </summary>
    public void SetText(string newText)
    {
        message = newText;
        ApplyMessageContent(true);
    }

    /// <summary>
    /// Changes the font asset and optionally its size at runtime.
    /// </summary>
    public void SetFont(TMP_FontAsset newFont, float? newSize = null)
    {
        fontAsset = newFont;
        if (newSize.HasValue) fontSize = newSize.Value;
        ApplyFontSettings();
    }

    /// <summary>
    /// Updates font point size in world units.
    /// </summary>
    public void SetFontSize(float newSize)
    {
        fontSize = newSize;
        ApplyFontSettings();
    }

    /// <summary>
    /// Updates font material preset (e.g. outline/glow/shadow).
    /// </summary>
    public void SetFontMaterial(Material newMaterial)
    {
        fontMaterial = newMaterial;
        ApplyFontSettings();
    }

    /// <summary>
    /// Updates text color tint.
    /// </summary>
    public void SetTextColor(Color newColor)
    {
        textColor = newColor;
        if (textComponent != null)
        {
            Color c = newColor;
            c.a = GetCurrentAlpha();
            textComponent.color = c;
        }
    }

    /// <summary>
    /// Forces the text prompt to display regardless of distance.
    /// </summary>
    public void ForceShow()
    {
        EnterRange();
    }

    /// <summary>
    /// Forces the text prompt to hide immediately.
    /// </summary>
    public void ForceHide()
    {
        ExitRange();
    }

    #endregion

    #region Helpers & Gizmos

    private Transform ResolveTarget()
    {
        if (targetOverride != null) return targetOverride;

        if (cachedTarget != null) return cachedTarget;

        if (PlayerController.Instance != null)
        {
            cachedTarget = PlayerController.Instance.transform;
            return cachedTarget;
        }

        if (!string.IsNullOrEmpty(playerTag))
        {
            GameObject playerObj = GameObject.FindWithTag(playerTag);
            if (playerObj != null)
            {
                cachedTarget = playerObj.transform;
                return cachedTarget;
            }
        }

        return null;
    }

    private void ApplyMessageContent(bool updateActiveTypewriter)
    {
        if (textComponent != null)
        {
            textComponent.text = message;
            textComponent.color = textColor;

            if (!useTypewriter || !updateActiveTypewriter)
            {
                textComponent.maxVisibleCharacters = 9999;
            }
        }
    }

    private void ApplySorting()
    {
        if (textComponent != null)
        {
            if (cachedTextMeshRenderer == null || cachedTextMeshRenderer.gameObject != textComponent.gameObject)
            {
                cachedTextMeshRenderer = textComponent.GetComponent<MeshRenderer>();
            }
            if (cachedTextMeshRenderer != null)
            {
                cachedTextMeshRenderer.sortingLayerName = sortingLayerName;
                cachedTextMeshRenderer.sortingOrder = sortingOrder;
            }
        }

        if (backgroundSprite != null)
        {
            backgroundSprite.sortingLayerName = sortingLayerName;
            backgroundSprite.sortingOrder = sortingOrder - 1;
        }
    }

    private void ApplyFontSettings()
    {
        if (textComponent == null) return;

        if (fontAsset != null && textComponent.font != fontAsset)
        {
            textComponent.font = fontAsset;
        }

        if (fontMaterial != null && textComponent.fontSharedMaterial != fontMaterial)
        {
            textComponent.fontSharedMaterial = fontMaterial;
        }

        textComponent.fontSize = fontSize;
        textComponent.fontStyle = fontStyle;
        textComponent.alignment = alignment;
        textComponent.characterSpacing = characterSpacing;
        textComponent.lineSpacing = lineSpacing;

        textComponent.enableAutoSizing = enableAutoSizing;
        if (enableAutoSizing)
        {
            textComponent.fontSizeMin = fontSizeMin;
            textComponent.fontSizeMax = fontSizeMax;
        }

        if (enableOutline)
        {
            textComponent.outlineColor = outlineColor;
            textComponent.outlineWidth = outlineWidth;
        }
    }

    private void EnsureTextComponent()
    {
        if (textComponent != null) return;

        textComponent = GetComponent<TMP_Text>();
        if (textComponent == null)
        {
            textComponent = GetComponentInChildren<TMP_Text>();
        }

        if (textComponent == null)
        {
            CreateDefaultTextObject();
        }
    }

    /// <summary>
    /// Creates a ready-to-use child TextMeshPro GameObject automatically if none was assigned.
    /// </summary>
    [ContextMenu("Create Default World Text Component")]
    public void CreateDefaultTextObject()
    {
        if (textComponent != null) return;

        GameObject textObj = new GameObject("ProximityWorldText");
        Transform textTransform = textObj.transform;
        textTransform.SetParent(transform, false);
        textTransform.localPosition = new Vector3(0f, 1.25f, 0f);

        TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
        tmp.text = message;
        tmp.fontSize = fontSize;
        tmp.fontStyle = fontStyle;
        tmp.alignment = alignment;
        tmp.color = textColor;

        if (fontAsset != null)
        {
            tmp.font = fontAsset;
        }
        else
        {
            #if UNITY_EDITOR
            TMP_FontAsset defaultPixelFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/PixelifySans-VariableFont_wght SDF.asset");
            if (defaultPixelFont != null)
            {
                tmp.font = defaultPixelFont;
                fontAsset = defaultPixelFont;
            }
            #endif
        }

        if (fontMaterial != null)
        {
            tmp.fontSharedMaterial = fontMaterial;
        }

        cachedTextMeshRenderer = tmp.GetComponent<MeshRenderer>();
        if (cachedTextMeshRenderer != null)
        {
            cachedTextMeshRenderer.sortingLayerName = sortingLayerName;
            cachedTextMeshRenderer.sortingOrder = sortingOrder;
        }

        textComponent = tmp;
        displayRoot = textObj;
        displayRootTransform = textTransform;

        ApplyFontSettings();

        Debug.Log($"[WorldSpaceTextInRange] Created child TextMeshPro object on {gameObject.name}.", this);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position + (Vector3)rangeOffset;

        // Draw detection range circle
        Gizmos.color = isInRange ? new Color(0.2f, 1f, 0.4f, 0.65f) : new Color(1f, 0.85f, 0.2f, 0.45f);
        Gizmos.DrawWireSphere(center, detectionRange);

        // Draw line to target if present
        Transform target = ResolveTarget();
        if (target != null)
        {
            Gizmos.color = isInRange ? Color.green : Color.yellow;
            Gizmos.DrawLine(center, target.position);
        }
    }

    #endregion
}
