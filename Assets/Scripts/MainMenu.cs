using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenu : MonoBehaviour
{
    #region Constants

    private const string DefaultFallbackSceneName = "LevelScene";
    private const string DynamicTransitionOverlayName = "DynamicTransitionOverlay";
    private const string DynamicSlashWipeFXName = "DynamicSlashWipeFX";
    private const string CrossfadeOverlayName = "BGCrossfadeOverlay";
    private const string AmbientVfxLayerName = "MenuAmbientFXLayer";
    private const float DuplicateSfxCooldown = 0.2f;
    private const float UiPixelGridStep = 4f;

    private struct AmbientPixelParticle
    {
        public RectTransform Rect;
        public Image Image;
        public Vector2 Position;
        public Vector2 Velocity;
        public float BaseAlpha;
        public float Phase;
        public float SwayAmp;
        public float SwaySpeed;
        public bool IsStreak;
    }

    private struct MenuLeafParticle
    {
        public RectTransform Rect;
        public Image Image;
        public Vector2 Position;
        public Vector2 ExtraVelocity;
        public float Speed;
        public float BaseAlpha;
        public float Phase;
        public float SwayAmp;
        public float SwaySpeed;
        public float AnimTimer;
        public float AnimRate;
        public int FrameIndex;
        public bool IsNearLayer;
    }

#if UNITY_EDITOR
    private static readonly string[] EditorCandidateSlidePaths =
    {
        "Assets/Textures/BG.png",
        "Assets/Textures/MapBG1.png",
        "Assets/Textures/MAP2_2.png",
        "Assets/FreeAssets/RockyWorld/background1.png",
        "Assets/FreeAssets/RockyWorld/background2.png",
        "Assets/FreeAssets/RockyWorld/background3.png"
    };

    private static readonly string[] EditorLeafSpritePaths =
    {
        "Assets/Textures/SFXs/Leaf1.png",
        "Assets/Textures/SFXs/Leaf2.png",
        "Assets/Textures/SFXs/Leaf3.png",
        "Assets/Textures/SFXs/Leaf4.png"
    };
#endif

    #endregion

    #region Serialized Fields

    [Header("Scene Transition Settings")]
    [SerializeField] private string gameSceneName = "Stage0";
    [SerializeField] private CanvasGroup transitionCanvasGroup; // Optional fullscreen panel with CanvasGroup
    [SerializeField] private float fadeDuration = 0.65f;
    [SerializeField] private float zoomScaleOnEnter = 1.15f;
    [SerializeField] private Color slashFlashColor = Color.white;
    [SerializeField] private AudioClip startTransitionSFX;
    [SerializeField] private AudioClip startPortalSFX;

    [Header("Background Mouse Parallax")]
    [SerializeField] private RectTransform parallaxBackground;
    [SerializeField] private float parallaxAmount = 20f;
    [SerializeField] private float parallaxSmoothSpeed = 5f;

    [Header("Dynamic Visual Polish & Depth")]
    [SerializeField] private float characterExtraParallax = 16f;
    [SerializeField] private float uiCounterParallax = 7f;
    [SerializeField] private float idleDriftScaleAmount = 0.032f;
    [SerializeField] private float idleDriftSpeed = 0.36f;
    [SerializeField] private bool enableAmbientParticles = true;
    [SerializeField] private int ambientParticleCount = 30;
    [SerializeField] private bool enableMenuLeaves = true;
    [SerializeField] private int menuLeafCount = 14;
    [SerializeField] private Vector2 menuWindDirection = new Vector2(1f, -0.25f);
    [SerializeField] private List<Sprite> menuLeafSprites = new List<Sprite>();
    [SerializeField] private bool enableCharacterAliveFX = true;
    [SerializeField] private bool enableTitleSlashFX = true;

    [Header("Background Slideshow Settings")]
    [Tooltip("List of background sprites to cycle through in the slideshow.")]
    [SerializeField] private List<Sprite> backgroundSlideshow = new List<Sprite>();
    [Tooltip("Display duration for each background slide in seconds.")]
    [SerializeField] private float slideDuration = 6.0f;
    [Tooltip("Crossfade blend transition time between background slides in seconds.")]
    [SerializeField] private float crossfadeDuration = 1.2f;
    [Tooltip("Whether the slideshow automatically cycles through the backgrounds.")]
    [SerializeField] private bool autoCycleSlideshow = true;
    [Tooltip("Whether to randomize the order of backgrounds instead of cycling sequentially.")]
    [SerializeField] private bool randomizeOrder = false;

    [Header("Audio Settings")]
    [SerializeField] private AudioSource bgmAudioSource;
    [SerializeField] private AudioSource sfxAudioSource;
    [SerializeField] private AudioClip buttonClickSFX;
    [SerializeField] private AudioClip buttonHoverSFX;
    [SerializeField] [Range(0f, 1f)] private float sfxVolume = 1f;

    #endregion

    #region Private State

    private bool isTransitioning = false;
    private bool isIntroComplete = false;
    private float lastPlayedSFXTime = -10f;
    private AudioClip lastPlayedSFXClip;
    private Vector3 initialBackgroundPos;
    private Vector3 initialBackgroundScale = Vector3.one;
    private Vector2 smoothedMouseNorm;
    private float menuAmbientClock;

    // Slideshow state
    private Image mainBgImage;
    private Image crossfadeBgImage;
    private int currentSlideIndex = 0;
    private Coroutine slideshowCoroutine;
    private Coroutine crossfadeCoroutine;
    private bool isCrossfading = false;
    private WaitForSecondsRealtime cachedSlideWait;
    private float cachedSlideWaitDuration = -1f;

    // Transition UI state
    private readonly List<RectTransform> menuUIElements = new List<RectTransform>();
    private readonly List<Vector2> menuUIInitialPositions = new List<Vector2>();
    private readonly List<CanvasGroup> menuUICanvasGroups = new List<CanvasGroup>();
    private readonly List<MenuButtonFX> menuButtonFXList = new List<MenuButtonFX>();

    // Procedural dynamic visual layers (static sprite caches prevent texture leaks across menu reloads)
    private static Sprite cachedTitleSlashSprite;
    private RectTransform characterRect;
    private Vector2 characterBasePos;
    private Vector3 characterBaseScale = Vector3.one;
    private float startHoverEchoBoost = 0f;

    private RectTransform titleRootRect;
    private RectTransform titleForegroundRect;
    private Vector2 titleForegroundBasePos;
    private RectTransform titleSlashStreakRect;
    private Image titleSlashStreakImg;
    private RectTransform dividerDiamondRect;
    private Image dividerBladeImg;
    private float titleSlashTimer = 1.9f;
    private float titleSlashAnimTime = -1f;

    private RectTransform ambientLayerRect;
    private readonly List<AmbientPixelParticle> ambientParticles = new List<AmbientPixelParticle>();
    private readonly List<MenuLeafParticle> menuLeaves = new List<MenuLeafParticle>();
    private float menuGustCountdown = 4.2f;
    private float menuActiveGustTimer = -1f;
    private Vector2 lastMouseCanvasPos;
    private bool hasMouseCanvasPos;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        EnsureAudioSources();
#if UNITY_EDITOR
        if (startTransitionSFX == null)
        {
            startTransitionSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SwordSlashSFX.mp3");
        }
        if (startPortalSFX == null)
        {
            startPortalSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/EnteringPortalSFX.mp3");
        }
        if (buttonHoverSFX == null)
        {
            buttonHoverSFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/TargetSelectSFX.mp3");
        }
        if (menuLeafSprites == null)
        {
            menuLeafSprites = new List<Sprite>();
        }
        if (menuLeafSprites.Count == 0)
        {
            for (int i = 0; i < EditorLeafSpritePaths.Length; i++)
            {
                Sprite leafSp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(EditorLeafSpritePaths[i]);
                if (leafSp != null) menuLeafSprites.Add(leafSp);
            }
        }
#endif
        EnsureTransitionUI();
        CacheMenuUIElements();
    }

    private void Start()
    {
        if (parallaxBackground != null)
        {
            initialBackgroundPos = parallaxBackground.anchoredPosition;
            initialBackgroundScale = parallaxBackground.localScale;
            SetupBackgroundSlideshow();
        }

        SetupVisualPolishLayers();

        // Auto-fade in from black on start
        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.alpha = 1f;
            transitionCanvasGroup.blocksRaycasts = true;
            isIntroComplete = false;
            StartCoroutine(MenuIntroRoutine());
        }
        else
        {
            isIntroComplete = true;
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        menuAmbientClock += dt;

        UpdateAmbientParticles(dt);
        UpdateMenuLeaves(dt);

        if (!isTransitioning)
        {
            HandleParallaxAndIdleDrift(dt);
            UpdateCharacterAliveFX(dt);
            UpdateTitleSlashFX(dt);
        }
    }

    public void NotifyStartButtonHovered()
    {
        startHoverEchoBoost = 1f;
        if (titleSlashAnimTime < 0f)
        {
            titleSlashAnimTime = 0f;
        }
    }

    #endregion

    #region Background & Multi-Plane Parallax

    private void HandleParallaxAndIdleDrift(float dt)
    {
        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        float normalizedX = Mathf.Clamp((mousePos.x / Mathf.Max(1f, Screen.width) - 0.5f) * 2f, -1f, 1f);
        float normalizedY = Mathf.Clamp((mousePos.y / Mathf.Max(1f, Screen.height) - 0.5f) * 2f, -1f, 1f);

        smoothedMouseNorm = Vector2.Lerp(
            smoothedMouseNorm,
            new Vector2(normalizedX, normalizedY),
            dt * parallaxSmoothSpeed
        );

        Vector2 idleDriftPan = new Vector2(
            Mathf.Sin(menuAmbientClock * idleDriftSpeed) * 4.5f,
            Mathf.Cos(menuAmbientClock * idleDriftSpeed * 0.77f) * 2.5f
        );

        if (parallaxBackground != null)
        {
            Vector3 targetPos = new Vector3(
                initialBackgroundPos.x + smoothedMouseNorm.x * parallaxAmount + idleDriftPan.x,
                initialBackgroundPos.y + smoothedMouseNorm.y * parallaxAmount + idleDriftPan.y,
                initialBackgroundPos.z
            );
            parallaxBackground.anchoredPosition = targetPos;

            if (isIntroComplete && idleDriftScaleAmount > 0f)
            {
                float breath = 0.5f + 0.5f * Mathf.Sin(menuAmbientClock * idleDriftSpeed);
                parallaxBackground.localScale = initialBackgroundScale * (1f + breath * idleDriftScaleAmount);
            }
        }

        if (isIntroComplete)
        {
            Vector2 uiShift = new Vector2(-smoothedMouseNorm.x * uiCounterParallax, -smoothedMouseNorm.y * (uiCounterParallax * 0.5f));
            for (int i = 0; i < menuUIElements.Count; i++)
            {
                if (menuUIElements[i] == null) continue;
                Vector2 btnOffset = (i < menuButtonFXList.Count && menuButtonFXList[i] != null)
                    ? menuButtonFXList[i].HoverAnchoredOffset
                    : Vector2.zero;
                menuUIElements[i].anchoredPosition = menuUIInitialPositions[i] + uiShift + btnOffset;
            }
        }
    }

    #endregion

    #region Background Slideshow System

    private void SetupBackgroundSlideshow()
    {
        if (parallaxBackground == null) return;

        if (!parallaxBackground.TryGetComponent(out mainBgImage))
        {
            mainBgImage = parallaxBackground.GetComponentInChildren<Image>();
        }

#if UNITY_EDITOR
        if (backgroundSlideshow == null)
        {
            backgroundSlideshow = new List<Sprite>();
        }

        if (backgroundSlideshow.Count == 0)
        {
            if (mainBgImage != null && mainBgImage.sprite != null)
            {
                backgroundSlideshow.Add(mainBgImage.sprite);
            }

            for (int i = 0; i < EditorCandidateSlidePaths.Length; i++)
            {
                Sprite sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(EditorCandidateSlidePaths[i]);
                if (sp != null && !backgroundSlideshow.Contains(sp))
                {
                    backgroundSlideshow.Add(sp);
                }
            }
        }
#endif

        if (mainBgImage != null && backgroundSlideshow.Count > 0 && mainBgImage.sprite == null)
        {
            mainBgImage.sprite = backgroundSlideshow[0];
        }

        EnsureCrossfadeOverlay();

        if (autoCycleSlideshow && backgroundSlideshow.Count > 1)
        {
            if (slideshowCoroutine != null) StopCoroutine(slideshowCoroutine);
            slideshowCoroutine = StartCoroutine(SlideshowRoutine());
        }
    }

    private void EnsureCrossfadeOverlay()
    {
        if (parallaxBackground == null) return;

        Transform existing = parallaxBackground.Find(CrossfadeOverlayName);
        if (existing != null)
        {
            crossfadeBgImage = existing.GetComponent<Image>();
        }

        if (crossfadeBgImage == null)
        {
            GameObject overlayObj = new GameObject(CrossfadeOverlayName);
            overlayObj.transform.SetParent(parallaxBackground, false);

            RectTransform rt = overlayObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            crossfadeBgImage = overlayObj.AddComponent<Image>();
            crossfadeBgImage.raycastTarget = false;
            crossfadeBgImage.color = new Color(1f, 1f, 1f, 0f);

            // Keep overlay behind any foreground character/props children
            overlayObj.transform.SetSiblingIndex(0);
        }
        else
        {
            crossfadeBgImage.color = new Color(1f, 1f, 1f, 0f);
        }
    }

    private IEnumerator SlideshowRoutine()
    {
        while (autoCycleSlideshow && backgroundSlideshow.Count > 1)
        {
            if (cachedSlideWait == null || !Mathf.Approximately(cachedSlideWaitDuration, slideDuration))
            {
                cachedSlideWaitDuration = slideDuration;
                cachedSlideWait = new WaitForSecondsRealtime(slideDuration);
            }

            yield return cachedSlideWait;

            if (isTransitioning) yield break;

            int count = backgroundSlideshow.Count;
            int nextIndex;
            if (randomizeOrder)
            {
                nextIndex = UnityEngine.Random.Range(0, count);
                if (nextIndex == currentSlideIndex && count > 1)
                {
                    nextIndex = (nextIndex + 1) % count;
                }
            }
            else
            {
                nextIndex = (currentSlideIndex + 1) % count;
            }

            currentSlideIndex = nextIndex;
            Sprite nextSprite = backgroundSlideshow[currentSlideIndex];

            if (crossfadeCoroutine != null) StopCoroutine(crossfadeCoroutine);
            yield return StartCoroutine(CrossfadeToSpriteRoutine(nextSprite));
        }
    }

    private IEnumerator CrossfadeToSpriteRoutine(Sprite newSprite)
    {
        if (newSprite == null || mainBgImage == null || crossfadeBgImage == null) yield break;
        if (mainBgImage.sprite == newSprite) yield break;

        isCrossfading = true;

        crossfadeBgImage.sprite = newSprite;
        crossfadeBgImage.color = new Color(1f, 1f, 1f, 0f);
        crossfadeBgImage.gameObject.SetActive(true);

        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, crossfadeDuration);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            crossfadeBgImage.color = new Color(1f, 1f, 1f, smoothT);
            yield return null;
        }

        mainBgImage.sprite = newSprite;
        crossfadeBgImage.color = new Color(1f, 1f, 1f, 0f);
        isCrossfading = false;
    }

    /// <summary>
    /// Manually switches to the next background slide in the list.
    /// </summary>
    public void NextSlide()
    {
        if (backgroundSlideshow.Count <= 1 || isCrossfading) return;
        TransitionToSlide((currentSlideIndex + 1) % backgroundSlideshow.Count);
    }

    /// <summary>
    /// Manually switches to the previous background slide in the list.
    /// </summary>
    public void PreviousSlide()
    {
        if (backgroundSlideshow.Count <= 1 || isCrossfading) return;
        TransitionToSlide((currentSlideIndex - 1 + backgroundSlideshow.Count) % backgroundSlideshow.Count);
    }

    /// <summary>
    /// Directly transitions to a specific background slide by index.
    /// </summary>
    public void SetSlide(int index)
    {
        if (backgroundSlideshow.Count == 0 || index < 0 || index >= backgroundSlideshow.Count || isCrossfading) return;
        TransitionToSlide(index);
    }

    private void TransitionToSlide(int index)
    {
        currentSlideIndex = index;
        if (crossfadeCoroutine != null) StopCoroutine(crossfadeCoroutine);
        crossfadeCoroutine = StartCoroutine(CrossfadeToSpriteRoutine(backgroundSlideshow[currentSlideIndex]));
    }

    #endregion

    #region Menu Actions & Transitions

    public void StartGame()
    {
        if (isTransitioning) return;
        StartCoroutine(TransitionAndLoadScene());
    }

    public void QuitGame()
    {
        if (isTransitioning) return;
        StartCoroutine(TransitionAndQuit());
    }

    private IEnumerator MenuIntroRoutine()
    {
        Vector2 startOffset = new Vector2(0f, -35f);

        // Stagger menu UI elements slightly on intro while fading in from black
        for (int i = 0; i < menuUIElements.Count; i++)
        {
            menuUIElements[i].anchoredPosition = menuUIInitialPositions[i] + startOffset;
            menuUICanvasGroups[i].alpha = 0f;
        }

        Vector3 introStartScale = initialBackgroundScale * 1.08f;
        if (parallaxBackground != null)
        {
            parallaxBackground.localScale = introStartScale;
        }

        float duration = Mathf.Max(0.35f, fadeDuration);
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / duration);
            float invT = 1f - t;
            float easeOut = 1f - (invT * invT * invT);

            if (transitionCanvasGroup != null)
            {
                transitionCanvasGroup.alpha = 1f - easeOut;
            }

            if (parallaxBackground != null)
            {
                parallaxBackground.localScale = Vector3.Lerp(introStartScale, initialBackgroundScale, easeOut);
            }

            for (int i = 0; i < menuUIElements.Count; i++)
            {
                float delay = i * 0.06f;
                float itemT = Mathf.Clamp01((timer - delay) / Mathf.Max(0.15f, duration - delay));
                float invItemT = 1f - itemT;
                float itemEase = 1f - (invItemT * invItemT * invItemT);
                menuUIElements[i].anchoredPosition = Vector2.Lerp(menuUIInitialPositions[i] + startOffset, menuUIInitialPositions[i], itemEase);
                menuUICanvasGroups[i].alpha = itemEase;
            }

            yield return null;
        }

        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.alpha = 0f;
            transitionCanvasGroup.blocksRaycasts = false;
        }

        if (parallaxBackground != null)
        {
            parallaxBackground.localScale = initialBackgroundScale;
        }

        Vector2 finalUiShift = new Vector2(-smoothedMouseNorm.x * uiCounterParallax, -smoothedMouseNorm.y * (uiCounterParallax * 0.5f));
        for (int i = 0; i < menuUIElements.Count; i++)
        {
            Vector2 btnOffset = (i < menuButtonFXList.Count && menuButtonFXList[i] != null)
                ? menuButtonFXList[i].HoverAnchoredOffset
                : Vector2.zero;
            menuUIElements[i].anchoredPosition = menuUIInitialPositions[i] + finalUiShift + btnOffset;
            menuUICanvasGroups[i].alpha = 1f;
        }

        isIntroComplete = true;
    }

    private IEnumerator TransitionAndLoadScene()
    {
        isTransitioning = true;

        for (int i = 0; i < menuButtonFXList.Count; i++)
        {
            if (menuButtonFXList[i] != null)
            {
                menuButtonFXList[i].SetTransitionSuppressed(true);
            }
        }

        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.blocksRaycasts = true;
            transitionCanvasGroup.alpha = 0f;
        }

        Vector3 startBgPos = parallaxBackground != null ? (Vector3)parallaxBackground.anchoredPosition : Vector3.zero;
        Vector3 startBgScale = parallaxBackground != null ? parallaxBackground.localScale : Vector3.one;
        Vector3 targetZoomScale = initialBackgroundScale * zoomScaleOnEnter;
        float startBgmVol = bgmAudioSource != null ? bgmAudioSource.volume : 1f;

        Vector2[] startUiPositions = new Vector2[menuUIElements.Count];
        for (int i = 0; i < menuUIElements.Count; i++)
        {
            startUiPositions[i] = menuUIElements[i] != null ? menuUIElements[i].anchoredPosition : Vector2.zero;
        }

        // Disperse menu UI buttons/title cleanly and punch-zoom the backdrop before slicing the frame
        float disperseDuration = Mathf.Max(0.14f, fadeDuration * 0.32f);
        float timer = 0f;

        while (timer < disperseDuration)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / disperseDuration);
            float easeIn = t * t;
            float invT = 1f - t;
            float easeOut = 1f - (invT * invT * invT);

            if (parallaxBackground != null)
            {
                parallaxBackground.anchoredPosition = Vector3.Lerp(startBgPos, initialBackgroundPos, easeOut);
                parallaxBackground.localScale = Vector3.Lerp(startBgScale, targetZoomScale, easeIn);
            }

            for (int i = 0; i < menuUIElements.Count; i++)
            {
                Vector2 dir = (i % 2 == 0) ? new Vector2(140f, 0f) : new Vector2(-140f, 0f);
                menuUIElements[i].anchoredPosition = Vector2.Lerp(startUiPositions[i], menuUIInitialPositions[i] + dir, easeIn);
                menuUICanvasGroups[i].alpha = 1f - easeOut;
            }

            if (bgmAudioSource != null)
            {
                bgmAudioSource.volume = Mathf.Lerp(startBgmVol, 0f, t);
            }

            yield return null;
        }

        for (int i = 0; i < menuUICanvasGroups.Count; i++)
        {
            menuUICanvasGroups[i].alpha = 0f;
        }

        if (bgmAudioSource != null)
        {
            bgmAudioSource.volume = 0f;
            bgmAudioSource.Stop();
        }

        // Wait one frame so UI dispersal is clean before capturing the menu frame
        yield return null;

        Camera mainCam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) rootCanvas = FindFirstObjectByType<Canvas>();

        PixelScreenSliceRunner.StartTransition(new PixelScreenSliceRunner.Config
        {
            CaptureCamera = mainCam,
            OptionalOverlayCanvasToCapture = rootCanvas,
            CutOriginViewport = new Vector2(0.5f, 0.5f),
            CutDirection = new Vector2(1f, 0.34f).normalized,
            VirtualPixelHeight = 180,
            KatanaCutDuration = 0.09f,
            ScreenSplitDuration = 0.30f,
            BlackHoldDuration = 0.06f,
            BlackFadeOutDuration = 0.24f,
            SlashSFX = startTransitionSFX,
            PortalSplitSFX = startPortalSFX,
            SFXVolume = sfxVolume,
            OutputMixerGroup = sfxAudioSource != null ? sfxAudioSource.outputAudioMixerGroup : null,
            LoadTargetSceneAction = LoadTargetGameScene
        });
    }

    private void LoadTargetGameScene()
    {
        Time.timeScale = 1f;
        ChainEnemy.isExploding = false;

        if (!string.IsNullOrWhiteSpace(gameSceneName) && Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            SceneManager.LoadScene(gameSceneName);
            return;
        }

        if (Application.CanStreamedLevelBeLoaded(DefaultFallbackSceneName))
        {
            SceneManager.LoadScene(DefaultFallbackSceneName);
            return;
        }

        // Fallback to index 0 in Build Settings
        SceneManager.LoadScene(0);
    }

    private IEnumerator TransitionAndQuit()
    {
        isTransitioning = true;
        PlayButtonClickSFX();

        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.blocksRaycasts = true;
            yield return FadeRoutine(0f, 1f, null);
        }

        Debug.Log("Exiting Game...");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private IEnumerator FadeRoutine(float startAlpha, float targetAlpha, System.Action onComplete)
    {
        float timer = 0f;
        float duration = Mathf.Max(0.01f, fadeDuration);
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            float t = timer / duration;
            if (transitionCanvasGroup != null)
            {
                transitionCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            }

            if (bgmAudioSource != null && Mathf.Approximately(targetAlpha, 1f))
            {
                bgmAudioSource.volume = Mathf.Lerp(1f, 0f, t);
            }

            yield return null;
        }

        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.alpha = targetAlpha;
        }

        onComplete?.Invoke();
    }

    #endregion

    #region UI & Audio Logic

    public void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;

        // Avoid double-playing the exact same clip on pointer-down + button-click release within the same press
        if (clip == lastPlayedSFXClip && Time.unscaledTime - lastPlayedSFXTime < DuplicateSfxCooldown)
        {
            return;
        }

        EnsureAudioSources();
        if (sfxAudioSource != null)
        {
            lastPlayedSFXClip = clip;
            lastPlayedSFXTime = Time.unscaledTime;
            sfxAudioSource.pitch = 1f;
            sfxAudioSource.PlayOneShot(clip, sfxVolume);
        }
    }

    public void PlayButtonClickSFX(AudioClip customClip = null)
    {
        AudioClip clipToPlay = customClip != null ? customClip : buttonClickSFX;
        PlaySFX(clipToPlay);
    }

    public void PlayButtonHoverSFX(AudioClip customClip = null)
    {
        AudioClip clipToPlay = customClip != null ? customClip : buttonHoverSFX;
        if (clipToPlay != null)
        {
            EnsureAudioSources();
            if (sfxAudioSource != null)
            {
                sfxAudioSource.pitch = UnityEngine.Random.Range(0.96f, 1.05f);
                sfxAudioSource.PlayOneShot(clipToPlay, sfxVolume * 0.35f);
            }
        }
    }

    private void EnsureAudioSources()
    {
        if (sfxAudioSource == null)
        {
            AudioSource existingSource = GetComponent<AudioSource>();
            if (existingSource != null && existingSource != bgmAudioSource)
            {
                sfxAudioSource = existingSource;
            }
            else
            {
                GameObject sfxObj = new GameObject("MenuSFXAudioSource");
                sfxObj.transform.SetParent(transform, false);
                sfxAudioSource = sfxObj.AddComponent<AudioSource>();
                sfxObj.hideFlags = HideFlags.None;
                sfxAudioSource.playOnAwake = false;
                sfxAudioSource.spatialBlend = 0f;
            }
        }
    }

    private void EnsureTransitionUI()
    {
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) rootCanvas = FindFirstObjectByType<Canvas>();
        if (rootCanvas == null) return;

        if (transitionCanvasGroup == null)
        {
            GameObject overlayObj = new GameObject(DynamicTransitionOverlayName);
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
            transitionCanvasGroup.alpha = 1f;
        }

        transitionCanvasGroup.transform.SetAsLastSibling();
    }

    private void CacheMenuUIElements()
    {
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) return;

        menuUIElements.Clear();
        menuUIInitialPositions.Clear();
        menuUICanvasGroups.Clear();
        menuButtonFXList.Clear();

        Transform rootTransform = rootCanvas.transform;
        int childCount = rootTransform.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = rootTransform.GetChild(i);
            if (child == null) continue;
            if (parallaxBackground != null && child == parallaxBackground.transform) continue;
            if (transitionCanvasGroup != null && child == transitionCanvasGroup.transform) continue;
            if (child.name == DynamicSlashWipeFXName
                || child.name == DynamicTransitionOverlayName
                || child.name == AmbientVfxLayerName)
            {
                continue;
            }

            if (!(child is RectTransform rt)) continue;

            if (!rt.TryGetComponent(out CanvasGroup cg))
            {
                cg = rt.gameObject.AddComponent<CanvasGroup>();
            }

            menuUIElements.Add(rt);
            menuUIInitialPositions.Add(rt.anchoredPosition);
            menuUICanvasGroups.Add(cg);
            menuButtonFXList.Add(rt.GetComponent<MenuButtonFX>());
        }
    }

    #endregion

    #region Visual Polish & Dynamic Atmosphere

    private void SetupVisualPolishLayers()
    {
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) rootCanvas = FindFirstObjectByType<Canvas>();
        if (rootCanvas == null) return;

        if (enableAmbientParticles)
        {
            SetupAmbientParticleLayer(rootCanvas.transform);
        }

        if (enableMenuLeaves)
        {
            SetupMenuLeafWind(rootCanvas.transform);
        }

        if (enableCharacterAliveFX)
        {
            SetupCharacterAliveFX(rootCanvas.transform);
        }

        if (enableTitleSlashFX)
        {
            SetupTitleVisualFX();
        }
    }

    private void SetupAmbientParticleLayer(Transform canvasRoot)
    {
        if (canvasRoot.Find(AmbientVfxLayerName) != null) return;

        GameObject layerObj = new GameObject(AmbientVfxLayerName);
        layerObj.transform.SetParent(canvasRoot, false);

        int insertIndex = parallaxBackground != null ? parallaxBackground.GetSiblingIndex() + 1 : 1;
        layerObj.transform.SetSiblingIndex(Mathf.Min(canvasRoot.childCount - 1, insertIndex));

        ambientLayerRect = CreateFullStretchRect(layerObj);
        Sprite pixel = GetWhitePixelSprite();

        ambientParticles.Clear();
        int count = Mathf.Clamp(ambientParticleCount, 8, 60);
        for (int i = 0; i < count; i++)
        {
            bool isStreak = (i % 6 == 0);
            GameObject pObj = new GameObject(isStreak ? "MenuWindStreak" : "MenuPixelEmber");
            pObj.transform.SetParent(ambientLayerRect, false);

            RectTransform rt = pObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);

            float size = isStreak ? 4f : ((i % 3 == 0) ? 8f : 4f);
            float width = isStreak ? (24f + (i % 3) * 12f) : size;
            rt.sizeDelta = new Vector2(width, size);

            Vector2 startPos = SnapToUiPixelGrid(new Vector2(
                UnityEngine.Random.Range(-1020f, 1020f),
                UnityEngine.Random.Range(-580f, 580f)
            ));
            rt.anchoredPosition = startPos;

            Image img = pObj.AddComponent<Image>();
            img.sprite = pixel;
            img.raycastTarget = false;

            float baseAlpha = isStreak
                ? UnityEngine.Random.Range(0.14f, 0.26f)
                : UnityEngine.Random.Range(0.24f, 0.65f);
            float tint = UnityEngine.Random.Range(0.82f, 1f);
            img.color = new Color(tint, tint, tint, baseAlpha);

            Vector2 velocity = isStreak
                ? new Vector2(UnityEngine.Random.Range(210f, 340f), UnityEngine.Random.Range(12f, 34f))
                : new Vector2(UnityEngine.Random.Range(14f, 42f), UnityEngine.Random.Range(22f, 56f));

            ambientParticles.Add(new AmbientPixelParticle
            {
                Rect = rt,
                Image = img,
                Position = startPos,
                Velocity = velocity,
                BaseAlpha = baseAlpha,
                Phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                SwayAmp = isStreak ? 4f : UnityEngine.Random.Range(8f, 20f),
                SwaySpeed = isStreak ? 1.5f : UnityEngine.Random.Range(0.9f, 2.1f),
                IsStreak = isStreak
            });
        }
    }

    private void UpdateAmbientParticles(float dt)
    {
        if (!enableAmbientParticles || ambientParticles.Count == 0) return;

        const float boundX = 1040f;
        const float boundY = 600f;

        for (int i = 0; i < ambientParticles.Count; i++)
        {
            AmbientPixelParticle p = ambientParticles[i];
            if (p.Rect == null || p.Image == null) continue;

            p.Position += p.Velocity * dt;
            if (p.Position.x > boundX) p.Position.x = -boundX;
            if (p.Position.y > boundY) p.Position.y = -boundY;

            float flutter = Mathf.Sin(menuAmbientClock * p.SwaySpeed + p.Phase) * p.SwayAmp;
            Vector2 drawPos = new Vector2(
                p.Position.x + (p.IsStreak ? 0f : flutter),
                p.Position.y + (p.IsStreak ? flutter : 0f)
            );
            p.Rect.anchoredPosition = SnapToUiPixelGrid(drawPos);

            float edgeFadeX = Mathf.Clamp01((boundX - Mathf.Abs(p.Position.x)) / 140f);
            float edgeFadeY = Mathf.Clamp01((boundY - Mathf.Abs(p.Position.y)) / 90f);
            float pulse = p.IsStreak ? 1f : (0.6f + 0.4f * EnemyBase.QuantizeStep(0.5f + 0.5f * Mathf.Sin(menuAmbientClock * 2.4f + p.Phase), 4));

            Color c = p.Image.color;
            c.a = p.BaseAlpha * edgeFadeX * edgeFadeY * pulse;
            p.Image.color = c;

            ambientParticles[i] = p;
        }
    }

    private void SetupMenuLeafWind(Transform canvasRoot)
    {
        if (menuLeafSprites == null || menuLeafSprites.Count == 0) return;

        if (ambientLayerRect == null)
        {
            Transform existing = canvasRoot.Find(AmbientVfxLayerName);
            if (existing != null)
            {
                ambientLayerRect = existing as RectTransform;
            }
            else
            {
                GameObject layerObj = new GameObject(AmbientVfxLayerName);
                layerObj.transform.SetParent(canvasRoot, false);
                int insertIndex = parallaxBackground != null ? parallaxBackground.GetSiblingIndex() + 1 : 1;
                layerObj.transform.SetSiblingIndex(Mathf.Min(canvasRoot.childCount - 1, insertIndex));
                ambientLayerRect = CreateFullStretchRect(layerObj);
            }
        }

        // Far leaf container placed inside BG behind Character so distant leaves drift behind the ninja
        RectTransform farParent = ambientLayerRect;
        if (parallaxBackground != null)
        {
            Transform farExisting = parallaxBackground.Find("MenuFarLeafLayer");
            if (farExisting != null)
            {
                farParent = farExisting as RectTransform;
            }
            else
            {
                GameObject farObj = new GameObject("MenuFarLeafLayer");
                farObj.transform.SetParent(parallaxBackground, false);
                Transform charTrans = parallaxBackground.Find("Character");
                farObj.transform.SetSiblingIndex(charTrans != null ? charTrans.GetSiblingIndex() : 1);
                farParent = CreateFullStretchRect(farObj);
            }
        }

        menuLeaves.Clear();
        int count = Mathf.Clamp(menuLeafCount, 2, 40);
        int nearCount = Mathf.RoundToInt(count * 0.55f);

        for (int i = 0; i < count; i++)
        {
            bool isNear = i < nearCount;
            RectTransform targetParent = isNear ? ambientLayerRect : farParent;

            GameObject leafObj = new GameObject(isNear ? $"MenuWindLeaf_Near_{i}" : $"MenuWindLeaf_Far_{i}");
            leafObj.transform.SetParent(targetParent, false);

            RectTransform rt = leafObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);

            float sizeW = isNear ? UnityEngine.Random.Range(38f, 52f) : UnityEngine.Random.Range(24f, 34f);
            rt.sizeDelta = new Vector2(sizeW, sizeW * 0.45f);

            Vector2 startPos = SnapToUiPixelGrid(new Vector2(
                UnityEngine.Random.Range(-1020f, 1020f),
                UnityEngine.Random.Range(-560f, 560f)
            ));
            rt.anchoredPosition = startPos;

            Image img = leafObj.AddComponent<Image>();
            int startFrame = UnityEngine.Random.Range(0, menuLeafSprites.Count);
            img.sprite = menuLeafSprites[startFrame];
            img.preserveAspect = true;
            img.raycastTarget = false;

            float baseAlpha = isNear ? UnityEngine.Random.Range(0.84f, 0.96f) : UnityEngine.Random.Range(0.45f, 0.62f);
            float tint = isNear ? UnityEngine.Random.Range(0.92f, 1f) : UnityEngine.Random.Range(0.76f, 0.88f);
            img.color = new Color(tint, Mathf.Clamp01(tint + 0.05f), tint, baseAlpha);

            menuLeaves.Add(new MenuLeafParticle
            {
                Rect = rt,
                Image = img,
                Position = startPos,
                ExtraVelocity = Vector2.zero,
                Speed = (isNear ? UnityEngine.Random.Range(155f, 225f) : UnityEngine.Random.Range(85f, 130f)),
                BaseAlpha = baseAlpha,
                Phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                SwayAmp = isNear ? UnityEngine.Random.Range(18f, 36f) : UnityEngine.Random.Range(10f, 22f),
                SwaySpeed = UnityEngine.Random.Range(1.8f, 3.1f),
                AnimTimer = UnityEngine.Random.value * 0.08f,
                AnimRate = UnityEngine.Random.Range(9f, 14f),
                FrameIndex = startFrame,
                IsNearLayer = isNear
            });
        }
    }

    private void UpdateMenuLeaves(float dt)
    {
        if (!enableMenuLeaves || menuLeaves.Count == 0 || menuLeafSprites == null || menuLeafSprites.Count == 0) return;

        // Evaluate shared wind gust surge
        float gustFactor = 1f;
        if (menuActiveGustTimer >= 0f)
        {
            menuActiveGustTimer += dt;
            const float gustDur = 2.6f;
            if (menuActiveGustTimer >= gustDur)
            {
                menuActiveGustTimer = -1f;
                menuGustCountdown = UnityEngine.Random.Range(6.5f, 11.5f);
            }
            else
            {
                float bell = Mathf.Sin(Mathf.Clamp01(menuActiveGustTimer / gustDur) * Mathf.PI);
                gustFactor = Mathf.Lerp(1f, 2.0f, bell);
            }
        }
        else
        {
            menuGustCountdown -= dt;
            if (menuGustCountdown <= 0f)
            {
                menuActiveGustTimer = 0f;
            }
        }

        // Convert mouse screen movement to 1920x1080 canvas coordinates for subtle cursor air turbulence
        Vector2 mouseDelta = Vector2.zero;
        Vector2 mouseCanvasPos = Vector2.zero;
        if (Mouse.current != null && Screen.width > 0 && Screen.height > 0)
        {
            Vector2 screenPos = Mouse.current.position.ReadValue();
            mouseCanvasPos = new Vector2(
                (screenPos.x / Screen.width - 0.5f) * 1920f,
                (screenPos.y / Screen.height - 0.5f) * 1080f
            );
            if (hasMouseCanvasPos && dt > 0.0001f)
            {
                mouseDelta = (mouseCanvasPos - lastMouseCanvasPos) / dt;
            }
            lastMouseCanvasPos = mouseCanvasPos;
            hasMouseCanvasPos = true;
        }
        bool hasFastMouseSweep = mouseDelta.sqrMagnitude > (420f * 420f);

        Vector2 windDir = menuWindDirection.sqrMagnitude > 0.001f
            ? menuWindDirection.normalized
            : new Vector2(1f, -0.25f).normalized;
        Vector2 perpDir = new Vector2(-windDir.y, windDir.x);

        const float boundX = 1040f;
        const float boundY = 580f;

        for (int i = 0; i < menuLeaves.Count; i++)
        {
            MenuLeafParticle leaf = menuLeaves[i];
            if (leaf.Rect == null || leaf.Image == null) continue;

            if (hasFastMouseSweep)
            {
                Vector2 diff = leaf.Position - mouseCanvasPos;
                float sqrDist = diff.sqrMagnitude;
                if (sqrDist < (180f * 180f))
                {
                    float dist = Mathf.Max(12f, Mathf.Sqrt(sqrDist));
                    float falloff = 1f - (dist / 180f);
                    Vector2 pushDir = (mouseDelta.normalized * 0.6f + diff / dist * 0.8f).normalized;
                    leaf.ExtraVelocity += pushDir * (240f * falloff * dt * 8f);
                }
            }

            float flutter = Mathf.Sin(menuAmbientClock * leaf.SwaySpeed + leaf.Phase) * leaf.SwayAmp;
            Vector2 vel = windDir * (leaf.Speed * gustFactor) + perpDir * flutter + leaf.ExtraVelocity;
            leaf.Position += vel * dt;
            leaf.ExtraVelocity *= Mathf.Clamp01(1f - 3.2f * dt);

            if (windDir.x >= 0f && leaf.Position.x > boundX)
            {
                leaf.Position.x = -boundX;
                leaf.Position.y = UnityEngine.Random.Range(-boundY, boundY);
            }
            else if (windDir.x < 0f && leaf.Position.x < -boundX)
            {
                leaf.Position.x = boundX;
                leaf.Position.y = UnityEngine.Random.Range(-boundY, boundY);
            }
            if (windDir.y <= 0f && leaf.Position.y < -boundY)
            {
                leaf.Position.y = boundY;
                leaf.Position.x = UnityEngine.Random.Range(-boundX, boundX);
            }
            else if (windDir.y > 0f && leaf.Position.y > boundY)
            {
                leaf.Position.y = -boundY;
                leaf.Position.x = UnityEngine.Random.Range(-boundX, boundX);
            }

            leaf.Rect.anchoredPosition = SnapToUiPixelGrid(leaf.Position);

            // Cycle Leaf1-Leaf4 frames at ~12 FPS (boosting faster during wind gusts/turbulence)
            float boost = 1f + Mathf.Clamp01(leaf.ExtraVelocity.magnitude / 220f) + Mathf.Clamp01(gustFactor - 1f) * 0.35f;
            leaf.AnimTimer += dt * leaf.AnimRate * boost;
            if (leaf.AnimTimer >= 1f)
            {
                int steps = Mathf.FloorToInt(leaf.AnimTimer);
                leaf.AnimTimer -= steps;
                leaf.FrameIndex = (leaf.FrameIndex + steps) % menuLeafSprites.Count;
                leaf.Image.sprite = menuLeafSprites[leaf.FrameIndex];
            }

            float edgeFadeX = Mathf.Clamp01((boundX - Mathf.Abs(leaf.Position.x)) / 100f);
            float edgeFadeY = Mathf.Clamp01((boundY - Mathf.Abs(leaf.Position.y)) / 70f);
            Color c = leaf.Image.color;
            c.a = leaf.BaseAlpha * edgeFadeX * edgeFadeY;
            leaf.Image.color = c;

            menuLeaves[i] = leaf;
        }
    }

    private void SetupCharacterAliveFX(Transform canvasRoot)
    {
        Transform charTrans = parallaxBackground != null
            ? parallaxBackground.Find("Character")
            : canvasRoot.Find("Character");
        if (charTrans == null) return;

        characterRect = charTrans as RectTransform;
        if (characterRect == null || !characterRect.TryGetComponent(out Image charImg) || charImg.sprite == null)
        {
            return;
        }

        characterBasePos = characterRect.anchoredPosition;
        characterBaseScale = characterRect.localScale;
        charImg.color = Color.black;
    }

    private void UpdateCharacterAliveFX(float dt)
    {
        startHoverEchoBoost = Mathf.MoveTowards(startHoverEchoBoost, 0f, dt * 1.4f);

        if (!enableCharacterAliveFX || characterRect == null) return;

        float breathWave = Mathf.Sin(menuAmbientClock * 1.9f);
        float steppedBreathY = Mathf.Round((breathWave * 4f) / UiPixelGridStep) * UiPixelGridStep;
        Vector2 extraParallax = new Vector2(
            smoothedMouseNorm.x * characterExtraParallax,
            smoothedMouseNorm.y * (characterExtraParallax * 0.45f)
        );
        characterRect.anchoredPosition = characterBasePos + extraParallax + new Vector2(0f, steppedBreathY);
        characterRect.localScale = new Vector3(
            characterBaseScale.x,
            characterBaseScale.y * (1f + 0.012f * Mathf.Round(breathWave * 2f) * 0.5f),
            characterBaseScale.z
        );
    }

    private void SetupTitleVisualFX()
    {
        for (int i = 0; i < menuUIElements.Count; i++)
        {
            RectTransform rt = menuUIElements[i];
            if (rt == null) continue;
            TextMeshProUGUI tmp = rt.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null && tmp.text.IndexOf("CHAIN", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                titleRootRect = rt;
                if (tmp.rectTransform != rt)
                {
                    titleForegroundRect = tmp.rectTransform;
                    titleForegroundBasePos = titleForegroundRect.anchoredPosition;
                }
                break;
            }
        }

        if (titleRootRect == null) return;

        Sprite pixel = GetWhitePixelSprite();

        // Crisp unrotated pixel-staircase white katana glint sweeping across CHAIN-SLASH
        GameObject streakObj = new GameObject("TitleKatanaGlint");
        streakObj.transform.SetParent(titleRootRect, false);
        titleSlashStreakRect = streakObj.AddComponent<RectTransform>();
        titleSlashStreakRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleSlashStreakRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleSlashStreakRect.sizeDelta = new Vector2(220f, 44f);
        titleSlashStreakImg = streakObj.AddComponent<Image>();
        titleSlashStreakImg.sprite = GetTitleKatanaGlintSprite();
        titleSlashStreakImg.raycastTarget = false;
        titleSlashStreakImg.color = new Color(1f, 1f, 1f, 0f);

        // Signature monochrome pixel-art Chain & Slash blade divider centered cleanly beneath the title
        GameObject dividerObj = new GameObject("ChainSlashTitleDivider");
        dividerObj.transform.SetParent(titleRootRect, false);
        RectTransform dividerRect = dividerObj.AddComponent<RectTransform>();
        dividerRect.anchorMin = new Vector2(0.5f, 0f);
        dividerRect.anchorMax = new Vector2(0.5f, 0f);
        dividerRect.anchoredPosition = new Vector2(0f, -26f);
        dividerRect.sizeDelta = new Vector2(464f, 16f);

        GameObject bladeObj = new GameObject("DividerBladeLine");
        bladeObj.transform.SetParent(dividerRect, false);
        RectTransform bladeRt = bladeObj.AddComponent<RectTransform>();
        bladeRt.anchorMin = new Vector2(0f, 0.5f);
        bladeRt.anchorMax = new Vector2(1f, 0.5f);
        bladeRt.sizeDelta = new Vector2(0f, 4f);
        dividerBladeImg = bladeObj.AddComponent<Image>();
        dividerBladeImg.sprite = pixel;
        dividerBladeImg.raycastTarget = false;
        dividerBladeImg.color = new Color(1f, 1f, 1f, 0.85f);

        GameObject diamondObj = new GameObject("DividerChainDiamond");
        diamondObj.transform.SetParent(dividerRect, false);
        dividerDiamondRect = diamondObj.AddComponent<RectTransform>();
        dividerDiamondRect.anchorMin = new Vector2(0.5f, 0.5f);
        dividerDiamondRect.anchorMax = new Vector2(0.5f, 0.5f);
        dividerDiamondRect.sizeDelta = new Vector2(18f, 18f);
        Image diamondImg = diamondObj.AddComponent<Image>();
        diamondImg.sprite = MenuButtonFX.GetPixelDiamondSprite();
        diamondImg.raycastTarget = false;
        diamondImg.color = Color.white;

        CreateDividerNode(dividerRect, "LeftChainNode", new Vector2(0f, 0.5f), new Vector2(8f, 0f));
        CreateDividerNode(dividerRect, "RightChainNode", new Vector2(1f, 0.5f), new Vector2(-8f, 0f));
    }

    private void CreateDividerNode(RectTransform parent, string name, Vector2 anchor, Vector2 anchoredPos)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.anchoredPosition = SnapToUiPixelGrid(anchoredPos);
        rt.sizeDelta = new Vector2(8f, 8f);
        Image img = obj.AddComponent<Image>();
        img.sprite = GetWhitePixelSprite();
        img.raycastTarget = false;
        img.color = Color.white;
    }

    private void UpdateTitleSlashFX(float dt)
    {
        if (!enableTitleSlashFX || titleRootRect == null) return;

        titleSlashTimer -= dt;
        if (titleSlashTimer <= 0f)
        {
            titleSlashTimer = UnityEngine.Random.Range(3.6f, 5.2f);
            titleSlashAnimTime = 0f;
        }

        if (titleSlashAnimTime >= 0f && titleSlashStreakRect != null && titleSlashStreakImg != null)
        {
            const float streakDur = 0.24f;
            titleSlashAnimTime += dt;
            float t = Mathf.Clamp01(titleSlashAnimTime / streakDur);
            float steppedT = EnemyBase.QuantizeStep(t, 8);

            float sweepX = Mathf.Lerp(-260f, 260f, steppedT);
            float sweepY = Mathf.Lerp(-28f, 36f, steppedT);
            titleSlashStreakRect.anchoredPosition = SnapToUiPixelGrid(new Vector2(sweepX, sweepY));
            float alpha = EnemyBase.QuantizeStep(Mathf.Sin(t * Mathf.PI), 4);
            titleSlashStreakImg.color = new Color(1f, 1f, 1f, alpha * 0.95f);

            if (titleForegroundRect != null)
            {
                float jitter = (t < 0.45f) ? Mathf.Round((Mathf.Sin(t * 60f) * 4f) / UiPixelGridStep) * UiPixelGridStep : 0f;
                titleForegroundRect.anchoredPosition = titleForegroundBasePos + new Vector2(jitter, jitter * 0.5f);
            }

            if (t >= 1f)
            {
                titleSlashAnimTime = -1f;
                titleSlashStreakImg.color = new Color(1f, 1f, 1f, 0f);
                if (titleForegroundRect != null)
                {
                    titleForegroundRect.anchoredPosition = titleForegroundBasePos;
                }
            }
        }

        if (dividerDiamondRect != null && dividerBladeImg != null)
        {
            float pulse = EnemyBase.QuantizeStep(0.5f + 0.5f * Mathf.Sin(menuAmbientClock * 2.2f), 4);
            float diamondScale = 0.90f + 0.20f * pulse + (startHoverEchoBoost > 0.25f ? 0.22f : 0f);
            dividerDiamondRect.localScale = new Vector3(diamondScale, diamondScale, 1f);
            dividerBladeImg.color = new Color(1f, 1f, 1f, 0.55f + 0.35f * pulse);
        }
    }

    private static Vector2 SnapToUiPixelGrid(Vector2 pos) => MenuButtonFX.SnapToUiPixelGrid(pos);

    private static RectTransform CreateFullStretchRect(GameObject obj)
    {
        RectTransform rt = obj.GetComponent<RectTransform>();
        if (rt == null) rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static Sprite GetWhitePixelSprite() => MenuButtonFX.GetWhitePixelSprite();

    private static Sprite GetTitleKatanaGlintSprite()
    {
        if (cachedTitleSlashSprite != null) return cachedTitleSlashSprite;

        const int w = 55;
        const int h = 11;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color pale = new Color(0.85f, 0.90f, 1f, 0.65f);
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        for (int x = 0; x < w; x++)
        {
            float t = x / (float)(w - 1);
            int centerY = Mathf.RoundToInt(Mathf.Lerp(1f, h - 2f, t));
            pixels[centerY * w + x] = Color.white;
            if (x > 6 && x < w - 7 && centerY + 1 < h)
            {
                pixels[(centerY + 1) * w + x] = ((x % 2) == 0) ? Color.white : pale;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        cachedTitleSlashSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
        return cachedTitleSlashSprite;
    }

    #endregion
}
