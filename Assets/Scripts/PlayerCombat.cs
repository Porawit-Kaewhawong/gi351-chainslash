using System.Collections;
using System.Collections.Generic;
using System.Net;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class PlayerCombat : MonoBehaviour
{
    public static PlayerCombat Instance { get; private set; }

    [Header("Chain Slash Mode Settings")]
    [SerializeField] private float timeSpeed = 0.25f;
    [SerializeField] private AudioClip targetSelectSFX;
    [SerializeField] private AudioClip postSlashSFX;
    public List<GameObject> targetEnemies = new List<GameObject>();
    [HideInInspector] public bool isInMode { get; private set; }
    private float fixedDeltaTime;
    private Coroutine activeAnimateCoroutine;
    private Coroutine activeKillCoroutine;
    private InputAction attackAction;
    private LineRenderer lineRenderer;

    void Awake()
    {
        Instance = this;
        lineRenderer = GetComponent<LineRenderer>();
        attackAction = InputSystem.actions.FindAction("Attack");
    }

    void Start()
    {
        fixedDeltaTime = Time.deltaTime;
    }


    void Update()
    {
        if (attackAction.IsPressed())
        {
            if (!isInMode)
            {
                EnterChainSlashMode();
            }
        }
        else
        {
            if (isInMode)
            {
                ExitChainSlashMode();
            }
        }
    }

    void EnterChainSlashMode()
    {
        isInMode = true;

        Time.timeScale = timeSpeed;
        Time.fixedDeltaTime = fixedDeltaTime * Time.timeScale;
    }

    void ExitChainSlashMode()
    {
        isInMode = false;

        if (activeAnimateCoroutine != null)
        {
            StopCoroutine(activeAnimateCoroutine);
        }

        if (activeKillCoroutine != null)
        {
            StopCoroutine(activeKillCoroutine);
        }
        activeKillCoroutine = StartCoroutine(AnimateKillLine());

        if (targetEnemies.Count > 0)
        {
            PlayerController playerController = GetComponent<PlayerController>();
            playerController.PlaySFX(postSlashSFX);
        }

        Time.timeScale = 1.0f;
    }

    public void TargetSetup(Transform enemyPos)
    {
        if (activeAnimateCoroutine != null)
        {
            StopCoroutine(activeAnimateCoroutine);

            if (targetEnemies.Count > 1)
            {
                Vector2 endPos = targetEnemies[targetEnemies.Count - 2].transform.position;
                lineRenderer.SetPosition(targetEnemies.Count - 2, endPos);
            }
        }

        activeAnimateCoroutine = StartCoroutine(AnimateSlashLine(enemyPos));
    }

    public IEnumerator AnimateKillLine()
    {
        lineRenderer.positionCount = 0;

        PlayerController playerController = GetComponent<PlayerController>();
        playerController.PlaySFX(null);

        for (int i = 0; i < targetEnemies.Count; i++)
        {
            lineRenderer.positionCount++;
            lineRenderer.SetPosition(i, targetEnemies[i].transform.position);

            if (i > 0)
            {
                float timer = 0f;
                float duration = 0.1f;

                while (timer < duration)
                {
                    timer += Time.deltaTime;
                    float time = timer / duration;

                    Vector2 currentStart = targetEnemies[i - 1].transform.position;
                    Vector2 currentEnd = targetEnemies[i].transform.position;
                    
                    lineRenderer.SetPosition(i, Vector2.Lerp(currentStart, currentEnd, time));

                    yield return null;
                }
            }
        }

        for (int i = 0; i < targetEnemies.Count; i++)
        {
            if (targetEnemies[i].TryGetComponent<Enemy>(out Enemy enemy))
            {
                if (enemy != null)
                {
                    enemy.OnDeath();
                }
            }
            else if (targetEnemies[i].TryGetComponent<ChainEnemy>(out ChainEnemy chainEnemy))
            {
                if (chainEnemy != null)
                {
                    chainEnemy.OnDeath();
                }
            }
        }

        targetEnemies.Clear();

        lineRenderer.positionCount = 0;
        lineRenderer.enabled = false;

        activeKillCoroutine = null;
    }

    public IEnumerator AnimateSlashLine(Transform enemyPos)
    {
        PlayerController playerController = GetComponent<PlayerController>();
        playerController.PlaySFX(targetSelectSFX);

        lineRenderer.positionCount = targetEnemies.Count;
        lineRenderer.SetPosition(targetEnemies.Count - 1, enemyPos.position);

        if (lineRenderer.positionCount <= 1) yield break;

        lineRenderer.enabled = true;

        float timer = 0f;
        float duration = 0.05f;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float time = timer / duration;

            Vector2 currentStart = targetEnemies[targetEnemies.Count - 2].transform.position;
            Vector2 currentEnd = targetEnemies[targetEnemies.Count - 1].transform.position;

            lineRenderer.SetPosition(targetEnemies.Count - 1, Vector2.Lerp(currentStart, currentEnd, time));

            yield return null;
        }

        activeAnimateCoroutine = null;
    }
}
