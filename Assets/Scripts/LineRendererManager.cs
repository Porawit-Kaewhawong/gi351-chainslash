using System.Collections.Generic;
using UnityEngine;

public class LineRendererManager : MonoBehaviour
{
    public Coroutine activeCoroutine;
    public List<GameObject> targets = new List<GameObject>();
    private LineRenderer lineRenderer;
    private PlayerCombat player;

    private void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        player = PlayerCombat.Instance;
    }
}
