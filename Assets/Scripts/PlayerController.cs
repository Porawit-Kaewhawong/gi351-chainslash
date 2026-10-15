using System.Collections;
using System.Runtime.CompilerServices;
using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class PlayerController : MonoBehaviour
{
    [Header("Player Movement Settings")]
    [SerializeField] private float moveSpeed = 7.5f;
    [SerializeField] private float jumpHeight = 15f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    private bool doubleJump;
    private Vector2 inputValue;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;
    private Rigidbody2D rb;

    [Header("Player Dashing Settings")]
    [SerializeField] private float dashingPower = 15;
    [SerializeField] private float dashingTime = 0.2f;
    [SerializeField] private float dashingCooldown = 1f;
    private bool canDash = true;
    private bool isDashing;
    private TrailRenderer trailRenderer;

    [Header("Player Visual Settings")]
    private SpriteRenderer spriteRenderer;
    
    private AudioSource audioSource;
    private int soundPlaying;
    private int maxSoundPlaying = 3;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        audioSource = GetComponent<AudioSource>();
        trailRenderer = GetComponent<TrailRenderer>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        dashAction = InputSystem.actions.FindAction("Dash");
    }        

    void Update()
    {
        if (Keyboard.current.rKey.isPressed)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        if (isDashing)
        {
            return;
        }

        if (IsGrounded())
        {
            doubleJump = true;
        }

        if (jumpAction.WasPressedThisFrame())
        {
            if (IsGrounded())
            {
                rb.linearVelocity = new Vector2(rb.linearVelocityX, jumpHeight);
            }
            else if (doubleJump)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocityX, jumpHeight);
                doubleJump = false;
            }

            PlayerCombat.Instance.DetachGrappling();
        }

        if (jumpAction.WasReleasedThisFrame() && rb.linearVelocityY > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocityX, rb.linearVelocityY * 0.5f);
        }

        if (dashAction.triggered && canDash)
        {
            StartCoroutine(Dash());
        }

        Flip();
    }

    void FixedUpdate()
    {
        if (isDashing)
        {
            return;
        }

        inputValue = moveAction.ReadValue<Vector2>();
        rb.linearVelocity = new Vector2(inputValue.x * moveSpeed, rb.linearVelocityY);
    }

    private void LateUpdate()
    {
        soundPlaying = 0;
    }

    private bool IsGrounded()
    {
        return Physics2D.OverlapCircle(groundCheck.position, 0.2f, groundLayer);
    }

    private void Flip()
    {
        if (inputValue.x < 0f)
        {
            spriteRenderer.flipX = true;
        }
        else if (inputValue.x > 0f)
        {
            spriteRenderer.flipX = false;
        }
    }

    private IEnumerator Dash()
    {
        canDash = false;
        isDashing = true;

        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0f;

        int dashDirection = spriteRenderer.flipX ? -1 : 1;
        rb.linearVelocity = new Vector2(dashDirection * dashingPower, 0f);
        trailRenderer.emitting = true;

        yield return new WaitForSeconds(dashingTime);
        trailRenderer.emitting = false;
        rb.gravityScale = originalGravity;
        isDashing = false;

        yield return new WaitForSeconds(dashingCooldown);
        canDash = true;
    }

    public void PlaySFX(AudioClip audioClip)
    {
        if (soundPlaying < maxSoundPlaying)
        {
            audioSource.PlayOneShot(audioClip);
            soundPlaying++;
        }
    }
}
