using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Shuriken : MonoBehaviour
{
    [Header("Shuriken Properties")]
    [SerializeField] private float speed = 11f;
    [SerializeField] private float damage = 12f;
    [SerializeField] private float spinSpeed = 720f;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private AudioClip hitSFX;

    private Vector2 moveDirection = Vector2.right;
    private Rigidbody2D rb;
    private float spawnTime;
    private bool hasHit = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void Start()
    {
        spawnTime = Time.time;
    }

    public void Launch(Vector2 direction, float customSpeed = -1f, float customDamage = -1f)
    {
        moveDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        if (customSpeed > 0f) speed = customSpeed;
        if (customDamage > 0f) damage = customDamage;

        if (rb != null)
        {
            rb.linearVelocity = moveDirection * speed;
        }
    }

    private void Update()
    {
        // Continuous spin rotation
        transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);

        // Backup movement if no Rigidbody2D or non-dynamic
        if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic)
        {
            transform.position += (Vector3)(moveDirection * speed * Time.deltaTime);
        }

        // Lifetime expiry
        if (Time.time >= spawnTime + lifetime && !hasHit)
        {
            Destroy(gameObject);
        }
    }

    private void FixedUpdate()
    {
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = moveDirection * speed;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleHit(other.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HandleHit(collision.gameObject);
    }

    private void HandleHit(GameObject other)
    {
        if (hasHit) return;

        // Ignore enemy collisions so shurikens don't collide with enemies
        if (other.GetComponentInParent<EnemyBase>() != null) return;

        // Player hit
        if (other.TryGetComponent<PlayerController>(out PlayerController player) ||
            (other.GetComponentInParent<PlayerController>() is PlayerController parentPlayer && (player = parentPlayer) != null))
        {
            hasHit = true;
            player.TakeDamage(damage);
            PlayHitFeedback(transform.position);
            Destroy(gameObject);
            return;
        }

        // Ground / Wall hit
        if (groundLayer != 0 && ((1 << other.layer) & groundLayer) != 0)
        {
            hasHit = true;
            PlayHitFeedback(transform.position);
            Destroy(gameObject);
            return;
        }
    }

    private void PlayHitFeedback(Vector3 position)
    {
        if (hitEffectPrefab != null)
        {
            Instantiate(hitEffectPrefab, position, Quaternion.identity);
        }

        if (hitSFX != null && PlayerController.Instance != null)
        {
            PlayerController.Instance.PlaySFX(hitSFX);
        }
    }

    private static Sprite cachedSprite;
    public static Sprite GetOrCreateShurikenSprite()
    {
        if (cachedSprite != null) return cachedSprite;

        int size = 16;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0);
        Color steel = new Color(0.92f, 0.94f, 0.98f, 1f);
        Color darkSteel = new Color(0.45f, 0.5f, 0.6f, 1f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, clear);
            }
        }

        int center = size / 2;
        // Central hub
        for (int y = center - 1; y <= center; y++)
        {
            for (int x = center - 1; x <= center; x++)
            {
                tex.SetPixel(x, y, darkSteel);
            }
        }

        // 4 blades extending outward
        for (int i = 1; i <= 6; i++)
        {
            // Up blade
            tex.SetPixel(center, center + i, steel);
            tex.SetPixel(center - 1, center + i, darkSteel);

            // Down blade
            tex.SetPixel(center - 1, center - 1 - i, steel);
            tex.SetPixel(center, center - 1 - i, darkSteel);

            // Right blade
            tex.SetPixel(center + i, center - 1, steel);
            tex.SetPixel(center + i, center, darkSteel);

            // Left blade
            tex.SetPixel(center - 1 - i, center, steel);
            tex.SetPixel(center - 1 - i, center - 1, darkSteel);
        }

        tex.Apply();
        cachedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        return cachedSprite;
    }

    public static GameObject CreateDefaultShuriken(Vector2 position, Vector2 direction, float speed, float damage, LayerMask groundLayer)
    {
        GameObject obj = new GameObject("Shuriken");
        obj.transform.position = position;

        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateShurikenSprite();
        sr.color = Color.white;
        sr.sortingOrder = 5;

        CircleCollider2D col = obj.AddComponent<CircleCollider2D>();
        col.radius = 0.35f;
        col.isTrigger = true;

        Rigidbody2D rb = obj.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        Shuriken shuriken = obj.AddComponent<Shuriken>();
        shuriken.groundLayer = groundLayer;
        shuriken.Launch(direction, speed, damage);

        return obj;
    }
}

