using UnityEngine;
using UnityEngine.EventSystems;

public class ChainEnemy : MonoBehaviour, IPointerEnterHandler
{
    [Header("Chain Enemy Settings")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private GameObject targetIndicator;
    [SerializeField] private GameObject explosionRadiusPrefab;
    [SerializeField] private float explosionRadius = 5f;
    private bool isDead = false;
    private bool isTarget = false;
    private Rigidbody2D rb;
    private GameObject theObject;
    private SpriteRenderer spriteRenderer;
    private PlayerController playerController;

    private void Awake()
    {
        explosionRadius = explosionRadius / 2;
        spriteRenderer = GetComponent<SpriteRenderer>();
        playerController = FindFirstObjectByType<PlayerController>();
        theObject = Instantiate(explosionRadiusPrefab, transform.position, Quaternion.identity);
        theObject.SetActive(false);
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (isDead) return;
        theObject.transform.position = transform.position;

        if (PlayerCombat.Instance.isInMode && !isTarget)
        {
            if (targetIndicator != null)
            {
                targetIndicator.SetActive(true);
            }
        }
        else if (!PlayerCombat.Instance.isInMode && !isTarget)
        {
            if (targetIndicator != null)
            {
                targetIndicator.SetActive(false);
            }
            theObject.SetActive(false);
        }
        else if (!PlayerCombat.Instance.isInMode && isTarget)
        {
            theObject.SetActive(false);
        }

        Movement();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (PlayerCombat.Instance.isInMode && !isTarget)
        {
            spriteRenderer.color = Color.purple;
            PlayerCombat.Instance.TargetSetup(transform);
            isTarget = true;

            if (targetIndicator != null)
            {
                targetIndicator.SetActive(false);
            }
            theObject.SetActive(true);
        }
    }

    public void OnDeath()
    {
        if (isDead) return;
        isDead = true;

        Collider2D[] withinRange = Physics2D.OverlapCircleAll(transform.position, explosionRadius);

        foreach (Collider2D collider in withinRange)
        {
            if (collider.gameObject == gameObject) continue;

            if (collider.TryGetComponent<Enemy>(out Enemy enemy))
            {
                if (enemy != null)
                {
                    enemy.OnDeath();
                }
            }
            else if (collider.TryGetComponent<ChainEnemy>(out ChainEnemy chainEnemy))
            {
                if (chainEnemy != null)
                {
                    chainEnemy.OnDeath();
                }
            }
        }

        theObject.SetActive(true);

        Destroy(theObject, 0.5f);
        Destroy(gameObject);
    }

    void Movement()
    {
        // Here
        Vector2 dir = (playerController.gameObject.transform.position - gameObject.transform.position).normalized;
        rb.AddForce(dir * moveSpeed);
    }
}
