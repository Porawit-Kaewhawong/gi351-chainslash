using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public List<GameObject> enemies = new List<GameObject>();
    private float betweenWaveTime = 3f;
    private PlayerController player;

    void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        StartCoroutine(spawnEnemies());
    }

    private IEnumerator spawnEnemies()
    {
        while (true)
        {
            foreach (GameObject enemy in enemies)
            {
                Vector2 randomPos = Random.insideUnitCircle * 10f;
                Vector2 spawnPos = player.gameObject.transform.position + (Vector3)randomPos;

                Instantiate(enemy, spawnPos, Quaternion.identity);
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(betweenWaveTime);
        }
    }
}
