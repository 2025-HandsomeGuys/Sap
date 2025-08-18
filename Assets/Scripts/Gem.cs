using UnityEngine;
using static Constants;

public class Gem : MonoBehaviour
{
    public Item itemData; // 이 보석의 데이터를 담고 있는 Item 에셋 참조

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other.CompareTag(TAG_PLAYER))
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