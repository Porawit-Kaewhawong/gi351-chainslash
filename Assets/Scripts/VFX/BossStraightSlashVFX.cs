using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pixel-art Straight-Line Slash VFX for Legendary Samurai.
/// Spawns a razor-sharp, blazing straight-line blade beam in the direction of the slash,
/// complete with parallel speed wisps, bursting pixel sparks, and retro 16 FPS stepped animation.
/// </summary>
public class BossStraightSlashVFX : MonoBehaviour
{
    #region Serialized Fields

    [Header("Pixel Straight Slash Settings")]
    [SerializeField] private float duration = 0.32f;
    [SerializeField] private float slashLength = 6.0f;
    [SerializeField] private Color coreColor = Color.white;
    [SerializeField] private Color bladeColor = new Color(1.0f, 0.12f, 0.24f, 1.0f);   // Crimson Ruby
    [SerializeField] private Color outerColor = new Color(0.65f, 0.0f, 0.12f, 0.95f);  // Dark Obsidian Wine
    [SerializeField] private Color streakColor = new Color(1.0f, 0.65f, 0.2f, 0.9f);   // Amber Spark Streak

    #endregion

    #region Constants

    private const int SparkCount = 8;
    private const float FrameInterval = 1f / 16f; // Retro 16 FPS stepped feel
    private const float PixelsPerUnit = 16f;

    private static readonly float[] StreakYOffsets = { -0.35f, -0.18f, 0.18f, 0.35f };
    private static readonly Color SecondarySparkColor = new Color(1f, 0.45f, 0.15f, 1f);

    #endregion

    #region Private State

    private SpriteRenderer mainBladeRenderer;
    private Transform mainBladeTransform;
    private readonly List<SpriteRenderer> flankStreaks = new List<SpriteRenderer>(4);
    private readonly List<Transform> sparkTransforms = new List<Transform>(SparkCount);
    private readonly List<Vector2> sparkVelocities = new List<Vector2>(SparkCount);

    private readonly List<SpriteRenderer> sparkRenderers = new List<SpriteRenderer>(SparkCount);

    private static Sprite cachedStraightBladeSprite;
    private static Sprite cachedTintableBladeSprite;
    private static Sprite cachedSparkSprite;
    private static Sprite cachedStreakBarSprite;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        SetupComponents();
    }

    private void Start()
    {
        StartCoroutine(SlashAnimationRoutine());
    }

    #endregion

    #region Public API

    public void SetSlashLength(float length)
    {
        slashLength = Mathf.Max(2f, length);
        if (mainBladeTransform != null)
        {
            mainBladeTransform.localScale = new Vector3(slashLength / 4.0f, 1.2f, 1f);
            mainBladeTransform.localPosition = new Vector3(slashLength * 0.5f, 0f, 0f);
        }
    }

    public void SetCustomColors(Color customCore, Color customBlade, Color customOuter, Color customStreak)
    {
        coreColor = customCore;
        bladeColor = customBlade;
        outerColor = customOuter;
        streakColor = customStreak;

        if (mainBladeRenderer != null)
        {
            mainBladeRenderer.sprite = GetOrCreateTintableBladeSprite();
            mainBladeRenderer.color = coreColor;
        }

        for (int i = 0; i < flankStreaks.Count; i++)
        {
            if (flankStreaks[i] != null)
            {
                flankStreaks[i].color = streakColor;
            }
        }

        for (int i = 0; i < sparkRenderers.Count; i++)
        {
            if (sparkRenderers[i] != null)
            {
                sparkRenderers[i].color = (i % 2 == 0) ? customCore : customStreak;
            }
        }
    }

    /// <summary>
    /// Factory helper to spawn and orient a pixel straight-line slash VFX.
    /// </summary>
    public static BossStraightSlashVFX Spawn(Vector3 startPos, Vector2 direction, float length = 6.0f)
    {
        GameObject vfxObj = new GameObject("BossStraightSlashVFX");
        vfxObj.transform.position = startPos;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        vfxObj.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        BossStraightSlashVFX comp = vfxObj.AddComponent<BossStraightSlashVFX>();
        comp.SetSlashLength(length);
        return comp;
    }

    /// <summary>
    /// Factory helper to spawn an emerald/cyan Wind Slash beam centered along a cut segment.
    /// </summary>
    public static BossStraightSlashVFX SpawnWindSlash(Vector3 startPos, Vector2 direction, float length, Color windPrimary, Color windCore)
    {
        BossStraightSlashVFX comp = Spawn(startPos, direction, length);
        Color darkWind = new Color(windPrimary.r * 0.45f, windPrimary.g * 0.65f, windPrimary.b * 0.55f, 0.9f);
        Color brightStreak = Color.Lerp(windPrimary, Color.white, 0.4f);
        comp.SetCustomColors(windCore, windPrimary, darkWind, brightStreak);
        return comp;
    }

    // --- Procedural Pixel Art Sprites with Point Filtering ---

    public static Sprite GetOrCreateTintableBladeSprite()
    {
        if (cachedTintableBladeSprite != null) return cachedTintableBladeSprite;

        const int width = 64;
        const int height = 12;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color core = Color.white;
        Color mid = new Color(0.85f, 0.95f, 0.92f, 0.95f);
        Color edge = new Color(0.55f, 0.75f, 0.70f, 0.8f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                tex.SetPixel(x, y, clear);
            }
        }

        const int midY = height / 2;
        const float invWidth = 1f / width;

        for (int x = 0; x < width; x++)
        {
            float nx = x * invWidth;
            float thicknessFactor = Mathf.Sin(nx * Mathf.PI);
            int halfThick = Mathf.RoundToInt(thicknessFactor * (height * 0.45f));
            if (halfThick < 1 && x > 2 && x < width - 2) halfThick = 1;

            for (int dy = -halfThick; dy <= halfThick; dy++)
            {
                int y = midY + dy;
                if (y < 0 || y >= height) continue;

                int absDy = Mathf.Abs(dy);
                if (absDy == 0)
                {
                    tex.SetPixel(x, y, core);
                }
                else if (absDy == 1)
                {
                    tex.SetPixel(x, y, mid);
                }
                else
                {
                    tex.SetPixel(x, y, edge);
                }
            }
        }

        tex.Apply();
        cachedTintableBladeSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        return cachedTintableBladeSprite;
    }

    public static Sprite GetOrCreateStraightBladeSprite()
    {
        if (cachedStraightBladeSprite != null) return cachedStraightBladeSprite;

        const int width = 64;
        const int height = 12;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0);
        Color core = Color.white;
        Color blade = new Color(1f, 0.15f, 0.25f, 1f);
        Color border = new Color(0.55f, 0f, 0.1f, 1f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                tex.SetPixel(x, y, clear);
            }
        }

        const int midY = height / 2;
        const float invWidth = 1f / width;

        for (int x = 0; x < width; x++)
        {
            float nx = x * invWidth; // 0 (start) to 1 (tip)

            // Blade profile: thickest in center-front, tapering to a sharp needle tip at x == width
            float thicknessFactor = Mathf.Sin(nx * Mathf.PI);
            int halfThick = Mathf.RoundToInt(thicknessFactor * (height * 0.45f));
            if (halfThick < 1 && x > 2 && x < width - 2) halfThick = 1;

            for (int dy = -halfThick; dy <= halfThick; dy++)
            {
                int y = midY + dy;
                if (y < 0 || y >= height) continue;

                int absDy = Mathf.Abs(dy);
                if (absDy == 0)
                {
                    // Hot core white
                    tex.SetPixel(x, y, core);
                }
                else if (absDy == 1)
                {
                    // Crimson blade
                    tex.SetPixel(x, y, (x > width * 0.8f) ? core : blade);
                }
                else
                {
                    // Dark ruby border
                    tex.SetPixel(x, y, border);
                }
            }
        }

        tex.Apply();
        cachedStraightBladeSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        return cachedStraightBladeSprite;
    }

    public static Sprite GetOrCreateStreakBarSprite()
    {
        if (cachedStreakBarSprite != null) return cachedStreakBarSprite;

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
                bool on = (x % 7) < 4;
                tex.SetPixel(x, y, on ? solid : clear);
            }
        }

        tex.Apply();
        cachedStreakBarSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        return cachedStreakBarSprite;
    }

    public static Sprite GetOrCreateSparkSprite()
    {
        if (cachedSparkSprite != null) return cachedSparkSprite;

        const int size = 4;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, Color.white);
            }
        }
        tex.Apply();
        cachedSparkSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        return cachedSparkSprite;
    }

    #endregion

    #region VFX / Rendering Logic

    private void SetupComponents()
    {
        // 1. Central Straight Slash Beam (facing local +X)
        GameObject bladeObj = new GameObject("StraightSlashBlade");
        mainBladeTransform = bladeObj.transform;
        mainBladeTransform.SetParent(transform, false);
        mainBladeTransform.localPosition = new Vector3(slashLength * 0.5f, 0f, 0f);
        mainBladeTransform.localScale = new Vector3(slashLength / 4.0f, 1.2f, 1f);

        mainBladeRenderer = bladeObj.AddComponent<SpriteRenderer>();
        mainBladeRenderer.sprite = GetOrCreateStraightBladeSprite();
        mainBladeRenderer.color = coreColor;
        mainBladeRenderer.sortingOrder = 18;

        // 2. Parallel Pixel Flank Streaks
        Sprite streakSprite = GetOrCreateStreakBarSprite();
        for (int i = 0; i < StreakYOffsets.Length; i++)
        {
            GameObject streakObj = new GameObject($"FlankStreak_{i}");
            Transform streakTransform = streakObj.transform;
            streakTransform.SetParent(transform, false);

            float xOffset = Random.Range(slashLength * 0.15f, slashLength * 0.45f);
            streakTransform.localPosition = new Vector3(xOffset, StreakYOffsets[i], 0f);
            streakTransform.localScale = new Vector3(Random.Range(1.2f, 2.2f), 0.75f, 1f);

            SpriteRenderer sr = streakObj.AddComponent<SpriteRenderer>();
            sr.sprite = streakSprite;
            sr.color = streakColor;
            sr.sortingOrder = 17;
            flankStreaks.Add(sr);
        }

        // 3. Pixel Sparks bursting outward perpendicular to the slash cut
        Sprite sparkSprite = GetOrCreateSparkSprite();
        for (int i = 0; i < SparkCount; i++)
        {
            GameObject sparkObj = new GameObject($"Spark_{i}");
            Transform sparkTransform = sparkObj.transform;
            sparkTransform.SetParent(transform, false);

            float spawnX = Random.Range(slashLength * 0.2f, slashLength * 0.85f);
            sparkTransform.localPosition = new Vector3(spawnX, 0f, 0f);
            float scale = Random.Range(0.7f, 1.3f);
            sparkTransform.localScale = new Vector3(scale, scale, 1f);

            SpriteRenderer sr = sparkObj.AddComponent<SpriteRenderer>();
            sr.sprite = sparkSprite;
            bool isEven = (i % 2 == 0);
            sr.color = isEven ? Color.white : SecondarySparkColor;
            sr.sortingOrder = 19;

            sparkRenderers.Add(sr);
            sparkTransforms.Add(sparkTransform);

            // Velocity perpendicular to the slash (+Y or -Y with slight forward momentum)
            float side = isEven ? 1f : -1f;
            Vector2 vel = new Vector2(Random.Range(-0.8f, 2.5f), side * Random.Range(3.5f, 7.0f));
            sparkVelocities.Add(vel);
        }
    }

    private IEnumerator SlashAnimationRoutine()
    {
        float timer = 0f;
        float frameTimer = 0f;

        Vector3 initialScale = new Vector3(slashLength / 12.0f, 1.8f, 1f);
        Vector3 peakScale = new Vector3(slashLength / 4.0f, 1.25f, 1f);

        while (timer < duration)
        {
            float dt = Time.deltaTime;
            timer += dt;
            frameTimer += dt;

            float t = Mathf.Clamp01(timer / duration);

            // Animate flying pixel sparks
            for (int i = 0; i < sparkTransforms.Count; i++)
            {
                Transform sparkTransform = sparkTransforms[i];
                if (sparkTransform != null)
                {
                    sparkTransform.localPosition += (Vector3)(sparkVelocities[i] * dt);
                }
            }

            if (frameTimer >= FrameInterval)
            {
                frameTimer = 0f;

                // Stepped blade expansion & color fade
                if (mainBladeRenderer != null)
                {
                    if (t < 0.25f)
                    {
                        // White-hot ignite & surge forward
                        mainBladeTransform.localScale = Vector3.Lerp(initialScale, peakScale, t / 0.25f);
                        mainBladeRenderer.color = coreColor;
                    }
                    else if (t < 0.65f)
                    {
                        // Blazing crimson blade body
                        mainBladeTransform.localScale = peakScale;
                        float fade = 1f - ((t - 0.25f) / 0.4f);
                        Color c = Color.Lerp(outerColor, bladeColor, fade);
                        c.a = Mathf.Clamp01(fade * 1.2f);
                        mainBladeRenderer.color = c;
                    }
                    else
                    {
                        // Dissolving pixel slice
                        float fade = 1f - ((t - 0.65f) / 0.35f);
                        Color c = outerColor;
                        c.a = Mathf.Clamp01(fade);
                        mainBladeRenderer.color = c;
                        mainBladeTransform.localScale = new Vector3(peakScale.x * (1f + (1f - fade) * 0.15f), peakScale.y * fade, 1f);
                    }
                }

                // Fade flank streaks
                Color streakFadeColor = streakColor;
                streakFadeColor.a = Mathf.Clamp01(1f - t * 1.5f);
                for (int i = 0; i < flankStreaks.Count; i++)
                {
                    if (flankStreaks[i] != null)
                    {
                        flankStreaks[i].color = streakFadeColor;
                    }
                }
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    #endregion
}
