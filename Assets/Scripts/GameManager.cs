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
    [Tooltip("Portal prefab to spawn once all waves are completed and all enemies are gone.")]
    [SerializeField] private GameObject portalPrefab;
    [Tooltip("Optional existing Portal object in the scene (hidden at start, activated when all waves are cleared).")]
    [SerializeField] private GameObject portalSceneObject;
    [Tooltip("Optional specific Transform where the portal spawns. If empty, spawns on valid ground near the player.")]
    [SerializeField] private Transform portalSpawnPoint;

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
    [SerializeField] private float spawnHeightOffset = 0.5f;

    [Header("Spawn Validation")]
    [SerializeField] private Vector2 checkExtent = new Vector2(0.8f, 0.8f);

    public int CurrentWave { get; private set; }
    public int TotalWaves => totalWaves;
    public int ActiveEnemyCount => activeEnemies.Count;
    public bool AllWavesCompleted { get; private set; }

    private PlayerController player;
    private List<GameObject> activeEnemies = new List<GameObject>();

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
    }

    private void Start()
    {
        player = FindFirstObjectByType<PlayerController>();

        if (player != null && enemyConfigs.Count > 0)
        {
            StartCoroutine(SpawnRoutine());
        }
        else
        {
            Debug.LogWarning("GameManager: PlayerController or EnemyConfigs missing!");
        }
    }

    private IEnumerator SpawnRoutine()
    {
        CurrentWave = 0;
        AllWavesCompleted = false;

        // Loop while in endless mode (totalWaves <= 0) or until totalWaves is reached
        while (totalWaves <= 0 || CurrentWave < totalWaves)
        {
            CurrentWave++;
            onWaveStarted?.Invoke(CurrentWave, totalWaves);

            // Clean up destroyed enemy references
            activeEnemies.RemoveAll(enemy => enemy == null);

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

                if ( player != null && TryGetValidSpawnPoint(out Vector2 spawnPoint, config.prefab))
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

    private bool TryGetValidSpawnPoint(out Vector2 validPoint, GameObject enemyPrefab)
    {
        validPoint = Vector2.zero;

        for (int i = 0; i < 5; i++)
        {
            float randomXOffset = Random.Range(minSpawnDistance, maxSpawnDistance) * (Random.value > 0.5f ? 1f : -1f);
            Vector2 originX = (Vector2)player.transform.position + new Vector2(randomXOffset, 0f);
            Vector2 rayOrigin = originX + Vector2.up * raycastHeight;

            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, raycastHeight * 2f, groundLayer);

            if (hit.collider != null)
            {
                float verticalOffset = spawnHeightOffset;
                Collider2D enemyCol = enemyPrefab.GetComponentInChildren<Collider2D>();
                if (enemyCol != null)
                {
                    verticalOffset += enemyCol.bounds.extents.y;
                }

                Vector2 candidatePoint = hit.point + Vector2.up * verticalOffset;

                Collider2D overlap = Physics2D.OverlapBox(candidatePoint, checkExtent, 0f, groundLayer);
                if (overlap == null)
                {
                    validPoint = candidatePoint;
                    return true;
                }
            }
        }

        return false;
    }

    public void UnregisterEnemy(GameObject enemy)
    {
        if (activeEnemies.Contains(enemy))
        {
            activeEnemies.Remove(enemy);
        }
    }
}