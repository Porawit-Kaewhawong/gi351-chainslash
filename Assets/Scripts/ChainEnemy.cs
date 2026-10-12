using UnityEngine;
using UnityEngine.EventSystems;

public class ChainEnemy : MonoBehaviour, IPointerEnterHandler
{
    [Header("Chain Enemy Settings")]
    [SerializeField] private GameObject targetIndicator;
    public GameObject explosionPrefab;

    private bool isTarget = false;
    private SpriteRenderer spriteRenderer;
    private PlayerController playerController;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        playerController = FindFirstObjectByType<PlayerController>();
    }

    void Update()
    {
        if (PlayerCombat.Instance.isInMode && !isTarget)
        {
            if (targetIndicator != null)
            {
                targetIndicator.SetActive(true);
            }
            if (explosionPrefab != null)
            {
                explosionPrefab.SetActive(true);
            }
        }
        else if (!PlayerCombat.Instance.isInMode && !isTarget)
        {
            if (targetIndicator != null)
            {
                targetIndicator.SetActive(false);
            }
            if (explosionPrefab != null)
            {
                explosionPrefab.SetActive(false);
            }
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
            if (explosionPrefab != null)
            {
                explosionPrefab.SetActive(true);
            }
        }
    }

    public void OnDeath(GameObject target)
    {
        Collider2D explosionCollider = GetComponent<Collider2D>();
        Collider2D[] withinColliders = Physics2D.OverlapCircleAll(explosionPrefab.transform.position, explosionCollider.density);
        foreach (Collider2D withinEnemy in withinColliders)
        {
            if (withinEnemy.gameObject.GetComponent<Enemy>() && withinEnemy.gameObject.CompareTag("Enemy"))
            {
                Destroy(withinEnemy);
            }
        }
    }
}
