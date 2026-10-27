using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public class ArmoredNinjaWeakPoint : EnemyBase
{
    [Header("Weak Point Settings")]
    [SerializeField] private Color normalColor = new Color(1f, 0.45f, 0.15f, 1f); // Glowing ember orange
    [SerializeField] private GameObject breakVFX;
    [SerializeField] private AudioClip breakSFX;

    private ArmoredNinja ownerNinja;
    private int pointIndex;
    private bool isSlashed = false;
    private bool isCurrentlyVisible = false;

    public int PointIndex => pointIndex;
    public bool IsSlashed => isSlashed;
    public bool IsCurrentlyVisible => isCurrentlyVisible;

    protected override void Awake()
    {
        base.Awake();

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
            spriteRenderer.sprite = GetOrCreateWeakPointSprite();
        }

        originalColor = normalColor;
        spriteRenderer.color = originalColor;
        spriteRenderer.sortingOrder = 6;

        // Target points start invisible until the player enters slash mode
        SetVisible(false);
    }

    public void Initialize(ArmoredNinja ninja, int index)
    {
        ownerNinja = ninja;
        pointIndex = index;
    }

    public void Activate(Vector3 worldPos)
    {
        transform.position = worldPos;
        gameObject.SetActive(true);
        isSlashed = false;
        IsDead = false;
        isTarget = false;

        // Hidden by default until slash mode begins
        SetVisible(false);
    }

    public void Deactivate()
    {
        isSlashed = false;
        IsDead = true;
        isTarget = false;

        SetVisible(false);
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Controls weak point visibility and targetability based on whether the player is in slash mode.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (isSlashed)
        {
            isCurrentlyVisible = false;
            if (spriteRenderer != null) spriteRenderer.enabled = false;
            if (col != null) col.enabled = false;
            if (targetIndicator != null) targetIndicator.SetActive(false);
            return;
        }

        isCurrentlyVisible = visible;

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = visible;
            if (visible && !isTarget)
            {
                spriteRenderer.color = originalColor;
            }
        }

        if (col != null)
        {
            col.enabled = visible;
        }

        if (targetIndicator != null)
        {
            targetIndicator.SetActive(visible && !isTarget);
        }
    }

    protected override void Update()
    {
        if (IsDead || isSlashed) return;

        // The 3 target points on its back are only visible when the player is about to slash.
        // The points disappear when the player is not slashing.
        bool isAboutToSlash = PlayerController.Instance != null && PlayerController.Instance.IsInMode;

        if (isCurrentlyVisible != isAboutToSlash)
        {
            SetVisible(isAboutToSlash);
        }

        if (isAboutToSlash)
        {
            UpdateTargetIndicator();
            CheckMouseHover();
        }
    }

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        if (isSlashed) return;
        isSlashed = true;
        IsDead = true;

        if (breakVFX != null)
        {
            Instantiate(breakVFX, transform.position, Quaternion.identity);
        }

        if (breakSFX != null && playerController != null)
        {
            playerController.PlaySFX(breakSFX);
        }

        SetVisible(false);

        if (ownerNinja != null)
        {
            ownerNinja.OnWeakPointSlashed(this, deathImpulse);
        }
    }

    // Disable movement and player collision logic for stationary weak points
    protected override void Movement() {}
    protected override void HandleJump(Vector2 dir) {}
    protected override void HandlePlayerContact(Collision2D collision) {}
    protected override void HandlePlayerTriggerContact(Collider2D other) {}
    protected override bool HandleThrownImpact(Collision2D collision) => false;
    protected override void FlipAndRotate() {}

    private static Sprite cachedWeakPointSprite;
    public static Sprite GetOrCreateWeakPointSprite()
    {
        if (cachedWeakPointSprite != null) return cachedWeakPointSprite;

        int size = 18;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color clear = new Color(0, 0, 0, 0);
        Color core = new Color(1f, 0.5f, 0.15f, 1f);
        Color rim = new Color(0.95f, 0.2f, 0.05f, 1f);
        Color metal = new Color(0.35f, 0.4f, 0.48f, 1f);

        Vector2 center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
        float maxRadius = size * 0.45f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                if (dist <= maxRadius * 0.65f)
                {
                    float t = dist / (maxRadius * 0.65f);
                    tex.SetPixel(x, y, Color.Lerp(core, rim, t));
                }
                else if (dist <= maxRadius)
                {
                    tex.SetPixel(x, y, metal);
                }
                else
                {
                    tex.SetPixel(x, y, clear);
                }
            }
        }

        tex.Apply();
        cachedWeakPointSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        return cachedWeakPointSprite;
    }
}

