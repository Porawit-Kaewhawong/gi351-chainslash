using UnityEngine;
using UnityEngine.EventSystems;

public class Enemy : MonoBehaviour, IPointerEnterHandler
{
    [Header("Enemy Settings")]
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
            targetIndicator.SetActive(true);
        }
        else
        {
            targetIndicator.SetActive(false);
        }

        transform.Translate((playerController.gameObject.transform.position - gameObject.transform.position) * 0.5f * Time.deltaTime);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (PlayerCombat.Instance.isInMode && !isTarget)
        {
            PlayerCombat.Instance.targetEnemies.Add(gameObject);
            spriteRenderer.color = Color.purple;
            PlayerCombat.Instance.LineSetup(transform);
            isTarget = true;

            targetIndicator.SetActive(false);
        }
    }
}
