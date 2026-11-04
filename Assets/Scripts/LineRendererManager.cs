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
    }

    private void Start()
    {
        cachedPlayer = PlayerController.Instance != null
            ? PlayerController.Instance
            : Object.FindFirstObjectByType<PlayerController>();
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
}