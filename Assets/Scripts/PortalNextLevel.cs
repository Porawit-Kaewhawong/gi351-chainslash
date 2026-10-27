using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider2D))]
public class PortalNextLevel : MonoBehaviour
{
    [Header("Next Level Settings")]
    [Tooltip("Leave empty to automatically load the next scene in Build Settings (buildIndex + 1).")]
    [SerializeField] private string nextSceneName = "";
    [SerializeField] private float teleportDelay = 0.1f;
    [SerializeField] private AudioClip portalEnterSFX;

    [Header("Visual Polish")]
    [SerializeField] private bool animateSpawnScale = true;
    [SerializeField] private float spawnAnimationDuration = 0.35f;

    private bool isTeleporting = false;
    private Vector3 targetScale;

    private void Awake()
    {
        targetScale = transform.localScale;

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnEnable()
    {
        if (animateSpawnScale)
        {
            StartCoroutine(SpawnScaleRoutine());
        }
    }

    private IEnumerator SpawnScaleRoutine()
    {
        transform.localScale = Vector3.zero;
        float elapsed = 0f;

        while (elapsed < spawnAnimationDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / spawnAnimationDuration);
            // Smooth overshoot pop-in curve
            float scaleFactor = 1f + 0.15f * Mathf.Sin(t * Mathf.PI) * (1f - t);
            transform.localScale = targetScale * Mathf.Lerp(0f, scaleFactor, t);
            yield return null;
        }

        transform.localScale = targetScale;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryTeleportPlayer(other.gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryTeleportPlayer(collision.gameObject);
    }

    private void TryTeleportPlayer(GameObject hitObject)
    {
        if (isTeleporting || hitObject == null) return;

        PlayerController player = hitObject.GetComponentInParent<PlayerController>();
        if (player == null && !hitObject.CompareTag("Player")) return;

        isTeleporting = true;

        if (player != null && portalEnterSFX != null)
        {
            player.PlaySFX(portalEnterSFX);
        }

        StartCoroutine(TeleportToNextSceneRoutine());
    }

    private IEnumerator TeleportToNextSceneRoutine()
    {
        if (teleportDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(teleportDelay);
        }

        // Ensure time scale and static combat flags are reset before switching scenes
        Time.timeScale = 1f;
        ChainEnemy.isExploding = false;

        if (!string.IsNullOrWhiteSpace(nextSceneName))
        {
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            int currentBuildIndex = SceneManager.GetActiveScene().buildIndex;
            int nextBuildIndex = currentBuildIndex + 1;

            if (nextBuildIndex < SceneManager.sceneCountInBuildSettings)
            {
                SceneManager.LoadScene(nextBuildIndex);
            }
            else
            {
                Debug.Log("PortalNextLevel: Reached final scene in Build Settings, wrapping to index 0.");
                SceneManager.LoadScene(0);
            }
        }
    }
}

