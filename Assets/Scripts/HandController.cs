using UnityEngine;

public class HandController : MonoBehaviour
{
    public Animator handAnim;
    public Animator playerAnim;
    public SpriteRenderer Img_Renderer;
    public Sprite hand;
    public bool rightLegUp;
    public bool leftLegUp;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        check();
        handmoving();
    }

    void check()
    {
        if (playerAnim.GetBool("isclimbing") == true)
        {
            Img_Renderer.sprite = hand;
        }
        else
        {
            Img_Renderer.sprite = null;
        }
    }
    void handmoving()
    {
        if (playerAnim.GetBool("isclbmoving") == true)
        {
            if (this.gameObject.name == "RightHand")
            {

            }
            else if(this.gameObject.name == "LeftHand")
            {
                 
            }
        }
    }
}
