using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
    private MainMenu mainMenu;

    private void Awake()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;
        mainMenu = FindFirstObjectByType<MainMenu>();
    }

    private void OnEnable()
    {
        transform.localScale = originalScale;
        targetScale = originalScale;
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * animationSpeed);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        targetScale = originalScale * hoverScale;
        if (mainMenu != null && hoverSFX != null)
        {
            mainMenu.PlaySFX(hoverSFX);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = originalScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        targetScale = originalScale * clickScale;
        if (mainMenu != null && clickSFX != null)
        {
            mainMenu.PlaySFX(clickSFX);
        }
    }
}