using System.Collections;
using System.Collections.Generic;
using System.Net;
using Unity.VisualScripting;
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
    private Coroutine activeCoroutine;
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
        Time.fixedDeltaTime = this.fixedDeltaTime * Time.timeScale;
    }

    void ExitChainSlashMode()
    {
        isInMode = false;

        foreach (GameObject enemy in targetEnemies)
        {
            if (enemy != null)
            {
                if (activeCoroutine != null)
                {
                    StopCoroutine(activeCoroutine);
                    Destroy(enemy);
                }
            }
        }

        if (targetEnemies.Count > 0)
        {
            PlayerController playerController = GetComponent<PlayerController>();
            playerController.PlaySFX(postSlashSFX);
        }

        targetEnemies.Clear();
        lineRenderer.positionCount = 0;
        Time.timeScale = 1.0f;

        lineRenderer.enabled = false;
    }

    public void LineSetup(Transform enemyPos)
    {
        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);

            if (targetEnemies.Count > 1)
            {
                Vector2 endPos = targetEnemies[targetEnemies.Count - 2].transform.position;
                lineRenderer.SetPosition(targetEnemies.Count - 2, endPos);
            }
        }

        activeCoroutine = StartCoroutine(AnimateLine(enemyPos));
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
            float time = (Time.time - startTime) / 0.25f;
            animatePos = Vector2.Lerp(startPos, endPos, time);
            lineRenderer.SetPosition(targetEnemies.Count - 1, animatePos);
            yield return null;
        }

        activeCoroutine = null;
    }
}
