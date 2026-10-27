using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public class BossTargetPoint : EnemyBase
{
    [Header("Target Point Visuals")]
    [SerializeField] private Color targetNormalColor = new Color(0.95f, 0.25f, 0.9f, 1f); // Vibrant purple-pink
    [SerializeField] private GameObject slashVFXPrefab;
    [SerializeField] private AudioClip slashSFX;

    private LegendarySamurai boss;
    private int pointIndex;
    private bool isSlashed;

    public int PointIndex => pointIndex;
    public bool IsSlashed => isSlashed;

    protected override void Awake()
    {
        base.Awake();

        // Target points do not use physical gravity or dynamic physics pushing
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
        }

        if (col != null)
        {
            col.isTrigger = true;
        }

        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        if (spriteRenderer.sprite == null)
        {
            spriteRenderer.sprite = GetOrCreateTargetPointSprite();
        }

        originalColor = targetNormalColor;
        spriteRenderer.color = originalColor;
        spriteRenderer.sortingOrder = 6;
    }

    public void Initialize(LegendarySamurai ownerBoss, int index)
    {
        boss = ownerBoss;
        pointIndex = index;
    }

    public void ActivatePoint(Vector3 worldPos)
    {
        transform.position = worldPos;
        gameObject.SetActive(true);
        isSlashed = false;
        IsDead = false;
        isTarget = false;

        if (col != null) col.enabled = true;
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
            spriteRenderer.color = originalColor;
        }
    }

    public void DeactivatePoint()
    {
        isSlashed = false;
        IsDead = true;
        isTarget = false;
        if (col != null) col.enabled = false;
        if (spriteRenderer != null) spriteRenderer.enabled = false;
        gameObject.SetActive(false);
    }

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        if (isSlashed) return;
        isSlashed = true;
        IsDead = true;

        if (slashVFXPrefab != null)
        {
            Instantiate(slashVFXPrefab, transform.position, Quaternion.identity);
        }

        if (slashSFX != null && playerController != null)
        {
            playerController.PlaySFX(slashSFX);
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false;
        }

        if (col != null)
        {
            col.enabled = false;
        }

        if (boss != null)
        {
            boss.OnTargetPointSlashed(this, deathImpulse);
        }
    }

    // Overrides to disable standard enemy pathfinding/jumping
    protected override void Movement() {}
    protected override void HandleJump(Vector2 dir) {}
    protected override void HandlePlayerContact(Collision2D collision) {}
    protected override void HandlePlayerTriggerContact(Collider2D other) {}
    protected override bool HandleThrownImpact(Collision2D collision) => false;
    protected override void FlipAndRotate() {}

    private static Sprite cachedTargetSprite;
    public static Sprite GetOrCreateTargetPointSprite()
    {
        if (cachedTargetSprite != null) return cachedTargetSprite;

        int size = 24;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color clear = new Color(0, 0, 0, 0);
        Color core = new Color(1f, 0.95f, 0.4f, 1f);
        Color rim = new Color(0.95f, 0.2f, 0.85f, 1f);

        Vector2 center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
        float maxRadius = size * 0.45f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                if (dist <= maxRadius)
                {
                    float t = dist / maxRadius;
                    Color pixelColor = Color.Lerp(core, rim, t);
                    pixelColor.a = Mathf.Clamp01(1f - Mathf.Pow(t, 3f));
                    tex.SetPixel(x, y, pixelColor);
                }
                else
                {
                    tex.SetPixel(x, y, clear);
                }
            }
        }

        tex.Apply();
        cachedTargetSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        return cachedTargetSprite;
    }
}

