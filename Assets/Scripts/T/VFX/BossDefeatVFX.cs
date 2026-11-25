using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossDefeatVFX : MonoBehaviour
{
    #region Serialized Fields

    [Header("Pixel Defeat VFX Settings")]
    [SerializeField] private float duration = 1.2f;
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private Color explosionColor1 = new Color(1f, 0.88f, 0.25f, 1f);
    [SerializeField] private Color explosionColor2 = new Color(1f, 0.3f, 0.1f, 1f);

    #endregion

    #region Constants

    private const int DebrisCount = 20;
    private const float FrameInterval = 1f / 16f; // Retro 16 FPS frame stepping
    private const float PixelsPerUnit = 16f;
    private const float MicroPopDuration = 0.18f;

    // Multi-stage explosion pop timings and positions
    private static readonly Vector2[] SubPopOffsets =
    {
        new Vector2(0.65f, 0.5f),
        new Vector2(-0.75f, -0.3f),
        new Vector2(0.35f, -0.75f),
        new Vector2(-0.4f, 0.6f)
    };

    private static readonly float[] SubPopTimes = { 0.10f, 0.22f, 0.36f, 0.50f };
    private static readonly Vector3 RingStartScale = Vector3.one * 0.5f;
    private static readonly Vector3 RingTargetScale = Vector3.one * 9.0f;

    #endregion

    #region Private State

    private SpriteRenderer mainExplosionRenderer;
    private Transform mainExplosionTransform;
    private SpriteRenderer steppedShockwaveRenderer;
    private Transform steppedShockwaveTransform;
    private readonly List<Transform> pixelDebris = new List<Transform>(DebrisCount);
    private readonly List<SpriteRenderer> debrisRenderers = new List<SpriteRenderer>(DebrisCount);
    private readonly List<Vector2> debrisVelocities = new List<Vector2>(DebrisCount);

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        SetupPixelComponents();
    }

    private void Start()
    {
        StartCoroutine(PixelDefeatRoutine());
    }

    #endregion

    #region Public API

    public static GameObject Spawn(Vector3 position)
    {
        GameObject vfx = new GameObject("BossDefeatVFX");
        vfx.transform.position = position;
        vfx.AddComponent<BossDefeatVFX>();
        return vfx;
    }

    #endregion

    #region VFX / Rendering Logic

    private void SetupPixelComponents()
    {
        // 1. Central Pixel Explosion
        GameObject expObj = new GameObject("MainPixelExplosion");
        mainExplosionTransform = expObj.transform;
        mainExplosionTransform.SetParent(transform, false);
        mainExplosionRenderer = expObj.AddComponent<SpriteRenderer>();
        mainExplosionRenderer.sprite = BossHitVFX.GetOrCreatePixelCrossSprite();
        mainExplosionRenderer.color = flashColor;
        mainExplosionRenderer.sortingOrder = 18;

        // 2. Stepped Pixel Shockwave Ring
        GameObject ringObj = new GameObject("PixelShockwaveRing");
        steppedShockwaveTransform = ringObj.transform;
        steppedShockwaveTransform.SetParent(transform, false);
        steppedShockwaveRenderer = ringObj.AddComponent<SpriteRenderer>();
        steppedShockwaveRenderer.sprite = BossHitVFX.GetOrCreatePixelRingSprite();
        steppedShockwaveRenderer.color = explosionColor1;
        steppedShockwaveRenderer.sortingOrder = 16;

        // 3. Chunky Square Pixel Shards
        Sprite squareSprite = BossHitVFX.GetOrCreatePixelSquareSprite();
        const float angleStep = 360f / DebrisCount;

        for (int i = 0; i < DebrisCount; i++)
        {
            float angle = (i * angleStep) + Random.Range(-8f, 8f);
            float rad = angle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float speed = Random.Range(7f, 16f);

            GameObject shard = new GameObject($"PixelShard_{i}");
            Transform shardTransform = shard.transform;
            shardTransform.SetParent(transform, false);
            shardTransform.localScale = Vector3.one * (Random.value > 0.5f ? 2.5f : 1.5f);

            SpriteRenderer sr = shard.AddComponent<SpriteRenderer>();
            sr.sprite = squareSprite;
            int colorIndex = i % 3;
            sr.color = (colorIndex == 0) ? Color.white : ((colorIndex == 1) ? explosionColor1 : explosionColor2);
            sr.sortingOrder = 17;

            pixelDebris.Add(shardTransform);
            debrisRenderers.Add(sr);
            debrisVelocities.Add(dir * speed);
        }
    }

    private IEnumerator PixelDefeatRoutine()
    {
        float timer = 0f;
        float frameTimer = 0f;
        int nextSubPopIndex = 0;

        while (timer < duration)
        {
            float dt = Time.deltaTime;
            timer += dt;
            frameTimer += dt;
            float t = Mathf.Clamp01(timer / duration);
            float easeOut = 1f - Mathf.Pow(1f - t, 2.5f);

            // Check secondary retro micro-explosions across boss body
            while (nextSubPopIndex < SubPopTimes.Length && timer >= SubPopTimes[nextSubPopIndex])
            {
                SpawnMicroExplosion(SubPopOffsets[nextSubPopIndex]);
                nextSubPopIndex++;
            }

            // Stepped discrete frame scale updates
            if (frameTimer >= FrameInterval)
            {
                frameTimer = 0f;

                // Central explosion pulse & stepped scale
                if (mainExplosionRenderer != null)
                {
                    float expT = Mathf.Clamp01(timer / 0.5f);
                    float steppedExp = Mathf.Floor(expT * 6f) / 6f;
                    float scale = Mathf.Lerp(2.0f, 6.5f, steppedExp);
                    mainExplosionTransform.localScale = Vector3.one * scale;

                    if (timer < 0.08f) mainExplosionRenderer.color = Color.white;
                    else if (timer < 0.22f) mainExplosionRenderer.color = explosionColor1;
                    else mainExplosionRenderer.color = explosionColor2;

                    if (timer >= 0.5f) mainExplosionRenderer.enabled = false;
                }

                // Stepped shockwave expansion
                if (steppedShockwaveRenderer != null)
                {
                    float steppedEase = Mathf.Floor(easeOut * 7f) / 7f;
                    steppedShockwaveTransform.localScale = Vector3.Lerp(RingStartScale, RingTargetScale, steppedEase);
                    Color rc = Color.Lerp(explosionColor1, explosionColor2, t);
                    rc.a = Mathf.Clamp01(1f - (t * 1.2f));
                    steppedShockwaveRenderer.color = rc;
                }
            }

            // Move chunky pixel debris
            float dragFactor = Mathf.Clamp01(1f - (dt * 2.5f));
            float debrisAlpha = Mathf.Clamp01(1f - t);

            for (int i = 0; i < pixelDebris.Count; i++)
            {
                Transform debrisTransform = pixelDebris[i];
                if (debrisTransform != null)
                {
                    Vector2 vel = debrisVelocities[i];
                    vel += Vector2.down * (14f * dt); // arcade gravity
                    vel *= dragFactor; // drag
                    debrisVelocities[i] = vel;

                    Vector3 newPos = debrisTransform.localPosition + (Vector3)(vel * dt);
                    debrisTransform.localPosition = BossHitVFX.SnapToPixel(newPos, PixelsPerUnit);

                    SpriteRenderer sr = debrisRenderers[i];
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

    #region Helpers

    private void SpawnMicroExplosion(Vector2 localOffset)
    {
        GameObject micro = new GameObject("MicroPop");
        Transform microTransform = micro.transform;
        microTransform.SetParent(transform, false);
        microTransform.localPosition = (Vector3)localOffset;
        microTransform.localScale = Vector3.one * Random.Range(1.8f, 2.8f);

        SpriteRenderer sr = micro.AddComponent<SpriteRenderer>();
        sr.sprite = BossHitVFX.GetOrCreatePixelCrossSprite();
        sr.color = (Random.value > 0.5f) ? explosionColor1 : Color.white;
        sr.sortingOrder = 19;

        StartCoroutine(MicroPopFade(micro, sr));
    }

    private IEnumerator MicroPopFade(GameObject obj, SpriteRenderer sr)
    {
        float t = 0f;
        while (t < MicroPopDuration && obj != null)
        {
            t += Time.deltaTime;
            float step = Mathf.Floor((t / MicroPopDuration) * 3f) / 3f;
            if (sr != null)
            {
                Color c = sr.color;
                c.a = 1f - step;
                sr.color = c;
            }
            yield return null;
        }
        if (obj != null) Destroy(obj);
    }

    #endregion
}
