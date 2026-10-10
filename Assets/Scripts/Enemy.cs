using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class Enemy : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private bool isTarget = false;
    private SpriteRenderer spriteRenderer;
    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (PlayerCombat.Instance.isInMode && !isTarget)
        {
            PlayerCombat.Instance.targetEnemies.Add(this.gameObject);
            spriteRenderer.color = Color.purple;
            isTarget = true;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        
    }
}
