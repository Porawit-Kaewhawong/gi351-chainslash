using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossDefeatVFX : MonoBehaviour
{
    [Header("Pixel Defeat VFX Settings")]
    [SerializeField] private float duration = 1.2f;
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private Color explosionColor1 = new Color(1f, 0.88f, 0.25f, 1f);
    [SerializeField] private Color explosionColor2 = new Color(1f, 0.3f, 0.1f, 1f);

    private SpriteRenderer mainExplosionRenderer;
    private SpriteRenderer steppedShockwaveRenderer;
    private readonly List<Transform> pixelDebris = new List<Transform>();
    private readonly List<Vector2> debrisVelocities = new List<Vector2>();

    private void Awake()
    {
        SetupPixelComponents();
    }

    private void Start()
    {
        StartCoroutine(PixelDefeatRoutine());
    }

    private void SetupPixelComponents()
    {
        // 1. Central Pixel Explosion
        GameObject expObj = new GameObject("MainPixelExplosion");
        expObj.transform.SetParent(transform, false);
        mainExplosionRenderer = expObj.AddComponent<SpriteRenderer>();
        mainExplosionRenderer.sprite = BossHitVFX.GetOrCreatePixelCrossSprite();
        mainExplosionRenderer.color = flashColor;
        mainExplosionRenderer.sortingOrder = 18;

        // 2. Stepped Pixel Shockwave Ring
        GameObject ringObj = new GameObject("PixelShockwaveRing");
        ringObj.transform.SetParent(transform, false);
        steppedShockwaveRenderer = ringObj.AddComponent<SpriteRenderer>();
        steppedShockwaveRenderer.sprite = BossHitVFX.GetOrCreatePixelRingSprite();
        steppedShockwaveRenderer.color = explosionColor1;
        steppedShockwaveRenderer.sortingOrder = 16;

        // 3. Chunky Square Pixel Shards
        Sprite squareSprite = BossHitVFX.GetOrCreatePixelSquareSprite();
        int debrisCount = 20;
        for (int i = 0; i < debrisCount; i++)
        {
            float angle = (i * (360f / debrisCount)) + Random.Range(-8f, 8f);
            float rad = angle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float speed = Random.Range(7f, 16f);

            GameObject shard = new GameObject($"PixelShard_{i}");
            shard.transform.SetParent(transform, false);
            shard.transform.localScale = Vector3.one * (Random.value > 0.5f ? 2.5f : 1.5f);

            SpriteRenderer sr = shard.AddComponent<SpriteRenderer>();
            sr.sprite = squareSprite;
            sr.color = (i % 3 == 0) ? Color.white : ((i % 3 == 1) ? explosionColor1 : explosionColor2);
            sr.sortingOrder = 17;

            pixelDebris.Add(shard.transform);
            debrisVelocities.Add(dir * speed);
        }
    }

    private IEnumerator PixelDefeatRoutine()
    {
        float timer = 0f;
        float frameInterval = 1f / 16f; // Retro 16 FPS frame stepping
        float frameTimer = 0f;

        // Multi-stage explosion pop timings and positions
        Vector2[] subPopOffsets = new Vector2[]
        {
            new Vector2(0.65f, 0.5f),
            new Vector2(-0.75f, -0.3f),
            new Vector2(0.35f, -0.75f),
            new Vector2(-0.4f, 0.6f)
        };
        float[] subPopTimes = new float[] { 0.10f, 0.22f, 0.36f, 0.50f };
        bool[] subPopTriggered = new bool[subPopOffsets.Length];

        Vector3 ringStartScale = Vector3.one * 0.5f;
        Vector3 ringTargetScale = Vector3.one * 9.0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            frameTimer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            float easeOut = 1f - Mathf.Pow(1f - t, 2.5f);

            // Check secondary retro micro-explosions across boss body
            for (int p = 0; p < subPopTimes.Length; p++)
            {
                if (!subPopTriggered[p] && timer >= subPopTimes[p])
                {
                    subPopTriggered[p] = true;
                    SpawnMicroExplosion(subPopOffsets[p]);
                }
            }

            // Stepped discrete frame scale updates
            if (frameTimer >= frameInterval)
            {
                frameTimer = 0f;

                // Central explosion pulse & stepped scale
                if (mainExplosionRenderer != null)
                {
                    float expT = Mathf.Clamp01(timer / 0.5f);
                    float steppedExp = Mathf.Floor(expT * 6f) / 6f;
                    float scale = Mathf.Lerp(2.0f, 6.5f, steppedExp);
                    mainExplosionRenderer.transform.localScale = Vector3.one * scale;

                    if (timer < 0.08f) mainExplosionRenderer.color = Color.white;
                    else if (timer < 0.22f) mainExplosionRenderer.color = explosionColor1;
                    else mainExplosionRenderer.color = explosionColor2;

                    if (timer >= 0.5f) mainExplosionRenderer.enabled = false;
                }

                // Stepped shockwave expansion
                if (steppedShockwaveRenderer != null)
                {
                    float steppedEase = Mathf.Floor(easeOut * 7f) / 7f;
                    steppedShockwaveRenderer.transform.localScale = Vector3.Lerp(ringStartScale, ringTargetScale, steppedEase);
                    Color rc = Color.Lerp(explosionColor1, explosionColor2, t);
                    rc.a = Mathf.Clamp01(1f - (t * 1.2f));
                    steppedShockwaveRenderer.color = rc;
                }
            }

            // Move chunky pixel debris
            for (int i = 0; i < pixelDebris.Count; i++)
            {
                if (pixelDebris[i] != null)
                {
                    Vector2 vel = debrisVelocities[i];
                    vel += Vector2.down * 14f * Time.deltaTime; // arcade gravity
                    vel *= Mathf.Clamp01(1f - (Time.deltaTime * 2.5f)); // drag
                    debrisVelocities[i] = vel;

                    Vector3 newPos = pixelDebris[i].localPosition + (Vector3)vel * Time.deltaTime;
                    pixelDebris[i].localPosition = BossHitVFX.SnapToPixel(newPos, 16f);

                    if (pixelDebris[i].TryGetComponent<SpriteRenderer>(out SpriteRenderer sr))
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

    private void SpawnMicroExplosion(Vector2 localOffset)
    {
        GameObject micro = new GameObject("MicroPop");
        micro.transform.SetParent(transform, false);
        micro.transform.localPosition = (Vector3)localOffset;
        micro.transform.localScale = Vector3.one * Random.Range(1.8f, 2.8f);

        SpriteRenderer sr = micro.AddComponent<SpriteRenderer>();
        sr.sprite = BossHitVFX.GetOrCreatePixelCrossSprite();
        sr.color = (Random.value > 0.5f) ? explosionColor1 : Color.white;
        sr.sortingOrder = 19;

        StartCoroutine(MicroPopFade(micro, sr));
    }

    private IEnumerator MicroPopFade(GameObject obj, SpriteRenderer sr)
    {
        float t = 0f;
        float dur = 0.18f;
        while (t < dur && obj != null)
        {
            t += Time.deltaTime;
            float step = Mathf.Floor((t / dur) * 3f) / 3f;
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

    public static GameObject Spawn(Vector3 position)
    {
        GameObject vfx = new GameObject("BossDefeatVFX");
        vfx.transform.position = position;
        vfx.AddComponent<BossDefeatVFX>();
        return vfx;
    }
}

