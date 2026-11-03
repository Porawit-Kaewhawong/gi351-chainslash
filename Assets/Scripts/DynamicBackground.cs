using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dynamically animates static backgrounds in 2D environments.
/// Supports camera parallax, infinite seamless horizontal/vertical wrapping with auto-tiling,
/// continuous ambient auto-scrolling (e.g. drifting clouds or fog), and multi-layer management.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Dynamic Background")]
public class DynamicBackground : MonoBehaviour
{
    public enum BackgroundMode
    {
        [Tooltip("Controls the GameObject this script is attached to (Single Layer).")]
        SingleObject,
        [Tooltip("Controls multiple child background layers from a single master component.")]
        MultiLayer
    }

    [System.Serializable]
    public class BackgroundLayerConfig
    {
        [Tooltip("Descriptive label for this layer (e.g. 'Far Mountains', 'Midground Trees').")]
        public string layerName = "Layer";

        [Tooltip("The Transform of this background layer.")]
        public Transform layerTransform;

        [Header("Movement & Parallax")]
        [Tooltip("Parallax movement multiplier relative to camera movement.\n0 = Fixed in world space.\n0.5 = Moves at half camera speed (classic distant layer).\n1 = Follows camera 1:1 (fixed on screen like skybox).")]
        public Vector2 parallaxMultiplier = new Vector2(0.5f, 0.05f);

        [Tooltip("If true, ignores camera vertical movement for this layer.")]
        public bool lockY = false;

        [Header("Ambient Auto-Scroll")]
        [Tooltip("Continuous drift velocity (units per second) independent of camera movement (e.g. drifting clouds).")]
        public Vector2 autoScrollSpeed = Vector2.zero;

        [Header("Infinite Seamless Wrapping")]
        [Tooltip("If true, wraps infinitely in X when the camera moves across the stage.")]
        public bool infiniteHorizontal = true;

        [Tooltip("If true, wraps infinitely in Y for vertical climbing/shaft levels.")]
        public bool infiniteVertical = false;

        [Tooltip("Automatically spawns left and right clone tiles at runtime to guarantee zero visible screen gaps at any aspect ratio.")]
        public bool autoTileBuffers = true;

        [Tooltip("Manual override for layer width in world units. Leave at 0 to auto-detect from SpriteRenderer.")]
        public float customWidth = 0f;

        [Tooltip("Manual override for layer height in world units. Leave at 0 to auto-detect from SpriteRenderer.")]
        public float customHeight = 0f;

        // Runtime Layer State (Hidden from inspector)
        [System.NonSerialized] public Vector3 initialPosition;
        [System.NonSerialized] public Vector3 currentBasePosition;
        [System.NonSerialized] public Vector2 accumulatedScroll;
        [System.NonSerialized] public float resolvedWidth;
        [System.NonSerialized] public float resolvedHeight;
        [System.NonSerialized] public Vector3 smoothVelocity;
        [System.NonSerialized] public SpriteRenderer cachedRenderer;
        [System.NonSerialized] public List<GameObject> spawnedCloneObjects = new List<GameObject>();
        [System.NonSerialized] public bool isInitialized = false;
    }

    [Header("Operating Mode")]
    [Tooltip("Choose whether this component controls this single GameObject or manages multiple child layers.")]
    [SerializeField] private BackgroundMode mode = BackgroundMode.SingleObject;

    [Header("Single Layer Settings (Used when Mode = SingleObject)")]
    [Tooltip("Parallax movement multiplier relative to camera movement.\n0 = Fixed in world space.\n0.5 = Moves at half camera speed (distant layer).\n1 = Follows camera 1:1 (fixed on screen like skybox).")]
    [SerializeField] private Vector2 parallaxMultiplier = new Vector2(0.5f, 0.05f);

    [Tooltip("If true, locks the Y position so camera jumps or vertical movement do not affect this background.")]
    [SerializeField] private bool lockY = false;

    [Tooltip("Continuous ambient drift velocity in world units per second (e.g. (0.2, 0) for slow wind/clouds).")]
    [SerializeField] private Vector2 autoScrollSpeed = Vector2.zero;

    [Tooltip("If true, wraps horizontally infinitely so the player never sees an edge.")]
    [SerializeField] private bool infiniteHorizontal = true;

    [Tooltip("If true, wraps vertically infinitely.")]
    [SerializeField] private bool infiniteVertical = false;

    [Tooltip("Automatically generates left and right companion tiles so there are never empty gaps at screen edges.")]
    [SerializeField] private bool autoTileBuffers = true;

    [Tooltip("Manual width in world units (0 = auto-calculate from SpriteRenderer bounds).")]
    [SerializeField] private float customWidth = 0f;

    [Tooltip("Manual height in world units (0 = auto-calculate from SpriteRenderer bounds).")]
    [SerializeField] private float customHeight = 0f;

    [Header("Multi-Layer Settings (Used when Mode = MultiLayer)")]
    [Tooltip("List of background layers managed by this controller.")]
    [SerializeField] private List<BackgroundLayerConfig> layers = new List<BackgroundLayerConfig>();

    [Header("Camera & Motion Settings")]
    [Tooltip("The camera to track. If left empty, automatically finds the active Main Camera.")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("If true, uses unscaled time for auto-scroll so backgrounds continue moving during game pause or slow motion.")]
    [SerializeField] private bool useUnscaledTime = false;

    [Tooltip("Enables smooth dampening for camera tracking.")]
    [SerializeField] private bool useSmoothing = false;

    [Range(0.01f, 0.5f)]
    [SerializeField] private float smoothTime = 0.05f;

    [Header("Pixel Art Settings")]
    [Tooltip("Snaps movement to the pixel grid to eliminate subpixel shimmering in pixel art games.")]
    [SerializeField] private bool pixelSnap = false;

    [Tooltip("Pixels Per Unit used for pixel grid snapping.")]
    [SerializeField] private float pixelsPerUnit = 16f;

    // Single object runtime state
    private BackgroundLayerConfig singleLayerConfig;
    private Vector3 initialCameraPosition;
    private bool cameraInitialized = false;

    private void Awake()
    {
        InitializeCamera();
        InitializeLayers();
    }

    private void Start()
    {
        if (!cameraInitialized)
        {
            InitializeCamera();
        }
    }

    private void OnValidate()
    {
        if (pixelsPerUnit <= 0f) pixelsPerUnit = 16f;
        if (smoothTime <= 0f) smoothTime = 0.05f;
    }

    private void InitializeCamera()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        }

        if (targetCamera != null)
        {
            initialCameraPosition = targetCamera.transform.position;
            cameraInitialized = true;
        }
    }

    private Camera GetActiveCamera()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (targetCamera != null && !cameraInitialized)
            {
                initialCameraPosition = targetCamera.transform.position;
                cameraInitialized = true;
            }
        }
        return targetCamera;
    }

    private void InitializeLayers()
    {
        if (mode == BackgroundMode.SingleObject)
        {
            singleLayerConfig = new BackgroundLayerConfig
            {
                layerName = gameObject.name,
                layerTransform = transform,
                parallaxMultiplier = parallaxMultiplier,
                lockY = lockY,
                autoScrollSpeed = autoScrollSpeed,
                infiniteHorizontal = infiniteHorizontal,
                infiniteVertical = infiniteVertical,
                autoTileBuffers = autoTileBuffers,
                customWidth = customWidth,
                customHeight = customHeight
            };

            SetupLayer(singleLayerConfig);
        }
        else
        {
            foreach (var layer in layers)
            {
                if (layer != null && layer.layerTransform != null)
                {
                    SetupLayer(layer);
                }
            }
        }
    }

    private void SetupLayer(BackgroundLayerConfig layer)
    {
        if (layer == null || layer.layerTransform == null) return;

        layer.initialPosition = layer.layerTransform.position;
        layer.currentBasePosition = layer.initialPosition;
        layer.accumulatedScroll = Vector2.zero;

        // Cache renderer
        layer.cachedRenderer = layer.layerTransform.GetComponent<SpriteRenderer>();
        if (layer.cachedRenderer == null)
        {
            layer.cachedRenderer = layer.layerTransform.GetComponentInChildren<SpriteRenderer>();
        }

        // Resolve width and height
        layer.resolvedWidth = ResolveLayerWidth(layer);
        layer.resolvedHeight = ResolveLayerHeight(layer);

        // Spawn tiling buffer clones if needed
        if (layer.autoTileBuffers && layer.infiniteHorizontal && layer.resolvedWidth > 0.01f)
        {
            CreateHorizontalBufferClones(layer);
        }

        layer.isInitialized = true;
    }

    private float ResolveLayerWidth(BackgroundLayerConfig layer)
    {
        if (layer.customWidth > 0.01f) return layer.customWidth;

        if (layer.cachedRenderer != null && layer.cachedRenderer.sprite != null)
        {
            float boundsWidth = layer.cachedRenderer.bounds.size.x;
            if (boundsWidth > 0.01f) return boundsWidth;

            float spriteUnitWidth = layer.cachedRenderer.sprite.rect.width / layer.cachedRenderer.sprite.pixelsPerUnit;
            return spriteUnitWidth * Mathf.Abs(layer.layerTransform.lossyScale.x);
        }

        return 19.2f; // Fallback standard 1080p width at 100 PPU
    }

    private float ResolveLayerHeight(BackgroundLayerConfig layer)
    {
        if (layer.customHeight > 0.01f) return layer.customHeight;

        if (layer.cachedRenderer != null && layer.cachedRenderer.sprite != null)
        {
            float boundsHeight = layer.cachedRenderer.bounds.size.y;
            if (boundsHeight > 0.01f) return boundsHeight;

            float spriteUnitHeight = layer.cachedRenderer.sprite.rect.height / layer.cachedRenderer.sprite.pixelsPerUnit;
            return spriteUnitHeight * Mathf.Abs(layer.layerTransform.lossyScale.y);
        }

        return 10.8f; // Fallback standard 1080p height at 100 PPU
    }

    private void CreateHorizontalBufferClones(BackgroundLayerConfig layer)
    {
        if (layer.cachedRenderer == null || layer.cachedRenderer.sprite == null) return;

        // Clear any previous clones
        foreach (var clone in layer.spawnedCloneObjects)
        {
            if (clone != null)
            {
                if (Application.isPlaying) Destroy(clone);
                else DestroyImmediate(clone);
            }
        }
        layer.spawnedCloneObjects.Clear();

        Camera cam = GetActiveCamera();
        float camHeight = (cam != null && cam.orthographic) ? cam.orthographicSize * 2f : 15f;
        float camAspect = cam != null ? cam.aspect : (16f / 9f);
        float camWidth = camHeight * camAspect;

        int clonesPerSide = Mathf.Max(1, Mathf.CeilToInt((camWidth * 0.5f) / layer.resolvedWidth));

        for (int i = 1; i <= clonesPerSide; i++)
        {
            // Left Clone
            GameObject leftClone = CreateCompanionClone(layer, -layer.resolvedWidth * i, $"[BG_Tile_Left_{i}]");
            layer.spawnedCloneObjects.Add(leftClone);

            // Right Clone
            GameObject rightClone = CreateCompanionClone(layer, layer.resolvedWidth * i, $"[BG_Tile_Right_{i}]");
            layer.spawnedCloneObjects.Add(rightClone);
        }
    }

    private GameObject CreateCompanionClone(BackgroundLayerConfig layer, float localOffsetX, string cloneName)
    {
        GameObject cloneObj = new GameObject(cloneName);
        cloneObj.transform.SetParent(layer.layerTransform, false);
        cloneObj.transform.localPosition = new Vector3(localOffsetX, 0f, 0f);
        cloneObj.transform.localRotation = Quaternion.identity;
        cloneObj.transform.localScale = Vector3.one;

        SpriteRenderer parentSr = layer.cachedRenderer;
        SpriteRenderer cloneSr = cloneObj.AddComponent<SpriteRenderer>();

        cloneSr.sprite = parentSr.sprite;
        cloneSr.sharedMaterial = parentSr.sharedMaterial;
        cloneSr.color = parentSr.color;
        cloneSr.flipX = parentSr.flipX;
        cloneSr.flipY = parentSr.flipY;
        cloneSr.sortingLayerID = parentSr.sortingLayerID;
        cloneSr.sortingOrder = parentSr.sortingOrder;
        cloneSr.drawMode = parentSr.drawMode;
        cloneSr.size = parentSr.size;
        cloneSr.maskInteraction = parentSr.maskInteraction;
        cloneSr.spriteSortPoint = parentSr.spriteSortPoint;

        return cloneObj;
    }

    /// <summary>
    /// Executes parallax and wrapping in LateUpdate to synchronize perfectly with Cinemachine and camera post-processing.
    /// </summary>
    private void LateUpdate()
    {
        Camera cam = GetActiveCamera();
        if (cam == null) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        Vector3 camPos = cam.transform.position;
        Vector3 camDeltaFromStart = camPos - initialCameraPosition;

        if (mode == BackgroundMode.SingleObject)
        {
            if (singleLayerConfig != null && singleLayerConfig.isInitialized)
            {
                UpdateLayer(singleLayerConfig, camPos, camDeltaFromStart, dt);
            }
        }
        else
        {
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer != null && layer.isInitialized && layer.layerTransform != null)
                {
                    UpdateLayer(layer, camPos, camDeltaFromStart, dt);
                }
            }
        }
    }

    private void UpdateLayer(BackgroundLayerConfig layer, Vector3 camPos, Vector3 camDeltaFromStart, float dt)
    {
        // 1. Accumulate continuous ambient auto-scroll
        if (layer.autoScrollSpeed != Vector2.zero)
        {
            layer.accumulatedScroll += layer.autoScrollSpeed * dt;

            // Constrain accumulated scroll within resolved bounds to prevent float overflow
            if (layer.resolvedWidth > 0.01f)
            {
                layer.accumulatedScroll.x %= layer.resolvedWidth;
            }
            if (layer.resolvedHeight > 0.01f)
            {
                layer.accumulatedScroll.y %= layer.resolvedHeight;
            }
        }

        // 2. Parallax camera tracking offset
        float parallaxShiftX = camDeltaFromStart.x * layer.parallaxMultiplier.x;
        float parallaxShiftY = layer.lockY ? 0f : (camDeltaFromStart.y * layer.parallaxMultiplier.y);

        // 3. Infinite horizontal wrapping
        if (layer.infiniteHorizontal && layer.resolvedWidth > 0.01f)
        {
            // Effective distance between camera and the layer's base origin
            float relativeDistX = (camPos.x * (1f - layer.parallaxMultiplier.x)) - layer.accumulatedScroll.x;

            float halfWidth = layer.resolvedWidth * 0.5f;
            float diffX = relativeDistX - layer.currentBasePosition.x;

            if (diffX > halfWidth)
            {
                float steps = Mathf.Floor((diffX + halfWidth) / layer.resolvedWidth);
                layer.currentBasePosition.x += steps * layer.resolvedWidth;
            }
            else if (diffX < -halfWidth)
            {
                float steps = Mathf.Floor((-diffX + halfWidth) / layer.resolvedWidth);
                layer.currentBasePosition.x -= steps * layer.resolvedWidth;
            }
        }

        // 4. Infinite vertical wrapping (optional)
        if (layer.infiniteVertical && !layer.lockY && layer.resolvedHeight > 0.01f)
        {
            float relativeDistY = (camPos.y * (1f - layer.parallaxMultiplier.y)) - layer.accumulatedScroll.y;
            float halfHeight = layer.resolvedHeight * 0.5f;
            float diffY = relativeDistY - layer.currentBasePosition.y;

            if (diffY > halfHeight)
            {
                float steps = Mathf.Floor((diffY + halfHeight) / layer.resolvedHeight);
                layer.currentBasePosition.y += steps * layer.resolvedHeight;
            }
            else if (diffY < -halfHeight)
            {
                float steps = Mathf.Floor((-diffY + halfHeight) / layer.resolvedHeight);
                layer.currentBasePosition.y -= steps * layer.resolvedHeight;
            }
        }

        // 5. Compute target position
        float finalX = layer.currentBasePosition.x + parallaxShiftX + layer.accumulatedScroll.x;
        float finalY = layer.lockY ? layer.initialPosition.y : (layer.currentBasePosition.y + parallaxShiftY + layer.accumulatedScroll.y);
        float finalZ = layer.initialPosition.z;

        Vector3 targetPosition = new Vector3(finalX, finalY, finalZ);

        // 6. Optional Pixel Grid Snap
        if (pixelSnap && pixelsPerUnit > 0f)
        {
            targetPosition.x = Mathf.Round(targetPosition.x * pixelsPerUnit) / pixelsPerUnit;
            targetPosition.y = Mathf.Round(targetPosition.y * pixelsPerUnit) / pixelsPerUnit;
        }

        // 7. Apply position with optional smoothing
        if (useSmoothing)
        {
            layer.layerTransform.position = Vector3.SmoothDamp(layer.layerTransform.position, targetPosition, ref layer.smoothVelocity, smoothTime);
        }
        else
        {
            layer.layerTransform.position = targetPosition;
        }
    }

    #region Context Menus & Utility

    /// <summary>
    /// Automatically populates child transforms as distinct parallax layers.
    /// </summary>
    [ContextMenu("Auto-Populate Child Layers")]
    public void AutoPopulateChildLayers()
    {
        mode = BackgroundMode.MultiLayer;
        layers.Clear();

        int childCount = transform.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = transform.GetChild(i);
            // Skip generated companion clones
            if (child.name.StartsWith("[BG_Tile_")) continue;

            float ratio = childCount > 1 ? (float)i / (childCount - 1) : 0.5f;
            float parallaxFactor = Mathf.Lerp(0.15f, 0.75f, ratio);

            BackgroundLayerConfig layer = new BackgroundLayerConfig
            {
                layerName = child.name,
                layerTransform = child,
                parallaxMultiplier = new Vector2(parallaxFactor, parallaxFactor * 0.1f),
                lockY = false,
                autoScrollSpeed = Vector2.zero,
                infiniteHorizontal = true,
                autoTileBuffers = true
            };

            layers.Add(layer);
        }

        Debug.Log($"[DynamicBackground] Auto-populated {layers.Count} child layers on {gameObject.name}.", this);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);

        if (mode == BackgroundMode.SingleObject)
        {
            DrawLayerGizmo(transform, customWidth > 0 ? customWidth : 19.2f, customHeight > 0 ? customHeight : 10.8f);
        }
        else if (layers != null)
        {
            foreach (var layer in layers)
            {
                if (layer != null && layer.layerTransform != null)
                {
                    DrawLayerGizmo(layer.layerTransform, layer.customWidth > 0 ? layer.customWidth : 19.2f, layer.customHeight > 0 ? layer.customHeight : 10.8f);
                }
            }
        }
    }

    private void DrawLayerGizmo(Transform targetTrans, float w, float h)
    {
        if (targetTrans == null) return;
        Vector3 center = targetTrans.position;
        Gizmos.DrawWireCube(center, new Vector3(w, h, 0.1f));
    }

    #endregion
}
