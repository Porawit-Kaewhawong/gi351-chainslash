using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Handles UI button hover, click scaling, and audio feedback for both MainMenu and GameOverPanel.
/// </summary>
[DisallowMultipleComponent]
public class MenuButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    #region Constants

    private const float ScaleSnapThresholdSqr = 0.00001f;

    #endregion

    #region Serialized Fields

    [Header("Scale Animation Settings")]
    [SerializeField] private float hoverScale = 1.12f;
    [SerializeField] private float clickScale = 0.95f;
    [SerializeField] private float animationSpeed = 12f;

    [Header("Audio Settings (Optional)")]
    [SerializeField] private AudioClip hoverSFX;
    [SerializeField] private AudioClip clickSFX;

    #endregion

    #region Private State

    private Transform cachedTransform;
    private Vector3 originalScale;
    private Vector3 targetScale;
    private bool isAnimating;
    private MainMenu cachedMainMenu;
    private GameOverPanel cachedGameOverPanel;

    #endregion

    #region Public Properties & Accessors

    private MainMenu MainMenuRef
    {
        get
        {
            if (cachedMainMenu == null)
            {
                cachedMainMenu = Object.FindFirstObjectByType<MainMenu>();
            }
            return cachedMainMenu;
        }
    }

    private GameOverPanel GameOverPanelRef
    {
        get
        {
            if (cachedGameOverPanel == null)
            {
                cachedGameOverPanel = GameOverPanel.Instance != null
                    ? GameOverPanel.Instance
                    : Object.FindFirstObjectByType<GameOverPanel>();
            }
            return cachedGameOverPanel;
        }
    }

    public void SetGameOverPanel(GameOverPanel panel)
    {
        cachedGameOverPanel = panel;
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        cachedTransform = transform;
        originalScale = cachedTransform.localScale;
        targetScale = originalScale;
        cachedMainMenu = Object.FindFirstObjectByType<MainMenu>();
        cachedGameOverPanel = GameOverPanel.Instance != null
            ? GameOverPanel.Instance
            : Object.FindFirstObjectByType<GameOverPanel>();
    }

    private void OnEnable()
    {
        if (cachedTransform == null) cachedTransform = transform;
        cachedTransform.localScale = originalScale;
        targetScale = originalScale;
        isAnimating = false;
    }

    private void Update()
    {
        // Avoid calculating lerps every frame when the button scale is already settled
        if (!isAnimating) return;

        Vector3 currentScale = cachedTransform.localScale;
        if ((currentScale - targetScale).sqrMagnitude > ScaleSnapThresholdSqr)
        {
            Vector3 nextScale = Vector3.Lerp(currentScale, targetScale, Time.unscaledDeltaTime * animationSpeed);
            if ((nextScale - targetScale).sqrMagnitude <= ScaleSnapThresholdSqr)
            {
                cachedTransform.localScale = targetScale;
                isAnimating = false;
            }
            else
            {
                cachedTransform.localScale = nextScale;
            }
        }
        else
        {
            cachedTransform.localScale = targetScale;
            isAnimating = false;
        }
    }

    #endregion

    #region Pointer Event Handlers

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetTargetScale(originalScale * hoverScale);
        PlayHoverAudio();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetTargetScale(originalScale);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        SetTargetScale(originalScale * clickScale);
        PlayClickAudio();
    }

    #endregion

    #region UI & Audio Logic

    private void SetTargetScale(Vector3 newTarget)
    {
        targetScale = newTarget;
        isAnimating = true;
    }

    private void PlayHoverAudio()
    {
        MainMenu menu = MainMenuRef;
        if (menu != null)
        {
            menu.PlayButtonHoverSFX(hoverSFX);
            return;
        }

        GameOverPanel panel = GameOverPanelRef;
        if (panel != null)
        {
            panel.PlayButtonHoverSFX(hoverSFX);
        }
    }

    private void PlayClickAudio()
    {
        MainMenu menu = MainMenuRef;
        if (menu != null)
        {
            menu.PlayButtonClickSFX(clickSFX);
            return;
        }

        GameOverPanel panel = GameOverPanelRef;
        if (panel != null)
        {
            panel.PlayButtonClickSFX(clickSFX);
        }
    }

    #endregion
}
