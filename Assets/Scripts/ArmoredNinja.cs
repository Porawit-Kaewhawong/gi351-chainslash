using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArmoredNinja : Enemy
{
    [Header("Armored Ninja Settings")]
    [SerializeField] private float slowMoveSpeed = 1.35f;
    [SerializeField] private Color armorColor = new Color(0.65f, 0.75f, 0.9f, 1f);
    [SerializeField] private Color hitFlashColor = Color.white;
    [SerializeField] private AudioClip armorClangSFX;
    [SerializeField] private GameObject hitVFXPrefab;
    [SerializeField] private GameObject armorShatterVFX;

    [Header("Back Target Points Settings")]
    [SerializeField] private int requiredWeakPointCount = 3;
    [SerializeField] private GameObject weakPointPrefab;
    [Tooltip("Local offsets on the back (when facing right, X is flipped when facing left)")]
    [SerializeField] private Vector2[] backPointOffsets = new Vector2[]
    {
        new Vector2(-0.45f, 0.95f),  // Upper back
        new Vector2(-0.55f, 0.65f),  // Mid back
        new Vector2(-0.45f, 0.35f)   // Lower back
    };

    private List<ArmoredNinjaWeakPoint> weakPoints = new List<ArmoredNinjaWeakPoint>();
    private int slashedPointsCount = 0;
    private Coroutine flashCoroutine;

    public int SlashedPointsCount => slashedPointsCount;
    public int TotalWeakPoints => requiredWeakPointCount;
    public bool AllPointsSlashed => slashedPointsCount >= requiredWeakPointCount;

    protected override void Awake()
    {
        base.Awake();

        // Moves slower than regular ninja and has heavy resistance
        moveSpeed = slowMoveSpeed;
        isHeavyEnemy = true;

        if (armorColor.a > 0f)
        {
            originalColor = armorColor;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = armorColor;
            }
        }
    }

    protected override void Start()
    {
        base.Start();

        SpawnOrInitializeWeakPoints();
    }

    private void SpawnOrInitializeWeakPoints()
    {
        weakPoints.Clear();

        for (int i = 0; i < requiredWeakPointCount; i++)
        {
            ArmoredNinjaWeakPoint point;

            if (weakPointPrefab != null)
            {
                GameObject obj = Instantiate(weakPointPrefab, transform.position, Quaternion.identity);
                point = obj.GetComponent<ArmoredNinjaWeakPoint>();
                if (point == null) point = obj.AddComponent<ArmoredNinjaWeakPoint>();
            }
            else
            {
                GameObject obj = new GameObject($"ArmoredNinja_WeakPoint_{i}");
                obj.layer = gameObject.layer;
                obj.tag = gameObject.tag;

                CircleCollider2D circleCol = obj.AddComponent<CircleCollider2D>();
                circleCol.radius = 0.38f;
                circleCol.isTrigger = true;

                Rigidbody2D kinematicRb = obj.AddComponent<Rigidbody2D>();
                kinematicRb.bodyType = RigidbodyType2D.Kinematic;
                kinematicRb.gravityScale = 0f;

                SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
                sr.sprite = ArmoredNinjaWeakPoint.GetOrCreateWeakPointSprite();
                sr.sortingOrder = 7;

                point = obj.AddComponent<ArmoredNinjaWeakPoint>();
            }

            point.Initialize(this, i);

            Vector2 offset = (i < backPointOffsets.Length) ? backPointOffsets[i] : new Vector2(-0.5f, 0.6f);
            point.Activate((Vector2)transform.position + offset);
            weakPoints.Add(point);
        }
    }

    protected override void Update()
    {
        base.Update();

        if (IsDead) return;

        UpdateWeakPointsPosition();
    }

    private void UpdateWeakPointsPosition()
    {
        // When facing right (spriteRenderer.flipX == false), the back is on the left (-X).
        // When facing left (spriteRenderer.flipX == true), the back is on the right (+X).
        bool facingLeft = (spriteRenderer != null && spriteRenderer.flipX);
        float sideMultiplier = facingLeft ? -1f : 1f;

        for (int i = 0; i < weakPoints.Count; i++)
        {
            if (weakPoints[i] == null || weakPoints[i].IsSlashed) continue;

            Vector2 offset = (i < backPointOffsets.Length) ? backPointOffsets[i] : new Vector2(-0.5f, 0.6f);
            // Flip X offset according to facing direction so it stays on the back
            offset.x *= sideMultiplier;

            weakPoints[i].transform.position = (Vector2)transform.position + offset;
        }
    }

    public void OnWeakPointSlashed(ArmoredNinjaWeakPoint point, Vector2 slashImpulse)
    {
        if (IsDead) return;

        slashedPointsCount++;

        // Visual flash feedback on the ninja
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(ArmorFlashRoutine());

        // Darken armor slightly as points are broken
        float damageRatio = (float)slashedPointsCount / requiredWeakPointCount;
        originalColor = Color.Lerp(armorColor, new Color(0.35f, 0.38f, 0.45f, 1f), damageRatio);

        // When all 3 points have been slashed, the ninja is defeated!
        if (slashedPointsCount >= requiredWeakPointCount)
        {
            DefeatArmoredNinja(slashImpulse);
        }
    }

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        if (IsDead) return;

        // The ninja can only be killed after all 3 points have been slashed!
        if (slashedPointsCount < requiredWeakPointCount)
        {
            // Direct slashes to the body ping off the armor
            if (armorClangSFX != null && playerController != null)
            {
                playerController.PlaySFX(armorClangSFX);
            }

            if (flashCoroutine != null) StopCoroutine(flashCoroutine);
            flashCoroutine = StartCoroutine(ArmorFlashRoutine());

            // Reset target state on body so player can target the back points
            isTarget = false;
            return;
        }

        // All 3 points have been slashed - execute defeat
        DefeatArmoredNinja(deathImpulse);
    }

    private void DefeatArmoredNinja(Vector2 impulse)
    {
        if (IsDead) return;
        IsDead = true;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.UnregisterEnemy(gameObject);
        }

        if (armorShatterVFX != null)
        {
            Instantiate(armorShatterVFX, transform.position, Quaternion.identity);
        }
        else if (hitVFXPrefab != null)
        {
            Instantiate(hitVFXPrefab, transform.position, Quaternion.identity);
        }

        if (impulse != default)
        {
            rb.AddForce(impulse * 15f, ForceMode2D.Impulse);
        }

        CleanUpWeakPoints();
        Destroy(gameObject);
    }

    private IEnumerator ArmorFlashRoutine()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = hitFlashColor;
        }

        yield return new WaitForSeconds(0.12f);

        if (spriteRenderer != null && !isTarget)
        {
            spriteRenderer.color = originalColor;
        }
    }

    private void CleanUpWeakPoints()
    {
        for (int i = 0; i < weakPoints.Count; i++)
        {
            if (weakPoints[i] != null && weakPoints[i].gameObject != null)
            {
                Destroy(weakPoints[i].gameObject);
            }
        }
        weakPoints.Clear();
    }

    private void OnDestroy()
    {
        CleanUpWeakPoints();
    }
}

