using System.Collections;
using UnityEngine;

public class ChainEnemy : EnemyBase
{
    #region Enums & Constants

    public static bool isExploding = false;
    private static readonly Collider2D[] ExplosionBuffer = new Collider2D[32];

    #endregion

    #region Serialized Fields

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

    #endregion

    #region Public Properties

    protected override float CurrentMoveSpeed => isAggro ? moveSpeed * aggroSpeedMultiplier : moveSpeed;

    #endregion

    #region Protected & Private State

    private GameObject insExplosionRadius;
    private Coroutine fuseCoroutine;
    private bool isAggro;
    private bool ownsExplodingLock;

    #endregion

    #region Unity Lifecycle

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
            Vector2 toPlayer = (Vector2)transform.position - (Vector2)playerController.transform.position;
            isAggro = toPlayer.sqrMagnitude <= (aggroDistance * aggroDistance);
        }
    }

    private void OnDestroy()
    {
        if (ownsExplodingLock)
        {
            isExploding = false;
            ownsExplodingLock = false;
        }

        if (insExplosionRadius != null)
        {
            Destroy(insExplosionRadius);
        }
    }

    #endregion

    #region Collision & Physics

    protected override void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsDead) return;

        // If punted into a wall or another enemy after a Grapple Kick, detonate on impact!
        if (HandleThrownImpact(collision))
        {
            return;
        }

        // Detonate immediately if Suicide Ninja collides with the Player (unless kicked away at high speed)
        if (collision.gameObject.TryGetComponent<PlayerController>(out PlayerController player))
        {
            if (player.TryExecuteGrappleKick(this))
            {
                if (fuseCoroutine == null)
                {
                    fuseCoroutine = StartCoroutine(TargetFuseCountdown());
                }
                return;
            }

            OnDeath();
            return;
        }

        base.OnCollisionEnter2D(collision);
    }

    #endregion

    #region Combat & Targeting

    public override void TrySelectTarget()
    {
        bool wasTarget = isTarget;
        base.TrySelectTarget();

        if (!wasTarget && isTarget && fuseCoroutine == null)
        {
            fuseCoroutine = StartCoroutine(TargetFuseCountdown());
        }
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

    #endregion

    #region Explosion & Helpers

    private IEnumerator TargetFuseCountdown()
    {
        float timer = 0f;

        while (timer < targetFuseDuration)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        OnDeath();
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
        ownsExplodingLock = true;

        Vector3 origScale = transform.localScale;
        Vector3 targetScale = origScale * 1.35f;
        const float buildUpTime = 0.25f;
        float elapsed = 0f;

        while (elapsed < buildUpTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / buildUpTime;

            transform.localScale = Vector3.Lerp(origScale, targetScale, t);
            if (spriteRenderer != null)
            {
                spriteRenderer.color = (Mathf.Sin(elapsed * 35f) > 0f) ? Color.red : Color.white;
            }
            yield return null;
        }

        // Trigger explosion SFX and camera shake before chain-reacting neighboring enemies
        PlayerController targetPlayer = playerController != null ? playerController : PlayerController.Instance;
        if (targetPlayer != null)
        {
            if (explosionSound != null)
            {
                targetPlayer.PlaySFX(explosionSound);
            }

            targetPlayer.TriggerCameraShake(explosionShakeDuration);
        }

        if (explosionPrefab != null)
        {
            GameObject fx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            Destroy(fx, 0.5f);
        }

        // Release lock before triggering chain-reaction deaths so chained ChainEnemies can immediately sequence
        isExploding = false;
        ownsExplodingLock = false;

        // Execute Explosion Damage on both Player and neighboring Enemies
        float effectiveRadius = explosionRadius * 0.5f;
        int hitCount = Physics2D.OverlapCircle((Vector2)transform.position, effectiveRadius, ContactFilter2D.noFilter, ExplosionBuffer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hitCollider = ExplosionBuffer[i];
            if (hitCollider == null || hitCollider.gameObject == gameObject) continue;

            if (hitCollider.TryGetComponent<PlayerController>(out PlayerController player))
            {
                player.TakeDamage(explosionDamage);

                if (hitCollider.TryGetComponent<Rigidbody2D>(out Rigidbody2D playerRb))
                {
                    Vector2 pushDir = (player.transform.position - transform.position).normalized;
                    if (pushDir == Vector2.zero) pushDir = Vector2.up;
                    playerRb.AddForce(pushDir * 12f, ForceMode2D.Impulse);
                }
            }
            else if (hitCollider.TryGetComponent<EnemyBase>(out EnemyBase enemy))
            {
                if (!enemy.IsDead)
                {
                    // Legendary Samurai takes explosion damage
                    LegendarySamurai boss = hitCollider.GetComponentInParent<LegendarySamurai>();
                    if (boss != null || enemy is LegendarySamurai || enemy is BossTargetPoint)
                    {
                        if (boss != null)
                        {
                            boss.TakeExplosionHit(transform.position, explosionDamage);
                        }
                        continue;
                    }

                    // Armored Ninja takes explosion damage and shatters
                    ArmoredNinja armoredNinja = hitCollider.GetComponentInParent<ArmoredNinja>();
                    if (armoredNinja != null || enemy is ArmoredNinja || enemy is ArmoredNinjaWeakPoint || hitCollider.GetComponentInParent<ArmoredNinjaWeakPoint>() != null)
                    {
                        if (armoredNinja != null)
                        {
                            armoredNinja.TakeExplosionDamage(transform.position);
                        }
                        continue;
                    }

                    enemy.OnDeath();
                }
            }
        }

        if (insExplosionRadius != null)
        {
            Destroy(insExplosionRadius);
        }
        Destroy(gameObject);
    }

    #endregion
}
