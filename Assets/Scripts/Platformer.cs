using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider2D))]
public class Platformer : MonoBehaviour
{
    [Header("Effector Settings")]
    [Tooltip("Arc angle of the top one-way surface (170° prevents side snags while allowing smooth landing).")]
    [SerializeField] private float surfaceArc = 170f;
    [Tooltip("Automatically attaches and configures a PlatformEffector2D on this GameObject.")]
    [SerializeField] private bool autoConfigureEffector = true;
    [Tooltip("Ensures this platform is set to the Ground layer so raycasts, ground checks, and grappling hook work.")]
    [SerializeField] private bool autoAssignGroundLayer = true;

    [Header("Player Drop-Through Settings")]
    [SerializeField] private bool allowPlayerDropThrough = true;
    [Tooltip("Allows dropping through by holding Down (S / Down Arrow / Stick) and pressing Jump.")]
    [SerializeField] private bool allowDownJump = true;
    [Tooltip("Allows dropping through simply by holding Down for a short duration.")]
    [SerializeField] private bool allowHoldDown = true;
    [SerializeField] private float holdDownDuration = 0.25f;
    [SerializeField] private float dropThroughDuration = 0.35f;
    [SerializeField] private float dropPushDownForce = 2.5f;

    [Header("Enemy Drop-Through Settings")]
    [SerializeField] private bool allowEnemyDropThrough = true;
    [Tooltip("Delay before an enemy standing on the platform decides to drop down toward a lower player.")]
    [SerializeField] private float enemyDropDelay = 0.35f;
    [Tooltip("Minimum vertical distance the player must be below the enemy to trigger enemy drop-through.")]
    [SerializeField] private float enemyVerticalThreshold = 1.0f;

    private Collider2D[] platformColliders;
    private PlatformEffector2D platformEffector;

    // Player contact tracking
    private bool isPlayerOnPlatform = false;
    private GameObject playerObject;
    private float playerHoldDownTimer = 0f;
    private InputAction cachedMoveAction;
    private InputAction cachedJumpAction;

    // Enemy contact tracking
    private readonly Dictionary<EnemyBase, float> enemyStayTimers = new Dictionary<EnemyBase, float>();
    private readonly List<EnemyBase> enemiesToRemove = new List<EnemyBase>();

    private void Reset()
    {
        ConfigurePlatform();
    }

    private void Awake()
    {
        ConfigurePlatform();
    }

    private void ConfigurePlatform()
    {
        if (autoAssignGroundLayer && gameObject.layer == 0)
        {
            int groundLayerIndex = LayerMask.NameToLayer("Ground");
            if (groundLayerIndex != -1)
            {
                gameObject.layer = groundLayerIndex;
            }
        }

        platformColliders = GetComponentsInChildren<Collider2D>();
        foreach (Collider2D col in platformColliders)
        {
            if (col != null && !col.isTrigger)
            {
                col.usedByEffector = true;
            }
        }

        if (autoConfigureEffector)
        {
            platformEffector = GetComponent<PlatformEffector2D>();
            if (platformEffector == null)
            {
                platformEffector = gameObject.AddComponent<PlatformEffector2D>();
            }

            platformEffector.useOneWay = true;
            platformEffector.surfaceArc = surfaceArc;
            platformEffector.rotationalOffset = 0f;
            platformEffector.useOneWayGrouping = true;
            platformEffector.useSideFriction = false;
            platformEffector.useSideBounce = false;
        }
    }

    private void Update()
    {
        if (allowPlayerDropThrough && isPlayerOnPlatform && playerObject != null)
        {
            HandlePlayerInput();
        }

        if (allowEnemyDropThrough && enemyStayTimers.Count > 0)
        {
            UpdateEnemyDropThrough();
        }
    }

    private void HandlePlayerInput()
    {
        bool downPressed = IsDownInputActive();
        bool jumpPressed = IsJumpInputPressed();

        if (downPressed)
        {
            playerHoldDownTimer += Time.deltaTime;

            // 1. Down + Jump
            if (allowDownJump && jumpPressed)
            {
                DropThrough(playerObject);
                playerHoldDownTimer = 0f;
                return;
            }

            // 2. Hold Down
            if (allowHoldDown && playerHoldDownTimer >= holdDownDuration)
            {
                DropThrough(playerObject);
                playerHoldDownTimer = 0f;
                return;
            }
        }
        else
        {
            playerHoldDownTimer = 0f;
        }
    }

    private bool IsDownInputActive()
    {
        // Try reading PlayerController input
        PlayerController player = PlayerController.Instance;
        if (player != null && player.InputValue.y < -0.4f)
        {
            return true;
        }

        // Action map check
        if (cachedMoveAction == null) cachedMoveAction = InputSystem.actions?.FindAction("Move");
        InputAction moveAction = cachedMoveAction;
        if (moveAction != null && moveAction.ReadValue<Vector2>().y < -0.4f)
        {
            return true;
        }

        // Direct Keyboard check
        if (Keyboard.current != null)
        {
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
            {
                return true;
            }
        }

        // Direct Gamepad check
        if (Gamepad.current != null)
        {
            if (Gamepad.current.leftStick.down.isPressed || Gamepad.current.dpad.down.isPressed)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsJumpInputPressed()
    {
        if (cachedJumpAction == null) cachedJumpAction = InputSystem.actions?.FindAction("Jump");
        InputAction jumpAction = cachedJumpAction;
        if (jumpAction != null && jumpAction.WasPressedThisFrame())
        {
            return true;
        }

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            return true;
        }

        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
        {
            return true;
        }

        return false;
    }

    private void UpdateEnemyDropThrough()
    {
        enemiesToRemove.Clear();

        PlayerController player = PlayerController.Instance;
        Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;

        foreach (var kvp in enemyStayTimers)
        {
            EnemyBase enemy = kvp.Key;
            if (enemy == null || enemy.IsDead || !enemy.gameObject.activeInHierarchy)
            {
                enemiesToRemove.Add(enemy);
                continue;
            }

            // Check if player is on a lower floor than this enemy
            bool playerIsBelow = player != null && (playerPos.y < enemy.transform.position.y - enemyVerticalThreshold);

            if (playerIsBelow)
            {
                float updatedTime = kvp.Value + Time.deltaTime;
                if (updatedTime >= enemyDropDelay)
                {
                    DropThrough(enemy.gameObject);
                    enemiesToRemove.Add(enemy);
                    continue;
                }
                else
                {
                    enemyStayTimers[enemy] = updatedTime;
                }
            }
            else
            {
                // Reset timer if player is no longer below
                enemyStayTimers[enemy] = 0f;
            }
        }

        for (int i = 0; i < enemiesToRemove.Count; i++)
        {
            enemyStayTimers.Remove(enemiesToRemove[i]);
        }
    }

    /// <summary>
    /// Drops the specified character (Player or Enemy) through this platform.
    /// </summary>
    public void DropThrough(GameObject character)
    {
        if (character == null) return;

        Collider2D[] characterColliders = character.GetComponentsInChildren<Collider2D>();
        if (characterColliders == null || characterColliders.Length == 0) return;

        // Apply a gentle downward nudge to begin falling immediately
        if (character.TryGetComponent<Rigidbody2D>(out Rigidbody2D rb))
        {
            if (rb.linearVelocityY > -dropPushDownForce)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocityX, -dropPushDownForce);
            }
        }

        StartCoroutine(IgnoreCollisionRoutine(characterColliders));
    }

    private IEnumerator IgnoreCollisionRoutine(Collider2D[] characterColliders)
    {
        if (platformColliders == null || platformColliders.Length == 0)
        {
            platformColliders = GetComponentsInChildren<Collider2D>();
        }

        // Disable collision with all colliders of this platform
        foreach (Collider2D pCol in platformColliders)
        {
            if (pCol == null) continue;
            foreach (Collider2D cCol in characterColliders)
            {
                if (cCol != null)
                {
                    Physics2D.IgnoreCollision(cCol, pCol, true);
                }
            }
        }

        float timer = 0f;
        float maxWaitTime = dropThroughDuration;

        // Wait until character has descended past the bottom of the platform, or timeout
        while (timer < maxWaitTime)
        {
            timer += Time.deltaTime;

            bool isCompletelyBelow = true;
            foreach (Collider2D pCol in platformColliders)
            {
                if (pCol == null) continue;
                float platformBottom = pCol.bounds.min.y;

                foreach (Collider2D cCol in characterColliders)
                {
                    if (cCol != null && cCol.bounds.max.y > platformBottom)
                    {
                        isCompletelyBelow = false;
                        break;
                    }
                }
                if (!isCompletelyBelow) break;
            }

            if (isCompletelyBelow && timer > 0.12f)
            {
                break;
            }

            yield return null;
        }

        // Restore collision
        foreach (Collider2D pCol in platformColliders)
        {
            if (pCol == null) continue;
            foreach (Collider2D cCol in characterColliders)
            {
                if (cCol != null)
                {
                    Physics2D.IgnoreCollision(cCol, pCol, false);
                }
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        HandleCollisionContact(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        HandleCollisionContact(collision);
    }

    private void HandleCollisionContact(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.GetComponent<PlayerController>() != null)
        {
            isPlayerOnPlatform = true;
            playerObject = collision.gameObject;
        }
        else if (collision.gameObject.TryGetComponent<EnemyBase>(out EnemyBase enemy) ||
                 (collision.gameObject.GetComponentInParent<EnemyBase>() is EnemyBase pEnemy && (enemy = pEnemy) != null))
        {
            if (!enemyStayTimers.ContainsKey(enemy))
            {
                enemyStayTimers[enemy] = 0f;
            }
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.GetComponent<PlayerController>() != null)
        {
            isPlayerOnPlatform = false;
            playerObject = null;
            playerHoldDownTimer = 0f;
        }
        else if (collision.gameObject.TryGetComponent<EnemyBase>(out EnemyBase enemy) ||
                 (collision.gameObject.GetComponentInParent<EnemyBase>() is EnemyBase pEnemy && (enemy = pEnemy) != null))
        {
            enemyStayTimers.Remove(enemy);
        }
    }
}

