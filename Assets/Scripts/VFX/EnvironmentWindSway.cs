using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Brings environment props (Trees, Bushes, Grass, Flowers, Wheat, Banners, Lanterns) to life
/// with realistic base-anchored wind sway, rolling forest wind waves, and Player walk/dash rustle physics.
///
/// Automatically scans and animates matching props on every scene load (Stage0 - Stage5),
/// and can also be added directly onto any specific GameObject to customize its sway in the Inspector.
/// </summary>
[AddComponentMenu("VFX/Environment Wind Sway")]
public class EnvironmentWindSway : MonoBehaviour
{
    public enum PropSwayCategory
    {
        AutoDetect,
        Tree,
        BushOrGrass,
        HangingBannerOrLantern
    }

    #region Serialized Fields (Per-Object Override)

    [Header("Sway Profile")]
    [SerializeField] private PropSwayCategory category = PropSwayCategory.AutoDetect;

    [Tooltip("Maximum sway angle in degrees under normal breeze.")]
    [SerializeField] private float swayAngleDegrees = 3.2f;

    [Tooltip("Primary wind oscillation speed.")]
    [SerializeField] private float swaySpeed = 1.45f;

    [Tooltip("Extra bend angle during periodic rolling wind gusts.")]
    [SerializeField] private float gustStrength = 2.4f;

    [Tooltip("Subtle canopy breathing/rustle squash-stretch amplitude.")]
    [SerializeField] private float foliageBreathAmount = 0.018f;

    [Header("Player Interaction (Walk / Dash Rustle)")]
    [Tooltip("If true, the prop bends and springs back when the Player runs or dashes past it.")]
    [SerializeField] private bool reactToPlayerMovement = true;

    [Tooltip("How strongly the Player's speed bends this prop when passing by.")]
    [SerializeField] private float playerPushStrength = 1.15f;

    [Tooltip("Horizontal distance from the prop within which the Player rustles it.")]
    [SerializeField] private float playerInteractRadius = 1.35f;

    #endregion

    #region Private Runtime State

    private SpriteRenderer sr;
    private Vector3 baseLocalPos;
    private Quaternion baseLocalRot;
    private Vector3 baseLocalScale;
    private Vector3 localPivotOffset; // Offset from transform.position to base (roots) or top (hanging)
    private float phaseOffset;
    private float worldXSeed;
    private bool isHanging;
    private bool initialized;

    // Damped spring physics for Player walk/dash rustle
    private float springAngle;
    private float springVelocity;

    #endregion

    #region Auto-Attachment Across All Stages

    private static bool sceneHookRegistered;

    private static readonly string[] TreeKeywords =
    {
        "tree", "sakura", "pine", "bamboo", "oak", "birch", "willow", "canopy", "foliage", "palm"
    };

    private static readonly string[] BushGrassKeywords =
    {
        "bush", "grass", "flower", "wheat", "plant", "fern", "shrub", "vine", "herb", "weed", "moss", "clover", "reed", "hedge"
    };

    private static readonly string[] HangingKeywords =
    {
        "banner", "flag", "lantern", "lamp", "hanger", "cloth", "curtain", "sign", "windchime", "bell", "tassel"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterAutoAttachHook()
    {
        if (!sceneHookRegistered)
        {
            sceneHookRegistered = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        AttachToSceneProps();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AttachToSceneProps();
    }

    private static void AttachToSceneProps()
    {
        SpriteRenderer[] allRenderers = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        for (int i = 0; i < allRenderers.Length; i++)
        {
            SpriteRenderer renderer = allRenderers[i];
            if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
            if (renderer.GetComponent<EnvironmentWindSway>() != null) continue;
            if (renderer.GetComponentInParent<EnvironmentWindSway>() != null) continue;

            // Skip dynamic actors, enemies, player, crates, portals, and scrolling backgrounds
            if (renderer.GetComponentInParent<PlayerController>() != null) continue;
            if (renderer.GetComponentInParent<EnemyBase>() != null) continue;
            if (renderer.GetComponentInParent<Crate>() != null) continue;
            if (renderer.GetComponentInParent<PortalNextLevel>() != null) continue;
            if (renderer.GetComponentInParent<DynamicBackground>() != null) continue;
            if (renderer.GetComponentInParent<Rigidbody2D>() != null) continue;

            string goName = renderer.gameObject.name.ToLowerInvariant();
            string parentName = renderer.transform.parent != null ? renderer.transform.parent.name.ToLowerInvariant() : string.Empty;
            string spriteName = renderer.sprite != null ? renderer.sprite.name.ToLowerInvariant() : string.Empty;

            if (TryDetectCategory(goName, parentName, spriteName, out PropSwayCategory detected))
            {
                EnvironmentWindSway sway = renderer.gameObject.AddComponent<EnvironmentWindSway>();
                sway.ConfigurePreset(detected);
            }
        }
    }

    private static bool TryDetectCategory(string goName, string parentName, string spriteName, out PropSwayCategory result)
    {
        if (MatchesAny(goName, TreeKeywords) || MatchesAny(spriteName, TreeKeywords))
        {
            result = PropSwayCategory.Tree;
            return true;
        }
        if (MatchesAny(goName, BushGrassKeywords) || MatchesAny(spriteName, BushGrassKeywords) || MatchesAny(parentName, BushGrassKeywords))
        {
            result = PropSwayCategory.BushOrGrass;
            return true;
        }
        if (MatchesAny(goName, HangingKeywords) || MatchesAny(spriteName, HangingKeywords))
        {
            result = PropSwayCategory.HangingBannerOrLantern;
            return true;
        }

        result = PropSwayCategory.AutoDetect;
        return false;
    }

    private static bool MatchesAny(string text, string[] keywords)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < keywords.Length; i++)
        {
            if (text.Contains(keywords[i])) return true;
        }
        return false;
    }

    #endregion

    #region Initialization & Presets

    public void ConfigurePreset(PropSwayCategory preset)
    {
        category = preset;
        switch (preset)
        {
            case PropSwayCategory.Tree:
                swayAngleDegrees = 2.4f;
                swaySpeed = 1.15f;
                gustStrength = 2.2f;
                foliageBreathAmount = 0.016f;
                playerPushStrength = 0.55f;
                playerInteractRadius = 1.6f;
                isHanging = false;
                break;

            case PropSwayCategory.BushOrGrass:
                swayAngleDegrees = 4.6f;
                swaySpeed = 1.85f;
                gustStrength = 3.2f;
                foliageBreathAmount = 0.025f;
                playerPushStrength = 1.65f;
                playerInteractRadius = 1.25f;
                isHanging = false;
                break;

            case PropSwayCategory.HangingBannerOrLantern:
                swayAngleDegrees = 5.2f;
                swaySpeed = 1.55f;
                gustStrength = 3.6f;
                foliageBreathAmount = 0.008f;
                playerPushStrength = 1.4f;
                playerInteractRadius = 1.35f;
                isHanging = true;
                break;
        }
    }

    private void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        if (category == PropSwayCategory.AutoDetect)
        {
            string goName = gameObject.name.ToLowerInvariant();
            string parentName = transform.parent != null ? transform.parent.name.ToLowerInvariant() : string.Empty;
            string spriteName = (sr != null && sr.sprite != null) ? sr.sprite.name.ToLowerInvariant() : string.Empty;
            if (TryDetectCategory(goName, parentName, spriteName, out PropSwayCategory detected))
            {
                ConfigurePreset(detected);
            }
            else
            {
                ConfigurePreset(PropSwayCategory.Tree);
            }
        }
        else
        {
            isHanging = (category == PropSwayCategory.HangingBannerOrLantern);
        }

        baseLocalPos = transform.localPosition;
        baseLocalRot = transform.localRotation;
        baseLocalScale = transform.localScale;
        worldXSeed = transform.position.x;
        phaseOffset = (worldXSeed * 0.37f) + (transform.position.y * 0.19f);

        // Compute anchor offset at exact bottom (roots) for trees/bushes, or exact top for banners/lanterns
        localPivotOffset = Vector3.zero;
        if (sr != null)
        {
            Bounds b = sr.bounds;
            Vector3 worldAnchor = isHanging
                ? new Vector3(b.center.x, b.max.y, transform.position.z)
                : new Vector3(b.center.x, b.min.y, transform.position.z);

            Vector3 worldDelta = worldAnchor - transform.position;
            if (transform.parent != null)
            {
                localPivotOffset = transform.parent.InverseTransformVector(worldDelta);
            }
            else
            {
                localPivotOffset = worldDelta;
            }
        }

        initialized = true;
    }

    #endregion

    #region Wind & Rustle Update

    private void Update()
    {
        if (!initialized) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float t = Time.time;

        // 1. Multi-layer organic wind wave traveling horizontally across the level
        float primaryWave = Mathf.Sin(t * swaySpeed + phaseOffset);
        float secondaryFlutter = Mathf.Sin(t * (swaySpeed * 2.35f) + phaseOffset * 1.7f) * 0.28f;

        // Periodic rolling wind gusts sweeping left-to-right across world X
        float gustWave = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 0.55f - worldXSeed * 0.12f), 2.4f);
        float gustBend = -gustWave * gustStrength; // Negative Z tilts clockwise (leaning right with the wind)

        float windAngle = (primaryWave + secondaryFlutter) * swayAngleDegrees + gustBend;

        // 2. Player walk/dash rustle interaction (damped spring)
        if (reactToPlayerMovement && PlayerController.Instance != null && !PlayerController.Instance.IsDead)
        {
            Transform playerTr = PlayerController.Instance.transform;
            Vector2 delta = (Vector2)playerTr.position - (Vector2)transform.position;
            if (Mathf.Abs(delta.x) <= playerInteractRadius && Mathf.Abs(delta.y) <= playerInteractRadius + 0.7f)
            {
                Rigidbody2D playerRb = PlayerController.Instance.GetComponent<Rigidbody2D>();
                if (playerRb != null && Mathf.Abs(playerRb.linearVelocityX) > 1.0f)
                {
                    // Bend foliage in the direction the Player is moving
                    float pushDir = -Mathf.Sign(playerRb.linearVelocityX);
                    float speedFactor = Mathf.Clamp(Mathf.Abs(playerRb.linearVelocityX) / 7.5f, 0.2f, 2.2f);
                    springVelocity += pushDir * speedFactor * playerPushStrength * 42f * dt;
                }
            }
        }

        // Integrate damped harmonic spring back to 0
        const float stiffness = 36f;
        const float damping = 5.2f;
        float springAccel = -stiffness * springAngle - damping * springVelocity;
        springVelocity += springAccel * dt;
        springAngle = Mathf.Clamp(springAngle + springVelocity * dt, -22f, 22f);

        float totalAngle = windAngle + springAngle;

        // 3. Apply base-anchored rotation so roots stay planted firmly on the ground while the top sways!
        Quaternion swayRot = Quaternion.Euler(0f, 0f, totalAngle);
        Vector3 pivotPos = baseLocalPos + localPivotOffset;
        Vector3 rotatedOffset = swayRot * (-localPivotOffset);
        transform.localPosition = pivotPos + rotatedOffset;
        transform.localRotation = baseLocalRot * swayRot;

        // 4. Subtle foliage breath / leaf flutter squash-stretch
        if (foliageBreathAmount > 0.0001f)
        {
            float breath = Mathf.Sin(t * (swaySpeed * 1.6f) + phaseOffset) * foliageBreathAmount;
            float gustStretch = gustWave * foliageBreathAmount * 0.65f;
            transform.localScale = new Vector3(
                baseLocalScale.x * (1f + breath + gustStretch),
                baseLocalScale.y * (1f - breath * 0.6f),
                baseLocalScale.z
            );
        }
    }

    #endregion
}

