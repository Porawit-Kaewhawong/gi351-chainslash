using UnityEngine;

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

        float distToPlayer = Vector2.Distance(transform.position, playerController.transform.position);

        if (distToPlayer <= dodgeTriggerDistance)
        {
            nextDodgeTime = Time.time + dodgeCooldown;
            Vector2 awayDir = (transform.position - playerController.transform.position).normalized;
            awayDir.y = 0.5f;

            currentState = State.Dodging;
            rb.linearVelocity = awayDir.normalized * dodgeForce;
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

        if (deathImpulse != default)
        {
            rb.AddForce(deathImpulse * 15f, ForceMode2D.Impulse);
        }

        Destroy(gameObject);
    }
}