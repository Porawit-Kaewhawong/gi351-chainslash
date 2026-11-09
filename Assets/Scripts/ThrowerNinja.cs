using System.Collections;
using UnityEngine;

public class ThrowerNinja : EnemyBase
{
    #region Serialized Fields

    [Header("Thrower Ninja Settings")]
    [SerializeField] private GameObject shurikenPrefab;
    [SerializeField] private float throwInterval = 3f;
    [SerializeField] private float throwRange = 13f;
    [SerializeField] private float minThrowDistance = 2.5f;
    [SerializeField] private float shurikenSpeed = 11f;
    [SerializeField] private float shurikenDamage = 12f;
    [SerializeField] private float throwWindup = 0.25f;
    [SerializeField] private Color throwTelegraphColor = new Color(1f, 0.55f, 0.1f, 1f);
    [SerializeField] private Transform throwPoint;
    [SerializeField] private AudioClip throwSFX;

    [Header("Death Projectiles Settings")]
    [SerializeField] private int deathProjectileCount = 2;
    [SerializeField] private float deathProjectileSpeed = 13.5f;

    #endregion

    #region Protected & Private State

    private float nextThrowTime;
    private bool isThrowing;
    private WaitForSeconds cachedWindupWait;
    private float cachedWindupDuration = -1f;

    #endregion

    #region Unity Lifecycle

    protected override void Start()
    {
        base.Start();
        nextThrowTime = Time.time + Random.Range(1f, throwInterval);
    }

    protected override void Update()
    {
        base.Update();

        if (IsDead || IsGrappleStunned || playerController == null || isThrowing) return;

        // Do not throw shurikens while telegraphing a dive or diving
        if (currentState == State.Telegraphing || currentState == State.Diving) return;

        float distSq = ((Vector2)transform.position - (Vector2)playerController.transform.position).sqrMagnitude;

        if (distSq >= (minThrowDistance * minThrowDistance) && distSq <= (throwRange * throwRange))
        {
            if (Time.time >= nextThrowTime)
            {
                StartCoroutine(ThrowShurikenRoutine());
            }
        }
    }

    #endregion

    #region Combat & Targeting

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        if (IsDead) return;
        IsDead = true;

        TrySpawnDeathSlice(deathImpulse);
        SpawnDeathShurikens();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.UnregisterEnemy(gameObject);
        }

        if (deathImpulse != default && rb != null)
        {
            rb.AddForce(deathImpulse * 15f, ForceMode2D.Impulse);
        }

        Destroy(gameObject);
    }

    #endregion

    #region Helpers

    private IEnumerator ThrowShurikenRoutine()
    {
        isThrowing = true;
        nextThrowTime = Time.time + throwInterval;

        // Telegraph windup flash
        if (spriteRenderer != null)
        {
            spriteRenderer.color = throwTelegraphColor;
        }

        if (!Mathf.Approximately(cachedWindupDuration, throwWindup))
        {
            cachedWindupDuration = throwWindup;
            cachedWindupWait = new WaitForSeconds(throwWindup);
        }

        yield return cachedWindupWait;

        if (spriteRenderer != null && !isTarget)
        {
            spriteRenderer.color = originalColor;
        }

        if (IsDead || IsGrappleStunned || playerController == null)
        {
            isThrowing = false;
            yield break;
        }

        // Aim towards player's center
        Vector2 origin = throwPoint != null ? (Vector2)throwPoint.position : (Vector2)transform.position + Vector2.up * 0.5f;
        Vector2 targetPos = (Vector2)playerController.transform.position + Vector2.up * 0.35f;
        Vector2 aimDir = (targetPos - origin).normalized;

        // Spawn Shuriken
        if (shurikenPrefab != null)
        {
            GameObject proj = Instantiate(shurikenPrefab, origin, Quaternion.identity);
            if (proj.TryGetComponent<Shuriken>(out Shuriken shurikenComp))
            {
                shurikenComp.Launch(aimDir, shurikenSpeed, shurikenDamage);
            }
        }
        else
        {
            Shuriken.CreateDefaultShuriken(origin, aimDir, shurikenSpeed, shurikenDamage, groundLayer);
        }

        if (throwSFX != null && playerController != null)
        {
            playerController.PlaySFX(throwSFX);
        }

        isThrowing = false;
    }

    private void SpawnDeathShurikens()
    {
        Vector2 spawnOrigin = throwPoint != null ? (Vector2)throwPoint.position : (Vector2)transform.position + Vector2.up * 0.5f;

        // Throw 2 shurikens in random spread-out directions that damage other enemies
        float baseAngle = Random.Range(0f, 360f);
        float angleStep = 360f / Mathf.Max(1, deathProjectileCount);

        for (int i = 0; i < deathProjectileCount; i++)
        {
            float rad = (baseAngle + (i * angleStep) + Random.Range(-25f, 25f)) * Mathf.Deg2Rad;
            Vector2 randomDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

            GameObject proj = shurikenPrefab != null
                ? Instantiate(shurikenPrefab, spawnOrigin, Quaternion.identity)
                : Shuriken.CreateDefaultShuriken(spawnOrigin, randomDir, deathProjectileSpeed, shurikenDamage, groundLayer);

            if (proj != null && proj.TryGetComponent<Shuriken>(out Shuriken shurikenComp))
            {
                shurikenComp.SetCanDamageEnemies(true, gameObject);
                shurikenComp.Launch(randomDir, deathProjectileSpeed, shurikenDamage);
            }
        }

        if (throwSFX != null && playerController != null)
        {
            playerController.PlaySFX(throwSFX);
        }
    }

    #endregion
}
