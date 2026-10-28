using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossSlashDashVFX : MonoBehaviour
{
    [Header("Pixel Slash Dash VFX Settings")]
    [SerializeField] private float duration = 0.38f;
    [SerializeField] private Color bladeCoreColor = Color.white;
    [SerializeField] private Color bladeColor = new Color(1f, 0.12f, 0.22f, 1f);
    [SerializeField] private Color streakColor = new Color(1f, 0.55f, 0.25f, 0.9f);

    private SpriteRenderer crescentBladeRenderer;
    private readonly List<SpriteRenderer> speedStreaks = new List<SpriteRenderer>();
    private Transform followTarget;
    private Vector3 followOffset;

    public void SetFollowTarget(Transform target, Vector3 offset = default)
    {
        followTarget = target;
        followOffset = offset;
        UpdateFollowPosition();
    }

    private void UpdateFollowPosition()
    {
        if (followTarget != null)
        {
            Vector3 center = followTarget.position + new Vector3(0f, followOffset.y, 0f);
            Vector3 forward = transform.rotation * new Vector3(followOffset.x, 0f, 0f);
            transform.position = center + forward;
        }
    }

    private void Awake()
    {
        SetupPixelComponents();
    }

    private void Start()
    {
        StartCoroutine(PixelSlashDashRoutine());
    }

    private void SetupPixelComponents()
    {
        // 1. Chunky Pixel Crescent Blade (facing local +X) - Compact, sleek scale
        GameObject bladeObj = new GameObject("PixelCrescentBlade");
        bladeObj.transform.SetParent(transform, false);
        bladeObj.transform.localPosition = new Vector3(0.35f, 0f, 0f);
        bladeObj.transform.localScale = new Vector3(0.85f, 1.1f, 1f);

        crescentBladeRenderer = bladeObj.AddComponent<SpriteRenderer>();
        crescentBladeRenderer.sprite = GetOrCreatePixelCrescentSprite();
        crescentBladeRenderer.color = bladeCoreColor;
        crescentBladeRenderer.sortingOrder = 16;

        // 2. Trailing Pixel Speed Lines (Dashed Pixel Bars running back along -X) - Sleek & compact
        Sprite barSprite = GetOrCreatePixelDashedBarSprite();
        float[] yOffsets = new float[] { -0.3f, -0.1f, 0.1f, 0.3f };
        for (int i = 0; i < yOffsets.Length; i++)
        {
            GameObject streakObj = new GameObject($"PixelStreak_{i}");
            streakObj.transform.SetParent(transform, false);
            streakObj.transform.localPosition = new Vector3(-0.35f - (i * 0.12f), yOffsets[i], 0f);
            streakObj.transform.localScale = new Vector3(Random.Range(1.1f, 1.8f), 0.7f, 1f);

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
        float frameInterval = 1f / 16f; // Retro 16 FPS frame stepping
        float frameTimer = 0f;

        Vector3 initialBladeScale = new Vector3(0.75f, 0.95f, 1f);
        Vector3 targetBladeScale = new Vector3(1.15f, 1.35f, 1f);

        while (timer < duration)
        {
            timer += Time.deltaTime;
            frameTimer += Time.deltaTime;

            UpdateFollowPosition();

            float t = Mathf.Clamp01(timer / duration);
            float easeOut = 1f - Mathf.Pow(1f - t, 2.5f);

            if (frameTimer >= frameInterval)
            {
                frameTimer = 0f;

                // Stepped blade expansion & forward surge
                if (crescentBladeRenderer != null)
                {
                    float steppedEase = Mathf.Floor(easeOut * 4f) / 4f;
                    crescentBladeRenderer.transform.localScale = Vector3.Lerp(initialBladeScale, targetBladeScale, steppedEase);
                    crescentBladeRenderer.transform.localPosition = new Vector3(Mathf.Lerp(0.35f, 0.85f, steppedEase), 0f, 0f);

                    // Flash from white to crimson then fade
                    if (t < 0.25f) crescentBladeRenderer.color = bladeCoreColor;
                    else
                    {
                        Color c = bladeColor;
                        c.a = Mathf.Clamp01(1f - (t * 1.4f));
                        crescentBladeRenderer.color = c;
                    }
                }

                // Stepped trailing streak fadeout
                for (int i = 0; i < speedStreaks.Count; i++)
                {
                    if (speedStreaks[i] != null)
                    {
                        Color sc = streakColor;
                        sc.a = Mathf.Clamp01(1f - (t * 1.6f));
                        speedStreaks[i].color = sc;
                    }
                }
            }

            yield return null;
        }

        Destroy(gameObject);
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
            comp.SetFollowTarget(followTarget, new Vector3(0.45f, 0.55f, 0f));
        }
        return vfx;
    }

    // --- Procedural Pixel Art Sprites with FilterMode.Point ---

    private static Sprite cachedCrescentSprite;
    public static Sprite GetOrCreatePixelCrescentSprite()
    {
        if (cachedCrescentSprite != null) return cachedCrescentSprite;

        int width = 24;
        int height = 36;
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

        float midY = height * 0.5f;

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
        cachedCrescentSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 16f);
        return cachedCrescentSprite;
    }

    private static Sprite cachedDashedBarSprite;
    public static Sprite GetOrCreatePixelDashedBarSprite()
    {
        if (cachedDashedBarSprite != null) return cachedDashedBarSprite;

        int width = 32;
        int height = 4;
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
        cachedDashedBarSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(1f, 0.5f), 16f);
        return cachedDashedBarSprite;
    }

    private static void SetPixel(Texture2D tex, int x, int y, Color c)
    {
        if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
        {
            tex.SetPixel(x, y, c);
        }
    }
}

