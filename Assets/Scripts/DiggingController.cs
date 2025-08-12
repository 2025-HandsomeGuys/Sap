using UnityEngine;

public class DiggingController : MonoBehaviour
{
    [Header("Digging Settings")]
    public float digRadius = 1.0f;         
    public float digOffset = 0.5f;        

    private Vector2 currentDigDirection = Vector2.right; 

    void Update()
    {
      
        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        
        
        if ((mousePosition - (Vector2)transform.position).sqrMagnitude > 0.01f)
        {
            currentDigDirection = (mousePosition - (Vector2)transform.position).normalized;
        }

        
        if (Input.GetMouseButtonDown(0)) 
        {
            Dig();
        }
    }

    void Dig()
    {
        
        Vector2 digCenter = (Vector2)transform.position + (currentDigDirection * digOffset);

       
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(digCenter, digRadius);

        foreach (var hitCollider in hitColliders)
        {
            if (hitCollider == null || hitCollider.gameObject == null) // Add null checks
            {
                continue;
            }
            
            if (hitCollider.transform == this.transform)
            {
                continue;
            }

            
            string tag = hitCollider.gameObject.tag;
            if (tag == "dirt" || tag == "stone")
            {
                
                ObjectPooler.Instance.ReturnToPool(tag, hitCollider.gameObject);
                WorldManager.Instance.TileDug(hitCollider.transform.position);
            }
        }
    }

    
    void OnDrawGizmosSelected()
    {
        
        Vector2 digCenter = (Vector2)transform.position + (currentDigDirection * digOffset);
        
        Gizmos.color = Color.yellow; 
        Gizmos.DrawWireSphere(digCenter, digRadius);
    }
}