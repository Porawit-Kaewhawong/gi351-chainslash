using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Dynamic Boss Health Bar UI Controller.
/// 
/// Accepts ONLY the 4 essential UI elements:
/// 1. Title (TMP_Text / Text)
/// 2. Phrase (TMP_Text / Text)
/// 3. Background Bar (GameObject / RectTransform / Image)
/// 4. Fill Bar (Image)
/// 
/// Automatically generates and drives high-impact dynamic features:
/// - Procedural Dual-Bar Damage Ghost (Golden amber lag bar trailing behind slashes)
/// - Procedural 20-Hit Segment Dividers (Thin crisp pixel tick marks across the bar)
/// - Elastic Hit Squish and Directional Impact Shake (Physical visceral punch on hit)
/// - Arcade Hit-Flash on the Fill Bar (Bright white impact spark on damage)
/// - Living Energy Shimmer and Phase-Reactive Colors (Phrase 1 through 5)
/// - Critical Adrenaline Heartbeat Pulse and Text Jitter in Phrase 5 (under 20% HP)
/// - Phrase Shift Cinematic Banner Punch (Elastic scale pop and color flair)
/// - Cinematic Intro Animation (Bar swells from 0 to 100% on encounter start)
/// - Dramatic Defeat Drain and Smooth Dissolve Outro
/// </summary>
public class BossHealthBarUI : MonoBehaviour
{
    [Header("Boss Reference (Auto-detected if left empty)")]
    [SerializeField] private LegendarySamurai boss;

    [Header("The 4 Core UI Elements")]
    [Tooltip("1. Boss Title text component (TextMeshPro or standard UI Text).")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Text legacyTitleText;
    [SerializeField] private string bossTitle = "Legendary Samurai";

    [Tooltip("2. Boss Phrase text component (TextMeshPro or standard UI Text).")]
    [SerializeField] private TMP_Text phraseText;
    [SerializeField] private Text legacyPhraseText;

    [Tooltip("3. Background Bar (GameObject, Panel, or Image).")]
    [SerializeField] private GameObject backgroundBar;

    [Tooltip("4. Health Fill Bar Image (Image with FillMethod set to Horizontal).")]
    [SerializeField] private Image fillBar;

    [Header("Dynamic Dual-Bar (Damage Ghost)")]
    [Tooltip("Automatically generates a damage ghost bar behind the main fill bar.")]
    [SerializeField] private bool enableGhostBar = true;
    [SerializeField] private Color ghostBarColor = new Color(1.0f, 0.78f, 0.18f, 0.95f); // Golden Amber
    [SerializeField] private float ghostGraceDelay = 0.35f;
    [SerializeField] private float ghostCatchupSpeed = 2.4f;

    [Header("Dynamic 20-Hit Segment Dividers")]
    [Tooltip("Generates thin pixel dividers across the bar marking each of the 20 hits.")]
    [SerializeField] private bool enableSegmentDividers = true;
    [SerializeField] private float segmentDividerWidth = 1.5f;
    [SerializeField] private Color segmentDividerColor = new Color(0.04f, 0.01f, 0.02f, 0.65f);

    [Header("Dynamic Elastic Hit Squish and Impact Shake")]
    [Tooltip("Applies physical elastic scale squish and position shake on hit.")]
    [SerializeField] private bool enableHitReaction = true;
    [SerializeField] private float hitShakeMagnitude = 4.0f;
    [SerializeField] private float hitSquishMagnitude = 0.045f;
    [SerializeField] private float hitReactionDuration = 0.16f;

    [Header("Cinematic Intro and Outro")]
    [Tooltip("Plays a dynamic swelling intro animation on boss encounter start.")]
    [SerializeField] private bool enableIntroAnimation = true;
    [SerializeField] private float introDuration = 0.85f;
    [SerializeField] private float defeatDrainDuration = 0.55f;
    [SerializeField] private float defeatFadeDelay = 1.2f;

    [Header("Phrase Display and Attack Names")]
    [Tooltip("Prefix before the phrase number (e.g. 'PHRASE ' or 'Phrase ').")]
    [SerializeField] private string phrasePrefix = "PHRASE ";
    [Tooltip("Whether to display the unlocked attack pattern name in the phrase text.")]
    [SerializeField] private bool showAttackName = true;
    [SerializeField] private string[] phraseAttackNames = new string[]
    {
        "DASH-SLASH",
        "STOMP",
        "360 SHURIKEN",
        "WIND SLASH",
        "HIDDEN STAB"
    };

    [Header("Phrase Color Themes")]
    [Tooltip("Colors for Fill Bar across Phrase 1 to Phrase 5.")]
    [SerializeField] private Color[] phraseFillColors = new Color[]
    {
        new Color(0.90f, 0.11f, 0.22f, 1.0f), // Phrase 1 (100% - 80%): Crimson Ruby
        new Color(0.96f, 0.32f, 0.12f, 1.0f), // Phrase 2 ( 80% - 60%): Flame Orange-Red
        new Color(0.98f, 0.55f, 0.00f, 1.0f), // Phrase 3 ( 60% - 40%): Sunburst Gold
        new Color(0.68f, 0.15f, 0.78f, 1.0f), // Phrase 4 ( 40% - 20%): Enraged Violet
        new Color(1.00f, 0.05f, 0.20f, 1.0f)  // Phrase 5 ( 20% -  0%): Calamity Blood Red
    };

    [Tooltip("Colors for Phrase Text across Phrase 1 to Phrase 5.")]
    [SerializeField] private Color[] phraseTextColors = new Color[]
    {
        new Color(0.95f, 0.95f, 0.95f, 1.0f), // Phrase 1: Pure Silver / White
        new Color(1.00f, 0.84f, 0.31f, 1.0f), // Phrase 2: Radiant Gold
        new Color(1.00f, 0.65f, 0.15f, 1.0f), // Phrase 3: Fiery Tangerine
        new Color(0.85f, 0.55f, 0.95f, 1.0f), // Phrase 4: Mystic Violet
        new Color(1.00f, 0.20f, 0.25f, 1.0f)  // Phrase 5: Burning Crimson
    };

    // Procedural runtime components
    private Image ghostBar;
    private GameObject segmentContainer;

    // Runtime tracking
    private int currentHits = 20;
    private int maxHits = 20;
    private float targetFillRatio = 1f;
    private float displayFillRatio = 1f;
    private float ghostFillRatio = 1f;
    private float ghostDelayTimer = 0f;
    private int currentPhase = 1;
    private bool isDefeated = false;
    private bool isIntroPlaying = false;

    // Visual animation states
    private Color currentTargetColor;
    private float hitFlashTimer = 0f;
    private Coroutine phrasePunchCoroutine;
    private Coroutine reactionCoroutine;
    private Coroutine introCoroutine;
    private RectTransform shakeRect;
    private RectTransform scaleRect;
    private Vector2 defaultShakePos = Vector2.zero;
    private Vector3 defaultPhraseLocalPos = Vector3.zero;
    private Transform activePhraseTransform;

    private static Sprite cachedPixelSprite;

    // Public properties
    public TMP_Text TitleText { get => titleText; set => titleText = value; }
    public TMP_Text PhraseText { get => phraseText; set => phraseText = value; }
    public GameObject BackgroundBar { get => backgroundBar; set => backgroundBar = value; }
    public Image FillBar { get => fillBar; set => fillBar = value; }

    private void Awake()
    {
        if (boss == null)
        {
            boss = GetComponentInParent<LegendarySamurai>();
            if (boss == null) boss = FindFirstObjectByType<LegendarySamurai>();
        }

        ConfigureFillBar();
        CacheRects();
    }

    private void Start()
    {
        if (boss != null)
        {
            Initialize(boss);
        }
        else
        {
            // Initial editor / scene preview
            UpdateTitleDisplay();
            UpdatePhraseDisplay(1, false);
            ApplyFillRatioInstant(1f);
        }
    }

    private void CacheRects()
    {
        if (backgroundBar != null)
        {
            shakeRect = backgroundBar.GetComponent<RectTransform>();
        }
        if (shakeRect == null)
        {
            shakeRect = GetComponent<RectTransform>();
        }
        if (shakeRect != null)
        {
            defaultShakePos = shakeRect.anchoredPosition;
            scaleRect = shakeRect;
        }

        activePhraseTransform = phraseText != null ? phraseText.transform : (legacyPhraseText != null ? legacyPhraseText.transform : null);
        if (activePhraseTransform != null)
        {
            defaultPhraseLocalPos = activePhraseTransform.localPosition;
        }
    }

    private void ConfigureFillBar()
    {
        if (fillBar != null)
        {
            if (fillBar.type != Image.Type.Filled)
            {
                fillBar.type = Image.Type.Filled;
                fillBar.fillMethod = Image.FillMethod.Horizontal;
                fillBar.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
        }
    }

    /// <summary>
    /// Direct shortcut to assign references programmatically.
    /// </summary>
    public void AssignElements(TMP_Text title, TMP_Text phrase, GameObject bgBar, Image fill)
    {
        if (title != null) titleText = title;
        if (phrase != null) phraseText = phrase;
        if (bgBar != null) backgroundBar = bgBar;
        if (fill != null) fillBar = fill;

        ConfigureFillBar();
        CacheRects();
    }

    public void AssignDirectElements(TMP_Text title, TMP_Text phrase, GameObject bgBar, Image fill)
    {
        AssignElements(title, phrase, bgBar, fill);
    }

    /// <summary>
    /// Binds to LegendarySamurai and initializes health, procedural layers, and events.
    /// </summary>
    public void Initialize(LegendarySamurai targetBoss)
    {
        boss = targetBoss;
        if (boss == null) return;

        maxHits = Mathf.Max(1, boss.MaxBossHealth);
        currentHits = boss.CurrentBossHealth;
        targetFillRatio = currentHits / (float)maxHits;
        displayFillRatio = targetFillRatio;
        ghostFillRatio = targetFillRatio;
        currentPhase = boss.CurrentPhase;
        isDefeated = false;

        ConfigureFillBar();
        CacheRects();

        // Procedural Dynamic Layers
        CreateGhostBar();
        CreateSegmentDividers();

        // Bind events
        boss.onBossHealthChanged.RemoveListener(OnHealthChanged);
        boss.onBossHealthChanged.AddListener(OnHealthChanged);

        boss.onBossDefeated.RemoveListener(OnBossDefeated);
        boss.onBossDefeated.AddListener(OnBossDefeated);

        // Apply visual initial state
        if (backgroundBar != null) backgroundBar.SetActive(true);
        UpdateTitleDisplay();
        UpdatePhraseDisplay(currentPhase, false);
        UpdateFillColor(currentPhase);

        if (enableIntroAnimation)
        {
            if (introCoroutine != null) StopCoroutine(introCoroutine);
            introCoroutine = StartCoroutine(IntroSequenceRoutine());
        }
        else
        {
            ApplyFillRatioInstant(targetFillRatio);
        }
    }

    private void CreateGhostBar()
    {
        if (!enableGhostBar || fillBar == null) return;

        Transform parent = fillBar.transform.parent;
        Transform existing = parent.Find("GhostLagBar");
        if (existing != null)
        {
            ghostBar = existing.GetComponent<Image>();
            if (ghostBar != null) return;
        }

        GameObject ghostObj = new GameObject("GhostLagBar");
        RectTransform ghostRect = ghostObj.AddComponent<RectTransform>();
        ghostRect.SetParent(parent, false);

        RectTransform fillRect = fillBar.rectTransform;
        ghostRect.anchorMin = fillRect.anchorMin;
        ghostRect.anchorMax = fillRect.anchorMax;
        ghostRect.pivot = fillRect.pivot;
        ghostRect.sizeDelta = fillRect.sizeDelta;
        ghostRect.anchoredPosition = fillRect.anchoredPosition;

        // Place immediately behind fillBar in hierarchy
        int fillIndex = fillBar.transform.GetSiblingIndex();
        ghostObj.transform.SetSiblingIndex(fillIndex);

        ghostBar = ghostObj.AddComponent<Image>();
        ghostBar.sprite = fillBar.sprite;
        ghostBar.type = Image.Type.Filled;
        ghostBar.fillMethod = fillBar.fillMethod;
        ghostBar.fillOrigin = fillBar.fillOrigin;
        ghostBar.color = ghostBarColor;
        ghostBar.fillAmount = displayFillRatio;
    }

    private void CreateSegmentDividers()
    {
        if (!enableSegmentDividers || fillBar == null) return;

        Transform parent = fillBar.transform.parent;
        Transform existing = parent.Find("SegmentDividers");
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        segmentContainer = new GameObject("SegmentDividers");
        RectTransform containerRect = segmentContainer.AddComponent<RectTransform>();
        containerRect.SetParent(parent, false);

        RectTransform fillRect = fillBar.rectTransform;
        containerRect.anchorMin = fillRect.anchorMin;
        containerRect.anchorMax = fillRect.anchorMax;
        containerRect.pivot = fillRect.pivot;
        containerRect.sizeDelta = fillRect.sizeDelta;
        containerRect.anchoredPosition = fillRect.anchoredPosition;

        // Place in front of fillBar
        containerRect.SetSiblingIndex(fillBar.transform.GetSiblingIndex() + 1);

        Sprite pixelSprite = GetOrCreatePixelSprite();
        int dividerCount = maxHits - 1;

        for (int i = 1; i <= dividerCount; i++)
        {
            float ratio = i / (float)maxHits;
            GameObject divObj = new GameObject($"Divider_{i}");
            RectTransform divRect = divObj.AddComponent<RectTransform>();
            divRect.SetParent(containerRect, false);
            divRect.anchorMin = new Vector2(ratio, 0f);
            divRect.anchorMax = new Vector2(ratio, 1f);
            divRect.pivot = new Vector2(0.5f, 0.5f);
            divRect.sizeDelta = new Vector2(segmentDividerWidth, 0f);
            divRect.anchoredPosition = Vector2.zero;

            Image divImg = divObj.AddComponent<Image>();
            divImg.sprite = pixelSprite;
            divImg.color = segmentDividerColor;
        }
    }

    private void Update()
    {
        if (isDefeated || isIntroPlaying) return;

        // 1. Smooth Eased Drainage of Main Fill Bar
        if (!Mathf.Approximately(displayFillRatio, targetFillRatio))
        {
            displayFillRatio = Mathf.MoveTowards(displayFillRatio, targetFillRatio, 3.8f * Time.unscaledDeltaTime);
            if (fillBar != null)
            {
                fillBar.fillAmount = displayFillRatio;
            }
        }

        // 2. Dual-Bar Ghost Catch-Up
        if (ghostBar != null)
        {
            if (ghostDelayTimer > 0f)
            {
                ghostDelayTimer -= Time.unscaledDeltaTime;
            }
            else if (ghostFillRatio > displayFillRatio)
            {
                ghostFillRatio = Mathf.MoveTowards(ghostFillRatio, displayFillRatio, ghostCatchupSpeed * Time.unscaledDeltaTime);
                ghostBar.fillAmount = ghostFillRatio;
            }
            else
            {
                ghostFillRatio = displayFillRatio;
                ghostBar.fillAmount = ghostFillRatio;
            }
        }

        // 3. Dynamic Hit Impact Flash and Phase 5 Heartbeat Pulse
        if (fillBar != null)
        {
            if (hitFlashTimer > 0f)
            {
                hitFlashTimer -= Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(hitFlashTimer / 0.08f);
                fillBar.color = Color.Lerp(currentTargetColor, Color.white, t);
            }
            else if (currentPhase >= 5)
            {
                // Phrase 5 Critical Adrenaline Heartbeat Pulse
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f);
                Color dangerPulseColor = Color.Lerp(currentTargetColor, new Color(1f, 0.55f, 0.6f, 1f), pulse * 0.55f);
                fillBar.color = dangerPulseColor;

                // Subtle urgency text jitter
                if (activePhraseTransform != null)
                {
                    Vector2 jitter = Random.insideUnitCircle * 0.85f;
                    activePhraseTransform.localPosition = defaultPhraseLocalPos + (Vector3)jitter;
                }
            }
            else
            {
                // Living subtle energy shimmer along the bar
                float shimmer = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);
                Color livingColor = Color.Lerp(currentTargetColor, currentTargetColor * 1.12f, shimmer * 0.18f);
                fillBar.color = livingColor;

                if (activePhraseTransform != null && activePhraseTransform.localPosition != defaultPhraseLocalPos)
                {
                    activePhraseTransform.localPosition = defaultPhraseLocalPos;
                }
            }
        }
    }

    private void OnHealthChanged(int hitsRemaining, int totalHits)
    {
        if (isDefeated) return;

        currentHits = Mathf.Max(0, hitsRemaining);
        maxHits = Mathf.Max(1, totalHits);
        targetFillRatio = currentHits / (float)maxHits;

        // Reset ghost grace delay so damage taken is visibly held on ghost bar
        ghostDelayTimer = ghostGraceDelay;

        // Trigger arcade white hit-flash on the bar
        hitFlashTimer = 0.08f;

        // Trigger physical elastic squish and directional impact shake
        TriggerHitReaction();

        // Check if boss transitioned to a new phrase
        int newPhase = boss != null ? boss.CurrentPhase : CalculatePhase(currentHits, maxHits);
        if (newPhase != currentPhase)
        {
            currentPhase = newPhase;
            UpdatePhraseDisplay(currentPhase, true); // Dramatic punch-scale banner pop!
            UpdateFillColor(currentPhase);
        }
    }

    private void UpdateTitleDisplay()
    {
        if (titleText != null && !string.IsNullOrEmpty(bossTitle))
        {
            titleText.text = bossTitle;
        }
        if (legacyTitleText != null && !string.IsNullOrEmpty(bossTitle))
        {
            legacyTitleText.text = bossTitle;
        }
    }

    private void UpdatePhraseDisplay(int phaseNumber, bool animatePunch)
    {
        phaseNumber = Mathf.Clamp(phaseNumber, 1, 5);

        string text;
        if (showAttackName && phaseNumber - 1 < phraseAttackNames.Length)
        {
            text = $"{phrasePrefix}{phaseNumber} - {phraseAttackNames[phaseNumber - 1]}";
        }
        else
        {
            text = $"{phrasePrefix}{phaseNumber}";
        }

        Color textColor = (phraseTextColors != null && phaseNumber - 1 < phraseTextColors.Length)
            ? phraseTextColors[phaseNumber - 1]
            : Color.white;

        if (phraseText != null)
        {
            phraseText.text = text;
            phraseText.color = textColor;
        }
        if (legacyPhraseText != null)
        {
            legacyPhraseText.text = text;
            legacyPhraseText.color = textColor;
        }

        if (animatePunch && activePhraseTransform != null)
        {
            if (phrasePunchCoroutine != null) StopCoroutine(phrasePunchCoroutine);
            phrasePunchCoroutine = StartCoroutine(PhrasePunchRoutine(activePhraseTransform, textColor));
        }
    }

    private IEnumerator PhrasePunchRoutine(Transform targetTransform, Color targetColor)
    {
        Vector3 baseScale = Vector3.one;
        float duration = 0.32f;
        float elapsed = 0f;

        // Momentary golden/white phase-shift flash on text
        if (phraseText != null) phraseText.color = Color.white;
        if (legacyPhraseText != null) legacyPhraseText.color = Color.white;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;

            // Elastic overshoot bounce (1.45x -> 0.92x -> 1.0x)
            float scaleMultiplier;
            if (t < 0.35f)
            {
                scaleMultiplier = Mathf.Lerp(1.0f, 1.45f, t / 0.35f);
            }
            else
            {
                float settleT = (t - 0.35f) / 0.65f;
                scaleMultiplier = Mathf.Lerp(1.45f, 1.0f, Mathf.SmoothStep(0f, 1f, settleT));
            }

            targetTransform.localScale = baseScale * scaleMultiplier;

            // Settle text color back to phase color
            Color c = Color.Lerp(Color.white, targetColor, t);
            if (phraseText != null) phraseText.color = c;
            if (legacyPhraseText != null) legacyPhraseText.color = c;

            yield return null;
        }

        targetTransform.localScale = baseScale;
        if (phraseText != null) phraseText.color = targetColor;
        if (legacyPhraseText != null) legacyPhraseText.color = targetColor;
        phrasePunchCoroutine = null;
    }

    private void UpdateFillColor(int phaseNumber)
    {
        int idx = Mathf.Clamp(phaseNumber - 1, 0, phraseFillColors.Length - 1);
        currentTargetColor = phraseFillColors[idx];

        if (fillBar != null && hitFlashTimer <= 0f)
        {
            fillBar.color = currentTargetColor;
        }
    }

    private void ApplyFillRatioInstant(float ratio)
    {
        targetFillRatio = Mathf.Clamp01(ratio);
        displayFillRatio = targetFillRatio;
        ghostFillRatio = targetFillRatio;

        if (fillBar != null) fillBar.fillAmount = displayFillRatio;
        if (ghostBar != null) ghostBar.fillAmount = ghostFillRatio;
    }

    private void TriggerHitReaction()
    {
        if (!enableHitReaction) return;
        if (reactionCoroutine != null) StopCoroutine(reactionCoroutine);
        reactionCoroutine = StartCoroutine(HitSquishAndShakeRoutine());
    }

    private IEnumerator HitSquishAndShakeRoutine()
    {
        float timer = 0f;
        float duration = hitReactionDuration;

        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            float progress = timer / duration;
            float damper = 1f - progress;

            // 1. Directional impact shake
            if (shakeRect != null)
            {
                float offsetX = Random.Range(-hitShakeMagnitude, hitShakeMagnitude) * damper;
                float offsetY = Random.Range(-hitShakeMagnitude * 0.6f, hitShakeMagnitude * 0.6f) * damper;
                shakeRect.anchoredPosition = defaultShakePos + new Vector2(offsetX, offsetY);
            }

            // 2. Physical elastic squish (horizontal compress and vertical pop)
            if (scaleRect != null)
            {
                float wave = Mathf.Sin(progress * Mathf.PI * 2f);
                float squishX = 1f + wave * hitSquishMagnitude * damper;
                float squishY = 1f - wave * (hitSquishMagnitude * 0.7f) * damper;
                scaleRect.localScale = new Vector3(squishX, squishY, 1f);
            }

            yield return null;
        }

        if (shakeRect != null) shakeRect.anchoredPosition = defaultShakePos;
        if (scaleRect != null) scaleRect.localScale = Vector3.one;
        reactionCoroutine = null;
    }

    private IEnumerator IntroSequenceRoutine()
    {
        isIntroPlaying = true;

        ApplyFillRatioInstant(0f);

        CanvasGroup cg = GetOrAddCanvasGroup();
        if (cg != null) cg.alpha = 0f;

        yield return new WaitForSeconds(0.12f);

        float elapsed = 0f;
        while (elapsed < introDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / introDuration);

            // Cubic ease out
            float ease = 1f - Mathf.Pow(1f - t, 3f);

            if (cg != null) cg.alpha = ease;

            float currentFill = Mathf.Lerp(0f, targetFillRatio, ease);
            if (fillBar != null) fillBar.fillAmount = currentFill;
            if (ghostBar != null) ghostBar.fillAmount = currentFill;

            yield return null;
        }

        if (cg != null) cg.alpha = 1f;
        ApplyFillRatioInstant(targetFillRatio);
        isIntroPlaying = false;
        introCoroutine = null;
    }

    private CanvasGroup GetOrAddCanvasGroup()
    {
        CanvasGroup cg = GetComponent<CanvasGroup>();
        if (cg == null && backgroundBar != null)
        {
            cg = backgroundBar.GetComponent<CanvasGroup>();
            if (cg == null) cg = backgroundBar.AddComponent<CanvasGroup>();
        }
        if (cg == null)
        {
            cg = gameObject.AddComponent<CanvasGroup>();
        }
        return cg;
    }

    private int CalculatePhase(int hits, int total)
    {
        float hpPercent = (float)hits / total;
        if (hpPercent > 0.80f) return 1;
        if (hpPercent > 0.60f) return 2;
        if (hpPercent > 0.40f) return 3;
        if (hpPercent > 0.20f) return 4;
        return 5;
    }

    private void OnBossDefeated()
    {
        if (isDefeated) return;
        isDefeated = true;

        if (phraseText != null)
        {
            phraseText.text = "<color=#FFD700><b>DEFEATED</b></color>";
            phraseText.color = new Color(1f, 0.84f, 0.2f, 1f);
        }
        if (legacyPhraseText != null)
        {
            legacyPhraseText.text = "DEFEATED";
            legacyPhraseText.color = new Color(1f, 0.84f, 0.2f, 1f);
        }

        if (activePhraseTransform != null)
        {
            StartCoroutine(PhrasePunchRoutine(activePhraseTransform, new Color(1f, 0.84f, 0.2f, 1f)));
        }

        StartCoroutine(DefeatOutroRoutine());
    }

    private IEnumerator DefeatOutroRoutine()
    {
        // 1. Victory gold flash across both bars
        if (fillBar != null) fillBar.color = new Color(1f, 0.95f, 0.4f, 1f);
        if (ghostBar != null) ghostBar.color = new Color(1f, 0.85f, 0.2f, 1f);

        // 2. Rapid drain to zero with quadratic acceleration
        float drainTimer = 0f;
        float drainDuration = defeatDrainDuration;
        float startFill = displayFillRatio;

        while (drainTimer < drainDuration)
        {
            drainTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(drainTimer / drainDuration);
            float f = Mathf.Lerp(startFill, 0f, 1f - Mathf.Pow(1f - t, 2f));

            if (fillBar != null) fillBar.fillAmount = f;
            if (ghostBar != null) ghostBar.fillAmount = f;

            yield return null;
        }

        if (fillBar != null) fillBar.fillAmount = 0f;
        if (ghostBar != null) ghostBar.fillAmount = 0f;

        // 3. Victory pause
        yield return new WaitForSeconds(defeatFadeDelay);

        // 4. Smooth dissolve fade out of the entire health bar
        CanvasGroup group = GetOrAddCanvasGroup();
        if (group != null)
        {
            float fadeTimer = 0f;
            float fadeDuration = 0.85f;
            while (fadeTimer < fadeDuration)
            {
                fadeTimer += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(1f, 0f, fadeTimer / fadeDuration);
                yield return null;
            }
            group.alpha = 0f;
        }
    }

    private static Sprite GetOrCreatePixelSprite()
    {
        if (cachedPixelSprite != null) return cachedPixelSprite;
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        cachedPixelSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return cachedPixelSprite;
    }

    private void OnDestroy()
    {
        if (boss != null)
        {
            boss.onBossHealthChanged.RemoveListener(OnHealthChanged);
            boss.onBossDefeated.RemoveListener(OnBossDefeated);
        }
    }
}

