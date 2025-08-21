using System.Collections.Generic;
using UnityEngine;

public class ToolController : MonoBehaviour
{
    public Vector3 followPos;
    public Vector3 scale;
    public int followDelay;
    public Transform parent;
    public Queue<Vector3> parentPos;
    public bool isleft;
    public int toolType = 0;

    Quaternion toolRot= Quaternion.Euler(0,0,-30);
    Quaternion toolReverseRot = Quaternion.Euler(0, 0, 30);


    public SpriteRenderer Img_Renderer;
    public Sprite sap;
    public Sprite pickaxe;


    void Start()
    {
        
    }
    void Awake()
    {
        parentPos = new Queue<Vector3>();
        
        
    }
    void Check()
    {
        scale = parent.localScale;
        if (scale.x == 1)
            isleft = true;
        else if (scale.x == -1)
            isleft= false;
    }
    void Watch()
    {
        parentPos.Enqueue(parent.position);
        if (parentPos.Count > followDelay)
            followPos = parentPos.Dequeue();
    }
    void Follow()
    {
        if (isleft == true)
        {
            transform.position = new Vector3(followPos.x + 0.1f, followPos.y, followPos.z);
            transform.rotation = toolRot;
        }


        else if (isleft == false)
        {
            transform.position = new Vector3(followPos.x - 0.1f, followPos.y, followPos.z);
            transform.rotation = toolReverseRot;
        }
    }
    //follow logic




    void Change()
    {
        if (Input.GetAxisRaw("Mouse ScrollWheel")>0)
        {
            
            if(toolType == 3)  
                toolType = 0;
            else
                toolType += 1;
        }
        else if (Input.GetAxisRaw("Mouse ScrollWheel") < 0)
        {

            if (toolType == 0)
                toolType = 3;
            else
                toolType -= 1;
        }


        if (toolType==0) 
            Img_Renderer.sprite = sap;
        else if (toolType==1)
            Img_Renderer.sprite = pickaxe;
        else
            Img_Renderer.sprite = pickaxe;

        
    }




    
    void Update()
    {
        Check();
        Watch();
        Follow();
        Change();
    }
}
