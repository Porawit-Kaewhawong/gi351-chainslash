using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Cinematic Game Over Panel.
/// When the player dies, the entire world fades into pitch black except the player,
/// who remains illuminated in the center of the dark void.
/// Features MainMenu-style button interactions (scale bounce, hover/click audio)
/// and the signature MainMenu dynamic diagonal slash-wipe transition on scene changes.
/// </summary>
[DisallowMultipleComponent]
public class GameOverPanel : MonoBehaviour
{
    public static GameOverPanel Instance { get; private set; }

    [Header("UI References (Optional - Auto Generated if not assigned)")]
    [Tooltip("Root Canvas for Game Over display.")]
    [SerializeField] private Canvas gameOverCanvas;
    [Tooltip("CanvasGroup controlling Game Over UI fade in.")]
    [SerializeField] private CanvasGroup uiCanvasGroup;
    [Tooltip("Main 'GAME OVER' title text.")]
    [SerializeField] private TextMeshProUGUI gameOverTitle;
    [Tooltip("Defeat subtitle text.")]
    [SerializeField] private TextMeshProUGUI subtitleText;
    [Tooltip("Restart button.")]
    [SerializeField] private Button restartButton;
    [Tooltip("Main Menu button.")]
    [SerializeField] private Button mainMenuButton;
    [Tooltip("Quit button.")]
    [SerializeField] private Button quitButton;
    [Tooltip("Keyboard shortcut hint label.")]
    [SerializeField] private TextMeshProUGUI shortcutHintText;

    [Header("Blackout Settings")]
    [Tooltip("Duration in seconds for the world to fade into pitch black.")]
    [SerializeField] private float blackoutFadeDuration = 0.55f;
    [Tooltip("Dramatic pause in the pitch black void before UI fades in.")]
    [SerializeField] private float postBlackoutDelay = 0.2f;
    [Tooltip("Duration in seconds for the Game Over UI to fade in.")]
    [SerializeField] private float uiFadeDuration = 0.4f;
    [Tooltip("Solid blackout color.")]
    [SerializeField] private Color blackoutColor = Color.black;
    [Tooltip("Whether to illuminate the player with a soft spotlight in the void.")]
    [SerializeField] private bool showPlayerSpotlight = true;
    [Tooltip("Color of the subtle player spotlight/vignette glow in the void.")]
    [SerializeField] private Color spotlightColor = new Color(0.85f, 0.15f, 0.25f, 0.42f);

    [Header("MainMenu-Style Slash Transition Settings")]
    [Tooltip("Total duration of the signature diagonal slash-wipe transition.")]
    [SerializeField] private float transitionDuration = 0.55f;
    [Tooltip("Color of the glowing diagonal slash beam.")]
    [SerializeField] private Color slashFlashColor = new Color(0.55f, 0.95f, 1f, 1f);
    [Tooltip("Audio clip played when diagonal slash transition starts.")]
    [SerializeField] private AudioClip startTransitionSFX;

    [Header("Audio Settings")]
    [Tooltip("Audio clip played when Game Over triggers.")]
    [SerializeField] private AudioClip gameOverCueSFX;
    [Tooltip("Audio clip played when UI buttons are clicked.")]
    [SerializeField] private AudioClip buttonClickSFX;
    [Tooltip("Audio clip played when UI buttons are hovered.")]
    [SerializeField] private AudioClip buttonHoverSFX;
    [Range(0f, 1f)] [SerializeField] private float audioVolume = 1f;

    [Header("Visual Styling (Configurable when using auto-generated UI)")]
    [Tooltip("Optional custom TextMeshPro font asset (defaults to PixelifySans if left empty).")]
    [SerializeField] private TMP_FontAsset customFont;

    [Header("Title & Subtitle")]
    [Tooltip("Title text with rich text color support.")]
    [SerializeField] private string titleText = "<color=#FF2A4D>GAME</color> OVER";
    [SerializeField] private float titleFontSize = 86f;
    [Range(0.5f, 0.95f)] [SerializeField] private float titleYAnchor = 0.78f;
    [Tooltip("Subtitle defeated flavor text.")]
    [SerializeField] private string subtitleString = "THE CHAIN HAS BROKEN";
    [SerializeField] private float subtitleFontSize = 22f;
    [SerializeField] private Color subtitleColor = new Color(0.65f, 0.72f, 0.82f, 0.9f);
    [Range(0.5f, 0.95f)] [SerializeField] private float subtitleYAnchor = 0.71f;

    [Header("Buttons Configuration")]
    [Tooltip("Whether to show the Quit button alongside Restart and Main Menu.")]
    [SerializeField] private bool showQuitButton = true;

    [SerializeField] private string restartButtonLabel = "RESTART";
    [SerializeField] private Color restartBorderColor = new Color(0.95f, 0.2f, 0.3f, 1f);
    [SerializeField] private Color restartInnerColor = new Color(0.08f, 0.09f, 0.13f, 0.96f);

    [SerializeField] private string mainMenuButtonLabel = "MAIN MENU";
    [SerializeField] private Color mainMenuBorderColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private Color mainMenuInnerColor = new Color(0.08f, 0.09f, 0.13f, 0.96f);

    [SerializeField] private string quitButtonLabel = "QUIT";
    [SerializeField] private Color quitBorderColor = new Color(0.42f, 0.48f, 0.58f, 1f);
    [SerializeField] private Color quitInnerColor = new Color(0.08f, 0.09f, 0.13f, 0.96f);

    [SerializeField] private Vector2 buttonSize = new Vector2(210f, 56f);
    [SerializeField] private float buttonSpacing = 220f;
    [Range(0.05f, 0.5f)] [SerializeField] private float buttonYAnchor = 0.22f;

#pragma warning disable CS0414
    [Header("Shortcut Hints (Deprecated - shortcuts disabled)")]
    [SerializeField] private string shortcutHintString = "[R] / [Space] Restart   •   [M] Main Menu   •   [Esc] Quit";
    [SerializeField] private Color shortcutHintColor = new Color(0.55f, 0.62f, 0.72f, 0.8f);
#pragma warning restore CS0414

    // Runtime state
    private bool isGameOverActive = false;
    private bool isTransitioning = false;
    private AudioSource audioSource;
    private GameObject blackoutObject;
    private SpriteRenderer blackoutRenderer;
    private GameObject spotlightObject;
    private SpriteRenderer spotlightRenderer;

    // Transition elements (MainMenu style)
    private CanvasGroup transitionCanvasGroup;
    private RectTransform topShutter;
    private RectTransform bottomShutter;
    private RectTransform slashLineRect;
    private Image slashLineImage;
    private readonly List<RectTransform> transitionUIElements = new List<RectTransform>();
    private readonly List<Vector2> transitionUIInitialPositions = new List<Vector2>();

    private readonly Dictionary<SpriteRenderer, int> cachedPlayerSortingOrders = new Dictionary<SpriteRenderer, int>();
    private readonly List<CanvasGroup> fadedOtherCanvases = new List<CanvasGroup>();
    private readonly Dictionary<AudioSource, float> originalAudioVolumes = new Dictionary<AudioSource, float>();
    private readonly List<Coroutine> activeAudioFadeCoroutines = new List<Coroutine>();

    private float lastPlayedSFXTime = -10f;
    private AudioClip lastPlayedSFXClip;

    private static Sprite cachedSolidWhiteSprite;
    private static Sprite cachedSpotlightSprite;
    private static TMP_FontAsset cachedFontAsset;

    public static GameOverPanel GetOrCreateInstance()
    {
        if (Instance != null) return Instance;

        Instance = Object.FindFirstObjectByType<GameOverPanel>();
        if (Instance != null) return Instance;

        GameObject go = new GameObject("GameOverManager");
        Instance = go.AddComponent<GameOverPanel>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        EnsureAudioSource();

#if UNITY_EDITOR
        if (startTransitionSFX == null)
        {
            startTransitionSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/EnteringPortalSFX.mp3");
        }
        if (gameOverCueSFX == null)
        {
            gameOverCueSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/TransitionSFX.mp3");
        }
        if (buttonClickSFX == null)
        {
            buttonClickSFX = startTransitionSFX;
        }
        if (buttonHoverSFX == null)
        {
            buttonHoverSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/DashSFX.mp3");
        }
#endif
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        if (isGameOverActive)
        {
            Time.timeScale = 1f;
            CleanupGameOverVisuals();
        }
    }

    // Interaction is strictly via UI button clicks (no empty Update loop)
    private void DisabledKeyboardShortcuts()
    {
        // Keyboard and gamepad shortcuts are intentionally disabled.
        // Interaction is strictly via UI button clicks.
    }

    [ContextMenu("Preview Game Over in Play Mode")]
    private void PreviewGameOver()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("GameOverPanel preview can only run during Play Mode!");
            return;
        }

        PlayerController player = PlayerController.Instance != null ? PlayerController.Instance : FindFirstObjectByType<PlayerController>();
        Transform pTransform = player != null ? player.transform : transform;
        SpriteRenderer pSprite = player != null ? player.GetComponentInChildren<SpriteRenderer>() : GetComponentInChildren<SpriteRenderer>();

        ShowGameOver(pTransform, pSprite);
    }

    /// <summary>
    /// Triggers the cinematic Game Over sequence:
    /// Everything turns pitch black except the player, followed by UI buttons.
    /// </summary>
    public void ShowGameOver(Transform playerTransform, SpriteRenderer playerSprite)
    {
        if (isGameOverActive) return;
        isGameOverActive = true;
        isTransitioning = false;

        // Ensure cursor is usable
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Cache player renderers and boost sorting order so player sits above the blackout quad
        CacheAndBoostPlayerRenderers(playerTransform);

        // Setup the camera-attached solid black overlay
        EnsureBlackoutOverlay();

        // Setup the subtle spotlight behind the player
        if (showPlayerSpotlight && playerTransform != null)
        {
            EnsurePlayerSpotlight(playerTransform);
        }

        // Setup UI Canvas, buttons, and slash transition system
        EnsureGameOverUI();
        EnsureTransitionUI();

        // Fade out existing HUD / non-GameOver canvases
        FadeOutOtherCanvases();

        // Fade out gameplay audio/BGM for dramatic quiet (excludes player audio)
        FadeOutGameplayAudio();

        // Play defeat sound cue
        PlaySFX(gameOverCueSFX);

        // Run the unscaled blackout & UI fade sequence
        StartCoroutine(GameOverSequenceRoutine());
    }

    private IEnumerator GameOverSequenceRoutine()
    {
        // Phase 1: Everything fades into pitch black while time slows down
        float elapsed = 0f;
        float initialTimeScale = Time.timeScale;

        while (elapsed < blackoutFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / blackoutFadeDuration);
            float smoothT = t * t * (3f - 2f * t);

            if (blackoutRenderer != null)
            {
                blackoutRenderer.color = new Color(blackoutColor.r, blackoutColor.g, blackoutColor.b, smoothT);
            }

            if (spotlightRenderer != null)
            {
                spotlightRenderer.color = new Color(spotlightColor.r, spotlightColor.g, spotlightColor.b, smoothT * spotlightColor.a);
            }

            Time.timeScale = Mathf.Lerp(initialTimeScale, 0f, smoothT);

            yield return null;
        }

        Time.timeScale = 0f;
        if (blackoutRenderer != null)
        {
            blackoutRenderer.color = new Color(blackoutColor.r, blackoutColor.g, blackoutColor.b, 1f);
        }

        // Phase 2: Dramatic brief beat in the dark void with only the player visible
        yield return new WaitForSecondsRealtime(postBlackoutDelay);

        // Phase 3: Fade in Game Over UI Canvas
        if (uiCanvasGroup != null)
        {
            uiCanvasGroup.gameObject.SetActive(true);
            elapsed = 0f;
            Vector3 initialTitleScale = new Vector3(1.12f, 1.12f, 1f);

            while (elapsed < uiFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / uiFadeDuration);
                float smoothT = t * t * (3f - 2f * t);

                uiCanvasGroup.alpha = smoothT;

                if (gameOverTitle != null)
                {
                    gameOverTitle.transform.localScale = Vector3.Lerp(initialTitleScale, Vector3.one, smoothT);
                }

                yield return null;
            }

            uiCanvasGroup.alpha = 1f;
            uiCanvasGroup.blocksRaycasts = true;
            uiCanvasGroup.interactable = true;
            if (gameOverTitle != null) gameOverTitle.transform.localScale = Vector3.one;
        }

        CacheTransitionUIElements();

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    /// <summary>
    /// Executes the signature MainMenu diagonal slash-wipe transition, deletes the dead player in DontDestroyOnLoad,
    /// and reloads the current scene so the new player starts completely fresh with meter, health, and UI intact.
    /// </summary>
    public void RestartGame()
    {
        if (isTransitioning) return;
        PlayButtonClickSFX();

        StartCoroutine(TransitionAndExecute(() =>
        {
            Time.timeScale = 1f;
            isGameOverActive = false;
            isTransitioning = false;

            CleanupGameOverVisuals();

            // Destroy the dead player in DontDestroyOnLoad so the new scene instantiates a fresh player with all UI bound
            PlayerController.DestroyCurrentInstance();

            ChainEnemy.isExploding = false;

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.buildIndex >= 0)
            {
                SceneManager.LoadScene(activeScene.buildIndex);
            }
            else
            {
                SceneManager.LoadScene(activeScene.name);
            }
        }));
    }

    /// <summary>
    /// Executes the signature MainMenu diagonal slash-wipe transition, destroys the persistent player,
    /// and returns to Main Menu (Stage0).
    /// </summary>
    public void ReturnToMainMenu()
    {
        if (isTransitioning) return;
        PlayButtonClickSFX();

        StartCoroutine(TransitionAndExecute(() =>
        {
            Time.timeScale = 1f;
            isGameOverActive = false;
            isTransitioning = false;

            CleanupGameOverVisuals();

            // Destroy persistent player before entering Main Menu so it starts completely fresh
            PlayerController.DestroyCurrentInstance();

            ChainEnemy.isExploding = false;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            // Load Stage0 (First scene in Build Settings / Main Menu)
            if (Application.CanStreamedLevelBeLoaded("Stage0"))
            {
                SceneManager.LoadScene("Stage0");
            }
            else
            {
                SceneManager.LoadScene(0);
            }
        }));
    }

    /// <summary>
    /// Quit button behavior: Returns to Main Menu (Stage0).
    /// </summary>
    public void QuitGame()
    {
        ReturnToMainMenu();
    }

    /// <summary>
    /// MainMenu-style transition: UI dispersal + diagonal cyan/white slash beam + slamming shutters + cross scene fade-in.
    /// </summary>
    private IEnumerator TransitionAndExecute(System.Action onTransitionComplete)
    {
        isTransitioning = true;

        EnsureTransitionUI();

        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.blocksRaycasts = true;
        }

        PlaySFX(startTransitionSFX);

        // Phase 1: Dynamic UI dispersal outward + diagonal slash beam cutting across screen (-18 degrees)
        float slashDuration = Mathf.Max(0.22f, transitionDuration * 0.45f);
        float timer = 0f;

        while (timer < slashDuration)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / slashDuration);
            float easeIn = t * t;
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);

            // Disperse UI elements to the left and right
            for (int i = 0; i < transitionUIElements.Count; i++)
            {
                if (transitionUIElements[i] == null) continue;
                Vector2 dir = (i % 2 == 0) ? new Vector2(160f, 0f) : new Vector2(-160f, 0f);
                transitionUIElements[i].anchoredPosition = Vector2.Lerp(transitionUIInitialPositions[i], transitionUIInitialPositions[i] + dir, easeIn);
            }

            if (uiCanvasGroup != null)
            {
                uiCanvasGroup.alpha = 1f - easeOut;
            }

            // Slice the glowing slash line across the screen
            if (slashLineRect != null && slashLineImage != null)
            {
                float width = Mathf.Lerp(0f, 3600f, easeOut);
                float thickness = Mathf.Lerp(26f, 6f, t);
                slashLineRect.sizeDelta = new Vector2(width, thickness);
                slashLineImage.color = Color.Lerp(Color.white, slashFlashColor, t);
            }

            yield return null;
        }

        // Phase 2: Diagonal shutters slam shut along the slash line
        float shutterDuration = Mathf.Max(0.25f, transitionDuration * 0.55f);
        timer = 0f;

        while (timer < shutterDuration)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / shutterDuration);
            float easeInOut = t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

            if (topShutter != null && bottomShutter != null)
            {
                topShutter.anchoredPosition = Vector2.Lerp(new Vector2(0f, 1600f), new Vector2(0f, 895f), easeInOut);
                bottomShutter.anchoredPosition = Vector2.Lerp(new Vector2(0f, -1600f), new Vector2(0f, -895f), easeInOut);
            }

            if (slashLineRect != null && slashLineImage != null)
            {
                slashLineRect.sizeDelta = new Vector2(3600f, Mathf.Lerp(40f, 0f, t));
                Color c = slashFlashColor;
                c.a = 1f - t;
                slashLineImage.color = c;
            }

            if (transitionCanvasGroup != null)
            {
                transitionCanvasGroup.alpha = Mathf.Clamp01((t - 0.35f) / 0.65f);
            }

            yield return null;
        }

        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.alpha = 1f;
        }

        // Spawn seamless cross-scene fade-in overlay so the new scene opens smoothly from black
        SpawnCrossSceneFadeIn(0.45f, audioSource);

        onTransitionComplete?.Invoke();
    }

    private void SpawnCrossSceneFadeIn(float duration, AudioSource activeSfxSource)
    {
        GameObject fadeCarryObj = new GameObject("GameOverCrossSceneFadeIn");
        DontDestroyOnLoad(fadeCarryObj);

        if (activeSfxSource != null && activeSfxSource.isPlaying && activeSfxSource.gameObject != gameObject)
        {
            activeSfxSource.transform.SetParent(fadeCarryObj.transform, false);
        }

        Canvas canvas = fadeCarryObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        GameObject panelObj = new GameObject("BlackFadePanel");
        panelObj.transform.SetParent(fadeCarryObj.transform, false);
        RectTransform rt = panelObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = panelObj.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;

        CanvasGroup cg = fadeCarryObj.AddComponent<CanvasGroup>();
        cg.alpha = 1f;
        cg.blocksRaycasts = false;

        GameOverCrossSceneFadeRunner runner = fadeCarryObj.AddComponent<GameOverCrossSceneFadeRunner>();
        runner.BeginFadeIn(cg, duration, activeSfxSource);
    }

    private class GameOverCrossSceneFadeRunner : MonoBehaviour
    {
        public void BeginFadeIn(CanvasGroup cg, float duration, AudioSource carriedAudio)
        {
            StartCoroutine(FadeInRoutine(cg, duration, carriedAudio));
        }

        private IEnumerator FadeInRoutine(CanvasGroup cg, float duration, AudioSource carriedAudio)
        {
            yield return null;

            float elapsed = 0f;
            while (elapsed < duration && cg != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                cg.alpha = 1f - (t * t);
                yield return null;
            }

            if (cg != null)
            {
                cg.alpha = 0f;
            }

            while (carriedAudio != null && carriedAudio.isPlaying)
            {
                yield return null;
            }

            Destroy(gameObject);
        }
    }

    public void CleanupGameOverVisuals()
    {
        isGameOverActive = false;
        isTransitioning = false;

        RestoreGameplayAudio();

        if (blackoutObject != null)
        {
            Destroy(blackoutObject);
            blackoutObject = null;
            blackoutRenderer = null;
        }

        if (spotlightObject != null)
        {
            Destroy(spotlightObject);
            spotlightObject = null;
            spotlightRenderer = null;
        }

        if (uiCanvasGroup != null)
        {
            uiCanvasGroup.alpha = 0f;
            uiCanvasGroup.blocksRaycasts = false;
            uiCanvasGroup.interactable = false;
            if (gameOverCanvas != null)
            {
                gameOverCanvas.gameObject.SetActive(false);
            }
        }

        RestorePlayerRenderers();
    }

    public void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;

        // Avoid double-playing identical clip within the same frame
        if (clip == lastPlayedSFXClip && Time.unscaledTime - lastPlayedSFXTime < 0.2f)
        {
            return;
        }

        EnsureAudioSource();
        if (audioSource != null)
        {
            lastPlayedSFXClip = clip;
            lastPlayedSFXTime = Time.unscaledTime;
            audioSource.PlayOneShot(clip, audioVolume);
        }
    }

    public void PlayButtonClickSFX(AudioClip customClip = null)
    {
        AudioClip clipToPlay = customClip != null
            ? customClip
            : (buttonClickSFX != null ? buttonClickSFX : startTransitionSFX);

        PlaySFX(clipToPlay);
    }

    public void PlayButtonHoverSFX(AudioClip customClip = null)
    {
        AudioClip clipToPlay = customClip != null ? customClip : buttonHoverSFX;
        if (clipToPlay != null)
        {
            EnsureAudioSource();
            if (audioSource != null)
            {
                audioSource.PlayOneShot(clipToPlay, audioVolume * 0.7f);
            }
        }
    }

    public void PlayHoverSFX()
    {
        PlayButtonHoverSFX();
    }

    private void EnsureAudioSource()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.ignoreListenerPause = true;
        }
    }

    private void EnsureBlackoutOverlay()
    {
        if (blackoutObject != null && blackoutRenderer != null) return;

        Camera activeCam = Camera.main;
        if (activeCam == null) activeCam = Object.FindFirstObjectByType<Camera>();

        blackoutObject = new GameObject("GameOver_BlackoutOverlay");
        if (activeCam != null)
        {
            blackoutObject.transform.SetParent(activeCam.transform, false);
            blackoutObject.transform.localPosition = new Vector3(0f, 0f, 10f);
        }
        else
        {
            blackoutObject.transform.SetParent(transform, false);
            blackoutObject.transform.position = Vector3.zero;
        }
        blackoutObject.transform.localScale = new Vector3(500f, 500f, 1f);

        blackoutRenderer = blackoutObject.AddComponent<SpriteRenderer>();
        blackoutRenderer.sprite = GetSolidWhiteSprite();
        blackoutRenderer.color = new Color(blackoutColor.r, blackoutColor.g, blackoutColor.b, 0f);
        // SortingOrder 900 sits above world backgrounds, tilemaps, enemies, and boss (<= 10)
        blackoutRenderer.sortingOrder = 900;
    }

    private void EnsurePlayerSpotlight(Transform playerTransform)
    {
        if (spotlightObject != null && spotlightRenderer != null) return;
        if (playerTransform == null) return;

        spotlightObject = new GameObject("GameOver_PlayerSpotlight");
        spotlightObject.transform.SetParent(playerTransform, false);
        spotlightObject.transform.localPosition = Vector3.zero;
        spotlightObject.transform.localScale = new Vector3(7.5f, 7.5f, 1f);

        spotlightRenderer = spotlightObject.AddComponent<SpriteRenderer>();
        spotlightRenderer.sprite = GetRadialSpotlightSprite();
        spotlightRenderer.color = new Color(spotlightColor.r, spotlightColor.g, spotlightColor.b, 0f);
        // SortingOrder 920 sits between the blackout quad (900) and player sprite (950)
        spotlightRenderer.sortingOrder = 920;
    }

    private void CacheAndBoostPlayerRenderers(Transform playerTransform)
    {
        if (playerTransform == null) return;

        cachedPlayerSortingOrders.Clear();
        SpriteRenderer[] renderers = playerTransform.GetComponentsInChildren<SpriteRenderer>(true);

        foreach (var sr in renderers)
        {
            if (sr == null) continue;
            if (sr == blackoutRenderer || sr == spotlightRenderer) continue;

            cachedPlayerSortingOrders[sr] = sr.sortingOrder;
            sr.sortingOrder = 950;
        }
    }

    private void RestorePlayerRenderers()
    {
        foreach (var kvp in cachedPlayerSortingOrders)
        {
            if (kvp.Key != null)
            {
                kvp.Key.sortingOrder = kvp.Value;
            }
        }
        cachedPlayerSortingOrders.Clear();
    }

    private void FadeOutOtherCanvases()
    {
        fadedOtherCanvases.Clear();
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            if (c == null) continue;
            if (gameOverCanvas != null && (c == gameOverCanvas || c.transform.IsChildOf(gameOverCanvas.transform))) continue;

            CanvasGroup cg = c.GetComponent<CanvasGroup>();
            if (cg == null) cg = c.gameObject.AddComponent<CanvasGroup>();

            cg.blocksRaycasts = false;
            fadedOtherCanvases.Add(cg);
        }

        StartCoroutine(FadeOutOtherCanvasesRoutine());
    }

    private IEnumerator FadeOutOtherCanvasesRoutine()
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, blackoutFadeDuration);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(1f - (elapsed / duration));

            for (int i = 0; i < fadedOtherCanvases.Count; i++)
            {
                if (fadedOtherCanvases[i] != null)
                {
                    fadedOtherCanvases[i].alpha = alpha;
                }
            }

            yield return null;
        }

        for (int i = 0; i < fadedOtherCanvases.Count; i++)
        {
            if (fadedOtherCanvases[i] != null)
            {
                fadedOtherCanvases[i].alpha = 0f;
            }
        }
    }

    private void FadeOutGameplayAudio()
    {
        RestoreGameplayAudio();

        AudioSource[] sources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        foreach (var src in sources)
        {
            if (src == null || src == audioSource || !src.isPlaying) continue;

            // Never fade out the player's own AudioSources so SFX volume never decays
            if (PlayerController.Instance != null && (src.gameObject == PlayerController.Instance.gameObject || src.transform.IsChildOf(PlayerController.Instance.transform)))
            {
                continue;
            }

            if (!originalAudioVolumes.ContainsKey(src))
            {
                originalAudioVolumes[src] = src.volume;
            }

            Coroutine c = StartCoroutine(FadeOutAudioSource(src, blackoutFadeDuration));
            activeAudioFadeCoroutines.Add(c);
        }
    }

    private IEnumerator FadeOutAudioSource(AudioSource src, float duration)
    {
        if (src == null) yield break;
        float startVol = src.volume;
        float elapsed = 0f;
        while (elapsed < duration && src != null)
        {
            elapsed += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
            yield return null;
        }
        if (src != null)
        {
            src.volume = 0f;
            src.Pause();
        }
    }

    public void RestoreGameplayAudio()
    {
        for (int i = 0; i < activeAudioFadeCoroutines.Count; i++)
        {
            if (activeAudioFadeCoroutines[i] != null)
            {
                StopCoroutine(activeAudioFadeCoroutines[i]);
            }
        }
        activeAudioFadeCoroutines.Clear();

        foreach (var kvp in originalAudioVolumes)
        {
            if (kvp.Key != null)
            {
                kvp.Key.volume = kvp.Value;
                kvp.Key.UnPause();
            }
        }
        originalAudioVolumes.Clear();
    }

    private void EnsureGameOverUI()
    {
        if (gameOverCanvas != null)
        {
            if (uiCanvasGroup == null) uiCanvasGroup = gameOverCanvas.GetComponent<CanvasGroup>();
            if (uiCanvasGroup == null) uiCanvasGroup = gameOverCanvas.gameObject.AddComponent<CanvasGroup>();
            uiCanvasGroup.alpha = 0f;
            uiCanvasGroup.blocksRaycasts = false;
            uiCanvasGroup.interactable = false;
            BindButtons();
            CacheTransitionUIElements();
            return;
        }

        EnsureEventSystem();

        GameObject canvasGo = new GameObject("GameOverCanvas");
        canvasGo.transform.SetParent(transform, false);

        gameOverCanvas = canvasGo.AddComponent<Canvas>();
        gameOverCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        gameOverCanvas.sortingOrder = 1000;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        uiCanvasGroup = canvasGo.AddComponent<CanvasGroup>();
        uiCanvasGroup.alpha = 0f;
        uiCanvasGroup.blocksRaycasts = false;
        uiCanvasGroup.interactable = false;

        TMP_FontAsset font = customFont != null ? customFont : GetGameFont();

        // 1. Title Container
        GameObject titleGo = new GameObject("GameOverTitle");
        titleGo.transform.SetParent(canvasGo.transform, false);
        RectTransform titleRect = titleGo.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, titleYAnchor);
        titleRect.anchorMax = new Vector2(0.5f, titleYAnchor);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.sizeDelta = new Vector2(900f, 130f);
        titleRect.anchoredPosition = Vector2.zero;

        gameOverTitle = titleGo.AddComponent<TextMeshProUGUI>();
        gameOverTitle.text = titleText;
        if (font != null) gameOverTitle.font = font;
        gameOverTitle.fontSize = titleFontSize;
        gameOverTitle.fontStyle = FontStyles.Bold;
        gameOverTitle.alignment = TextAlignmentOptions.Center;
        gameOverTitle.characterSpacing = 8f;
        gameOverTitle.color = Color.white;
        gameOverTitle.raycastTarget = false;

        // 2. Subtitle
        GameObject subGo = new GameObject("GameOverSubtitle");
        subGo.transform.SetParent(canvasGo.transform, false);
        RectTransform subRect = subGo.AddComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0.5f, subtitleYAnchor);
        subRect.anchorMax = new Vector2(0.5f, subtitleYAnchor);
        subRect.pivot = new Vector2(0.5f, 0.5f);
        subRect.sizeDelta = new Vector2(800f, 40f);
        subRect.anchoredPosition = Vector2.zero;

        subtitleText = subGo.AddComponent<TextMeshProUGUI>();
        subtitleText.text = subtitleString;
        if (font != null) subtitleText.font = font;
        subtitleText.fontSize = subtitleFontSize;
        subtitleText.alignment = TextAlignmentOptions.Center;
        subtitleText.characterSpacing = 12f;
        subtitleText.color = subtitleColor;
        subtitleText.raycastTarget = false;

        // 3. Buttons Container (Anchored lower portion, leaving player visible in center)
        GameObject btnContGo = new GameObject("ButtonsContainer");
        btnContGo.transform.SetParent(canvasGo.transform, false);
        RectTransform btnContRect = btnContGo.AddComponent<RectTransform>();
        btnContRect.anchorMin = new Vector2(0.5f, buttonYAnchor);
        btnContRect.anchorMax = new Vector2(0.5f, buttonYAnchor);
        btnContRect.pivot = new Vector2(0.5f, 0.5f);
        btnContRect.sizeDelta = new Vector2(showQuitButton ? 720f : 550f, 140f);
        btnContRect.anchoredPosition = Vector2.zero;

        if (showQuitButton)
        {
            // 3 Buttons Layout: Restart (-230), Main Menu (0), Quit (+230)
            Vector2 triBtnSize = new Vector2(200f, buttonSize.y);

            restartButton = CreateStyledButton(
                btnContGo.transform,
                "RestartButton",
                new Vector2(-230f, 15f),
                triBtnSize,
                restartButtonLabel,
                restartBorderColor,
                restartInnerColor,
                font,
                RestartGame
            );

            mainMenuButton = CreateStyledButton(
                btnContGo.transform,
                "MainMenuButton",
                new Vector2(0f, 15f),
                triBtnSize,
                mainMenuButtonLabel,
                mainMenuBorderColor,
                mainMenuInnerColor,
                font,
                ReturnToMainMenu
            );

            quitButton = CreateStyledButton(
                btnContGo.transform,
                "QuitButton",
                new Vector2(230f, 15f),
                triBtnSize,
                quitButtonLabel,
                quitBorderColor,
                quitInnerColor,
                font,
                ReturnToMainMenu
            );
        }
        else
        {
            // 2 Buttons Layout: Restart (-135), Main Menu (+135)
            float halfSpacing = buttonSpacing * 0.5f;

            restartButton = CreateStyledButton(
                btnContGo.transform,
                "RestartButton",
                new Vector2(-halfSpacing, 15f),
                buttonSize,
                restartButtonLabel,
                restartBorderColor,
                restartInnerColor,
                font,
                RestartGame
            );

            mainMenuButton = CreateStyledButton(
                btnContGo.transform,
                "MainMenuButton",
                new Vector2(halfSpacing, 15f),
                buttonSize,
                mainMenuButtonLabel,
                mainMenuBorderColor,
                mainMenuInnerColor,
                font,
                ReturnToMainMenu
            );
        }

        CacheTransitionUIElements();
    }

    private void CacheTransitionUIElements()
    {
        transitionUIElements.Clear();
        transitionUIInitialPositions.Clear();

        if (gameOverTitle != null) AddTransitionElement(gameOverTitle.rectTransform);
        if (subtitleText != null) AddTransitionElement(subtitleText.rectTransform);
        if (restartButton != null) AddTransitionElement(restartButton.GetComponent<RectTransform>());
        if (mainMenuButton != null) AddTransitionElement(mainMenuButton.GetComponent<RectTransform>());
        if (quitButton != null) AddTransitionElement(quitButton.GetComponent<RectTransform>());
        if (shortcutHintText != null) AddTransitionElement(shortcutHintText.rectTransform);
    }

    private void AddTransitionElement(RectTransform rt)
    {
        if (rt == null || transitionUIElements.Contains(rt)) return;
        transitionUIElements.Add(rt);
        transitionUIInitialPositions.Add(rt.anchoredPosition);
    }

    private void EnsureTransitionUI()
    {
        Canvas rootCanvas = gameOverCanvas;
        if (rootCanvas == null) rootCanvas = GetComponentInChildren<Canvas>();
        if (rootCanvas == null) return;

        if (transitionCanvasGroup == null)
        {
            GameObject overlayObj = new GameObject("DynamicTransitionOverlay");
            overlayObj.transform.SetParent(rootCanvas.transform, false);

            RectTransform overlayRect = overlayObj.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            Image fadeImg = overlayObj.AddComponent<Image>();
            fadeImg.color = Color.black;
            fadeImg.raycastTarget = true;

            transitionCanvasGroup = overlayObj.AddComponent<CanvasGroup>();
            transitionCanvasGroup.alpha = 0f;
            transitionCanvasGroup.blocksRaycasts = false;
        }

        transitionCanvasGroup.transform.SetAsLastSibling();

        // Create diagonal slash & shutter container on the root canvas for the dynamic slash-wipe transition (-18 deg)
        Transform existingFX = rootCanvas.transform.Find("DynamicSlashWipeFX");
        if (existingFX != null)
        {
            Destroy(existingFX.gameObject);
        }

        GameObject fxContainer = new GameObject("DynamicSlashWipeFX");
        fxContainer.transform.SetParent(rootCanvas.transform, false);
        RectTransform fxRect = fxContainer.AddComponent<RectTransform>();
        fxRect.anchorMin = new Vector2(0.5f, 0.5f);
        fxRect.anchorMax = new Vector2(0.5f, 0.5f);
        fxRect.sizeDelta = new Vector2(3200f, 3200f);
        fxRect.localRotation = Quaternion.Euler(0f, 0f, -18f);

        topShutter = CreateShutterPanel(fxRect, "TopShutter", new Vector2(0f, 1600f), new Vector2(3600f, 1800f));
        bottomShutter = CreateShutterPanel(fxRect, "BottomShutter", new Vector2(0f, -1600f), new Vector2(3600f, 1800f));

        GameObject slashObj = new GameObject("SlashBeam");
        slashObj.transform.SetParent(fxRect, false);
        slashLineRect = slashObj.AddComponent<RectTransform>();
        slashLineRect.anchoredPosition = Vector2.zero;
        slashLineRect.sizeDelta = new Vector2(0f, 18f);
        slashLineImage = slashObj.AddComponent<Image>();
        slashLineImage.color = slashFlashColor;
        slashLineImage.raycastTarget = false;

        transitionCanvasGroup.transform.SetAsLastSibling();
    }

    private RectTransform CreateShutterPanel(RectTransform parent, string shutterName, Vector2 startAnchoredPos, Vector2 size)
    {
        GameObject panelObj = new GameObject(shutterName);
        panelObj.transform.SetParent(parent, false);
        RectTransform rt = panelObj.AddComponent<RectTransform>();
        rt.anchoredPosition = startAnchoredPos;
        rt.sizeDelta = size;
        Image img = panelObj.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        return rt;
    }

    private Button CreateStyledButton(
        Transform parent,
        string buttonName,
        Vector2 anchoredPos,
        Vector2 size,
        string label,
        Color borderColor,
        Color innerColor,
        TMP_FontAsset font,
        UnityEngine.Events.UnityAction onClickAction)
    {
        GameObject btnObj = new GameObject(buttonName);
        btnObj.transform.SetParent(parent, false);

        RectTransform rect = btnObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPos;

        Image img = btnObj.AddComponent<Image>();
        img.sprite = CreateButtonBoxSprite(borderColor, innerColor);
        img.type = Image.Type.Sliced;
        img.raycastTarget = true;

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;

        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = Color.white;
        colors.colorMultiplier = 1f;
        btn.colors = colors;

        if (onClickAction != null) btn.onClick.AddListener(onClickAction);

        // Button text
        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        if (font != null) tmp.font = font;
        tmp.fontSize = 24f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.characterSpacing = 4f;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        // Attach MainMenu's signature MenuButtonFX component
        MenuButtonFX btnFX = btnObj.AddComponent<MenuButtonFX>();
        btnFX.SetGameOverPanel(this);

        return btn;
    }

    private void BindButtons()
    {
        if (shortcutHintText != null)
        {
            shortcutHintText.gameObject.SetActive(false);
        }

        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(RestartGame);
            restartButton.onClick.AddListener(RestartGame);
            if (restartButton.GetComponent<MenuButtonFX>() == null)
            {
                MenuButtonFX fx = restartButton.gameObject.AddComponent<MenuButtonFX>();
                fx.SetGameOverPanel(this);
            }
        }
        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
            mainMenuButton.onClick.AddListener(ReturnToMainMenu);
            if (mainMenuButton.GetComponent<MenuButtonFX>() == null)
            {
                MenuButtonFX fx = mainMenuButton.gameObject.AddComponent<MenuButtonFX>();
                fx.SetGameOverPanel(this);
            }
        }
        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(QuitGame);
            quitButton.onClick.RemoveListener(ReturnToMainMenu);
            quitButton.onClick.AddListener(ReturnToMainMenu);
            if (quitButton.GetComponent<MenuButtonFX>() == null)
            {
                MenuButtonFX fx = quitButton.gameObject.AddComponent<MenuButtonFX>();
                fx.SetGameOverPanel(this);
            }
        }
    }

    private void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
    }

    private Sprite CreateButtonBoxSprite(Color borderColor, Color innerColor)
    {
        int width = 32;
        int height = 32;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isBorder = (x < 2 || x >= width - 2 || y < 2 || y >= height - 2);
                pixels[y * width + x] = isBorder ? borderColor : innerColor;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(3, 3, 3, 3));
    }

    private Sprite GetSolidWhiteSprite()
    {
        if (cachedSolidWhiteSprite != null) return cachedSolidWhiteSprite;

        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();

        cachedSolidWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);
        return cachedSolidWhiteSprite;
    }

    private Sprite GetRadialSpotlightSprite()
    {
        if (cachedSpotlightSprite != null) return cachedSpotlightSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxDist = (size - 1) * 0.5f;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                float alpha = Mathf.Clamp01(1f - dist);
                alpha = alpha * alpha;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        cachedSpotlightSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return cachedSpotlightSprite;
    }

    public static TMP_FontAsset GetGameFont()
    {
        if (cachedFontAsset != null) return cachedFontAsset;

#if UNITY_EDITOR
        cachedFontAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/PixelifySans-VariableFont_wght SDF.asset");
        if (cachedFontAsset != null) return cachedFontAsset;
#endif

        TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        foreach (var f in fonts)
        {
            if (f != null && f.name.Contains("PixelifySans"))
            {
                cachedFontAsset = f;
                return cachedFontAsset;
            }
        }

        cachedFontAsset = TMP_Settings.defaultFontAsset;
        return cachedFontAsset;
    }
}

