using UnityEngine;

public class B1Push : MonoBehaviour
{
    public SpriteRenderer Img_Renderer;
    public Sprite stat1;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void OnButtonClicked()
    {
        Img_Renderer.sprite = stat1;
    }
}