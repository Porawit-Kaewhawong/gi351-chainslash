using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

public class ChainEnemy : MonoBehaviour, IPointerEnterHandler
{
    public static bool isExploding = false;

    [Header("Chain Enemy Settings")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private GameObject targetIndicator;
    [SerializeField] private AudioClip explosionSound;
    [SerializeField] private GameObject explosionRadiusPrefab;
    [SerializeField] private float explosionRadius = 5f;
    public bool IsDead { get; private set; }
    private bool isTarget = false;
    private Rigidbody2D rb;
    private GameObject insExplosion;
    private SpriteRenderer spriteRenderer;
    private PlayerController playerController;

    private void Awake()
    {
        explosionRadius = explosionRadius / 2;
        spriteRenderer = GetComponent<SpriteRenderer>();
        playerController = FindFirstObjectByType<PlayerController>();
        insExplosion = Instantiate(explosionRadiusPrefab, transform.position, Quaternion.identity);
        insExplosion.SetActive(false);
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        insExplosion.transform.position = transform.position;

        Movement();

        if (IsDead) return;

        if (PlayerCombat.Instance.IsInMode && !isTarget)
        {
            if (targetIndicator != null)
            {
                targetIndicator.SetActive(true);
            }
        }
        else if (!PlayerCombat.Instance.IsInMode && !isTarget)
        {
            if (targetIndicator != null)
            {
                targetIndicator.SetActive(false);
            }
            insExplosion.SetActive(false);
        }
        else if (!PlayerCombat.Instance.IsInMode && isTarget)
        {
            insExplosion.SetActive(false);
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
            insExplosion.SetActive(true);
        }
    }

    public IEnumerator OnDeath()
    {
        if (IsDead) yield break;
        IsDead = true;

        while (isExploding)
        {
            yield return null;
        }

        isExploding = true;

        yield return new WaitForSeconds(0.25f);

        Collider2D[] withinRange = Physics2D.OverlapCircleAll(transform.position, explosionRadius);

        foreach (Collider2D collider in withinRange)
        {
            if (collider == null || collider.gameObject == gameObject) continue;

            if (collider.TryGetComponent<Enemy>(out Enemy enemy))
            {
                if (enemy != null && enemy.IsDead == false)
                {
                    enemy.OnDeath();
                }
            }
        }

        foreach (Collider2D collider in withinRange)
        {
            if (collider == null || collider.gameObject == gameObject) continue;

            if (collider.TryGetComponent<ChainEnemy>(out ChainEnemy chainEnemy))
            {
                if (chainEnemy != null && chainEnemy.IsDead == false)
                {
                    chainEnemy.StartCoroutine(chainEnemy.OnDeath());
                }
            }
        }

        insExplosion.SetActive(true);

        playerController.PlaySFX(explosionSound);

        isExploding = false;

        PlayerCombat.Instance.ShakingTrigger(1f, 0.25f);

        Destroy(insExplosion, 0.5f);
        Destroy(gameObject);
    }

    void Movement()
    {
        // Here
        Vector2 dir = (playerController.gameObject.transform.position - gameObject.transform.position).normalized;
        rb.AddForce(dir * moveSpeed);
    }
}
