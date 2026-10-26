using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    [SerializeField] private int enemiesPerWave = 10; // Used for WeightedRandom mode
    [SerializeField] private float timeBetweenWaves = 3f;
    [SerializeField] private float timeBetweenSpawns = 0.2f;
    [SerializeField] private int maxActiveEnemies = 20;

    [Header("Ground & Raycast Settings")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float minSpawnDistance = 6f;
    [SerializeField] private float maxSpawnDistance = 14f;
    [SerializeField] private float raycastHeight = 12f;
    [SerializeField] private float spawnHeightOffset = 0.5f;

    [Header("Spawn Validation")]
    [SerializeField] private Vector2 checkExtent = new Vector2(0.8f, 0.8f);

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
        while (true)
        {
            // Clean up destroyed enemy references
            activeEnemies.RemoveAll(enemy => enemy == null);

            if (player != null && activeEnemies.Count < maxActiveEnemies)
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

            yield return waitForWaveInterval;
        }
    }

    // Mode 1: Spawns specific quantities per enemy type per wave
    private IEnumerator SpawnByWaveCounts()
    {
        foreach (EnemySpawnConfig config in enemyConfigs)
        {
            if (config.prefab == null) continue;

            for (int i = 0; i < config.spawnCount; i++)
            {
                if (activeEnemies.Count >= maxActiveEnemies) yield break;

                if (TryGetValidSpawnPoint(out Vector2 spawnPoint, config.prefab))
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

        for (int i = 0; i < enemiesPerWave; i++)
        {
            if (activeEnemies.Count >= maxActiveEnemies) yield break;

            GameObject selectedPrefab = GetRandomPrefabByWeight(totalWeight);
            if (selectedPrefab != null && TryGetValidSpawnPoint(out Vector2 spawnPoint, selectedPrefab))
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