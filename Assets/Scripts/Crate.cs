using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Attached to a GameObject on the "Crate" layer.
/// Can be selected and targeted while the player is in Chain Slash mode,
/// and is destroyed after exiting the mode (via slash animation or mode exit).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class Crate : MonoBehaviour, IPointerEnterHandler, ISlashable
{
    [Header("Layer Configuration")]
    [Tooltip("Designated layer name for this destructible crate.")]
    [SerializeField] private string targetLayerName = "Crate";

    [Tooltip("If true, automatically ensures this GameObject and its children are assigned to the target layer at startup.")]
    [SerializeField] private bool autoAssignLayer = true;

    [Tooltip("If true, only allows selection if this GameObject is on the designated layer.")]
    [SerializeField] private bool requireLayerMatch = false;

    [Header("Targeting / Chain Slash Mode")]
    [Tooltip("Color tint applied when targeted in Chain Slash mode.")]
    [SerializeField] private Color selectedColor = new Color(0.2f, 0.95f, 1f, 1f);

    [Tooltip("Outline color applied when targeted in Chain Slash mode.")]
    [SerializeField] private Color targetableColor = new Color(1f, 0.95f, 0.35f, 1f);

    [Tooltip("Pixel outline thickness when targeted.")]
    [SerializeField] private float outlineThickness = 2.0f;

    [Tooltip("Intensity of the jitter/shake effect while queued in Chain Slash mode.")]
    [SerializeField] private float targetJitterStrength = 0.035f;

    [Header("Destruction Settings")]
    [Tooltip("If true, destroys the crate when exiting mode even if the slash was cancelled or missed.")]
    [SerializeField] private bool destroyOnAnyModeExit = true;

    [Tooltip("If true, destroys immediately the instant the player exits mode rather than waiting for the slash line animation.")]
    [SerializeField] private bool destroyImmediatelyOnModeExit = false;

    [Tooltip("Grace period (in seconds) after exiting mode to allow the slash line animation to reach this crate before triggering fallback destruction.")]
    [SerializeField] private float modeExitGracePeriod = 0.35f;

    [Tooltip("Optional VFX prefab spawned upon destruction (e.g. ExplosionPrefab, wood shards).")]
    [SerializeField] private GameObject breakVFXPrefab;

    [Tooltip("Audio clip played when destroyed.")]
    [SerializeField] private AudioClip breakSFX;

    [Range(0f, 1f)]
    [SerializeField] private float breakSFXVolume = 0.9f;

    [Tooltip("Camera shake duration triggered on destruction.")]
    [SerializeField] private float breakCameraShake = 0.12f;

    [Header("Procedural Wood Debris")]
    [Tooltip("Whether to spawn procedural wood debris fragments on destruction.")]
    [SerializeField] private bool spawnDebris = true;

    [Tooltip("Number of procedural wood debris fragments to spawn.")]
    [SerializeField] private int debrisPiecesCount = 5;

    [Tooltip("Explosion force applied to procedural debris.")]
    [SerializeField] private float debrisExplosionForce = 7f;

    [Tooltip("Color tint applied to procedural wood fragments.")]
    [SerializeField] private Color debrisColor = new Color(0.62f, 0.42f, 0.22f, 1f);

    [Header("Optional Loot / Item Drop")]
    [Tooltip("Optional item/pickup prefab to drop upon destruction.")]
    [SerializeField] private GameObject dropItemPrefab;

    [Range(0f, 1f)]
    [SerializeField] private float dropChance = 0.5f;

    // Public Status Properties
    public bool IsTarget => isTarget;
    public bool IsDestroyed => isDestroyed;

    // Internal State
    private bool isTarget = false;
    private bool isDestroyed = false;
    private bool wasInMode = false;

    // Cached References
    private Collider2D col;
    private SpriteRenderer spriteRenderer;
    private PixelSpriteEffect pixelEffect;
    private Vector3 originalSpriteLocalPos;
    private Color originalSpriteColor = Color.white;
    private Material originalSpriteMaterial;

    // Continuous mouse swipe tracking
    private Vector2 lastMousePos;
    private bool hasLastMousePos = false;
    private Coroutine fallbackDestroyCoroutine;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        if (col == null) col = GetComponentInChildren<Collider2D>();

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        pixelEffect = GetComponent<PixelSpriteEffect>();
        if (pixelEffect == null) pixelEffect = GetComponentInChildren<PixelSpriteEffect>();

        if (spriteRenderer != null)
        {
            originalSpriteLocalPos = spriteRenderer.transform.localPosition;
            originalSpriteColor = spriteRenderer.color;
            originalSpriteMaterial = spriteRenderer.sharedMaterial;
        }

        EnforceCrateLayer();
    }

    private void OnValidate()
    {
        if (col == null) col = GetComponent<Collider2D>();
        if (col == null) col = GetComponentInChildren<Collider2D>();

        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (pixelEffect == null) pixelEffect = GetComponent<PixelSpriteEffect>();
        if (pixelEffect == null) pixelEffect = GetComponentInChildren<PixelSpriteEffect>();
    }

    private void EnforceCrateLayer()
    {
        if (!autoAssignLayer) return;

        int crateLayer = LayerMask.NameToLayer(targetLayerName);
        if (crateLayer != -1 && gameObject.layer != crateLayer)
        {
            SetLayerRecursively(gameObject, crateLayer);
        }
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    private bool IsLayerValid()
    {
        if (!requireLayerMatch) return true;

        int crateLayer = LayerMask.NameToLayer(targetLayerName);
        if (crateLayer == -1) return true; // Layer not registered yet, do not block

        return gameObject.layer == crateLayer;
    }

    private void Update()
    {
        if (isDestroyed) return;

        PlayerController player = PlayerController.Instance;
        bool inMode = player != null && player.IsInMode;

        // While in Chain Slash mode and not yet targeted, detect selection
        if (inMode && !isTarget)
        {
            CheckMouseSelection();
        }

        // Apply visual jitter while selected during the mode
        if (inMode && isTarget)
        {
            ApplyTargetJitter();
        }

        // Handle transitioning out of Chain Slash mode
        if (wasInMode && !inMode)
        {
            HandleModeExited();
        }

        wasInMode = inMode;
    }

    #region Mouse & Target Selection

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (CanSelectTarget())
        {
            TrySelectTarget();
        }
    }

    private void CheckMouseSelection()
    {
        if (isTarget || col == null) return;

        Camera cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (cam == null || Mouse.current == null) return;

        Vector3 screenPos = Mouse.current.position.ReadValue();
        screenPos.z = -cam.transform.position.z;
        Vector2 currentMouse = cam.ScreenToWorldPoint(screenPos);

        bool isHit = col.OverlapPoint(currentMouse);

        // Fast swipe interpolation check if the mouse skipped over the collider between frames
        if (!isHit && hasLastMousePos)
        {
            RaycastHit2D lineHit = Physics2D.Linecast(lastMousePos, currentMouse, 1 << gameObject.layer);
            if (lineHit.collider == col)
            {
                isHit = true;
            }
            else if (LineIntersectsBounds(col.bounds, lastMousePos, currentMouse))
            {
                isHit = true;
            }
        }

        lastMousePos = currentMouse;
        hasLastMousePos = true;

        if (isHit && CanSelectTarget())
        {
            TrySelectTarget();
        }
    }

    private bool CanSelectTarget()
    {
        if (isDestroyed || isTarget) return false;

        PlayerController player = PlayerController.Instance;
        if (player == null || !player.IsInMode) return false;

        if (!IsLayerValid()) return false;

        return true;
    }

    /// <summary>
    /// Selects this crate during Chain Slash mode and registers it with the player's target chain.
    /// </summary>
    public void TrySelectTarget()
    {
        if (!CanSelectTarget()) return;

        isTarget = true;

        // Register with PlayerController and LineRendererManager
        PlayerController player = PlayerController.Instance;
        if (player != null)
        {
            player.TargetSetup(transform);
        }

        ApplySelectionVisuals();
    }

    private void ApplySelectionVisuals()
    {
        if (pixelEffect != null)
        {
            pixelEffect.SetOutline(true, targetableColor, outlineThickness, PixelSpriteEffect.OutlineMode.Both);
            pixelEffect.Flash(0.08f, Color.white);
        }
        else if (spriteRenderer != null)
        {
            spriteRenderer.color = selectedColor;
        }
    }

    private void ApplyTargetJitter()
    {
        if (spriteRenderer == null || targetJitterStrength <= 0f) return;

        Vector2 jitter = Random.insideUnitCircle * targetJitterStrength;
        spriteRenderer.transform.localPosition = originalSpriteLocalPos + new Vector3(jitter.x, jitter.y, 0f);
    }

    #endregion

    #region Destruction

    /// <summary>
    /// Implementation of ISlashable. Invoked by PlayerController.AnimateKillLine when the slash animation strikes this crate.
    /// </summary>
    public void OnSlashed(Vector2 slashVector)
    {
        if (isDestroyed) return;
        ExecuteDestruction(slashVector);
    }

    private void HandleModeExited()
    {
        if (!isTarget || isDestroyed) return;

        if (destroyImmediatelyOnModeExit)
        {
            ExecuteDestruction(Vector2.zero);
            return;
        }

        if (destroyOnAnyModeExit)
        {
            // Allow the slash line animation (AnimateKillLine) to reach this crate and trigger OnSlashed.
            // If OnSlashed does not run within the grace period (e.g. cancelled mode), fallback destroys it.
            if (fallbackDestroyCoroutine != null) StopCoroutine(fallbackDestroyCoroutine);
            fallbackDestroyCoroutine = StartCoroutine(FallbackModeExitDestroyRoutine());
        }
        else
        {
            // Reset targeting visuals if not configured to destroy on arbitrary cancellation
            ResetSelectionVisuals();
        }
    }

    private IEnumerator FallbackModeExitDestroyRoutine()
    {
        yield return new WaitForSeconds(modeExitGracePeriod);
        if (!isDestroyed && isTarget)
        {
            ExecuteDestruction(Vector2.zero);
        }
    }

    private void ResetSelectionVisuals()
    {
        isTarget = false;
        hasLastMousePos = false;

        if (pixelEffect != null)
        {
            pixelEffect.SetOutline(false);
        }

        if (spriteRenderer != null)
        {
            if (originalSpriteMaterial != null)
            {
                spriteRenderer.sharedMaterial = originalSpriteMaterial;
            }
            spriteRenderer.color = originalSpriteColor;
            spriteRenderer.transform.localPosition = originalSpriteLocalPos;
        }
    }

    /// <summary>
    /// Executes the destruction sequence, spawning VFX, SFX, procedural debris, camera shake, and optional loot.
    /// </summary>
    public void ExecuteDestruction(Vector2 slashVector)
    {
        if (isDestroyed) return;
        isDestroyed = true;

        if (fallbackDestroyCoroutine != null)
        {
            StopCoroutine(fallbackDestroyCoroutine);
            fallbackDestroyCoroutine = null;
        }

        // Disable all colliders immediately so neither player nor enemies collide
        if (col != null) col.enabled = false;
        foreach (Collider2D c in GetComponentsInChildren<Collider2D>())
        {
            c.enabled = false;
        }

        // Trigger camera shake
        if (breakCameraShake > 0f && PlayerController.Instance != null)
        {
            PlayerController.Instance.TriggerCameraShake(breakCameraShake);
        }

        // Spawn destruction VFX prefab if assigned
        if (breakVFXPrefab != null)
        {
            Instantiate(breakVFXPrefab, transform.position, Quaternion.identity);
        }

        // Play destruction SFX
        if (breakSFX != null)
        {
            AudioSource.PlayClipAtPoint(breakSFX, transform.position, breakSFXVolume);
        }

        // Spawn procedural wood debris fragments
        if (spawnDebris && debrisPiecesCount > 0)
        {
            SpawnWoodDebris(slashVector);
        }

        // Drop loot item if configured
        if (dropItemPrefab != null && Random.value <= dropChance)
        {
            Instantiate(dropItemPrefab, transform.position, Quaternion.identity);
        }

        // Hide renderers immediately
        if (spriteRenderer != null) spriteRenderer.enabled = false;
        foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>())
        {
            sr.enabled = false;
        }

        // Destroy the GameObject
        Destroy(gameObject, 0.05f);
    }

    #endregion

    #region Procedural Wood Debris

    private void SpawnWoodDebris(Vector2 slashVector)
    {
        Sprite sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        Vector2 baseImpulse = slashVector.normalized * (debrisExplosionForce * 0.75f);

        for (int i = 0; i < debrisPiecesCount; i++)
        {
            GameObject fragment = new GameObject($"CrateDebris_{i}");
            fragment.transform.position = transform.position + (Vector3)(Random.insideUnitCircle * 0.2f);
            fragment.transform.localScale = Vector3.one * Random.Range(0.2f, 0.45f);

            SpriteRenderer sr = fragment.AddComponent<SpriteRenderer>();
            sr.sprite = sprite != null ? sprite : GetFallbackDebrisSprite();
            sr.color = debrisColor;
            sr.sortingLayerID = spriteRenderer != null ? spriteRenderer.sortingLayerID : 0;
            sr.sortingOrder = (spriteRenderer != null ? spriteRenderer.sortingOrder : 3) + 1;

            Rigidbody2D rb = fragment.AddComponent<Rigidbody2D>();
            rb.gravityScale = 2.5f;

            Vector2 randomDir = (Random.insideUnitCircle + Vector2.up * 0.5f).normalized;
            Vector2 launchVel = baseImpulse + randomDir * Random.Range(2f, debrisExplosionForce);
            rb.linearVelocity = launchVel;
            rb.angularVelocity = Random.Range(-360f, 360f);

            // Start lifetime fade out
            StartCoroutine(DebrisLifetimeRoutine(fragment, sr, Random.Range(0.5f, 0.8f)));
        }
    }

    private static Sprite cachedDebrisSprite;
    private static Sprite GetFallbackDebrisSprite()
    {
        if (cachedDebrisSprite == null)
        {
            Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            Color[] colors = new Color[16];
            for (int i = 0; i < 16; i++) colors[i] = Color.white;
            tex.SetPixels(colors);
            tex.Apply();
            cachedDebrisSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 16f);
        }
        return cachedDebrisSprite;
    }

    private static IEnumerator DebrisLifetimeRoutine(GameObject fragment, SpriteRenderer sr, float lifetime)
    {
        float timer = 0f;
        Color startColor = sr != null ? sr.color : Color.white;

        while (timer < lifetime && fragment != null)
        {
            timer += Time.deltaTime;
            float alpha = Mathf.Clamp01(1f - (timer / lifetime));
            if (sr != null)
            {
                sr.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            }
            yield return null;
        }

        if (fragment != null)
        {
            Destroy(fragment);
        }
    }

    #endregion

    #region Helper Geometry

    private static bool LineIntersectsBounds(Bounds bounds, Vector2 p1, Vector2 p2)
    {
        Vector2 min = bounds.min;
        Vector2 max = bounds.max;

        if (bounds.Contains(p1) || bounds.Contains(p2)) return true;

        return LineIntersectsSegment(p1, p2, new Vector2(min.x, min.y), new Vector2(min.x, max.y)) ||
               LineIntersectsSegment(p1, p2, new Vector2(min.x, max.y), new Vector2(max.x, max.y)) ||
               LineIntersectsSegment(p1, p2, new Vector2(max.x, max.y), new Vector2(max.x, min.y)) ||
               LineIntersectsSegment(p1, p2, new Vector2(max.x, min.y), new Vector2(min.x, min.y));
    }

    private static bool LineIntersectsSegment(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        Vector2 b = a2 - a1;
        Vector2 d = b2 - b1;
        float bDotDPerp = b.x * d.y - b.y * d.x;

        if (Mathf.Approximately(bDotDPerp, 0f)) return false;

        Vector2 c = b1 - a1;
        float t = (c.x * d.y - c.y * d.x) / bDotDPerp;
        if (t < 0f || t > 1f) return false;

        float u = (c.x * b.y - c.y * b.x) / bDotDPerp;
        if (u < 0f || u > 1f) return false;

        return true;
    }

    #endregion
}
