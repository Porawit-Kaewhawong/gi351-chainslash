using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossStompShockwaveVFX : MonoBehaviour
{
    #region Serialized Fields

    [Header("Pixel Stomp Shockwave Settings")]
    [SerializeField] private float duration = 0.58f;
    [SerializeField] private float maxHorizontalScale = 11.0f; // Matches 5.5m radius
    [SerializeField] private float maxVerticalScale = 2.4f;
    [SerializeField] private Color groundWaveColor = new Color(1f, 0.45f, 0.15f, 1f);
    [SerializeField] private Color dustColor1 = new Color(0.95f, 0.88f, 0.72f, 1f);
    [SerializeField] private Color dustColor2 = new Color(0.65f, 0.5f, 0.35f, 1f);

    #endregion

    #region Constants

    private const int DebrisCount = 16;
    private const float FrameInterval = 1f / 16f; // Retro 16 FPS frame stepping
    private const float PixelsPerUnit = 16f;
    private static readonly Vector3 InitialRingScale = new Vector3(1.2f, 0.4f, 1f);

    #endregion

    #region Private State

    private SpriteRenderer shockwaveEllipseRenderer;
    private Transform shockwaveEllipseTransform;
    private SpriteRenderer centralImpactFlash;
    private Transform centralImpactFlashTransform;
    private readonly List<Transform> pixelDirtDebris = new List<Transform>(DebrisCount);
    private readonly List<SpriteRenderer> dirtRenderers = new List<SpriteRenderer>(DebrisCount);
    private readonly List<Vector2> dirtVelocities = new List<Vector2>(DebrisCount);

    private static Sprite cachedPixelGroundEllipse;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        SetupPixelComponents();
    }

    private void Start()
    {
        StartCoroutine(PixelStompRoutine());
    }

    #endregion

    #region Public API

    public static GameObject Spawn(Vector3 position)
    {
        GameObject vfx = new GameObject("BossStompShockwaveVFX");
        vfx.transform.position = position;
        vfx.AddComponent<BossStompShockwaveVFX>();
        return vfx;
    }

    // --- Procedural Pixel Art Sprites with FilterMode.Point ---

    public static Sprite GetOrCreatePixelGroundEllipseSprite()
    {
        if (cachedPixelGroundEllipse != null) return cachedPixelGroundEllipse;

        const int width = 48;
        const int height = 16;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0);
        Color solid = Color.white;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                tex.SetPixel(x, y, clear);
            }
        }

        Vector2 center = new Vector2(width * 0.5f - 0.5f, height * 0.5f - 0.5f);
        const float radiusX = width * 0.46f;
        const float radiusY = height * 0.44f;
        const float innerRadiusX = radiusX - 3.2f;
        const float innerRadiusY = radiusY - 2.8f;

        const float invRadiusX = 1f / radiusX;
        const float invRadiusY = 1f / radiusY;
        const float invInnerRadiusX = 1f / innerRadiusX;
        const float invInnerRadiusY = 1f / innerRadiusY;

        // Draw stepped pixel ellipse ring
        for (int y = 0; y < height; y++)
        {
            float offsetY = y - center.y;
            float dy = offsetY * invRadiusY;
            float dySq = dy * dy;
            float idy = offsetY * invInnerRadiusY;
            float idySq = idy * idy;

            for (int x = 0; x < width; x++)
            {
                float offsetX = x - center.x;
                float dx = offsetX * invRadiusX;
                float distOuterSq = dx * dx + dySq;

                float idx = offsetX * invInnerRadiusX;
                float distInnerSq = idx * idx + idySq;

                if (distOuterSq <= 1.0f && distInnerSq >= 1.0f)
                {
                    tex.SetPixel(x, y, solid);
                }
            }
        }

        tex.Apply();
        cachedPixelGroundEllipse = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        return cachedPixelGroundEllipse;
    }

    #endregion

    #region VFX / Rendering Logic

    private void SetupPixelComponents()
    {
        // 1. Chunky Pixel Ground Shockwave Ellipse
        GameObject ringObj = new GameObject("PixelGroundShockwave");
        shockwaveEllipseTransform = ringObj.transform;
        shockwaveEllipseTransform.SetParent(transform, false);
        shockwaveEllipseTransform.localPosition = new Vector3(0f, 0.08f, 0f);

        shockwaveEllipseRenderer = ringObj.AddComponent<SpriteRenderer>();
        shockwaveEllipseRenderer.sprite = GetOrCreatePixelGroundEllipseSprite();
        shockwaveEllipseRenderer.color = Color.white;
        shockwaveEllipseRenderer.sortingOrder = 14;

        // 2. Central Impact Flash (Pixel Cross)
        GameObject flashObj = new GameObject("PixelImpactFlash");
        centralImpactFlashTransform = flashObj.transform;
        centralImpactFlashTransform.SetParent(transform, false);
        centralImpactFlash = flashObj.AddComponent<SpriteRenderer>();
        centralImpactFlash.sprite = BossHitVFX.GetOrCreatePixelCrossSprite();
        centralImpactFlash.color = Color.white;
        centralImpactFlash.sortingOrder = 15;

        // 3. Chunky Square Pixel Dirt/Rock Debris
        Sprite squareSprite = BossHitVFX.GetOrCreatePixelSquareSprite();
        for (int i = 0; i < DebrisCount; i++)
        {
            float side = (i % 2 == 0) ? -1f : 1f;
            float horizontalSpeed = Random.Range(5f, 13f) * side;
            float upwardSpeed = Random.Range(3f, 8f);

            GameObject dirtObj = new GameObject($"PixelDirt_{i}");
            Transform dirtTransform = dirtObj.transform;
            dirtTransform.SetParent(transform, false);
            dirtTransform.localScale = Vector3.one * (Random.value > 0.5f ? 2.5f : 1.5f);

            SpriteRenderer sr = dirtObj.AddComponent<SpriteRenderer>();
            sr.sprite = squareSprite;
            sr.color = (i % 2 == 0) ? dustColor1 : dustColor2;
            sr.sortingOrder = 13;

            pixelDirtDebris.Add(dirtTransform);
            dirtRenderers.Add(sr);
            dirtVelocities.Add(new Vector2(horizontalSpeed, upwardSpeed));
        }
    }

    private IEnumerator PixelStompRoutine()
    {
        float timer = 0f;
        float frameTimer = 0f;

        Vector3 targetRingScale = new Vector3(maxHorizontalScale, maxVerticalScale, 1f);

        while (timer < duration)
        {
            float dt = Time.deltaTime;
            timer += dt;
            frameTimer += dt;
            float t = Mathf.Clamp01(timer / duration);
            float easeOut = 1f - Mathf.Pow(1f - t, 2.8f);

            if (frameTimer >= FrameInterval)
            {
                frameTimer = 0f;

                // Stepped shockwave expansion along the floor
                if (shockwaveEllipseRenderer != null)
                {
                    float steppedEase = Mathf.Floor(easeOut * 5f) / 5f;
                    shockwaveEllipseTransform.localScale = Vector3.Lerp(InitialRingScale, targetRingScale, steppedEase);

                    Color c = Color.Lerp(Color.white, groundWaveColor, t * 1.5f);
                    c.a = Mathf.Clamp01(1f - t);
                    shockwaveEllipseRenderer.color = c;
                }

                // Impact flash fadeout
                if (centralImpactFlash != null)
                {
                    float flashT = Mathf.Clamp01(timer / 0.18f);
                    float flashScale = Mathf.Lerp(2.2f, 4.0f, flashT);
                    centralImpactFlashTransform.localScale = new Vector3(flashScale, flashScale * 0.6f, 1f);
                    centralImpactFlash.color = new Color(1f, 1f, 1f, 1f - flashT);
                    if (timer >= 0.18f) centralImpactFlash.enabled = false;
                }
            }

            // Move chunky dirt particles with arcade floor physics
            float horizontalDrag = Mathf.Clamp01(1f - (dt * 3.5f));
            float debrisAlpha = Mathf.Clamp01(1f - t);

            for (int i = 0; i < pixelDirtDebris.Count; i++)
            {
                Transform dirtTransform = pixelDirtDebris[i];
                if (dirtTransform != null)
                {
                    Vector2 vel = dirtVelocities[i];
                    vel += Vector2.down * (18f * dt); // gravity
                    vel.x *= horizontalDrag; // floor drag
                    dirtVelocities[i] = vel;

                    Vector3 newPos = dirtTransform.localPosition + (Vector3)(vel * dt);
                    dirtTransform.localPosition = BossHitVFX.SnapToPixel(newPos, PixelsPerUnit);

                    SpriteRenderer sr = dirtRenderers[i];
                    if (sr != null)
                    {
                        Color c = sr.color;
                        c.a = debrisAlpha;
                        sr.color = c;
                    }
                }
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    #endregion
}
