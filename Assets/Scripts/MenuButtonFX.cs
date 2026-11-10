using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles UI button hover, click scaling, Chain & Slash hover accents, pixel sparks, and audio feedback.
/// </summary>
[DisallowMultipleComponent]
public class MenuButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    #region Constants

    private const float ScaleSnapThresholdSqr = 0.00001f;
    private const float HoverOffsetSnapThresholdSqr = 0.01f;
    private const float UiPixelGridStep = 4f;

    private struct ButtonSpark
    {
        public RectTransform Rect;
        public Image Image;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Elapsed;
        public float Duration;
    }

    private static Sprite cachedWhitePixelSprite;
    private static Sprite cachedPixelPlateSprite;
    private static Sprite cachedPixelDiamondSprite;

    #endregion

    #region Serialized Fields

    [Header("Scale Animation Settings")]
    [SerializeField] private float hoverScale = 1.12f;
    [SerializeField] private float clickScale = 0.95f;
    [SerializeField] private float animationSpeed = 12f;

    [Header("Chain & Slash Hover FX (MainMenu)")]
    [SerializeField] private bool enableSlashHoverFX = true;
    [SerializeField] private float hoverSlideX = 18f;
    [SerializeField] private float hoverPlateAlpha = 0.68f;
    [SerializeField] private int hoverSparkCount = 5;

    [Header("Audio Settings (Optional)")]
    [SerializeField] private AudioClip hoverSFX;
    [SerializeField] private AudioClip clickSFX;

    #endregion

    #region Private State

    private Transform cachedTransform;
    private RectTransform cachedRect;
    private Vector3 originalScale;
    private Vector3 targetScale;
    private bool isAnimating;
    private bool isHovered;
    private bool isSuppressed;
    private Vector2 currentHoverOffset;
    private Vector2 targetHoverOffset;

    private MainMenu cachedMainMenu;
    private GameOverPanel cachedGameOverPanel;

    // Procedural Chain & Slash hover visuals (created only on MainMenu buttons)
    private bool hasBuiltSlashVisuals;
    private Image hoverPlateImg;
    private RectTransform hoverBladeRect;
    private Image hoverBladeImg;
    private RectTransform leftHookRect;
    private Image leftHookImg;
    private RectTransform rightHookRect;
    private Image rightHookImg;
    private float currentFxBlend;
    private float targetFxBlend;
    private readonly List<ButtonSpark> activeSparks = new List<ButtonSpark>();

    #endregion

    #region Public Properties & Accessors

    public Vector2 HoverAnchoredOffset => SnapToUiPixelGrid(currentHoverOffset);

    internal static Vector2 SnapToUiPixelGrid(Vector2 rawPos)
    {
        return new Vector2(
            Mathf.Round(rawPos.x / UiPixelGridStep) * UiPixelGridStep,
            Mathf.Round(rawPos.y / UiPixelGridStep) * UiPixelGridStep
        );
    }

    public void SetTransitionSuppressed(bool suppressed)
    {
        isSuppressed = suppressed;
        if (suppressed)
        {
            isHovered = false;
            targetScale = originalScale;
            targetHoverOffset = Vector2.zero;
            targetFxBlend = 0f;
            isAnimating = true;
        }
    }

    private MainMenu MainMenuRef
    {
        get
        {
            if (cachedMainMenu == null)
            {
                cachedMainMenu = UnityEngine.Object.FindFirstObjectByType<MainMenu>();
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
                    : UnityEngine.Object.FindFirstObjectByType<GameOverPanel>();
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
        cachedRect = transform as RectTransform;
        originalScale = cachedTransform.localScale;
        targetScale = originalScale;
        cachedMainMenu = UnityEngine.Object.FindFirstObjectByType<MainMenu>();
        cachedGameOverPanel = GameOverPanel.Instance != null
            ? GameOverPanel.Instance
            : UnityEngine.Object.FindFirstObjectByType<GameOverPanel>();

        if (ShouldUseSlashHoverFX())
        {
            BuildSlashHoverVisuals();
        }
    }

    private void OnEnable()
    {
        if (cachedTransform == null) cachedTransform = transform;
        cachedTransform.localScale = originalScale;
        targetScale = originalScale;
        currentHoverOffset = Vector2.zero;
        targetHoverOffset = Vector2.zero;
        currentFxBlend = 0f;
        targetFxBlend = 0f;
        isHovered = false;
        isSuppressed = false;
        isAnimating = false;
        ApplyHoverVisualBlend(0f);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (activeSparks.Count > 0)
        {
            UpdateActiveSparks(dt);
        }

        if (!isAnimating) return;

        bool keepAnimating = false;

        Vector3 currentScale = cachedTransform.localScale;
        if ((currentScale - targetScale).sqrMagnitude > ScaleSnapThresholdSqr)
        {
            Vector3 nextScale = Vector3.Lerp(currentScale, targetScale, dt * animationSpeed);
            if ((nextScale - targetScale).sqrMagnitude <= ScaleSnapThresholdSqr)
            {
                cachedTransform.localScale = targetScale;
            }
            else
            {
                cachedTransform.localScale = nextScale;
                keepAnimating = true;
            }
        }
        else
        {
            cachedTransform.localScale = targetScale;
        }

        if ((currentHoverOffset - targetHoverOffset).sqrMagnitude > HoverOffsetSnapThresholdSqr)
        {
            currentHoverOffset = Vector2.Lerp(currentHoverOffset, targetHoverOffset, dt * (animationSpeed * 1.15f));
            keepAnimating = true;
        }
        else
        {
            currentHoverOffset = targetHoverOffset;
        }

        if (hasBuiltSlashVisuals)
        {
            if (Mathf.Abs(currentFxBlend - targetFxBlend) > 0.004f)
            {
                currentFxBlend = Mathf.MoveTowards(currentFxBlend, targetFxBlend, dt * 6.5f);
                ApplyHoverVisualBlend(currentFxBlend);
                keepAnimating = true;
            }
            else if (!Mathf.Approximately(currentFxBlend, targetFxBlend))
            {
                currentFxBlend = targetFxBlend;
                ApplyHoverVisualBlend(currentFxBlend);
            }
        }

        isAnimating = keepAnimating;
    }

    #endregion

    #region Pointer Event Handlers

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isSuppressed) return;

        isHovered = true;
        bool useSlashFX = ShouldUseSlashHoverFX();
        float appliedHoverScale = useSlashFX ? Mathf.Min(hoverScale, 1.07f) : hoverScale;
        SetTargetState(originalScale * appliedHoverScale, useSlashFX ? new Vector2(hoverSlideX, 0f) : Vector2.zero, 1f);

        if (useSlashFX)
        {
            SpawnHoverSparks(hoverSparkCount, 125f);
            if (MainMenuRef != null && IsStartButton())
            {
                MainMenuRef.NotifyStartButtonHovered();
            }
        }

        PlayHoverAudio();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (isSuppressed) return;

        isHovered = false;
        SetTargetState(originalScale, Vector2.zero, 0f);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (isSuppressed) return;

        bool useSlashFX = ShouldUseSlashHoverFX();
        Vector2 pressOffset = useSlashFX && isHovered ? new Vector2(hoverSlideX * 0.65f, 0f) : Vector2.zero;
        SetTargetState(originalScale * clickScale, pressOffset, isHovered ? 1f : 0f);

        if (useSlashFX)
        {
            SpawnHoverSparks(hoverSparkCount + 2, 165f);
        }

        // Keep StartButton silent on click so the single SwordSlashSFX owns the transition cue cleanly
        if (!IsStartButton() || MainMenuRef == null)
        {
            PlayClickAudio();
        }
    }

    #endregion

    #region Chain & Slash Visual Helpers

    private bool ShouldUseSlashHoverFX()
    {
        return enableSlashHoverFX && MainMenuRef != null && GameOverPanelRef == null && cachedRect != null;
    }

    private bool IsStartButton()
    {
        return gameObject.name.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void SetTargetState(Vector3 newScale, Vector2 newHoverOffset, float fxBlend)
    {
        targetScale = newScale;
        targetHoverOffset = newHoverOffset;
        targetFxBlend = hasBuiltSlashVisuals ? fxBlend : 0f;
        isAnimating = true;
    }

    private void BuildSlashHoverVisuals()
    {
        if (hasBuiltSlashVisuals || cachedRect == null) return;

        Sprite whitePixel = GetWhitePixelSprite();

        // Pixelated chamfered dark blade plaque behind button label
        GameObject plateObj = new GameObject("SlashHoverPlate");
        plateObj.transform.SetParent(cachedRect, false);
        plateObj.transform.SetAsFirstSibling();
        RectTransform plateRect = plateObj.AddComponent<RectTransform>();
        plateRect.anchorMin = new Vector2(0.02f, 0.04f);
        plateRect.anchorMax = new Vector2(0.98f, 0.96f);
        plateRect.offsetMin = Vector2.zero;
        plateRect.offsetMax = Vector2.zero;
        hoverPlateImg = plateObj.AddComponent<Image>();
        hoverPlateImg.sprite = GetPixelPlateSprite();
        hoverPlateImg.raycastTarget = false;
        hoverPlateImg.color = new Color(1f, 1f, 1f, 0f);

        // Chunky pixel-art white katana slash bar sweeping along bottom edge on hover
        GameObject bladeObj = new GameObject("SlashHoverBlade");
        bladeObj.transform.SetParent(plateRect, false);
        hoverBladeRect = bladeObj.AddComponent<RectTransform>();
        hoverBladeRect.anchorMin = new Vector2(0f, 0f);
        hoverBladeRect.anchorMax = new Vector2(1f, 0f);
        hoverBladeRect.pivot = new Vector2(0f, 0.5f);
        hoverBladeRect.anchoredPosition = new Vector2(0f, 2f);
        hoverBladeRect.sizeDelta = new Vector2(0f, 4f);
        hoverBladeImg = bladeObj.AddComponent<Image>();
        hoverBladeImg.sprite = whitePixel;
        hoverBladeImg.raycastTarget = false;
        hoverBladeImg.color = new Color(1f, 1f, 1f, 0f);

        // Symmetrical unrotated pixel-art diamond chain hooks at left & right edges
        leftHookImg = CreateHookDiamond(plateRect, "HoverHookLeft", out leftHookRect, new Vector2(0f, 0.5f), new Vector2(16f, 0f));
        rightHookImg = CreateHookDiamond(plateRect, "HoverHookRight", out rightHookRect, new Vector2(1f, 0.5f), new Vector2(-16f, 0f));

        hasBuiltSlashVisuals = true;
        ApplyHoverVisualBlend(0f);
    }

    private static Image CreateHookDiamond(RectTransform parent, string name, out RectTransform rt, Vector2 anchor, Vector2 anchoredPos)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = SnapToUiPixelGrid(anchoredPos);
        rt.sizeDelta = new Vector2(16f, 16f);
        Image img = obj.AddComponent<Image>();
        img.sprite = GetPixelDiamondSprite();
        img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, 0f);
        return img;
    }

    private void ApplyHoverVisualBlend(float t)
    {
        if (!hasBuiltSlashVisuals) return;

        float stepAlpha = EnemyBase.QuantizeStep(t, 4);
        float stepWipe = EnemyBase.QuantizeStep(t, 6);

        if (hoverPlateImg != null)
        {
            hoverPlateImg.color = new Color(1f, 1f, 1f, stepAlpha * Mathf.Max(0.75f, hoverPlateAlpha));
        }

        if (hoverBladeRect != null && hoverBladeImg != null)
        {
            hoverBladeRect.localScale = new Vector3(Mathf.Max(0.001f, stepWipe), 1f, 1f);
            hoverBladeImg.color = new Color(1f, 1f, 1f, stepAlpha);
        }

        float hookX = Mathf.Round(Mathf.Lerp(-8f, 16f, stepWipe) / UiPixelGridStep) * UiPixelGridStep;
        if (leftHookRect != null && leftHookImg != null)
        {
            leftHookRect.anchoredPosition = new Vector2(hookX, 0f);
            leftHookImg.color = new Color(1f, 1f, 1f, stepAlpha);
        }

        if (rightHookRect != null && rightHookImg != null)
        {
            rightHookRect.anchoredPosition = new Vector2(-hookX, 0f);
            rightHookImg.color = new Color(1f, 1f, 1f, stepAlpha);
        }
    }

    private void SpawnHoverSparks(int count, float maxSpeed)
    {
        if (cachedRect == null || count <= 0) return;

        Sprite pixelSprite = GetWhitePixelSprite();
        float halfW = cachedRect.rect.width * 0.42f;
        float halfH = cachedRect.rect.height * 0.25f;

        for (int i = 0; i < count; i++)
        {
            GameObject sparkObj = new GameObject("HoverPixelSpark");
            sparkObj.transform.SetParent(cachedRect, false);
            RectTransform rt = sparkObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            float size = (i % 2 == 0) ? 4f : 8f;
            rt.sizeDelta = new Vector2(size, size);

            bool spawnLeft = (i % 2 == 0);
            float startX = spawnLeft ? UnityEngine.Random.Range(-halfW, -halfW * 0.55f) : UnityEngine.Random.Range(halfW * 0.55f, halfW);
            float startY = UnityEngine.Random.Range(-halfH, halfH);
            Vector2 startPos = SnapToUiPixelGrid(new Vector2(startX, startY));
            rt.anchoredPosition = startPos;

            Image img = sparkObj.AddComponent<Image>();
            img.sprite = pixelSprite;
            img.raycastTarget = false;
            img.color = Color.white;

            float dirX = spawnLeft ? UnityEngine.Random.Range(-1f, -0.25f) : UnityEngine.Random.Range(0.25f, 1f);
            float dirY = UnityEngine.Random.Range(-0.7f, 0.9f);
            Vector2 vel = new Vector2(dirX, dirY).normalized * UnityEngine.Random.Range(maxSpeed * 0.5f, maxSpeed);

            activeSparks.Add(new ButtonSpark
            {
                Rect = rt,
                Image = img,
                Position = startPos,
                Velocity = vel,
                Elapsed = 0f,
                Duration = UnityEngine.Random.Range(0.16f, 0.28f)
            });
        }
    }

    private void UpdateActiveSparks(float dt)
    {
        for (int i = activeSparks.Count - 1; i >= 0; i--)
        {
            ButtonSpark spark = activeSparks[i];
            if (spark.Rect == null || spark.Image == null)
            {
                activeSparks.RemoveAt(i);
                continue;
            }

            spark.Elapsed += dt;
            float t = Mathf.Clamp01(spark.Elapsed / spark.Duration);

            spark.Position += spark.Velocity * dt;
            spark.Rect.anchoredPosition = SnapToUiPixelGrid(spark.Position);
            spark.Velocity *= Mathf.Clamp01(1f - 5.5f * dt);

            float alpha = 1f - EnemyBase.QuantizeStep(t, 4);
            spark.Image.color = new Color(1f, 1f, 1f, alpha);

            if (t >= 1f)
            {
                Destroy(spark.Rect.gameObject);
                activeSparks.RemoveAt(i);
            }
            else
            {
                activeSparks[i] = spark;
            }
        }
    }

    internal static Sprite GetWhitePixelSprite()
    {
        if (cachedWhitePixelSprite != null) return cachedWhitePixelSprite;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        cachedWhitePixelSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return cachedWhitePixelSprite;
    }

    private static Sprite GetPixelPlateSprite()
    {
        if (cachedPixelPlateSprite != null) return cachedPixelPlateSprite;

        const int w = 120;
        const int h = 28;
        const int chamfer = 3;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color inkFill = new Color(0.05f, 0.05f, 0.09f, 0.90f);
        Color ditherFill = new Color(0.05f, 0.05f, 0.09f, 0.55f);
        Color whiteRim = new Color(0.92f, 0.94f, 0.98f, 0.85f);
        Color[] px = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Top-left & bottom-right pixel staircase chamfers
                int tlDist = x + (h - 1 - y);
                int brDist = (w - 1 - x) + y;
                if (tlDist < chamfer || brDist < chamfer)
                {
                    px[y * w + x] = clear;
                    continue;
                }

                bool isBorder = x == 0 || x == w - 1 || y == 0 || y == h - 1 || tlDist == chamfer || brDist == chamfer;
                if (isBorder)
                {
                    px[y * w + x] = whiteRim;
                }
                else if (x < 6 || x > w - 7)
                {
                    px[y * w + x] = ((x + y) % 2 == 0) ? inkFill : ditherFill;
                }
                else
                {
                    px[y * w + x] = inkFill;
                }
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        cachedPixelPlateSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
        return cachedPixelPlateSprite;
    }

    internal static Sprite GetPixelDiamondSprite()
    {
        if (cachedPixelDiamondSprite != null) return cachedPixelDiamondSprite;

        const int s = 9;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] px = new Color[s * s];
        int half = s / 2;

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                int manhattan = Mathf.Abs(x - half) + Mathf.Abs(y - half);
                px[y * s + x] = manhattan <= half ? Color.white : clear;
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        cachedPixelDiamondSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 1f);
        return cachedPixelDiamondSprite;
    }

    #endregion

    #region UI & Audio Logic

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
