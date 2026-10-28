using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(Collider2D))]
public class PortalNextLevel : MonoBehaviour
{
    [Header("Next Level Settings")]
    [SerializeField] private float teleportDelay = 0.05f;

    [Header("Portal Audio Settings")]
    [Tooltip("Sound effect played when the player enters the portal.")]
    [SerializeField] private AudioClip portalEnterSFX;
    [SerializeField] [Range(0f, 1f)] private float portalSFXVolume = 1f;

    [Header("Spawn & Idle Visuals")]
    [SerializeField] private bool animateSpawnScale = true;
    [SerializeField] private float spawnAnimationDuration = 0.35f;
    [SerializeField] private bool animateIdlePulse = true;
    [SerializeField] private float idlePulseSpeed = 3f;
    [SerializeField] private float idlePulseAmount = 0.06f;

    [Header("Camera Zoom & Fade Transition")]
    [SerializeField] private float zoomInDuration = 0.85f;
    [SerializeField] private float zoomInOrthoSize = 1.8f;
    [SerializeField] [Range(0f, 0.8f)] private float fadeInStartNormalizedTime = 0.2f;
    [SerializeField] private float blackScreenHoldDuration = 0.1f;
    [SerializeField] private float fadeOutDuration = 0.6f;
    [SerializeField] private float portalEnterExpandMultiplier = 1.35f;

    private bool isTeleporting = false;
    private bool spawnAnimFinished = false;
    private Vector3 targetScale;
    private Collider2D portalCollider;
    private AudioSource portalAudioSource;

    private void Awake()
    {
        targetScale = transform.localScale;
        if (targetScale.sqrMagnitude < 0.0001f)
        {
            targetScale = Vector3.one;
        }

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
    }

    private void OnEnable()
    {
        if (animateSpawnScale && !isTeleporting)
        {
            spawnAnimFinished = false;
            StartCoroutine(SpawnScaleRoutine());
        }
        else
        {
            spawnAnimFinished = true;
        }
    }

    private void Update()
    {
        if (isTeleporting || !spawnAnimFinished || !animateIdlePulse) return;

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * idlePulseSpeed) * idlePulseAmount;
        transform.localScale = targetScale * pulse;
    }

    private IEnumerator SpawnScaleRoutine()
    {
        transform.localScale = Vector3.zero;
        float elapsed = 0f;

        while (elapsed < spawnAnimationDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / spawnAnimationDuration);
            // Smooth overshoot pop-in curve
            float scaleFactor = 1f + 0.15f * Mathf.Sin(t * Mathf.PI) * (1f - t);
            transform.localScale = targetScale * Mathf.Lerp(0f, scaleFactor, t);
            yield return null;
        }

        transform.localScale = targetScale;
        spawnAnimFinished = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryTeleportPlayer(other.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryTeleportPlayer(collision.gameObject);
    }

    private void TryTeleportPlayer(GameObject hitObject)
    {
        if (isTeleporting || hitObject == null) return;

        PlayerController player = hitObject.GetComponentInParent<PlayerController>();
        if (player == null && !hitObject.CompareTag("Player")) return;

        if (player == null)
        {
            player = PlayerController.Instance != null ? PlayerController.Instance : FindFirstObjectByType<PlayerController>();
        }

        isTeleporting = true;
        if (portalCollider != null)
        {
            portalCollider.enabled = false;
        }

        // Persist this portal object across the scene load so it can drive the post-load fade-out and finish its audio
        transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);

        PlayPortalEnterSound(player);
        StartCoroutine(TeleportToNextSceneRoutine(player));
    }

    private void PlayPortalEnterSound(PlayerController player)
    {
        if (portalEnterSFX == null) return;

        if (portalAudioSource == null)
        {
            portalAudioSource = gameObject.AddComponent<AudioSource>();
            portalAudioSource.playOnAwake = false;
            portalAudioSource.spatialBlend = 0f;
        }

        if (player != null)
        {
            AudioSource playerAudio = player.GetComponent<AudioSource>();
            if (playerAudio != null && portalAudioSource.outputAudioMixerGroup == null)
            {
                portalAudioSource.outputAudioMixerGroup = playerAudio.outputAudioMixerGroup;
            }
        }

        portalAudioSource.PlayOneShot(portalEnterSFX, portalSFXVolume);
    }

    private IEnumerator TeleportToNextSceneRoutine(PlayerController player)
    {
        if (teleportDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(teleportDelay);
        }

        // 1. Keep player standing in place during the portal transition
        Rigidbody2D playerRb = null;
        Animator playerAnimator = null;
        RigidbodyConstraints2D originalConstraints = RigidbodyConstraints2D.FreezeRotation;

        if (player != null)
        {
            player.DetachGrappling();
            playerRb = player.GetComponent<Rigidbody2D>();
            if (playerRb != null)
            {
                originalConstraints = playerRb.constraints;
                playerRb.linearVelocity = new Vector2(0f, Mathf.Min(0f, playerRb.linearVelocity.y));
                playerRb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
            }

            playerAnimator = player.GetComponentInChildren<Animator>();
            if (playerAnimator != null)
            {
                playerAnimator.SetBool("isRunning", false);
                playerAnimator.SetBool("isDash", false);
            }
        }

        // 2. Lock camera to the portal and prepare zoom
        Camera mainCam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        CinemachineCamera cinemachineCam = FindFirstObjectByType<CinemachineCamera>();

        float startOrthoSize = 6.25f;
        if (mainCam != null && mainCam.orthographic)
        {
            startOrthoSize = mainCam.orthographicSize;
        }
        else if (cinemachineCam != null)
        {
            startOrthoSize = cinemachineCam.Lens.OrthographicSize;
        }

        if (cinemachineCam != null)
        {
            // Disable Group Framing so it no longer overrides OrthographicSize or tracks the target group
            CinemachineGroupFraming groupFraming = cinemachineCam.GetComponent<CinemachineGroupFraming>();
            if (groupFraming != null)
            {
                groupFraming.enabled = false;
            }

            CinemachinePositionComposer composer = cinemachineCam.GetComponent<CinemachinePositionComposer>();
            if (composer != null)
            {
                composer.TargetOffset = Vector3.zero;
                composer.Damping = new Vector3(0.12f, 0.12f, 0.12f);
            }

            LensSettings lens = cinemachineCam.Lens;
            lens.OrthographicSize = startOrthoSize;
            cinemachineCam.Lens = lens;
            cinemachineCam.Target.TrackingTarget = transform;
        }

        Vector3 camStartPos = mainCam != null ? mainCam.transform.position : transform.position + new Vector3(0f, 0f, -10f);
        Vector3 camTargetPos = new Vector3(transform.position.x, transform.position.y, camStartPos.z);

        // 3. Create full-screen black overlay Canvas for the fade-in / fade-out transition
        CanvasGroup fadeCanvasGroup = CreateScreenFadeOverlay();

        // 4. Zoom camera into the portal while fading to black
        float elapsed = 0f;
        float duration = Mathf.Max(0.1f, zoomInDuration);
        Vector3 portalStartScale = transform.localScale;
        Vector3 portalZoomScale = targetScale * portalEnterExpandMultiplier;
        float targetOrtho = Mathf.Max(0.5f, zoomInOrthoSize);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = t * t * (3f - 2f * t); // SmoothStep curve

            // Zoom camera in on the portal
            float currentOrtho = Mathf.Lerp(startOrthoSize, targetOrtho, smoothT);
            if (cinemachineCam != null)
            {
                LensSettings lens = cinemachineCam.Lens;
                lens.OrthographicSize = currentOrtho;
                cinemachineCam.Lens = lens;
            }
            if (mainCam != null)
            {
                mainCam.orthographicSize = currentOrtho;
                if (cinemachineCam == null)
                {
                    mainCam.transform.position = Vector3.Lerp(camStartPos, camTargetPos, smoothT);
                }
            }

            // Expand portal slightly as camera dives into it
            transform.localScale = Vector3.Lerp(portalStartScale, portalZoomScale, smoothT);

            // Keep player standing idle in place
            if (playerRb != null)
            {
                playerRb.linearVelocity = new Vector2(0f, Mathf.Min(0f, playerRb.linearVelocity.y));
            }
            if (playerAnimator != null)
            {
                playerAnimator.SetBool("isRunning", false);
            }

            // Fade in black screen overlay
            if (fadeCanvasGroup != null)
            {
                float fadeT = Mathf.InverseLerp(fadeInStartNormalizedTime, 1f, t);
                fadeCanvasGroup.alpha = fadeT * fadeT * (3f - 2f * fadeT);
            }

            yield return null;
        }

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 1f;
        }

        if (blackScreenHoldDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(blackScreenHoldDuration);
        }

        // 5. Restore player physics constraints while screen is fully black, before loading next scene
        if (playerRb != null)
        {
            playerRb.constraints = originalConstraints;
            playerRb.linearVelocity = Vector2.zero;
        }

        // Hide this portal's visuals so it is invisible in the next scene while fading out
        Renderer[] portalRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in portalRenderers)
        {
            if (r != null) r.enabled = false;
        }

        // Ensure time scale and static combat flags are reset before switching scenes
        Time.timeScale = 1f;
        ChainEnemy.isExploding = false;

        // 6. Load next scene from Build Profile
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

        // Wait two frames for the new scene's Awake/Start/OnSceneLoaded to position the player & camera
        yield return null;
        yield return null;

        CinemachineCamera newSceneCinemachine = FindFirstObjectByType<CinemachineCamera>();
        if (newSceneCinemachine != null)
        {
            newSceneCinemachine.PreviousStateIsValid = false;
        }

        // 7. Fade out the black screen in the new scene
        if (fadeCanvasGroup != null)
        {
            float fadeOutElapsed = 0f;
            float outDuration = Mathf.Max(0.05f, fadeOutDuration);

            while (fadeOutElapsed < outDuration)
            {
                fadeOutElapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(fadeOutElapsed / outDuration);
                float smoothT = t * t * (3f - 2f * t);
                fadeCanvasGroup.alpha = 1f - smoothT;
                yield return null;
            }

            fadeCanvasGroup.alpha = 0f;
            Destroy(fadeCanvasGroup.gameObject);
        }

        while (portalAudioSource != null && portalAudioSource.isPlaying)
        {
            yield return null;
        }

        Destroy(gameObject);
    }

    private CanvasGroup CreateScreenFadeOverlay()
    {
        GameObject canvasObj = new GameObject("PortalTransitionFadeCanvas");
        canvasObj.transform.SetParent(transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        CanvasGroup canvasGroup = canvasObj.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = false;

        GameObject imageObj = new GameObject("BlackOverlay");
        imageObj.transform.SetParent(canvasObj.transform, false);

        Image blackImage = imageObj.AddComponent<Image>();
        blackImage.color = Color.black;
        blackImage.raycastTarget = true;

        RectTransform rect = imageObj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        return canvasGroup;
    }
}

