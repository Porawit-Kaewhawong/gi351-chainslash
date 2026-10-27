using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    [Header("Scene Transition Settings")]
    [SerializeField] private string gameSceneName = "GameScene";
    [SerializeField] private CanvasGroup transitionCanvasGroup; // Fullscreen black panel with CanvasGroup
    [SerializeField] private float fadeDuration = 0.5f;

    [Header("Background Mouse Parallax")]
    [SerializeField] private RectTransform parallaxBackground;
    [SerializeField] private float parallaxAmount = 20f;
    [SerializeField] private float parallaxSmoothSpeed = 5f;

    [Header("Audio Settings")]
    [SerializeField] private AudioSource bgmAudioSource;
    [SerializeField] private AudioSource sfxAudioSource;

    private bool isTransitioning = false;
    private Vector3 initialBackgroundPos;

    private void Start()
    {
        if (parallaxBackground != null)
        {
            initialBackgroundPos = parallaxBackground.anchoredPosition;
        }

        // Auto-fade in from black on start
        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.alpha = 1f;
            transitionCanvasGroup.blocksRaycasts = true;
            StartCoroutine(FadeRoutine(1f, 0f, () =>
            {
                transitionCanvasGroup.blocksRaycasts = false;
            }));
        }
    }

    private void Update()
    {
        HandleParallax();
    }

    private void HandleParallax()
    {
        if (parallaxBackground == null) return;

        Vector2 mousePos = Vector2.zero;
        if (Mouse.current != null)
        {
            mousePos = Mouse.current.position.ReadValue();
        }

        // Calculate normalized screen coordinates (-1 to 1)
        float normalizedX = (mousePos.x / Screen.width - 0.5f) * 2f;
        float normalizedY = (mousePos.y / Screen.height - 0.5f) * 2f;

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

    private IEnumerator TransitionAndLoadScene()
    {
        isTransitioning = true;

        if (transitionCanvasGroup != null)
        {
            transitionCanvasGroup.blocksRaycasts = true;
            yield return FadeRoutine(0f, 1f, null);
        }

        SceneManager.LoadScene(gameSceneName);
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
}