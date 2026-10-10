using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class PlayerCombat : MonoBehaviour
{
    public static PlayerCombat Instance { get; private set; }

    [Header("Chain Slash Mode Settings")]
    [SerializeField] private float timeSpeed = 0.25f;
    [HideInInspector] public bool isInMode { get; private set; }
    public List<GameObject> targetEnemies = new List<GameObject>();
    private float fixedDeltaTime;
    private InputAction attackAction;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        this.fixedDeltaTime = Time.deltaTime;
        attackAction = InputSystem.actions.FindAction("Attack");
    }

    
    void Update()
    {
        if (attackAction.IsPressed())
        {
            EnterChainSlashMode();
        }
        else
        {
            ExitChainSlashMode();
            Time.timeScale = 1.0f;
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
        targetEnemies.Clear();
    }
}
