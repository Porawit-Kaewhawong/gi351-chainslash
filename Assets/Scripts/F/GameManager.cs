using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    #region Singleton & Constants

    public static GameManager Instance { get; private set; }

    private static readonly float[] ProbeHeights = { 1.5f, 3.5f, 0.5f, 6f, -1f, 9f, -3f };
    private static readonly System.Predicate<GameObject> IsNullEnemyPredicate = enemy => enemy == null;

    #endregion

    #region Types & Enums

    [System.Serializable]
    public struct EnemySpawnConfig
    {
        [Tooltip("Optional label for Inspector organization")]
        public string label;

        [Tooltip("The enemy prefab to spawn")]
        public GameObject prefab;

        [Tooltip("Amount to spawn per wave (Used in 'PerWaveCounts' mode)")]
        public int spawnCount;

        [Tooltip("Relative probability weight (Used in 'WeightedRandom' mode)")]
        public int spawnWeight;
    }

    public enum SpawnMode
    {
        PerWaveCounts,   // Spawns specified spawnCount of each prefab in shuffled random order every wave cycle
        WeightedRandom   // Selects prefabs randomly based on their relative spawnWeight
    }

    #endregion

    #region Serialized Fields

    [Header("Enemy Configuration")]
    [SerializeField] private SpawnMode spawnMode = SpawnMode.PerWaveCounts;
    [SerializeField] private List<EnemySpawnConfig> enemyConfigs = new List<EnemySpawnConfig>();

    [Header("Wave & Timing Settings")]
    [Tooltip("Total number of waves to spawn. Set to 0 or negative for endless waves.")]
    [SerializeField] private int totalWaves = 5;
    [Tooltip("Wait until all enemies in the current wave are defeated before starting the next wave timer.")]
    [SerializeField] private bool waitForWaveClear = true;
    [SerializeField] private int enemiesPerWave = 10; // Used for WeightedRandom mode
    [Tooltip("Extra enemies added per subsequent wave for escalating difficulty.")]
    [SerializeField] private int bonusEnemiesPerWave = 2;
    [SerializeField] private float timeBetweenWaves = 3f;
    [SerializeField] private float timeBetweenSpawns = 0.2f;
    [SerializeField] private int maxActiveEnemies = 20;

    [Header("Portal & Level Progression")]
    [Tooltip("Optional Transform where the player spawns when entering this scene. If empty, auto-finds a safe ground position.")]
    [SerializeField] private Transform playerSpawnPoint;
    [Tooltip("Portal prefab to spawn once all waves are completed and all enemies are gone.")]
    [SerializeField] private GameObject portalPrefab;
    [Tooltip("Optional existing Portal object in the scene (hidden at start, activated when all waves are cleared).")]
    [SerializeField] private GameObject portalSceneObject;
    [Tooltip("Optional specific Transform where the portal spawns. If empty, spawns on valid ground near the player.")]
    [SerializeField] private Transform portalSpawnPoint;

    [Header("Ground & Raycast Settings")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float minSpawnDistance = 6f;
    [SerializeField] private float maxSpawnDistance = 14f;
    [SerializeField] private float raycastHeight = 12f;
    [SerializeField] private float spawnHeightOffset = 0.1f;

    [Header("Spawn Validation")]
    [SerializeField] private Vector2 checkExtent = new Vector2(0.8f, 0.8f);

    [Header("Stage 1 Ambush Intro Cutscene")]
    [Tooltip("If true, plays the Stage 1 slow-motion ninja ambush cutscene before starting waves.")]
    [SerializeField] private bool enableStage1AmbushCutscene = true;
    [SerializeField] private float stage1LetterboxHeight = 165f;

    [Header("Stage 2 Clear Player Dialogue")]
    [SerializeField] private string stage2FirstLine = "At last...";
    [SerializeField] private string stage2SecondLine = "Is it over?";
    [Tooltip("Custom TMP Font Asset (auto-loads PixelifySans SDF if left empty).")]
    [SerializeField] private TMP_FontAsset dialogueFontAsset;
    [SerializeField] private float dialogueFadeInDuration = 0.35f;
    [SerializeField] private float dialogueFadeOutDuration = 0.40f;
    [SerializeField] private float firstLineHoldDuration = 1.4f;
    [SerializeField] private float secondLineHoldDuration = 1.9f;
    [SerializeField] private float delayBetweenLines = 0.25f;
    [SerializeField] private float dialogueHeightAbovePlayer = 1.65f;
    [Tooltip("Optional custom sigh SFX (auto-synthesizes a soft breath exhale sound if empty).")]
    [SerializeField] private AudioClip playerSighSFX;

    #endregion

    #region Public Properties

    public Transform PlayerSpawnPoint => playerSpawnPoint;
    public int CurrentWave { get; private set; }
    public int TotalWaves => totalWaves;
    public int ActiveEnemyCount => activeEnemies.Count;
    public IReadOnlyList<GameObject> ActiveEnemies => activeEnemies;
    public bool AllWavesCompleted { get; private set; }

    #endregion

    #region Private State

    private PlayerController player;
    private readonly List<GameObject> activeEnemies = new List<GameObject>();
    private readonly Dictionary<GameObject, (Vector2 offset, Vector2 size)> prefabGeometryCache = new Dictionary<GameObject, (Vector2 offset, Vector2 size)>();
    private Bounds levelColliderBounds;
    private bool hasLevelBounds;

    private WaitForSeconds waitForSpawnInterval;
    private WaitForSeconds waitForWaveInterval;
    private WaitForSeconds waitForVoidCheck;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        waitForSpawnInterval = new WaitForSeconds(timeBetweenSpawns);
        waitForWaveInterval = new WaitForSeconds(timeBetweenWaves);
        waitForVoidCheck = new WaitForSeconds(0.5f);

        if (portalSceneObject != null)
        {
            portalSceneObject.SetActive(false);
        }

        CacheLevelBounds();
    }

    private void Start()
    {
        EnsurePlayerReference();

        if (player != null && enemyConfigs.Count > 0)
        {
            bool playStage1Ambush = enableStage1AmbushCutscene &&
                SceneManager.GetActiveScene().name.Equals("Stage1", System.StringComparison.OrdinalIgnoreCase);

            if (playStage1Ambush)
            {
                StartCoroutine(Stage1AmbushCutsceneThenStartWavesRoutine());
            }
            else
            {
                StartCoroutine(SpawnRoutine());
                StartCoroutine(MonitorVoidEnemiesRoutine());
            }
        }
        else
        {
            Debug.LogWarning("GameManager: PlayerController or EnemyConfigs missing!");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    #endregion

    #region Stage 1 Ninja Ambush Cutscene

    private IEnumerator Stage1AmbushCutsceneThenStartWavesRoutine()
    {
        GameObject overlayObj = CreateStage1CutsceneOverlay(
            out RectTransform topBar,
            out RectTransform bottomBar,
            out Image fadeScreenImg
        );

        float barHeight = Mathf.Max(80f, stage1LetterboxHeight);
        topBar.sizeDelta = new Vector2(0f, barHeight);
        bottomBar.sizeDelta = new Vector2(0f, barHeight);
        if (fadeScreenImg != null) fadeScreenImg.color = Color.black;

        yield return new WaitForFixedUpdate();
        EnsurePlayerReference();

        CinemachineCamera cmCam = FindFirstObjectByType<CinemachineCamera>();
        if (player == null)
        {
            if (overlayObj != null) Destroy(overlayObj);
            StartCoroutine(SpawnRoutine());
            StartCoroutine(MonitorVoidEnemiesRoutine());
            yield break;
        }

        // Lock player in the center
        player.SetPortalTransitionState(true);
        Rigidbody2D playerRb = player.GetComponent<Rigidbody2D>();
        if (playerRb != null) playerRb.linearVelocity = Vector2.zero;

        Transform origTrackTarget = cmCam != null ? cmCam.Target.TrackingTarget : null;
        float origOrthoSize = (cmCam != null && cmCam.Lens.OrthographicSize > 0.5f) ? cmCam.Lens.OrthographicSize : 6.5f;

        GameObject camAnchorObj = new GameObject("Stage1CutsceneCamAnchor");
        Transform camAnchor = camAnchorObj.transform;
        Vector3 centerPos = player.transform.position + new Vector3(0f, 0.5f, 0f);
        camAnchor.position = centerPos;
        if (cmCam != null)
        {
            cmCam.Target.TrackingTarget = camAnchor;
            cmCam.Lens.OrthographicSize = origOrthoSize * 0.82f;
        }

        // Resolve ninja sprite from enemyConfigs[0] (NormalNinja) so ambush ninjas match the real enemies
        Sprite ninjaSprite = ResolveAmbushNinjaSprite(out Vector3 ninjaScale, out Color ninjaColor, out int ninjaSortOrder);
        List<GameObject> ambushActors = new List<GameObject>();

        bool skipped = false;
        float elapsed = 0f;

        // Smooth Fade In from Black onto the Protagonist in the center
        const float fadeInDur = 0.95f;
        while (elapsed < fadeInDur && !skipped)
        {
            if (IsStage1SkipPressed()) { skipped = true; break; }
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeInDur);
            if (playerRb != null) playerRb.linearVelocity = new Vector2(0f, Mathf.Min(playerRb.linearVelocityY, 0f));
            camAnchor.position = player.transform.position + new Vector3(0f, 0.5f, 0f);
            if (fadeScreenImg != null)
            {
                fadeScreenImg.color = new Color(0f, 0f, 0f, 1f - (t * t * (3f - 2f * t)));
            }
            yield return null;
        }
        if (fadeScreenImg != null) fadeScreenImg.color = Color.clear;

        // Protagonist calmly glances right then left sensing the ambush
        if (!skipped)
        {
            player.FaceWorldDirection(1f);
            yield return WaitStage1Skippable(0.65f, playerRb, () => skipped = true);
        }
        if (!skipped)
        {
            player.FaceWorldDirection(-1f);
            yield return WaitStage1Skippable(0.65f, playerRb, () => skipped = true);
        }

        // --- BEAT 2: 6 Ninjas Drop from High Above Wide on BOTH Left & Right Sides (Epic Wide Shot!) ---
        Vector3 playerGroundPos = player.transform.position;
        float[] xOffsets = { -8.8f, -6.5f, -4.5f, 4.5f, 6.5f, 8.8f }; // 3 Left, 3 Right — wide & grand!
        int ninjaCount = xOffsets.Length;
        Vector3[] landPositions = new Vector3[ninjaCount];
        Vector3[] startDropPositions = new Vector3[ninjaCount];
        SpriteRenderer[] actorRenderers = new SpriteRenderer[ninjaCount];

        if (!skipped)
        {
            for (int i = 0; i < ninjaCount; i++)
            {
                float targetX = playerGroundPos.x + xOffsets[i];
                float floorY = FindFloorYAtX(targetX, playerGroundPos.y);
                landPositions[i] = new Vector3(targetX, floorY, 0f);
                startDropPositions[i] = landPositions[i] + new Vector3(0f, 8.5f + (i % 3) * 1.4f, 0f);

                GameObject actor = new GameObject($"AmbushNinja_{i}");
                actor.transform.position = startDropPositions[i];
                actor.transform.localScale = ninjaScale;

                SpriteRenderer sr = actor.AddComponent<SpriteRenderer>();
                sr.sprite = ninjaSprite;
                sr.color = ninjaColor;
                sr.sortingOrder = ninjaSortOrder;
                sr.flipX = xOffsets[i] > 0f;

                ambushActors.Add(actor);
                actorRenderers[i] = sr;
            }

            elapsed = 0f;
            const float dropDur = 0.95f;
            bool landedFXPlayed = false;
            while (elapsed < dropDur && !skipped)
            {
                if (IsStage1SkipPressed()) { skipped = true; break; }
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dropDur);
                float fallEase = t * t;

                if (playerRb != null) playerRb.linearVelocity = new Vector2(0f, Mathf.Min(playerRb.linearVelocityY, 0f));

                for (int i = 0; i < ambushActors.Count; i++)
                {
                    ambushActors[i].transform.position = Vector3.Lerp(startDropPositions[i], landPositions[i], fallEase);
                }

                // Pull camera far out into an epic wide shot framing the whole ambush
                if (cmCam != null)
                {
                    float easeOut = 1f - Mathf.Pow(1f - t, 3f);
                    cmCam.Lens.OrthographicSize = Mathf.Lerp(origOrthoSize * 0.85f, origOrthoSize * 1.38f, easeOut);
                }

                if (!landedFXPlayed && t >= 0.92f)
                {
                    landedFXPlayed = true;
                    player.TriggerCameraShake(0.20f);
                    if (player.SlashSFX != null) player.PlayPitchedSFX(player.SlashSFX, 0.72f, 0.85f);
                }

                yield return null;
            }
        }

        // Protagonist sees the surrounding ninjas and shifts into the ready-to-slash sword stance -> Smooth Fade Out!
        if (!skipped)
        {
            yield return WaitStage1Skippable(0.90f, playerRb, () => skipped = true);

            if (!skipped)
            {
                player.SetCutsceneSlashingStance(true);
                if (player.SlashSFX != null) player.PlayPitchedSFX(player.SlashSFX, 1.05f, 0.9f);

                // Smoothly tighten camera onto the protagonist drawing their sword
                elapsed = 0f;
                const float stanceHoldDur = 1.35f;
                float startSize = cmCam != null ? cmCam.Lens.OrthographicSize : origOrthoSize;
                while (elapsed < stanceHoldDur && !skipped)
                {
                    if (IsStage1SkipPressed()) { skipped = true; break; }
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / stanceHoldDur);
                    if (playerRb != null) playerRb.linearVelocity = new Vector2(0f, Mathf.Min(playerRb.linearVelocityY, 0f));
                    if (cmCam != null)
                    {
                        float smooth = 1f - Mathf.Pow(1f - t, 3f);
                        cmCam.Lens.OrthographicSize = Mathf.Lerp(startSize, origOrthoSize * 1.05f, smooth);
                    }
                    yield return null;
                }
            }

            // Clean Fade Out at the end of the Cutscene -> restore gameplay
            if (!skipped && fadeScreenImg != null)
            {
                elapsed = 0f;
                const float fadeOutDur = 0.65f;
                float startBarH = topBar != null ? topBar.sizeDelta.y : 0f;
                while (elapsed < fadeOutDur)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeOutDur);
                    float smooth = t * t * (3f - 2f * t);
                    if (playerRb != null) playerRb.linearVelocity = new Vector2(0f, Mathf.Min(playerRb.linearVelocityY, 0f));
                    fadeScreenImg.color = new Color(0f, 0f, 0f, smooth);
                    if (topBar != null) topBar.sizeDelta = new Vector2(0f, Mathf.Lerp(startBarH, 0f, smooth));
                    if (bottomBar != null) bottomBar.sizeDelta = new Vector2(0f, Mathf.Lerp(startBarH, 0f, smooth));
                    yield return null;
                }
            }
        }

        // Clean up cutscene ninjas & restore camera/player straight into live Stage 1 combat
        for (int i = 0; i < ambushActors.Count; i++)
        {
            if (ambushActors[i] != null) Destroy(ambushActors[i]);
        }
        ambushActors.Clear();

        if (cmCam != null)
        {
            cmCam.Target.TrackingTarget = origTrackTarget != null ? origTrackTarget : player.transform;
            cmCam.Lens.OrthographicSize = origOrthoSize;
        }
        Destroy(camAnchorObj);

        if (player != null)
        {
            player.SetCutsceneSlashingStance(false);
            player.SetPortalTransitionState(false);
        }

        // Begin Stage 1 enemy waves immediately and clear black fade cleanly
        StartCoroutine(SpawnRoutine());
        StartCoroutine(MonitorVoidEnemiesRoutine());

        if (!skipped && fadeScreenImg != null)
        {
            elapsed = 0f;
            const float revealDur = 0.45f;
            while (elapsed < revealDur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / revealDur);
                fadeScreenImg.color = new Color(0f, 0f, 0f, 1f - t);
                yield return null;
            }
        }

        if (overlayObj != null) Destroy(overlayObj);
    }

    private IEnumerator WaitStage1Skippable(float seconds, Rigidbody2D playerRb, System.Action onSkip)
    {
        float t = 0f;
        while (t < seconds)
        {
            if (IsStage1SkipPressed())
            {
                onSkip?.Invoke();
                yield break;
            }
            if (playerRb != null) playerRb.linearVelocity = new Vector2(0f, Mathf.Min(playerRb.linearVelocityY, 0f));
            t += Time.deltaTime;
            yield return null;
        }
    }

    private static bool IsStage1SkipPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
        {
            return true;
        }
#endif
        return false;
    }

    private float FindFloorYAtX(float worldX, float refY)
    {
        RaycastHit2D hit = Physics2D.Raycast(new Vector2(worldX, refY + 2.5f), Vector2.down, 8f, groundLayer);
        if (hit.collider != null && !hit.collider.isTrigger)
        {
            return hit.point.y + 0.55f;
        }
        return refY;
    }

    private Sprite ResolveAmbushNinjaSprite(out Vector3 scale, out Color color, out int sortOrder)
    {
        scale = Vector3.one;
        color = Color.white;
        sortOrder = 5;

        for (int i = 0; i < enemyConfigs.Count; i++)
        {
            GameObject prefab = enemyConfigs[i].prefab;
            if (prefab == null) continue;

            SpriteRenderer sr = prefab.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                scale = sr.transform.lossyScale;
                color = sr.color;
                sortOrder = Mathf.Max(sr.sortingOrder, 4);
                return sr.sprite;
            }
        }

        return EnemyBase.GetSharedPixelBlockSprite(3);
    }

    private GameObject CreateStage1CutsceneOverlay(
        out RectTransform topBar,
        out RectTransform bottomBar,
        out Image fadeScreenImg)
    {
        GameObject canvasObj = new GameObject("Stage1AmbushCutsceneCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Black Fade-In / Hard-Cut Curtain
        GameObject fadeObj = new GameObject("FadeCurtain");
        fadeObj.transform.SetParent(canvasObj.transform, false);
        RectTransform fadeRect = fadeObj.AddComponent<RectTransform>();
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.offsetMin = Vector2.zero;
        fadeRect.offsetMax = Vector2.zero;
        fadeScreenImg = fadeObj.AddComponent<Image>();
        fadeScreenImg.color = Color.black;
        fadeScreenImg.raycastTarget = false;

        // Top & Bottom Cinema Letterbox Bars
        GameObject topObj = new GameObject("TopLetterbox");
        topObj.transform.SetParent(canvasObj.transform, false);
        topBar = topObj.AddComponent<RectTransform>();
        topBar.anchorMin = new Vector2(0f, 1f);
        topBar.anchorMax = new Vector2(1f, 1f);
        topBar.pivot = new Vector2(0.5f, 1f);
        topBar.anchoredPosition = Vector2.zero;
        topBar.sizeDelta = Vector2.zero;
        topObj.AddComponent<Image>().color = Color.black;

        GameObject botObj = new GameObject("BottomLetterbox");
        botObj.transform.SetParent(canvasObj.transform, false);
        bottomBar = botObj.AddComponent<RectTransform>();
        bottomBar.anchorMin = new Vector2(0f, 0f);
        bottomBar.anchorMax = new Vector2(1f, 0f);
        bottomBar.pivot = new Vector2(0.5f, 0f);
        bottomBar.anchoredPosition = Vector2.zero;
        bottomBar.sizeDelta = Vector2.zero;
        botObj.AddComponent<Image>().color = Color.black;

        TMP_FontAsset font = ResolveDialogueFont();

        // Skip Hint
        GameObject skipObj = new GameObject("SkipHint");
        skipObj.transform.SetParent(botObj.transform, false);
        RectTransform skipRect = skipObj.AddComponent<RectTransform>();
        skipRect.anchorMin = new Vector2(1f, 0f);
        skipRect.anchorMax = new Vector2(1f, 0f);
        skipRect.pivot = new Vector2(1f, 0f);
        skipRect.anchoredPosition = new Vector2(-32f, 16f);
        skipRect.sizeDelta = new Vector2(300f, 34f);

        TextMeshProUGUI skipTmp = skipObj.AddComponent<TextMeshProUGUI>();
        if (font != null) skipTmp.font = font;
        skipTmp.text = "[SPACE] SKIP";
        skipTmp.fontSize = 22f;
        skipTmp.alignment = TextAlignmentOptions.Right;
        skipTmp.color = new Color(0.85f, 0.85f, 0.85f, 0.65f);

        return canvasObj;
    }

    #endregion

    #region Wave & Spawn Logic

    private IEnumerator SpawnRoutine()
    {
        // Wait one physics step so persistent player position and level colliders are synced
        yield return new WaitForFixedUpdate();

        CurrentWave = 0;
        AllWavesCompleted = false;

        // Loop while in endless mode (totalWaves <= 0) or until totalWaves is reached
        while (totalWaves <= 0 || CurrentWave < totalWaves)
        {
            CurrentWave++;

            // Clean up destroyed enemy references
            PruneNullEnemies();
            EnsurePlayerReference();

            if (player != null)
            {
                List<GameObject> waveQueue = spawnMode == SpawnMode.PerWaveCounts
                    ? BuildShuffledWaveCountQueue()
                    : (spawnMode == SpawnMode.WeightedRandom ? BuildWeightedRandomQueue() : null);

                if (waveQueue != null && waveQueue.Count > 0)
                {
                    yield return StartCoroutine(SpawnEnemyQueueRoutine(waveQueue));
                }
            }

            // Wait for the current wave to be cleared if enabled, or if this is the final wave
            bool isFinalWave = totalWaves > 0 && CurrentWave >= totalWaves;
            if (waitForWaveClear || isFinalWave)
            {
                while (activeEnemies.Count > 0)
                {
                    PruneNullEnemies();
                    yield return null;
                }
            }

            // Wait between waves if there are more waves coming
            if (!isFinalWave)
            {
                yield return waitForWaveInterval;
            }
        }

        // Ensure any remaining enemies in the scene (including pre-placed enemies) are completely gone
        while (activeEnemies.Count > 0)
        {
            PruneNullEnemies();
            yield return null;
        }

        while (HasAnyLivingEnemies())
        {
            PruneNullEnemies();
            yield return waitForSpawnInterval;
        }

        AllWavesCompleted = true;
        Debug.Log($"All {totalWaves} waves completed!");

        SpawnNextLevelPortal();

        string activeSceneName = SceneManager.GetActiveScene().name;

        // Stage 1 Clear: last enemy drops the Ancient Guide Book -> zoom -> rapid page flip -> dock to top-right!
        if (activeSceneName.Equals("Stage1", System.StringComparison.OrdinalIgnoreCase))
        {
            Vector3 fallbackPos = player != null ? player.transform.position : Vector3.zero;
            HealGuideBookUI.TriggerStage1GuideBookDrop(fallbackPos);
        }

        // Only play this dialogue in Stage 2 after all enemies are defeated
        if (activeSceneName.Equals("Stage2", System.StringComparison.OrdinalIgnoreCase))
        {
            StartCoroutine(PlayStage2ClearDialogueRoutine());
        }
    }

    private IEnumerator PlayStage2ClearDialogueRoutine()
    {
        EnsurePlayerReference();
        if (player == null) yield break;

        yield return new WaitForSeconds(0.35f);

        GameObject canvasObj = new GameObject("Stage2ClearDialogueCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 50;

        CanvasGroup cg = canvasObj.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(360f, 90f);
        canvasRect.localScale = Vector3.one * 0.012f;

        // Container for pop animation (no black background box)
        GameObject bgObj = new GameObject("DialogueContainer");
        bgObj.transform.SetParent(canvasObj.transform, false);
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0.5f);
        bgRect.anchorMax = new Vector2(0.5f, 0.5f);
        bgRect.sizeDelta = new Vector2(360f, 70f);

        GameObject textObj = new GameObject("DialogueText");
        textObj.transform.SetParent(bgObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset resolvedFont = ResolveDialogueFont();
        if (resolvedFont != null)
        {
            tmp.font = resolvedFont;
        }
        tmp.fontSize = 36f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = new Color32(0, 0, 0, 220);
        tmp.raycastTarget = false;

        // 1. "At last..." + Stage 2 exclusive Player Sigh VFX & SFX -> Fade In -> Hold -> Fade Out
        TriggerPlayerSighEffect();
        yield return StartCoroutine(ShowDialogueLineRoutine(
            canvasRect, bgRect, cg, tmp,
            stage2FirstLine,
            firstLineHoldDuration,
            false
        ));

        float pauseElapsed = 0f;
        while (pauseElapsed < delayBetweenLines)
        {
            pauseElapsed += Time.deltaTime;
            UpdateDialogueFollowPosition(canvasRect, 0f);
            yield return null;
        }

        // 2. "Is it over?" -> Pop + Fade In -> Hold -> Fade Out
        yield return StartCoroutine(ShowDialogueLineRoutine(
            canvasRect, bgRect, cg, tmp,
            stage2SecondLine,
            secondLineHoldDuration,
            true
        ));

        Destroy(canvasObj);
    }

    private IEnumerator ShowDialogueLineRoutine(
        RectTransform canvasRect,
        RectTransform bgRect,
        CanvasGroup cg,
        TMP_Text textComp,
        string line,
        float holdDuration,
        bool popOnEnter)
    {
        if (textComp == null || cg == null || canvasRect == null) yield break;

        textComp.text = line;
        if (bgRect != null)
        {
            float pillWidth = Mathf.Clamp(line.Length * 18f + 72f, 170f, 340f);
            bgRect.sizeDelta = new Vector2(pillWidth, 60f);
        }

        // Fade In (with pop bounce for second line)
        float elapsed = 0f;
        float fadeIn = Mathf.Max(0.05f, dialogueFadeInDuration);
        while (elapsed < fadeIn)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeIn);
            cg.alpha = t;

            if (popOnEnter && bgRect != null)
            {
                const float c1 = 1.7f;
                const float c3 = c1 + 1f;
                float u = t - 1f;
                float pop = 1f + c3 * u * u * u + c1 * u * u;
                float s = Mathf.LerpUnclamped(0.6f, 1f, pop);
                bgRect.localScale = new Vector3(s, s, 1f);
            }
            else if (bgRect != null)
            {
                bgRect.localScale = Vector3.one;
            }

            UpdateDialogueFollowPosition(canvasRect, (1f - t) * -0.15f);
            yield return null;
        }

        cg.alpha = 1f;
        if (bgRect != null) bgRect.localScale = Vector3.one;

        // Hold visible above the player
        float holdElapsed = 0f;
        while (holdElapsed < holdDuration)
        {
            holdElapsed += Time.deltaTime;
            float bob = Mathf.Sin(holdElapsed * 3.5f) * 0.04f;
            UpdateDialogueFollowPosition(canvasRect, bob);
            yield return null;
        }

        // Fade Out
        elapsed = 0f;
        float fadeOut = Mathf.Max(0.05f, dialogueFadeOutDuration);
        while (elapsed < fadeOut)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeOut);
            cg.alpha = 1f - t;
            UpdateDialogueFollowPosition(canvasRect, t * 0.18f);
            yield return null;
        }

        cg.alpha = 0f;
    }

    private TMP_FontAsset ResolveDialogueFont()
    {
        if (dialogueFontAsset != null) return dialogueFontAsset;

        // 1. Reuse PixelifySans SDF from any existing TMP_Text in the scene/memory
        TMP_Text[] existingTexts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < existingTexts.Length; i++)
        {
            if (existingTexts[i] != null && existingTexts[i].font != null &&
                existingTexts[i].font.name.IndexOf("Pixelify", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                dialogueFontAsset = existingTexts[i].font;
                return dialogueFontAsset;
            }
        }

        // 2. Fallback: find loaded PixelifySans SDF asset in memory
        TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        for (int i = 0; i < loadedFonts.Length; i++)
        {
            if (loadedFonts[i] != null &&
                loadedFonts[i].name.IndexOf("Pixelify", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                dialogueFontAsset = loadedFonts[i];
                return dialogueFontAsset;
            }
        }

#if UNITY_EDITOR
        // 3. Editor fallback: load the project's PixelifySans SDF directly from Assets/Fonts
        dialogueFontAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/Fonts/PixelifySans-VariableFont_wght SDF.asset"
        );
#endif
        return dialogueFontAsset;
    }

    private void UpdateDialogueFollowPosition(RectTransform canvasRect, float extraOffsetY)
    {
        if (canvasRect == null) return;
        EnsurePlayerReference();
        if (player == null) return;

        Vector3 p = player.transform.position;
        canvasRect.position = new Vector3(p.x, p.y + dialogueHeightAbovePlayer + extraOffsetY, 0f);
    }

    private static AudioClip cachedProceduralSighClip;

    private void TriggerPlayerSighEffect()
    {
        EnsurePlayerReference();
        if (player == null) return;

        if (cachedProceduralSighClip == null)
        {
            cachedProceduralSighClip = BuildProceduralSighSFX();
        }
        AudioClip clip = playerSighSFX != null ? playerSighSFX : cachedProceduralSighClip;
        if (clip != null)
        {
            player.PlayPitchedSFX(clip, 1f, 0.75f);
        }

        StartCoroutine(PlayerSighBreathVFXRoutine());
    }

    private IEnumerator PlayerSighBreathVFXRoutine()
    {
        if (player == null) yield break;

        SpriteRenderer playerSR = player.MainSpriteRenderer;
        Transform spriteTr = playerSR != null ? playerSR.transform : null;
        Vector3 origScale = spriteTr != null ? spriteTr.localScale : Vector3.one;

        const int puffCount = 7;
        for (int i = 0; i < puffCount; i++)
        {
            if (player == null) yield break;

            float facingDir = (playerSR != null && playerSR.flipX) ? -1f : 1f;
            Vector3 mouthPos = player.transform.position + new Vector3(facingDir * 0.34f, 0.42f, 0f);
            SpawnSingleBreathPuff(mouthPos, facingDir, playerSR != null ? playerSR.sortingOrder + 2 : 12);

            if (spriteTr != null)
            {
                float progress = (float)i / (puffCount - 1);
                float squashY = 1f - Mathf.Sin(progress * Mathf.PI) * 0.08f;
                spriteTr.localScale = new Vector3(origScale.x, origScale.y * squashY, origScale.z);
            }

            yield return new WaitForSeconds(0.075f);
        }

        if (spriteTr != null)
        {
            spriteTr.localScale = origScale;
        }
    }

    private void SpawnSingleBreathPuff(Vector3 startPos, float facingDir, int sortingOrder)
    {
        GameObject puff = new GameObject("PlayerSighBreathPuff");
        puff.transform.position = startPos;
        puff.transform.localScale = Vector3.one * Random.Range(0.8f, 1.2f);

        SpriteRenderer sr = puff.AddComponent<SpriteRenderer>();
        sr.sprite = EnemyBase.GetSharedPixelBlockSprite(3);
        sr.sortingOrder = sortingOrder;
        sr.color = new Color(0.92f, 0.96f, 1f, 0.72f);

        StartCoroutine(AnimateBreathPuffRoutine(puff, sr, facingDir));
    }

    private static IEnumerator AnimateBreathPuffRoutine(GameObject puff, SpriteRenderer sr, float facingDir)
    {
        Vector2 velocity = new Vector2(facingDir * Random.Range(1.0f, 1.8f), Random.Range(0.15f, 0.45f));
        Vector3 startScale = puff.transform.localScale;
        float duration = Random.Range(0.65f, 0.95f);
        float elapsed = 0f;

        while (elapsed < duration && puff != null)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            float t = Mathf.Clamp01(elapsed / duration);

            velocity.x *= Mathf.Clamp01(1f - 2.4f * dt);
            velocity.y += 0.55f * dt;
            puff.transform.position += (Vector3)(velocity * dt);
            puff.transform.localScale = Vector3.Lerp(startScale, startScale * 1.9f, t);

            if (sr != null)
            {
                float alpha = (1f - t) * 0.68f;
                sr.color = new Color(0.92f, 0.96f, 1f, alpha);
            }

            yield return null;
        }

        if (puff != null)
        {
            Destroy(puff);
        }
    }

    private static AudioClip BuildProceduralSighSFX()
    {
        const int sampleRate = 22050;
        const float duration = 0.62f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        uint seed = 0x42A17Fu;
        float lp1 = 0f;
        float lp2 = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            seed = seed * 1664525u + 1013904223u;
            float white = ((seed & 0xFFFF) / 32767.5f) - 1f;

            float hiCut = Mathf.Lerp(0.24f, 0.08f, t);
            float loCut = Mathf.Lerp(0.05f, 0.015f, t);
            lp1 += (white - lp1) * hiCut;
            lp2 += (white - lp2) * loCut;
            float breath = lp1 - lp2;

            float attack = Mathf.Clamp01(t / 0.16f);
            float release = Mathf.Pow(1f - t, 1.35f);
            samples[i] = Mathf.Clamp(breath * attack * release * 1.6f, -0.8f, 0.8f);
        }

        AudioClip clip = AudioClip.Create("PlayerSighSFX", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// Rescues any enemy that accidentally falls below the level into the void so a wave never soft-locks.
    /// </summary>
    private IEnumerator MonitorVoidEnemiesRoutine()
    {
        while (!AllWavesCompleted)
        {
            PruneNullEnemies();

            float voidThresholdY = hasLevelBounds
                ? levelColliderBounds.min.y - 8f
                : (player != null ? player.transform.position.y - 30f : -40f);

            for (int i = 0; i < activeEnemies.Count; i++)
            {
                GameObject enemyObj = activeEnemies[i];
                if (enemyObj == null) continue;

                if (enemyObj.TryGetComponent(out EnemyBase enemyBase) && enemyBase.IsDead) continue;

                if (enemyObj.transform.position.y < voidThresholdY)
                {
                    if (TryGetValidSpawnPoint(out Vector2 rescuedPos, enemyObj))
                    {
                        enemyObj.transform.position = rescuedPos;
                        if (enemyObj.TryGetComponent(out Rigidbody2D enemyRb))
                        {
                            enemyRb.linearVelocity = Vector2.zero;
                        }
                        Physics2D.SyncTransforms();
                    }
                }
            }

            yield return waitForVoidCheck;
        }
    }

    private void SpawnNextLevelPortal()
    {
        if (portalSceneObject != null)
        {
            if (portalSpawnPoint != null)
            {
                portalSceneObject.transform.position = portalSpawnPoint.position;
            }
            portalSceneObject.SetActive(true);
            return;
        }

        if (portalPrefab == null) return;

        Vector2 spawnPos;
        if (portalSpawnPoint != null)
        {
            spawnPos = portalSpawnPoint.position;
        }
        else if (player != null && TryGetValidSpawnPoint(out Vector2 validGroundPoint, portalPrefab))
        {
            spawnPos = validGroundPoint;
        }
        else if (player != null)
        {
            spawnPos = (Vector2)player.transform.position + Vector2.right * 3f;
        }
        else
        {
            spawnPos = Vector2.zero;
        }

        Instantiate(portalPrefab, spawnPos, Quaternion.identity);
    }

    /// <summary>
    /// Collects each configured prefab's wave quota and Fisher-Yates shuffles the wave roster
    /// so enemy types interleave unpredictably instead of finishing one type before the next.
    /// </summary>
    private List<GameObject> BuildShuffledWaveCountQueue()
    {
        List<GameObject> queue = new List<GameObject>();
        int waveBonus = Mathf.Max(0, (CurrentWave - 1) * bonusEnemiesPerWave);

        for (int c = 0; c < enemyConfigs.Count; c++)
        {
            EnemySpawnConfig config = enemyConfigs[c];
            if (config.prefab == null || config.spawnCount <= 0) continue;

            int countToSpawn = config.spawnCount + waveBonus;
            for (int i = 0; i < countToSpawn; i++)
            {
                queue.Add(config.prefab);
            }
        }

        ShuffleList(queue);
        return queue;
    }

    /// <summary>
    /// Selects wave enemies randomly according to their relative spawnWeight.
    /// </summary>
    private List<GameObject> BuildWeightedRandomQueue()
    {
        int totalWeight = 0;
        for (int i = 0; i < enemyConfigs.Count; i++)
        {
            EnemySpawnConfig config = enemyConfigs[i];
            if (config.prefab != null && config.spawnWeight > 0)
            {
                totalWeight += config.spawnWeight;
            }
        }

        if (totalWeight <= 0) return null;

        int totalToSpawn = enemiesPerWave + Mathf.Max(0, (CurrentWave - 1) * bonusEnemiesPerWave);
        List<GameObject> queue = new List<GameObject>(Mathf.Max(0, totalToSpawn));

        for (int i = 0; i < totalToSpawn; i++)
        {
            GameObject selectedPrefab = GetRandomPrefabByWeight(totalWeight);
            if (selectedPrefab != null)
            {
                queue.Add(selectedPrefab);
            }
        }

        return queue;
    }

    private IEnumerator SpawnEnemyQueueRoutine(IReadOnlyList<GameObject> spawnQueue)
    {
        for (int i = 0; i < spawnQueue.Count; i++)
        {
            GameObject prefabToSpawn = spawnQueue[i];
            if (prefabToSpawn == null) continue;

            // Wait if maxActiveEnemies cap is currently reached so the wave still spawns its full quota
            while (activeEnemies.Count >= maxActiveEnemies)
            {
                PruneNullEnemies();
                yield return null;
            }

            if (player != null && TryGetValidSpawnPoint(out Vector2 spawnPoint, prefabToSpawn))
            {
                GameObject spawnedEnemy = Instantiate(prefabToSpawn, spawnPoint, Quaternion.identity);
                activeEnemies.Add(spawnedEnemy);
            }

            yield return waitForSpawnInterval;
        }
    }

    private static void ShuffleList<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }

    private GameObject GetRandomPrefabByWeight(int totalWeight)
    {
        int randomValue = Random.Range(0, totalWeight);
        int currentSum = 0;

        for (int i = 0; i < enemyConfigs.Count; i++)
        {
            EnemySpawnConfig config = enemyConfigs[i];
            if (config.prefab == null || config.spawnWeight <= 0) continue;

            currentSum += config.spawnWeight;
            if (randomValue < currentSum)
            {
                return config.prefab;
            }
        }

        return null;
    }

    #endregion

    #region Spawn Point Evaluation

    /// <summary>
    /// Finds a valid floor spawn point around the player (never on top of the player),
    /// free of walls/ceilings and away from void edges.
    /// </summary>
    public bool TryGetValidSpawnPoint(out Vector2 validPoint, GameObject enemyPrefab)
    {
        validPoint = Vector2.zero;
        if (player == null) return false;

        GetPrefabColliderGeometry(enemyPrefab, out Vector2 colOffset, out Vector2 colSize);

        Vector2 playerPos = player.transform.position;
        Vector2 arenaAnchor = playerSpawnPoint != null ? (Vector2)playerSpawnPoint.position : playerPos;

        float effectiveMinDist = Mathf.Max(2.5f, minSpawnDistance);
        float effectiveMaxDist = Mathf.Max(effectiveMinDist + 2f, maxSpawnDistance);
        const float minPlayerClearance = 2.5f;

        // Pass 1: Random attempts in [minSpawnDistance, maxSpawnDistance] around the player
        for (int i = 0; i < 20; i++)
        {
            float dir = (i % 2 == 0) ? 1f : -1f;
            if (Random.value > 0.5f) dir = -dir;

            float dist = Random.Range(effectiveMinDist, effectiveMaxDist);
            float candidateX = playerPos.x + dir * dist;

            if (TryEvaluateSpawnColumn(playerPos, candidateX, colOffset, colSize, minPlayerClearance, out validPoint))
            {
                return true;
            }
        }

        // Pass 2: Random attempts in [minPlayerClearance, maxSpawnDistance] around the player & level spawn anchor
        for (int i = 0; i < 20; i++)
        {
            Vector2 refOrigin = (i < 10) ? playerPos : arenaAnchor;
            float dir = (i % 2 == 0) ? 1f : -1f;
            float dist = Random.Range(minPlayerClearance, effectiveMaxDist);
            float candidateX = refOrigin.x + dir * dist;

            if (TryEvaluateSpawnColumn(refOrigin, candidateX, colOffset, colSize, minPlayerClearance, out validPoint))
            {
                return true;
            }
        }

        // Pass 3: Deterministic sweep outward from the player (left and right) outside minPlayerClearance
        for (float dist = minPlayerClearance; dist <= effectiveMaxDist + 6f; dist += 1.0f)
        {
            if (TryEvaluateSpawnColumn(playerPos, playerPos.x + dist, colOffset, colSize, minPlayerClearance, out validPoint))
            {
                return true;
            }
            if (TryEvaluateSpawnColumn(playerPos, playerPos.x - dist, colOffset, colSize, minPlayerClearance, out validPoint))
            {
                return true;
            }
        }

        // Pass 4: Deterministic sweep around the arena's playerSpawnPoint (still keeping minPlayerClearance from the player)
        if (playerSpawnPoint != null)
        {
            for (float dist = 0f; dist <= effectiveMaxDist + 8f; dist += 1.0f)
            {
                if (TryEvaluateSpawnColumn(arenaAnchor, arenaAnchor.x + dist, colOffset, colSize, minPlayerClearance, out validPoint))
                {
                    return true;
                }
                if (dist > 0f && TryEvaluateSpawnColumn(arenaAnchor, arenaAnchor.x - dist, colOffset, colSize, minPlayerClearance, out validPoint))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryEvaluateSpawnColumn(
        Vector2 refOrigin,
        float candidateX,
        Vector2 colOffset,
        Vector2 colSize,
        float minPlayerClearance,
        out Vector2 validSpawnPos)
    {
        validSpawnPos = Vector2.zero;

        // 1. Never spawn on top of the player! Keep horizontal distance >= minPlayerClearance
        if (player != null && Mathf.Abs(candidateX - player.transform.position.x) < minPlayerClearance)
        {
            return false;
        }

        // 2. Stay within the overall level collider bounds so we never spawn outside the map in the void
        if (hasLevelBounds)
        {
            float margin = Mathf.Max(1.2f, colSize.x);
            if (candidateX < levelColliderBounds.min.x + margin || candidateX > levelColliderBounds.max.x - margin)
            {
                return false;
            }
        }

        // 3. Probe vertical heights near refOrigin.y to find an open-air raycast start (not inside a ceiling or wall collider)
        float maxDownDist = Mathf.Max(raycastHeight * 1.5f, 18f);
        float footOffset = Mathf.Max(0.35f, colSize.x * 0.38f);
        float colliderBottomRelY = colOffset.y - (colSize.y * 0.5f);
        float clearanceY = Mathf.Max(0.08f, Mathf.Min(spawnHeightOffset, 0.2f));
        Vector2 checkBoxSize = new Vector2(
            Mathf.Max(colSize.x, checkExtent.x) + 0.1f,
            Mathf.Max(colSize.y, checkExtent.y)
        );
        float minPlayerClearanceSqr = minPlayerClearance * minPlayerClearance;

        for (int i = 0; i < ProbeHeights.Length; i++)
        {
            Vector2 airStart = new Vector2(candidateX, refOrigin.y + ProbeHeights[i]);

            // Ensure rayOrigin is in open air and NOT inside a wall/ceiling collider
            if (Physics2D.OverlapPoint(airStart, groundLayer) != null)
            {
                continue;
            }

            // Clamp below any low ceiling directly above airStart
            RaycastHit2D ceilingHit = Physics2D.Raycast(airStart, Vector2.up, 2.5f, groundLayer);
            if (ceilingHit.collider != null && ceilingHit.distance > 0.1f)
            {
                airStart.y = ceilingHit.point.y - 0.25f;
                if (Physics2D.OverlapPoint(airStart, groundLayer) != null)
                {
                    continue;
                }
            }

            // Cast downward to find the floor surface
            RaycastHit2D floorHit = Physics2D.Raycast(airStart, Vector2.down, maxDownDist, groundLayer);
            if (floorHit.collider == null || floorHit.distance <= 0.02f || floorHit.normal.y < 0.7f)
            {
                continue;
            }

            // 4. Ledge/Void check: ensure both the left foot and right foot have solid ground support beneath them
            if (!HasSolidFloorSupport(floorHit.point.x - footOffset, floorHit.point.y) ||
                !HasSolidFloorSupport(floorHit.point.x + footOffset, floorHit.point.y))
            {
                continue;
            }

            // 5. Compute exact transform position so the bottom of the enemy's BoxCollider2D sits cleanly above the floor
            Vector2 candidateTransformPos = new Vector2(
                floorHit.point.x,
                floorHit.point.y - colliderBottomRelY + clearanceY
            );

            // Check radial distance from player as well
            if (player != null && (candidateTransformPos - (Vector2)player.transform.position).sqrMagnitude < minPlayerClearanceSqr)
            {
                continue;
            }

            // 6. Verify the enemy's ACTUAL collider box (at candidateTransformPos + colOffset) does not overlap any wall or ground
            Vector2 candidateBoxCenter = candidateTransformPos + colOffset;
            if (Physics2D.OverlapBox(candidateBoxCenter, checkBoxSize, 0f, groundLayer) != null)
            {
                continue;
            }

            // 7. Verify the enemy isn't spawning on the outside of a boundary wall (check that walking slightly left/right doesn't immediately drop into the void)
            if (hasLevelBounds && candidateTransformPos.y < levelColliderBounds.min.y + 0.5f)
            {
                continue;
            }

            validSpawnPos = candidateTransformPos;
            return true;
        }

        return false;
    }

    private bool HasSolidFloorSupport(float x, float floorY)
    {
        Vector2 origin = new Vector2(x, floorY + 0.35f);
        if (Physics2D.OverlapPoint(origin, groundLayer) != null)
        {
            return false;
        }

        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, 0.9f, groundLayer);
        return hit.collider != null && hit.normal.y >= 0.65f;
    }

    private void GetPrefabColliderGeometry(GameObject prefab, out Vector2 colliderOffset, out Vector2 colliderSize)
    {
        colliderOffset = new Vector2(0f, 0.65f);
        colliderSize = checkExtent;

        if (prefab == null) return;

        if (prefabGeometryCache.TryGetValue(prefab, out (Vector2 offset, Vector2 size) cached))
        {
            colliderOffset = cached.offset;
            colliderSize = cached.size;
            return;
        }

        Vector3 localScale = prefab.transform.localScale;
        Vector2 scale = new Vector2(Mathf.Abs(localScale.x), Mathf.Abs(localScale.y));
        if (scale.x <= 0.001f) scale.x = 1f;
        if (scale.y <= 0.001f) scale.y = 1f;

        Collider2D[] colliders = prefab.GetComponentsInChildren<Collider2D>(true);
        Collider2D chosenCol = null;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && !colliders[i].isTrigger)
            {
                chosenCol = colliders[i];
                break;
            }
        }

        if (chosenCol == null && colliders.Length > 0)
        {
            chosenCol = colliders[0];
        }

        if (chosenCol is BoxCollider2D box)
        {
            colliderOffset = Vector2.Scale(box.offset, scale);
            colliderSize = Vector2.Scale(box.size, scale);
        }
        else if (chosenCol is CircleCollider2D circle)
        {
            colliderOffset = Vector2.Scale(circle.offset, scale);
            float diameter = circle.radius * 2f * Mathf.Max(scale.x, scale.y);
            colliderSize = new Vector2(diameter, diameter);
        }
        else if (chosenCol is CapsuleCollider2D capsule)
        {
            colliderOffset = Vector2.Scale(capsule.offset, scale);
            colliderSize = Vector2.Scale(capsule.size, scale);
        }

        prefabGeometryCache[prefab] = (colliderOffset, colliderSize);
    }

    #endregion

    #region Enemy Registration & Helpers

    public void RegisterEnemy(GameObject enemy)
    {
        if (enemy != null && !activeEnemies.Contains(enemy))
        {
            activeEnemies.Add(enemy);
        }
    }

    public void UnregisterEnemy(GameObject enemy)
    {
        activeEnemies.Remove(enemy);
    }

    private void PruneNullEnemies()
    {
        activeEnemies.RemoveAll(IsNullEnemyPredicate);
    }

    private void EnsurePlayerReference()
    {
        if (player == null)
        {
            player = PlayerController.Instance != null
                ? PlayerController.Instance
                : FindFirstObjectByType<PlayerController>();
        }
    }

    private void CacheLevelBounds()
    {
        hasLevelBounds = false;
        Collider2D[] allColliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        for (int i = 0; i < allColliders.Length; i++)
        {
            Collider2D col = allColliders[i];
            if (col == null || !col.enabled || col.isTrigger) continue;
            if (((1 << col.gameObject.layer) & groundLayer.value) == 0) continue;

            if (!hasLevelBounds)
            {
                levelColliderBounds = col.bounds;
                hasLevelBounds = true;
            }
            else
            {
                levelColliderBounds.Encapsulate(col.bounds);
            }
        }
    }

    private bool HasAnyLivingEnemies()
    {
        EnemyBase[] remaining = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);
        for (int i = 0; i < remaining.Length; i++)
        {
            EnemyBase enemy = remaining[i];
            if (enemy != null && !enemy.IsDead && !(enemy is ArmoredNinjaWeakPoint) && !(enemy is BossTargetPoint))
            {
                return true;
            }
        }
        return false;
    }

    #endregion
}
