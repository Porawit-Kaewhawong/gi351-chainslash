using UnityEngine;

/// <summary>
/// Basic Ninja enemy with proximity flanking and evasive dodge behavior.
/// </summary>
[DisallowMultipleComponent]
public class Enemy : EnemyBase
{
    [Header("Flanking / Dodge Settings")]
    [SerializeField] private float dodgeTriggerDistance = 3.5f;
    [SerializeField] private float dodgeForce = 9f;
    [SerializeField] private float dodgeCooldown = 3f;

    private float nextDodgeTime;

    protected override void Update()
    {
        base.Update();

        if (!IsDead && currentState == State.Normal)
        {
            CheckPlayerProximityDodge();
        }
    }

    private void CheckPlayerProximityDodge()
    {
        if (playerController == null || Time.time < nextDodgeTime) return;

        Vector2 toPlayer = (Vector2)playerController.transform.position - (Vector2)transform.position;
        float triggerDistSq = dodgeTriggerDistance * dodgeTriggerDistance;

        if (toPlayer.sqrMagnitude <= triggerDistSq && toPlayer.sqrMagnitude > 0.0001f)
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

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        if (IsDead) return;
        IsDead = true;

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
}