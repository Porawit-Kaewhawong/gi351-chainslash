using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

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
        PerWaveCounts,   // Spawns specified spawnCount of each prefab every wave cycle
        WeightedRandom   // Selects prefabs randomly based on their relative spawnWeight
    }

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

    public Transform PlayerSpawnPoint => playerSpawnPoint;

    [Header("Wave UI & Events (Optional)")]
    [SerializeField] private GameObject victoryUIObject;
    public UnityEvent<int, int> onWaveStarted;
    public UnityEvent<int> onWaveCleared;
    public UnityEvent onAllWavesCompleted;

    [Header("Ground & Raycast Settings")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float minSpawnDistance = 6f;
    [SerializeField] private float maxSpawnDistance = 14f;
    [SerializeField] private float raycastHeight = 12f;
    [SerializeField] private float spawnHeightOffset = 0.1f;

    [Header("Spawn Validation")]
    [SerializeField] private Vector2 checkExtent = new Vector2(0.8f, 0.8f);

    public int CurrentWave { get; private set; }
    public int TotalWaves => totalWaves;
    public int ActiveEnemyCount => activeEnemies.Count;
    public bool AllWavesCompleted { get; private set; }

    private PlayerController player;
    private List<GameObject> activeEnemies = new List<GameObject>();
    private Bounds levelColliderBounds;
    private bool hasLevelBounds;

    private WaitForSeconds waitForSpawnInterval;
    private WaitForSeconds waitForWaveInterval;

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

        if (victoryUIObject != null)
        {
            victoryUIObject.SetActive(false);
        }

        if (portalSceneObject != null)
        {
            portalSceneObject.SetActive(false);
        }

        CacheLevelBounds();
    }

    private void Start()
    {
        player = PlayerController.Instance != null ? PlayerController.Instance : FindFirstObjectByType<PlayerController>();

        if (player != null && enemyConfigs.Count > 0)
        {
            StartCoroutine(SpawnRoutine());
            StartCoroutine(MonitorVoidEnemiesRoutine());
        }
        else
        {
            Debug.LogWarning("GameManager: PlayerController or EnemyConfigs missing!");
        }
    }

    private void CacheLevelBounds()
    {
        hasLevelBounds = false;
        Collider2D[] allColliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
        foreach (Collider2D col in allColliders)
        {
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
            onWaveStarted?.Invoke(CurrentWave, totalWaves);

            // Clean up destroyed enemy references
            activeEnemies.RemoveAll(enemy => enemy == null);

            if (player == null)
            {
                player = PlayerController.Instance != null ? PlayerController.Instance : FindFirstObjectByType<PlayerController>();
            }

            if (player != null)
            {
                if (spawnMode == SpawnMode.PerWaveCounts)
                {
                    yield return StartCoroutine(SpawnByWaveCounts());
                }
                else if (spawnMode == SpawnMode.WeightedRandom)
                {
                    yield return StartCoroutine(SpawnByWeightedRandom());
                }
            }

            // Wait for the current wave to be cleared if enabled, or if this is the final wave
            bool isFinalWave = totalWaves > 0 && CurrentWave >= totalWaves;
            if (waitForWaveClear || isFinalWave)
            {
                while (activeEnemies.Count > 0)
                {
                    activeEnemies.RemoveAll(enemy => enemy == null);
                    yield return null;
                }
            }

            onWaveCleared?.Invoke(CurrentWave);

            // Wait between waves if there are more waves coming
            if (!isFinalWave)
            {
                yield return waitForWaveInterval;
            }
        }

        // Ensure any remaining enemies in the scene (including pre-placed enemies) are completely gone
        while (activeEnemies.Count > 0 || FindObjectsByType<EnemyBase>(FindObjectsSortMode.None).Length > 0)
        {
            activeEnemies.RemoveAll(enemy => enemy == null);
            yield return null;
        }

        AllWavesCompleted = true;
        Debug.Log($"All {totalWaves} waves completed!");

        SpawnNextLevelPortal();

        if (victoryUIObject != null)
        {
            victoryUIObject.SetActive(true);
        }

        onAllWavesCompleted?.Invoke();
    }

    /// <summary>
    /// Rescues any enemy that accidentally falls below the level into the void so a wave never soft-locks.
    /// </summary>
    private IEnumerator MonitorVoidEnemiesRoutine()
    {
        WaitForSeconds checkWait = new WaitForSeconds(0.5f);

        while (!AllWavesCompleted)
        {
            activeEnemies.RemoveAll(enemy => enemy == null);

            float voidThresholdY = hasLevelBounds
                ? levelColliderBounds.min.y - 8f
                : (player != null ? player.transform.position.y - 30f : -40f);

            for (int i = 0; i < activeEnemies.Count; i++)
            {
                GameObject enemyObj = activeEnemies[i];
                if (enemyObj == null) continue;

                EnemyBase enemyBase = enemyObj.GetComponent<EnemyBase>();
                if (enemyBase != null && enemyBase.IsDead) continue;

                if (enemyObj.transform.position.y < voidThresholdY)
                {
                    if (TryGetValidSpawnPoint(out Vector2 rescuedPos, enemyObj))
                    {
                        enemyObj.transform.position = rescuedPos;
                        Rigidbody2D enemyRb = enemyObj.GetComponent<Rigidbody2D>();
                        if (enemyRb != null)
                        {
                            enemyRb.linearVelocity = Vector2.zero;
                        }
                        Physics2D.SyncTransforms();
                    }
                }
            }

            yield return checkWait;
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

    // Mode 1: Spawns specific quantities per enemy type per wave
    private IEnumerator SpawnByWaveCounts()
    {
        int waveBonus = Mathf.Max(0, (CurrentWave - 1) * bonusEnemiesPerWave);

        foreach (EnemySpawnConfig config in enemyConfigs)
        {
            if (config.prefab == null) continue;

            int countToSpawn = config.spawnCount + (config.spawnCount > 0 ? waveBonus : 0);

            for (int i = 0; i < countToSpawn; i++)
            {
                // Wait if maxActiveEnemies cap is currently reached so the wave still spawns its full quota
                while (activeEnemies.Count >= maxActiveEnemies)
                {
                    activeEnemies.RemoveAll(enemy => enemy == null);
                    yield return null;
                }

                if (player != null && TryGetValidSpawnPoint(out Vector2 spawnPoint, config.prefab))
                {
                    GameObject spawnedEnemy = Instantiate(config.prefab, spawnPoint, Quaternion.identity);
                    activeEnemies.Add(spawnedEnemy);
                }

                yield return waitForSpawnInterval;
            }
        }
    }

    // Mode 2: Randomly picks prefabs according to relative weight
    private IEnumerator SpawnByWeightedRandom()
    {
        int totalWeight = 0;
        foreach (EnemySpawnConfig config in enemyConfigs)
        {
            if (config.prefab != null && config.spawnWeight > 0)
            {
                totalWeight += config.spawnWeight;
            }
        }

        if (totalWeight <= 0) yield break;

        int totalToSpawn = enemiesPerWave + Mathf.Max(0, (CurrentWave - 1) * bonusEnemiesPerWave);

        for (int i = 0; i < totalToSpawn; i++)
        {
            while (activeEnemies.Count >= maxActiveEnemies)
            {
                activeEnemies.RemoveAll(enemy => enemy == null);
                yield return null;
            }

            GameObject selectedPrefab = GetRandomPrefabByWeight(totalWeight);
            if (player != null && selectedPrefab != null && TryGetValidSpawnPoint(out Vector2 spawnPoint, selectedPrefab))
            {
                GameObject spawnedEnemy = Instantiate(selectedPrefab, spawnPoint, Quaternion.identity);
                activeEnemies.Add(spawnedEnemy);
            }

            yield return waitForSpawnInterval;
        }
    }

    private GameObject GetRandomPrefabByWeight(int totalWeight)
    {
        int randomValue = Random.Range(0, totalWeight);
        int currentSum = 0;

        foreach (EnemySpawnConfig config in enemyConfigs)
        {
            if (config.prefab == null || config.spawnWeight <= 0) continue;

            currentSum += config.spawnWeight;
            if (randomValue < currentSum)
            {
                return config.prefab;
            }
        }

        return null;
    }

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
        float minPlayerClearance = 2.5f;

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
        float[] probeHeights = new float[] { 1.5f, 3.5f, 0.5f, 6f, -1f, 9f, -3f };

        foreach (float yOff in probeHeights)
        {
            Vector2 airStart = new Vector2(candidateX, refOrigin.y + yOff);

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
            float maxDownDist = Mathf.Max(raycastHeight * 1.5f, 18f);
            RaycastHit2D floorHit = Physics2D.Raycast(airStart, Vector2.down, maxDownDist, groundLayer);
            if (floorHit.collider == null || floorHit.distance <= 0.02f || floorHit.normal.y < 0.7f)
            {
                continue;
            }

            // 4. Ledge/Void check: ensure both the left foot and right foot have solid ground support beneath them
            float footOffset = Mathf.Max(0.35f, colSize.x * 0.38f);
            if (!HasSolidFloorSupport(floorHit.point.x - footOffset, floorHit.point.y) ||
                !HasSolidFloorSupport(floorHit.point.x + footOffset, floorHit.point.y))
            {
                continue;
            }

            // 5. Compute exact transform position so the bottom of the enemy's BoxCollider2D sits cleanly above the floor
            float colliderBottomRelY = colOffset.y - (colSize.y * 0.5f);
            float clearanceY = Mathf.Max(0.08f, Mathf.Min(spawnHeightOffset, 0.2f));
            Vector2 candidateTransformPos = new Vector2(
                floorHit.point.x,
                floorHit.point.y - colliderBottomRelY + clearanceY
            );

            // Check radial distance from player as well
            if (player != null && Vector2.Distance(candidateTransformPos, player.transform.position) < minPlayerClearance)
            {
                continue;
            }

            // 6. Verify the enemy's ACTUAL collider box (at candidateTransformPos + colOffset) does not overlap any wall or ground
            Vector2 candidateBoxCenter = candidateTransformPos + colOffset;
            Vector2 checkBoxSize = new Vector2(
                Mathf.Max(colSize.x, checkExtent.x) + 0.1f,
                Mathf.Max(colSize.y, checkExtent.y)
            );

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

        Vector2 scale = new Vector2(Mathf.Abs(prefab.transform.localScale.x), Mathf.Abs(prefab.transform.localScale.y));
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
    }

    public void UnregisterEnemy(GameObject enemy)
    {
        if (activeEnemies.Contains(enemy))
        {
            activeEnemies.Remove(enemy);
        }
    }
}