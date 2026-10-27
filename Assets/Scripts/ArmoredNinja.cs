using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArmoredNinja : EnemyBase
{
    [Header("Armored Ninja Settings")]
    [SerializeField] private float slowMoveSpeed = 1.35f;
    [SerializeField] private Color armorColor = new Color(0.65f, 0.75f, 0.9f, 1f);
    [SerializeField] private Color hitFlashColor = Color.white;
    [SerializeField] private AudioClip armorClangSFX;
    [SerializeField] private GameObject hitVFXPrefab;
    [SerializeField] private GameObject armorShatterVFX;

    [Header("Immunity Settings")]
    [SerializeField] private bool immuneToShurikens = true;
    [SerializeField] private bool immuneToExplosions = true;

    [Header("Target Circles Settings")]
    [SerializeField] private int requiredWeakPointCount = 3;
    [SerializeField] private GameObject weakPointPrefab;
    [SerializeField] private float circleOrbitRadiusMin = 1.8f;
    [SerializeField] private float circleOrbitRadiusMax = 2.4f;

    private readonly List<ArmoredNinjaWeakPoint> weakPoints = new List<ArmoredNinjaWeakPoint>();
    private Transform weakPointContainer;
    private int slashedPointsCount = 0;
    private bool isDefeated = false;
    private bool areWeakPointsActive = false;
    private float modeExitTimer = 0f;
    private Coroutine flashCoroutine;

    protected override void Awake()
    {
        base.Awake();

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
        CreateWeakPointContainer();
    }

    private void CreateWeakPointContainer()
    {
        weakPoints.Clear();

        GameObject containerObj = new GameObject("WeakPointContainer");
        weakPointContainer = containerObj.transform;
        weakPointContainer.SetParent(transform, false);
        weakPointContainer.localPosition = new Vector3(0f, 0.65f, 0f);

        for (int i = 0; i < requiredWeakPointCount; i++)
        {
            ArmoredNinjaWeakPoint point;

            if (weakPointPrefab != null)
            {
                GameObject obj = Instantiate(weakPointPrefab, weakPointContainer);
                point = obj.GetComponent<ArmoredNinjaWeakPoint>();
                if (point == null) point = obj.AddComponent<ArmoredNinjaWeakPoint>();
            }
            else
            {
                GameObject obj = new GameObject($"WeakPoint_{i}");
                obj.transform.SetParent(weakPointContainer, false);
                obj.layer = gameObject.layer;

                SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
                if (spriteRenderer != null) sr.sprite = spriteRenderer.sprite;
                sr.color = new Color(1f, 0.6f, 0.15f, 1f);
                sr.sortingOrder = 7;
                obj.transform.localScale = Vector3.one * 1.5f;

                CircleCollider2D circleCol = obj.AddComponent<CircleCollider2D>();
                circleCol.radius = 0.5f;
                circleCol.isTrigger = true;

                point = obj.AddComponent<ArmoredNinjaWeakPoint>();
            }

            point.Initialize(this, i);
            point.Deactivate();
            weakPoints.Add(point);
        }
    }

    public override void TrySelectTarget()
    {
        if (IsDead || isDefeated) return;

        bool wasTarget = isTarget;
        base.TrySelectTarget();

        // When the armored enemy is targeted in Chain Slash mode, display 3 random circles around itself
        if (!wasTarget && isTarget)
        {
            ShowRandomCircles();
        }
    }

    private void ShowRandomCircles()
    {
        slashedPointsCount = 0;
        areWeakPointsActive = true;
        modeExitTimer = 0f;

        Vector3 centerPos = weakPointContainer != null
            ? weakPointContainer.position
            : transform.position + new Vector3(0f, 0.65f, 0f);

        List<Vector2> placedOffsets = new List<Vector2>();

        for (int i = 0; i < weakPoints.Count; i++)
        {
            if (weakPoints[i] == null) continue;

            Vector2 offset = Vector2.zero;
            bool foundValid = false;

            // Pick a fully random angle and distance, with just enough separation so circles don't completely overlap
            for (int attempt = 0; attempt < 15; attempt++)
            {
                float angle = Random.Range(0f, 360f);
                float dist = Random.Range(circleOrbitRadiusMin, circleOrbitRadiusMax);
                offset = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * dist;

                bool tooClose = false;
                for (int j = 0; j < placedOffsets.Count; j++)
                {
                    if (Vector2.Distance(offset, placedOffsets[j]) < 0.85f)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    foundValid = true;
                    break;
                }
            }

            if (!foundValid)
            {
                float angle = Random.Range(0f, 360f);
                float dist = Random.Range(circleOrbitRadiusMin, circleOrbitRadiusMax);
                offset = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * dist;
            }

            placedOffsets.Add(offset);
            Vector3 worldPos = centerPos + (Vector3)offset;
            weakPoints[i].ActivateAndShow(worldPos);
        }
    }

    private void ResetWeakPoints()
    {
        slashedPointsCount = 0;
        areWeakPointsActive = false;
        isTarget = false;

        for (int i = 0; i < weakPoints.Count; i++)
        {
            if (weakPoints[i] != null)
            {
                weakPoints[i].Deactivate();
            }
        }
    }

    protected override void Update()
    {
        base.Update();

        if (IsDead || isDefeated) return;

        // Reset circles if the player leaves Chain Slash mode without slashing (grace period for animation)
        if (areWeakPointsActive && (PlayerController.Instance == null || !PlayerController.Instance.IsInMode))
        {
            modeExitTimer += Time.unscaledDeltaTime;
            if (modeExitTimer > 0.45f)
            {
                ResetWeakPoints();
            }
        }
        else
        {
            modeExitTimer = 0f;
        }
    }

    public void OnWeakPointSlashed(ArmoredNinjaWeakPoint point, Vector2 slashImpulse)
    {
        if (IsDead || isDefeated) return;

        slashedPointsCount++;
        TriggerFlash();

        if (slashedPointsCount >= requiredWeakPointCount)
        {
            DefeatArmoredNinja(slashImpulse);
        }
    }

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        if (IsDead || isDefeated) return;

        // If player managed to select all circles, the enemy dies!
        if (AreAllCirclesSelected() || slashedPointsCount >= requiredWeakPointCount)
        {
            DefeatArmoredNinja(deathImpulse);
            return;
        }

        // If not, the enemy won't take any damage and plays SFX for failed hit
        HandleDeflectedHit();
    }

    private bool AreAllCirclesSelected()
    {
        if (weakPoints.Count < requiredWeakPointCount) return false;

        for (int i = 0; i < weakPoints.Count; i++)
        {
            if (weakPoints[i] == null || (!weakPoints[i].IsTarget && !weakPoints[i].IsSlashed))
            {
                return false;
            }
        }
        return true;
    }

    public bool IsImmuneToShurikens => immuneToShurikens;
    public bool IsImmuneToExplosions => immuneToExplosions;

    public void DeflectProjectile(Vector2 hitPoint)
    {
        if (IsDead || isDefeated) return;
        PlayClangSFX();
        TriggerFlash();
    }

    public void OnExplosionHit(Vector3 explosionPos)
    {
        if (IsDead || isDefeated) return;
        PlayClangSFX();
        TriggerFlash();
    }

    private void HandleDeflectedHit()
    {
        PlayClangSFX();
        TriggerFlash();
        StartCoroutine(ResetAfterDeflectRoutine());
    }

    private IEnumerator ResetAfterDeflectRoutine()
    {
        yield return new WaitForSeconds(0.2f);
        ResetWeakPoints();
    }

    private void PlayClangSFX()
    {
        if (armorClangSFX != null && playerController != null)
        {
            playerController.PlayPitchedSFX(armorClangSFX, Random.Range(0.96f, 1.04f), 1f);
        }
    }

    private void TriggerFlash()
    {
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(ArmorFlashRoutine());
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

    private void DefeatArmoredNinja(Vector2 impulse)
    {
        if (isDefeated) return;
        isDefeated = true;
        IsDead = true;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.UnregisterEnemy(gameObject);
        }

        if (col != null) col.enabled = false;

        GameObject fx = armorShatterVFX != null ? armorShatterVFX : hitVFXPrefab;
        if (fx != null)
        {
            Instantiate(fx, transform.position, Quaternion.identity);
        }

        if (impulse != default && rb != null)
        {
            rb.AddForce(impulse * 15f, ForceMode2D.Impulse);
        }

        CleanUpWeakPoints();
        Destroy(gameObject);
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

public class ArmoredNinjaWeakPoint : EnemyBase
{
    [Header("Weak Point Settings")]
    [SerializeField] private Color normalColor = new Color(1f, 0.6f, 0.15f, 1f);
    [SerializeField] private Color selectedColor = new Color(0.2f, 0.95f, 1f, 1f);
    [SerializeField] private GameObject breakVFX;
    [SerializeField] private AudioClip breakSFX;

    private ArmoredNinja ownerNinja;
    private int pointIndex;
    private bool isSlashed = false;

    public int PointIndex => pointIndex;
    public bool IsSlashed => isSlashed;

    protected override void Awake()
    {
        // Lightweight setup: avoid heavy EnemyBase Awake (outlines, patrol, physics)
        col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            if (spriteRenderer.color.a > 0f) normalColor = spriteRenderer.color;
            originalColor = normalColor;
            spriteRenderer.sortingOrder = 7;
        }

        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
        }

        Deactivate();
    }

    public void Initialize(ArmoredNinja ninja, int index)
    {
        ownerNinja = ninja;
        pointIndex = index;
    }

    public void ActivateAndShow(Vector3 worldPos)
    {
        transform.position = worldPos;
        isSlashed = false;
        IsDead = false;
        isTarget = false;

        if (spriteRenderer != null)
        {
            spriteRenderer.color = normalColor;
            spriteRenderer.enabled = true;
        }

        if (col != null) col.enabled = true;

        gameObject.SetActive(true);
    }

    public void Deactivate()
    {
        isSlashed = false;
        IsDead = false;
        isTarget = false;

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false;
        }

        if (col != null) col.enabled = false;

        gameObject.SetActive(false);
    }

    public override void TrySelectTarget()
    {
        if (IsDead || isSlashed || isTarget) return;

        if (PlayerController.Instance != null && PlayerController.Instance.IsInMode)
        {
            isTarget = true;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = selectedColor;
            }

            PlayerController.Instance.TargetSetup(transform);
        }
    }

    protected override void Update()
    {
        if (IsDead || isSlashed || !gameObject.activeSelf) return;

        if (!isTarget && PlayerController.Instance != null && PlayerController.Instance.IsInMode)
        {
            CheckMouseHover();
        }
    }

    protected override void CheckMouseHover()
    {
        if (isTarget || col == null) return;

        Camera cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (cam == null || UnityEngine.InputSystem.Mouse.current == null) return;

        Vector3 screenPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        screenPos.z = -cam.transform.position.z;
        Vector2 mouseWorld = cam.ScreenToWorldPoint(screenPos);

        // Check collider or generous proximity for smooth mouse swipes
        if (col.OverlapPoint(mouseWorld) || Vector2.Distance(transform.position, mouseWorld) <= 0.6f)
        {
            TrySelectTarget();
        }
    }

    public override void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        TrySelectTarget();
    }

    public override void OnDeath(Vector2 deathImpulse = default)
    {
        // Weak points can ONLY be triggered when targeted by the player's chain slash
        if (isSlashed || !isTarget) return;
        isSlashed = true;
        IsDead = true;

        if (breakVFX != null)
        {
            Instantiate(breakVFX, transform.position, Quaternion.identity);
        }

        if (breakSFX != null && PlayerController.Instance != null)
        {
            PlayerController.Instance.PlaySFX(breakSFX);
        }

        if (spriteRenderer != null) spriteRenderer.enabled = false;
        if (col != null) col.enabled = false;

        if (ownerNinja != null)
        {
            ownerNinja.OnWeakPointSlashed(this, deathImpulse);
        }
    }

    // Stubs to disable enemy movement/patrol behaviors for stationary circles
    protected override void Movement() {}
    protected override void UpdateTargetIndicator() {}
    protected override void FlipAndRotate() {}
    protected override void HandlePlayerContact(Collision2D collision) {}
    protected override void HandlePlayerTriggerContact(Collider2D other) {}
    protected override bool HandleThrownImpact(Collision2D collision) => false;
}

