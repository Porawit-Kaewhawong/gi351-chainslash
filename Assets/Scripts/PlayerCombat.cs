using System.Collections;
using System.Collections.Generic;
using System.Net;
using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCombat : MonoBehaviour
{
    public static PlayerCombat Instance { get; private set; }

    [Header("Chain Slash Mode Settings")]
    [SerializeField] private float timeSpeed = 0.25f;
    [SerializeField] private AudioClip targetSelectSFX;
    [SerializeField] private AudioClip postSlashSFX;
    [SerializeField] private GameObject lineRendererObject;
    [SerializeField] private CinemachineTargetGroup targetGroup;
    public bool isInMode { get; private set; }
    private float fixedDeltaTime;
    private Coroutine activeAnimateCoroutine;
    private InputAction attackAction;
    private PlayerController playerController;
    private GameObject insLineRendererObj;
    private LineRenderer insLineRendererCom;

    void Awake()
    {
        Instance = this;
        attackAction = InputSystem.actions.FindAction("Attack");
        playerController = GetComponent<PlayerController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
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
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                EnterChainSlashMode();
            }
        }
        else
        {
            if (isInMode)
            {
                if (Cursor.lockState == CursorLockMode.None)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                ExitChainSlashMode();
            }
        }

        if (isInMode && insLineRendererObj != null)
        {
            LineRendererManager lineRendererManager = insLineRendererObj.GetComponent<LineRendererManager>();

            insLineRendererCom.positionCount = lineRendererManager.targets.Count;

            for (int i = 0; i < lineRendererManager.targets.Count; i++)
            {
                if (lineRendererManager.targets[i] != null)
                {
                    if (!lineRendererManager.targets[i].IsDestroyed())
                    {
                        insLineRendererCom.SetPosition(i, lineRendererManager.targets[i].transform.position);
                    }
                }
            }
        }

        if (insLineRendererObj != null)
        {
            LineRendererManager lineRendererManager = insLineRendererObj.GetComponent<LineRendererManager>();

            if (lineRendererManager.targets.Count > 0)
            {
                for (int i = 0; i < lineRendererManager.targets.Count; i++)
                {
                    if (lineRendererManager.targets[i].gameObject == null)
                    {
                        lineRendererManager.targets.Remove(lineRendererManager.targets[i]);
                    }
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

        targetGroup.Targets[1].Object = null;

        if (activeAnimateCoroutine != null)
        {
            StopCoroutine(activeAnimateCoroutine);
        }

        if (insLineRendererObj != null)
        {
            LineRendererManager lineRendererManager = insLineRendererObj.GetComponent<LineRendererManager>();
            lineRendererManager.activeCoroutine = StartCoroutine(AnimateKillLine(insLineRendererCom));
        }

        insLineRendererCom = null;
        insLineRendererObj = null;

        Time.timeScale = 1.0f;
    }

    public void TargetSetup(Transform enemyPos)
    {
        if (insLineRendererObj != null)
        {
            targetGroup.Targets[1].Object = enemyPos;

            LineRendererManager lineRendererManager = insLineRendererObj.GetComponent<LineRendererManager>();

            lineRendererManager.targets.Add(enemyPos.gameObject);

            if (lineRendererManager.activeCoroutine != null)
            {
                StopCoroutine(lineRendererManager.activeCoroutine);

                if (lineRendererManager.targets.Count >= 2 && lineRendererManager.targets[lineRendererManager.targets.Count - 2] != null)
                {
                    Vector2 endPos = lineRendererManager.targets[lineRendererManager.targets.Count - 2].transform.position;
                    insLineRendererCom.SetPosition(lineRendererManager.targets.Count - 2, endPos);
                }
            }

            lineRendererManager.activeCoroutine = StartCoroutine(AnimateSlashLine(enemyPos, insLineRendererCom));
        }
    }

    public IEnumerator AnimateSlashLine(Transform enemyPos, LineRenderer lineRenderer)
    {
        LineRendererManager lineRendererManager = lineRenderer.GetComponent<LineRendererManager>();

        playerController.PlaySFX(targetSelectSFX);

        int index = lineRendererManager.targets.Count;

        lineRenderer.positionCount++;
        lineRenderer.SetPosition(index - 1, enemyPos.position);

        if (lineRenderer.positionCount < 2) yield break;

        lineRenderer.enabled = true;

        if (index >= 2 && lineRendererManager.targets[index - 2] != null)
        {
            Vector2 currentStart = lineRendererManager.targets[index - 2].transform.position;
            Vector2 currentEnd = lineRendererManager.targets[index - 1].transform.position;

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
        LineRendererManager lineRendererManager = lineRenderer.GetComponent<LineRendererManager>();

        if (lineRendererManager.targets.Count == 0)
        {
            Destroy(lineRenderer.gameObject);
            yield break;
        }

        lineRenderer.enabled = false;

        yield return new WaitForSeconds(0.25f);

        lineRenderer.enabled = true;

        for (int i = 0; i < lineRendererManager.targets.Count; i++)
        {
            if (lineRendererManager.targets[i] != null)
            {
                if (lineRendererManager.targets[i].TryGetComponent<Enemy>(out Enemy enemy))
                {
                    if (enemy != null)
                    {
                        enemy.OnDeath();
                    }
                }
                else if (lineRendererManager.targets[i].TryGetComponent<ChainEnemy>(out ChainEnemy chainEnemy))
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

        if (lineRendererManager.targets.Count > 0)
        {
            playerController.PlaySFX(postSlashSFX);
        }

        timer = 0;

        while (timer < duration / 2)
        {
            timer += Time.deltaTime;
            float time = timer / (duration / 2);

            lineRenderer.widthMultiplier = Mathf.Lerp(orgWidth * 2, orgWidth, time);
            yield return null;
        }

        yield return new WaitForSeconds(0.25f);

        lineRenderer.enabled = false;

        Destroy(lineRendererManager.gameObject);
    }
}
