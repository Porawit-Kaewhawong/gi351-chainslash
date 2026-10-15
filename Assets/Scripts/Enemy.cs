using UnityEngine;
using UnityEngine.EventSystems;

public class Enemy : MonoBehaviour, IPointerEnterHandler
{
    [Header("Enemy Settings")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private GameObject targetIndicator;
    private bool isTarget = false;
    public bool IsDead { get; private set; }
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private PlayerController playerController;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        playerController = FindFirstObjectByType<PlayerController>();
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        Movement();

        if (IsDead) return;

        if (PlayerCombat.Instance.IsInMode && !isTarget)
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
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (PlayerCombat.Instance.IsInMode && !isTarget)
        {
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
        if (IsDead) return;
        IsDead = true;
        
        Destroy(gameObject);
    }

    void Movement()
    {
        // Here
        Vector2 dir = (playerController.gameObject.transform.position - gameObject.transform.position).normalized;
        rb.AddForce(dir * moveSpeed);
    }
}
