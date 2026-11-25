using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Self-contained Top-Right Ancient 2-Page Guide Book & Enemy Bestiary UI.
/// - Spread 0 (Pages I & II): Draw-to-Heal Guide (Cross 2 Lines for Small Heal, Heart for Big Heal).
/// - Spread 1 (Pages III & IV): Enemy Bestiary — 2 enemy types per page (Shadow, Shuriken, Suicide, Armored).
/// - Spread 2 (Pages V & VI): Boss Chronicle (Legendary Samurai) & Shinobi Combat Arts.
/// - Enemies start locked as black silhouettes with "?" and "???" descriptions until defeated.
/// - Features Page-Turn buttons, Page-Flip fold animation, procedural paper-flip SFX,
///   pop-up open/close animation, and first-time / new-unlock Wiggle + Green Aurora alert.
/// </summary>
public class HealGuideBookUI : MonoBehaviour
{
    #region Types & Static Access

    public enum BestiaryEnemyId
    {
        ShadowNinja = 0,
        ShurikenNinja = 1,
        SuicideNinja = 2,
        ArmoredNinja = 3,
        LegendarySamurai = 4
    }

    private struct BestiaryEntryData
    {
        public BestiaryEnemyId id;
        public string displayName;
        public string tagLine;
        public string abilityDescription;
        public Color accentColor;
    }

    private class BestiaryCardUI
    {
        public BestiaryEnemyId id;
        public Image portraitImage;
        public Outline portraitOutline;
        public Image goldAuraBorderImage;
        public RectTransform goldAuraBorderRect;
        public GameObject newBadgeObj;
        public TMP_Text questionMarkOverlay;
        public TMP_Text nameText;
        public TMP_Text descText;
    }

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

    /// <summary>
    /// Called automatically when an enemy is defeated to unlock its Bestiary entry in the Guide Book.
    /// </summary>
    public static void NotifyEnemyDefeated(EnemyBase enemy)
    {
        if (enemy == null) return;
        if (enemy is ArmoredNinjaWeakPoint || enemy is BossTargetPoint) return;

        BestiaryEnemyId id;
        if (enemy is LegendarySamurai) id = BestiaryEnemyId.LegendarySamurai;
        else if (enemy is ArmoredNinja) id = BestiaryEnemyId.ArmoredNinja;
        else if (enemy is ChainEnemy) id = BestiaryEnemyId.SuicideNinja;
        else if (enemy is ThrowerNinja) id = BestiaryEnemyId.ShurikenNinja;
        else id = BestiaryEnemyId.ShadowNinja;

        SpriteRenderer sr = enemy.GetComponentInChildren<SpriteRenderer>();
        Sprite capturedSprite = sr != null ? sr.sprite : null;
        Color capturedTint = sr != null ? sr.color : Color.white;

        if (Instance == null && PlayerController.Instance != null)
        {
            EnsureInstance(PlayerController.Instance);
        }

        if (Instance != null)
        {
            Instance.UnlockBestiaryEntry(id, capturedSprite, capturedTint);
        }
    }

    #endregion

    #region Serialized Fields

    [Header("Controls & Behavior")]
    [Tooltip("Keyboard shortcut to open/close the Ancient Guide Book.")]
    [SerializeField] private Key toggleKey = Key.B;
    [Tooltip("If true, pauses game time while reading the tome.")]
    [SerializeField] private bool pauseGameWhileReading = true;
    [Tooltip("If true (default), saves opened state and unlocked Bestiary progress across deaths, restarts, and returning to Main Menu.")]
    [SerializeField] private bool saveFirstTimeState = true;
    [Tooltip("Sorting order of the overlay canvas.")]
    [SerializeField] private int canvasSortingOrder = 120;

    [Header("Top-Right Scroll Button Layout")]
    [SerializeField] private Vector2 buttonTopRightMargin = new Vector2(28f, 28f);
    [SerializeField] private Vector2 buttonSize = new Vector2(92f, 96f);

    [Header("Optional Custom Font & Heal Guide Text")]
    [SerializeField] private TMP_FontAsset customFont;
    [SerializeField] private string tomeBannerTitle = "~ ANCIENT SHINOBI GUIDE ~";
    [SerializeField] private string leftPageTitle = "CROSS 2 LINES";
    [SerializeField] private string leftPageTag = "SMALL HEAL";
    [SerializeField] private string leftPageSteps = "Doesn't have to be a perfect +\nJust draw any 2 lines that cross!";
    [SerializeField] private string rightPageTitle = "DRAW A HEART";
    [SerializeField] private string rightPageTag = "BIG HEAL";
    [SerializeField] private string rightPageSteps = "Draw a Heart shape\nto heal a lot more HP!";

    [Header("Audio")]
    [Tooltip("Optional custom SFX when opening the scroll (auto-synthesizes pure paper rustle if empty).")]
    [SerializeField] private AudioClip bookOpenSFX;
    [Tooltip("Optional custom SFX when closing the scroll (auto-synthesizes pure paper rustle if empty).")]
    [SerializeField] private AudioClip bookCloseSFX;
    [Tooltip("Optional custom SFX when flipping to the next/previous page (auto-synthesizes crisp page flip if empty).")]
    [SerializeField] private AudioClip pageFlipSFX;
    [Range(0f, 1f)] [SerializeField] private float bookSFXVolume = 0.85f;

    #endregion

    #region Private State

    private const string HasOpenedBookPrefKey = "HealGuideBook_HasOpenedOnce";
    private const string BestiaryUnlockPrefPrefix = "HealGuideBook_Unlocked_";
    private const int TotalSpreads = 3;

    private static readonly BestiaryEntryData[] BestiaryCatalog = new BestiaryEntryData[]
    {
        new BestiaryEntryData
        {
            id = BestiaryEnemyId.ShadowNinja,
            displayName = "NORMAL NINJA",
            tagLine = "AGILE NINJA",
            abilityDescription = "Runs up walls and ceilings. Leaps backward to dodge when you get too close.",
            accentColor = new Color(0.22f, 0.24f, 0.36f, 1f)
        },
        new BestiaryEntryData
        {
            id = BestiaryEnemyId.ShurikenNinja,
            displayName = "SHURIKEN NINJA",
            tagLine = "RANGED THROWER",
            abilityDescription = "Throws shurikens from range and scatters 2 extra blades upon death. Parry them back!",
            accentColor = new Color(0.72f, 0.32f, 0.10f, 1f)
        },
        new BestiaryEntryData
        {
            id = BestiaryEnemyId.SuicideNinja,
            displayName = "SUICIDE NINJA",
            tagLine = "EXPLOSIVE BOMBER",
            abilityDescription = "Rushes in and detonates on contact or death. Chain-explodes nearby enemies!",
            accentColor = new Color(0.76f, 0.14f, 0.16f, 1f)
        },
        new BestiaryEntryData
        {
            id = BestiaryEnemyId.ArmoredNinja,
            displayName = "ARMORED NINJA",
            tagLine = "HEAVY GUARD",
            abilityDescription = "Blocks normal slashes. Target all 3 glowing weak points or shatter with explosions.",
            accentColor = new Color(0.24f, 0.38f, 0.58f, 1f)
        },
        new BestiaryEntryData
        {
            id = BestiaryEnemyId.LegendarySamurai,
            displayName = "LEGENDARY SAMURAI",
            tagLine = "STAGE 5 BOSS",
            abilityDescription = "5-Phase master swordsman. Uses Dash-Slash, Stomp Shockwave, 360 Shurikens, Wind Slash, and Hidden Stab.",
            accentColor = new Color(0.74f, 0.12f, 0.18f, 1f)
        }
    };

    private static readonly Vector2[] PlusStrokePath = new Vector2[]
    {
        new Vector2( 0.00f,  0.68f),
        new Vector2( 0.00f, -0.68f),
        new Vector2( 0.00f,  0.00f),
        new Vector2(-0.68f,  0.00f),
        new Vector2( 0.68f,  0.00f)
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

    private PlayerController boundPlayer;
    private AudioSource uiAudioSource;
    private Canvas rootCanvas;
    private RectTransform bookButtonRect;
    private GameObject bookButtonObj;
    private TMP_Text scrollButtonLabel;
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
    private RectTransform pagesContentRect;
    private Image pageFlipShadowImg;
    private GameObject[] spreadContainers = new GameObject[TotalSpreads];

    private Button prevPageButton;
    private Button nextPageButton;
    private TMP_Text spreadCounterText;

    private TMP_Text leftStatsText;
    private TMP_Text rightStatsText;
    private RectTransform plusGuideDot;
    private RectTransform heartGuideDot;
    private RectTransform[] plusSparkleTrail;
    private RectTransform[] heartSparkleTrail;

    // Bestiary runtime unlock state
    private readonly HashSet<BestiaryEnemyId> unlockedEnemies = new HashSet<BestiaryEnemyId>();
    private readonly HashSet<BestiaryEnemyId> newlyUnlockedHighlights = new HashSet<BestiaryEnemyId>();
    private readonly Dictionary<BestiaryEnemyId, Sprite> capturedEnemySprites = new Dictionary<BestiaryEnemyId, Sprite>();
    private readonly Dictionary<BestiaryEnemyId, Color> capturedEnemyColors = new Dictionary<BestiaryEnemyId, Color>();
    private readonly Dictionary<BestiaryEnemyId, BestiaryCardUI> bestiaryCards = new Dictionary<BestiaryEnemyId, BestiaryCardUI>();

    private bool isOpen;
    private bool isFlippingPage;
    private int currentSpreadIndex = 0;
    private bool hasUnreadAttention;
    private bool isHoveringToggleButton;
    private float attentionAnimTime;
    private float savedTimeScale = 1f;
    private float savedFixedDeltaTime = 0.02f;
    private float strokeAnimTime;
    private Coroutine transitionCoroutine;
    private Coroutine pageFlipCoroutine;

    // Cached procedural pixel-art sprites & audio
    private static Sprite cachedScrollIconSprite;
    private static Sprite cachedParchmentSprite;
    private static Sprite cachedRedSealSprite;
    private static Sprite cachedPlusDiagramSprite;
    private static Sprite cachedHeartDiagramSprite;
    private static Sprite cachedWhitePixelSprite;
    private static Sprite cachedGlowDotSprite;
    private static Sprite cachedAuroraGlowSprite;
    private static Sprite cachedNinjaSilhouetteSprite;
    private static Sprite cachedSamuraiSilhouetteSprite;
    private static AudioClip cachedScrollOpenClip;
    private static AudioClip cachedScrollCloseClip;
    private static AudioClip cachedPageFlipClip;

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

        bool alreadyOpenedBefore = saveFirstTimeState && PlayerPrefs.GetInt(HasOpenedBookPrefKey, 0) == 1;
        hasUnreadAttention = !alreadyOpenedBefore;
        LoadSavedBestiaryProgress();

        EnsureProceduralSprites();
        EnsureAudioSource();
        BuildUI();
        SetBookImmediate(false);
        ApplySpreadVisibility(0);
        RefreshAllBestiaryCards();
        UpdateAttentionVisuals();
    }

    private void LoadSavedBestiaryProgress()
    {
        if (!saveFirstTimeState) return;

        for (int i = 0; i < BestiaryCatalog.Length; i++)
        {
            BestiaryEnemyId id = BestiaryCatalog[i].id;
            if (PlayerPrefs.GetInt(BestiaryUnlockPrefPrefix + (int)id, 0) == 1)
            {
                unlockedEnemies.Add(id);
            }
        }
    }

    [ContextMenu("Reset First-Time Book & Bestiary")]
    public void ResetFirstTimeAttention()
    {
        PlayerPrefs.DeleteKey(HasOpenedBookPrefKey);
        for (int i = 0; i < BestiaryCatalog.Length; i++)
        {
            PlayerPrefs.DeleteKey(BestiaryUnlockPrefPrefix + (int)BestiaryCatalog[i].id);
        }
        PlayerPrefs.Save();
        unlockedEnemies.Clear();
        newlyUnlockedHighlights.Clear();
        hasUnreadAttention = true;
        attentionAnimTime = 0f;
        RefreshAllBestiaryCards();
        UpdateAttentionVisuals();
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
            SetRootCanvasVisible(false);
        }
        else
        {
            SetRootCanvasVisible(true);
            UpdateAttentionVisuals();
            BindPlayer(PlayerController.Instance);
            RefreshStatsText();
            RefreshAllBestiaryCards();
        }
    }

    private void SetRootCanvasVisible(bool visible)
    {
        if (rootCanvas == null) return;

        rootCanvas.enabled = visible;
        if (rootCanvas.TryGetComponent(out CanvasGroup cg))
        {
            cg.alpha = visible ? 1f : 0f;
            cg.blocksRaycasts = visible;
            cg.interactable = visible;
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

            if (hasUnreadAttention && !isOpen)
            {
                attentionAnimTime += Time.unscaledDeltaTime;

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
            else if (isOpen)
            {
                if (Keyboard.current[Key.Escape].wasPressedThisFrame)
                {
                    CloseBook();
                }
                else if (Keyboard.current[Key.RightArrow].wasPressedThisFrame || Keyboard.current[Key.D].wasPressedThisFrame)
                {
                    NextSpread();
                }
                else if (Keyboard.current[Key.LeftArrow].wasPressedThisFrame || Keyboard.current[Key.A].wasPressedThisFrame)
                {
                    PreviousSpread();
                }
            }
        }

        if (isOpen)
        {
            strokeAnimTime += Time.unscaledDeltaTime;
            if (currentSpreadIndex == 0)
            {
                UpdatePlusStrokeGuide();
                UpdateHeartStrokeGuide();
            }
            UpdateGoldenHighlightEffects();
        }
    }

    #endregion

    #region Public & Bestiary Unlock API

    public void BindPlayer(PlayerController player)
    {
        boundPlayer = player != null ? player : PlayerController.Instance;
        if (boundPlayer != null && FindFirstObjectByType<MainMenu>() == null)
        {
            SetRootCanvasVisible(true);
        }
        RefreshStatsText();
    }

    public void UnlockBestiaryEntry(BestiaryEnemyId id, Sprite capturedSprite, Color capturedTint)
    {
        if (capturedSprite != null && !capturedEnemySprites.ContainsKey(id))
        {
            capturedEnemySprites[id] = capturedSprite;
            capturedEnemyColors[id] = capturedTint.a > 0.1f ? capturedTint : Color.white;
        }

        bool isNewUnlock = unlockedEnemies.Add(id);
        if (isNewUnlock)
        {
            if (saveFirstTimeState)
            {
                PlayerPrefs.SetInt(BestiaryUnlockPrefPrefix + (int)id, 1);
                PlayerPrefs.Save();
            }

            newlyUnlockedHighlights.Add(id);

            // Notify via Wiggle + Green Aurora on the top-right scroll button so the player can open it themselves
            hasUnreadAttention = true;
            attentionAnimTime = 0f;
            UpdateAttentionVisuals();
        }
        RefreshBestiaryCard(id);
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

        if (hasUnreadAttention)
        {
            hasUnreadAttention = false;
            if (saveFirstTimeState)
            {
                PlayerPrefs.SetInt(HasOpenedBookPrefKey, 1);
                PlayerPrefs.Save();
            }
            UpdateAttentionVisuals();
            if (bookButtonRect != null)
            {
                bookButtonRect.localRotation = Quaternion.identity;
            }
        }

        RefreshStatsText();
        RefreshAllBestiaryCards();

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

        // Clear newly-unlocked golden portrait border highlights once the player closes the book
        if (newlyUnlockedHighlights.Count > 0)
        {
            newlyUnlockedHighlights.Clear();
            RefreshAllBestiaryCards();
        }

        if (pauseGameWhileReading)
        {
            Time.timeScale = savedTimeScale > 0.01f ? savedTimeScale : 1f;
            Time.fixedDeltaTime = savedFixedDeltaTime > 0f ? savedFixedDeltaTime : 0.02f;
        }

        PlayBookSFX(false);

        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(AnimateBookRoutine(false));
    }

    public void NextSpread()
    {
        if (!isOpen || isFlippingPage || currentSpreadIndex >= TotalSpreads - 1) return;
        if (pageFlipCoroutine != null) StopCoroutine(pageFlipCoroutine);
        pageFlipCoroutine = StartCoroutine(FlipSpreadRoutine(currentSpreadIndex + 1));
    }

    public void PreviousSpread()
    {
        if (!isOpen || isFlippingPage || currentSpreadIndex <= 0) return;
        if (pageFlipCoroutine != null) StopCoroutine(pageFlipCoroutine);
        pageFlipCoroutine = StartCoroutine(FlipSpreadRoutine(currentSpreadIndex - 1));
    }

    private void UpdateAttentionVisuals()
    {
        SetAuroraActive(hasUnreadAttention && !isOpen);
        if (scrollButtonLabel != null)
        {
            string label = (hasUnreadAttention && unlockedEnemies.Count > 0)
                ? $"NEW! [{toggleKey}]"
                : $"SCROLL [{toggleKey}]";
            scrollButtonLabel.text = WrapNumbersWithNormalFont(label);
            scrollButtonLabel.color = (hasUnreadAttention && unlockedEnemies.Count > 0)
                ? new Color(0.45f, 1.0f, 0.72f, 1f)
                : new Color(0.96f, 0.85f, 0.56f, 1f);
        }
    }

    #endregion

    #region Audio Synthesis (Pure Paper Open / Close / Page Flip)

    private void EnsureAudioSource()
    {
        if (uiAudioSource == null)
        {
            uiAudioSource = GetComponent<AudioSource>();
            if (uiAudioSource == null) uiAudioSource = gameObject.AddComponent<AudioSource>();
            uiAudioSource.playOnAwake = false;
            uiAudioSource.spatialBlend = 0f;
            uiAudioSource.ignoreListenerPause = true;
        }

        if (cachedScrollOpenClip == null) cachedScrollOpenClip = BuildProceduralPaperSFX(0.26f, true, false);
        if (cachedScrollCloseClip == null) cachedScrollCloseClip = BuildProceduralPaperSFX(0.21f, false, false);
        if (cachedPageFlipClip == null) cachedPageFlipClip = BuildProceduralPaperSFX(0.17f, true, true);
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

    private void PlayPageFlipSFX()
    {
        EnsureAudioSource();
        AudioClip clip = pageFlipSFX != null ? pageFlipSFX : cachedPageFlipClip;
        if (clip != null && uiAudioSource != null)
        {
            uiAudioSource.pitch = Random.Range(0.96f, 1.08f);
            uiAudioSource.PlayOneShot(clip, bookSFXVolume);
        }
    }

    private static AudioClip BuildProceduralPaperSFX(float duration, bool opening, bool isPageFlip)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        uint seed = isPageFlip ? 0x73D241u : (opening ? 0x5A17C3u : 0x9E31B7u);
        float lpHigh = 0f;
        float lpLow = 0f;
        float prevHigh = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float timeSec = (float)i / sampleRate;

            seed = seed * 1664525u + 1013904223u;
            float white = ((seed & 0xFFFF) / 32767.5f) - 1f;

            float hiCut = isPageFlip
                ? Mathf.Lerp(0.44f, 0.72f, t)
                : (opening ? Mathf.Lerp(0.32f, 0.65f, t) : Mathf.Lerp(0.58f, 0.26f, t));
            float loCut = isPageFlip
                ? 0.16f
                : (opening ? Mathf.Lerp(0.10f, 0.22f, t) : Mathf.Lerp(0.20f, 0.09f, t));

            lpHigh += (white - lpHigh) * hiCut;
            lpLow += (white - lpLow) * loCut;
            float paperBand = lpHigh - lpLow;

            float crispEdge = (lpHigh - prevHigh) * (isPageFlip ? 0.58f : 0.45f);
            prevHigh = lpHigh;

            float mainEnv = Mathf.Pow(Mathf.Sin(t * Mathf.PI), isPageFlip ? 0.65f : 0.75f);
            float secondPeakPos = isPageFlip ? 0.55f : (opening ? 0.68f : 0.60f);
            float dPeak = (t - secondPeakPos) / 0.13f;
            float crinkleBurst = Mathf.Exp(-dPeak * dPeak) * 0.55f;

            float fineFlutter = 0.85f + 0.15f * Mathf.Sin(timeSec * (isPageFlip ? 120f : (opening ? 95f : 72f)));
            float env = (mainEnv * 0.85f + crinkleBurst) * fineFlutter;

            samples[i] = Mathf.Clamp((paperBand * 1.45f + crispEdge) * env, -0.95f, 0.95f);
        }

        string clipName = isPageFlip ? "PageFlipSFX" : (opening ? "PaperOpenSFX" : "PaperCloseSFX");
        AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
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

        bookButtonRect = bookButtonObj.AddComponent<RectTransform>();
        bookButtonRect.anchorMin = new Vector2(1f, 1f);
        bookButtonRect.anchorMax = new Vector2(1f, 1f);
        bookButtonRect.pivot = new Vector2(0.5f, 0.5f);
        bookButtonRect.anchoredPosition = new Vector2(
            -(buttonTopRightMargin.x + buttonSize.x * 0.5f),
            -(buttonTopRightMargin.y + buttonSize.y * 0.5f)
        );
        bookButtonRect.sizeDelta = buttonSize;

        BuildGreenAuroraAroundButton(parent, bookButtonRect.anchoredPosition);
        bookButtonObj.transform.SetAsLastSibling();

        Image bg = bookButtonObj.AddComponent<Image>();
        bg.sprite = cachedWhitePixelSprite;
        bg.color = new Color(0.10f, 0.06f, 0.05f, 0.84f);

        Outline outline = bookButtonObj.AddComponent<Outline>();
        outline.effectColor = new Color(0.84f, 0.64f, 0.28f, 0.95f);
        outline.effectDistance = new Vector2(3f, -3f);

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

        scrollButtonLabel = CreateTMPText(
            bookButtonObj.transform,
            "ScrollKeyLabel",
            $"SCROLL [{toggleKey}]",
            13f,
            new Color(0.96f, 0.85f, 0.56f, 1f),
            TextAlignmentOptions.Center,
            FontStyles.Bold
        );
        RectTransform labelRect = scrollButtonLabel.rectTransform;
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
        bookOverlayObj = new GameObject("AncientTomeOverlay");
        bookOverlayObj.transform.SetParent(parent, false);
        RectTransform overlayRect = bookOverlayObj.AddComponent<RectTransform>();
        StretchFill(overlayRect);

        bookOverlayCanvasGroup = bookOverlayObj.AddComponent<CanvasGroup>();

        Image backdropImg = bookOverlayObj.AddComponent<Image>();
        backdropImg.sprite = cachedWhitePixelSprite;
        backdropImg.color = new Color(0.03f, 0.02f, 0.02f, 0.78f);

        Button backdropBtn = bookOverlayObj.AddComponent<Button>();
        backdropBtn.transition = Selectable.Transition.None;
        backdropBtn.onClick.AddListener(CloseBook);

        // Outer Lacquer / Cedar Cover
        GameObject coverObj = new GameObject("AncientTomeSpread");
        coverObj.transform.SetParent(bookOverlayObj.transform, false);
        bookSpreadRect = coverObj.AddComponent<RectTransform>();
        bookSpreadRect.anchorMin = new Vector2(0.5f, 0.5f);
        bookSpreadRect.anchorMax = new Vector2(0.5f, 0.5f);
        bookSpreadRect.sizeDelta = new Vector2(1100f, 680f);

        Image coverImg = coverObj.AddComponent<Image>();
        coverImg.sprite = cachedWhitePixelSprite;
        coverImg.color = new Color(0.18f, 0.10f, 0.07f, 1f);

        Outline coverGoldTrim = coverObj.AddComponent<Outline>();
        coverGoldTrim.effectColor = new Color(0.76f, 0.56f, 0.22f, 1f);
        coverGoldTrim.effectDistance = new Vector2(5f, -5f);

        CreateVerticalRod(coverObj.transform, "LeftWoodenRod", 0f, -14f);
        CreateVerticalRod(coverObj.transform, "RightWoodenRod", 1f, 14f);

        TMP_Text banner = CreateTMPText(
            coverObj.transform,
            "TomeBannerTitle",
            tomeBannerTitle,
            24f,
            new Color(0.95f, 0.82f, 0.48f, 1f),
            TextAlignmentOptions.Center,
            FontStyles.Bold
        );
        SetAnchoredBox(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 24f), new Vector2(720f, 38f));

        // Aged paper base
        GameObject pageStackObj = CreatePanel(
            coverObj.transform,
            "AgedPaperBase",
            new Color(0.56f, 0.45f, 0.30f, 1f),
            new Vector2(22f, 22f),
            new Vector2(-22f, -22f)
        );

        // Animated page-flip wrapper holding all 3 spreads
        GameObject pagesWrap = new GameObject("PagesFlipWrapper");
        pagesWrap.transform.SetParent(pageStackObj.transform, false);
        pagesContentRect = pagesWrap.AddComponent<RectTransform>();
        StretchFill(pagesContentRect);

        // --- SPREAD 0: Heal Guide (Pages I & II) ---
        spreadContainers[0] = CreateSpreadContainer(pagesContentRect, "Spread0_HealGuide", out Transform s0Left, out Transform s0Right);
        BuildHealLeftPage(s0Left);
        BuildHealRightPage(s0Right);

        // --- SPREAD 1: Enemy Bestiary — 2 Enemy Types per Page (Pages III & IV) ---
        spreadContainers[1] = CreateSpreadContainer(pagesContentRect, "Spread1_Bestiary", out Transform s1Left, out Transform s1Right);
        BuildBestiaryTwoSlotPage(s1Left, "III", "ENEMIES", BestiaryEnemyId.ShadowNinja, BestiaryEnemyId.ShurikenNinja, true);
        BuildBestiaryTwoSlotPage(s1Right, "IV", "ENEMIES", BestiaryEnemyId.SuicideNinja, BestiaryEnemyId.ArmoredNinja, false);

        // --- SPREAD 2: Boss Chronicle & Combat Arts (Pages V & VI) ---
        spreadContainers[2] = CreateSpreadContainer(pagesContentRect, "Spread2_BossAndArts", out Transform s2Left, out Transform s2Right);
        BuildBossBestiaryPage(s2Left);
        BuildShinobiTipsPage(s2Right);

        // Page-flip shadow overlay (fades in briefly mid-flip for tactile depth)
        GameObject flipShadowObj = new GameObject("PageFlipShadow");
        flipShadowObj.transform.SetParent(pageStackObj.transform, false);
        RectTransform flipShadowRect = flipShadowObj.AddComponent<RectTransform>();
        StretchFill(flipShadowRect);
        pageFlipShadowImg = flipShadowObj.AddComponent<Image>();
        pageFlipShadowImg.sprite = cachedWhitePixelSprite;
        pageFlipShadowImg.color = new Color(0.10f, 0.06f, 0.03f, 0f);
        pageFlipShadowImg.raycastTarget = false;

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

        // Top-Right Close [X] Button
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

        // Navigation Bar below the book: < PREV   |   PAGE 1 / 3   |   NEXT >
        prevPageButton = CreateNavButton(coverObj.transform, "PrevPageButton", "< PREV", new Vector2(0f, 0f), new Vector2(96f, -38f), PreviousSpread);
        nextPageButton = CreateNavButton(coverObj.transform, "NextPageButton", "NEXT >", new Vector2(1f, 0f), new Vector2(-96f, -38f), NextSpread);

        spreadCounterText = CreateTMPText(
            coverObj.transform,
            "SpreadCounterText",
            "PAGES 1 - 2   ( 1 / 3 )",
            20f,
            new Color(0.95f, 0.88f, 0.68f, 0.95f),
            TextAlignmentOptions.Center,
            FontStyles.Bold
        );
        SetAnchoredBox(spreadCounterText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -38f), new Vector2(460f, 36f));
    }

    private GameObject CreateSpreadContainer(Transform parent, string name, out Transform leftPage, out Transform rightPage)
    {
        GameObject container = new GameObject(name);
        container.transform.SetParent(parent, false);
        RectTransform rt = container.AddComponent<RectTransform>();
        StretchFill(rt);

        leftPage = CreateParchmentPage(container.transform, name + "_Left", new Vector2(12f, 12f), new Vector2(-530f, -12f)).transform;
        rightPage = CreateParchmentPage(container.transform, name + "_Right", new Vector2(530f, 12f), new Vector2(-12f, -12f)).transform;
        return container;
    }

    private Button CreateNavButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(168f, 44f);

        Image bg = obj.AddComponent<Image>();
        bg.sprite = cachedWhitePixelSprite;
        bg.color = new Color(0.22f, 0.12f, 0.08f, 0.96f);

        Outline outline = obj.AddComponent<Outline>();
        outline.effectColor = new Color(0.84f, 0.65f, 0.28f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        TMP_Text txt = CreateTMPText(obj.transform, "Label", label, 20f, new Color(0.96f, 0.88f, 0.66f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
        StretchFill(txt.rectTransform);

        Button btn = obj.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(onClick);
        return btn;
    }

    #endregion

    #region Spread 0: Heal Guide Pages

    private void BuildHealLeftPage(Transform page)
    {
        Color inkSumi = new Color(0.16f, 0.11f, 0.08f, 1f);
        Color jadeInk = new Color(0.08f, 0.45f, 0.22f, 1f);

        CreateThinInkFrame(page, new Color(0.45f, 0.33f, 0.20f, 0.45f));

        TMP_Text tag = CreateTMPText(page, "LeftTag", $"[ I ]  {leftPageTag}", 18f, new Color(0.48f, 0.32f, 0.18f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
        SetAnchoredBox(tag.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(400f, 28f));

        TMP_Text title = CreateTMPText(page, "TitleLeft", leftPageTitle, 34f, jadeInk, TextAlignmentOptions.Center, FontStyles.Bold);
        SetAnchoredBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(430f, 44f));

        GameObject diagramObj = new GameObject("PlusLineDrawing");
        diagramObj.transform.SetParent(page, false);
        RectTransform diagRect = diagramObj.AddComponent<RectTransform>();
        SetAnchoredBox(diagRect, new Vector2(0.5f, 0.55f), Vector2.zero, new Vector2(180f, 180f));
        Image diagImg = diagramObj.AddComponent<Image>();
        diagImg.sprite = cachedPlusDiagramSprite;
        diagImg.preserveAspect = true;
        diagImg.raycastTarget = false;

        plusSparkleTrail = CreateSparkleTrail(diagramObj.transform, 4);
        plusGuideDot = CreateStrokeGuideDot(diagramObj.transform, Color.white);
        plusGuideDot.sizeDelta = new Vector2(22f, 22f);

        GameObject pill = CreatePanel(page, "LeftStatsPill", new Color(0.12f, 0.40f, 0.20f, 0.14f), Vector2.zero, Vector2.zero);
        SetAnchoredBox(pill.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 152f), new Vector2(400f, 42f));
        leftStatsText = CreateTMPText(pill.transform, "LeftStats", "+25 HP   •   15 METER", 24f, jadeInk, TextAlignmentOptions.Center, FontStyles.Bold);
        StretchFill(leftStatsText.rectTransform);

        TMP_Text steps = CreateTMPText(page, "LeftSteps", leftPageSteps, 21f, inkSumi, TextAlignmentOptions.Center, FontStyles.Normal);
        SetAnchoredBox(steps.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 72f), new Vector2(420f, 100f));

        PlaceRedStamp(page, new Vector2(0f, 0f), new Vector2(44f, 44f));
    }

    private void BuildHealRightPage(Transform page)
    {
        Color inkSumi = new Color(0.16f, 0.11f, 0.08f, 1f);
        Color crimsonInk = new Color(0.68f, 0.10f, 0.22f, 1f);

        CreateThinInkFrame(page, new Color(0.45f, 0.33f, 0.20f, 0.45f));

        TMP_Text tag = CreateTMPText(page, "RightTag", $"[ II ]  {rightPageTag}", 18f, new Color(0.48f, 0.32f, 0.18f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
        SetAnchoredBox(tag.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(400f, 28f));

        TMP_Text title = CreateTMPText(page, "TitleRight", rightPageTitle, 34f, crimsonInk, TextAlignmentOptions.Center, FontStyles.Bold);
        SetAnchoredBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(430f, 44f));

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

        GameObject pill = CreatePanel(page, "RightStatsPill", new Color(0.68f, 0.12f, 0.22f, 0.13f), Vector2.zero, Vector2.zero);
        SetAnchoredBox(pill.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 152f), new Vector2(400f, 42f));
        rightStatsText = CreateTMPText(pill.transform, "RightStats", "+50 HP   •   28 METER", 24f, crimsonInk, TextAlignmentOptions.Center, FontStyles.Bold);
        StretchFill(rightStatsText.rectTransform);

        TMP_Text steps = CreateTMPText(page, "RightSteps", rightPageSteps, 21f, inkSumi, TextAlignmentOptions.Center, FontStyles.Normal);
        SetAnchoredBox(steps.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 72f), new Vector2(420f, 100f));

        PlaceRedStamp(page, new Vector2(1f, 0f), new Vector2(-44f, 44f));
    }

    #endregion

    #region Spread 1 & 2: Enemy Bestiary Pages (2 Enemies Per Page)

    private void BuildBestiaryTwoSlotPage(Transform page, string romanNum, string headerTitle, BestiaryEnemyId topEnemy, BestiaryEnemyId bottomEnemy, bool stampLeft)
    {
        CreateThinInkFrame(page, new Color(0.45f, 0.33f, 0.20f, 0.45f));

        TMP_Text header = CreateTMPText(
            page,
            $"BestiaryHeader_{romanNum}",
            $"[ {romanNum} ]  {headerTitle}",
            20f,
            new Color(0.48f, 0.28f, 0.15f, 1f),
            TextAlignmentOptions.Center,
            FontStyles.Bold
        );
        SetAnchoredBox(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(420f, 30f));

        // Top Enemy Slot
        BuildBestiaryCard(page, topEnemy, new Vector2(0f, 118f), new Vector2(436f, 224f));

        // Subtle horizontal ink divider between the 2 enemies on the page
        GameObject divider = CreatePanel(page, "SlotDivider", new Color(0.42f, 0.30f, 0.18f, 0.35f), Vector2.zero, Vector2.zero);
        SetAnchoredBox(divider.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(390f, 3f));

        // Bottom Enemy Slot
        BuildBestiaryCard(page, bottomEnemy, new Vector2(0f, -134f), new Vector2(436f, 224f));

        if (stampLeft) PlaceRedStamp(page, new Vector2(0f, 0f), new Vector2(42f, 42f));
        else PlaceRedStamp(page, new Vector2(1f, 0f), new Vector2(-42f, 42f));
    }

    private void BuildBossBestiaryPage(Transform page)
    {
        CreateThinInkFrame(page, new Color(0.45f, 0.33f, 0.20f, 0.45f));

        TMP_Text header = CreateTMPText(
            page,
            "BossHeader",
            "[ V ]  BOSS CHRONICLE",
            20f,
            new Color(0.68f, 0.12f, 0.16f, 1f),
            TextAlignmentOptions.Center,
            FontStyles.Bold
        );
        SetAnchoredBox(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(420f, 30f));

        // Single large Boss Card centered on Page V
        BuildBestiaryCard(page, BestiaryEnemyId.LegendarySamurai, new Vector2(0f, -10f), new Vector2(436f, 390f), isLargeBossCard: true);

        PlaceRedStamp(page, new Vector2(0f, 0f), new Vector2(42f, 42f));
    }

    private void BuildShinobiTipsPage(Transform page)
    {
        Color inkSumi = new Color(0.16f, 0.11f, 0.08f, 1f);
        Color accent = new Color(0.58f, 0.18f, 0.12f, 1f);

        CreateThinInkFrame(page, new Color(0.45f, 0.33f, 0.20f, 0.45f));

        TMP_Text header = CreateTMPText(page, "TipsHeader", "[ VI ]  SHINOBI ARTS", 20f, accent, TextAlignmentOptions.Center, FontStyles.Bold);
        SetAnchoredBox(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(420f, 30f));

        TMP_Text title = CreateTMPText(page, "TipsTitle", "COMBAT SECRETS", 30f, accent, TextAlignmentOptions.Center, FontStyles.Bold);
        SetAnchoredBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -82f), new Vector2(420f, 40f));

        string tipsBody =
            "• CHAIN SLASH\nHold Attack to slow time and slice through multiple enemies.\n\n" +
            "• GRAPPLE HOOK\nReel light enemies in to kick them, or grab sliced corpses and throw them!\n\n" +
            "• DEFEAT TO UNLOCK\nSlay each enemy type once to reveal their secrets in this scroll.";

        TMP_Text body = CreateTMPText(page, "TipsBody", tipsBody, 20f, inkSumi, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        SetAnchoredBox(body.rectTransform, new Vector2(0.5f, 0.46f), Vector2.zero, new Vector2(396f, 370f));

        PlaceRedStamp(page, new Vector2(1f, 0f), new Vector2(-42f, 42f));
    }

    private void BuildBestiaryCard(Transform page, BestiaryEnemyId id, Vector2 anchoredPos, Vector2 cardSize, bool isLargeBossCard = false)
    {
        GameObject cardObj = CreatePanel(page, $"BestiaryCard_{id}", new Color(0.45f, 0.33f, 0.20f, 0.10f), Vector2.zero, Vector2.zero);
        SetAnchoredBox(cardObj.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), anchoredPos, cardSize);

        // Portrait Frame (left side for 2-slot cards, top-center for large Boss card)
        GameObject portraitBox = CreatePanel(cardObj.transform, "PortraitFrame", new Color(0.24f, 0.17f, 0.12f, 0.22f), Vector2.zero, Vector2.zero);
        RectTransform portRect = portraitBox.GetComponent<RectTransform>();
        Vector2 portraitSize = isLargeBossCard ? new Vector2(168f, 168f) : new Vector2(124f, 148f);
        if (isLargeBossCard)
        {
            SetAnchoredBox(portRect, new Vector2(0.5f, 1f), new Vector2(0f, -102f), portraitSize);
        }
        else
        {
            SetAnchoredBox(portRect, new Vector2(0f, 0.5f), new Vector2(74f, 0f), portraitSize);
        }

        // Shimmering Golden Aura Border (only active when this enemy is newly unlocked!)
        GameObject goldAuraObj = new GameObject("GoldUnlockBorderGlow");
        goldAuraObj.transform.SetParent(portraitBox.transform, false);
        RectTransform goldAuraRect = goldAuraObj.AddComponent<RectTransform>();
        goldAuraRect.anchorMin = new Vector2(0.5f, 0.5f);
        goldAuraRect.anchorMax = new Vector2(0.5f, 0.5f);
        goldAuraRect.sizeDelta = portraitSize + new Vector2(22f, 22f);
        Image goldAuraImg = goldAuraObj.AddComponent<Image>();
        goldAuraImg.sprite = cachedWhitePixelSprite;
        goldAuraImg.color = new Color(1.0f, 0.84f, 0.22f, 0.0f);
        goldAuraImg.raycastTarget = false;
        goldAuraObj.transform.SetAsFirstSibling();
        goldAuraObj.SetActive(false);

        Outline portOutline = portraitBox.AddComponent<Outline>();
        portOutline.effectColor = new Color(0.42f, 0.28f, 0.16f, 0.65f);
        portOutline.effectDistance = new Vector2(2f, -2f);

        // Enemy Sprite Portrait (pure black silhouette when locked, full color when unlocked)
        GameObject sprObj = new GameObject("EnemyPortrait");
        sprObj.transform.SetParent(portraitBox.transform, false);
        RectTransform sprRect = sprObj.AddComponent<RectTransform>();
        sprRect.anchorMin = new Vector2(0.5f, 0.5f);
        sprRect.anchorMax = new Vector2(0.5f, 0.5f);
        sprRect.sizeDelta = isLargeBossCard ? new Vector2(140f, 140f) : new Vector2(98f, 120f);

        Image portImg = sprObj.AddComponent<Image>();
        portImg.preserveAspect = true;
        portImg.raycastTarget = false;

        // Golden "NEW!" corner badge (only shown while newly unlocked)
        GameObject newBadgeObj = CreatePanel(portraitBox.transform, "NewUnlockBadge", new Color(0.88f, 0.64f, 0.10f, 1f), Vector2.zero, Vector2.zero);
        SetAnchoredBox(newBadgeObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, 6f), new Vector2(70f, 24f));
        TMP_Text newBadgeTxt = CreateTMPText(newBadgeObj.transform, "NewLabel", "NEW!", 14f, new Color(0.18f, 0.08f, 0.02f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
        StretchFill(newBadgeTxt.rectTransform);
        newBadgeObj.SetActive(false);

        // Centered "?" Overlay on top of the black silhouette when locked
        TMP_Text qMark = CreateTMPText(
            portraitBox.transform,
            "QuestionOverlay",
            "?",
            isLargeBossCard ? 64f : 50f,
            new Color(0.98f, 0.88f, 0.52f, 1f),
            TextAlignmentOptions.Center,
            FontStyles.Bold
        );
        StretchFill(qMark.rectTransform);
        qMark.outlineWidth = 0.25f;
        qMark.outlineColor = new Color32(0, 0, 0, 230);

        // Enemy Name + Ability Description
        TMP_Text nameTxt;
        TMP_Text descTxt;
        if (isLargeBossCard)
        {
            nameTxt = CreateTMPText(cardObj.transform, "EnemyName", "? ? ?", 28f, new Color(0.68f, 0.12f, 0.16f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
            SetAnchoredBox(nameTxt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 152f), new Vector2(400f, 38f));

            descTxt = CreateTMPText(cardObj.transform, "EnemyDesc", "? ? ?", 19f, new Color(0.18f, 0.12f, 0.09f, 1f), TextAlignmentOptions.Top, FontStyles.Normal);
            SetAnchoredBox(descTxt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 76f), new Vector2(396f, 114f));
        }
        else
        {
            nameTxt = CreateTMPText(cardObj.transform, "EnemyName", "? ? ?", 23f, new Color(0.58f, 0.14f, 0.14f, 1f), TextAlignmentOptions.Left, FontStyles.Bold);
            SetAnchoredBox(nameTxt.rectTransform, new Vector2(0f, 1f), new Vector2(286f, -44f), new Vector2(276f, 34f));

            descTxt = CreateTMPText(cardObj.transform, "EnemyDesc", "? ? ?", 17f, new Color(0.18f, 0.12f, 0.09f, 1f), TextAlignmentOptions.TopLeft, FontStyles.Normal);
            SetAnchoredBox(descTxt.rectTransform, new Vector2(0f, 1f), new Vector2(286f, -134f), new Vector2(276f, 136f));
        }

        bestiaryCards[id] = new BestiaryCardUI
        {
            id = id,
            portraitImage = portImg,
            portraitOutline = portOutline,
            goldAuraBorderImage = goldAuraImg,
            goldAuraBorderRect = goldAuraRect,
            newBadgeObj = newBadgeObj,
            questionMarkOverlay = qMark,
            nameText = nameTxt,
            descText = descTxt
        };
    }

    private void RefreshAllBestiaryCards()
    {
        for (int i = 0; i < BestiaryCatalog.Length; i++)
        {
            RefreshBestiaryCard(BestiaryCatalog[i].id);
        }
    }

    private void RefreshBestiaryCard(BestiaryEnemyId id)
    {
        if (!bestiaryCards.TryGetValue(id, out BestiaryCardUI card) || card == null) return;

        BestiaryEntryData data = BestiaryCatalog[(int)id];
        bool isUnlocked = unlockedEnemies.Contains(id);

        Sprite fallbackSil = (id == BestiaryEnemyId.LegendarySamurai) ? cachedSamuraiSilhouetteSprite : cachedNinjaSilhouetteSprite;
        Sprite spr = capturedEnemySprites.TryGetValue(id, out Sprite cap) && cap != null ? cap : fallbackSil;

        if (card.portraitImage != null)
        {
            card.portraitImage.sprite = spr;
            if (isUnlocked)
            {
                Color tint = capturedEnemyColors.TryGetValue(id, out Color capCol) ? capCol : Color.white;
                card.portraitImage.color = tint;
            }
            else
            {
                // Solid black silhouette until this enemy type is defeated
                card.portraitImage.color = Color.black;
            }
        }

        if (card.questionMarkOverlay != null)
        {
            card.questionMarkOverlay.gameObject.SetActive(!isUnlocked);
        }

        if (card.nameText != null)
        {
            card.nameText.text = isUnlocked ? WrapNumbersWithNormalFont(data.displayName) : "? ? ?";
            card.nameText.color = isUnlocked ? data.accentColor : new Color(0.26f, 0.20f, 0.16f, 1f);
        }

        bool isNewlyUnlocked = isUnlocked && newlyUnlockedHighlights.Contains(id);

        if (card.portraitOutline != null)
        {
            card.portraitOutline.effectColor = isNewlyUnlocked
                ? new Color(1.0f, 0.84f, 0.22f, 1f)
                : new Color(0.42f, 0.28f, 0.16f, 0.65f);
            card.portraitOutline.effectDistance = isNewlyUnlocked ? new Vector2(5f, -5f) : new Vector2(2f, -2f);
        }

        if (card.goldAuraBorderImage != null)
        {
            card.goldAuraBorderImage.gameObject.SetActive(isNewlyUnlocked);
        }

        if (card.newBadgeObj != null)
        {
            card.newBadgeObj.SetActive(isNewlyUnlocked);
        }

        if (card.descText != null)
        {
            card.descText.text = isUnlocked
                ? WrapNumbersWithNormalFont(data.abilityDescription)
                : "? ? ?";
        }
    }

    private void UpdateGoldenHighlightEffects()
    {
        if (newlyUnlockedHighlights.Count == 0) return;

        float t = strokeAnimTime;
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 6.0f);
        Color deepGold = new Color(0.95f, 0.68f, 0.12f, 1f);
        Color brightGold = new Color(1.00f, 0.94f, 0.52f, 1f);
        Color shimmerCol = Color.Lerp(deepGold, brightGold, pulse);

        foreach (BestiaryEnemyId id in newlyUnlockedHighlights)
        {
            if (!bestiaryCards.TryGetValue(id, out BestiaryCardUI card) || card == null) continue;

            if (card.portraitOutline != null)
            {
                card.portraitOutline.effectColor = shimmerCol;
            }
            if (card.goldAuraBorderImage != null)
            {
                Color glow = shimmerCol;
                glow.a = 0.35f + 0.30f * pulse;
                card.goldAuraBorderImage.color = glow;
            }
            if (card.goldAuraBorderRect != null)
            {
                float s = 1.0f + 0.045f * pulse;
                card.goldAuraBorderRect.localScale = new Vector3(s, s, 1f);
            }
        }
    }

    #endregion

    #region Page Flip & Open/Close Animations

    private void ApplySpreadVisibility(int spreadIndex)
    {
        currentSpreadIndex = Mathf.Clamp(spreadIndex, 0, TotalSpreads - 1);

        for (int i = 0; i < spreadContainers.Length; i++)
        {
            if (spreadContainers[i] != null)
            {
                spreadContainers[i].SetActive(i == currentSpreadIndex);
            }
        }

        if (prevPageButton != null) prevPageButton.interactable = currentSpreadIndex > 0;
        if (nextPageButton != null) nextPageButton.interactable = currentSpreadIndex < TotalSpreads - 1;

        if (spreadCounterText != null)
        {
            int leftP = currentSpreadIndex * 2 + 1;
            int rightP = leftP + 1;
            spreadCounterText.text = WrapNumbersWithNormalFont($"PAGES {leftP} - {rightP}   ( {currentSpreadIndex + 1} / {TotalSpreads} )");
        }
    }

    private IEnumerator FlipSpreadRoutine(int targetSpread)
    {
        isFlippingPage = true;
        PlayPageFlipSFX();

        const float halfDuration = 0.11f;
        float elapsed = 0f;

        // Fold current spread closed horizontally
        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / halfDuration);
            float easeIn = t * t;
            if (pagesContentRect != null)
            {
                pagesContentRect.localScale = new Vector3(Mathf.Lerp(1f, 0.04f, easeIn), 1f, 1f);
            }
            if (pageFlipShadowImg != null)
            {
                pageFlipShadowImg.color = new Color(0.10f, 0.06f, 0.03f, easeIn * 0.35f);
            }
            yield return null;
        }

        ApplySpreadVisibility(targetSpread);
        RefreshAllBestiaryCards();

        // Unfold new spread open horizontally
        elapsed = 0f;
        const float openDuration = 0.13f;
        while (elapsed < openDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / openDuration);
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);
            if (pagesContentRect != null)
            {
                pagesContentRect.localScale = new Vector3(Mathf.Lerp(0.04f, 1f, easeOut), 1f, 1f);
            }
            if (pageFlipShadowImg != null)
            {
                pageFlipShadowImg.color = new Color(0.10f, 0.06f, 0.03f, (1f - easeOut) * 0.35f);
            }
            yield return null;
        }

        if (pagesContentRect != null) pagesContentRect.localScale = Vector3.one;
        if (pageFlipShadowImg != null) pageFlipShadowImg.color = new Color(0.10f, 0.06f, 0.03f, 0f);
        isFlippingPage = false;
        pageFlipCoroutine = null;
    }

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

    #endregion

    #region Stroke Guides & Helpers

    private Vector2 SamplePlusPath(float time)
    {
        float cycle = (time * 0.42f) % 1f;
        if (cycle < 0f) cycle += 1f;
        float dist = cycle * 6f;

        Vector2 normPos;
        if (dist < 2f) normPos = Vector2.Lerp(PlusStrokePath[0], PlusStrokePath[1], dist / 2f);
        else if (dist < 3f) normPos = Vector2.Lerp(PlusStrokePath[1], PlusStrokePath[2], dist - 2f);
        else if (dist < 4f) normPos = Vector2.Lerp(PlusStrokePath[2], PlusStrokePath[3], dist - 3f);
        else normPos = Vector2.Lerp(PlusStrokePath[3], PlusStrokePath[4], (dist - 4f) / 2f);

        return normPos * 90f;
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

        plusGuideDot.anchoredPosition = SamplePlusPath(strokeAnimTime);
        float twinkle = 0.88f + Mathf.Sin(strokeAnimTime * 14f) * 0.18f;
        plusGuideDot.localScale = new Vector3(twinkle, twinkle, 1f);
        plusGuideDot.localRotation = Quaternion.Euler(0f, 0f, strokeAnimTime * 90f);

        if (plusSparkleTrail != null)
        {
            for (int i = 0; i < plusSparkleTrail.Length; i++)
            {
                if (plusSparkleTrail[i] == null) continue;
                plusSparkleTrail[i].anchoredPosition = SamplePlusPath(strokeAnimTime - (i + 1) * 0.045f);
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

    private static string WrapNumbersWithNormalFont(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return System.Text.RegularExpressions.Regex.Replace(input, @"\d+", "<font=\"LiberationSans SDF\">$0</font>");
    }

    private void RefreshStatsText()
    {
        float plusHeal = boundPlayer != null ? boundPlayer.PlusHealAmount : 25f;
        float plusCost = boundPlayer != null ? boundPlayer.PlusHealMeterCost : 15f;
        float heartHeal = boundPlayer != null ? boundPlayer.HeartHealAmount : 50f;
        float heartCost = boundPlayer != null ? boundPlayer.HeartHealMeterCost : 28f;

        if (leftStatsText != null)
        {
            leftStatsText.text = WrapNumbersWithNormalFont($"+{Mathf.RoundToInt(plusHeal)} HP   •   {Mathf.RoundToInt(plusCost)} METER");
        }
        if (rightStatsText != null)
        {
            rightStatsText.text = WrapNumbersWithNormalFont($"+{Mathf.RoundToInt(heartHeal)} HP   •   {Mathf.RoundToInt(heartCost)} METER");
        }
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
        rt.sizeDelta = new Vector2(32f, 32f);

        Image img = stamp.AddComponent<Image>();
        img.sprite = cachedRedSealSprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

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

    private TMP_Text CreateTMPText(Transform parent, string name, string content, float fontSize, Color color, TextAlignmentOptions alignment, FontStyles style)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset resolvedFont = ResolveBookFont();
        if (resolvedFont != null) tmp.font = resolvedFont;
        tmp.text = WrapNumbersWithNormalFont(content);
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

    #endregion

    #region Procedural Pixel Sprites

    private static void EnsureProceduralSprites()
    {
        if (cachedWhitePixelSprite == null || cachedWhitePixelSprite.texture == null)
        {
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            Color[] cols = { Color.white, Color.white, Color.white, Color.white };
            tex.SetPixels(cols);
            tex.Apply();
            cachedWhitePixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 16f);
        }

        if (cachedGlowDotSprite == null || cachedGlowDotSprite.texture == null)
        {
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

        if (cachedAuroraGlowSprite == null || cachedAuroraGlowSprite.texture == null)
        {
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
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(radial, 1.85f) * ribbonWave));
                }
            }
            tex.Apply();
            cachedAuroraGlowSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
        }

        if (cachedScrollIconSprite == null || cachedScrollIconSprite.texture == null) cachedScrollIconSprite = BuildAncientScrollIconSprite();
        if (cachedParchmentSprite == null || cachedParchmentSprite.texture == null) cachedParchmentSprite = BuildWeatheredParchmentSprite();
        if (cachedRedSealSprite == null || cachedRedSealSprite.texture == null) cachedRedSealSprite = BuildRedSealSprite();
        if (cachedPlusDiagramSprite == null || cachedPlusDiagramSprite.texture == null) cachedPlusDiagramSprite = BuildPlusLineSprite();
        if (cachedHeartDiagramSprite == null || cachedHeartDiagramSprite.texture == null) cachedHeartDiagramSprite = BuildHeartDiagramSprite();
        if (cachedNinjaSilhouetteSprite == null || cachedNinjaSilhouetteSprite.texture == null) cachedNinjaSilhouetteSprite = BuildHumanoidSilhouetteSprite(false);
        if (cachedSamuraiSilhouetteSprite == null || cachedSamuraiSilhouetteSprite.texture == null) cachedSamuraiSilhouetteSprite = BuildHumanoidSilhouetteSprite(true);
    }

    private static Sprite BuildHumanoidSilhouetteSprite(bool isSamuraiBoss)
    {
        const int w = 16;
        const int h = 20;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color clear = new Color(0f, 0f, 0f, 0f);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, clear);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool filled = false;

                // Head
                if (x >= 6 && x <= 9 && y >= 13 && y <= 16) filled = true;

                // Samurai horned kabuto helmet crest
                if (isSamuraiBoss)
                {
                    if ((x >= 4 && x <= 11 && y == 16) ||
                        ((x == 4 || x == 11) && y == 17) ||
                        ((x == 3 || x == 12) && y == 18))
                    {
                        filled = true;
                    }
                }
                else
                {
                    // Ninja headband tail
                    if ((x == 10 || x == 11) && y == 15) filled = true;
                }

                // Torso & shoulders
                int shoulderMin = isSamuraiBoss ? 3 : 4;
                int shoulderMax = isSamuraiBoss ? 12 : 11;
                if (x >= shoulderMin && x <= shoulderMax && y >= 7 && y <= 12) filled = true;

                // Legs
                if (((x >= 5 && x <= 6) || (x >= 9 && x <= 10)) && y >= 1 && y <= 6) filled = true;

                if (filled) tex.SetPixel(x, y, Color.white);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    private static Sprite BuildAncientScrollIconSprite()
    {
        const int w = 16, h = 16;
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
                if ((y == 13 || y == 14 || y == 1 || y == 2) && x >= 1 && x <= 14)
                {
                    c = (x == 1 || x == 14) ? gold : wood;
                }
                else if (y >= 3 && y <= 12 && x >= 3 && x <= 12)
                {
                    c = (x == 3 || x == 12 || y == 3) ? paperShade : paperLight;
                    if ((y == 10 && x >= 5 && x <= 10 && x != 8) || (y == 5 && x >= 5 && x <= 10 && x != 7)) c = ink;
                    if (y == 7 || y == 8) c = cord;
                    if ((x == 7 || x == 8) && (y == 7 || y == 8)) c = gold;
                }
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

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
                bool isInnerMark = (x == 5 && y >= 3 && y <= 8) || (y == 6 && x >= 3 && x <= 8) || (x == 7 && y >= 4 && y <= 7) || (y == 4 && x >= 4 && x <= 7);
                tex.SetPixel(x, y, (isOuter || isInnerMark) ? stampRed : clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
    }

    private static Sprite BuildPlusLineSprite()
    {
        const int s = 32;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color inkLine = new Color(0.14f, 0.36f, 0.20f, 1f);

        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                tex.SetPixel(x, y, clear);

        float half = (s - 1) * 0.5f;
        float span = 10.8f;
        DrawPixelSegment(tex, s, new Vector2(half, half - span), new Vector2(half, half + span), 1.05f, inkLine, inkLine);
        DrawPixelSegment(tex, s, new Vector2(half - span, half), new Vector2(half + span, half), 1.05f, inkLine, inkLine);

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
    }

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

                if (dist <= thickness * 0.52f) tex.SetPixel(x, y, coreCol);
                else if (dist <= thickness && tex.GetPixel(x, y).a < 0.9f) tex.SetPixel(x, y, outerCol);
            }
        }
    }

    #endregion
}
