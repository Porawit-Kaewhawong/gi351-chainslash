using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public class BossTargetPoint : EnemyBase
{
    [Header("Target Point Visuals")]
    [SerializeField] private Color normalColor = new Color(1f, 0.6f, 0.15f, 1f);
    [SerializeField] private Color selectedColor = new Color(0.2f, 0.95f, 1f, 1f);
    [SerializeField] private GameObject slashVFXPrefab;
    [SerializeField] private AudioClip slashSFX;

    private LegendarySamurai boss;
    private int pointIndex;
    private bool isSlashed;

    public int PointIndex => pointIndex;
    public bool IsSlashed => isSlashed;
    public LegendarySamurai OwnerBoss => boss;

    protected override void Awake()
    {
        col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            if (spriteRenderer.color.a > 0f) normalColor = spriteRenderer.color;
            originalColor = normalColor;
            spriteRenderer.sortingOrder = 7;
        }

        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
        }

        DeactivatePoint();
    }

    public void Initialize(LegendarySamurai ownerBoss, int index)
    {
        boss = ownerBoss;
        pointIndex = index;
    }

    public void ActivatePoint(Vector3 worldPos)
    {
        transform.position = worldPos;
        isSlashed = false;
        IsDead = false;
        isTarget = false;

        if (spriteRenderer != null)
        {
            spriteRenderer.color = normalColor;
            spriteRenderer.enabled = true;
        }

        if (col != null) col.enabled = true;

        gameObject.SetActive(true);
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

    public override void TrySelectTarget()
    {
        if (IsDead || isSlashed || isTarget) return;

        if (PlayerController.Instance != null && PlayerController.Instance.IsInMode)
        {
            isTarget = true;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = selectedColor;
            }

            PlayerController.Instance.TargetSetup(transform);
        }
    }

    protected override void Update()
    {
        if (IsDead || isSlashed || !gameObject.activeSelf) return;

        if (!isTarget && PlayerController.Instance != null && PlayerController.Instance.IsInMode)
        {
            CheckMouseHover();
        }
    }

    protected override void CheckMouseHover()
    {
        if (isTarget || col == null) return;

        Camera cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (cam == null || UnityEngine.InputSystem.Mouse.current == null) return;

        Vector3 screenPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        screenPos.z = -cam.transform.position.z;
        Vector2 mouseWorld = cam.ScreenToWorldPoint(screenPos);

        if (col.OverlapPoint(mouseWorld) || Vector2.Distance(transform.position, mouseWorld) <= 0.6f)
        {
            TrySelectTarget();
        }
    }

    public override void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        TrySelectTarget();
    }

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        if (isSlashed || !isTarget) return;
        isSlashed = true;
        IsDead = true;

        if (slashVFXPrefab != null) Instantiate(slashVFXPrefab, transform.position, Quaternion.identity);
        if (slashSFX != null && PlayerController.Instance != null) PlayerController.Instance.PlaySFX(slashSFX);

        if (spriteRenderer != null) spriteRenderer.enabled = false;
        if (col != null) col.enabled = false;

        gameObject.SetActive(false);

        if (boss != null)
        {
            boss.OnTargetPointSlashed(this, deathImpulse);
        }
    }

    // Lightweight stubs to prevent enemy pathfinding/jumping
    protected override void Movement() {}
    protected override void HandleJump(Vector2 dir) {}
    protected override void HandlePlayerContact(Collision2D collision) {}
    protected override void HandlePlayerTriggerContact(Collider2D other) {}
    protected override bool HandleThrownImpact(Collision2D collision) => false;
    protected override void FlipAndRotate() {}
    protected override void UpdateTargetIndicator() {}

    private static Sprite cachedTargetSprite;
    public static Sprite GetOrCreateTargetPointSprite()
    {
        if (cachedTargetSprite != null) return cachedTargetSprite;

        int size = 24;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0);
        Color core = new Color(1f, 0.95f, 0.8f, 1f);
        Color border = new Color(0.95f, 0.25f, 0.9f, 1f);

        Vector2 center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
        float radius = size * 0.45f;
        float coreRadius = radius * 0.45f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                if (dist <= coreRadius)
                {
                    tex.SetPixel(x, y, core);
                }
                else if (dist <= radius)
                {
                    tex.SetPixel(x, y, border);
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

