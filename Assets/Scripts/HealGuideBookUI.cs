using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Self-contained Top-Right Ancient Healing Scroll / 2-Page Tome UI.
/// - Displays a compact ancient scroll button at the top-right corner of the screen.
/// - Clicking the scroll (or pressing B) unfurls an ancient 2-page stitched parchment tome
///   showing easy-to-follow seals for Minor Heal (Plus + on Left Page) and Major Heal (Heart on Right Page),
///   complete with weathered parchment texture, red wax/hanko seal stamps, and animated brush-stroke guides.
/// </summary>
public class HealGuideBookUI : MonoBehaviour
{
    #region Static Access & State

    public static HealGuideBookUI Instance { get; private set; }
    public static bool IsBookOpen => Instance != null && Instance.isOpen;
    public static bool IsPointerOverBookButton => Instance != null && Instance.isHoveringToggleButton;

    public static HealGuideBookUI EnsureInstance(PlayerController player)
    {
        if (Instance != null)
        {
            Instance.BindPlayer(player);
            return Instance;
        }

        HealGuideBookUI existing = FindFirstObjectByType<HealGuideBookUI>();
        if (existing != null)
        {
            existing.BindPlayer(player);
            return existing;
        }

        GameObject root = new GameObject("HealGuideBookUI");
        HealGuideBookUI book = root.AddComponent<HealGuideBookUI>();
        book.BindPlayer(player);
        return book;
    }

    #endregion

    #region Serialized Fields

    [Header("Controls & Behavior")]
    [Tooltip("Keyboard shortcut to open/close the Ancient Healing Tome.")]
    [SerializeField] private Key toggleKey = Key.B;
    [Tooltip("If true, pauses game time while reading the tome.")]
    [SerializeField] private bool pauseGameWhileReading = true;
    [Tooltip("If false (default), resets every new game / Play session so starting a new game shows the wiggle & green aurora again until opened once.")]
    [SerializeField] private bool saveFirstTimeState = false;
    [Tooltip("Sorting order of the overlay canvas.")]
    [SerializeField] private int canvasSortingOrder = 120;

    [Header("Top-Right Scroll Button Layout")]
    [SerializeField] private Vector2 buttonTopRightMargin = new Vector2(28f, 28f);
    [SerializeField] private Vector2 buttonSize = new Vector2(92f, 96f);

    [Header("Optional Custom Font & Ancient Tome Text")]
    [Tooltip("Optional custom TMP Font Asset (assign a Thai-capable font here if you customize text to Thai).")]
    [SerializeField] private TMP_FontAsset customFont;
    [SerializeField] private string tomeBannerTitle = "~ HEAL GUIDE ~";
    [SerializeField] private string leftPageTitle = "CROSS 2 LINES";
    [SerializeField] private string leftPageTag = "SMALL HEAL";
    [SerializeField] private string leftPageSteps = "Doesn't have to be a perfect +\nJust draw any 2 lines that cross!";
    [SerializeField] private string rightPageTitle = "DRAW A HEART";
    [SerializeField] private string rightPageTag = "BIG HEAL";
    [SerializeField] private string rightPageSteps = "Draw a Heart shape\nto heal a lot more HP!";

    [Header("Audio")]
    [Tooltip("Optional custom SFX when opening the scroll (auto-synthesizes a parchment unfurl sound if empty).")]
    [SerializeField] private AudioClip bookOpenSFX;
    [Tooltip("Optional custom SFX when closing the scroll (auto-synthesizes a parchment roll-up sound if empty).")]
    [SerializeField] private AudioClip bookCloseSFX;
    [Range(0f, 1f)] [SerializeField] private float bookSFXVolume = 0.85f;

    #endregion

    #region Private UI & Animation State

    private const string HasOpenedBookPrefKey = "HealGuideBook_HasOpenedOnce";

    private PlayerController boundPlayer;
    private AudioSource uiAudioSource;
    private Canvas rootCanvas;
    private RectTransform bookButtonRect;
    private GameObject bookButtonObj;
    private GameObject auroraContainerObj;
    private RectTransform auroraOuterRect;
    private RectTransform auroraInnerRect;
    private Image auroraOuterImg;
    private Image auroraInnerImg;
    private RectTransform[] auroraWisps;
    private Image[] auroraWispImages;
    private GameObject bookOverlayObj;
    private CanvasGroup bookOverlayCanvasGroup;
    private RectTransform bookSpreadRect;

    private TMP_Text leftStatsText;
    private TMP_Text rightStatsText;
    private RectTransform plusGuideDot;
    private RectTransform heartGuideDot;
    private RectTransform[] plusSparkleTrail;
    private RectTransform[] heartSparkleTrail;

    private bool isOpen;
    private bool hasBeenOpenedOnce;
    private bool isHoveringToggleButton;
    private float attentionAnimTime;
    private float savedTimeScale = 1f;
    private float savedFixedDeltaTime = 0.02f;
    private float strokeAnimTime;
    private Coroutine transitionCoroutine;

    // Cached procedural pixel-art sprites & audio
    private static Sprite cachedScrollIconSprite;
    private static Sprite cachedParchmentSprite;
    private static Sprite cachedRedSealSprite;
    private static Sprite[] cachedCrossExampleSprites;
    private static Sprite cachedHeartDiagramSprite;
    private static Sprite cachedWhitePixelSprite;
    private static Sprite cachedGlowDotSprite;
    private static Sprite cachedAuroraGlowSprite;
    private static AudioClip cachedScrollOpenClip;
    private static AudioClip cachedScrollCloseClip;

    private static readonly Vector2[] PlusStrokePath = new Vector2[]
    {
        new Vector2( 0.0f,  0.68f),
        new Vector2( 0.0f, -0.68f),
        new Vector2(-0.68f, 0.0f),
        new Vector2( 0.68f, 0.0f)
    };

    private static readonly Vector2[] HeartStrokePath = new Vector2[]
    {
        new Vector2( 0.00f,  0.32f),
        new Vector2(-0.28f,  0.62f),
        new Vector2(-0.62f,  0.50f),
        new Vector2(-0.68f,  0.14f),
        new Vector2(-0.42f, -0.26f),
        new Vector2( 0.00f, -0.68f),
        new Vector2( 0.42f, -0.26f),
        new Vector2( 0.68f,  0.14f),
        new Vector2( 0.62f,  0.50f),
        new Vector2( 0.28f,  0.62f),
        new Vector2( 0.00f,  0.32f)
    };

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        hasBeenOpenedOnce = saveFirstTimeState && PlayerPrefs.GetInt(HasOpenedBookPrefKey, 0) == 1;

        EnsureProceduralSprites();
        EnsureAudioSource();
        BuildUI();
        SetBookImmediate(false);
        SetAuroraActive(!hasBeenOpenedOnce);
    }

    [ContextMenu("Reset First-Time Book Attention")]
    public void ResetFirstTimeAttention()
    {
        PlayerPrefs.DeleteKey(HasOpenedBookPrefKey);
        hasBeenOpenedOnce = false;
        attentionAnimTime = 0f;
        SetAuroraActive(true);
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
        if (isOpen && pauseGameWhileReading)
        {
            Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;
            Time.fixedDeltaTime = savedFixedDeltaTime > 0f ? savedFixedDeltaTime : 0.02f;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        isHoveringToggleButton = false;
        CloseBook();

        bool isMenuScene = scene.buildIndex == 0 || FindFirstObjectByType<MainMenu>() != null;
        if (isMenuScene)
        {
            if (rootCanvas != null) rootCanvas.enabled = false;

            // Reset attention state on Main Menu so starting a New Game shows the wiggle & green aurora again
            if (!saveFirstTimeState)
            {
                hasBeenOpenedOnce = false;
                attentionAnimTime = 0f;
            }
        }
        else
        {
            if (rootCanvas != null) rootCanvas.enabled = true;
            SetAuroraActive(!hasBeenOpenedOnce);
            BindPlayer(PlayerController.Instance);
            RefreshStatsText();
        }
    }

    private void Update()
    {
        if (rootCanvas == null || !rootCanvas.enabled) return;

        if (bookButtonRect != null)
        {
            float baseTargetScale = isHoveringToggleButton ? 1.10f : 1.0f;
            float wiggleScaleBoost = 0f;
            float targetRotZ = 0f;

            // First-time attention wiggle + waving green Aurora glow until the player opens the book once
            if (!hasBeenOpenedOnce && !isOpen)
            {
                attentionAnimTime += Time.unscaledDeltaTime;

                // Periodic playful wiggle burst every ~1.85 seconds
                float cycle = attentionAnimTime % 1.85f;
                if (cycle < 0.62f)
                {
                    float burstT = cycle / 0.62f;
                    float env = Mathf.Sin(burstT * Mathf.PI);
                    targetRotZ = Mathf.Sin(burstT * Mathf.PI * 5f) * 11f * env;
                    wiggleScaleBoost = 0.08f * env;
                }

                UpdateGreenAuroraEffect();
            }

            float targetScale = baseTargetScale + wiggleScaleBoost;
            float s = Mathf.Lerp(bookButtonRect.localScale.x, targetScale, Time.unscaledDeltaTime * 14f);
            bookButtonRect.localScale = new Vector3(s, s, 1f);

            Quaternion desiredRot = Quaternion.Euler(0f, 0f, targetRotZ);
            bookButtonRect.localRotation = Quaternion.Lerp(bookButtonRect.localRotation, desiredRot, Time.unscaledDeltaTime * 18f);
        }

        if (Keyboard.current != null)
        {
            if (toggleKey != Key.None && Keyboard.current[toggleKey].wasPressedThisFrame)
            {
                if (boundPlayer == null || !boundPlayer.IsDead)
                {
                    ToggleBook();
                }
            }
            else if (isOpen && Keyboard.current[Key.Escape].wasPressedThisFrame)
            {
                CloseBook();
            }
        }

        if (isOpen)
        {
            strokeAnimTime += Time.unscaledDeltaTime;
            UpdatePlusStrokeGuide();
            UpdateHeartStrokeGuide();
        }
    }

    #endregion

    #region Public API

    public void BindPlayer(PlayerController player)
    {
        boundPlayer = player != null ? player : PlayerController.Instance;
        RefreshStatsText();
    }

    public void ToggleBook()
    {
        if (isOpen) CloseBook();
        else OpenBook();
    }

    public void OpenBook()
    {
        if (isOpen) return;
        isOpen = true;
        strokeAnimTime = 0f;

        // Mark first-time guide book as read so attention wiggle & green aurora disappear after reading
        if (!hasBeenOpenedOnce)
        {
            hasBeenOpenedOnce = true;
            if (saveFirstTimeState)
            {
                PlayerPrefs.SetInt(HasOpenedBookPrefKey, 1);
                PlayerPrefs.Save();
            }
            SetAuroraActive(false);
            if (bookButtonRect != null)
            {
                bookButtonRect.localRotation = Quaternion.identity;
            }
        }

        RefreshStatsText();

        if (pauseGameWhileReading)
        {
            savedTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
            savedFixedDeltaTime = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
            Time.timeScale = 0f;
        }

        PlayBookSFX(true);

        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(AnimateBookRoutine(true));
    }

    public void CloseBook()
    {
        if (!isOpen) return;
        isOpen = false;

        if (pauseGameWhileReading)
        {
            Time.timeScale = savedTimeScale > 0.01f ? savedTimeScale : 1f;
            Time.fixedDeltaTime = savedFixedDeltaTime > 0f ? savedFixedDeltaTime : 0.02f;
        }

        PlayBookSFX(false);

        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(AnimateBookRoutine(false));
    }

    private void EnsureAudioSource()
    {
        if (uiAudioSource == null)
        {
            uiAudioSource = GetComponent<AudioSource>();
            if (uiAudioSource == null)
            {
                uiAudioSource = gameObject.AddComponent<AudioSource>();
            }
            uiAudioSource.playOnAwake = false;
            uiAudioSource.spatialBlend = 0f;
            uiAudioSource.ignoreListenerPause = true;
        }

        if (cachedScrollOpenClip == null) cachedScrollOpenClip = BuildProceduralScrollSFX(true);
        if (cachedScrollCloseClip == null) cachedScrollCloseClip = BuildProceduralScrollSFX(false);
    }

    private void PlayBookSFX(bool opening)
    {
        EnsureAudioSource();
        AudioClip clip = opening
            ? (bookOpenSFX != null ? bookOpenSFX : cachedScrollOpenClip)
            : (bookCloseSFX != null ? bookCloseSFX : cachedScrollCloseClip);

        if (clip != null && uiAudioSource != null)
        {
            uiAudioSource.pitch = opening ? Random.Range(0.97f, 1.04f) : Random.Range(0.95f, 1.02f);
            uiAudioSource.PlayOneShot(clip, bookSFXVolume);
        }
    }

    /// <summary>
    /// Synthesizes a pure, crisp paper page-turn / parchment rustle sound effect (no wooden tone).
    /// </summary>
    private static AudioClip BuildProceduralScrollSFX(bool opening)
    {
        const int sampleRate = 44100;
        float duration = opening ? 0.26f : 0.21f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        uint seed = opening ? 0x5A17C3u : 0x9E31B7u;
        float lpHigh = 0f;
        float lpLow = 0f;
        float prevHigh = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float timeSec = (float)i / sampleRate;

            seed = seed * 1664525u + 1013904223u;
            float white = ((seed & 0xFFFF) / 32767.5f) - 1f;

            // Pure airy paper-band noise (high-passed crisp rustle, zero low muddiness)
            float hiCut = opening ? Mathf.Lerp(0.32f, 0.65f, t) : Mathf.Lerp(0.58f, 0.26f, t);
            float loCut = opening ? Mathf.Lerp(0.10f, 0.22f, t) : Mathf.Lerp(0.20f, 0.09f, t);
            lpHigh += (white - lpHigh) * hiCut;
            lpLow += (white - lpLow) * loCut;
            float paperBand = lpHigh - lpLow;

            // Extra crisp paper edge derivative (high-frequency fiber snap)
            float crispEdge = (lpHigh - prevHigh) * 0.45f;
            prevHigh = lpHigh;

            // Natural two-stage page-flip envelope (initial lift + crisp settle swish)
            float mainEnv = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.75f);
            float secondPeakPos = opening ? 0.68f : 0.60f;
            float dPeak = (t - secondPeakPos) / 0.14f;
            float crinkleBurst = Mathf.Exp(-dPeak * dPeak) * 0.55f;

            float fineFlutter = 0.85f + 0.15f * Mathf.Sin(timeSec * (opening ? 95f : 72f));
            float env = (mainEnv * 0.85f + crinkleBurst) * fineFlutter;

            samples[i] = Mathf.Clamp((paperBand * 1.45f + crispEdge) * env, -0.95f, 0.95f);
        }

        AudioClip clip = AudioClip.Create(opening ? "PaperOpenSFX" : "PaperCloseSFX", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    #endregion

    #region UI Construction

    private void BuildUI()
    {
        GameObject canvasObj = new GameObject("AncientHealTomeCanvas");
        canvasObj.transform.SetParent(transform, false);

        rootCanvas = canvasObj.AddComponent<Canvas>();
        rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        rootCanvas.sortingOrder = canvasSortingOrder;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        BuildTopRightScrollButton(canvasObj.transform);
        BuildAncientTwoPageTome(canvasObj.transform);

        RefreshStatsText();
    }

    private void BuildTopRightScrollButton(Transform parent)
    {
        bookButtonObj = new GameObject("TopRightAncientScrollButton");
        bookButtonObj.transform.SetParent(parent, false);

        // Center the pivot so wiggle rotation & scale pop happen cleanly around the book's center
        bookButtonRect = bookButtonObj.AddComponent<RectTransform>();
        bookButtonRect.anchorMin = new Vector2(1f, 1f);
        bookButtonRect.anchorMax = new Vector2(1f, 1f);
        bookButtonRect.pivot = new Vector2(0.5f, 0.5f);
        bookButtonRect.anchoredPosition = new Vector2(
            -(buttonTopRightMargin.x + buttonSize.x * 0.5f),
            -(buttonTopRightMargin.y + buttonSize.y * 0.5f)
        );
        bookButtonRect.sizeDelta = buttonSize;

        // Green Aurora aura effect behind the button (visible only until the player opens the book once)
        BuildGreenAuroraAroundButton(parent, bookButtonRect.anchoredPosition);
        bookButtonObj.transform.SetAsLastSibling();

        // Dark lacquer backing with antique gold border
        Image bg = bookButtonObj.AddComponent<Image>();
        bg.sprite = cachedWhitePixelSprite;
        bg.color = new Color(0.10f, 0.06f, 0.05f, 0.84f);

        Outline outline = bookButtonObj.AddComponent<Outline>();
        outline.effectColor = new Color(0.84f, 0.64f, 0.28f, 0.95f);
        outline.effectDistance = new Vector2(3f, -3f);

        // Ancient Scroll / Tome Pixel Icon
        GameObject iconObj = new GameObject("AncientScrollIcon");
        iconObj.transform.SetParent(bookButtonObj.transform, false);
        RectTransform iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.60f);
        iconRect.anchorMax = new Vector2(0.5f, 0.60f);
        iconRect.sizeDelta = new Vector2(62f, 62f);

        Image iconImg = iconObj.AddComponent<Image>();
        iconImg.sprite = cachedScrollIconSprite;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;

        // Key Hint
        TMP_Text keyLabel = CreateTMPText(
            bookButtonObj.transform,
            "ScrollKeyLabel",
            $"SCROLL [{toggleKey}]",
            13f,
            new Color(0.96f, 0.85f, 0.56f, 1f),
            TextAlignmentOptions.Center,
            FontStyles.Bold
        );
        RectTransform labelRect = keyLabel.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0.26f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Button btn = bookButtonObj.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(ToggleBook);

        EventTrigger trigger = bookButtonObj.AddComponent<EventTrigger>();
        EventTrigger.Entry enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enterEntry.callback.AddListener(_ => isHoveringToggleButton = true);
        trigger.triggers.Add(enterEntry);

        EventTrigger.Entry exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exitEntry.callback.AddListener(_ => isHoveringToggleButton = false);
        trigger.triggers.Add(exitEntry);
    }

    private void BuildGreenAuroraAroundButton(Transform canvasParent, Vector2 centerPos)
    {
        auroraContainerObj = new GameObject("FirstTimeGreenAurora");
        auroraContainerObj.transform.SetParent(canvasParent, false);
        RectTransform containerRect = auroraContainerObj.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(1f, 1f);
        containerRect.anchorMax = new Vector2(1f, 1f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.anchoredPosition = centerPos;
        containerRect.sizeDelta = new Vector2(190f, 190f);

        // Outer flowing emerald-teal aurora curtain
        GameObject outerObj = new GameObject("AuroraOuterWave");
        outerObj.transform.SetParent(auroraContainerObj.transform, false);
        auroraOuterRect = outerObj.AddComponent<RectTransform>();
        auroraOuterRect.anchorMin = new Vector2(0.5f, 0.5f);
        auroraOuterRect.anchorMax = new Vector2(0.5f, 0.5f);
        auroraOuterRect.sizeDelta = new Vector2(178f, 162f);
        auroraOuterImg = outerObj.AddComponent<Image>();
        auroraOuterImg.sprite = cachedAuroraGlowSprite;
        auroraOuterImg.color = new Color(0.12f, 0.96f, 0.56f, 0.55f);
        auroraOuterImg.raycastTarget = false;

        // Inner shimmering mint-cyan aurora core
        GameObject innerObj = new GameObject("AuroraInnerWave");
        innerObj.transform.SetParent(auroraContainerObj.transform, false);
        auroraInnerRect = innerObj.AddComponent<RectTransform>();
        auroraInnerRect.anchorMin = new Vector2(0.5f, 0.5f);
        auroraInnerRect.anchorMax = new Vector2(0.5f, 0.5f);
        auroraInnerRect.sizeDelta = new Vector2(142f, 152f);
        auroraInnerImg = innerObj.AddComponent<Image>();
        auroraInnerImg.sprite = cachedAuroraGlowSprite;
        auroraInnerImg.color = new Color(0.38f, 1.0f, 0.78f, 0.65f);
        auroraInnerImg.raycastTarget = false;

        // 4 floating green aurora sparkle wisps orbiting the scroll button
        const int wispCount = 4;
        auroraWisps = new RectTransform[wispCount];
        auroraWispImages = new Image[wispCount];
        for (int i = 0; i < wispCount; i++)
        {
            GameObject wispObj = new GameObject($"AuroraWisp_{i}");
            wispObj.transform.SetParent(auroraContainerObj.transform, false);
            RectTransform rt = wispObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(14f, 14f);

            Image img = wispObj.AddComponent<Image>();
            img.sprite = cachedGlowDotSprite;
            img.color = new Color(0.45f, 1.0f, 0.70f, 0.85f);
            img.raycastTarget = false;

            auroraWisps[i] = rt;
            auroraWispImages[i] = img;
        }
    }

    private void SetAuroraActive(bool active)
    {
        if (auroraContainerObj != null)
        {
            auroraContainerObj.SetActive(active);
        }
    }

    private void UpdateGreenAuroraEffect()
    {
        if (auroraContainerObj == null || !auroraContainerObj.activeSelf) return;

        float t = attentionAnimTime;

        // Shifting northern-lights green/emerald/cyan palette
        Color emerald = new Color(0.10f, 0.95f, 0.45f, 1f);
        Color auroraTeal = new Color(0.18f, 1.00f, 0.78f, 1f);
        Color mintGlow = new Color(0.55f, 1.00f, 0.68f, 1f);

        if (auroraOuterRect != null && auroraOuterImg != null)
        {
            float sx = 1.0f + Mathf.Sin(t * 2.3f) * 0.14f;
            float sy = 1.0f + Mathf.Cos(t * 1.9f) * 0.16f;
            auroraOuterRect.localScale = new Vector3(sx, sy, 1f);
            auroraOuterRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.1f) * 18f);

            Color c = Color.Lerp(emerald, auroraTeal, 0.5f + 0.5f * Mathf.Sin(t * 2.1f));
            c.a = 0.42f + 0.18f * Mathf.Sin(t * 3.0f);
            auroraOuterImg.color = c;
        }

        if (auroraInnerRect != null && auroraInnerImg != null)
        {
            float sx = 1.0f + Mathf.Cos(t * 2.8f) * 0.12f;
            float sy = 1.0f + Mathf.Sin(t * 2.5f) * 0.14f;
            auroraInnerRect.localScale = new Vector3(sx, sy, 1f);
            auroraInnerRect.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Cos(t * 1.4f) * 22f);

            Color c = Color.Lerp(auroraTeal, mintGlow, 0.5f + 0.5f * Mathf.Cos(t * 2.7f));
            c.a = 0.48f + 0.22f * Mathf.Cos(t * 3.4f);
            auroraInnerImg.color = c;
        }

        if (auroraWisps != null)
        {
            for (int i = 0; i < auroraWisps.Length; i++)
            {
                if (auroraWisps[i] == null) continue;
                float angle = t * 1.45f + (i * Mathf.PI * 0.5f);
                float radiusX = 56f + Mathf.Sin(t * 2.6f + i) * 8f;
                float radiusY = 58f + Mathf.Cos(t * 2.2f + i) * 8f;
                auroraWisps[i].anchoredPosition = new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);

                float twinkle = 0.65f + 0.45f * Mathf.Sin(t * 6.5f + i * 1.7f);
                auroraWisps[i].localScale = new Vector3(twinkle, twinkle, 1f);
                if (auroraWispImages[i] != null)
                {
                    Color wc = Color.Lerp(emerald, mintGlow, 0.5f + 0.5f * Mathf.Sin(t * 3f + i));
                    wc.a = 0.45f + 0.45f * Mathf.Sin(t * 4.5f + i * 1.3f);
                    auroraWispImages[i].color = wc;
                }
            }
        }
    }

    private void BuildAncientTwoPageTome(Transform parent)
    {
        // Dimmed ink-wash backdrop (click outside to close)
        bookOverlayObj = new GameObject("AncientTomeOverlay");
        bookOverlayObj.transform.SetParent(parent, false);
        RectTransform overlayRect = bookOverlayObj.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        bookOverlayCanvasGroup = bookOverlayObj.AddComponent<CanvasGroup>();

        Image backdropImg = bookOverlayObj.AddComponent<Image>();
        backdropImg.sprite = cachedWhitePixelSprite;
        backdropImg.color = new Color(0.03f, 0.02f, 0.02f, 0.78f);

        Button backdropBtn = bookOverlayObj.AddComponent<Button>();
        backdropBtn.transition = Selectable.Transition.None;
        backdropBtn.onClick.AddListener(CloseBook);

        // Ancient Lacquer / Aged Wood Outer Tome Cover
        GameObject coverObj = new GameObject("AncientTomeSpread");
        coverObj.transform.SetParent(bookOverlayObj.transform, false);
        bookSpreadRect = coverObj.AddComponent<RectTransform>();
        bookSpreadRect.anchorMin = new Vector2(0.5f, 0.5f);
        bookSpreadRect.anchorMax = new Vector2(0.5f, 0.5f);
        bookSpreadRect.sizeDelta = new Vector2(1100f, 680f);

        Image coverImg = coverObj.AddComponent<Image>();
        coverImg.sprite = cachedWhitePixelSprite;
        coverImg.color = new Color(0.18f, 0.10f, 0.07f, 1f); // Dark aged cedar/lacquer

        Outline coverGoldTrim = coverObj.AddComponent<Outline>();
        coverGoldTrim.effectColor = new Color(0.76f, 0.56f, 0.22f, 1f); // Antique bronze-gold
        coverGoldTrim.effectDistance = new Vector2(5f, -5f);

        // Ancient Scroll Wooden Roller Rods on Left & Right Outer Edges
        CreateVerticalRod(coverObj.transform, "LeftWoodenRod", 0f, -14f);
        CreateVerticalRod(coverObj.transform, "RightWoodenRod", 1f, 14f);

        // Top Banner Title above the pages
        TMP_Text banner = CreateTMPText(
            coverObj.transform,
            "TomeBannerTitle",
            tomeBannerTitle,
            24f,
            new Color(0.95f, 0.82f, 0.48f, 1f),
            TextAlignmentOptions.Center,
            FontStyles.Bold
        );
        SetAnchoredBox(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 24f), new Vector2(680f, 38f));

        // Aged stacked paper under-layer
        GameObject pageStackObj = CreatePanel(
            coverObj.transform,
            "AgedPaperBase",
            new Color(0.56f, 0.45f, 0.30f, 1f),
            new Vector2(22f, 22f),
            new Vector2(-22f, -22f)
        );

        // Left Parchment Page (Page I: Plus / Small Heal)
        GameObject leftPage = CreateParchmentPage(
            pageStackObj.transform,
            "LeftAncientPage",
            new Vector2(12f, 12f),
            new Vector2(-530f, -12f)
        );
        BuildLeftPageContent(leftPage.transform);

        // Right Parchment Page (Page II: Heart / Big Heal)
        GameObject rightPage = CreateParchmentPage(
            pageStackObj.transform,
            "RightAncientPage",
            new Vector2(530f, 12f),
            new Vector2(-12f, -12f)
        );
        BuildRightPageContent(rightPage.transform);

        // Center Spine Crease + Traditional Ancient Thread Stitches
        BuildStitchedSpine(pageStackObj.transform);

        // Crimson Silk Cord Bookmark
        GameObject ribbonObj = new GameObject("SilkBookmarkCord");
        ribbonObj.transform.SetParent(coverObj.transform, false);
        RectTransform ribbonRect = ribbonObj.AddComponent<RectTransform>();
        ribbonRect.anchorMin = new Vector2(0.5f, 1f);
        ribbonRect.anchorMax = new Vector2(0.5f, 1f);
        ribbonRect.pivot = new Vector2(0.5f, 1f);
        ribbonRect.anchoredPosition = new Vector2(0f, -6f);
        ribbonRect.sizeDelta = new Vector2(18f, 76f);
        Image ribbonImg = ribbonObj.AddComponent<Image>();
        ribbonImg.sprite = cachedWhitePixelSprite;
        ribbonImg.color = new Color(0.72f, 0.12f, 0.14f, 1f);
        ribbonImg.raycastTarget = false;

        // Top-Right Close [X] Wax Seal Button
        GameObject closeObj = new GameObject("CloseTomeButton");
        closeObj.transform.SetParent(coverObj.transform, false);
        RectTransform closeRect = closeObj.AddComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1f, 1f);
        closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(1f, 1f);
        closeRect.anchoredPosition = new Vector2(-14f, -14f);
        closeRect.sizeDelta = new Vector2(46f, 46f);

        Image closeBg = closeObj.AddComponent<Image>();
        closeBg.sprite = cachedWhitePixelSprite;
        closeBg.color = new Color(0.68f, 0.12f, 0.14f, 1f);
        Outline closeOutline = closeObj.AddComponent<Outline>();
        closeOutline.effectColor = new Color(0.92f, 0.74f, 0.34f, 1f);
        closeOutline.effectDistance = new Vector2(2f, -2f);

        TMP_Text closeLabel = CreateTMPText(closeObj.transform, "XLabel", "X", 26f, new Color(0.98f, 0.92f, 0.78f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
        StretchFill(closeLabel.rectTransform);

        Button closeBtn = closeObj.AddComponent<Button>();
        closeBtn.targetGraphic = closeBg;
        closeBtn.onClick.AddListener(CloseBook);

        // Footer hint
        TMP_Text footer = CreateTMPText(
            coverObj.transform,
            "FooterHint",
            $"Press [{toggleKey}] or [ESC] or click outside to close scroll",
            19f,
            new Color(0.92f, 0.84f, 0.66f, 0.9f),
            TextAlignmentOptions.Center,
            FontStyles.Normal
        );
        RectTransform footerRect = footer.rectTransform;
        footerRect.anchorMin = new Vector2(0f, 0f);
        footerRect.anchorMax = new Vector2(1f, 0f);
        footerRect.pivot = new Vector2(0.5f, 1f);
        footerRect.anchoredPosition = new Vector2(0f, -16f);
        footerRect.sizeDelta = new Vector2(0f, 34f);
    }

    private void BuildLeftPageContent(Transform page)
    {
        Color inkSumi = new Color(0.16f, 0.11f, 0.08f, 1f);
        Color jadeInk = new Color(0.08f, 0.45f, 0.22f, 1f);

        // Decorative thin inner ink border
        CreateThinInkFrame(page, new Color(0.45f, 0.33f, 0.20f, 0.45f));

        // Page I header tag
        TMP_Text tag = CreateTMPText(page, "LeftTag", $"[ I ]  {leftPageTag}", 18f, new Color(0.48f, 0.32f, 0.18f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
        SetAnchoredBox(tag.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(400f, 28f));

        // Main Title
        TMP_Text title = CreateTMPText(page, "TitleLeft", leftPageTitle, 34f, jadeInk, TextAlignmentOptions.Center, FontStyles.Bold);
        SetAnchoredBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(430f, 44f));

        // Plain ink line drawing of Plus (+) directly on the parchment (no box or decoration)
        GameObject diagramObj = new GameObject("PlusLineDrawing");
        diagramObj.transform.SetParent(page, false);
        RectTransform diagRect = diagramObj.AddComponent<RectTransform>();
        SetAnchoredBox(diagRect, new Vector2(0.5f, 0.55f), Vector2.zero, new Vector2(180f, 180f));
        Image diagImg = diagramObj.AddComponent<Image>();
        diagImg.sprite = cachedCrossExampleSprites[0];
        diagImg.preserveAspect = true;
        diagImg.raycastTarget = false;

        plusSparkleTrail = CreateSparkleTrail(diagramObj.transform, 4);
        plusGuideDot = CreateStrokeGuideDot(diagramObj.transform, Color.white);
        plusGuideDot.sizeDelta = new Vector2(22f, 22f);

        // Clear Stats Pill
        GameObject pill = CreatePanel(page, "LeftStatsPill", new Color(0.12f, 0.40f, 0.20f, 0.14f), Vector2.zero, Vector2.zero);
        SetAnchoredBox(pill.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 152f), new Vector2(400f, 42f));
        leftStatsText = CreateTMPText(pill.transform, "LeftStats", "+25 HP   •   15 METER", 24f, jadeInk, TextAlignmentOptions.Center, FontStyles.Bold);
        StretchFill(leftStatsText.rectTransform);

        // Simple 3-step guide
        TMP_Text steps = CreateTMPText(page, "LeftSteps", leftPageSteps, 21f, inkSumi, TextAlignmentOptions.Center, FontStyles.Normal);
        SetAnchoredBox(steps.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 72f), new Vector2(420f, 100f));

        // Red Ancient Stamp in bottom-left corner
        PlaceRedStamp(page, new Vector2(0f, 0f), new Vector2(44f, 44f));
    }

    private void BuildRightPageContent(Transform page)
    {
        Color inkSumi = new Color(0.16f, 0.11f, 0.08f, 1f);
        Color crimsonInk = new Color(0.68f, 0.10f, 0.22f, 1f);

        // Decorative thin inner ink border
        CreateThinInkFrame(page, new Color(0.45f, 0.33f, 0.20f, 0.45f));

        // Page II header tag
        TMP_Text tag = CreateTMPText(page, "RightTag", $"[ II ]  {rightPageTag}", 18f, new Color(0.48f, 0.32f, 0.18f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
        SetAnchoredBox(tag.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(400f, 28f));

        // Main Title
        TMP_Text title = CreateTMPText(page, "TitleRight", rightPageTitle, 34f, crimsonInk, TextAlignmentOptions.Center, FontStyles.Bold);
        SetAnchoredBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(430f, 44f));

        // Plain ink line drawing of Heart directly on the parchment (no box or decoration)
        GameObject diagramObj = new GameObject("HeartLineDrawing");
        diagramObj.transform.SetParent(page, false);
        RectTransform diagRect = diagramObj.AddComponent<RectTransform>();
        SetAnchoredBox(diagRect, new Vector2(0.5f, 0.55f), Vector2.zero, new Vector2(180f, 180f));
        Image diagImg = diagramObj.AddComponent<Image>();
        diagImg.sprite = cachedHeartDiagramSprite;
        diagImg.preserveAspect = true;
        diagImg.raycastTarget = false;

        heartSparkleTrail = CreateSparkleTrail(diagramObj.transform, 4);
        heartGuideDot = CreateStrokeGuideDot(diagramObj.transform, Color.white);
        heartGuideDot.sizeDelta = new Vector2(22f, 22f);

        // Clear Stats Pill
        GameObject pill = CreatePanel(page, "RightStatsPill", new Color(0.68f, 0.12f, 0.22f, 0.13f), Vector2.zero, Vector2.zero);
        SetAnchoredBox(pill.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 152f), new Vector2(400f, 42f));
        rightStatsText = CreateTMPText(pill.transform, "RightStats", "+50 HP   •   28 METER", 24f, crimsonInk, TextAlignmentOptions.Center, FontStyles.Bold);
        StretchFill(rightStatsText.rectTransform);

        // Simple 3-step guide
        TMP_Text steps = CreateTMPText(page, "RightSteps", rightPageSteps, 21f, inkSumi, TextAlignmentOptions.Center, FontStyles.Normal);
        SetAnchoredBox(steps.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 72f), new Vector2(420f, 100f));

        // Red Ancient Stamp in bottom-right corner
        PlaceRedStamp(page, new Vector2(1f, 0f), new Vector2(-44f, 44f));
    }

    private void CreateVerticalRod(Transform parent, string name, float anchorX, float offsetX)
    {
        GameObject rod = new GameObject(name);
        rod.transform.SetParent(parent, false);
        RectTransform rt = rod.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(anchorX, 0f);
        rt.anchorMax = new Vector2(anchorX, 1f);
        rt.anchoredPosition = new Vector2(offsetX, 0f);
        rt.sizeDelta = new Vector2(24f, 36f);

        Image img = rod.AddComponent<Image>();
        img.sprite = cachedWhitePixelSprite;
        img.color = new Color(0.28f, 0.15f, 0.08f, 1f);
        img.raycastTarget = false;

        Outline goldCap = rod.AddComponent<Outline>();
        goldCap.effectColor = new Color(0.84f, 0.65f, 0.26f, 1f);
        goldCap.effectDistance = new Vector2(2f, -2f);
    }

    private void BuildStitchedSpine(Transform parent)
    {
        GameObject spineObj = new GameObject("AncientSpineShadow");
        spineObj.transform.SetParent(parent, false);
        RectTransform spineRect = spineObj.AddComponent<RectTransform>();
        spineRect.anchorMin = new Vector2(0.5f, 0f);
        spineRect.anchorMax = new Vector2(0.5f, 1f);
        spineRect.sizeDelta = new Vector2(28f, -18f);
        Image spineImg = spineObj.AddComponent<Image>();
        spineImg.sprite = cachedWhitePixelSprite;
        spineImg.color = new Color(0.26f, 0.17f, 0.10f, 0.45f);
        spineImg.raycastTarget = false;

        // 5 traditional horizontal binding stitches along the center spine
        float[] stitchAnchorsY = { 0.18f, 0.34f, 0.50f, 0.66f, 0.82f };
        for (int i = 0; i < stitchAnchorsY.Length; i++)
        {
            GameObject stitch = new GameObject($"SpineStitch_{i}");
            stitch.transform.SetParent(spineObj.transform, false);
            RectTransform stRect = stitch.AddComponent<RectTransform>();
            stRect.anchorMin = new Vector2(0.5f, stitchAnchorsY[i]);
            stRect.anchorMax = new Vector2(0.5f, stitchAnchorsY[i]);
            stRect.sizeDelta = new Vector2(24f, 5f);
            Image stImg = stitch.AddComponent<Image>();
            stImg.sprite = cachedWhitePixelSprite;
            stImg.color = new Color(0.84f, 0.68f, 0.34f, 0.9f);
            stImg.raycastTarget = false;
        }
    }

    private void CreateThinInkFrame(Transform page, Color lineColor)
    {
        GameObject frame = new GameObject("InnerInkBorder");
        frame.transform.SetParent(page, false);
        RectTransform rt = frame.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(18f, 18f);
        rt.offsetMax = new Vector2(-18f, -18f);

        Image img = frame.AddComponent<Image>();
        img.sprite = cachedWhitePixelSprite;
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;

        Outline border = frame.AddComponent<Outline>();
        border.effectColor = lineColor;
        border.effectDistance = new Vector2(2f, -2f);
    }

    private void PlaceRedStamp(Transform page, Vector2 anchor, Vector2 pos)
    {
        GameObject stamp = new GameObject("RedAncientSeal");
        stamp.transform.SetParent(page, false);
        RectTransform rt = stamp.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(36f, 36f);

        Image img = stamp.AddComponent<Image>();
        img.sprite = cachedRedSealSprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private void RefreshStatsText()
    {
        float plusHeal = boundPlayer != null ? boundPlayer.PlusHealAmount : 25f;
        float plusCost = boundPlayer != null ? boundPlayer.PlusHealMeterCost : 15f;
        float heartHeal = boundPlayer != null ? boundPlayer.HeartHealAmount : 50f;
        float heartCost = boundPlayer != null ? boundPlayer.HeartHealMeterCost : 28f;

        if (leftStatsText != null)
        {
            leftStatsText.text = $"+{Mathf.RoundToInt(plusHeal)} HP   •   {Mathf.RoundToInt(plusCost)} METER";
        }
        if (rightStatsText != null)
        {
            rightStatsText.text = $"+{Mathf.RoundToInt(heartHeal)} HP   •   {Mathf.RoundToInt(heartCost)} METER";
        }
    }

    #endregion

    #region Animations & Stroke Guides

    private void SetBookImmediate(bool open)
    {
        isOpen = open;
        if (bookOverlayObj != null) bookOverlayObj.SetActive(open);
        if (bookOverlayCanvasGroup != null) bookOverlayCanvasGroup.alpha = open ? 1f : 0f;
        if (bookSpreadRect != null)
        {
            bookSpreadRect.localScale = open ? Vector3.one : Vector3.one * 0.22f;
            bookSpreadRect.anchoredPosition = open ? Vector2.zero : new Vector2(0f, -48f);
            bookSpreadRect.localRotation = Quaternion.identity;
        }
    }

    private IEnumerator AnimateBookRoutine(bool opening)
    {
        if (bookOverlayObj == null || bookOverlayCanvasGroup == null || bookSpreadRect == null) yield break;

        // Tactile click squish on the top-right scroll button
        if (bookButtonRect != null)
        {
            bookButtonRect.localScale = new Vector3(0.84f, 0.84f, 1f);
        }

        if (opening)
        {
            bookOverlayObj.SetActive(true);
            bookSpreadRect.localScale = Vector3.one * 0.22f;
            bookSpreadRect.anchoredPosition = new Vector2(0f, -48f);
            bookSpreadRect.localRotation = Quaternion.Euler(0f, 0f, -3.5f);
        }

        float duration = opening ? 0.28f : 0.18f;
        float elapsed = 0f;
        float startAlpha = bookOverlayCanvasGroup.alpha;
        float endAlpha = opening ? 1f : 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (opening)
            {
                // Elastic Ease-Out Back pop-up overshoot (0.22 -> 1.08 -> 1.00)
                const float c1 = 1.95f;
                const float c3 = c1 + 1f;
                float u = t - 1f;
                float popCurve = 1f + c3 * u * u * u + c1 * u * u;

                float scale = Mathf.LerpUnclamped(0.22f, 1f, popCurve);
                float posY = Mathf.LerpUnclamped(-48f, 0f, popCurve);
                float rotZ = Mathf.Lerp(-3.5f, 0f, 1f - Mathf.Pow(1f - t, 3f));

                bookSpreadRect.localScale = new Vector3(scale, scale, 1f);
                bookSpreadRect.anchoredPosition = new Vector2(0f, posY);
                bookSpreadRect.localRotation = Quaternion.Euler(0f, 0f, rotZ);
                bookOverlayCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, Mathf.Clamp01(t * 1.6f));
            }
            else
            {
                // Snappy Ease-In Back pop-down (1.00 -> 1.04 -> 0.22)
                const float c1 = 1.45f;
                const float c3 = c1 + 1f;
                float backIn = c3 * t * t * t - c1 * t * t;

                float scale = Mathf.LerpUnclamped(1f, 0.22f, backIn);
                float posY = Mathf.LerpUnclamped(0f, -36f, backIn);

                bookSpreadRect.localScale = new Vector3(Mathf.Max(0.05f, scale), Mathf.Max(0.05f, scale), 1f);
                bookSpreadRect.anchoredPosition = new Vector2(0f, posY);
                bookSpreadRect.localRotation = Quaternion.identity;
                bookOverlayCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
            }

            yield return null;
        }

        bookOverlayCanvasGroup.alpha = endAlpha;
        bookSpreadRect.localScale = opening ? Vector3.one : Vector3.one * 0.22f;
        bookSpreadRect.anchoredPosition = opening ? Vector2.zero : new Vector2(0f, -48f);
        bookSpreadRect.localRotation = Quaternion.identity;

        if (!opening)
        {
            bookOverlayObj.SetActive(false);
        }

        transitionCoroutine = null;
    }

    private bool SamplePlusPath(float time, out Vector2 pos, out int strokeIndex)
    {
        float cycle = (time * 0.55f) % 1f;
        if (cycle < 0f) cycle += 1f;

        if (cycle < 0.44f)
        {
            strokeIndex = 0;
            float t = Mathf.Clamp01(cycle / 0.44f);
            pos = Vector2.Lerp(PlusStrokePath[0], PlusStrokePath[1], t) * 90f;
            return true;
        }
        if (cycle >= 0.50f && cycle < 0.94f)
        {
            strokeIndex = 1;
            float t = Mathf.Clamp01((cycle - 0.50f) / 0.44f);
            pos = Vector2.Lerp(PlusStrokePath[2], PlusStrokePath[3], t) * 90f;
            return true;
        }

        strokeIndex = -1;
        pos = Vector2.zero;
        return false;
    }

    private Vector2 SampleHeartPath(float time)
    {
        int segments = HeartStrokePath.Length - 1;
        float norm = (time * 0.45f) % 1f;
        if (norm < 0f) norm += 1f;
        float progress = norm * segments;
        int idx = Mathf.Clamp(Mathf.FloorToInt(progress), 0, segments - 1);
        float localT = progress - idx;

        return Vector2.Lerp(HeartStrokePath[idx], HeartStrokePath[idx + 1], localT) * 74f;
    }

    private void UpdatePlusStrokeGuide()
    {
        if (plusGuideDot == null) return;

        bool headVisible = SamplePlusPath(strokeAnimTime, out Vector2 headPos, out int headStroke);
        plusGuideDot.gameObject.SetActive(headVisible);

        if (headVisible)
        {
            plusGuideDot.anchoredPosition = headPos;
            float twinkle = 0.88f + Mathf.Sin(strokeAnimTime * 14f) * 0.18f;
            plusGuideDot.localScale = new Vector3(twinkle, twinkle, 1f);
            plusGuideDot.localRotation = Quaternion.Euler(0f, 0f, strokeAnimTime * 90f);
        }

        if (plusSparkleTrail != null)
        {
            for (int i = 0; i < plusSparkleTrail.Length; i++)
            {
                if (plusSparkleTrail[i] == null) continue;
                bool trailVisible = SamplePlusPath(strokeAnimTime - (i + 1) * 0.045f, out Vector2 trailPos, out int trailStroke)
                    && (!headVisible || trailStroke == headStroke);
                plusSparkleTrail[i].gameObject.SetActive(trailVisible);
                if (trailVisible)
                {
                    plusSparkleTrail[i].anchoredPosition = trailPos;
                }
            }
        }
    }

    private void UpdateHeartStrokeGuide()
    {
        if (heartGuideDot == null) return;

        heartGuideDot.anchoredPosition = SampleHeartPath(strokeAnimTime);
        float twinkle = 0.88f + Mathf.Sin(strokeAnimTime * 14f + 1.5f) * 0.18f;
        heartGuideDot.localScale = new Vector3(twinkle, twinkle, 1f);
        heartGuideDot.localRotation = Quaternion.Euler(0f, 0f, -strokeAnimTime * 90f);

        if (heartSparkleTrail != null)
        {
            for (int i = 0; i < heartSparkleTrail.Length; i++)
            {
                if (heartSparkleTrail[i] == null) continue;
                heartSparkleTrail[i].anchoredPosition = SampleHeartPath(strokeAnimTime - (i + 1) * 0.06f);
            }
        }
    }

    #endregion

    #region UI & Procedural Ancient Pixel Sprite Helpers

    private GameObject CreatePanel(Transform parent, string name, Color color, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;

        Image img = obj.AddComponent<Image>();
        img.sprite = cachedWhitePixelSprite;
        img.color = color;
        return obj;
    }

    private GameObject CreateParchmentPage(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;

        Image img = obj.AddComponent<Image>();
        img.sprite = cachedParchmentSprite;
        img.color = Color.white;
        return obj;
    }

    private GameObject CreateDiagramFrame(Transform parent, string name, Color bgColor, Color borderColor)
    {
        GameObject box = new GameObject(name);
        box.transform.SetParent(parent, false);
        box.AddComponent<RectTransform>();

        Image img = box.AddComponent<Image>();
        img.sprite = cachedWhitePixelSprite;
        img.color = bgColor;
        img.raycastTarget = false;

        Outline outline = box.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(4f, -4f);
        return box;
    }

    private RectTransform CreateStrokeGuideDot(Transform parent, Color color)
    {
        GameObject dotObj = new GameObject("StrokeGuideSparkle");
        dotObj.transform.SetParent(parent, false);
        RectTransform rt = dotObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(22f, 22f);

        Image img = dotObj.AddComponent<Image>();
        img.sprite = cachedGlowDotSprite;
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    private RectTransform[] CreateSparkleTrail(Transform parent, int count)
    {
        RectTransform[] trail = new RectTransform[count];
        for (int i = 0; i < count; i++)
        {
            float frac = 1f - ((float)(i + 1) / (count + 1));
            RectTransform rt = CreateStrokeGuideDot(parent, new Color(1f, 0.98f, 0.90f, frac * 0.72f));
            float size = Mathf.Lerp(9f, 16f, frac);
            rt.sizeDelta = new Vector2(size, size);
            trail[i] = rt;
        }
        return trail;
    }

    private TMP_FontAsset ResolveBookFont()
    {
        if (customFont != null) return customFont;

        TMP_Text[] existingTexts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < existingTexts.Length; i++)
        {
            if (existingTexts[i] != null && existingTexts[i].font != null &&
                existingTexts[i].font.name.IndexOf("Pixelify", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                customFont = existingTexts[i].font;
                return customFont;
            }
        }

        TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        for (int i = 0; i < loadedFonts.Length; i++)
        {
            if (loadedFonts[i] != null &&
                loadedFonts[i].name.IndexOf("Pixelify", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                customFont = loadedFonts[i];
                return customFont;
            }
        }

#if UNITY_EDITOR
        customFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/Fonts/PixelifySans-VariableFont_wght SDF.asset"
        );
#endif
        return customFont;
    }

    private TMP_Text CreateTMPText(
        Transform parent,
        string name,
        string content,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment,
        FontStyles style)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset resolvedFont = ResolveBookFont();
        if (resolvedFont != null) tmp.font = resolvedFont;
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.fontStyle = style;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void SetAnchoredBox(RectTransform rt, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
    }

    private static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void EnsureProceduralSprites()
    {
        if (cachedWhitePixelSprite == null)
        {
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            Color[] cols = { Color.white, Color.white, Color.white, Color.white };
            tex.SetPixels(cols);
            tex.Apply();
            cachedWhitePixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 16f);
        }

        if (cachedGlowDotSprite == null)
        {
            // 12x12 pixel-art 4-point white sparkle star with soft golden-white glow and subtle dark rim
            const int s = 12;
            Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color coreWhite = Color.white;
            Color softGlow = new Color(1f, 0.96f, 0.78f, 0.78f);
            Color rimTint = new Color(0.25f, 0.18f, 0.12f, 0.35f);

            float c = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float ax = Mathf.Abs(x - c);
                    float ay = Mathf.Abs(y - c);

                    bool isStarCore = (ax <= 1.2f && ay <= 4.6f) || (ay <= 1.2f && ax <= 4.6f) || (ax + ay <= 2.4f);
                    bool isStarGlow = (ax <= 2.0f && ay <= 5.2f && ax + ay <= 5.5f) || (ay <= 2.0f && ax <= 5.2f && ax + ay <= 5.5f) || (ax + ay <= 3.4f);
                    bool isRim = (ax + ay <= 4.4f);

                    if (isStarCore) tex.SetPixel(x, y, coreWhite);
                    else if (isStarGlow) tex.SetPixel(x, y, softGlow);
                    else if (isRim) tex.SetPixel(x, y, rimTint);
                    else tex.SetPixel(x, y, clear);
                }
            }
            tex.Apply();
            cachedGlowDotSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
        }

        if (cachedAuroraGlowSprite == null)
        {
            // Soft 32x32 radial aurora glow with wavy organic intensity falloff
            const int s = 32;
            Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float half = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    float ribbonWave = 0.86f + 0.14f * Mathf.Sin(angle * 3f + dist * 4f);
                    float radial = Mathf.Clamp01(1f - dist);
                    float alpha = Mathf.Pow(radial, 1.85f) * ribbonWave;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            cachedAuroraGlowSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
        }

        if (cachedScrollIconSprite == null) cachedScrollIconSprite = BuildAncientScrollIconSprite();
        if (cachedParchmentSprite == null) cachedParchmentSprite = BuildWeatheredParchmentSprite();
        if (cachedRedSealSprite == null) cachedRedSealSprite = BuildRedSealSprite();
        if (cachedCrossExampleSprites == null || cachedCrossExampleSprites.Length < 5)
        {
            cachedCrossExampleSprites = new Sprite[5];
            for (int i = 0; i < 5; i++)
            {
                cachedCrossExampleSprites[i] = BuildCrossingExampleSprite(i);
            }
        }
        if (cachedHeartDiagramSprite == null) cachedHeartDiagramSprite = BuildHeartDiagramSprite();
    }

    /// <summary>
    /// 16x16 pixel-art ancient tied scroll with wooden rollers, gold caps, and crimson silk cord.
    /// </summary>
    private static Sprite BuildAncientScrollIconSprite()
    {
        const int w = 16;
        const int h = 16;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color paperLight = new Color(0.93f, 0.85f, 0.66f, 1f);
        Color paperShade = new Color(0.76f, 0.65f, 0.45f, 1f);
        Color wood = new Color(0.36f, 0.18f, 0.09f, 1f);
        Color gold = new Color(0.94f, 0.76f, 0.28f, 1f);
        Color cord = new Color(0.78f, 0.14f, 0.18f, 1f);
        Color ink = new Color(0.24f, 0.16f, 0.10f, 1f);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = clear;

                // Top and bottom wooden scroll rollers with gold end caps
                if ((y == 13 || y == 14 || y == 1 || y == 2) && x >= 1 && x <= 14)
                {
                    if (x == 1 || x == 14) c = gold;
                    else c = wood;
                }
                // Aged parchment body between rollers
                else if (y >= 3 && y <= 12 && x >= 3 && x <= 12)
                {
                    c = (x == 3 || x == 12 || y == 3) ? paperShade : paperLight;

                    // Subtle ancient brush calligraphy marks
                    if ((y == 10 && x >= 5 && x <= 10 && x != 8) ||
                        (y == 5 && x >= 5 && x <= 10 && x != 7))
                    {
                        c = ink;
                    }

                    // Crimson silk tie cord across the middle
                    if (y == 7 || y == 8)
                    {
                        c = cord;
                    }
                    // Gold clasp knot in the center
                    if ((x == 7 || x == 8) && (y == 7 || y == 8))
                    {
                        c = gold;
                    }
                }

                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    /// <summary>
    /// 32x32 procedural weathered ancient parchment texture with subtle grain and darkened aged edges.
    /// </summary>
    private static Sprite BuildWeatheredParchmentSprite()
    {
        const int s = 32;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };

        Color centerBase = new Color(0.92f, 0.84f, 0.66f, 1f);
        Color edgeAged = new Color(0.74f, 0.61f, 0.41f, 1f);

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float edgeDistX = Mathf.Min(x, s - 1 - x) / 6f;
                float edgeDistY = Mathf.Min(y, s - 1 - y) / 6f;
                float edgeFactor = 1f - Mathf.Clamp01(Mathf.Min(edgeDistX, edgeDistY));

                // Deterministic subtle paper grain
                int hash = (x * 73856093) ^ (y * 19349663);
                float grain = (((hash & 255) / 255f) - 0.5f) * 0.06f;

                Color c = Color.Lerp(centerBase, edgeAged, edgeFactor * 0.85f);
                c.r = Mathf.Clamp01(c.r + grain);
                c.g = Mathf.Clamp01(c.g + grain);
                c.b = Mathf.Clamp01(c.b + grain * 0.8f);
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
    }

    /// <summary>
    /// 12x12 pixel-art traditional red hanko/ink seal stamp for the corners of the ancient pages.
    /// </summary>
    private static Sprite BuildRedSealSprite()
    {
        const int s = 12;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color stampRed = new Color(0.76f, 0.14f, 0.14f, 0.88f);

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                bool isOuter = x == 1 || x == s - 2 || y == 1 || y == s - 2;
                bool isInnerMark =
                    (x == 5 && y >= 3 && y <= 8) ||
                    (y == 6 && x >= 3 && x <= 8) ||
                    (x == 7 && y >= 4 && y <= 7) ||
                    (y == 4 && x >= 4 && x <= 7);

                tex.SetPixel(x, y, (isOuter || isInnerMark) ? stampRed : clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
    }

    /// <summary>
    /// Plain single-color thin ink line drawing of 2 crossing lines (+), aligned exactly to PlusStrokePath.
    /// </summary>
    private static Sprite BuildCrossingExampleSprite(int variant)
    {
        const int s = 32;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color inkLine = new Color(0.14f, 0.36f, 0.20f, 1f);

        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                tex.SetPixel(x, y, clear);

        float half = (s - 1) * 0.5f;
        float span = 10.8f; // Matches PlusStrokePath (+/- 0.68 of half-size)
        DrawPixelSegment(tex, s, new Vector2(half, half - span), new Vector2(half, half + span), 1.05f, inkLine, inkLine);
        DrawPixelSegment(tex, s, new Vector2(half - span, half), new Vector2(half + span, half), 1.05f, inkLine, inkLine);

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
    }

    private static void DrawPixelSegment(Texture2D tex, int size, Vector2 p1, Vector2 p2, float thickness, Color outerCol, Color coreCol)
    {
        Vector2 seg = p2 - p1;
        float lenSq = Mathf.Max(0.0001f, seg.sqrMagnitude);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x, y);
                float t = Mathf.Clamp01(Vector2.Dot(p - p1, seg) / lenSq);
                Vector2 proj = p1 + seg * t;
                float dist = Vector2.Distance(p, proj);

                if (dist <= thickness * 0.52f)
                {
                    tex.SetPixel(x, y, coreCol);
                }
                else if (dist <= thickness && tex.GetPixel(x, y).a < 0.9f)
                {
                    tex.SetPixel(x, y, outerCol);
                }
            }
        }
    }

    /// <summary>
    /// Plain single-color thin ink line drawing of a Heart shape (no fill, glow, or extra decoration).
    /// </summary>
    private static Sprite BuildHeartDiagramSprite()
    {
        const int s = 32;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color inkLine = new Color(0.62f, 0.10f, 0.18f, 1f);

        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                tex.SetPixel(x, y, clear);

        float half = s * 0.5f;
        float radius = 13.0f;
        for (int i = 0; i < HeartStrokePath.Length - 1; i++)
        {
            Vector2 p1 = new Vector2(half + HeartStrokePath[i].x * radius, half + HeartStrokePath[i].y * radius);
            Vector2 p2 = new Vector2(half + HeartStrokePath[i + 1].x * radius, half + HeartStrokePath[i + 1].y * radius);
            DrawPixelSegment(tex, s, p1, p2, 0.95f, inkLine, inkLine);
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
    }

    #endregion
}
