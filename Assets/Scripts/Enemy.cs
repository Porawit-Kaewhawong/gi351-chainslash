using UnityEngine;
using UnityEngine.EventSystems;

public class Enemy : MonoBehaviour, IPointerEnterHandler
{
    [Header("Enemy Settings")]
    [SerializeField] private GameObject targetIndicator;

    private bool isTarget = false;
    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
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
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (PlayerCombat.Instance.isInMode && !isTarget)
        {
            PlayerCombat.Instance.targetEnemies.Add(gameObject);
            spriteRenderer.color = Color.purple;
            PlayerCombat.Instance.LineSetup();
            isTarget = true;

            targetIndicator.SetActive(false);
        }
    }
}
