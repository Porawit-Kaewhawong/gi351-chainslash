using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossSlashDashVFX : MonoBehaviour
{
    #region Serialized Fields

    [Header("Pixel Slash Dash VFX Settings")]
    [SerializeField] private float duration = 0.38f;
    [SerializeField] private Color bladeCoreColor = Color.white;
    [SerializeField] private Color bladeColor = new Color(1f, 0.12f, 0.22f, 1f);
    [SerializeField] private Color streakColor = new Color(1f, 0.55f, 0.25f, 0.9f);

    #endregion

    #region Constants

    private const float FrameInterval = 1f / 16f; // Retro 16 FPS frame stepping
    private const float PixelsPerUnit = 16f;

    private static readonly float[] StreakYOffsets = { -0.3f, -0.1f, 0.1f, 0.3f };
    private static readonly Vector3 InitialBladeScale = new Vector3(0.75f, 0.95f, 1f);
    private static readonly Vector3 TargetBladeScale = new Vector3(1.15f, 1.35f, 1f);
    private static readonly Vector3 DefaultFollowOffset = new Vector3(0.45f, 0.55f, 0f);

    #endregion

    #region Private State

    private SpriteRenderer crescentBladeRenderer;
    private Transform crescentBladeTransform;
    private readonly List<SpriteRenderer> speedStreaks = new List<SpriteRenderer>(4);
    private Transform followTarget;
    private Vector3 followOffset;
    private Vector2 travelVelocity;

    private static Sprite cachedCrescentSprite;
    private static Sprite cachedTintableCrescentSprite;
    private static Sprite cachedDashedBarSprite;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        SetupPixelComponents();
    }

    private void Start()
    {
        StartCoroutine(PixelSlashDashRoutine());
    }

    #endregion

    #region Public API

    public void SetFollowTarget(Transform target, Vector3 offset = default)
    {
        followTarget = target;
        followOffset = offset;
        UpdateFollowPosition();
    }

    public static GameObject Spawn(Vector3 position, Vector2 direction, Transform followTarget = null)
    {
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Quaternion rot = Quaternion.Euler(0f, 0f, angle);

        GameObject vfx = new GameObject("BossSlashDashVFX");
        vfx.transform.position = position;
        vfx.transform.rotation = rot;
        BossSlashDashVFX comp = vfx.AddComponent<BossSlashDashVFX>();
        if (followTarget != null)
        {
            comp.SetFollowTarget(followTarget, DefaultFollowOffset);
        }
        return vfx;
    }

    public void ConfigureAsTravelingWave(Vector2 velocity, float waveDuration, Color coreTint, Color waveTint, Color trailTint)
    {
        travelVelocity = velocity;
        duration = waveDuration;
        bladeCoreColor = coreTint;
        bladeColor = waveTint;
        streakColor = trailTint;

        if (crescentBladeRenderer != null)
        {
            crescentBladeRenderer.sprite = GetOrCreateTintableCrescentSprite();
            crescentBladeRenderer.color = bladeCoreColor;
        }

        for (int i = 0; i < speedStreaks.Count; i++)
        {
            if (speedStreaks[i] != null)
            {
                speedStreaks[i].color = streakColor;
            }
        }
    }

    public static GameObject SpawnTravelingWindCrescent(Vector3 startPos, Vector2 direction, float speed, float waveDuration, Color windColor, Color coreColor)
    {
        Vector2 dirNorm = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        float angle = Mathf.Atan2(dirNorm.y, dirNorm.x) * Mathf.Rad2Deg;
        Quaternion rot = Quaternion.Euler(0f, 0f, angle);

        GameObject vfx = new GameObject("BossWindCrescentWaveVFX");
        vfx.transform.position = startPos;
        vfx.transform.rotation = rot;

        BossSlashDashVFX comp = vfx.AddComponent<BossSlashDashVFX>();
        Color streakTint = Color.Lerp(windColor, Color.white, 0.35f);
        comp.ConfigureAsTravelingWave(dirNorm * speed, waveDuration, coreColor, windColor, streakTint);
        return vfx;
    }

    // --- Procedural Pixel Art Sprites with FilterMode.Point ---

    public static Sprite GetOrCreateTintableCrescentSprite()
    {
        if (cachedTintableCrescentSprite != null) return cachedTintableCrescentSprite;

        const int width = 24;
        const int height = 36;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color core = Color.white;
        Color body = new Color(0.85f, 0.95f, 0.92f, 0.95f);
        Color border = new Color(0.55f, 0.78f, 0.72f, 0.8f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                tex.SetPixel(x, y, clear);
            }
        }

        const float midY = height * 0.5f;

        for (int y = 2; y < height - 2; y++)
        {
            float ny = (y - midY) / midY;
            float curveX = (1f - (ny * ny)) * (width * 0.75f) + 3f;
            int arcX = Mathf.RoundToInt(curveX);

            SetPixel(tex, arcX, y, core);
            SetPixel(tex, arcX - 1, y, core);
            SetPixel(tex, arcX - 2, y, body);
            SetPixel(tex, arcX - 3, y, body);
            SetPixel(tex, arcX + 1, y, border);
            SetPixel(tex, arcX - 4, y, border);
        }

        tex.Apply();
        cachedTintableCrescentSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        return cachedTintableCrescentSprite;
    }

    public static Sprite GetOrCreatePixelCrescentSprite()
    {
        if (cachedCrescentSprite != null) return cachedCrescentSprite;

        const int width = 24;
        const int height = 36;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0);
        Color core = Color.white;
        Color blade = new Color(1f, 0.15f, 0.25f, 1f);
        Color border = new Color(0.6f, 0f, 0.1f, 1f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                tex.SetPixel(x, y, clear);
            }
        }

        const float midY = height * 0.5f;

        // Draw chunky pixel crescent arc curving to the right (+X)
        for (int y = 2; y < height - 2; y++)
        {
            float ny = (y - midY) / midY; // -1 to 1
            float curveX = (1f - (ny * ny)) * (width * 0.75f) + 3f;
            int arcX = Mathf.RoundToInt(curveX);

            // Leading tip (white)
            SetPixel(tex, arcX, y, core);
            SetPixel(tex, arcX - 1, y, core);

            // Body (crimson)
            SetPixel(tex, arcX - 2, y, blade);
            SetPixel(tex, arcX - 3, y, blade);

            // Border (ruby dark outline)
            SetPixel(tex, arcX + 1, y, border);
            SetPixel(tex, arcX - 4, y, border);
        }

        tex.Apply();
        cachedCrescentSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        return cachedCrescentSprite;
    }

    public static Sprite GetOrCreatePixelDashedBarSprite()
    {
        if (cachedDashedBarSprite != null) return cachedDashedBarSprite;

        const int width = 32;
        const int height = 4;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0);
        Color solid = Color.white;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Alternating pixel dashes: 5 on, 3 off
                bool on = (x % 8) < 5;
                tex.SetPixel(x, y, on ? solid : clear);
            }
        }

        tex.Apply();
        cachedDashedBarSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(1f, 0.5f), PixelsPerUnit);
        return cachedDashedBarSprite;
    }

    #endregion

    #region VFX / Rendering Logic

    private void SetupPixelComponents()
    {
        // 1. Chunky Pixel Crescent Blade (facing local +X) - Compact, sleek scale
        GameObject bladeObj = new GameObject("PixelCrescentBlade");
        crescentBladeTransform = bladeObj.transform;
        crescentBladeTransform.SetParent(transform, false);
        crescentBladeTransform.localPosition = new Vector3(0.35f, 0f, 0f);
        crescentBladeTransform.localScale = new Vector3(0.85f, 1.1f, 1f);

        crescentBladeRenderer = bladeObj.AddComponent<SpriteRenderer>();
        crescentBladeRenderer.sprite = GetOrCreatePixelCrescentSprite();
        crescentBladeRenderer.color = bladeCoreColor;
        crescentBladeRenderer.sortingOrder = 16;

        // 2. Trailing Pixel Speed Lines (Dashed Pixel Bars running back along -X) - Sleek & compact
        Sprite barSprite = GetOrCreatePixelDashedBarSprite();
        for (int i = 0; i < StreakYOffsets.Length; i++)
        {
            GameObject streakObj = new GameObject($"PixelStreak_{i}");
            Transform streakTransform = streakObj.transform;
            streakTransform.SetParent(transform, false);
            streakTransform.localPosition = new Vector3(-0.35f - (i * 0.12f), StreakYOffsets[i], 0f);
            streakTransform.localScale = new Vector3(Random.Range(1.1f, 1.8f), 0.7f, 1f);

            SpriteRenderer sr = streakObj.AddComponent<SpriteRenderer>();
            sr.sprite = barSprite;
            sr.color = streakColor;
            sr.sortingOrder = 15;

            speedStreaks.Add(sr);
        }
    }

    private IEnumerator PixelSlashDashRoutine()
    {
        float timer = 0f;
        float frameTimer = 0f;

        while (timer < duration)
        {
            float dt = Time.deltaTime;
            timer += dt;
            frameTimer += dt;

            UpdateFollowPosition();

            float t = Mathf.Clamp01(timer / duration);
            float easeOut = 1f - Mathf.Pow(1f - t, 2.5f);

            if (frameTimer >= FrameInterval)
            {
                frameTimer = 0f;

                // Stepped blade expansion & forward surge
                if (crescentBladeRenderer != null)
                {
                    float steppedEase = Mathf.Floor(easeOut * 4f) / 4f;
                    crescentBladeTransform.localScale = Vector3.Lerp(InitialBladeScale, TargetBladeScale, steppedEase);
                    crescentBladeTransform.localPosition = new Vector3(Mathf.Lerp(0.35f, 0.85f, steppedEase), 0f, 0f);

                    // Flash from white to crimson then fade
                    if (t < 0.25f)
                    {
                        crescentBladeRenderer.color = bladeCoreColor;
                    }
                    else
                    {
                        Color c = bladeColor;
                        c.a = Mathf.Clamp01(1f - (t * 1.4f));
                        crescentBladeRenderer.color = c;
                    }
                }

                // Stepped trailing streak fadeout
                Color sc = streakColor;
                sc.a = Mathf.Clamp01(1f - (t * 1.6f));
                for (int i = 0; i < speedStreaks.Count; i++)
                {
                    if (speedStreaks[i] != null)
                    {
                        speedStreaks[i].color = sc;
                    }
                }
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    #endregion

    #region Helpers

    private void UpdateFollowPosition()
    {
        if (followTarget != null)
        {
            Vector3 center = followTarget.position + new Vector3(0f, followOffset.y, 0f);
            Vector3 forward = transform.rotation * new Vector3(followOffset.x, 0f, 0f);
            transform.position = center + forward;
        }
        else if (travelVelocity.sqrMagnitude > 0.0001f)
        {
            transform.position += (Vector3)(travelVelocity * Time.deltaTime);
        }
    }

    private static void SetPixel(Texture2D tex, int x, int y, Color c)
    {
        if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
        {
            tex.SetPixel(x, y, c);
        }
    }

    #endregion
}
