using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    [Header("Scene Transition Settings")]
    [SerializeField] private string gameSceneName = "LevelScene";
    [SerializeField] private CanvasGroup transitionCanvasGroup; // Optional fullscreen panel with CanvasGroup
    [SerializeField] private float fadeDuration = 0.65f;
    [SerializeField] private float zoomScaleOnEnter = 1.22f;
    [SerializeField] private Color slashFlashColor = new Color(0.55f, 0.95f, 1f, 1f);
    [SerializeField] private AudioClip startTransitionSFX;

    [Header("Background Mouse Parallax")]
    [SerializeField] private RectTransform parallaxBackground;
    [SerializeField] private float parallaxAmount = 20f;
    [SerializeField] private float parallaxSmoothSpeed = 5f;

    [Header("Audio Settings")]
    [SerializeField] private AudioSource bgmAudioSource;
    [SerializeField] private AudioSource sfxAudioSource;

    private bool isTransitioning = false;
    private Vector3 initialBackgroundPos;
    private Vector3 initialBackgroundScale = Vector3.one;

    private RectTransform topShutter;
    private RectTransform bottomShutter;
    private RectTransform slashLineRect;
    private Image slashLineImage;
    private List<RectTransform> menuUIElements = new List<RectTransform>();
    private List<Vector2> menuUIInitialPositions = new List<Vector2>();
    private List<CanvasGroup> menuUICanvasGroups = new List<CanvasGroup>();

    private void Awake()
    {
        EnsureTransitionUI();
        CacheMenuUIElements();
    }

    private void Start()
    {
        if (parallaxBackground != null)
        {
            initialBackgroundPos = parallaxBackground.anchoredPosition;
            initialBackgroundScale = parallaxBackground.localScale;
        }

        // Auto-fade in from black on start
        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.alpha = 1f;
            transitionCanvasGroup.blocksRaycasts = true;
            StartCoroutine(MenuIntroRoutine());
        }
    }

    private void Update()
    {
        if (!isTransitioning)
        {
            HandleParallax();
        }
    }

    private void HandleParallax()
    {
        if (parallaxBackground == null) return;

        Vector2 mousePos = Vector2.zero;
        if (Mouse.current != null)
        {
            mousePos = Mouse.current.position.ReadValue();
        }

        float normalizedX = (mousePos.x / Mathf.Max(1f, Screen.width) - 0.5f) * 2f;
        float normalizedY = (mousePos.y / Mathf.Max(1f, Screen.height) - 0.5f) * 2f;

        Vector3 targetPos = initialBackgroundPos + new Vector3(normalizedX * parallaxAmount, normalizedY * parallaxAmount, 0f);
        parallaxBackground.anchoredPosition = Vector3.Lerp(parallaxBackground.anchoredPosition, targetPos, Time.unscaledDeltaTime * parallaxSmoothSpeed);
    }

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

    private void EnsureTransitionUI()
    {
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) rootCanvas = FindFirstObjectByType<Canvas>();
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
            transitionCanvasGroup.alpha = 1f;
        }

        transitionCanvasGroup.transform.SetAsLastSibling();

        // Create diagonal slash & shutter container on the root canvas for the dynamic slash-wipe transition
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

        // Keep fade overlay on top of everything when fading
        transitionCanvasGroup.transform.SetAsLastSibling();
    }

    private RectTransform CreateShutterPanel(RectTransform parent, string name, Vector2 startAnchoredPos, Vector2 size)
    {
        GameObject panelObj = new GameObject(name);
        panelObj.transform.SetParent(parent, false);
        RectTransform rt = panelObj.AddComponent<RectTransform>();
        rt.anchoredPosition = startAnchoredPos;
        rt.sizeDelta = size;
        Image img = panelObj.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        return rt;
    }

    private void CacheMenuUIElements()
    {
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) return;

        menuUIElements.Clear();
        menuUIInitialPositions.Clear();
        menuUICanvasGroups.Clear();

        foreach (Transform child in rootCanvas.transform)
        {
            if (child == null) continue;
            if (parallaxBackground != null && child == parallaxBackground.transform) continue;
            if (transitionCanvasGroup != null && child == transitionCanvasGroup.transform) continue;
            if (child.name == "DynamicSlashWipeFX" || child.name == "DynamicTransitionOverlay") continue;

            RectTransform rt = child as RectTransform;
            if (rt == null) continue;

            CanvasGroup cg = rt.GetComponent<CanvasGroup>();
            if (cg == null) cg = rt.gameObject.AddComponent<CanvasGroup>();

            menuUIElements.Add(rt);
            menuUIInitialPositions.Add(rt.anchoredPosition);
            menuUICanvasGroups.Add(cg);
        }
    }

    private IEnumerator MenuIntroRoutine()
    {
        // Stagger menu UI elements slightly on intro while fading in from black
        for (int i = 0; i < menuUIElements.Count; i++)
        {
            menuUIElements[i].anchoredPosition = menuUIInitialPositions[i] + new Vector2(0f, -35f);
            menuUICanvasGroups[i].alpha = 0f;
        }

        if (parallaxBackground != null)
        {
            parallaxBackground.localScale = initialBackgroundScale * 1.08f;
        }

        float duration = Mathf.Max(0.35f, fadeDuration);
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / duration);
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);

            if (transitionCanvasGroup != null)
            {
                transitionCanvasGroup.alpha = 1f - easeOut;
            }

            if (parallaxBackground != null)
            {
                parallaxBackground.localScale = Vector3.Lerp(initialBackgroundScale * 1.08f, initialBackgroundScale, easeOut);
            }

            for (int i = 0; i < menuUIElements.Count; i++)
            {
                float delay = i * 0.06f;
                float itemT = Mathf.Clamp01((timer - delay) / Mathf.Max(0.15f, duration - delay));
                float itemEase = 1f - Mathf.Pow(1f - itemT, 3f);
                menuUIElements[i].anchoredPosition = Vector2.Lerp(menuUIInitialPositions[i] + new Vector2(0f, -35f), menuUIInitialPositions[i], itemEase);
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

        for (int i = 0; i < menuUIElements.Count; i++)
        {
            menuUIElements[i].anchoredPosition = menuUIInitialPositions[i];
            menuUICanvasGroups[i].alpha = 1f;
        }
    }

    private IEnumerator TransitionAndLoadScene()
    {
        isTransitioning = true;

        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.blocksRaycasts = true;
        }

        PlaySFX(startTransitionSFX);

        Vector3 startBgPos = parallaxBackground != null ? parallaxBackground.anchoredPosition : Vector3.zero;
        Vector3 startBgScale = parallaxBackground != null ? parallaxBackground.localScale : Vector3.one;
        float startBgmVol = bgmAudioSource != null ? bgmAudioSource.volume : 1f;

        // Phase 1: Dynamic UI dispersal + camera zoom-in + diagonal slash beam across the screen
        float slashDuration = Mathf.Max(0.22f, fadeDuration * 0.45f);
        float timer = 0f;

        while (timer < slashDuration)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / slashDuration);
            float easeIn = t * t;
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);

            if (parallaxBackground != null)
            {
                parallaxBackground.anchoredPosition = Vector3.Lerp(startBgPos, initialBackgroundPos, easeOut);
                parallaxBackground.localScale = Vector3.Lerp(startBgScale, initialBackgroundScale * zoomScaleOnEnter, easeIn * 0.6f);
            }

            // Slide and fade menu buttons/title outward
            for (int i = 0; i < menuUIElements.Count; i++)
            {
                Vector2 dir = (i % 2 == 0) ? new Vector2(140f, 0f) : new Vector2(-140f, 0f);
                menuUIElements[i].anchoredPosition = Vector2.Lerp(menuUIInitialPositions[i], menuUIInitialPositions[i] + dir, easeIn);
                menuUICanvasGroups[i].alpha = 1f - easeOut;
            }

            // Slice the glowing slash line across the screen
            if (slashLineRect != null && slashLineImage != null)
            {
                float width = Mathf.Lerp(0f, 3600f, easeOut);
                float thickness = Mathf.Lerp(26f, 6f, t);
                slashLineRect.sizeDelta = new Vector2(width, thickness);
                slashLineImage.color = Color.Lerp(Color.white, slashFlashColor, t);
            }

            if (bgmAudioSource != null)
            {
                bgmAudioSource.volume = Mathf.Lerp(startBgmVol, startBgmVol * 0.4f, t);
            }

            yield return null;
        }

        // Phase 2: Diagonal shutters slam shut along the slash line + final zoom & fade to black
        float shutterDuration = Mathf.Max(0.25f, fadeDuration * 0.55f);
        timer = 0f;

        while (timer < shutterDuration)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / shutterDuration);
            float easeInOut = t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

            if (parallaxBackground != null)
            {
                parallaxBackground.localScale = Vector3.Lerp(
                    initialBackgroundScale * Mathf.Lerp(1f, zoomScaleOnEnter, 0.6f),
                    initialBackgroundScale * zoomScaleOnEnter,
                    t
                );
            }

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

            if (bgmAudioSource != null)
            {
                bgmAudioSource.volume = Mathf.Lerp(startBgmVol * 0.4f, 0f, t);
            }

            yield return null;
        }

        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.alpha = 1f;
        }

        // Spawn a seamless cross-scene fade-in overlay so the gameplay scene opens smoothly from black
        SpawnCrossSceneFadeIn(0.45f);

        LoadTargetGameScene();
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

        if (Application.CanStreamedLevelBeLoaded("LevelScene"))
        {
            SceneManager.LoadScene("LevelScene");
            return;
        }

        // Fallback to index 0 in Build Settings
        SceneManager.LoadScene(0);
    }

    private static void SpawnCrossSceneFadeIn(float duration)
    {
        GameObject fadeCarryObj = new GameObject("CrossSceneFadeIn");
        DontDestroyOnLoad(fadeCarryObj);

        Canvas canvas = fadeCarryObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

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

        CrossSceneFadeRunner runner = fadeCarryObj.AddComponent<CrossSceneFadeRunner>();
        runner.BeginFadeIn(cg, duration);
    }

    private IEnumerator TransitionAndQuit()
    {
        isTransitioning = true;

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
        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime;
            if (transitionCanvasGroup != null)
            {
                transitionCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, timer / fadeDuration);
            }

            if (bgmAudioSource != null && targetAlpha == 1f)
            {
                bgmAudioSource.volume = Mathf.Lerp(1f, 0f, timer / fadeDuration);
            }

            yield return null;
        }

        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.alpha = targetAlpha;
        }

        onComplete?.Invoke();
    }

    public void PlaySFX(AudioClip clip)
    {
        if (sfxAudioSource != null && clip != null)
        {
            sfxAudioSource.PlayOneShot(clip);
        }
    }

    private class CrossSceneFadeRunner : MonoBehaviour
    {
        public void BeginFadeIn(CanvasGroup cg, float duration)
        {
            StartCoroutine(FadeInRoutine(cg, duration));
        }

        private IEnumerator FadeInRoutine(CanvasGroup cg, float duration)
        {
            // Wait one frame for the new scene to initialize
            yield return null;

            float elapsed = 0f;
            while (elapsed < duration && cg != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                cg.alpha = 1f - (t * t);
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}