using Unity.Android.Types;
using UnityEngine;
using UnityEngine.EventSystems;

public class ChainEnemy : MonoBehaviour, IPointerEnterHandler
{
    [Header("Chain Enemy Settings")]
    [SerializeField] private GameObject targetIndicator;

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
        }
        else if (!PlayerCombat.Instance.isInMode && !isTarget)
        {
            if (targetIndicator != null)
            {
                targetIndicator.SetActive(false);
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
        }
    }

    public void OnDeath()
    {
        Collider2D[] withinRange = Physics2D.OverlapCircleAll(transform.position, 5f);

        foreach (Collider2D collider in withinRange)
        {
            if (collider.CompareTag("Enemy"))
            {
                Destroy(collider.gameObject);
            }
        }
    }
}
