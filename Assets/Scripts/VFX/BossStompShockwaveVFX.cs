using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossStompShockwaveVFX : MonoBehaviour
{
    [Header("Pixel Stomp Shockwave Settings")]
    [SerializeField] private float duration = 0.58f;
    [SerializeField] private float maxHorizontalScale = 11.0f; // Matches 5.5m radius
    [SerializeField] private float maxVerticalScale = 2.4f;
    [SerializeField] private Color groundWaveColor = new Color(1f, 0.45f, 0.15f, 1f);
    [SerializeField] private Color dustColor1 = new Color(0.95f, 0.88f, 0.72f, 1f);
    [SerializeField] private Color dustColor2 = new Color(0.65f, 0.5f, 0.35f, 1f);

    private SpriteRenderer shockwaveEllipseRenderer;
    private SpriteRenderer centralImpactFlash;
    private readonly List<Transform> pixelDirtDebris = new List<Transform>();
    private readonly List<Vector2> dirtVelocities = new List<Vector2>();

    private void Awake()
    {
        SetupPixelComponents();
    }

    private void Start()
    {
        StartCoroutine(PixelStompRoutine());
    }

    private void SetupPixelComponents()
    {
        // 1. Chunky Pixel Ground Shockwave Ellipse
        GameObject ringObj = new GameObject("PixelGroundShockwave");
        ringObj.transform.SetParent(transform, false);
        ringObj.transform.localPosition = new Vector3(0f, 0.08f, 0f);

        shockwaveEllipseRenderer = ringObj.AddComponent<SpriteRenderer>();
        shockwaveEllipseRenderer.sprite = GetOrCreatePixelGroundEllipseSprite();
        shockwaveEllipseRenderer.color = Color.white;
        shockwaveEllipseRenderer.sortingOrder = 14;

        // 2. Central Impact Flash (Pixel Cross)
        GameObject flashObj = new GameObject("PixelImpactFlash");
        flashObj.transform.SetParent(transform, false);
        centralImpactFlash = flashObj.AddComponent<SpriteRenderer>();
        centralImpactFlash.sprite = BossHitVFX.GetOrCreatePixelCrossSprite();
        centralImpactFlash.color = Color.white;
        centralImpactFlash.sortingOrder = 15;

        // 3. Chunky Square Pixel Dirt/Rock Debris
        Sprite squareSprite = BossHitVFX.GetOrCreatePixelSquareSprite();
        int debrisCount = 16;
        for (int i = 0; i < debrisCount; i++)
        {
            float side = (i % 2 == 0) ? -1f : 1f;
            float horizontalSpeed = Random.Range(5f, 13f) * side;
            float upwardSpeed = Random.Range(3f, 8f);

            GameObject dirtObj = new GameObject($"PixelDirt_{i}");
            dirtObj.transform.SetParent(transform, false);
            dirtObj.transform.localScale = Vector3.one * (Random.value > 0.5f ? 2.5f : 1.5f);

            SpriteRenderer sr = dirtObj.AddComponent<SpriteRenderer>();
            sr.sprite = squareSprite;
            sr.color = (i % 2 == 0) ? dustColor1 : dustColor2;
            sr.sortingOrder = 13;

            pixelDirtDebris.Add(dirtObj.transform);
            dirtVelocities.Add(new Vector2(horizontalSpeed, upwardSpeed));
        }
    }

    private IEnumerator PixelStompRoutine()
    {
        float timer = 0f;
        float frameInterval = 1f / 16f; // Retro 16 FPS frame stepping
        float frameTimer = 0f;

        Vector3 initialRingScale = new Vector3(1.2f, 0.4f, 1f);
        Vector3 targetRingScale = new Vector3(maxHorizontalScale, maxVerticalScale, 1f);

        while (timer < duration)
        {
            timer += Time.deltaTime;
            frameTimer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            float easeOut = 1f - Mathf.Pow(1f - t, 2.8f);

            if (frameTimer >= frameInterval)
            {
                frameTimer = 0f;

                // Stepped shockwave expansion along the floor
                if (shockwaveEllipseRenderer != null)
                {
                    float steppedEase = Mathf.Floor(easeOut * 5f) / 5f;
                    shockwaveEllipseRenderer.transform.localScale = Vector3.Lerp(initialRingScale, targetRingScale, steppedEase);

                    Color c = Color.Lerp(Color.white, groundWaveColor, t * 1.5f);
                    c.a = Mathf.Clamp01(1f - t);
                    shockwaveEllipseRenderer.color = c;
                }

                // Impact flash fadeout
                if (centralImpactFlash != null)
                {
                    float flashT = Mathf.Clamp01(timer / 0.18f);
                    float flashScale = Mathf.Lerp(2.2f, 4.0f, flashT);
                    centralImpactFlash.transform.localScale = new Vector3(flashScale, flashScale * 0.6f, 1f);
                    centralImpactFlash.color = new Color(1f, 1f, 1f, 1f - flashT);
                    if (timer >= 0.18f) centralImpactFlash.enabled = false;
                }
            }

            // Move chunky dirt particles with arcade floor physics
            for (int i = 0; i < pixelDirtDebris.Count; i++)
            {
                if (pixelDirtDebris[i] != null)
                {
                    Vector2 vel = dirtVelocities[i];
                    vel += Vector2.down * 18f * Time.deltaTime; // gravity
                    vel.x *= Mathf.Clamp01(1f - (Time.deltaTime * 3.5f)); // floor drag
                    dirtVelocities[i] = vel;

                    Vector3 newPos = pixelDirtDebris[i].localPosition + (Vector3)vel * Time.deltaTime;
                    pixelDirtDebris[i].localPosition = BossHitVFX.SnapToPixel(newPos, 16f);

                    if (pixelDirtDebris[i].TryGetComponent<SpriteRenderer>(out SpriteRenderer sr))
                    {
                        Color c = sr.color;
                        c.a = Mathf.Clamp01(1f - t);
                        sr.color = c;
                    }
                }
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    public static GameObject Spawn(Vector3 position)
    {
        GameObject vfx = new GameObject("BossStompShockwaveVFX");
        vfx.transform.position = position;
        vfx.AddComponent<BossStompShockwaveVFX>();
        return vfx;
    }

    // --- Procedural Pixel Art Sprites with FilterMode.Point ---

    private static Sprite cachedPixelGroundEllipse;
    public static Sprite GetOrCreatePixelGroundEllipseSprite()
    {
        if (cachedPixelGroundEllipse != null) return cachedPixelGroundEllipse;

        int width = 48;
        int height = 16;
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
        float radiusX = width * 0.46f;
        float radiusY = height * 0.44f;
        float innerRadiusX = radiusX - 3.2f;
        float innerRadiusY = radiusY - 2.8f;

        // Draw stepped pixel ellipse ring
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = (x - center.x) / radiusX;
                float dy = (y - center.y) / radiusY;
                float distOuterSq = dx * dx + dy * dy;

                float idx = (x - center.x) / innerRadiusX;
                float idy = (y - center.y) / innerRadiusY;
                float distInnerSq = idx * idx + idy * idy;

                if (distOuterSq <= 1.0f && distInnerSq >= 1.0f)
                {
                    tex.SetPixel(x, y, solid);
                }
            }
        }

        tex.Apply();
        cachedPixelGroundEllipse = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 16f);
        return cachedPixelGroundEllipse;
    }
}

