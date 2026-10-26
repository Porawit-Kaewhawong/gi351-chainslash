using System.Collections;
using UnityEngine;

public class ChainEnemy : EnemyBase
{
    public static bool isExploding = false;

    [Header("Suicide Aggression Settings")]
    [SerializeField] private float aggroDistance = 4.5f;
    [SerializeField] private float aggroSpeedMultiplier = 1.8f;

    [Header("Target Fuse Settings")]
    [SerializeField] private float targetFuseDuration = 2.0f;

    [Header("Explosion Settings")]
    [SerializeField] private float explosionDamage = 35f;
    [SerializeField] private AudioClip explosionSound;
    [SerializeField] private GameObject explosionPrefab;
    [SerializeField] private GameObject explosionRadiusPrefab;
    [SerializeField] private float explosionRadius = 5f;
    [SerializeField] private float explosionShakeDuration = 0.25f;

    private GameObject insExplosionRadius;
    private Coroutine fuseCoroutine;
    private bool isAggro;

    protected override float CurrentMoveSpeed => isAggro ? moveSpeed * aggroSpeedMultiplier : moveSpeed;

    protected override void Start()
    {
        base.Start();

        if (explosionRadiusPrefab != null)
        {
            insExplosionRadius = Instantiate(explosionRadiusPrefab, transform.position, Quaternion.identity);
            insExplosionRadius.SetActive(false);
        }
    }

    protected override void Update()
    {
        base.Update();

        if (IsDead) return;

        if (insExplosionRadius != null && insExplosionRadius.activeSelf)
        {
            insExplosionRadius.transform.position = transform.position;
        }

        if (playerController != null)
        {
            isAggro = Vector2.Distance(transform.position, playerController.transform.position) <= aggroDistance;
        }
    }

    protected override void OnCollisionEnter2D(Collision2D collision)
    {
        // Detonate immediately if Suicide Ninja collides with the Player
        if (!IsDead && collision.gameObject.TryGetComponent<PlayerController>(out PlayerController player))
        {
            OnDeath();
            return;
        }

        base.OnCollisionEnter2D(collision);
    }

    private void OnDestroy()
    {
        if (insExplosionRadius != null)
        {
            Destroy(insExplosionRadius);
        }
    }

    public override void TrySelectTarget()
    {
        bool wasTarget = isTarget;
        base.TrySelectTarget();

        if (!wasTarget && isTarget && fuseCoroutine == null)
        {
            fuseCoroutine = StartCoroutine(TargetFuseCountdown());
        }
    }

    private IEnumerator TargetFuseCountdown()
    {
        float timer = 0f;

        while (timer < targetFuseDuration)
        {
            timer += Time.deltaTime;

            if (spriteRenderer != null)
            {
                spriteRenderer.color = (Mathf.Sin(timer * 20f) > 0) ? Color.red : Color.purple;
            }

            yield return null;
        }

        OnDeath();
    }

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        if (IsDead) return;

        if (fuseCoroutine != null)
        {
            StopCoroutine(fuseCoroutine);
        }

        StartCoroutine(ExplosionSequence());
    }

    private IEnumerator ExplosionSequence()
    {
        IsDead = true;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.UnregisterEnemy(gameObject);
        }

        while (isExploding)
        {
            yield return null;
        }

        isExploding = true;

        Vector3 origScale = transform.localScale;
        float buildUpTime = 0.25f;
        float elapsed = 0f;

        while (elapsed < buildUpTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / buildUpTime;

            transform.localScale = Vector3.Lerp(origScale, origScale * 1.35f, t);
            if (spriteRenderer != null)
            {
                spriteRenderer.color = (Mathf.Sin(elapsed * 35f) > 0) ? Color.red : Color.white;
            }
            yield return null;
        }

        // Execute Explosion Damage on both Player and neighboring Enemies
        float effectiveRadius = explosionRadius * 0.5f;
        Collider2D[] withinRange = Physics2D.OverlapCircleAll(transform.position, effectiveRadius);

        foreach (Collider2D collider in withinRange)
        {
            if (collider == null || collider.gameObject == gameObject) continue;

            if (collider.TryGetComponent<PlayerController>(out PlayerController player))
            {
                player.TakeDamage(explosionDamage);

                if (collider.TryGetComponent<Rigidbody2D>(out Rigidbody2D playerRb))
                {
                    Vector2 pushDir = (player.transform.position - transform.position).normalized;
                    if (pushDir == Vector2.zero) pushDir = Vector2.up;
                    playerRb.AddForce(pushDir * 12f, ForceMode2D.Impulse);
                }
            }
            else if (collider.TryGetComponent<EnemyBase>(out EnemyBase enemy))
            {
                if (!enemy.IsDead)
                {
                    enemy.OnDeath();
                }
            }
        }

        if (explosionPrefab != null)
        {
            GameObject fx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            Destroy(fx, 0.5f);
        }

        if (playerController != null)
        {
            if (explosionSound != null)
            {
                playerController.PlaySFX(explosionSound);
            }

            playerController.StartCoroutine(playerController.ShakingTrigger(explosionShakeDuration));
        }

        isExploding = false;

        Destroy(insExplosionRadius);
        Destroy(gameObject);
    }
}