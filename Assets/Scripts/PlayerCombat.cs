using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
                Destroy(enemy);
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

    public void LineSetup()
    {
        lineRenderer.enabled = true;

        lineRenderer.positionCount = targetEnemies.Count;

        for (int i = 0; i < targetEnemies.Count; i++)
        {
            lineRenderer.SetPosition(i, targetEnemies[i].transform.position);
        }

        PlayerController playerController = GetComponent<PlayerController>();
        playerController.PlaySFX(targetSelectSFX);
    }
}
