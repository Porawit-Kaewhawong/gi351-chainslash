using UnityEngine;
using UnityEngine.EventSystems;

public class Enemy : MonoBehaviour, IPointerEnterHandler
{
    [Header("Enemy Settings")]
    [SerializeField] private GameObject targetIndicator;

    private bool isTarget = false;
    private bool isDead = false;
    private SpriteRenderer spriteRenderer;
    private PlayerController playerController;
    private PlayerCombat playerCombat;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        playerController = FindFirstObjectByType<PlayerController>();
        playerCombat = FindFirstObjectByType<PlayerCombat>();
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
        else
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
        if (isDead) return;
        isDead = true;
        
        Destroy(gameObject);
    }
}
