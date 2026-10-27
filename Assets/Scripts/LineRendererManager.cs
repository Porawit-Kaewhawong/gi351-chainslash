using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class LineRendererManager : MonoBehaviour
{
    public Coroutine activeCoroutine;
    public List<GameObject> targets = new List<GameObject>();

    public LineRenderer LineRendererCom { get; private set; }
    public PlayerController Player { get; private set; }

    private void Awake()
    {
        LineRendererCom = GetComponent<LineRenderer>();
    }

    private void Start()
    {
        Player = PlayerController.Instance != null ? PlayerController.Instance : FindFirstObjectByType<PlayerController>();
    }

    /// <summary>
    /// Removes null or destroyed game object entries from the targets list.
    /// </summary>
    public void CleanNullTargets()
    {
        targets.RemoveAll(target => target == null);
    }

    private void OnDestroy()
    {
        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
            activeCoroutine = null;
        }
    }
}