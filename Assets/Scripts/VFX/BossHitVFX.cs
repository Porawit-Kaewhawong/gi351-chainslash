using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossHitVFX : MonoBehaviour
{
    #region Serialized Fields

    [Header("Pixel Hit VFX Settings")]
    [SerializeField] private float duration = 0.28f;
    [SerializeField] private float maxRingScale = 3.2f;
    [SerializeField] private Color coreColor = Color.white;
    [SerializeField] private Color sparkColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color edgeColor = new Color(1f, 0.25f, 0.15f, 1f);

    #endregion

    #region Constants

    private const int DebrisCount = 8;
    // Stepped frame animation: 18 FPS
    private const float FrameInterval = 1f / 18f;
    private const float PixelsPerUnit = 16f;
    private static readonly Vector3 RingStartScale = Vector3.one * 0.4f;

    #endregion

    #region Private State

    private SpriteRenderer crossSlashRenderer;
    private Transform crossSlashTransform;
    private SpriteRenderer shockwaveRingRenderer;
    private Transform shockwaveRingTransform;
    private readonly List<Transform> pixelDebris = new List<Transform>(DebrisCount);
    private readonly List<SpriteRenderer> debrisRenderers = new List<SpriteRenderer>(DebrisCount);
    private readonly List<Vector2> debrisVelocities = new List<Vector2>(DebrisCount);

    private static Sprite cachedPixelSquare;
    private static Sprite cachedPixelCross;
    private static Sprite cachedPixelRing;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        SetupPixelComponents();
    }

    private void Start()
    {
        StartCoroutine(PixelHitRoutine());
    }

    #endregion

    #region Public API

    public static Vector3 SnapToPixel(Vector3 pos, float pixelsPerUnit)
    {
        return new Vector3(
            Mathf.Round(pos.x * pixelsPerUnit) / pixelsPerUnit,
            Mathf.Round(pos.y * pixelsPerUnit) / pixelsPerUnit,
            pos.z
        );
    }

    public static GameObject Spawn(Vector3 position, Quaternion rotation = default)
    {
        GameObject vfx = new GameObject("BossHitVFX");
        vfx.transform.position = position;
        vfx.transform.rotation = rotation;
        vfx.AddComponent<BossHitVFX>();
        return vfx;
    }

    // --- Procedural Pixel Art Sprites with FilterMode.Point ---

    public static Sprite GetOrCreatePixelSquareSprite()
    {
        if (cachedPixelSquare != null) return cachedPixelSquare;

        const int size = 4;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color solid = Color.white;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, solid);
            }
        }
        tex.Apply();
        cachedPixelSquare = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        return cachedPixelSquare;
    }

    public static Sprite GetOrCreatePixelCrossSprite()
    {
        if (cachedPixelCross != null) return cachedPixelCross;

        const int size = 24;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0);
        Color white = Color.white;
        Color border = new Color(1f, 0.5f, 0.1f, 1f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, clear);
            }
        }

        // Draw chunky pixel cross ("X") with 2-pixel width core and 1-pixel border
        for (int i = 2; i < size - 2; i++)
        {
            // Main diagonal
            SetPixelSafe(tex, i, i, white);
            SetPixelSafe(tex, i + 1, i, border);
            SetPixelSafe(tex, i - 1, i, border);

            // Anti diagonal
            int j = size - 1 - i;
            SetPixelSafe(tex, i, j, white);
            SetPixelSafe(tex, i + 1, j, border);
            SetPixelSafe(tex, i - 1, j, border);
        }

        tex.Apply();
        cachedPixelCross = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        return cachedPixelCross;
    }

    public static Sprite GetOrCreatePixelRingSprite()
    {
        if (cachedPixelRing != null) return cachedPixelRing;

        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0);
        Color solid = Color.white;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, clear);
            }
        }

        Vector2 center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
        float radius = size * 0.44f;
        float innerRadius = radius - 2.5f;
        float radiusSq = radius * radius;
        float innerRadiusSq = innerRadius * innerRadius;

        // Draw stepped pixel ring
        for (int y = 0; y < size; y++)
        {
            float dy = y - center.y;
            float dySq = dy * dy;
            for (int x = 0; x < size; x++)
            {
                float dx = x - center.x;
                float distSq = dx * dx + dySq;
                if (distSq >= innerRadiusSq && distSq <= radiusSq)
                {
                    tex.SetPixel(x, y, solid);
                }
            }
        }

        tex.Apply();
        cachedPixelRing = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        return cachedPixelRing;
    }

    #endregion

    #region VFX / Rendering Logic

    private void SetupPixelComponents()
    {
        // 1. Chunky Pixel Cross-Slash ("X")
        GameObject crossObj = new GameObject("PixelCrossSlash");
        crossSlashTransform = crossObj.transform;
        crossSlashTransform.SetParent(transform, false);
        crossSlashRenderer = crossObj.AddComponent<SpriteRenderer>();
        crossSlashRenderer.sprite = GetOrCreatePixelCrossSprite();
        crossSlashRenderer.color = coreColor;
        crossSlashRenderer.sortingOrder = 15;

        // 2. Stepped Pixel Shockwave Ring
        GameObject ringObj = new GameObject("PixelShockwaveRing");
        shockwaveRingTransform = ringObj.transform;
        shockwaveRingTransform.SetParent(transform, false);
        shockwaveRingRenderer = ringObj.AddComponent<SpriteRenderer>();
        shockwaveRingRenderer.sprite = GetOrCreatePixelRingSprite();
        shockwaveRingRenderer.color = sparkColor;
        shockwaveRingRenderer.sortingOrder = 14;

        // 3. Chunky Square Pixel Sparks
        Sprite squareSprite = GetOrCreatePixelSquareSprite();
        const float angleStep = 360f / DebrisCount;

        for (int i = 0; i < DebrisCount; i++)
        {
            float angle = (i * angleStep) + Random.Range(-12f, 12f);
            float rad = angle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float speed = Random.Range(6f, 12f);

            GameObject sparkObj = new GameObject($"PixelSpark_{i}");
            Transform sparkTransform = sparkObj.transform;
            sparkTransform.SetParent(transform, false);
            sparkTransform.localScale = Vector3.one * (Random.value > 0.5f ? 1.5f : 1.0f);

            SpriteRenderer sr = sparkObj.AddComponent<SpriteRenderer>();
            sr.sprite = squareSprite;
            sr.color = (i % 2 == 0) ? sparkColor : edgeColor;
            sr.sortingOrder = 16;

            pixelDebris.Add(sparkTransform);
            debrisRenderers.Add(sr);
            debrisVelocities.Add(dir * speed);
        }
    }

    private IEnumerator PixelHitRoutine()
    {
        float timer = 0f;
        float frameTimer = 0f;

        Vector3 ringTargetScale = Vector3.one * maxRingScale;

        while (timer < duration)
        {
            float dt = Time.deltaTime;
            timer += dt;
            frameTimer += dt;

            float t = Mathf.Clamp01(timer / duration);
            float easeOut = 1f - Mathf.Pow(1f - t, 2.5f);

            // Stepped discrete frame scale & color for retro feel
            if (frameTimer >= FrameInterval)
            {
                frameTimer = 0f;

                // Stepped cross-slash expansion and dissolve
                if (crossSlashRenderer != null)
                {
                    float crossT = Mathf.Clamp01(timer / 0.16f);
                    float crossScale = Mathf.Lerp(1.2f, 2.8f, crossT);
                    crossSlashTransform.localScale = Vector3.one * crossScale;
                    crossSlashRenderer.color = (timer < 0.08f) ? coreColor : edgeColor;
                    if (timer >= 0.16f) crossSlashRenderer.enabled = false;
                }

                // Stepped shockwave expansion
                if (shockwaveRingRenderer != null)
                {
                    // Discrete stepped scale
                    float steppedEase = Mathf.Floor(easeOut * 5f) / 5f;
                    shockwaveRingTransform.localScale = Vector3.Lerp(RingStartScale, ringTargetScale, steppedEase);
                    Color c = Color.Lerp(sparkColor, edgeColor, t);
                    c.a = Mathf.Clamp01(1f - t);
                    shockwaveRingRenderer.color = c;
                }
            }

            // Move chunky pixel debris
            float dragFactor = Mathf.Clamp01(1f - (dt * 4f));
            float debrisAlpha = Mathf.Clamp01(1f - (t * 1.3f));

            for (int i = 0; i < pixelDebris.Count; i++)
            {
                Transform debrisTransform = pixelDebris[i];
                if (debrisTransform != null)
                {
                    Vector2 vel = debrisVelocities[i];
                    vel += Vector2.down * (12f * dt); // arcade gravity
                    vel *= dragFactor; // drag
                    debrisVelocities[i] = vel;

                    // Pixel snap local position to 16 PPU
                    Vector3 newPos = debrisTransform.localPosition + (Vector3)(vel * dt);
                    debrisTransform.localPosition = SnapToPixel(newPos, PixelsPerUnit);

                    SpriteRenderer sr = debrisRenderers[i];
                    if (sr != null)
                    {
                        Color sc = sr.color;
                        sc.a = debrisAlpha;
                        sr.color = sc;
                    }
                }
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    #endregion

    #region Helpers

    private static void SetPixelSafe(Texture2D tex, int x, int y, Color c)
    {
        if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
        {
            tex.SetPixel(x, y, c);
        }
    }

    #endregion
}
