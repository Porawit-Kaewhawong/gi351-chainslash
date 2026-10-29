using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Handles UI button hover, click scaling, and audio feedback for both MainMenu and GameOverPanel.
/// </summary>
[DisallowMultipleComponent]
public class MenuButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    [Header("Scale Animation Settings")]
    [SerializeField] private float hoverScale = 1.12f;
    [SerializeField] private float clickScale = 0.95f;
    [SerializeField] private float animationSpeed = 12f;

    [Header("Audio Settings (Optional)")]
    [SerializeField] private AudioClip hoverSFX;
    [SerializeField] private AudioClip clickSFX;

    private Vector3 originalScale;
    private Vector3 targetScale;
    private MainMenu cachedMainMenu;
    private GameOverPanel cachedGameOverPanel;

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
                cachedGameOverPanel = GameOverPanel.Instance != null ? GameOverPanel.Instance : Object.FindFirstObjectByType<GameOverPanel>();
            }
            return cachedGameOverPanel;
        }
    }

    private void Awake()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;
        cachedMainMenu = Object.FindFirstObjectByType<MainMenu>();
        cachedGameOverPanel = GameOverPanel.Instance != null ? GameOverPanel.Instance : Object.FindFirstObjectByType<GameOverPanel>();
    }

    public void SetGameOverPanel(GameOverPanel panel)
    {
        cachedGameOverPanel = panel;
    }

    private void OnEnable()
    {
        transform.localScale = originalScale;
        targetScale = originalScale;
    }

    private void Update()
    {
        // Avoid calculating lerps every frame when the button scale is already settled
        if ((transform.localScale - targetScale).sqrMagnitude > 0.00001f)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * animationSpeed);
            if ((transform.localScale - targetScale).sqrMagnitude <= 0.00001f)
            {
                transform.localScale = targetScale;
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        targetScale = originalScale * hoverScale;

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

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = originalScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        targetScale = originalScale * clickScale;

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
}