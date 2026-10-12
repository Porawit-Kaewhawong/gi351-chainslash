using UnityEngine;
using UnityEngine.EventSystems;

public class ChainEnemy : MonoBehaviour, IPointerEnterHandler
{
    [Header("Chain Enemy Settings")]
    [SerializeField] private GameObject targetIndicator;
    [SerializeField] private GameObject explosionRadiusPrefab;
    [SerializeField] private float explosionRadius = 5f;
    private GameObject theObject;
    private bool isDead;
    private bool isTarget = false;
    private SpriteRenderer spriteRenderer;
    private PlayerController playerController;

    private void Awake()
    {
        explosionRadius = explosionRadius / 2;
        spriteRenderer = GetComponent<SpriteRenderer>();
        playerController = FindFirstObjectByType<PlayerController>();
        theObject = Instantiate(explosionRadiusPrefab, transform.position, Quaternion.identity);
        theObject.SetActive(false);
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

        transform.Translate((playerController.gameObject.transform.position - gameObject.transform.position) * 0.5f * Time.deltaTime);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (PlayerCombat.Instance.isInMode && !isTarget)
        {
            PlayerCombat.Instance.targetEnemies.Add(gameObject);
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
            isDead = true;
        }
        Destroy(theObject);
        Destroy(gameObject);
    }
}
