using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages an atmospheric, one-direction leaf wind stream using pooled instances of Leaf.prefab.
/// Features 2-layer parallax depth (behind and in front of the player), camera-wrapped infinite spawning,
/// periodic wind gust surges, stage mood tinting, and combat slash/dash scatter impulses.
/// </summary>
public class LeafWindAtmosphere : MonoBehaviour
{
    #region Types

    private struct LeafInstanceState
    {
        public Transform Transform;
        public Animator Animator;
        public bool IsNearLayer;
        public float SpeedFactor;
        public float WavePhase;
        public float WaveFreq;
        public float WaveAmp;
        public float BaseRotAmp;
        public float BaseAnimSpeed;
        public float AnimBoostTimer;
        public Vector2 Position;
        public Vector2 ExtraVelocity;
    }

    #endregion

    #region Singleton

    public static LeafWindAtmosphere Instance { get; private set; }

    #endregion

    #region Serialized Settings

    [Header("Prefab & Pool")]
    [SerializeField] private GameObject leafPrefab;
    [SerializeField] private int totalLeafCount = 22;
    [Range(0f, 1f)] [SerializeField] private float nearLayerRatio = 0.55f;

    [Header("Wind Stream & Flutter")]
    [SerializeField] private Vector2 windDirection = new Vector2(1f, -0.26f);
    [SerializeField] private float baseWindSpeed = 3.2f;
    [SerializeField] private float farSpeedMultiplier = 0.68f;
    [SerializeField] private float nearSpeedMultiplier = 1.24f;
    [SerializeField] private float viewportPadding = 2.4f;

    [Header("Depth Sorting & Scale")]
    [SerializeField] private int farSortingOrder = 1;
    [SerializeField] private int nearSortingOrder = 12;
    [SerializeField] private Vector2 farScaleRange = new Vector2(0.95f, 1.20f);
    [SerializeField] private Vector2 nearScaleRange = new Vector2(1.40f, 1.85f);

    [Header("Periodic Wind Gusts")]
    [SerializeField] private Vector2 gustIntervalRange = new Vector2(7f, 12f);
    [SerializeField] private float gustDuration = 2.6f;
    [SerializeField] private float gustSpeedMultiplier = 1.95f;

    #endregion

    #region Private State

    private readonly List<LeafInstanceState> leafPool = new List<LeafInstanceState>();
    private Camera trackedCamera;
    private Vector2 lastCameraPos;
    private bool hasLastCameraPos;
    private bool isInitialized;

    private float nextGustCountdown = 3.5f;
    private float activeGustTimer = -1f;

    #endregion

    #region Public Entry Points

    public static bool IsSceneSupported(Scene scene)
    {
        if (FindFirstObjectByType<MainMenu>() != null)
        {
            return false;
        }

        string name = scene.name;
        if (string.IsNullOrEmpty(name)) return true;

        return name.IndexOf("Stage3", StringComparison.OrdinalIgnoreCase) < 0
            && name.IndexOf("Stage4", StringComparison.OrdinalIgnoreCase) < 0
            && name.IndexOf("Stage5", StringComparison.OrdinalIgnoreCase) < 0
            && name.IndexOf("Stage 3", StringComparison.OrdinalIgnoreCase) < 0
            && name.IndexOf("Stage 4", StringComparison.OrdinalIgnoreCase) < 0
            && name.IndexOf("Stage 5", StringComparison.OrdinalIgnoreCase) < 0;
    }

    public static LeafWindAtmosphere EnsureForScene(GameObject prefabOverride)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!IsSceneSupported(activeScene))
        {
            if (Instance != null)
            {
                Destroy(Instance.gameObject);
                Instance = null;
            }
            return null;
        }

        if (Instance != null && Instance.gameObject.scene == activeScene)
        {
            if (prefabOverride != null && Instance.leafPrefab == null)
            {
                Instance.leafPrefab = prefabOverride;
                Instance.TryInitializePool();
            }
            return Instance;
        }

        GameObject host = new GameObject("LeafWindAtmosphere");
        LeafWindAtmosphere atmosphere = host.AddComponent<LeafWindAtmosphere>();
        atmosphere.leafPrefab = ResolvePrefab(prefabOverride);
        atmosphere.TryInitializePool();
        return atmosphere;
    }

    public static void ApplyWindBurst(Vector3 worldCenter, Vector2 dir, float strength = 7.5f, float radius = 3.6f)
    {
        if (Instance == null) return;
        Instance.ApplyLocalWindBurst(worldCenter, dir, strength, radius);
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (!IsSceneSupported(gameObject.scene))
        {
            Destroy(gameObject);
            return;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        nextGustCountdown = UnityEngine.Random.Range(gustIntervalRange.x * 0.5f, gustIntervalRange.y);
    }

    private void Start()
    {
        if (leafPrefab == null)
        {
            leafPrefab = ResolvePrefab(null);
        }
        TryInitializePool();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!isInitialized)
        {
            TryInitializePool();
            if (!isInitialized) return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (trackedCamera == null || !trackedCamera.isActiveAndEnabled)
        {
            trackedCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            hasLastCameraPos = false;
        }
        if (trackedCamera == null) return;

        UpdateViewportBounds(trackedCamera, out float minX, out float maxX, out float minY, out float maxY);

        Vector2 camPos = trackedCamera.transform.position;
        if (!hasLastCameraPos || (camPos - lastCameraPos).sqrMagnitude > 144f)
        {
            ScatterWithinBounds(minX, maxX, minY, maxY);
            hasLastCameraPos = true;
        }
        lastCameraPos = camPos;

        float gustFactor = EvaluateGustMultiplier(dt);
        Vector2 baseWindDir = windDirection.sqrMagnitude > 0.001f
            ? windDirection.normalized
            : new Vector2(1f, -0.26f).normalized;
        Vector2 perpDir = new Vector2(-baseWindDir.y, baseWindDir.x);

        // During a gust surge, flatten downward drop slightly so leaves sweep more forcefully across
        Vector2 gustWindDir = new Vector2(baseWindDir.x, baseWindDir.y * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(gustFactor - 1f))).normalized;
        float now = Time.time;

        for (int i = 0; i < leafPool.Count; i++)
        {
            LeafInstanceState leaf = leafPool[i];
            if (leaf.Transform == null) continue;

            float layerMult = leaf.IsNearLayer ? nearSpeedMultiplier : farSpeedMultiplier;
            float speed = baseWindSpeed * layerMult * leaf.SpeedFactor * gustFactor;

            float flutterWave = Mathf.Sin(now * leaf.WaveFreq + leaf.WavePhase) * leaf.WaveAmp;
            Vector2 velocity = gustWindDir * speed + perpDir * flutterWave + leaf.ExtraVelocity;

            leaf.Position += velocity * dt;
            leaf.ExtraVelocity *= Mathf.Clamp01(1f - 3.6f * dt);

            WrapLeafToBounds(ref leaf.Position, baseWindDir, minX, maxX, minY, maxY);

            leaf.Transform.position = new Vector3(leaf.Position.x, leaf.Position.y, 0f);
            float tiltZ = Mathf.Sin(now * (leaf.WaveFreq * 1.15f) + leaf.WavePhase) * leaf.BaseRotAmp
                          + Mathf.Clamp(leaf.ExtraVelocity.y * 2.5f, -28f, 28f);
            leaf.Transform.rotation = Quaternion.Euler(0f, 0f, tiltZ);

            if (leaf.Animator != null)
            {
                if (leaf.AnimBoostTimer > 0f)
                {
                    leaf.AnimBoostTimer = Mathf.Max(0f, leaf.AnimBoostTimer - dt);
                    float boostLerp = leaf.AnimBoostTimer / 0.45f;
                    leaf.Animator.speed = leaf.BaseAnimSpeed * (1f + 1.25f * boostLerp);
                }
                else
                {
                    leaf.Animator.speed = leaf.BaseAnimSpeed * Mathf.Lerp(1f, 1.35f, Mathf.Clamp01(gustFactor - 1f));
                }
            }

            leafPool[i] = leaf;
        }
    }

    #endregion

    #region Pool Setup & Stage Mood

    private void TryInitializePool()
    {
        if (isInitialized || leafPrefab == null) return;

        if (trackedCamera == null)
        {
            trackedCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        }
        if (trackedCamera == null) return;

        ResolveStageTints(out Color nearColor, out Color farColor);
        UpdateViewportBounds(trackedCamera, out float minX, out float maxX, out float minY, out float maxY);

        int count = Mathf.Max(4, totalLeafCount);
        int nearCount = Mathf.RoundToInt(count * Mathf.Clamp01(nearLayerRatio));

        for (int i = 0; i < count; i++)
        {
            bool isNear = i < nearCount;
            GameObject leafObj = Instantiate(leafPrefab, transform);
            leafObj.name = isNear ? $"WindLeaf_Near_{i}" : $"WindLeaf_Far_{i}";

            SpriteRenderer sr = leafObj.GetComponentInChildren<SpriteRenderer>(true);
            Animator anim = leafObj.GetComponentInChildren<Animator>(true);

            float scale = isNear
                ? UnityEngine.Random.Range(nearScaleRange.x, nearScaleRange.y)
                : UnityEngine.Random.Range(farScaleRange.x, farScaleRange.y);
            leafObj.transform.localScale = new Vector3(scale, scale, 1f);

            Color baseTint = isNear ? nearColor : farColor;
            float brightnessJitter = UnityEngine.Random.Range(0.90f, 1.06f);
            Color finalColor = new Color(
                Mathf.Clamp01(baseTint.r * brightnessJitter),
                Mathf.Clamp01(baseTint.g * brightnessJitter),
                Mathf.Clamp01(baseTint.b * brightnessJitter),
                baseTint.a * UnityEngine.Random.Range(0.88f, 1f)
            );

            if (sr != null)
            {
                sr.color = finalColor;
                sr.sortingOrder = isNear ? nearSortingOrder : farSortingOrder;
                sr.flipX = UnityEngine.Random.value > 0.5f;
            }

            float baseAnimSpeed = UnityEngine.Random.Range(0.72f, 1.28f);
            if (anim != null)
            {
                anim.Play(0, 0, UnityEngine.Random.value);
                anim.speed = baseAnimSpeed;
            }

            Vector2 startPos = new Vector2(
                UnityEngine.Random.Range(minX, maxX),
                UnityEngine.Random.Range(minY, maxY)
            );
            leafObj.transform.position = new Vector3(startPos.x, startPos.y, 0f);

            leafPool.Add(new LeafInstanceState
            {
                Transform = leafObj.transform,
                Animator = anim,
                IsNearLayer = isNear,
                SpeedFactor = UnityEngine.Random.Range(0.82f, 1.22f),
                WavePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                WaveFreq = UnityEngine.Random.Range(1.8f, 3.3f),
                WaveAmp = UnityEngine.Random.Range(0.35f, 0.85f),
                BaseRotAmp = UnityEngine.Random.Range(8f, 20f),
                BaseAnimSpeed = baseAnimSpeed,
                AnimBoostTimer = 0f,
                Position = startPos,
                ExtraVelocity = Vector2.zero
            });
        }

        lastCameraPos = trackedCamera.transform.position;
        hasLastCameraPos = true;
        isInitialized = true;
    }

    private static GameObject ResolvePrefab(GameObject candidate)
    {
        if (candidate != null) return candidate;
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VFX/Leaf.prefab");
#else
        return null;
#endif
    }

    private static void ResolveStageTints(out Color nearTint, out Color farTint)
    {
        // Forest / outdoor stages (Stage0-Stage2): natural leaf green
        nearTint = new Color(0.94f, 1f, 0.90f, 0.94f);
        farTint = new Color(0.76f, 0.86f, 0.78f, 0.55f);
    }

    #endregion

    #region Wind Math & Combat Scatter

    private float EvaluateGustMultiplier(float dt)
    {
        if (activeGustTimer >= 0f)
        {
            activeGustTimer += dt;
            float dur = Mathf.Max(0.4f, gustDuration);
            if (activeGustTimer >= dur)
            {
                activeGustTimer = -1f;
                nextGustCountdown = UnityEngine.Random.Range(gustIntervalRange.x, gustIntervalRange.y);
                return 1f;
            }

            float t = Mathf.Clamp01(activeGustTimer / dur);
            float bell = Mathf.Sin(t * Mathf.PI);
            return Mathf.Lerp(1f, gustSpeedMultiplier, bell);
        }

        nextGustCountdown -= dt;
        if (nextGustCountdown <= 0f)
        {
            activeGustTimer = 0f;
        }
        return 1f;
    }

    private void UpdateViewportBounds(Camera cam, out float minX, out float maxX, out float minY, out float maxY)
    {
        float halfH = cam.orthographic ? cam.orthographicSize : 6f;
        float halfW = halfH * Mathf.Max(0.5f, cam.aspect);
        Vector3 camPos = cam.transform.position;

        minX = camPos.x - halfW - viewportPadding;
        maxX = camPos.x + halfW + viewportPadding;
        minY = camPos.y - halfH - viewportPadding;
        maxY = camPos.y + halfH + viewportPadding;
    }

    private void ScatterWithinBounds(float minX, float maxX, float minY, float maxY)
    {
        for (int i = 0; i < leafPool.Count; i++)
        {
            LeafInstanceState leaf = leafPool[i];
            if (leaf.Transform == null) continue;

            leaf.Position = new Vector2(
                UnityEngine.Random.Range(minX, maxX),
                UnityEngine.Random.Range(minY, maxY)
            );
            leaf.Transform.position = new Vector3(leaf.Position.x, leaf.Position.y, 0f);
            leafPool[i] = leaf;
        }
    }

    private static void WrapLeafToBounds(ref Vector2 pos, Vector2 dir, float minX, float maxX, float minY, float maxY)
    {
        bool wrapped = false;
        if (dir.x >= 0f && pos.x > maxX)
        {
            pos.x = minX;
            pos.y = UnityEngine.Random.Range(minY, maxY);
            wrapped = true;
        }
        else if (dir.x < 0f && pos.x < minX)
        {
            pos.x = maxX;
            pos.y = UnityEngine.Random.Range(minY, maxY);
            wrapped = true;
        }

        if (!wrapped)
        {
            if (dir.y <= 0f && pos.y < minY)
            {
                pos.y = maxY;
                pos.x = UnityEngine.Random.Range(minX, maxX);
            }
            else if (dir.y > 0f && pos.y > maxY)
            {
                pos.y = minY;
                pos.x = UnityEngine.Random.Range(minX, maxX);
            }
        }
    }

    private void ApplyLocalWindBurst(Vector3 worldCenter, Vector2 dir, float strength, float radius)
    {
        if (leafPool.Count == 0 || radius <= 0.01f) return;

        float sqrRadius = radius * radius;
        Vector2 center2D = worldCenter;
        Vector2 alignedDir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;

        for (int i = 0; i < leafPool.Count; i++)
        {
            LeafInstanceState leaf = leafPool[i];
            if (leaf.Transform == null) continue;

            Vector2 toLeaf = leaf.Position - center2D;
            float sqrDist = toLeaf.sqrMagnitude;
            if (sqrDist > sqrRadius) continue;

            float dist = Mathf.Sqrt(sqrDist);
            float falloff = 1f - (dist / radius);
            Vector2 radial = dist > 0.05f ? (toLeaf / dist) : UnityEngine.Random.insideUnitCircle.normalized;
            Vector2 swirl = (alignedDir * 0.65f + radial * 0.85f).normalized;

            leaf.ExtraVelocity += swirl * (strength * falloff);
            leaf.AnimBoostTimer = 0.45f;
            leafPool[i] = leaf;
        }
    }

    #endregion
}
