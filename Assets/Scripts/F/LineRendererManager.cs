using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the visual LineRenderer and targeted enemies during Chain Slash mode.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public class LineRendererManager : MonoBehaviour
{
    #region Public Fields & State

    public Coroutine activeCoroutine;
    public List<GameObject> targets = new List<GameObject>();

    public LineRenderer LineRendererCom { get; private set; }

    #endregion

    #region Private State & Properties

    private PlayerController cachedPlayer;
    private readonly List<SpriteRenderer> pixelBlockPool = new List<SpriteRenderer>(256);
    private Vector3[] pointBuffer = new Vector3[128];
    private float baseWidthMultiplier = -1f;
    private const int MaxPixelBlocks = 420;
    private const float DefaultPixelStep = 1f / 16f;

    public PlayerController Player
    {
        get
        {
            if (PlayerController.Instance != null)
            {
                return PlayerController.Instance;
            }
            if (cachedPlayer == null)
            {
                cachedPlayer = Object.FindFirstObjectByType<PlayerController>();
            }
            return cachedPlayer;
        }
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        LineRendererCom = GetComponent<LineRenderer>();
        if (LineRendererCom != null)
        {
            LineRendererCom.forceRenderingOff = true;
            baseWidthMultiplier = Mathf.Max(0.01f, LineRendererCom.widthMultiplier);
        }
    }

    private void Start()
    {
        cachedPlayer = PlayerController.Instance != null
            ? PlayerController.Instance
            : Object.FindFirstObjectByType<PlayerController>();
    }

    private void LateUpdate()
    {
        RenderPixelSlashLine();
    }

    private void OnDestroy()
    {
        StopActiveCoroutine();
    }

    #endregion

    #region Target & Coroutine Management

    /// <summary>
    /// Removes null or destroyed game object entries from the targets list without allocating a predicate delegate.
    /// </summary>
    public void CleanNullTargets()
    {
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            if (targets[i] == null)
            {
                targets.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Adds a valid target to the chain list if not already present.
    /// </summary>
    public bool AddTarget(GameObject target)
    {
        if (target != null && !targets.Contains(target))
        {
            targets.Add(target);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clears all targets and resets the line renderer positions.
    /// </summary>
    public void ClearTargets()
    {
        targets.Clear();
        if (LineRendererCom != null)
        {
            LineRendererCom.positionCount = 0;
        }
    }

    /// <summary>
    /// Safely stops any currently running animation routine on this line.
    /// </summary>
    public void StopActiveCoroutine()
    {
        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
            activeCoroutine = null;
        }
    }

    #endregion

    #region Pixel Slash Line Rasterizer

    public void RenderPixelSlashLine()
    {
        if (LineRendererCom == null) return;
        if (!LineRendererCom.forceRenderingOff)
        {
            LineRendererCom.forceRenderingOff = true;
        }

        int count = LineRendererCom.positionCount;
        if (!LineRendererCom.enabled || count < 1)
        {
            HidePixelBlocksFrom(0);
            return;
        }

        if (pointBuffer.Length < count)
        {
            pointBuffer = new Vector3[Mathf.NextPowerOfTwo(count)];
        }

        LineRendererCom.GetPositions(pointBuffer);

        if (baseWidthMultiplier <= 0.001f)
        {
            baseWidthMultiplier = Mathf.Max(0.05f, LineRendererCom.widthMultiplier);
        }

        float widthRatio = LineRendererCom.widthMultiplier / baseWidthMultiplier;
        int pixelBlockSize = widthRatio > 1.45f ? 3 : (widthRatio < 0.55f ? 1 : 2);
        Sprite blockSprite = EnemyBase.GetSharedPixelBlockSprite(pixelBlockSize);

        Gradient grad = LineRendererCom.colorGradient;
        int sortLayerId = LineRendererCom.sortingLayerID;
        int sortOrder = Mathf.Max(14, LineRendererCom.sortingOrder);

        float totalLength = 0f;
        for (int i = 0; i < count - 1; i++)
        {
            totalLength += Vector2.Distance(pointBuffer[i], pointBuffer[i + 1]);
        }

        if (count == 1 || totalLength < 0.001f)
        {
            Vector3 snappedPt = EnemyBase.SnapToPixelGrid(pointBuffer[0], 16f);
            Color startCol = grad != null ? grad.Evaluate(0f) : LineRendererCom.startColor;
            SpriteRenderer singleSr = GetOrCreatePixelBlock(0, sortLayerId, sortOrder);
            singleSr.transform.position = snappedPt;
            singleSr.sprite = blockSprite;
            singleSr.color = startCol;
            if (!singleSr.gameObject.activeSelf) singleSr.gameObject.SetActive(true);
            HidePixelBlocksFrom(1);
            return;
        }

        float stepWorld = Mathf.Max(DefaultPixelStep, totalLength / MaxPixelBlocks);

        int activeCount = 0;
        float distSoFar = 0f;
        Vector3 lastSnapped = new Vector3(float.MinValue, float.MinValue, 0f);

        for (int i = 0; i < count - 1; i++)
        {
            Vector2 a = pointBuffer[i];
            Vector2 b = pointBuffer[i + 1];
            float segLen = Vector2.Distance(a, b);
            if (segLen < 0.0001f) continue;

            int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / stepWorld));
            for (int s = 0; s <= steps; s++)
            {
                if (activeCount >= MaxPixelBlocks) break;

                float segT = (float)s / steps;
                Vector2 sample = Vector2.Lerp(a, b, segT);
                Vector3 snapped = EnemyBase.SnapToPixelGrid(sample, 16f);

                if ((snapped - lastSnapped).sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                lastSnapped = snapped;
                float u = Mathf.Clamp01((distSoFar + segLen * segT) / totalLength);
                Color col = grad != null ? grad.Evaluate(u) : LineRendererCom.startColor;

                SpriteRenderer sr = GetOrCreatePixelBlock(activeCount, sortLayerId, sortOrder);
                sr.transform.position = snapped;
                sr.sprite = blockSprite;
                sr.color = col;
                if (!sr.gameObject.activeSelf) sr.gameObject.SetActive(true);
                activeCount++;
            }

            distSoFar += segLen;
            if (activeCount >= MaxPixelBlocks) break;
        }

        HidePixelBlocksFrom(activeCount);
    }

    private SpriteRenderer GetOrCreatePixelBlock(int index, int sortingLayerId, int sortingOrder)
    {
        while (pixelBlockPool.Count <= index)
        {
            GameObject obj = new GameObject("SlashPixel_" + pixelBlockPool.Count);
            obj.transform.SetParent(transform, false);
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;

            SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
            pixelBlockPool.Add(sr);
        }

        SpriteRenderer block = pixelBlockPool[index];
        block.sortingLayerID = sortingLayerId;
        block.sortingOrder = sortingOrder;
        return block;
    }

    private void HidePixelBlocksFrom(int startIndex)
    {
        for (int i = startIndex; i < pixelBlockPool.Count; i++)
        {
            if (pixelBlockPool[i] != null && pixelBlockPool[i].gameObject.activeSelf)
            {
                pixelBlockPool[i].gameObject.SetActive(false);
            }
        }
    }

    #endregion
}