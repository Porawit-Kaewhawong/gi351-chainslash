using System.Collections;
using UnityEngine;

/// <summary>
/// Controller for the Custom/Pixel2D_Sprite shader.
/// Manages Hit Flash, Pixel Outline, Retro Dither Fade, and Pixelation via MaterialPropertyBlock.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[DisallowMultipleComponent]
public class PixelSpriteEffect : MonoBehaviour
{
    public enum OutlineMode
    {
        Outer = 0,
        Inner = 1,
        Both = 2
    }

    [Header("Shader References")]
    [Tooltip("Optional reference material using Custom/Pixel2D_Sprite. If null, a shared default instance is automatically applied.")]
    [SerializeField] private Material pixelMaterial;

    [Header("Outline Settings")]
    [SerializeField] private bool outlineEnabled = false;
    [SerializeField] private Color outlineColor = new Color(1f, 0.95f, 0.35f, 1f);
    [Range(0f, 10f)]
    [SerializeField] private float outlineThickness = 1.2f;
    [Range(0.01f, 0.99f)]
    [SerializeField] private float outlineAlphaThreshold = 0.1f;
    [SerializeField] private OutlineMode outlineMode = OutlineMode.Outer;

    [Header("Hit Flash Settings")]
    [SerializeField] private Color defaultFlashColor = Color.white;
    [Range(0.01f, 1f)]
    [SerializeField] private float defaultFlashDuration = 0.12f;

    [Header("Pixel Snap & Pixelation")]
    [SerializeField] private bool snapToPixelGrid = false;
    [SerializeField] private float pixelsPerUnit = 16f;
    [SerializeField] private bool pixelateEnabled = false;
    [Range(1f, 16f)]
    [SerializeField] private float pixelBlockSize = 1f;

    [Header("Retro Quantize & Dither")]
    [SerializeField] private bool quantizeEnabled = false;
    [Range(2, 32)]
    [SerializeField] private int colorSteps = 8;
    [SerializeField] private bool ditherEnabled = false;
    [Range(0f, 1f)]
    [SerializeField] private float ditherFade = 0f;

    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock propertyBlock;
    private Coroutine flashCoroutine;

    private static Material sharedPixelMaterial;
    public static Material SharedPixelMaterial
    {
        get
        {
            if (sharedPixelMaterial == null)
            {
                Shader shader = Shader.Find("Custom/Pixel2D_Sprite");
                if (shader != null)
                {
                    sharedPixelMaterial = new Material(shader) { name = "Pixel2D_Sprite_Shared" };
                }
            }
            return sharedPixelMaterial;
        }
    }

    // Cached shader property IDs
    private static readonly int PropEnableOutline = Shader.PropertyToID("_EnableOutline");
    private static readonly int PropOutlineColor = Shader.PropertyToID("_OutlineColor");
    private static readonly int PropOutlineThickness = Shader.PropertyToID("_OutlineThickness");
    private static readonly int PropOutlineThreshold = Shader.PropertyToID("_OutlineAlphaThreshold");
    private static readonly int PropOutlineMode = Shader.PropertyToID("_OutlineMode");
    private static readonly int PropFlashColor = Shader.PropertyToID("_FlashColor");
    private static readonly int PropFlashAmount = Shader.PropertyToID("_FlashAmount");
    private static readonly int PropSnapGrid = Shader.PropertyToID("_SnapToPixelGrid");
    private static readonly int PropPPU = Shader.PropertyToID("_PixelsPerUnit");
    private static readonly int PropEnablePixelate = Shader.PropertyToID("_EnablePixelate");
    private static readonly int PropPixelBlock = Shader.PropertyToID("_PixelBlockSize");
    private static readonly int PropEnableQuantize = Shader.PropertyToID("_EnableQuantize");
    private static readonly int PropColorSteps = Shader.PropertyToID("_ColorSteps");
    private static readonly int PropEnableDither = Shader.PropertyToID("_EnableDither");
    private static readonly int PropDitherFade = Shader.PropertyToID("_DitherFade");

    public bool OutlineEnabled
    {
        get => outlineEnabled;
        set { outlineEnabled = value; ApplyProperties(); }
    }

    public Color OutlineColor
    {
        get => outlineColor;
        set { outlineColor = value; ApplyProperties(); }
    }

    public float OutlineThickness
    {
        get => outlineThickness;
        set { outlineThickness = value; ApplyProperties(); }
    }

    public OutlineMode CurrentOutlineMode
    {
        get => outlineMode;
        set { outlineMode = value; ApplyProperties(); }
    }

    public float DitherFade
    {
        get => ditherFade;
        set { ditherFade = Mathf.Clamp01(value); ditherEnabled = ditherFade > 0.001f; ApplyProperties(); }
    }

    private void Awake()
    {
        InitializeRenderer();
        ApplyProperties();
    }

    private void OnValidate()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        ApplyProperties();
    }

    private void InitializeRenderer()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        if (pixelMaterial != null)
        {
            if (spriteRenderer.sharedMaterial != pixelMaterial)
            {
                spriteRenderer.sharedMaterial = pixelMaterial;
            }
        }
        else if (spriteRenderer.sharedMaterial == null || !spriteRenderer.sharedMaterial.shader.name.Contains("Pixel2D_Sprite"))
        {
            Material shared = SharedPixelMaterial;
            if (shared != null)
            {
                spriteRenderer.sharedMaterial = shared;
            }
        }
    }

    /// <summary>
    /// Applies all active inspector settings to the SpriteRenderer via MaterialPropertyBlock.
    /// </summary>
    public void ApplyProperties()
    {
        if (spriteRenderer == null) return;
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

        spriteRenderer.GetPropertyBlock(propertyBlock);

        // Outline
        propertyBlock.SetFloat(PropEnableOutline, outlineEnabled ? 1f : 0f);
        propertyBlock.SetColor(PropOutlineColor, outlineColor);
        propertyBlock.SetFloat(PropOutlineThickness, outlineThickness);
        propertyBlock.SetFloat(PropOutlineThreshold, outlineAlphaThreshold);
        propertyBlock.SetFloat(PropOutlineMode, (float)outlineMode);

        // Pixel Snap & Pixelation
        propertyBlock.SetFloat(PropSnapGrid, snapToPixelGrid ? 1f : 0f);
        propertyBlock.SetFloat(PropPPU, pixelsPerUnit);
        propertyBlock.SetFloat(PropEnablePixelate, pixelateEnabled ? 1f : 0f);
        propertyBlock.SetFloat(PropPixelBlock, pixelBlockSize);

        // Quantize & Dither
        propertyBlock.SetFloat(PropEnableQuantize, quantizeEnabled ? 1f : 0f);
        propertyBlock.SetFloat(PropColorSteps, colorSteps);
        propertyBlock.SetFloat(PropEnableDither, (ditherEnabled || ditherFade > 0.001f) ? 1f : 0f);
        propertyBlock.SetFloat(PropDitherFade, ditherFade);

        spriteRenderer.SetPropertyBlock(propertyBlock);
    }

    /// <summary>
    /// Triggers a hit flash effect smoothly fading back to normal.
    /// </summary>
    public void Flash(float duration = -1f, Color? customFlashColor = null)
    {
        if (!isActiveAndEnabled || spriteRenderer == null) return;
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(FlashRoutine(duration > 0f ? duration : defaultFlashDuration, customFlashColor ?? defaultFlashColor));
    }

    private IEnumerator FlashRoutine(float duration, Color color)
    {
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
        spriteRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(PropFlashColor, color);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / duration);
            propertyBlock.SetFloat(PropFlashAmount, t);
            spriteRenderer.SetPropertyBlock(propertyBlock);
            yield return null;
        }

        propertyBlock.SetFloat(PropFlashAmount, 0f);
        spriteRenderer.SetPropertyBlock(propertyBlock);
        flashCoroutine = null;
    }

    /// <summary>
    /// Enables or disables the outline effect and updates outline parameters dynamically.
    /// </summary>
    public void SetOutline(bool enabled, Color? color = null, float thickness = -1f, OutlineMode? mode = null)
    {
        outlineEnabled = enabled;
        if (color.HasValue) outlineColor = color.Value;
        if (thickness >= 0f) outlineThickness = thickness;
        if (mode.HasValue) outlineMode = mode.Value;
        ApplyProperties();
    }
}
