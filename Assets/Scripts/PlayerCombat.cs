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

        if (isInMode)
        {
            for (int i = 0; i < targetEnemies.Count; i++)
            {
                if (targetEnemies[i] != null)
                {
                    lineRenderer.SetPosition(i, targetEnemies[i].transform.position);
                }
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
        activeKillCoroutine = StartCoroutine(AnimateLineKill());

        if (targetEnemies.Count > 0)
        {
            PlayerController playerController = GetComponent<PlayerController>();
            playerController.PlaySFX(postSlashSFX);
        }

        Time.timeScale = 1.0f;
    }

    public void LineSetup(Transform enemyPos)
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

        activeAnimateCoroutine = StartCoroutine(AnimateLine(enemyPos));
    }

    public IEnumerator AnimateLineKill()
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
                Vector2 startPos = lineRenderer.GetPosition(i - 1);
                Vector2 endPos = lineRenderer.GetPosition(i);

                float startTime = Time.time;
                Vector2 animatePos = startPos;

                while (animatePos != endPos)
                {
                    float time = (Time.time - startTime) / 0.1f;
                    animatePos = Vector2.Lerp(startPos, endPos, time);
                    lineRenderer.SetPosition(i, animatePos);
                    yield return null;
                }
            }
        }

        foreach (GameObject enemy in targetEnemies)
        {
            if (enemy != null)
            {
                yield return new WaitForSeconds(0.025f);
                Destroy(enemy);
            }
        }

        targetEnemies.Clear();

        lineRenderer.positionCount = 0;
        lineRenderer.enabled = false;

        activeKillCoroutine = null;
    }

    public IEnumerator AnimateLine(Transform enemyPos)
    {
        PlayerController playerController = GetComponent<PlayerController>();
        playerController.PlaySFX(targetSelectSFX);

        lineRenderer.positionCount = targetEnemies.Count;
        lineRenderer.SetPosition(targetEnemies.Count - 1, enemyPos.position);

        if (lineRenderer.positionCount <= 1) yield break;

        Vector2 startPos = lineRenderer.GetPosition(targetEnemies.Count - 2);
        Vector2 endPos = lineRenderer.GetPosition(targetEnemies.Count - 1);

        lineRenderer.enabled = true;

        float startTime = Time.time;
        Vector2 animatePos = startPos;
        while (animatePos != endPos)
        {
            float time = (Time.time - startTime) / 0.1f;

            startPos = targetEnemies[targetEnemies.Count - 2].transform.position;
            endPos = targetEnemies[targetEnemies.Count - 1].transform.position;

            animatePos = Vector2.Lerp(startPos, endPos, time);
            lineRenderer.SetPosition(targetEnemies.Count - 1, animatePos);
            yield return null;
        }

        activeAnimateCoroutine = null;
    }
}
