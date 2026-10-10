using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    [SerializeField] private float timeSpeed = 0.25f;
    private InputAction attackAction;

    void Start()
    {
        attackAction = InputSystem.actions.FindAction("Attack");
    }

    
    void Update()
    {
        if (attackAction.IsPressed())
        {
            Time.timeScale = timeSpeed;
        }
    }

    void ChainSlashMode()
    {
        
    }
}
