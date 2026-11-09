using UnityEngine;

/// <summary>
/// Basic Ninja enemy with proximity flanking and evasive dodge behavior.
/// </summary>
[DisallowMultipleComponent]
public class Enemy : EnemyBase
{
    #region Serialized Fields

    [Header("Flanking / Dodge Settings")]
    [SerializeField] private float dodgeTriggerDistance = 3.5f;
    [SerializeField] private float dodgeForce = 9f;
    [SerializeField] private float dodgeCooldown = 3f;

    #endregion

    #region Protected & Private State

    private float nextDodgeTime;

    #endregion

    #region Unity Lifecycle

    protected override void Update()
    {
        base.Update();

        if (!IsDead && currentState == State.Normal)
        {
            CheckPlayerProximityDodge();
        }
    }

    #endregion

    #region Movement & AI

    private void CheckPlayerProximityDodge()
    {
        if (playerController == null || Time.time < nextDodgeTime) return;

        Vector2 toPlayer = (Vector2)playerController.transform.position - (Vector2)transform.position;
        float distSq = toPlayer.sqrMagnitude;
        float triggerDistSq = dodgeTriggerDistance * dodgeTriggerDistance;

        if (distSq <= triggerDistSq && distSq > 0.0001f)
        {
            nextDodgeTime = Time.time + dodgeCooldown;
            Vector2 awayDir = -toPlayer.normalized;
            awayDir.y = 0.5f;

            currentState = State.Dodging;
            if (rb != null)
            {
                rb.linearVelocity = awayDir.normalized * dodgeForce;
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
}
