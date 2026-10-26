using System.Collections;
using System.Collections.Generic;
using System.Net;
using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PlayerCombat : MonoBehaviour
{
    public static PlayerCombat Instance { get; private set; }

    [Header("Chain Slash Mode Settings")]
    [SerializeField] private float maxMeter = 100f;
    [SerializeField] private float timeSpeed = 0.25f;
    [SerializeField] private AudioClip targetSelectSFX;
    [SerializeField] private AudioClip postSlashSFX;
    [SerializeField] private GameObject lineRendererObject;
    [SerializeField] private CinemachineTargetGroup targetGroup;
    [SerializeField] private Image modeMeterImage;
    [SerializeField] private NoiseSettings noiseSettings;
    
    private float orgAmplitudeGain;
    public bool IsInMode { get; private set; }
    private float currentMeter;
    private float fixedDeltaTime;
    private Coroutine activeAnimateCoroutine;
    private Coroutine activeMeterCoroutine;
    private InputAction attackAction;
    private PlayerController playerController;
    private GameObject insLineRendererObj;
    private LineRenderer insLineRendererCom;
    private CinemachineCamera cinemachineCamera;
    private CinemachineBasicMultiChannelPerlin perlin;
    private NoiseSettings orgNoiseSettings;

    [Header("Grappling Hook Settings")]
    [SerializeField] private float maxGrappleRange = 8f;
    [SerializeField] private float grappleTime = 0.5f;
    [SerializeField] private float pullForce = 10f;
    [SerializeField] private LayerMask grappleLayer;
    public bool IsGrappling { get; private set; }
    private bool isFocusing = false;
    private SpringJoint2D springJoint;
    private LineRenderer grappleLine;
    private Coroutine activeGrapplingCoroutine;
    private InputAction grappleAction;
    private float minDistance = 0.5f;

    void Awake()
    {
        Instance = this;
        attackAction = InputSystem.actions.FindAction("Attack");
        playerController = GetComponent<PlayerController>();
        cinemachineCamera = FindAnyObjectByType<CinemachineCamera>();
        perlin = cinemachineCamera.GetComponent<CinemachineBasicMultiChannelPerlin>();
        orgNoiseSettings = perlin.NoiseProfile;
        orgAmplitudeGain = perlin.AmplitudeGain;
        grappleAction = InputSystem.actions.FindAction("Grapple");
        grappleLine = GetComponent<LineRenderer>();
        springJoint = GetComponent<SpringJoint2D>();
    }

    void Start()
    {
        fixedDeltaTime = Time.deltaTime;
        currentMeter = maxMeter;
    }


    void Update()
    {
        if (IsGrappling && grappleLine.positionCount > 0)
        {
            grappleLine.SetPosition(0, transform.position);
        }

        if (IsInMode && insLineRendererObj != null)
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

        if (attackAction.IsPressed() && currentMeter <= 0)
        {
            ExitChainSlashMode();
            return;
        }

        if (attackAction.IsPressed())
        {
            if (isFocusing) return;

            if (!IsInMode)
            {
                EnterChainSlashMode();
            }
        }
        else
        {
            if (IsInMode)
            {
                ExitChainSlashMode();
            }
        }

        if (grappleAction.IsPressed() && currentMeter <= 0)
        {
            GrappingHook();
            return;
        }

        if (grappleAction.IsPressed())
        {
            if (IsInMode) return;

            if (!IsGrappling && !isFocusing)
            {
                GrapplingFocus();
            }
        }
        else
        {
            if (!IsGrappling && isFocusing)
            {
                GrappingHook();
            }
        }
    }

    void EnterChainSlashMode()
    {
        IsInMode = true;

        Time.timeScale = timeSpeed;
        Time.fixedDeltaTime = fixedDeltaTime * Time.timeScale;

        insLineRendererObj = Instantiate(lineRendererObject);
        insLineRendererCom = insLineRendererObj.GetComponent<LineRenderer>();

        if (activeMeterCoroutine != null)
        {
            StopCoroutine(activeMeterCoroutine);
        }

        activeMeterCoroutine = StartCoroutine(MeterDegeneration(1 ,0.005f));
    }

    void ExitChainSlashMode()
    {
        IsInMode = false;

        targetGroup.Targets[1].Object = null;

        if (activeAnimateCoroutine != null)
        {
            StopCoroutine(activeAnimateCoroutine);
        }

        if (activeMeterCoroutine != null)
        {
            StopCoroutine(activeMeterCoroutine);
        }

        activeMeterCoroutine = StartCoroutine(MeterRegenerate());

        if (insLineRendererObj != null)
        {
            LineRendererManager lineRendererManager = insLineRendererObj.GetComponent<LineRendererManager>();
            lineRendererManager.activeCoroutine = StartCoroutine(AnimateKillLine(insLineRendererCom));
        }
        else
        {
            Debug.LogWarning("insLineRendererObj is missing!");
        }

        attackAction.Disable();
        attackAction.Enable();

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

            currentMeter -= maxMeter / 40;

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
                    if (enemy != null && enemy.IsDead == false)
                    {
                        enemy.OnDeath();
                    }
                }
                else if (lineRendererManager.targets[i].TryGetComponent<ChainEnemy>(out ChainEnemy chainEnemy))
                {
                    if (chainEnemy != null && chainEnemy.IsDead == false)
                    {
                        chainEnemy.StartCoroutine(chainEnemy.OnDeath());
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

    IEnumerator MeterRegenerate()
    {
        if (currentMeter == 0)
        {
            yield return new WaitForSeconds(1f);
        }
        else
        {
            yield return new WaitForSeconds(0.5f);
        }

        while (currentMeter < maxMeter)
        {
            currentMeter++;
            currentMeter = Mathf.Clamp(currentMeter, 0, maxMeter);

            modeMeterImage.fillAmount = currentMeter / maxMeter;

            yield return new WaitForSeconds(0.03f);
        }

        activeMeterCoroutine = null;
    }

    IEnumerator MeterDegeneration(float amount, float interval)
    {
        while (currentMeter > 0)
        {
            currentMeter -= amount;
            currentMeter = Mathf.Clamp(currentMeter, 0, maxMeter);

            modeMeterImage.fillAmount = currentMeter / maxMeter;

            yield return new WaitForSeconds(interval);
        }

        activeMeterCoroutine = null;
    }

    public IEnumerator ShakingTrigger(float duration)
    {
        if (perlin != null)
        {
            perlin.NoiseProfile = noiseSettings;
            perlin.AmplitudeGain = 10;

            yield return new WaitForSeconds(duration);

            perlin.NoiseProfile = orgNoiseSettings;
            perlin.AmplitudeGain = orgAmplitudeGain;
        }
    }


    private void GrappingHook()
    {
        if (activeGrapplingCoroutine != null)
        {
            StopCoroutine(activeGrapplingCoroutine);
        }

        activeGrapplingCoroutine = StartCoroutine(AttachGrapple());
    }

    private IEnumerator AttachGrapple()
    {
        IsGrappling = true;
        isFocusing = false;

        if (activeMeterCoroutine != null)
        {
            StopCoroutine(activeMeterCoroutine);
        }

        activeMeterCoroutine = StartCoroutine(MeterRegenerate());

        grappleLine.positionCount = 0;

        yield return null;

        Time.timeScale = 1.0f;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(mousePos);
        Vector2 dir = (mouseWorldPos - (Vector2)transform.position).normalized;
        RaycastHit2D rayHit = Physics2D.Raycast(transform.position, dir, maxGrappleRange, grappleLayer);

        grappleLine.positionCount = 2;
        grappleLine.SetPosition(0, transform.position);
        grappleLine.SetPosition(1, transform.position);
        grappleLine.enabled = true;

        Vector2 targetPos;
        if (rayHit.collider != null)
        {
            targetPos = rayHit.point;
        }
        else
        {
            targetPos = (Vector2)transform.position + (dir * maxGrappleRange);
        }

        float timer = 0f;
        while (timer < grappleTime)
        {
            timer += Time.deltaTime;
            float time = timer / grappleTime;
            grappleLine.SetPosition(1, Vector2.Lerp(transform.position, targetPos, time));

            yield return null;
        }

        if (rayHit.collider != null)
        {
            springJoint.enabled = true;
            springJoint.connectedAnchor = rayHit.point;
            springJoint.distance = Vector2.Distance(transform.position, targetPos);

            while (springJoint.distance > minDistance)
            {
                springJoint.distance -= pullForce * Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            springJoint.enabled = false;
            grappleLine.enabled = false;
        }
    }

    private void GrapplingFocus()
    {
        if (activeMeterCoroutine != null)
        {
            StopCoroutine(activeMeterCoroutine);
        }

        activeMeterCoroutine = StartCoroutine(MeterDegeneration(1, 0.005f));

        isFocusing = true;

        Time.timeScale = timeSpeed;
        Time.fixedDeltaTime = fixedDeltaTime * Time.timeScale;
    }

    public void DetachGrappling()
    {
        IsGrappling = false;
        grappleLine.enabled = false;
        springJoint.enabled = false;
    }
}
