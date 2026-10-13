using System.Collections;
using System.Collections.Generic;
using System.Net;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class PlayerCombat : MonoBehaviour
{
    public static PlayerCombat Instance { get; private set; }

    [Header("Chain Slash Mode Settings")]
    [SerializeField] private float timeSpeed = 0.25f;
    [SerializeField] private AudioClip targetSelectSFX;
    [SerializeField] private AudioClip postSlashSFX;
    [SerializeField] private GameObject lineRendererObject;
    public List<GameObject> targetEnemies = new List<GameObject>();
    public bool isInMode { get; private set; }
    private float fixedDeltaTime;
    private Coroutine activeAnimateCoroutine;
    private Coroutine activeKillCoroutine;
    private InputAction attackAction;
    private PlayerController playerController;
    private GameObject insLineRendererObj;
    private LineRenderer insLineRendererCom;

    void Awake()
    {
        Instance = this;
        attackAction = InputSystem.actions.FindAction("Attack");
        playerController = GetComponent<PlayerController>();
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

        if (isInMode && insLineRendererObj != null)
        {
            insLineRendererCom.positionCount = targetEnemies.Count;

            for (int i = 0; i < targetEnemies.Count; i++)
            {
                if (targetEnemies[i] != null)
                {
                    if (!targetEnemies[i].IsDestroyed())
                    {
                        insLineRendererCom.SetPosition(i, targetEnemies[i].transform.position);
                    }
                }
            }
        }

        if (targetEnemies.Count > 0)
        {
            for (int i = 0; i < targetEnemies.Count; i++)
            {
                if (targetEnemies[i].gameObject == null)
                {
                    targetEnemies.Remove(targetEnemies[i]);
                }
            }
        }
    }

    void EnterChainSlashMode()
    {
        isInMode = true;

        Time.timeScale = timeSpeed;
        Time.fixedDeltaTime = fixedDeltaTime * Time.timeScale;

        insLineRendererObj = Instantiate(lineRendererObject);
        insLineRendererCom = insLineRendererObj.GetComponent<LineRenderer>();
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

        activeKillCoroutine = StartCoroutine(AnimateKillLine(insLineRendererCom));

        Time.timeScale = 1.0f;
    }

    public void TargetSetup(Transform enemyPos)
    {
        if (insLineRendererCom != null)
        {
            StartCoroutine(AnimateSlashLine(enemyPos, insLineRendererCom));
        }

        if (activeKillCoroutine != null)
        {
            StopCoroutine(activeKillCoroutine);
        }
        
        if (activeAnimateCoroutine != null)
        {
            StopCoroutine(activeAnimateCoroutine);

            if (targetEnemies.Count >= 2 && targetEnemies[targetEnemies.Count - 2] != null)
            {
                Vector2 endPos = targetEnemies[targetEnemies.Count - 2].transform.position;
                insLineRendererCom.SetPosition(targetEnemies.Count - 2, endPos);
            }
        }

        activeAnimateCoroutine = StartCoroutine(AnimateSlashLine(enemyPos, insLineRendererCom));
    }

    public IEnumerator AnimateSlashLine(Transform enemyPos, LineRenderer lineRenderer)
    {
        playerController.PlaySFX(targetSelectSFX);

        int index = targetEnemies.Count;

        lineRenderer.positionCount++;
        lineRenderer.SetPosition(index - 1, enemyPos.position);

        if (lineRenderer.positionCount < 2) yield break;

        lineRenderer.enabled = true;

        if (index >= 2 && targetEnemies[index - 2] != null)
        {
            Vector2 currentStart = targetEnemies[index - 2].transform.position;
            Vector2 currentEnd = targetEnemies[index - 1].transform.position;

            float timer = 0f;
            float duration = 0.05f;
            while (timer < duration)
            {
                timer += Time.deltaTime;
                float time = timer / duration;

                lineRenderer.SetPosition(index - 1, Vector2.Lerp(currentStart, currentEnd, time));

                yield return null;
            }

            activeAnimateCoroutine = null;
        }
    }

    public IEnumerator AnimateKillLine(LineRenderer lineRenderer)
    {
        lineRenderer.enabled = false;

        yield return new WaitForSeconds(0.25f);

        lineRenderer.enabled = true;

        for (int i = 0; i < targetEnemies.Count; i++)
        {
            if (targetEnemies[i] != null)
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
        }

        yield return new WaitForSeconds(0.05f);

        float orgWidth = lineRenderer.widthMultiplier;
        float duration = 0.25f;
        float timer = 0;

        while (timer < duration / 2)
        {
            timer += Time.deltaTime;
            float time = timer / (duration / 2);

            lineRenderer.widthMultiplier = Mathf.Lerp(orgWidth, orgWidth * 2, time);
            yield return null;
        }

        timer = 0;

        while (timer < duration / 2)
        {
            timer += Time.deltaTime;
            float time = timer / (duration / 2);

            lineRenderer.widthMultiplier = Mathf.Lerp(orgWidth * 2, orgWidth, time);
            yield return null;
        }

        if (targetEnemies.Count > 0)
        {
            playerController.PlaySFX(postSlashSFX);
        }

        yield return new WaitForSeconds(0.25f);

        lineRenderer.enabled = false;

        targetEnemies.Clear();

        lineRenderer.positionCount = 0;
        lineRenderer.enabled = false;

        Destroy(insLineRendererCom);
        Destroy(insLineRendererObj);

        activeKillCoroutine = null;
    }
}
