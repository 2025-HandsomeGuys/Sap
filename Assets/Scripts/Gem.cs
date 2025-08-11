using UnityEngine;

public class Gem : MonoBehaviour
{
    public float weight = 1.0f; // 기본 무게, 나중에 보석 종류별로 다르게 설정 가능

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponent<PlayerController>();
            if (player != null)
            {
                player.AddCollectibleGem(this.gameObject);
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponent<PlayerController>();
            if (player != null)
            {
                player.RemoveCollectibleGem(this.gameObject);
            }
        }
    }
}